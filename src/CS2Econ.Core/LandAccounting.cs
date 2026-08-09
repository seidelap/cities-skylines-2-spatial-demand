using System;

namespace CS2Econ.Core
{
    /// <summary>Cluster-level price/cost context Tier C reads from Tier D.
    /// Kept behind an interface so land accounting never reaches into trade
    /// internals (and the harness can stub it for unit tests).</summary>
    public interface IPriceContext
    {
        double LocalPrice(Res r);
        /// <summary>Delivered cost of one unit of r at cluster c (local price + haul,
        /// or import parity — whichever is lower). Design §4.2 freight-in term.</summary>
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
    /// this file reads Parcel.OccupantHouseholds' payments or any realized rent.</summary>
    public static class LandAccounting
    {
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

        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p, double addUnits = 0, double minSupply = 0)
            => ResidentialBidPerUnit(acc, cluster, kind, level, segmentPresence, p, out _, addUnits, minSupply);

        /// <summary>Overload exposing the expected fill ratio at the returned
        /// price — 1.0 in the cleared regime (the stock fills), below 1.0 in
        /// excess supply where the curve's depth caps the fill and the
        /// balance stands vacant at the flat-tail price. For telemetry,
        /// overlays and checks; valuation stays price-based (see Assess).</summary>
        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p, out double fillRatio,
            double addUnits = 0, double minSupply = 0)
        {
            fillRatio = 1.0;
            System.Threading.Interlocked.Increment(ref AuditTotalPriced);
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
                const double MinTailMass = 0.05;
                double target = Math.Max(1e-9, totalAt0 - MinTailMass);
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
            out Res chosenOutput, ClusterInfo[]? workCluster)
        {
            chosenOutput = Res.Services;
            double fillEst = FirmFillEstimate(acc, cluster, sector);
            double quality = p.Quality(level) / p.Quality(1);
            // Production needs labor: revenue AND wages both scale with fill —
            // an unstaffed firm produces (and earns) nothing.
            double profitPerFilledSlot;
            switch (sector)
            {
                case ZoneKind.Commercial:
                {
                    double capturePerSlot = acc.PhantomCommercialCapture(cluster, 6.0 * quality) / 6.0;
                    double wage = 0.7 * p.WageBasic + 0.3 * p.WageSkilled;
                    // Restocking cost: the consumption basket at delivered prices,
                    // relative to its anchor value (imported/near baskets squeeze margin).
                    double cogsIndex = 0;
                    foreach (var (res, share) in ResourceCatalog.Basket)
                        cogsIndex += share * prices.DeliveredCost(res, cluster) / ResourceCatalog.Anchor[(int)res];
                    profitPerFilledSlot = capturePerSlot * (p.CommercialMarkup - 0.1 * (cogsIndex - 0.3)) - wage;
                    break;
                }
                case ZoneKind.Industrial:
                {
                    // Weber: max over recipes of margin at THIS location.
                    double wage = 0.6 * p.WageBasic + 0.4 * p.WageSkilled;
                    profitPerFilledSlot = double.NegativeInfinity;
                    foreach (var recipe in ResourceCatalog.Recipes)
                    {
                        double outNet = Math.Max(prices.LocalPrice(recipe.Output),
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
                    double wage = 0.3 * p.WageSkilled + 0.7 * p.WageEducated;
                    profitPerFilledSlot = p.OfficeOutputPerSlot * quality * p.OfficeOutputPrice
                                          * acc.OfficeAgglomMult[cluster] - wage;
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
                        double outNet = Math.Max(prices.LocalPrice(res), prices.BestExportNet(res, cluster));
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

        public static double FirmFillEstimate(AccessState acc, int cluster, ZoneKind sector)
        {
            double fill = sector switch
            {
                ZoneKind.Commercial => 0.7 * acc.JobFillRate[0][cluster] + 0.3 * acc.JobFillRate[1][cluster],
                ZoneKind.Industrial => 0.6 * acc.JobFillRate[0][cluster] + 0.4 * acc.JobFillRate[1][cluster],
                ZoneKind.Office => 0.3 * acc.JobFillRate[1][cluster] + 0.7 * acc.JobFillRate[2][cluster],
                ZoneKind.Extractor => acc.JobFillRate[0][cluster],
                _ => 0.5,
            };
            // A new firm competes for labor at roughly the cluster's current fill;
            // never assume total famine or perfection.
            return MathUtil.Clamp(0.35 + 0.65 * fill, 0.35, 1.0);
        }

        public static double BidPerUnit(
            AccessState acc, IPriceContext prices, int cluster, ZoneKind use, int level,
            double[] segmentPresence, EconParams p, double addUnits = 0, double minSupply = 0)
            => use == ZoneKind.ResidentialLow || use == ZoneKind.ResidentialHigh
                ? ResidentialBidPerUnit(acc, cluster, use, level, segmentPresence, p, addUnits, minSupply)
                : FirmBidPerSlot(acc, prices, cluster, use, level, p);

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
                                           addUnits: 0, minSupply: parcel.Units)
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
                    ? BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p, addUnits: 0, minSupply: units)
                    : BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p, addUnits: units);
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
