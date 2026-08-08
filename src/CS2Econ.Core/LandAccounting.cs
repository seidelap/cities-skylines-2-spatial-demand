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

        /// <summary>Residential bid per unit at (cluster, kind, level): the
        /// MARKET-CLEARING price — the willingness-to-pay of the marginal
        /// bidder, i.e. the price at which the stock exactly fills.
        ///
        /// Rank the segments that would choose this cluster by WTP descending
        /// and walk down the queue accumulating their mass until the standing
        /// units are filled; the bidder who takes the last unit sets the price
        /// (interpolated within a segment's mass, so the demand curve is
        /// continuous rather than an 8-step staircase). Excess supply — demand
        /// exhausted before the stock fills — prices DOWN in proportion to the
        /// shortfall, which is how a vacancy overhang softens rent; at zero
        /// demand the bid goes to zero and the land rent with it, since land
        /// nobody wants earns nothing.
        ///
        /// Neither the max bidder (one rich eccentric re-rating a building —
        /// the affordability spiral this replaced) nor the presence-weighted
        /// mean (which ignores quantity entirely and so could not respond to
        /// vacancy at all). This is the Alonso bid-rent price and the
        /// Shapley–Shubik equilibrium price; a person-by-unit ascending auction
        /// converges to it.
        ///
        /// Circularity guard (§3) intact and, if anything, stronger: the inputs
        /// are potential demand (presence × access share) and standing stock —
        /// never this parcel's realized rent, and never realized occupancy. The
        /// price↔quantity feedback it does create is NEGATIVE (dear → vacancy →
        /// cheaper), i.e. stabilizing tâtonnement, not the self-reinforcing loop
        /// the guard exists to forbid.
        ///
        /// supplyFloor: units the candidate configuration would itself add, so
        /// the first tower in a neighborhood is priced to fill ITSELF rather
        /// than dividing by an empty stock.</summary>
        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p, double supplyFloor = 0)
        {
            Span<double> wtp = stackalloc double[Segment.Count];
            Span<double> mass = stackalloc double[Segment.Count];
            int n = 0;
            bool highDensity = kind == ZoneKind.ResidentialHigh;
            double quality = p.Quality(level) / p.Quality(1);
            for (int s = 0; s < Segment.Count; s++)
            {
                double pres = segmentPresence[s];
                if (pres < 1) continue;
                var seg = Segment.All[s];
                // Density enters as a PREFERENCE discount on willingness to pay,
                // not as a gate: a family will pay for an apartment, just less
                // than for a house of the same access and quality. The old hard
                // exclusion left high-density stock with no legal bidders and
                // therefore no land rent (see Segment.DensityAppeal).
                double appeal = seg.DensityAppeal(kind);
                double income = acc.ExpectedIncome(s, cluster, p);
                // Convex premium: location differences must be strong enough to
                // produce level geography (ℓ* gradients), not a flat ±20% band.
                double rel = acc.AccessValue[s][cluster] / acc.MeanAccess;
                double premium = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                wtp[n] = seg.MaxRentShare * income * premium * p.BidAccessScale * quality * appeal;
                // How many of this segment want THIS (kind, cluster) — the
                // share is normalized over kind AND cluster, so it is
                // commensurate with the per-kind stock below. A per-cluster
                // share here double-counts every density-tolerant household
                // across both queues.
                int ki = highDensity ? 1 : 0;
                double share = acc.SegmentKindShare.Length > s
                               && acc.SegmentKindShare[s][ki].Length > cluster
                    ? acc.SegmentKindShare[s][ki][cluster] : 0;
                mass[n] = pres * share;
                n++;
            }
            if (n == 0) return 0;

            // Descending by WTP (insertion sort; n ≤ Segment.Count).
            for (int i = 1; i < n; i++)
            {
                double kw = wtp[i], km = mass[i];
                int j = i - 1;
                while (j >= 0 && wtp[j] < kw) { wtp[j + 1] = wtp[j]; mass[j + 1] = mass[j]; j--; }
                wtp[j + 1] = kw; mass[j + 1] = km;
            }

            double stock = acc.HousingStock.Length == 2 && acc.HousingStock[highDensity ? 1 : 0].Length > cluster
                ? acc.HousingStock[highDensity ? 1 : 0][cluster] : 0;
            double supply = Math.Max(stock, supplyFloor);
            if (supply <= 1e-9) return wtp[0];        // nothing to fill: top bidder

            double cum = 0;
            for (int i = 0; i < n; i++)
            {
                double prev = cum;
                cum += mass[i];
                if (cum >= supply)
                {
                    // Marginal bidder lies inside segment i's mass; interpolate
                    // from the segment above so the curve is continuous.
                    double frac = mass[i] > 1e-12 ? (supply - prev) / mass[i] : 1.0;
                    double above = i > 0 ? wtp[i - 1] : wtp[0];
                    return above + (wtp[i] - above) * MathUtil.Clamp(frac, 0, 1);
                }
            }
            // Excess supply: demand runs out before the stock fills. Price falls
            // below the deepest bidder in proportion to how far short it fell —
            // continuous, and → 0 as demand → 0.
            return wtp[n - 1] * MathUtil.Clamp(cum / supply, 0, 1);
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
            double[] segmentPresence, EconParams p, double supplyFloor = 0)
            => use == ZoneKind.ResidentialLow || use == ZoneKind.ResidentialHigh
                ? ResidentialBidPerUnit(acc, cluster, use, level, segmentPresence, p, supplyFloor)
                : FirmBidPerSlot(acc, prices, cluster, use, level, p);

        /// <summary>Permitted configurations for a parcel: its zoned kind at any
        /// level. (Rezoning arrives as a change to Zoned from the host.)</summary>
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
                // supplyFloor = this parcel's own units: a standing building is
                // part of the stock its price has to clear.
                double bidCur = BidPerUnit(acc, prices, c, parcel.Use, parcel.Level, segmentPresence, p,
                                           parcel.Units)
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
                // A candidate configuration must be priced to fill ITSELF: the
                // proposed units join the stock the clearing price has to
                // absorb, so a tower proposed into a thin market prices as a
                // tower, not as the neighborhood's scarcest unit.
                double bid = BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p, units);
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
