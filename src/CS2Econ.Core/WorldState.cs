using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Static per-cluster attributes plus slowly varying local fields.
    /// Clusters are the spatial unit of every economic computation (nested-
    /// dissection cells in-game; grid cells in the harness).</summary>
    public sealed class ClusterInfo
    {
        public int Id;
        public double X, Y;
        public int District;
        public double Amenity;        // parks, waterfront, ... (exogenous + service-driven)
        public double School;         // school seats access mass
        public double Health;         // healthcare access mass
        public double Pollution;      // industrial ground/air pollution (updated by engine)
        public double Noise;
        /// <summary>Natural-resource suitability per raw (indexed by Res 0..3):
        /// extraction output scales with it; zero elsewhere. The spatial anchor
        /// of the whole Weber structure.</summary>
        public double[] ResourceSuitability = new double[ResourceCatalog.RawCount];
        public bool WedgeEarmark = true; // per-district: wedge → escrow vs general revenue
    }

    public enum ParcelState : byte { Empty, UnderConstruction, Built }

    /// <summary>One buildable lot. Building state is flattened in (no separate
    /// Building class): a parcel is the unit of assessment, escrow, and
    /// redevelopment (design §4.3–4.4).</summary>
    public sealed class Parcel
    {
        public int Id;
        public int Cluster;
        public ZoneKind Zoned;          // what the player zoned (permitted configurations)
        public ParcelState State;

        // Built state
        public ZoneKind Use;            // actual current use (== Zoned unless legacy)
        public int Level;               // discrete 1–5 (vanilla interop, design §3)
        public double Condition;        // 0..1; V = Condition × RC
        public int Units;               // households or firm job slots the structure holds
        public bool OwnerOccupied;      // owner tag (§4.4): consent gate on redevelopment
        public bool Warehousing;        // scrape pending: vacated units stop re-letting

        // Land accounting (assessed, never from own realized rent — §3 circularity guard)
        public double AssessedLR;       // best-permitted-use land flow per tick (all units)
        public double CurrentResidual;  // Bid_current − S_current (all units)
        public double Wedge;            // AssessedLR − CurrentResidual, ≥ 0
        public double Escrow;           // upgrade escrow balance (earmarked wedge)
        public int TargetLevel;         // ℓ* of the winning configuration
        public ZoneKind TargetUse;      // use of the winning configuration
        public bool TargetIsScrape;     // winning conversion needs physical vacancy

        public int ScrapePressure;      // consecutive ticks the scrape gap has been sustained

        // Construction pipeline
        public int BuildProgress;       // ticks completed
        public int BuildTotal;          // == ConstructionLag
        public double CommittedCost;    // remaining spend at start (milestone re-eval basis)
        public double PredictedRentAtDecision;   // calibration loop inputs (§4.6)
        public double PredictedAbsorptionAtDecision;
        public long CompletedTick = -1;

        public List<int> OccupantHouseholds = new List<int>();
        public int OccupantFirm = -1;

        // Per-tick structure-charge bookkeeping (drives condition decay)
        public double PaidTickS, OwedTickS;

        public int Vacant => State == ParcelState.Built && Use != ZoneKind.Commercial
                             && Use != ZoneKind.Industrial && Use != ZoneKind.Office && Use != ZoneKind.Extractor
                             ? Math.Max(0, Units - OccupantHouseholds.Count) : 0;
        public bool IsResidential => Use == ZoneKind.ResidentialLow || Use == ZoneKind.ResidentialHigh;
    }

    public enum InsolvencyStage : byte { Solvent, CutConsumption, SortDown, Emigrate, Sheltered }

    public sealed class Household
    {
        public int Id;
        public int Segment;
        public double Money;
        public int HomeParcel = -1;      // -1 = unhoused (arriving or sheltered)
        /// <summary>True iff at least one adult holds a job (Earners > 0).
        /// Kept for the many read sites that only ask "does anyone here work";
        /// income uses Earners and JobLevel.</summary>
        public bool Employed;
        /// <summary>How many of this household's adults hold a job. From the
        /// game: the count of Game.Citizens.Worker members with a workplace
        /// (EconReader.HhAgg.Workers). A two-earner family out-earns a
        /// one-earner family of identical education — the largest single
        /// source of within-segment income spread.</summary>
        public byte Earners;
        /// <summary>The job LEVEL this household's earners hold (CS2's
        /// Game.Citizens.Worker.m_Level, which is the job taken — not the
        /// citizen's education: over-qualification is normal when the matching
        /// tier's FreeWorkplaces run out). Stable per household; drawn from the
        /// segment's job-level distribution (Income.JobLevels).</summary>
        public byte JobLevel;
        /// <summary>Consecutive ticks with no earner, for a household that has
        /// working-age adults. CS2 pays the unemployment benefit only up to
        /// `Unemployment Allowance Max Days`, after which the household has no
        /// support and leaves; this is the counter that expiry reads.</summary>
        public int UnemployedTicks;
        /// <summary>The parcel this household's earners work at — CS2's
        /// Game.Citizens.Worker.m_Workplace, which the adapter already reads
        /// and used to discard. An individual holds a specific job at a
        /// specific firm; that link is what makes a worker collective's
        /// surplus payable to its own members, and what turns job placement
        /// into a decision an individual makes rather than a rate it is
        /// sampled from. -1 = not working anywhere.</summary>
        public int WorkplaceParcel = -1;

        // ---- personal attributes, DRAWN AT BIRTH ---------------------------
        // The only place a distribution legitimately enters: an individual is
        // handed its own preferences once, at spawn, and thereafter acts on
        // them. Everything downstream — the demand curve, the clearing price,
        // the observed income spread — must EMERGE from individuals holding
        // these, never from a distribution recomputed and walked in aggregate.
        /// <summary>This household's own share of income it will bid for
        /// housing, drawn around its segment's archetype. Two families of the
        /// same segment genuinely differ in how much rent they will carry.</summary>
        public double RentShare;
        /// <summary>This household's own tolerance for high density, drawn
        /// around its segment's archetype (0..1).</summary>
        public double DensityTol;

        /// <summary>Draw this household's personal attributes once, at spawn.
        /// Deterministic in the household id so a run is reproducible and an
        /// attribute never silently re-rolls: re-drawing per tick would make
        /// the "individual" a sampling artifact of a distribution, which is
        /// exactly the aggregate-first modelling this architecture rejects.</summary>
        public void DrawAtBirth(CS2Econ.Core.Segment seg)
        {
            double u = SplitMix64.Hash01((ulong)Id * 2654435761UL + 11UL);
            double v = SplitMix64.Hash01((ulong)Id * 2654435761UL + 29UL);
            // Symmetric ±25% spread on the willingness to commit income, and
            // ±0.2 on density tolerance — heterogeneity a segment mean hides.
            RentShare = seg.MaxRentShare * (0.75 + 0.5 * u);
            DensityTol = MathUtil.Clamp(seg.DensityTolerance + 0.4 * (v - 0.5), 0, 1);
        }

        /// <summary>How much this household values a unit of the given density
        /// — its OWN tolerance, not its segment's. Same floor rationale as
        /// Segment.DensityAppeal (preference, never permission).</summary>
        public double DensityAppeal(ZoneKind kind)
            => kind == ZoneKind.ResidentialHigh
                ? CS2Econ.Core.Segment.DensityFloor + (1 - CS2Econ.Core.Segment.DensityFloor) * MathUtil.Clamp(DensityTol, 0, 1)
                : 1.0;
        public double ChargedAssessment; // per-tick S+tax+wedge currently being charged
        public double MovingCostDraw;    // within-segment heterogeneity draw (§4.4)
        public long TenureStart;
        public int StressTicks;          // consecutive ticks payment > income capacity
        public InsolvencyStage Stage;
        public long ArrivedTick;
        public long ExitedTick = -1;     // set on emigration/displacement (telemetry)
    }

    public sealed class Firm
    {
        public int Id;
        public ZoneKind Sector;          // Commercial | Industrial | Office | Extractor
        /// <summary>What this firm produces: extractor → its cluster's raw;
        /// industrial → its chosen recipe's output (the Weber decision, fixed at
        /// entry); commercial → Services; office → OfficeOutput.</summary>
        public Res Output = Res.Services;
        public int Parcel = -1;
        public double Money;
        public int JobSlots;
        public double WorkersFilled;     // fractional fill from IPF flows
        public double[] FilledByClass = new double[3];
        public double ProfitEma;
        public long EnteredTick;
        public bool Dead;
        /// <summary>The households that actually work here. A firm is a WORKER
        /// COLLECTIVE: its surplus above a working-capital reserve is paid out
        /// to these members and to nobody else — not pooled, not spread across
        /// the city. Rebuilt each refresh from individual job placements
        /// (Household.WorkplaceParcel), so it is a summary of real people, not
        /// an allocation rule.</summary>
        public readonly List<int> Members = new List<int>();
        /// <summary>Cumulative surplus this firm has distributed to its own
        /// members — telemetry for the circular flow.</summary>
        public double DividendsPaid;

        // Per-tick scratch (settlement + telemetry)
        public double RevenueThisTick;
        public double OutputThisTick;
        public double[] InputNeedByRes = new double[ResourceCatalog.Count];
    }

    /// <summary>One outside connection with its own supply/demand law
    /// p(Q) = anchor ± t·(Q/ρ)^(1/d) (design §4.5).</summary>
    public sealed class TradeExit
    {
        public int Id;
        public ExitMode Mode;
        public int Cluster;              // where haulage is routed to
        public Res Resource;             // one law per (resource × exit)
        public double Anchor;            // world price at the fundamental
        public double T;                 // transport-intensity slope
        public double Rho;               // depth-relevant density (region-size knob)
        public double D;                 // catchment dimension: road 2, rail 1, sea/air ∞
        public double PerUnitHandling;   // rail terminal handling; 0 for road
        public double Capacity;          // per tick; sea/air are capacity-capped
        public int RegionGroup = -1;     // exits sharing a group share their sustained scalar
        public double SustainedQ;        // EMA of drawn volume (the slow position)
        public double TransientB;        // burst layer, decays at resilience rate
        public double DrawnThisTick;
        public double ExportedThisTick, ImportedThisTick;   // direction split (telemetry)

        // Pricing lives in TradeSystem (group coupling, net positions) — no
        // per-exit marginal methods here (a diverging duplicate was scrutiny
        // finding #11).
    }

    /// <summary>Tier A state: the weakly endogenous outside world (design §4.1).</summary>
    public sealed class MigrationState
    {
        public double[] ReservationThreshold = new double[Segment.Count]; // rises with cumulative net inflow
        public double[] OutSignalEma = new double[Segment.Count];         // lagged out-migration signal
        public double NetworkMemory;                                      // chain-migration stock
        public double CumulativeNetInflow;
        public double[] SegmentAttractEma = new double[Segment.Count];    // telemetry
    }

    /// <summary>Claims ledger (§4.6): units committed but not yet delivered, per
    /// (cluster, use). Later deciders see the pipeline, not the mirage.</summary>
    public sealed class ClaimsLedger
    {
        private readonly Dictionary<(int cluster, ZoneKind use), double> _claims
            = new Dictionary<(int, ZoneKind), double>();

        /// <summary>Raw claim booked AT a cluster (the site's own pipeline).</summary>
        public double Get(int cluster, ZoneKind use) => _claims.TryGetValue((cluster, use), out var v) ? v : 0;

        /// <summary>Every (cluster, use) carrying a live claim — the emitter set
        /// ResidualDemand smears through the vacancy kernel.</summary>
        public IEnumerable<KeyValuePair<(int cluster, ZoneKind use), double>> Entries => _claims;

        /// <summary>Bumped on every mutation so the smeared-claim field can
        /// invalidate: commits inside a refresh window MUST be visible to later
        /// deciders in the same window (§4.6 pipeline-not-mirage).</summary>
        public int Version { get; private set; }

        public void Add(int cluster, ZoneKind use, double units)
        {
            _claims.TryGetValue((cluster, use), out var v);
            _claims[(cluster, use)] = Math.Max(0, v + units);
            Version++;
        }
    }

    /// <summary>Calibration loop state (§4.6): realized-vs-predicted at decision
    /// time, folded back as a shrunk multiplicative correction. Target is
    /// measured, mean-reverting drift — not zero drift.</summary>
    public sealed class CalibrationState
    {
        public sealed class PerUse { public double SumRatio; public double N; public double Factor = 1.0; }
        public readonly Dictionary<ZoneKind, PerUse> ByUse = new Dictionary<ZoneKind, PerUse>();

        public double Factor(ZoneKind use) => ByUse.TryGetValue(use, out var s) ? s.Factor : 1.0;

        public void Observe(ZoneKind use, double realizedOverPredicted, double shrinkN0)
        {
            if (!ByUse.TryGetValue(use, out var s)) ByUse[use] = s = new PerUse();
            s.SumRatio += MathUtil.Clamp(realizedOverPredicted, 0.1, 4.0);
            s.N += 1;
            double mean = s.SumRatio / s.N;
            double w = s.N / (s.N + shrinkN0);          // shrink toward 1.0 on few observations
            s.Factor = MathUtil.Clamp(1.0 + w * (mean - 1.0), 0.4, 2.5);
        }
    }

    /// <summary>The whole simulated economy. Built by an IParcelWorld adapter
    /// (harness: SyntheticCity; game: ECS readers) and stepped by EconomyEngine.</summary>
    public sealed class WorldState
    {
        /// <summary>Meters per ClusterInfo.X/Y coordinate unit, set by the
        /// world builder: 1.0 in-game (CS2 world units are meters), ~700 for
        /// the synthetic grid (neighborhood spacing), 0.3048 for TNTP imports
        /// in state-plane feet. The vacancy kernel's λ is in real meters.</summary>
        public double MetersPerUnit = 1.0;
        public ClusterInfo[] Clusters = Array.Empty<ClusterInfo>();
        public List<Parcel> Parcels = new List<Parcel>();
        public List<Household> Households = new List<Household>();
        public List<Firm> Firms = new List<Firm>();
        public List<TradeExit> Exits = new List<TradeExit>();
        public MigrationState Migration = new MigrationState();
        public ClaimsLedger Claims = new ClaimsLedger();
        public CalibrationState Calibration = new CalibrationState();
        public Ledger Ledger = new Ledger(0, 0, 0);
        public long Tick;
        public SplitMix64 Rng;
        public int RenovationsTotal, ScrapesTotal;
        /// <summary>[0=Low,1=High] residential units freed this tick via
        /// Allocation.Vacate — the raw signal behind the engine's measured
        /// turnover EMA (see EconomyEngine.MigrationStep). Reset each tick.</summary>
        public double[] FreedUnitsThisTick = new double[2];

        // Scratch indices rebuilt by the engine each refresh
        public List<int>[] ParcelsByCluster = Array.Empty<List<int>>();
        public int[] HouseholdCountByCluster = Array.Empty<int>();

        public void RebuildIndices()
        {
            int c = Clusters.Length;
            if (ParcelsByCluster.Length != c)
            {
                ParcelsByCluster = new List<int>[c];
                for (int i = 0; i < c; i++) ParcelsByCluster[i] = new List<int>();
            }
            else for (int i = 0; i < c; i++) ParcelsByCluster[i].Clear();
            for (int i = 0; i < Parcels.Count; i++) ParcelsByCluster[Parcels[i].Cluster].Add(i);

            if (HouseholdCountByCluster.Length != c) HouseholdCountByCluster = new int[c];
            else Array.Clear(HouseholdCountByCluster, 0, c);
            foreach (var h in Households)
                if (h.HomeParcel >= 0) HouseholdCountByCluster[Parcels[h.HomeParcel].Cluster]++;
        }
    }
}
