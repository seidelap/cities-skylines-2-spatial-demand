using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Buildings;
using Game.Citizens;
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
    // A quote reader, called by the construction gate; it never moves a household.
    public partial class ConstructionHousingSystem : GameSystemBase
    {
        private EntityQuery households, vacancies, economy;
        private TaxSystem tax = null!;
        private HousingChoiceSystem housing = null!;
        private int cursor;

        internal sealed class Prospect
        {
            internal Entity Entity;
            internal uint Seed;
            internal Core.Household Household;
            internal readonly List<float3> Workplaces = new List<float3>();
            internal double Outside;
            internal float Travel(float3 position)
            {
                // Same explicit approximation for the proposed and incumbent homes.
                // No employed member means no work commute; schools/services are not modeled.
                double speed = Mod.Settings!.ConstructionTravelKph / 3.6;
                return (float)(Workplaces.Count == 0 ? 0 : Workplaces.Max(p => math.distance(p.xz, position.xz)) / speed);
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            tax = World.GetOrCreateSystemManaged<TaxSystem>();
            housing = World.GetOrCreateSystemManaged<HousingChoiceSystem>();
            households = GetEntityQuery(ComponentType.ReadOnly<Game.Citizens.Household>(), ComponentType.ReadOnly<HouseholdCitizen>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<TouristHousehold>(),
                ComponentType.Exclude<CommuterHousehold>(), ComponentType.Exclude<MovingAway>());
            vacancies = GetEntityQuery(ComponentType.ReadOnly<ResidentialProperty>(), ComponentType.ReadOnly<PropertyOnMarket>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            economy = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
        }
        protected override void OnUpdate() { }

        internal List<Prospect> Read()
        {
            var result = new List<Prospect>();
            if (economy.IsEmptyIgnoreFilter) return result;
            var parameters = economy.GetSingleton<EconomyParameterData>();
            var workers = GetComponentLookup<Worker>(true);
            var citizens = GetComponentLookup<Citizen>(true);
            var health = GetComponentLookup<HealthProblem>(true);
            var rates = tax.GetTaxRates();
            using var people = households.ToEntityArray(Allocator.Temp);
            using var empty = vacancies.ToEntityArray(Allocator.Temp);
            // Sample rotation prevents one fixed group from supplying every future bid.
            int count = Math.Min(512, people.Length);
            for (int i = 0; i < count; i++)
            {
                var entity = people[(cursor + i) % people.Length];
                var members = EntityManager.GetBuffer<HouseholdCitizen>(entity, true);
                if (members.Length == 0 || EntityManager.HasComponent<PendingHome>(entity)) continue;
                var prospect = new Prospect { Entity = entity };
                bool known = true;
                foreach (var member in members)
                {
                    if (citizens.HasComponent(member.m_Citizen))
                        prospect.Seed = unchecked(prospect.Seed + StableHash.Mix(citizens[member.m_Citizen].m_PseudoRandom));
                    if (!workers.HasComponent(member.m_Citizen)) continue;
                    var workplace = workers[member.m_Citizen].m_Workplace;
                    if (EntityManager.HasComponent<PropertyRenter>(workplace))
                        workplace = EntityManager.GetComponentData<PropertyRenter>(workplace).m_Property;
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(workplace)) { known = false; break; }
                    prospect.Workplaces.Add(EntityManager.GetComponentData<Game.Objects.Transform>(workplace).m_Position);
                }
                if (!known) continue; // Unlocated jobs do not become zero-minute commutes.
                if (EntityManager.HasComponent<SavedPreferences>(entity)) prospect.Seed = EntityManager.GetComponentData<SavedPreferences>(entity).Seed;
                int income = EconomyUtils.GetHouseholdIncome(members, ref workers, ref citizens, ref health, ref parameters, rates);
                prospect.Household = new Core.Household(Id(entity), members.Length, Math.Max(0, income), Preferences.FromSeed(prospect.Seed));
                Entity current = EntityManager.HasComponent<PropertyRenter>(entity)
                    ? EntityManager.GetComponentData<PropertyRenter>(entity).m_Property : Entity.Null;
                if (current != Entity.Null)
                {
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(current) || !housing.TryOffer(entity, current, current,
                        prospect.Travel(EntityManager.GetComponentData<Game.Objects.Transform>(current).m_Position), out var home)) continue;
                    var value = Housing.Evaluate(prospect.Household, new HomeOffer(home.Id, home.Rent, home.Space, home.TravelSeconds, 1));
                    if (!value.Feasible) continue;
                    prospect.Outside = Math.Max(0, value.Utility);
                }
                // Compare with observable vacancies too, so a new building must improve on
                // renting an existing home. Capped snapshot, not a claim of city-wide optimality.
                foreach (var building in empty.Take(32))
                {
                    if (building == current) continue;
                    var position = EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position;
                    if (housing.TryOffer(entity, building, Entity.Null, prospect.Travel(position), out var offer))
                        prospect.Outside = Math.Max(prospect.Outside, Housing.Evaluate(prospect.Household, offer).Utility - 0.05);
                }
                result.Add(prospect);
            }
            cursor = people.Length == 0 ? 0 : (cursor + count) % people.Length;
            return result;
        }

        internal List<TenantBid> Bids(List<Prospect> prospects, Entity prefab, float3 position, long site)
        {
            var property = EntityManager.GetComponentData<BuildingPropertyData>(prefab);
            var building = EntityManager.GetComponentData<BuildingData>(prefab);
            double space = property.m_SpaceMultiplier * (double)building.m_LotSize.x * building.m_LotSize.y / property.m_ResidentialProperties;
            return prospects.Select(p => new TenantBid { Tenant = p.Household.Id,
                Rent = DevelopmentMarket.HousingBid(p.Household, new HomeOffer(site, 0, space, p.Travel(position), 1), p.Outside) }).ToList();
        }
        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
    }
}
