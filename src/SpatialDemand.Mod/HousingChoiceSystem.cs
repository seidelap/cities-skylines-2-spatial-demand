using System;
using System.Collections.Generic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
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
    // Owns only the decision between completed residential path candidates. Vanilla owns
    // discovery, spawning, shelters, moving-away policy, tenancy settlement, rent and upkeep.
    public partial class HousingChoiceSystem : GameSystemBase
    {
        private EntityQuery searches, economy, pending;
        private PropertyProcessingSystem processing = null!;
        private TaxSystem tax = null!;
        private SimulationSystem simulation = null!;
        private bool faulted;
        private uint lastLogFrame;
        private bool? lastLoggedApplyMode;
        private long evaluated, proposedMoves, queuedMoves, settledMoves, retriedMoves, delegated;

        protected override void OnCreate()
        {
            base.OnCreate();
            processing = World.GetOrCreateSystemManaged<PropertyProcessingSystem>();
            tax = World.GetOrCreateSystemManaged<TaxSystem>();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            searches = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CimHousehold>(), ComponentType.ReadOnly<HouseholdCitizen>(),
                    ComponentType.ReadWrite<PropertySeeker>(), ComponentType.ReadWrite<PathInformations>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TouristHousehold>(), ComponentType.ReadOnly<CommuterHousehold>(),
                    ComponentType.ReadOnly<MovingAway>() }
            });
            economy = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            pending = GetEntityQuery(ComponentType.ReadOnly<PendingHome>());
            RequireForUpdate(economy);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;

        protected override void OnUpdate()
        {
            if (Mod.Settings == null) return;
            try
            {
                // Finish/recover already submitted actions even if the user turns evaluation off.
                RecoverSubmissions();
                if (Mod.Settings.Enabled && !faulted && processing.Enabled && !searches.IsEmptyIgnoreFilter)
                    EvaluateSearches(Mod.Settings.ApplyChoices);
            }
            catch (Exception error)
            {
                faulted = true;
                Mod.Log.Error(error, "Housing choice stopped for this session; vanilla remains enabled.");
            }
        }

        private void RecoverSubmissions()
        {
            if (pending.IsEmptyIgnoreFilter || !processing.Enabled) return;
            EntityManager.CompleteAllTrackedJobs();
            using var entities = pending.ToEntityArray(Allocator.Temp);
            foreach (var household in entities)
            {
                var receipt = EntityManager.GetComponentData<PendingHome>(household);
                if (receipt.Frame == simulation.frameIndex) continue;
                bool settled = EntityManager.HasComponent<PropertyRenter>(household) &&
                    EntityManager.GetComponentData<PropertyRenter>(household).m_Property == receipt.Property;
                if (settled) settledMoves++;
                else if (!EntityManager.HasComponent<Deleted>(household) &&
                    !EntityManager.HasComponent<MovingAway>(household) && EntityManager.HasComponent<PropertySeeker>(household))
                {
                    EntityManager.SetComponentEnabled<PropertySeeker>(household, true);
                    retriedMoves++;
                }
                EntityManager.RemoveComponent<PendingHome>(household);
            }
        }

        private void EvaluateSearches(bool apply)
        {
            // First version uses a main-thread batch over already completed searches. Complete
            // game jobs before reading their buffers or writing the shared tenancy queue.
            EntityManager.CompleteAllTrackedJobs();
            var queue = processing.GetRentActionQueue(out var writers);
            writers.Complete();
            var parameters = economy.GetSingleton<EconomyParameterData>();
            var workers = GetComponentLookup<Worker>(true);
            var citizens = GetComponentLookup<Citizen>(true);
            var health = GetComponentLookup<HealthProblem>(true);
            var rates = tax.GetTaxRates();
            var batch = new List<HomeSearch>();
            var households = new Dictionary<long, Entity>();
            var properties = new Dictionary<long, Entity>();
            var seeds = new Dictionary<long, uint>();

            using (var entities = searches.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var paths = EntityManager.GetBuffer<PathInformations>(entity, true);
                    if (paths.Length == 0) continue;
                    bool pending = false;
                    foreach (var path in paths)
                        pending |= (path.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0;
                    if (pending) continue;

                    var members = EntityManager.GetBuffer<HouseholdCitizen>(entity, true);
                    if (members.Length == 0) { delegated++; continue; }
                    uint seed = 0;
                    if (EntityManager.HasComponent<SavedPreferences>(entity))
                        seed = EntityManager.GetComponentData<SavedPreferences>(entity).Seed;
                    else
                    {
                        // Combine saved citizen seeds without relying on buffer order. Persist
                        // once on the household so later births/deaths do not change its tastes.
                        foreach (var member in members)
                            if (citizens.HasComponent(member.m_Citizen))
                                seed = unchecked(seed + StableHash.Mix(citizens[member.m_Citizen].m_PseudoRandom));
                    }
                    int income = EconomyUtils.GetHouseholdIncome(members, ref workers, ref citizens,
                        ref health, ref parameters, rates);
                    var household = new Core.Household(Id(entity), members.Length, Math.Max(0, income), Preferences.FromSeed(seed));
                    Entity current = Entity.Null;
                    if (EntityManager.HasComponent<PropertyRenter>(entity))
                        current = EntityManager.GetComponentData<PropertyRenter>(entity).m_Property;
                    var seeker = EntityManager.GetComponentData<PropertySeeker>(entity);
                    var offers = new List<HomeOffer>();
                    foreach (var path in paths)
                    {
                        if ((path.m_State & (PathFlags.Failed | PathFlags.Obsolete)) != 0) continue;
                        var property = seeker.m_TargetProperty == Entity.Null ? path.m_Destination : path.m_Origin;
                        if (TryOffer(entity, property, current, path.m_Duration, out var offer))
                        { offers.Add(offer); properties[offer.Id] = property; }
                    }
                    // Without a comparable route to the current home, the outside option is
                    // unknown. Do not silently treat it as a zero-minute commute or evict anyone.
                    bool hasCurrent = current == Entity.Null || offers.Exists(o => o.Id == Id(current));
                    if (offers.Count == 0 || !hasCurrent) { delegated++; continue; }
                    if (current != Entity.Null)
                    {
                        var home = offers.Find(o => o.Id == Id(current));
                        var occupiedHome = new HomeOffer(home.Id, home.Rent, home.Space, home.TravelSeconds, 1);
                        // Leave an existing insolvency situation to the game's established lifecycle.
                        if (!Housing.Evaluate(household, occupiedHome).Feasible) { delegated++; continue; }
                    }
                    batch.Add(new HomeSearch(household, offers, current == Entity.Null ? (long?)null : Id(current)));
                    households[household.Id] = entity;
                    seeds[household.Id] = seed;
                }
            }

            var choices = HousingMarket.Clear(batch, simulation.frameIndex / 16);
            evaluated += choices.Count;
            // No structural changes occurred while borrowed buffers/lookups above were in use.
            foreach (var choice in choices)
            {
                if (choice.Moved) proposedMoves++;
                if (!apply) continue;
                var household = households[choice.HouseholdId];
                if (!EntityManager.HasComponent<SavedPreferences>(household))
                    EntityManager.AddComponentData(household, new SavedPreferences { Seed = seeds[choice.HouseholdId] });
                // Reject this shortlist without letting another scorer choose from it. Vanilla
                // behavior may enable a later search; no household is forced out by this mod.
                var seeker = EntityManager.GetComponentData<PropertySeeker>(household);
                seeker.m_BestProperty = Entity.Null;
                seeker.m_BestPropertyScore = float.NegativeInfinity;
                seeker.m_LastPropertySeekFrame = simulation.frameIndex;
                EntityManager.SetComponentData(household, seeker);
                if (choice.Moved)
                {
                    var property = properties[choice.HomeId!.Value];
                    EntityManager.AddComponentData(household, new PendingHome { Property = property, Frame = simulation.frameIndex });
                    queue.Enqueue(new RentAction { m_Property = property, m_Renter = household });
                    queuedMoves++;
                }
                EntityManager.SetComponentEnabled<PropertySeeker>(household, false);
                EntityManager.RemoveComponent<PathInformations>(household);
            }
            if (choices.Count > 0 && (lastLoggedApplyMode != apply || unchecked(simulation.frameIndex - lastLogFrame) >= 4096))
            {
                Mod.Log.Info($"housing evaluated={evaluated}, proposed={proposedMoves}, queued={queuedMoves}, settled={settledMoves}, retried={retriedMoves}, delegated={delegated}, apply={apply}");
                var sample = choices[0];
                Mod.Log.Info($"housing sample household={sample.HouseholdId}, home={sample.HomeId}, utility={sample.Utility:F3}, " +
                    $"space={sample.Evaluation.SpaceBenefit:F3}, rent={sample.Evaluation.RentCost:F3}, travel={sample.Evaluation.TravelCost:F3}, moved={sample.Moved}");
                lastLogFrame = simulation.frameIndex;
                lastLoggedApplyMode = apply;
            }
        }

        private bool TryOffer(Entity household, Entity property, Entity current, float duration, out HomeOffer offer)
        {
            offer = default;
            if (!EntityManager.Exists(property) || !EntityManager.HasComponent<ResidentialProperty>(property) ||
                EntityManager.HasComponent<Deleted>(property) || EntityManager.HasComponent<Temp>(property) ||
                EntityManager.HasComponent<Abandoned>(property) || EntityManager.HasComponent<Condemned>(property) ||
                EntityManager.HasComponent<Destroyed>(property) ||
                EntityManager.HasComponent<Game.Objects.UnderConstruction>(property) ||
                !EntityManager.HasComponent<PrefabRef>(property) || !EntityManager.HasBuffer<Renter>(property)) return false;
            bool incumbent = property == current;
            if (!incumbent && !EntityManager.HasComponent<PropertyOnMarket>(property)) return false;
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(property).m_Prefab;
            if (!EntityManager.HasComponent<BuildingPropertyData>(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) return false;
            var data = EntityManager.GetComponentData<BuildingPropertyData>(prefab);
            if (data.m_ResidentialProperties <= 0) return false;
            int occupied = 0;
            foreach (var renter in EntityManager.GetBuffer<Renter>(property, true))
                if (EntityManager.HasComponent<CimHousehold>(renter.m_Renter)) occupied++;
            var building = EntityManager.GetComponentData<BuildingData>(prefab);
            double space = data.m_SpaceMultiplier * (double)building.m_LotSize.x * building.m_LotSize.y / data.m_ResidentialProperties;
            int rent = incumbent ? EntityManager.GetComponentData<PropertyRenter>(household).m_Rent
                : EntityManager.GetComponentData<PropertyOnMarket>(property).m_AskingRent;
            offer = new HomeOffer(Id(property), rent, space, duration, Math.Max(0, data.m_ResidentialProperties - occupied));
            return true;
        }

        // Entity version is part of the transient key; recycled indices cannot steal a quote.
        private static long Id(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
    }
}
