using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Tier A (design §4.1): migration as rate-limited flow against a
    /// weakly endogenous outside world. Inflow = elasticity × (attractiveness −
    /// outside utility), lagged and ASYMMETRIC — out-migration responds more
    /// slowly than in-migration (attachment, loss aversion), giving boom/bust
    /// asymmetry. Outside utility carries three slow scalars: reservation
    /// threshold (field drawdown), prominence (field widening with city size),
    /// and network memory (chain-migration momentum).</summary>
    public static class Migration
    {
        public const double BaseOutsideUtility = 1.05;

        /// <summary>Per-segment Rosen–Roback attractiveness: wages, rents,
        /// amenities — with the rent term damping arrivals under housing stress
        /// (the principled version of vanilla's homelessness rule).</summary>
        public static double Attractiveness(
            WorldState w, AccessState acc, int segment, double avgRentBySeg, double homelessShare, EconParams p)
        {
            var seg = Segment.All[segment];
            // Population-weighted mean access value for the segment.
            double sumV = 0, sumW = 0;
            for (int c = 0; c < acc.C; c++)
            {
                double weight = 1 + w.HouseholdCountByCluster[c];
                sumV += acc.AccessValue[segment][c] * weight;
                sumW += weight;
            }
            double meanAccess = sumW > 0 ? sumV / sumW : 0;
            double meanIncome = 0, incomeW = 0;
            for (int c = 0; c < acc.C; c++)
            {
                double wgt = 1 + w.HouseholdCountByCluster[c];
                meanIncome += acc.ExpectedIncome(segment, c, p) * wgt;
                incomeW += wgt;
            }
            meanIncome = incomeW > 0 ? meanIncome / incomeW : 0;

            double rentBurden = meanIncome > 0.1 ? avgRentBySeg / (seg.MaxRentShare * meanIncome) : 2.0;
            // Absolute scale: a citywide amenity/access improvement MUST move the
            // migration margin (normalizing by the city mean would cancel it).
            return meanAccess / 4.0
                   + 0.35 * meanIncome / p.WageBasic
                   - 0.55 * rentBurden
                   - 2.0 * homelessShare;
        }

        public struct Flows
        {
            public int[] ArrivalsBySegment;
            public int[] DeparturesBySegment;
        }

        public static Flows Step(
            WorldState w, AccessState acc, double[] avgRentBySeg, double homelessShare,
            EconParams p, bool endogenousOutside)
        {
            var m = w.Migration;
            int nSeg = Segment.Count;
            var flows = new Flows
            {
                ArrivalsBySegment = new int[nSeg],
                DeparturesBySegment = new int[nSeg],
            };

            int cityPop = 0;
            foreach (var h in w.Households) if (h.ExitedTick < 0) cityPop++;
            double popScale = Math.Max(60, cityPop);
            double prominence = endogenousOutside ? 1.0 + cityPop / p.ProminenceScale : 1.0;
            double netInflowThisTick = 0;

            for (int s = 0; s < nSeg; s++)
            {
                double attract = Attractiveness(w, acc, s, avgRentBySeg[s], homelessShare, p);
                m.SegmentAttractEma[s] = MathUtil.Ema(m.SegmentAttractEma[s], attract, 0.05);

                double outsideU = BaseOutsideUtility + (endogenousOutside ? m.ReservationThreshold[s] : 0);
                // The design's inflow is LAGGED: word travels before people move.
                double gap = m.SegmentAttractEma[s] - outsideU;

                // In-migration: faster than outflow, widened by prominence,
                // momentum from network memory (chain migration).
                double inRate = p.MigInElasticity * Math.Max(0, gap) * popScale * prominence
                                * (1.0 + (endogenousOutside ? m.NetworkMemory : 0));
                // With the endogenous outside world OFF there is no reservation
                // threshold to equilibrate, so a physical absorption cap (~1%/tick
                // citywide) is the only brake. Tier A's own brake is the threshold.
                if (!endogenousOutside)
                    inRate = Math.Min(inRate, 0.01 * popScale / Segment.Count);
                // Out-migration: responds to a LAGGED signal, lower elasticity.
                m.OutSignalEma[s] = MathUtil.Ema(m.OutSignalEma[s], Math.Max(0, -gap), p.MigOutLagAlpha);
                double outRate = p.MigOutElasticity * m.OutSignalEma[s] * popScale;

                flows.ArrivalsBySegment[s] = PoissonDraw(inRate, ref w.Rng);
                flows.DeparturesBySegment[s] = PoissonDraw(outRate, ref w.Rng);
                netInflowThisTick += flows.ArrivalsBySegment[s] - flows.DeparturesBySegment[s];

                if (endogenousOutside)
                {
                    // Reservation threshold: drawdown of the regional migration
                    // field by cumulative NET in-migration (§4.1 — return flow
                    // replenishes the field), decaying replenishment over time.
                    double net = flows.ArrivalsBySegment[s] - flows.DeparturesBySegment[s];
                    m.ReservationThreshold[s] += net / (p.RegionSize * p.MigFieldResponsiveness);
                    m.ReservationThreshold[s] = Math.Max(0, m.ReservationThreshold[s] * (1.0 - p.ReservationReplenish));
                }
            }

            if (endogenousOutside)
            {
                m.CumulativeNetInflow += netInflowThisTick;
                m.NetworkMemory = m.NetworkMemory * p.NetworkMemoryDecay
                                  + p.NetworkMemoryGain * Math.Max(0, netInflowThisTick) / Math.Max(1, popScale * 0.01);
                m.NetworkMemory = Math.Min(m.NetworkMemory, 2.0);
            }
            return flows;
        }

        /// <summary>Small-rate Poisson via inversion; deterministic given rng.</summary>
        public static int PoissonDraw(double lambda, ref SplitMix64 rng)
        {
            if (lambda <= 0) return 0;
            if (lambda > 30) return (int)Math.Round(lambda + Math.Sqrt(lambda) * (rng.NextDouble() * 2 - 1));
            double l = Math.Exp(-lambda), q = 1.0;
            int k = 0;
            do { k++; q *= rng.NextDouble(); } while (q > l);
            return k - 1;
        }
    }
}
