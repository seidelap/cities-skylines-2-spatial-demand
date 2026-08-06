using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Residual demand per (cluster, use) — what predicts whether a new
    /// building fills (design §4.2): seeker/profit-based demand net of incumbents'
    /// vacancies AND the pipeline claims ledger. Computed at refresh cadence.</summary>
    public sealed class ResidualDemand
    {
        public double[][] ByUse = Array.Empty<double[]>();   // [use][cluster], units/slots
        private static readonly ZoneKind[] Uses =
            { ZoneKind.ResidentialLow, ZoneKind.ResidentialHigh, ZoneKind.Commercial, ZoneKind.Industrial, ZoneKind.Office, ZoneKind.Extractor };

        public static int UseIndex(ZoneKind k) => k switch
        {
            ZoneKind.ResidentialLow => 0, ZoneKind.ResidentialHigh => 1, ZoneKind.Commercial => 2,
            ZoneKind.Industrial => 3, ZoneKind.Office => 4, ZoneKind.Extractor => 5, _ => -1,
        };

        public double Get(int cluster, ZoneKind use, ClaimsLedger claims)
        {
            int u = UseIndex(use);
            return u < 0 ? 0 : ByUse[u][cluster] - claims.Get(cluster, use);
        }

        /// <summary>seekersBySegment: households currently looking (unhoused +
        /// sheltered + expected near-term arrivals EMA).</summary>
        public void Refresh(WorldState w, AccessState acc, TradeSystem trade,
                            double[] seekersBySegment, double[] seekerPresence, EconParams p)
        {
            int C = acc.C;
            ByUse = new double[6][];
            for (int u = 0; u < 6; u++) ByUse[u] = new double[C];

            // ---- residential: seekers spread by choice shares, weighted by
            // AFFORDABILITY of the stock their demand would trigger. A seeker who
            // cannot pay a new unit's assessment is shelter/migration pressure,
            // not construction demand — residual predicts FILL (§4.2), and
            // counting priced-out demand stalls construction against a vacancy
            // overhang it can never absorb.
            var share = new double[C];
            for (int s = 0; s < Segment.Count; s++)
            {
                double seekers = seekersBySegment[s];
                if (seekers <= 0) continue;
                var seg = Segment.All[s];
                ZoneKind kind = seg.DensityTolerance >= 0.5 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
                double sum = 0;
                for (int c = 0; c < C; c++)
                {
                    double bid = LandAccounting.ResidentialBidPerUnit(acc, c, kind, 2, seekerPresence, p);
                    double assessProxy = p.CaptureFraction * bid
                                         + (1 - p.CaptureFraction) * LandAccounting.SPerUnit(2, 1.0, p);
                    double affordCap = seg.MaxRentShare * acc.ExpectedIncome(s, c, p) * 1.1;
                    double afford = assessProxy > 1e-9
                        ? MathUtil.Clamp(1.2 * affordCap / assessProxy - 0.2, 0, 1) : 1;
                    share[c] = Math.Exp(acc.AccessValue[s][c] / 1.5) * afford;
                    sum += share[c];
                }
                if (sum <= 0) continue;
                for (int c = 0; c < C; c++)
                {
                    double mass = seekers * share[c] / sum;
                    if (seg.DensityTolerance >= 0.5) { ByUse[1][c] += mass * 0.75; ByUse[0][c] += mass * 0.25; }
                    else { ByUse[0][c] += mass * 0.85; ByUse[1][c] += mass * 0.15; }
                }
            }

            // ---- firm demand signals -----------------------------------------
            for (int c = 0; c < C; c++)
            {
                double comProfit = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Commercial, 2, p);
                ByUse[2][c] = 10.0 * comProfit / p.WageBasic;
                double indProfit = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Industrial, 2, p);
                ByUse[3][c] = 10.0 * indProfit / p.WageBasic;
                double offProfit = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Office, 2, p);
                ByUse[4][c] = 10.0 * offProfit / p.WageBasic;
                double extProfit = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Extractor, 2, p);
                ByUse[5][c] = 10.0 * extProfit / p.WageBasic;
            }

            // Export-base feedback: sustained raw exports induce processing
            // demand near the raw sources (Weber pull, design §4.5).
            if (trade.InducedProcessingSlots > 0)
            {
                double totalRawW = 0;
                var rawW = new double[C];
                for (int c = 0; c < C; c++)
                {
                    double cost = trade.DeliveredCost(Res.Raw, c);
                    rawW[c] = Math.Exp(-1.2 * cost);
                    totalRawW += rawW[c];
                }
                if (totalRawW > 0)
                    for (int c = 0; c < C; c++)
                        ByUse[3][c] += trade.InducedProcessingSlots * rawW[c] / totalRawW;
            }

            // ---- net out incumbents' vacancies and the claims ledger ---------
            var vacantByUse = new double[6][];
            for (int u = 0; u < 6; u++) vacantByUse[u] = new double[C];
            foreach (var pl in w.Parcels)
            {
                int u = UseIndex(pl.Use);
                if (u < 0) continue;
                if (pl.State == ParcelState.Built)
                {
                    if (pl.IsResidential && !pl.Warehousing) vacantByUse[u][pl.Cluster] += pl.Vacant;
                    else if (!pl.IsResidential && pl.OccupantFirm < 0) vacantByUse[u][pl.Cluster] += pl.Units;
                }
            }
            for (int u = 0; u < 6; u++)
                for (int c = 0; c < C; c++)
                    ByUse[u][c] -= vacantByUse[u][c];
            // NOTE: claims are subtracted LIVE in Get(), not baked at refresh —
            // commits inside a refresh window must see each other immediately
            // (the §4.6 pipeline-not-mirage discipline; scrutiny finding #19).
        }
    }

    /// <summary>Construction dynamics (design §4.6): softmax over hurdle-gated
    /// developer returns; claims ledger decremented at commitment; construction
    /// lag; milestone re-evaluation; calibration loop against predictions at
    /// decision time.</summary>
    public sealed class ConstructionSystem
    {
        public int StartedTotal, AbandonedTotal, CompletedTotal;
        public readonly List<(long tick, int parcel)> AbandonEvents = new List<(long, int)>();

        private struct Candidate
        {
            public int ParcelId;
            public int Level;
            public double ReturnRate;
            public double PredictedRent, PredictedAbsorption;
        }
        private readonly List<Candidate> _cands = new List<Candidate>();

        public void Step(WorldState w, AccessState acc, TradeSystem trade, ResidualDemand residuals,
                         double[] segmentPresence, EconParams p, FeatureFlags flags)
        {
            // ---- progress existing projects (always, any mode) ---------------
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.UnderConstruction) continue;
                pl.BuildProgress++;

                if (pl.CommittedCost > 0)
                {
                    // Spend evenly over the lag: developer capital → materials.
                    double spend = pl.CommittedCost / pl.BuildTotal;
                    w.Ledger.Transfer(Account.PhantomDeveloper, Account.OutsideWorld, spend);
                }

                // Milestone re-evaluation (§4.6): abandon when expected return has
                // fallen below the remaining cost — stalled half-built projects in
                // busts, from existing machinery.
                if (flags.ConstructionRewire && pl.CommittedCost > 0 &&
                    (pl.BuildProgress == pl.BuildTotal / 4 || pl.BuildProgress == pl.BuildTotal / 2
                     || pl.BuildProgress == 3 * pl.BuildTotal / 4))
                {
                    double flow = ExpectedFlow(w, acc, trade, residuals, segmentPresence, pl, pl.Level, p,
                                               out _, out _);
                    double remaining = pl.CommittedCost * (1.0 - (double)pl.BuildProgress / pl.BuildTotal);
                    if (flow < p.AbandonMarginFactor * p.HurdleRate * Math.Max(1, remaining))
                    {
                        w.Claims.Add(pl.Cluster, pl.Use, -pl.Units);
                        pl.State = ParcelState.Empty;
                        pl.Units = 0; pl.CommittedCost = 0;
                        AbandonedTotal++;
                        AbandonEvents.Add((w.Tick, pl.Id));
                        continue;
                    }
                }

                if (pl.BuildProgress >= pl.BuildTotal)
                {
                    pl.State = ParcelState.Built;
                    pl.Condition = 1.0;
                    pl.CompletedTick = w.Tick;
                    pl.CommittedCost = 0;
                    w.Claims.Add(pl.Cluster, pl.Use, -pl.Units);
                    CompletedTotal++;
                }
            }

            if (!flags.ConstructionRewire) return;

            // ---- scan eligible empty parcels (staggered 1/5 per tick) --------
            _cands.Clear();
            for (int i = 0; i < w.Parcels.Count; i++)
            {
                var pl = w.Parcels[i];
                if (pl.State != ParcelState.Empty || pl.Zoned == ZoneKind.None) continue;
                if ((pl.Id % 5) != (int)(w.Tick % 5)) continue;

                // "New construction spawns directly at the residual-maximizing
                // level" (§4.4): choose ℓ by FLOW, then gate on the return rate.
                int bestLvl = 0; double bestFlow = double.NegativeInfinity, bestRet = 0, bestRent = 0, bestAbs = 0;
                for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
                {
                    double flow = ExpectedFlow(w, acc, trade, residuals, segmentPresence, pl, lvl, p,
                                               out double rent, out double abs);
                    int units = LandAccounting.UnitsFor(pl.Zoned);
                    double rc = p.RC(lvl, units);
                    if (flow > bestFlow) { bestFlow = flow; bestRet = flow / rc; bestLvl = lvl; bestRent = rent; bestAbs = abs; }
                }
                if (bestRet > p.HurdleRate * 1.5)   // commit margin damps churn at the hurdle boundary
                    _cands.Add(new Candidate
                    {
                        ParcelId = i, Level = bestLvl, ReturnRate = bestRet,
                        PredictedRent = bestRent, PredictedAbsorption = bestAbs,
                    });
            }
            if (_cands.Count == 0) return;

            // ---- softmax start selection (logit spread: near-equivalent sites
            // split rather than herd), capacity-capped -------------------------
            int starts = Math.Min(p.MaxStartsPerTick, _cands.Count);
            var weights = new double[_cands.Count];
            for (int k = 0; k < starts; k++)
            {
                double sum = 0;
                for (int i = 0; i < _cands.Count; i++)
                {
                    weights[i] = _cands[i].ParcelId < 0 ? 0
                        : Math.Exp((_cands[i].ReturnRate - p.HurdleRate) / p.SoftmaxSpread);
                    sum += weights[i];
                }
                if (sum <= 0) break;
                double r = w.Rng.NextDouble() * sum, accum = 0;
                int chosen = -1;
                for (int i = 0; i < _cands.Count; i++)
                {
                    accum += weights[i];
                    if (r < accum && _cands[i].ParcelId >= 0) { chosen = i; break; }
                }
                if (chosen < 0) break;

                var cand = _cands[chosen];
                var pl = w.Parcels[cand.ParcelId];
                int units = LandAccounting.UnitsFor(pl.Zoned);
                pl.State = ParcelState.UnderConstruction;
                pl.Use = pl.Zoned;
                pl.Level = cand.Level;
                pl.Units = units;
                pl.BuildProgress = 0;
                pl.BuildTotal = p.ConstructionLag;
                pl.CommittedCost = p.RC(cand.Level, units);
                pl.PredictedRentAtDecision = cand.PredictedRent;
                pl.PredictedAbsorptionAtDecision = cand.PredictedAbsorption;
                // Claims ledger at COMMITMENT: later deciders see the pipeline,
                // not the mirage (§4.6 cobweb self-metering).
                w.Claims.Add(pl.Cluster, pl.Use, units);
                StartedTotal++;
                _cands[chosen] = new Candidate { ParcelId = -1 };
            }
        }

        /// <summary>Developer flow profit for building (parcel, level): predicted
        /// rent × predicted absorption − structure charge − land charge, all per
        /// tick, calibration-corrected. The land-charge deduction φ·LR is what
        /// makes the phantom developer expected-zero at full capture.</summary>
        public double ExpectedFlow(WorldState w, AccessState acc, TradeSystem trade,
                                   ResidualDemand residuals, double[] segmentPresence,
                                   Parcel pl, int level, EconParams p,
                                   out double predictedRent, out double predictedAbsorption)
        {
            ZoneKind use = pl.State == ParcelState.UnderConstruction ? pl.Use : pl.Zoned;
            int units = LandAccounting.UnitsFor(use);
            double bid = LandAccounting.BidPerUnit(acc, trade, pl.Cluster, use, level, segmentPresence, p)
                         * w.Calibration.Factor(use);
            double resid = residuals.Get(pl.Cluster, use, w.Claims);
            // A project under construction already sits in the claims ledger;
            // its own claim must not count against its own re-evaluation.
            if (pl.State == ParcelState.UnderConstruction) resid += pl.Units;
            // Absorption comes from residual demand and local vacancy (§4.6): a
            // cluster with zero residual predicts an empty building — no floor
            // that would let pure bid strength override the demand signal.
            double absorption = MathUtil.Clamp(resid / Math.Max(1, units), 0.02, 1.0);
            predictedRent = bid;
            predictedAbsorption = absorption;
            double s = LandAccounting.SPerUnit(level, 1.0, p);
            double landCharge = p.CaptureFraction * pl.AssessedLR;
            return (bid * absorption - s) * units - landCharge;
        }

        /// <summary>Calibration loop (§4.6): observe realized occupancy × rent of
        /// recent builds against the prediction at their decision time; the shrunk
        /// ratio folds back into predicted returns.</summary>
        public void ObserveCompletions(WorldState w, EconParams p)
        {
            foreach (var pl in w.Parcels)
            {
                if (pl.CompletedTick < 0 || w.Tick != pl.CompletedTick + 60) continue;
                if (pl.State != ParcelState.Built || pl.PredictedRentAtDecision <= 0) continue;
                double predicted = pl.PredictedRentAtDecision * Math.Max(0.1, pl.PredictedAbsorptionAtDecision);
                double occ;
                if (pl.IsResidential) occ = pl.Units > 0 ? (double)pl.OccupantHouseholds.Count / pl.Units : 0;
                else occ = pl.OccupantFirm >= 0 ? 1 : 0;
                double realizedRent = LandAccounting.UnitAssessment(pl, p);
                w.Calibration.Observe(pl.Use, realizedRent * occ / Math.Max(1e-6, predicted), p.CalibShrinkN0);
                pl.PredictedRentAtDecision = 0;   // observe once
            }
        }
    }
}
