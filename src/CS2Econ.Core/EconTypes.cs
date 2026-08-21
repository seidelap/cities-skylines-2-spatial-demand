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

    /// <summary>What an office actually does. The base game's offices are not
    /// one business — they produce different immaterial goods — and until this
    /// existed ours were: a single hardcoded Res.OfficeOutput at a single
    /// exogenous price.
    ///
    /// WHY IT MATTERS BEYOND FLAVOUR. `LandAccounting.Assess` charges a site on
    /// the BEST configuration its zoning permits, not on what its tenant is
    /// doing — that is the design, and `Parcel.Wedge` names the gap. For
    /// industrial that maximum ranges over recipes (the Weber choice) and for
    /// extractor over the raws the geology supports, so "the best business that
    /// could stand here" is a real maximum over a real set. For office it
    /// ranged over a set of size ONE, which makes the maximum a relabelling of
    /// the single candidate and leaves a failed office parcel able to re-let
    /// only to an identical office that fails identically.
    ///
    /// The kinds are deliberately NOT differentiated by price. They differ by
    /// WHERE THEY ALREADY ARE: each draws its agglomeration from its own kind's
    /// jobs, so the best office use of a site is the specialization that site's
    /// neighbourhood already supports. That is a Marshallian localization
    /// economy, it is local in exactly the sense this model is about, and it
    /// introduces no price constant that has not been measured — because it
    /// introduces no price constant at all.</summary>
    public enum OfficeKind : byte { Software, Financial, Media }

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
        /// <summary>Per-offer-batch decay of the per-cluster chain-migration
        /// stock (MigrationState.NetworkTies): each Prospects.Step call — one
        /// per tick on the engine path — multiplies the whole stock by this
        /// before the batch's admits are added, so one remembered arrival
        /// fades on a ~34-tick half-life and the standing stock is ~50× the
        /// per-tick admit flow (≈550 on the settled 10×10/3000 fixture, where
        /// admits run ~11/tick — bring-up runs). Links go stale as the people
        /// who hold them move on.</summary>
        public double NetworkTieDecay = 0.98;
        /// <summary>Familiarity bonus for a prospect's tie cluster, as a
        /// fraction of its own bid base at that door (the same money unit the
        /// taste term uses — AuctionTasteScale scales a Gumbel draw by bse;
        /// this scales a constant). An individual's taste for the one place
        /// its predecessors landed — individually legitimate, like taste.
        /// Swept on the tie-channel fixture (`tiesweep --seeds 4 --tie-bonus
        /// X`, seeds 0–3 + 9, 13, item-#34 bring-up) at {0.05, 0.10, 0.15,
        /// 0.20}: tie-landing lift over the independence baseline reads
        /// +0.008..+0.019 / +0.024..+0.032 / +0.032..+0.052 / +0.059..+0.074
        /// by dose, zero-bonus mutant arm 0.000..+0.006 at every dose. 0.15
        /// is the largest swept value not exceeding AuctionTasteScale — one
        /// Gumbel sd of idiosyncratic taste (~0.19·bse) still outweighs the
        /// tie, so familiarity tilts a close call rather than beating a
        /// genuinely better door — and its worst-seed lift clears the 0.015
        /// check bar with ~2x margin (0.10's worst seed left only 1.6x).</summary>
        public double NetworkTieBonusScale = 0.15;
        /// <summary>MUTANT SWITCH, harness-only (see the tie-channel verify
        /// check): zeroes the familiarity bonus while the tie draw and the
        /// stock keep running, so admits land independently of their tie
        /// cluster. Exists so that check stays falsifiable; never a shipping
        /// mode.</summary>
        public bool MutantZeroTieBonus = false;
        public double FirmEntryElasticity = 0.004;

        // ---- trade (design §4.5) --------------------------------------------
        public double TradeSustainAlpha = 0.02;   // EMA to sustained Q
        public double TradeTransientDecay = 0.85; // per tick resilience decay
        public double TradeTransientBeta = 0.6;   // burst weight on effective position
        public double LotSize = 25.0;             // quantized offer size
        /// <summary>Shrinkage prior weight (in VOLUME units) for the
        /// per-(resource, cluster) realized-price statistics: a cluster's
        /// DeliveredStat/OriginStat is its own transaction EMA weighted by its
        /// sustained transacted-volume EMA against the citywide volume-weighted
        /// prior at this weight. Calibrated against the measured per-(resource,
        /// cluster) transacted-volume EMA distribution on the reference fixture
        /// (`goodsprobe`, 10×10, 3000 households, seeds 0–3, t=40..300):
        /// positive delivered-volume EMAs read p10 0.29, p25 0.85, p50 1.37,
        /// p90 8.23, max 51.5 (origin thinner: p25 0.08, p50 0.36, p90 5.56).
        /// 1.0 ≈ delivered p25–median: a fraction-of-a-lot cluster is mostly
        /// prior, a median cluster an even split, an active market (p90+)
        /// mostly itself; origin markets, being thinner, read prior-heavier,
        /// which is honest for sparser evidence. Swept over {0.25, 1, 4, 16}
        /// (goodssweep, task #30 commit; seeds 0–3 at each point, plus 9/13/25
        /// at 1×): every point passes both goods checks; tilt corr reads
        /// 0.983–0.989 at 0.25×, 0.979–0.989 at 1×, 0.959–0.974 at 4×,
        /// 0.926–0.945 at 16×, with the localized-stat spread narrowing as the
        /// prior swamps evidence (mean rel spread 4.7–5.6% → 3.6–4.3% →
        /// 2.2–2.8% → 1.0–1.3%) — 1.0 keeps a median market's own price
        /// expressed without letting one lot set a place's price (the n0→0
        /// and n0→10⁶ endpoints are the goods-localization check's mutants,
        /// measured red there).</summary>
        public double TradePricePriorVolume = 1.0;

        // ---- construction (design §4.6) -------------------------------------
        public int ConstructionLag = 45;          // ticks to complete
        public double SoftmaxSpread = 0.0005;     // logit spread over developer returns
        public int MaxStartsPerTick = 6;          // construction industry capacity
        public double AbandonMarginFactor = 0.25; // abandon if E[flow] < factor·h·remainingCost
        public double CalibShrinkN0 = 12.0;       // shrinkage prior weight for correction factors
        /// <summary>n0 in CalibrationState.Factor(use, cluster): the prior
        /// weight (in completions observed) at which a cluster's own
        /// realized-vs-predicted record counts as much as the use-wide factor.
        /// Picked from the MEASURED per-cell completion counts on the
        /// reference fixture (10×10, 3000 households, 400 ticks —
        /// `calibsweep --seeds 4`, seeds 0–3 + 9, 13, item-#34 bring-up):
        /// occupied (use, cluster) cells hold n_c min 1, p25 1–2, median 2–3,
        /// p90 4–5, max 6–8 over 71–86 cells and 184–238 completions per
        /// seed. n0 = 2 sits at the p25–median: a single-completion cell is
        /// 1/3 its own record, a median cell an even split, and the deepest
        /// cells (6–8) are 75–80% their own.</summary>
        public double CalibClusterShrinkN0 = 2.0;

        // ---- condition / decay ----------------------------------------------
        public double ConditionDecayScale = 8.0;  // multiplies δ when S unpaid: vacant stock cheapens in ~sim-months, not years
        public double EscrowToConditionRate = 0.02; // stalled escrow drains into condition

        // ---- insolvency / floor (design §4.2) -------------------------------
        public int InsolvencyGraceTicks = 18;
        /// <summary>Task #19: put non-residential land on the residential rules.
        /// Four behaviours, one switch, because they are one finding:
        ///
        ///   1. ASSESSMENT READS THE PARCEL'S OWN GEOLOGY. LandAccounting.Assess
        ///      priced every extractor configuration at a flat 0.5 suitability
        ///      (BidPerUnit had no way to carry ClusterInfo — an artifact of the
        ///      residential signature being written first), while firm ENTRY and
        ///      Construction priced the same parcel at the real geology.
        ///      Measured, parityprobe seed 1 at 88fc88c: the two reads differ by
        ///      up to 530% on a single parcel.
        ///   2. A ONE-SELLER CELL IS NOT ITS OWN COMPARABLE. The industrial and
        ///      extractor output term read OriginStat — "what sellers at c
        ///      netted" — which where the parcel's own occupant is the only
        ///      seller is that firm's own realized revenue pricing the land it
        ///      stands on. That is the §3 circularity guard's firm analog, and
        ///      it was open: 6 of 8 occupied producing parcels (seed 1, 400
        ///      ticks), local evidence carrying 0.906 of the read.
        ///   3. OFFICE AND EXTRACTOR PRODUCE WHAT THEY ARE ASSESSED ON. Their
        ///      bids carry Quality(ℓ)/Quality(1) (and, for office, condition)
        ///      while their production functions carried neither — commercial
        ///      and industrial already carry both. An office was therefore
        ///      billed for output that does not exist: 489/tick against 428 of
        ///      GROSS revenue, same run.
        ///   4. AN UNMET LAND CHARGE REACHES AN OUTCOME. Firms had no arrears
        ///      semantics at all: pay min(money, bill), remainder forgiven, no
        ///      counter, no consequence. The levy alone can never push a firm
        ///      past CompanyBankruptcyLimit, and office and extractor firms
        ///      carry no input debits either, so those sectors were immortal in
        ///      permanent arrears — 54 of 62 standing offices short for 200+
        ///      CONSECUTIVE ticks, 63.8% of the whole firm bill uncollected,
        ///      against households paying 100.00% of theirs. With the switch on
        ///      a firm gets the household pipeline's own shape on the household's
        ///      own clock: grace (InsolvencyGraceTicks), sort down to land its
        ///      own forecast can carry, then release the parcel
        ///      (LandArrearsTicks).
        ///
        /// OFF BY DEFAULT, AND THE REASON IS MEASURED. Every one of these makes
        /// the assessment MORE accurate, and the non-residential base cannot
        /// carry an accurate assessment yet. ℓ* sits at the CORNER for every
        /// firm sector — mean TargetLevel 5.00 for office and industrial against
        /// standing levels 3.06 and 2.00 — because the firm bid is a per-slot
        /// margin formula scaled by Quality(ℓ) with no market on the other side
        /// to bound it, where the residential ladder is bounded by the auction's
        /// posted price and its shadow queue (LandAccounting.BidPerUnit drops
        /// `realized`, `addUnits` and `minSupply` on the firm branch because
        /// none of them has a meaning there). Charging best-permitted-use at 95%
        /// capture against a corner ℓ* exceeds what a firm below that level can
        /// earn; a vacated parcel does not re-let either, because firm entry
        /// compares a bid at the parcel's CURRENT level against an assessment
        /// priced at ℓ*. So a correction that raises assessment where it was too
        /// low kills the firms it lands on, and enforcement turns the standing
        /// gap into exits.
        ///
        /// Measured ON, this commit's build: firms' uncollected share 64% → 21%
        /// and no firm sits past the clock, but `webersweep` (seeds 0-7) reads
        /// 0/8 against 6/8 at 88fc88c — every failure on the EXTRACTOR
        /// POPULATION precondition (1 to 4 extractors against the ≥5 bar) with
        /// the alignment legs the check exists for still at 100% — standing
        /// offices go 62 → 8, and verify seed 9 loses the same check (48/51
        /// against 51/51). That is vanilla's land-value death spiral (design §2)
        /// reappearing on the firm side.
        ///
        /// ATTRIBUTED, not guessed: `webersweep --seeds 2` was run once per
        /// mutant on the ungated build. All four behaviours on — 3 and 1
        /// extractors, both red. Item 1 alone removed (--mutant-flat-geology,
        /// items 2-4 still on) — 10 and 8 extractors, seed 1 green and seed 0
        /// red on the industrial DIVERSITY precondition only (1 distinct
        /// output). Item 2 alone removed — 3 and 1, red. Item 3 alone removed —
        /// 1 and 0, red. All removed — 9 and 9, both green. So the extractor
        /// collapse is ITEM 1: pricing ore land at its real geology is correct
        /// and is exactly what the sector cannot carry at 95% capture against a
        /// corner ℓ*, which is the same wall items 3 and 4 hit from their own
        /// sides. One finding, one switch.
        ///
        /// THE OWED WORK, named: the non-residential ladder needs the bound the
        /// residential one has — a realized comparable per (cluster, sector,
        /// level) over OTHER firms, which is the same assessment-comparables
        /// pattern this switch's item 2 installs on the goods side. Until that
        /// exists this ships built, checked on both arms, and off.</summary>
        public bool NonResLandParity = false;

        /// <summary>Whether a developer's staffing forecast lets a cluster's own
        /// hiring record outweigh the no-evidence prior
        /// (LandAccounting.FirmFillEstimate carries the rule and the
        /// measurement). OFF because it moves a shipping default: with it off
        /// the estimate is the old `Clamp(0.35 + 0.65 * fill, 0.35, 1.0)` bit
        /// for bit, so the arm can be measured before anything is flipped.
        ///
        /// What it fixes: a third of built non-residential parcels hold firms
        /// that employ nobody, because the 0.35 floor let a developer clear its
        /// hurdle on staff the site could never attract. Measured at those
        /// clusters, the old estimate reads exactly 0.350 while realized fill
        /// is 0.000.</summary>
        public bool FillEvidenceWeighting = false;
        /// <summary>Probe-scoped experiment control (--fill-office-prior):
        /// FillEvidenceWeighting applies to commercial, industrial and
        /// extractor while OFFICE keeps the old prior rule. Exists to isolate
        /// the measured office regression — under the full mutant office reads
        /// 10 firms at +319 and under full evidence 1 firm at −43, but the
        /// mutant flips all four sectors at once, so which sector's treatment
        /// causes it was untested. Never a shipping mode; a forecast rule that
        /// is sector-selective about believing evidence is a control arm, not
        /// a model.</summary>
        public bool FillEvidenceOfficeExempt = false;
        /// <summary>Whether office and extractor configuration forecasts carry
        /// the Quality(level) premium ONLY in the world whose production
        /// function delivers it (--assess-deliverable; OFF because it moves a
        /// shipping default). The engine scales those two sectors' realized
        /// output by Quality(ℓ) only under NonResLandParity — the extractor
        /// production comment says in words why ("the land is charged for a
        /// level premium the production function does not deliver") — but
        /// FirmBidPerSlot's office and extractor branches multiply by quality
        /// UNCONDITIONALLY, so on the default path the assessor prices a
        /// production function the world does not run. Measured consequence,
        /// office-isolation arms (seed 1, 400 ticks): entrant offices in L5
        /// towers billed 460–471/tick against a GROSS revenue ceiling of
        /// ~442 at full staff — insolvent at zero wages, by construction.
        /// Industrial and commercial are untouched: their realized output is
        /// quality-scaled unconditionally, so their forecasts already match.
        /// With parity ON this flag is a no-op by design.</summary>
        public bool AssessDeliverableQuality = false;
        /// <summary>ONE productivity rule for all four non-residential sectors,
        /// applied at all THREE sites that price a slot: the land bid
        /// (LandAccounting.FirmBidPerSlot), the labor auction's door cap
        /// (LaborAuction's per-sector MRP), and realized production
        /// (EconomyEngine). Ships OFF because it moves a shipping default.
        ///
        /// WHY IT EXISTS. Audited across those three sites, the four sectors do
        /// not run one system or even two — industrial is the only one coherent
        /// end to end (cond x Quality(l) everywhere), while OFFICE gives three
        /// different answers at its three sites (bid: level premium; door cap:
        /// neither term; production: both, but only under NonResLandParity) and
        /// EXTRACTOR gives two. On the shipping default an office's output
        /// therefore ignores its building's CONDITION entirely, which no other
        /// sector does.
        ///
        /// WHY IT MATTERS BEYOND TIDINESS. A parcel's land value is a maximum
        /// over the configurations its zoning permits. That maximum is only
        /// meaningful if the candidates are commensurable — if office and
        /// extractor price a slot level-free while industrial and commercial
        /// price it at Quality(l), then on any plot where several uses are
        /// allowed the winner is decided by which formula each sector happens
        /// to be wired to, not by which use is worth more there. The four have
        /// to fight on the same terms for the maximum to mean anything.
        ///
        /// WHAT IT DOES: every sector's per-slot productivity carries
        /// condition x Quality(l)/Quality(1) at every site, unconditionally.
        /// Commercial already complies (its capture mass is units x cond x
        /// Quality(l)); industrial already complies; this brings office and
        /// extractor into line and makes AssessDeliverableQuality redundant —
        /// that flag patched the FORECAST to match a production function that
        /// was itself the anomaly. MutantLevelFreeFirmOutput remains the
        /// falsifier for the level term at all sites.</summary>
        public bool UniformSiteProductivity = false;
        /// <summary>Whether the developer's construction scan may also price
        /// BUILT parcels whose standing building is economically dead
        /// (--redevelop; OFF because it moves a shipping default). Scope is
        /// the assessment's own verdict, not a condition threshold: only
        /// non-residential, unoccupied, unowned, non-warehousing parcels with
        /// CurrentResidual <= 0 are admitted, and the return gate divides by
        /// the FULL project cost (replacement + demolition - salvage).
        ///
        /// This is the #56 fix, and the mechanism choice is the point. The
        /// only demolition path before it — Leveling's scrape — finances from
        /// the parcel's own escrow, and a derelict parcel's escrow is fed by a
        /// wedge on a land value that is ~zero BECAUSE the parcel is derelict:
        /// measured, 0/65 ruins had TargetIsScrape set and 0/65 could fund
        /// (mean escrow 0.0 against a mean 12,419 bill). The developer already
        /// commits its OWN capital against its OWN forecast on empty land;
        /// this admits a site whose project cost includes clearing a dead
        /// building, which breaks the self-financing circularity without a new
        /// funding mechanism or a new constant.</summary>
        public bool DerelictRedevelopment = false;
        /// <summary>Consecutive ticks a firm may fail to meet its land charge in
        /// full before it releases the parcel — the firm side of the same floor
        /// the household pipeline defines, and deliberately the SAME clock: a
        /// household reaches its exit at StressTicks > 3 × InsolvencyGraceTicks,
        /// which is 54 ticks on the shipped grace. Parity is the whole rationale
        /// for the value, so it is written as the product rather than as an
        /// independent number. UNSWEPT: no sweep has been run over this clock;
        /// the population it acts on is in the parityprobe run recorded with
        /// this commit.</summary>
        public int LandArrearsTicks => 3 * InsolvencyGraceTicks;
        public double ConsumptionCutFactor = 0.6;
        public double EmigrationMoveCost = 40.0;
        public double ShelterCapacityShare = 0.015; // of population

        // ---- consumption ----------------------------------------------------
        public double BaseConsumptionShare = 0.80; // of after-housing income, spent at commercial
        public double MovingCostMean = 25.0;
        public double OwnerMovingCostMult = 2.2;   // owner-tagged margins are larger (§4.4)
        /// <summary>Probability a Family-lifecycle household is OwnerMinded
        /// (drawn at birth, Household.DrawAtBirth). The 0.35 that lived as a
        /// literal in the engine's posted-path arrival loop, promoted to a
        /// name so the auction path's arrivals draw it too. Value unchanged.</summary>
        public double OwnerMindedShare = 0.35;
        /// <summary>Scale on the owner's ask (item #41). Exists for
        /// measurement, not taste; 1 is the shipped behavior.
        ///
        /// WHAT 0 IS, EXACTLY: with the fold rule (a door exists where an ask
        /// bids), a zero ask never clears its own structure floor, so at 0 NO
        /// owner door unfolds and the auction's index space is the pre-item
        /// one. The 0 arm therefore isolates the tag/disposition bookkeeping
        /// — the owner tag naming a household, the birth-drawn disposition,
        /// the seeding claim — and NOT the door structure. There is no
        /// "doors without asks" arm because there is no such configuration:
        /// an unbinding ask prices its door within its own floor of the
        /// pooled one, which is exactly what folding says. Attribution runs
        /// both ways round that: tip → scale 0 is the bookkeeping, scale 0 →
        /// scale 1 is the doors and their asks together (auctionprobe
        /// --ticks 300 --seed 20260806, both arms, at the item commit).
        ///
        /// Values above 1 stay inside the min(·, R_i) clamp in PostOwnerAsks
        /// — an owner can always afford to match its own floor — with one
        /// boundary case a sweep should expect: at share ≥ 1/scale the clamp
        /// binds, A = R exactly, and an owner whose own door is its best
        /// option holds surplus exactly equal to its outside option — which
        /// the solve declines (`bestSur <= _outside[i]`, RunAuction).</summary>
        public double OwnerAskScale = 1.0;
        public double OutsideShopMinutes = 40.0;   // outside option in the shopping logit
        public double OutsideShopMass = 60.0;      // (uncaptured spending leaks outward)

        // ---- commercial service capacity (StoreLevelSpending path) -----------
        /// <summary>Services sold per FILLED job slot per tick, at level-1
        /// quality and condition 1. Commercial's production coefficient — the
        /// same kind of technology constant as ExtractorOutputPerSlot,
        /// OfficeOutputPerSlot and recipe.OutputPerSlot, and the term commercial
        /// was missing: a shop's takings are min(custom presented, staff × this
        /// × cond × quality), so takings are bounded by the staff the shop
        /// actually has.
        ///
        /// This is a SERVICE capacity — staff-hours — and NOT an inventory.
        /// Restocking is same-tick and proportional to takings
        /// (EconomyEngine.ProductionAndTrade), so nothing stocks out; what runs
        /// short is the ability to serve. A shop's ATTRACTIVENESS to a shopper
        /// stays its building (JobSlots × cond × quality, what ChooseShops
        /// already uses — a shopper sees shelf space, not the roster); its
        /// THROUGHPUT is its staff. The gap between the two is the mechanism,
        /// and it is what makes hiring worth money.
        ///
        /// PICK: 60, between the p75 and the p90 of the presented-per-FILLED-slot
        /// distribution measured with capacity switched off
        /// (`shopprobe --seeds 4 --service 1e9`, task #20: p25 35.8, p50 45.2,
        /// p75 55.3, p90 65.3, p99 85.7, max 115.6 over 40k firm-ticks), so
        /// capacity binds on the busiest shops and not on the median. Measured
        /// consequence at this pick (`shopprobe --seeds 4`, 4 seeds × 300
        /// ticks): the bound binds on 8.8% of 128240 commercial firm-ticks and
        /// 1.8% of presented custom is turned away, while the sector's capacity
        /// at full staffing is 1.40× the spending presented to it — above 1, so
        /// what is turned away is siting and staffing rather than a sector-wide
        /// shortfall. Swept
        /// {0.5×, 1×, 2×, 4×} — the sweep is in impl-20-commerce.md.</summary>
        public double CommercialServicePerSlot = 60.0;
        /// <summary>Rounds of pro-rata rationing over each household's OWN
        /// shortlist. K = 1 is exactly the defaults rule (turned away from your
        /// chosen shop → out of town, which is your own default); K &gt; 1 lets
        /// the household improve on that default by walking to its own
        /// next-best shop, and never places it below the default because the
        /// shortlist only ever holds shops it ranks above its own realized
        /// outside option.</summary>
        public int ShopRationingRounds = 2;
        /// <summary>How many shops a household ranks. Must be ≥
        /// ShopRationingRounds. ChooseShops already scores every live shop for
        /// every household, so keeping the top few costs one insertion each.</summary>
        public int ShopShortlist = 3;

        // ---- counted shop intents (StoreLevelSpending entry signal) ----------
        /// <summary>Bins in the per-cluster size histogram of counted shop
        /// intents. Each settled household contributes, per cluster, the SIZE
        /// M* above which a hypothetical shop there would systematically beat
        /// what that household actually settled for; the histogram is over
        /// log M*, so a developer reads a demand curve in size.
        ///
        /// The read is the CONSERVATIVE cumulative — only bins whose whole range
        /// is beaten — so the discretization error is one-sided and a developer
        /// never over-counts, which is the safe direction given that the defect
        /// being removed was over-promising.
        ///
        /// PICK: 96. Measured against an EXACT unbucketed read of the same
        /// intents, averaged over four read sizes (`shopprobe --seeds 4`,
        /// task #20): city-aggregate under-count 24 bins 13.97%, 96 bins 2.06%,
        /// 192 bins 1.59% — 96 is where the curve flattens. The averaging over
        /// sizes is not decoration: at a single fixed read size the metric is
        /// degenerate, because bin counts 12/24/48 place a boundary at the same
        /// point below log 6 and all three then report an identical 4.52%.</summary>
        public int IntentProbeBins = 96;
        /// <summary>Log-mass range the histogram covers. Realized shop mass
        /// (JobSlots × cond × quality) over live shops spans 1.60–10.92
        /// (log 0.47–2.39; `shopprobe --seeds 4`, task #20) and the entry probe
        /// reads at 6 × quality, so the range carries well over a decade of
        /// margin on each side. Intents falling past the top are expected and
        /// legitimate — remote clusters no shop could win — and are counted
        /// separately (AccessState.IntentOverflow); what would be an error is a
        /// READ landing there, and none does.</summary>
        public double IntentProbeLogMassLo = -2.5, IntentProbeLogMassHi = 6.0;
        /// <summary>Minimum number of individual households a read must rest on
        /// before it is anything but zero. One household's basket is not a
        /// place's retail forecast — the #30 thin-market argument transposed to
        /// the entry field, and zeroed rather than shrunk toward a prior because
        /// the safe direction here is under-promising.
        ///
        /// PICK: 5. Measured head counts BACKING the reference read
        /// (`shopprobe --seeds 4`, task #20, over 784 cluster-observations):
        /// min 0, p05 3, p10 4, p25 6, p50 9, max 26. So the floor excludes
        /// roughly the bottom decile — clusters whose whole retail case rests on
        /// four households or fewer — and leaves every cluster with real
        /// catchment reading. Counted AT THE READ'S SIZE, never per cluster:
        /// WShop is positive everywhere, so a per-cluster head count is just the
        /// population (1690–3772 of ~8000 at every cluster, same run) and would
        /// gate nothing.</summary>
        public double IntentHeadFloor = 5.0;

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
        /// <summary>Carry the previous solve's repair columns into the next one.
        /// OFF: it makes the solve path-dependent — two solves of the same world
        /// no longer agree — and measured WORSE, not just different (1281
        /// envious households against 0, and an improving swap). The speed it
        /// was reaching for came from somewhere else anyway: caching the
        /// level-independent half of each valuation took the same run from 6m35s
        /// to 1m31s on its own, and that is a pure-function optimization.</summary>
        public bool AuctionWarmStart = false;
        /// <summary>Idiosyncratic taste for a specific place, as a fraction of
        /// the household's own housing budget (Gumbel, σ ≈ 1.28, so this is
        /// roughly the ± swing at one standard deviation).</summary>
        public double AuctionTasteScale = 0.15;
        /// <summary>Bid increment. Every eviction lifts a submarket's admitted
        /// price by at least this, which is what makes the ascent finite; the
        /// result is an ε-equilibrium (nobody envies another's place by more
        /// than ε).</summary>
        public double AuctionEpsilon = 0.002;
        /// <summary>ε as a fraction of the value being bid for. The binding one
        /// in practice: a flat ε has to climb from the reserve to the top of the
        /// market in fixed steps and pays for every one, while a proportional ε
        /// bounds residual envy at a fixed fraction of what a place is worth.</summary>
        public double AuctionEpsilonRel = 0.005;
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
        /// <summary>Hard ceiling on a household's housing bid as a share of its
        /// own income — the bid-rent budget constraint. Above its own
        /// MaxRentShare because a household stretches for somewhere it really
        /// wants; below 1 because it still has to eat.</summary>
        public double MaxRentOfIncome = 0.55;
        /// <summary>How many individuals the REGION sends to look at the city
        /// per tick, before prominence. Region-side only: it must not read any
        /// measure of how good the city is, or it becomes the citywide
        /// attractiveness scalar again wearing a different hat.</summary>
        public double RegionOfferRate = 2.5;
        /// <summary>Mean rent share, used only to recover a prospect's income
        /// from its housing budget when deriving its ability to pay.</summary>
        public double ProspectRentShareForCap = 0.30;
        /// <summary>n0 in AccessState.ProspectLocalOdds: the prior weight (in
        /// workers) pulling a cluster's local employment rate toward the
        /// citywide worker-weighted bench when a prospect prices its odds
        /// there. Calibrated against the measured per-cluster worker-count
        /// distribution on the auction reference fixture (10×10, 3000
        /// households, seeds 0–1, t=40..240): occupied clusters hold min
        /// 0.5–2, p25 2–5, median 4–9, p90 13–60, max 84 workers by class.
        /// 4 ≈ p25–median: sparse clusters read as the city, real job centres
        /// read as themselves.</summary>
        public double ProspectOddsPriorWeight = 4.0;
        /// <summary>Employment odds in the OUTSIDE region, where a prospect's
        /// reservation lives. A global, and a legitimate one: it is genuinely a
        /// property of the outside world, not a stand-in for anything local —
        /// the default is staying outside, and how this city is doing does not
        /// change what staying outside is worth. Set to 1 − NeutralUnemployment
        /// (0.08, vanilla's m_NeutralUnemployment): the region at its neutral
        /// unemployment rate.</summary>
        public double OutsideEmploymentOdds = 0.92;
        /// <summary>The OUTSIDE region's access level, in the same units as
        /// AccessState.AccessValue — the third demotion of a citywide read to a
        /// property of the outside world, after the outside wage
        /// (OutsideWageMult) and OutsideEmploymentOdds. The outside option is
        /// ONE MORE DOOR priced by the same premium rule every city door uses
        /// (AccessState.OutsidePremium), so a uniformly better city raises
        /// MeanAccess, the outside door's relative premium falls, and the
        /// come/stay margin responds — while within-city allocation is
        /// untouched (the MeanAccess normalizer is shared by all city doors and
        /// cancels across them).
        ///
        /// Value: settled fixtures measure MeanAccess in a narrow band —
        /// 18.4–20.6 across the boombust (14×14/6000, t=300), canary
        /// (10×10/3000, seeds 0/1/9/13/25, t=160) and 8×8/2000 fixtures
        /// (item-#42 probe run, seed 20260806) — so 20 puts the outside region
        /// at the access level of a settled reference city (relative premium
        /// ≈ 1 on the reference fixtures, the middle of the clamp's responsive
        /// band). Swept over the boombust fixture at {14, 17, 20, 23, 26}
        /// (item-#42 sweep run; inflow-margin response by value recorded in
        /// KNOWN-RED's boombust row); outside that fixture the value is
        /// unswept.</summary>
        public double OutsideAccessValue = 20.0;
        /// <summary>MUTANT SWITCH, harness-only (see the uniform-pulse verify
        /// check): restores the defect item #42 removed — the outside door's
        /// access anchored on the city's OWN MeanAccess, so its relative
        /// premium is a constant and a spatially uniform improvement is
        /// structurally invisible to the come/stay margin. Exists so that
        /// check stays falsifiable; never a shipping mode. The per-instance
        /// field takes its value from the process-wide default so a whole
        /// FIXTURE can be run mutated from the CLI (`--mutant-relative-outside`,
        /// how the boom/bust scenario's mutant arm is measured); the verify
        /// check still sets the field directly on its own mutant arm, and the
        /// clean arm reads this default on purpose, so flipping the shipped
        /// default reds that check too.</summary>
        public bool MutantRelativeOutsideAccess = MutantRelativeOutsideAccessDefault;
        /// <summary>Process-wide default for <see cref="MutantRelativeOutsideAccess"/>
        /// (`--mutant-relative-outside`). Never set outside the harness.</summary>
        public static bool MutantRelativeOutsideAccessDefault = false;
        /// <summary>How long a household that would rather be elsewhere waits
        /// before actually going. Its own patience, scaled by its own moving
        /// cost at the use site.</summary>
        public int DeclinePatienceTicks = 40;
        /// <summary>Cap on column-generation rounds: each one shows every
        /// envious household the place it wishes it had been offered, then
        /// clears again. This is a BACKSTOP, not a target — the loop normally
        /// runs out of violations around round 6 and stops there. It has to be
        /// comfortably above that: at a cap of 4 the loop ended on a re-solve
        /// nobody scanned and left 2–6 households per seed envious by up to 2%
        /// of value, and at 8 it reached zero envy but still could not say so,
        /// because a cap that binds means the last solve was never checked.
        /// Zero disables the repair entirely and leaves the result an
        /// equilibrium only over the initial shortlists.
        ///
        /// Raised 12 -> 16 with the full-re-clear repair rounds: each round now
        /// rebuilds the whole market, so a solve needs as many rounds as its
        /// longest column-generation chain, and seed 22 measurably hit the cap
        /// at 12 (converged False, clean False) while 16 cleared it.
        ///
        /// Raised 16 -> 20 with owner doors (item #41): owner doors are
        /// discovered by the repair scan on purpose (they never enter the
        /// price-free opening walk), which lengthens the longest chains. The
        /// whole canary distribution shifts — max rounds over the 39 seeds
        /// 11 -> 16 — and seed 16 is the binding one, measured three ways at
        /// the item commit: 9 rounds clean before the doors, 16 rounds with
        /// them and its 17th scan clean (the cap-20 canary), and converged
        /// False at cap 16 with envy already 0 (`canary --from 16 --seeds 17`
        /// on a cap-16 build — pure cap binding, not a defect). So 16 bound
        /// by one; 20 carries the same +4 the 12 -> 16 raise did, and no
        /// headroom beyond that is claimed; Converged is the arbiter and the
        /// canary sweeps it.</summary>
        public int AuctionRepairRounds = 20;
        // ---- labor assignment market (LaborAuction; Flags.LaborAuction) ------
        /// <summary>Money per generalized commute minute per earner per tick —
        /// what a worker's own commute from its own home subtracts from a
        /// door's total comp when it values the door. At 0.05, a 20-minute
        /// commute costs 1.0/tick against a basic-class mean wage of 10.
        ///
        /// NOW SWEPT — `laborprobe --commute-cost {0.00,0.05,0.20,0.50}` on the
        /// pinned labor fixture. This is also the term the outside option is
        /// netted of (`wage × OutsideWageMult − CommuteCostPerMinute ×
        /// minutes-to-the-nearest-exit`), so the sweep answers whether the
        /// fixture's outside-worker share is an artifact of a near border:
        ///
        ///   cost   mean outsideNet  outside share  unemp
        ///   0.00      11.77           0.604        0.000
        ///   0.05      11.24           0.589        0.000
        ///   0.20       9.51           0.562        0.002
        ///   0.50       5.81           0.363        0.095
        ///
        /// Making the border FREE — the strongest possible "the border is next
        /// door" case — moves the share by 1.5 points. The border commute is
        /// not what puts most of this fixture's workers outside; door supply is
        /// (OutsideWageMult's comment carries that table). Default unchanged:
        /// nothing in the sweep argues for a different price of a minute, and
        /// the 0.50 arm is a 10× commute cost rather than a calibration
        /// candidate.</summary>
        public double CommuteCostPerMinute = 0.05;
        /// <summary>The cross-border wage as a fraction of the class mean. On
        /// the labor-auction path the global class wage DEMOTES to what it
        /// truly is: the world price of labor outside the region — a genuine
        /// property of the outside world, legitimate as a global. Working
        /// outside is the always-available default the auction must beat per
        /// individual. Chosen so both inside employment and outside work occur
        /// on the verify fixture (measured at the labor bring-up run, seeds
        /// 0-3).
        ///
        /// NOW SWEPT — `laborprobe --outside-mult {0.30,0.45,0.55,0.70,0.85}` on
        /// the pinned labor fixture (8×8 / 1500 / 120 ticks, seed 1). The
        /// outside-worker share responds strongly and monotonically:
        ///
        ///   mult   workers  door slots  slots/worker  outside  unemp  T/cap
        ///   0.30    1247      992         80%          0.065   0.215  0.238
        ///   0.45    1345      999         74%          0.338   0.051  0.305
        ///   0.55    1537      926         60%          0.490   0.001  0.333
        ///   0.70    1802      942         52%          0.589   0.000  0.366
        ///   0.85    1755      954         54%          0.621   0.000  0.402
        ///
        /// Read the last two columns together with the third. The wage share of
        /// marginal product RISES with the outside wage because the outside
        /// option is the worker's side of the bargain — that is the mechanism
        /// working. What the same table shows is that the shipped 0.70 sits
        /// past the point where the city's own unemployment reaches zero, and
        /// zero is not a market outcome here: LaborAuction.Why labels every
        /// unmatched worker `Outside` whenever its outside net beats its own
        /// leisure floor (11.24 vs 2.77 at the default), so `unempShare` is
        /// pinned to zero at any mult ≥ ~0.55 and carries no information about
        /// whether city work was available.
        ///
        /// NOT CHANGED, and the reason is the charter's globals rule: this is a
        /// property of the outside world, so calibrating it to make the CITY's
        /// unemployment look right would be fitting a global to a local
        /// outcome. What the level would need is an outside-world anchor, and
        /// none is measured. What IS measured, and is the fixture caveat any
        /// reader of the 0.589 needs: the pinned 8×8 fixture builds job slots
        /// for 52% of its workers, so at least 48% of them are outside at ANY
        /// outside wage that beats leisure. See CommuteCostPerMinute for the
        /// border-distance half of the same question.</summary>
        public double OutsideWageMult = 0.7;
        /// <summary>Worker-side lump for changing employer, scaling the
        /// household's own MovingCostDraw. Folded worker-side together with
        /// OnboardingCostMean: the auction decides only the ALLOCATION, and
        /// under quasilinearity the incidence of a match-specific friction
        /// does not change it — so both sides of the friction are priced on
        /// one side. Default unswept.</summary>
        public double JobSwitchCostMult = 1.0;
        /// <summary>Firm-side cost of onboarding a new member, folded into the
        /// worker's stay bonus (see JobSwitchCostMult for why one side
        /// carries both). Default unswept.</summary>
        public double OnboardingCostMean = 25.0;
        /// <summary>Doors a worker household carries into the labor auction,
        /// ranked price-free, floored by its own outside option, its current
        /// employer always included. Seeded from AuctionShortlist; unswept
        /// for labor.</summary>
        public int LaborShortlist = 12;
        /// <summary>Column-generation cap for the labor auction — same
        /// contract as AuctionRepairRounds (a backstop; RepairClean is the
        /// arbiter). Heavy excess labor makes the column-generation chains
        /// far longer than housing's (each losing worker must be shown
        /// enough doors to price its default honestly). Measured at the
        /// per-earner fix round's 39-seed laborcanary sweep (39/39): clean
        /// scans in 30-41 rounds (seed 25 the longest at 41; the bring-up
        /// cap of 40 bound on seed 4, clean False there). 64 is the
        /// backstop, not a target; the canary is the cheap watch for a
        /// seed that binds it.</summary>
        public int LaborAuctionRepairRounds = 64;
        /// <summary>Bid increment for the labor auction's ascent (run in
        /// p = cap − T space). Same role as AuctionEpsilon. Seeded from the
        /// housing value; unswept for labor.</summary>
        public double LaborAuctionEpsilon = 0.002;
        /// <summary>ε as a fraction of the value being bid for — the binding
        /// one in practice, as in housing. Seeded; unswept for labor.</summary>
        public double LaborAuctionEpsilonRel = 0.005;
        /// <summary>Bids per worker (per re-clear) before the labor solve
        /// gives up and reports Converged = false. The exact-set bring-up
        /// regime needed 400 (its displacement re-bid the whole excess pool
        /// as each door's comp descended); per-earner unit demand with the
        /// ε-entry price does not — measured at the per-earner fix round's
        /// 39-seed laborcanary sweep, converged re-clears spend 13-17
        /// bids/worker. 64 is the backstop, not a target, and small enough
        /// that a future livelock reports Converged = false in seconds
        /// rather than minutes.</summary>
        public int LaborBidBudget = 64;
        /// <summary>HARNESS-ONLY MUTANT of the Ward rule this design refuses
        /// to encode: the firm caps admissions at the point where forecast
        /// per-member income falls. With a fixed working-capital pool, any
        /// admission beyond the incumbent membership dilutes the members'
        /// claim on it, so the member-income-maximizing cap IS the incumbent
        /// membership (floored at one slot so an empty firm can hire a first
        /// member). Under this mutant the refusal check
        /// ("no surplus-positive hire is refused") MUST go red — a check that
        /// cannot fail is not a check. Never set outside the harness.</summary>
        public bool LaborWardMutant = false;

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
        /// <summary>Owner parcels are their own auction doors (item #41): an
        /// owner-tagged res-low parcel whose ask exceeds its own structure
        /// floor holds its units at a per-parcel door whose reserve is
        /// max(own condition floor, its owner's ask), instead of pooling into
        /// the (density, cluster, level) submarket. OFF returns the auction to
        /// pooled doors exactly — partition, reserve, home-key binding and the
        /// ask hook all branch on it — but NOT to the pre-item world: the owner
        /// tag names a household and is drawn at birth whatever this flag says,
        /// which is what the tag needed to stop decaying. Posted arms never
        /// construct an auction and see only that tag change.</summary>
        public bool OwnerDoors = true;
        public bool ShadowAccountingOnly = false;// stage 3: assess + log, levy nothing
        /// <summary>THE FIRM EXIT MARGIN (Dixit). A firm whose own read of
        /// revenue-less-avoidable-cost stays negative past its patience gives up
        /// its site and leaves — after first trying the site it could carry.
        ///
        /// OFF BY DEFAULT, and the reason is a check, not caution. The
        /// nonres-parity fixture's OFF-arm FLOOR leg asserts that firms really
        /// do sit past the arrears clock on the shipped default
        /// (`aOff.stuck >= 1`) — that leg states the defect and is the
        /// non-degeneracy floor for its partner. A cash-flow exit kills exactly
        /// that population, so turning this on by default would empty a floor
        /// leg's population and red it. The flip is a separate, evidenced step
        /// that has to rewrite that leg first; see KNOWN-RED.
        ///
        /// WHAT IT IS NOT: it is not a rule that names taxes or missing workers
        /// as reasons to fail, which is how the base game triggers company
        /// relocation. Both of those raise the exit hazard here because they
        /// drain the margin — a taxed firm pays more, an unstaffed firm earns
        /// less — and neither appears anywhere in the trigger. That is the
        /// difference between a hazard that emerges and a hazard that is
        /// declared.</summary>
        public bool FirmExitMargin = false;
        /// <summary>Offices carry a SPECIALIZATION, and each draws its
        /// agglomeration from its own kind's jobs rather than from office jobs
        /// pooled. Turns "the best office that could stand here" from a maximum
        /// over one candidate into a maximum over three, which is what the
        /// assessment has always claimed to be taking.
        ///
        /// OFF BY DEFAULT while it is measured. With it off,
        /// AccessState.OfficeAgglom(kind, c) returns the pooled multiplier for
        /// every kind, so the maximum is the old single value and every office
        /// bid, assessment and revenue is arithmetically unchanged — which the
        /// fingerprint is what checks, not this comment.</summary>
        public bool OfficeSpecializations = false;
        /// <summary>Shops retail ONE basket line rather than all of them, and
        /// each line's spending is captured only by the shops that sell it.
        ///
        /// The second half is not decoration, it is the whole mechanism. A shop
        /// that specializes without per-line capture is choosing purely on
        /// basket share, and the shares (food 0.16 against timber 0.03 of a
        /// 0.30 basket) span 5x while the delivered-cost term they would
        /// compete against varies by about 13% across clusters — so every shop
        /// in the city would sell food, which is one company type wearing four
        /// names. Partitioning capture is what makes a niche pay: a cluster
        /// thick with grocers leaves its machinery spending unserved, and the
        /// shop that sells machinery there takes all of it.
        ///
        /// OFF BY DEFAULT while it is measured. With it off every shop carries
        /// Res.Services, capture is the single undivided pool it has always
        /// been, and restocking is the whole basket — the arithmetic is
        /// untouched, which the fingerprint checks rather than this comment.
        /// The store-level consumption path is NOT covered yet: it routes each
        /// household to one shop rather than distributing a pool, so per-line
        /// capture there is a different change, and it is named in KNOWN-RED
        /// rather than silently half-done.</summary>
        public bool CommercialLines = false;
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
        /// THE ENTRY GAP IS NOW CLOSED, AND THE FLAG STILL DOES NOT FLIP. Task
        /// #20 replaced the two things this comment was waiting on. Commercial
        /// production gained the linear-in-labor relation the other three
        /// sectors already had, so a shop's takings are bounded by the staff it
        /// actually has (CommercialServicePerSlot) and the labor auction's
        /// commercial door cap became a technology ceiling instead of an EMA of
        /// realized takings. And the phantom-entrant capture field was replaced,
        /// on this path only, by a per-cluster histogram of COUNTED individual
        /// shop intents (AccessState.CountShopIntents).
        ///
        /// What that bought, measured (`shopprobe --seeds 4`, 300 ticks, against
        /// `firmdiag` at 58cab48 on the same seeds): on this path commercial
        /// firms alive 98.0 → 103.5 and commercial-parcel vacancy 42% → 39%,
        /// against a pooled path this item leaves BIT-IDENTICAL at 130.2 alive
        /// and 26% vacancy. The gap narrowed; it did not close. And it was not
        /// free: commercial deaths rose 44.2 → 77.5, concentrated entirely in
        /// entry — 141 of 161 shops born mid-run die within 40 ticks, against
        /// 0 of 48 on the pooled path.
        ///
        /// THAT ENTRANT DEATH MODE WAS A DEFECT, AND IT IS DIAGNOSED AND FIXED.
        /// Reproduced at the two-track merge (`shopprobe --seeds 4`, 300
        /// ticks): 137 of 153, 89.5 %, against 0 of 40 pooled. `entrydiag`
        /// separated the three candidates on the same run:
        ///  - NOT a cold start. 130 of the 132 that died young had taken
        ///    custom, and the first customer lands at age p50 5 — the refresh
        ///    grid, not a starving shop. (The A3 cold-start rules are not even
        ///    reachable in the shipping calibration: both live in
        ///    LaborAuction.BuildDoors, and LaborAuction ships false.)
        ///  - NOT crowding. Dying and surviving entrants alike arrive at
        ///    clusters with a median of 0 other shops, and only 28 of 143
        ///    shared a refresh window with another entrant.
        ///  - THE FORECAST. The entry decision read the counted field at a
        ///    fixed CLUSTER reference mass — a 6-slot condition-1 shop — while
        ///    the market that generates its catchment scores the building it
        ///    would actually occupy. Entrants take over standing buildings
        ///    whose condition has decayed, and condition multiplies the mass a
        ///    shopper sees. Measured: the reference read is 1.63× the same
        ///    field asked at the entrant's own mass at the median and NEVER
        ///    smaller (own/reference p10 0.374, p50 0.612, p90 1.000). Against
        ///    what the entrant actually took over its own first 40 ticks the
        ///    reference read over-predicts 3.4× (p50 0.293) where the own-mass
        ///    read over-predicts 2.05× (p50 0.488, p90 1.260) — the residue
        ///    being the probe's stated taste-blindness.
        /// The fix is the charter's own answer: make the forecast honest, do
        /// not subsidise the entrant. LandAccounting.FirmBidPerSlot's
        /// store-level commercial leg now reads the field at the mass a shopper
        /// sees at THIS parcel and divides by THIS parcel's slots, with
        /// condition priced once (the caller's CondFactor is skipped where the
        /// catchment already carries it). Measured on the same command:
        /// entrants dying inside 40 ticks 137/153 → 7/13, commercial deaths
        /// 77.5 → 42.5, with alive 101.5 → 100.2 and vacancy 40 % → 41 %. The
        /// churn was the entire cost; the standing sector did not pay for it.
        ///
        /// WHAT THE FIX EXPOSES, and it is not this item's to fix: a commercial
        /// parcel that falls vacant is an absorbing state. It pays no S, so
        /// ConditionDecay walks it to the 0.05 floor, and the vacancy drain
        /// empties the escrow Leveling would have restored it from. Under the
        /// dishonest read those buildings were continuously re-occupied by
        /// firms that died in ~9 ticks; under the honest one nobody takes them
        /// and they stay vacant. That is why the vacancy number does not close.
        ///
        /// WHY THE FLIP IS STILL NOT DECIDABLE, and this is the measured
        /// blocker rather than caution: a flip inventory needs checks that can
        /// SEE the change, and at the start of this item TestRunner.cs contained
        /// zero occurrences of "Commercial", "shop", "retail" or "capture"
        /// outside three lines discussing MUT-L and known limit 4, against 26
        /// for "residential" — and both ledger arms ran this flag false. Every
        /// commercial verdict would have classified as "unchanged by
        /// construction" and meant nothing. This item lands that coverage: two
        /// checks, ten legs, each with a mutant run red. The inventory is the
        /// next round's work, and it now has instruments.
        ///
        /// WHAT THE CENSUS GAP IS, per number, since two numbers moving in
        /// opposite directions are not one band. Both are the same mechanism
        /// seen twice, and it is the pooled path's, not this one's:
        ///  - ALIVE 100.2 against 127.0. The pooled rule hands every live
        ///    commercial firm a share of citywide spending proportional to
        ///    JobSlots × CaptureIncumbentPerMass, so a shop nobody would walk
        ///    into still earns. Its own tell is that 0 of 40 mid-run entrants
        ///    die there within 40 ticks: the pooled path cannot kill a badly
        ///    sited shop at ANY parameter setting, which is the same structural
        ///    property the discrimination leg asserts about its entry field.
        ///  - VACANCY 41 % against 27 %. The same fact from the parcel side —
        ///    if any occupant earns, every parcel is worth occupying — plus the
        ///    absorbing state above.
        ///
        /// THE FLIP INVENTORY RAN ON THAT COVERAGE, AND DECIDED: STAYS FALSE.
        /// Two of the decision rule's own clauses fail on their own numbers,
        /// either sufficient alone. `webersweep` — the flag's own long-stated
        /// blocker — is WORSE on the ON arm: 21/26 (red 0, 2, 5, 9, 21) against
        /// the OFF arm's record on the same seeds, 24/26 (red 0, 5); three seeds
        /// fail on this path that do not fail off it. And the census fails the
        /// rule's own "no worse than pooled on BOTH numbers": ALIVE 100.2
        /// against pooled 127.0, VACANCY 41 % against pooled 27 %, both worse.
        /// Both canaries DO pass on the ON arm (`canary` 39/39, `laborcanary`
        /// 39/39, this commit) and the mechanical pins (`--pooled`, vanillaMode,
        /// flags-off smoke, the occupancy pin, the pooled fingerprint arm) and
        /// ledger coverage (conservation and reconciliation exact on posted,
        /// auction AND store-level arms, unmoved) are all in place — so the
        /// remaining blocker is exactly two items, not the whole list: close or
        /// reverse the Weber regression, and close the census against the
        /// POOLED arm specifically (its own prior number narrowed; the pooled
        /// comparison did not). The absorbing vacant-commercial-parcel state
        /// this fix exposed (above) is the most likely lever on the second one
        /// and is unowned by this item.
        ///
        /// Harness: `--store-level` on any command turns it on, `--pooled`
        /// forces the pooled path; `shopsweep` runs the two commerce checks
        /// alone across seeds, `shopprobe` is the census and `entrydiag` is the
        /// entrant post-mortem. Measured verify over seeds 0–7 BEFORE task #20:
        /// 7/8 with the flag off (seed 3 fails on the clearing-price check, and
        /// did so before this branch), 5/8 with it on (seeds 0 and 5
        /// additionally fail Weber).</summary>
        public bool StoreLevelSpending = false;
        /// <summary>Solve housing as ONE assignment market (HousingAuction):
        /// prices and who-lives-where come out of the same ascending auction,
        /// instead of a demand curve inverted for the price and a
        /// first-come-first-served queue for the keys. See HousingAuction for
        /// why the two-mechanism version could let the highest bidder lose a
        /// unit to whoever had a lower household id.
        ///
        /// DEFAULT TRUE since the flip inventory measured the full battery on
        /// both arms (39/39 both canaries; one real red exposed, Weber seed 13,
        /// recorded in KNOWN-RED; seed 9's occupancy red and the vacancy
        /// scenario both HEAL on this path). The posted path stays reachable
        /// via `--posted` and stays covered by the pinned posted arms of the
        /// ledger/reconciliation/shelter checks, the occupancy-channel check,
        /// and the fingerprint's posted arm — while it ships at all.</summary>
        public bool HousingAuction = true;
        /// <summary>Clear the labor market as ONE assignment auction
        /// (LaborAuction): who works where, at what TOTAL comp, comes out of
        /// the same ascending machinery as the housing market, run in
        /// p = cap − T space. Replaces, on this path only: the i.i.d.
        /// employment draw against the Sinkhorn-balanced rate, the
        /// commute-weighted reservoir placement, and the citywide pro-rata
        /// wage pooling (each firm is debited exactly its own members' base
        /// comp). The Sinkhorn model keeps running on the flag-off path,
        /// which must stay byte-identical.</summary>
        public bool LaborAuction = false;
    }
}
