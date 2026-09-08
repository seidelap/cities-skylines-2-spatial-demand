using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Buildings;
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
    // New entrants only. Existing firms retain their activity, stock, employees and history.
    // Vanilla initialization constructs the chosen template and submits its initial tenancy.
    public partial class BusinessChoiceSystem : GameSystemBase
    {
        private EntityQuery sites, templates, requests, suppliers, receipts, entries, economy;
        private SimulationSystem simulation = null!;
        private ResourceSystem resources = null!;
        private EndFrameBarrier barrier = null!;
        private bool faulted;
        private int cursor;
        private long evaluated, proposed, submitted, settled, recovered;

        protected override void OnCreate()
        {
            base.OnCreate();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            resources = World.GetOrCreateSystemManaged<ResourceSystem>();
            barrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            sites = GetEntityQuery(ComponentType.ReadOnly<PropertyOnMarket>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Renter>(), ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            templates = GetEntityQuery(ComponentType.ReadOnly<ArchetypeData>(), ComponentType.ReadOnly<IndustrialProcessData>(),
                ComponentType.ReadOnly<WorkplaceData>(), ComponentType.Exclude<ExtractorCompanyData>(), ComponentType.Exclude<StorageCompanyData>());
            requests = GetEntityQuery(ComponentType.ReadOnly<ResourceBuyer>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            suppliers = GetEntityQuery(new EntityQueryDesc {
                All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>() },
                Any = new[] { ComponentType.ReadOnly<ResourceSeller>(), ComponentType.ReadOnly<StorageCompany>(), ComponentType.ReadOnly<CargoTransportStation>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Game.Routes.ShipStop>(), ComponentType.ReadOnly<Game.Routes.AirplaneStop>(), ComponentType.ReadOnly<Game.Routes.TrainStop>() }
            });
            receipts = GetEntityQuery(ComponentType.ReadOnly<PendingBusiness>(), ComponentType.Exclude<Created>());
            entries = GetEntityQuery(ComponentType.ReadOnly<BusinessEntry>());
            economy = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            RequireForUpdate(economy);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 256;

        protected override void OnUpdate()
        {
            if (Mod.Settings == null) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                EntityManager.CompleteAllTrackedJobs();
                Reconcile();
                if (!Mod.Settings.BusinessEnabled || faulted) return;
                var buyers = ReadBuyers();
                var sellers = ReadSellers();
                var book = MakeMarket(buyers, sellers);
                var reasons = new Dictionary<string, int>();
                bool cooldown = false;
                using (var recent = entries.ToComponentDataArray<BusinessEntry>(Allocator.Temp))
                    foreach (var entry in recent)
                        cooldown |= unchecked(simulation.frameIndex - entry.Frame) < 262144;
                int scanned = 0, compatible = 0;
                using var buildings = sites.ToEntityArray(Allocator.Temp);
                using var activities = templates.ToEntityArray(Allocator.Temp);
                var parameters = economy.GetSingleton<EconomyParameterData>();
                // Bounded work, rotating through premises rather than rescanning the city each frame.
                for (int i = 0; i < Math.Min(buildings.Length, 16); i++)
                {
                    var building = buildings[(cursor + i) % buildings.Length];
                    scanned++;
                    if (!Available(building)) { Count(reasons, "unavailable-premises"); continue; }
                    var options = new List<BusinessActivity>();
                    var prefabs = new Dictionary<long, Entity>();
                    foreach (var prefab in activities)
                    {
                        if (!TryActivity(building, prefab, parameters, out var option)) continue;
                        compatible++; evaluated++;
                        options.Add(option); prefabs[option.Id] = prefab;
                        Count(reasons, book.Evaluate(option).Reason);
                    }
                    var best = book.Choose(options);
                    if (best == null) continue;
                    proposed++;
                    var chosen = prefabs[best.Activity.Id];
                    string sector = best.Activity.Retail ? "commercial" :
                        (Data((Resource)best.Activity.Output).m_Weight == 0 ? "office" : "industrial");
                    Mod.Log.Info($"business choice sector={sector}, property={Id(building)}, prefab={Id(chosen)}, output={(Resource)best.Activity.Output}, quantity={best.Quantity:F2}, revenue={best.Revenue:F2}, inputs={best.InputCost:F2}, fixedCost={best.Activity.FixedCost:F2}, surplus={best.Profit:F2}, apply={Mod.Settings.ApplyBusinessChoices}, cooldown={cooldown}; forecast=current-orders, transport=game-freight-cost-on-straight-line, prices=checkout-quotes");
                    book.Commit(best); // Reserve projected buyers and inputs before considering another site.
                    if (!Mod.Settings.ApplyBusinessChoices || cooldown || World.GetOrCreateSystemManaged<ConstructionChoiceSystem>().HasPendingProject) continue;
                    var commands = barrier.CreateCommandBuffer();
                    var company = commands.CreateEntity(EntityManager.GetComponentData<ArchetypeData>(chosen).m_Archetype);
                    commands.SetComponent(company, new PrefabRef { m_Prefab = chosen });
                    commands.AddComponent(company, new PropertyRenter { m_Property = building });
                    commands.AddComponent(company, new PendingBusiness { Property = building, Frame = simulation.frameIndex });
                    commands.AddComponent(company, new BusinessEntry { Frame = simulation.frameIndex });
                    submitted++; cooldown = true;
                }
                cursor = buildings.Length == 0 ? 0 : (cursor + scanned) % buildings.Length;
                Mod.Log.Info($"business status frame={simulation.frameIndex}, apply={Mod.Settings.ApplyBusinessChoices}, buyers={buyers.Count}/{requests.CalculateEntityCount()}, suppliers={sellers.Count}, sites={scanned}/{buildings.Length}, compatible={compatible}, evaluated={evaluated}, proposed={proposed}, submitted={submitted}, settled={settled}, recovered={recovered}, pending={receipts.CalculateEntityCount()}, cooldown={cooldown}, ms={timer.Elapsed.TotalMilliseconds:F2}, reasons={string.Join(";", reasons.Select(p => p.Key + "=" + p.Value))}; samples-capped=512, entry-cap=one-per-game-day");
            }
            catch (Exception error)
            {
                faulted = true;
                Mod.Log.Error(error, "Business selection stopped for this session; vanilla company systems remain enabled.");
            }
        }

        internal BusinessMarket ReadMarket() => MakeMarket(ReadBuyers(), ReadSellers());

        private BusinessMarket MakeMarket(List<Purchase> buyers, List<StockOffer> sellers)
            => new BusinessMarket(buyers, sellers, Mod.Settings!.BusinessRangeMetres, 0, TransportQuote);

        // Quote only the new leg. Seller prices already include their embedded upstream
        // trade costs. Game freight uses resource weight and actual proposed shipment size;
        // distance is still an estimate, not proof of a usable road connection.
        private double TransportQuote(long resource, double quantity, double distance, bool retail)
        {
            if (retail)
            {
                double speed = Mod.Settings!.ConstructionTravelKph;
                if (speed <= 0 || double.IsNaN(speed) || double.IsInfinity(speed)) return double.PositiveInfinity;
                return distance / 1000 / speed * Mod.Settings.ShoppingTimeValuePerHour;
            }
            if (quantity > int.MaxValue || distance > float.MaxValue) return double.PositiveInfinity;
            var data = Data((Resource)resource);
            if (data.m_Weight < 0 || !math.isfinite(data.m_Weight)) return double.PositiveInfinity;
            int cost = EconomyUtils.GetTransportCost((float)distance, (Resource)resource, (int)Math.Ceiling(quantity), data.m_Weight);
            return cost >= 0 ? cost : double.PositiveInfinity;
        }

        internal List<BusinessActivity> Activities(Entity buildingPrefab, float3 position, long site, double rent)
        {
            var result = new List<BusinessActivity>();
            if (economy.IsEmptyIgnoreFilter) return result;
            var parameters = economy.GetSingleton<EconomyParameterData>();
            using var prefabs = templates.ToEntityArray(Allocator.Temp);
            foreach (var prefab in prefabs)
                if (TryActivity(buildingPrefab, position, site, rent, prefab, parameters, out var activity)) result.Add(activity);
            return result;
        }

        private List<Purchase> ReadBuyers()
        {
            var result = new List<Purchase>();
            using var entities = requests.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities.Take(512))
            {
                var buyer = EntityManager.GetComponentData<ResourceBuyer>(entity);
                if (buyer.m_AmountNeeded <= 0 || buyer.m_ResourceNeeded == Resource.NoResource || !math.all(math.isfinite(buyer.m_Location))) continue;
                bool retail = EntityManager.HasComponent<Game.Citizens.Household>(buyer.m_Payer);
                // Never invent purchasing power for an unobservable payer.
                if (!EntityManager.HasBuffer<Resources>(buyer.m_Payer)) continue;
                int money = EconomyUtils.GetResources(Resource.Money, EntityManager.GetBuffer<Resources>(buyer.m_Payer, true));
                double spendable = retail ? Math.Max(0d, (double)money - HouseholdBehaviorSystem.kMinimumShoppingMoney) : Math.Max(0, money);
                double budget = spendable / buyer.m_AmountNeeded;
                result.Add(new Purchase { Id = Id(entity), Resource = (long)buyer.m_ResourceNeeded, Quantity = buyer.m_AmountNeeded,
                    BudgetPerUnit = budget, Retail = retail, X = buyer.m_Location.x, Z = buyer.m_Location.z });
            }
            return result;
        }

        private List<StockOffer> ReadSellers()
        {
            var result = new List<StockOffer>();
            var data = GetComponentLookup<ResourceData>(true);
            var resourcePrefabs = resources.GetPrefabs();
            var trucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true);
            var guests = GetBufferLookup<Game.Vehicles.GuestVehicle>(true);
            var layouts = GetBufferLookup<Game.Vehicles.LayoutElement>(true);
            using var entities = suppliers.ToEntityArray(Allocator.Temp);
            long quoteId = 0;
            foreach (var entity in entities.OrderBy(Id).Take(512))
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                var building = EntityManager.HasComponent<PropertyRenter>(entity)
                    ? EntityManager.GetComponentData<PropertyRenter>(entity).m_Property : entity;
                if (!EntityManager.HasComponent<Game.Objects.Transform>(building) || EntityManager.HasComponent<Abandoned>(building) ||
                    EntityManager.HasComponent<Condemned>(building) || EntityManager.HasComponent<Destroyed>(building)) continue;
                if (EntityManager.HasComponent<Game.Buildings.Building>(building) && BuildingUtils.CheckOption(
                    EntityManager.GetComponentData<Game.Buildings.Building>(building), BuildingOption.Inactive)) continue;
                bool retail = EntityManager.HasComponent<ServiceAvailable>(entity);
                Resource allowed = EntityManager.HasComponent<StorageCompanyData>(prefab) && !retail
                    ? EntityManager.GetComponentData<StorageCompanyData>(prefab).m_StoredResources
                    : EntityManager.HasComponent<IndustrialProcessData>(prefab)
                        ? EntityManager.GetComponentData<IndustrialProcessData>(prefab).m_Output.m_Resource : Resource.NoResource;
                var position = EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position;
                foreach (var stock in EntityManager.GetBuffer<Resources>(entity, true))
                {
                    if (stock.m_Resource == Resource.Money || stock.m_Resource == Resource.NoResource || (allowed & stock.m_Resource) == 0) continue;
                    int reserved = Game.Vehicles.VehicleUtils.GetAllBuyingResourcesTrucks(entity, stock.m_Resource, ref trucks, ref guests, ref layouts);
                    int amount = stock.m_Amount - reserved;
                    if (amount <= 0 || !ShoppingPriceQuote.TryGet(EntityManager, resourcePrefabs, ref data, entity, stock.m_Resource, retail, out var quote)) continue;
                    // Per-resource IDs are local to this snapshot; outside connections and
                    // warehouses may offer several resources. Never fabricate import stock.
                    result.Add(new StockOffer { Id = ++quoteId, Resource = (long)stock.m_Resource,
                        Quantity = amount, Price = quote.UnitPrice, Retail = retail, X = position.x, Z = position.z });
                }
            }
            return result;
        }

        private bool TryActivity(Entity building, Entity prefab, EconomyParameterData parameters, out BusinessActivity activity)
            => TryActivity(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab,
                EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position, Id(building),
                Math.Max(0, EntityManager.GetComponentData<PropertyOnMarket>(building).m_AskingRent), prefab, parameters, out activity);

        private bool TryActivity(Entity buildingPrefab, float3 position, long site, double rent,
            Entity prefab, EconomyParameterData parameters, out BusinessActivity activity)
        {
            activity = null!;
            bool retail = EntityManager.HasComponent<CommercialCompanyData>(prefab);
            if (!retail && !EntityManager.HasComponent<IndustrialCompanyData>(prefab)) return false;
            if (!EntityManager.HasComponent<BuildingPropertyData>(buildingPrefab) || !EntityManager.HasComponent<SpawnableBuildingData>(buildingPrefab)) return false;
            var property = EntityManager.GetComponentData<BuildingPropertyData>(buildingPrefab);
            var process = EntityManager.GetComponentData<IndustrialProcessData>(prefab);
            var output = process.m_Output.m_Resource;
            if (process.m_Output.m_Amount <= 0 || output == Resource.NoResource ||
                ((retail ? property.m_AllowedSold : property.m_AllowedManufactured) & output) == 0) return false;
            if ((process.m_Input1.m_Resource & property.m_AllowedInput) != process.m_Input1.m_Resource ||
                (process.m_Input2.m_Resource & property.m_AllowedInput) != process.m_Input2.m_Resource) return false;
            var workplace = EntityManager.GetComponentData<WorkplaceData>(prefab);
            var data = Data(output);
            if (data.m_Price.x <= 0 || (retail ? data.m_NeededWorkPerUnit.y : data.m_NeededWorkPerUnit.x) <= 0) return false;
            int level = EntityManager.GetComponentData<SpawnableBuildingData>(buildingPrefab).m_Level;
            int workers = Math.Max(1, workplace.m_MaxWorkers);
            activity = new BusinessActivity { Id = Id(prefab), Site = site, Retail = retail, Output = (long)output,
                X = position.x, Z = position.z, Price = retail ? data.m_Price.y : data.m_Price.x,
                Capacity = EconomyUtils.GetCompanyProductionPerDay(1f, workers, level, !retail, workplace, process, data, ref parameters),
                FixedCost = Math.Max(0, rent) +
                    Math.Max(0, EconomyUtils.CalculateTotalWage(workers, workplace.m_Complexity, level, parameters)) };
            AddInput(activity, process.m_Input1, process.m_Output.m_Amount);
            AddInput(activity, process.m_Input2, process.m_Output.m_Amount);
            return true;
        }

        private bool Available(Entity building)
        {
            if (EntityManager.HasComponent<Signature>(building) || EntityManager.HasComponent<Abandoned>(building) || EntityManager.HasComponent<Condemned>(building) ||
                EntityManager.HasComponent<Destroyed>(building) || EntityManager.HasComponent<Game.Objects.UnderConstruction>(building)) return false;
            foreach (var renter in EntityManager.GetBuffer<Renter>(building, true))
                if (EntityManager.HasComponent<CompanyData>(renter.m_Renter)) return false;
            return true;
        }

        private void Reconcile()
        {
            using var entities = receipts.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var receipt = EntityManager.GetComponentData<PendingBusiness>(entity);
                if (simulation.frameIndex == receipt.Frame) continue;
                bool found = EntityManager.HasBuffer<Renter>(receipt.Property) &&
                    EntityManager.GetBuffer<Renter>(receipt.Property, true).Any(r => r.m_Renter == entity);
                if (found) settled++;
                else
                {
                    // Let the game's property finder relocate a refused entrant with its chosen activity.
                    if (EntityManager.HasComponent<PropertyRenter>(entity)) EntityManager.RemoveComponent<PropertyRenter>(entity);
                    if (EntityManager.HasComponent<PropertySeeker>(entity)) EntityManager.SetComponentEnabled<PropertySeeker>(entity, true);
                    recovered++;
                }
                Mod.Log.Info($"business settlement company={Id(entity)}, property={Id(receipt.Property)}, result={(found ? "settled" : "delegated-to-property-search")}");
                EntityManager.RemoveComponent<PendingBusiness>(entity);
            }
        }

        private ResourceData Data(Resource resource)
        {
            var prefab = resources.GetPrefab(resource);
            return EntityManager.HasComponent<ResourceData>(prefab) ? EntityManager.GetComponentData<ResourceData>(prefab) : default;
        }
        private static void AddInput(BusinessActivity a, ResourceStack input, int output)
        {
            if (input.m_Resource == Resource.NoResource || input.m_Amount <= 0) return;
            long key = (long)input.m_Resource;
            a.Inputs.TryGetValue(key, out double previous);
            a.Inputs[key] = previous + input.m_Amount / (double)output;
        }
        private static void Count(Dictionary<string, int> counts, string key) { counts.TryGetValue(key, out int n); counts[key] = n + 1; }
        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
    }

    public struct BusinessEntry : IComponentData, ISerializable
    {
        public uint Frame;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Frame); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Frame); }
    }
    public struct PendingBusiness : IComponentData, ISerializable
    {
        public Entity Property;
        public uint Frame;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { writer.Write(Property); writer.Write(Frame); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out Property); reader.Read(out Frame); }
    }
}
