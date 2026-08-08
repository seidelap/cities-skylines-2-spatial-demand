using System;

namespace CS2Econ.Core
{
    public enum ZoneKind : byte { None, ResidentialLow, ResidentialHigh, Commercial, Industrial, Office, Extractor }

    /// <summary>Resource-level economy (design §4.2/§4.5): four extracted raws
    /// with per-cluster natural suitability, processed goods with recipes (one
    /// two-input chain so multi-sourcing is real), a local-only service good, and
    /// near-exogenous office output. Maps 1:1 onto CS2's Resource members at the
    /// mod boundary (EconAdapters) — the harness set is the spatial skeleton.</summary>
    public enum Res : byte
    {
        Grain, Wood, Ore, Oil,                       // raws (extractor output)
        Food, Timber, Metals, Plastics, Machinery,   // processed (industrial recipes)
        Services,                                    // local-only (commercial)
        OfficeOutput,                                // exogenous price
    }

    /// <summary>One industrial recipe: inputs (with quantities per unit of
    /// output) → output. Recipe CHOICE at a location is the Weber decision:
    /// where each input is sourced (local haul vs import parity) prices into
    /// the location's industrial bid.</summary>
    public readonly struct Recipe
    {
        public readonly Res Output;
        public readonly (Res res, double qty)[] Inputs;
        public readonly double OutputPerSlot;      // units per filled slot per tick
        public Recipe(Res output, double outputPerSlot, params (Res, double)[] inputs)
        { Output = output; OutputPerSlot = outputPerSlot; Inputs = inputs; }
    }

    public static class ResourceCatalog
    {
        public const int Count = 11;
        public const int RawCount = 4;

        public static bool IsRaw(Res r) => (byte)r < RawCount;
        public static bool IsProcessed(Res r) => r >= Res.Food && r <= Res.Machinery;
        public static bool IsTradable(Res r) => r != Res.Services && r != Res.OfficeOutput;

        /// <summary>World anchor prices (per unit, at the fundamental).</summary>
        public static readonly double[] Anchor =
        {
            /*Grain*/ 1.6, /*Wood*/ 2.0, /*Ore*/ 2.6, /*Oil*/ 3.2,
            /*Food*/ 3.6, /*Timber*/ 4.2, /*Metals*/ 5.4, /*Plastics*/ 6.0, /*Machinery*/ 13.0,
            /*Services*/ 1.0, /*Office*/ 0,
        };

        /// <summary>Freight weight multiplier (heavier goods haul dearer —
        /// vanilla's per-mode m_WeightMultiplier analog, per resource).</summary>
        public static readonly double[] Weight =
        {
            /*Grain*/ 1.0, /*Wood*/ 1.3, /*Ore*/ 1.6, /*Oil*/ 1.2,
            /*Food*/ 0.9, /*Timber*/ 1.1, /*Metals*/ 1.2, /*Plastics*/ 0.7, /*Machinery*/ 0.8,
            /*Services*/ 0, /*Office*/ 0,
        };

        /// <summary>Household consumption basket: how captured commercial
        /// spending decomposes into restocking demand (share of spend by value).</summary>
        public static readonly (Res res, double share)[] Basket =
        {
            (Res.Food, 0.16), (Res.Machinery, 0.08), (Res.Timber, 0.03), (Res.Plastics, 0.03),
        };

        /// <summary>Recipes; industrial firms choose one at entry (argmax profit
        /// at their location). Machinery is the two-input chain.</summary>
        public static readonly Recipe[] Recipes =
        {
            new Recipe(Res.Food,      3.4, (Res.Grain, 0.55)),
            new Recipe(Res.Timber,    3.2, (Res.Wood, 0.60)),
            new Recipe(Res.Metals,    3.0, (Res.Ore, 0.60)),
            new Recipe(Res.Plastics,  2.8, (Res.Oil, 0.55)),
            new Recipe(Res.Machinery, 1.1, (Res.Metals, 0.45), (Res.Plastics, 0.35)),
        };

        public static Recipe RecipeFor(Res output)
        {
            foreach (var r in Recipes) if (r.Output == output) return r;
            return default;
        }
    }

    public enum ExitMode : byte { Road, Rail, Sea, Air }

    public enum LaborClass : byte { Basic, Skilled, Educated }

    public enum Lifecycle : byte { Student, Single, Family, Senior }

    /// <summary>Population segment = income × education × lifecycle (design §4.2),
    /// with lifecycle carrying real weight structure: seniors have transfer income,
    /// near-zero job-access weight and high amenity/healthcare weights; students
    /// and singles bid low with high density tolerance.</summary>
    public readonly struct Segment
    {
        public readonly string Name;
        public readonly Lifecycle Life;
        public readonly LaborClass Labor;
        public readonly double Participation;   // labor-force share (seniors 0, students partial)
        public readonly double Transfer;        // exogenous per tick (seniors, students)
        public readonly double JobAccessW;      // weight on job access
        public readonly double GoodsAccessW;    // weight on shopping access
        public readonly double SchoolAccessW;
        public readonly double AmenityW;
        public readonly double HealthW;
        public readonly double PollutionW;      // negative term weight
        public readonly double DensityTolerance;// 0..1: 1 = happy in ResidentialHigh

        /// <summary>How much this segment values a unit of the given density,
        /// relative to its ideal — a PREFERENCE weight in (0,1], never a
        /// permission. Low density suits everyone (1.0); high density is
        /// discounted by the segment's tolerance, floored so no household is
        /// absolutely barred.
        ///
        /// This used to be a hard gate (`DensityTolerance >= 0.5`), which is a
        /// category error on a field documented as a 0..1 preference: it barred
        /// every Family segment from apartments outright, so high-density stock
        /// structurally exceeded the population permitted to occupy it and no
        /// apartment could clear its market or carry land rent — while the
        /// allocator filled those units anyway, leaving allocation and pricing
        /// disagreeing about who may live where (adversarial review, confirmed).
        /// As a weight it does what its name says: families CAN live in
        /// apartments, they just bid less for them.</summary>
        public double DensityAppeal(ZoneKind kind)
            => kind == ZoneKind.ResidentialHigh
                ? DensityFloor + (1 - DensityFloor) * MathUtil.Clamp(DensityTolerance, 0, 1)
                : 1.0;

        /// <summary>Appeal of the least-tolerant segment for high density —
        /// i.e. the steepest discount density aversion can impose.
        ///
        /// Deliberately mild (a ~28% haircut at tolerance 0.3, not a 70% one)
        /// because the segment table locks density tolerance INVERSELY to
        /// wealth: every affluent segment sits at 0.28–0.35 while the poorest
        /// sit at 0.9–1.0. Scaling willingness-to-pay by raw tolerance
        /// therefore caps what apartments can ever fetch far below houses
        /// (measured: high-density WTP topped out at 3.21 against 9.68 for
        /// low), which makes a premium high-rise structurally impossible and
        /// strands high density permanently at the extensive margin. Density
        /// belongs in a household's valuation as a modifier, not as the
        /// dominant term. The inverse wealth/tolerance coupling in the segment
        /// table is itself a design assumption worth revisiting.</summary>
        public const double DensityFloor = 0.6;
        public readonly double MaxRentShare;    // fraction of income bid for housing

        public Segment(string name, Lifecycle life, LaborClass labor, double participation, double transfer,
                       double jobW, double goodsW, double schoolW, double amenW, double healthW,
                       double pollW, double densTol, double rentShare)
        {
            Name = name; Life = life; Labor = labor; Participation = participation; Transfer = transfer;
            JobAccessW = jobW; GoodsAccessW = goodsW; SchoolAccessW = schoolW; AmenityW = amenW;
            HealthW = healthW; PollutionW = pollW; DensityTolerance = densTol; MaxRentShare = rentShare;
        }

        /// <summary>The fixed segment table. Index = segment id everywhere. Wages
        /// are per labor class (EconParams.Wage) — firms pay them; segments differ
        /// through participation, transfers, and weights.</summary>
        public static readonly Segment[] All = new[]
        {
            //           name          life               labor                part  transfer jobW  goodsW schoolW amenW healthW pollW densTol rentShare
            new Segment("StudentLow",  Lifecycle.Student, LaborClass.Basic,    0.5,  3.0,     0.5,  0.6,   1.5,    0.6,  0.2,    0.5,  1.0,    0.40),
            new Segment("SingleBasic", Lifecycle.Single,  LaborClass.Basic,    1.0,  0.0,     1.2,  0.8,   0.0,    0.7,  0.2,    0.6,  1.0,    0.34),
            new Segment("SingleSkill", Lifecycle.Single,  LaborClass.Skilled,  1.0,  0.0,     1.2,  1.0,   0.0,    1.0,  0.2,    0.8,  0.9,    0.32),
            new Segment("FamilyBasic", Lifecycle.Family,  LaborClass.Basic,    1.0,  1.0,     1.0,  1.0,   1.2,    0.9,  0.5,    1.0,  0.35,   0.30),
            new Segment("FamilySkill", Lifecycle.Family,  LaborClass.Skilled,  1.0,  1.0,     1.0,  1.1,   1.3,    1.1,  0.5,    1.1,  0.30,   0.28),
            new Segment("FamilyEdu",   Lifecycle.Family,  LaborClass.Educated, 1.0,  1.0,     1.0,  1.2,   1.4,    1.3,  0.5,    1.2,  0.30,   0.26),
            new Segment("SeniorLow",   Lifecycle.Senior,  LaborClass.Basic,    0.0,  8.0,     0.05, 0.9,   0.0,    1.4,  1.5,    1.0,  0.5,    0.32),
            new Segment("SeniorMid",   Lifecycle.Senior,  LaborClass.Skilled,  0.0, 13.0,     0.05, 1.0,   0.0,    1.6,  1.6,    1.1,  0.4,    0.30),
        };

        public static int Count => All.Length;
    }

    /// <summary>All named parameters with design-doc references. Everything the
    /// design calls a knob lives here; nothing is buried as a literal.</summary>
    public sealed class EconParams
    {
        // ---- shared rates (design §4.3) -------------------------------------
        public double HurdleRate = 0.0004;        // h per tick (~15%/yr at 365 ticks/yr)
        public double Depreciation = 0.00035;     // δ per tick
        public double MaintenanceRate = 0.00025;  // m per tick
        public int AnnuityHorizon = 3650;         // L ticks for a(h,L)

        // ---- structure / levels (design §4.4) -------------------------------
        public double RC1PerUnit = 700.0;         // replacement cost, level 1, per unit
        public double LevelCostGamma = 1.5;       // RC_ℓ = RC1·γ^(ℓ−1), γ ∈ [1.4,1.6]
        public double LevelBidAlpha = 0.45;       // concave bid uplift: quality(ℓ) = ℓ^α
        public int MaxLevel = 5;
        public double SalvageFraction = 0.35;     // salvage of V on scrape
        public double DemolitionPerUnit = 60.0;

        // ---- land accounting (design §4.3) ----------------------------------
        public double CaptureFraction = 0.95;     // φ = τ_L/(r+τ_L); "full capture" default
        public double StructureTaxRate = 0.0;     // τ_S default zero (self-teaching slider)
        public bool WedgeEarmarkDefault = true;   // wedge → parcel escrow (TIF analog)
        /// <summary>Mean ticks between a stressed household's relocation
        /// searches (per-tick hazard 1/this — memoryless, so moves never
        /// synchronize). Re-RATING itself is instant and uniform per parcel
        /// (co-op assessment): search friction is the only lag left.</summary>
        public int MoveSearchPeriod = 30;
        /// <summary>Minimum tenure before a housed household will consider a
        /// voluntary cost-driven move — a lease term. The co-op re-rate still
        /// moves the CHARGE instantly (prices are always market); this limits
        /// how often a household re-solves its location problem. Without it,
        /// pricing at the marginal bidder's expected-income WTP leaves the
        /// below-average half of every marginal segment permanently over its
        /// REALIZED-income margin, and they hop between similar units forever
        /// (~2 moves/household/run measured). The insolvency pipeline is NOT
        /// gated by this — genuine distress still moves immediately.</summary>
        public int MinLeaseTicks = 30;
        /// <summary>One-time transition window when Tier C first goes live:
        /// charges converge from whatever they were (in-game: vanilla rents
        /// mirrored by the reader) to the market assessment over this many
        /// ticks, staggered per household. Steady state is untouched — pricing
        /// is instant and uniform forever after. Without it, flipping the mod
        /// out of shadow mode re-rates an entire city in ONE tick and writes
        /// every renter's rent at once (adversarial review, confirmed).</summary>
        public int GoLiveRampTicks = 30;

        // ---- vacancy field (submarket kernel) --------------------------------
        /// <summary>e-folding radius, in METERS of straight-line (walking-
        /// proxy) distance, of one vacant unit's competitive influence. The
        /// kernel is normalized so each vacancy's total influence integrates
        /// to exactly one unit of demand (V = 1 — vacancies neither mint nor
        /// destroy aggregate demand); λ only shapes where it lands.</summary>
        public double VacancyKernelLambdaM = 800.0;
        /// <summary>Leases per tick per vacant unit — the absorption hazard
        /// that paces in-migration (arrivals ≤ hazard × vacant stock). Its
        /// inverse is mean time-to-fill in ticks (≈ days on market).</summary>
        public double VacancyFillHazard = 1.0 / 60.0;
        /// <summary>Turnover PRIOR for the absorption budget: assumed share of
        /// occupied units re-let per tick, used as a floor under the MEASURED
        /// turnover flow (EMA of units actually freed via Allocation.Vacate —
        /// see EconomyEngine.TurnoverEma). As the budget's sole churn term it
        /// admitted ~50× more arrivals than units actually freed (churnprobe:
        /// 98% of "displacement" exits were arrivals that never found a
        /// unit); the bare measurement instead deadlocked a saturated
        /// no-churn city. Floor + measurement + the unhoused-queue congestion
        /// gate together pace admission honestly.</summary>
        public double HousingTurnoverRate = 0.004;

        // ---- access (design §4.2) -------------------------------------------
        public double ThetaCommute = 0.055;       // e^(−θc) decay, c in generalized minutes
        public double ThetaShopping = 0.09;
        public double ThetaFreight = 0.03;
        public double ThetaOffice = 0.07;
        public double OfficeAgglomGamma = 0.075;  // A(p)^γ, γ ∈ [0.05, 0.1]
        public int IpfIterations = 6;

        // ---- bids -----------------------------------------------------------
        public double PremiumExponent = 2.2;      // convexity of the location premium
        /// <summary>Scale from a segment's rent CAP to the bid it actually
        /// makes. Under the old presence-weighted MEAN bid this stood in for
        /// "occupants keep surplus" — the average bidder pays less than their
        /// ceiling. Under marginal-bidder clearing that adjustment is already
        /// in the mechanism: the price IS the marginal bidder's willingness to
        /// pay, and the marginal bidder by construction retains no surplus,
        /// while everyone above them retains theirs automatically. Keeping the
        /// old 0.47 therefore discounted the competitive price a second time,
        /// pushing bids below structure cost across most of the map, flattening
        /// ℓ* and thinning land rent. Raised to 1.0 so the clearing price is the
        /// competitive price; heterogeneity and surplus now come from where
        /// they belong — the queue.</summary>
        public double BidAccessScale = 1.0;
        /// <summary>How far PAST the last filled unit the clearing walk reads
        /// the demand curve: price = WTP at queue position supply×(1+band) —
        /// the first EXCLUDED tranche of bidders, not the last admitted one.
        ///
        /// This is the buyer-optimal end of the assignment-market equilibrium
        /// band, and it is the rule as originally specified: "rent equal to
        /// the maximum another person would pay to move here" — the excluded
        /// challenger sets the price. Pricing at the marginal ADMITTED
        /// bidder's own WTP (band = 0) extracts that tenant's entire surplus,
        /// and under the instant co-op re-rate that pins every marginal
        /// household at exactly zero surplus every tick: any perturbation
        /// displaces them, the price re-clears at the next bidder, who then
        /// sits on the same knife edge — measured as a ~50× displacement-churn
        /// explosion. With the band, every admitted tenant strictly prefers
        /// staying by at least the WTP gap down to the excluded tranche.</summary>
        public double ClearingBand = 0.10;
        /// <summary>Income of the MARGINAL renter within a segment, as a
        /// fraction of the segment's expected income. ExpectedIncome averages
        /// employed and unemployed members (rate×wage + transfer), but the
        /// clearing price is paid by a PERSON, and the person on the margin of
        /// a segment earns less than its mean — person-level pricing would
        /// produce this dispersion automatically; the segment aggregation
        /// hides it. This is NOT the old BidAccessScale double discount (that
        /// scaled the whole bid for surplus the clearing mechanism already
        /// provides). Calibration honesty: this was cut to 0.55 to damp an
        /// emigration "turnstile"; churnprobe then showed 98% of those exits
        /// were arrivals that never found a unit — an absorption-budget bug
        /// (see HousingTurnoverRate), not a price-level effect — while the
        /// cut itself halved assessments and stalled construction (starts
        /// 265 → 42 in the debug fixture; commercial/industrial development
        /// to zero). With the churn justification disproven the cut was
        /// reverted; 0.75 keeps the dispersion story at its original,
        /// A/B-validated level.</summary>
        public double MarginalIncomeQuantile = 0.75;
        public double CommercialMarkup = 0.35;    // gross margin on captured spending
        public double OfficeOutputPrice = 3.1;    // near-exogenous (design §4.2)

        // ---- production coefficients (viable at world anchors vs wages) ------
        public double ExtractorOutputPerSlot = 6.0;  // × cluster suitability for the raw
        public double OfficeOutputPerSlot = 10.0;
        public double RecipeOutputScale = 1.0;       // global multiplier on catalog OutputPerSlot

        /// <summary>Flow value of liquid savings for affordability decisions:
        /// households draw down wealth over roughly this horizon (a wealthy
        /// retiree can rent; a broke worker cannot, whatever the wage tables say).</summary>
        public double WealthDrawdownTicks = 300.0;

        // ---- migration (design §4.1) ----------------------------------------
        public double MigInElasticity = 0.0030;   // per segment per tick, on utility gap
        public double MigOutElasticity = 0.0008;  // slower: attachment / loss aversion
        public double MigOutLagAlpha = 0.008;     // EMA lag on the out-migration signal
        public double RegionSize = 40_000;        // the shared "how big is the world" knob
        public double ReservationReplenish = 0.001;
        public double MigFieldResponsiveness = 0.025; // threshold responds at RegionSize×this arrivals
        public double ProminenceScale = 60_000;   // city size at which field widening doubles
        public double NetworkMemoryDecay = 0.995;
        public double NetworkMemoryGain = 0.08;
        public double FirmEntryElasticity = 0.004;

        // ---- trade (design §4.5) --------------------------------------------
        public double TradeSustainAlpha = 0.02;   // EMA to sustained Q
        public double TradeTransientDecay = 0.85; // per tick resilience decay
        public double TradeTransientBeta = 0.6;   // burst weight on effective position
        public double LotSize = 25.0;             // quantized offer size

        // ---- construction (design §4.6) -------------------------------------
        public int ConstructionLag = 45;          // ticks to complete
        public double SoftmaxSpread = 0.0005;     // logit spread over developer returns
        public int MaxStartsPerTick = 6;          // construction industry capacity
        public double AbandonMarginFactor = 0.25; // abandon if E[flow] < factor·h·remainingCost
        public double CalibShrinkN0 = 12.0;       // shrinkage prior weight for correction factors

        // ---- condition / decay ----------------------------------------------
        public double ConditionDecayScale = 8.0;  // multiplies δ when S unpaid: vacant stock cheapens in ~sim-months, not years
        public double EscrowToConditionRate = 0.02; // stalled escrow drains into condition

        // ---- insolvency / floor (design §4.2) -------------------------------
        public int InsolvencyGraceTicks = 18;
        public double ConsumptionCutFactor = 0.6;
        public double EmigrationMoveCost = 40.0;
        public double ShelterCapacityShare = 0.015; // of population

        // ---- consumption ----------------------------------------------------
        public double BaseConsumptionShare = 0.80; // of after-housing income, spent at commercial
        public double MovingCostMean = 25.0;
        public double OwnerMovingCostMult = 2.2;   // owner-tagged margins are larger (§4.4)
        public double OutsideShopMinutes = 40.0;   // outside option in the shopping logit
        public double OutsideShopMass = 60.0;      // (uncaptured spending leaks outward)

        // ---- city services / fiscal -----------------------------------------
        public double ServiceCostPerHousehold = 1.2; // per tick, paid by Treasury
        public double ServiceCostPerFirmSlot = 0.35;

        // ---- engine cadence -------------------------------------------------
        public int RefreshInterval = 5;            // ticks between Tier B refreshes
        public int AssessSlices = 10;              // parcels assessed 1/N per tick (staggered)
        public int ScrapePressureTicks = 60;       // sustained-gap requirement before warehousing
        public double CondBidFloor = 0.45;         // bid factor at condition 0

        // ---- population dynamics --------------------------------------------
        public double ArrivalSavingsMean = 100.0, ArrivalSavingsSd = 30.0;
        public double FirmSeedCapital = 200.0;
        public double CompanyBankruptcyLimit = -150.0;
        public double SeniorMortalityPerTick = 1.0 / 5475.0;  // ~15 sim-years

        public double CondFactor(double condition) => CondBidFloor + (1 - CondBidFloor) * condition;

        // ---- wages by labor class (education → qualification → wage, §1.1) ---
        public double WageBasic = 10.0, WageSkilled = 16.0, WageEducated = 26.0;
        public double Wage(LaborClass c) => c switch
        {
            LaborClass.Basic => WageBasic,
            LaborClass.Skilled => WageSkilled,
            _ => WageEducated,
        };

        // ---- labor matching (Balancing.cs slack cost: reservation friction) --
        public double LaborSlackMinutes = 55.0;   // e^(−θ·slack) weight for unmatched

        // ---- income tax (vanilla-retained labor side, §4.3) ------------------
        public double IncomeTaxBasic = 0.10, IncomeTaxSkilled = 0.12, IncomeTaxEducated = 0.13;

        public double IncomeTax(LaborClass c) => c switch
        {
            LaborClass.Basic => IncomeTaxBasic,
            LaborClass.Skilled => IncomeTaxSkilled,
            _ => IncomeTaxEducated,
        };

        public double RC(int level, int units) => RC1PerUnit * Math.Pow(LevelCostGamma, level - 1) * units;
        public double Quality(int level) => Math.Pow(level, LevelBidAlpha);
    }

    /// <summary>Feature flags: every tier independently revertible (design §3,
    /// PLAN §1). Off means the vanilla-analog behavior in whatever host runs the
    /// core (the harness's vanilla baseline; the game's stock systems).</summary>
    public sealed class FeatureFlags
    {
        public bool TierA_Migration = true;      // endogenous outside world
        public bool TierB_Allocation = true;     // access-based allocation + residuals
        public bool TierC_LandAccounting = true; // S/tax/wedge, escrow, LVT
        public bool TierC2_Leveling = true;      // ℓ*, renovation clock, decay
        public bool TierD_Trade = true;          // finite-depth exits, parity bands
        public bool ConstructionRewire = true;   // residual-driven site selection
        public bool ShadowAccountingOnly = false;// stage 3: assess + log, levy nothing
    }
}
