using System;
using System.Collections.Generic;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>One simulation run: synthetic city + engine (+ vanilla spawner in
    /// baseline mode) + telemetry series the scenarios assert against.</summary>
    public sealed class Sim
    {
        public WorldState W = null!;
        public GridAccess Access = null!;
        public EconomyEngine Engine = null!;
        public EconParams P = null!;
        public FeatureFlags Flags = null!;
        public VanillaSpawner? Vanilla;

        // Telemetry series (per tick)
        public readonly List<int> Population = new List<int>();
        public readonly List<double> Treasury = new List<double>();
        public readonly List<double> LandRevenue = new List<double>();
        public readonly List<int> Sheltered = new List<int>();
        public readonly List<(long tick, int parcel, int cluster)> Starts = new List<(long, int, int)>();

        private int _lastStartedTotal;

        /// <summary>Set by `--store-level` on any harness command: turns on
        /// FeatureFlags.StoreLevelSpending for every sim this process builds, so
        /// the experimental path can be measured without editing a default.</summary>
        public static bool ForceStoreLevelSpending;
        /// <summary>Set by `--auction`: solve housing as one assignment market.</summary>
        public static bool ForceHousingAuction;
        /// <summary>Set by `--posted`: force the posted/Poisson path even though
        /// the auction path is now the default. The posted curve must stay
        /// exercisable from the CLI for as long as it ships at all — the flip
        /// inventory measured that without this switch NOTHING in the harness
        /// could reach it (20/24 fixtures solve the auction under the new
        /// default). Applied after --auction so an explicit --posted wins.</summary>
        public static bool ForcePosted;
        /// <summary>Set by `--nonres-parity`: turns on EconParams.NonResLandParity
        /// for every sim this process builds, so the task-#19 arm can be
        /// measured without editing a default that ships off.</summary>
        public static bool ForceNonResLandParity;
        /// <summary>Set by `--pooled`: force the pooled consumption path even
        /// where a fixture or a future default asks for store-level spending.
        /// The mirror of `--posted`, and it exists for the same measured
        /// reason: the flip inventory found that without an explicit switch
        /// NOTHING in the harness could reach the non-default path. Applied
        /// after `--store-level` so an explicit `--pooled` wins.</summary>
        public static bool ForcePooled;
        /// <summary>Set by `--exit-margin`: turns on FeatureFlags.FirmExitMargin
        /// for every sim this process builds, so the arm can be measured
        /// without editing a default that ships off (and must ship off until
        /// the nonres-parity arrears FLOOR leg, whose population a cash-flow
        /// exit kills, has been rewritten).</summary>
        public static bool ForceFirmExitMargin;
        /// <summary>Set by `--office-kinds`: turns on
        /// FeatureFlags.OfficeSpecializations so the arm can be measured
        /// without editing a default that ships off.</summary>
        public static bool ForceOfficeSpecializations;
        /// <summary>Set by `--retail-lines`: turns on
        /// FeatureFlags.CommercialLines so the arm can be measured without
        /// editing a default that ships off.</summary>
        public static bool ForceCommercialLines;
        /// <summary>Set by `--labor`: solve labor as one assignment market.
        /// The arm ships OFF (FeatureFlags.LaborAuction = false) and until this
        /// existed was reachable only from three hard-coded fixtures, so the
        /// sector-viability question could not be asked of it from the CLI at
        /// all. It is the FIRST item in task #57's fix ordering: on the default
        /// path AssignWorkplaces fills slots by commute softmax and the firm
        /// pays class wages whatever its product, while the auction's door cap
        /// refuses a hire above the firm's own marginal-revenue forecast.</summary>
        public static bool ForceLaborAuction;
        /// <summary>Set by `--fill-evidence`: turns on
        /// EconParams.FillEvidenceWeighting so the arm can be measured without
        /// editing a default that ships off.</summary>
        public static bool ForceFillEvidence;
        /// <summary>Set by `--repair-rounds N`: raises the housing auction's
        /// repair budget (EconParams.AuctionRepairRounds, default 20) for every
        /// sim this process builds. Not a model knob — a SOLVER budget, and it
        /// exists so "the market did not converge" can be told apart from "the
        /// allocation is not an equilibrium". The labor auction already carries
        /// 64 for the same contract; housing has never had a way to ask.</summary>
        public static int ForceRepairRounds = -1;

        public static Sim Create(SyntheticCity.Config cfg, EconParams p, FeatureFlags flags, bool vanillaMode = false)
        {
            if (ForceStoreLevelSpending) flags.StoreLevelSpending = true;
            if (ForcePooled) flags.StoreLevelSpending = false;
            if (ForceHousingAuction) flags.HousingAuction = true;
            if (ForcePosted) flags.HousingAuction = false;
            if (ForceNonResLandParity) p.NonResLandParity = true;
            if (ForceFirmExitMargin) flags.FirmExitMargin = true;
            if (ForceOfficeSpecializations) flags.OfficeSpecializations = true;
            if (ForceCommercialLines) flags.CommercialLines = true;
            if (ForceLaborAuction) flags.LaborAuction = true;
            if (ForceFillEvidence) p.FillEvidenceWeighting = true;
            if (ForceRepairRounds >= 0) p.AuctionRepairRounds = ForceRepairRounds;
            var sim = new Sim { P = p, Flags = flags };
            (sim.W, sim.Access) = SyntheticCity.Build(cfg, p);
            sim.Engine = new EconomyEngine(sim.W, sim.Access, p, flags);
            if (vanillaMode)
            {
                flags.ConstructionRewire = false;
                flags.TierC2_Leveling = false;
                // Vanilla mode means the VANILLA market. With the auction now
                // the process default, inheriting it here would run vanilla
                // spawner + assignment auction — a hybrid nothing ships — and
                // every scenario's "vs vanilla" baseline would quietly stop
                // being vanilla (measured at the flip round: the levels
                // scenario's vanilla arm read −0.16 through the auction).
                flags.HousingAuction = false;
                // Same reason, same measured precedent: vanilla means the
                // VANILLA market on BOTH sides. If StoreLevelSpending ever
                // becomes the default, inheriting it here would run the vanilla
                // spawner against a discrete shop market and every scenario's
                // "vs vanilla" arm would quietly stop being vanilla. Pinned now,
                // while it is a no-op, so the flip cannot forget it — the
                // housing flip hit exactly this defect and it is recorded.
                flags.StoreLevelSpending = false;
                sim.Vanilla = new VanillaSpawner();
            }
            return sim;
        }

        public void Run(int ticks, Action<Sim>? perTick = null)
        {
            for (int t = 0; t < ticks; t++) Step(perTick);
        }

        public void Step(Action<Sim>? perTick = null)
        {
            // Record construction starts with their location (for localization tests).
            var beforeStates = _pendingStartScan ? CaptureUnderConstruction() : null;

            Engine.Step();
            Vanilla?.Step(W, P);

            if (_pendingStartScan && beforeStates != null)
            {
                foreach (var pl in W.Parcels)
                    if (pl.State == ParcelState.UnderConstruction && !beforeStates.Contains(pl.Id)
                        && pl.CommittedCost > 0)   // developer/spawner commits; scrapes are §4.4 events
                        Starts.Add((W.Tick, pl.Id, pl.Cluster));
            }

            int pop = 0, shel = 0;
            foreach (var h in W.Households)
                if (h.ExitedTick < 0) { pop++; if (h.Stage == InsolvencyStage.Sheltered) shel++; }
            Population.Add(pop);
            Sheltered.Add(shel);
            Treasury.Add(W.Ledger.Balance(Account.Treasury));
            LandRevenue.Add(Engine.LandRevenueThisTick);
            _lastStartedTotal = Engine.Construction.StartedTotal;
            perTick?.Invoke(this);
        }

        private bool _pendingStartScan = true;
        private HashSet<int> CaptureUnderConstruction()
        {
            var s = new HashSet<int>();
            foreach (var pl in W.Parcels)
                if (pl.State == ParcelState.UnderConstruction) s.Add(pl.Id);
            return s;
        }

        public ulong TelemetryHash()
        {
            ulong h = 1469598103934665603UL;
            void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
            foreach (var hh in W.Households)
            {
                Mix((ulong)hh.HomeParcel + 7);
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(hh.Money, 6)));
            }
            foreach (var pl in W.Parcels)
            {
                Mix((ulong)pl.Level); Mix((ulong)pl.State);
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(pl.Escrow, 6)));
                // AssessedLR joined this hash after an adversarial round found a
                // change that moved land assessment citywide by -4.2 % with the
                // whole suite silent. Escrow is a FILTERED view of assessment
                // (only the earmarked wedge, only where levying is on), so it
                // does not stand in for the tax base: MUT-C below (AssessedLR ×
                // 0.958) moved ΣLR visibly while every hash in the suite
                // covered it only through that filter. Rounded to 6 dp on the
                // same rationale as the other terms — the last bits of a
                // Math.Pow chain are not a model change.
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(pl.AssessedLR, 6)));
            }
            Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(W.Ledger.Balance(Account.Treasury), 6)));
            return h;
        }
    }
}
