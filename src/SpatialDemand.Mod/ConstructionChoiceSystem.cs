using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
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
    // Intercepts temporary spawn definitions BEFORE GenerateObjectsSystem creates a
    // building. One persisted project at a time is the pipeline reservation policy.
    public partial class ConstructionChoiceSystem : GameSystemBase
    {
        private EntityQuery definitions, children, permits, buildings;
        private SimulationSystem simulation = null!;
        private BusinessChoiceSystem business = null!;
        private ConstructionHousingSystem housing = null!;
        private bool faulted;
        internal bool ForcedBatch;
        private uint lastReconcile, lastLog;
        private long proposed, funded, rejected, completed;
        private readonly Dictionary<string, long> reasons = new Dictionary<string, long>();
        private string lastQuote = "none";
        internal bool CanGenerate => !faulted && Mod.Settings != null && Mod.Settings.ConstructionEnabled &&
            Mod.Settings.ApplyConstruction && SettingsValid() && permits.IsEmptyIgnoreFilter;
        internal bool HasPendingProject
        {
            get
            {
                using var data = permits.ToComponentDataArray<DevelopmentPermit>(Allocator.Temp);
                return data.Any(p => p.Finished == 0);
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            business = World.GetOrCreateSystemManaged<BusinessChoiceSystem>();
            housing = World.GetOrCreateSystemManaged<ConstructionHousingSystem>();
            // Vanilla spawn definitions carry Deleted as a cleanup marker. Excluding it
            // would silently miss every generated project.
            definitions = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<ObjectDefinition>(),
                ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>());
            children = GetEntityQuery(ComponentType.ReadOnly<OwnerDefinition>(), ComponentType.ReadOnly<CreationDefinition>());
            permits = GetEntityQuery(ComponentType.ReadOnly<DevelopmentPermit>());
            buildings = GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
        }

        protected override void OnUpdate()
        {
            var settings = Mod.Settings;
            bool reconcileDue = !permits.IsEmptyIgnoreFilter && unchecked(simulation.frameIndex - lastReconcile) >= 256;
            bool logDue = unchecked(simulation.frameIndex - lastLog) >= 4096;
            if (!ForcedBatch && !reconcileDue && (faulted || settings == null || !settings.ConstructionEnabled ||
                (definitions.IsEmptyIgnoreFilter && !logDue))) return;
            EntityManager.CompleteAllTrackedJobs();
            bool apply = settings != null && settings.ConstructionEnabled && settings.ApplyConstruction && !faulted;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var roots = new List<Entity>();
            try
            {
                // Receipts must reconcile even when evaluation is switched off.
                if (reconcileDue)
                { Reconcile(); lastReconcile = simulation.frameIndex; }
                if (!ForcedBatch && (faulted || settings == null || !settings.ConstructionEnabled)) return;
                using (var all = definitions.ToEntityArray(Allocator.Temp))
                    foreach (var entity in all) if (IsProject(entity)) roots.Add(entity);
                bool valid = settings != null && SettingsValid();
                var choices = new Dictionary<long, DevelopmentChoice>();
                var businessChoices = new Dictionary<long, BusinessChoice>();
                var prospects = new List<ConstructionHousingSystem.Prospect>();
                bool busy = !permits.IsEmptyIgnoreFilter;
                if (roots.Count > 0 && valid && !busy && (!ForcedBatch || apply))
                {
                    prospects = housing.Read();
                    var book = business.ReadMarket();
                    var developers = new DevelopmentMarket();
                    foreach (var entity in roots.Take(16))
                    {
                        var definition = EntityManager.GetComponentData<CreationDefinition>(entity);
                        var prefab = definition.m_Prefab;
                        var position = EntityManager.GetComponentData<ObjectDefinition>(entity).m_Position;
                        var property = EntityManager.GetComponentData<BuildingPropertyData>(prefab);
                        var lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize;
                        bool residential = property.m_ResidentialProperties > 0;
                        var project = new DevelopmentProject { Id = Id(entity), Site = Id(entity),
                            Units = residential ? property.m_ResidentialProperties : 1,
                            ConstructionCost = (double)lot.x * lot.y * settings!.ConstructionCostPerCell,
                            UpkeepPerDay = EntityManager.HasComponent<ConsumptionData>(prefab)
                                ? Math.Max(0, EntityManager.GetComponentData<ConsumptionData>(prefab).m_Upkeep) : 0,
                            PaybackDays = settings.ConstructionPaybackDays };
                        string costSource = "configured-per-cell";
                        if (EntityManager.HasComponent<PlaceableObjectData>(prefab))
                        {
                            uint cost = EntityManager.GetComponentData<PlaceableObjectData>(prefab).m_ConstructionCost;
                            if (cost > 0) { project.ConstructionCost = cost; costSource = "prefab"; }
                        }
                        string businessReason = "not-applicable";
                        if (residential) project.Bids = housing.Bids(prospects, prefab, position, project.Site);
                        else
                        {
                            // Wages/inputs are already deducted. Retain one currency cent
                            // for the prospective firm, so zero operating surplus is not entry.
                            var activities = business.Activities(prefab, position, project.Site, 0);
                            var chosen = book.Choose(activities);
                            businessReason = chosen?.Reason ?? (activities.Count == 0 ? "unsupported-activity" :
                                string.Join("/", activities.Select(a => book.Evaluate(a).Reason).Distinct()));
                            if (chosen != null)
                            {
                                businessChoices[project.Id] = chosen;
                                project.Bids = new[] { new TenantBid { Tenant = project.Id, Rent = Math.Max(0, chosen.Profit - 0.01) } };
                            }
                        }
                        var choice = developers.Evaluate(project);
                        choices[project.Id] = choice;
                        proposed++;
                        string reason = residential ? choice.Reason : choice.Reason + "/" + businessReason;
                        reasons.TryGetValue(reason, out long count); reasons[reason] = count + 1;
                        lastQuote = $"sector={(residential ? "residential" : "business")}, tenants={choice.Tenants.Count}/{project.Units}, rent={choice.RentPerUnit:F2}, capital={project.ConstructionCost:F2}, costSource={costSource}, upkeep={project.UpkeepPerDay:F2}, surplus={choice.Surplus:F2}, result={reason}, households={prospects.Count}";
                        if (logDue || (apply && choice.Reason == "funded"))
                            Mod.Log.Info($"construction quote frame={simulation.frameIndex}, sector={(residential ? "residential" : "business")}, proposal={project.Id}, prefab={Id(prefab)}, tenants={choice.Tenants.Count}/{project.Units}, rent={choice.RentPerUnit:F2}, capital={project.ConstructionCost:F2}, costSource={costSource}, upkeep={project.UpkeepPerDay:F2}, paybackDays={project.PaybackDays}, surplus={choice.Surplus:F2}, result={choice.Reason}, business={businessReason}; forecast-only, travel=straight-line, sampleHouseholds={prospects.Count}");
                    }
                }
                // The developer's outside option is no project. The largest surplus wins
                // this finite proposal round; vanilla still supplies the geometry shortlist.
                var winner = choices.Values.Where(c => c.Reason == "funded")
                    .OrderByDescending(c => c.Surplus).ThenBy(c => c.Project.Id).FirstOrDefault();
                Entity accepted = Entity.Null;
                if (apply && valid && winner != null)
                {
                    accepted = roots.Single(e => Id(e) == winner.Project.Id);
                    var definition = EntityManager.GetComponentData<CreationDefinition>(accepted);
                    var position = EntityManager.GetComponentData<ObjectDefinition>(accepted).m_Position;
                    Entity activity = Entity.Null;
                    if (businessChoices.TryGetValue(winner.Project.Id, out var firm))
                        activity = FromId(firm.Activity.Id);
                    // Standalone entity survives deletion of the temporary spawn definition.
                    var receipt = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(receipt, new DevelopmentPermit { Prefab = definition.m_Prefab,
                        Position = position, Activity = activity, Frame = simulation.frameIndex,
                        ExpectedTenants = winner.Tenants.Count, Rent = winner.RentPerUnit });
                    if (activity == Entity.Null)
                        foreach (var person in prospects.Where(p => winner.Tenants.Contains(p.Household.Id)))
                            if (!EntityManager.HasComponent<SavedPreferences>(person.Entity))
                                EntityManager.AddComponentData(person.Entity, new SavedPreferences { Seed = person.Seed });
                    funded++;
                    Mod.Log.Info($"construction permitted proposal={winner.Project.Id}, activity={Id(activity)}; one-project pipeline reserved, no money debited or lease guaranteed");
                }
                // If Apply was switched off after demand-free generation, discard that
                // forced batch. Turning a setting off must never leak bypassed proposals.
                if (apply || ForcedBatch)
                {
                    var denied = roots.Where(e => e != accepted).ToList();
                    RemoveProjects(denied);
                    rejected += denied.Count;
                }
                if (unchecked(simulation.frameIndex - lastLog) >= 4096 || accepted != Entity.Null)
                {
                    Mod.Log.Info($"construction status frame={simulation.frameIndex}, apply={apply}, forced={ForcedBatch}, validSettings={valid}, pipelineBusy={busy}, proposals={proposed}, funded={funded}, rejected={rejected}, completed={completed}, roots={roots.Count}, evaluated={choices.Count}, ms={timer.Elapsed.TotalMilliseconds:F2}; cap=one-project-at-a-time-and-per-game-day, invalid-or-unsampled=wait");
                    Mod.Log.Info($"construction reasons {string.Join(";", reasons.Select(p => p.Key + "=" + p.Value))}; lastQuote={lastQuote}");
                    lastLog = simulation.frameIndex;
                }
            }
            catch (Exception error)
            {
                faulted = true;
                // Fail closed for this intercepted batch, then restore vanilla generation.
                if (apply || ForcedBatch) RemoveProjects(roots);
                Mod.Log.Error(error, "Construction selection stopped; intercepted proposals cancelled. Vanilla generation resumes next round.");
            }
            // Keep the latch if no definitions have arrived yet. The source's ECB may
            // play back after this invocation; switching Apply off must still cancel it.
            finally { if (roots.Count > 0) ForcedBatch = false; }
        }

        private bool IsProject(Entity entity)
        {
            var d = EntityManager.GetComponentData<CreationDefinition>(entity);
            const CreationFlags required = CreationFlags.Permanent | CreationFlags.Construction;
            return (d.m_Flags & required) == required && d.m_Original == Entity.Null && d.m_Owner == Entity.Null &&
                !EntityManager.HasComponent<SignatureBuildingData>(d.m_Prefab) &&
                EntityManager.HasComponent<SpawnableBuildingData>(d.m_Prefab) &&
                EntityManager.HasComponent<BuildingPropertyData>(d.m_Prefab) && EntityManager.HasComponent<BuildingData>(d.m_Prefab);
        }

        private void RemoveProjects(List<Entity> roots)
        {
            var owners = new HashSet<OwnerDefinition>();
            foreach (var entity in roots)
            {
                if (!EntityManager.Exists(entity)) continue;
                var d = EntityManager.GetComponentData<CreationDefinition>(entity);
                var o = EntityManager.GetComponentData<ObjectDefinition>(entity);
                owners.Add(new OwnerDefinition { m_Prefab = d.m_Prefab, m_Position = o.m_Position, m_Rotation = o.m_Rotation });
            }
            using var owned = children.ToEntityArray(Allocator.Temp);
            foreach (var entity in owned)
                if (owners.Contains(EntityManager.GetComponentData<OwnerDefinition>(entity))) EntityManager.DestroyEntity(entity);
            foreach (var entity in roots) if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
        }

        private void Reconcile()
        {
            if (permits.IsEmptyIgnoreFilter) return;
            using var receipts = permits.ToEntityArray(Allocator.Temp);
            using var actual = buildings.ToEntityArray(Allocator.Temp);
            foreach (var receipt in receipts)
            {
                var p = EntityManager.GetComponentData<DevelopmentPermit>(receipt);
                uint age = unchecked(simulation.frameIndex - p.Frame);
                if (p.Finished != 0) { if (age >= 262144) EntityManager.DestroyEntity(receipt); continue; }
                if (p.Building == Entity.Null)
                    foreach (var building in actual)
                        if (EntityManager.GetComponentData<PrefabRef>(building).m_Prefab == p.Prefab &&
                            math.distancesq(EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position.xz, p.Position.xz) < 1f)
                        { p.Building = building; break; }
                string result = "";
                if (p.Building != Entity.Null)
                {
                    if (!EntityManager.Exists(p.Building) || EntityManager.HasComponent<Deleted>(p.Building) || EntityManager.HasComponent<Destroyed>(p.Building))
                        result = "cancelled-or-demolished";
                    else if (!EntityManager.HasComponent<Game.Objects.UnderConstruction>(p.Building))
                    { result = "completed"; completed++; }
                }
                else if (age >= 16384) result = "definition-did-not-produce-building";
                if (result.Length > 0)
                {
                    p.Finished = 1;
                    int occupants = EntityManager.HasBuffer<Renter>(p.Building) ? EntityManager.GetBuffer<Renter>(p.Building, true).Length : 0;
                    Mod.Log.Info($"construction settlement result={result}, building={Id(p.Building)}, expectedTenants={p.ExpectedTenants}, actualRenters={occupants}, forecastRent={p.Rent:F2}, forecastActivity={Id(p.Activity)}; forecast lease not enforced, normal tenancy and business selection resume");
                }
                EntityManager.SetComponentData(receipt, p);
            }
        }

        private static bool SettingsValid() => Mod.Settings!.ConstructionCostPerCell > 0 && Mod.Settings.ConstructionPaybackDays > 0 &&
            Mod.Settings.ConstructionTravelKph > 0 && !float.IsInfinity(Mod.Settings.ConstructionTravelKph) &&
            Mod.Settings.BusinessRangeMetres >= 0 && Mod.Settings.ShoppingTimeValuePerHour >= 0 && !float.IsInfinity(Mod.Settings.ShoppingTimeValuePerHour);
        private static long Id(Entity e) => ((long)e.Version << 32) | (uint)e.Index;
        private static Entity FromId(long id) => new Entity { Index = (int)id, Version = (int)(id >> 32) };
    }

    public struct DevelopmentPermit : IComponentData, ISerializable
    {
        public Entity Prefab, Building, Activity;
        public float3 Position;
        public uint Frame;
        public byte Finished;
        public int ExpectedTenants;
        public double Rent;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { writer.Write(1); writer.Write(Prefab); writer.Write(Building); writer.Write(Activity); writer.Write(Position);
            writer.Write(Frame); writer.Write(Finished); writer.Write(ExpectedTenants); writer.Write(Rent); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        { reader.Read(out int version); if (version != 1) throw new InvalidOperationException("Unknown development permit version.");
            reader.Read(out Prefab); reader.Read(out Building); reader.Read(out Activity); reader.Read(out Position);
            reader.Read(out Frame); reader.Read(out Finished); reader.Read(out ExpectedTenants); reader.Read(out Rent); }
    }
}
