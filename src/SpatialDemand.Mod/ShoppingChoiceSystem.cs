using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Serialization;
using Game.Simulation;
using Game.Tools;
using SpatialDemand.Core;
using Unity.Collections;
using Unity.Entities;

namespace SpatialDemand.Mod
{
    // Every alternative, including the incumbent, is routed from one captured building
    // using one real pedestrian profile. Vanilla owns purchase, stock, money and trips.
    public partial class ShoppingChoiceSystem : GameSystemBase, IPreDeserialize
    {
        private const int CandidateLimit = 8, ObserveConcurrency = 4;
        private const uint TimeoutFrames = 512, RetryFrames = 4096;
        private EntityQuery searches, sellers, residents, holds, probes, backups;
        private ResourceSystem resources = null!;
        private SimulationSystem simulation = null!;
        private PathfindSetupSystem paths = null!;
        private ResourceBuyerSystem buying = null!;
        private readonly Dictionary<Entity, Session> sessions = new Dictionary<Entity, Session>();
        private readonly Dictionary<Entity, uint> recent = new Dictionary<Entity, uint>();
        private bool faulted, logged;
        private uint lastLogFrame;
        private int cursor;
        private string? lastQuote;
        private long started, completed, unsupported, failedRoutes, held, handedOff, restored;

        private sealed class Candidate
        {
            internal Entity Seller, Probe;
            internal ShopOffer Offer;
            internal double DistanceSquared;
        }
        private sealed class Session
        {
            internal Entity Shopper, Origin;
            internal ResourceBuyer Request;
            internal PathInformation Original;
            internal uint Started;
            internal double Budget, TimeValue;
            internal bool Apply;
            internal readonly List<Candidate> Candidates = new List<Candidate>();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            resources = World.GetOrCreateSystemManaged<ResourceSystem>();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            paths = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            buying = World.GetOrCreateSystemManaged<ResourceBuyerSystem>();
            searches = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<ResourceBuyer>(),
                    ComponentType.ReadOnly<PathInformation>(), ComponentType.ReadOnly<PathElement>(),
                    ComponentType.ReadOnly<CurrentBuilding>(), ComponentType.ReadOnly<TripNeeded>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TravelPurpose>(), ComponentType.ReadOnly<AttendingMeeting>(),
                    ComponentType.ReadOnly<CurrentTransport>(), ComponentType.ReadOnly<PathOwner>(),
                    ComponentType.ReadOnly<ShoppingRouteHold>() }
            });
            sellers = GetEntityQuery(ComponentType.ReadOnly<ResourceSeller>(), ComponentType.ReadOnly<ServiceAvailable>(),
                ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Game.Companies.StorageCompany>());
            residents = GetEntityQuery(ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<HumanData>(),
                ComponentType.ReadOnly<ResidentData>(), ComponentType.ReadOnly<CreatureData>(), ComponentType.ReadOnly<PrefabData>());
            holds = GetEntityQuery(ComponentType.ReadOnly<ShoppingRouteHold>());
            probes = GetEntityQuery(ComponentType.ReadOnly<ShoppingRouteProbe>());
            backups = GetEntityQuery(ComponentType.ReadOnly<ShoppingOriginalPathElement>());
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;

        protected override void OnUpdate()
        {
            try
            {
                EntityManager.CompleteAllTrackedJobs();
                RecoverOrphans();
                bool enabled = Mod.Settings != null && Mod.Settings.ShoppingEnabled && !faulted && buying.Enabled;
                bool apply = enabled && Mod.Settings!.ApplyShoppingChoices;
                foreach (var session in sessions.Values.ToArray())
                {
                    if (!enabled || apply != session.Apply || unchecked(simulation.frameIndex - session.Started) >= TimeoutFrames)
                    { Finish(session, true, "cancelled-or-timeout"); continue; }
                    if (session.Apply && !StillCurrent(session))
                    { Finish(session, true, "shopper-or-order-changed"); continue; }
                    if (session.Candidates.Any(c => IsPending(c.Probe))) continue;
                    Complete(session);
                }
                if (enabled) StartSearches(apply);
                LogStatus(enabled, apply);
            }
            catch (Exception error)
            {
                faulted = true;
                Restore();
                Mod.Log.Error(error, "Shopping routing stopped; held vanilla routes were restored.");
            }
        }

        private void StartSearches(bool apply)
        {
            foreach (var key in recent.Where(p => unchecked(simulation.frameIndex - p.Value) >= RetryFrames).Select(p => p.Key).ToArray())
                recent.Remove(key);
            int limit = apply ? 1 : ObserveConcurrency;
            if (sessions.Count >= limit) return;
            using var entities = searches.ToEntityArray(Allocator.Temp);
            int count = Math.Min(entities.Length, 128);
            for (int i = 0; i < count && sessions.Count < limit; i++)
            {
                Entity shopper = entities[(cursor + i) % entities.Length];
                if (recent.ContainsKey(shopper) || sessions.ContainsKey(shopper)) continue;
                var original = EntityManager.GetComponentData<PathInformation>(shopper);
                if (!Ready(original) || original.m_Methods != PathMethod.Pedestrian ||
                    EntityManager.GetBuffer<PathElement>(shopper, true).Length == 0) continue;
                var request = EntityManager.GetComponentData<ResourceBuyer>(shopper);
                Entity origin = EntityManager.GetComponentData<CurrentBuilding>(shopper).m_CurrentBuilding;
                if (!Eligible(shopper, request, origin, out double budget) ||
                    !TryProfile(shopper, request.m_Payer, out var profile, out var activities))
                { unsupported++; continue; }
                var session = new Session { Shopper = shopper, Origin = origin, Request = request, Original = original,
                    Started = simulation.frameIndex, Apply = apply, Budget = budget,
                    TimeValue = Mod.Settings!.ShoppingTimeValuePerHour };
                SelectCandidates(session);
                if (session.Candidates.Count < 2 || !session.Candidates.Any(c => c.Seller == original.m_Destination))
                { unsupported++; continue; }
                sessions.Add(shopper, session);
                recent[shopper] = simulation.frameIndex;
                if (apply) Hold(session);
                var queue = paths.GetQueue(this, 80, 16);
                foreach (var candidate in session.Candidates)
                {
                    candidate.Probe = EntityManager.CreateEntity(typeof(ShoppingRouteProbe), typeof(PathInformation), typeof(PathElement));
                    EntityManager.SetComponentData(candidate.Probe, new ShoppingRouteProbe { Shopper = shopper, Started = session.Started });
                    EntityManager.SetComponentData(candidate.Probe, new PathInformation { m_State = PathFlags.Pending });
                    var from = new SetupQueueTarget { m_Type = SetupTargetType.CurrentLocation,
                        m_Entity = origin, m_Methods = PathMethod.Pedestrian, m_ActivityMask = activities };
                    var to = new SetupQueueTarget { m_Type = SetupTargetType.CurrentLocation,
                        m_Entity = candidate.Seller, m_Methods = PathMethod.Pedestrian, m_ActivityMask = activities };
                    queue.Enqueue(new SetupQueueItem(candidate.Probe, profile, from, to));
                }
                started++;
            }
            cursor = entities.Length == 0 ? 0 : (cursor + count) % entities.Length;
        }

        private void Hold(Session session)
        {
            using var original = EntityManager.GetBuffer<PathElement>(session.Shopper, true).ToNativeArray(Allocator.Temp);
            var backup = EntityManager.AddBuffer<ShoppingOriginalPathElement>(session.Shopper);
            foreach (var element in original) backup.Add(new ShoppingOriginalPathElement { Element = element });
            EntityManager.AddComponentData(session.Shopper, new ShoppingRouteHold { Original = session.Original,
                Request = session.Request, Origin = session.Origin, Started = session.Started });
            var pending = session.Original; pending.m_State |= PathFlags.Pending;
            EntityManager.SetComponentData(session.Shopper, pending);
            held++;
        }

        private bool StillCurrent(Session session)
        {
            Entity shopper = session.Shopper;
            return EntityManager.HasComponent<ShoppingRouteHold>(shopper) &&
                EntityManager.HasComponent<ResourceBuyer>(shopper) && EntityManager.HasComponent<CurrentBuilding>(shopper) &&
                EntityManager.GetComponentData<CurrentBuilding>(shopper).m_CurrentBuilding == session.Origin &&
                SameRequest(EntityManager.GetComponentData<ResourceBuyer>(shopper), session.Request) &&
                EntityManager.HasComponent<PathInformation>(shopper) &&
                SamePath(EntityManager.GetComponentData<PathInformation>(shopper), session.Original) &&
                !EntityManager.HasComponent<Deleted>(shopper) && !EntityManager.HasComponent<TravelPurpose>(shopper) &&
                !EntityManager.HasComponent<AttendingMeeting>(shopper) && !EntityManager.HasComponent<CurrentTransport>(shopper) &&
                !EntityManager.HasComponent<PathOwner>(shopper) &&
                Eligible(shopper, session.Request, session.Origin, out var budget);
        }

        private bool IsPending(Entity probe) => EntityManager.HasComponent<PathInformation>(probe) &&
            (EntityManager.GetComponentData<PathInformation>(probe).m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0;

        private bool ReadRoute(Session session, Candidate candidate, out PathInformation route)
        {
            route = default;
            if (!EntityManager.HasComponent<PathInformation>(candidate.Probe) || !EntityManager.HasBuffer<PathElement>(candidate.Probe)) return false;
            route = EntityManager.GetComponentData<PathInformation>(candidate.Probe);
            return Ready(route) && route.m_Origin == session.Origin && route.m_Destination == candidate.Seller &&
                route.m_Methods == PathMethod.Pedestrian && EntityManager.GetBuffer<PathElement>(candidate.Probe, true).Length > 0;
        }

        private void Complete(Session session)
        {
            var offers = new List<ShopOffer>();
            bool incumbentRoute = false;
            double budget = session.Budget;
            if (session.Apply && !Eligible(session.Shopper, session.Request, session.Origin, out budget))
            { Finish(session, true, "basket-no-longer-affordable"); return; }
            foreach (var candidate in session.Candidates)
            {
                if (!ReadRoute(session, candidate, out var route)) { failedRoutes++; continue; }
                if (candidate.Seller == session.Original.m_Destination) incumbentRoute = true;
                if (session.Apply)
                {
                    if (TryOffer(candidate.Seller, session.Request, route.m_Duration, out var offer)) offers.Add(offer);
                }
                else
                    offers.Add(new ShopOffer(candidate.Offer.SellerId, candidate.Offer.Resource, candidate.Offer.Stock,
                        candidate.Offer.UnitPrice, route.m_Duration));
            }
            var search = new ShoppingSearch(Id(session.Shopper), (long)session.Request.m_ResourceNeeded,
                session.Request.m_AmountNeeded, budget, session.TimeValue, offers, Id(session.Original.m_Destination));
            var choice = ShoppingMarket.Choose(search);
            bool changed = choice.Selected && choice.SellerId != Id(session.Original.m_Destination);
            completed++;
            lastQuote = $"shopping routed observedFrame={simulation.frameIndex}, startFrame={session.Started}, shopper={Id(session.Shopper)}, origin={Id(session.Origin)}, incumbent={Id(session.Original.m_Destination)}, selected={choice.SellerId}, basket={session.Request.m_AmountNeeded}, candidates={session.Candidates.Count}, validRoutes={offers.Count}, basketPrice={choice.Evaluation.BasketPrice:F2}, timeCost={choice.Evaluation.TimeCost:F2}, changed={changed}, apply={session.Apply}; profile=actual-resident-pedestrian, routes=game-network, prices={(session.Apply ? "revalidated" : "capture-snapshot")}, duration-assumed-seconds, no-fare-for-walking, settlement=vanilla";
            if (session.Apply && changed && incumbentRoute && StillCurrent(session))
            {
                var winner = session.Candidates.First(c => Id(c.Seller) == choice.SellerId);
                if (ReadRoute(session, winner, out var route))
                {
                    using var path = EntityManager.GetBuffer<PathElement>(winner.Probe, true).ToNativeArray(Allocator.Temp);
                    var destination = EntityManager.GetBuffer<PathElement>(session.Shopper);
                    destination.Clear(); destination.AddRange(path);
                    EntityManager.SetComponentData(session.Shopper, route);
                    // Metadata and elements are from the SAME completed query. Retain
                    // the recovery receipt until both have been copied successfully.
                    EntityManager.RemoveComponent<ShoppingRouteHold>(session.Shopper);
                    EntityManager.RemoveComponent<ShoppingOriginalPathElement>(session.Shopper);
                    handedOff++;
                    Finish(session, false, "whole-route-handed-to-vanilla");
                    return;
                }
            }
            Finish(session, session.Apply, !incumbentRoute ? "incumbent-route-unavailable" : "incumbent-or-wait");
        }

        private void Finish(Session session, bool restore, string reason)
        {
            if (restore) RestoreShopper(session.Shopper);
            foreach (var candidate in session.Candidates)
                if (EntityManager.Exists(candidate.Probe)) EntityManager.DestroyEntity(candidate.Probe);
            sessions.Remove(session.Shopper);
            if (session.Apply) Mod.Log.Info($"shopping route release shopper={Id(session.Shopper)}, reason={reason}; sale-submitted-by-mod=false");
        }

        private void RestoreShopper(Entity shopper)
        {
            if (!EntityManager.HasComponent<ShoppingRouteHold>(shopper)) return;
            var hold = EntityManager.GetComponentData<ShoppingRouteHold>(shopper);
            // Never overwrite an unrelated newer path. Saved holds deserialize with
            // Obsolete replacing Pending, so identity deliberately excludes state bits.
            bool ours = EntityManager.HasComponent<PathInformation>(shopper) &&
                SamePath(EntityManager.GetComponentData<PathInformation>(shopper), hold.Original);
            if (ours)
            {
                bool unchanged = EntityManager.HasComponent<ResourceBuyer>(shopper) &&
                    SameRequest(EntityManager.GetComponentData<ResourceBuyer>(shopper), hold.Request) &&
                    EntityManager.HasComponent<CurrentBuilding>(shopper) &&
                    EntityManager.GetComponentData<CurrentBuilding>(shopper).m_CurrentBuilding == hold.Origin;
                if (unchanged && EntityManager.HasBuffer<ShoppingOriginalPathElement>(shopper))
                {
                    using var saved = EntityManager.GetBuffer<ShoppingOriginalPathElement>(shopper, true).ToNativeArray(Allocator.Temp);
                    var target = EntityManager.HasBuffer<PathElement>(shopper)
                        ? EntityManager.GetBuffer<PathElement>(shopper) : EntityManager.AddBuffer<PathElement>(shopper);
                    target.Clear(); foreach (var element in saved) target.Add(element.Element);
                    EntityManager.SetComponentData(shopper, hold.Original);
                }
                else
                {
                    // Changed order/origin or missing backup: let vanilla request a new
                    // route instead of leaving Pending or consuming an obsolete route.
                    EntityManager.RemoveComponent<PathInformation>(shopper);
                    if (EntityManager.HasBuffer<PathElement>(shopper)) EntityManager.RemoveComponent<PathElement>(shopper);
                }
                restored++;
            }
            EntityManager.RemoveComponent<ShoppingRouteHold>(shopper);
            if (EntityManager.HasBuffer<ShoppingOriginalPathElement>(shopper))
                EntityManager.RemoveComponent<ShoppingOriginalPathElement>(shopper);
            recent[shopper] = simulation.frameIndex;
        }

        private void RecoverOrphans()
        {
            using (var entities = holds.ToEntityArray(Allocator.Temp))
                foreach (var shopper in entities)
                    if (!sessions.TryGetValue(shopper, out var session) ||
                        session.Started != EntityManager.GetComponentData<ShoppingRouteHold>(shopper).Started) RestoreShopper(shopper);
            using (var entities = probes.ToEntityArray(Allocator.Temp))
                foreach (var probe in entities)
                {
                    var marker = EntityManager.GetComponentData<ShoppingRouteProbe>(probe);
                    if (!sessions.TryGetValue(marker.Shopper, out var session) || session.Started != marker.Started)
                        EntityManager.DestroyEntity(probe);
                }
            using (var entities = backups.ToEntityArray(Allocator.Temp))
                foreach (var shopper in entities)
                    if (!EntityManager.HasComponent<ShoppingRouteHold>(shopper)) EntityManager.RemoveComponent<ShoppingOriginalPathElement>(shopper);
        }

        // Used on error, before deserialization and from Mod.OnDispose. PathfindJobs
        // uses TryGet on results; late results for destroyed proxies are discarded.
        // The game's queue retains ownership of its own native action memory.
        public void Restore()
        {
            EntityManager.CompleteAllTrackedJobs();
            using (var entities = holds.ToEntityArray(Allocator.Temp)) foreach (var shopper in entities) RestoreShopper(shopper);
            using (var entities = probes.ToEntityArray(Allocator.Temp)) foreach (var probe in entities) EntityManager.DestroyEntity(probe);
            using (var entities = backups.ToEntityArray(Allocator.Temp)) foreach (var shopper in entities) EntityManager.RemoveComponent<ShoppingOriginalPathElement>(shopper);
            sessions.Clear(); recent.Clear();
        }
        public void PreDeserialize(Context context) { Restore(); }
        protected override void OnStopRunning() { Restore(); base.OnStopRunning(); }
        protected override void OnDestroy() { Restore(); base.OnDestroy(); }

        private void LogStatus(bool enabled, bool apply)
        {
            if (logged && unchecked(simulation.frameIndex - lastLogFrame) < 4096) return;
            if (lastQuote != null) Mod.Log.Info(lastQuote);
            Mod.Log.Info($"shopping status frame={simulation.frameIndex}, enabled={enabled}, apply={apply}, faulted={faulted}, active={sessions.Count}, started={started}, completed={completed}, unsupported={unsupported}, failedRoutes={failedRoutes}, held={held}, handedOff={handedOff}, restored={restored}, savedHolds={holds.CalculateEntityCount()}, probes={probes.CalculateEntityCount()}; candidateLimit=8, sampleLimit=512, supported=pedestrian-full-basket, transactions=vanilla");
            logged = true; lastLogFrame = simulation.frameIndex;
        }

        private static bool Ready(PathInformation path) =>
            (path.m_State & (PathFlags.Pending | PathFlags.Scheduled | PathFlags.Failed | PathFlags.Obsolete)) == 0 &&
            path.m_Origin != Entity.Null && path.m_Destination != Entity.Null &&
            ShoppingPriceQuote.NonNegative(path.m_Duration) && ShoppingPriceQuote.NonNegative(path.m_Distance);
        private static bool SameRequest(ResourceBuyer a, ResourceBuyer b) => a.m_Payer == b.m_Payer &&
            a.m_ResourceNeeded == b.m_ResourceNeeded && a.m_AmountNeeded == b.m_AmountNeeded && a.m_Flags == b.m_Flags;
        private static bool SamePath(PathInformation a, PathInformation b) => a.m_Origin == b.m_Origin &&
            a.m_Destination == b.m_Destination && a.m_Distance == b.m_Distance && a.m_Duration == b.m_Duration &&
            a.m_TotalCost == b.m_TotalCost && a.m_Methods == b.m_Methods;
        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
    }
}
