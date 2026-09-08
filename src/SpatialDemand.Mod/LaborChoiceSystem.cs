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
    // Read-only contract forecasts. Worker/Employee contain no salary field;
    // vanilla's private Burst payroll job also owns company debits and tax accrual.
    // Never write wages, money, job matches, or disable the vanilla payroll here.
    public partial class LaborChoiceSystem : GameSystemBase
    {
        private EntityQuery seekers, employers, economy;
        private SimulationSystem simulation = null!;
        private ResourceSystem resources = null!;
        private TaxSystem taxes = null!;
        private bool faulted;

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
            RequireForUpdate(economy);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 4096;

        protected override void OnUpdate()
        {
            if (Mod.Settings == null || !Mod.Settings.LaborEnabled || faulted) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                double speed = Mod.Settings.ConstructionTravelKph, timeValue = Mod.Settings.ShoppingTimeValuePerHour;
                if (!Finite(speed) || speed <= 0 || !Finite(timeValue) || timeValue < 0) return;
                EntityManager.CompleteAllTrackedJobs();
                var parameters = economy.GetSingleton<EconomyParameterData>();
                var people = ReadWorkers(parameters);
                var jobs = ReadJobs(parameters);
                // Daily round-trip time, using the same physical speed and time value
                // as the other choice diagnostics. Straight line does not prove access.
                double commutePerMetre = 2 * timeValue / (speed * 1000);
                var result = LaborMarket.Clear(people, jobs, double.MaxValue, commutePerMetre, 1, 10000);
                if (result.Converged)
                    foreach (var match in result.Assignments.Take(6))
                        Mod.Log.Info($"labor contract forecast employer={match.Job.Employer}, slot={match.Job.Id}, citizen={match.Worker.Id}, education={match.Job.RequiredEducation}, gross={match.GrossWage:F2}, vanillaGross={parameters.GetWage(match.Job.RequiredEducation)}, takeHome={match.TakeHomeIncome:F2}, commute={match.CommuteCost:F2}, workerGain={match.WorkerUtility - match.Worker.OutsideOption:F2}, valueCeiling={match.Job.MarginalValue:F2}, reservedCash={match.Job.WageBudget:F2}, fillSurplus={match.EmployerSurplus:F2}, emptySurplus=0; applied=0, payroll=vanilla, horizon=game-day, value=full-sales-technology-estimate");
                Mod.Log.Info($"labor status frame={simulation.frameIndex}, sampledSeekers={people.Count}/{seekers.CalculateEntityCount()}, sampledSlots={jobs.Count}, eligibleEmployers={employers.CalculateEntityCount()}, hypotheticalMatches={result.Assignments.Count}, unfilled={result.UnfilledJobs.Count}, outside={result.OutsideWorkers.Count}, bids={result.Bids}, converged={result.Converged}, ms={timer.Elapsed.TotalMilliseconds:F2}; applied=0, payroll=vanilla, city-equilibrium=false, sample-cap=128-seekers/64-employers/128-slots, outside=observed-benefit-only, value=technology-at-prefab-prices-assuming-sales-and-full-efficiency, cash=after-incumbent-wages-rent-new-inputs, commute=straight-line-round-trip, net-bid-increment=1");
            }
            catch (Exception error)
            {
                faulted = true;
                Mod.Log.Error(error, "Labor diagnostics stopped for this session; vanilla employment and payroll remain enabled.");
            }
        }

        private List<LaborWorker> ReadWorkers(EconomyParameterData parameters)
        {
            var result = new List<LaborWorker>();
            using var entities = seekers.ToEntityArray(Allocator.Temp);
            foreach (var person in entities.OrderBy(Id).Take(128))
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

        private List<LaborJob> ReadJobs(EconomyParameterData parameters)
        {
            var result = new List<LaborJob>();
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
                if (count == 0 || maxWorkers <= 0) continue;
                double cash = EconomyUtils.GetResources(Resource.Money, EntityManager.GetBuffer<Resources>(company, true));
                double reserve = Math.Max(0, cash - Math.Max(0, lease.m_Rent) - EconomyUtils.CalculateTotalWage(staff, ref parameters));
                // Divide by every real vacancy, including slots outside this sample.
                // Each sampled position therefore has a disjoint cash reservation.
                double perSlotCash = reserve / count;
                double workforce = EconomyUtils.GetAverageWorkforce(maxWorkers, workplace.m_Complexity, level);
                if (workforce <= 0) continue;
                double production = EconomyUtils.GetCompanyProductionPerDay(1f, maxWorkers, level, !retail,
                    workplace, process, data, ref parameters);
                for (int education = 0; education < 5; education++)
                {
                    double quantity = production * EconomyUtils.GetWorkerWorkforce(50, education) / workforce;
                    double value = Math.Max(0, quantity * margin);
                    double wageBudget = Math.Max(0, perSlotCash - quantity * inputPrice);
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
    }
}
