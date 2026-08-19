// The lifecycle owner of the in-game economy (PLAN §5 stage 7): one
// GameSystemBase (managed systems verified as the modding surface, notes §1)
// that constructs reader / writer / access provider / engine, paces the engine
// against game frames, and owns the vanilla-system replacement seam.
//
// Replacement seam (notes §1, the sanctioned pattern proven by the shipped
// overhaul mods in §6): enumerate world.Systems empirically at OnCreate — never
// hardcode decompile guesses — log everything matching
// Demand|LandValue|Rent|Upkeep|Trade|Spawn, then set Enabled=false ONLY on the
// systems whose tier is live (EconSeams.DisableList below; every system name in
// it is §4-verified). In shadow mode (ShadowAccountingOnly, the load-time
// default) NOTHING is disabled and EconWriter never runs: the engine observes,
// predicts, and logs, and cannot corrupt a save (stage 3 of the wiring order).
//
// Tick cadence (the contract's "one engine tick per game day-slice"):
// OnUpdate runs once per simulation pass at Modification5 (registered in
// Mod.cs); every EngineTickFrames-th pass (EconModSettings, default 16) is one
// engine tick: reader.SyncTick → engine.Step() → writer.Apply. The engine's own
// calendar (EconParams.HurdleRate comment) treats 365 ticks ≈ one sim-year,
// i.e. one tick ≈ one sim-day slice. CS2's frames-per-sim-day constant is NOT
// notes-verified, so the exact frames↔day ratio is a bring-up measurement:
// watch StatusLine's tick counter over one in-game day and set EngineTickFrames
// so the engine advances ≈ 1 tick per day. The fast default deliberately
// over-samples in shadow mode — harmless when levying nothing, and it feeds
// the stage-3 predicted-vs-vanilla comparison more observations.
//
// Bring-up safety: any exception inside the tick loop is caught and logged;
// after MaxConsecutiveErrors failures the bridge re-enables every system it
// disabled and switches itself off — the save ends the session vanilla-coherent
// (notes §7 architecture: all authoritative state lives in vanilla components).

using System;
using CS2Econ.Core;
#if !OUT_OF_GAME_BUILD
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game;
using Unity.Entities;
using UnityEngine;
#endif

namespace CS2Econ.Mod
{
    /// <summary>The system-disable seam as DATA, compiled in both builds so the
    /// flag→system resolution is harness-testable. Each row pairs a tier tag
    /// with a regex over vanilla system type names — every name §4-verified.
    /// Two rows carry a sub-flag nuance, resolved per SYSTEM in VanillaDisabled:
    /// ZoneSpawnSystem rides the TierB row but gates on ConstructionRewire
    /// (site selection is the construction rewire, not the demand rewire), and
    /// BuildingUpkeepSystem rides the TierC row but gates on TierC2_Leveling
    /// (post-Economy-2.0 leveling is believed to live there — notes §4 note,
    /// confirmed by bring-up checklist item 2).</summary>
    public static class EconSeams
    {
        /// <summary>OnCreate boot-enumeration filter (notes §1: empirical
        /// enumeration beats guessing; the log is the arbiter of what the
        /// current game version actually runs).</summary>
        public const string EnumerationPattern = "Demand|LandValue|Rent|Upkeep|Trade|Spawn";

        public static readonly (string Flag, string Pattern)[] DisableList =
        {
            // TierB/construction: the three global-scalar demand systems + the
            // spawner they gate (§4: ZoneSpawnSystem "uses" all three demand
            // systems and reads Game.Net.LandValue — the site-selection seam).
            ("TierB", "ResidentialDemandSystem|CommercialDemandSystem|IndustrialDemandSystem|ZoneSpawnSystem"),
            // TierC: land value, rent setting/collection, upkeep+condition
            // (LandValueOverhaul precedent replaces exactly these, notes §6).
            ("TierC", "LandValueSystem|RentAdjustSystem|PropertyRenterSystem|BuildingUpkeepSystem"),
            // TierA: the hidden outside world — spawn/move-away (§4).
            ("TierA", "HouseholdSpawnSystem|HouseholdMoveAwaySystem"),
            // TierD: trade volume/price plumbing (§4).
            ("TierD", "TradeSystem|ResourceExporterSystem|ResourceBuyerSystem"),
        };

        /// <summary>Should this vanilla system be disabled under these flags?
        /// Pure and total: shadow mode disables nothing; unknown tags disable
        /// nothing. flag is the row tag from DisableList; systemName the full
        /// vanilla type name the row's regex matched.</summary>
        public static bool VanillaDisabled(FeatureFlags f, string flag, string systemName)
        {
            if (f.ShadowAccountingOnly) return false;      // observe-only: never touch vanilla
            switch (flag)
            {
                case "TierB":
                    return systemName.Contains("ZoneSpawnSystem") ? f.ConstructionRewire : f.TierB_Allocation;
                case "TierC":
                    return systemName.Contains("BuildingUpkeepSystem") ? f.TierC2_Leveling : f.TierC_LandAccounting;
                case "TierA": return f.TierA_Migration;
                case "TierD": return f.TierD_Trade;
                default: return false;
            }
        }
    }

#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked (same
    /// discipline as Mod.cs); the harness drives EconomyEngine directly and
    /// never needs a bridge. EconSeams above is the testable part.</summary>
    public sealed class EconBridgeSystem
    {
        /// <summary>Tooling access point (overlay/UI systems read the live
        /// engine through this). Always null out-of-game.</summary>
        public static EconomyEngine? ActiveEngine => null;

        public string StatusLine() => "[CS2Econ] EconBridgeSystem: out-of-game stub (dotnet build -p:InGame=true)";
    }
#else
    public sealed class EconBridgeSystem : GameSystemBase
    {
        /// <summary>ClusterAccessProvider bucket target. ~200 keeps the
        /// provider's all-pairs Dijkstra trivial (its own sizing comment) while
        /// giving districts distinct access geography.</summary>
        public const int DefaultTargetClusters = 192;
        /// <summary>Fallback cadence when settings are unavailable (see the
        /// header's cadence mapping; EconModSettings.EngineTickFrames rules).</summary>
        public const int DefaultEngineTickFrames = 16;

        private const int InitRetryFrames = 256;       // lazy-init probe interval (pre-city frames)
        private const int LiveCostRefreshTicks = 4;    // engine ticks between LaneFlow congestion refreshes
        private const int LogEveryTicks = 30;          // StatusLine cadence (~monthly at 1 tick/day)
        private const int MaxConsecutiveErrors = 5;

        /// <summary>Tooling access point: overlay/tooltip/UI systems and the
        /// developer console read the live engine (SegmentPresence, residuals,
        /// Ledger, calibration) through this. Null until a city initializes.</summary>
        public static EconomyEngine? ActiveEngine { get; private set; }

        private ClusterAccessProvider? _provider;
        private EconReader? _reader;
        private EconWriter? _writer;
        private WorldState? _world;
        private EconomyEngine? _engine;

        private EntityQuery _householdQuery;           // "is a city loaded yet" probe
        private long _frame;
        private int _flagSignature = -1;
        private int _consecutiveErrors;
        private readonly HashSet<ComponentSystemBase> _disabled = new HashSet<ComponentSystemBase>();
        private static readonly Regex[] RowRegex = BuildRowRegex();

        public EconomyEngine? Engine => _engine;
        public EconWriter? Writer => _writer;

        private static Regex[] BuildRowRegex()
        {
            var rows = EconSeams.DisableList;
            var r = new Regex[rows.Length];
            for (int i = 0; i < rows.Length; i++) r[i] = new Regex(rows[i].Pattern);
            return r;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _householdQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Citizens.Household>());  // §3
            EnumerateSeamSystems();
            ApplySystemDisables();       // shadow default: this disables nothing
            Debug.Log($"[CS2Econ] EconBridgeSystem created (shadow={Mod.Flags.ShadowAccountingOnly}, "
                      + $"cadence={TickCadenceFrames} frames/engine-tick)");
        }

        protected override void OnDestroy()
        {
            foreach (var sys in _disabled)
                if (sys != null) sys.Enabled = true;
            _disabled.Clear();
            if (ReferenceEquals(ActiveEngine, _engine)) ActiveEngine = null;
            base.OnDestroy();
        }

        /// <summary>Game frames per engine tick (see the header's cadence
        /// mapping). Live from settings so the Options slider takes effect
        /// without a reload.</summary>
        public int TickCadenceFrames => Math.Max(1, Mod.Settings?.EngineTickFrames ?? DefaultEngineTickFrames);

        protected override void OnUpdate()
        {
            _frame++;
            if (_engine == null)
            {
                if (_frame % InitRetryFrames == 0) TryInitialize();
                return;
            }
            if (_frame % TickCadenceFrames != 0) return;

            try
            {
                // Options screen → flags, live (the runbook's one-tier-per-
                // session flips happen here, no reload needed).
                Mod.Settings?.ApplyTo(Mod.Flags, Mod.Params);
                int sig = FlagSignature(Mod.Flags);
                if (sig != _flagSignature)
                {
                    _flagSignature = sig;
                    ApplySystemDisables();
                }

                // Access-cost upkeep: congestion refresh over fixed topology
                // every few ticks (LaneFlow-derived, provider's own contract);
                // a Dirty provider means the road TOPOLOGY churned enough that
                // the cluster set itself is stale — rebuild the whole stack
                // (cluster count changes; the engine's arrays are sized to it).
                if (_provider!.Dirty)
                {
                    Debug.LogWarning("[CS2Econ] road topology churned — rebuilding clusters + engine");
                    Reinitialize();
                    return;
                }
                if (_engine.W.Tick % LiveCostRefreshTicks == 0)
                    _provider.UpdateLiveCosts(EntityManager);

                // One engine tick. vanillaAuthoritative: while not levying
                // (shadow, or Tier C off) vanilla owns money/rents/condition
                // and the reader mirrors them; once levying, the engine owns
                // them and the reader only pulls existence/employment/topology.
                _reader!.SyncTick(EntityManager, _world!, Mod.Params, vanillaAuthoritative: !_engine.Levying);
                _engine.Step();

                // Writer runs ONLY outside shadow mode: observe-only cannot
                // touch a component, so it cannot corrupt a save (notes §1
                // replacement pattern, stage-3 discipline).
                if (!Mod.Flags.ShadowAccountingOnly)
                {
                    _writer!.Apply(EntityManager, _world!, _engine);
                    // Save seam: stamp mod-native state onto entities so it
                    // rides whatever save the player makes next (same shadow
                    // gate — observe-only leaves zero footprint in a save).
                    if (_engine.W.Tick % EconStatePersistence.CaptureEveryTicks == 0)
                        EconStatePersistence.Capture(EntityManager, _reader!, _world!);
                }

                if (_engine.W.Tick % LogEveryTicks == 0) Debug.Log(StatusLine());
                _consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                Debug.LogError($"[CS2Econ] engine tick failed ({_consecutiveErrors}/{MaxConsecutiveErrors}): {ex}");
                if (_consecutiveErrors >= MaxConsecutiveErrors)
                {
                    // Fail vanilla-coherent: hand every seam back and stop.
                    foreach (var sys in _disabled)
                        if (sys != null) sys.Enabled = true;
                    _disabled.Clear();
                    Enabled = false;
                    Debug.LogError("[CS2Econ] bridge disabled after repeated failures; vanilla systems re-enabled");
                }
            }
        }

        /// <summary>One-line health readout: engine tick, live population and
        /// firm counts, and drift = mean distance of the §4.6 calibration
        /// correction factors from 1.0 (the measured realized-vs-predicted
        /// gap), plus writer activity. Logged every LogEveryTicks; also the
        /// developer-console readout during bring-up.</summary>
        public string StatusLine()
        {
            var e = _engine;
            if (e == null) return "[CS2Econ] engine not initialized (waiting for a loaded city)";
            int pop = 0;
            foreach (var h in e.W.Households) if (h.ExitedTick < 0) pop++;
            int firms = 0;
            foreach (var f in e.W.Firms) if (!f.Dead) firms++;
            double drift = 0; int n = 0;
            foreach (var kv in e.W.Calibration.ByUse) { drift += Math.Abs(kv.Value.Factor - 1.0); n++; }
            if (n > 0) drift /= n;
            // Site-link audit (EconSiteLink): live firms pointing at a site they
            // do not hold, and sites naming a firm that is not standing on them.
            // BOTH MUST READ ZERO — a non-zero left leg is the silent mode this
            // instrument exists for (the firm stays inside every engine loop,
            // including the one its own exit check sits in). This is telemetry,
            // not a check: the mod arm has no in-game test to fail, so the
            // bring-up log is where the invariant is observed. The out-of-game
            // check that CAN fail is the harness `modsync` fixture.
            int siteDrift = EconSiteLink.Audit(e.W, out int badFirms, out int badSites);
            var wtr = _writer;
            return $"[CS2Econ] tick={e.W.Tick} pop={pop} firms={firms} drift={drift:F3} "
                 + (siteDrift > 0 ? $"SITELINK[firms={badFirms} sites={badSites}] " : "sitelink=ok ")
                 + $"landRev={e.LandRevenueThisTick:F1} shelter={e.ShelterOccupied}/{e.ShelterCapacity} "
                 + $"shadow={Mod.Flags.ShadowAccountingOnly} clusters={e.Costs.ClusterCount} costsV={e.Costs.Version} "
                 + $"writes[rent={wtr?.RentWrites ?? 0} cond={wtr?.ConditionWrites ?? 0} "
                 + $"lv={wtr?.LandValueEdgeWrites ?? 0} swaps={wtr?.LevelSwapsApplied ?? 0} "
                 + $"constrQ={wtr?.PendingConstruction.Count ?? 0}]";
        }

        /// <summary>Drop the whole stack and lazily rebuild on a later frame
        /// (fresh clusters → fresh WorldState via EconReader.BuildInitial,
        /// which also restores mod-native EconSerialization state at the load
        /// seam). Used on topology churn and available to tooling.
        ///
        /// Vanilla systems are handed BACK for the rebuild window: init can
        /// take many frames (or fail retrying), and a city with neither
        /// vanilla nor engine economy running would drift unrecoverably.
        /// _flagSignature resets so the first post-init OnUpdate re-applies
        /// the disables that the current flags call for.</summary>
        public void Reinitialize()
        {
            foreach (var sys in _disabled)
                if (sys != null) sys.Enabled = true;
            _disabled.Clear();
            _flagSignature = -1;
            if (ReferenceEquals(ActiveEngine, _engine)) ActiveEngine = null;
            _engine = null; _world = null; _reader = null; _writer = null; _provider = null;
        }

        // ------------------------------------------------------------------
        private void TryInitialize()
        {
            if (_householdQuery.IsEmptyIgnoreFilter) return;   // no city loaded yet
            try
            {
                var provider = new ClusterAccessProvider(DefaultTargetClusters);
                provider.RebuildClusters(EntityManager);
                if (provider.ClusterCount == 0) return;        // road network not deserialized yet

                var reader = new EconReader(provider);
                var world = reader.BuildInitial(EntityManager, Mod.Params);
                if (world.Parcels.Count == 0 && world.Households.Count == 0)
                {
                    Debug.Log("[CS2Econ] init deferred: no parcels/households visible yet");
                    return;
                }

                _provider = provider;
                _reader = reader;
                _world = world;
                _writer = new EconWriter(reader, provider);
                _engine = new EconomyEngine(world, provider, Mod.Params, Mod.Flags);
                ActiveEngine = _engine;
                // Apply disables HERE, not via the signature check: setting
                // _flagSignature below makes OnUpdate see "no change", so a
                // Reinitialize that handed systems back would otherwise leave
                // vanilla running alongside a levying engine.
                ApplySystemDisables();
                _flagSignature = FlagSignature(Mod.Flags);
                Debug.Log($"[CS2Econ] engine initialized: clusters={provider.ClusterCount} "
                          + $"parcels={world.Parcels.Count} households={world.Households.Count} "
                          + $"firms={world.Firms.Count} exits={world.Exits.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CS2Econ] initialization failed (will retry): " + ex);
            }
        }

        /// <summary>Boot-time empirical enumeration (notes §1): log every
        /// vanilla system matching the seam filter WITH its enabled state.
        /// Bring-up checklist item 2 reads this log before trusting any
        /// disable call — the log, not the dump, is the arbiter of what the
        /// current game version runs.</summary>
        private void EnumerateSeamSystems()
        {
            var filter = new Regex(EconSeams.EnumerationPattern);
            int n = 0;
            foreach (var sys in World.Systems)
            {
                if (sys == null) continue;
                string name = sys.GetType().FullName ?? string.Empty;
                if (!name.StartsWith("Game.", StringComparison.Ordinal)) continue;  // vanilla only
                if (!filter.IsMatch(name)) continue;
                Debug.Log($"[CS2Econ] seam candidate: {name} enabled={sys.Enabled}");
                n++;
            }
            Debug.Log($"[CS2Econ] seam enumeration: {n} systems matched '{EconSeams.EnumerationPattern}'");
        }

        /// <summary>Reconcile vanilla systems with the current flags: disable
        /// what a live tier replaces, re-enable what a reverted tier hands
        /// back. Only systems THIS bridge disabled are ever re-enabled (never
        /// undo another mod's work); Mod.OnDispose keeps its broader
        /// filter-based sweep as the last-resort cleanup.</summary>
        private void ApplySystemDisables()
        {
            var rows = EconSeams.DisableList;
            foreach (var sys in World.Systems)
            {
                if (sys == null) continue;
                string name = sys.GetType().FullName ?? string.Empty;
                if (!name.StartsWith("Game.", StringComparison.Ordinal)) continue;  // vanilla only, never our own
                for (int i = 0; i < rows.Length; i++)
                {
                    if (!RowRegex[i].IsMatch(name)) continue;
                    bool wantDisabled = EconSeams.VanillaDisabled(Mod.Flags, rows[i].Flag, name);
                    if (wantDisabled && sys.Enabled)
                    {
                        sys.Enabled = false;
                        _disabled.Add(sys);
                        Debug.Log($"[CS2Econ] disabled {name} ({rows[i].Flag} live)");
                    }
                    else if (!wantDisabled && !sys.Enabled && _disabled.Contains(sys))
                    {
                        sys.Enabled = true;
                        _disabled.Remove(sys);
                        Debug.Log($"[CS2Econ] re-enabled {name} ({rows[i].Flag} reverted)");
                    }
                    break;      // first matching row owns the system
                }
            }
        }

        private static int FlagSignature(FeatureFlags f)
            => (f.ShadowAccountingOnly ? 1 : 0) | (f.TierA_Migration ? 2 : 0)
             | (f.TierB_Allocation ? 4 : 0) | (f.TierC_LandAccounting ? 8 : 0)
             | (f.TierC2_Leveling ? 16 : 0) | (f.TierD_Trade ? 32 : 0)
             | (f.ConstructionRewire ? 64 : 0);
    }
#endif
}
