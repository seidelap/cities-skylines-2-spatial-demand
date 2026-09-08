using System;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SpatialDemand.Core;
using Unity.Collections;
using Unity.Entities;
using CimHousehold = Game.Citizens.Household;

namespace SpatialDemand.Mod
{
    // Observe only. Vanilla already chooses a network route using citizen path weights,
    // trade costs and service/stock scores. Shopping exposes one PathInformation winner,
    // unlike housing's PathInformations shortlist. A different seller cannot safely be
    // substituted without obtaining a new route. This system never edits a game component,
    // submits a path or sale, or disables any vanilla system.
    public partial class ShoppingChoiceSystem : GameSystemBase
    {
        private EntityQuery searches;
        private ResourceSystem resources = null!;
        private SimulationSystem simulation = null!;
        private bool faulted, logged;
        private uint lastLogFrame;
        private string? lastQuote;
        private int cursor;
        private long observed, pending, failedRoutes, unsupported, quoted, affordable;

        protected override void OnCreate()
        {
            base.OnCreate();
            resources = World.GetOrCreateSystemManaged<ResourceSystem>();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            searches = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<ResourceBuyer>(),
                    ComponentType.ReadOnly<PathInformation>(), ComponentType.ReadOnly<TripNeeded>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TravelPurpose>() }
            });
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;

        protected override void OnUpdate()
        {
            if (Mod.Settings == null || !Mod.Settings.ShoppingEnabled || faulted) return;
            try
            {
                EntityManager.CompleteAllTrackedJobs();
                var prefabs = resources.GetPrefabs();
                var data = GetComponentLookup<ResourceData>(true);
                using var entities = searches.ToEntityArray(Allocator.Temp);
                bool log = !logged || unchecked(simulation.frameIndex - lastLogFrame) >= 4096;
                string? sample = null;
                int count = Math.Min(entities.Length, 512);
                for (int i = 0; i < count; i++)
                {
                    var shopper = entities[(cursor + i) % entities.Length];
                    observed++;
                    var path = EntityManager.GetComponentData<PathInformation>(shopper);
                    if ((path.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0) { pending++; continue; }
                    if ((path.m_State & (PathFlags.Failed | PathFlags.Obsolete)) != 0 ||
                        !ShoppingPriceQuote.NonNegative(path.m_Duration) || !ShoppingPriceQuote.NonNegative(path.m_Distance))
                    { failedRoutes++; continue; }
                    var request = EntityManager.GetComponentData<ResourceBuyer>(shopper);
                    var seller = path.m_Destination;
                    // Keep import, storage, company, tourist and non-household special cases
                    // wholly in vanilla. A quote here is only for ordinary local retail.
                    if (!EntityManager.HasComponent<CimHousehold>(request.m_Payer) ||
                        EntityManager.HasComponent<TouristHousehold>(request.m_Payer) ||
                        EntityManager.HasComponent<CommuterHousehold>(request.m_Payer) ||
                        !EntityManager.HasBuffer<Resources>(request.m_Payer) ||
                        !EntityManager.HasComponent<ServiceAvailable>(seller) ||
                        !EntityManager.HasComponent<PropertyRenter>(seller) ||
                        !EntityManager.HasBuffer<Resources>(seller) ||
                        EntityManager.HasComponent<StorageCompany>(seller) ||
                        !ShoppingPriceQuote.TryGet(EntityManager, prefabs, ref data, seller, request.m_ResourceNeeded, true, out var price))
                    { unsupported++; continue; }
                    int stock = EconomyUtils.GetResources(request.m_ResourceNeeded, EntityManager.GetBuffer<Resources>(seller, true));
                    int money = EconomyUtils.GetResources(Resource.Money, EntityManager.GetBuffer<Resources>(request.m_Payer, true));
                    double budget = Math.Max(0d, (double)money - HouseholdBehaviorSystem.kMinimumShoppingMoney);
                    var offer = new ShopOffer(Id(seller), (long)request.m_ResourceNeeded, stock, price.UnitPrice, path.m_Duration);
                    var search = new ShoppingSearch(Id(shopper), (long)request.m_ResourceNeeded, request.m_AmountNeeded,
                        budget, Mod.Settings.ShoppingTimeValuePerHour, new[] { offer });
                    var evaluation = ShoppingMarket.Evaluate(search, offer);
                    quoted++;
                    if (evaluation.Feasible) affordable++;
                    if (sample == null)
                        sample = $"shopping quote observedFrame={simulation.frameIndex}, shopper={Id(shopper)}, seller={Id(seller)}, resource={request.m_ResourceNeeded}, requested={request.m_AmountNeeded}, stock={stock}, baseUnitPrice={price.BasePrice:F4}, embeddedBuyCost={price.EmbeddedBuyCost:F4}, serviceMultiplier={price.ServiceMultiplier:F4}, unitPrice={price.UnitPrice:F4}, spendableCash={budget:F2}, routeDistanceGameUnits={path.m_Distance:F2}, routeDurationGameUnits={path.m_Duration:F2}, fullBasketCheck={evaluation.Rejection}, estimatedTimeCost={evaluation.TimeCost:F2}; price=checkout-quote-before-total-rounding, route=vanilla-completed, duration-assumed-seconds, time-value=setting-not-charge, trip-fare=not-quoted, candidates=one-vanilla-winner, applied=false";
                }
                cursor = entities.Length == 0 ? 0 : (cursor + count) % entities.Length;
                if (sample != null) lastQuote = sample;
                if (!log) return;
                if (lastQuote != null) Mod.Log.Info(lastQuote);
                Mod.Log.Info($"shopping status frame={simulation.frameIndex}, observed={observed}, pending={pending}, failedRoutes={failedRoutes}, unsupported={unsupported}, quoted={quoted}, fullBasketsFeasibleBeforeTripFees={affordable}, sampleCap=512, applied=0; counters=observations, mode=observe-only, choice-owner=vanilla, shortlist=unavailable");
                logged = true; lastLogFrame = simulation.frameIndex;
            }
            catch (Exception error)
            {
                faulted = true;
                Mod.Log.Error(error, "Shopping observations stopped for this session; vanilla shopping remains unchanged.");
            }
        }

        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
    }
}
