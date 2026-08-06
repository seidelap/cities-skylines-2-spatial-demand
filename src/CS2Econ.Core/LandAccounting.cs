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
        /// presence-weighted bid of the segments feasible for the density kind —
        /// the WTP of the demand that actually exists, not of the single richest
        /// bidder (a segment with 300 members cannot be the marginal bidder for
        /// 8,000 units citywide). Uniform assessment then charges this marginal
        /// bid; heterogeneity acts through sorting and timing (design §4.3).
        /// Concave quality uplift against convex RC gives the interior ℓ*.</summary>
        public static double ResidentialBidPerUnit(
            AccessState acc, int cluster, ZoneKind kind, int level,
            double[] segmentPresence, EconParams p)
        {
            double sumW = 0, sumBid = 0;
            for (int s = 0; s < Segment.Count; s++)
            {
                double pres = segmentPresence[s];
                if (pres < 1) continue;
                var seg = Segment.All[s];
                bool highDensity = kind == ZoneKind.ResidentialHigh;
                if (highDensity && seg.DensityTolerance < 0.5) continue;
                double income = acc.ExpectedIncome(s, cluster, p);
                // Convex premium: location differences must be strong enough to
                // produce level geography (ℓ* gradients), not a flat ±20% band.
                double rel = acc.AccessValue[s][cluster] / acc.MeanAccess;
                double premium = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                double bid = seg.MaxRentShare * income * premium * p.BidAccessScale
                             * (p.Quality(level) / p.Quality(1));
                // Presence-weighted with a mild tilt toward stronger bidders: the
                // richer half of feasible demand moves the marginal bid up, but a
                // thin sliver of rich demand cannot set the price for everyone.
                double weight = pres * (0.5 + bid);
                sumW += weight; sumBid += weight * bid;
            }
            return sumW > 0 ? sumBid / sumW : 0;
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
            double[] segmentPresence, EconParams p)
            => use == ZoneKind.ResidentialLow || use == ZoneKind.ResidentialHigh
                ? ResidentialBidPerUnit(acc, cluster, use, level, segmentPresence, p)
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
                double bidCur = BidPerUnit(acc, prices, c, parcel.Use, parcel.Level, segmentPresence, p)
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
                double bid = BidPerUnit(acc, prices, c, zonedUse, lvl, segmentPresence, p);
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
