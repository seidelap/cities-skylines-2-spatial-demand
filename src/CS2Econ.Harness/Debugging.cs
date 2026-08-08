using System;
using System.Collections.Generic;
using System.Linq;
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
            return 0;
        }

        /// <summary>`harness vacprobe` — replicates the vacancy-localization
        /// scenario's setup and prints the overhang's life cycle every 10
        /// ticks: does the shock create persistent vacancies, does the kernel
        /// drive residual negative in the shocked set, and what refills it
        /// (arrivals vs internal chain moves)?</summary>
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
                $"pOre={engine.Trade.LocalPrice(Res.Ore):F2} pMetals={engine.Trade.LocalPrice(Res.Metals):F2} | " +
                $"wedgeΣ={wedgeSum:F0} LRΣ={lrSum:F0} avgAssess={assessSum / Math.Max(1, occupiedRes):F2} | " +
                $"displ={engine.DisplacementExits.Count} " +
                $"arr={engine.LastFlows.ArrivalsBySegment?.Sum() ?? 0} dep={engine.LastFlows.DeparturesBySegment?.Sum() ?? 0} " +
                $"drift={w.Ledger.Drift():E1}");
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
    }
}
