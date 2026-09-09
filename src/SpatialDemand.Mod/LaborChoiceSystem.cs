using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SpatialDemand.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SpatialDemand.Mod
{
    // Negotiate against actual standing jobs and vacancies. Only retention raises
    // can settle here: employment changes still belong to vanilla's routed search.
    public partial class LaborChoiceSystem : GameSystemBase
    {
        private EntityQuery seekers, employers, economy, contracts;
        private SimulationSystem simulation = null!;
        private ResourceSystem resources = null!;
        private TaxSystem taxes = null!;
        private bool faulted;
        private readonly Dictionary<long, double> raiseCash = new Dictionary<long, double>();
        private long accepted;

        protected override void OnCreate()
        {
            base.OnCreate();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            resources = World.GetOrCreateSystemManaged<ResourceSystem>();
            taxes = World.GetOrCreateSystemManaged<TaxSystem>();
            seekers = GetEntityQuery(ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<HouseholdMember>(),
                ComponentType.ReadOnly<HasJobSeeker>(), ComponentType.Exclude<Worker>(), ComponentType.Exclude<Game.Citizens.Student>(),
                ComponentType.Exclude<HealthProblem>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            employers = GetEntityQuery(ComponentType.ReadOnly<CompanyData>(), ComponentType.ReadOnly<WorkProvider>(),
                ComponentType.ReadOnly<FreeWorkplaces>(), ComponentType.ReadOnly<Employee>(),
                ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Resources>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            economy = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            contracts = GetEntityQuery(ComponentType.ReadOnly<NegotiatedWage>());
            RequireForUpdate(economy);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 4096;

        protected override void OnUpdate()
        {
            // Retired employment agreements must not revive after a later rehire,
            // including while new negotiations are switched off.
            if (!contracts.IsEmptyIgnoreFilter)
            {
                EntityManager.CompleteAllTrackedJobs();
                using var entities = contracts.ToEntityArray(Allocator.Temp);
                foreach (var person in entities)
                    if (!EntityManager.HasComponent<Worker>(person) || !ContractSalary.TryGet(EntityManager, person,
                        EntityManager.GetComponentData<Worker>(person), out _)) EntityManager.RemoveComponent<NegotiatedWage>(person);
            }
            if (Mod.Settings == null || !Mod.Settings.LaborEnabled || faulted) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                double speed = Mod.Settings.ConstructionTravelKph, timeValue = Mod.Settings.ShoppingTimeValuePerHour;
                if (!Finite(speed) || speed <= 0 || !Finite(timeValue) || timeValue < 0) return;
                EntityManager.CompleteAllTrackedJobs();
                var parameters = economy.GetSingleton<EconomyParameterData>();
                // Daily round-trip time, using the same physical speed and time value
                // as the other choice diagnostics. Straight line does not prove access.
                double commutePerMetre = 2 * timeValue / (speed * 1000);
                var people = ReadWorkers(parameters);
                var jobs = ReadJobs(parameters, people, commutePerMetre);
                var result = LaborMarket.Clear(people, jobs, double.MaxValue, commutePerMetre, 1, 10000);
                int applied = result.Converged && NegotiatedPayrollHooks.Active ? ApplyRetention(result, parameters) : 0;
                if (result.Converged)
                    foreach (var match in result.Assignments.Take(6))
                        Mod.Log.Info($"labor contract forecast employer={match.Job.Employer}, slot={match.Job.Id}, citizen={match.Worker.Id}, education={match.Job.RequiredEducation}, gross={match.GrossWage:F2}, vanillaGross={parameters.GetWage(match.Job.RequiredEducation)}, takeHome={match.TakeHomeIncome:F2}, commute={match.CommuteCost:F2}, workerGain={match.WorkerUtility - match.Worker.OutsideOption:F2}, valueCeiling={match.Job.MarginalValue:F2}, reservedCash={match.Job.WageBudget:F2}, fillSurplus={match.EmployerSurplus:F2}, emptySurplus=0; job-change=forecast-only, horizon=game-day, value=full-sales-technology-estimate");
                Mod.Log.Info($"labor status frame={simulation.frameIndex}, sampledPeople={people.Count}, sampledSlots={jobs.Count}, hypotheticalMatches={result.Assignments.Count}, unfilled={result.UnfilledJobs.Count}, outside={result.OutsideWorkers.Count}, bids={result.Bids}, converged={result.Converged}, appliedRaises={applied}, acceptedTotal={accepted}, hooksReady={NegotiatedPayrollHooks.Ready}, payrollActive={NegotiatedPayrollHooks.Active}, salaryReads={NegotiatedPayrollHooks.SalarySubstitutions}, paymentReceipts={NegotiatedPayrollHooks.PaymentReceipts}, paymentMismatches={NegotiatedPayrollHooks.PaymentMismatches}, managedJobs={NegotiatedPayrollHooks.ManagedJobs}, managedMs={NegotiatedPayrollHooks.ManagedMilliseconds:F2}, ms={timer.Elapsed.TotalMilliseconds:F2}; transactions=vanilla, city-equilibrium=false, retention-only, cap=128-people/64-employers/128-slots, value=full-sales-technology-estimate, cash=after-standing-wages-rent-full-input-reserve, commute=straight-line-round-trip");
            }
            catch (Exception error)
            {
                faulted = true;
                Mod.Log.Error(error, "New wage negotiations stopped for this session; existing valid wage obligations and game payroll continue.");
            }
        }

        private List<LaborWorker> ReadWorkers(EconomyParameterData parameters)
        {
            var result = new List<LaborWorker>();
            using var entities = seekers.ToEntityArray(Allocator.Temp);
            foreach (var person in entities.OrderBy(Id).Take(64))
            {
                var citizen = EntityManager.GetComponentData<Citizen>(person);
                if (citizen.GetAge() != CitizenAge.Adult) continue;
                var household = EntityManager.GetComponentData<HouseholdMember>(person).m_Household;
                if (!EntityManager.HasComponent<Game.Citizens.Household>(household) || !EntityManager.HasComponent<PropertyRenter>(household) ||
                    EntityManager.HasComponent<CommuterHousehold>(household) || EntityManager.HasComponent<TouristHousehold>(household) ||
                    EntityManager.HasComponent<MovingAway>(household)) continue;
                var home = EntityManager.GetComponentData<PropertyRenter>(household).m_Property;
                if (!EntityManager.HasComponent<Game.Objects.Transform>(home)) continue;
                var position = EntityManager.GetComponentData<Game.Objects.Transform>(home).m_Position;
                if (!math.all(math.isfinite(position))) continue;
                double benefit = citizen.m_UnemploymentCounter < parameters.m_UnemploymentAllowanceMaxDays * PayWageSystem.kUpdatesPerDay ?
                    Math.Max(0, parameters.m_UnemploymentBenefit) : 0;
                // Payroll taxes an unemployed adult at the default job level (zero).
                double outside = benefit - Math.Max(0, benefit - parameters.m_ResidentialMinimumEarnings) *
                    taxes.GetResidentialTaxRate(0) / 100d;
                result.Add(new LaborWorker { Id = Id(person), Education = citizen.GetEducationLevel(),
                    X = position.x, Z = position.z, OutsideOption = Math.Max(0, outside) });
            }
            return result;
        }

        private List<LaborJob> ReadJobs(EconomyParameterData parameters, List<LaborWorker> people, double commutePerMetre)
        {
            var result = new List<LaborJob>();
            raiseCash.Clear();
            using var entities = employers.ToEntityArray(Allocator.Temp);
            foreach (var company in entities.OrderBy(Id).Take(64))
            {
                if (result.Count >= 128) break;
                var prefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
                var lease = EntityManager.GetComponentData<PropertyRenter>(company);
                var building = lease.m_Property;
                if (!EntityManager.HasComponent<IndustrialProcessData>(prefab) || !EntityManager.HasComponent<WorkplaceData>(prefab) ||
                    EntityManager.HasComponent<ExtractorCompanyData>(prefab) || EntityManager.HasComponent<StorageCompanyData>(prefab) ||
                    !EntityManager.HasComponent<Game.Objects.Transform>(building) || !EntityManager.HasComponent<PrefabRef>(building) ||
                    EntityManager.HasComponent<Abandoned>(building) || EntityManager.HasComponent<Destroyed>(building)) continue;
                var buildingPrefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                if (!EntityManager.HasComponent<SpawnableBuildingData>(buildingPrefab)) continue;
                var position = EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position;
                if (!math.all(math.isfinite(position))) continue;
                var process = EntityManager.GetComponentData<IndustrialProcessData>(prefab);
                if (process.m_Output.m_Amount <= 0 || process.m_Output.m_Resource == Resource.NoResource) continue;
                var data = Data(process.m_Output.m_Resource);
                bool retail = EntityManager.HasComponent<CommercialCompany>(company);
                if ((retail ? data.m_NeededWorkPerUnit.y : data.m_NeededWorkPerUnit.x) <= 0) continue;
                double inputPrice = (InputCost(process.m_Input1) + InputCost(process.m_Input2)) / process.m_Output.m_Amount;
                double price = retail ? data.m_Price.y : data.m_Price.x;
                double margin = price - inputPrice;
                if (!Finite(margin) || margin <= 0) continue;
                var workplace = EntityManager.GetComponentData<WorkplaceData>(prefab);
                int maxWorkers = EntityManager.GetComponentData<WorkProvider>(company).m_MaxWorkers;
                int level = EntityManager.GetComponentData<SpawnableBuildingData>(buildingPrefab).m_Level;
                var staff = EntityManager.GetBuffer<Employee>(company, true);
                // Recalculate vacancies from the current roster, not potentially stale FreeWorkplaces counts.
                var vacancies = EconomyUtils.CalculateNumberOfWorkplaces(maxWorkers, workplace.m_Complexity, level);
                foreach (var employee in staff)
                    if (employee.m_Level <= 4) vacancies[employee.m_Level]--;
                int count = 0;
                for (int education = 0; education < 5; education++) count += Math.Max(0, vacancies[education]);
                if (maxWorkers <= 0) continue;
                double cash = EconomyUtils.GetResources(Resource.Money, EntityManager.GetBuffer<Resources>(company, true));
                double workforce = EconomyUtils.GetAverageWorkforce(maxWorkers, workplace.m_Complexity, level);
                if (workforce <= 0) continue;
                double production = EconomyUtils.GetCompanyProductionPerDay(1f, maxWorkers, level, !retail,
                    workplace, process, data, ref parameters);
                double reserve = Math.Max(0, cash - Math.Max(0, lease.m_Rent) -
                    EconomyUtils.CalculateTotalWage(staff, ref parameters) - production * inputPrice);
                raiseCash[Id(company)] = reserve;
                // Divide spare cash among every actual position, including unsampled
                // positions. Existing payroll is reserved before considering any raise.
                double perSlotCash = reserve / Math.Max(1, count + staff.Length);
                foreach (var employee in staff)
                {
                    if (result.Count >= 128 || people.Count >= 128) break;
                    Entity person = employee.m_Worker;
                    if (employee.m_Level > 4 || !ContractSalary.Eligible(EntityManager, person, company, employee.m_Level) ||
                        !EntityManager.HasComponent<Citizen>(person) || EntityManager.HasComponent<HealthProblem>(person) ||
                        EntityManager.HasComponent<Game.Citizens.Student>(person)) continue;
                    var citizen = EntityManager.GetComponentData<Citizen>(person);
                    if (citizen.GetAge() != CitizenAge.Adult) continue;
                    var household = EntityManager.GetComponentData<HouseholdMember>(person).m_Household;
                    if (!EntityManager.HasComponent<PropertyRenter>(household)) continue;
                    Entity home = EntityManager.GetComponentData<PropertyRenter>(household).m_Property;
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(home)) continue;
                    var location = EntityManager.GetComponentData<Game.Objects.Transform>(home).m_Position;
                    if (!math.all(math.isfinite(location))) continue;
                    var worker = EntityManager.GetComponentData<Worker>(person);
                    int gross = ContractSalary.Get(EntityManager, person, worker, parameters);
                    double taxRate = taxes.GetResidentialTaxRate(employee.m_Level);
                    double takeHome = 1 - taxRate / 100;
                    double dx = (double)position.x - location.x, dz = (double)position.z - location.z;
                    double travel = Math.Sqrt(dx * dx + dz * dz) * commutePerMetre;
                    // Match the auction's continuous quote exactly. Integer payroll
                    // rounding is checked again before accepting a real raise.
                    double allowance = Math.Max(0, parameters.m_ResidentialMinimumEarnings);
                    double net = gross <= allowance ? gross : allowance + (gross - allowance) * takeHome;
                    double outside = Math.Max(0, net - travel);
                    double value = production * EconomyUtils.GetWorkerWorkforce(50, employee.m_Level) / workforce * margin;
                    if (!Finite(value) || !Finite(takeHome) || takeHome <= 0) continue;
                    var personQuote = new LaborWorker { Id = Id(person), Education = citizen.GetEducationLevel(),
                        X = location.x, Z = location.z, OutsideOption = outside };
                    if (people.Any(p => p.Id == personQuote.Id)) continue;
                    people.Add(personQuote);
                    result.Add(new LaborJob { Id = result.Count + 1, Employer = Id(company), RequiredEducation = employee.m_Level,
                        X = position.x, Z = position.z, MarginalValue = value, WageBudget = gross + perSlotCash,
                        TakeHomeRate = takeHome, TaxFreeAllowance = Math.Max(0, parameters.m_ResidentialMinimumEarnings),
                        IncumbentWorker = Id(person), IncumbentGrossWage = gross });
                }
                for (int education = 0; education < 5; education++)
                {
                    double quantity = production * EconomyUtils.GetWorkerWorkforce(50, education) / workforce;
                    double value = Math.Max(0, quantity * margin);
                    double wageBudget = perSlotCash;
                    double takeHomeRate = 1 - taxes.GetResidentialTaxRate(education) / 100d;
                    if (!Finite(value) || !Finite(wageBudget) || !Finite(takeHomeRate) || takeHomeRate <= 0) continue;
                    for (int slot = 0; slot < vacancies[education] && result.Count < 128; slot++)
                        result.Add(new LaborJob { Id = result.Count + 1, Employer = Id(company), RequiredEducation = education,
                            X = position.x, Z = position.z, MarginalValue = value, WageBudget = wageBudget,
                            TakeHomeRate = takeHomeRate, TaxFreeAllowance = Math.Max(0, parameters.m_ResidentialMinimumEarnings) });
                }
            }
            return result;
        }

        private int ApplyRetention(LaborResult result, EconomyParameterData parameters)
        {
            int count = 0;
            foreach (var proposal in result.Assignments.OrderBy(m => m.Job.Id))
            {
                if (proposal.Job.IncumbentWorker != proposal.Worker.Id || !proposal.RetentionAfterCompetition) continue;
                Entity person = FromId(proposal.Worker.Id), employer = FromId(proposal.Job.Employer);
                if (!ContractSalary.Eligible(EntityManager, person, employer, proposal.Job.RequiredEducation)) continue;
                var worker = EntityManager.GetComponentData<Worker>(person);
                int previous = ContractSalary.Get(EntityManager, person, worker, parameters);
                if (!raiseCash.TryGetValue(proposal.Job.Employer, out double remaining) ||
                    !LaborContractRules.TryAgree(proposal, previous, remaining, PayWageSystem.kUpdatesPerDay, out int gross)) continue;
                int rate = taxes.GetResidentialTaxRate(worker.m_Level);
                if (LaborContractRules.NetDailyIncome(gross, parameters.m_ResidentialMinimumEarnings, rate) <=
                    LaborContractRules.NetDailyIncome(previous, parameters.m_ResidentialMinimumEarnings, rate)) continue;
                var contract = new NegotiatedWage { Employer = employer, JobLevel = worker.m_Level,
                    DailyGross = gross, AcceptedFrame = simulation.frameIndex };
                if (EntityManager.HasComponent<NegotiatedWage>(person)) EntityManager.SetComponentData(person, contract);
                else EntityManager.AddComponentData(person, contract);
                raiseCash[proposal.Job.Employer] = remaining - (gross - previous);
                count++; accepted++;
                Mod.Log.Info($"labor agreed citizen={proposal.Worker.Id}, employer={proposal.Job.Employer}, level={worker.m_Level}, previousDaily={previous}, dailyGross={gross}, frame={simulation.frameIndex}; retention-only, no-money-written, payroll=original-game-job");
            }
            return count;
        }

        private double InputCost(ResourceStack input)
        {
            if (input.m_Resource == Resource.NoResource || input.m_Amount <= 0) return 0;
            double price = Data(input.m_Resource).m_Price.x;
            return price > 0 ? input.m_Amount * price : double.PositiveInfinity;
        }
        private ResourceData Data(Resource resource)
        {
            var prefab = resources.GetPrefab(resource);
            return EntityManager.HasComponent<ResourceData>(prefab) ? EntityManager.GetComponentData<ResourceData>(prefab) : default;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
        private static Entity FromId(long id) => new Entity { Index = (int)id, Version = (int)(id >> 32) };
    }
}
