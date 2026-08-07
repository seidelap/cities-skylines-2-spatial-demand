// WorldState → ECS writer (stage 7) — the game-facing other half of the
// adapter seam (reader: EconReader.cs). Results land in VANILLA components
// wherever vanilla systems also read them (notes §7 architecture: a save
// opened without the mod stays coherent; feature-flags-off degrades cleanly):
//
//   assessments  → Game.Buildings.PropertyRenter.m_Rent (§3) on each renter
//                  entity: households pay their engine-computed per-unit
//                  ChargedAssessment (φ·land + τ_S·structure, design §4.3);
//                  firms pay the parcel's per-unit assessment × job slots.
//                  m_MaxRent is left untouched (semantics not dump-verified).
//   land value   → the land component of assessments mirrored into
//                  Game.Net.LandValue.m_LandValue (§3: lives on NET edges) on
//                  every edge, cluster-averaged and capitalized at the hurdle
//                  rate — vanilla readers (ZoneSpawnSystem, tooltips) keep a
//                  coherent field while LandValueSystem is disabled (the
//                  LandValueOverhaul precedent, notes §6; write semantics are
//                  bring-up checklist item 6). m_Weight untouched.
//   condition    → Game.Buildings.BuildingCondition.m_Condition (§3), the
//                  inverse of EconReader.Cond01's money-like mapping.
//   level change → prefab swap (the discrete 1–5 level lives on the PREFAB:
//                  SpawnableBuildingData.m_Level, §3), fully isolated in
//                  Verify_SwapLevelPrefab — §9 item 7 decides whether renters
//                  survive an external swap; the evict-rehouse fallback is
//                  documented there.
//   construction → engine-selected starts recorded as ConstructionRequests —
//                  the ZoneSpawnSystem seam (§4: ZoneSpawnSystem is the
//                  spawner the TierB row disables; BuildingConstructionSystem
//                  already models the lag via Game.Objects.UnderConstruction
//                  and is KEPT, fed by this queue at bring-up).
//
// Tier D price injection (where companies read buy/sell prices) is bring-up
// checklist item 8 (ResourceSystem prefab table vs Game.Economy.ResourceInfo)
// and deliberately NOT guessed here. Mod-native state (escrow, trade EMAs,
// migration scalars, calibration) rides EconSerialization's ISerializable
// components at the save seam, not this per-tick writer.
//
// Entity correlation: EconReader's XEntities[k] pairs with engine id XIds[k]
// (ids append-only, never reused) — NOT with WorldState list position k, since
// the engine appends its own arrivals/entrants to those lists. The writer
// walks the reader's entity slots and reaches core objects through the id
// lists. Each write section is gated on its own feature flag,
// and the whole Apply is skipped by EconBridgeSystem in shadow mode (belt and
// braces: it also self-checks), so observe-only can never touch a component.

using System;
using System.Collections.Generic;
using CS2Econ.Core;
#if !OUT_OF_GAME_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
#endif

namespace CS2Econ.Mod
{
    /// <summary>One engine-selected construction start, queued for the
    /// ZoneSpawnSystem seam. ParcelId indexes both WorldState.Parcels and
    /// EconReader.ParcelEntities (the zoned-empty Game.Zones.Block entity to
    /// build on). Consumed at bring-up by the spawn injector that pushes the
    /// start through vanilla's BuildingConstructionSystem path (§4: "keep it,
    /// feed it from the new site-selection logic").</summary>
    public readonly struct ConstructionRequest
    {
        public readonly int ParcelId;
        public readonly int Cluster;
        public readonly ZoneKind Use;
        public readonly int Level;
        public readonly int Units;
        public readonly long Tick;

        public ConstructionRequest(int parcelId, int cluster, ZoneKind use, int level, int units, long tick)
        { ParcelId = parcelId; Cluster = cluster; Use = use; Level = level; Units = units; Tick = tick; }
    }

    /// <summary>Pure assessment mirror math (compiled in both builds so the
    /// harness can pin it): the per-unit flow a parcel's occupants are charged
    /// — the captured land flow φ·AssessedLR split over units plus the
    /// structure-tax leg τ_S on per-unit replacement cost (design §4.3). The
    /// ENGINE is the source of truth for household charges (ChargedAssessment,
    /// re-rated to the parcel's live market assessment each tick); this
    /// mirror prices firm parcels and
    /// sanity checks.</summary>
    public static class EconAssess
    {
        public static double PerUnitFlow(Parcel pl, EconParams p)
        {
            int units = Math.Max(1, pl.Units);
            double land = p.CaptureFraction * pl.AssessedLR / units;
            double structure = p.StructureTaxRate * p.RC(pl.Level, 1);
            return Math.Max(0, land + structure);
        }
    }

#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked (same
    /// discipline as EconReader). The harness writes nothing — its world IS
    /// the WorldState.</summary>
    public sealed class EconWriter
    {
        public readonly List<ConstructionRequest> PendingConstruction = new List<ConstructionRequest>();
        public int RentWrites, ConditionWrites, LandValueEdgeWrites, LevelSwapsApplied;

        public void Apply(WorldState w, EconomyEngine engine) =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
    }
#else
    public sealed class EconWriter
    {
        // ---- calibration constants (numeric, not name guesses) ---------------
        /// <summary>Engine assessment flow → PropertyRenter.m_Rent money units.
        /// 1.0 by construction: the reader seeds ChargedAssessment FROM m_Rent
        /// in shadow mode, so the units already agree; re-calibrate only if the
        /// stage-3 comparison shows a systematic offset.</summary>
        private const double RentScale = 1.0;
        /// <summary>Rent-write deadband: a change must move at least one money
        /// unit AND this share of the standing rent to be written through to
        /// the ECS. The co-op re-rate makes ChargedAssessment drift on EVERY
        /// household every tick (the old anniversary regime moved 1/30 of
        /// them); without a band that is an unbounded per-tick write storm on
        /// PropertyRenter. Sub-threshold drift is not lost — the engine holds
        /// the true value and it lands on the first write that clears.</summary>
        private const double RentDeadbandShare = 0.01;
        /// <summary>Inverse of EconReader.Cond01: core condition 0..1 →
        /// money-like m_Condition int (0.5 ↦ 0). KEEP IN LOCKSTEP with
        /// EconReader.ConditionMoneyRange; both are one calibration item
        /// (bring-up checklist item 2 reads the real range from the
        /// BuildingUpkeepSystem decompile).</summary>
        private const double ConditionMoneyRange = 200_000.0;
        /// <summary>Cap on prefab swaps per Apply: renovations trickle (the
        /// engine's renovation clock is staggered anyway — design §3, no
        /// citywide synchronized event), and a bad Verify_ guess surfaces on a
        /// handful of buildings, not the whole city at once.</summary>
        private const int MaxLevelSwapsPerApply = 8;
        private const int MaxPendingConstruction = 4096;

        private readonly EconReader _reader;
        private readonly ClusterAccessProvider _access;

        // Per-Apply write telemetry (surfaced by EconBridgeSystem.StatusLine).
        public int RentWrites, ConditionWrites, LandValueEdgeWrites, LevelSwapsApplied;

        /// <summary>Engine-selected construction starts awaiting the spawn
        /// injector (see ConstructionRequest doc). Append-only within a
        /// session, oldest trimmed past MaxPendingConstruction.</summary>
        public readonly List<ConstructionRequest> PendingConstruction = new List<ConstructionRequest>();
        private readonly HashSet<int> _recordedStarts = new HashSet<int>();

        private EntityQuery _landValueEdgeQuery;
        private EntityQuery _spawnablePrefabQuery;
        private bool _queriesCreated;
        private Dictionary<(Entity zone, int lotX, int lotY, byte level), List<Entity>>? _prefabIndex;

        public EconWriter(EconReader reader, ClusterAccessProvider access)
        { _reader = reader; _access = access; }

        /// <summary>One writer pass after engine.Step(). Sections gate on their
        /// tier's flag so a reverted tier immediately stops writing its seam
        /// (vanilla re-enabled by the bridge takes back over).</summary>
        public void Apply(EntityManager em, WorldState w, EconomyEngine engine)
        {
            if (engine.Flags.ShadowAccountingOnly) return;   // belt and braces: observe-only never writes
            EnsureQueries(em);
            RentWrites = ConditionWrites = LandValueEdgeWrites = LevelSwapsApplied = 0;

            if (engine.Flags.TierC_LandAccounting)
            {
                ApplyRents(em, w, engine.P);
                MirrorLandValue(em, w, engine);
            }
            if (engine.Flags.TierC2_Leveling)
                ApplyConditionAndLevels(em, w, engine.P);
            if (engine.Flags.ConstructionRewire)
                RecordConstructionRequests(w);
        }

        /// <summary>Deadband test for rent writes (see RentDeadbandShare).
        /// Always writes when either side is non-positive so going to/from
        /// zero rent is never swallowed.</summary>
        private static bool WorthWriting(int current, int target)
        {
            if (current == target) return false;
            if (current <= 0 || target <= 0) return true;
            return Math.Abs(target - current) >= Math.Max(1.0, RentDeadbandShare * current);
        }

        private void EnsureQueries(EntityManager em)
        {
            if (_queriesCreated) return;
            // §3: LandValue lives on net entities; pair with Edge so the
            // midpoint lookup below has endpoints.
            _landValueEdgeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadWrite<Game.Net.LandValue>());
            // §3: per-level data lives on PREFAB entities.
            _spawnablePrefabQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.SpawnableBuildingData>(),
                ComponentType.ReadOnly<Game.Prefabs.BuildingData>());
            _queriesCreated = true;
        }

        // ------------------------------------------------------------------
        // Assessments → PropertyRenter.m_Rent (§3)
        // ------------------------------------------------------------------
        private void ApplyRents(EntityManager em, WorldState w, EconParams p)
        {
            // Households: the engine's per-household charge (S + tax + wedge).
            // Under the co-op re-rate this tracks the parcel's live market
            // assessment EVERY tick, so — unlike the old anniversary regime —
            // the value genuinely drifts on every household continuously.
            // RentDeadband is what keeps that from becoming an ECS write storm:
            // only a move of at least one money unit AND RentDeadbandShare of
            // the current rent is written through. Sub-threshold drift
            // accumulates in the engine (the source of truth) and lands on the
            // first write that clears the band, so nothing is lost — only the
            // ECS traffic is thinned.
            // Pairing is entity-slot → engine id via the reader's id lists;
            // WorldState list position is NOT a valid key (the engine appends
            // its own arrivals/entrants there — EconReader header).
            var hhEnts = _reader.HouseholdEntities;
            var hhIds = _reader.HouseholdIds;
            for (int i = 0; i < hhEnts.Count; i++)
            {
                var h = w.Households[hhIds[i]];
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                var e = hhEnts[i];
                if (!em.Exists(e) || !em.HasComponent<Game.Buildings.PropertyRenter>(e)) continue;
                var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                int rent = (int)Math.Round(Math.Max(0.0, h.ChargedAssessment) * RentScale);
                if (WorthWriting(pr.m_Rent, rent))
                {
                    pr.m_Rent = rent;
                    em.SetComponentData(e, pr);
                    RentWrites++;
                }
            }

            // Firms: parcel-level assessment (per-unit flow × job slots).
            var firmEnts = _reader.FirmEntities;
            var firmIds = _reader.FirmIds;
            for (int i = 0; i < firmEnts.Count; i++)
            {
                var f = w.Firms[firmIds[i]];
                if (f.Dead || f.Parcel < 0) continue;
                var e = firmEnts[i];
                if (!em.Exists(e) || !em.HasComponent<Game.Buildings.PropertyRenter>(e)) continue;
                var pl = w.Parcels[f.Parcel];
                int rent = (int)Math.Round(EconAssess.PerUnitFlow(pl, p) * Math.Max(1, pl.Units) * RentScale);
                var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                if (pr.m_Rent != rent)
                {
                    pr.m_Rent = rent;
                    em.SetComponentData(e, pr);
                    RentWrites++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Land component → Game.Net.LandValue on net edges (§3, §4)
        // ------------------------------------------------------------------
        /// <summary>Cluster-mean assessed land flow, capitalized at the hurdle
        /// rate (stock = flow / h), written to every net edge in the cluster.
        /// This is a MIRROR for vanilla readers — the engine never reads it
        /// back (the §3 circularity guard: assessments come from best-permitted
        /// use, never from realized land value). LandValueSystem is disabled
        /// when this runs (TierC row), so the field is otherwise frozen.</summary>
        private void MirrorLandValue(EntityManager em, WorldState w, EconomyEngine engine)
        {
            int cc = _access.ClusterCount;
            if (cc == 0) return;
            var p = engine.P;

            var flow = new double[cc];
            var count = new int[cc];
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                if ((uint)pl.Cluster >= (uint)cc) continue;
                flow[pl.Cluster] += pl.AssessedLR;
                count[pl.Cluster]++;
            }
            var lv = new float[cc];
            for (int c = 0; c < cc; c++)
            {
                double meanFlow = count[c] > 0 ? flow[c] / count[c] : 0;
                lv[c] = (float)(p.CaptureFraction * meanFlow / Math.Max(p.HurdleRate, 1e-9));
            }

            using var edges = _landValueEdgeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                if (!Verify_NetEdgeMidpoint(em, e, out float x, out float z)) continue;
                int c = _access.ClusterOf(x, z);
                if ((uint)c >= (uint)cc) continue;
                var comp = em.GetComponentData<Game.Net.LandValue>(e);
                if (Math.Abs(comp.m_LandValue - lv[c]) > 0.01f)
                {
                    comp.m_LandValue = lv[c];
                    // m_Weight untouched: only the field's EXISTENCE is dump-
                    // verified; its semantics are checklist-item-6 territory.
                    em.SetComponentData(e, comp);
                    LandValueEdgeWrites++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Condition + level → BuildingCondition / prefab swap (§3)
        // ------------------------------------------------------------------
        private void ApplyConditionAndLevels(EntityManager em, WorldState w, EconParams p)
        {
            int swapsLeft = MaxLevelSwapsPerApply;
            var plEnts = _reader.ParcelEntities;
            var plIds = _reader.ParcelIds;
            for (int i = 0; i < plEnts.Count; i++)
            {
                var pl = w.Parcels[plIds[i]];
                if (pl.State != ParcelState.Built) continue;
                var e = plEnts[i];
                if (!em.Exists(e)) continue;

                // Condition: inverse of the reader's money-like mapping —
                // engine-owned once Tier C2 is live (vanilla decay disabled
                // with BuildingUpkeepSystem; ours decays only where S goes
                // unpaid — design §4.4).
                if (em.HasComponent<Game.Buildings.BuildingCondition>(e))
                {
                    int cond = (int)Math.Round((Math.Clamp(pl.Condition, 0.0, 1.0) - 0.5) * ConditionMoneyRange);
                    var bc = em.GetComponentData<Game.Buildings.BuildingCondition>(e);
                    if (bc.m_Condition != cond)
                    {
                        bc.m_Condition = cond;
                        em.SetComponentData(e, bc);
                        ConditionWrites++;
                    }
                }

                // Level: renovations completed by the engine's leveling clock
                // materialize as a prefab swap (level lives on the PREFAB).
                if (swapsLeft > 0
                    && !em.HasComponent<Game.Buildings.Abandoned>(e)                       // §3
                    && GameLevelOf(em, e, out byte gameLevel, out Entity currentPrefab))
                {
                    byte targetLevel = (byte)Math.Clamp(pl.Level, 1, p.MaxLevel);
                    if (gameLevel != targetLevel
                        && Verify_SwapLevelPrefab(em, e, currentPrefab, targetLevel))
                    {
                        LevelSwapsApplied++;
                        swapsLeft--;
                        Debug.Log($"[CS2Econ] level swap parcel={pl.Id} {gameLevel}→{targetLevel} "
                                  + $"use={pl.Use} cluster={pl.Cluster}");
                    }
                }
            }
        }

        /// <summary>Current game-side level: instance → prefab (PrefabRef, the
        /// same pointer EconReader builds parcels from — the one un-dumped name
        /// in this pair, flagged in ITS header) → SpawnableBuildingData.m_Level
        /// (§3, verified).</summary>
        private static bool GameLevelOf(EntityManager em, Entity building, out byte level, out Entity prefab)
        {
            level = 0; prefab = Entity.Null;
            if (!em.HasComponent<Game.Prefabs.PrefabRef>(building)) return false;
            prefab = em.GetComponentData<Game.Prefabs.PrefabRef>(building).m_Prefab;
            if (prefab == Entity.Null || !em.HasComponent<Game.Prefabs.SpawnableBuildingData>(prefab)) return false;
            level = em.GetComponentData<Game.Prefabs.SpawnableBuildingData>(prefab).m_Level;
            return true;
        }

        // ------------------------------------------------------------------
        // Construction starts → the ZoneSpawnSystem seam
        // ------------------------------------------------------------------
        /// <summary>Record every parcel the engine's ConstructionSystem moved
        /// into UnderConstruction since the last pass. The queue is the
        /// authoritative site-selection output while vanilla ZoneSpawnSystem is
        /// disabled (ConstructionRewire): the bring-up spawn injector drains it
        /// into real building spawns (vanilla's Game.Objects.UnderConstruction
        /// + BuildingConstructionSystem carry the visual lag, §3/§4). A parcel
        /// re-arms after completion, so redevelopment cycles re-queue.</summary>
        private void RecordConstructionRequests(WorldState w)
        {
            foreach (var pl in w.Parcels)
            {
                if (pl.State == ParcelState.UnderConstruction)
                {
                    if (_recordedStarts.Add(pl.Id))
                    {
                        var use = pl.TargetUse != ZoneKind.None ? pl.TargetUse : pl.Zoned;
                        PendingConstruction.Add(new ConstructionRequest(
                            pl.Id, pl.Cluster, use, Math.Max(1, pl.TargetLevel), pl.Units, w.Tick));
                        Debug.Log($"[CS2Econ] construction start parcel={pl.Id} use={use} "
                                  + $"level={pl.TargetLevel} units={pl.Units} cluster={pl.Cluster}");
                    }
                }
                else
                {
                    _recordedStarts.Remove(pl.Id);
                }
            }
            if (PendingConstruction.Count > MaxPendingConstruction)
                PendingConstruction.RemoveRange(0, PendingConstruction.Count - MaxPendingConstruction);
        }

        // ==================================================================
        // Verify_ isolation cells. Research-notes contract: every ECS name
        // NOT in the verified dump lives in exactly one of these tiny
        // methods, so a wrong guess is one localized compile error on the
        // game machine — never a silent bug.
        // ==================================================================

        /// <summary>// VERIFY-INGAME: the dump lists Game.Net.Edge (routing-repo
        /// verified fields m_Start/m_End) but NOT Game.Net.Node's fields — the
        /// SAME "float3 m_Position" guess as ClusterAccessProvider.
        /// Verify_NodePosition; fix both files together if it differs.
        /// Midpoint of the edge's endpoint nodes locates the edge for
        /// ClusterOf (block-level precision is plenty at cluster granularity —
        /// no Curve/Bezier guess needed).</summary>
        private static bool Verify_NetEdgeMidpoint(EntityManager em, Entity edge, out float x, out float z)
        {
            x = z = 0f;
            var ed = em.GetComponentData<Game.Net.Edge>(edge);
            if (!em.HasComponent<Game.Net.Node>(ed.m_Start) || !em.HasComponent<Game.Net.Node>(ed.m_End))
                return false;
            float3 a = em.GetComponentData<Game.Net.Node>(ed.m_Start).m_Position;
            float3 b = em.GetComponentData<Game.Net.Node>(ed.m_End).m_Position;
            x = (a.x + b.x) * 0.5f;
            z = (a.z + b.z) * 0.5f;
            return true;
        }

        /// <summary>// VERIFY-INGAME (§9 item 7 — run with tenants in place):
        /// renovation = prefab swap, because the discrete level lives on the
        /// PREFAB (SpawnableBuildingData.m_Level, §3). Three things to confirm
        /// in the decompile before trusting this body:
        ///   1. PrefabRef.m_Prefab is the instance→prefab pointer and is
        ///      SETtable to retarget the instance (vanilla's own level-up path
        ///      — post-2.0 believed inside BuildingUpkeepSystem — is the
        ///      template: reuse its candidate selection and any notification/
        ///      event components it adds, e.g. an Updated tag or a
        ///      BatchesUpdated event, so rendering + zone-check systems pick
        ///      the swap up).
        ///   2. Candidate legality: same zone prefab AND same lot footprint at
        ///      the target level (both keys §3-verified fields). If no such
        ///      prefab exists the swap is skipped — the engine keeps the
        ///      target and retries as the catalog allows.
        ///   3. Renter survival. If renters do NOT survive an external swap:
        ///      EVICT-REHOUSE FALLBACK (the documented contract) — before
        ///      swapping, clear each renter's PropertyRenter and let the
        ///      engine's allocation machinery re-house them next tick;
        ///      core-side the occupants keep their parcel assignment, so the
        ///      round trip is invisible to the economy (same recovery path
        ///      SyncParcels already uses for demolitions).</summary>
        private bool Verify_SwapLevelPrefab(EntityManager em, Entity building, Entity currentPrefab, byte targetLevel)
        {
            // Lazy prefab catalog index over §3-verified prefab data:
            // (zone prefab, lot size, level) → candidate prefab entities.
            if (_prefabIndex == null)
            {
                _prefabIndex = new Dictionary<(Entity, int, int, byte), List<Entity>>();
                using var prefabs = _spawnablePrefabQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var pe = prefabs[i];
                    var sb = em.GetComponentData<Game.Prefabs.SpawnableBuildingData>(pe);  // §3: m_ZonePrefab/m_Level
                    var bd = em.GetComponentData<Game.Prefabs.BuildingData>(pe);           // §3: m_LotSize
                    var key = (sb.m_ZonePrefab, (int)bd.m_LotSize.x, (int)bd.m_LotSize.y, sb.m_Level);
                    if (!_prefabIndex.TryGetValue(key, out var list))
                        _prefabIndex[key] = list = new List<Entity>();
                    list.Add(pe);
                }
            }

            var cur = em.GetComponentData<Game.Prefabs.SpawnableBuildingData>(currentPrefab);
            var curBd = em.GetComponentData<Game.Prefabs.BuildingData>(currentPrefab);
            if (!_prefabIndex.TryGetValue(
                    (cur.m_ZonePrefab, (int)curBd.m_LotSize.x, (int)curBd.m_LotSize.y, targetLevel),
                    out var candidates) || candidates.Count == 0)
                return false;                              // catalog has no same-footprint prefab at ℓ*

            // Deterministic variety: pick by building id, not RNG (design §3:
            // reproducibility; no shared-state draw in the writer).
            var target = candidates[(building.Index & 0x7FFFFFFF) % candidates.Count];
            if (target == currentPrefab) return false;

            em.SetComponentData(building, new Game.Prefabs.PrefabRef { m_Prefab = target });
            return true;
        }
    }
#endif
}
