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
        /// <summary>PER-ADULT labor-force participation; household labor supply
        /// is Adults × Participation. Family adults participate at 1.0 because
        /// that is what CS2 does: job-seeking is per-citizen — "when a citizen
        /// reaches working age, they look for a job" (CS2 wiki, Citizens) —
        /// and there is no breadwinner/homemaker mechanic, so a two-adult
        /// family supplies two workers, subject to unemployment. (An earlier
        /// build set family adults to 0.5 to hold household labor supply at
        /// its pre-distribution level; that was a calibration convenience
        /// contradicting the game and was reverted — the labor market
        /// re-equilibrates around the true supply instead.) StudentLow's 0.5
        /// reflects student-led households where members study rather than
        /// work; Seniors 0 (pension). In-game this is measurable directly
        /// (Worker members ÷ adult members).</summary>
        public readonly double Participation;
        /// <summary>Non-wage household transfer per tick. Maps to CS2's
        /// Game.Prefabs.EconomyParameterData taps: m_Pension (Senior segments),
        /// m_FamilyAllowance × children (Family segments), student stipend
        /// (StudentLow). One calibrated aggregate rather than three taps because
        /// the segments are already composition archetypes. The per-adult
        /// unemployment benefit is NOT here — it varies WITHIN a segment and so
        /// belongs to the distribution (EconParams.UnemploymentBenefit).</summary>
        public readonly double Transfer;
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

        /// <summary>Working-age adults in a typical household of this segment —
        /// the EARNER COUNT, and the largest single source of within-segment
        /// income spread (a two-earner family out-earns a one-earner family of
        /// the same education by ~2×). Read from the game as the adult members
        /// of Game.Citizens.Household (HouseholdMember buffer); seniors are 0
        /// (pension only). See Income.Build.</summary>
        public readonly int Adults;

        public Segment(string name, Lifecycle life, LaborClass labor, double participation, double transfer,
                       double jobW, double goodsW, double schoolW, double amenW, double healthW,
                       double pollW, double densTol, double rentShare, int adults)
        {
            Name = name; Life = life; Labor = labor; Participation = participation; Transfer = transfer;
            JobAccessW = jobW; GoodsAccessW = goodsW; SchoolAccessW = schoolW; AmenityW = amenW;
            HealthW = healthW; PollutionW = pollW; DensityTolerance = densTol; MaxRentShare = rentShare;
            Adults = adults;
        }

        /// <summary>The fixed segment table. Index = segment id everywhere. Wages
        /// are per labor class (EconParams.Wage) — firms pay them; segments differ
        /// through participation, transfers, and weights.</summary>
        public static readonly Segment[] All = new[]
        {
            //           name          life               labor                part  transfer jobW  goodsW schoolW amenW healthW pollW densTol rentShare adults
            new Segment("StudentLow",  Lifecycle.Student, LaborClass.Basic,    0.5,  3.0,     0.5,  0.6,   1.5,    0.6,  0.2,    0.5,  1.0,    0.40,     1),
            new Segment("SingleBasic", Lifecycle.Single,  LaborClass.Basic,    1.0,  0.0,     1.2,  0.8,   0.0,    0.7,  0.2,    0.6,  1.0,    0.34,     1),
            new Segment("SingleSkill", Lifecycle.Single,  LaborClass.Skilled,  1.0,  0.0,     1.2,  1.0,   0.0,    1.0,  0.2,    0.8,  0.9,    0.32,     1),
            new Segment("FamilyBasic", Lifecycle.Family,  LaborClass.Basic,    1.0,  1.0,     1.0,  1.0,   1.2,    0.9,  0.5,    1.0,  0.35,   0.30,     2),
            new Segment("FamilySkill", Lifecycle.Family,  LaborClass.Skilled,  1.0,  1.0,     1.0,  1.1,   1.3,    1.1,  0.5,    1.1,  0.30,   0.28,     2),
            new Segment("FamilyEdu",   Lifecycle.Family,  LaborClass.Educated, 1.0,  1.0,     1.0,  1.2,   1.4,    1.3,  0.5,    1.2,  0.30,   0.26,     2),
            new Segment("SeniorLow",   Lifecycle.Senior,  LaborClass.Basic,    0.0,  8.0,     0.05, 0.9,   0.0,    1.4,  1.5,    1.0,  0.5,    0.32,     0),
            new Segment("SeniorMid",   Lifecycle.Senior,  LaborClass.Skilled,  0.0, 13.0,     0.05, 1.0,   0.0,    1.6,  1.6,    1.1,  0.4,    0.30,     0),
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
        /// ℓ* and thinning land rent. Heterogeneity and surplus now come from
        /// where they belong — the queue.
        ///
        /// Calibration anchor. This USED to be half of a product — scale ×
        /// MarginalIncomeQuantile = 1.0 — where the quantile stood in for the
        /// within-segment income distribution the model did not carry. With
        /// Income.cs carrying the real distribution the quantile is retired and
        /// this is the whole anchor. The value did not need re-tuning: the
        /// distribution is built at CONSTANT MEAN, and the marginal bidder the
        /// price now FINDS on the real curve sits close to where the retired
        /// 0.75 approximation put it (measured, same fixture: supply×{0.5,2,8}
        /// → 2.61/1.42/1.42 against 2.71/1.36/1.36 before). What did change is
        /// the SLOPE — a real distribution makes the curve much steeper, so
        /// scarcity bites harder and gluts price softer than a point estimate
        /// allowed.</summary>
        public double BidAccessScale = 1.33;
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
        // MarginalIncomeQuantile RETIRED. It was a scalar ("the marginal member
        // of a segment earns ~75% of its mean") standing in for the within-
        // segment income distribution the model did not carry. Income.cs now
        // carries the real distribution — built from CS2's own income model —
        // so the marginal bidder is FOUND by walking the demand curve rather
        // than approximated by a hand-tuned discount. Its calibration history
        // is worth remembering: it was cut 0.75 → 0.55 to damp an emigration
        // "turnstile", churnprobe then showed 98% of those exits were arrivals
        // that never found a unit (an absorption-budget bug), and the cut had
        // meanwhile halved assessments and stalled construction. A parameter
        // standing in for a missing mechanism attracts exactly that kind of
        // misattributed tuning.
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
        /// <summary>Weight on a household's own consumption value when it
        /// judges a location: its location utility carries
        /// ConsumptionWeight·log(1 − rent/income), so what is left to live on
        /// after rent is part of the decision. This is what makes the location
        /// choice a MARKET decision — dear places lose bidders, which lowers
        /// their next clearing price — rather than a pure attractiveness
        /// ranking, and the log's curvature is what sorts households across
        /// price levels instead of shifting everyone by the same amount.</summary>
        public double ConsumptionWeight = 1.5;
        /// <summary>How much a household values not moving, in the units of its
        /// location taste shock (Gumbel, σ = π/√6 ≈ 1.28), at the mean moving
        /// cost. Each household scales this by its OWN MovingCostDraw. This is
        /// a genuine knob and it is load-bearing: too small and everyone shops
        /// at once so demand piles onto a handful of favourites; too large and
        /// sitting tenants are unmovable and no submarket's price responds to
        /// anything.</summary>
        public double MoveInertia = 1.5;

        // ---- housing assignment market (HousingAuction) ---------------------
        /// <summary>How many (density, cluster) pairs a household carries into
        /// the auction, ranked price-free. Bounded from below by the outside
        /// option: a place worth less than leaving the city is never listed.</summary>
        public int AuctionShortlist = 12;
        /// <summary>Idiosyncratic taste for a specific place, as a fraction of
        /// the household's own housing budget (Gumbel, σ ≈ 1.28, so this is
        /// roughly the ± swing at one standard deviation).</summary>
        public double AuctionTasteScale = 0.15;
        /// <summary>Bid increment. Every eviction lifts a submarket's admitted
        /// price by at least this, which is what makes the ascent finite; the
        /// result is an ε-equilibrium (nobody envies another's place by more
        /// than ε).</summary>
        public double AuctionEpsilon = 0.002;
        /// <summary>Bids per household before the solve gives up and reports
        /// Converged = false. A backstop, not a target: a warm market settles in
        /// a small multiple of one bid each.</summary>
        public int AuctionBidBudget = 40;
        /// <summary>Surplus from leaving the city, in the same money as a rent.
        /// Zero means "a home is worth having if it beats its own reserve
        /// price"; raising it makes the city pickier and emigration easier.</summary>
        public double OutsideOption = 0.0;
        /// <summary>Ticks a household spreads its moving cost over when it
        /// values staying put. Turns a lump into a flow so it is commensurate
        /// with a rent.</summary>
        public int MoveAmortTicks = 60;
        /// <summary>How much a household favours the shop it already uses, in
        /// the units of its shop taste shock (Gumbel, σ ≈ 1.28). Switching
        /// friction: people go back to their usual shop unless another is
        /// clearly better.</summary>
        public double ShopLoyalty = 1.0;
        /// <summary>Smoothing on the posted price households read when judging
        /// affordability. The price they react to is the price their reaction
        /// sets, so the loop needs damping or it rings.</summary>
        public double PostedPriceAlpha = 0.35;
        public int AssessSlices = 10;              // parcels assessed 1/N per tick (staggered)
        public int ScrapePressureTicks = 60;       // sustained-gap requirement before warehousing
        public double CondBidFloor = 0.45;         // bid factor at condition 0

        // ---- population dynamics --------------------------------------------
        public double ArrivalSavingsMean = 100.0, ArrivalSavingsSd = 30.0;
        public double FirmSeedCapital = 200.0;
        public double CompanyBankruptcyLimit = -150.0;
        /// <summary>Working capital a firm keeps before distributing surplus,
        /// as a multiple of its per-tick wage bill. Below this it retains
        /// everything — a collective does not pay itself into insolvency.</summary>
        public double FirmWorkingCapitalTicks = 20.0;
        /// <summary>Share of the surplus ABOVE that reserve paid out per tick
        /// to the firm's own members. A WORKER COLLECTIVE: the surplus goes to
        /// the people who work there, not to a pool and not spread across the
        /// city. This is the circular-flow channel the economy was missing —
        /// CS2 has one (Game.Simulation.CompanyDividendSystem) and we did not,
        /// so firm cash rose monotonically ($1.3M in commercial by t=400),
        /// permanently draining household spending and starving the
        /// consumption → commercial → jobs loop. Our ledger conserves money
        /// (CS2's deliberately does not), so un-recycled profit cannot
        /// evaporate — it just piles up.</summary>
        public double FirmDividendRate = 0.02;
        public double SeniorMortalityPerTick = 1.0 / 5475.0;  // ~15 sim-years

        public double CondFactor(double condition) => CondBidFloor + (1 - CondBidFloor) * condition;

        // ---- wages by labor class (education → qualification → wage, §1.1) ---
        // These are the class MEANS. The job-level ladder below disperses around
        // them at constant mean, so every aggregate calibrated on these is
        // untouched by the introduction of the distribution.
        public double WageBasic = 10.0, WageSkilled = 16.0, WageEducated = 26.0;

        // ---- within-segment income dispersion (CS2's own income model) -------
        // Research notes §3, Game.Prefabs.EconomyParameterData. See Income.cs.
        /// <summary>Wage ratio between adjacent JOB levels — the shape of CS2's
        /// m_Wage0..m_Wage4 ladder. Only the ratio matters: Income.JobLevels
        /// rescales the ladder so its weighted mean is exactly Wage(class).</summary>
        public double JobLevelSpread = 1.35;
        /// <summary>Weight decay per level BELOW a worker's top qualification —
        /// over-qualification. CS2 produces it structurally: FreeWorkplaces is
        /// per-education-tier and runs out, so citizens take lower jobs. 0.5 =
        /// each step down half as likely as the step above.</summary>
        public double JobLevelDownshift = 0.5;
        /// <summary>m_UnemploymentBenefit: what a working-age adult with no job
        /// receives. Previously missing entirely, which gave unemployed Single
        /// households an income of exactly ZERO — the zero-WTP tranches the
        /// clearing price had to filter out by hand.</summary>
        public double UnemploymentBenefit = 3.0;
        /// <summary>m_ResidentialMinimumEarnings: floor under household earnings.</summary>
        public double ResidentialMinimumEarnings = 1.0;
        /// <summary>How long the unemployment benefit is paid before it stops.
        /// This is CS2's `Unemployment Allowance Max Days` and it is the game's
        /// LABOR-MARKET CLEARING mechanism: "if you don't provide them with
        /// suitable jobs, they will eventually have no other option than to
        /// leave the city". Without it, benefits are permanent, unemployment is
        /// comfortably survivable, and a city can sit at 55% unemployment
        /// forever while still growing — measured before this existed: employment
        /// flat at 43–46% over 400 ticks with departures of exactly zero.
        ///
        /// CS2's own value is 10 in-game days. Ours is 60 ticks because the job
        /// search re-rolls on a 60-tick epoch (EconomyEngine): a shorter limit
        /// would cut support off before the household had a single genuine
        /// chance to find work. One full search cycle, then the insolvency
        /// pipeline takes over.</summary>
        public int UnemploymentAllowanceTicks = 60;
        /// <summary>m_UnemploymentEffect / m_NeutralUnemployment: how strongly
        /// citywide unemployment above its natural rate damps in-migration.
        /// Vanilla puts unemployment DIRECTLY into its demand calculation; we
        /// had it only indirectly through mean income, which is far too weak a
        /// brake — arrivals kept pouring into a city with no jobs.</summary>
        public double UnemploymentEffect = 3.0;
        public double NeutralUnemployment = 0.08;
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
        /// <summary>Route each household's consumption to the ONE shop it chose,
        /// instead of pooling all consumption citywide and handing it back out
        /// pro-rata to (slots × cluster capture strength).
        ///
        /// OFF by default, and the reason is measured, not cautious. The
        /// mechanism works and does what it says: a shop's takings become its own
        /// customers' money, so revenue-per-slot stops being identical across the
        /// sector (it varied by 1.8e-10 under the pool) and a shop with no
        /// catchment dies. What follows it does not hold up yet. The commercial
        /// sector settles ~30% smaller (108 → 76 firms) with commercial parcel
        /// vacancy at 54% against 34%, because a discrete market kills the
        /// marginal shops the pool was quietly subsidising and construction keeps
        /// rebuilding them. Goods demand shrinks with it: the Weber invariant
        /// (extraction follows geology, recipes follow input sourcing) fails on
        /// 2 of 8 seeds — not on the extraction leg, which stays at 100%, but on
        /// the industrial sector thinning to 3 firms or to a single output.
        ///
        /// Tried and rejected as fixes, all measured over 8 seeds and a 2000-tick
        /// firm census: shop loyalty at 0/0.4/1.0 (churn unchanged); staggering
        /// each household's shop review so catchments drift rather than step
        /// (deaths 217 → 680 — new shops starve before they fill); counting the
        /// unhoused in the entry field for consistency with the realized market
        /// (deaths 187 → 217, three more seeds lost — the shelter population is
        /// too volatile to capitalize a building against); and larger firm cash
        /// buffers (working capital 20 → 45 ticks, dividend rate 0.02 → 0.008 —
        /// deaths fall but the seed that matters does not recover).
        ///
        /// What DID work rides along with this flag: deducting housing from
        /// SpendMass, which the new rent level made material (deaths 217 → 94).
        /// It is tied here rather than made unconditional because on the pooled
        /// path the same deduction is just a ~30% cut to an entry signal the
        /// pooled calibration was set against, and it costs three seeds.
        ///
        /// The remaining gap is ENTRY. The phantom-entrant capture field still
        /// describes a pooled market — it tells a developer it will earn its
        /// proportional share of nearby spending — so developers keep building
        /// shops the discrete market cannot feed. Closing that is the next step,
        /// and it is what this flag is waiting on.
        ///
        /// Harness: `--store-level` on any command turns it on. Measured
        /// verify over seeds 0–7: 7/8 with the flag off (seed 3 fails on the
        /// clearing-price check, and did so before this branch), 5/8 with it
        /// on (seeds 0 and 5 additionally fail Weber).</summary>
        public bool StoreLevelSpending = false;
        /// <summary>Solve housing as ONE assignment market (HousingAuction):
        /// prices and who-lives-where come out of the same ascending auction,
        /// instead of a demand curve inverted for the price and a
        /// first-come-first-served queue for the keys. See HousingAuction for
        /// why the two-mechanism version could let the highest bidder lose a
        /// unit to whoever had a lower household id.</summary>
        public bool HousingAuction = false;
    }
}
