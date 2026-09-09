using System;
using System.Collections.Generic;
using System.Linq;
using Game.Agents;
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
using Unity.Mathematics;
using CimHousehold = Game.Citizens.Household;

namespace SpatialDemand.Mod
{
    public partial class ShoppingChoiceSystem
    {
        private bool Eligible(Entity shopper, ResourceBuyer request, Entity origin, out double budget)
        {
            budget = 0;
            if (request.m_AmountNeeded <= 0 || request.m_ResourceNeeded == Resource.NoResource ||
                (request.m_Flags & SetupTargetFlags.Commercial) == 0 ||
                (request.m_Flags & (SetupTargetFlags.RequireTransport | SetupTargetFlags.BuildingUpkeep | SetupTargetFlags.SecondaryPath)) != 0 ||
                !EntityManager.HasComponent<CimHousehold>(request.m_Payer) ||
                !EntityManager.HasBuffer<HouseholdCitizen>(request.m_Payer) ||
                EntityManager.HasComponent<TouristHousehold>(request.m_Payer) ||
                EntityManager.HasComponent<CommuterHousehold>(request.m_Payer) || EntityManager.HasComponent<MovingAway>(request.m_Payer) ||
                !EntityManager.HasBuffer<Resources>(request.m_Payer) || !EntityManager.Exists(origin) ||
                !EntityManager.HasComponent<Game.Objects.Transform>(origin) ||
                !EntityManager.HasBuffer<TripNeeded>(shopper) || EntityManager.GetBuffer<TripNeeded>(shopper, true).Length != 0 ||
                EntityManager.HasComponent<Game.Citizens.Student>(shopper) ||
                (EntityManager.HasComponent<CarKeeper>(shopper) && EntityManager.IsComponentEnabled<CarKeeper>(shopper)) ||
                (EntityManager.HasComponent<BicycleOwner>(shopper) && EntityManager.IsComponentEnabled<BicycleOwner>(shopper))) return false;
            int money = EconomyUtils.GetResources(Resource.Money, EntityManager.GetBuffer<Resources>(request.m_Payer, true));
            budget = Math.Max(0d, (double)money - HouseholdBehaviorSystem.kMinimumShoppingMoney);
            var prefabs = resources.GetPrefabs();
            var data = GetComponentLookup<ResourceData>(true);
            if (!data.HasComponent(prefabs[request.m_ResourceNeeded]) ||
                EconomyUtils.GetWeight(request.m_ResourceNeeded, prefabs, ref data) <= 0) return false;
            double market = EconomyUtils.GetMarketPrice(request.m_ResourceNeeded, prefabs, ref data);
            // Vanilla reserves quantity using 1.4 times base market price. We support
            // only whole baskets that also pass this cap, not a competing partial order.
            return ShoppingPriceQuote.NonNegative(market) && market > 0 &&
                request.m_AmountNeeded <= Math.Floor(budget / (market * 1.4));
        }

        private bool TryProfile(Entity shopper, Entity household, out PathfindParameters profile, out ActivityMask activities)
        {
            profile = default; activities = default;
            var citizen = EntityManager.GetComponentData<Citizen>(shopper);
            using var chunks = residents.ToArchetypeChunkListAsync(Allocator.TempJob, out var dependency);
            dependency.Complete();
            var creatures = GetComponentTypeHandle<CreatureData>(true);
            var residentData = GetComponentTypeHandle<ResidentData>(true);
            Entity prefab = Game.Objects.ObjectEmergeSystem.SelectResidentPrefab(citizen, chunks, GetEntityTypeHandle(),
                ref creatures, ref residentData, out var creature, out var seed);
            if (!EntityManager.HasComponent<HumanData>(prefab)) return false;
            float walk = EntityManager.GetComponentData<HumanData>(prefab).m_WalkSpeed;
            int members = EntityManager.GetBuffer<HouseholdCitizen>(household, true).Length;
            if (members <= 0 || walk <= 0 || !math.isfinite(walk)) return false;
            var weights = CitizenUtils.GetPathfindWeights(citizen, EntityManager.GetComponentData<CimHousehold>(household), members);
            if (!math.all(math.isfinite(weights.m_Value)) || math.any(weights.m_Value < 0)) return false;
            profile = new PathfindParameters { m_MaxSpeed = 277.77777f, m_WalkSpeed = walk, m_Weights = weights,
                m_Methods = PathMethod.Pedestrian, m_MaxCost = CitizenBehaviorSystem.kMaxPathfindCost,
                m_MaxResultCount = 1, m_PathfindFlags = PathfindFlags.Stable,
                m_Authorization1 = EntityManager.HasComponent<PropertyRenter>(household) ? Property(household) : Entity.Null };
            if (EntityManager.HasComponent<Worker>(shopper))
                profile.m_Authorization2 = Property(EntityManager.GetComponentData<Worker>(shopper).m_Workplace);
            activities = creature.m_SupportedActivities;
            return true;
        }

        private Entity Property(Entity entity) => EntityManager.HasComponent<PropertyRenter>(entity)
            ? EntityManager.GetComponentData<PropertyRenter>(entity).m_Property : entity;

        private void SelectCandidates(Session session)
        {
            var offers = new List<Candidate>();
            using var entities = sellers.ToEntityArray(Allocator.Temp);
            float3 origin = EntityManager.GetComponentData<Game.Objects.Transform>(session.Origin).m_Position;
            foreach (var seller in entities.OrderBy(Id).Take(512))
            {
                if (!TryOffer(seller, session.Request, 0, out var offer)) continue;
                float3 position = EntityManager.GetComponentData<Game.Objects.Transform>(Property(seller)).m_Position;
                double distance = (double)math.lengthsq(position - origin);
                if (!ShoppingPriceQuote.NonNegative(distance)) continue;
                offers.Add(new Candidate { Seller = seller, Offer = offer, DistanceSquared = distance });
            }
            // The incumbent is reprobed even when outside the capped city sample.
            if (TryOffer(session.Original.m_Destination, session.Request, 0, out var incumbent))
                session.Candidates.Add(new Candidate { Seller = session.Original.m_Destination, Offer = incumbent });
            // Geometry only bounds discovery. All shortlisted shops get a real route;
            // the eventual preference compares checkout price and routed duration.
            var ranked = offers.OrderBy(c => c.Offer.UnitPrice).ThenBy(c => Id(c.Seller)).Take(3)
                .Concat(offers.OrderBy(c => c.DistanceSquared).ThenBy(c => Id(c.Seller)));
            foreach (var candidate in ranked)
            {
                if (session.Candidates.Any(c => c.Seller == candidate.Seller)) continue;
                session.Candidates.Add(candidate);
                if (session.Candidates.Count == CandidateLimit) break;
            }
        }

        private bool TryOffer(Entity seller, ResourceBuyer request, double seconds, out ShopOffer offer)
        {
            offer = default;
            if (!EntityManager.HasComponent<ResourceSeller>(seller) || !EntityManager.HasComponent<ServiceAvailable>(seller) ||
                !EntityManager.HasComponent<PropertyRenter>(seller) || !EntityManager.HasComponent<PrefabRef>(seller) ||
                !EntityManager.HasBuffer<Resources>(seller) || EntityManager.HasComponent<Deleted>(seller) ||
                EntityManager.HasComponent<Temp>(seller) || EntityManager.HasComponent<Game.Companies.StorageCompany>(seller)) return false;
            Entity property = Property(seller);
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(seller).m_Prefab;
            if (!EntityManager.HasComponent<IndustrialProcessData>(prefab) ||
                EntityManager.GetComponentData<IndustrialProcessData>(prefab).m_Output.m_Resource != request.m_ResourceNeeded ||
                !EntityManager.HasComponent<Game.Objects.Transform>(property) || EntityManager.HasComponent<Deleted>(property) ||
                EntityManager.HasComponent<Abandoned>(property) || EntityManager.HasComponent<Condemned>(property) ||
                EntityManager.HasComponent<Destroyed>(property) || EntityManager.HasComponent<Game.Objects.UnderConstruction>(property)) return false;
            if (EntityManager.HasComponent<Building>(property) && BuildingUtils.CheckOption(
                EntityManager.GetComponentData<Building>(property), BuildingOption.Inactive)) return false;
            var data = GetComponentLookup<ResourceData>(true);
            if (!ShoppingPriceQuote.TryGet(EntityManager, resources.GetPrefabs(), ref data, seller, request.m_ResourceNeeded, true, out var price)) return false;
            int stock = EconomyUtils.GetResources(request.m_ResourceNeeded, EntityManager.GetBuffer<Resources>(seller, true));
            if (stock < request.m_AmountNeeded) return false;
            offer = new ShopOffer(Id(seller), (long)request.m_ResourceNeeded, stock, price.UnitPrice, seconds);
            return true;
        }
    }
}
