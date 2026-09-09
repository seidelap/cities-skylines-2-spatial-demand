using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using Game.Citizens;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using HarmonyLib;
using SpatialDemand.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace SpatialDemand.Mod
{
    // Replace the wage input, not the payment. Vanilla remains the only owner of
    // payroll cadence, eligibility, company debits, household credits and tax accrual.
    // Managed helper patches cannot affect native Burst callers, so every inspected
    // salary-consuming job has an explicit, verified managed scheduling seam.
    internal static class NegotiatedPayrollHooks
    {
        private const string PatchId = "SpatialDemand.NegotiatedPayroll";
        private static readonly Harmony Harmony = new Harmony(PatchId);
        private static readonly Dictionary<MethodBase, string> JobNames = new Dictionary<MethodBase, string>();
        private static MethodInfo runWithoutJobs = null!;
        private static World? contextWorld;
        private static Dictionary<Entity, NegotiatedWage>? snapshot;
        private static Dictionary<IntPtr, Entity>? rosterOwners;
        internal static bool Ready { get; private set; }
        internal static bool Active => Ready && Mod.Settings != null && Mod.Settings.ApplyNegotiatedWages;
        internal static long ManagedJobs, SalarySubstitutions;
        internal static double ManagedMilliseconds;

        internal static bool Install()
        {
            if (Ready) return true;
            try
            {
                runWithoutJobs = typeof(JobChunkExtensions).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(m => m.Name == "RunByRefWithoutJobs" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                // Fail before installing any patch if this game changed a required seam.
                var specifications = new[] {
                    new[] { "Game.Simulation.PayWageSystem", "OnUpdate", "PayWageJob" },
                    new[] { "Game.Simulation.HouseholdBehaviorSystem", "OnUpdate", "HouseholdTickJob" },
                    new[] { "Game.Simulation.HouseholdFindPropertySystem", "OnUpdate", "FindPropertyJob" },
                    new[] { "Game.Simulation.CompanyEconomyStatisticSystem", "OnUpdate", "CompanyEconomyStatisticJob" },
                    new[] { "Game.Simulation.ProcessingCompanySystem", "OnUpdate", "UpdateProcessingJob" },
                    new[] { "Game.Simulation.SicknessCheckSystem", "OnUpdate", "SicknessCheckJob" },
                    new[] { "Game.Simulation.RentAdjustSystem", "OnUpdate", "AdjustRentJob" },
                    new[] { "Game.Simulation.CitizenPathfindSetup", "SetupFindHome", "SetupFindHomeJob" }
                };
                JobNames.Clear();
                foreach (var specification in specifications)
                {
                    var type = typeof(PayWageSystem).Assembly.GetType(specification[0], true)!;
                    var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        .Single(m => m.Name == specification[1]);
                    var job = type.GetNestedType(specification[2], BindingFlags.NonPublic | BindingFlags.Public)
                        ?? throw new MissingMemberException(type.FullName, specification[2]);
                    if (!typeof(IJobChunk).IsAssignableFrom(job) && !typeof(IJob).IsAssignableFrom(job))
                        throw new InvalidOperationException("Unsupported salary job: " + job.FullName);
                    if (job.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(f =>
                        f.GetCustomAttributes(false).Any(a => a.GetType().Name == "DeallocateOnJobCompletionAttribute")))
                        throw new InvalidOperationException("Salary job requires scheduler-owned disposal: " + job.FullName);
                    JobNames.Add(method, specification[2]);
                }
                var wageJob = typeof(PayWageSystem).GetNestedType("PayWageJob", BindingFlags.NonPublic)!;
                var pay = wageJob.GetMethod("PayWage", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingMethodException("PayWageJob.PayWage");
                if (pay.GetParameters().Length != 9 || pay.GetParameters()[8].ParameterType != typeof(EconomyParameterData).MakeByRefType())
                    throw new InvalidOperationException("Payroll signature changed.");
                var income = typeof(EconomyUtils).GetMethod("GetHouseholdIncome", BindingFlags.Static | BindingFlags.Public)!;
                var wages = typeof(EconomyUtils).GetMethod("CalculateTotalWage", new[] {
                    typeof(DynamicBuffer<Employee>), typeof(EconomyParameterData).MakeByRefType() })!;
                if (income == null || wages == null) throw new MissingMethodException("Salary helper changed.");

                Harmony.Patch(pay, prefix: Hook(nameof(BeforePayWage)), finalizer: Hook(nameof(RestorePayWage)));
                Harmony.Patch(income, postfix: Hook(nameof(AfterHouseholdIncome)));
                Harmony.Patch(wages, postfix: Hook(nameof(AfterTotalWage)));
                foreach (var method in JobNames.Keys)
                {
                    Harmony.Patch(method, prefix: Hook(method.Name == "SetupFindHome" ? nameof(EnterPathfind) : nameof(EnterSystem)),
                        transpiler: Hook(nameof(ReplaceSalarySchedule)), finalizer: Hook(nameof(LeaveSystem)));
                }
                Ready = true;
                Mod.Log.Info($"negotiated payroll preflight passed: salary-input hooks=3, scoped-managed-schedules={JobNames.Count}; default-off, Burst-global=unchanged, money-owner=vanilla");
                return true;
            }
            catch (Exception error)
            {
                Ready = false;
                Harmony.UnpatchAll(PatchId);
                JobNames.Clear();
                Mod.Log.Error(error, "Negotiated payroll preflight failed; no wage contracts can activate and vanilla payroll remains authoritative.");
                return false;
            }
        }

        internal static void Uninstall()
        {
            Ready = false;
            Harmony.UnpatchAll(PatchId);
            contextWorld = null; snapshot = null; rosterOwners = null; JobNames.Clear();
        }

        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NegotiatedPayrollHooks).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Static));

        private static IEnumerable<CodeInstruction> ReplaceSalarySchedule(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replacements = 0;
            string jobName = JobNames[__originalMethod];
            var result = instructions.ToList();
            foreach (var instruction in result)
            {
                if (!(instruction.operand is MethodInfo called) || !called.IsGenericMethod ||
                    called.GetGenericArguments().Length != 1 || called.GetGenericArguments()[0].Name != jobName) continue;
                string replacement;
                if (called.DeclaringType == typeof(JobChunkExtensions) && called.Name == "ScheduleParallel" && called.GetParameters().Length == 3)
                    replacement = nameof(ScheduleChunk);
                else if (called.DeclaringType == typeof(IJobExtensions) && called.Name == "Schedule" && called.GetParameters().Length == 2)
                    replacement = nameof(ScheduleSingle);
                else continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(NegotiatedPayrollHooks).GetMethod(replacement,
                    BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(called.GetGenericArguments());
                replacements++;
            }
            if (replacements != 1) throw new InvalidOperationException($"Expected one salary schedule in {__originalMethod}; found {replacements}.");
            return result;
        }

        private static void EnterSystem(ComponentSystemBase __instance, out World? __state)
        { __state = contextWorld; contextWorld = __instance.World; }
        private static void EnterPathfind(PathfindSetupSystem __0, out World? __state)
        { __state = contextWorld; contextWorld = __0.World; }
        private static Exception? LeaveSystem(Exception? __exception, World? __state)
        { contextWorld = __state; return __exception; }

        private static JobHandle ScheduleChunk<T>(T job, EntityQuery query, JobHandle dependsOn) where T : struct, IJobChunk
        {
            if (!Active) return job.ScheduleParallel(query, dependsOn);
            var timer = Stopwatch.StartNew();
            dependsOn.Complete();
            var previous = snapshot;
            var previousOwners = rosterOwners;
            snapshot = ReadContracts();
            try { runWithoutJobs.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { job, query }); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                throw;
            }
            finally { snapshot = previous; rosterOwners = previousOwners; ManagedJobs++; ManagedMilliseconds += timer.Elapsed.TotalMilliseconds; }
            return default;
        }

        private static JobHandle ScheduleSingle<T>(T job, JobHandle dependsOn) where T : struct, IJob
        {
            if (!Active) return job.Schedule(dependsOn);
            var timer = Stopwatch.StartNew();
            dependsOn.Complete();
            var previous = snapshot;
            var previousOwners = rosterOwners;
            snapshot = ReadContracts();
            try { job.Execute(); }
            finally { snapshot = previous; rosterOwners = previousOwners; ManagedJobs++; ManagedMilliseconds += timer.Elapsed.TotalMilliseconds; }
            return default;
        }

        // Snapshot outside job execution; all helper reads inside the managed job
        // use these validated values rather than touching EntityManager mid-iteration.
        private static Dictionary<Entity, NegotiatedWage> ReadContracts()
        {
            var world = contextWorld ?? World.DefaultGameObjectInjectionWorld;
            var result = new Dictionary<Entity, NegotiatedWage>();
            rosterOwners = new Dictionary<IntPtr, Entity>();
            if (world == null || !world.IsCreated) return result;
            var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<NegotiatedWage>(), ComponentType.ReadOnly<Worker>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var worker = manager.GetComponentData<Worker>(entity);
                if (ContractSalary.TryGet(manager, entity, worker, out _))
                {
                    result[entity] = manager.GetComponentData<NegotiatedWage>(entity);
                    rosterOwners[RosterIdentity(manager.GetBuffer<Employee>(worker.m_Workplace, true))] = worker.m_Workplace;
                }
            }
            return result;
        }

        private static bool Salary(Entity citizen, Worker worker, out int wage)
        {
            wage = 0;
            var contracts = snapshot ?? ReadContracts();
            if (!contracts.TryGetValue(citizen, out var contract) || contract.Employer != worker.m_Workplace || contract.JobLevel != worker.m_Level) return false;
            wage = contract.DailyGross;
            return true;
        }

        private static void BeforePayWage(Entity __1, Worker __3, ref EconomyParameterData __8, out EconomyParameterData __state)
        {
            __state = __8;
            if (!Active || !Salary(__1, __3, out int wage)) return;
            switch (__3.m_Level)
            {
                case 0: __8.m_Wage0 = wage; break;
                case 1: __8.m_Wage1 = wage; break;
                case 2: __8.m_Wage2 = wage; break;
                case 3: __8.m_Wage3 = wage; break;
                case 4: __8.m_Wage4 = wage; break;
            }
            SalarySubstitutions++;
        }
        private static Exception? RestorePayWage(Exception? __exception, ref EconomyParameterData __8, EconomyParameterData __state)
        { __8 = __state; return __exception; }

        private static void AfterHouseholdIncome(DynamicBuffer<HouseholdCitizen> __0, ref ComponentLookup<Worker> __1,
            ref ComponentLookup<HealthProblem> __3, ref EconomyParameterData __4, NativeArray<int> __5, ref int __result)
        {
            if (!Active) return;
            foreach (var member in __0)
            {
                var citizen = member.m_Citizen;
                if (!__1.HasComponent(citizen) || CitizenUtils.IsDead(citizen, ref __3)) continue;
                var worker = __1[citizen];
                if (!Salary(citizen, worker, out int wage)) continue;
                int rate = TaxSystem.GetResidentialTaxRate(worker.m_Level, __5);
                __result += LaborContractRules.NetDailyIncome(wage, __4.m_ResidentialMinimumEarnings, rate) -
                    LaborContractRules.NetDailyIncome(__4.GetWage(worker.m_Level), __4.m_ResidentialMinimumEarnings, rate);
            }
        }

        private static void AfterTotalWage(DynamicBuffer<Employee> __0, ref EconomyParameterData __1, ref int __result)
        {
            if (!Active) return;
            var contracts = snapshot ?? ReadContracts();
            // A stale foreign roster can briefly contain the same worker. Apply a
            // contract only to the exact current employer buffer captured before the
            // job. The address is an identity token; it is never dereferenced here.
            if (rosterOwners == null || !rosterOwners.TryGetValue(RosterIdentity(__0), out Entity employer)) return;
            foreach (var employee in __0)
                if (contracts.TryGetValue(employee.m_Worker, out var contract) && contract.Employer == employer && contract.JobLevel == employee.m_Level)
                    __result += contract.DailyGross - __1.GetWage(employee.m_Level);
        }

        private static unsafe IntPtr RosterIdentity(DynamicBuffer<Employee> employees)
            => (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(employees.AsNativeArray());
    }
}
