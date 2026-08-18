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

        private static double Pct(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return 0;
            double idx = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
        }
    }
}
