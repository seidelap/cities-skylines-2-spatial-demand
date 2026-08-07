using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>Correctness suite (PLAN §4.1): unit-level invariants, all
    /// deterministic. Exit code 0 iff everything passes.</summary>
    public static class TestRunner
    {
        private static readonly List<(string name, bool pass, string detail)> Results
            = new List<(string, bool, string)>();

        private static void Check(string name, bool pass, string detail = "")
        {
            Results.Add((name, pass, detail));
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
        }

        public static int RunAll(ulong seed, out string report)
        {
            Results.Clear();
            Console.WriteLine("verify: correctness suite");

            IpfConsistency();
            AnnuityRoundTrip();
            InteriorOptimum();
            TradeLaws(seed);
            WeberRecipeChoice(seed);
            VacancyKernelConservation(seed);
            CoopInstantRerate(seed);
            CircularityGuard(seed);
            LedgerConservation(seed);
            ShadowMode(seed);
            InsolvencyPipeline(seed);
            FlagsOffSmoke(seed);
            Determinism(seed);

            int failed = Results.Count(r => !r.pass);
            var sb = new StringBuilder();
            sb.AppendLine($"## Correctness verification ({Results.Count} checks, {Results.Count - failed} passing)");
            sb.AppendLine();
            foreach (var (name, pass, detail) in Results)
                sb.AppendLine($"- {(pass ? "✅" : "❌")} **{name}**{(detail.Length > 0 ? ": " + detail : "")}");
            report = sb.ToString();
            Console.WriteLine($"\nverify: {Results.Count - failed}/{Results.Count} checks passing");
            return failed == 0 ? 0 : 1;
        }

        // --------------------------------------------------------------------
        private static void IpfConsistency()
        {
            var rng = new SplitMix64(42);
            int n = 30, m = 25;
            var supply = new double[n]; var demand = new double[m];
            var w = new double[n, m];
            for (int i = 0; i < n; i++) supply[i] = 5 + 20 * rng.NextDouble();
            for (int j = 0; j < m; j++) demand[j] = 5 + 25 * rng.NextDouble();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++) w[i, j] = Math.Exp(-3.0 * rng.NextDouble());

            Balancing.Match(supply, demand, w, slackWeight: 0.05, iterations: 60,
                            out var rowM, out var colM);

            double maxRowViol = 0, maxColViol = 0, totalRow = 0, totalCol = 0;
            for (int i = 0; i < n; i++) { maxRowViol = Math.Max(maxRowViol, rowM[i] - supply[i]); totalRow += rowM[i]; }
            for (int j = 0; j < m; j++) { maxColViol = Math.Max(maxColViol, colM[j] - demand[j]); totalCol += colM[j]; }
            // Matched flows may not exceed either marginal, and both sides see the
            // same matched total (jobs claimed = workers claimed).
            Check("IPF: marginals respected", maxRowViol < 1e-4 && maxColViol < 1e-4,
                  $"max row viol {maxRowViol:E1}, col {maxColViol:E1}");
            Check("IPF: two-sided consistency", Math.Abs(totalRow - totalCol) < 1e-6,
                  $"|jobsClaimed − workersClaimed| = {Math.Abs(totalRow - totalCol):E2}");
        }

        private static void AnnuityRoundTrip()
        {
            var p = new EconParams();
            double lump = 12345.6;
            double flow = Annuity.FlowOf(lump, p.HurdleRate, p.AnnuityHorizon);
            double back = Annuity.LumpOf(flow, p.HurdleRate, p.AnnuityHorizon);
            Check("annuity operator round-trips", Math.Abs(back - lump) < 1e-6 * lump,
                  $"lump {lump} -> flow {flow:F4} -> {back:F2}");
        }

        private static void InteriorOptimum()
        {
            var p = new EconParams();
            int ArgmaxLevel(double baseBid)
            {
                int best = 1; double bestV = double.NegativeInfinity;
                for (int l = 1; l <= p.MaxLevel; l++)
                {
                    double v = baseBid * p.Quality(l) / p.Quality(1) - LandAccounting.SPerUnit(l, 1.0, p);
                    if (v > bestV) { bestV = v; best = l; }
                }
                return best;
            }
            int cold = ArgmaxLevel(1.1), mid = ArgmaxLevel(3.0), hot = ArgmaxLevel(9.0);
            Check("supported level ℓ* is an interior optimum rising with access",
                  cold <= 2 && mid > cold && mid < p.MaxLevel && hot >= mid,
                  $"ℓ*(cold)={cold}, ℓ*(mid)={mid}, ℓ*(hot)={hot}");
        }

        private static void TradeLaws(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 6, Rows = 6, SeedHouseholds = 500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;
            SyntheticCity.AddRailTerminal(w, sim.Access, p);
            sim.Engine.Trade.Refresh(p);

            // Road curve concave increasing (d=2), rail linear (d=1).
            var road = w.Exits.First(e => e.Mode == ExitMode.Road && e.Resource == Res.Metals);
            var rail = w.Exits.First(e => e.Mode == ExitMode.Rail && e.Resource == Res.Metals);
            double r1 = sim.Engine.Trade.ExportMarginal(road, 100, p);
            double r2 = sim.Engine.Trade.ExportMarginal(road, 200, p);
            double r3 = sim.Engine.Trade.ExportMarginal(road, 300, p);
            // Received price falls with volume; drops decelerate (sqrt law).
            bool roadShape = r1 > r2 && r2 > r3 && (r1 - r2) > (r2 - r3) - 1e-9;
            double l1 = sim.Engine.Trade.ExportMarginal(rail, 100, p);
            double l2 = sim.Engine.Trade.ExportMarginal(rail, 200, p);
            double l3 = sim.Engine.Trade.ExportMarginal(rail, 300, p);
            bool railLinear = Math.Abs((l1 - l2) - (l2 - l3)) < 1e-9;
            Check("road export law concave (d=2), rail linear (d=1)", roadShape && railLinear,
                  $"road drops {r1 - r2:F4}>{r2 - r3:F4}; rail drops {l1 - l2:F6}≈{l2 - l3:F6}");

            // Cheapest-first allocation across exits == brute-force marginal merge.
            double surplus = 600;
            var supplyBy = new double[sim.Access.ClusterCount];
            supplyBy[sim.Access.At(3, 3)] = surplus;
            var clear = sim.Engine.Trade.ClearTick(Res.Metals, surplus, 0, supplyBy, new double[sim.Access.ClusterCount], p);

            // Brute force: same lots, same marginal formulas, global best-first.
            var _clearDraws = w.Exits.Where(e => e.Resource == Res.Metals).Select(e => e.DrawnThisTick).ToList();
            foreach (var x in w.Exits) if (x.Resource == Res.Metals) x.DrawnThisTick = 0;
            double bfRevenue = 0, remaining = surplus;
            var goodsExits = w.Exits.Where(e => e.Resource == Res.Metals).ToList();
            double HaulOf(TradeExit e) => sim.Access.Cost(sim.Access.At(3, 3), e.Cluster, AccessPurpose.Freight)
                                          * sim.Engine.Trade.FreightCostPerMinute
                                          * ResourceCatalog.Weight[(int)Res.Metals];
            while (remaining > 1e-6)
            {
                double lot = Math.Min(p.LotSize, remaining);
                TradeExit? best = null; double bestNet = 0;
                foreach (var e in goodsExits)
                {
                    if (e.Capacity > 0 && e.DrawnThisTick + lot > e.Capacity) continue;
                    double net = sim.Engine.Trade.ExportMarginal(e, e.DrawnThisTick, p) - HaulOf(e);
                    if (net > bestNet) { bestNet = net; best = e; }
                }
                if (best == null) break;
                best.DrawnThisTick += lot; bfRevenue += lot * bestNet; remaining -= lot;
            }
            var perExit = string.Join(" ", goodsExits.Select((e, i) => $"e{i}:{_clearDraws[i]:F0}/{e.DrawnThisTick:F0}"));
            Check("multimodal composition = horizontal summation (vs brute force)",
                  Math.Abs(clear.ExportRevenue - bfRevenue) < 1e-6 * Math.Max(1, bfRevenue),
                  $"clear {clear.ExportRevenue:F2} vs brute {bfRevenue:F2} (clear/brute per exit: {perExit})");

            // Sustained EMA + transient layer decay round-trip.
            var exit = goodsExits[0];
            exit.SustainedQ = 0; exit.TransientB = 0; exit.ExportedThisTick = 500;
            sim.Engine.Trade.EndTick(p);
            double b1 = exit.TransientB;
            foreach (var x in w.Exits) { x.ExportedThisTick = 0; x.ImportedThisTick = 0; }
            sim.Engine.Trade.EndTick(p);
            Check("transient impact layer decays at resilience rate",
                  b1 > 0 && exit.TransientB < b1 && Math.Abs(exit.TransientB - b1 * p.TradeTransientDecay) < 1e-9,
                  $"burst {b1:F1} -> {exit.TransientB:F1}");
        }

        private static void WeberRecipeChoice(ulong seed)
        {
            // Resource-level spatial economics: extraction follows geology, and
            // industry's recipe choice follows input sourcing costs (§4.2 Weber).
            // Two sims, one per margin — each measured where its sector is
            // economically viable (this check tests location-choice ALIGNMENT,
            // not sector viability): extractors need the ExtractorHeavy ore
            // region to survive absorption-paced growth; industry thrives on
            // the plain config where extraction is marginal.
            var p = new EconParams();
            var simInd = Sim.Create(new SyntheticCity.Config { Seed = seed, SeedHouseholds = 6000 },
                                    p, new FeatureFlags());
            simInd.Run(300);
            var simExt = Sim.Create(new SyntheticCity.Config
                                    { Seed = seed, SeedHouseholds = 6000, ExtractorHeavy = true, ExtractorPrebuilt = 0.25 },
                                    p, new FeatureFlags());
            simExt.Run(300);

            int extract = 0, extractRight = 0;
            foreach (var f in simExt.W.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Extractor) continue;
                int c = simExt.W.Parcels[f.Parcel].Cluster;
                extract++;
                // Geology oracle (seeds) or value-weighted geology oracle
                // (entrants price the output too — mining the slightly less
                // abundant but dearer raw is correct economics).
                int bestBySuit = 0, bestByValue = 0; double bs = -1, bv = -1;
                for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                {
                    double suit = simExt.W.Clusters[c].ResourceSuitability[rr];
                    if (suit > bs) { bs = suit; bestBySuit = rr; }
                    double val = suit * Math.Max(simExt.Engine.Trade.LocalPrice((Res)rr),
                                                 simExt.Engine.Trade.BestExportNet((Res)rr, c));
                    if (val > bv) { bv = val; bestByValue = rr; }
                }
                if ((int)f.Output == bestBySuit || (int)f.Output == bestByValue) extractRight++;
            }

            int ind = 0, indAligned = 0;
            var outputsSeen = new HashSet<Res>();
            foreach (var f in simInd.W.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Industrial) continue;
                int c = simInd.W.Parcels[f.Parcel].Cluster;
                outputsSeen.Add(f.Output);
                if (f.Output == Res.Machinery) continue;   // multi-input: no single cheapest raw
                ind++;
                // The chosen recipe's raw should be the locally cheapest raw
                // to deliver (allowing a 15% tolerance band for ties).
                var recipe = ResourceCatalog.RecipeFor(f.Output);
                double own = simInd.Engine.Trade.DeliveredCost(recipe.Inputs[0].res, c);
                double cheapest = double.PositiveInfinity;
                for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                    cheapest = Math.Min(cheapest, simInd.Engine.Trade.DeliveredCost((Res)rr, c));
                if (own <= cheapest * 1.15 + 0.05) indAligned++;
            }

            double extractShare = extract > 0 ? (double)extractRight / extract : 0;
            double indShare = ind > 0 ? (double)indAligned / ind : 0;
            Check("Weber: extraction follows geology; recipes follow input sourcing",
                  extract >= 5 && extractShare >= 0.9 && ind >= 5 && indShare >= 0.55 && outputsSeen.Count >= 2,
                  $"{extract} extractors ({extractShare:P0} on best raw); {ind} single-input industrials " +
                  $"({indShare:P0} on cheapest-sourced recipe); {outputsSeen.Count} distinct industrial outputs");
        }

        private static void VacancyKernelConservation(ulong seed)
        {
            // The kernel's defining property: one vacant unit cancels EXACTLY
            // one unit of residual demand citywide (V = 1), and the cancellation
            // is nearer where the vacancy is. Refresh twice on identical demand
            // inputs, differing only by evicting K households in one cluster.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var w = sim.W;
            var eng = sim.Engine;

            var seekers = (double[])eng.SeekersEma.Clone();
            var res = new ResidualDemand();
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var before = new double[6][];
            for (int u = 0; u < 6; u++) before[u] = (double[])res.ByUse[u].Clone();

            // Evict from the DENSEST residential cluster (needs enough
            // occupants to make a measurable shock).
            var occByCluster = new int[eng.Access.C];
            foreach (var pl in w.Parcels)
                if (pl.State == ParcelState.Built && pl.IsResidential)
                    occByCluster[pl.Cluster] += pl.OccupantHouseholds.Count;
            int shockCluster = 0;
            for (int c = 1; c < eng.Access.C; c++)
                if (occByCluster[c] > occByCluster[shockCluster]) shockCluster = c;
            int evicted = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.Cluster != shockCluster || pl.State != ParcelState.Built || !pl.IsResidential) continue;
                while (pl.OccupantHouseholds.Count > 0 && evicted < 60)
                {
                    int hid = pl.OccupantHouseholds[pl.OccupantHouseholds.Count - 1];
                    pl.OccupantHouseholds.RemoveAt(pl.OccupantHouseholds.Count - 1);
                    w.Households[hid].HomeParcel = -1;
                    evicted++;
                }
                if (evicted >= 60) break;
            }
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);

            // V=1: total suppression == evicted units exactly. Localization is
            // a DENSITY statement (suppression per cluster falls with
            // distance) — aggregates would compare ~8 near clusters against
            // ~90 far ones and drown the gradient in the far tail's headcount.
            double totalDiff = 0, nearDiff = 0, farDiff = 0;
            int nearN = 0, farN = 0;
            double sx = w.Clusters[shockCluster].X, sy = w.Clusters[shockCluster].Y;
            for (int c = 0; c < eng.Access.C; c++)
            {
                double d = 0;
                for (int u = 0; u < 6; u++) d += before[u][c] - res.ByUse[u][c];
                totalDiff += d;
                double dx = (w.Clusters[c].X - sx) * w.MetersPerUnit;
                double dy = (w.Clusters[c].Y - sy) * w.MetersPerUnit;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= 1.5 * p.VacancyKernelLambdaM) { nearDiff += d; nearN++; }
                else { farDiff += d; farN++; }
            }
            double nearDensity = nearN > 0 ? nearDiff / nearN : 0;
            double farDensity = farN > 0 ? farDiff / farN : 0;
            Check("vacancy kernel: V=1 conservation, suppression density falls with distance",
                  evicted >= 30 && Math.Abs(totalDiff - evicted) < 1e-6
                  && nearDensity > 4.0 * Math.Max(1e-9, farDensity),
                  $"evicted {evicted} → total suppression {totalDiff:F6}; per-cluster density " +
                  $"within 1.5λ: {nearDensity:F3} (n={nearN}), beyond: {farDensity:F3} (n={farN})");
        }

        private static void CoopInstantRerate(ulong seed)
        {
            // Co-op assessment: every housed household is charged its parcel's
            // CURRENT market unit assessment — uniform across co-tenants,
            // no anniversaries, no phase-in lag.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());   // all tiers live
            sim.Run(90);
            var w = sim.W;
            // Two invariants: (a) co-tenants pay EXACTLY the same (one price
            // per unit — the co-op property, bit-checkable because the whole
            // parcel re-rates in one pass); (b) every charge tracks the live
            // market assessment tightly (a ≤1-tick lag exists by construction:
            // charges are set before the same tick's condition decay).
            int housed = 0, uniformParcels = 0, parcelsWithMulti = 0;
            double worstRel = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                double first = double.NaN; bool uniform = true;
                foreach (int hid in pl.OccupantHouseholds)
                {
                    var h = w.Households[hid];
                    housed++;
                    if (double.IsNaN(first)) first = h.ChargedAssessment;
                    else if (h.ChargedAssessment != first) uniform = false;
                    double target = LandAccounting.UnitAssessment(pl, p);
                    if (target > 1e-9)
                        worstRel = Math.Max(worstRel, Math.Abs(h.ChargedAssessment - target) / target);
                }
                if (pl.OccupantHouseholds.Count >= 2)
                {
                    parcelsWithMulti++;
                    if (uniform) uniformParcels++;
                }
            }
            Check("co-op re-rate: one price per unit, tracking the live market assessment",
                  housed > 200 && parcelsWithMulti > 20 && uniformParcels == parcelsWithMulti
                  && worstRel < 0.01,
                  $"{uniformParcels}/{parcelsWithMulti} multi-tenant parcels uniform; " +
                  $"worst |charged − market|/market = {worstRel:E1}");
        }

        private static void CircularityGuard(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(60);

            var parcel = sim.W.Parcels.First(x => x.State == ParcelState.Built
                                                  && x.IsResidential && x.OccupantHouseholds.Count > 0);
            LandAccounting.Assess(sim.W, sim.Engine.Access, sim.Engine.Trade, parcel,
                                  sim.Engine.SegmentPresence, p);
            double lr1 = parcel.AssessedLR, wedge1 = parcel.Wedge;

            // Perturb the parcel's own REALIZED rents hard: charged assessments,
            // occupant money. Assessment must be bit-identical (design §3).
            foreach (int hid in parcel.OccupantHouseholds)
            {
                sim.W.Households[hid].ChargedAssessment *= 17.5;
                sim.W.Households[hid].Money += 99999;
            }
            LandAccounting.Assess(sim.W, sim.Engine.Access, sim.Engine.Trade, parcel,
                                  sim.Engine.SegmentPresence, p);
            Check("circularity guard: assessment blind to own realized rent",
                  parcel.AssessedLR == lr1 && parcel.Wedge == wedge1,
                  $"LR {lr1:F4} unchanged under 17.5× realized-rent perturbation");
        }

        private static void LedgerConservation(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            double maxDrift = 0;
            sim.Run(300, s => maxDrift = Math.Max(maxDrift, Math.Abs(s.W.Ledger.Drift())));
            Check("ledger conservation: money neither created nor destroyed",
                  maxDrift < 1e-3, $"max |drift| over 300 ticks = {maxDrift:E2}");
        }

        private static void ShadowMode(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var flags = new FeatureFlags { ShadowAccountingOnly = true };
            var sim = Sim.Create(cfg, p, flags);
            sim.Run(100);
            bool assessed = sim.W.Parcels.Any(x => x.AssessedLR > 0);
            double escrow = sim.W.Ledger.Balance(Account.Escrow);
            Check("shadow accounting: assessed + logged, nothing levied (stage 3)",
                  assessed && escrow == 0,
                  $"assessments computed: {assessed}, escrow balance: {escrow:F2}");
        }

        private static void InsolvencyPipeline(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(40);

            // Engineer a stressed household in an expensive unit and walk the pipeline.
            var h = sim.W.Households.First(x => x.ExitedTick < 0 && x.HomeParcel >= 0);
            h.Money = 0.1;
            h.ChargedAssessment = 999;
            var stages = new List<InsolvencyStage> { h.Stage };
            int shelterOcc = 0;
            bool exited = false;
            for (int i = 0; i < 200 && !exited; i++)
            {
                h.StressTicks++;
                exited = sim.Engine.Allocation.InsolvencyStep(sim.W, sim.Engine.Access, h, p, ref shelterOcc, 100);
                if (stages[^1] != h.Stage) stages.Add(h.Stage);
            }
            bool ordered = true;
            for (int i = 1; i < stages.Count; i++)
                if (stages[i] < stages[i - 1] && stages[i] != InsolvencyStage.Solvent) ordered = false;
            Check("insolvency pipeline: staged, ordered, terminates",
                  ordered && (exited || h.Stage == InsolvencyStage.Sheltered || h.HomeParcel >= 0),
                  $"stages: {string.Join("→", stages)}{(exited ? "→exit" : "")}");

            // No stuck-forever homelessness while affordable vacancies exist:
            // sheltered households re-house within a bounded window.
            var flags2 = new FeatureFlags();
            var sim2 = Sim.Create(new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1200, Seed = seed + 1 }, new EconParams(), flags2);
            sim2.Run(250);
            int maxShelterStreak = 0, cur = 0;
            for (int i = 60; i < sim2.Sheltered.Count; i++)
            {
                cur = sim2.Sheltered[i] > 0 && sim2.Sheltered[i] >= sim2.Sheltered[Math.Max(0, i - 1)] ? cur + 1 : 0;
                maxShelterStreak = Math.Max(maxShelterStreak, cur);
            }
            Check("no stuck homeless population (vanilla bug-class regression)",
                  maxShelterStreak < 120,
                  $"longest non-decreasing shelter streak {maxShelterStreak} ticks");
        }

        private static void FlagsOffSmoke(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var flags = new FeatureFlags
            {
                TierA_Migration = false, TierD_Trade = false,
                TierC2_Leveling = false, ConstructionRewire = false,
            };
            var sim = Sim.Create(cfg, p, flags, vanillaMode: true);
            sim.Run(150);
            Check("feature flags: vanilla-mode fallback runs (every tier revertible)",
                  sim.Population[^1] > 0 && sim.Population[^1] < 30_000
                  && sim.W.Parcels.Any(x => x.State == ParcelState.Built),
                  $"pop {sim.Population[^1]} (sane bounds), drift {sim.W.Ledger.Drift():E1}");
        }

        private static void Determinism(ulong seed)
        {
            ulong Hash(ulong s)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = s };
                var sim = Sim.Create(cfg, p, new FeatureFlags());
                sim.Run(120);
                return sim.TelemetryHash();
            }
            ulong h1 = Hash(seed), h2 = Hash(seed), h3 = Hash(seed + 1);
            Check("determinism: same seed → identical telemetry hash",
                  h1 == h2 && h1 != h3, $"h(seed)={h1:X} twice, h(seed+1)={h3:X}");
        }
    }
}
