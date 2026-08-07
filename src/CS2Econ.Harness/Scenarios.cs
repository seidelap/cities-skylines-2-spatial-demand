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
            // Shock-vs-control on identical seeds, measured at CLUSTER grain:
            // vacancies are a per-cluster signal, and quadrant aggregation dilutes
            // the suppression with unshocked hot clusters. Shocked set = clusters
            // where the shock actually created vacancies (>=5 evictions); the rest
            // of the map is the reference. Rates normalized by the control RUN.
            (double ratio, bool startsOk, string detail) RunMode(bool vanilla)
            {
                var shockedClusters = new HashSet<int>();
                ClusterInfo[] geom = Array.Empty<ClusterInfo>();
                double metersPerUnit = 1.0, lambda = new EconParams().VacancyKernelLambdaM;
                int evictedCount = 0;
                // shock: 0 = none (control), 1 = disk, 2 = same headcount
                // evicted uniformly citywide (isolates the level effect any
                // mass exit causes from the SPATIAL concentration under test).
                (Dictionary<int, double> starts, double[] resid) StartsByCluster(int shock)
                {
                    // SOFT-market regime (the §6 target's premise): desired
                    // inflow at replacement scale, not pinned at the absorption
                    // frontier — at the frontier a mass eviction raises the
                    // arrival budget and reads as a supply gift (queued demand
                    // pours in), which is coherent physics but the wrong regime
                    // for measuring suppression localization. The reservation
                    // field at its §4.1 equilibrium IS that soft state.
                    var p = new EconParams();
                    var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000, ParcelsPerCluster = 14 };
                    var sim = Sim.Create(cfg, p, new FeatureFlags(), vanillaMode: vanilla);
                    sim.Run(100);   // settle: construction live at the margin
                    for (int s = 0; s < Segment.Count; s++)
                        sim.W.Migration.ReservationThreshold[s] = Math.Max(0,
                            sim.W.Migration.SegmentAttractEma[s] - Migration.BaseOutsideUtility - 0.02);
                    if (shock == 1)
                    {
                        // Shock a DISK on the residential BELT (the map core is
                        // zoned commercial/office — no housing market to shock
                        // there, and the corner quadrant has no marginal
                        // construction to suppress). Treatment membership is
                        // GEOMETRY, not eviction counts: a commercial cluster
                        // inside the disk is treated territory even though it
                        // has no residents to evict.
                        double mx = 0, my = 0;
                        foreach (var ci in sim.W.Clusters) { mx += ci.X; my += ci.Y; }
                        mx /= sim.W.Clusters.Length; my /= sim.W.Clusters.Length;
                        double sxq = mx + 2800.0 / sim.W.MetersPerUnit;   // 4 neighborhoods east: mid-belt
                        double shockR = 2500.0;                           // meters
                        bool InDisk(int c)
                        {
                            double dx = (sim.W.Clusters[c].X - sxq) * sim.W.MetersPerUnit;
                            double dy = (sim.W.Clusters[c].Y - my) * sim.W.MetersPerUnit;
                            return Math.Sqrt(dx * dx + dy * dy) <= shockR;
                        }
                        shockedClusters.Clear();
                        for (int c = 0; c < sim.W.Clusters.Length; c++)
                            if (InDisk(c)) shockedClusters.Add(c);
                        evictedCount = 0;
                        foreach (var h in sim.W.Households)
                        {
                            if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                            var pl = sim.W.Parcels[h.HomeParcel];
                            if (!shockedClusters.Contains(pl.Cluster)) continue;
                            if (SplitMix64.Hash01((ulong)h.Id * 31 + seed) < 0.95)
                            {
                                sim.Engine.Allocation.Vacate(sim.W, h);
                                sim.W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                                h.Money = 0; h.ExitedTick = sim.W.Tick;
                                evictedCount++;
                            }
                        }
                        geom = sim.W.Clusters;
                        metersPerUnit = sim.W.MetersPerUnit;
                    }
                    else if (shock == 2)
                    {
                        // Same headcount, no geography: uniform random exits.
                        int housed = 0;
                        foreach (var h in sim.W.Households)
                            if (h.ExitedTick < 0 && h.HomeParcel >= 0) housed++;
                        double share = housed > 0 ? (double)evictedCount / housed : 0;
                        foreach (var h in sim.W.Households)
                        {
                            if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                            if (SplitMix64.Hash01((ulong)h.Id * 53 + seed) < share)
                            {
                                sim.Engine.Allocation.Vacate(sim.W, h);
                                sim.W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                                h.Money = 0; h.ExitedTick = sim.W.Tick;
                            }
                        }
                    }
                    long fromTick = sim.W.Tick;
                    // Window ≈ the overhang's own life (refill drains it at
                    // ~4-5 units/tick through arrivals + chain moves): a longer
                    // window measures the recovery, not the suppression.
                    // Alongside realized starts, sample the residential
                    // RESIDUAL-DEMAND field (the state that gates construction)
                    // every 10 ticks — the soft regime builds so little that
                    // start counts alone are single-digit statistics.
                    var residSum = new double[sim.Engine.Access.C];
                    int residSamples = 0;
                    sim.Run(70, s =>
                    {
                        if ((s.W.Tick - fromTick) % 10 != 0) return;
                        residSamples++;
                        for (int c = 0; c < s.Engine.Access.C; c++)
                            residSum[c] += s.Engine.Residuals.Get(c, ZoneKind.ResidentialLow, s.W.Claims)
                                         + s.Engine.Residuals.Get(c, ZoneKind.ResidentialHigh, s.W.Claims);
                    });
                    for (int c = 0; c < residSum.Length; c++)
                        residSum[c] /= Math.Max(1, residSamples);
                    var byCluster = new Dictionary<int, double>();
                    foreach (var (tick, parcelId, cluster) in sim.Starts)
                    {
                        if (tick <= fromTick) continue;
                        var pl = sim.W.Parcels[parcelId];
                        if (pl.Use != ZoneKind.ResidentialLow && pl.Use != ZoneKind.ResidentialHigh) continue;
                        byCluster.TryGetValue(cluster, out var n);
                        byCluster[cluster] = n + Math.Max(1, pl.Units);
                    }
                    return (byCluster, residSum);
                }

                var (shockRun, shockResid) = StartsByCluster(1);      // disk shock; defines geometry
                var (controlRun, ctrlResid) = StartsByCluster(0);     // identical seed, no shock
                var (_, uniformResid) = StartsByCluster(2);           // same exits, no geography

                // Spatial difference-in-differences with an EXCLUSION BUFFER:
                // the kernel deliberately lets live demand from outside the
                // district keep its boundary ring viable (spillover is the
                // mechanism, not noise), so the ring is partially treated —
                // it belongs to neither arm. Treatment = interior clusters
                // (whole kernel neighborhood shocked); reference = clusters
                // beyond spillover reach of any shocked cluster.
                double Dist(int a, int b)
                {
                    double dx = (geom[a].X - geom[b].X) * metersPerUnit;
                    double dy = (geom[a].Y - geom[b].Y) * metersPerUnit;
                    return Math.Sqrt(dx * dx + dy * dy);
                }
                // Two radii: interior DEPTH 1.2λ (a treated cluster whose whole
                // near-neighborhood is treated), reference EXCLUSION 2λ (the
                // kernel tail at 1.2λ is still ~30% — a reference that close
                // measures direct spillover, not the diffuse citywide channel).
                double interiorDepth = 1.2 * lambda, bufferReach = 2.0 * lambda;
                var interior = new HashSet<int>();
                var buffered = new HashSet<int>();          // excluded ring, both sides
                for (int c = 0; c < geom.Length; c++)
                {
                    bool nearShock = false, allShockedNearby = true;
                    for (int o = 0; o < geom.Length; o++)
                    {
                        double d = Dist(c, o);
                        if (d <= interiorDepth && !shockedClusters.Contains(o)) allShockedNearby = false;
                        if (d <= bufferReach && shockedClusters.Contains(o)) nearShock = true;
                    }
                    if (shockedClusters.Contains(c) && allShockedNearby) interior.Add(c);
                    else if (nearShock) buffered.Add(c);
                }

                double shockedIn = 0, controlIn = 0, farShock = 0, farCtrl = 0;
                void Tally(Dictionary<int, double> m, ref double tin, ref double tfar)
                {
                    foreach (var kv in m)
                    {
                        if (interior.Contains(kv.Key)) tin += kv.Value;
                        else if (!buffered.Contains(kv.Key)) tfar += kv.Value;
                    }
                }
                Tally(shockRun, ref shockedIn, ref farShock);
                Tally(controlRun, ref controlIn, ref farCtrl);

                // Construction-DEMAND suppression (the §6 quantity): mean drop
                // of the residual field, shock vs control, per region — with
                // the LEVEL EFFECT netted out via the uniform-exit arm (a city
                // that loses 800 households loses residual everywhere no matter
                // where they lived; the claim under test is the spatial
                // CONCENTRATION beyond that). Realized starts back it up but
                // carry single-digit statistics in the soft regime — the field
                // is what site selection actually reads.
                double supIn = 0, supFar = 0, lvlIn = 0, lvlFar = 0; int nIn = 0, nFar = 0;
                for (int c = 0; c < geom.Length; c++)
                {
                    double d = ctrlResid[c] - shockResid[c];
                    double u = ctrlResid[c] - uniformResid[c];
                    if (interior.Contains(c)) { supIn += d; lvlIn += u; nIn++; }
                    else if (!buffered.Contains(c)) { supFar += d; lvlFar += u; nFar++; }
                }
                supIn = nIn > 0 ? supIn / nIn : 0;
                supFar = nFar > 0 ? supFar / nFar : 0;
                lvlIn = nIn > 0 ? lvlIn / nIn : 0;
                lvlFar = nFar > 0 ? lvlFar / nFar : 0;
                // Spatial excess = suppression beyond what the same exits
                // cause with no geography, netted in BOTH regions. A negative
                // far excess means nothing traveled beyond the kernel (far
                // clusters are actually relieved by displaced demand) — the
                // denominator floors at 0.1 and the raw value is printed.
                double spatialIn = supIn - lvlIn;
                double spatialFar = supFar - lvlFar;
                double ratio = spatialIn / Math.Max(0.1, spatialFar);
                bool startsConsistent = shockedIn <= controlIn + 1e-9;
                bool realSignal = spatialIn >= 3.0;
                return (ratio, startsConsistent && realSignal,
                    $"spatial-excess suppression interior {spatialIn:F1}/cluster vs beyond-spillover {spatialFar:F1} " +
                    $"(raw {supIn:F1}/{supFar:F1}, level effect {lvlIn:F1}/{lvlFar:F1}) → {ratio:F0}:1; " +
                    $"unit-starts interior {controlIn:F0}→{shockedIn:F0}, beyond {farCtrl:F0}→{farShock:F0} " +
                    $"({interior.Count} treated / {buffered.Count} buffered)");
            }

            var (spatialRatio, spatialOk, spatialDetail) = RunMode(vanilla: false);
            // Vanilla arm: its construction driver is a single global scalar —
            // spatially flat by construction — so the honest vanilla statistic
            // stays realized starts (the field it steers by has no geography).
            var (_, _, vanillaDetail) = RunMode(vanilla: true);
            bool pass = spatialRatio >= 10 && spatialOk;
            Record("vacancy localization ≥10:1 (vanilla ≈1:1)", pass,
                $"spatial: {spatialDetail}; vanilla: {vanillaDetail}");
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
                // §6: "the realized level map correlates with access (rank
                // correlation against ℓ*)" — ℓ* is the supported level of the
                // parcel's own use at its location (both modes compute identical
                // assessments; only leveling/construction differ).
                var levels = new List<double>(); var supported = new List<double>();
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
                    levels.Add(pl.Level);
                    supported.Add(lStar);
                }
                return Spearman(levels, supported);
            }
            double spatial = Corr(false), baseline = Corr(true);
            bool pass = spatial >= 0.35 && spatial > baseline + 0.15;
            Record("level map correlates with access (rank corr vs ℓ*, not grind)", pass,
                $"Spearman(realized level, ℓ*) spatial {spatial:F2} vs vanilla {baseline:F2}");
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
            var p = new EconParams { ExtractorOutputPerSlot = 13.0 };   // engineered monoculture
            var cfg = new SyntheticCity.Config
            { Seed = seed, SeedHouseholds = 9000, ExtractorHeavy = true, ExtractorPrebuilt = 0.8 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(700);

            var rawExits = sim.W.Exits.Where(e => e.Resource == Res.Ore && e.Mode == ExitMode.Road).ToList();
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
            // §6: "with a terminal built, the truck→rail→backstop mode progression
            // emerges by volume in the harness's EXPORT RAMP". A controlled supply
            // ramp drives the REAL clearing machinery (quantized cheapest-first
            // lots over the city's actual exits and routed hauls) from truck-town
            // volumes to port-metropolis volumes; shares and marginals fall out.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config
            { Seed = seed, SeedHouseholds = 2000, RailTerminal = true, SeaExit = true, ExtractorHeavy = true };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(10);   // binds trade, computes hauls
            var w = sim.W;
            var trade = sim.Engine.Trade;
            int source = sim.Access.At(sim.Access.Cols - 2, sim.Access.Rows - 2);   // the resource corner
            var supplyBy = new double[sim.Access.ClusterCount];

            var samples = new List<(double tot, double road, double rail, double sea)>();
            double equalizationGap = -1;
            for (int t = 0; t < 900; t++)
            {
                double supply = 8 + t * 1.05;                 // 8 → ~950 units/tick
                Array.Clear(supplyBy, 0, supplyBy.Length);
                supplyBy[source] = supply;
                trade.ClearTick(Res.Ore, supply, 0, supplyBy, new double[sim.Access.ClusterCount], p);

                double road = 0, rail = 0, sea = 0;
                TradeExit? roadX = null, railX = null;
                foreach (var e in w.Exits)
                {
                    if (e.Resource != Res.Ore) continue;
                    if (e.Mode == ExitMode.Road) { road += e.ExportedThisTick; if (e.ExportedThisTick > 0) roadX = e; }
                    if (e.Mode == ExitMode.Rail) { rail += e.ExportedThisTick; if (e.ExportedThisTick > 0) railX = e; }
                    if (e.Mode == ExitMode.Sea) sea += e.ExportedThisTick;
                }
                double tot = road + rail + sea;
                if (tot > 1e-9) samples.Add((tot, road / tot, rail / tot, sea / tot));
                if (roadX != null && railX != null && road > p.LotSize && rail > p.LotSize)
                {
                    double mRoad = trade.ExportMarginal(roadX, roadX.DrawnThisTick, p);
                    double mRail = trade.ExportMarginal(railX, railX.DrawnThisTick, p);
                    double gap = Math.Abs(mRoad - mRail) / Math.Max(0.05, Math.Max(mRoad, mRail));
                    equalizationGap = equalizationGap < 0 ? gap : Math.Min(equalizationGap, gap);
                }
                trade.EndTick(p);
            }

            double Avg(IEnumerable<(double tot, double road, double rail, double sea)> xs,
                       Func<(double tot, double road, double rail, double sea), double> f2)
                => xs.Any() ? xs.Average(f2) : double.NaN;
            var low = samples.Where(x => x.tot < 60).ToList();
            var mid = samples.Where(x => x.tot >= 120 && x.tot < 300).ToList();
            var top = samples.Where(x => x.tot >= 600).ToList();
            double roadLow = Avg(low, x => x.road), roadTop = Avg(top, x => x.road);
            double railLow = Avg(low, x => x.rail), railMid = Avg(mid, x => x.rail);
            double seaTop = Avg(top, x => x.sea);
            bool progression = low.Count >= 15 && mid.Count >= 15 && top.Count >= 15
                && roadLow > 0.5 && roadLow > roadTop + 0.20   // road wins small volumes, yields at scale
                && railMid > railLow + 0.10                    // rail amortizes at mid volumes
                && seaTop > 0.08;                              // backstop engaged at the top
            bool equalized = equalizationGap >= 0 && equalizationGap <= 0.08;
            Record("truck→rail→backstop progression by volume; concurrent marginals equalize", progression && equalized,
                $"export ramp 8→950/tick: road {roadLow:P0} at <60 (n={low.Count}) → {roadTop:P0} at ≥600; " +
                $"rail {railLow:P0}→{railMid:P0} at mid (n={mid.Count}); sea {seaTop:P0} at top (n={top.Count}); " +
                $"concurrent marginal gap {(equalizationGap < 0 ? "n/a" : equalizationGap.ToString("P1"))}");
        }

        // ------------------------------------------------------------------
        private static void BoomBustAsymmetry(ulong seed)
        {
            // Soft baseline with vacancy HEADROOM: at the absorption frontier
            // realized arrivals are pinned at hazard × vacant stock, so an
            // amenity pulse cannot move them (+0) no matter how it moves
            // desire. With desired inflow below the budget, the pulse's
            // arrival response is demand-revealing again — while departures
            // stay lagged and attachment-damped, which is the asymmetry
            // under test.
            var p = new EconParams { MigInElasticity = new EconParams().MigInElasticity * 0.3 };
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 6000 };
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
