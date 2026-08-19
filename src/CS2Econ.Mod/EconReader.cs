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

    /// <summary>The firm↔site link — Firm.Parcel on one side, Parcel
    /// .OccupantFirm on the other — as one small set of PAIRED mutations.
    /// Compiled in BOTH builds on purpose (the discipline EconWriter.EconAssess
    /// and EconSeams already follow): the game-facing halves of this file do not
    /// compile out-of-game, so anything that has to be testable has to live
    /// outside the #if. Every place the reader moves a firm on or off a site
    /// goes through here, so the invariant has exactly one implementation.
    ///
    /// THE INVARIANT, both directions:
    ///   (1) a LIVE firm never points at a site it does not hold —
    ///       !f.Dead ∧ f.Parcel ≥ 0  ⇒  Parcels[f.Parcel].OccupantFirm == f.Id
    ///   (2) a site never points at a firm that is not standing on it —
    ///       pl.OccupantFirm ≥ 0  ⇒  Firms[pl.OccupantFirm] is live ∧ its
    ///       Parcel is pl.Id
    ///
    /// WHY IT MUST HOLD ON THE READER SIDE SPECIFICALLY. Every firm loop in the
    /// engine is guarded by `if (f.Dead || f.Parcel &lt; 0) continue;` —
    /// Access.cs:467, EconomyEngine.cs:770/903/1021/1035/1110/1238/1256/1263/
    /// 1385/1481/1617/1673, LaborAuction.cs:317, Trade.cs:221, and the writer's
    /// own EconWriter.cs:244. That predicate is a SITE test standing in for a
    /// liveness test, so breaking (1) does not hide the firm — it does the
    /// opposite. A live firm still pointing at a demolished or re-let parcel
    /// keeps producing off that parcel's level and condition, keeps its slots in
    /// the labor market, keeps its money inside ledger conservation, and owes
    /// the levy on land it does not hold; and because the firm exit path sits
    /// INSIDE the loop guarded at EconomyEngine.cs:1673 (the exit itself is at
    /// :1782-1792), it reads the same stale site and can never die of running
    /// out of money. The failure is silent in the strongest sense: no loop skips
    /// it, so no count moves.
    ///
    /// Breaking (2) is quieter but real: a parcel naming a firm that stands
    /// elsewhere is neither vacant to the entry rule (EconomyEngine.cs:1798)
    /// nor a relocation candidate (EconomyEngine.cs:1868), so the site is
    /// withdrawn from the non-residential market with nothing on it.
    ///
    /// THE RULE THESE METHODS ENFORCE. Leaving a site always clears BOTH ends.
    /// Taking a site is REFUSED when a live firm already holds it, and the
    /// firm that loses the claim is left UNSITED (Parcel = −1) rather than
    /// half-linked. Unsited is a state FirmLifecycle's reconciliation pass sees
    /// and gives a stated outcome to (re-site, or exit with the residual
    /// booked); half-linked is the state nothing can see.</summary>
    public static class EconSiteLink
    {
        /// <summary>MUTANT SWITCH (harness `modsync --mutant-site-steal`):
        /// restores the pre-fix "last writer wins" claim — EconReader's AddFirm
        /// and SyncFirms both wrote `Parcels[pid].OccupantFirm = f.Id`
        /// unconditionally, so a second company resolving onto a site another
        /// firm already held silently left the incumbent pointing at a parcel
        /// that named someone else. Never a shipping mode.</summary>
        public static bool MutantSiteStealing;

        /// <summary>MUTANT SWITCH (harness `modsync --mutant-demolition-keeps-site`):
        /// restores the pre-fix demolition path — SyncParcels cleared the PARCEL
        /// end (`pl.OccupantFirm = -1`) and left the firm still pointing at the
        /// parcel it had just been evicted from. Never a shipping mode.</summary>
        public static bool MutantDemolitionKeepsFirmSite;

        /// <summary>Is this claim real? A parcel id on a firm, or a firm id on a
        /// parcel, is only meaningful while the other end agrees; an id that
        /// fails this test is a stale claim and may be taken over rather than
        /// respected. Total: out-of-range ids answer false instead of throwing,
        /// because the reader runs against a world the engine also appends
        /// to.</summary>
        private static bool HoldsSite(WorldState w, int firmId, int parcelId)
            => firmId >= 0 && firmId < w.Firms.Count
               && !w.Firms[firmId].Dead && w.Firms[firmId].Parcel == parcelId;

        /// <summary>The firm gives up whatever site it holds; both ends end
        /// consistent. Idempotent, and safe on a firm that holds nothing. The
        /// parcel end is cleared only when it actually names THIS firm — a
        /// parcel that names somebody else is that firm's claim, not ours to
        /// revoke.</summary>
        public static void Detach(WorldState w, Firm f)
        {
            int pid = f.Parcel;
            if (pid >= 0 && pid < w.Parcels.Count && w.Parcels[pid].OccupantFirm == f.Id)
                w.Parcels[pid].OccupantFirm = -1;
            f.Parcel = -1;
        }

        /// <summary>Put the firm on parcelId, or leave it UNSITED. Total on
        /// every input: parcelId &lt; 0 (or out of range) simply detaches, and a
        /// site a live firm already holds is refused. The caller learns which
        /// happened from the return value and must not assume the firm moved —
        /// that assumption is what the AddFirm hole was.</summary>
        public static bool Attach(WorldState w, Firm f, int parcelId)
        {
            Detach(w, f);
            if (parcelId < 0 || parcelId >= w.Parcels.Count) return false;
            var pl = w.Parcels[parcelId];
            // A claim whose firm is dead, gone, or standing somewhere else is
            // stale — taking it over REPAIRS invariant (2) rather than
            // violating it. Only a live firm actually standing here wins.
            if (!MutantSiteStealing && HoldsSite(w, pl.OccupantFirm, parcelId)) return false;
            pl.OccupantFirm = f.Id;
            f.Parcel = parcelId;
            return true;
        }

        /// <summary>The site loses its firm — demolition, despawn, any event
        /// that ends the tenancy from the PARCEL's side. Mirrors what
        /// SyncParcels already does for households (HomeParcel = −1 on every
        /// occupant, then the list cleared): the firm end is cleared too, so
        /// what is left behind is an unsited firm the reconciliation pass can
        /// act on rather than a firm reading a building that is gone.</summary>
        public static void ReleaseSite(WorldState w, Parcel pl)
        {
            int fid = pl.OccupantFirm;
            if (!MutantDemolitionKeepsFirmSite && fid >= 0 && fid < w.Firms.Count
                && w.Firms[fid].Parcel == pl.Id)
            {
                w.Firms[fid].Parcel = -1;
                // THE ONE PATH HERE THAT IS AN ECONOMIC EVENT. Of the four ways
                // this class leaves a firm unsited, three are the adapter
                // failing to place a company that is still standing in its
                // building — Attach refusing a contested claim, Attach on an
                // unresolved pid in AddFirm, the same in the SyncFirms re-link.
                // This one is different: the building is GONE. So this is the
                // only one that marks the firm as having lost a site it held,
                // which is what makes the engine's reconciliation pass resolve
                // it rather than leave it standing (Firm.SiteLostTick).
                w.Firms[fid].SiteLostTick = w.Tick;
            }
            pl.OccupantFirm = -1;
        }

        /// <summary>Count live violations of (1) and (2). Read-only; O(firms +
        /// parcels). The harness asserts on it (ModSiteLink), and
        /// EconBridgeSystem.StatusLine prints it during bring-up, where it is
        /// the only instrument the mod arm has.</summary>
        public static int Audit(WorldState w, out int firmsOnSitesTheyDoNotHold,
                                out int sitesNamingAbsentFirms)
        {
            firmsOnSitesTheyDoNotHold = 0;
            sitesNamingAbsentFirms = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                if (f.Parcel >= w.Parcels.Count || w.Parcels[f.Parcel].OccupantFirm != f.Id)
                    firmsOnSitesTheyDoNotHold++;
            }
            foreach (var pl in w.Parcels)
            {
                if (pl.OccupantFirm < 0) continue;
                if (!HoldsSite(w, pl.OccupantFirm, pl.Id)) sitesNamingAbsentFirms++;
            }
            return firmsOnSitesTheyDoNotHold + sitesNamingAbsentFirms;
        }
    }

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
            /// <summary>Highest Game.Citizens.Worker.m_Level held in the
            /// household — the JOB level, which is what CS2 pays for
            /// (m_Wage0..m_Wage4), not the citizen's education. Over-qualified
            /// citizens hold levels below their tier because FreeWorkplaces is
            /// per-tier and runs out; reading the level rather than assuming it
            /// is what makes our income spread match the game's.</summary>
            public int MaxJobLevel;
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
                bool worker = false; int jobLevel = 0;
                if (em.HasComponent<Game.Citizens.Worker>(e))                          // §3: m_Workplace, m_Level
                {
                    var wk = em.GetComponentData<Game.Citizens.Worker>(e);
                    worker = wk.m_Workplace != Entity.Null;
                    jobLevel = Verify_WorkerJobLevel(wk);
                }
                agg.TryGetValue(hh, out var a);
                if (age <= 1) a.Children++;
                else if (age == 2) { a.Adults++; if (student) a.AdultStudents++; }
                else a.Elderly++;
                if (worker) { a.Workers++; if (jobLevel > a.MaxJobLevel) a.MaxJobLevel = jobLevel; }
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
            // Children OR a second adult: see PromoteToFamily's doc — the
            // Family segments are the 2-adult archetypes, and a childless
            // couple left in a Single segment loses its second earner.
            return a.Children > 0 || a.Adults >= 2 ? ResourceMap.PromoteToFamily(seg) : seg;
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
                // Real earner count and job level, not a bool: these are the
                // two dispersion sources the income distribution is built on
                // (Income.cs). Earners is capped at the segment's modelled
                // adult count so a 3-earner household cannot out-earn the
                // distribution the price is walking.
                Earners = (byte)Math.Min(a.Workers, Math.Max(0, Segment.All[SegmentOf(a)].Adults)),
                Employed = a.Workers > 0,
                JobLevel = (byte)Math.Max(0, a.MaxJobLevel),
                ArrivedTick = w.Tick,
                TenureStart = w.Tick,
                MovingCostDraw = p.MovingCostMean * (0.4 + 1.2 * w.Rng.NextDouble()),
            };
            // Personal attributes, drawn once and keyed to the household id so
            // they survive a save/load round trip and a mid-session re-read.
            // VERIFY-INGAME: Game.Citizens.Citizen.m_PseudoRandom is the game's
            // OWN stable per-citizen idiosyncratic seed (research notes §3) and
            // is the better key here — swap it in once the field's accessor is
            // confirmed, so our taste draws line up with the game's own.
            h.DrawAtBirth(Segment.All[h.Segment], p);
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
                // Born UNSITED and sited below through EconSiteLink.Attach,
                // never here. Writing pid straight into the field was the older
                // shape and it had two ways to lie: pid < 0 (the vanilla
                // property did not resolve — a service building, a prefab
                // without SpawnableBuildingData, a building spawned since the
                // last sync) produced a live firm pointing at nothing that the
                // block below then never registered anywhere; and pid ≥ 0 onto a
                // site another firm already held produced two firms claiming one
                // parcel, with the incumbent left pointing at a parcel that
                // named the newcomer. Attach is total over both.
                Parcel = -1,
                Money = Verify_CompanyMoney(em, e, p.FirmSeedCapital),
                JobSlots = slots,
                EnteredTick = w.Tick,
            };
            if (em.HasComponent<Game.Companies.Employer>(e))                           // §3: m_Workers
                f.WorkersFilled = em.GetComponentData<Game.Companies.Employer>(e).m_Workers;
            if (em.HasComponent<Game.Companies.Profitability>(e))                      // §3: byte
                f.ProfitEma = ProfitabilityToFlow(
                    em.GetComponentData<Game.Companies.Profitability>(e).m_Profitability, slots);
            FirmIndex[e] = f.Id;
            FirmEntities.Add(e);
            FirmIds.Add(f.Id);
            // Registered BEFORE the claim: EconSiteLink checks the incumbent by
            // id against w.Firms, and a parcel is not allowed to name a firm the
            // list does not yet hold (invariant 2) even for one statement.
            w.Firms.Add(f);
            // A refused or absent claim leaves the firm unsited — the state
            // FirmLifecycle's reconciliation pass picks up and gives a stated
            // outcome (re-site, or exit with the residual booked). Units is the
            // assessment unit basis and is only raised where the claim landed.
            if (EconSiteLink.Attach(w, f, pid))
                w.Parcels[pid].Units = Math.Max(w.Parcels[pid].Units, slots);
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
                    // The company is gone: release the site from BOTH ends. The
                    // parcel end alone was enough while the firm was about to be
                    // skipped as Dead anyway, but a dead firm still naming a
                    // parcel is a claim nothing ever revokes — and the parcel it
                    // names may since have been re-let to a live firm.
                    EconSiteLink.Detach(w, f);
                    f.Dead = true;
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
                // ParcelIndex no longer holds entities that have despawned
                // (SyncParcels prunes them), so a company whose m_Property still
                // names a demolished building resolves to −1 here and is left
                // unsited rather than re-linked onto the emptied parcel.
                int newPid = -1;
                if (em.HasComponent<Game.Buildings.PropertyRenter>(e))
                {
                    var pr = em.GetComponentData<Game.Buildings.PropertyRenter>(e);
                    if (!ParcelIndex.TryGetValue(pr.m_Property, out newPid)) newPid = -1;
                }
                if (newPid != f.Parcel)
                {
                    // Attach detaches from the old site first, so the old
                    // parcel is never left naming a firm that has moved on, and
                    // refuses a site a live firm already holds — vanilla can
                    // report two companies in one building (relocation overlap,
                    // multi-tenant lots) and the engine's Parcel carries exactly
                    // one OccupantFirm, so one of the two must end unsited
                    // rather than both claiming it. A refusal costs at most one
                    // tick of lag and heals itself: the loser sits at
                    // Parcel = −1, so newPid != f.Parcel still holds next sync
                    // and it retries — by which time either the incumbent has
                    // left (and the claim lands) or FirmLifecycle's
                    // reconciliation pass has already given it an outcome.
                    if (EconSiteLink.Attach(w, f, newPid))
                        w.Parcels[newPid].Units = Math.Max(w.Parcels[newPid].Units, f.JobSlots);
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
                    // The entity is gone, so the entity→parcel mapping is a lie
                    // from here on. Drop it BEFORE anything else resolves
                    // through it: SyncFirms and SyncHouseholds both run after
                    // this pass in the same SyncTick and both re-link renters by
                    // looking up PropertyRenter.m_Property in ParcelIndex, so a
                    // company or household whose m_Property still names the
                    // demolished building would be put straight back onto the
                    // parcel this branch has just emptied — undoing the eviction
                    // below inside the same tick. The append-only ParcelEntities
                    // / ParcelIds pairing is untouched (the writer and the save
                    // seam walk those by slot), and a recycled Entity carries a
                    // new Version so it can never collide with the dropped key.
                    ParcelIndex.Remove(e);
                    if (pl.State != ParcelState.Empty)
                    {
                        // Demolished/despawned: evict; the engine's allocation
                        // machinery re-houses next tick.
                        foreach (int hid in pl.OccupantHouseholds) w.Households[hid].HomeParcel = -1;
                        pl.OccupantHouseholds.Clear();
                        // The firm end is released the same way the household
                        // end is, and for the same reason. Clearing only
                        // pl.OccupantFirm left the firm still pointing here:
                        // that firm is WORSE off than an unsited one, because
                        // `f.Dead || f.Parcel < 0` is what every engine loop
                        // guards on, so it stays fully visible — producing off a
                        // demolished building's level and condition, owing no
                        // levy on Units == 0, and unable to reach its own
                        // bankruptcy check, which sits inside that same guarded
                        // loop (EconomyEngine.cs:1673/1782). Unsited, it is a
                        // firm FirmLifecycle's reconciliation pass can see.
                        EconSiteLink.ReleaseSite(w, pl);
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

        /// <summary>// VERIFY-INGAME: Worker.m_Level is dump-listed as a byte
        /// (research notes §3) but its RANGE is decompile knowledge: it is read
        /// here as CS2's 0..4 job level, the index into
        /// EconomyParameterData.m_Wage0..m_Wage4. On the game machine confirm
        /// (a) the range is 0..4 and not a 0..100 progress counter, and (b) that
        /// the wage paid is indexed by THIS and not by citizen education — if it
        /// is education-indexed, feed Verify_CitizenEducation here instead and
        /// the distribution's job-level leg collapses to a point (the earner
        /// count and employment legs are unaffected either way).</summary>
        private static int Verify_WorkerJobLevel(Game.Citizens.Worker w)
            => Math.Max(0, Math.Min(4, (int)w.m_Level));

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
