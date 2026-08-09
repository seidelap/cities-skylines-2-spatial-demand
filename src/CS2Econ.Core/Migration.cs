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
        /// (the principled version of vanilla's homelessness rule).
        /// distressShare counts sheltered households in full and households in
        /// any insolvency stage at half weight: funded emigration removes the
        /// distressed before they shelter, so a pure homeless share was blind
        /// to an insolvency conveyor and kept arrivals flowing (turnstile).</summary>
        public static double Attractiveness(
            WorldState w, AccessState acc, int segment, double avgRentBySeg, double distressShare,
            double unemploymentRate, EconParams p)
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
            // Unemployment above its natural rate is a DIRECT brake, as in
            // vanilla (DemandParameterData.m_UnemploymentEffect against
            // m_NeutralUnemployment). Routing it only through mean income —
            // which is what we did — is far too weak: the city sat at 55%
            // unemployment for 400 ticks while still taking arrivals and
            // never sending a single departure (measured).
            double slack = Math.Max(0, unemploymentRate - p.NeutralUnemployment);
            return meanAccess / 4.0
                   + 0.35 * meanIncome / p.WageBasic
                   - 0.55 * rentBurden
                   - 2.0 * distressShare
                   - p.UnemploymentEffect * slack;
        }

        public struct Flows
        {
            public int[] ArrivalsBySegment;
            public int[] DeparturesBySegment;
            /// <summary>Pre-cap desired inflow RATE per segment (households/tick).
            /// Arrivals the absorption budget deferred still queue as demand
            /// pressure: residual demand reads THIS, so construction sees the
            /// queue at the border — realized arrivals wait for units.</summary>
            public double[] DesiredBySegment;
        }

        public static Flows Step(
            WorldState w, AccessState acc, double[] avgRentBySeg, double distressShare,
            double unemploymentRate, double[] measuredTurnover, EconParams p, bool endogenousOutside)
        {
            var m = w.Migration;
            int nSeg = Segment.Count;
            var flows = new Flows
            {
                ArrivalsBySegment = new int[nSeg],
                DeparturesBySegment = new int[nSeg],
                DesiredBySegment = new double[nSeg],
            };

            int cityPop = 0;
            foreach (var h in w.Households) if (h.ExitedTick < 0) cityPop++;
            double popScale = Math.Max(60, cityPop);
            double prominence = endogenousOutside ? 1.0 + cityPop / p.ProminenceScale : 1.0;
            double netInflowThisTick = 0;

            // Absorption budget (the vacancy field's volume integral × the fill
            // hazard): arrivals per tick are capped by how fast the standing
            // vacant stock can actually lease up. One brake, both outside-world
            // modes; replaces the old flat 1%/tick clamp.
            // Vacancy weighted by how much each density APPEALS to a segment
            // (Segment.DensityAppeal), rather than split by a hard permission:
            // a tower overhang is worth little to a family but not nothing, so
            // it neither fully counts toward their absorption budget nor is
            // excluded from it.
            double vacantLow = 0, vacantHigh = 0, occupiedLow = 0, occupiedHigh = 0;
            double pipelineLow = 0, pipelineHigh = 0;
            foreach (var pl in w.Parcels)
            {
                bool high = pl.Use == ZoneKind.ResidentialHigh;
                if (pl.State == ParcelState.UnderConstruction && pl.IsResidential)
                {
                    // Units completing shortly are absorbable supply: a mover
                    // signs for a home before it is finished.
                    if (high) pipelineHigh += pl.Units; else pipelineLow += pl.Units;
                    continue;
                }
                if (pl.State != ParcelState.Built || !pl.IsResidential || pl.Warehousing) continue;
                int v = pl.Vacant;
                double occ = Math.Max(0, pl.Units - v);
                if (high) { vacantHigh += v; occupiedHigh += occ; }
                else { vacantLow += v; occupiedLow += occ; }
            }
            // Available-unit FLOW, not vacancy stock: standing vacancy leasing
            // up, plus the pipeline, plus churn out of occupied stock. The
            // churn term is the MEASURED flow (EMA of units actually freed
            // via Vacate, kept by the engine) floored by the turnover prior
            // (`HousingTurnoverRate` × occupied). Neither alone works: the
            // bare prior admitted ~50× more arrivals than units actually
            // freed (churnprobe: 98% of exits were arrivals that waited out
            // their patience and never found a unit), while the bare
            // measurement deadlocks a saturated no-churn city (no churn → no
            // arrivals → no churn; the debug fixture froze at 40 starts /
            // zero industry). The prior keeps the door ajar; the queue-
            // congestion gate below is what now stops over-admission from
            // piling up a doomed queue.
            double flowLow = p.VacancyFillHazard * (vacantLow + pipelineLow)
                             + Math.Max(measuredTurnover[0], p.HousingTurnoverRate * occupiedLow);
            double flowHigh = p.VacancyFillHazard * (vacantHigh + pipelineHigh)
                              + Math.Max(measuredTurnover[1], p.HousingTurnoverRate * occupiedHigh);

            // NOTE on the standing unhoused queue: arrivals the budget admits
            // can wait up to one patience window (3 × InsolvencyGraceTicks)
            // before giving up, so a queue of roughly budget × patience stands
            // at the door in steady state, and arrivals beyond its service
            // rate bounce ("failed arrivals" in the exit telemetry, distinct
            // from displacement). An experiment gated admissions by the
            // queue's Little's-law drain rate; it eliminated most bouncing
            // but froze the city: the standing queue is load-bearing — it is
            // counted in SegmentPresence, where it holds clearing prices up
            // and feeds the residual demand that makes construction pencil.
            // Bouncing is the honest cost of a queue the market can see; it
            // damps further arrivals through the distress term above.

            // Pass 1: desired inflow per segment (uncapped Rosen–Roback gap).
            double desiredTotal = 0;
            for (int s = 0; s < nSeg; s++)
            {
                double attract = Attractiveness(w, acc, s, avgRentBySeg[s], distressShare, unemploymentRate, p);
                m.SegmentAttractEma[s] = MathUtil.Ema(m.SegmentAttractEma[s], attract, 0.05);

                double outsideU = BaseOutsideUtility + (endogenousOutside ? m.ReservationThreshold[s] : 0);
                // The design's inflow is LAGGED: word travels before people move.
                double gap = m.SegmentAttractEma[s] - outsideU;
                double desired = p.MigInElasticity * Math.Max(0, gap) * popScale * prominence
                                 * (1.0 + (endogenousOutside ? m.NetworkMemory : 0));
                flows.DesiredBySegment[s] = desired;
                desiredTotal += desired;
            }

            // Pass 2: ration each segment against ITS OWN feasible-vacancy
            // budget, shared pro-rata with the other segments competing for
            // the same stock; draw realized flows.
            for (int s = 0; s < nSeg; s++)
            {
                // Absorbable flow for THIS segment: low-density supply in full
                // plus high-density supply discounted by its appeal.
                double appealHigh = Segment.All[s].DensityAppeal(ZoneKind.ResidentialHigh);
                double budget = flowLow + appealHigh * flowHigh;
                double segScale = desiredTotal > budget && desiredTotal > 1e-9
                    ? budget / desiredTotal : 1.0;
                double inRate = flows.DesiredBySegment[s] * segScale;
                // Out-migration: responds to a LAGGED signal, lower elasticity.
                double outsideU = BaseOutsideUtility + (endogenousOutside ? m.ReservationThreshold[s] : 0);
                double gap = m.SegmentAttractEma[s] - outsideU;
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
                    // REALIZED net, not desired: only actual moves draw it down.
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
