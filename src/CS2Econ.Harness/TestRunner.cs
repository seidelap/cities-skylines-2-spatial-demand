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
            ClaimVacancyWash(seed);
            OccupancyChannel(seed);
            ClearingPrice(seed);
            OccupiedStockCarriesRent(seed);
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

        private static void ClearingPrice(ulong seed)
        {
            // The price is the MARKET-CLEARING price: the marginal bidder's WTP
            // at the quantity that fills the stock. Three properties, all of
            // which the old presence-weighted mean failed (it had no quantity
            // term at all): rent falls as supply grows against fixed demand,
            // rent rises as demand grows against fixed supply, and — the gap
            // this closes — a vacancy overhang SOFTENS rent instead of leaving
            // it untouched.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var acc = sim.Engine.Access;
            var pres = sim.Engine.SegmentPresence;

            // Pick a cluster with real stock and real demand.
            int c0 = 0;
            for (int c = 1; c < acc.C; c++)
                if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;
            double stock = acc.HousingStock[0][c0];

            // (a) supply ladder at fixed demand: strictly non-increasing.
            // Supply is varied through the PRODUCTION stock array (and
            // restored), so the check exercises the same read path Assess uses.
            double PriceAtStock(double units)
            {
                double saved0 = acc.HousingStock[0][c0];
                acc.HousingStock[0][c0] = units;
                double v = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p);
                acc.HousingStock[0][c0] = saved0;
                return v;
            }
            // Two-regime economics (see ResidentialBidPerUnit): while the
            // stock CLEARS, price carries the quantity response; once demand
            // exhausts, price floors FLAT at the deepest positive bidder and
            // the response moves to expected VACANCY (fillRatio). So the
            // ladders assert weak price monotonicity with at least one strict
            // step, and require the fill ratio to carry the response wherever
            // the price has gone flat.
            double FillAtStock(double units)
            {
                double saved0 = acc.HousingStock[0][c0];
                acc.HousingStock[0][c0] = units;
                LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double f);
                acc.HousingStock[0][c0] = saved0;
                return f;
            }
            double pLow = PriceAtStock(stock * 0.5);
            double pMid = PriceAtStock(stock * 2.0);
            double pHigh = PriceAtStock(stock * 8.0);
            double fMid = FillAtStock(stock * 2.0), fHigh = FillAtStock(stock * 8.0);
            bool supplyMonotone = pLow >= pMid - 1e-9 && pMid >= pHigh - 1e-9 && pLow > pHigh * 0.999 + 1e-6
                // Where the price ladder goes flat (excess regime), the
                // expected fill must fall instead: supply×8 leaves more
                // units unlet than supply×2 at the same flat price.
                && (pMid > pHigh + 1e-9 || fHigh < fMid - 1e-9);

            // (b) demand ladder at fixed supply: weakly increasing price,
            // strictly from base to double; where flat (excess), fill rises.
            var presHalf = new double[Segment.Count];
            var presDouble = new double[Segment.Count];
            for (int s = 0; s < Segment.Count; s++) { presHalf[s] = pres[s] * 0.5; presDouble[s] = pres[s] * 2.0; }
            double dLow = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, presHalf, p, out double fdLow);
            double dMid = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fdMid);
            double dHigh = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, presDouble, p, out _);
            bool demandMonotone = dLow <= dMid + 1e-9 && dMid <= dHigh + 1e-9 && dHigh > dLow + 1e-6
                && (dLow < dMid - 1e-9 || fdLow < fdMid - 1e-9);

            // (c) population collapse: with the flat-tail excess price the
            // response is regime-dependent — price falls while the submarket
            // clears, expected fill falls once it does not. Composition drift
            // (WHO remains changes the tail tranche's value) may nudge the
            // flat price a few percent either way; what must never happen is
            // the market registering NO response on either margin.
            double before = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fillBefore);
            int killed = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if (sim.W.Parcels[h.HomeParcel].Cluster == c0) continue;   // keep c0's own residents
                if (SplitMix64.Hash01((ulong)h.Id * 977 + 5) < 0.6)
                { h.ExitedTick = sim.W.Tick; killed++; }                   // citywide demand collapse
            }
            sim.Run(10);
            double after = LandAccounting.ResidentialBidPerUnit(
                sim.Engine.Access, c0, ZoneKind.ResidentialLow, 2, sim.Engine.SegmentPresence, p, out double fillAfter);
            bool softens = after < before * 0.98
                           || (fillAfter < fillBefore - 0.02 && after < before * 1.10);

            Check("clearing price: quantity responds (supply ↓, demand ↑, population ↓ — price while cleared, vacancy once flat)",
                  supplyMonotone && demandMonotone && killed > 100 && softens,
                  $"supply×{{0.5,2,8}} → {pLow:F2}/{pMid:F2}/{pHigh:F2} (fill {fMid:F2}→{fHigh:F2}); " +
                  $"demand×{{0.5,1,2}} → {dLow:F2}/{dMid:F2}/{dHigh:F2} (fill {fdLow:F2}→{fdMid:F2}); " +
                  $"after {killed} citywide exits bid {before:F2} → {after:F2}, fill {fillBefore:F2} → {fillAfter:F2}");
        }

        private static void OccupiedStockCarriesRent(ulong seed)
        {
            // Commensurability of the clearing condition, per DENSITY KIND.
            // The demand mass a queue accumulates and the stock it clears must
            // be the same kind of quantity. When mass was a per-CLUSTER share
            // compared against per-KIND stock, every density-tolerant household
            // was counted at full weight in BOTH queues while each faced only
            // its own stock: the high-density queue could never reach its stock,
            // so every apartment building priced as a permanent vacancy
            // overhang at 100% occupancy and ALL high-density land rent went to
            // exactly zero (adversarial review, three independent lenses).
            //
            // The invariant: fully-occupied stock is not an overhang. If a
            // density kind is essentially fully let citywide, a healthy share
            // of its parcels must carry positive assessed land rent.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);
            var w = sim.W;

            // Force a full reassessment so nothing is stale from the slice.
            foreach (var pl in w.Parcels)
                LandAccounting.Assess(w, sim.Engine.Access, sim.Engine.Trade, pl, sim.Engine.SegmentPresence, p);

            (int built, int positive, int units, int filled, double sumLR) Census(ZoneKind kind)
            {
                int b = 0, pos = 0, u = 0, f = 0; double lr = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != kind) continue;
                    b++; u += pl.Units; f += pl.OccupantHouseholds.Count; lr += pl.AssessedLR;
                    if (pl.AssessedLR > 1e-9) pos++;
                }
                return (b, pos, u, f, lr);
            }
            var lo = Census(ZoneKind.ResidentialLow);
            var hi = Census(ZoneKind.ResidentialHigh);

            double hiOcc = hi.units > 0 ? (double)hi.filled / hi.units : 0;
            double loOcc = lo.units > 0 ? (double)lo.filled / lo.units : 0;

            // A Ricardian extensive margin is CORRECT and must not be
            // legislated away: the worst land in use earns no rent, so a large
            // zero-LR tail among peripheral parcels is the model working. What
            // must never happen is an entire density class pinned at zero —
            // that was the commensurability bug (152/152 towers at exactly
            // zero, ΣLR == 0). So the invariant is stated on the land that is
            // NOT marginal: rank each kind's parcels by their cluster's access
            // and require the best quartile to earn rent, plus ΣLR > 0.
            var accessState = sim.Engine!.Access!;
            double AccessOf(int cluster)
            {
                double v = 0;
                for (int s = 0; s < Segment.Count; s++) v += accessState!.AccessValue[s][cluster];
                return v / Segment.Count;
            }
            (int n, int pos) BestQuartile(ZoneKind kind)
            {
                var ps = w.Parcels.Where(pl => pl.State == ParcelState.Built && pl.Use == kind)
                                  .OrderByDescending(pl => AccessOf(pl.Cluster)).ToList();
                int take = Math.Max(1, ps.Count / 4);
                int pos = ps.Take(take).Count(pl => pl.AssessedLR > 1e-9);
                return (take, pos);
            }
            var hiQ = BestQuartile(ZoneKind.ResidentialHigh);
            var loQ = BestQuartile(ZoneKind.ResidentialLow);
            bool hiOk = hi.built < 5 || hiOcc < 0.5
                        || (hi.sumLR > 1e-6 && hiQ.pos >= 0.5 * hiQ.n);
            bool loOk = lo.built < 5 || loOcc < 0.5
                        || (lo.sumLR > 1e-6 && loQ.pos >= 0.5 * loQ.n);

            Check("occupied stock carries land rent in BOTH densities (per-kind commensurability)",
                  hiOk && loOk,
                  $"high: best-access quartile {hiQ.pos}/{hiQ.n} with LR>0, ΣLR {hi.sumLR:F1}, " +
                  $"{hi.positive}/{hi.built} overall at {hiOcc:P0} occupancy; " +
                  $"low: quartile {loQ.pos}/{loQ.n}, ΣLR {lo.sumLR:F1}, " +
                  $"{lo.positive}/{lo.built} overall at {loOcc:P0}");
        }

        private static void OccupancyChannel(ulong seed)
        {
            // Does REALIZED VACANCY move rent, at fixed citywide population?
            // Two legs, because an integration test alone is confounded by the
            // allocator instantly re-housing whoever you evict:
            //   (a) AccessState.FillEma really is measured occupancy;
            //   (b) lowering FillEma lowers the clearing price.
            // Together those are the channel. Stated separately and honestly
            // because an earlier check CLAIMED to measure a vacancy overhang
            // and in fact only varied population (adversarial review).
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(120);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

            // (a) FillEma tracks measured occupancy. It is an EMA by design, so
            // the claim is that it TRACKS (small mean error), not that it
            // equals the instantaneous value.
            double sumErr = 0, worstErr = 0; int compared = 0;
            for (int k = 0; k < 2; k++)
            {
                var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
                for (int c = 0; c < acc.C; c++)
                {
                    double units = 0, occ = 0;
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == kind && pl.Cluster == c)
                        { units += pl.Units; occ += Math.Min(pl.Units, pl.OccupantHouseholds.Count); }
                    if (units < 4) continue;
                    compared++;
                    double err = Math.Abs(acc.FillEma[k][c] - occ / units);
                    sumErr += err;
                    worstErr = Math.Max(worstErr, err);
                }
            }

            // (b) the same cluster, priced at full vs collapsed occupancy —
            // population, stock, access and geometry all held identical.
            int c0 = 0;
            for (int c = 1; c < acc.C; c++)
                if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;
            double stock = acc.HousingStock[0][c0];
            double[] saved = { acc.FillEma[0][c0], acc.FillEma[1][c0] };

            void Reprice(double fill)
            {
                acc.FillEma[0][c0] = fill; acc.FillEma[1][c0] = fill;
                acc.RebuildDemandShares();      // same recompute the refresh does
            }
            Reprice(1.0);
            double bidFull = LandAccounting.ResidentialBidPerUnit(
                acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fillFull);
            Reprice(0.2);
            double bidEmpty = LandAccounting.ResidentialBidPerUnit(
                acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fillEmpty);
            acc.FillEma[0][c0] = saved[0]; acc.FillEma[1][c0] = saved[1];
            acc.RebuildDemandShares();

            // Two-regime economics: while the submarket CLEARS, the thinner
            // demand share reads deeper down the curve and the PRICE falls.
            // Once demand exhausts, the price floors flat at the deepest
            // positive bidder (cutting below them buys no tenant that
            // exists) and the channel's response moves to expected FILL —
            // the vacancy deepens instead. Either margin must respond.
            bool priceLeg = bidEmpty < bidFull * 0.95;
            bool vacancyLeg = bidEmpty <= bidFull * 1.001 && fillEmpty < fillFull * 0.8;

            double meanErr = compared > 0 ? sumErr / compared : 1;
            Check("occupancy channel: realized vacancy softens rent (price while cleared, deeper vacancy once floored)",
                  compared >= 10 && meanErr < 0.06 && worstErr < 0.5 && (priceLeg || vacancyLeg),
                  $"FillEma tracks measured occupancy on {compared} submarkets " +
                  $"(mean err {meanErr:F3}, worst {worstErr:F2}); " +
                  $"cluster {c0} bid {bidFull:F3} (fill {fillFull:F2}) at full occupancy → " +
                  $"{bidEmpty:F3} (fill {fillEmpty:F2}) at 20 % ({(priceLeg ? "price leg" : "vacancy leg")})");
        }

        private static void ClaimVacancyWash(ulong seed)
        {
            // The claim↔vacancy wash: a unit UNDER CONSTRUCTION and the same
            // unit COMPLETED-BUT-VACANT are the same competitive object, so
            // they must suppress demand identically. When claims were netted
            // 100% locally while vacancy smeared through the kernel (~15% local
            // retention), COMPLETING an empty building RAISED its own cluster's
            // residual by ~0.85×units — a ratchet that kept starting towers
            // beside standing empties (adversarial review, confirmed HIGH).
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var w = sim.W;
            var eng = sim.Engine;
            int C = eng.Access.C;
            var seekers = (double[])eng.SeekersEma.Clone();   // frozen: isolate the supply side
            var res = new ResidualDemand();

            // Pick the cluster with the most occupied ResidentialLow units.
            var occ = new int[C];
            foreach (var pl in w.Parcels)
                if (pl.State == ParcelState.Built && pl.Use == ZoneKind.ResidentialLow)
                    occ[pl.Cluster] += pl.OccupantHouseholds.Count;
            int site = 0;
            for (int c = 1; c < C; c++) if (occ[c] > occ[site]) site = c;

            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var baseline = new double[C];
            for (int c = 0; c < C; c++) baseline[c] = res.Get(c, ZoneKind.ResidentialLow, w.Claims);

            // (a) as PIPELINE: book a claim of K units at the site.
            const int K = 20;
            w.Claims.Add(site, ZoneKind.ResidentialLow, K);
            var deltaClaim = new double[C];
            for (int c = 0; c < C; c++)
                deltaClaim[c] = baseline[c] - res.Get(c, ZoneKind.ResidentialLow, w.Claims);
            w.Claims.Add(site, ZoneKind.ResidentialLow, -K);

            // (b) as COMPLETED-VACANT: evict at the site and re-refresh on the
            // SAME seekers. Normalize by the vacancy ACTUALLY created, not by
            // evictions — warehoused parcels re-let nothing, so the two counts
            // differ and the invariant under test is the PER-UNIT footprint.
            double VacAt(int cluster)
            {
                double v = 0;
                foreach (var pl in w.Parcels)
                    if (pl.Cluster == cluster && pl.State == ParcelState.Built
                        && pl.Use == ZoneKind.ResidentialLow && !pl.Warehousing) v += pl.Vacant;
                return v;
            }
            double vacBefore = VacAt(site);
            int evicted = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.Cluster != site || pl.State != ParcelState.Built
                    || pl.Use != ZoneKind.ResidentialLow || pl.Warehousing) continue;
                while (pl.OccupantHouseholds.Count > 0 && evicted < K)
                {
                    int hid = pl.OccupantHouseholds[pl.OccupantHouseholds.Count - 1];
                    pl.OccupantHouseholds.RemoveAt(pl.OccupantHouseholds.Count - 1);
                    w.Households[hid].HomeParcel = -1;
                    evicted++;
                }
                if (evicted >= K) break;
            }
            double vacCreated = VacAt(site) - vacBefore;
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var deltaVac = new double[C];
            for (int c = 0; c < C; c++)
                deltaVac[c] = baseline[c] - res.Get(c, ZoneKind.ResidentialLow, w.Claims);

            // Compare PER-UNIT footprints: suppression(cluster) / units emitted.
            double sumClaim = 0, sumVac = 0, worstGap = 0;
            for (int c = 0; c < C; c++)
            {
                double perClaim = deltaClaim[c] / K;
                double perVac = vacCreated > 0 ? deltaVac[c] / vacCreated : 0;
                sumClaim += perClaim;
                sumVac += perVac;
                worstGap = Math.Max(worstGap, Math.Abs(perClaim - perVac));
            }
            // Local retention must match too — the exact quantity that broke.
            double localClaim = deltaClaim[site] / K;
            double localVac = vacCreated > 0 ? deltaVac[site] / vacCreated : 0;
            Check("claim↔vacancy wash: pipeline and completed-vacant suppress identically",
                  vacCreated > 5 && worstGap < 0.02
                  && Math.Abs(localClaim - localVac) < 0.02 && localClaim < 0.9,
                  $"{K} claimed vs {vacCreated:F0} vacated: worst per-unit gap {worstGap:E1}; " +
                  $"local retention claim {localClaim:P0} vs vacancy {localVac:P0}; " +
                  $"low-channel share claim {sumClaim:P0} vs vacancy {sumVac:P0}");
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

            // Pick the occupied parcel with the LARGEST assessed rent, and
            // require it to be positive: on a marginal parcel LR and wedge are
            // both floored at 0, so the assertion below degenerates to 0 == 0
            // and the guard is green no matter what assessment reads
            // (adversarial review: First() landed on a zero-LR parcel in 55
            // of 133 occupied cases at the check's own seed).
            var parcel = sim.W.Parcels
                .Where(x => x.State == ParcelState.Built
                            && x.IsResidential && x.OccupantHouseholds.Count > 0)
                .OrderByDescending(x => x.AssessedLR).First();
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
                  lr1 > 1e-9 && parcel.AssessedLR == lr1 && parcel.Wedge == wedge1,
                  $"LR {lr1:F4} (> 0) unchanged under 17.5× realized-rent perturbation");
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
