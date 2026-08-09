using System;
using System.Collections.Generic;
using System.Linq;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>`harness hhcurve` — AUDIT PROBE for the individual-agency
    /// principle. Builds the residential demand curve two ways at the same
    /// (cluster, kind, level) and clears both against the same stock:
    ///
    ///   PARAMETRIC — exactly what LandAccounting.ResidentialBidPerUnit does:
    ///     Income.Build's synthetic (earners × job-level × employment)
    ///     convolution, quantile-binned into Income.Bins tranches.
    ///   EMPIRICAL  — the same segment mass (presence × SegmentKindShare, so
    ///     the share channel is held FIXED and only the income distribution
    ///     differs) spread over the ACTUAL living households of that segment,
    ///     each carrying its own Earners / JobLevel / UnemployedTicks via
    ///     AccessState.HouseholdIncomeEstimate.
    ///   EMPIRICAL+W — same, but willingness to pay uses EffectiveIncome
    ///     (income + Money/WealthDrawdownTicks), which is the quantity the
    ///     ALLOCATOR already tests affordability against.
    ///
    /// Nothing here is used by the model; it only measures the divergence.</summary>
    public static class HouseholdCurveProbe
    {
        private struct Tranche { public double Wtp, Mass; }

        /// <summary>Verbatim re-implementation of the clearing walk in
        /// LandAccounting.ResidentialBidPerUnit (band read + flat tail), so the
        /// two curves are cleared by identical rules.</summary>
        private static double Clear(List<Tranche> t, double supply, EconParams p, out double fill)
        {
            fill = 1.0;
            var a = t.Where(x => x.Wtp > 1e-12 && x.Mass > 1e-12).OrderByDescending(x => x.Wtp).ToList();
            if (a.Count == 0) return 0;
            if (supply <= 1e-9) return a[0].Wtp;
            double band = 1.0 + Math.Max(0, p.ClearingBand);
            double readAt = supply * band;
            double cum = 0;
            for (int i = 0; i < a.Count; i++)
            {
                double prev = cum;
                cum += a[i].Mass;
                if (cum >= readAt)
                {
                    double frac = a[i].Mass > 1e-12 ? (readAt - prev) / a[i].Mass : 1.0;
                    double above = i > 0 ? a[i - 1].Wtp : a[0].Wtp;
                    return above + (a[i].Wtp - above) * MathUtil.Clamp(frac, 0, 1);
                }
            }
            const double MinTailMass = 0.05;
            double acc = 0;
            for (int i = a.Count - 1; i >= 0; i--)
            {
                acc += a[i].Mass;
                if (acc >= MinTailMass)
                {
                    fill = MathUtil.Clamp(cum / band / supply, 0, 1);
                    return a[i].Wtp;
                }
            }
            fill = 0;
            return 0;
        }

        public static int Run(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(ticks);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;
            int K = Income.Bins;

            // Living households by segment (the real population).
            var bySeg = new List<Household>[Segment.Count];
            for (int s = 0; s < Segment.Count; s++) bySeg[s] = new List<Household>();
            foreach (var h in w.Households) if (h.ExitedTick < 0) bySeg[h.Segment].Add(h);

            Console.WriteLine($"=== hhcurve @ tick {w.Tick}, pop {bySeg.Sum(l => l.Count)} ===");

            // ---- 1. income distribution: parametric vs realized -------------
            int c0 = 0;
            for (int c = 1; c < acc.C; c++) if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;
            Console.WriteLine($"\n-- income distribution, parametric at cluster {c0} vs realized households --");
            Console.WriteLine($"{"segment",-12} {"paramMean",9} {"realMean",9} {"ratio",6} | "
                + $"{"pTop",7} {"rTop",7} | {"pBot",7} {"rBot",7} | {"pSpread",8} {"rSpread",8} | {"cv_p",5} {"cv_r",5}");
            for (int s = 0; s < Segment.Count; s++)
            {
                var seg = Segment.All[s];
                int b0 = (s * acc.C + c0) * K;
                double pMean = acc.ExpectedIncome(s, c0, p);
                var real = bySeg[s].Select(h => AccessState.HouseholdIncomeEstimate(h, seg, p)).OrderByDescending(x => x).ToList();
                if (real.Count == 0) continue;
                double rMean = real.Average();
                // realized quantile bins, same equal-probability construction
                var rBins = new double[K];
                for (int k = 0; k < K; k++)
                {
                    int lo = (int)Math.Floor(k * real.Count / (double)K);
                    int hi = (int)Math.Floor((k + 1) * real.Count / (double)K);
                    hi = Math.Max(hi, lo + 1);
                    hi = Math.Min(hi, real.Count);
                    double sum = 0; for (int i = lo; i < hi; i++) sum += real[i];
                    rBins[k] = sum / (hi - lo);
                }
                double pTop = acc.IncomeBinInc[b0], pBot = acc.IncomeBinInc[b0 + K - 1];
                double pVar = 0; for (int k = 0; k < K; k++) pVar += acc.IncomeBinWt[b0 + k] * Math.Pow(acc.IncomeBinInc[b0 + k] - pMean, 2);
                double rVar = real.Sum(x => Math.Pow(x - rMean, 2)) / real.Count;
                Console.WriteLine($"{seg.Name,-12} {pMean,9:F2} {rMean,9:F2} {rMean / Math.Max(1e-9, pMean),6:F2} | "
                    + $"{pTop,7:F2} {rBins[0],7:F2} | {pBot,7:F2} {rBins[K - 1],7:F2} | "
                    + $"{pTop / Math.Max(1e-9, pBot),8:F2} {rBins[0] / Math.Max(1e-9, rBins[K - 1]),8:F2} | "
                    + $"{Math.Sqrt(pVar) / Math.Max(1e-9, pMean),5:F2} {Math.Sqrt(rVar) / Math.Max(1e-9, rMean),5:F2}");
            }

            // ---- 2. clearing price, both curves, top submarkets --------------
            Console.WriteLine($"\n-- clearing price: parametric bins vs real households (same mass, same clearing rule) --");
            Console.WriteLine($"{"kind",-4} {"c",3} {"lvl",3} {"stock",7} {"P_param",8} {"P_real",8} {"d%",8} "
                + $"{"P_real+w",8} {"d%",8} {"fillP",6} {"fillR",6} {"regime",8}");
            double sumAbsDev = 0, sumAbsDevW = 0; int nCells = 0;
            foreach (var (kind, ki) in new[] { (ZoneKind.ResidentialLow, 0), (ZoneKind.ResidentialHigh, 1) })
            {
                var order = Enumerable.Range(0, acc.C).OrderByDescending(c => acc.HousingStock[ki][c]).Take(6);
                foreach (int c in order)
                {
                    double stock = acc.HousingStock[ki][c];
                    if (stock <= 0) continue;
                    foreach (int lvl in new[] { 1, 2, 3 })
                    {
                        double quality = p.Quality(lvl) / p.Quality(1);
                        var para = new List<Tranche>();
                        var emp = new List<Tranche>();
                        var empW = new List<Tranche>();
                        for (int s = 0; s < Segment.Count; s++)
                        {
                            if (pres[s] < 1) continue;
                            var seg = Segment.All[s];
                            double rel = acc.AccessValue[s][c] / acc.MeanAccess;
                            double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                            double share = acc.SegmentKindShare[s][ki][c];
                            if (share <= 0) continue;
                            double common = seg.MaxRentShare * prem * p.BidAccessScale * quality * seg.DensityAppeal(kind);
                            int b0 = (s * acc.C + c) * K;
                            for (int k = 0; k < K; k++)
                                para.Add(new Tranche { Wtp = common * acc.IncomeBinInc[b0 + k], Mass = pres[s] * share * acc.IncomeBinWt[b0 + k] });
                            var hs = bySeg[s];
                            if (hs.Count == 0) continue;
                            double per = pres[s] * share / hs.Count;
                            foreach (var h in hs)
                            {
                                double inc = AccessState.HouseholdIncomeEstimate(h, seg, p);
                                emp.Add(new Tranche { Wtp = common * inc, Mass = per });
                                empW.Add(new Tranche { Wtp = common * AccessState.EffectiveIncome(h, seg, p), Mass = per });
                            }
                        }
                        double pp = Clear(para, stock, p, out double fp);
                        double pr = Clear(emp, stock, p, out double fr);
                        double pw = Clear(empW, stock, p, out _);
                        double totMass = para.Sum(t => t.Mass);
                        string regime = totMass >= stock * (1 + p.ClearingBand) ? "cleared" : "excess";
                        double d = pp > 1e-9 ? (pr - pp) / pp : 0;
                        double dw = pp > 1e-9 ? (pw - pp) / pp : 0;
                        sumAbsDev += Math.Abs(d); sumAbsDevW += Math.Abs(dw); nCells++;
                        Console.WriteLine($"{(ki == 0 ? "Low" : "High"),-4} {c,3} {lvl,3} {stock,7:F0} {pp,8:F3} {pr,8:F3} {d,8:P1} "
                            + $"{pw,8:F3} {dw,8:P1} {fp,6:P0} {fr,6:P0} {regime,8}");
                    }
                }
            }
            Console.WriteLine($"\nmean |Δ| over {nCells} (cluster,kind,level) cells: "
                + $"income-only {sumAbsDev / Math.Max(1, nCells):P1}, with-wealth {sumAbsDevW / Math.Max(1, nCells):P1}");

            // ---- 3. what the assessment does with it -------------------------
            Console.WriteLine("\n-- land rent consequence at the largest Low submarket --");
            {
                int c = c0; int ki = 0;
                double stock = acc.HousingStock[ki][c];
                int lvl = 2;
                double quality = p.Quality(lvl) / p.Quality(1);
                var para = new List<Tranche>(); var emp = new List<Tranche>();
                for (int s = 0; s < Segment.Count; s++)
                {
                    if (pres[s] < 1) continue;
                    var seg = Segment.All[s];
                    double rel = acc.AccessValue[s][c] / acc.MeanAccess;
                    double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                    double share = acc.SegmentKindShare[s][ki][c];
                    if (share <= 0) continue;
                    double common = seg.MaxRentShare * prem * p.BidAccessScale * quality * seg.DensityAppeal(ZoneKind.ResidentialLow);
                    int b0 = (s * acc.C + c) * K;
                    for (int k = 0; k < K; k++)
                        para.Add(new Tranche { Wtp = common * acc.IncomeBinInc[b0 + k], Mass = pres[s] * share * acc.IncomeBinWt[b0 + k] });
                    var hs = bySeg[s];
                    double per = hs.Count > 0 ? pres[s] * share / hs.Count : 0;
                    foreach (var h in hs)
                        emp.Add(new Tranche { Wtp = common * AccessState.HouseholdIncomeEstimate(h, seg, p), Mass = per });
                }
                double pp = Clear(para, stock, p, out _), pr = Clear(emp, stock, p, out _);
                double S = LandAccounting.SPerUnit(lvl, 1.0, p);
                Console.WriteLine($"  S(lvl2)={S:F3}  P_param={pp:F3} → LR/unit={pp - S:F3}   "
                    + $"P_real={pr:F3} → LR/unit={pr - S:F3}   LR ratio {(pp - S) / Math.Max(1e-9, pr - S):F2}x");
            }

            // ---- 3b. LR overstatement across CLEARED cells -------------------
            Console.WriteLine("\n-- land-rent overstatement, cleared submarkets, level 2 --");
            Console.WriteLine($"{"kind",-4} {"c",3} {"S",7} {"P_param",8} {"P_real",8} {"LR_param",9} {"LR_real",9} {"LRover",8}");
            foreach (var (kind2, ki2) in new[] { (ZoneKind.ResidentialLow, 0), (ZoneKind.ResidentialHigh, 1) })
            {
                foreach (int c in Enumerable.Range(0, acc.C).OrderByDescending(x => acc.HousingStock[ki2][x]).Take(8))
                {
                    double stock = acc.HousingStock[ki2][c];
                    if (stock <= 0) continue;
                    int lvl = 2; double quality = p.Quality(lvl) / p.Quality(1);
                    var para = new List<Tranche>(); var emp = new List<Tranche>();
                    double totm = 0;
                    for (int s = 0; s < Segment.Count; s++)
                    {
                        if (pres[s] < 1) continue;
                        var seg = Segment.All[s];
                        double rel = acc.AccessValue[s][c] / acc.MeanAccess;
                        double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                        double share = acc.SegmentKindShare[s][ki2][c];
                        if (share <= 0) continue;
                        double common = seg.MaxRentShare * prem * p.BidAccessScale * quality * seg.DensityAppeal(kind2);
                        int b0 = (s * acc.C + c) * K;
                        for (int k = 0; k < K; k++)
                        { para.Add(new Tranche { Wtp = common * acc.IncomeBinInc[b0 + k], Mass = pres[s] * share * acc.IncomeBinWt[b0 + k] }); totm += pres[s] * share * acc.IncomeBinWt[b0 + k]; }
                        var hs = bySeg[s]; if (hs.Count == 0) continue;
                        double per = pres[s] * share / hs.Count;
                        foreach (var h in hs) emp.Add(new Tranche { Wtp = common * AccessState.HouseholdIncomeEstimate(h, seg, p), Mass = per });
                    }
                    if (totm < stock * (1 + p.ClearingBand)) continue;
                    double pp = Clear(para, stock, p, out _), pr = Clear(emp, stock, p, out _);
                    double S = LandAccounting.SPerUnit(lvl, 1.0, p);
                    Console.WriteLine($"{(ki2 == 0 ? "Low" : "High"),-4} {c,3} {S,7:F3} {pp,8:F3} {pr,8:F3} "
                        + $"{pp - S,9:F3} {pr - S,9:F3} {(pp - S) / Math.Max(1e-9, pr - S),8:F2}x");
                }
            }

            // ---- 3c. counterfactual: real households WITHOUT benefit expiry --
            Console.WriteLine("\n-- isolating the benefit-expiry channel (excess-regime anchors) --");
            foreach (int cc in Enumerable.Range(0, acc.C).OrderByDescending(x => acc.HousingStock[0][x]).Take(5))
            {
                int ki = 0; int lvl = 2;
                double stock = acc.HousingStock[ki][cc];
                if (stock <= 0) continue;
                double quality = p.Quality(lvl) / p.Quality(1);
                var emp = new List<Tranche>(); var empNoExp = new List<Tranche>();
                Span<double> lw2 = stackalloc double[5]; Span<double> lwage2 = stackalloc double[5];
                for (int s = 0; s < Segment.Count; s++)
                {
                    if (pres[s] < 1) continue;
                    var seg = Segment.All[s];
                    double rel = acc.AccessValue[s][cc] / acc.MeanAccess;
                    double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                    double share = acc.SegmentKindShare[s][ki][cc]; if (share <= 0) continue;
                    double common = seg.MaxRentShare * prem * p.BidAccessScale * quality * seg.DensityAppeal(ZoneKind.ResidentialLow);
                    var hs = bySeg[s]; if (hs.Count == 0) continue;
                    double per = pres[s] * share / hs.Count;
                    int levels2 = Income.JobLevels(seg, p, lw2, lwage2);
                    foreach (var h in hs)
                    {
                        emp.Add(new Tranche { Wtp = common * AccessState.HouseholdIncomeEstimate(h, seg, p), Mass = per });
                        int earners = Math.Min(h.Earners, Math.Max(0, seg.Adults));
                        double wageNE = earners * lwage2[Math.Min(h.JobLevel, levels2 - 1)] * (1 - p.IncomeTax(seg.Labor));
                        double benNE = Math.Max(0, seg.Adults - earners) * p.UnemploymentBenefit * MathUtil.Clamp(seg.Participation, 0, 1);
                        double incNE = Math.Max(p.ResidentialMinimumEarnings, wageNE + benNE + seg.Transfer);
                        empNoExp.Add(new Tranche { Wtp = common * incNE, Mass = per });
                    }
                }
                double pr = Clear(emp, stock, p, out _), pn = Clear(empNoExp, stock, p, out _);
                Console.WriteLine($"  cluster {cc,3} stock {stock,5:F0}: P_real(expiry live)={pr:F3}  P_real(expiry suppressed)={pn:F3}  "
                    + $"expiry moves price {(pn > 1e-9 ? (pr - pn) / pn : 0):P1}");
            }

            // ---- 3d. share channel: parametric logit vs realized locations ---
            Console.WriteLine("\n-- SegmentKindShare (parametric logit) vs where households ACTUALLY live --");
            Console.WriteLine($"{"segment",-12} {"TVdist",8} {"corr",7} {"topShare",16} {"topReal",16}");
            for (int s = 0; s < Segment.Count; s++)
            {
                if (pres[s] < 1) continue;
                var real = new double[2][];
                real[0] = new double[acc.C]; real[1] = new double[acc.C];
                double rt = 0;
                foreach (var h in bySeg[s])
                {
                    if (h.HomeParcel < 0) continue;
                    var pl = w.Parcels[h.HomeParcel];
                    real[pl.Use == ZoneKind.ResidentialHigh ? 1 : 0][pl.Cluster] += 1; rt += 1;
                }
                if (rt <= 0) continue;
                double tv = 0, sxy = 0, sx = 0, sy = 0, sxx = 0, syy = 0; int n2 = 0;
                for (int k = 0; k < 2; k++)
                    for (int c = 0; c < acc.C; c++)
                    {
                        double a = acc.SegmentKindShare[s][k][c], b = real[k][c] / rt;
                        tv += Math.Abs(a - b);
                        sxy += a * b; sx += a; sy += b; sxx += a * a; syy += b * b; n2++;
                    }
                double corr = (n2 * sxy - sx * sy) / Math.Max(1e-12, Math.Sqrt((n2 * sxx - sx * sx) * (n2 * syy - sy * sy)));
                int bs = 0, bk = 0, rs = 0, rk = 0; double bv = -1, rv = -1;
                for (int k = 0; k < 2; k++) for (int c = 0; c < acc.C; c++)
                { if (acc.SegmentKindShare[s][k][c] > bv) { bv = acc.SegmentKindShare[s][k][c]; bs = c; bk = k; }
                  if (real[k][c] / rt > rv) { rv = real[k][c] / rt; rs = c; rk = k; } }
                Console.WriteLine($"{Segment.All[s].Name,-12} {tv / 2,8:P1} {corr,7:F2} "
                    + $"{(bk == 0 ? "L" : "H") + bs + "@" + bv.ToString("P1"),16} {(rk == 0 ? "L" : "H") + rs + "@" + rv.ToString("P1"),16}");
            }

            // ---- 4. where does the divergence come from? ---------------------
            Console.WriteLine("\n-- decomposition of the income gap (citywide, per segment) --");
            Console.WriteLine($"{"segment",-12} {"n",6} {"realMean",9} {"paramMean*",10} {"benefitExpired",14} {"assortGap",10}");
            for (int s = 0; s < Segment.Count; s++)
            {
                var seg = Segment.All[s]; var hs = bySeg[s];
                if (hs.Count == 0) continue;
                // population-weighted parametric mean over the clusters households actually live in
                double pm = 0, wsum = 0;
                foreach (var h in hs)
                {
                    int c = h.HomeParcel >= 0 ? w.Parcels[h.HomeParcel].Cluster : 0;
                    pm += acc.ExpectedIncome(s, c, p); wsum++;
                }
                pm /= Math.Max(1, wsum);
                double rm = hs.Average(h => AccessState.HouseholdIncomeEstimate(h, seg, p));
                int expired = hs.Count(h => h.Earners == 0 && seg.Adults > 0 && h.UnemployedTicks > p.UnemploymentAllowanceTicks);
                // assortativity: realized 2-earner households share one JobLevel; the
                // parametric convolution draws the two levels independently.
                double assort = 0;
                if (seg.Adults == 2)
                {
                    Span<double> lw = stackalloc double[5]; Span<double> lwage = stackalloc double[5];
                    int levels = Income.JobLevels(seg, p, lw, lwage);
                    double indepVar = 0, mean = 0;
                    for (int a = 0; a < levels; a++) for (int b = 0; b < levels; b++) mean += lw[a] * lw[b] * (lwage[a] + lwage[b]);
                    for (int a = 0; a < levels; a++) for (int b = 0; b < levels; b++) indepVar += lw[a] * lw[b] * Math.Pow(lwage[a] + lwage[b] - mean, 2);
                    double sameVar = 0;
                    for (int a = 0; a < levels; a++) sameVar += lw[a] * Math.Pow(2 * lwage[a] - mean, 2);
                    assort = Math.Sqrt(sameVar) / Math.Max(1e-9, Math.Sqrt(indepVar));
                }
                Console.WriteLine($"{seg.Name,-12} {hs.Count,6} {rm,9:F2} {pm,10:F2} "
                    + $"{expired + " (" + (100.0 * expired / hs.Count).ToString("F1") + "%)",14} {assort,10:F2}");
            }

            // ---- 5. the OTHER parametric legs of the bid ---------------------
            Console.WriteLine("\n-- premium denominator: MeanAccess is an UNWEIGHTED mean over (segment x cluster) --");
            {
                double unw = 0; int cnt = 0;
                double popw = 0, popwsum = 0;
                int occupiedClusters = 0;
                for (int c = 0; c < acc.C; c++) if (w.HouseholdCountByCluster[c] > 0) occupiedClusters++;
                for (int s = 0; s < Segment.Count; s++)
                    for (int c = 0; c < acc.C; c++)
                    {
                        unw += acc.AccessValue[s][c]; cnt++;
                        double wg = w.HouseholdCountByCluster[c];
                        popw += acc.AccessValue[s][c] * wg; popwsum += wg;
                    }
                double mUnw = Math.Max(0.5, unw / cnt), mPop = Math.Max(0.5, popw / Math.Max(1e-9, popwsum));
                Console.WriteLine($"  clusters {acc.C}, of which occupied {occupiedClusters}");
                Console.WriteLine($"  MeanAccess (as used) = {mUnw:F3}; population-weighted = {mPop:F3}; ratio {mPop / mUnw:F3}");
                Console.WriteLine($"  every premium scales by (ratio)^PremiumExponent = {Math.Pow(mPop / mUnw, p.PremiumExponent):F3}"
                    + $"  → citywide bid level would move {Math.Pow(mUnw / mPop, p.PremiumExponent) - 1:P1} if the denominator were population-weighted");
            }
            Console.WriteLine("\n-- employment rate used by Income.Build is the PRICED cluster's, applied to citywide bidders --");
            for (int cl = 0; cl < 3; cl++)
            {
                double lo = double.MaxValue, hi = 0, sum = 0; int n3 = 0;
                for (int c = 0; c < acc.C; c++)
                {
                    if (acc.WorkersByClass[cl][c] < 1) continue;
                    double e = acc.EmploymentRate[cl][c];
                    lo = Math.Min(lo, e); hi = Math.Max(hi, e); sum += e; n3++;
                }
                if (n3 == 0) continue;
                Console.WriteLine($"  class {cl}: emp rate across {n3} inhabited clusters min {lo:P0} mean {sum / n3:P0} max {hi:P0}");
            }
            Console.WriteLine("\n-- per-household attributes drawn at birth that the BID uses: --");
            Console.WriteLine("  MaxRentShare: segment constant (Segment.MaxRentShare) — no per-household draw");
            Console.WriteLine("  DensityTolerance: segment constant — no per-household draw");
            Console.WriteLine("  access weights (JobAccessW/AmenityW/...): segment constants — no per-household draw");
            Console.WriteLine("  MovingCostDraw: the ONLY per-household birth draw, and it is NOT in the bid");
            return 0;
        }
    }
}