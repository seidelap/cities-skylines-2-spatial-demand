using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>`harness debug` — one instrumented run with periodic prints of
    /// the quantities the acceptance tests depend on. Tuning aid, not a test.</summary>
    public static class Debugging
    {
        /// <summary>`harness churnprobe` — decompose DisplacementExits: reason
        /// (relocation vs insolvency emigration), distinct households, repeat
        /// offenders, segment mix, and employment/stage at exit. Answers "who
        /// is churning and why" instead of theorizing about it.</summary>
        public static int ChurnProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 9000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);
            long from = sim.W.Tick;
            sim.Run(ticks);
            var exits = sim.Engine.DisplacementExits.Where(e => e.tick > from).ToList();
            int reloc = exits.Count(e => e.reason == 0), emig = exits.Count(e => e.reason == 1);
            int failedArr = exits.Count(e => e.reason == 2);
            var byHh = exits.GroupBy(e => e.household).ToList();
            int repeat = byHh.Count(g => g.Count() > 1);
            var bySeg = exits.GroupBy(e => sim.W.Households[e.household].Segment)
                             .OrderByDescending(g => g.Count());
            int pop = sim.W.Households.Count(h => h.ExitedTick < 0);
            Console.WriteLine($"window {ticks} ticks, pop {pop}: {exits.Count} exits = "
                + $"{reloc} relocations + {emig} housed-insolvency emigrations + "
                + $"{failedArr} failed arrivals (never housed); {byHh.Count} distinct households, {repeat} repeat");
            double vacL = 0, vacH = 0, pipeL = 0, pipeH = 0;
            foreach (var pl in sim.W.Parcels)
            {
                if (pl.State == ParcelState.UnderConstruction && pl.IsResidential)
                { if (pl.Use == ZoneKind.ResidentialHigh) pipeH += pl.Units; else pipeL += pl.Units; }
                else if (pl.State == ParcelState.Built && pl.IsResidential && !pl.Warehousing)
                { if (pl.Use == ZoneKind.ResidentialHigh) vacH += pl.Vacant; else vacL += pl.Vacant; }
            }
            int unhoused = sim.W.Households.Count(h => h.ExitedTick < 0 && h.HomeParcel < 0);
            Console.WriteLine($"  at probe end: vacant L/H {vacL}/{vacH}, pipeline L/H {pipeL}/{pipeH}, "
                + $"unhoused {unhoused}, turnoverEma L/H {sim.Engine.TurnoverEma[0]:F2}/{sim.Engine.TurnoverEma[1]:F2}");
            foreach (var g in bySeg)
            {
                var seg = Segment.All[g.Key];
                int emp = g.Count(e => sim.W.Households[e.household].Employed);
                Console.WriteLine($"  {seg.Name,-12} {g.Count(),5} exits "
                    + $"({g.Count(e => e.reason == 1),4} housed-emig, {g.Count(e => e.reason == 2),4} failed-arrival) "
                    + $"presence {sim.Engine.SegmentPresence[g.Key],6:F0} employedAtProbe {emp}");
            }
            return 0;
        }

        /// <summary>`harness lambdasweep` — is the vacancy kernel still doing
        /// work now that prices clear? The kernel adds a SECOND spatial decay
        /// (λ, Euclidean metres) on top of the one the choice model already has
        /// (θ, in the access weights). If clearing prices carry the spatial
        /// signal, localization should survive λ growing to map scale, and the
        /// kernel is a redundant parameter that can be retired.
        ///
        /// Runs the disk-shock experiment at several λ and reports the
        /// suppression localization each time. λ → very large ≈ "no kernel"
        /// (suppression spread uniformly over the whole map).</summary>
        public static int LambdaSweep(ulong seed, int ticks)
        {
            double[] lambdas = { 200, 800, 3200, 12800, 1e9 };
            Console.WriteLine("λ (m) | interior suppression | beyond-spillover | localization | interior starts");
            foreach (double lam in lambdas)
            {
                var p = new EconParams { VacancyKernelLambdaM = lam };
                var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000, ParcelsPerCluster = 14 };

                (double[] resid, double starts) Arm(bool shock)
                {
                    var sim = Sim.Create(cfg, p, new FeatureFlags());
                    sim.Run(ticks);
                    for (int s = 0; s < Segment.Count; s++)
                        sim.W.Migration.ReservationThreshold[s] = Math.Max(0,
                            sim.W.Migration.SegmentAttractEma[s] - Migration.BaseOutsideUtility - 0.02);
                    double mx = 0, my = 0;
                    foreach (var ci in sim.W.Clusters) { mx += ci.X; my += ci.Y; }
                    mx /= sim.W.Clusters.Length; my /= sim.W.Clusters.Length;
                    double sxq = mx + 2800.0 / sim.W.MetersPerUnit;
                    bool InDisk(int c)
                    {
                        double dx = (sim.W.Clusters[c].X - sxq) * sim.W.MetersPerUnit;
                        double dy = (sim.W.Clusters[c].Y - my) * sim.W.MetersPerUnit;
                        return Math.Sqrt(dx * dx + dy * dy) <= 2500.0;
                    }
                    if (shock)
                        foreach (var h in sim.W.Households)
                        {
                            if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                            if (!InDisk(sim.W.Parcels[h.HomeParcel].Cluster)) continue;
                            if (SplitMix64.Hash01((ulong)h.Id * 31 + seed) < 0.95)
                            {
                                sim.Engine.Allocation.Vacate(sim.W, h);
                                sim.W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                                h.Money = 0; h.ExitedTick = sim.W.Tick;
                            }
                        }
                    long from = sim.W.Tick;
                    var acc = new double[sim.Engine.Access.C]; int n = 0;
                    sim.Run(70, s2 =>
                    {
                        if ((s2.W.Tick - from) % 10 != 0) return;
                        n++;
                        for (int c = 0; c < s2.Engine.Access.C; c++)
                            acc[c] += s2.Engine.Residuals.Get(c, ZoneKind.ResidentialLow, s2.W.Claims)
                                    + s2.Engine.Residuals.Get(c, ZoneKind.ResidentialHigh, s2.W.Claims);
                    });
                    for (int c = 0; c < acc.Length; c++) acc[c] /= Math.Max(1, n);
                    double st = 0;
                    foreach (var (tick, pid, cl) in sim.Starts)
                        if (tick > from && InDisk(cl)) st += Math.Max(1, sim.W.Parcels[pid].Units);
                    return (acc, st);
                }

                var shocked = Arm(true);
                var control = Arm(false);
                // Interior = deep inside the disk; reference = beyond 2λ reach
                // (capped at map scale so the huge-λ arm still has a reference).
                var simGeom = Sim.Create(cfg, p, new FeatureFlags());
                var g = simGeom.W.Clusters;
                double gx = 0, gy = 0;
                foreach (var ci in g) { gx += ci.X; gy += ci.Y; }
                gx /= g.Length; gy /= g.Length;
                double cx = gx + 2800.0 / simGeom.W.MetersPerUnit;
                double D(int c) => Math.Sqrt(Math.Pow((g[c].X - cx) * simGeom.W.MetersPerUnit, 2)
                                           + Math.Pow((g[c].Y - gy) * simGeom.W.MetersPerUnit, 2));
                double inSup = 0, farSup = 0; int nIn = 0, nFar = 0;
                for (int c = 0; c < g.Length; c++)
                {
                    double d = control.resid[c] - shocked.resid[c];
                    if (D(c) <= 1500) { inSup += d; nIn++; }
                    else if (D(c) >= 6000) { farSup += d; nFar++; }
                }
                inSup = nIn > 0 ? inSup / nIn : 0;
                farSup = nFar > 0 ? farSup / nFar : 0;
                Console.WriteLine($"{lam,9:G4} | {inSup,20:F2} | {farSup,16:F2} | "
                    + $"{inSup / Math.Max(0.05, Math.Abs(farSup)),12:F1}:1 | {shocked.starts,6:F0} vs {control.starts:F0}");
            }
            return 0;
        }

        /// <summary>`harness priceprobe` — for the densest residential clusters,
        /// dump the clearing queue: standing stock, summed demand mass, the
        /// demand/supply ratio, the WTP ladder, the resulting bid and the
        /// structure charge it must beat. Answers "why is LR zero here?".</summary>
        /// <summary>`harness indprobe` — why is (or isn't) industrial being
        /// built in the Weber check's plain fixture? Prints the industrial
        /// firm bid at the best clusters, the residual demand routed to
        /// industrial, and construction outcomes by use.</summary>
        public static int IndProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 6000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            for (int t = 0; t < ticks; t += 50)
            {
                sim.Run(Math.Min(50, ticks - t));
                int alive = 0, dead = 0; double money = 0, rev = 0;
                foreach (var f in sim.W.Firms)
                    if (f.Sector == ZoneKind.Industrial)
                    {
                        if (f.Dead) dead++;
                        else { alive++; money += f.Money; rev += f.ProfitEma; }
                    }
                double condSum = 0; int nInd = 0; double assessSum = 0;
                foreach (var pl in sim.W.Parcels)
                    if (pl.State == ParcelState.Built && pl.Use == ZoneKind.Industrial)
                    { nInd++; condSum += pl.Condition; assessSum += LandAccounting.UnitAssessment(pl, p); }
                Console.WriteLine($"t={sim.W.Tick,4}: ind alive={alive} dead={dead} "
                    + $"money/firm={(alive > 0 ? money / alive : 0):F0} revEma/firm={(alive > 0 ? rev / alive : 0):F2} | "
                    + $"parcels={nInd} meanCond={(nInd > 0 ? condSum / nInd : 0):F2} meanAssess/unit={(nInd > 0 ? assessSum / nInd : 0):F2}");
            }
            var w = sim.W; var acc = sim.Engine.Access;

            var builtByUse = new int[7]; var ucByUse = new int[7];
            foreach (var pl in w.Parcels)
            {
                if (pl.State == ParcelState.Built) builtByUse[(int)pl.Use]++;
                else if (pl.State == ParcelState.UnderConstruction) ucByUse[(int)pl.Use]++;
            }
            Console.WriteLine("built by use: " + string.Join(" ", Enum.GetValues<ZoneKind>()
                .Where(k => k != ZoneKind.None)
                .Select(k => $"{k}={builtByUse[(int)k]}+{ucByUse[(int)k]}uc")));

            double[] resid = new double[6];
            for (int u = 0; u < 6; u++)
                for (int c = 0; c < acc.C; c++) resid[u] += sim.Engine.Residuals.ByUse[u][c];
            Console.WriteLine($"residual Σ: resLow={resid[0]:F0} resHigh={resid[1]:F0} com={resid[2]:F0} ind={resid[3]:F0} off={resid[4]:F0} ext={resid[5]:F0}");

            var top = Enumerable.Range(0, acc.C)
                .OrderByDescending(c => sim.Engine.Residuals.ByUse[3][c]).Take(5);
            double S2 = LandAccounting.SPerUnit(2, 1.0, p);
            foreach (int c in top)
            {
                double bid = LandAccounting.FirmBidPerSlot(acc, sim.Engine.Trade, c, ZoneKind.Industrial, 2, p);
                Console.WriteLine($"  cluster {c}: indBid/slot={bid:F3} vs S(2)/unit={S2:F3} resid={sim.Engine.Residuals.ByUse[3][c]:F2}");
            }

            // Entry margin on each VACANT industrial parcel: excess = bid − assessment.
            int shown = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Use != ZoneKind.Industrial || pl.OccupantFirm >= 0) continue;
                double bid = LandAccounting.FirmBidPerSlot(acc, sim.Engine.Trade, pl.Cluster, pl.Use, pl.Level, p,
                                                           out _, w.Clusters) * p.CondFactor(pl.Condition);
                double assess = LandAccounting.UnitAssessment(pl, p);
                if (shown++ < 8)
                    Console.WriteLine($"  parcel {pl.Id} cl={pl.Cluster} lvl={pl.Level} cond={pl.Condition:F2}: "
                        + $"bid={bid:F3} assess={assess:F3} excess={bid - assess:F3} LR={pl.AssessedLR:F2}");
            }
            Console.WriteLine($"  vacant industrial parcels: {shown}");
            return 0;
        }

        /// <summary>`harness incomeprobe` — show the within-segment income
        /// distribution the clearing price now walks (Income.cs): per segment,
        /// the quantile bins at a representative cluster, the mean, and the
        /// spread. Answers "is the demand curve actually a curve now, or still
        /// a staircase" with the numbers rather than an argument.</summary>
        /// <summary>`harness incomeprobe` — the demand side as it actually
        /// is: the REAL households behind each segment's bid ladder, their
        /// observed income spread (an emergent property of the population, not
        /// a fitted distribution), and the demand curve they generate at a
        /// cluster.</summary>
        public static int IncomeProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(ticks);
            var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence; var w = sim.W;

            int c0 = 0;
            for (int c = 1; c < acc.C; c++)
                if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;

            Console.WriteLine("observed household income by segment (emergent — these are the people, counted):");
            var incBySeg = new List<double>[Segment.Count];
            for (int s = 0; s < Segment.Count; s++) incBySeg[s] = new List<double>();
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0) continue;
                incBySeg[h.Segment].Add(AccessState.HouseholdIncomeEstimate(h, Segment.All[h.Segment], p));
            }
            for (int s = 0; s < Segment.Count; s++)
            {
                var xs = incBySeg[s]; if (xs.Count == 0) continue;
                xs.Sort();
                double Q(double q) => xs[Math.Min(xs.Count - 1, (int)(q * xs.Count))];
                Console.WriteLine($"  {Segment.All[s].Name,-12} n={xs.Count,5} adults={Segment.All[s].Adults} "
                    + $"mean={xs.Average(),6:F2}  p10={Q(0.10),6:F2} p50={Q(0.50),6:F2} p90={Q(0.90),6:F2} "
                    + $"spread={(Q(0.10) > 1e-9 ? Q(0.90) / Q(0.10) : 0),5:F1}x  ladder={acc.BidLadder[0][s].Length}");
            }

            Console.WriteLine($"\ndemand curve at cluster {c0} (ResidentialLow, level 2), "
                + $"stock={acc.HousingStock[0][c0]:F0} — every rung is one real household:");
            var pts = new List<(double wtp, double mass, string who)>();
            for (int s = 0; s < Segment.Count; s++)
            {
                if (pres[s] < 1) continue;
                var seg = Segment.All[s];
                double rel = acc.AccessValue[s][c0] / acc.MeanAccess;
                double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                double mult = prem * p.BidAccessScale * (p.Quality(2) / p.Quality(1));
                var lad = acc.BidLadder[0][s];
                if (lad.Length == 0) continue;
                double massPer = acc.SegmentKindShare[s][0][c0] * (pres[s] / lad.Length);
                foreach (var b in lad) pts.Add((b * mult, massPer, seg.Name));
            }
            pts.Sort((x, y) => y.wtp.CompareTo(x.wtp));
            double cum = 0; int shown = 0;
            foreach (var t in pts)
            {
                cum += t.mass;
                if (shown++ < 10 || (shown % Math.Max(1, pts.Count / 6) == 0 && shown < pts.Count - 1))
                    Console.WriteLine($"    wtp {t.wtp,8:F3}  cum {cum,8:F2}  ({t.who})");
            }
            Console.WriteLine($"    ... {pts.Count} real households on the curve, total mass {cum:F1}");
            double bid = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fill);
            Console.WriteLine($"  → clearing bid {bid:F3}, expected fill {fill:P0}");
            return 0;
        }

        public static int PriceProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(ticks);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

            int pop = 0; foreach (var h in w.Households) if (h.ExitedTick < 0) pop++;
            double totalStock = 0;
            for (int k = 0; k < 2; k++) for (int c = 0; c < acc.C; c++) totalStock += acc.HousingStock[k][c];
            Console.WriteLine($"pop={pop} totalStock={totalStock:F0} citywide occupancy={pop / Math.Max(1, totalStock):P1}");

            foreach (var (kind, ki) in new[] { (ZoneKind.ResidentialLow, 0), (ZoneKind.ResidentialHigh, 1) })
            {
                var order = Enumerable.Range(0, acc.C).OrderByDescending(c => acc.HousingStock[ki][c]).Take(4);
                Console.WriteLine($"--- {kind} (S(1)={LandAccounting.SPerUnit(1, 1.0, p):F3} S(2)={LandAccounting.SPerUnit(2, 1.0, p):F3}) ---");
                foreach (int c in order)
                {
                    double stock = acc.HousingStock[ki][c];
                    if (stock <= 0) continue;
                    double mass = 0, wtpTop = 0, wtpBot = double.MaxValue;
                    for (int s = 0; s < Segment.Count; s++)
                    {
                        if (pres[s] < 1) continue;
                        var seg = Segment.All[s];
                        double m = pres[s] * acc.SegmentKindShare[s][ki][c];
                        mass += m;
                        double income = acc.ExpectedIncome(s, c, p);
                        double rel = acc.AccessValue[s][c] / acc.MeanAccess;
                        double prem = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                        double wtp = seg.MaxRentShare * income * prem * p.BidAccessScale * seg.DensityAppeal(kind);
                        if (m > 1e-9) { wtpTop = Math.Max(wtpTop, wtp); wtpBot = Math.Min(wtpBot, wtp); }
                    }
                    double bid = LandAccounting.ResidentialBidPerUnit(acc, c, kind, 1, pres, p);
                    double filled = 0;
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == kind && pl.Cluster == c)
                            filled += pl.OccupantHouseholds.Count;
                    Console.WriteLine(
                        $"  c={c,3} stock={stock,6:F0} filled={filled,6:F0} ({filled / stock,5:P0}) mass={mass,7:F1} "
                        + $"D/S={mass / stock,5:F2} fillEma={acc.FillEma[ki][c],4:F2} wtp[{wtpBot,5:F2}..{wtpTop,5:F2}] bid={bid,6:F3}");
                }
            }
            Console.WriteLine($"AUDIT priced={LandAccounting.AuditTotalPriced} excess={LandAccounting.AuditExcessCalls} "
                + $"breakIter1={LandAccounting.AuditBreakIter1} ratio5e-5={LandAccounting.AuditRatio5e5} "
                + $"allLadderAbove={LandAccounting.AuditAllAbove} dustZero={LandAccounting.AuditDustZero}");
            return 0;
        }

        /// <summary>`harness vacprobe` — replicates the vacancy-localization
        /// scenario's setup and prints the overhang's life cycle every 10
        /// ticks: does the shock create persistent vacancies, does the kernel
        /// drive residual negative in the shocked set, and what refills it
        /// (arrivals vs internal chain moves)?</summary>
        /// <summary>Where the level geography comes from: the spread of the
        /// clearing price across clusters and the spread of the supported level
        /// ℓ* it implies. A flat price field cannot produce a level map that
        /// correlates with access, however good the construction rule is, so
        /// when the level-map scenario slips this is the first thing to read.</summary>
        public static int LevelProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(ticks);
            var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

            foreach (var (kind, ki) in new[] { (ZoneKind.ResidentialLow, 0), (ZoneKind.ResidentialHigh, 1) })
            {
                var bids = new List<double>(); var stars = new List<int>(); var prem = new List<double>();
                for (int c = 0; c < acc.C; c++)
                {
                    if (acc.HousingStock[ki][c] < 4) continue;
                    int lStar = 1; double best = double.NegativeInfinity;
                    for (int l = 1; l <= p.MaxLevel; l++)
                    {
                        double b = LandAccounting.ResidentialBidPerUnit(acc, c, kind, l, pres, p);
                        double v = b - LandAccounting.SPerUnit(l, 1.0, p);
                        if (v > best) { best = v; lStar = l; }
                    }
                    bids.Add(LandAccounting.ResidentialBidPerUnit(acc, c, kind, 2, pres, p));
                    stars.Add(lStar);
                    double rel = acc.AccessValue[0][c] / acc.MeanAccess;
                    prem.Add(MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0));
                }
                if (bids.Count == 0) { Console.WriteLine($"{kind}: no stock"); continue; }
                var sorted = bids.OrderBy(x => x).ToList();
                double Pct(double q) => sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];
                var hist = new int[p.MaxLevel + 1];
                foreach (int l in stars) hist[l]++;
                Console.WriteLine($"{kind}: {bids.Count} clusters  bid p10={Pct(0.1):F2} p50={Pct(0.5):F2} "
                    + $"p90={Pct(0.9):F2} (p90/p10 {Pct(0.9) / Math.Max(1e-9, Pct(0.1)):F1}×)  "
                    + $"premium [{prem.Min():F2}..{prem.Max():F2}]");
                Console.WriteLine("   ℓ* histogram: " + string.Join("  ",
                    Enumerable.Range(1, p.MaxLevel).Select(l => $"L{l}={hist[l]}")));
                var built = new int[p.MaxLevel + 1];
                foreach (var pl in sim.W.Parcels)
                    if (pl.State == ParcelState.Built && pl.Use == kind) built[pl.Level]++;
                Console.WriteLine("   realized:     " + string.Join("  ",
                    Enumerable.Range(1, p.MaxLevel).Select(l => $"L{l}={built[l]}")));
            }
            // Same statistic the §6 level-map target scores, so a calibration
            // sweep can be read here without a 2×1300-tick scenario run.
            var lv = new List<double>(); var sup = new List<double>();
            foreach (var pl in sim.W.Parcels)
            {
                if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                int lStar = 1; double best = double.NegativeInfinity;
                for (int l = 1; l <= p.MaxLevel; l++)
                {
                    double b = LandAccounting.BidPerUnit(acc, sim.Engine.Trade, pl.Cluster, pl.Use, l, pres, p);
                    double v = b - LandAccounting.SPerUnit(l, 1.0, p);
                    if (v > best) { best = v; lStar = l; }
                }
                lv.Add(pl.Level); sup.Add(lStar);
            }
            Console.WriteLine($"   Spearman(realized, ℓ*) = {Scenarios.SpearmanPublic(lv, sup):F2}  (§6 bar 0.35)");
            return 0;
        }

        /// <summary>`harness auctionprobe` — does the assignment market clear,
        /// and does it clear at a sane price? Reports the solve's own telemetry
        /// (bids, evictions, whether it reached an ε-equilibrium), the price
        /// distribution, and the two equilibrium conditions that matter:
        /// capacity is never oversubscribed, and no household would rather have
        /// somebody else's place at the posted price (no envy).</summary>
        public static int AuctionProbe(ulong seed, int ticks, double ownerAskScale = 1.0)
        {
            // ownerAskScale: the item-#41 cost/attribution A/B — 0 folds every
            // owner door (asks never bind), 1 is shipped behavior; both runs
            // belong in any claim about what the doors cost.
            var p = new EconParams { OwnerAskScale = ownerAskScale };
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            sim.Run(ticks);
            var w = sim.W; var a = sim.Engine.Auction; var acc = sim.Engine.Access;

            int pop = 0, housed = 0;
            foreach (var h in w.Households) { if (h.ExitedTick < 0) { pop++; if (h.HomeParcel >= 0) housed++; } }
            double stock = 0;
            for (int s = 0; s < a.Capacity.Length; s++) stock += a.Capacity[s];

            Console.WriteLine($"phases(ms, cumulative over run): submarkets={HousingAuction.MsSubmarkets:F0} "
                + $"households={HousingAuction.MsHouseholds:F0} auction={HousingAuction.MsAuction:F0} "
                + $"repairScan={HousingAuction.MsRepairScan:F0} "
                + $"shadow={HousingAuction.MsShadow:F0}");
            Console.WriteLine($"calls: valueAtSlot={HousingAuction.CallsValueSlot:N0} softCap={HousingAuction.CallsSoftCap:N0}");
            if (HousingAuction.ScanOracle)
                Console.WriteLine($"oracle: scans={HousingAuction.OracleScans:N0} "
                    + $"householdScans={HousingAuction.OracleHhScans:N0} "
                    + $"columnMismatch={HousingAuction.OracleKcMismatch:N0} "
                    + $"gainMismatch={HousingAuction.OracleGainMismatch:N0} "
                    + $"dirtySetMismatch={HousingAuction.OracleSetMismatch:N0}");
            Console.WriteLine($"solve: bids={a.Bids} evictions={a.Evictions} converged={a.Converged} "
                + $"unassigned={a.Unassigned}");
            Console.WriteLine($"world: pop={pop} housed={housed} lettable units={stock:F0} "
                + $"({housed / Math.Max(1.0, stock):P0} of stock)");

            // Owner doors (item #41): the door count is printed, not inferred
            // — it is what the scan-cost budget scales with — alongside the
            // decline-exit telemetry the self-pricing bound is measured by.
            int ownerTagged = 0, bindingAsks = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.OwnerHousehold < 0) continue;
                ownerTagged++;
                if (pl.OwnerAskPerUnit > 0) bindingAsks++;
            }
            Console.WriteLine($"owners: tagged={ownerTagged} standingAsks={bindingAsks} "
                + $"liveDoors={a.OwnerDoorCount} ownerDeclineExits={sim.Engine.OwnerDeclineExitsTotal} "
                + $"tenantVacatesAtOwnerParcels={sim.Engine.OwnerParcelTenantVacatesTotal} "
                + $"(OwnerAskScale={p.OwnerAskScale})");

            // Price distribution over submarkets that hold stock.
            var live = new List<int>();
            for (int s = 0; s < a.Capacity.Length; s++) if (a.Capacity[s] > 0) live.Add(s);
            var px = live.Select(s => a.Price[s]).OrderBy(x => x).ToList();
            if (px.Count > 0)
            {
                double Pc(double q) => px[Math.Min(px.Count - 1, (int)(q * px.Count))];
                Console.WriteLine($"price: {live.Count} live submarkets  p10={Pc(0.1):F2} p50={Pc(0.5):F2} "
                    + $"p90={Pc(0.9):F2} ({Pc(0.9) / Math.Max(1e-9, Pc(0.1)):F1}×)  "
                    + $"at reserve: {live.Count(s => a.Price[s] <= a.Reserve[s] + 1e-9)}");
            }

            // (1) capacity respected, (2) posted ≤ admitted (the marginal tenant
            // keeps surplus), (3) fill.
            int over = 0, inverted = 0; double filledSum = 0;
            foreach (int s in live)
            {
                if (a.Filled[s] > a.Capacity[s]) over++;
                if (a.Price[s] > a.Admitted[s] + 1e-9) inverted++;
                filledSum += a.Filled[s];
            }
            Console.WriteLine($"invariants: oversubscribed={over} posted>admitted={inverted} "
                + $"occupancy={filledSum / Math.Max(1, stock):P0}");

            // (4) NO ENVY. At the posted prices, no household may strictly
            // prefer another submarket it could have had. This is THE property
            // that makes the outcome a competitive equilibrium, and it is the
            // one the old two-mechanism model could not state, let alone hold.
            int envy = 0, checkedHh = 0; double worstEnvy = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick < 0 && h.HomeParcel >= 0) checkedHh++;
            }
            (envy, worstEnvy) = Debugging.EnvyCount(w, a, p);
            Console.WriteLine($"no-envy: {envy} of {checkedHh} housed households would rather have "
                + $"another submarket at its posted price (worst gain {worstEnvy:F3}, ε={p.AuctionEpsilon})");
            return 0;
        }

        /// <summary>How many housed households would strictly gain by taking a
        /// different submarket at ITS posted price, and by how much. Shared with
        /// the acceptance check so both measure exactly the same thing.</summary>
        public static (int count, double worst) EnvyCount(WorldState w, HousingAuction a, EconParams p)
        {
            int envy = 0; double worst = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if ((uint)h.Id >= (uint)a.Assignment.Length) continue;
                int mine = a.Assignment[h.Id];
                if (mine < 0) continue;
                double myS = a.ValueOf(h.Id, mine, p) - a.Price[mine];
                double best = myS; int bestSub = mine;
                for (int q = 0; q < a.ShortlistCount(h.Id); q++)
                    for (int l = 1; l <= HousingAuction.Levels; l++)
                    {
                        int sub = HousingAuction.Sub(a.ShortlistAt(h.Id, q), l, a.C);
                        if (a.Capacity[sub] <= 0) continue;
                        double s2 = a.ValueOf(h.Id, sub, p) - a.Price[sub];
                        if (s2 > best) { best = s2; bestSub = sub; }
                    }
                if (bestSub != mine && best > myS + 1e-9)
                { envy++; worst = Math.Max(worst, best - myS); }
            }
            return (envy, worst);
        }

        public static int VacProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000, ParcelsPerCluster = 14 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(100);
            var w = sim.W;
            // Soft market: the reservation field at equilibrium (gap ≈ ε), the
            // design's own §4.1 soft-landing state — desired inflow ≈ replacement.
            for (int s = 0; s < Segment.Count; s++)
                w.Migration.ReservationThreshold[s] = Math.Max(0,
                    w.Migration.SegmentAttractEma[s] - Migration.BaseOutsideUtility - 0.02);

            // Mirror the scenario's disk shock exactly.
            double mx = 0, my = 0;
            foreach (var ci in w.Clusters) { mx += ci.X; my += ci.Y; }
            mx /= w.Clusters.Length; my /= w.Clusters.Length;
            double sxq = mx + 2800.0 / w.MetersPerUnit;
            var shocked = new HashSet<int>();
            for (int c = 0; c < w.Clusters.Length; c++)
            {
                double dx = (w.Clusters[c].X - sxq) * w.MetersPerUnit;
                double dy = (w.Clusters[c].Y - my) * w.MetersPerUnit;
                if (Math.Sqrt(dx * dx + dy * dy) <= 2500.0) shocked.Add(c);
            }
            int evictedN = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                var pl = w.Parcels[h.HomeParcel];
                if (!shocked.Contains(pl.Cluster)) continue;
                if (SplitMix64.Hash01((ulong)h.Id * 31 + seed) < 0.95)
                {
                    sim.Engine.Allocation.Vacate(w, h);
                    w.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                    h.Money = 0; h.ExitedTick = w.Tick;
                    evictedN++;
                }
            }
            Console.WriteLine($"shock applied: {evictedN} evicted over {shocked.Count} disk clusters");
            var reported = new HashSet<int>();

            long from = w.Tick;
            int startsShocked = 0, startsFar = 0, arrivalsCum = 0;
            sim.Run(ticks, s =>
            {
                arrivalsCum += s.Engine.LastFlows.ArrivalsBySegment?.Sum() ?? 0;
                // Forensics: report each in-disk residential start once, with
                // the cluster residual at that moment.
                foreach (var (tick, pid, cluster) in sim.Starts)
                {
                    if (tick <= from || !shocked.Contains(cluster) || !reported.Add(pid)) continue;
                    var sp = s.W.Parcels[pid];
                    if (sp.Use != ZoneKind.ResidentialLow && sp.Use != ZoneKind.ResidentialHigh) continue;
                    double r = s.Engine.Residuals.Get(cluster, sp.Use, s.W.Claims);
                    Console.WriteLine($"    START t+{tick - from} parcel={pid} cl={cluster} use={sp.Use} " +
                                      $"lvl={sp.Level} units={sp.Units} resid({sp.Use})={r:F1}");
                }
                if ((s.W.Tick - from) % 10 != 0) return;
                double vacShocked = 0, vacFar = 0;
                foreach (var pl in s.W.Parcels)
                    if (pl.State == ParcelState.Built && pl.IsResidential)
                    { if (shocked.Contains(pl.Cluster)) vacShocked += pl.Vacant; else vacFar += pl.Vacant; }
                double resShocked = 0, resFar = 0;
                int nS = 0, nF = 0;
                for (int c = 0; c < s.Engine.Access.C; c++)
                {
                    double r = s.Engine.Residuals.Get(c, ZoneKind.ResidentialLow, s.W.Claims)
                             + s.Engine.Residuals.Get(c, ZoneKind.ResidentialHigh, s.W.Claims);
                    if (shocked.Contains(c)) { resShocked += r; nS++; } else { resFar += r; nF++; }
                }
                startsShocked = 0; startsFar = 0;
                foreach (var (tick, pid, cluster) in sim.Starts)
                {
                    if (tick <= from) continue;
                    var pl = s.W.Parcels[pid];
                    if (!pl.IsResidential && pl.Use != ZoneKind.ResidentialLow && pl.Use != ZoneKind.ResidentialHigh) continue;
                    if (shocked.Contains(cluster)) startsShocked++; else startsFar++;
                }
                int unhoused = 0;
                foreach (var h in s.W.Households) if (h.ExitedTick < 0 && h.HomeParcel < 0) unhoused++;
                double desired = s.Engine.LastFlows.DesiredBySegment?.Sum() ?? 0;
                Console.WriteLine(
                    $"t+{s.W.Tick - from,4} vacS={vacShocked,5:F0} vacF={vacFar,5:F0} " +
                    $"resS/cl={(nS > 0 ? resShocked / nS : 0),7:F1} resF/cl={(nF > 0 ? resFar / nF : 0),7:F1} " +
                    $"unhoused={unhoused,4} desired={desired,6:F1}/t arrCum={arrivalsCum,5} " +
                    $"startsS={startsShocked,3} startsF={startsFar,4}");
            });
            return 0;
        }

        /// <summary>`harness firmdiag` — firm-level A/B: demand-informed siting
        /// (spatial) vs geography-blind spawning (vanilla) on identical seeds.
        /// Answers: do shops sit where demand is, are they meeting real demand
        /// (zombie share), and is profitability higher under residual-driven
        /// site selection?</summary>
        public static int FirmDiag(ulong seed, int ticks)
        {
            (string label, double profitRate, double zombieShare, double comVacancy,
             int alive, int dead, double alignment, double capturePerSlot)
                RunMode(bool vanilla)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
                var sim = Sim.Create(cfg, p, new FeatureFlags(), vanillaMode: vanilla);
                var w = sim.W;

                // Track cumulative commercial revenue and wage cost over the last
                // window for a real operating-margin measure.
                double revWindow = 0, ageProfitNum = 0, ageProfitDen = 0;
                int windowFrom = ticks - 200;
                sim.Run(ticks, s2 =>
                {
                    if (s2.W.Tick < windowFrom) return;
                    foreach (var fm in s2.W.Firms)
                        if (!fm.Dead && fm.Sector == ZoneKind.Commercial) revWindow += fm.RevenueThisTick;
                });

                int alive = 0, dead = 0; double zombies = 0;
                int slots = 0;
                foreach (var fm in w.Firms)
                {
                    if (fm.Sector != ZoneKind.Commercial) continue;
                    if (fm.Dead) { dead++; continue; }
                    alive++;
                    slots += fm.JobSlots;
                    double seedCap = fm.EnteredTick > 0 ? p.FirmSeedCapital : 300;
                    long age = Math.Max(1, w.Tick - fm.EnteredTick);
                    ageProfitNum += (fm.Money - seedCap) / age;
                    ageProfitDen += 1;
                    // Zombie: sustained revenue below its wage bill.
                    double wageBill = 0;
                    for (int cl = 0; cl < 3; cl++) wageBill += fm.FilledByClass[cl] * p.Wage((LaborClass)cl);
                    if (fm.ProfitEma < wageBill) zombies++;
                }

                int comParcels = 0, comVacant = 0;
                foreach (var pl in w.Parcels)
                    if (pl.State == ParcelState.Built && pl.Use == ZoneKind.Commercial)
                    { comParcels++; if (pl.OccupantFirm < 0) comVacant++; }

                // Alignment: rank correlation across clusters between commercial
                // slots and household spending mass (do shops sit where money is?).
                var acc = sim.Engine.Access;
                var slotsByCluster = new double[acc.C];
                foreach (var fm in w.Firms)
                    if (!fm.Dead && fm.Sector == ZoneKind.Commercial && fm.Parcel >= 0)
                        slotsByCluster[w.Parcels[fm.Parcel].Cluster] += fm.JobSlots;
                double alignment = SpearmanArr(slotsByCluster, acc.SpendMass);

                return (vanilla ? "vanilla" : "spatial",
                        ageProfitDen > 0 ? ageProfitNum / ageProfitDen : 0,
                        alive > 0 ? zombies / alive : 0,
                        comParcels > 0 ? (double)comVacant / comParcels : 0,
                        alive, dead, alignment,
                        slots > 0 ? revWindow / 200 / slots : 0);
            }

            foreach (var vanilla in new[] { false, true })
            {
                var r = RunMode(vanilla);
                Console.WriteLine(
                    $"{r.label,8}: com firms alive={r.alive} dead={r.dead} | " +
                    $"profit/firm/tick={r.profitRate:F3} | zombie share={r.zombieShare:P0} | " +
                    $"com-parcel vacancy={r.comVacancy:P0} | " +
                    $"shops-vs-spending alignment (Spearman)={r.alignment:F2} | " +
                    $"capture rev/slot/tick={r.capturePerSlot:F2}");
            }
            return 0;
        }

        private static double SpearmanArr(double[] a, double[] b)
        {
            var la = a.ToList(); var lb = b.ToList();
            int n = la.Count;
            double[] Ranks(System.Collections.Generic.List<double> xs)
            {
                var idx = Enumerable.Range(0, xs.Count).OrderBy(i => xs[i]).ToArray();
                var ranks = new double[xs.Count];
                int i0 = 0;
                while (i0 < idx.Length)
                {
                    int i1 = i0;
                    while (i1 + 1 < idx.Length && xs[idx[i1 + 1]] == xs[idx[i0]]) i1++;
                    double avg = (i0 + i1) / 2.0;
                    for (int k = i0; k <= i1; k++) ranks[idx[k]] = avg;
                    i0 = i1 + 1;
                }
                return ranks;
            }
            var ra = Ranks(la); var rb = Ranks(lb);
            double ma = ra.Average(), mb = rb.Average(), cov = 0, va = 0, vb = 0;
            for (int i = 0; i < n; i++)
            {
                cov += (ra[i] - ma) * (rb[i] - mb);
                va += (ra[i] - ma) * (ra[i] - ma);
                vb += (rb[i] - mb) * (rb[i] - mb);
            }
            return va > 0 && vb > 0 ? cov / Math.Sqrt(va * vb) : 0;
        }

        public static int Run(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;

            Console.WriteLine($"city: {w.Clusters.Length} clusters, {w.Parcels.Count} parcels, " +
                              $"{w.Households.Count} households, {w.Firms.Count} firms, {w.Exits.Count} exits");

            for (int t = 0; t < ticks; t++)
            {
                sim.Step();
                if (w.Tick % 100 != 0) continue;
                PrintTickSummary(w, sim.Engine, p);
            }

            PrintLevelByAccess(w, sim.Engine);
            return 0;
        }

        /// <summary>`harness map` — the same instrumented run on an imported real
        /// road topology (.cs2city). Converts the committed TNTP source on first
        /// use if the binary file is missing.</summary>
        public static int MapRun(ulong seed, int ticks, string file)
        {
            var p = new EconParams();
            if (!System.IO.File.Exists(file))
            {
                string net = System.IO.Path.Combine("data", "chicago-regional", "ChicagoRegional_net.tntp");
                string node = System.IO.Path.Combine("data", "chicago-regional", "ChicagoRegional_node.tntp");
                if (!System.IO.File.Exists(net) || !System.IO.File.Exists(node))
                {
                    Console.WriteLine($"map file not found: {file} (and no TNTP source in data/ to convert)");
                    return 2;
                }
                Console.WriteLine($"converting {net} + {node} -> {file}");
                CityImport.ConvertTntp(net, node, file);
            }

            var cfg = new CityImport.Options { Seed = seed };
            var (w, access) = CityImport.BuildWorld(file, cfg, p);
            var engine = new EconomyEngine(w, access, p, new FeatureFlags());

            Console.WriteLine($"map: {file} — real road topology, economy template layered by geometry");
            Console.WriteLine($"graph: {access.NodeCount} nodes, {access.EdgeCount} directed edges");
            Console.WriteLine($"city: {w.Clusters.Length} clusters, {w.Parcels.Count} parcels, " +
                              $"{w.Households.Count} households, {w.Firms.Count} firms, {w.Exits.Count} exits");

            // Cluster stats: parcel density and the commute cost matrix spread.
            var offDiag = new List<double>();
            for (int i = 0; i < access.ClusterCount; i++)
                for (int j = 0; j < access.ClusterCount; j++)
                    if (i != j) offDiag.Add(access.Cost(i, j, AccessPurpose.Commute));
            offDiag.Sort();
            double p50 = offDiag[offDiag.Count / 2];
            double p95 = offDiag[(int)(offDiag.Count * 0.95)];
            Console.WriteLine($"clusters: {access.ClusterCount}, mean parcels/cluster={(double)w.Parcels.Count / access.ClusterCount:F1}, " +
                              $"cost matrix p50={p50:F1} min p95={p95:F1} min");

            for (int t = 0; t < ticks; t++)
            {
                engine.Step();
                if (w.Tick % 100 != 0) continue;
                PrintTickSummary(w, engine, p);
            }

            PrintLevelByAccess(w, engine);
            return 0;
        }

        /// <summary>Citywide realized prior plus the min/max per-cluster origin
        /// statistic — the localized price's spread at a glance (§4.7 honesty:
        /// the realized stat floats inside the parity band; a spread of zero
        /// means no localization is expressing on this fixture).</summary>
        private static string PriceSpread(TradeSystem trade, Res r, WorldState w)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int c = 0; c < w.Clusters.Length; c++)
            {
                double v = trade.OriginStat(r, c);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            return $"{trade.CityOrigin(r):F2}[{lo:F2}..{hi:F2}]";
        }

        /// <summary>The per-100-ticks status block shared by `debug` and `map`.</summary>
        private static void PrintTickSummary(WorldState w, EconomyEngine engine, EconParams p)
        {
            int pop = 0, housed = 0, shel = 0, employed = 0;
            foreach (var h in w.Households)
                if (h.ExitedTick < 0)
                {
                    pop++;
                    if (h.HomeParcel >= 0) housed++;
                    if (h.Stage == InsolvencyStage.Sheltered) shel++;
                    if (h.Employed) employed++;
                }
            double hhMoney = w.Ledger.Balance(Account.Households);
            double firmMoney = w.Ledger.Balance(Account.Firms);
            double treasury = w.Ledger.Balance(Account.Treasury);
            double escrow = w.Ledger.Balance(Account.Escrow);

            int built = 0, uc = 0, empty = 0;
            var levelHist = new int[6];
            double wedgeSum = 0, lrSum = 0; int occupiedRes = 0; double assessSum = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State == ParcelState.Built) { built++; levelHist[pl.Level]++; }
                else if (pl.State == ParcelState.UnderConstruction) uc++;
                else empty++;
                wedgeSum += pl.Wedge; lrSum += pl.AssessedLR;
                if (pl.State == ParcelState.Built && pl.IsResidential && pl.OccupantHouseholds.Count > 0)
                { occupiedRes++; assessSum += LandAccounting.UnitAssessment(pl, p); }
            }

            double rawExp = 0, rawImp = 0, goodsExp = 0, goodsImp = 0, rawSust = 0, goodsSust = 0;
            foreach (var x in w.Exits)
            {
                if (ResourceCatalog.IsRaw(x.Resource)) { rawSust += x.SustainedQ; rawExp += x.ExportedThisTick; rawImp += x.ImportedThisTick; }
                if (ResourceCatalog.IsProcessed(x.Resource)) { goodsSust += x.SustainedQ; goodsExp += x.ExportedThisTick; goodsImp += x.ImportedThisTick; }
            }

            var aliveBySector = w.Firms.Where(x => !x.Dead).GroupBy(x => x.Sector)
                .ToDictionary(g => g.Key, g => (n: g.Count(), fill: g.Average(x => x.WorkersFilled / Math.Max(1, x.JobSlots)), money: g.Sum(x => x.Money)));
            string FirmS(ZoneKind k) => aliveBySector.TryGetValue(k, out var v) ? $"{v.n}({v.fill:F1},{v.money:F0})" : "0";
            double renoReady = 0, renoDone = w.RenovationsTotal; int renoCand = 0;
            foreach (var pl in w.Parcels)
                if (pl.State == ParcelState.Built && !pl.TargetIsScrape && pl.TargetUse == pl.Use && pl.TargetLevel > pl.Level)
                {
                    renoCand++;
                    double cost = Math.Max(1, p.RC(pl.TargetLevel, pl.Units) - pl.Condition * p.RC(pl.Level, pl.Units));
                    renoReady += Math.Min(1.0, pl.Escrow / cost);
                }
            Console.WriteLine(
                $"  firms: com={FirmS(ZoneKind.Commercial)} ind={FirmS(ZoneKind.Industrial)} off={FirmS(ZoneKind.Office)} ext={FirmS(ZoneKind.Extractor)} | " +
                $"renoCand={renoCand} meanFill={(renoCand > 0 ? renoReady / renoCand : 0):P0} renos={renoDone} scrapes={w.ScrapesTotal}");
            Console.WriteLine(
                $"t={w.Tick,5} pop={pop,6} housed={housed,6} emp={100.0 * employed / Math.Max(1, pop):F0}% shel={shel,4} | " +
                $"$hh={hhMoney:F0} $firm={firmMoney:F0} $trs={treasury:F0} $esc={escrow:F0} | " +
                $"built={built} uc={uc} empty={empty} L={string.Join(",", levelHist.Skip(1))} | " +
                $"starts={engine.Construction.StartedTotal} aband={engine.Construction.AbandonedTotal} | " +
                $"raw sust={rawSust:F0} x={rawExp:F0}/i={rawImp:F0} goods sust={goodsSust:F0} x={goodsExp:F0}/i={goodsImp:F0} | " +
                $"pOre={PriceSpread(engine.Trade, Res.Ore, w)} pMetals={PriceSpread(engine.Trade, Res.Metals, w)} | " +
                $"wedgeΣ={wedgeSum:F0} LRΣ={lrSum:F0} avgAssess={assessSum / Math.Max(1, occupiedRes):F2} | " +
                $"displ={engine.DisplacementExits.Count} " +
                $"arr={engine.LastFlows.ArrivalsBySegment?.Sum() ?? 0} dep={engine.LastFlows.DeparturesBySegment?.Sum() ?? 0} " +
                $"drift={w.Ledger.Drift():E1}");
        }

        /// <summary>`harness goodsprobe` — measurement aid for the local
        /// goods-price work (task #30): on the reference fixture (10×10
        /// clusters, 3000 seed households, default flags, seeds start..start+3),
        /// print (a) the per-(resource, cluster) transacted-volume EMA
        /// distribution over t=40..300 — what calibrates the shrinkage prior
        /// weight EconParams.TradePricePriorVolume — (b) the per-lot bounds the
        /// clearing comments state: worst phase-2 seller regret, worst band
        /// floor violation, and the buyer-side band ceiling in its three
        /// references (marginal unit, marginal lot, tick-opening parity —
        /// TradeSystem.MeasureBand), and (c) the end-state per-resource spread
        /// of the delivered/origin statistics against the citywide prior.
        /// Prints, asserts nothing.</summary>
        public static int GoodsProbe(ulong startSeed, int ticks)
        {
            var dEv = new List<double>();   // positive delivered-volume EMA samples
            var oEv = new List<double>();
            long dZero = 0, oZero = 0, cells = 0;
            double worstRegret = double.NegativeInfinity, worstFloor = double.NegativeInfinity;
            double worstUnit = double.NegativeInfinity, worstLot = double.NegativeInfinity;
            double worstOpen = double.NegativeInfinity;
            for (ulong seed = startSeed; seed < startSeed + 4; seed++)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
                var sim = Sim.Create(cfg, p, new FeatureFlags());
                var tel = new List<TradeSystem.SettleRecord>();
                TradeSystem.SettleTelemetry = tel;
                try
                {
                    for (int t = 1; t <= Math.Max(300, ticks); t++)
                    {
                        sim.Engine.Step();
                        if (t < 40 || t % 20 != 0) continue;
                        for (int r = 0; r < ResourceCatalog.Count; r++)
                        {
                            if (!ResourceCatalog.IsTradable((Res)r)) continue;
                            for (int c = 0; c < sim.Access.ClusterCount; c++)
                            {
                                cells++;
                                double dv = sim.Engine.Trade.DeliveredEvidence((Res)r, c);
                                double ov = sim.Engine.Trade.OriginEvidence((Res)r, c);
                                if (dv > 0.01) dEv.Add(dv); else dZero++;
                                if (ov > 0.01) oEv.Add(ov); else oZero++;
                            }
                        }
                    }
                }
                finally { TradeSystem.SettleTelemetry = null; }
                foreach (var rec in tel)
                {
                    worstRegret = Math.Max(worstRegret, rec.WorstPhase2Regret);
                    worstFloor = Math.Max(worstFloor, rec.WorstFloorViolation);
                    worstUnit = Math.Max(worstUnit, rec.WorstMarginExcess);
                    worstLot = Math.Max(worstLot, rec.WorstMarginExcessLot);
                    worstOpen = Math.Max(worstOpen, rec.WorstOpenParityExcess);
                }
                // End-state spread per resource on this seed.
                Console.WriteLine($"--- seed {seed} end-state (t={Math.Max(300, ticks)}):");
                for (int r = 0; r < ResourceCatalog.Count; r++)
                {
                    var res = (Res)r;
                    if (!ResourceCatalog.IsTradable(res)) continue;
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity, evTot = 0;
                    int active = 0;
                    for (int c = 0; c < sim.Access.ClusterCount; c++)
                    {
                        double v = sim.Engine.Trade.DeliveredStat(res, c);
                        lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                        double ev = sim.Engine.Trade.DeliveredEvidence(res, c);
                        evTot += ev;
                        if (ev > 0.01) active++;
                    }
                    Console.WriteLine($"  {res,-9} city D={sim.Engine.Trade.CityDelivered(res):F3} "
                        + $"O={sim.Engine.Trade.CityOrigin(res):F3} stat[{lo:F3}..{hi:F3}] "
                        + $"activeClusters={active} ΣdeliveredEv={evTot:F1}");
                }
            }
            double P(List<double> xs, double q)
            {
                if (xs.Count == 0) return 0;
                xs.Sort();
                return xs[Math.Min(xs.Count - 1, (int)(xs.Count * q))];
            }
            Console.WriteLine($"\nvolume-EMA samples (per res×cluster×20-tick, {cells} cells): "
                + $"delivered {dEv.Count} positive / {dZero} ~zero; origin {oEv.Count} / {oZero}");
            Console.WriteLine($"delivered EMA p10={P(dEv, 0.10):F2} p25={P(dEv, 0.25):F2} "
                + $"p50={P(dEv, 0.50):F2} p90={P(dEv, 0.90):F2} max={P(dEv, 1.0):F2}");
            Console.WriteLine($"origin    EMA p10={P(oEv, 0.10):F2} p25={P(oEv, 0.25):F2} "
                + $"p50={P(oEv, 0.50):F2} p90={P(oEv, 0.90):F2} max={P(oEv, 1.0):F2}");
            Console.WriteLine($"worst phase-2 regret (ask − realized net) = {worstRegret:F4}; "
                + $"worst band floor violation (ask+haul − settled) = {worstFloor:E2}");
            Console.WriteLine($"band ceiling — settled local price − cheapest alternative at the margin: "
                + $"unit-granularity {worstUnit:F4}, lot-granularity {worstLot:E2}; "
                + $"vs tick-opening import parity {worstOpen:F4}");
            return 0;
        }

        /// <summary>`harness jobspread` — measurement aid for the prospect
        /// local-odds work (task #31): on the auction reference fixture
        /// (10×10 clusters, 3000 seed households — the auction-equilibrium /
        /// canary fixture), print the per-cluster worker-count distribution and
        /// the per-cluster employment-rate spread for each labor class, at a few
        /// ticks. This is what calibrates the shrinkage prior weight n0 in
        /// AccessState.ProspectLocalOdds and decides whether the
        /// prospect-localization check can bite. Prints, asserts nothing.</summary>
        public static int JobSpread(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            var sampleAt = new[] { 40, 80, 160, Math.Max(240, ticks) };
            int done = 0;
            for (int t = 1; t <= sampleAt[sampleAt.Length - 1]; t++)
            {
                sim.Step();
                if (done < sampleAt.Length && t == sampleAt[done])
                {
                    done++;
                    var acc = sim.Engine.Access;
                    Console.WriteLine($"--- t={t} C={acc.C}");
                    for (int cl = 0; cl < 3; cl++)
                    {
                        var wk = acc.WorkersByClass[cl];
                        var er = acc.EmploymentRate[cl];
                        var withWorkers = new List<(double w, double r)>();
                        double totW = 0, totMatched = 0; int zero = 0;
                        for (int c = 0; c < acc.C; c++)
                        {
                            if (wk[c] > 1e-9) { withWorkers.Add((wk[c], er[c])); totW += wk[c]; totMatched += wk[c] * er[c]; }
                            else zero++;
                        }
                        if (withWorkers.Count == 0) { Console.WriteLine($"  class {cl}: no workers"); continue; }
                        var ws = withWorkers.Select(x => x.w).OrderBy(x => x).ToList();
                        double P(List<double> xs, double q) => xs[Math.Min(xs.Count - 1, (int)(q * xs.Count))];
                        double bench = totMatched / totW;                    // worker-weighted citywide
                        double dilute = 0; for (int c = 0; c < acc.C; c++) dilute += er[c];
                        dilute /= acc.C;                                     // the old zero-diluted mean
                        var rs = withWorkers.Select(x => x.r).OrderBy(x => x).ToList();
                        double var2 = withWorkers.Sum(x => x.w * (x.r - bench) * (x.r - bench)) / totW;
                        Console.WriteLine(
                            $"  class {cl}: clusters w/ workers {withWorkers.Count}/{acc.C} (zero {zero}) | " +
                            $"workers/cluster min={ws[0]:F1} p25={P(ws, 0.25):F1} med={P(ws, 0.5):F1} p75={P(ws, 0.75):F1} p90={P(ws, 0.90):F1} max={ws[ws.Count - 1]:F1} | " +
                            $"rate bench(w-wtd)={bench:F3} diluted-mean={dilute:F3} " +
                            $"p10={P(rs, 0.10):F3} med={P(rs, 0.5):F3} p90={P(rs, 0.90):F3} w-wtd-sd={Math.Sqrt(var2):F3}");
                    }
                }
            }
            return 0;
        }

        /// <summary>End-of-run snapshot shared by `debug` and `map`.</summary>
        private static void PrintLevelByAccess(WorldState w, EconomyEngine engine)
        {
            Console.WriteLine("\nlevel vs access-rank (built residential):");
            var rows = w.Parcels.Where(x => x.State == ParcelState.Built && x.IsResidential)
                .Select(x => (x.Level, acc: Enumerable.Range(0, Segment.Count).Sum(s => engine.Access.AccessValue[s][x.Cluster])))
                .OrderBy(x => x.acc).ToList();
            int q = Math.Max(1, rows.Count / 5);
            for (int i = 0; i < 5; i++)
            {
                var slice = rows.Skip(i * q).Take(q).ToList();
                if (slice.Count > 0)
                    Console.WriteLine($"  access quintile {i + 1}: mean level {slice.Average(x => x.Level):F2} (n={slice.Count})");
            }
        }

        /// <summary>`harness entrydiag` — the entrant post-mortem. Measured on
        /// the store-level path, 88% of commercial shops born mid-run died
        /// within 40 ticks against 0% on the pooled path, and this command is
        /// what separates the three candidate causes with numbers instead of an
        /// argument:
        ///
        ///  (a) an arithmetic COLD START — the entrant never trades at all, so
        ///      it dies of a history it could not have had. Measured as the
        ///      share of dead entrants with zero presented custom over their
        ///      whole life, and the age at which the first custom arrives;
        ///  (b) a forecast/realization MISMATCH — the entry decision's own read
        ///      does not predict the entrant's own catchment. Measured as
        ///      forecast-at-entry against realized presented over the first 40
        ///      ticks, per entrant, plus the CO-ENTRY count: how many shops
        ///      entered the same cluster off the same read before that read was
        ///      next rebuilt;
        ///  (c) correct SELECTION — the entrants that die are the ones with no
        ///      catchment, the survivors are the ones with custom, and the fix
        ///      is the entry rate. Measured as the realized-custom distribution
        ///      of dying entrants against surviving ones.
        ///
        /// Both arms run, because the pooled arm's zero deaths is a tell rather
        /// than a target: its field feeds every entrant a share of citywide
        /// spending whether or not anyone would ever shop there.</summary>
        public static int EntryDiag(ulong startSeed, int seeds, int ticks)
        {
            Console.WriteLine($"entrydiag: seeds {startSeed}..{startSeed + (ulong)seeds - 1}, {ticks} ticks");
            foreach (bool on in new[] { false, true })
            {
                // Per entrant. read6 is what FirmBidPerSlot's commercial leg
                // actually asks for — the CLUSTER reference mass 6.0, blind to
                // this parcel's own units and condition. readOwn is the same
                // field asked at the mass a SHOPPER sees at this parcel
                // (units x condition x quality), which is the object
                // ChooseShops scores. Realized is presented custom per tick.
                var life = new List<(double read6, double readOwn, double fcast, double pres,
                                     int age, bool dead, int firstCustomAge, int coEntry,
                                     int liveAt, int vacantAt, double presLife, double cond)>();
                var vacantCond = new List<double>();
                for (ulong s = startSeed; s < startSeed + (ulong)seeds; s++)
                {
                    var p = new EconParams();
                    var sim = Sim.Create(new SyntheticCity.Config { Seed = s, SeedHouseholds = 8000 },
                                         p, new FeatureFlags { StoreLevelSpending = on });
                    var rec = new Dictionary<int, (double read6, double readOwn, double fcast, long born,
                                                   int cluster, double pres, int firstCustom, int liveAt,
                                                   int vacantAt, double presLife, bool dead, int age, double cond)>();
                    // Entrants per (cluster, refresh window): how many shops were
                    // sited off ONE read before that read was next rebuilt.
                    var window = new Dictionary<(int c, long w), int>();
                    sim.Run(ticks, s2 =>
                    {
                        var w2 = s2.W; var acc = s2.Engine.Access;
                        foreach (var f in w2.Firms)
                        {
                            if (f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                            var pl = w2.Parcels[f.Parcel];
                            if (!f.Dead && f.EnteredTick > 20 && !rec.ContainsKey(f.Id)
                                && w2.Tick - f.EnteredTick == 1)
                            {
                                double quality = p.Quality(pl.Level) / p.Quality(1);
                                double fe = LandAccounting.FirmFillEstimate(acc, pl.Cluster, ZoneKind.Commercial);
                                double read6 = acc.CommercialCapture(pl.Cluster, 6.0 * quality);
                                double ownMass = f.JobSlots * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level);
                                double readOwn = acc.CommercialCapture(pl.Cluster, ownMass);
                                // The forecast in the realization's own unit:
                                // money of custom per tick this shop expects at
                                // its own roster, formed exactly as
                                // FirmBidPerSlot forms it.
                                double perFilled = Math.Min(read6 / Math.Max(1e-9, 6.0 * fe),
                                                            p.CommercialServicePerSlot * quality);
                                int liveAt = 0, vacantAt = 0;
                                foreach (var g in w2.Firms)
                                    if (!g.Dead && g.Sector == ZoneKind.Commercial && g.Parcel >= 0
                                        && w2.Parcels[g.Parcel].Cluster == pl.Cluster && g.Id != f.Id) liveAt++;
                                foreach (var q in w2.Parcels)
                                    if (q.State == ParcelState.Built && q.Use == ZoneKind.Commercial
                                        && q.Cluster == pl.Cluster && q.OccupantFirm < 0) vacantAt++;
                                long win = f.EnteredTick / p.RefreshInterval;
                                window.TryGetValue((pl.Cluster, win), out int k);
                                window[(pl.Cluster, win)] = k + 1;
                                rec[f.Id] = (read6, readOwn, perFilled * f.JobSlots * fe, f.EnteredTick,
                                             pl.Cluster, 0, -1, liveAt, vacantAt, 0, false, 0, pl.Condition);
                            }
                            if (!rec.TryGetValue(f.Id, out var r)) continue;
                            int age = (int)(w2.Tick - r.born);
                            if (f.Dead)
                            {
                                if (!r.dead)
                                    rec[f.Id] = (r.read6, r.readOwn, r.fcast, r.born, r.cluster, r.pres,
                                                 r.firstCustom, r.liveAt, r.vacantAt, r.presLife, true, age,
                                                 r.cond);
                                continue;
                            }
                            double pres = f.PresentedThisTick;
                            // The pooled path never fills PresentedThisTick — it
                            // has no per-shop custom at all — so its realization
                            // is read from the revenue the pool hands it. Without
                            // this the pooled column reads 0.000 everywhere and
                            // says nothing.
                            if (!on) pres = f.RevenueThisTick;
                            // Realization window: ticks 6..40 of its life. It
                            // starts at 6 because ChooseShops runs on refresh
                            // ticks only, so a shop born at t has no customer
                            // until the next refresh; counting those ticks would
                            // measure the refresh grid rather than the catchment.
                            rec[f.Id] = (r.read6, r.readOwn, r.fcast, r.born, r.cluster,
                                         age >= 6 && age <= 40 ? r.pres + pres : r.pres,
                                         r.firstCustom < 0 && pres > 1e-9 ? age : r.firstCustom,
                                         r.liveAt, r.vacantAt, r.presLife + pres, false,
                                         Math.Max(r.age, age), r.cond);
                        }
                    });
                    foreach (var pl in sim.W.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == ZoneKind.Commercial
                            && pl.OccupantFirm < 0) vacantCond.Add(pl.Condition);
                    foreach (var kv in rec)
                    {
                        var r = kv.Value;
                        long win = r.born / p.RefreshInterval;
                        window.TryGetValue((r.cluster, win), out int co);
                        int span = Math.Max(1, Math.Min(40, r.age) - 5);
                        life.Add((r.read6, r.readOwn, r.fcast, r.pres / span, r.age, r.dead,
                                  r.firstCustom, co, r.liveAt, r.vacantAt, r.presLife, r.cond));
                    }
                }
                string arm = on ? "store-level" : "pooled    ";
                if (life.Count == 0) { Console.WriteLine($"  {arm}: no mid-run entrants"); continue; }
                var died40 = life.Where(x => x.dead && x.age <= 40).ToList();
                var lived = life.Where(x => !x.dead || x.age > 40).ToList();
                Console.WriteLine($"  {arm}: entrants {life.Count}, died<=40t {died40.Count} "
                    + $"({(double)died40.Count / life.Count:P0})");
                int neverTraded = died40.Count(x => x.presLife <= 1e-9);
                var firstAges = life.Where(x => x.firstCustomAge >= 0)
                                    .Select(x => (double)x.firstCustomAge).OrderBy(z => z).ToList();
                Console.WriteLine($"    (a) COLD START: of the {died40.Count} that died young, "
                    + $"{neverTraded} never took a single customer; first custom at age "
                    + (firstAges.Count > 0 ? $"p50={Pct(firstAges, .5):F0} p90={Pct(firstAges, .9):F0}" : "n/a")
                    + $"; {life.Count(x => x.firstCustomAge < 0)} of {life.Count} never saw one");
                var withF = life.Where(x => x.fcast > 1e-6).ToList();
                if (withF.Count > 0)
                {
                    var r1 = withF.Select(x => x.pres / x.fcast).OrderBy(z => z).ToList();
                    Console.WriteLine($"    (b) FORECAST: realized presented/tick over ticks 6-40 "
                        + $"/ the entry decision's own forecast (n={r1.Count}): "
                        + $"p10={Pct(r1, .10):F3} p50={Pct(r1, .50):F3} p90={Pct(r1, .90):F3}");
                }
                var withR = life.Where(x => x.read6 > 1e-6 && x.readOwn > 1e-6).ToList();
                if (withR.Count > 0)
                {
                    // Where the error sits. GRAIN is the read's own mass error:
                    // the decision asks at the cluster reference mass 6.0, a
                    // shopper sees units x condition x quality.
                    var grain = withR.Select(x => x.readOwn / x.read6).OrderBy(z => z).ToList();
                    var vs6 = withR.Select(x => x.pres / x.read6).OrderBy(z => z).ToList();
                    var vsOwn = withR.Select(x => x.pres / x.readOwn).OrderBy(z => z).ToList();
                    Console.WriteLine($"        GRAIN read(own mass)/read(6.0): p10={Pct(grain, .1):F3} "
                        + $"p50={Pct(grain, .5):F3} p90={Pct(grain, .9):F3}");
                    Console.WriteLine($"        realized / read(6.0)     : p10={Pct(vs6, .1):F3} "
                        + $"p50={Pct(vs6, .5):F3} p90={Pct(vs6, .9):F3}");
                    Console.WriteLine($"        realized / read(own mass): p10={Pct(vsOwn, .1):F3} "
                        + $"p50={Pct(vsOwn, .5):F3} p90={Pct(vsOwn, .9):F3}");
                }
                var co2 = life.Select(x => (double)x.coEntry).OrderBy(z => z).ToList();
                var vac = life.Select(x => (double)x.vacantAt).OrderBy(z => z).ToList();
                Console.WriteLine($"        CO-ENTRY off one read: p50={Pct(co2, .5):F0} p90={Pct(co2, .9):F0} "
                    + $"max={co2[co2.Count - 1]:F0}; {life.Count(x => x.coEntry > 1)}/{life.Count} arrived where "
                    + $"another entered the same refresh window. Vacant commercial parcels at the cluster "
                    + $"reading the same value: p50={Pct(vac, .5):F0} p90={Pct(vac, .9):F0}");
                var econd = life.Select(x => x.cond).OrderBy(z => z).ToList();
                vacantCond.Sort();
                Console.WriteLine($"        CONDITION of the building taken: p10={Pct(econd, .1):F2} "
                    + $"p50={Pct(econd, .5):F2} p90={Pct(econd, .9):F2}; of commercial parcels left VACANT "
                    + $"at the end (n={vacantCond.Count}): p10={Pct(vacantCond, .1):F2} "
                    + $"p50={Pct(vacantCond, .5):F2} p90={Pct(vacantCond, .9):F2}");
                if (died40.Count > 0 && lived.Count > 0)
                {
                    var dp = died40.Select(x => x.pres).OrderBy(z => z).ToList();
                    var lp = lived.Select(x => x.pres).OrderBy(z => z).ToList();
                    var dl = died40.Select(x => (double)x.liveAt).OrderBy(z => z).ToList();
                    var ll = lived.Select(x => (double)x.liveAt).OrderBy(z => z).ToList();
                    Console.WriteLine($"    (c) SELECTION: realized presented/tick — "
                        + $"died p50={Pct(dp, .5):F1} p90={Pct(dp, .9):F1} | "
                        + $"survived p50={Pct(lp, .5):F1} p90={Pct(lp, .9):F1}; live shops already at "
                        + $"the cluster — died p50={Pct(dl, .5):F0}, survived p50={Pct(ll, .5):F0}");
                }
            }
            return 0;
        }

        /// <summary>`harness shopprobe` — the task #20 measurement aid. Every
        /// numeric bound this item ships comes from here, and every bound's
        /// comment names this command and the run.
        ///
        /// Prints, per seed and pooled across seeds:
        ///  - the realized presented-per-FILLED-slot distribution on the
        ///    store-level arm, which is what CommercialServicePerSlot is set at
        ///    a high percentile of (capacity should bind on the busiest shops,
        ///    never on the median);
        ///  - the shop-mass distribution over live shops, which is what the
        ///    intent histogram's log range must cover, plus any overflow reads
        ///    (a read landing in overflow is a bug, so the count must be 0);
        ///  - the histogram's discretization error against an EXACT unbucketed
        ///    read of the same intents, which is what IntentProbeBins is chosen by;
        ///  - the per-cluster head distribution behind IntentHeadFloor;
        ///  - capacity binding share, turned-away share, and entrant survival by
        ///    AGE — the population the cold start would kill;
        ///  - the sector-coherence relation: capacity at full staffing against
        ///    the spending the city presents. If the sector structurally cannot
        ///    serve the city, leakage explodes for reasons that have nothing to
        ///    do with siting, and this is the first thing to look at;
        ///  - the flag comment's own census on BOTH arms: alive/dead commercial
        ///    firms and commercial-parcel vacancy.</summary>
        public static int ShopProbe(ulong startSeed, int seeds, int ticks, double servicePerSlot = 0)
        {
            Console.WriteLine($"shopprobe: seeds {startSeed}..{startSeed + (ulong)seeds - 1}, {ticks} ticks"
                + (servicePerSlot > 0 ? $", CommercialServicePerSlot={servicePerSlot:G}" : ""));
            var presentedPerFilled = new List<double>();
            var massAll = new List<double>();
            double capBindTicks = 0, allTicks = 0, turnedAway = 0, servedTot = 0, overflowTot = 0;
            var headsAll = new List<double>();
            var countedPerSlot = new List<double>();
            var pooledPerSlot = new List<double>();
            var binErr = new Dictionary<int, double>();
            var census = new List<(bool on, int alive, int dead, double vac,
                                   double coherence, int youngDead, int born)>();

            for (ulong s = startSeed; s < startSeed + (ulong)seeds; s++)
                foreach (bool on in new[] { false, true })
                {
                    var p = new EconParams();
                    if (servicePerSlot > 0) p.CommercialServicePerSlot = servicePerSlot;
                    var sim = Sim.Create(new SyntheticCity.Config { Seed = s, SeedHouseholds = 8000 },
                                         p, new FeatureFlags { StoreLevelSpending = on });
                    int youngDead = 0, born = 0;
                    var seenBorn = new HashSet<int>();
                    var seenDead = new HashSet<int>();
                    sim.Run(ticks, s2 =>
                    {
                        var w2 = s2.W;
                        foreach (var f in w2.Firms)
                        {
                            if (f.Sector != ZoneKind.Commercial) continue;
                            if (f.EnteredTick > 0 && seenBorn.Add(f.Id)) born++;
                            if (f.Dead)
                            {
                                if (f.EnteredTick > 0 && seenDead.Add(f.Id)
                                    && w2.Tick - f.EnteredTick <= 40) youngDead++;
                                continue;
                            }
                            if (!on || f.Parcel < 0) continue;
                            allTicks++;
                            double c0 = EconomyEngine.CommercialServiceCapacity(f, w2.Parcels[f.Parcel], p);
                            if (c0 > 1e-9 && f.PresentedThisTick > c0 + 1e-9) capBindTicks++;
                            turnedAway += Math.Max(0, f.PresentedThisTick - f.ServedThisTick);
                            servedTot += f.ServedThisTick;
                            if (f.WorkersFilled > 1e-9 && w2.Tick > ticks - 200)
                                presentedPerFilled.Add(f.PresentedThisTick / f.WorkersFilled);
                        }
                    });

                    var w = sim.W; var acc = sim.Engine.Access;
                    int alive = 0, dead = 0;
                    foreach (var f in w.Firms)
                    {
                        if (f.Sector != ZoneKind.Commercial) continue;
                        if (f.Dead) { dead++; continue; }
                        alive++;
                        if (!on || f.Parcel < 0) continue;
                        var pl = w.Parcels[f.Parcel];
                        massAll.Add(f.JobSlots * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level));
                    }
                    int comParcels = 0, comVacant = 0;
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == ZoneKind.Commercial)
                        { comParcels++; if (pl.OccupantFirm < 0) comVacant++; }

                    double fullCap = 0, presented = 0;
                    foreach (var f in w.Firms)
                    {
                        if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                        var pl = w.Parcels[f.Parcel];
                        fullCap += p.CommercialServicePerSlot * f.JobSlots
                                   * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) / p.Quality(1);
                        presented += f.PresentedEma;
                    }
                    double coherence = presented > 1e-9 ? fullCap / presented : 0;
                    if (on)
                        for (int c = 0; c < acc.C; c++)
                        {
                            overflowTot += acc.IntentOverflow.Length > c ? acc.IntentOverflow[c] : 0;
                            double fe = LandAccounting.FirmFillEstimate(acc, c, ZoneKind.Commercial);
                            double read = acc.CountedIntent(c, 6.0);
                            headsAll.Add(acc.IntentHeadsFor(c, 6.0));
                            countedPerSlot.Add(read / Math.Max(1e-9, 6.0 * fe));
                            pooledPerSlot.Add(acc.PhantomCommercialCapture(c, 6.0) / 6.0);
                        }
                    double vac = comParcels > 0 ? (double)comVacant / comParcels : 0;
                    census.Add((on, alive, dead, vac, coherence, youngDead, born));
                    Console.WriteLine($"  seed {s} {(on ? "ON " : "OFF")}: com alive={alive} dead={dead} "
                        + $"vac={vac:P0} | entrants born={born} died≤40t={youngDead}"
                        + (on ? $" | fullcap/presented={coherence:F2}" : ""));
                    if (on) MeasureBinError(sim, p, binErr);
                }

            Console.WriteLine();
            if (presentedPerFilled.Count > 0)
            {
                presentedPerFilled.Sort();
                Console.WriteLine($"  presented per FILLED slot per tick (n={presentedPerFilled.Count}): "
                    + $"p25={Pct(presentedPerFilled, .25):F2} p50={Pct(presentedPerFilled, .50):F2} "
                    + $"p75={Pct(presentedPerFilled, .75):F2} p90={Pct(presentedPerFilled, .90):F2} "
                    + $"p99={Pct(presentedPerFilled, .99):F2} max={presentedPerFilled[presentedPerFilled.Count - 1]:F2}");
            }
            if (massAll.Count > 0)
            {
                massAll.Sort();
                Console.WriteLine($"  live shop mass (n={massAll.Count}): min={massAll[0]:F2} "
                    + $"p50={Pct(massAll, .5):F2} max={massAll[massAll.Count - 1]:F2} "
                    + $"| log range [{Math.Log(massAll[0]):F2}, {Math.Log(massAll[massAll.Count - 1]):F2}] "
                    + $"| intents past the range top={overflowTot:F0}");
            }
            if (headsAll.Count > 0)
            {
                headsAll.Sort();
                Console.WriteLine($"  intent heads BACKING the reference read (n={headsAll.Count}): min={headsAll[0]:F0} "
                    + $"p05={Pct(headsAll, .05):F0} p10={Pct(headsAll, .10):F0} p25={Pct(headsAll, .25):F0} "
                    + $"p50={Pct(headsAll, .50):F0} max={headsAll[headsAll.Count - 1]:F0}");
            }
            if (countedPerSlot.Count > 0)
            {
                countedPerSlot.Sort(); pooledPerSlot.Sort();
                Console.WriteLine($"  ENTRY READ per (filled) slot at the reference mass 6, over clusters: "
                    + $"counted p10={Pct(countedPerSlot, .10):F2} p50={Pct(countedPerSlot, .50):F2} "
                    + $"p90={Pct(countedPerSlot, .90):F2} max={countedPerSlot[countedPerSlot.Count - 1]:F2} "
                    + $"| pooled p10={Pct(pooledPerSlot, .10):F2} p50={Pct(pooledPerSlot, .50):F2} "
                    + $"p90={Pct(pooledPerSlot, .90):F2} max={pooledPerSlot[pooledPerSlot.Count - 1]:F2}");
            }
            if (allTicks > 0)
                Console.WriteLine($"  capacity binds on {capBindTicks / allTicks:P1} of {allTicks:F0} firm-ticks "
                    + $"| turned-away share of presented "
                    + $"{(servedTot + turnedAway > 0 ? turnedAway / (servedTot + turnedAway) : 0):P1}");
            foreach (var kv in binErr.OrderBy(k => k.Key))
                Console.WriteLine($"  discretization error, {kv.Key} bins: worst {kv.Value:P2} of the exact read (city aggregate)");
            foreach (var grp in census.GroupBy(c => c.on))
                Console.WriteLine($"  CENSUS {(grp.Key ? "store-level" : "pooled    ")}: "
                    + $"alive {grp.Average(c => (double)c.alive):F1} dead {grp.Average(c => (double)c.dead):F1} "
                    + $"com-parcel vacancy {grp.Average(c => c.vac):P0} "
                    + $"| entrants dying ≤40 ticks {grp.Sum(c => (double)c.youngDead):F0}"
                    + $"/{grp.Sum(c => (double)c.born):F0}"
                    + (grp.Key ? $" | fullcap/presented {grp.Average(c => c.coherence):F2}" : ""));
            return 0;
        }

        /// <summary>The binned counted-intent read against an EXACT unbucketed
        /// read built from the same households' own M* values, at the size the
        /// entry probe actually asks about. The binned read is deliberately
        /// conservative, so the error is one-sided; what is measured is what
        /// size resolution costs, which is how IntentProbeBins is chosen.</summary>
        private static void MeasureBinError(Sim sim, EconParams p, Dictionary<int, double> binErr)
        {
            var w = sim.W; var acc = sim.Engine.Access;
            var bestU = sim.Engine.ShopBestUtility;
            // Averaged over four read sizes. At one fixed size the metric is
            // degenerate: bin counts 12/24/48 put a boundary at the SAME place
            // below log 6, so all three drop the same window and report an
            // identical 4.52% (measured). What resolution costs is the distance
            // from the read to the nearest boundary below it, so the honest
            // measure averages over reads.
            var masses = new[] { 4.0, 6.0, 9.0, 14.0 };
            var exact = new double[acc.C * masses.Length];
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                int o = w.Parcels[h.HomeParcel].Cluster;
                if ((uint)o >= (uint)acc.C || (uint)h.Id >= (uint)bestU.Length) continue;
                var seg = Segment.All[h.Segment];
                double wh = AccessState.HouseholdIncomeEstimate(h, seg, p)
                            * (1 - MathUtil.Clamp(h.RentShare, 0, 0.9)) * p.BaseConsumptionShare;
                double bs = bestU[h.Id];
                for (int c = 0; c < acc.C; c++)
                {
                    double wsc = acc.WShop[o, c];
                    if (wsc <= 1e-12) continue;
                    // The same comparison the mechanism makes, unbucketed: the
                    // household own taste for a new store at c on one side,
                    // its own realized best on the other.
                    double e = SplitMix64.Hash01((ulong)h.Id * 2246822519UL + (ulong)c * 40503UL + 7717UL);
                    double eps = -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
                    for (int m = 0; m < masses.Length; m++)
                        if (Math.Log(wsc * masses[m]) + eps > bs) exact[m * acc.C + c] += wh;
                }
            }
            foreach (int bins in new[] { 24, 96, 192 })
            {
                var p2 = new EconParams
                {
                    IntentProbeBins = bins,
                    IntentProbeLogMassLo = p.IntentProbeLogMassLo,
                    IntentProbeLogMassHi = p.IntentProbeLogMassHi,
                    IntentHeadFloor = 0,
                };
                acc.CountShopIntents(w, p2, bestU);
                // AGGREGATE relative error, not the worst cluster: a read rests
                // on ~9 households (measured), so one household landing in the
                // partial bin is a ~11% single-cluster error at ANY bin width
                // and the max is a knife-edge, not a resolution measure. What a
                // developer signal is judged on is how much of the counted money
                // the bucketing drops city-wide.
                double gotSum = 0, exactSum = 0;
                for (int m = 0; m < masses.Length; m++)
                    for (int c = 0; c < acc.C; c++)
                    { gotSum += acc.CountedIntent(c, masses[m]); exactSum += exact[m * acc.C + c]; }
                double err = exactSum > 1e-9 ? Math.Abs(gotSum - exactSum) / exactSum : 0;
                binErr[bins] = Math.Max(binErr.TryGetValue(bins, out var prev) ? prev : 0, err);
            }
            acc.CountShopIntents(w, p, bestU);   // leave the field as the engine had it
        }

        /// <summary>`harness boomprobe` — the boom/bust scenario's own fixture,
        /// with every migration quantity printed per sub-window instead of one
        /// windowed total. It exists because the scenario's asserted inflow
        /// margin is `LastProspects.Admitted + Priced`, and that sum is bounded
        /// above by `Offered`, which `Prospects.Step` sizes from population and
        /// the tie stock alone — region-side, blind to city quality by the
        /// anti-smuggling rule. A pulse therefore cannot move the COUNT; it can
        /// only move how the same lookers SPLIT between coming, being priced
        /// out, and declining. This prints Offered alongside the split so that
        /// distinction is visible, and prints it per sub-window so a response
        /// that decays as the city absorbs the pulse can be told from one that
        /// never happened.
        ///
        /// Every bound the restated scenario ships is set from this output.</summary>
        public static int BoomProbe(ulong seed, int win, int sub)
        {
            var p = new EconParams { MigInElasticity = new EconParams().MigInElasticity * 0.3 };
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 6000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);
            Console.WriteLine($"boomprobe seed {seed}: {win}-tick windows in {sub}-tick sub-windows"
                              + " (base / +0.9 amenity / -0.9)");
            Console.WriteLine("  phase  t      offered admitted  priced declined  want%   desired  departs "
                              + "declEx    pop");
            void Windows(string tag)
            {
                for (int done = 0; done < win; done += sub)
                {
                    double offered = 0, admitted = 0, priced = 0, declined = 0,
                           desired = 0, departs = 0, declEx = 0;
                    sim.Run(sub, s =>
                    {
                        var pr = s.Engine.LastProspects;
                        offered += pr.Offered; admitted += pr.Admitted;
                        priced += pr.Priced; declined += pr.Declined;
                        desired += s.Engine.LastFlows.DesiredBySegment?.Sum() ?? 0;
                        departs += s.Engine.LastFlows.DeparturesBySegment?.Sum() ?? 0;
                        declEx += s.Engine.DeclineExitsThisTick;
                    });
                    int pop = 0;
                    foreach (var h in sim.W.Households) if (h.ExitedTick < 0) pop++;
                    double want = offered > 0 ? (admitted + priced) / offered : 0;
                    Console.WriteLine($"  {tag,-6} {sim.W.Tick,5}  {offered,8:F0}{admitted,9:F0}{priced,8:F0}"
                        + $"{declined,9:F0}{want,7:P1}{desired,10:F0}{departs,9:F0}{declEx,7:F0}{pop,7}");
                }
            }
            Windows("base");
            foreach (var c in sim.W.Clusters) c.Amenity += 0.9;
            Windows("boom");
            foreach (var c in sim.W.Clusters) c.Amenity -= 1.8;
            Windows("bust");
            foreach (var c in sim.W.Clusters) c.Amenity += 0.9;
            return 0;
        }

        /// <summary>`harness leveltraj` — the §6 level-map statistic as a
        /// TRAJECTORY instead of one reading. Same fixture and same ℓ* oracle as
        /// the levels scenario (8000 households, forecast curve across five
        /// levels of each built residential parcel), scored every `--every`
        /// ticks out to `--ticks`, on the spatial arm and the vanilla arm.
        ///
        /// It exists because the scenario's 2600-tick horizon and 0.35 bar were
        /// both derived on the POSTED path, and neither had been re-derived
        /// since the auction became the default. A horizon is only honest if
        /// the series has flattened by it, and a bar is only honest if it sits
        /// clear of where the series settles — this prints both so the two
        /// numbers can be set from a measurement rather than from one
        /// reading.</summary>
        public static int LevelTrajectory(ulong seed, int ticks, int every)
        {
            var p0 = new EconParams();
            double Score(Sim sim, EconParams p)
            {
                var lv = new List<double>(); var sup = new List<double>();
                foreach (var pl in sim.W.Parcels)
                {
                    if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                    int lStar = 1; double best = double.NegativeInfinity;
                    for (int l = 1; l <= p.MaxLevel; l++)
                    {
                        double bid = LandAccounting.BidPerUnit(sim.Engine.Access, sim.Engine.Trade,
                            pl.Cluster, pl.Use, l, sim.Engine.SegmentPresence, p);
                        double v = bid - LandAccounting.SPerUnit(l, 1.0, p);
                        if (v > best) { best = v; lStar = l; }
                    }
                    lv.Add(pl.Level); sup.Add(lStar);
                }
                return Scenarios.SpearmanPublic(lv, sup);
            }
            var arms = new List<(string name, Sim sim, EconParams p)>();
            foreach (bool vanilla in new[] { false, true })
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
                arms.Add((vanilla ? "vanilla" : "spatial",
                          Sim.Create(cfg, p, new FeatureFlags(), vanillaMode: vanilla), p));
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"leveltraj seed {seed}: Spearman(realized level, ℓ*) every {every} to {ticks} ticks");
            for (int t = every; t <= ticks; t += every)
            {
                var line = new StringBuilder($"  t={t,5}");
                foreach (var (name, sim, p) in arms)
                {
                    sim.Run(every);
                    line.Append($"  {name} {Score(sim, p):F3}");
                }
                var built = new int[p0.MaxLevel + 2];
                foreach (var pl in arms[0].sim.W.Parcels)
                    if (pl.State == ParcelState.Built && pl.IsResidential) built[pl.Level]++;
                line.Append("  spatial levels " + string.Join("/",
                    Enumerable.Range(1, p0.MaxLevel).Select(l => built[l].ToString())));
                line.Append($"  [{sw.Elapsed.TotalSeconds:F0}s]");
                Console.WriteLine(line.ToString());
            }
            return 0;
        }

        /// <summary>`harness occprobe` — the occupancy check's two legs measured
        /// as DISTRIBUTIONS instead of at one selected cluster, on the same
        /// pinned-posted fixture the check uses (10×10, 3000 households, 120
        /// ticks). Every bound the rewritten check ships is set from this
        /// command's output; it asserts nothing itself.
        ///
        /// Leg (b): the FillEma 1.0 → 0.2 reprice is run on EVERY cleared
        /// candidate submarket, not on the lowest-indexed one, and the response
        /// ratio bidEmpty/bidFull is printed as a distribution. `flat` counts
        /// clusters whose price did not move AT ALL (ratio within 1e-9 of 1) and
        /// `noShare` how many of those also saw no demand-share movement — the
        /// attribution for a mute cluster.
        ///
        /// Leg (a): the EMA recursion residual. FillEma is snapshotted before a
        /// refresh tick, the fill target is recomputed independently from the
        /// parcels, one tick is run, and |F_new − Ema(F_prev, target)| is
        /// printed. `moved` and `below1` are the non-degeneracy populations —
        /// how many submarkets the EMA actually stepped on, and how many carry a
        /// target below 1 (a constant-1 target makes the recursion vacuous).</summary>
        public static int OccProbe(ulong start, int n, int ticks)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (ulong seed = start; seed < start + (ulong)n; seed++)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
                var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = false });
                sim.Run(ticks);
                var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

                // ---- leg (b): response on EVERY cleared candidate -----------
                var ratios = new List<double>();
                int candidates = 0, cleared = 0, flat = 0, noShare = 0;
                var flatList = new List<int>();
                for (int c = 0; c < acc.C; c++)
                {
                    if (acc.HousingStock[0][c] < 4) continue;
                    candidates++;
                    LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double f0);
                    if (f0 < 1.0 - 1e-9) continue;
                    cleared++;
                    double[] saved = { acc.FillEma[0][c], acc.FillEma[1][c] };
                    double ShareAt(int cl)
                    {
                        double m = 0;
                        for (int s = 0; s < Segment.Count; s++) m += acc.SegmentKindShare[s][0][cl] * pres[s];
                        return m;
                    }
                    void Reprice(double fill)
                    {
                        acc.FillEma[0][c] = fill; acc.FillEma[1][c] = fill;
                        acc.RebuildDemandShares(w, p);
                    }
                    Reprice(1.0);
                    double bidFull = LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p);
                    double mFull = ShareAt(c);
                    Reprice(0.2);
                    double bidEmpty = LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p);
                    double mEmpty = ShareAt(c);
                    acc.FillEma[0][c] = saved[0]; acc.FillEma[1][c] = saved[1];
                    acc.RebuildDemandShares(w, p);
                    double r = bidFull > 1e-12 ? bidEmpty / bidFull : 1.0;
                    ratios.Add(r);
                    if (r > 1 - 1e-9) { flat++; flatList.Add(c); if (mFull - mEmpty < 1e-9) noShare++; }
                }
                ratios.Sort();

                // ---- leg (a): EMA recursion residual ------------------------
                var target = new double[2][]; var prev = new double[2][];
                for (int k = 0; k < 2; k++) { target[k] = new double[acc.C]; prev[k] = new double[acc.C]; }
                var stock = new double[2][];
                for (int k = 0; k < 2; k++) stock[k] = new double[acc.C];
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                    if ((uint)pl.Cluster >= (uint)acc.C) continue;
                    int k = pl.Use == ZoneKind.ResidentialHigh ? 1 : 0;
                    stock[k][pl.Cluster] += pl.Units;
                    target[k][pl.Cluster] += Math.Min(pl.Units, pl.OccupantHouseholds.Count);
                }
                for (int k = 0; k < 2; k++)
                    for (int c = 0; c < acc.C; c++)
                    {
                        prev[k][c] = acc.FillEma[k][c];
                        target[k][c] = stock[k][c] > 0 ? MathUtil.Clamp(target[k][c] / stock[k][c], 0, 1) : 1.0;
                    }
                sim.Run(1);
                double worstRes = 0, worstErr = 0, sumErr = 0;
                int compared = 0, moved = 0, below1 = 0;
                var live = new List<(int k, int c)>();
                for (int k = 0; k < 2; k++)
                    for (int c = 0; c < acc.C; c++)
                    {
                        if (stock[k][c] < 4) continue;
                        compared++; live.Add((k, c));
                        double want = MathUtil.Ema(prev[k][c], target[k][c], AccessState.FillEmaAlpha);
                        worstRes = Math.Max(worstRes, Math.Abs(acc.FillEma[k][c] - want));
                        double err = Math.Abs(prev[k][c] - target[k][c]);
                        sumErr += err; worstErr = Math.Max(worstErr, err);
                        if (Math.Abs(want - prev[k][c]) > 1e-6) moved++;
                        if (target[k][c] < 0.98) below1++;
                    }
                // Discrimination: the same identity read against the NEXT
                // submarket's inputs. This is the paired non-degeneracy leg —
                // it counts how many of the compared submarkets the identity
                // can actually tell apart, so a world where every submarket
                // carries the same fill cannot pass leg (a) vacuously.
                int discriminated = 0;
                for (int i = 0; i < live.Count; i++)
                {
                    var (k, c) = live[i];
                    var (k2, c2) = live[(i + 1) % live.Count];
                    double wrong = MathUtil.Ema(prev[k2][c2], target[k2][c2], AccessState.FillEmaAlpha);
                    if (Math.Abs(acc.FillEma[k][c] - wrong) > 1e-9) discriminated++;
                }

                Console.WriteLine(
                    $"seed {seed,4}: cand {candidates,3} cleared {cleared,3} | ratio min {Pct(ratios, 0):F3} "
                    + $"p10 {Pct(ratios, 0.10):F3} p25 {Pct(ratios, 0.25):F3} med {Pct(ratios, 0.50):F3} "
                    + $"p75 {Pct(ratios, 0.75):F3} max {Pct(ratios, 1):F3} | <0.95 {ratios.Count(x => x < 0.95),3} "
                    + $"flat {flat,3} noShare {noShare,3}"
                    + (flatList.Count > 0 && flatList.Count <= 6 ? " @" + string.Join(",", flatList) : "")
                    + $" || ema: cmp {compared,3} residual {worstRes:E2} lagErr mean {sumErr / Math.Max(1, compared):F3} "
                    + $"worst {worstErr:F3} moved {moved,3} below1 {below1,3} discr {discriminated,3}");
            }
            Console.WriteLine($"occprobe: {n} seeds in {sw.Elapsed.TotalSeconds:F0}s");
            return 0;
        }

        /// <summary>`harness parityprobe` — the task-#19 measurement: what a
        /// firm on a parcel actually faces against what a household on a parcel
        /// faces. Every number the parity answer quotes comes from here.</summary>
        public static int ParityProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 9000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;

            double hhOwed = 0, hhPaid = 0, fOwed = 0, fPaid = 0;
            int firmShortTicks = 0, firmTicks = 0;
            sim.Run(ticks, s =>
            {
                var e = s.Engine;
                hhOwed += e.HouseholdLevyOwedThisTick; hhPaid += e.HouseholdLevyPaidThisTick;
                fOwed += e.FirmLevyOwedThisTick; fPaid += e.FirmLevyPaidThisTick;
                foreach (var fm in s.W.Firms)
                {
                    if (fm.Dead || fm.Parcel < 0) continue;
                    firmTicks++;
                    if (fm.LevyShortTicks > 0) firmShortTicks++;
                }
            });

            Console.WriteLine($"parityprobe seed={seed} ticks={ticks} pop={sim.Population[^1]}");
            Console.WriteLine("-- levy incidence (cumulative over the run) --");
            Console.WriteLine($"  households  owed={hhOwed:F0} paid={hhPaid:F0} " +
                              $"shortfall={(hhOwed > 0 ? 1 - hhPaid / hhOwed : 0) * 100:F2}%");
            Console.WriteLine($"  firms       owed={fOwed:F0} paid={fPaid:F0} " +
                              $"shortfall={(fOwed > 0 ? 1 - fPaid / fOwed : 0) * 100:F2}%");
            Console.WriteLine($"  firm share of billed assessment = " +
                              $"{(hhOwed + fOwed > 0 ? fOwed / (hhOwed + fOwed) : 0) * 100:F2}%");
            Console.WriteLine($"  firm-ticks in arrears = {firmShortTicks}/{firmTicks} " +
                              $"({(firmTicks > 0 ? (double)firmShortTicks / firmTicks : 0) * 100:F2}%)");
            Console.WriteLine($"  arrears exits = {sim.Engine.FirmArrearsExitsTotal} " +
                              $"(land charge unmet for {p.LandArrearsTicks} consecutive ticks)");

            // -- sustained arrears, by sector, and what happened to those firms.
            var bySector = new Dictionary<ZoneKind, (int alive, int arrears50, int arrears200, double cumShort)>();
            int aliveTotal = 0, deadTotal = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead) { deadTotal++; continue; }
                if (f.Parcel < 0) continue;
                aliveTotal++;
                bySector.TryGetValue(f.Sector, out var e2);
                e2.alive++;
                if (f.LevyShortTicks >= 50) e2.arrears50++;
                if (f.LevyShortTicks >= 200) e2.arrears200++;
                e2.cumShort += Math.Max(0, f.LevyOwedCum - f.LevyPaidCum);
                bySector[f.Sector] = e2;
            }
            Console.WriteLine($"-- sustained arrears at t={w.Tick} (alive={aliveTotal} dead={deadTotal}) --");
            foreach (var kv in bySector.OrderBy(k => k.Key.ToString()))
                Console.WriteLine($"  {kv.Key,-12} alive={kv.Value.alive,4} " +
                                  $"short>=50t={kv.Value.arrears50,4} short>=200t={kv.Value.arrears200,4} " +
                                  $"cumUnpaid={kv.Value.cumShort:F0}");

            // -- can the bill be met? Per sector: the firm's own revenue EMA
            // against the per-tick bill on the land it stands on, plus fill and
            // condition. The household analog pays 95% of a price it bid itself;
            // a firm is billed 95% of a forecast made for a hypothetical entrant.
            Console.WriteLine("-- bill vs takings, per sector (alive, parcelled firms) --");
            foreach (var sector in new[] { ZoneKind.Commercial, ZoneKind.Industrial,
                                           ZoneKind.Office, ZoneKind.Extractor })
            {
                int n = 0; double bill = 0, rev = 0, wages = 0, money = 0, cond = 0, fill = 0;
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != sector) continue;
                    var pl = w.Parcels[f.Parcel];
                    n++;
                    bill += LandAccounting.UnitAssessment(pl, p) * pl.Units;
                    rev += f.ProfitEma;
                    for (int cl = 0; cl < 3; cl++) wages += f.FilledByClass[cl] * p.Wage((LaborClass)cl);
                    money += f.Money; cond += pl.Condition;
                    fill += pl.Units > 0 ? f.WorkersFilled / pl.Units : 0;
                }
                if (n == 0) { Console.WriteLine($"  {sector,-12} none alive"); continue; }
                Console.WriteLine($"  {sector,-12} n={n,4} bill/tick={bill / n,9:F2} revEma={rev / n,9:F2} " +
                                  $"wages={wages / n,8:F2} money={money / n,9:F2} " +
                                  $"cond={cond / n:F2} fill={fill / n:F2}");
            }

            // -- which configuration is the bill actually priced at? The
            // residential level ladder is bounded by what households would pay
            // (the auction price / shadow queue); the firm ladder is a margin
            // formula with no clearing, so the level-max is unopposed.
            Console.WriteLine("-- assessed configuration, per sector (occupied parcels) --");
            foreach (var sector in new[] { ZoneKind.Commercial, ZoneKind.Industrial,
                                           ZoneKind.Office, ZoneKind.Extractor })
            {
                int n = 0; double lvl = 0, tlvl = 0, lrAt = 0, lrCur = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != sector || pl.OccupantFirm < 0) continue;
                    n++; lvl += pl.Level; tlvl += pl.TargetLevel;
                    lrAt += pl.AssessedLR; lrCur += pl.CurrentResidual;
                }
                if (n == 0) continue;
                Console.WriteLine($"  {sector,-12} n={n,4} meanLevel={lvl / n:F2} meanTargetLevel={tlvl / n:F2} " +
                                  $"meanLR={lrAt / n,9:F2} meanCurrentResidual={lrCur / n,9:F2}");
            }

            // -- is there cheaper land to move to? The firm analog of the
            // household's SortDown leg needs a vacant same-sector parcel whose
            // bill the firm could actually meet.
            Console.WriteLine("-- vacant non-residential land, per sector --");
            foreach (var sector in new[] { ZoneKind.Commercial, ZoneKind.Industrial,
                                           ZoneKind.Office, ZoneKind.Extractor })
            {
                int nv = 0; double minBill = double.PositiveInfinity, sumBill = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != sector || pl.OccupantFirm >= 0) continue;
                    if (pl.Warehousing) continue;
                    nv++;
                    double b = LandAccounting.UnitAssessment(pl, p) * pl.Units;
                    sumBill += b; if (b < minBill) minBill = b;
                }
                Console.WriteLine($"  {sector,-12} vacant={nv,4} " +
                                  $"minBill={(nv > 0 ? minBill : 0),9:F2} meanBill={(nv > 0 ? sumBill / nv : 0),9:F2}");
            }

            // -- circularity exposure: which (resource, cluster) origin cells a
            // non-residential assessment reads, and how many sellers stand in
            // them. A singleton cell is a firm's own realized revenue feeding
            // the assessment of the land it sits on.
            int C = sim.Engine.Access.C;
            var sellers = new int[ResourceCatalog.Count, C];
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                if (f.Sector != ZoneKind.Industrial && f.Sector != ZoneKind.Extractor) continue;
                sellers[(int)f.Output, w.Parcels[f.Parcel].Cluster]++;
            }
            int occupied = 0, singleton = 0, thin = 0;
            double ownWeightSum = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.OccupantFirm < 0) continue;
                var f = w.Firms[pl.OccupantFirm];
                if (f.Dead) continue;
                if (f.Sector != ZoneKind.Industrial && f.Sector != ZoneKind.Extractor) continue;
                occupied++;
                int n = sellers[(int)f.Output, pl.Cluster];
                if (n == 1) singleton++;
                else if (n == 2) thin++;
                // How much of OriginStat is the LOCAL cell (the rest is the
                // citywide shrinkage prior): local weight n/(n+n0).
                double ev = sim.Engine.Trade.OriginEvidence(f.Output, pl.Cluster);
                double n0 = Math.Max(0, p.TradePricePriorVolume);
                if (n == 1) ownWeightSum += ev + n0 > 1e-12 ? ev / (ev + n0) : 0;
            }
            Console.WriteLine("-- assessment circularity exposure (industrial + extractor) --");
            Console.WriteLine($"  occupied producing parcels = {occupied}");
            Console.WriteLine($"  whose (output, cluster) origin cell has EXACTLY ONE seller = {singleton}" +
                              $" ({(occupied > 0 ? (double)singleton / occupied : 0) * 100:F1}%)");
            Console.WriteLine($"  two sellers = {thin}");
            Console.WriteLine($"  mean local-evidence weight on those singleton cells = " +
                              $"{(singleton > 0 ? ownWeightSum / singleton : 0):F3}");

            // -- extractor geology: Assess prices extractor land at a flat 0.5
            // suitability (the 4-arg FirmBidPerSlot passes workCluster: null),
            // while firm ENTRY prices the same parcel at the real geology.
            double flatSum = 0, geoSum = 0; int extParcels = 0; double worst = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Use != ZoneKind.Extractor) continue;
                extParcels++;
                double flat = LandAccounting.FirmBidPerSlot(sim.Engine.Access, sim.Engine.Trade,
                                                            pl.Cluster, pl.Use, pl.Level, p);
                double geo = LandAccounting.FirmBidPerSlot(sim.Engine.Access, sim.Engine.Trade,
                                                           pl.Cluster, pl.Use, pl.Level, p, out _, w.Clusters);
                flatSum += flat; geoSum += geo;
                double rel = flat > 1e-9 ? Math.Abs(geo - flat) / flat : (geo > 1e-9 ? 1 : 0);
                if (rel > worst) worst = rel;
            }
            Console.WriteLine("-- extractor assessment geology --");
            Console.WriteLine($"  built extractor parcels = {extParcels} " +
                              $"Σbid(flat 0.5)={flatSum:F1} Σbid(own geology)={geoSum:F1} " +
                              $"worst per-parcel rel gap={worst * 100:F1}%");

            // -- the three causal probes the parity checks are built on, run
            // here first so their bands are set from measurement.
            void Reassess(Parcel q) => LandAccounting.Assess(w, sim.Engine.Access, sim.Engine.Trade,
                                                             q, sim.Engine.SegmentPresence, p);
            var exList = w.Parcels.Where(x => x.State == ParcelState.Built && x.Zoned == ZoneKind.Extractor
                                              && w.Clusters[x.Cluster].ResourceSuitability.Max() > 0.05).ToList();
            Console.WriteLine($"-- probe A: geology response (candidates={exList.Count}) --");
            if (exList.Count > 0)
            {
                var ex = exList.OrderByDescending(x => w.Clusters[x.Cluster].ResourceSuitability.Max()).First();
                var ci = w.Clusters[ex.Cluster];
                var saved = (double[])ci.ResourceSuitability.Clone();
                for (int i = 0; i < ci.ResourceSuitability.Length; i++) ci.ResourceSuitability[i] = 1.0;
                Reassess(ex); double hi = ex.AssessedLR;
                for (int i = 0; i < ci.ResourceSuitability.Length; i++) ci.ResourceSuitability[i] = 0.01;
                Reassess(ex); double lo = ex.AssessedLR;
                Array.Copy(saved, ci.ResourceSuitability, saved.Length);
                Reassess(ex);
                Console.WriteLine($"  parcel {ex.Id} L{ex.Level} cond={ex.Condition:F2}: " +
                                  $"LR(suit=1.0)={hi:F3} LR(suit=0.01)={lo:F3} LR(own)={ex.AssessedLR:F3}");
            }

            Console.WriteLine("-- probe B: admitting a second seller to a one-seller cell --");
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.OccupantFirm < 0) continue;
                var f = w.Firms[pl.OccupantFirm];
                if (f.Dead || (f.Sector != ZoneKind.Industrial && f.Sector != ZoneKind.Extractor)) continue;
                if (sim.Engine.Trade.SellersAt(f.Output, pl.Cluster) != 1) continue;
                Reassess(pl); double before = pl.AssessedLR;
                var ghost = new Firm { Id = w.Firms.Count, Sector = f.Sector, Parcel = pl.Id, Output = f.Output };
                w.Firms.Add(ghost);
                sim.Engine.Trade.Refresh(p);
                Reassess(pl); double after = pl.AssessedLR;
                w.Firms.RemoveAt(w.Firms.Count - 1);
                sim.Engine.Trade.Refresh(p);
                Reassess(pl);
                Console.WriteLine($"  parcel {pl.Id} out={f.Output} cl={pl.Cluster}: LR {before:F3} -> {after:F3} " +
                                  $"(originStat={sim.Engine.Trade.OriginStat(f.Output, pl.Cluster):F3} " +
                                  $"cityOrigin={sim.Engine.Trade.CityOrigin(f.Output):F3} " +
                                  $"export={sim.Engine.Trade.BestExportNet(f.Output, pl.Cluster):F3})");
            }

            Console.WriteLine("-- probe C: realized office product vs the base it is assessed on --");
            var ratios = new List<double>();
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Office || f.WorkersFilled <= 0) continue;
                var pl = w.Parcels[f.Parcel];
                double base_ = f.WorkersFilled * p.OfficeOutputPerSlot * p.OfficeOutputPrice
                               * sim.Engine.Access.OfficeAgglomMult[pl.Cluster];
                if (base_ > 1e-9) ratios.Add(f.ProfitEma / base_);
            }
            ratios.Sort();
            Console.WriteLine($"  alive office firms with staff = {ratios.Count} " +
                              $"median realized/base = {(ratios.Count > 0 ? Pct(ratios, 0.5) : 0):F3} " +
                              $"p10={(ratios.Count > 0 ? Pct(ratios, 0.1) : 0):F3} " +
                              $"p90={(ratios.Count > 0 ? Pct(ratios, 0.9) : 0):F3}");

            // -- owner disposition on firm-occupied land.
            int firmParcels = 0, firmOwned = 0, firmAsk = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.Use == ZoneKind.None) continue;
                firmParcels++;
                if (pl.OwnerHousehold >= 0) firmOwned++;
                if (pl.OwnerAskPerUnit > 0) firmAsk++;
            }
            Console.WriteLine("-- owner doors on non-residential land --");
            Console.WriteLine($"  built non-residential parcels = {firmParcels} " +
                              $"with OwnerHousehold >= 0 = {firmOwned} with an ask = {firmAsk}");

            // -- the go-live ramp: households interpolate onto the market
            // assessment over a staggered window; firms are billed the market
            // assessment from the first levying tick.
            Console.WriteLine($"-- go-live ramp -- GoLiveRampTicks={p.GoLiveRampTicks} " +
                              "(households staggered; firm path has no ramp term)");
            return 0;
        }

        private static double Pct(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return 0;
            double idx = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
        }

        /// <summary>`harness laborprobe` — the labor arm's outside-worker share,
        /// and whether it is a CHOICE or a RATION. The fingerprint's labor lane
        /// reports the share and nothing about its cause; LaborAuction.Why
        /// labels every unmatched worker `Outside` whenever its outside net beats
        /// its own leisure reservation, so `unempShare = 0` is a statement about
        /// the leisure floor and not about whether city work was available.
        ///
        /// The decisive number is door CAPACITY against worker count: where
        /// capacity is short, the outside share is bounded below by the shortfall
        /// whatever the outside wage is, and moving OutsideWageMult cannot fix
        /// it. Printed alongside the border commute the outside option is netted
        /// of, so "the border is two minutes away" is measured rather than
        /// assumed.</summary>
        public static int LaborProbe(ulong seed, int cols, int rows, int households, int ticks,
                                     double outsideMult, double commuteCost)
        {
            var p = new EconParams();
            if (outsideMult >= 0) p.OutsideWageMult = outsideMult;
            if (commuteCost >= 0) p.CommuteCostPerMinute = commuteCost;
            var cfg = new SyntheticCity.Config
            { Cols = cols, Rows = rows, SeedHouseholds = households, Seed = seed };
            var sim = new Sim { P = p, Flags = new FeatureFlags { HousingAuction = true, LaborAuction = true } };
            (sim.W, sim.Access) = SyntheticCity.Build(cfg, p);
            sim.Engine = new EconomyEngine(sim.W, sim.Access, p, sim.Flags);
            sim.Run(ticks);
            var w = sim.W; var a = sim.Engine.Labor;

            double demanded = 0, matched = 0, outside = 0, unemp = 0;
            var slackNoDoorBeatsOutside = new List<double>();
            int unmatchedWithShortlist = 0, unmatchedNoShortlist = 0;
            var borderMins = new List<double>();
            double outsideNetSum = 0, reservationSum = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || !a.ActiveWorker(h.Id)) continue;
                int e = a.EarnersOf(h.Id);
                for (int s = 0; s < e; s++)
                {
                    int wk = a.WorkerOf(h.Id, s);
                    demanded += 1;
                    outsideNetSum += a.OutsideNetOf(wk);
                    reservationSum += a.ReservationOf(wk);
                    if (a.Assignment[wk] >= 0) { matched += 1; continue; }
                    if (a.Why[wk] == LaborAuction.Outcome.Outside) outside += 1; else unemp += 1;
                    // What the loser could see: the best door VALUE on its own
                    // shortlist against its own outside option, price-free. A
                    // worker with no door worth more than its default at ANY
                    // comp is a genuine outside CHOICE; one whose best door beats
                    // its default price-free lost on price, i.e. was rationed.
                    double best = double.NegativeInfinity;
                    int n = a.ShortCountOf(wk);
                    for (int q = 0; q < n; q++)
                    {
                        int d = a.ShortDoorOf(wk, q);
                        if (d < 0) continue;
                        best = Math.Max(best, a.ValueOf(wk, d, p));
                    }
                    if (n == 0 || double.IsNegativeInfinity(best)) unmatchedNoShortlist++;
                    else { unmatchedWithShortlist++; slackNoDoorBeatsOutside.Add(best - a.OutsideOf(wk)); }
                }
                if (h.HomeParcel >= 0) borderMins.Add(a.BorderMinutesOf(w.Parcels[h.HomeParcel].Cluster));
            }
            borderMins.Sort(); slackNoDoorBeatsOutside.Sort();

            double capSum = 0, usedSum = 0, tSum = 0, capComp = 0;
            for (int d = 0; d < a.D; d++)
            {
                capSum += a.Capacity[d];                  // slots the door OFFERS this solve
                usedSum += a.Used[d];
                tSum += a.CompMember(d) * a.Used[d];
                capComp += a.Cap[d] * a.Used[d];
            }

            double slotsBuilt = 0, slotsFilled = 0, firmMoney = 0;
            int liveFirms = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                liveFirms++;
                slotsBuilt += f.JobSlots;
                for (int cl = 0; cl < 3; cl++) slotsFilled += f.FilledByClass[cl];
                firmMoney += f.Money;
            }

            int pop = 0; double hhMoney = 0;
            foreach (var h in w.Households) if (h.ExitedTick < 0) { pop++; hhMoney += h.Money; }

            Console.WriteLine($"laborprobe seed {seed} {cols}×{rows} hh={households} t={ticks} "
                + $"OutsideWageMult={p.OutsideWageMult:F2} CommuteCostPerMinute={p.CommuteCostPerMinute:F3}");
            Console.WriteLine($"  pop {pop}  workers {demanded:F0}  doors {a.D}  door slots {capSum:F0} "
                + $"({capSum / Math.Max(1, demanded):P0} of workers)  taken {usedSum:F0}");
            Console.WriteLine($"  empRate {(demanded > 0 ? (matched + outside) / demanded : 0):F4}  "
                + $"outsideShare {(demanded > 0 ? outside / demanded : 0):F4}  "
                + $"unempShare {(demanded > 0 ? unemp / demanded : 0):F4}  "
                + $"meanToverCap {(capComp > 0 ? tSum / capComp : 0):F4}");
            Console.WriteLine($"  border minutes over worker homes: min {Pct(borderMins, 0):F1} "
                + $"p50 {Pct(borderMins, 0.5):F1} p90 {Pct(borderMins, 0.9):F1} max {Pct(borderMins, 1):F1}; "
                + $"mean outsideNet {(demanded > 0 ? outsideNetSum / demanded : 0):F3} "
                + $"vs mean leisure reservation {(demanded > 0 ? reservationSum / demanded : 0):F3}");
            Console.WriteLine($"  unmatched: {unmatchedWithShortlist} had a shortlisted door, "
                + $"{unmatchedNoShortlist} had none; best-door value minus own default over the first group: "
                + $"p10 {Pct(slackNoDoorBeatsOutside, 0.1):F3} p50 {Pct(slackNoDoorBeatsOutside, 0.5):F3} "
                + $"p90 {Pct(slackNoDoorBeatsOutside, 0.9):F3} (>0 = rationed, not chosen)");
            Console.WriteLine($"  firms {liveFirms}: slots {slotsBuilt:F0} filled {slotsFilled:F0} "
                + $"({slotsFilled / Math.Max(1, slotsBuilt):P0}); firmMoney {firmMoney:F0}; hhMoney {hhMoney:F0}; "
                + $"treasury {w.Ledger.Balance(Account.Treasury):F0}");
            Console.WriteLine($"  payroll this tick: firms {sim.Engine.LaborFirmDebitsThisTick:F1} vs "
                + $"OutsideWorld {sim.Engine.LaborOutsideCreditsThisTick:F1} "
                + $"({sim.Engine.LaborOutsideCreditsThisTick / Math.Max(1e-9, sim.Engine.LaborFirmDebitsThisTick + sim.Engine.LaborOutsideCreditsThisTick):P0} "
                + $"of wage income paid from outside the region)");
            return 0;
        }

        /// <summary>`harness weberprobe` — what sets the number of DISTINCT
        /// industrial outputs on the Weber fixture, which is the leg seed 5
        /// fails while holding the Weber property perfectly (100% extraction,
        /// 100% recipe alignment).
        ///
        /// For each seed it prints the realized output census and then the
        /// decision every entrant actually faces: LandAccounting.FirmBidPerSlot's
        /// industrial leg is argmax over recipes of
        /// `OutputPerSlot × RecipeOutputScale × quality × (outNet − inputCost) − wage`,
        /// so the same expression is evaluated here at EVERY cluster. If one
        /// recipe is the argmax at every cluster, a second output exists only
        /// if some entrant chooses against its own margin — and the runner-up
        /// gap says by how much.</summary>
        public static int WeberProbe(ulong start, int n, int households)
        {
            Console.WriteLine($"weberprobe: seeds {start}..{start + (ulong)n - 1}, "
                + $"{households} seed households (Weber industrial fixture, 300 ticks)");
            for (ulong seed = start; seed < start + (ulong)n; seed++)
            {
                var p = new EconParams();
                var sim = Sim.Create(new SyntheticCity.Config { Seed = seed, SeedHouseholds = households },
                                     p, new FeatureFlags());
                sim.Run(300);
                var w = sim.W; var tr = sim.Engine.Trade;

                var byOutput = new Dictionary<Res, int>();
                int firms = 0;
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Industrial) continue;
                    firms++;
                    byOutput[f.Output] = byOutput.TryGetValue(f.Output, out var q) ? q + 1 : 1;
                }

                // The entrant's own argmax at every cluster, at level-1 quality
                // (entry level). wage matches FirmBidPerSlot's industrial leg.
                double wage = 0.6 * p.WageBasic + 0.4 * p.WageSkilled;
                var argmaxCount = new Dictionary<Res, int>();
                int viable = 0; double worstGap = double.PositiveInfinity; int worstAt = -1;
                Res worstAlt = Res.Services;
                var gaps = new List<double>();
                for (int c = 0; c < sim.Engine.Access.C; c++)
                {
                    double best = double.NegativeInfinity, second = double.NegativeInfinity;
                    Res bestRes = Res.Services, secondRes = Res.Services;
                    foreach (var recipe in ResourceCatalog.Recipes)
                    {
                        double outNet = Math.Max(tr.OriginStat(recipe.Output, c), tr.BestExportNet(recipe.Output, c));
                        double inputCost = 0;
                        foreach (var (res, qty) in recipe.Inputs) inputCost += qty * tr.DeliveredCost(res, c);
                        double perSlot = recipe.OutputPerSlot * p.RecipeOutputScale * (outNet - inputCost) - wage;
                        if (perSlot > best) { second = best; secondRes = bestRes; best = perSlot; bestRes = recipe.Output; }
                        else if (perSlot > second) { second = perSlot; secondRes = recipe.Output; }
                    }
                    argmaxCount[bestRes] = argmaxCount.TryGetValue(bestRes, out var q2) ? q2 + 1 : 1;
                    if (best > 0)
                    {
                        viable++;
                        double gap = best - second;
                        gaps.Add(gap);
                        if (gap < worstGap) { worstGap = gap; worstAt = c; worstAlt = secondRes; }
                    }
                }
                gaps.Sort();
                string census = string.Join(" ", byOutput.OrderByDescending(kv => kv.Value)
                                                        .Select(kv => $"{kv.Key}×{kv.Value}"));
                string argm = string.Join(" ", argmaxCount.OrderByDescending(kv => kv.Value)
                                                          .Select(kv => $"{kv.Key}×{kv.Value}"));
                Console.WriteLine($"seed {seed,3}: {firms,3} industrial firms, {byOutput.Count} distinct outputs [{census}]");
                Console.WriteLine($"           entrant argmax over {sim.Engine.Access.C} clusters: "
                    + $"{argmaxCount.Count} distinct [{argm}]; {viable} clusters with positive margin; "
                    + $"runner-up gap p10 {Pct(gaps, 0.1):F3} median {Pct(gaps, 0.5):F3} "
                    + $"min {(worstAt >= 0 ? worstGap : 0):F3} at c{worstAt} (2nd best {worstAlt})");
            }
            return 0;
        }

        /// <summary>Everything ResidentialBidPerUnit reads on the forecast path,
        /// captured so a post-collapse price can be re-read with any single
        /// input held at its pre-collapse value. Deep copies: the engine
        /// overwrites these arrays in place on every refresh.</summary>
        private sealed class PriceInputs
        {
            public double[][][] Ladder = null!;
            public double[][][] Share = null!;
            public double[][] Access = null!;
            public double MeanAccess;
            public double[][] Stock = null!;
            public double[] Presence = null!;

            public static PriceInputs Capture(AccessState a, double[] pres)
            {
                var s = new PriceInputs { MeanAccess = a.MeanAccess, Presence = (double[])pres.Clone() };
                s.Ladder = new double[a.BidLadder.Length][][];
                for (int k = 0; k < a.BidLadder.Length; k++)
                {
                    s.Ladder[k] = new double[a.BidLadder[k].Length][];
                    for (int q = 0; q < a.BidLadder[k].Length; q++)
                        s.Ladder[k][q] = (double[])(a.BidLadder[k][q] ?? Array.Empty<double>()).Clone();
                }
                s.Share = new double[a.SegmentKindShare.Length][][];
                for (int q = 0; q < a.SegmentKindShare.Length; q++)
                {
                    s.Share[q] = new double[a.SegmentKindShare[q].Length][];
                    for (int k = 0; k < a.SegmentKindShare[q].Length; k++)
                        s.Share[q][k] = (double[])(a.SegmentKindShare[q][k] ?? Array.Empty<double>()).Clone();
                }
                s.Access = new double[a.AccessValue.Length][];
                for (int q = 0; q < a.AccessValue.Length; q++)
                    s.Access[q] = (double[])(a.AccessValue[q] ?? Array.Empty<double>()).Clone();
                s.Stock = new double[a.HousingStock.Length][];
                for (int k = 0; k < a.HousingStock.Length; k++)
                    s.Stock[k] = (double[])(a.HousingStock[k] ?? Array.Empty<double>()).Clone();
                return s;
            }
        }

        /// <summary>`harness collapseprobe` — the clearing-price check's
        /// population-collapse leg (TestRunner.ClearingPrice leg (d)), run
        /// alone and decomposed. Same fixture (8×8 / 2500 / 80 ticks), same
        /// cull (60% of households whose home is outside the probed cluster),
        /// same 10-tick settle.
        ///
        /// What it prints beyond the check's own two numbers: the post-collapse
        /// price re-read with each pricing input in turn held at its
        /// pre-collapse value, so a rise can be attributed to the demand COUNT,
        /// the surviving population's bid ladder (composition), the
        /// kind/cluster shares, the access field, or the stock — and the same
        /// price on a REBUILT post ladder, because the check reads `before` off
        /// a ladder it rebuilds itself and `after` off whatever the engine's
        /// last refresh left behind.
        ///
        /// `--owner-ask 0` folds every owner door (item #41); the process-wide
        /// `--mutant-citywide-goods` restores the one-scalar goods price
        /// (item #30). Both are the arms the T3a registry row names.
        /// `--spare-c0 0` culls the probed cluster's own residents too, which is
        /// the arm that says whether the leg's stimulus reaches the quantity
        /// its price inverts.</summary>
        public static int CollapseProbe(ulong seed, double ownerAskScale, bool spareC0 = true)
        {
            var p = new EconParams { OwnerAskScale = ownerAskScale };
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var acc = sim.Engine.Access;
            acc.RebuildHouseholdLadders(sim.W, p);
            var pres = sim.Engine.SegmentPresence;

            int c0 = 0;
            for (int c = 1; c < acc.C; c++)
                if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;

            double Price(AccessState a, double[] pv, out double fill)
                => LandAccounting.ResidentialBidPerUnit(a, c0, ZoneKind.ResidentialLow, 2, pv, p, out fill);

            double before = Price(acc, pres, out double fillBefore);
            var pre = PriceInputs.Capture(acc, pres);

            // Who is in the probed submarket's queue, and how rich. The price
            // is a rung of some segment's ladder times that segment's location
            // multiplier, so the marginal bidder is identified by inverting
            // that: for each segment, the cheapest rung still bidding at or
            // above the clearing price.
            void Composition(string tag, AccessState a, double[] pv, double price)
            {
                double quality = p.Quality(2) / p.Quality(1);
                double bestRung = double.PositiveInfinity; int bestSeg = -1; double massAt = 0;
                for (int s = 0; s < Segment.Count; s++)
                {
                    if (pv[s] < 1) continue;
                    var lad = a.BidLadder[0][s];
                    if (lad == null || lad.Length == 0) continue;
                    double share = a.SegmentKindShare[s][0][c0];
                    if (share <= 0) continue;
                    double rel = a.AccessValue[s][c0] / a.MeanAccess;
                    double premium = MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0);
                    double mult = premium * p.BidAccessScale * quality;
                    if (mult <= 0) continue;
                    double need = price / mult;
                    int cnt = 0;
                    for (int i = 0; i < lad.Length; i++) { if (lad[i] >= need) cnt++; else break; }
                    massAt += cnt * share * (pv[s] / lad.Length);
                    if (cnt > 0 && lad[cnt - 1] < bestRung) { bestRung = lad[cnt - 1]; bestSeg = s; }
                }
                Console.WriteLine($"  {tag}: price {price:F4}  marginal rung {(bestSeg >= 0 ? bestRung : 0):F4} "
                    + $"(segment {bestSeg})  mass at price {massAt:F1}  stock {a.HousingStock[0][c0]:F1}");
            }
            Composition("pre ", acc, pres, before);

            int pop0 = 0, inC0 = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                pop0++;
                if (h.HomeParcel >= 0 && sim.W.Parcels[h.HomeParcel].Cluster == c0) inC0++;
            }

            int killed = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if (spareC0 && sim.W.Parcels[h.HomeParcel].Cluster == c0) continue;
                if (SplitMix64.Hash01((ulong)h.Id * 977 + 5) < 0.6)
                { h.ExitedTick = sim.W.Tick; killed++; }
            }
            sim.Run(10);

            var acc2 = sim.Engine.Access;
            var pres2 = sim.Engine.SegmentPresence;
            double after = Price(acc2, pres2, out double fillAfter);      // exactly what the check reads
            Composition("post", acc2, pres2, after);

            int pop1 = 0, inC0After = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                pop1++;
                if (h.HomeParcel >= 0 && sim.W.Parcels[h.HomeParcel].Cluster == c0) inC0After++;
            }

            // PAIRED arm: the check builds `before` on a ladder it rebuilds and
            // `after` on the engine's, which Access.Refresh writes at the TOP of
            // RefreshTick — one refresh behind the benefit cliff. Leg (c) was
            // paired for exactly this; leg (d) was not.
            var stale = PriceInputs.Capture(acc2, pres2);
            acc2.RebuildHouseholdLadders(sim.W, p);
            double afterPaired = Price(acc2, pres2, out double fillPaired);
            var post = PriceInputs.Capture(acc2, pres2);

            // One input at a time, held at its PRE value on the paired post
            // state: the gap each input is responsible for.
            double Hold(string which)
            {
                var savedL = acc2.BidLadder; var savedS = acc2.SegmentKindShare;
                var savedA = acc2.AccessValue; double savedM = acc2.MeanAccess;
                var savedH = acc2.HousingStock; var pv = post.Presence;
                if (which == "ladder") acc2.BidLadder = pre.Ladder;
                if (which == "share") acc2.SegmentKindShare = pre.Share;
                if (which == "access") { acc2.AccessValue = pre.Access; acc2.MeanAccess = pre.MeanAccess; }
                if (which == "stock") acc2.HousingStock = pre.Stock;
                if (which == "presence") pv = pre.Presence;
                // share × presence is the RAW BID COUNT at this cluster
                // (LandAccounting divides the share by the ladder length and
                // multiplies by presence), so holding either one alone breaks
                // an identity the pricing path relies on. "count" holds both.
                if (which == "count") { acc2.SegmentKindShare = pre.Share; pv = pre.Presence; }
                double v = Price(acc2, pv, out _);
                acc2.BidLadder = savedL; acc2.SegmentKindShare = savedS;
                acc2.AccessValue = savedA; acc2.MeanAccess = savedM; acc2.HousingStock = savedH;
                return v;
            }

            Console.WriteLine($"collapseprobe seed {seed} OwnerAskScale={ownerAskScale} "
                + $"citywideGoodsMutant={TradeSystem.MutantCitywideGoodsPrice} spareC0={spareC0}");
            Console.WriteLine($"  cluster {c0}: pop {pop0} → {pop1} ({killed} culled), residents of c0 {inC0} → {inC0After}");
            Console.WriteLine($"  CHECK READS  before {before:F4} (fill {fillBefore:F2}) → after {after:F4} "
                + $"(fill {fillAfter:F2})  ratio {after / Math.Max(1e-12, before):F4}  "
                + $"softens={(after < before * 0.98 || (fillAfter < fillBefore * 0.8 && after < before * 1.20))}");
            Console.WriteLine($"  PAIRED       before {before:F4} → afterPaired {afterPaired:F4} (fill {fillPaired:F2})  "
                + $"ratio {afterPaired / Math.Max(1e-12, before):F4}  "
                + $"[stale-ladder wedge {after - afterPaired:+0.0000;-0.0000}]");
            Console.WriteLine($"  HOLD ONE PRE-COLLAPSE (paired post otherwise): "
                + $"ladder {Hold("ladder"):F4}  share {Hold("share"):F4}  access {Hold("access"):F4}  "
                + $"stock {Hold("stock"):F4}  presence {Hold("presence"):F4}  "
                + $"count(share×presence) {Hold("count"):F4}  none {afterPaired:F4}");
            // The quantity the leg believes it is collapsing: how many
            // households actually bid at the probed submarket. share × presence
            // is that count, and it is what the price inverts against supply.
            double bidsPre = 0, bidsPost = 0;
            for (int s = 0; s < Segment.Count; s++)
            {
                bidsPre += pre.Share[s][0][c0] * pre.Presence[s];
                bidsPost += post.Share[s][0][c0] * post.Presence[s];
            }
            Console.WriteLine($"  BIDS AT c0 (low): {bidsPre:F2} → {bidsPost:F2} "
                + $"({bidsPost / Math.Max(1e-12, bidsPre):F3}×) against stock {pre.Stock[0][c0]:F1} → {post.Stock[0][c0]:F1}");
            for (int s = 0; s < Segment.Count; s++)
            {
                var lp = pre.Ladder[0][s]; var lq = post.Ladder[0][s];
                if (lp.Length == 0 && lq.Length == 0) continue;
                double MeanOf(double[] x) { double t = 0; foreach (var v in x) t += v; return x.Length > 0 ? t / x.Length : 0; }
                double TailOf(double[] x) => x.Length > 0 ? x[Math.Max(0, x.Length - 1 - x.Length / 200)] : 0;
                Console.WriteLine($"    seg {s,2} {Segment.All[s].Name,-12} n {lp.Length,5} → {lq.Length,5}  "
                    + $"mean rung {MeanOf(lp):F3} → {MeanOf(lq):F3}  bottom-0.5% rung {TailOf(lp):F3} → {TailOf(lq):F3}  "
                    + $"share(c0,low) {pre.Share[s][0][c0]:F5} → {post.Share[s][0][c0]:F5}  "
                    + $"access(c0) {pre.Access[s][c0]:F3} → {post.Access[s][c0]:F3}");
            }
            Console.WriteLine($"  meanAccess {pre.MeanAccess:F4} → {post.MeanAccess:F4}; "
                + $"stock(c0,low) {pre.Stock[0][c0]:F2} → {post.Stock[0][c0]:F2}; "
                + $"stale ladder n(seg0) {stale.Ladder[0][0].Length} vs rebuilt {post.Ladder[0][0].Length}");
            return 0;
        }

        /// <summary>The exit margin's census. Prints, per sector, how many
        /// firms are under water on their OWN cash-flow read, how long they
        /// have been, what the two exit routes actually did, and what became of
        /// the sites they left.
        ///
        /// The last of those is the point. The existing zombie metric
        /// (FirmDiag: EMA(revenue) &lt; wage bill, commercial only) reads 0 % on
        /// every seed, which says the shops are fine and says nothing about the
        /// sector the arrears measurement actually indicts. This one asks the
        /// Dixit question — revenue against AVOIDABLE cost, which is wages plus
        /// inputs plus the land charge OWED — for every sector, and it reports
        /// re-letting beside exits, because an exit into a market that cannot
        /// re-let the building is not a fix.</summary>
        /// <summary>THE FAIR-FIGHT TEST. A parcel's land value is a maximum
        /// over the configurations its zoning permits; that maximum is only
        /// meaningful if the candidates are COMMENSURABLE. This puts all four
        /// non-residential sectors on the SAME parcel -- same cluster, same
        /// level, same condition, each given the entrant mass its own entry
        /// path would give it -- and reports who wins and by how much.
        ///
        /// The diagnostic is the LEVEL PROFILE. If the sectors price a slot on
        /// different terms, the winner is decided by which formula each is
        /// wired to rather than by what the site is worth to each use, and the
        /// tell is that the winner distribution swings systematically with
        /// LEVEL rather than with location. A regime where one pair of sectors
        /// sweeps every high-level plot and the other pair sweeps every
        /// low-level one is the signature of a term asymmetry, not of
        /// geography.</summary>
        public static int ZoneFight(ulong seed, int ticks)
        {
            var sim = Sim.Create(new SyntheticCity.Config
                                 { Seed = seed, Cols = 12, Rows = 12, SeedHouseholds = 6000 },
                                 new EconParams(), new FeatureFlags());
            var p = sim.P; var w = sim.W;
            sim.Run(ticks);

            var sectors = new[] { ZoneKind.Commercial, ZoneKind.Industrial,
                                  ZoneKind.Office, ZoneKind.Extractor };
            Console.WriteLine($"seed {seed} ticks {ticks} "
                + $"uniform={p.UniformSiteProductivity} parity={p.NonResLandParity} "
                + $"assessDeliverable={p.AssessDeliverableQuality}");
            Console.WriteLine("-- all four sectors bidding for the SAME parcel --");

            // winner counts by level, and the four sectors' mean bid by level.
            var winByLevel = new Dictionary<int, int[]>();
            var sumByLevel = new Dictionary<int, double[]>();
            var nByLevel = new Dictionary<int, int>();
            int scored = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.Use == ZoneKind.None) continue;
                if (pl.Warehousing) continue;
                var bids = new double[4];
                for (int k = 0; k < 4; k++)
                {
                    // Each sector gets EXACTLY the entrant read its own entry
                    // path uses, so this compares the sectors as the engine
                    // would actually ask them -- not a tidied-up version.
                    double em = sectors[k] == ZoneKind.Commercial
                        ? pl.Units * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) : 0;
                    double b = LandAccounting.FirmBidPerSlot(
                        sim.Engine.Access, sim.Engine.Trade, pl.Cluster, sectors[k], pl.Level, p,
                        out _, w.Clusters, out bool condPriced,
                        em, em > 0 ? pl.Units : 0, em > 0 ? pl.Condition : 0);
                    if (!condPriced) b *= p.CondFactor(pl.Condition);
                    bids[k] = b;
                }
                int win = 0;
                for (int k = 1; k < 4; k++) if (bids[k] > bids[win]) win = k;
                if (!winByLevel.ContainsKey(pl.Level))
                { winByLevel[pl.Level] = new int[4]; sumByLevel[pl.Level] = new double[4]; nByLevel[pl.Level] = 0; }
                winByLevel[pl.Level][win]++;
                for (int k = 0; k < 4; k++) sumByLevel[pl.Level][k] += bids[k];
                nByLevel[pl.Level]++;
                scored++;
            }
            var levels = new List<int>(winByLevel.Keys); levels.Sort();
            Console.WriteLine($"  {scored} parcels scored, all four sectors priced at each");
            Console.WriteLine($"  {"level",-6}{"n",-5}| winners: com / ind / off / ext "
                + "| mean bid: com / ind / off / ext");
            foreach (int lv in levels)
            {
                var wl = winByLevel[lv]; var sl = sumByLevel[lv]; int n = nByLevel[lv];
                Console.WriteLine($"  L{lv,-5}{n,-5}| {wl[0],4} {wl[1],4} {wl[2],4} {wl[3],4}"
                    + $"        | {sl[0] / n,7:F3} {sl[1] / n,7:F3} {sl[2] / n,7:F3} {sl[3] / n,7:F3}");
            }
            // The summary statistic: does any sector win NOTHING, and does the
            // winner set change with level? Both are reported rather than
            // asserted -- this is a probe, and the check that asserts on it
            // lives in the suite.
            var totals = new int[4];
            foreach (int lv in levels) for (int k = 0; k < 4; k++) totals[k] += winByLevel[lv][k];
            int shutOut = 0;
            for (int k = 0; k < 4; k++) if (totals[k] == 0) shutOut++;
            Console.WriteLine($"  totals: com={totals[0]} ind={totals[1]} off={totals[2]} ext={totals[3]}"
                + $" | sectors that win NOTHING anywhere: {shutOut}/4");
            return 0;
        }

        public static int MarginProbe(ulong seed, int ticks,
                                      double recipeScale = -1, double extractSlot = -1,
                                      double condBidFloor = -1)
        {
            var p = new EconParams();
            if (recipeScale > 0) p.RecipeOutputScale = recipeScale;
            if (extractSlot > 0) p.ExtractorOutputPerSlot = extractSlot;
            if (condBidFloor >= 0) p.CondBidFloor = condBidFloor;
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;

            // SUCCESSION CENSUS. "Does the business of a plot ever switch?"
            // is a question about TURNOVER, not about a living firm's product
            // (which is fixed at entry, deliberately). A firm dies, the
            // building stands, an entrant takes the standing building and
            // re-decides everything from today's prices — recipe, office
            // kind, retail line. This tracks every occupant handover on a
            // parcel and asks whether the successor is a DIFFERENT business:
            // different output for industrial/extractor, different
            // specialization for office, different line for commercial.
            var lastOcc = new Dictionary<int, int>();          // parcel -> firm id
            var succN = new Dictionary<ZoneKind, int>();
            var succChanged = new Dictionary<ZoneKind, int>();
            var vacGap = new List<int>();                      // ticks the site sat empty
            var vacSince = new Dictionary<int, long>();        // parcel -> tick it fell vacant
            sim.Run(ticks, s2 =>
            {
                foreach (var pl in s2.W.Parcels)
                {
                    if (pl.IsResidential || pl.Use == ZoneKind.None || pl.Warehousing) continue;
                    int cur = pl.State == ParcelState.Built ? pl.OccupantFirm : -1;
                    lastOcc.TryGetValue(pl.Id, out int prev0);
                    int prev = lastOcc.ContainsKey(pl.Id) ? prev0 : -1;
                    if (cur < 0 && prev >= 0 && !vacSince.ContainsKey(pl.Id))
                        vacSince[pl.Id] = s2.W.Tick;
                    if (cur >= 0 && prev >= 0 && cur != prev)
                    {
                        var a = s2.W.Firms[prev]; var b = s2.W.Firms[cur];
                        succN.TryGetValue(b.Sector, out int n0); succN[b.Sector] = n0 + 1;
                        bool changed = b.Sector switch
                        {
                            ZoneKind.Industrial => a.Output != b.Output,
                            ZoneKind.Extractor => a.Output != b.Output,
                            ZoneKind.Office => a.Office != b.Office,
                            ZoneKind.Commercial => a.Retail != b.Retail,
                            _ => false,
                        };
                        if (changed)
                        { succChanged.TryGetValue(b.Sector, out int c0); succChanged[b.Sector] = c0 + 1; }
                        if (vacSince.TryGetValue(pl.Id, out long t0))
                        { vacGap.Add((int)(s2.W.Tick - t0)); vacSince.Remove(pl.Id); }
                    }
                    if (cur >= 0) { lastOcc[pl.Id] = cur; vacSince.Remove(pl.Id); }
                    else if (prev >= 0) lastOcc[pl.Id] = prev;   // remember through the gap
                }
            });
            {
                // CAPTURE RATE: the share of household spending that reaches a
                // city shop rather than leaking out of town. This is the honest
                // measure of whether commercial demand is SERVED, and it is the
                // one the pooled-vs-store-level decision rule never had. ALIVE
                // and VACANCY both flatter the pooled path by construction --
                // it pays every standing shop a slots-share whether or not a
                // household would walk in, so more shops exist and they fill
                // more buildings. Neither says whether anyone was served.
                double capSum = 0, leakSum = 0;
                for (int t = 0; t < 40; t++)
                {
                    sim.Step();
                    capSum += sim.Engine.ConsumptionCapturedThisTick;
                    leakSum += sim.Engine.ConsumptionLeakedThisTick;
                }
                Console.WriteLine($"  consumption over 40 further ticks: captured {capSum:F0}, "
                    + $"leaked {leakSum:F0} => capture rate "
                    + $"{(capSum + leakSum > 0 ? 100.0 * capSum / (capSum + leakSum) : 0):F1} %");
                int totS = 0, totC = 0;
                var parts = new List<string>();
                foreach (var kv in succN)
                {
                    succChanged.TryGetValue(kv.Key, out int ch);
                    totS += kv.Value; totC += ch;
                    parts.Add($"{kv.Key} {ch}/{kv.Value}");
                }
                vacGap.Sort();
                Console.WriteLine($"  successions (a NEW firm took a previously-occupied building): "
                    + $"{totS} total, {totC} changed business ({(totS > 0 ? 100.0 * totC / totS : 0):F0} %) "
                    + $"| by sector (changed/total): {string.Join(", ", parts)}"
                    + (vacGap.Count > 0
                       ? $" | vacancy gap ticks p50={vacGap[vacGap.Count / 2]} max={vacGap[vacGap.Count - 1]}"
                       : ""));
            }

            Console.WriteLine($"seed {seed} ticks {ticks} exitMargin={sim.Flags.FirmExitMargin} "
                + $"noMargin={EconomyEngine.MutantFirmExitNoMargin} flatPatience={EconomyEngine.MutantFirmFlatPatience}"
                + (recipeScale > 0 || extractSlot > 0 || condBidFloor >= 0
                   ? $" | OVERRIDES recipeScale={p.RecipeOutputScale} extractSlot={p.ExtractorOutputPerSlot} condBidFloor={p.CondBidFloor}"
                   : ""));
            var sectors = new[] { ZoneKind.Commercial, ZoneKind.Industrial, ZoneKind.Office, ZoneKind.Extractor };
            foreach (var s in sectors)
            {
                int alive = 0, under = 0, deep = 0;
                double cfSum = 0;
                var cfs = new List<double>();
                // STAFFING CENSUS, split by solvency. The cash-flow percentiles
                // alone cannot tell an idle shell from a working firm that is
                // narrowly losing, and the two want opposite verdicts from the
                // exit margin: the shell should go, the working firm should be
                // given room. Measured need — the band round found extractors
                // sitting at exactly −0.86 with a dispersion of ~0, which is
                // the signature of a firm whose costs and revenue are both
                // constant, i.e. one that is producing nothing.
                double uFilled = 0, uSlots = 0, sFilled = 0, sSlots = 0;
                int uIdle = 0, uN = 0, sIdle = 0, sN = 0;
                var mads = new List<double>();
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != s) continue;
                    alive++;
                    cfSum += f.CashFlowEma; cfs.Add(f.CashFlowEma);
                    mads.Add(f.CashFlowMadEma);
                    bool wet = f.CashFlowObserved && f.CashFlowEma < 0;
                    if (wet) under++;
                    if (f.CashFlowShortTicks >= p.InsolvencyGraceTicks) deep++;
                    double filled = f.FilledByClass[0] + f.FilledByClass[1] + f.FilledByClass[2];
                    if (wet) { uN++; uFilled += filled; uSlots += f.JobSlots; if (filled <= 1e-9) uIdle++; }
                    else { sN++; sFilled += filled; sSlots += f.JobSlots; if (filled <= 1e-9) sIdle++; }
                }
                cfs.Sort(); mads.Sort();
                int dCash = 0, dArr = 0, dCap = 0, dDisp = 0;
                foreach (var f in w.Firms)
                {
                    if (!f.Dead || f.Sector != s) continue;
                    if (f.DiedOfCashFlow) dCash++;
                    else if (f.DiedOfArrears) dArr++;
                    else if (f.DiedOfWorkingCapital) dCap++;
                    else if (f.DiedOfDisplacement) dDisp++;
                }
                Console.WriteLine($"  {s,-11} alive={alive,4} underwater={under,4} "
                    + $"({(alive > 0 ? 100.0 * under / alive : 0):F1} %) pastGrace={deep,4} "
                    + $"| cashflow p10={(cfs.Count > 0 ? Pct(cfs, 0.1) : 0),9:F2} "
                    + $"p50={(cfs.Count > 0 ? Pct(cfs, 0.5) : 0),9:F2} "
                    + $"p90={(cfs.Count > 0 ? Pct(cfs, 0.9) : 0),9:F2} "
                    + $"| dead: margin={dCash} arrears={dArr} capital={dCap} displaced={dDisp}");
                if (alive > 0)
                    Console.WriteLine($"  {"",-11}   underwater: n={uN,4} staffed={uFilled,7:F1}/{uSlots,-7:F1} "
                        + $"idle={uIdle,4} | solvent: n={sN,4} staffed={sFilled,7:F1}/{sSlots,-7:F1} idle={sIdle,4} "
                        + $"| cashflow MAD p50={(mads.Count > 0 ? Pct(mads, 0.5) : 0):F2}");
            }
            // Does the specialization actually vary? A maximum over kinds that
            // always names the same kind is the pooled value wearing a loop,
            // and two earlier cuts of this mechanism measured exactly that.
            var kindLive = new int[AccessState.OfficeKindCount];
            var kindAll = new int[AccessState.OfficeKindCount];
            foreach (var f in w.Firms)
            {
                if (f.Sector != ZoneKind.Office) continue;
                kindAll[(int)f.Office]++;
                if (!f.Dead && f.Parcel >= 0) kindLive[(int)f.Office]++;
            }
            // Same question for retail: a maximum over lines that always names
            // the same line is one company type wearing four names, which is
            // exactly what basket share alone would produce.
            var retailLive = new Dictionary<Res, int>();
            var retailAll = new Dictionary<Res, int>();
            foreach (var f in w.Firms)
            {
                if (f.Sector != ZoneKind.Commercial) continue;
                retailAll.TryGetValue(f.Retail, out int a); retailAll[f.Retail] = a + 1;
                if (!f.Dead && f.Parcel >= 0)
                { retailLive.TryGetValue(f.Retail, out int l); retailLive[f.Retail] = l + 1; }
            }
            var rparts = new List<string>();
            foreach (var kv in retailAll)
                rparts.Add($"{kv.Key} {(retailLive.TryGetValue(kv.Key, out int lv) ? lv : 0)}/{kv.Value}");
            Console.WriteLine($"  retail lines (live / ever): {string.Join(", ", rparts)}");

            Console.WriteLine($"  office kinds (live / ever): "
                + $"software {kindLive[0]}/{kindAll[0]}, financial {kindLive[1]}/{kindAll[1]}, "
                + $"media {kindLive[2]}/{kindAll[2]}");
            // The increasing-returns signature, measured rather than argued:
            // OfficeAgglomMult at the clusters live offices actually sit in.
            // If the 10-firm arm carries a visibly larger multiplier than the
            // 1-firm arm, thinning entry attacks office through this term and
            // through nothing the constant-returns sectors have.
            //
            // (It ran, and the multiplier CANNOT be the mechanism: 1.003 at
            // 1 firm, 1.010 at 5, 1.019 at 10 — a ~1.6 % output term against
            // a ±580/tick cash-flow swing. The per-firm anatomy below exists
            // because that refutation left "then what is?" unanswered.)
            {
                var aggs = new List<double>();
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Office) continue;
                    aggs.Add(sim.Engine.Access.OfficeAgglomMult[w.Parcels[f.Parcel].Cluster]);
                }
                aggs.Sort();
                if (aggs.Count > 0)
                    Console.WriteLine($"  office agglom mult at live sites: n={aggs.Count} "
                        + $"p10={Pct(aggs, 0.1):F3} p50={Pct(aggs, 0.5):F3} p90={Pct(aggs, 0.9):F3}");
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Office) continue;
                    var pl = w.Parcels[f.Parcel];
                    // BILLED payroll, not FilledByClass x Wage(class): the
                    // latter is the POSTED class wage and the auction bills
                    // cleared base comp (Firm.PayrollThisTick carries why).
                    double notional = 0;
                    for (int cl = 0; cl < 3; cl++)
                        notional += f.FilledByClass[cl] * p.Wage((LaborClass)cl);
                    double landBill = LandAccounting.UnitAssessment(pl, p) * pl.Units;
                    Console.WriteLine($"    office {f.Id} L{pl.Level} cl={pl.Cluster} cond={pl.Condition:F2} "
                        + $"staff={f.WorkersFilled}/{f.JobSlots} revEma={f.ProfitEma,8:F2} "
                        + $"payroll={f.PayrollLastTick,7:F2} (posted {notional,6:F0}) "
                        + $"landBill={landBill,7:F2} cashEma={f.CashFlowEma,8:F2}");
                }
            }

            int built = 0, vacant = 0, shell = 0;
            var vacCond = new List<double>();
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.Use == ZoneKind.None) continue;
                built++;
                if (pl.OccupantFirm < 0) { vacant++; vacCond.Add(pl.Condition); continue; }
                // A SHELL is a parcel whose firm employs nobody. Counting it as
                // occupied is what made the exit margin look destructive: on
                // the labor arm without the margin, seed 1 read 13.6 % vacant
                // while 48 further parcels held zero-staff firms. The margin
                // did not create that emptiness, it RETIRED the shells holding
                // it — so the two arms are only comparable on this number.
                var of = w.Firms[pl.OccupantFirm];
                if (!of.Dead
                    && of.FilledByClass[0] + of.FilledByClass[1] + of.FilledByClass[2] <= 1e-9)
                    shell++;
            }
            vacCond.Sort();
            var e = sim.Engine;
            Console.WriteLine($"  sites: {vacant}/{built} built non-res parcels VACANT "
                + $"({(built > 0 ? 100.0 * vacant / built : 0):F1} %), vacant condition "
                + $"p10={(vacCond.Count > 0 ? Pct(vacCond, 0.1) : 0):F2} "
                + $"p50={(vacCond.Count > 0 ? Pct(vacCond, 0.5) : 0):F2} "
                + $"p90={(vacCond.Count > 0 ? Pct(vacCond, 0.9) : 0):F2}");
            Console.WriteLine($"  sites: {shell} further parcels hold a ZERO-STAFF SHELL "
                + $"=> {vacant + shell}/{built} ({(built > 0 ? 100.0 * (vacant + shell) / built : 0):F1} %) "
                + $"EFFECTIVELY IDLE");

            // WHY IS A SHELL A SHELL? A firm staffs nobody when its own door
            // cap is <= 0, because LaborAuction skips such a firm outright ("a
            // door with nothing to pay is not a door") and its slots never
            // reach the market. The cap is a product, so exactly one factor
            // usually kills it — recomputed here from the same inputs the
            // auction reads so the answer names a term rather than a symptom.
            int noSuit = 0, noPrice = 0, noMargin2 = 0, otherIdle = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                if (f.FilledByClass[0] + f.FilledByClass[1] + f.FilledByClass[2] > 1e-9) continue;
                var pl = w.Parcels[f.Parcel];
                if (f.Sector == ZoneKind.Extractor)
                {
                    double suit = w.Clusters[pl.Cluster].ResourceSuitability[(int)f.Output];
                    double px = sim.Engine.Trade.OriginStat(f.Output, pl.Cluster);
                    if (suit <= 1e-9) noSuit++;
                    else if (px <= 1e-9) noPrice++;
                    else otherIdle++;
                }
                else if (f.Sector == ZoneKind.Industrial)
                {
                    var recipe = ResourceCatalog.RecipeFor(f.Output);
                    double m = sim.Engine.Trade.OriginStat(f.Output, pl.Cluster);
                    if (recipe.Inputs != null)
                        foreach (var (res, qty) in recipe.Inputs)
                            m -= qty * sim.Engine.Trade.DeliveredStat(res, pl.Cluster);
                    if (m <= 0) noMargin2++; else otherIdle++;
                }
                else otherIdle++;
            }
            Console.WriteLine($"  idle firms by cause: extractor no-geology={noSuit} "
                + $"extractor no-local-price={noPrice} industrial negative-recipe-margin={noMargin2} "
                + $"other={otherIdle}");
            // DID THE DEVELOPER HAVE THE SIGNAL AND IGNORE IT? FirmFillEstimate
            // floors at 0.35, so a cluster where every door goes unfilled still
            // forecasts a third of a roster. If realized fill at these clusters
            // is near zero, the signal exists and only the floor hides it.
            var idleFill = new List<double>(); var idleEst = new List<double>();
            var liveFill = new List<double>();
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                if (f.Sector == ZoneKind.Commercial) continue;   // shops fill; the question is the other three
                int c = w.Parcels[f.Parcel].Cluster;
                double est = LandAccounting.FirmFillEstimate(sim.Engine.Access, c, f.Sector, p);
                double realized = f.JobSlots > 1e-9
                    ? (f.FilledByClass[0] + f.FilledByClass[1] + f.FilledByClass[2]) / f.JobSlots : 0;
                if (f.FilledByClass[0] + f.FilledByClass[1] + f.FilledByClass[2] <= 1e-9)
                { idleFill.Add(realized); idleEst.Add(est); }
                else liveFill.Add(est);
            }
            idleEst.Sort(); liveFill.Sort();
            if (idleEst.Count > 0)
                Console.WriteLine($"  fill forecast at IDLE non-commercial firms: n={idleEst.Count} "
                    + $"FirmFillEstimate p10={Pct(idleEst, 0.1):F3} p50={Pct(idleEst, 0.5):F3} "
                    + $"p90={Pct(idleEst, 0.9):F3} (realized fill there is 0.000) "
                    + $"| at STAFFED firms p50={(liveFill.Count > 0 ? Pct(liveFill, 0.5) : 0):F3}");

            // WHY DOES NOTHING RE-ENTER A VACANT BUILDING? Entry (EconomyEngine,
            // "Entry into existing vacant firm parcels") fires only where the
            // entrant's own bid beats the parcel's ASSESSMENT. Assessment is a
            // max over every configuration the zoning permits; the bid is the
            // one entrant's, for the building that actually stands. So the gate
            // can be shut by a configuration nobody is going to build. Measured
            // here rather than argued: the sign of (bid − assess) over the
            // parcels that are standing empty is the whole question.
            var excesses = new List<double>(); int openable = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.OccupantFirm >= 0) continue;
                if (pl.Use == ZoneKind.None || pl.Warehousing) continue;
                double em = pl.Use == ZoneKind.Commercial
                    ? pl.Units * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) : 0;
                double bid = LandAccounting.FirmBidPerSlot(
                    sim.Engine.Access, sim.Engine.Trade, pl.Cluster, pl.Use, pl.Level, p,
                    out _, w.Clusters, out bool condPriced, em, em > 0 ? pl.Units : 0,
                    em > 0 ? pl.Condition : 0);
                if (!condPriced) bid *= p.CondFactor(pl.Condition);
                double ex = bid - LandAccounting.UnitAssessment(pl, p);
                excesses.Add(ex);
                if (ex > 0) openable++;
            }
            excesses.Sort();
            if (excesses.Count > 0)
                Console.WriteLine($"  re-entry gate on {excesses.Count} vacant non-res parcels: "
                    + $"{openable} have bid > assessment ({100.0 * openable / excesses.Count:F1} %) "
                    + $"| (bid − assess) p10={Pct(excesses, 0.1):F3} p50={Pct(excesses, 0.5):F3} "
                    + $"p90={Pct(excesses, 0.9):F3}");

            // WHY IS THE RUIN STILL STANDING? Entry is refused correctly -- a
            // derelict building produces ~nothing and still owes SPerUnit, so
            // bid < assess is the right answer, and the constant residual is
            // exactly that charge. The question is therefore not entry but
            // DEMOLITION: Leveling's scrape path needs four things at once, and
            // this reports which of them the standing ruins actually have.
            int derelict = 0, wantScrape = 0, hasPressure = 0, funded = 0, physVacant = 0;
            double sumEscrow = 0, sumCost = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.Use == ZoneKind.None) continue;
                if (pl.OccupantFirm >= 0 || pl.Condition > 0.25) continue;
                derelict++;
                if (pl.TargetIsScrape) wantScrape++;
                if (pl.Wedge > 0.15 * Math.Max(1e-9, Math.Abs(pl.CurrentResidual) + pl.Wedge)) hasPressure++;
                int newUnits = LandAccounting.UnitsFor(pl.TargetUse);
                double vNow = pl.Condition * p.RC(pl.Level, pl.Units);
                double cost = Math.Max(0, p.DemolitionPerUnit * pl.Units
                                          + p.RC(pl.TargetLevel, newUnits) - p.SalvageFraction * vNow);
                sumEscrow += pl.Escrow; sumCost += cost;
                if (pl.Escrow >= cost) funded++;
                if (pl.OccupantHouseholds.Count == 0 && pl.OccupantFirm < 0) physVacant++;
            }
            Console.WriteLine($"  construction: starts={sim.Engine.Construction.StartedTotal} "
                + $"redevelopments={sim.Engine.Construction.RedevelopmentsTotal} "
                + $"abandoned={sim.Engine.Construction.AbandonedTotal} scrapes(Leveling)={w.ScrapesTotal} "
                + $"startsCapBound={sim.Engine.Construction.StartsCapBoundTicks} ticks");
            if (derelict > 0)
                Console.WriteLine($"  scrape gates on {derelict} derelict (cond<=0.25) vacant parcels: "
                    + $"TargetIsScrape={wantScrape} pressure={hasPressure} funded={funded} "
                    + $"physicallyVacant={physVacant} | mean escrow={sumEscrow / derelict:F1} "
                    + $"vs mean scrape cost={sumCost / derelict:F1}");

            // WEBER BY COHORT. The Weber check's alignment leg reads the
            // STANDING stock against the cheapest-delivered raw NOW, but a firm
            // commits at entry, and two cohorts never made that choice at all
            // or made it against other prices: the t=0 seeded cohort
            // (SeedRecipeOutput picks by a proximity-weighted GEOLOGY heuristic,
            // not delivered cost, and consults no bid) and old entrants whose
            // price path moved under them. Splitting the census tells the model
            // question (do entrants choose by their own margin?) apart from the
            // fixture question (who is still standing?).
            {
                int sN = 0, sOn = 0, eN = 0, eOn = 0, sM = 0, eM = 0;
                foreach (var f2 in w.Firms)
                {
                    if (f2.Dead || f2.Parcel < 0 || f2.Sector != ZoneKind.Industrial) continue;
                    if (f2.Output == Res.Machinery) continue;
                    int c2 = w.Parcels[f2.Parcel].Cluster;
                    var recipe = ResourceCatalog.RecipeFor(f2.Output);
                    double own = sim.Engine.Trade.DeliveredCost(recipe.Inputs[0].res, c2);
                    double cheapest = double.PositiveInfinity;
                    for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                        cheapest = Math.Min(cheapest, sim.Engine.Trade.DeliveredCost((Res)rr, c2));
                    bool on = own <= cheapest * 1.15 + 0.05;
                    // The model's OWN criterion, asked now: is this firm's
                    // recipe the margin argmax FirmBidPerSlot would choose at
                    // this site today? The check's cheapest-raw test is a PROXY
                    // for this, and with asymmetric output anchors the two can
                    // disagree while the firm is exactly on its own margin.
                    LandAccounting.FirmBidPerSlot(
                        sim.Engine.Access, sim.Engine.Trade, c2, ZoneKind.Industrial,
                        w.Parcels[f2.Parcel].Level, p, out Res argmaxNow, w.Clusters);
                    bool onMargin = argmaxNow == f2.Output;
                    if (f2.EnteredTick == 0) { sN++; if (on) sOn++; if (onMargin) sM++; }
                    else { eN++; if (on) eOn++; if (onMargin) eM++; }
                }
                // HOW FAR BEHIND is a firm that is not on the argmax? A firm
                // commits its recipe at entry and NOTHING in the model ever
                // changes it (Firm.Output is assigned once, at entry). So the
                // question "should a near-miss second-best survive" is today
                // answered with infinite inertia, and whether that is harmless
                // depends entirely on this distribution: a firm 2 % behind is
                // a firm with an ordinary amount of hysteresis, one 60 % behind
                // is a firm making the wrong thing.
                var gaps = new List<double>();
                foreach (var f2 in w.Firms)
                {
                    if (f2.Dead || f2.Parcel < 0 || f2.Sector != ZoneKind.Industrial) continue;
                    int c2 = w.Parcels[f2.Parcel].Cluster;
                    double ownM = double.NaN, bestM = double.NegativeInfinity;
                    foreach (var recipe in ResourceCatalog.Recipes)
                    {
                        double outNet = Math.Max(
                            p.NonResLandParity ? sim.Engine.Trade.OriginComparable(recipe.Output, c2)
                                               : sim.Engine.Trade.OriginStat(recipe.Output, c2),
                            sim.Engine.Trade.BestExportNet(recipe.Output, c2));
                        double ic = 0;
                        foreach (var (res, qty) in recipe.Inputs)
                            ic += qty * sim.Engine.Trade.DeliveredCost(res, c2);
                        double m = recipe.OutputPerSlot * p.RecipeOutputScale * (outNet - ic);
                        if (recipe.Output == f2.Output) ownM = m;
                        if (m > bestM) bestM = m;
                    }
                    if (double.IsNaN(ownM) || bestM <= 1e-9) continue;
                    gaps.Add((bestM - ownM) / bestM);   // 0 = on the argmax
                }
                gaps.Sort();
                if (gaps.Count > 0)
                {
                    int within2 = 0, within10 = 0;
                    foreach (var g in gaps) { if (g <= 0.02) within2++; if (g <= 0.10) within10++; }
                    Console.WriteLine($"  recipe drift over {gaps.Count} live industrials "
                        + $"(margin shortfall vs the argmax at their OWN site): "
                        + $"p50={Pct(gaps, 0.5):P1} p90={Pct(gaps, 0.9):P1} max={gaps[gaps.Count - 1]:P1} "
                        + $"| within 2 %: {within2} ({100.0 * within2 / gaps.Count:F0} %), "
                        + $"within 10 %: {within10} ({100.0 * within10 / gaps.Count:F0} %)");
                }
                Console.WriteLine($"  weber cohorts (single-input industrial, alive): "
                    + $"seeded {sOn}/{sN} on-cheapest-raw, {sM}/{sN} on-margin-argmax-now | "
                    + $"entrants {eOn}/{eN} on-cheapest-raw, {eM}/{eN} on-margin-argmax-now "
                    + $"(check bar 55 % on cheapest-raw over the pooled {sN + eN})");
            }

            var la = sim.Engine.Labor;
            if (la != null && la.DoorsTotal > 0)
                Console.WriteLine($"  labor doors: {la.DoorsUnlisted}/{la.DoorsTotal} on NO worker's "
                    + $"shortlist ({100.0 * la.DoorsUnlisted / la.DoorsTotal:F1} %) — a door worth less "
                    + $"than every worker's own outside option");
            Console.WriteLine($"  engine: margin exits={e.FirmMarginExitsTotal} "
                + $"margin relocations={e.FirmMarginRelocationsTotal} "
                + $"arrears exits={e.FirmArrearsExitsTotal} all relocations={e.FirmRelocationsTotal}");
            return 0;
        }

        /// <summary>Task #49 measurement aid: what actually happens to a firm
        /// that loses its site. The pure simulation cannot strand a firm on its
        /// own (the scrape path gates on `OccupantFirm &lt; 0`, and the abandon
        /// path only touches parcels under construction, which no firm occupies)
        /// — but the mod arm can (EconReader) and the coming non-residential
        /// site market will, so the displacement is injected here through the
        /// engine's own DisplaceFirm entry point, which is the same one those
        /// two producers use.
        ///
        /// Prints, for a run with displacement injected: how many firms were
        /// displaced, how they resolved, how much money the exits carried out,
        /// and — under `--mutant-displaced-no-exit` — how many live firms are
        /// left holding no site, together with the ledger drift and sector
        /// reconciliation at that moment. The last pair is the point: the money
        /// still balances while the firms are stranded.</summary>
        public static int DisplaceProbe(ulong seed, int ticks, double rate, long atTick)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;

            int injected = 0;
            double moneyAtInjection = 0;
            var rng = new System.Random(unchecked((int)seed) ^ 0x5EED);
            sim.Run(ticks, s =>
            {
                if (s.W.Tick != atTick) return;
                foreach (var f in s.W.Firms)
                {
                    if (f.Dead || f.Parcel < 0) continue;
                    if (rng.NextDouble() >= rate) continue;
                    double m = f.Money;
                    if (s.Engine.DisplaceFirm(f)) { injected++; moneyAtInjection += m; }
                }
            });

            int liveSiteless = 0, liveSited = 0, deadDisp = 0, deadArr = 0, deadOther = 0;
            double siteless = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead)
                {
                    if (f.DiedOfDisplacement) deadDisp++;
                    else if (f.DiedOfArrears) deadArr++;
                    else deadOther++;
                    continue;
                }
                if (f.Parcel < 0) { liveSiteless++; siteless += f.Money; }
                else liveSited++;
            }
            // The pointer between a firm and its site is TWO fields that must
            // agree; counted in both directions because a half-unlink shows up
            // in only one of them.
            int fwdBreak = 0, revBreak = 0;
            foreach (var f in w.Firms)
                if (!f.Dead && f.Parcel >= 0 && w.Parcels[f.Parcel].OccupantFirm != f.Id) fwdBreak++;
            foreach (var pl in w.Parcels)
                if (pl.OccupantFirm >= 0
                    && (w.Firms[pl.OccupantFirm].Dead || w.Firms[pl.OccupantFirm].Parcel != pl.Id)) revBreak++;

            var e = sim.Engine;
            Console.WriteLine($"seed {seed} ticks {ticks} inject t={atTick} rate {rate:P0} "
                + $"mutant={EconomyEngine.MutantDisplacedFirmNoExit} halfUnlink={EconomyEngine.MutantHalfUnlinkDisplacement}");
            Console.WriteLine($"  injected {injected} firms holding {moneyAtInjection:F0} at the moment of displacement");
            Console.WriteLine($"  engine counters: seen {e.FirmDisplacedSeenTotal}, resited {e.FirmDisplacedResitedTotal}, "
                + $"exits {e.FirmDisplacedExitsTotal}, arrears exits {e.FirmArrearsExitsTotal}, "
                + $"relocations {e.FirmRelocationsTotal}");
            Console.WriteLine($"  end state: {liveSited} live firms sited, {liveSiteless} live firms SITELESS "
                + $"holding {siteless:F0}; dead: {deadDisp} displacement, {deadArr} arrears, {deadOther} other");
            Console.WriteLine($"  link breaks: {fwdBreak} live firms naming a site that does not name them back, "
                + $"{revBreak} sites naming a firm that is dead or elsewhere");
            Console.WriteLine($"  ledger drift {Math.Abs(w.Ledger.Drift()):E2} — "
                + "the existing conservation invariant, at the same moment");
            return 0;
        }

        /// <summary>`harness firmprobe` — the non-residential land census.
        ///
        /// READ-ONLY. It builds a run, reads it and changes nothing: no
        /// parameter is written, no parcel or firm field is assigned, and
        /// Assess is never called. Every quantity a non-residential land design
        /// quotes about the world as it stands is produced here, so a claim
        /// about the corner, the bill, the thin cell, the would-move population
        /// or the cost can be re-derived on demand instead of being carried in
        /// prose.
        ///
        /// Three readings need their own statement of what they are.
        ///
        /// THE LADDER-BOUND CRITERION (block I) is arithmetic over the standing
        /// bid ladder, not a forecast of any market. Write the per-slot bid as
        /// X·q(ℓ) − W, q(ℓ) = Quality(ℓ)/Quality(1) = ℓ^LevelBidAlpha. A bidder
        /// standing at rung ℓ0 whose alternative is its own site, netted at that
        /// site's structure floor, offers X·(q(ℓ)−q(ℓ0)) + S(ℓ0,m) at the ℓ
        /// door; the largest bar that door can carry is S(ℓ,1.0) (an unbuilt
        /// rung pools at condition 1.0, HousingAuction.cs:521-537). So the ℓ
        /// door is queued from ℓ0 exactly when X > D(ℓ,ℓ0) =
        /// (S(ℓ,1)−S(ℓ0,m))/(q(ℓ)−q(ℓ0)), and the ladder is bounded at that
        /// rung exactly when X &lt; D. X and W are FITTED per (sector, cluster)
        /// from the two endpoints bid(1), bid(5) of FirmBidPerSlot, and the
        /// worst interior-rung residual is printed beside them: linearity in q
        /// is a claim about this file's own code, and a claim about code is
        /// reported with its own error rather than assumed. FirmBidPerSlot
        /// floors at 0 (LandAccounting.cs:585), which is the one nonlinearity,
        /// so the count of clusters whose bottom rung sits on that floor is
        /// printed too.
        ///
        /// THE PHASE TIMERS (block L) separate what is measured from what is
        /// not, and the separation is the point. The per-door value table and
        /// the door/reserve pass are timed here because neither needs a market
        /// to exist. No firm-side SOLVE exists at this commit, so none is timed;
        /// what stands in its place is the housing solve that any firm-side
        /// estimate would be extrapolated FROM, printed with its ROUND counts,
        /// because scaling a solve time by a bidder ratio assumes a round count
        /// that an identical-bidder market need not have (KNOWN-RED.md's labor
        /// bring-up: 796,000 bids against 48,955 evictions, converged False).
        /// Scan width and round count are different failure modes and they do
        /// not share a number here.
        ///
        /// BLOCK J counts a failure class nothing else in the suite can see. A
        /// firm with Parcel &lt; 0 and !Dead is skipped by every firm loop in the
        /// engine (EconomyEngine.cs:770, 903, 1021, 1035, 1110, 1238, 1256,
        /// 1481, 1617, 1673; LaborAuction.cs:317) — including FirmLifecycle's
        /// own bankruptcy branch, which sits INSIDE the skip at :1673 — so such
        /// a firm never produces, never pays, never dies. Its Money stays in
        /// both Σ f.Money and Ledger.Balance(Firms), so SectorAudit's
        /// reconciliation (TestRunner.cs:5158-5175, which sums every firm
        /// including dead ones) stays green over any number of them. A
        /// displacement that forgot its exit is therefore invisible to
        /// conservation; this block is what would see it.</summary>
        public static int FirmProbe(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());

            // HousingAuction's Rounds/Bids/Evictions/RepairRounds are per-solve
            // fields zeroed at the top of Solve, so a run total needs sampling
            // on the tick the solve counter moves. Exactly one solve per tick is
            // reachable (EconomyEngine.cs:94 gates RefreshTick on
            // Tick % RefreshInterval, :515 solves once inside it); the printed
            // solvesSampled against HousingAuction.SolveCalls is the check.
            long solvesSeen = HousingAuction.SolveCalls;
            long acRounds = 0, acBids = 0, acEvict = 0, acRepair = 0;
            int solvesSampled = 0, multiSolveTicks = 0;
            var swRun = System.Diagnostics.Stopwatch.StartNew();
            sim.Run(ticks, s =>
            {
                long d = HousingAuction.SolveCalls - solvesSeen;
                if (d <= 0) return;
                if (d > 1) multiSolveTicks++;
                solvesSeen = HousingAuction.SolveCalls;
                var a = s.Engine.Auction;
                acRounds += a.Rounds; acBids += a.Bids; acEvict += a.Evictions;
                acRepair += a.RepairRounds; solvesSampled++;
            });
            swRun.Stop();

            var w = sim.W;
            var acc = sim.Engine.Access;
            var trade = sim.Engine.Trade;
            int C = acc.C;
            var sectors = new[] { ZoneKind.Commercial, ZoneKind.Industrial,
                                  ZoneKind.Office, ZoneKind.Extractor };

            bool Live(Parcel q) => q.OccupantFirm >= 0 && !w.Firms[q.OccupantFirm].Dead;
            bool BuiltNonRes(Parcel q) => q.State == ParcelState.Built && !q.IsResidential
                                          && q.Use != ZoneKind.None;

            int pop = 0; foreach (var h in w.Households) if (h.ExitedTick < 0) pop++;
            int alive = 0; foreach (var f in w.Firms) if (!f.Dead) alive++;

            Console.WriteLine($"firmprobe seed={seed} ticks={ticks} clusters={C} " +
                              $"parity={p.NonResLandParity} storeLevel={sim.Flags.StoreLevelSpending} " +
                              $"auction={sim.Flags.HousingAuction} wall={swRun.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"world: pop={pop} households={w.Households.Count} parcels={w.Parcels.Count} " +
                              $"firmsAlive={alive} firmsEver={w.Firms.Count}");

            // ---- A. the stock ------------------------------------------------
            Console.WriteLine("-- A. built non-residential parcels, per sector --");
            foreach (var s in sectors)
            {
                int built = 0, occ = 0, vac = 0; var vc = new List<double>();
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != s) continue;
                    built++;
                    if (Live(pl)) occ++; else { vac++; vc.Add(pl.Condition); }
                }
                vc.Sort();
                Console.WriteLine($"  {s,-11} built={built,4} occupied={occ,4} vacant={vac,4} " +
                                  $"vacantCond p10={Pct(vc, 0.1):F2} p50={Pct(vc, 0.5):F2} p90={Pct(vc, 0.9):F2}");
            }

            // ---- B. the cell census, at cell grain AND column grain ----------
            // The column row is what a (cluster, use)-wide exclusion rule costs:
            // a door whose only bidders held a site in the column reads empty.
            Console.WriteLine("-- B. occupied non-res parcels alone in their cell / column --");
            void Grain(string label, Func<Parcel, (int, int, int)> key, ZoneKind? only)
            {
                var counts = new Dictionary<(int, int, int), int>();
                int tot = 0;
                foreach (var pl in w.Parcels)
                {
                    if (!BuiltNonRes(pl) || !Live(pl)) continue;
                    if (only.HasValue && pl.Use != only.Value) continue;
                    var k = key(pl);
                    counts[k] = counts.TryGetValue(k, out int v) ? v + 1 : 1;
                    tot++;
                }
                int a0 = counts.Values.Where(v => v == 1).Sum();
                int a1 = counts.Values.Where(v => v == 2).Sum();
                int a2 = counts.Values.Where(v => v >= 3).Sum();
                Console.WriteLine($"  {label,-32} cells={counts.Count,4} occupied={tot,4} " +
                                  $"alone={a0,4} ({(tot > 0 ? 100.0 * a0 / tot : 0),5:F1}%) " +
                                  $"oneOther={a1,4} twoPlusOthers={a2,4}");
            }
            Grain("(cluster,use,level) pooled", pl => (pl.Cluster, (int)pl.Use, pl.Level), null);
            Grain("(cluster,use)       pooled", pl => (pl.Cluster, (int)pl.Use, 0), null);
            Grain("(use,level) citywide", pl => (0, (int)pl.Use, pl.Level), null);
            Grain("(use) citywide", pl => (0, (int)pl.Use, 0), null);
            foreach (var s in sectors)
                Grain($"(cluster,use,level) {s}", pl => (pl.Cluster, (int)pl.Use, pl.Level), s);
            foreach (var s in sectors)
                Grain($"(cluster,use) {s}", pl => (pl.Cluster, (int)pl.Use, 0), s);

            // ---- C. the ell-star census -------------------------------------
            Console.WriteLine("-- C. standing level vs TargetLevel (built parcels) --");
            void Ladder(string label, Func<Parcel, bool> pick)
            {
                int n = 0, corner = 0; double lv = 0, tg = 0;
                var gap = new int[p.MaxLevel + 1];   // TargetLevel - Level, clamped for display
                int gapUp = 0, gapDown = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || !pick(pl)) continue;
                    n++; lv += pl.Level; tg += pl.TargetLevel;
                    if (pl.TargetLevel >= p.MaxLevel) corner++;
                    int d = pl.TargetLevel - pl.Level;
                    if (d > 0) { gapUp++; gap[Math.Min(p.MaxLevel, d)]++; }
                    else if (d < 0) gapDown++;
                }
                if (n == 0) { Console.WriteLine($"  {label,-11} none built"); return; }
                Console.WriteLine($"  {label,-11} n={n,4} meanLevel={lv / n:F2} meanTarget={tg / n:F2} " +
                                  $"atCorner(L{p.MaxLevel})={corner,4} ({100.0 * corner / n,5:F1}%) " +
                                  $"target>level={gapUp,4} target<level={gapDown,4} " +
                                  $"gap[+1..+4]={gap[1]}/{gap[2]}/{gap[3]}/{gap[4]}");
            }
            foreach (var s in sectors) Ladder(s.ToString(), pl => pl.Use == s);
            Ladder("Residential", pl => pl.IsResidential);

            // ---- D. per-cluster MAX standing rung, per sector -----------------
            // The bound a standing-rungs candidate loop would impose: no rung
            // above this can be proposed if candidates are gated on capacity.
            Console.WriteLine("-- D. per-cluster max standing rung (built parcels), per sector --");
            foreach (var s in sectors)
            {
                var maxRung = new int[C];
                var occMaxRung = new int[C];
                for (int c = 0; c < C; c++) { maxRung[c] = 0; occMaxRung[c] = 0; }
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != s) continue;
                    if ((uint)pl.Cluster >= (uint)C) continue;
                    if (pl.Level > maxRung[pl.Cluster]) maxRung[pl.Cluster] = pl.Level;
                    if (Live(pl) && pl.Level > occMaxRung[pl.Cluster]) occMaxRung[pl.Cluster] = pl.Level;
                }
                var hist = new int[p.MaxLevel + 1];
                int nc = 0; double sum = 0;
                for (int c = 0; c < C; c++)
                {
                    if (maxRung[c] == 0) continue;
                    nc++; sum += maxRung[c]; hist[maxRung[c]]++;
                }
                if (nc == 0) { Console.WriteLine($"  {s,-11} no cluster holds a built parcel"); continue; }
                int occNc = 0; double occSum = 0;
                for (int c = 0; c < C; c++) if (occMaxRung[c] > 0) { occNc++; occSum += occMaxRung[c]; }
                Console.WriteLine($"  {s,-11} clusters={nc,4} meanMaxRung={sum / nc:F2} " +
                                  $"hist[L1..L5]={hist[1]}/{hist[2]}/{hist[3]}/{hist[4]}/{hist[5]} " +
                                  $"| occupied-only clusters={occNc,4} meanMaxRung=" +
                                  $"{(occNc > 0 ? occSum / occNc : 0):F2}");

                // Q13: THE SAME QUESTION AT PARCEL GRAIN, which is the grain the
                // benefit is actually felt at. Counting CLUSTERS answers "where
                // would Option L bind"; it cannot answer "how much". A sector
                // whose ladder is capped in one thin cluster and uncapped in
                // eleven fat ones reads the same per-cluster mean as the
                // reverse. `moved` is the parcel count Option L would actually
                // pull down — ℓ* strictly above the parcel's own column max —
                // and `rungsSaved` is by how much in total, so a benefit of
                // "two parcels by one rung" cannot be reported as if it were
                // the sector.
                int built = 0, occupied = 0, moved = 0, rungsSaved = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != s) continue;
                    if ((uint)pl.Cluster >= (uint)C) continue;
                    built++;
                    if (Live(pl)) occupied++;
                    int cap = maxRung[pl.Cluster];
                    if (pl.TargetLevel > cap) { moved++; rungsSaved += pl.TargetLevel - cap; }
                }
                Console.WriteLine($"              Q13 parcel grain: built={built} occupied={occupied} " +
                                  $"| Option L would move {moved} ({(built > 0 ? 100.0 * moved / built : 0):F1} % of built) " +
                                  $"by {rungsSaved} rungs total " +
                                  $"({(moved > 0 ? (double)rungsSaved / moved : 0):F2} per moved parcel)");
            }

            // ---- E. the bill, decomposed, and at LR = 0 ----------------------
            Console.WriteLine("-- E. occupied non-res parcels: bill decomposition (per parcel per tick) --");
            foreach (var s in sectors)
            {
                var landShare = new List<double>();
                var billNow = new List<double>(); var billZero = new List<double>();
                var coverNow = new List<double>(); var coverZero = new List<double>();
                double sumLR = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != s || !Live(pl)) continue;
                    var f = w.Firms[pl.OccupantFirm];
                    double sper = LandAccounting.SPerUnit(pl.Level, pl.Condition, p);
                    double land = p.CaptureFraction * pl.AssessedLR / Math.Max(1, pl.Units);
                    double tax = LandAccounting.StructureTaxPerUnit(pl, p);
                    double bill = sper + land + tax;
                    sumLR += pl.AssessedLR;
                    if (bill <= 1e-12) continue;
                    landShare.Add(land / bill);
                    billNow.Add(bill * pl.Units); billZero.Add((sper + tax) * pl.Units);
                    double rev = f.GrossRevenueLastTick;
                    coverNow.Add(rev / Math.Max(1e-9, bill * pl.Units));
                    coverZero.Add(rev / Math.Max(1e-9, (sper + tax) * pl.Units));
                }
                landShare.Sort(); billNow.Sort(); billZero.Sort(); coverNow.Sort(); coverZero.Sort();
                Console.WriteLine($"  {s,-11} n={landShare.Count,4} landShare p50={Pct(landShare, 0.5):F2} " +
                                  $"p90={Pct(landShare, 0.9):F2} | bill p50={Pct(billNow, 0.5),7:F1} " +
                                  $"billAtLR0 p50={Pct(billZero, 0.5),6:F1} | rev/bill p50={Pct(coverNow, 0.5):F2} " +
                                  $"atLR0 p50={Pct(coverZero, 0.5):F2} " +
                                  $"rev<bill now={coverNow.Count(x => x < 1)}/{coverNow.Count} " +
                                  $"atLR0={coverZero.Count(x => x < 1)}/{coverZero.Count} | ΣAssessedLR={sumLR:F0}");
            }
            // The same population split by STANDING RUNG: a claim that compares
            // a p50 revenue to a bill at some other rung is comparing two rungs.
            Console.WriteLine("-- E2. the same, by standing rung (occupied parcels) --");
            foreach (var s in sectors)
                for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
                {
                    var bn = new List<double>(); var rv = new List<double>(); var cv = new List<double>();
                    var lr = new List<double>();
                    foreach (var pl in w.Parcels)
                    {
                        if (pl.State != ParcelState.Built || pl.Use != s || pl.Level != lvl) continue;
                        if (!Live(pl)) continue;
                        var f = w.Firms[pl.OccupantFirm];
                        double bill = LandAccounting.UnitAssessment(pl, p) * pl.Units;
                        bn.Add(bill); rv.Add(f.GrossRevenueLastTick); lr.Add(pl.AssessedLR);
                        cv.Add(f.GrossRevenueLastTick / Math.Max(1e-9, bill));
                    }
                    if (bn.Count == 0) continue;
                    bn.Sort(); rv.Sort(); cv.Sort(); lr.Sort();
                    Console.WriteLine($"  {s,-11} L{lvl} n={bn.Count,4} bill p50={Pct(bn, 0.5),7:F1} " +
                                      $"rev p50={Pct(rv, 0.5),7:F1} rev/bill p50={Pct(cv, 0.5):F2} " +
                                      $"rev<bill={cv.Count(x => x < 1),4} AssessedLR p50={Pct(lr, 0.5),7:F1}");
                }

            // ---- F. entry excess on vacant parcels, at AssessedLR and at 0 ----
            // Both legs price the SPECIFIC vacant building the entrant would
            // occupy (entrantMass/slots/condition), which is what
            // EconomyEngine's entry test at :1800-1826 asks; the LR = 0 leg
            // differs only in dropping the land term from the assessment side.
            Console.WriteLine("-- F. vacant non-res parcels: entrant excess at AssessedLR and at LR=0 --");
            foreach (var s in sectors)
            {
                var exNow = new List<double>(); var exZero = new List<double>(); int nvac = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != s || Live(pl)) continue;
                    if (pl.Warehousing) continue;
                    nvac++;
                    double entrantMass = pl.Use == ZoneKind.Commercial
                        ? pl.Units * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) : 0;
                    double bid = LandAccounting.FirmBidPerSlot(acc, trade, pl.Cluster, pl.Use, pl.Level, p,
                                                              out _, w.Clusters, out bool condPriced,
                                                              entrantMass, entrantMass > 0 ? pl.Units : 0,
                                                              entrantMass > 0 ? pl.Condition : 0);
                    if (!condPriced) bid *= p.CondFactor(pl.Condition);
                    exNow.Add(bid - LandAccounting.UnitAssessment(pl, p));
                    exZero.Add(bid - (LandAccounting.SPerUnit(pl.Level, pl.Condition, p)
                                      + LandAccounting.StructureTaxPerUnit(pl, p)));
                }
                exNow.Sort(); exZero.Sort();
                Console.WriteLine($"  {s,-11} vacant={nvac,4} | now p10={Pct(exNow, 0.1),8:F2} " +
                                  $"p50={Pct(exNow, 0.5),7:F2} p90={Pct(exNow, 0.9),7:F2} " +
                                  $"positive={exNow.Count(x => x > 0),4} " +
                                  $"({(nvac > 0 ? 100.0 * exNow.Count(x => x > 0) / nvac : 0),3:F0}%) " +
                                  $"| atLR0 p10={Pct(exZero, 0.1),7:F2} p50={Pct(exZero, 0.5),7:F2} " +
                                  $"p90={Pct(exZero, 0.9),7:F2} positive={exZero.Count(x => x > 0),4} " +
                                  $"({(nvac > 0 ? 100.0 * exZero.Count(x => x > 0) / nvac : 0),3:F0}%)");
            }

            // ---- G. the would-move census ------------------------------------
            // The firm's OWN forecast at a standing vacant site of its own
            // sector, against the same arithmetic at its current site — the
            // identical expression RelocateFirm scores (EconomyEngine.cs:1859-1866).
            Console.WriteLine("-- G. incumbents with a strictly better standing vacant site of their own sector --");
            var vacList = w.Parcels.Where(q => q.State == ParcelState.Built && !q.IsResidential
                                               && q.Use != ZoneKind.None && !q.Warehousing && !Live(q)).ToList();
            double MoveValue(Parcel q) =>
                LandAccounting.FirmBidPerSlot(acc, trade, q.Cluster, q.Use, q.Level, p, out _, w.Clusters)
                * p.CondFactor(q.Condition) * q.Units
                - LandAccounting.UnitAssessment(q, p) * q.Units;
            foreach (var s in sectors)
            {
                int firms = 0, movers = 0; var gain = new List<double>();
                var mine = vacList.Where(q => q.Use == s).ToList();
                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != s) continue;
                    firms++;
                    double here = MoveValue(w.Parcels[f.Parcel]);
                    double best = here;
                    foreach (var q in mine) { double v = MoveValue(q); if (v > best) best = v; }
                    if (best > here) { movers++; gain.Add(best - here); }
                }
                gain.Sort();
                Console.WriteLine($"  {s,-11} firms={firms,4} vacantSameUse={mine.Count,4} " +
                                  $"wouldMove={movers,4} ({(firms > 0 ? 100.0 * movers / firms : 0),3:F0}%) " +
                                  $"gain p50={Pct(gain, 0.5),7:F2} p90={Pct(gain, 0.9),7:F2}");
            }

            // ---- H. within-cell condition spread -----------------------------
            // The door pools every parcel of a (cluster, use, level) behind one
            // reserve; UnitsFor is constant per use and the level is in the key,
            // so CONDITION is the only within-cell variation there is.
            {
                var cells = new Dictionary<(int, int, int), List<Parcel>>();
                foreach (var pl in w.Parcels)
                {
                    if (!BuiltNonRes(pl)) continue;
                    var k = (pl.Cluster, (int)pl.Use, pl.Level);
                    if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<Parcel>();
                    l.Add(pl);
                }
                var spread = new List<double>(); var spreadMixed = new List<double>();
                var occSpread = new List<double>();
                int multi = 0, mixed = 0, occMulti = 0;
                foreach (var kv in cells)
                {
                    if (kv.Value.Count >= 2)
                    {
                        multi++;
                        double mn = kv.Value.Min(x => x.Condition), mx = kv.Value.Max(x => x.Condition);
                        spread.Add(mx - mn);
                        if (kv.Value.Any(Live) && kv.Value.Any(x => !Live(x)))
                        { mixed++; spreadMixed.Add(mx - mn); }
                    }
                    var occ = kv.Value.Where(Live).ToList();
                    if (occ.Count >= 2)
                    { occMulti++; occSpread.Add(occ.Max(x => x.Condition) - occ.Min(x => x.Condition)); }
                }
                spread.Sort(); spreadMixed.Sort(); occSpread.Sort();
                var occCond = new List<double>();
                foreach (var pl in w.Parcels) if (BuiltNonRes(pl) && Live(pl)) occCond.Add(pl.Condition);
                occCond.Sort();
                Console.WriteLine("-- H. within-cell condition spread (built non-res) --");
                Console.WriteLine($"  cells={cells.Count} withTwoPlus={multi} mixedOccupiedVacant={mixed} | " +
                                  $"spread p10={Pct(spread, 0.1):F2} p50={Pct(spread, 0.5):F2} " +
                                  $"p90={Pct(spread, 0.9):F2} | mixed-cell spread p50={Pct(spreadMixed, 0.5):F2}");
                Console.WriteLine($"  occupied-only cells withTwoPlus={occMulti} spread " +
                                  $"p50={Pct(occSpread, 0.5):F2} p90={Pct(occSpread, 0.9):F2} | " +
                                  $"occupied condition p10={Pct(occCond, 0.1):F2} p50={Pct(occCond, 0.5):F2} " +
                                  $"p90={Pct(occCond, 0.9):F2} (n={occCond.Count})");
            }

            // ---- I. the ladder-bound criterion, X vs D(5, ell0) ---------------
            Console.WriteLine("-- I. ladder-bound criterion: fitted X against D(5,L0) per (sector,cluster) --");
            Console.WriteLine($"     bound BINDS at rung 5 from L0 iff X < D(5,L0); " +
                              $"D=(S(5,1)-S(L0,m))/(q(5)-q(L0)); S(5,1.0)=" +
                              $"{LandAccounting.SPerUnit(p.MaxLevel, 1.0, p):F5}");
            {
                double[] q = new double[p.MaxLevel + 1];
                for (int l = 1; l <= p.MaxLevel; l++) q[l] = p.Quality(l) / p.Quality(1);
                foreach (var s in sectors)
                {
                    var occCond = new List<double>();
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == s && Live(pl)) occCond.Add(pl.Condition);
                    occCond.Sort();
                    double m = occCond.Count > 0 ? Pct(occCond, 0.5) : 0;

                    var present = new bool[C];
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == s && (uint)pl.Cluster < (uint)C)
                            present[pl.Cluster] = true;

                    var xs = new List<double>(); var ws = new List<double>(); var resid = new List<double>();
                    int nc = 0, floorBottom = 0, dead = 0, fitBad = 0;
                    // Which rung a bidder ranks BEST on surplus over the door's
                    // own structure floor. An alt term taken over ALL doors
                    // admits a bidder above reserve only at this rung — bid >
                    // Reserve[s] rearranges to "s is the strict argmax of
                    // value − Reserve" — so this histogram is where a
                    // one-entry-per-bidder queue would sit.
                    var argmaxHist = new int[p.MaxLevel + 1];
                    var bindM = new int[p.MaxLevel]; var bind0 = new int[p.MaxLevel];
                    for (int c = 0; c < C; c++)
                    {
                        if (!present[c]) continue;
                        nc++;
                        var bid = new double[p.MaxLevel + 1];
                        for (int l = 1; l <= p.MaxLevel; l++)
                            bid[l] = LandAccounting.FirmBidPerSlot(acc, trade, c, s, l, p, out _, w.Clusters);
                        if (bid[p.MaxLevel] <= 1e-12) { dead++; }
                        if (bid[1] <= 1e-12) floorBottom++;
                        double X = (bid[p.MaxLevel] - bid[1]) / (q[p.MaxLevel] - q[1]);
                        double W = X * q[1] - bid[1];
                        double worst = 0;
                        for (int l = 2; l < p.MaxLevel; l++)
                            worst = Math.Max(worst, Math.Abs(bid[l] - (X * q[l] - W)));
                        int am = 1; double amBest = double.NegativeInfinity;
                        for (int l = 1; l <= p.MaxLevel; l++)
                        {
                            double surplus = bid[l] - LandAccounting.SPerUnit(l, 1.0, p);
                            if (surplus > amBest) { amBest = surplus; am = l; }
                        }
                        argmaxHist[am]++;
                        xs.Add(X); ws.Add(W);
                        double rel = bid[p.MaxLevel] > 1e-9 ? worst / bid[p.MaxLevel] : 0;
                        resid.Add(rel);
                        if (rel > 0.01) fitBad++;
                        for (int l0 = 1; l0 < p.MaxLevel; l0++)
                        {
                            double dm = (LandAccounting.SPerUnit(p.MaxLevel, 1.0, p)
                                         - LandAccounting.SPerUnit(l0, m, p)) / (q[p.MaxLevel] - q[l0]);
                            double d0 = (LandAccounting.SPerUnit(p.MaxLevel, 1.0, p)
                                         - LandAccounting.SPerUnit(l0, 0.0, p)) / (q[p.MaxLevel] - q[l0]);
                            if (X < dm) bindM[l0]++;
                            if (X < d0) bind0[l0]++;
                        }
                    }
                    if (nc == 0) { Console.WriteLine($"  {s,-11} no cluster holds a built parcel"); continue; }
                    xs.Sort(); ws.Sort(); resid.Sort();
                    var dmRow = new List<string>(); var d0Row = new List<string>();
                    for (int l0 = 1; l0 < p.MaxLevel; l0++)
                    {
                        double dm = (LandAccounting.SPerUnit(p.MaxLevel, 1.0, p)
                                     - LandAccounting.SPerUnit(l0, m, p)) / (q[p.MaxLevel] - q[l0]);
                        double d0 = (LandAccounting.SPerUnit(p.MaxLevel, 1.0, p)
                                     - LandAccounting.SPerUnit(l0, 0.0, p)) / (q[p.MaxLevel] - q[l0]);
                        dmRow.Add($"L{l0}:{dm:F3}({bindM[l0]})");
                        d0Row.Add($"L{l0}:{d0:F3}({bind0[l0]})");
                    }
                    Console.WriteLine($"  {s,-11} clusters={nc,4} occCondP50={m:F2} " +
                                      $"X p10={Pct(xs, 0.1),8:F3} p50={Pct(xs, 0.5),8:F3} p90={Pct(xs, 0.9),8:F3} " +
                                      $"min={(xs.Count > 0 ? xs[0] : 0),8:F3} " +
                                      $"| W p50={Pct(ws, 0.5),8:F3} " +
                                      $"| fit residual/bid(5) p50={Pct(resid, 0.5):E1} max=" +
                                      $"{(resid.Count > 0 ? resid[^1] : 0):E1} over1%={fitBad} " +
                                      $"| bid(1)=0 on {floorBottom} bid(5)=0 on {dead}");
                    Console.WriteLine($"              argmax rung of (bid - S(L,1.0)) over clusters, hist[L1..L5]=" +
                                      $"{argmaxHist[1]}/{argmaxHist[2]}/{argmaxHist[3]}/{argmaxHist[4]}/{argmaxHist[5]}");
                    Console.WriteLine($"              D(5,L0) at m={m:F2} (clusters BOUNDED): " + string.Join(" ", dmRow));
                    Console.WriteLine($"              D(5,L0) at m=0.00 (clusters BOUNDED): " + string.Join(" ", d0Row));

                    // Q12: THE CROSS-CLUSTER CRITERION. Everything above tests a
                    // cluster against its OWN slope X_c, which is the top-cluster
                    // special case. A bidder with an alternative taken over doors
                    // ANYWHERE nets that alternative off every bid it makes, so
                    // the question at a door is not "is this column steep" but
                    // "does this door still clear its own structure floor once
                    // the bidder's best alternative elsewhere is netted off":
                    //     value_c(5) − A ≤ S(5,1.0)   ⇒ the ℓ5 door is bounded.
                    // A is reported under BOTH candidate scopes because the
                    // design has not settled which it means and the two give
                    // different answers: over every door in the city, and over
                    // only those doors a built parcel actually stands at. No
                    // mechanism is being exercised here — nothing named FirmSite
                    // exists at this commit — these are reads of the same value
                    // table the ladder already uses, under two definitions of A.
                    // A EXCLUDES THE DOOR UNDER TEST. This is not a refinement,
                    // it is the difference between a measurement and a
                    // tautology: with the door left in, A ≥ value_c(5) − S(5,1)
                    // holds by construction for every cluster, so
                    // `value_c(5) − A ≤ S(5,1.0)` is true everywhere and the
                    // only clusters that ever read unbounded are the argmax
                    // itself failing an equality test on floating point. The
                    // first cut of this block did exactly that and printed
                    // 11/12, 111/112, 31/31 — numbers that look like findings
                    // and are arithmetic. It is also the economically right
                    // reading: a bidder's alternative is its best OTHER door,
                    // which is the exclusion the design's own rule is built on.
                    // Implemented as top-two, so the excluded max falls back to
                    // the runner-up rather than rescanning per door.
                    double a1All = double.NegativeInfinity, a2All = double.NegativeInfinity;
                    int a1AllC = -1, a1AllL = -1;
                    double a1St = double.NegativeInfinity, a2St = double.NegativeInfinity;
                    int a1StC = -1, a1StL = -1;
                    var standingDoor = new bool[C, p.MaxLevel + 1];
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == s
                            && (uint)pl.Cluster < (uint)C && pl.Level >= 1 && pl.Level <= p.MaxLevel)
                            standingDoor[pl.Cluster, pl.Level] = true;
                    for (int c = 0; c < C; c++)
                    {
                        if (!present[c]) continue;
                        for (int l = 1; l <= p.MaxLevel; l++)
                        {
                            double surplus = LandAccounting.FirmBidPerSlot(acc, trade, c, s, l, p, out _, w.Clusters)
                                             - LandAccounting.SPerUnit(l, 1.0, p);
                            if (surplus > a1All) { a2All = a1All; a1All = surplus; a1AllC = c; a1AllL = l; }
                            else if (surplus > a2All) a2All = surplus;
                            if (standingDoor[c, l])
                            {
                                if (surplus > a1St) { a2St = a1St; a1St = surplus; a1StC = c; a1StL = l; }
                                else if (surplus > a2St) a2St = surplus;
                            }
                        }
                    }
                    double Fin(double v) => double.IsNegativeInfinity(v) ? 0 : v;
                    double s5 = LandAccounting.SPerUnit(p.MaxLevel, 1.0, p);
                    int bcAll = 0, bcStand = 0, bpAll = 0, bpStand = 0, parcelsHere = 0;
                    var margin = new List<double>();
                    for (int c = 0; c < C; c++)
                    {
                        if (!present[c]) continue;
                        double v5 = LandAccounting.FirmBidPerSlot(acc, trade, c, s, p.MaxLevel, p, out _, w.Clusters);
                        // The alternative this cluster's ℓ5 door faces is the
                        // best door that is NOT it.
                        double aA = Fin(a1AllC == c && a1AllL == p.MaxLevel ? a2All : a1All);
                        double aS = Fin(a1StC == c && a1StL == p.MaxLevel ? a2St : a1St);
                        bool bAll = v5 - aA <= s5, bStand = v5 - aS <= s5;
                        if (bAll) bcAll++;
                        if (bStand) bcStand++;
                        margin.Add(v5 - aA - s5);
                        int here = 0;
                        foreach (var pl in w.Parcels)
                            if (pl.State == ParcelState.Built && pl.Use == s && pl.Cluster == c) here++;
                        parcelsHere += here;
                        if (bAll) bpAll += here;
                        if (bStand) bpStand += here;
                    }
                    margin.Sort();
                    Console.WriteLine($"              Q12 cross-cluster (A excludes the door under test): " +
                                      $"A1(all)={Fin(a1All):F3} A2(all)={Fin(a2All):F3} " +
                                      $"A1(standing)={Fin(a1St):F3} A2(standing)={Fin(a2St):F3} S(5,1.0)={s5:F3}");
                    Console.WriteLine($"              Q12 BOUNDED by value_c(5)−A ≤ S(5,1.0): " +
                                      $"all-doors {bcAll}/{nc} clusters ({bpAll}/{parcelsHere} parcels), " +
                                      $"standing-doors {bcStand}/{nc} clusters ({bpStand}/{parcelsHere} parcels) " +
                                      $"| margin v5−A(all)−S(5,1) p10={Pct(margin, 0.1):F3} p50={Pct(margin, 0.5):F3} " +
                                      $"p90={Pct(margin, 0.9):F3}");
                }
            }

            // ---- J. the silent failure class ---------------------------------
            {
                int orphan = 0, dead = 0, backRefBad = 0;
                double orphanMoney = 0, allMoney = 0;
                foreach (var f in w.Firms)
                {
                    allMoney += f.Money;
                    if (f.Dead) { dead++; continue; }
                    if (f.Parcel < 0) { orphan++; orphanMoney += f.Money; continue; }
                    if ((uint)f.Parcel >= (uint)w.Parcels.Count
                        || w.Parcels[f.Parcel].OccupantFirm != f.Id)
                        backRefBad++;
                }
                int parcelClaimsDead = 0;
                foreach (var pl in w.Parcels)
                    if (pl.OccupantFirm >= 0 && w.Firms[pl.OccupantFirm].Dead) parcelClaimsDead++;
                double ledgerFirms = w.Ledger.Balance(Account.Firms);
                Console.WriteLine("-- J. firm/parcel binding census (the class conservation cannot see) --");
                Console.WriteLine($"  alive={alive} dead={dead} " +
                                  $"| !Dead && Parcel<0 = {orphan} holding money={orphanMoney:F2} " +
                                  $"| !Dead with parcel back-reference mismatch = {backRefBad}" +
                                  $" | parcels whose OccupantFirm is Dead = {parcelClaimsDead}");
                Console.WriteLine($"  Σ f.Money={allMoney:F2} Ledger.Balance(Firms)={ledgerFirms:F2} " +
                                  $"rel={Math.Abs(allMoney - ledgerFirms) / Math.Max(1.0, Math.Max(Math.Abs(allMoney), Math.Abs(ledgerFirms))):E2} " +
                                  $"(this reconciliation is INSENSITIVE to the count above: " +
                                  $"TestRunner.cs:5158-5175 sums every firm, parcelled or not)");
                Console.WriteLine($"  relocations over the run={sim.Engine.FirmRelocationsTotal} " +
                                  $"arrears exits={sim.Engine.FirmArrearsExitsTotal}");
            }

            // ---- K. sizing ----------------------------------------------------
            {
                var capByCell = new Dictionary<(int, int, int), int>();
                int items = 0, vacantItems = 0;
                foreach (var pl in w.Parcels)
                {
                    if (!BuiltNonRes(pl)) continue;
                    items++;
                    if (!Live(pl)) vacantItems++;
                    if (pl.Warehousing) continue;
                    var k = (pl.Cluster, (int)pl.Use, pl.Level);
                    capByCell[k] = capByCell.TryGetValue(k, out int v) ? v + 1 : 1;
                }
                int resSubs = 0;
                for (int si = 0; si < sim.Engine.Auction.Capacity.Length; si++)
                    if (sim.Engine.Auction.Capacity[si] > 0) resSubs++;
                Console.WriteLine("-- K. sizing --");
                Console.WriteLine($"  non-res built parcels={items} vacant={vacantItems} " +
                                  $"incumbent firms={alive} | live (cluster,use,level) doors={capByCell.Count} " +
                                  $"of {C * 4 * p.MaxLevel} withCapacity>=2={capByCell.Values.Count(v => v >= 2)}");
                Console.WriteLine($"  residential bidders={pop} live residential submarkets={resSubs} " +
                                  $"| firm/residential bidder ratio=" +
                                  $"{(pop > 0 ? 100.0 * alive / pop : 0):F2}%");
            }

            // ---- L. phase timers ---------------------------------------------
            Console.WriteLine("-- L. phase timers -- MEASURED HERE (no firm-side market exists at this commit) --");
            {
                // One untimed pass of each phase first. Without it the number
                // carries the JIT and cold-cache term and therefore depends on
                // what ran BEFORE it in the same process: the same five-rep loop
                // over the same 3920 cells on the same seed-1/400-tick world
                // read 3.60 ms/pass as the first heavy FirmBidPerSlot consumer
                // (scratchpad cellprobe at 234d1c3) and 2.67 ms/pass with
                // blocks F/G/I ahead of it (this command, before this warm-up
                // was added). A timer whose value depends on its position in
                // the caller is not a measurement of the phase.
                const int reps = 5;
                double warm = 0;
                for (int c = 0; c < C; c++)
                    foreach (var s in sectors)
                        for (int l = 1; l <= p.MaxLevel; l++)
                            warm += LandAccounting.FirmBidPerSlot(acc, trade, c, s, l, p, out _, w.Clusters);
                var swT = System.Diagnostics.Stopwatch.StartNew();
                double sink = warm * 0;
                for (int rep = 0; rep < reps; rep++)
                    for (int c = 0; c < C; c++)
                        foreach (var s in sectors)
                            for (int l = 1; l <= p.MaxLevel; l++)
                                sink += LandAccounting.FirmBidPerSlot(acc, trade, c, s, l, p, out _, w.Clusters);
                swT.Stop();
                int cells = C * sectors.Length * p.MaxLevel;
                Console.WriteLine($"  value table   cells={cells} reps={reps} " +
                                  $"perPass={swT.Elapsed.TotalMilliseconds / reps:F2} ms " +
                                  $"({swT.Elapsed.TotalMilliseconds * 1000 / (reps * (double)cells):F2} us/call, sink={sink:F0})");

                var cap0 = new Dictionary<(int, int, int), (int n, double cond)>();   // untimed warm-up
                foreach (var pl in w.Parcels)
                {
                    if (!BuiltNonRes(pl) || pl.Warehousing) continue;
                    var k0 = (pl.Cluster, (int)pl.Use, pl.Level);
                    cap0.TryGetValue(k0, out var e0);
                    cap0[k0] = (e0.n + 1, e0.cond + pl.Condition);
                }
                int doorSink = cap0.Count * 0;
                var swD = System.Diagnostics.Stopwatch.StartNew();
                for (int rep = 0; rep < reps; rep++)
                {
                    var cap = new Dictionary<(int, int, int), (int n, double cond)>();
                    foreach (var pl in w.Parcels)
                    {
                        if (!BuiltNonRes(pl) || pl.Warehousing) continue;
                        var k = (pl.Cluster, (int)pl.Use, pl.Level);
                        cap.TryGetValue(k, out var e);
                        cap[k] = (e.n + 1, e.cond + pl.Condition);
                    }
                    doorSink += cap.Count;
                }
                swD.Stop();
                Console.WriteLine($"  doors+reserves parcels={w.Parcels.Count} reps={reps} " +
                                  $"perPass={swD.Elapsed.TotalMilliseconds / reps:F2} ms (sink={doorSink / reps})");
            }
            Console.WriteLine("   NOT MEASURED HERE: any firm-side SOLVE. What follows is the housing " +
                              "solve an estimate would be extrapolated FROM, with its ROUND counts, " +
                              "because a bidder-ratio scaling of a solve time assumes a round count.");
            {
                double solves = Math.Max(1, HousingAuction.SolveCalls);
                double perSolve = (HousingAuction.MsSubmarkets + HousingAuction.MsHouseholds
                                   + HousingAuction.MsAuction + HousingAuction.MsRepairScan
                                   + HousingAuction.MsShadow) / solves;
                Console.WriteLine($"  housing solve  total={perSolve:F1} ms/solve " +
                                  $"solves={HousingAuction.SolveCalls} " +
                                  $"sampled={solvesSampled} multiSolveTicks={multiSolveTicks} " +
                                  $"| per solve (ms): submarkets={HousingAuction.MsSubmarkets / solves:F1} " +
                                  $"households={HousingAuction.MsHouseholds / solves:F1} " +
                                  $"auction={HousingAuction.MsAuction / solves:F1} " +
                                  $"repairScan={HousingAuction.MsRepairScan / solves:F1} " +
                                  $"shadow={HousingAuction.MsShadow / solves:F1}");
                Console.WriteLine($"  housing rounds over the run: Rounds={acRounds} Bids={acBids} " +
                                  $"Evictions={acEvict} RepairRounds={acRepair} " +
                                  $"| per solve: Rounds={acRounds / (double)Math.Max(1, solvesSampled):F1} " +
                                  $"Bids={acBids / (double)Math.Max(1, solvesSampled):F0} " +
                                  $"Evictions={acEvict / (double)Math.Max(1, solvesSampled):F0} " +
                                  $"RepairRounds={acRepair / (double)Math.Max(1, solvesSampled):F1} " +
                                  $"| last solve Converged={sim.Engine.Auction.Converged} " +
                                  $"RepairClean={sim.Engine.Auction.RepairClean}");
            }
            return 0;
        }
    }
}
