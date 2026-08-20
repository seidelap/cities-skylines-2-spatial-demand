using System;

namespace CS2Econ.Core
{
    /// <summary>Cluster-level price/cost context Tier C reads from Tier D.
    /// Kept behind an interface so land accounting never reaches into trade
    /// internals (and the harness can stub it for unit tests).</summary>
    public interface IPriceContext
    {
        /// <summary>Statistic of what buyers AT cluster c actually paid per
        /// delivered unit of r (realized transactions, haul included), shrunk
        /// toward the citywide realized prior where evidence is thin. There is
        /// deliberately NO citywide price read on this interface: the price a
        /// firm experiences is a property of its place (task #30).</summary>
        double DeliveredStat(Res r, int cluster);
        /// <summary>Statistic of what sellers AT cluster c actually netted per
        /// unit of r, same shrinkage.</summary>
        double OriginStat(Res r, int cluster);
        /// <summary>OriginStat restricted to what counts as a COMPARABLE for
        /// assessment: the local cell's realized statistic is admitted only
        /// where more than one seller of r stands at c, so a lone producer's
        /// own realized takings can never price the land it stands on (§3).
        /// Thinner than that, the read falls back to the citywide realized
        /// prior, and where the whole city has no second seller it returns 0 —
        /// leaving the caller's max() to price the output at exit parity, an
        /// outside-world price. Assessment reads this; settlement and the
        /// firms' own trading read OriginStat.</summary>
        double OriginComparable(Res r, int cluster);
        /// <summary>Delivered cost of one unit of r at cluster c (a producing
        /// cluster's realized price + haul, or import parity — whichever is
        /// lower). Design §4.2 freight-in term.</summary>
        double DeliveredCost(Res r, int cluster);
        /// <summary>Best net price for exporting one marginal unit of r from cluster c
        /// (exit marginal minus routed haul). Design §4.2 exit-parity term.</summary>
        double BestExportNet(Res r, int cluster);
    }

    /// <summary>Tier C (design §4.3): uniform assessment. Every parcel's land flow
    /// LR is the max over PERMITTED configurations of [Bid − S − a(h,L)·remaining
    /// transition cost], with the parcel's escrow reducing the deduction (the
    /// self-ramping wedge). Assessments derive ONLY from market-access bids over
    /// the population/firm distribution — never from the parcel's own realized
    /// rents. That is the §3 circularity guard and it is structural: nothing in
    /// this file reads Parcel.OccupantHouseholds' payments or any realized rent.
    ///
    /// Item #41 sharpens the guard's statement and its enforcement: assessment
    /// reads the SUBMARKET's market bids — never the parcel's own ask, own
    /// door price, or own realized rent. Every auction read here is keyed by
    /// (cluster, kind, level) through HousingAuction.SubOf, whose arithmetic
    /// can never resolve to an owner parcel's own door, and the pricing API
    /// (BidPerUnit and down) takes no parcel and no door index — per-parcel
    /// assessment pricing is unrepresentable in it, and the signature must not
    /// widen. HousingAuction.DoorOf is never called from this file (nor from
    /// Construction or Overlays); with owner parcels' units OUT of the uniform
    /// capacity, a lone owner parcel's assessment prices from its submarket's
    /// comparables or the shadow queue, closing the singleton self-assessment
    /// that pooled granularity used to permit.
    ///
    /// The guard binds on the FIRM side through the same shape. FirmBidPerSlot
    /// takes no parcel and no firm, so per-parcel firm pricing is likewise
    /// unrepresentable; the one read that could still resolve to the sitting
    /// occupant is the output-price term, because "what sellers at c netted"
    /// is that firm's own record wherever it is the only seller at c. That is
    /// why the industrial and extractor legs read IPriceContext.
    /// OriginComparable rather than OriginStat: a cell with fewer than two
    /// sellers carries no comparable and the read falls back to the citywide
    /// realized prior, or to exit parity where the city has no second seller
    /// either. Both fallbacks are market signals no single parcel produces.</summary>
    public static class LandAccounting
    {
        /// <summary>MUTANT SWITCH (`--mutant-entry-reference-mass`): makes the
        /// store-level commercial entry read ask the counted field about a
        /// 6-slot condition-1 shop again, whatever building the firm would
        /// actually occupy. That is the entrant death mode restored verbatim —
        /// the entrant-survival leg must go red under it. Never a shipping
        /// mode.</summary>
        public static bool MutantEntryReferenceMass;

        public static int UnitsFor(ZoneKind use) => use switch
        {
            ZoneKind.ResidentialLow => 2,
            ZoneKind.ResidentialHigh => 12,
            ZoneKind.Commercial => 8,
            ZoneKind.Industrial => 10,
            ZoneKind.Office => 14,
            ZoneKind.Extractor => 6,
            _ => 0,
        };

        /// <summary>Structure charge per unit: S = (h + δ + m)·V, V = condition·RC
        /// (design §4.3). Charging on V, not RC: run-down buildings are cheap.</summary>
        public static double SPerUnit(int level, double condition, EconParams p)
            => (p.HurdleRate + p.Depreciation + p.MaintenanceRate) * condition * p.RC1PerUnit
               * Math.Pow(p.LevelCostGamma, level - 1);

        /// <summary>Residential bid per unit at (cluster, kind, level) — TWO
        /// REGIMES, split by whether the stock can actually fill:
        ///
        /// CLEARED (demand reaches supply×(1+ClearingBand)): the fills-all
        /// market-clearing price, read at the excluded challenger so every
        /// admitted tenant keeps strictly positive surplus. Competition
        /// disciplines this regime: an owner holding units above the clearing
        /// price is undercut by a neighbor who steals the tenant and fills
        /// their own building, so submarket-wide revenue-maximization is not
        /// an equilibrium here. (Tried at full strength: every submarket
        /// priced at its richest tranche's WTP — 22.25 invariant to supply×8,
        /// demand×2 and 397 exits — 2,187 units held vacant, housed
        /// insolvency ×20, one segment extinct. A cartel, measured.)
        ///
        /// EXCESS SUPPLY (demand exhausts first): price FLAT at the deepest
        /// positive bidder's WTP — never below (cutting under the last real
        /// bidder buys no tenant that exists: the revenue-max argument
        /// "4 × $6 beats 5 × $4" at the one point it binds monotonically)
        /// and never above (an unconstrained revenue-max here priced excess
        /// submarkets above their own scarcity price — supply×2 read 2.71
        /// vs 1.77 at ×0.5, rents rising as population fled; measured,
        /// reverted). Withholding is uncontested in this regime — nobody is
        /// coming for the marginal unit — and empirically rents are
        /// downward-rigid in gluts. This replaces the old proportional-decay
        /// branch, which chased absent demand toward zero and whose anchor
        /// on the deepest QUEUED bidder pinned 41 occupied submarkets at
        /// exactly zero. The shortfall surfaces as VACANCY (fillRatio): the
        /// affordability gate stops bidders below P from taking units, so
        /// fill settles near the curve's depth without explicit rationing.
        ///
        /// Two filters guard both regimes: zero-WTP entries (a segment with
        /// zero expected income contributes no demand) and zero-MASS entries
        /// (a positive WTP with no mass behind it is not a bid) are excluded
        /// from the queue, and the flat tail additionally requires its
        /// anchor tranche to carry a small minimum of cumulative mass
        /// (MinTailMass, a dust guard) — so a segment's last remnant near
        /// the presence gate or a zero-capacity cluster's empty shares can
        /// neither fake market depth nor set the price. KNOWN PROPERTY:
        /// the excess price is the WTP surface of the poorest segment with
        /// REAL mass here — extinction of such a segment still re-rates
        /// excess submarkets together, which is the market changing, not an
        /// artifact; the instant co-op re-rate delivers it in one tick.
        ///
        /// Neither regime is the max bidder (one rich eccentric re-rating a
        /// building — the affordability spiral this replaced) nor the
        /// presence-weighted mean (which ignores quantity entirely and so
        /// could not respond to vacancy at all).
        ///
        /// Circularity guard (§3) intact and, if anything, stronger: the inputs
        /// are potential demand (presence × access share) and standing stock —
        /// never this parcel's realized rent, and never realized occupancy. The
        /// price↔quantity feedback it does create is NEGATIVE (dear → vacancy →
        /// cheaper), i.e. stabilizing tâtonnement, not the self-reinforcing loop
        /// the guard exists to forbid.
        ///
        /// addUnits: units the candidate configuration would ADD to this
        /// kind's stock (greenfield, scrape-to-other-use, a project in the
        /// pipeline, an overlay hypothetical). They join the supply the price
        /// must clear — max(stock, units) would price the candidate as if its
        /// units displaced existing ones, overstating land rent exactly where
        /// stock already stands. minSupply: floor for configurations whose
        /// units are ALREADY in the stock count (the standing building itself)
        /// but may not have been seen by the last refresh. CAVEAT: in the
        /// excess regime the flat tail is supply-invariant, so addUnits does
        /// not lower the PRICE of adding units into a glut (only fillRatio
        /// falls); the discipline against overbuilding there comes from the
        /// tail sitting below structure cost on current calibration and from
        /// construction's separate absorption gate, not from this term.
        ///
        /// Density appeal enters HERE, on the WTP leg, and only here. An
        /// earlier build also weighted the demand share by appeal (the
        /// discrete-choice "consideration × conditional bid" story), but the
        /// share is normalized jointly over kind AND cluster, so shrinking
        /// the high-density rows renormalized that mass INTO the low-density
        /// rows: the apartment discount doubled as a +40% house subsidy, and
        /// the single calibrated haircut (Segment.DensityFloor) was applied
        /// roughly twice. One parameter, one channel: appeal prices the bid;
        /// the share stays kind-neutral (adversarial review, measured A/B).</summary>
        public static long AuditExcessCalls, AuditBreakIter1, AuditRatio5e5, AuditAllAbove, AuditDustZero, AuditTotalPriced;

        /// <summary>MUTANT SWITCH: restores the flat-0.5 geology the assessment
        /// path used to price every extractor configuration at, so the
        /// nonres-parity check's geology leg can be shown to fail. Never a
        /// shipping mode.</summary>
        public static bool MutantFlatGeologyAssessment;

        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p, double addUnits = 0, double minSupply = 0,
            bool realized = false)
            => ResidentialBidPerUnit(acc, cluster, kind, level, segmentPresence, p, out _,
                                     addUnits, minSupply, realized);

        /// <summary>Overload exposing the expected fill ratio at the returned
        /// price — 1.0 in the cleared regime (the stock fills), below 1.0 in
        /// excess supply where the curve's depth caps the fill and the
        /// balance stands vacant at the flat-tail price. For telemetry,
        /// overlays and checks; valuation stays price-based (see Assess).</summary>
        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p, out double fillRatio,
            double addUnits = 0, double minSupply = 0, bool realized = false)
        {
            fillRatio = 1.0;
            System.Threading.Interlocked.Increment(ref AuditTotalPriced);

            // The assignment market and this curve inversion answer DIFFERENT
            // questions, and `realized` is the caller saying which one it wants.
            //
            //   realized: true  → what this stock actually lets for right now.
            //                     That is the auction's posted price: the bid of
            //                     the household actually turned away at that
            //                     door. Assessment and the posted price want
            //                     this, because a land value is capitalized from
            //                     rent that is really collected.
            //   realized: false → the FORECAST, which is all this curve ever
            //                     was. Construction asking what a building it
            //                     has not started would earn, or a check asking
            //                     what happens if the population doubles, are
            //                     questions about a world that does not exist,
            //                     and no realized market can answer them.
            //
            // Construction.ObserveCompletions compares the two on purpose —
            // realized rent × occupancy of a completed build against the
            // forecast at its decision time, folded back through the
            // calibration factor. That is the honesty check on the forecast
            // (Overlays.cs only DISPLAYS the forecast; an earlier version of
            // this comment claimed the comparison lived there), and it only
            // means anything while the two stay separate.
            var auction = realized ? acc.Auction : null;
            if (auction != null && auction.C == acc.C)
            {
                int sub = auction.SubOf(cluster, kind, level);
                if (sub >= 0)
                {
                    bool standing = auction.Capacity[sub] > 0
                                    && addUnits <= 0 && minSupply <= auction.Capacity[sub];
                    if (standing)
                    {
                        fillRatio = MathUtil.Clamp(
                            auction.Filled[sub] / (double)auction.Capacity[sub], 0, 1);
                        return auction.Price[sub];
                    }
                    // Units that are not standing — a renovation to a level this
                    // cluster has none of, or a building that does not exist
                    // yet — let at what the QUEUE would pay: the n-th best bid
                    // waiting behind the door, from households that do not
                    // already hold a slot there.
                    int want = (int)Math.Ceiling(Math.Max(1, Math.Max(addUnits, minSupply)));
                    double marginal = auction.ShadowAt(sub, want);
                    fillRatio = MathUtil.Clamp(auction.ShadowCount[sub] / (double)want, 0, 1);
                    return marginal;
                }
            }

            bool highDensity = kind == ZoneKind.ResidentialHigh;
            int ki = highDensity ? 1 : 0;
            double quality = p.Quality(level) / p.Quality(1);

            // The demand curve is the POPULATION, not a distribution fitted to
            // it. acc.BidLadder[kind][segment] holds every living household's
            // own bid base (its own rent share × its own income × its own
            // density appeal), sorted descending. A household's willingness to
            // pay here is that base times one per-segment location multiplier,
            // so the curve at this (cluster, level) is the merge of eight real,
            // already-sorted ladders — and the price is the WTP of the actual
            // marginal household, found by inverting the cumulative count.
            //
            // segmentPresence stays a per-segment WEIGHT on that population, so
            // callers that price a counterfactual demand (construction's
            // seekers, the checks' scaled presence) still work: it scales how
            // many of these real bidders are in this market, never what they
            // would pay.
            Span<double> mult = stackalloc double[Segment.Count];
            Span<double> massPer = stackalloc double[Segment.Count];
            bool haveLadder = acc.BidLadder.Length == 2;
            double maxBid = 0, totalMass = 0;
            for (int s = 0; s < Segment.Count; s++)
            {
                mult[s] = 0; massPer[s] = 0;
                double pres = segmentPresence[s];
                if (pres < 1) continue;
                var seg = Segment.All[s];
                // Convex premium: location differences must be strong enough to
                // produce level geography (ℓ* gradients), not a flat ±20% band.
                // Density appeal is NOT here — it is each household's own, and
                // is already baked into its rung of the ladder.
                double rel = acc.AccessValue[s][cluster] / acc.MeanAccess;
                double premium = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                // How many of this segment want THIS (kind, cluster) — the
                // share is normalized over kind AND cluster, so it is
                // commensurate with the per-kind stock below. A per-cluster
                // share here double-counts every density-tolerant household
                // across both queues.
                double share = acc.SegmentKindShare.Length > s
                               && acc.SegmentKindShare[s][ki].Length > cluster
                    ? acc.SegmentKindShare[s][ki][cluster] : 0;
                if (share <= 0) continue;
                int nHh = haveLadder && acc.BidLadder[ki][s] != null ? acc.BidLadder[ki][s].Length : 0;
                if (nHh == 0) continue;

                mult[s] = premium * p.BidAccessScale * quality;
                // Each real household carries the caller's presence weight
                // spread over the population actually standing behind it.
                massPer[s] = share * (pres / nHh);
                totalMass += massPer[s] * nHh;
                double top = acc.BidLadder[ki][s][0] * mult[s];
                if (top > maxBid) maxBid = top;
            }
            if (maxBid <= 1e-12 || totalMass <= 1e-12) return 0;

            double stock = acc.HousingStock.Length == 2 && acc.HousingStock[highDensity ? 1 : 0].Length > cluster
                ? acc.HousingStock[highDensity ? 1 : 0][cluster] : 0;
            double supply = Math.Max(stock, minSupply) + Math.Max(0, addUnits);
            if (supply <= 1e-9) return maxBid;        // nothing to fill: top bidder

            // Read the demand curve at the first EXCLUDED position, not the
            // last admitted bidder (EconParams.ClearingBand): the price is
            // what the challenger who did NOT get a unit would pay.
            double band = 1.0 + Math.Max(0, p.ClearingBand);
            double readAt = supply * band;

            // Cumulative demand at price P: how many real households, across
            // all eight ladders, would pay at least P here. Each ladder is
            // sorted, so this is eight binary searches — and because it is
            // monotone in P, the clearing price is recovered by bisection.
            // The result is the WTP of the ACTUAL marginal household, with no
            // interpolation over synthetic tranches.
            var ladders = acc.BidLadder[ki];
            static double Cum(double price, Span<double> mult_, Span<double> massPer_, double[][] lads)
            {
                double c = 0;
                for (int s = 0; s < Segment.Count; s++)
                {
                    if (massPer_[s] <= 0) continue;
                    var lad = lads[s];
                    double need = price / mult_[s];
                    // Descending array: count entries >= need.
                    int lo = 0, hi = lad.Length;
                    while (lo < hi) { int mid = (lo + hi) >> 1; if (lad[mid] >= need) lo = mid + 1; else hi = mid; }
                    c += massPer_[s] * lo;
                }
                return c;
            }

            double totalAt0 = Cum(0, mult, massPer, ladders);
            if (totalAt0 < readAt)
            {
                // EXCESS-SUPPLY regime: demand exhausts before the stock fills.
                // Price at the DEEPEST real bidder — the lowest willingness to
                // pay still backed by more than dust — because cutting below
                // the last household that exists gains no tenant (pure revenue
                // loss), and pricing above the cleared boundary would invert
                // supply monotonicity.
                //
                // This is the same bisection as the cleared branch with the
                // read position moved to the BOTTOM of the curve: at most
                // MinTailMass of mass may lie strictly below the answer. An
                // earlier version searched from the top for the price with
                // MinTailMass ABOVE it, which is the opposite end of the curve
                // — it made a thinning market price HIGHER than a full one
                // (demand ×0.5 read 0.59 against 0.46 at ×1.0, measured) and
                // broke continuity at the regime boundary. With the read at
                // totalMass − dust, the two branches meet: as demand thins to
                // the boundary the cleared read and the tail read converge on
                // the same household.
                // The dust guard must be a FRACTION of the demand actually
                // present, not an absolute mass. As an absolute (0.05),
                // halving the caller's presence halves every household's mass,
                // so the same 0.05 reached twice as far UP the ladder and the
                // anchor climbed: demand ×0.5 priced at 0.59 against 0.46 at
                // ×1.0 — halving demand RAISED the price, exactly the
                // non-monotonicity the two-regime design exists to forbid
                // (measured). As a fraction the anchor lands on the same
                // household under any scaling of the caller's presence.
                const double MinTailFraction = 0.005;
                double target = Math.Max(1e-9, totalAt0 * (1 - MinTailFraction));
                double loT = 0, hiT = maxBid;
                for (int it = 0; it < 40; it++)
                {
                    double mid = 0.5 * (loT + hiT);
                    if (Cum(mid, mult, massPer, ladders) >= target) loT = mid; else hiT = mid;
                }
                if (loT <= 1e-12) { fillRatio = 0; return 0; }
                fillRatio = MathUtil.Clamp(totalAt0 / band / supply, 0, 1);
                return loT;
            }

            // CLEARED regime: bisect for the price at which exactly readAt
            // households remain willing — the marginal challenger.
            double lo2 = 0, hi2 = maxBid;
            for (int it = 0; it < 34; it++)
            {
                double mid = 0.5 * (lo2 + hi2);
                if (Cum(mid, mult, massPer, ladders) >= readAt) lo2 = mid; else hi2 = mid;
            }
            return lo2;
        }

        /// <summary>Firm bid per job slot for a hypothetical occupant of (cluster,
        /// sector, level). Commercial uses the phantom-entrant capture; industrial
        /// prices output at best-of local/exit-parity net of haul (Weber falls out
        /// of the input-haul term); office has near-exogenous output and the
        /// agglomeration multiplier (design §4.2).</summary>
        public static double FirmBidPerSlot(
            AccessState acc, IPriceContext prices, int cluster, ZoneKind sector, int level, EconParams p)
            => FirmBidPerSlot(acc, prices, cluster, sector, level, p, out _, workCluster: null);

        /// <summary>Overload exposing the chosen output — for industrial the
        /// argmax RECIPE (the Weber decision: each input priced at its own
        /// delivered cost — local source + haul vs import parity — so where to
        /// source each specific resource shapes where each industry bids), and
        /// for extractors the best raw the cluster's geology supports.</summary>
        public static double FirmBidPerSlot(
            AccessState acc, IPriceContext prices, int cluster, ZoneKind sector, int level, EconParams p,
            out Res chosenOutput, ClusterInfo[]? workCluster,
            double entrantMass = 0, double entrantSlots = 0, double entrantCondition = 0)
            => FirmBidPerSlot(acc, prices, cluster, sector, level, p, out chosenOutput, workCluster,
                              out _, entrantMass, entrantSlots, entrantCondition);

        /// <summary>Overload reporting whether the returned bid ALREADY prices
        /// the parcel's condition. It does exactly when the store-level
        /// commercial branch was given a specific building: there condition
        /// enters the catchment mass a shopper sees and the service ceiling a
        /// slot can deliver, which is the whole of what condition means to a
        /// shop, so the caller's generic CondFactor discount would charge the
        /// same fact a second time.</summary>
        public static double FirmBidPerSlot(
            AccessState acc, IPriceContext prices, int cluster, ZoneKind sector, int level, EconParams p,
            out Res chosenOutput, ClusterInfo[]? workCluster, out bool conditionPriced,
            double entrantMass = 0, double entrantSlots = 0, double entrantCondition = 0)
        {
            chosenOutput = Res.Services;
            conditionPriced = false;
            double fillEst = FirmFillEstimate(acc, cluster, sector, p);
            double quality = p.Quality(level) / p.Quality(1);
            // Production needs labor: revenue AND wages both scale with fill —
            // an unstaffed firm produces (and earns) nothing.
            double profitPerFilledSlot;
            switch (sector)
            {
                case ZoneKind.Commercial:
                {
                    double wage = 0.7 * p.WageBasic + 0.3 * p.WageSkilled;
                    // Restocking cost: the consumption basket at delivered prices,
                    // relative to its anchor value (imported/near baskets squeeze margin).
                    double cogsIndex = 0;
                    foreach (var (res, share) in ResourceCatalog.Basket)
                        cogsIndex += share * prices.DeliveredCost(res, cluster) / ResourceCatalog.Anchor[(int)res];
                    double capturePerSlot;
                    if (acc.HasCountedIntents)
                    {
                        // Store-level path. TWO changes, and they close the flag's
                        // own stated gap from both sides: a developer will not
                        // build for custom that is not COUNTED, and will not build
                        // what it cannot STAFF.
                        //
                        // The count is individual intents (AccessState.
                        // CountShopIntents) rather than the phantom-entrant share,
                        // which was the pooled allocation rule re-served as a
                        // forecast of a discrete market that does not work that way.
                        //
                        // The ceiling is this item's production relation: however
                        // much custom a location would draw, a slot cannot serve
                        // more than CommercialServicePerSlot × quality of it.
                        //
                        // Capture is divided by FILLED slots and the trailing
                        // × fillEst below multiplies it back, so both sides of the
                        // min are per FILLED slot. That is NOT an identity with the
                        // pooled leg's capture/6: it drops a fillEst ∈ [0.35, 1]
                        // from the capture side, raising it by up to 2.9×. It is
                        // the economically coherent form — custom arriving does not
                        // depend on your roster, your ability to serve it does, and
                        // that is what the min says — and it is a measured level
                        // change against a calibrated signal, which is why it is
                        // reported separately from the counted-vs-pooled swap and
                        // why it is confined to this branch.
                        // ASK THE FIELD ABOUT THE SHOP THAT WOULD ACTUALLY
                        // STAND HERE. The counted field is a demand curve in
                        // SIZE, so the mass it is read at is not a formality: it
                        // is the object EconomyEngine.ChooseShops scores, namely
                        // units × max(0.2, condition) × Quality(level). A caller
                        // that leaves entrantMass at 0 gets the CLUSTER
                        // reference — a 6-slot, condition-1 shop at this level —
                        // which is the right question for Construction's
                        // per-cluster signal and its calibrated scale, and the
                        // wrong one for a firm deciding whether to take a
                        // SPECIFIC vacant building.
                        //
                        // Measured at the two-track merge (`entrydiag --seeds 4`,
                        // 300 ticks, store-level arm, 143 mid-run entrants):
                        // the reference read is 1.63× the same field asked at
                        // the entrant's own mass at the median and never
                        // smaller (own/reference p10 0.374, p50 0.612, p90
                        // 1.000) — entrants take over standing buildings whose
                        // condition has decayed, and condition multiplies the
                        // mass a shopper sees. Against realized presented
                        // custom over the entrant's own first 40 ticks the
                        // reference read over-predicts 3.4× (p50 0.293); the
                        // own-mass read over-predicts 2.05× (p50 0.488, p90
                        // 1.260). The residue is the probe's stated
                        // taste-blindness, measured rather than corrected away.
                        //
                        // CONDITION IS PRICED ONCE. It enters here twice on
                        // purpose — the mass a shopper sees and the service a
                        // slot can deliver both carry max(0.2, condition), which
                        // is exactly how EconomyEngine.ChooseShops and
                        // CommercialServiceCapacity carry it — and the caller is
                        // told so through conditionPriced, so the generic
                        // CondFactor bid discount is not charged on top. Without
                        // that the same run-down building is discounted twice
                        // and entry stops: measured on seeds 0-1, 3 mid-run
                        // entrants across four seeds against 143.
                        double mass = entrantMass > 0 ? entrantMass : 6.0 * quality;
                        double slots = entrantSlots > 0 ? entrantSlots : 6.0;
                        double cond = entrantCondition > 0 ? Math.Max(0.2, entrantCondition) : 1.0;
                        if (MutantEntryReferenceMass) { mass = 6.0 * quality; slots = 6.0; cond = 1.0; }
                        else conditionPriced = entrantMass > 0 && entrantCondition > 0;
                        double counted = acc.CommercialCapture(cluster, mass);
                        capturePerSlot = Math.Min(counted / Math.Max(1e-9, slots * fillEst),
                                                  p.CommercialServicePerSlot * quality * cond);
                    }
                    else capturePerSlot = acc.PhantomCommercialCapture(cluster, 6.0 * quality) / 6.0;
                    profitPerFilledSlot = capturePerSlot * (p.CommercialMarkup - 0.1 * (cogsIndex - 0.3)) - wage;
                    // RETAIL WEBER: which line to sell here. Same shape as the
                    // industrial branch below — a maximum over what the firm
                    // could do, with the input side priced at THIS location —
                    // and it replaces the whole-basket read above rather than
                    // adding to it, so a shop is one business and not four.
                    //
                    // Both terms are local and both matter. The capture side
                    // asks how badly this catchment is served in this line
                    // (thin competition, more spending per unit of mass); the
                    // cost side asks what the good costs delivered here. A
                    // niche pays where it is underserved, which is the thing
                    // basket share alone could never express.
                    if (lineAware)
                    {
                        profitPerFilledSlot = double.NegativeInfinity;
                        for (int q = 0; q < ResourceCatalog.Basket.Length; q++)
                        {
                            var (lres, _) = ResourceCatalog.Basket[q];
                            double lineCapPerSlot = capturePerSlot <= 0 ? 0
                                : capturePerSlot * SafeRatio(acc.CaptureLine(q, cluster),
                                                             acc.CaptureLine(-1, cluster));
                            double lineCogs = prices.DeliveredCost(lres, cluster)
                                              / ResourceCatalog.Anchor[(int)lres];
                            double v = lineCapPerSlot * (p.CommercialMarkup - 0.1 * (lineCogs - 0.3)) - wage;
                            if (v > profitPerFilledSlot) { profitPerFilledSlot = v; chosenOutput = lres; }
                        }
                    }
                    break;
                }
                case ZoneKind.Industrial:
                {
                    // Weber: max over recipes of margin at THIS location.
                    double wage = 0.6 * p.WageBasic + 0.4 * p.WageSkilled;
                    profitPerFilledSlot = double.NegativeInfinity;
                    foreach (var recipe in ResourceCatalog.Recipes)
                    {
                        // A hypothetical entrant prices its output at realized
                        // COMPARABLES at the place — sellers other than the one
                        // standing here — or the exit alternative. OriginStat
                        // itself would admit a cell whose only seller is this
                        // parcel's own occupant, which is the §3 guard's firm
                        // analog of assessing a parcel from its own realized
                        // rent (measured at 88fc88c, parityprobe seed 1/400t:
                        // 6 of 8 occupied producing parcels).
                        double outNet = Math.Max(
                            p.NonResLandParity ? prices.OriginComparable(recipe.Output, cluster)
                                               : prices.OriginStat(recipe.Output, cluster),
                            prices.BestExportNet(recipe.Output, cluster));
                        double inputCost = 0;
                        foreach (var (res, qty) in recipe.Inputs)
                            inputCost += qty * prices.DeliveredCost(res, cluster);
                        double perSlot = recipe.OutputPerSlot * p.RecipeOutputScale * quality
                                         * (outNet - inputCost) - wage;
                        if (perSlot > profitPerFilledSlot)
                        { profitPerFilledSlot = perSlot; chosenOutput = recipe.Output; }
                    }
                    break;
                }
                case ZoneKind.Office:
                {
                    // THE MAXIMUM OVER COMPANY TYPES, which is what a site's
                    // land value has always claimed to be: not what the office
                    // standing here earns, but what the best office that could
                    // stand here would. For industrial that maximum ranges over
                    // recipes and for extractor over the raws the geology
                    // supports; office had one candidate, so its "maximum" was
                    // a relabelling. Now it ranges over the specializations,
                    // each priced at its own neighbourhood's localization.
                    double wage = 0.3 * p.WageSkilled + 0.7 * p.WageEducated;
                    profitPerFilledSlot = double.NegativeInfinity;
                    for (int k = 0; k < AccessState.OfficeKindCount; k++)
                    {
                        double v = p.OfficeOutputPerSlot * quality * p.OfficeOutputPrice
                                   * acc.OfficeAgglom((OfficeKind)k, cluster, p) - wage;
                        if (v > profitPerFilledSlot) profitPerFilledSlot = v;
                    }
                    chosenOutput = Res.OfficeOutput;
                    break;
                }
                case ZoneKind.Extractor:
                {
                    // Best raw the geology supports, priced at its own market.
                    profitPerFilledSlot = double.NegativeInfinity;
                    var suit = workCluster != null ? workCluster[cluster].ResourceSuitability : null;
                    for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                    {
                        double s2 = suit != null ? suit[rr] : 0.5;
                        if (s2 <= 0.05) continue;
                        var res = (Res)rr;
                        // Comparables, not the cell's own record — see the
                        // industrial leg above.
                        double outNet = Math.Max(
                            p.NonResLandParity ? prices.OriginComparable(res, cluster)
                                               : prices.OriginStat(res, cluster),
                            prices.BestExportNet(res, cluster));
                        double perSlot = p.ExtractorOutputPerSlot * quality * s2 * outNet - p.WageBasic;
                        if (perSlot > profitPerFilledSlot)
                        { profitPerFilledSlot = perSlot; chosenOutput = res; }
                    }
                    if (double.IsNegativeInfinity(profitPerFilledSlot)) return 0;
                    break;
                }
                default: return 0;
            }
            // Firms bid most of operating profit for space; the retained sliver is
            // their normal return (the full h margin is already in S).
            return Math.Max(0, profitPerFilledSlot * fillEst * 0.85);
        }

        /// <summary>What share of its roster a firm opening here should expect to
        /// fill, from this cluster's OWN hiring record. A developer can see the
        /// job postings around a site and how they fared, so this is its own
        /// forecast from its own information — never a citywide staffing rate.
        ///
        /// THE PRIOR YIELDS TO EVIDENCE, which is the whole content of this
        /// function. The old form ended `Clamp(0.35 + 0.65 * fill, 0.35, 1.0)`,
        /// so a cluster where every posted slot went begging still forecast a
        /// third of a roster, and a rich remote deposit cleared its hurdle on
        /// staff it would never get. Measured at the clusters holding
        /// zero-staff firms (seeds 1/9/13, --labor, 400 ticks): the estimate sat
        /// pinned on 0.350 at both p10 and p50 while realized fill there was
        /// 0.000, and read 1.000 at staffed firms. The signal separated
        /// perfectly; only the floor hid it.
        ///
        /// THE FLOOR COULD NOT SIMPLY GO, and that is why this is a shrinkage
        /// rather than a deletion. `JobFillRate` is `matched / posted` where
        /// anything was posted and 0 everywhere else, so "nobody would work
        /// here" and "nobody has ever tried to hire here" are the SAME number.
        /// At t = 0 nothing is staffed anywhere; a bare floor-free rule would
        /// mean nothing is ever built. So the 0.35 stays — as a prior for a
        /// developer with no evidence — and posted slots buy it out:
        ///
        ///   weight = posted / (posted + FillEvidenceSlots)
        ///   est    = weight * realized fill + (1 − weight) * 0.35
        ///
        /// An untried cluster still reads 0.35, exactly as before. A cluster
        /// with a long record of full doors still reads 1.0, exactly as before.
        /// A cluster that has posted hundreds of slots and filled none now
        /// reads ~0, which it could not before. The middle moves too — half the
        /// doors filled forecasts half a roster rather than 0.675 — and that is
        /// the same correction, not a separate one.</summary>
        public static double FirmFillEstimate(AccessState acc, int cluster, ZoneKind sector,
                                              EconParams? p = null)
        {
            bool evidence = p != null && p.FillEvidenceWeighting;
            // Per class, so a cluster with a long basic-labor record and no
            // educated-labor record is not told one answer for both.
            double F(int cl) => evidence
                ? ClassFill(acc, cl, cluster)
                : acc.JobFillRate[cl][cluster];
            double fill = sector switch
            {
                ZoneKind.Commercial => 0.7 * F(0) + 0.3 * F(1),
                ZoneKind.Industrial => 0.6 * F(0) + 0.4 * F(1),
                ZoneKind.Office => 0.3 * F(1) + 0.7 * F(2),
                ZoneKind.Extractor => F(0),
                _ => evidence ? 0.5 : 0.5,
            };
            // Flag off: the old affine lift off a hard 0.35 floor, bit for bit.
            return evidence
                ? MathUtil.Clamp(fill, 0.0, 1.0)
                : MathUtil.Clamp(0.35 + 0.65 * fill, 0.35, 1.0);
        }

        /// <summary>The prior 0.35 a developer holds where this cluster has no
        /// hiring record of its own. Kept at the value the old floor used, so a
        /// never-posted-to cluster forecasts exactly what it always did.</summary>
        public const double FillPriorNoEvidence = 0.35;
        /// <summary>Posted-slot mass at which a cluster's own hiring record
        /// carries half the forecast. Roughly a few buildings' worth of doors —
        /// enough that one firm's bad tick does not condemn a location, few
        /// enough that a district with a standing record is believed.</summary>
        public const double FillEvidenceSlots = 20.0;

        private static double ClassFill(AccessState acc, int cl, int cluster)
        {
            if ((uint)cl >= (uint)acc.JobFillRate.Length) return FillPriorNoEvidence;
            var rate = acc.JobFillRate[cl];
            if ((uint)cluster >= (uint)rate.Length) return FillPriorNoEvidence;
            double posted = (uint)cl < (uint)acc.JobsByClass.Length
                            && (uint)cluster < (uint)acc.JobsByClass[cl].Length
                ? acc.JobsByClass[cl][cluster] : 0;
            // MUTANT: evidence never accumulates, so the prior stands whatever
            // the cluster's record — the pre-fix behaviour in the new shape.
            // Its purpose is to fail a check that the shrinkage passes; without
            // it "the estimate is between 0 and 1" would be the only claim, and
            // that cannot fail.
            if (MutantFillPriorNeverYields) posted = 0;
            double weight = posted / (posted + FillEvidenceSlots);
            return weight * rate[cluster] + (1 - weight) * FillPriorNoEvidence;
        }

        /// <summary>MUTANT SWITCH: a cluster's own hiring record never outweighs
        /// the no-evidence prior, so a location that has posted hundreds of
        /// slots and filled none still forecasts a third of a roster. Never a
        /// shipping mode.</summary>
        public static bool MutantFillPriorNeverYields;

        /// <summary>workCluster is the geology the extractor leg prices against.
        /// Passing null prices EVERY raw at suitability 0.5 — a uniform geology
        /// that exists nowhere, and the opposite of the Weber structure the
        /// suitability field anchors. Every caller that holds a WorldState
        /// passes w.Clusters; the null default survives only for callers that
        /// have no world (unit fixtures).</summary>
        /// <summary>Which specialization an office entrant at this cluster
        /// should commit to: the one whose own neighbourhood carries it best.
        /// The entrant's own forecast from its own site — the same shape as an
        /// industrial entrant choosing its recipe by Weber, and the reason the
        /// bid above is a maximum a real firm can actually realize rather than
        /// a number nobody can earn.</summary>
        /// <summary>Set by the engine from FeatureFlags.CommercialLines. A
        /// static because FirmBidPerSlot is static and takes no flags — the
        /// same shape MutantFlatGeologyAssessment beside it uses. Off, the
        /// commercial branch keeps its whole-basket read untouched.</summary>
        public static bool CommercialLinesActive;
        private static bool lineAware => CommercialLinesActive;
        /// <summary>a/b, and 1.0 when b is not usably positive — a shop with no
        /// undivided capture to scale from is not a shop with an infinite
        /// niche.</summary>
        private static double SafeRatio(double a, double b) => b > 1e-12 ? a / b : 1.0;

        public static OfficeKind BestOfficeKind(AccessState acc, int cluster, EconParams p, int siteId)
        {
            var best = OfficeKind.Software; double bestV = double.NegativeInfinity, worstV = double.PositiveInfinity;
            for (int k = 0; k < AccessState.OfficeKindCount; k++)
            {
                double v = acc.OfficeAgglom((OfficeKind)k, cluster, p);
                if (v > bestV) { bestV = v; best = (OfficeKind)k; }
                if (v < worstV) worstV = v;
            }
            // WHEN NOTHING LOCAL FAVOURS ONE SPECIALIZATION, WHAT YOU GET
            // DEPENDS ON WHO TURNS UP. Localization is self-referential — a
            // kind is attractive where that kind already is — so a city that
            // starts with none of them has every kind equally unattractive and
            // whichever the argmax happens to name wins forever. The first cut
            // of this did exactly that: every office chose Software, its jobs
            // WERE the pooled jobs, the maximum over kinds was arithmetically
            // the pooled value, and the whole mechanism measured as a no-op on
            // seed 1 (identical firm counts, identical cash flow, identical
            // vacancy, with the flag on and off).
            //
            // So a flat signal is broken by the entrant itself, deterministic
            // in its own site. This is not noise standing in for a mechanism:
            // it is the honest content of "several specializations are equally
            // viable here", and once one lands its jobs tilt the field for the
            // next entrant nearby. Diversity is seeded by history and then
            // reinforced by localization, which is how specialization actually
            // arises — and it makes the maximum over kinds a real maximum with
            // a different answer in different places, which is the entire point
            // of taking one.
            if (bestV - worstV <= 1e-9)
                best = (OfficeKind)(int)(SplitMix64.Hash01((ulong)siteId * 2654435761UL + 17UL)
                                         * AccessState.OfficeKindCount) ;
            return (OfficeKind)Math.Min((int)best, AccessState.OfficeKindCount - 1);
        }

        public static double BidPerUnit(
            AccessState acc, IPriceContext prices, int cluster, ZoneKind use, int level,
            double[] segmentPresence, EconParams p, double addUnits = 0, double minSupply = 0,
            bool realized = false, ClusterInfo[]? workCluster = null)
            => use == ZoneKind.ResidentialLow || use == ZoneKind.ResidentialHigh
                ? ResidentialBidPerUnit(acc, cluster, use, level, segmentPresence, p,
                                        addUnits, minSupply, realized)
                : FirmBidPerSlot(acc, prices, cluster, use, level, p, out _, workCluster);

        /// <summary>Permitted configurations for a parcel: its zoned kind at any
        /// level. (Rezoning arrives as a change to Zoned from the host.)
        ///
        /// Valuation is PRICE-based: LR = (bid − S) × units even where the
        /// revenue-max price expects fill below the stock. Scaling LR by the
        /// expected fill would feed back into UnitAssessment (tenants pay
        /// S + φ·LR/units) and charge sitting tenants BELOW the market price,
        /// re-opening the door the revenue-max point closed. The cost is that
        /// a thin submarket's paper LR overstates its collected rent by the
        /// expected-vacancy share — visible via the fillRatio overload, never
        /// hidden in the tenant's bill.</summary>
        public static void Assess(
            WorldState w, AccessState acc, IPriceContext prices, Parcel parcel,
            double[] segmentPresence, EconParams p)
        {
            if (parcel.Zoned == ZoneKind.None) { parcel.AssessedLR = 0; parcel.Wedge = 0; return; }

            double aOp(double lump) => Annuity.FlowOf(lump, p.HurdleRate, p.AnnuityHorizon);
            int c = parcel.Cluster;
            var geology = p.NonResLandParity && !MutantFlatGeologyAssessment ? w.Clusters : null;

            // Current-configuration residual (also a candidate: "current use at
            // declining condition, saving δ").
            double currentResidual = 0;
            if (parcel.State == ParcelState.Built)
            {
                // Condition scales the bid (decayed stock commands less) AND the
                // charge base V — decay makes stock cheap on both sides (§4.3).
                // The standing building's units are already IN the stock count
                // (it is Built); minSupply only guards the one refresh window
                // where a just-completed building has not been counted yet.
                double bidCur = BidPerUnit(acc, prices, c, parcel.Use, parcel.Level, segmentPresence, p,
                                           addUnits: 0, minSupply: parcel.Units, realized: true,
                                           workCluster: geology)
                                * p.CondFactor(parcel.Condition);
                currentResidual = (bidCur - SPerUnit(parcel.Level, parcel.Condition, p)) * parcel.Units;
            }

            double bestLR = Math.Max(0, currentResidual);
            int bestLevel = parcel.State == ParcelState.Built ? parcel.Level : 1;
            ZoneKind bestUse = parcel.State == ParcelState.Built ? parcel.Use : parcel.Zoned;
            bool bestScrape = false;

            ZoneKind zonedUse = parcel.Zoned;
            int units = UnitsFor(zonedUse);
            double vNow = parcel.State == ParcelState.Built
                ? parcel.Condition * p.RC(parcel.Level, parcel.Units) : 0;

            for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
            {
                // A candidate configuration must be priced to fill ITSELF.
                // If its units are already part of this kind's standing stock
                // (in-place renovation: same use, same unit count) they must
                // not be added again; a greenfield build or a scrape into a
                // different use ADDS its units to the supply the price clears.
                bool alreadyInStock = parcel.State == ParcelState.Built
                                      && zonedUse == parcel.Use && units == parcel.Units;
                double bid = alreadyInStock
                    ? BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p,
                                 addUnits: 0, minSupply: units, realized: true, workCluster: geology)
                    : BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p,
                                 addUnits: units, realized: true, workCluster: geology);
                double flow = (bid - SPerUnit(lvl, 1.0, p)) * units;
                if (flow <= 0) continue;

                double cost; bool scrape;
                if (parcel.State != ParcelState.Built)
                {
                    cost = p.RC(lvl, units); scrape = false;      // greenfield build
                }
                else if (zonedUse == parcel.Use && units == parcel.Units)
                {
                    if (lvl == parcel.Level) continue;            // == current candidate above
                    // Renovation in place: pay the increment over what stands.
                    cost = Math.Max(0, p.RC(lvl, units) - vNow); scrape = false;
                }
                else
                {
                    // Scrape and rebuild: demolition + new RC − salvage of V (§4.4).
                    // Salvage can never mint money: cost floors at zero.
                    cost = Math.Max(0, p.DemolitionPerUnit * parcel.Units + p.RC(lvl, units)
                           - p.SalvageFraction * vNow);
                    scrape = true;
                }

                double lr = flow - aOp(Math.Max(0, cost - parcel.Escrow));
                if (lr > bestLR)
                {
                    bestLR = lr; bestLevel = lvl; bestUse = zonedUse; bestScrape = scrape;
                }
            }

            parcel.AssessedLR = bestLR;
            parcel.CurrentResidual = currentResidual;
            parcel.Wedge = Math.Max(0, bestLR - Math.Max(0, currentResidual));
            parcel.TargetLevel = bestLevel;
            parcel.TargetUse = bestUse;
            parcel.TargetIsScrape = bestScrape;
        }

        /// <summary>Per-unit assessment charged to an occupant: S + φ·LR/units.
        /// Decomposition for the §4.7 tooltip: S / tax (φ·residual share) /
        /// wedge (φ·wedge share).</summary>
        public static double UnitAssessment(Parcel parcel, EconParams p)
        {
            if (parcel.Units == 0) return 0;
            double s = SPerUnit(parcel.Level, parcel.Condition, p);
            double land = p.CaptureFraction * parcel.AssessedLR / parcel.Units;
            return s + land + StructureTaxPerUnit(parcel, p);
        }

        /// <summary>τ_S leg of the split rate (§4.3) — treasury revenue on
        /// structure value, never escrow (raising it must STALL upgrades).</summary>
        public static double StructureTaxPerUnit(Parcel parcel, EconParams p)
            => p.StructureTaxRate * parcel.Condition * p.RC(parcel.Level, 1);

        /// <summary>Capitalized land value for display: P_L = LR/(r + τ_L) with
        /// r = h (design §4.3). At full capture the market price of land
        /// approaches zero — deliberately nothing to speculate on.</summary>
        public static double CapitalizedLandValue(Parcel parcel, EconParams p)
        {
            double phi = MathUtil.Clamp(p.CaptureFraction, 0, 0.999);
            double tauL = phi / (1 - phi) * p.HurdleRate;
            return parcel.AssessedLR / (p.HurdleRate + tauL);
        }
    }
}
