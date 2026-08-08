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

        // Vacancy kernel e^(−d/λ) over straight-line meters between cluster
        // centroids (walking-distance proxy — deliberately NOT generalized
        // travel cost). Geometry is fixed per run; rebuilt only if the cluster
        // count or λ changes.
        private double[] _kern = Array.Empty<double>();      // C×C flattened
        private int _kernC = -1;
        private double _kernLambda, _kernScale;

        /// <summary>Residential cross-substitution shares — the SAME splits the
        /// seeker allocation uses (0.85/0.15 low-preferring, 0.25/0.75
        /// high-tolerant). A vacant tower unit competes for the seekers a
        /// would-be duplex courts, so suppression must mirror the demand
        /// structure or tower vacancies never touch low-density residual.</summary>
        private static readonly double[][] ResCross = { new[] { 0.85, 0.15 }, new[] { 0.25, 0.75 } };
        private double[][] _stockByUse = Array.Empty<double[]>();

        /// <summary>Deposit `amount` units of competing supply emitted at
        /// cluster i (use u) onto `target`, weighted by e^(−d/λ)·exposure and
        /// normalized so the deposited total is EXACTLY `amount` (V = 1: one
        /// unit of supply cancels one unit of demand citywide, and λ only
        /// decides where). The single definition of the footprint — vacancies
        /// and pipeline claims both route through here.</summary>
        private void Spread(int u, int i, double amount, double[][] target)
        {
            int C = _kernC;
            if (u < 2)
            {
                var x = ResCross[u];
                double denom = 0;
                for (int k = 0; k < C; k++)
                    denom += _kern[i * C + k] * (x[0] * _stockByUse[0][k] + x[1] * _stockByUse[1][k]);
                if (denom <= 1e-9) { target[u][i] += amount; return; }   // no stock anywhere: land at home
                for (int j = 0; j < C; j++)
                {
                    double kij = _kern[i * C + j];
                    target[0][j] += amount * kij * x[0] * _stockByUse[0][j] / denom;
                    target[1][j] += amount * kij * x[1] * _stockByUse[1][j] / denom;
                }
            }
            else
            {
                var stock = _stockByUse[u];
                double denom = 0;
                for (int k = 0; k < C; k++) denom += _kern[i * C + k] * stock[k];
                if (denom <= 1e-9) { target[u][i] += amount; return; }
                for (int j = 0; j < C; j++)
                    target[u][j] += amount * _kern[i * C + j] * stock[j] / denom;
            }
        }

        private void EnsureKernel(WorldState w, int C, EconParams p)
        {
            if (_kernC == C && _kernLambda == p.VacancyKernelLambdaM && _kernScale == w.MetersPerUnit)
                return;
            _kernC = C; _kernLambda = p.VacancyKernelLambdaM; _kernScale = w.MetersPerUnit;
            _kern = new double[C * C];
            double lam = Math.Max(1.0, p.VacancyKernelLambdaM);
            for (int i = 0; i < C; i++)
            {
                var a = w.Clusters[i];
                for (int j = i; j < C; j++)
                {
                    var b = w.Clusters[j];
                    double dx = (a.X - b.X) * w.MetersPerUnit;
                    double dy = (a.Y - b.Y) * w.MetersPerUnit;
                    double k = Math.Exp(-Math.Sqrt(dx * dx + dy * dy) / lam);
                    _kern[i * C + j] = k;
                    _kern[j * C + i] = k;
                }
            }
        }

        public static int UseIndex(ZoneKind k) => k switch
        {
            ZoneKind.ResidentialLow => 0, ZoneKind.ResidentialHigh => 1, ZoneKind.Commercial => 2,
            ZoneKind.Industrial => 3, ZoneKind.Office => 4, ZoneKind.Extractor => 5, _ => -1,
        };

        public double Get(int cluster, ZoneKind use, ClaimsLedger claims)
        {
            int u = UseIndex(use);
            if (u < 0) return 0;
            return ByUse[u][cluster] - ClaimExposure(u, cluster, claims);
        }

        /// <summary>Pipeline claims seen at a cluster, smeared through the SAME
        /// kernel as vacancies. This symmetry is load-bearing, not tidiness: a
        /// unit under construction and a finished vacant unit are the same
        /// competitive object, so they must have the same spatial footprint.
        /// Subtracting claims 100% locally while smearing vacancy ~15% locally
        /// made COMPLETING an empty building raise its own cluster's residual
        /// demand by ~0.85×units — a ratchet that kept starting towers beside
        /// standing empties on a ConstructionLag cycle (adversarial review,
        /// confirmed HIGH). Recomputed lazily and invalidated by the ledger's
        /// Version so commits inside a refresh window still see each other
        /// immediately (§4.6 pipeline-not-mirage).</summary>
        private double ClaimExposure(int u, int cluster, ClaimsLedger claims)
        {
            if (_kernC <= 0) return claims.Get(cluster, UseAt(u));
            if (_claimVersion != claims.Version || _claimField.Length != 6) RebuildClaimField(claims);
            return _claimField[u][cluster];
        }

        private static ZoneKind UseAt(int u) => Uses[u];

        private double[][] _claimField = Array.Empty<double[]>();
        private int _claimVersion = -1;

        private void RebuildClaimField(ClaimsLedger claims)
        {
            int C = _kernC;
            if (_claimField.Length != 6)
            {
                _claimField = new double[6][];
                for (int u = 0; u < 6; u++) _claimField[u] = new double[C];
            }
            for (int u = 0; u < 6; u++)
            {
                if (_claimField[u].Length != C) _claimField[u] = new double[C];
                else Array.Clear(_claimField[u], 0, C);
            }
            foreach (var kv in claims.Entries)
            {
                int u = UseIndex(kv.Key.use);
                if (u < 0 || kv.Value <= 0) continue;
                int i = kv.Key.cluster;
                if ((uint)i >= (uint)C) continue;
                Spread(u, i, kv.Value, _claimField);
            }
            _claimVersion = claims.Version;
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
                // Which density this segment leans toward — a lean, not a
                // permission. Gate on the RAW tolerance: DensityAppeal is
                // floored at DensityFloor=0.6, so `appeal >= 0.5` is true for
                // every segment — the old form routed ALL seeker mass through
                // the high-density branch (75% of citywide construction
                // demand to towers; adversarial review, measured A/B).
                ZoneKind kind = seg.DensityTolerance >= 0.5
                    ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
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
                    // Same raw-tolerance gate as the lean above (appeal's
                    // floor makes it vacuously high for all segments).
                    if (seg.DensityTolerance >= 0.5)
                    { ByUse[1][c] += mass * 0.75; ByUse[0][c] += mass * 0.25; }
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
                double extProfit = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Extractor, 2, p,
                                                                 out _, w.Clusters);
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
                    double cost = double.PositiveInfinity;
                    for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                        cost = Math.Min(cost, trade.DeliveredCost((Res)rr, c));
                    rawW[c] = Math.Exp(-1.2 * cost);
                    totalRawW += rawW[c];
                }
                if (totalRawW > 0)
                    for (int c = 0; c < C; c++)
                        ByUse[3][c] += trade.InducedProcessingSlots * rawW[c] / totalRawW;
            }

            // ---- net out incumbents' vacancies through the spatial kernel ----
            // Each vacant unit's competitive weight is spread over nearby
            // substitutable SUPPLY: K_ij ∝ e^(−d_ij/λ)·U_j, normalized per
            // emitter so Σ_j K_ij = 1 — one vacancy cancels EXACTLY one unit
            // of demand citywide (V = 1; a free amplitude would mint phantom
            // over/under-supply), and λ only decides how it is smeared.
            // Exposure U_j = built units PLUS the buildable margin (zoned-empty
            // capacity and pipeline units) of the same use at j: a vacancy
            // competes wherever substitutable supply exists OR COULD APPEAR —
            // weighting by standing stock alone leaves a hole where a
            // greenfield parcel 300 m from a sea of vacancies feels nothing
            // (its cluster holds no stock) and happily starts construction.
            EnsureKernel(w, C, p);
            var vacantByUse = new double[6][];
            var stockByUse = new double[6][];
            for (int u = 0; u < 6; u++) { vacantByUse[u] = new double[C]; stockByUse[u] = new double[C]; }
            foreach (var pl in w.Parcels)
            {
                if (pl.State == ParcelState.Built)
                {
                    int u = UseIndex(pl.Use);
                    if (u < 0) continue;
                    stockByUse[u][pl.Cluster] += pl.Units;
                    if (pl.IsResidential && !pl.Warehousing) vacantByUse[u][pl.Cluster] += pl.Vacant;
                    else if (!pl.IsResidential && pl.OccupantFirm < 0) vacantByUse[u][pl.Cluster] += pl.Units;
                }
                else
                {
                    // Buildable margin: zoned-empty capacity at its zone's unit
                    // count; pipeline projects at their committed unit count.
                    int u = UseIndex(pl.State == ParcelState.UnderConstruction ? pl.Use : pl.Zoned);
                    if (u < 0) continue;
                    stockByUse[u][pl.Cluster] += pl.State == ParcelState.UnderConstruction
                        ? pl.Units : LandAccounting.UnitsFor(pl.Zoned);
                }
            }
            // Suppression is deposited by the SHARED Spread() used for claims,
            // so a pipeline unit and a finished vacant unit have identical
            // footprints (see ClaimExposure).
            _stockByUse = stockByUse;
            var supp = new double[6][];
            for (int u = 0; u < 6; u++) supp[u] = new double[C];
            for (int u = 0; u < 6; u++)
                for (int i = 0; i < C; i++)
                    if (vacantByUse[u][i] > 0) Spread(u, i, vacantByUse[u][i], supp);
            for (int u = 0; u < 6; u++)
                for (int c = 0; c < C; c++)
                    ByUse[u][c] -= supp[u][c];

            // Stock moved, so the smeared-claim field's weights are stale.
            // Claims themselves are still subtracted LIVE in Get() — commits
            // inside a refresh window must see each other immediately (the
            // §4.6 pipeline-not-mirage discipline; scrutiny finding #19).
            _claimVersion = -1;
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

        /// <summary>CS2ECON_MILESTONE_DEBUG=1 traces every milestone
        /// re-evaluation (flow vs abandon threshold). Read once — milestones
        /// are rare, but the hot path should not touch the environment.</summary>
        private static readonly bool MilestoneDebug =
            Environment.GetEnvironmentVariable("CS2ECON_MILESTONE_DEBUG") == "1";

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
                    if (MilestoneDebug)
                        Console.WriteLine($"[MILESTONE] t={w.Tick} parcel={pl.Id} prog={pl.BuildProgress}/{pl.BuildTotal} "
                            + $"flow={flow:F3} thresh={p.AbandonMarginFactor * p.HurdleRate * Math.Max(1, remaining):F3} "
                            + $"committed={pl.CommittedCost:F0}");
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
            // Developer prices the project at the clearing price with its OWN
            // units ADDED to the stock (they are not yet in it — stock counts
            // Built only), the same discipline Assess uses for candidates.
            double bid = LandAccounting.BidPerUnit(acc, trade, pl.Cluster, use, level, segmentPresence, p,
                                                   addUnits: units)
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
