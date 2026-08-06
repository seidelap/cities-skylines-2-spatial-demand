using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>Acceptance scenarios (PLAN §4.2): the design's §6 targets as
    /// numbers, several as A/Bs against the vanilla baseline on identical seeds.</summary>
    public static class Scenarios
    {
        public static readonly List<(string name, bool pass, string detail)> Results
            = new List<(string, bool, string)>();

        private static void Record(string name, bool pass, string detail)
        {
            Results.Add((name, pass, detail));
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name} — {detail}");
        }

        public static int RunAll(ulong seed, out string report, string? only = null)
        {
            Results.Clear();
            Console.WriteLine("scenarios: §6 acceptance targets" + (only != null ? $" (only: {only})" : ""));
            var all = new (string key, Action<ulong> run)[]
            {
                ("vacancy", VacancyLocalization),
                ("levels", LevelGeography),
                ("fiscal", FiscalLoop),
                ("tradebend", TradeBend),
                ("modes", ModeProgression),
                ("boombust", BoomBustAsymmetry),
                ("nosync", NoSynchronization),
                ("stalled", StalledConstruction),
                ("overlay", OverlayHonesty),
                ("perf", PerformanceShape),
            };
            foreach (var (key, run) in all)
                if (only == null || key == only) run(seed);

            int failed = Results.Count(r => !r.pass);
            var sb = new StringBuilder();
            sb.AppendLine($"## Acceptance scenarios ({Results.Count} targets, {Results.Count - failed} passing)");
            sb.AppendLine();
            sb.AppendLine("| §6 target | result | measured |");
            sb.AppendLine("|---|---|---|");
            foreach (var (name, pass, detail) in Results)
                sb.AppendLine($"| {name} | {(pass ? "✅" : "❌")} | {detail} |");
            report = sb.ToString();
            Console.WriteLine($"\nscenarios: {Results.Count - failed}/{Results.Count} targets met");
            return failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        private static void VacancyLocalization(ulong seed)
        {
            // Shock-vs-control on identical seeds: the shock's construction
            // suppression should land in the shocked district (spatial mode) or
            // spread evenly everywhere (vanilla's global scalar). Measured over
            // the 150 ticks after the shock, before in-migration refills it.
            (double ratio, string detail) RunMode(bool vanilla)
            {
                double[] StartsByDistrict(bool applyShock)
                {
                    var p = new EconParams();
                    var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
                    var sim = Sim.Create(cfg, p, new FeatureFlags(), vanillaMode: vanilla);
                    sim.Run(350);
                    if (applyShock)
                        foreach (var h in sim.W.Households)
                        {
                            if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                            var pl = sim.W.Parcels[h.HomeParcel];
                            if (sim.W.Clusters[pl.Cluster].District != 0) continue;
                            if (SplitMix64.Hash01((ulong)h.Id * 31 + seed) < 0.45)
                            {
                                sim.Engine.Allocation.Vacate(sim.W, h);
                                sim.W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                                h.Money = 0; h.ExitedTick = sim.W.Tick;
                            }
                        }
                    long fromTick = sim.W.Tick;
                    sim.Run(150);
                    var starts = new double[4];
                    foreach (var (tick, parcelId, cluster) in sim.Starts)
                    {
                        if (tick <= fromTick) continue;
                        var pl = sim.W.Parcels[parcelId];
                        if (pl.Use != ZoneKind.ResidentialLow && pl.Use != ZoneKind.ResidentialHigh) continue;
                        starts[sim.W.Clusters[cluster].District]++;
                    }
                    return starts;
                }

                var shock = StartsByDistrict(true);
                var control = StartsByDistrict(false);
                // Survival = post-shock construction relative to control (+1 Laplace).
                double survShocked = (shock[0] + 1) / (control[0] + 1);
                double survFar = (shock[3] + 1) / (control[3] + 1);
                double ratio = survFar / Math.Max(1e-9, survShocked);
                return (ratio,
                    $"shocked {control[0]:F0}→{shock[0]:F0} starts, far {control[3]:F0}→{shock[3]:F0}, " +
                    $"suppression localization {ratio:F1}:1");
            }

            var spatial = RunMode(vanilla: false);
            var baseline = RunMode(vanilla: true);
            bool pass = spatial.ratio >= 10 && baseline.ratio < 3;
            Record("vacancy localization ≥10:1 (vanilla ≈1:1)", pass,
                $"spatial: {spatial.detail}; vanilla: {baseline.detail}");
        }

        private static string Fmt(double r) => double.IsPositiveInfinity(r) ? "∞" : r.ToString("F1");

        // ------------------------------------------------------------------
        private static void LevelGeography(ulong seed)
        {
            double Corr(bool vanilla)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
                var sim = Sim.Create(cfg, p, new FeatureFlags(), vanillaMode: vanilla);
                sim.Run(1300);
                var levels = new List<double>(); var accessVals = new List<double>();
                foreach (var pl in sim.W.Parcels)
                {
                    if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                    levels.Add(pl.Level);
                    double a = 0;
                    for (int s = 0; s < Segment.Count; s++) a += sim.Engine.Access.AccessValue[s][pl.Cluster];
                    accessVals.Add(a);
                }
                return Spearman(levels, accessVals);
            }
            double spatial = Corr(false), baseline = Corr(true);
            bool pass = spatial >= 0.35 && spatial > baseline + 0.15;
            Record("level map correlates with access (not uniform grind)", pass,
                $"Spearman spatial {spatial:F2} vs vanilla {baseline:F2}");
        }

        // ------------------------------------------------------------------
        private static void FiscalLoop(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(350);

            int C = sim.Access.ClusterCount;
            int corridorRow = sim.Access.Rows / 2 - 2, controlRow = 1;
            var corridor = Enumerable.Range(0, sim.Access.Cols).Select(x => sim.Access.At(x, corridorRow)).ToList();
            var control = Enumerable.Range(0, sim.Access.Cols).Select(x => sim.Access.At(x, controlRow)).ToList();

            double[] RevenueWindow(int ticks)
            {
                var acc = new double[C];
                for (int t = 0; t < ticks; t++)
                {
                    sim.Step();
                    for (int c = 0; c < C; c++) acc[c] += sim.Engine.LandRevenueByCluster[c];
                }
                return acc;
            }

            var before = RevenueWindow(60);
            // The transit investment: express links along the corridor row.
            for (int x = 0; x + 2 < sim.Access.Cols; x += 2)
                sim.Access.AddExpressLink(sim.Access.At(x, corridorRow), sim.Access.At(x + 2, corridorRow), 0.9);
            var after = RevenueWindow(60);

            double corridorBefore = corridor.Sum(c => before[c]), corridorAfter = corridor.Sum(c => after[c]);
            double controlBefore = control.Sum(c => before[c]), controlAfter = control.Sum(c => after[c]);
            double corridorGain = corridorAfter / Math.Max(1e-9, corridorBefore) - 1;
            double controlGain = controlAfter / Math.Max(1e-9, controlBefore) - 1;
            bool pass = corridorGain > 0.04 && corridorGain > controlGain + 0.03;
            Record("fiscal loop: transit raises LR revenue along its corridor", pass,
                $"corridor {corridorGain:+0.0%;-0.0%} vs control {controlGain:+0.0%;-0.0%} within 60 ticks");
        }

        // ------------------------------------------------------------------
        private static void TradeBend(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 9000, ExtractorHeavy = true };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(700);

            var rawExits = sim.W.Exits.Where(e => e.Resource == Res.Raw && e.Mode == ExitMode.Road).ToList();
            double sustained = rawExits.Sum(e => e.SustainedQ);
            var busiest = rawExits.OrderByDescending(e => e.SustainedQ).First();
            double actual = sim.Engine.Trade.ExportMarginal(busiest, 0, p);
            double flat = busiest.Anchor - busiest.PerUnitHandling;   // flat-price baseline at equal volume
            double bend = flat > 0 ? (flat - actual) / flat : 0;
            bool pass = sustained > 50 && bend >= 0.30;
            Record("monoculture export bends marginal price ≥30% below flat", pass,
                $"sustained {sustained:F0}/tick, marginal {actual:F2} vs flat {flat:F2} → bend {bend:P0}");
        }

        // ------------------------------------------------------------------
        private static void ModeProgression(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config
            { Seed = seed, SeedHouseholds = 9000, RailTerminal = true, SeaExit = true, ExtractorHeavy = true };
            var sim = Sim.Create(cfg, p, new FeatureFlags());

            var roadShare = new List<double>(); var railShare = new List<double>();
            double equalizationGap = -1;
            sim.Run(900, s =>
            {
                double road = 0, rail = 0, sea = 0;
                TradeExit? roadX = null, railX = null;
                foreach (var e in s.W.Exits)
                {
                    if (e.Resource != Res.Raw) continue;
                    if (e.Mode == ExitMode.Road) { road += e.DrawnThisTick; if (e.DrawnThisTick > 0) roadX = e; }
                    if (e.Mode == ExitMode.Rail) { rail += e.DrawnThisTick; if (e.DrawnThisTick > 0) railX = e; }
                    if (e.Mode == ExitMode.Sea) sea += e.DrawnThisTick;
                }
                double tot = road + rail + sea;
                roadShare.Add(tot > 1e-9 ? road / tot : double.NaN);
                railShare.Add(tot > 1e-9 ? rail / tot : double.NaN);
                // Marginal-price equalization whenever both modes are concurrently active.
                if (roadX != null && railX != null && road > p.LotSize && rail > p.LotSize)
                {
                    double mRoad = s.Engine.Trade.ExportMarginal(roadX, roadX.DrawnThisTick, p);
                    double mRail = s.Engine.Trade.ExportMarginal(railX, railX.DrawnThisTick, p);
                    double gap = Math.Abs(mRoad - mRail) / Math.Max(0.05, Math.Max(mRoad, mRail));
                    equalizationGap = equalizationGap < 0 ? gap : Math.Min(equalizationGap, gap);
                }
            });

            double W(List<double> xs, int from, int to)
            {
                var v = xs.Skip(from).Take(to - from).Where(x => !double.IsNaN(x)).ToList();
                return v.Count > 0 ? v.Average() : double.NaN;
            }
            double earlyRoad = W(roadShare, 60, 200);
            double earlyRail = W(railShare, 60, 200);
            double lateRail = W(railShare, 600, 900);
            bool progression = earlyRoad > 0.6 && lateRail > earlyRail + 0.15;
            bool equalized = equalizationGap >= 0 && equalizationGap <= 0.08;
            Record("truck→rail progression by volume; concurrent marginals equalize", progression && equalized,
                $"road share early {earlyRoad:P0}; rail early {earlyRail:P0} → late {lateRail:P0}; " +
                $"best concurrent marginal gap {(equalizationGap < 0 ? "n/a" : equalizationGap.ToString("P1"))}");
        }

        // ------------------------------------------------------------------
        private static void BoomBustAsymmetry(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);

            double ArrivalsOver(int ticks)
            {
                double n = 0;
                sim.Run(ticks, s => n += s.Engine.LastFlows.ArrivalsBySegment?.Sum() ?? 0);
                return n;
            }
            double DeparturesOver(int ticks)
            {
                double n = 0;
                sim.Run(ticks, s => n += s.Engine.LastFlows.DeparturesBySegment?.Sum() ?? 0);
                return n;
            }

            double baseIn = ArrivalsOver(120), baseOut = 0;
            sim.Run(0);
            // Symmetric amenity pulse: +Δ then −Δ.
            foreach (var c in sim.W.Clusters) c.Amenity += 0.9;
            double boomIn = ArrivalsOver(120);
            foreach (var c in sim.W.Clusters) c.Amenity -= 1.8;
            baseOut = 0; // measured inside next window against ~zero baseline
            double bustOut = DeparturesOver(120);
            foreach (var c in sim.W.Clusters) c.Amenity += 0.9;

            double inResponse = Math.Max(0, boomIn - baseIn);
            double outResponse = Math.Max(1, bustOut - baseOut);
            bool pass = inResponse > 1.5 * outResponse;
            Record("boom/bust asymmetry: inflow reacts faster than outflow", pass,
                $"arrival response +{inResponse:F0} vs departure response +{outResponse:F0} over equal windows/pulse");
        }

        // ------------------------------------------------------------------
        private static void NoSynchronization(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 9000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);

            // Gentrification frontier: district 0 gets an amenity + access boost.
            foreach (var c in sim.W.Clusters)
                if (c.District == 0) c.Amenity += 1.4;
            int mid = sim.Access.Rows / 4;
            for (int x = 0; x + 2 < sim.Access.Cols / 2; x += 2)
                sim.Access.AddExpressLink(sim.Access.At(x, mid), sim.Access.At(x + 2, mid), 0.8);

            long from = sim.W.Tick;
            sim.Run(700);

            var exits = sim.Engine.DisplacementExits.Where(e => e.tick > from).ToList();
            var byTick = exits.GroupBy(e => e.tick).Select(g => g.Count()).ToList();
            int total = exits.Count, maxTick = byTick.Count > 0 ? byTick.Max() : 0;
            double worstShare = total > 0 ? (double)maxTick / total : 0;
            bool pass = total >= 25 && worstShare <= 0.05;
            Record("no synchronized displacement: exit times form a distribution", pass,
                $"{total} displacement exits, worst single tick {worstShare:P1} (cliff would be ≫5%)");
        }

        // ------------------------------------------------------------------
        private static void StalledConstruction(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(400);

            int abandonedBefore = sim.Engine.Construction.AbandonedTotal;
            // Engineered bust: attractiveness collapses and a third of the city leaves.
            foreach (var c in sim.W.Clusters) c.Amenity -= 1.6;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if (SplitMix64.Hash01((ulong)h.Id * 131 + seed) < 0.33)
                {
                    sim.Engine.Allocation.Vacate(sim.W, h);
                    sim.W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                    h.Money = 0; h.ExitedTick = sim.W.Tick;
                }
            }
            sim.Run(200);
            int abandonedDuringBust = sim.Engine.Construction.AbandonedTotal - abandonedBefore;
            bool pass = abandonedDuringBust > 0;
            Record("stalled construction appears in engineered busts", pass,
                $"{abandonedDuringBust} projects abandoned mid-build after demand collapse " +
                $"({sim.Engine.Construction.StartedTotal} lifetime starts)");
        }

        // ------------------------------------------------------------------
        private static void OverlayHonesty(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());

            var factorSeries = new List<double>();
            sim.Run(900, s =>
            {
                if (s.W.Tick % 50 == 0)
                {
                    double f = s.W.Calibration.Factor(ZoneKind.ResidentialLow)
                             + s.W.Calibration.Factor(ZoneKind.ResidentialHigh);
                    factorSeries.Add(f / 2);
                }
            });

            double obsN = sim.W.Calibration.ByUse.Values.Sum(x => x.N);
            var late = factorSeries.Skip(factorSeries.Count * 2 / 3).ToList();
            double lateSwing = late.Count > 1 ? late.Max() - late.Min() : 1;
            bool bounded = factorSeries.All(f => f >= 0.4 && f <= 2.5);
            bool converged = lateSwing < 0.20;
            bool pass = obsN >= 10 && bounded && converged;
            Record("overlay honesty: correction factors bounded and settling", pass,
                $"{obsN:F0} realized-vs-predicted observations; factor range " +
                $"[{factorSeries.Min():F2},{factorSeries.Max():F2}], late swing {lateSwing:F2}");
        }

        // ------------------------------------------------------------------
        private static void PerformanceShape(ulong seed)
        {
            double RefreshCost(int parcelsPerCluster)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config
                { Seed = seed, SeedHouseholds = 6000, ParcelsPerCluster = parcelsPerCluster };
                var sim = Sim.Create(cfg, p, new FeatureFlags());
                sim.Run(60);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 12; i++) sim.Engine.Access.Refresh(sim.W, sim.Access, p);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / 12;
            }
            double t1 = RefreshCost(8), t2 = RefreshCost(16);
            double ratio = t2 / Math.Max(0.01, t1);
            bool pass = ratio < 1.8;
            Record("Tier B refresh scales with clusters, not parcel count", pass,
                $"refresh {t1:F1} ms at 8 parcels/cluster vs {t2:F1} ms at 16 → ratio {ratio:F2} (cluster count fixed)");
        }

        // ------------------------------------------------------------------
        private static double Spearman(List<double> a, List<double> b)
        {
            int n = a.Count;
            if (n < 3) return 0;
            double[] ra = Ranks(a), rb = Ranks(b);
            double ma = ra.Average(), mb = rb.Average();
            double cov = 0, va = 0, vb = 0;
            for (int i = 0; i < n; i++)
            {
                cov += (ra[i] - ma) * (rb[i] - mb);
                va += (ra[i] - ma) * (ra[i] - ma);
                vb += (rb[i] - mb) * (rb[i] - mb);
            }
            return va > 0 && vb > 0 ? cov / Math.Sqrt(va * vb) : 0;
        }

        private static double[] Ranks(List<double> xs)
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
    }
}
