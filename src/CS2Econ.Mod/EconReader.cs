// ECS → WorldState reader (stage 7) — the game-facing half of the adapter
// seam. Mappings, all against [verified-dump] components (research notes §3)
// unless isolated in a Verify_ method:
//
//   Clusters      ← ClusterAccessProvider's spatial buckets over Game.Net
//                   nodes (standalone; the routing rebuild's ClusterCache /
//                   CCH costs are an optional swap-in — see
//                   ClusterAccessProvider.cs header, design §3).
//   Parcel        ← building entities: PrefabRef → SpawnableBuildingData
//                   (m_Level — the discrete 1–5 level lives on the PREFAB;
//                   level change = prefab swap) + BuildingData (m_LotSize) +
//                   BuildingPropertyData (m_ResidentialProperties,
//                   m_SpaceMultiplier) + BuildingCondition + UnderConstruction
//                   + Abandoned; zoned-EMPTY parcels from Game.Zones.Block
//                   (m_Position/m_Size), zone kind via the block's cells
//                   (Verify_BlockZoneKind).
//   Household     ← Game.Citizens.Household (m_Resources = money) + Citizen
//                   (age/education → segment) + Worker (m_Workplace →
//                   employed; m_LastCommuteTime = realized commute telemetry)
//                   + HouseholdMember (citizen → household grouping) +
//                   PropertyRenter (m_Property → home, m_Rent → charged) +
//                   HomelessHousehold → Sheltered.
//   Firm          ← Game.Companies.* (CompanyData gate, WorkProvider
//                   .m_MaxWorkers → JobSlots, Employer.m_Workers → filled,
//                   Profitability), sector via company/building tags, output
//                   via the prefab's process data (Verify_).
//   TradeExit     ← Game.Objects.OutsideConnection + Game.Prefabs.
//                   OutsideTradeParameterData (per-mode Weight/Distance
//                   multipliers seed t in p(Q) = a ± t·(Q/ρ)^(1/d); the
//                   region-size knob seeds ρ) — one law per (resource × exit).
//   Segment       ← ResourceMap.SegmentFor: income × education × lifecycle
//                   decomposition (§4.2) onto Segment.All indices.
//   Suitability   ← the game's natural-resource layer — NOT covered by the
//                   notes; fully isolated in Verify_ReadResourceSuitability.
//
// Counterpart writer seam (doc folded from EconAdapters.cs — implementation in
// EconWriter.cs): results land in VANILLA components wherever vanilla systems
// also read them (PLAN §5: a save opened without the mod stays coherent;
// feature-flags-off degrades cleanly): assessments → PropertyRenter.m_Rent,
// land component mirrored into Game.Net.LandValue (LandValueSystem disabled —
// the LandValueOverhaul precedent, notes §6), condition →
// BuildingCondition.m_Condition, level changes → prefab swap (verification
// item #7: renters must survive the swap; fallback evict-and-rehouse through
// the allocation machinery), construction → ZoneSpawnSystem-seam spawn
// requests (Game.Objects.UnderConstruction carries the lag), Tier D prices →
// verification item #8 (ResourceSystem prefab table vs Game.Economy.
// ResourceInfo). Mod-native state (escrow, trade EMAs, migration scalars,
// calibration) rides the EconSerialization ISerializable components (§7),
// schema-versioned from day one.

using System;
using System.Collections.Generic;
using CS2Econ.Core;

#if !OUT_OF_GAME_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
#endif

namespace CS2Econ.Mod
{
    // NOTE: the game↔core resource/segment mapping lives in ResourceMap.cs
    // (one copy, one Verify_ method — the file with the IsTracked/ToGameIndex
    // surface). An earlier draft embedded a second ResourceMap here; the two
    // tables diverged (Minerals, Pharmaceuticals, Meals, the senior education
    // split) and the duplicate was removed — reconciliation rationale is
    // documented at the fold table in ResourceMap.cs.

#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked (same
    /// discipline as Mod.cs). The real implementation is the
    /// !OUT_OF_GAME_BUILD half of this file.</summary>
    public sealed class EconReader
    {
        public EconReader(ClusterAccessProvider access) { _ = access; }
        public WorldState BuildInitial() =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
        public void SyncTick() =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
    }
#else
    public sealed class EconReader
    {
        // ---- calibration constants (numeric, not name guesses) ---------------
        private const double SpacePerJobSlotM2 = 40.0;   // firm floor-space per slot (lot m² × m_SpaceMultiplier)
        private const double ConditionMoneyRange = 200_000.0; // BuildingCondition.m_Condition is money-like post-2.0 — calibrate on the game machine (notes §9 item 2)
        private const double TradeTBase = 0.5;           // t seed as a fraction of anchor before mode multipliers (notes §3: OutsideTradeParameterData is "seed data for calibrating t")
        private const double RailHandlingShare = 0.05;   // per-unit terminal handling as anchor fraction (design §4.5)
        private const double SeaAirCapacityUnits = 300.0;// per-tick cap on the capacity-capped modes (design §4.5) — calibrate
        private const float OccCell = 16f;               // occupancy hash cell (m) for the zoned-empty pass

        private readonly ClusterAccessProvider _access;

        // Entity ↔ engine-id maps. Each XEntities[k] pairs with the engine id
        // XIds[k] — NEVER with WorldState list position k: the ENGINE also
        // appends to the WorldState lists (migration arrivals, firm entry), so
        // positional pairing silently cross-wires entities to engine objects
        // after the first engine tick. Both lists are append-only (ids never
        // reused — same discipline as the engine's Dead/ExitedTick flags).
        // Public: EconWriter maps engine results back onto entities through
        // these. XIndex maps entity → engine id directly.
        public readonly List<Entity> ParcelEntities = new List<Entity>();
        public readonly List<int> ParcelIds = new List<int>();
        public readonly Dictionary<Entity, int> ParcelIndex = new Dictionary<Entity, int>();
        public readonly List<Entity> HouseholdEntities = new List<Entity>();
        public readonly List<int> HouseholdIds = new List<int>();
        public readonly Dictionary<Entity, int> HouseholdIndex = new Dictionary<Entity, int>();
        public readonly List<Entity> FirmEntities = new List<Entity>();
        public readonly List<int> FirmIds = new List<int>();
        public readonly Dictionary<Entity, int> FirmIndex = new Dictionary<Entity, int>();
        /// <summary>Parallel to WorldState.Exits (several exits — one per
        /// tradable resource — share one OutsideConnection entity). Exits are
        /// ONLY ever created by this reader, so positional alignment with
        /// WorldState.Exits is safe here — unlike households/firms above.</summary>
        public readonly List<Entity> ExitEntities = new List<Entity>();

        private readonly HashSet<long> _occupiedCells = new HashSet<long>();
        private readonly Dictionary<long, int> _emptyParcelByCell = new Dictionary<long, int>();
        private Dictionary<ushort, ZoneKind>? _zoneKindByIndex;

        public EconReader(ClusterAccessProvider access) { _access = access; }

        // ==================================================================
        // BuildInitial: one full ECS scan → a complete WorldState.
        // ==================================================================
        public WorldState BuildInitial(EntityManager em, EconParams p, ulong seed = 0xC57E5EEDUL)
        {
            if (_access.ClusterCount == 0)
                throw new InvalidOperationException(
                    "ClusterAccessProvider.RebuildClusters must run before EconReader.BuildInitial.");

            // CS2 world units ARE meters — the vacancy kernel's λ needs no scale.
            var w = new WorldState { Rng = new SplitMix64(seed), MetersPerUnit = 1.0 };
            BuildClusters(w);
            Verify_ReadResourceSuitability(em, w);
            ReadBuildingParcels(em, w, p);
            ReadZonedEmptyParcels(em, w);
            var agg = ScanCitizens(em);
            ReadHouseholds(em, w, p, agg);
            ReadFirms(em, w, p);
            ReadTradeExits(em, w, p);
            // Load seam: pull mod-native state (escrow/targets, trade EMAs,
            // migration + calibration scalars) back off the save's entities.
            // No-op on a save that never ran a levying session.
            EconStatePersistence.Restore(em, this, w);
            w.RebuildIndices();
            return w;
        }

        // ==================================================================
        // SyncTick: incremental game→engine refresh, called every N game
        // frames by EconBridgeSystem before engine.Step(). In shadow mode
        // (vanillaAuthoritative=true) vanilla remains the source of truth for
        // money / rents / condition / occupancy, and the engine only predicts;
        // once the writer levies for real, those become engine-owned and only
        // existence / employment / topology are pulled from the game.
        // ==================================================================
        public void SyncTick(EntityManager em, WorldState w, EconParams p, bool vanillaAuthoritative = true)
        {
            var agg = ScanCitizens(em);
            SyncParcels(em, w, p, vanillaAuthoritative);
            SyncHouseholds(em, w, p, agg, vanillaAuthoritative);
            SyncFirms(em, w, p, vanillaAuthoritative);
            w.RebuildIndices();
        }

        // ------------------------------------------------------------------
        // Clusters
        // ------------------------------------------------------------------
        private void BuildClusters(WorldState w)
        {
            int c0 = _access.ClusterCount;
            w.Clusters = new ClusterInfo[c0];
            for (int c = 0; c < c0; c++)
            {
                var (x, z) = _access.ClusterCentroid(c);
                // Amenity/School/Health start at neutral baselines — the
                // city-service coupling stage feeds them; Pollution/Noise are
                // engine-updated.
                w.Clusters[c] = new ClusterInfo { Id = c, X = x, Y = z, Amenity = 1.0, School = 0.5, Health = 0.5 };
            }
        }

        // ------------------------------------------------------------------
        // Parcels — built buildings
        // ------------------------------------------------------------------
        private static EntityQuery BuildingQuery(EntityManager em)
            => em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.PrefabRef>(),
                ComponentType.ReadOnly<Game.Buildings.BuildingCondition>());   // §3: growables carry condition

        private void ReadBuildingParcels(EntityManager em, WorldState w, EconParams p)
        {
            var q = BuildingQuery(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                AddBuildingParcel(em, w, p, ents[i]);
        }

        private int AddBuildingParcel(EntityManager em, WorldState w, EconParams p, Entity e)
        {
            // §3: PrefabRef → the prefab entity; per-level data lives THERE.
            Entity prefab = em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab;
            if (prefab == Entity.Null || !em.HasComponent<Game.Prefabs.SpawnableBuildingData>(prefab))
                return -1;                                    // service/signature buildings are not parcels
            var sb = em.GetComponentData<Game.Prefabs.SpawnableBuildingData>(prefab); // §3: m_Level on the PREFAB
            int lotCells = 4;
            float lotRadius = 12f;
            if (em.HasComponent<Game.Prefabs.BuildingData>(prefab))
            {
                var bd = em.GetComponentData<Game.Prefabs.BuildingData>(prefab);      // §3: m_LotSize (cells of 8 m)
                lotCells = Math.Max(1, bd.m_LotSize.x * bd.m_LotSize.y);
                lotRadius = Math.Max(bd.m_LotSize.x, bd.m_LotSize.y) * 8f * 0.5f + 8f;
            }
            bool hasBp = em.HasComponent<Game.Prefabs.BuildingPropertyData>(prefab);
            var bp = hasBp
                ? em.GetComponentData<Game.Prefabs.BuildingPropertyData>(prefab)      // §3: m_ResidentialProperties/m_SpaceMultiplier
                : default;

            ZoneKind use = ClassifyBuilding(em, e, bp, hasBp);
            if (use == ZoneKind.None) return -1;

            float3 pos = Verify_ObjectPosition(em, e);
            bool residential = use == ZoneKind.ResidentialLow || use == ZoneKind.ResidentialHigh;
            int units = residential
                ? Math.Max(1, bp.m_ResidentialProperties)
                : Math.Max(1, (int)Math.Round(lotCells * 64.0 * Math.Max(0.25f, hasBp ? bp.m_SpaceMultiplier : 1f)
                                              / SpacePerJobSlotM2));
            int level = Math.Clamp(sb.m_Level, (byte)1, (byte)5);

            var pl = new Parcel
            {
                Id = w.Parcels.Count,
                Cluster = _access.ClusterOf(pos.x, pos.z),
                Zoned = use,
                Use = use,
                State = em.HasComponent<Game.Objects.UnderConstruction>(e)            // §3/§4: vanilla models the lag
                    ? ParcelState.UnderConstruction : ParcelState.Built,
                Level = level,
                Units = units,
                Condition = Cond01(em.GetComponentData<Game.Buildings.BuildingCondition>(e).m_Condition),
                TargetLevel = level,
                TargetUse = use,
                BuildTotal = p.ConstructionLag,
                CommittedCost = p.RC(level, units),
            };
            if (em.HasComponent<Game.Buildings.Abandoned>(e))                          // §3
                pl.Condition = Math.Min(pl.Condition, 0.1);

            ParcelIndex[e] = pl.Id;
            ParcelEntities.Add(e);
            ParcelIds.Add(pl.Id);
            w.Parcels.Add(pl);
            MarkOccupied(w, pos.x, pos.z, lotRadius);
            return pl.Id;
        }

        /// <summary>Use classification from the §3-verified property tags on
        /// the building entity. Mixed-use (residential over commercial) counts
        /// as residential — housing supply is what the engine allocates over.
        /// Low/High density heuristic: unit count (the zone table's
        /// m_ScaleResidentials is the cleaner signal, used for empty blocks).</summary>
        private static ZoneKind ClassifyBuilding(EntityManager em, Entity e,
            in Game.Prefabs.BuildingPropertyData bp, bool hasBp)
        {
            if (em.HasComponent<Game.Buildings.ResidentialProperty>(e) && (!hasBp || bp.m_ResidentialProperties > 0))
                return hasBp && bp.m_ResidentialProperties >= 5 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
            if (em.HasComponent<Game.Buildings.ExtractorProperty>(e)) return ZoneKind.Extractor;
            if (em.HasComponent<Game.Buildings.OfficeProperty>(e)) return ZoneKind.Office;
            if (em.HasComponent<Game.Buildings.IndustrialProperty>(e)) return ZoneKind.Industrial;
            if (em.HasComponent<Game.Buildings.CommercialProperty>(e)) return ZoneKind.Commercial;
            return ZoneKind.None;
        }

        /// <summary>BuildingCondition.m_Condition (int, §3) → the core's 0..1.
        /// Post-Economy-2.0 condition is money-like (accumulates toward the
        /// level-up threshold, negative toward abandonment) — the range
        /// constant is a calibration item on the game machine.</summary>
        private static double Cond01(int condition)
            => Math.Clamp(0.5 + condition / ConditionMoneyRange, 0.05, 1.0);

        // ------------------------------------------------------------------
        // Parcels — zoned-empty blocks (construction supply for Tier E)
        // ------------------------------------------------------------------
        private void ReadZonedEmptyParcels(EntityManager em, WorldState w)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Zones.Block>());  // §3: the zoning-grid substrate
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var block = em.GetComponentData<Game.Zones.Block>(e);                  // §3: m_Position/m_Size verified
                ZoneKind zoned = Verify_BlockZoneKind(em, e);
                if (zoned == ZoneKind.None) continue;
                float x = block.m_Position.x, z = block.m_Position.z;
                if (IsOccupied(x, z)) continue;               // a built parcel already covers this block
                var pl = new Parcel
                {
                    Id = w.Parcels.Count,
                    Cluster = _access.ClusterOf(x, z),
                    Zoned = zoned,
                    Use = ZoneKind.None,
                    State = ParcelState.Empty,
                    Level = 1,
                    Units = 0,
                    Condition = 1.0,
                };
                ParcelIndex[e] = pl.Id;
                ParcelEntities.Add(e);
                ParcelIds.Add(pl.Id);
                w.Parcels.Add(pl);
                _emptyParcelByCell[CellKey(x, z)] = pl.Id;
            }
        }

        private static long CellKey(float x, float z)
            => ((long)(int)MathF.Floor(x / OccCell) << 32) ^ (uint)(int)MathF.Floor(z / OccCell);

        private void MarkOccupied(WorldState w, float x, float z, float radius)
        {
            int r = Math.Max(0, (int)MathF.Ceiling(radius / OccCell));
            int ix = (int)MathF.Floor(x / OccCell), iz = (int)MathF.Floor(z / OccCell);
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    long key = ((long)(ix + dx) << 32) ^ (uint)(iz + dz);
                    _occupiedCells.Add(key);
                    // A building landing on a previously-empty block retires
                    // that block-parcel from site selection (claims stay real).
                    if (_emptyParcelByCell.TryGetValue(key, out int pid)
                        && w.Parcels[pid].State == ParcelState.Empty)
                        w.Parcels[pid].Zoned = ZoneKind.None;
                }
        }

        private bool IsOccupied(float x, float z) => _occupiedCells.Contains(CellKey(x, z));

        // ------------------------------------------------------------------
        // Citizens → per-household aggregates (§4.2 segment decomposition)
        // ------------------------------------------------------------------
        private struct HhAgg
        {
            public int Children, Adults, AdultStudents, Elderly, Workers, MaxEdu;
        }

        private Dictionary<Entity, HhAgg> ScanCitizens(EntityManager em)
        {
            var agg = new Dictionary<Entity, HhAgg>();
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Citizens.Citizen>(),                       // §3
                ComponentType.ReadOnly<Game.Citizens.HouseholdMember>());              // §3 (fields via Verify_)
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                Entity hh = Verify_HouseholdOfCitizen(em.GetComponentData<Game.Citizens.HouseholdMember>(e));
                if (hh == Entity.Null) continue;
                var cz = em.GetComponentData<Game.Citizens.Citizen>(e);
                int age = Verify_CitizenAgeGroup(cz);            // 0 Child .. 3 Elderly
                int edu = Verify_CitizenEducation(cz);           // 0..4
                bool student = em.HasComponent<Game.Citizens.Student>(e);              // §3 tag
                bool worker = em.HasComponent<Game.Citizens.Worker>(e)                 // §3: m_Workplace
                    && em.GetComponentData<Game.Citizens.Worker>(e).m_Workplace != Entity.Null;
                agg.TryGetValue(hh, out var a);
                if (age <= 1) a.Children++;
                else if (age == 2) { a.Adults++; if (student) a.AdultStudents++; }
                else a.Elderly++;
                if (worker) a.Workers++;
                if (age >= 2 && edu > a.MaxEdu) a.MaxEdu = edu;
                agg[hh] = a;
            }
            return agg;
        }

        private static int SegmentOf(in HhAgg a)
        {
            bool retired = a.Adults == 0 && a.Elderly > 0;
            bool studentLed = a.Adults > 0 && a.AdultStudents == a.Adults;
            int seg = ResourceMap.SegmentFor(a.MaxEdu, retired ? 3 : 2, retired, studentLed);
            return a.Children > 0 ? ResourceMap.PromoteToFamily(seg) : seg;
        }

        // ------------------------------------------------------------------
        // Households
        // ------------------------------------------------------------------
        private void ReadHouseholds(EntityManager em, WorldState w, EconParams p, Dictionary<Entity, HhAgg> agg)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Citizens.Household>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                AddHousehold(em, w, p, ents[i], agg);
        }

        private void AddHousehold(EntityManager em, WorldState w, EconParams p, Entity e,
            Dictionary<Entity, HhAgg> agg)
        {
            // Visitors are not residents (§4.2): tourist/commuter tags, §3.
            if (em.HasComponent<Game.Citizens.TouristHousehold>(e)
                || em.HasComponent<Game.Citizens.CommuterHousehold>(e)) return;
            var hh = em.GetComponentData<Game.Citizens.Household>(e);                  // §3: m_Resources = money
            agg.TryGetValue(e, out var a);
            var h = new Household
            {
                Id = w.Households.Count,
                Segment = SegmentOf(a),
                Money = hh.m_Resources,
                Employed = a.Workers > 0,
                ArrivedTick = w.Tick,
                TenureStart = w.Tick,
                MovingCostDraw = p.MovingCostMean * (0.4 + 1.2 * w.Rng.NextDouble()),
            };
            if (em.HasComponent<Game.Citizens.HomelessHousehold>(e))                   // §3: m_TempHome
                h.Stage = InsolvencyStage.Sheltered;
            if (em.HasComponent<Game.Buildings.PropertyRenter>(e))
            {
                var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);        // §3: m_Property/m_Rent
                if (ParcelIndex.TryGetValue(pr.m_Property, out int pid))
                {
                    h.HomeParcel = pid;
                    h.ChargedAssessment = pr.m_Rent;
                    w.Parcels[pid].OccupantHouseholds.Add(h.Id);
                }
            }
            HouseholdIndex[e] = h.Id;
            HouseholdEntities.Add(e);
            HouseholdIds.Add(h.Id);
            w.Households.Add(h);
        }

        private void SyncHouseholds(EntityManager em, WorldState w, EconParams p,
            Dictionary<Entity, HhAgg> agg, bool vanillaAuthoritative)
        {
            for (int i = 0; i < HouseholdEntities.Count; i++)
            {
                var e = HouseholdEntities[i];
                var h = w.Households[HouseholdIds[i]];
                if (h.ExitedTick >= 0) continue;
                if (!em.Exists(e))
                {
                    // Vanilla moved them away (shadow mode) or the household
                    // dissolved — mirror the exit; ledger reconciliation is the
                    // bridge's shadow-accounting concern.
                    if (h.HomeParcel >= 0) w.Parcels[h.HomeParcel].OccupantHouseholds.Remove(h.Id);
                    h.HomeParcel = -1;
                    h.ExitedTick = w.Tick;
                    continue;
                }
                if (vanillaAuthoritative)
                    h.Money = em.GetComponentData<Game.Citizens.Household>(e).m_Resources;
                if (agg.TryGetValue(e, out var a)) h.Employed = a.Workers > 0;
                // Segment identity stays sticky: re-deriving it every tick would
                // fake migration churn out of aging noise.

                int newHome = -1;
                if (em.HasComponent<Game.Buildings.PropertyRenter>(e))
                {
                    var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                    if (ParcelIndex.TryGetValue(pr.m_Property, out int pid)) newHome = pid;
                    if (vanillaAuthoritative) h.ChargedAssessment = pr.m_Rent;
                }
                if (newHome != h.HomeParcel)
                {
                    if (h.HomeParcel >= 0) w.Parcels[h.HomeParcel].OccupantHouseholds.Remove(h.Id);
                    if (newHome >= 0)
                    {
                        w.Parcels[newHome].OccupantHouseholds.Add(h.Id);
                        h.TenureStart = w.Tick;
                    }
                    h.HomeParcel = newHome;
                }
                if (h.HomeParcel < 0 && em.HasComponent<Game.Citizens.HomelessHousehold>(e))
                    h.Stage = InsolvencyStage.Sheltered;
            }
            // Newly spawned households.
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Citizens.Household>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (!HouseholdIndex.ContainsKey(ents[i]))
                    AddHousehold(em, w, p, ents[i], agg);
        }

        // ------------------------------------------------------------------
        // Firms
        // ------------------------------------------------------------------
        private static EntityQuery FirmQuery(EntityManager em)
            => em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Companies.CompanyData>(),     // §3: gates out city-service employers
                ComponentType.ReadOnly<Game.Companies.WorkProvider>(),    // §3: m_MaxWorkers
                ComponentType.ReadOnly<Game.Prefabs.PrefabRef>());

        private void ReadFirms(EntityManager em, WorldState w, EconParams p)
        {
            var q = FirmQuery(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                AddFirm(em, w, p, ents[i]);
        }

        private void AddFirm(EntityManager em, WorldState w, EconParams p, Entity e)
        {
            // Warehouses and virtual outside traders are not producing firms
            // (§3 tags); the engine's trade layer owns those flows.
            if (em.HasComponent<Game.Companies.StorageCompany>(e)
                || em.HasComponent<Game.Companies.OutsideTrader>(e)) return;

            var wp = em.GetComponentData<Game.Companies.WorkProvider>(e);              // §3: m_MaxWorkers
            int pid = -1;
            if (em.HasComponent<Game.Buildings.PropertyRenter>(e))
            {
                var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                if (!ParcelIndex.TryGetValue(pr.m_Property, out pid)) pid = -1;
            }
            // Positional reverse lookup is legal for PARCELS only: parcels are
            // created solely by this reader, so ParcelIds[k] == k always holds
            // (households/firms lost that invariant to engine-side entry).
            Entity bld = pid >= 0 ? ParcelEntities[pid] : Entity.Null;
            ZoneKind sector =
                em.HasComponent<Game.Companies.ExtractorCompany>(e) ? ZoneKind.Extractor :     // §3 tags
                em.HasComponent<Game.Companies.CommercialCompany>(e) ? ZoneKind.Commercial :
                em.HasComponent<Game.Companies.ProcessingCompany>(e) ? ZoneKind.Industrial :
                bld != Entity.Null && em.HasComponent<Game.Buildings.OfficeProperty>(bld) ? ZoneKind.Office :
                bld != Entity.Null && em.HasComponent<Game.Buildings.IndustrialProperty>(bld) ? ZoneKind.Industrial :
                ZoneKind.Commercial;

            int slots = Math.Max(1, wp.m_MaxWorkers);
            int cluster = pid >= 0 ? w.Parcels[pid].Cluster : 0;
            Res output;
            switch (sector)
            {
                case ZoneKind.Commercial: output = Res.Services; break;
                case ZoneKind.Office: output = Res.OfficeOutput; break;
                case ZoneKind.Extractor:
                {
                    // Fallback = the local geology's best raw (Weber anchor).
                    var suit = w.Clusters[cluster].ResourceSuitability;
                    int best = 0;
                    for (int r = 1; r < ResourceCatalog.RawCount; r++) if (suit[r] > suit[best]) best = r;
                    output = Verify_CompanyOutputResource(em,
                        em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab, (Res)best);
                    // HARD CLAMP to the raw range: the engine prices extractor
                    // output through ResourceSuitability[(int)Output], an array
                    // sized RawCount — a processed bucket here (possible if the
                    // game tags an oddly-mapped company as extractor) would
                    // index out of bounds, not just misprice.
                    if (!ResourceCatalog.IsRaw(output)) output = (Res)best;
                    break;
                }
                default:
                    output = Verify_CompanyOutputResource(em,
                        em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab, Res.Food);
                    break;
            }

            var f = new Firm
            {
                Id = w.Firms.Count,
                Sector = sector,
                Output = output,
                Parcel = pid,
                Money = Verify_CompanyMoney(em, e, p.FirmSeedCapital),
                JobSlots = slots,
                EnteredTick = w.Tick,
            };
            if (em.HasComponent<Game.Companies.Employer>(e))                           // §3: m_Workers
                f.WorkersFilled = em.GetComponentData<Game.Companies.Employer>(e).m_Workers;
            if (em.HasComponent<Game.Companies.Profitability>(e))                      // §3: byte
                f.ProfitEma = ProfitabilityToFlow(
                    em.GetComponentData<Game.Companies.Profitability>(e).m_Profitability, slots);
            if (pid >= 0)
            {
                w.Parcels[pid].OccupantFirm = f.Id;
                w.Parcels[pid].Units = Math.Max(w.Parcels[pid].Units, slots);  // job slots = the assessment unit basis
            }
            FirmIndex[e] = f.Id;
            FirmEntities.Add(e);
            FirmIds.Add(f.Id);
            w.Firms.Add(f);
        }

        /// <summary>Vanilla Profitability byte (0..255, ~127 break-even) → a
        /// money-flow-per-tick seed for the engine's ProfitEma. Heuristic
        /// scale; the EMA converges to real settlement within ~20 ticks.</summary>
        private static double ProfitabilityToFlow(byte profitability, int slots)
            => (profitability - 127) / 128.0 * slots;

        private void SyncFirms(EntityManager em, WorldState w, EconParams p, bool vanillaAuthoritative)
        {
            for (int i = 0; i < FirmEntities.Count; i++)
            {
                var e = FirmEntities[i];
                var f = w.Firms[FirmIds[i]];
                if (f.Dead) continue;
                if (!em.Exists(e))
                {
                    f.Dead = true;
                    if (f.Parcel >= 0 && w.Parcels[f.Parcel].OccupantFirm == f.Id)
                        w.Parcels[f.Parcel].OccupantFirm = -1;
                    continue;
                }
                f.JobSlots = Math.Max(1, em.GetComponentData<Game.Companies.WorkProvider>(e).m_MaxWorkers);
                if (em.HasComponent<Game.Companies.Employer>(e))
                    f.WorkersFilled = em.GetComponentData<Game.Companies.Employer>(e).m_Workers;
                if (vanillaAuthoritative)
                {
                    f.Money = Verify_CompanyMoney(em, e, f.Money);
                    if (em.HasComponent<Game.Companies.Profitability>(e))
                        f.ProfitEma = 0.8 * f.ProfitEma + 0.2 * ProfitabilityToFlow(
                            em.GetComponentData<Game.Companies.Profitability>(e).m_Profitability, f.JobSlots);
                }
                // Property re-link (vanilla relocations in shadow mode).
                int newPid = -1;
                if (em.HasComponent<Game.Buildings.PropertyRenter>(e))
                {
                    var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                    if (!ParcelIndex.TryGetValue(pr.m_Property, out newPid)) newPid = -1;
                }
                if (newPid != f.Parcel)
                {
                    if (f.Parcel >= 0 && w.Parcels[f.Parcel].OccupantFirm == f.Id)
                        w.Parcels[f.Parcel].OccupantFirm = -1;
                    if (newPid >= 0)
                    {
                        w.Parcels[newPid].OccupantFirm = f.Id;
                        w.Parcels[newPid].Units = Math.Max(w.Parcels[newPid].Units, f.JobSlots);
                    }
                    f.Parcel = newPid;
                }
            }
            // Newly spawned companies.
            var q = FirmQuery(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (!FirmIndex.ContainsKey(ents[i]))
                    AddFirm(em, w, p, ents[i]);
        }

        // ------------------------------------------------------------------
        // Parcels sync
        // ------------------------------------------------------------------
        private void SyncParcels(EntityManager em, WorldState w, EconParams p, bool vanillaAuthoritative)
        {
            for (int i = 0; i < ParcelEntities.Count; i++)
            {
                var e = ParcelEntities[i];
                var pl = w.Parcels[ParcelIds[i]];
                if (!em.Exists(e))
                {
                    if (pl.State != ParcelState.Empty)
                    {
                        // Demolished/despawned: evict; the engine's allocation
                        // machinery re-houses next tick.
                        foreach (int hid in pl.OccupantHouseholds) w.Households[hid].HomeParcel = -1;
                        pl.OccupantHouseholds.Clear();
                        pl.OccupantFirm = -1;
                        pl.State = ParcelState.Empty;
                        pl.Units = 0;
                    }
                    continue;
                }
                if (vanillaAuthoritative && em.HasComponent<Game.Buildings.BuildingCondition>(e))
                    pl.Condition = Cond01(em.GetComponentData<Game.Buildings.BuildingCondition>(e).m_Condition);
                if (pl.State == ParcelState.UnderConstruction
                    && !em.HasComponent<Game.Objects.UnderConstruction>(e))
                {
                    pl.State = ParcelState.Built;
                    pl.CompletedTick = w.Tick;
                }
                if (em.HasComponent<Game.Buildings.Abandoned>(e))
                    pl.Condition = Math.Min(pl.Condition, 0.1);
            }
            // Newly spawned buildings since the last sync. (New zone BLOCKS —
            // replans — are picked up with the next full rebuild; incremental
            // block diffing is not worth the churn.)
            var q = BuildingQuery(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (!ParcelIndex.ContainsKey(ents[i]))
                    AddBuildingParcel(em, w, p, ents[i]);
        }

        // ------------------------------------------------------------------
        // Trade exits (design §4.5: one law per resource × exit)
        // ------------------------------------------------------------------
        private void ReadTradeExits(EntityManager em, WorldState w, EconParams p)
        {
            // §3 verified singleton: vanilla's ONLY per-mode trade
            // differentiation — weight/distance multipliers seed t.
            var otpQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.OutsideTradeParameterData>());
            bool haveOtp = !otpQ.IsEmptyIgnoreFilter;
            var otp = haveOtp ? otpQ.GetSingleton<Game.Prefabs.OutsideTradeParameterData>() : default;

            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.OutsideConnection>()); // §3 tag
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                float3 pos = Verify_ObjectPosition(em, e);
                int cluster = _access.ClusterOf(pos.x, pos.z);
                ExitMode mode = Verify_OutsideConnectionMode(em, e);

                double wm = 1, dm = 1;
                if (haveOtp)
                    (wm, dm) = mode switch
                    {
                        ExitMode.Road => ((double)otp.m_RoadWeightMultiplier, (double)otp.m_RoadDistanceMultiplier),
                        ExitMode.Rail => ((double)otp.m_TrainWeightMultiplier, (double)otp.m_TrainDistanceMultiplier),
                        ExitMode.Sea => ((double)otp.m_ShipWeightMultiplier, (double)otp.m_ShipDistanceMultiplier),
                        _ => ((double)otp.m_AirWeightMultiplier, (double)otp.m_AirDistanceMultiplier),
                    };
                if (wm <= 0) wm = 1;
                if (dm <= 0) dm = 1;

                for (int r = 0; r < ResourceCatalog.Count; r++)
                {
                    var res = (Res)r;
                    if (!ResourceCatalog.IsTradable(res)) continue;
                    double anchor = ResourceCatalog.Anchor[r];
                    w.Exits.Add(new TradeExit
                    {
                        Id = w.Exits.Count,
                        Mode = mode,
                        Cluster = cluster,
                        Resource = res,
                        Anchor = anchor,
                        // t seed: anchor-relative base × freight weight × the
                        // game's per-mode multipliers (notes §3).
                        T = TradeTBase * anchor * ResourceCatalog.Weight[r] * wm * dm,
                        Rho = p.RegionSize,                       // the region-size knob seeds ρ
                        // Catchment dimension (design §4.5): road 2, rail 1,
                        // sea/air ∞ (flat but capacity-capped).
                        D = mode == ExitMode.Road ? 2.0
                          : mode == ExitMode.Rail ? 1.0
                          : double.PositiveInfinity,
                        PerUnitHandling = mode == ExitMode.Rail ? RailHandlingShare * anchor : 0.0,
                        Capacity = mode == ExitMode.Sea || mode == ExitMode.Air ? SeaAirCapacityUnits : 0.0,
                        RegionGroup = -1,                         // TradeSystem's mode default grouping
                    });
                    ExitEntities.Add(e);
                }
            }
        }

        // ==================================================================
        // Verify_ isolation cells. Research-notes contract: every ECS name
        // NOT in the verified dump lives in exactly one of these tiny
        // methods, so a wrong guess is one localized compile error on the
        // game machine — never a silent bug.
        // ==================================================================

        /// <summary>// VERIFY-INGAME: HouseholdMember is dump-listed with NO
        /// fields ("tags/small", §3). Guess: it rides the CITIZEN and points
        /// at its household via "Entity m_Household".</summary>
        private static Entity Verify_HouseholdOfCitizen(Game.Citizens.HouseholdMember m) => m.m_Household;

        /// <summary>// VERIFY-INGAME: age is derived, not a dumped field
        /// (Citizen has only m_BirthDay, §3). Guess: Citizen.GetAge() returns
        /// the CitizenAge enum ordered Child, Teen, Adult, Elderly — confirm
        /// the method AND that ordering (our convention: 0..3).</summary>
        private static int Verify_CitizenAgeGroup(Game.Citizens.Citizen c) => (int)c.GetAge();

        /// <summary>// VERIFY-INGAME: education is packed into Citizen.m_State
        /// flags. Guess: Citizen.GetEducationLevel() returns int 0..4 (the
        /// five FreeWorkplaces tiers, §3).</summary>
        private static int Verify_CitizenEducation(Game.Citizens.Citizen c) => c.GetEducationLevel();

        /// <summary>// VERIFY-INGAME: world position of buildings/objects is
        /// not in the dump. Guess: Game.Objects.Transform { float3 m_Position }
        /// on every placed object entity.</summary>
        private static float3 Verify_ObjectPosition(EntityManager em, Entity e)
            => em.HasComponent<Game.Objects.Transform>(e)
                ? em.GetComponentData<Game.Objects.Transform>(e).m_Position
                : default;

        /// <summary>// VERIFY-INGAME: a block's zoning is per-CELL and the Cell
        /// element is not in the dump. Guesses: DynamicBuffer&lt;Game.Zones.Cell&gt;
        /// on the block with { Game.Zones.ZoneType m_Zone }, and ZoneType
        /// exposing "ushort m_Index". Majority vote over the block's cells.</summary>
        private ZoneKind Verify_BlockZoneKind(EntityManager em, Entity block)
        {
            if (_zoneKindByIndex == null) Verify_BuildZoneKindTable(em);
            if (!em.HasBuffer<Game.Zones.Cell>(block)) return ZoneKind.None;
            var cells = em.GetBuffer<Game.Zones.Cell>(block, true);
            var votes = new Dictionary<ushort, int>();
            for (int i = 0; i < cells.Length; i++)
            {
                ushort zi = cells[i].m_Zone.m_Index;
                if (zi == 0) continue;                       // unzoned cell
                votes.TryGetValue(zi, out int v);
                votes[zi] = v + 1;
            }
            ushort bestIdx = 0; int bestV = 0;
            foreach (var kv in votes) if (kv.Value > bestV) { bestV = kv.Value; bestIdx = kv.Key; }
            return bestV > 0 && _zoneKindByIndex!.TryGetValue(bestIdx, out var kind) ? kind : ZoneKind.None;
        }

        /// <summary>// VERIFY-INGAME: built on the VERIFIED ZoneData fields
        /// (m_ZoneType/m_AreaType/m_ZoneFlags, §3) plus three guesses: AreaType
        /// members Residential/Commercial/Industrial (namespace Game.Zones?),
        /// a ZoneFlags.Office flag (office zones are Industrial-area), and
        /// ZoneType.m_Index. Residential density uses the VERIFIED
        /// ZonePropertiesData.m_ScaleResidentials (scaling row-homes = low
        /// density).</summary>
        private void Verify_BuildZoneKindTable(EntityManager em)
        {
            var table = new Dictionary<ushort, ZoneKind>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.ZoneData>());
            using var prefabs = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
            {
                var zd = em.GetComponentData<Game.Prefabs.ZoneData>(prefabs[i]);
                ZoneKind kind;
                switch (zd.m_AreaType)
                {
                    case Game.Zones.AreaType.Residential:
                        bool scaled = em.HasComponent<Game.Prefabs.ZonePropertiesData>(prefabs[i])
                            && em.GetComponentData<Game.Prefabs.ZonePropertiesData>(prefabs[i]).m_ScaleResidentials;
                        kind = scaled ? ZoneKind.ResidentialLow : ZoneKind.ResidentialHigh;
                        break;
                    case Game.Zones.AreaType.Commercial:
                        kind = ZoneKind.Commercial;
                        break;
                    case Game.Zones.AreaType.Industrial:
                        kind = (zd.m_ZoneFlags & Game.Prefabs.ZoneFlags.Office) != 0
                            ? ZoneKind.Office : ZoneKind.Industrial;
                        break;
                    default:
                        kind = ZoneKind.None;
                        break;
                }
                table[zd.m_ZoneType.m_Index] = kind;
            }
            _zoneKindByIndex = table;
        }

        /// <summary>// VERIFY-INGAME: process recipes are not field-dumped
        /// (only the NAME IndustrialProcessData is, via TaxSystem's reads,
        /// §4). Guess: the company PREFAB carries Game.Prefabs.
        /// IndustrialProcessData with m_Output.m_Resource (a Game.Economy.
        /// Resource). Fix the nesting here if it differs; ResourceMap.ToCore
        /// owns the index mapping.</summary>
        private static Res Verify_CompanyOutputResource(EntityManager em, Entity companyPrefab, Res fallback)
        {
            if (companyPrefab != Entity.Null
                && em.HasComponent<Game.Prefabs.IndustrialProcessData>(companyPrefab))
            {
                var pd = em.GetComponentData<Game.Prefabs.IndustrialProcessData>(companyPrefab);
                return ResourceMap.ToCore(Game.Economy.EconomyUtils.GetResourceIndex(pd.m_Output.m_Resource));
            }
            return fallback;
        }

        /// <summary>// VERIFY-INGAME: company cash is not a dumped component
        /// (households keep money in Household.m_Resources, §3 — companies do
        /// not). Guess: DynamicBuffer&lt;Game.Economy.Resources&gt; { Resource
        /// m_Resource; int m_Amount } with the Money member holding cash.</summary>
        private static double Verify_CompanyMoney(EntityManager em, Entity company, double fallback)
        {
            if (em.HasBuffer<Game.Economy.Resources>(company))
            {
                var buf = em.GetBuffer<Game.Economy.Resources>(company, true);
                for (int i = 0; i < buf.Length; i++)
                    if (buf[i].m_Resource == Game.Economy.Resource.Money)
                        return buf[i].m_Amount;
            }
            return fallback;
        }

        /// <summary>// VERIFY-INGAME: transport mode of an outside connection
        /// is not in the dump (§3 lists OutsideConnectionData with NO fields).
        /// Guess: prefab carries Game.Prefabs.OutsideConnectionData with an
        /// enum field m_Type whose members include Road/Train/Ship/Air (map
        /// Train→Rail, Ship→Sea). Fallback: Road.</summary>
        private static ExitMode Verify_OutsideConnectionMode(EntityManager em, Entity connection)
        {
            if (em.HasComponent<Game.Prefabs.PrefabRef>(connection))
            {
                Entity prefab = em.GetComponentData<Game.Prefabs.PrefabRef>(connection).m_Prefab;
                if (prefab != Entity.Null && em.HasComponent<Game.Prefabs.OutsideConnectionData>(prefab))
                {
                    var d = em.GetComponentData<Game.Prefabs.OutsideConnectionData>(prefab);
                    if ((d.m_Type & Game.Prefabs.OutsideConnectionTransferType.Train) != 0) return ExitMode.Rail;
                    if ((d.m_Type & Game.Prefabs.OutsideConnectionTransferType.Ship) != 0) return ExitMode.Sea;
                    if ((d.m_Type & Game.Prefabs.OutsideConnectionTransferType.Air) != 0) return ExitMode.Air;
                }
            }
            return ExitMode.Road;
        }

        /// <summary>// VERIFY-INGAME: the natural-resource layer is NOT
        /// covered by the research notes (contract flags it explicitly).
        /// Guesses to check in the decompile: (a) Game.Simulation.
        /// NaturalResourceCell buffer element on a singleton map entity with
        /// { NaturalResourceAmount m_Fertility, m_Forest, m_Ore, m_Oil }, each
        /// { ushort m_Base, m_Used }; (b) the grid is square, side =
        /// sqrt(buffer length), centered on the map with world extent
        /// kMapExtent (confirm CellMapSystem's actual size). Fallback: flat
        /// 0.35 suitability so extractors stay viable pending verification.
        /// Mapping: fertility→Grain, forest→Wood, ore→Ore, oil→Oil — the
        /// spatial anchor of the Weber structure (ClusterInfo doc).</summary>
        private void Verify_ReadResourceSuitability(EntityManager em, WorldState w)
        {
            const float kMapExtent = 14336f;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Simulation.NaturalResourceCell>());
            if (q.IsEmptyIgnoreFilter)
            {
                foreach (var ci in w.Clusters)
                    for (int r = 0; r < ResourceCatalog.RawCount; r++)
                        ci.ResourceSuitability[r] = 0.35;
                return;
            }
            Entity mapEnt = q.GetSingletonEntity();
            var buf = em.GetBuffer<Game.Simulation.NaturalResourceCell>(mapEnt, true);
            int side = (int)Math.Round(Math.Sqrt(buf.Length));
            if (side < 2) return;
            float cell = kMapExtent / side, half = kMapExtent * 0.5f;
            int C = w.Clusters.Length;
            var sum = new double[C, ResourceCatalog.RawCount];
            var cnt = new int[C];
            for (int i = 0; i < buf.Length; i++)
            {
                float x = -half + (i % side + 0.5f) * cell;
                float z = -half + (i / side + 0.5f) * cell;
                int cl = _access.ClusterOf(x, z);
                var c0 = buf[i];
                sum[cl, (int)Res.Grain] += (c0.m_Fertility.m_Base - c0.m_Fertility.m_Used) / 65535.0;
                sum[cl, (int)Res.Wood] += (c0.m_Forest.m_Base - c0.m_Forest.m_Used) / 65535.0;
                sum[cl, (int)Res.Ore] += (c0.m_Ore.m_Base - c0.m_Ore.m_Used) / 65535.0;
                sum[cl, (int)Res.Oil] += (c0.m_Oil.m_Base - c0.m_Oil.m_Used) / 65535.0;
                cnt[cl]++;
            }
            // Max-normalize per raw across clusters → suitability ∈ [0,1].
            for (int r = 0; r < ResourceCatalog.RawCount; r++)
            {
                double max = 1e-9;
                for (int cl = 0; cl < C; cl++)
                    if (cnt[cl] > 0) max = Math.Max(max, sum[cl, r] / cnt[cl]);
                for (int cl = 0; cl < C; cl++)
                    w.Clusters[cl].ResourceSuitability[r] = cnt[cl] > 0 ? sum[cl, r] / cnt[cl] / max : 0;
            }
        }
    }
#endif
}
