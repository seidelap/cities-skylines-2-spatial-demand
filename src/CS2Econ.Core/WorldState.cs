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
        /// <summary>The household that OWNS this parcel (res-low only), −1 =
        /// none. The owner tag with an agent behind it: set at seeding (the
        /// household seeded into owner-rolled stock) or when an OwnerMinded
        /// household moves into an unowned res-low parcel; cleared — with the
        /// ask — whenever that household stops living here (Allocation.Vacate,
        /// plus the engine's pre-solve sweep for exits that bypass it).
        /// OwnerEligibleSeed (−2) marks stock rolled owner-occupied at world
        /// build, before any household exists to claim it.</summary>
        public int OwnerHousehold = -1;
        public const int OwnerEligibleSeed = -2;
        /// <summary>The owner's own ask per unit per tick — the reserve its
        /// parcel-door floors at, written by EconomyEngine.PostOwnerAsks
        /// BETWEEN solves from the owner's own valuation (never from the
        /// market's answer at this parcel — the no-ratchet rule). It allocates
        /// and is paid to nobody; no assessment may read it (§3 guard).</summary>
        public double OwnerAskPerUnit;
        /// <summary>Owner tag (§4.4): consent gate on redevelopment. Derived —
        /// OwnerHousehold is the one source of truth; a parcel with no owning
        /// household is not owner-occupied whatever it was rolled at build.</summary>
        public bool OwnerOccupied => OwnerHousehold >= 0;
        public bool Warehousing;        // scrape pending: vacated units stop re-letting

        // Land accounting (assessed, never from own realized rent — §3 circularity guard)
        public double AssessedLR;       // best-permitted-use land flow per tick (all units)
        public double CurrentResidual;  // Bid_current − S_current (all units)
        public double Wedge;            // AssessedLR − CurrentResidual, ≥ 0
        /// <summary>The running quote of the CURRENT vacancy spell under
        /// EconParams.VacantRepricing: seeded at the computed ladder value when
        /// a Built non-residential parcel is assessed unoccupied, marked down
        /// by VacantLRDecay each refresh while no taker appears, and reset to
        /// the −1 sentinel by occupancy or ineligibility so the next spell
        /// starts fresh from the computed value — a new building has been
        /// refused by nobody, and must not inherit a dead spell's markdown.
        /// Flag off it stays −1 and nothing reads it.</summary>
        public double VacantMarkLR = -1;
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
        /// <summary>Whether the CURRENT tenure was created by the housing
        /// auction's apply, as opposed to a direct Allocation move (the
        /// affordability-displacement path, arrival placement). The door↔parcel
        /// integrity check needs this positively: the invariant "a household
        /// holding an owner door lives at that door's parcel" belongs to the
        /// APPLY, and a household legitimately re-housed by another mechanism
        /// holds a stale Assignment snapshot, not a violated invariant. Tick
        /// arithmetic cannot make the distinction — the displacement path can
        /// run in the SAME tick as the apply (measured: canary seed 6, hh668,
        /// tenure@155 == lastApply@155) — so the mover marks the tenure
        /// itself.</summary>
        public bool PlacedByAuction;
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
        public void DrawAtBirth(CS2Econ.Core.Segment seg, EconParams p)
        {
            double u = SplitMix64.Hash01((ulong)Id * 2654435761UL + 11UL);
            double v = SplitMix64.Hash01((ulong)Id * 2654435761UL + 29UL);
            // Symmetric ±25% spread on the willingness to commit income, and
            // ±0.2 on density tolerance — heterogeneity a segment mean hides.
            RentShare = seg.MaxRentShare * (0.75 + 0.5 * u);
            DensityTol = MathUtil.Clamp(seg.DensityTolerance + 0.4 * (v - 0.5), 0, 1);
            // What this household could get by living somewhere else in the
            // region, as a share of its own housing budget. Its OUTSIDE OPTION,
            // and the only honest place for one: an individual's alternative to
            // this city is a fact about that individual, not a citywide index.
            // The housing auction used a single shared constant here, so every
            // household in the city walked away at exactly the same moment.
            double r = SplitMix64.Hash01((ulong)Id * 2654435761UL + 47UL);
            ReservationShare = 0.5 * r * r;      // skewed low: most people are movable
            // Work reservation: the comp per earner per tick below which this
            // household would rather not work, as a share of its class wage.
            // Same skewed-low shape as ReservationShare: most people work at
            // going rates; a few price themselves above every door AND the
            // border, and are voluntarily unemployed.
            double q = SplitMix64.Hash01((ulong)Id * 2654435761UL + 53UL);
            WorkReservationShare = 0.5 * q * q;
            // Owner disposition: whether this household would claim the parcel
            // it settles in (Family lifecycle only — §4.4 scope), and how much
            // of its own reservation it would hold its spare units at. Drawn
            // from the id hash so prospects get both too: on the auction path
            // arrivals can become owners again, where the old engine-side roll
            // reached only posted-path arrivals.
            double om = SplitMix64.Hash01((ulong)Id * 2654435761UL + 61UL);
            OwnerMinded = seg.Life == Lifecycle.Family && om < p.OwnerMindedShare;
            // u² skews low (median 0.25): most asks fold into the pooled door
            // and only the high tail produces tenure geography — see the fold
            // rule in HousingAuction.BuildSubmarkets.
            double oa = SplitMix64.Hash01((ulong)Id * 2654435761UL + 67UL);
            OwnerAskShare = oa * oa;
        }

        /// <summary>Would this household claim ownership of the res-low parcel
        /// it settles in? Drawn at birth (id hash, Family lifecycle ×
        /// EconParams.OwnerMindedShare); recomputable from the id, so nothing
        /// to persist.</summary>
        public bool OwnerMinded;
        /// <summary>The share of its own reservation this household asks for
        /// its spare units when it owns (u² draw at birth, ∈ [0,1]). A
        /// preference at the charter-blessed entry point, not a parameter.</summary>
        public double OwnerAskShare;

        /// <summary>Outside option as a fraction of this household's own housing
        /// budget, drawn at birth. Held as a SHARE rather than a level so it
        /// tracks the household's own income without ever being re-rolled — the
        /// preference is fixed, what it is worth is not.</summary>
        public double ReservationShare;

        /// <summary>Leisure floor for the labor auction: comp per earner per
        /// tick below which this household prefers no work at all, as a share
        /// of its class mean wage. Drawn at birth (see DrawAtBirth), skewed
        /// low — a personal attribute, never a citywide constant.</summary>
        public double WorkReservationShare;
        /// <summary>Labor-auction outcome: every working earner is OUTSIDE
        /// the region — employed (Earners > 0) with WorkplaceParcel −1, paid
        /// the outside net wage by Account.OutsideWorld. Bidding is per
        /// EARNER, so a household can mix in-region and outside earners; the
        /// flag is false then (it has a real workplace). Only the
        /// labor-auction path writes or reads this.</summary>
        public bool OutsideWorker;
        /// <summary>The household's TOTAL base pay per tick under the labor
        /// auction, summed over its working earners: each in-region earner's
        /// max(0, door comp T − firm.DividendPerEarnerEma), each outside
        /// earner's own outside net wage. The market prices only T; this
        /// split is accounting. Earners bid individually and may sit at
        /// different doors, so the engine keeps the per-earner records
        /// (EconomyEngine._earnerFirm/_earnerBase) — this field is their sum
        /// for telemetry and the income pass. The flag-off wage path never
        /// reads it.</summary>
        public double BaseComp;

        /// <summary>This household's outside option in money per tick.</summary>
        public double Reservation(double budget) => ReservationShare * budget;

        /// <summary>How much this household values a unit of the given density
        /// — its OWN tolerance, not its segment's. Same floor rationale as
        /// Segment.DensityAppeal (preference, never permission).</summary>
        public double DensityAppeal(ZoneKind kind)
            => kind == ZoneKind.ResidentialHigh
                ? CS2Econ.Core.Segment.DensityFloor + (1 - CS2Econ.Core.Segment.DensityFloor) * MathUtil.Clamp(DensityTol, 0, 1)
                : 1.0;
        /// <summary>The commercial firm this household actually shops at, or −1
        /// for the out-of-town option. Chosen by the household itself at refresh
        /// cadence and kept between refreshes: people have a usual shop and go
        /// back to it until something changes. Its spending is the whole of that
        /// firm's takings from it — no pooling, no pro-rata.</summary>
        public int ShopFirm = -1;
        public double ChargedAssessment; // per-tick S+tax+wedge currently being charged
        public double MovingCostDraw;    // within-segment heterogeneity draw (§4.4)
        public long TenureStart;
        public int StressTicks;          // consecutive ticks payment > income capacity
        /// <summary>Consecutive ticks this household has judged that nothing in
        /// the city beats its own reservation. Out-migration's whole clock.</summary>
        public int DeclineTicks;
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
        /// <summary>EMA of realized dividend per member-earner per tick — the
        /// firm's own forecast of the dividend component of total comp, built
        /// from its own payouts. The labor auction clears TOTAL comp T with
        /// cap = the firm's marginal-product forecast; the firm then pays
        /// base = max(0, T − this) and the dividend pass tops the rest up.
        /// Updated in the dividend pass on both flag paths; only the
        /// labor-auction path reads it.</summary>
        public double DividendPerEarnerEma;
        /// <summary>EMA of the money of custom that showed up at this shop's
        /// door per tick, INCLUDING what it had to turn away for want of staff.
        /// The firm's own observation, and the only thing its labor bid may be
        /// driven by: a shop that lost its staff serves nothing, so a cap read
        /// off SERVED volume could never buy back the staff that would let it
        /// serve and any shop losing one labor round would be dead permanently.
        /// Presented custom is the honest observation ("people walked in and I
        /// turned them away") and it is what breaks that spiral.
        /// Store-level path only; the pooled path's cap stays on ProfitEma.</summary>
        public double PresentedEma;
        /// <summary>Whether this firm has ever seen custom at its door. Until
        /// it has, PresentedEma is not a forecast it could hold and the labor
        /// cap falls back to the firm's full staffing ceiling — a shop that has
        /// not yet opened forecasts it can use its slots. Without this a new
        /// firm starts at PresentedEma = 0, posts no door, hires nobody, has
        /// zero service capacity and therefore zero takings, and starves on its
        /// seed capital before the EMA can ramp: the measured
        /// "new shops starve before they fill" mode (deaths 217 → 680) in a new
        /// disguise. The first STRICTLY POSITIVE observation both seeds the EMA
        /// and sets this — the same first-observation seed the posted-price EMA
        /// uses (AccessState.Refresh).</summary>
        public bool PresentedObserved;

        // Per-tick scratch (settlement + telemetry)
        public double RevenueThisTick;
        public double OutputThisTick;
        /// <summary>Money of custom presented at this shop's door this tick,
        /// summed over rationing rounds; what PresentedEma consumes. Distinct
        /// from RoundPresented, which is one round's presentation and is what
        /// the pro-rata ratio divides capacity by — using the tick-cumulative
        /// figure for the ratio under-serves every round after the first.</summary>
        public double PresentedThisTick;
        public double ServedThisTick;
        public double RoundPresented;
        /// <summary>This round's pro-rata share, fixed before anybody is
        /// served, so which of a full shop's customers the loop reaches first
        /// cannot change what any of them gets.</summary>
        public double RoundRatio;
        public double RemainingCapacity;
        public double[] InputNeedByRes = new double[ResourceCatalog.Count];

        // ---- land-charge account (the firm side of the §4.3 levy) -----------
        /// <summary>The gross revenue this firm's production booked on the last
        /// tick it ran, kept because FirmLifecycle zeroes RevenueThisTick after
        /// consuming it. Telemetry: the realized side of the parity check's
        /// office identity, read against what the assessor prices the same
        /// building at.</summary>
        public double GrossRevenueLastTick;
        /// <summary>Cumulative assessment billed to, and paid by, this firm.
        /// The firm analog of the household's payment record; telemetry, and
        /// what the parity checks read.</summary>
        public double LevyOwedCum, LevyPaidCum;
        /// <summary>Consecutive ticks this firm did not meet its land charge in
        /// full — the firm analog of Household.StressTicks, and the clock the
        /// arrears outcome reads. Reset by any tick paid in full.</summary>
        public int LevyShortTicks;
        /// <summary>Set when this firm exited because its land charge went
        /// unmet for LandArrearsTicks consecutive ticks (as distinct from the
        /// working-capital bankruptcy at CompanyBankruptcyLimit). Telemetry for
        /// the parity checks' attribution leg.</summary>
        public bool DiedOfArrears;
        /// <summary>Set when this firm exited because it lost its site and no
        /// site it could carry was open to it — the firm analog of the
        /// household that is displaced and emigrates rather than being rehoused.
        /// Distinct from DiedOfArrears (which is a failure to PAY at a site the
        /// firm still held) and from the working-capital bankruptcy, so no
        /// check can confuse the three.</summary>
        public bool DiedOfDisplacement;
        /// <summary>The tick this firm LOST A SITE IT HELD, or −1 if it never
        /// has. This is the difference between two states that look identical
        /// in `Parcel &lt; 0` and must not be treated alike.
        ///
        /// A firm that lost a held site suffered an economic event — the
        /// building came down, or another bidder took it — and its default is
        /// to be gone, so the reconciliation pass resolves it at once.
        ///
        /// A firm that is siteless for any OTHER reason has suffered nothing.
        /// On the mod arm that is the common case, and EconReader.AddFirm's own
        /// comment names the causes: a prefab without SpawnableBuildingData, a
        /// building spawned since the last sync, a claim refused because
        /// another firm holds the site. In every one of those the company is
        /// still standing in its building and only the ADAPTER lost track of
        /// it. Its default is to stay put, and an engine that relocated or
        /// killed it would be moving an agent against its own default on the
        /// strength of its own ignorance — and killing it is permanent, since
        /// EconReader.SyncFirms skips a dead firm forever and FirmIndex blocks
        /// re-creation. So the pass counts those and leaves them alone.</summary>
        public long SiteLostTick = -1;

        // ---- the exit margin (Dixit): revenue against AVOIDABLE cost --------
        /// <summary>Everything this firm must pay THIS TICK to keep operating,
        /// accumulated as it is incurred: wages, input purchases, and the land
        /// charge. Sunk cost is excluded because there is none to exclude — a
        /// firm here owns nothing, and FirmSeedCapital round-trips to
        /// PhantomBank on every exit path, so it is a line of credit and not a
        /// stake. The worker-collective dividend is excluded because it is a
        /// DISTRIBUTION of surplus, not a cost of operating: a firm that pays
        /// one is by definition covering everything above.
        ///
        /// THE LAND TERM IS `owed`, NOT `pay`. The levy takes
        /// `min(money, owed)` (EconomyEngine.cs, the Levying block), so a firm
        /// can never fail to cover REALIZED rent — the shortfall is zero by
        /// construction exactly when the firm is broke, which is exactly when
        /// the margin matters. A margin built on realized cash would be blind
        /// to the only cost measured to strand firms here (offices billed
        /// 489/tick against 428 of gross revenue; see EconParams' parity
        /// note).</summary>
        public double OperatingCostThisTick;
        /// <summary>EMA of (revenue − avoidable cost) per tick: the firm's own
        /// running read of whether it is covering its costs. Rate 0.05,
        /// inherited from ProfitEma — no new parameter.
        ///
        /// NOT ProfitEma, which despite the name is an EMA of gross REVENUE
        /// (it is fed RevenueThisTick). Its consumers — the labor auction's
        /// door cap above all — read it as revenue and are correct to; the name
        /// is the only thing wrong with it, and renaming a field the auction
        /// depends on is not this item's business.</summary>
        public double CashFlowEma;
        /// <summary>EMA of |cashFlow − CashFlowEma|: the firm's own read of how
        /// much its cash flow ordinarily varies. Same rate as the mean it is
        /// measured against, and it exists to be the exit margin's inaction
        /// band — see the band's own comment in FirmLifecycle for why the
        /// clock asymmetry could not serve as one.</summary>
        public double CashFlowMadEma;
        /// <summary>DIAGNOSTIC ONLY: the payroll actually DEBITED this tick,
        /// latched into PayrollLastTick beside the cash-flow close. Exists
        /// because a probe that reconstructs a wage bill from
        /// FilledByClass x EconParams.Wage(class) reports the POSTED class
        /// wage, which under the labor auction is not what the firm pays: the
        /// auction bills cleared BASE comp, and for a worker collective most
        /// of a member's take is the dividend -- a distribution of surplus,
        /// deliberately not an avoidable cost. Reconstructing it overstated
        /// office payroll by ~324/tick against an actual near-zero base.</summary>
        public double PayrollThisTick;
        public double PayrollLastTick;
        /// <summary>Whether this firm has ever completed a tick it could have
        /// operated in. Until it has, CashFlowEma is not a forecast it could
        /// hold, and the first observation SEEDS the EMA rather than being
        /// averaged against a zero it never lived.
        ///
        /// Without this the pipeline kills every entrant. An entrant is
        /// negative-cash-flow by construction: production is linear in
        /// WorkersFilled, which is rebuilt only on the RefreshInterval grid,
        /// while the land charge bills it from its first tick. A zero-seeded
        /// EMA plus a consecutive-negative counter starts the death clock at
        /// tick 1 for every new firm — the measured "starves before the EMA can
        /// ramp" mode that PresentedObserved above exists to prevent, in a new
        /// disguise. Note the test is COULD-OPERATE, not strictly-positive:
        /// cash flow is signed, and a genuinely loss-making first tick is a
        /// real observation.</summary>
        public bool CashFlowObserved;
        /// <summary>Consecutive ticks this firm's own cash-flow read has been
        /// negative. The firm analog of Household.StressTicks, and it follows
        /// that field's ASYMMETRY deliberately: it increments on a bad tick and
        /// DECREMENTS on a good one rather than resetting, so recovery costs as
        /// many ticks as decline did. That asymmetry is the inaction band —
        /// there is no liquidation cost to build one from, because there is
        /// nothing to liquidate.
        ///
        /// (LevyShortTicks, the older clock beside this one, RESETS on a paid
        /// tick instead. The divergence is deliberate: that clock asks "is this
        /// firm in arrears right now", which a single full payment answers,
        /// while this one asks "is this business viable", which one good tick
        /// does not.)</summary>
        public int CashFlowShortTicks;
        /// <summary>Set when this firm exited because its own cash-flow read
        /// stayed below zero past its patience — the exit margin proper, as
        /// distinct from running the balance to CompanyBankruptcyLimit
        /// (DiedOfWorkingCapital), from arrears (DiedOfArrears), and from
        /// losing its site (DiedOfDisplacement).</summary>
        public bool DiedOfCashFlow;
        /// <summary>Set when this firm exited on the working-capital floor
        /// (Money &lt; CompanyBankruptcyLimit). This exit shipped for a long
        /// time with NO flag of its own and was identified by elimination in
        /// the probes — which stops working the moment a fourth exit exists.
        /// Naming it is part of adding the fourth.</summary>
        public bool DiedOfWorkingCapital;
        /// <summary>Which immaterial good this office produces. Fixed at entry
        /// the way an industrial firm's recipe is — the entrant commits to the
        /// specialization its own site's neighbourhood best supports, and then
        /// lives with that choice as the neighbourhood changes around it.
        /// Meaningless for other sectors and ignored by them.</summary>
        public OfficeKind Office;
        /// <summary>Which basket line this shop retails. Fixed at entry, the
        /// way an industrial firm's recipe is: the entrant sells whatever its
        /// own catchment is least well served in, priced at what that good
        /// costs to get delivered HERE.
        ///
        /// Res.Services — the old single line — means "sells the whole basket",
        /// which is what every shop did and what they all still do while
        /// FeatureFlags.CommercialLines is off. Meaningless for other sectors.</summary>
        public Res Retail = Res.Services;
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
        /// <summary>Chain-migration stock, POSTED-LEGACY: one citywide scalar,
        /// read only by Migration.Step. The auction path keeps the per-cluster
        /// stock below — people follow people to NEIGHBORHOODS, not to a
        /// city-shaped average.</summary>
        public double NetworkMemory;
        /// <summary>Per-cluster chain-migration stock (auction path): a decaying
        /// EMA of ADMITTED prospects by the cluster each one chose, maintained
        /// by Prospects.Step (decay p.NetworkTieDecay per offer batch, +1 at
        /// the chosen cluster per admit). Each unit is one remembered past
        /// arrival — a link somebody outside has to a specific neighborhood.
        ///
        /// ANTI-SMUGGLING GUARD: this stock is a COUNT of past arrivals, never
        /// a quality read. It may enter exactly two places — a prospect's own
        /// tie-cluster familiarity bonus (Prospects.Step → QuoteOutsider's
        /// tieCluster arg) and the total-stock term of prominence (offer count
        /// only). It must never enter door valuation beyond that bonus, never
        /// prices, assessment, or the firm side.</summary>
        public double[] NetworkTies = Array.Empty<double>();
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
        public sealed class Cell { public double SumRatio; public double N; }
        public readonly Dictionary<ZoneKind, PerUse> ByUse = new Dictionary<ZoneKind, PerUse>();
        /// <summary>Per-(use, cluster) realized-vs-predicted history: a
        /// developer's own record of how its forecasts did AT A PLACE. The
        /// routing doc's calibration is per-corridor; the citywide PerUse
        /// factor above is kept as the shrinkage anchor (and the posted-path
        /// telemetry read), not as the decision input.</summary>
        public readonly Dictionary<(ZoneKind use, int cluster), Cell> ByCell
            = new Dictionary<(ZoneKind, int), Cell>();

        public double Factor(ZoneKind use) => ByUse.TryGetValue(use, out var s) ? s.Factor : 1.0;

        /// <summary>The correction factor a construction forecast at (use,
        /// cluster) trusts: the cluster's own mean realized/predicted ratio,
        /// shrunk toward the use-level factor by its own observation count —
        ///     factor_c = clamp(useFactor + n_c/(n_c + n0) · (mean_c − useFactor))
        /// — the same shrinkage form ProspectLocalOdds uses (n0 =
        /// p.CalibClusterShrinkN0). A cluster with one completion is mostly
        /// the use-wide record; a cluster with many is mostly its own.</summary>
        public double Factor(ZoneKind use, int cluster, double clusterN0)
        {
            double useF = Factor(use);
            if (!ByCell.TryGetValue((use, cluster), out var c) || c.N <= 0) return useF;
            double mean = c.SumRatio / c.N;
            double w = c.N / (c.N + Math.Max(1e-9, clusterN0));
            return MathUtil.Clamp(useF + w * (mean - useF), 0.4, 2.5);
        }

        public void Observe(ZoneKind use, int cluster, double realizedOverPredicted, double shrinkN0)
        {
            double clamped = MathUtil.Clamp(realizedOverPredicted, 0.1, 4.0);
            if (!ByUse.TryGetValue(use, out var s)) ByUse[use] = s = new PerUse();
            s.SumRatio += clamped;
            s.N += 1;
            double mean = s.SumRatio / s.N;
            double w = s.N / (s.N + shrinkN0);          // shrink toward 1.0 on few observations
            s.Factor = MathUtil.Clamp(1.0 + w * (mean - 1.0), 0.4, 2.5);
            if (!ByCell.TryGetValue((use, cluster), out var c)) ByCell[(use, cluster)] = c = new Cell();
            c.SumRatio += clamped;
            c.N += 1;
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
