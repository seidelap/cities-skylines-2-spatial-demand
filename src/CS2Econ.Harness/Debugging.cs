using System;
using System.Linq;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>`harness debug` — one instrumented run with periodic prints of
    /// the quantities the acceptance tests depend on. Tuning aid, not a test.</summary>
    public static class Debugging
    {
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
                double capturePerSlot = 0; int slots = 0;
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
                    if (x.Resource == Res.Raw) { rawSust += x.SustainedQ; rawExp += x.ExportedThisTick; rawImp += x.ImportedThisTick; }
                    if (x.Resource == Res.Goods) { goodsSust += x.SustainedQ; goodsExp += x.ExportedThisTick; goodsImp += x.ImportedThisTick; }
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
                    $"starts={sim.Engine.Construction.StartedTotal} aband={sim.Engine.Construction.AbandonedTotal} | " +
                    $"raw sust={rawSust:F0} x={rawExp:F0}/i={rawImp:F0} goods sust={goodsSust:F0} x={goodsExp:F0}/i={goodsImp:F0} | " +
                    $"pRaw={sim.Engine.Trade.LocalPrice(Res.Raw):F2} pGoods={sim.Engine.Trade.LocalPrice(Res.Goods):F2} | " +
                    $"wedgeΣ={wedgeSum:F0} LRΣ={lrSum:F0} avgAssess={assessSum / Math.Max(1, occupiedRes):F2} | " +
                    $"displ={sim.Engine.DisplacementExits.Count} " +
                    $"arr={sim.Engine.LastFlows.ArrivalsBySegment?.Sum() ?? 0} dep={sim.Engine.LastFlows.DeparturesBySegment?.Sum() ?? 0} " +
                    $"drift={w.Ledger.Drift():E1}");
            }

            // Level-by-access snapshot
            Console.WriteLine("\nlevel vs access-rank (built residential):");
            var rows = w.Parcels.Where(x => x.State == ParcelState.Built && x.IsResidential)
                .Select(x => (x.Level, acc: Enumerable.Range(0, Segment.Count).Sum(s => sim.Engine.Access.AccessValue[s][x.Cluster])))
                .OrderBy(x => x.acc).ToList();
            int q = Math.Max(1, rows.Count / 5);
            for (int i = 0; i < 5; i++)
            {
                var slice = rows.Skip(i * q).Take(q).ToList();
                if (slice.Count > 0)
                    Console.WriteLine($"  access quintile {i + 1}: mean level {slice.Average(x => x.Level):F2} (n={slice.Count})");
            }
            return 0;
        }
    }
}
