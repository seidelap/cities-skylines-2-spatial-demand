using System;
using System.Collections.Generic;
using System.Linq;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>TEMPORARY diagnostic: residential vs firm bid parity.</summary>
    public static class BidParityProbe
    {
        public static int Run(ulong seed, int ticks)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(ticks);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;
            var trade = sim.Engine.Trade;
            int C = acc.C;

            // ---------- 1. stock / vacancy by use ----------
            var built = new double[6]; var occUnits = new double[6]; var parcels = new int[6];
            var vacantParcels = new int[6];
            var stockU = new double[6][]; var vacU = new double[6][];
            for (int u = 0; u < 6; u++) { stockU[u] = new double[C]; vacU[u] = new double[C]; }
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                int u = (int)pl.Use - 1;
                if (u < 0 || u > 5) continue;
                built[u] += pl.Units; parcels[u]++;
                stockU[u][pl.Cluster] += pl.Units;
                if (pl.IsResidential)
                {
                    occUnits[u] += Math.Min(pl.Units, pl.OccupantHouseholds.Count);
                    vacU[u][pl.Cluster] += Math.Max(0, pl.Units - pl.OccupantHouseholds.Count);
                    if (pl.OccupantHouseholds.Count == 0) vacantParcels[u]++;
                }
                else
                {
                    if (pl.OccupantFirm >= 0) occUnits[u] += pl.Units;
                    else { vacU[u][pl.Cluster] += pl.Units; vacantParcels[u]++; }
                }
            }
            string[] un = { "resLow", "resHigh", "com", "ind", "off", "ext" };
            Console.WriteLine($"=== tick {w.Tick}, C={C} ===");
            Console.WriteLine("use      parcels  units   occupied   vacancy%  vacantParcels");
            for (int u = 0; u < 6; u++)
                Console.WriteLine($"{un[u],-8} {parcels[u],6} {built[u],7:F0} {occUnits[u],9:F0} "
                    + $"{(built[u] > 0 ? 1 - occUnits[u] / built[u] : 0),9:P1} {vacantParcels[u],10}");

            // ---------- 2. AssessedLR per unit by use ----------
            Console.WriteLine("\n--- AssessedLR per unit (built parcels only) ---");
            var lrByUse = new List<double>[6];
            var bidByUse = new List<double>[6];
            for (int u = 0; u < 6; u++) { lrByUse[u] = new List<double>(); bidByUse[u] = new List<double>(); }
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                int u = (int)pl.Use - 1; if (u < 0 || u > 5 || pl.Units == 0) continue;
                lrByUse[u].Add(pl.AssessedLR / pl.Units);
                double b = LandAccounting.BidPerUnit(acc, trade, pl.Cluster, pl.Use, pl.Level, pres, p,
                                                     addUnits: 0, minSupply: pl.Units) * p.CondFactor(pl.Condition);
                bidByUse[u].Add(b);
            }
            double S2 = LandAccounting.SPerUnit(2, 1.0, p);
            Console.WriteLine($"S(lvl2,cond1)={S2:F3}  S(lvl1)={LandAccounting.SPerUnit(1, 1.0, p):F3}");
            Console.WriteLine("use       n   LR/unit mean   p50     max    | bid/unit mean   p50    max");
            for (int u = 0; u < 6; u++)
            {
                var xs = lrByUse[u]; if (xs.Count == 0) continue;
                var ys = bidByUse[u];
                var sx = xs.OrderBy(v => v).ToList(); var sy = ys.OrderBy(v => v).ToList();
                Console.WriteLine($"{un[u],-8}{xs.Count,4} {xs.Average(),12:F3} {sx[sx.Count / 2],7:F3} {sx[^1],7:F3}"
                    + $"  | {ys.Average(),12:F3} {sy[sy.Count / 2],7:F3} {sy[^1],7:F3}");
            }

            // ---------- 3. per-cluster: access, res bid+fill, firm bids ----------
            Console.WriteLine("\n--- per cluster: meanAccess-relative, residential clearing bid+fill, firm bids ---");
            Console.WriteLine("  c  relAcc  resLowStock resLowBid resFill | comUnits comVac% comBid | offUnits offVac% offBid | indBid  extBid");
            var order = Enumerable.Range(0, C).OrderByDescending(c => stockU[2][c] + stockU[4][c]).Take(14);
            foreach (int c in order)
            {
                double rel = 0; for (int s = 0; s < Segment.Count; s++) rel += acc.AccessValue[s][c];
                rel /= Segment.Count * acc.MeanAccess;
                double resBid = LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double rf);
                double comBid = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Commercial, 2, p);
                double indBid = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Industrial, 2, p);
                double offBid = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Office, 2, p);
                double extBid = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Extractor, 2, p, out _, w.Clusters);
                double comVac = stockU[2][c] > 0 ? vacU[2][c] / stockU[2][c] : 0;
                double offVac = stockU[4][c] > 0 ? vacU[4][c] / stockU[4][c] : 0;
                Console.WriteLine($"{c,3} {rel,7:F3} {stockU[0][c],11:F0} {resBid,9:F3} {rf,7:P0} | "
                    + $"{stockU[2][c],8:F0} {comVac,6:P0} {comBid,7:F3} | {stockU[4][c],8:F0} {offVac,6:P0} {offBid,7:F3} | "
                    + $"{indBid,7:F3} {extBid,7:F3}");
            }

            // ---------- 4. does the firm bid respond to firm-side VACANCY? ----------
            // Counterfactual: recompute the access aggregates with VACANT
            // non-residential units counted as if they were occupied competitors
            // (commercial mass / office jobs). Delta = the vacancy response that
            // the current firm bid does NOT have.
            Console.WriteLine("\n--- counterfactual: what if vacant firm space competed? ---");
            var cmBase = (double[])acc.CommercialMass.Clone();
            var ojBase = (double[])acc.OfficeJobs.Clone();
            var comBase = new double[C]; var offBase = new double[C];
            for (int c = 0; c < C; c++)
            {
                comBase[c] = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Commercial, 2, p);
                offBase[c] = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Office, 2, p);
            }
            // add vacant units into the competitor masses
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.OccupantFirm >= 0) continue;
                double cond = Math.Max(0.2, pl.Condition);
                if (pl.Use == ZoneKind.Commercial)
                    acc.CommercialMass[pl.Cluster] += pl.Units * cond * p.Quality(pl.Level);
                else if (pl.Use == ZoneKind.Office) acc.OfficeJobs[pl.Cluster] += pl.Units;
            }
            // rebuild the two derived fields the bids read
            double wOutside = Math.Exp(-p.ThetaShopping * p.OutsideShopMinutes) * p.OutsideShopMass;
            for (int i = 0; i < C; i++)
            {
                double s = wOutside + 1e-9;
                for (int j = 0; j < C; j++) s += acc.WShop[i, j] * acc.CommercialMass[j];
                acc.IncumbentShopWeight[i] = s;
            }
            for (int c = 0; c < C; c++)
            {
                double a = 0;
                for (int j = 0; j < C; j++) a += acc.WOffice[c, j] * acc.OfficeJobs[j];
                acc.OfficeAgglomMult[c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
            }
            Console.WriteLine("  c   comBid base -> counterfactual (delta%)   |  offBid base -> cf (delta%)");
            foreach (int c in order)
            {
                double cb = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Commercial, 2, p);
                double ob = LandAccounting.FirmBidPerSlot(acc, trade, c, ZoneKind.Office, 2, p);
                Console.WriteLine($"{c,3} {comBase[c],10:F3} -> {cb,8:F3} ({(comBase[c] > 1e-9 ? cb / comBase[c] - 1 : 0),7:P1})"
                    + $"   | {offBase[c],8:F3} -> {ob,8:F3} ({(offBase[c] > 1e-9 ? ob / offBase[c] - 1 : 0),7:P1})");
            }
            Array.Copy(cmBase, acc.CommercialMass, C);
            Array.Copy(ojBase, acc.OfficeJobs, C);
            for (int i = 0; i < C; i++)
            {
                double s = wOutside + 1e-9;
                for (int j = 0; j < C; j++) s += acc.WShop[i, j] * acc.CommercialMass[j];
                acc.IncumbentShopWeight[i] = s;
            }
            for (int c = 0; c < C; c++)
            {
                double a = 0;
                for (int j = 0; j < C; j++) a += acc.WOffice[c, j] * acc.OfficeJobs[j];
                acc.OfficeAgglomMult[c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
            }

            // ---------- 5. supply-invariance: firm bid vs added units ----------
            Console.WriteLine("\n--- supply sensitivity of the BID at one cluster (units added to that use) ---");
            int c0 = Enumerable.Range(0, C).OrderByDescending(c => stockU[0][c]).First();
            Console.Write($"cluster {c0}: resLow bid at addUnits = ");
            foreach (double add in new double[] { 0, 50, 200, 1000, 5000 })
                Console.Write($"{LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, addUnits: add),7:F3}");
            Console.WriteLine("   (0/50/200/1000/5000)");
            Console.WriteLine($"cluster {c0}: FirmBidPerSlot has NO addUnits/minSupply parameter — "
                + $"com={LandAccounting.FirmBidPerSlot(acc, trade, c0, ZoneKind.Commercial, 2, p):F3} "
                + $"ind={LandAccounting.FirmBidPerSlot(acc, trade, c0, ZoneKind.Industrial, 2, p):F3} "
                + $"off={LandAccounting.FirmBidPerSlot(acc, trade, c0, ZoneKind.Office, 2, p):F3} (invariant by construction)");

            // ---------- 5b. cleared-regime residential supply sensitivity ----------
            int cCleared = -1;
            for (int c = 0; c < C; c++)
            {
                if (acc.HousingStock[0][c] < 4) continue;
                LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double f);
                if (f > 0.999) { cCleared = c; break; }
            }
            if (cCleared >= 0)
            {
                Console.Write($"CLEARED cluster {cCleared} (stock={acc.HousingStock[0][cCleared]:F0}): resLow bid at addUnits = ");
                foreach (double add in new double[] { 0, 2, 10, 50, 200 })
                    Console.Write($"{LandAccounting.ResidentialBidPerUnit(acc, cCleared, ZoneKind.ResidentialLow, 2, pres, p, addUnits: add),7:F3}");
                Console.WriteLine("   (0/2/10/50/200)");
            }

            // ---------- 5c. office agglomeration: sign of the supply response ----------
            Console.WriteLine("\n--- office bid vs office STOCK at one cluster (scale OfficeJobs) ---");
            int cOff = Enumerable.Range(0, C).OrderByDescending(c => stockU[4][c]).First();
            double ojSave = acc.OfficeJobs[cOff];
            foreach (double k in new double[] { 0.25, 0.5, 1, 2, 4, 8 })
            {
                acc.OfficeJobs[cOff] = ojSave * k;
                for (int c = 0; c < C; c++)
                {
                    double a = 0;
                    for (int j = 0; j < C; j++) a += acc.WOffice[c, j] * acc.OfficeJobs[j];
                    acc.OfficeAgglomMult[c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
                }
                Console.WriteLine($"  officeJobs[{cOff}] x{k,-5} = {acc.OfficeJobs[cOff],7:F0} -> "
                    + $"agglom={acc.OfficeAgglomMult[cOff]:F4} offBid={LandAccounting.FirmBidPerSlot(acc, trade, cOff, ZoneKind.Office, 2, p):F3}");
            }
            acc.OfficeJobs[cOff] = ojSave;
            for (int c = 0; c < C; c++)
            {
                double a = 0;
                for (int j = 0; j < C; j++) a += acc.WOffice[c, j] * acc.OfficeJobs[j];
                acc.OfficeAgglomMult[c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
            }

            // ---------- 5d. clusters holding BOTH residential and firm stock ----------
            Console.WriteLine("\n--- clusters holding both res and non-res built stock: LR/unit side by side ---");
            var lrPer = new double[6][]; var nPer = new int[6][];
            for (int u = 0; u < 6; u++) { lrPer[u] = new double[C]; nPer[u] = new int[C]; }
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Units == 0) continue;
                int u = (int)pl.Use - 1; if (u < 0 || u > 5) continue;
                lrPer[u][pl.Cluster] += pl.AssessedLR / pl.Units; nPer[u][pl.Cluster]++;
            }
            int shownBoth = 0;
            Console.WriteLine("  c  relAcc | resLow resHigh |    com     ind     off     ext");
            for (int c = 0; c < C && shownBoth < 16; c++)
            {
                bool hasRes = nPer[0][c] + nPer[1][c] > 0;
                bool hasFirm = nPer[2][c] + nPer[3][c] + nPer[4][c] + nPer[5][c] > 0;
                if (!hasRes || !hasFirm) continue;
                shownBoth++;
                double rel = 0; for (int s = 0; s < Segment.Count; s++) rel += acc.AccessValue[s][c];
                rel /= Segment.Count * acc.MeanAccess;
                string F(int u) => nPer[u][c] > 0 ? $"{lrPer[u][c] / nPer[u][c],7:F2}" : "      -";
                Console.WriteLine($"{c,3} {rel,7:F3} |{F(0)}{F(1)} |{F(2)}{F(3)}{F(4)}{F(5)}");
            }

            // ---------- 6. fill estimates ----------
            Console.WriteLine("\n--- fill: residential expected fill vs FirmFillEstimate (labor, clamped [0.35,1]) ---");
            double minF = 2, maxF = -1;
            for (int c = 0; c < C; c++)
            {
                if (acc.HousingStock[0][c] <= 0) continue;
                LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double f);
                minF = Math.Min(minF, f); maxF = Math.Max(maxF, f);
            }
            Console.WriteLine($"residential fillRatio range over stocked clusters: [{minF:P0} .. {maxF:P0}]");
            foreach (var sec in new[] { ZoneKind.Commercial, ZoneKind.Industrial, ZoneKind.Office, ZoneKind.Extractor })
            {
                double lo = 2, hi = -1;
                for (int c = 0; c < C; c++)
                {
                    double f = LandAccounting.FirmFillEstimate(acc, c, sec);
                    lo = Math.Min(lo, f); hi = Math.Max(hi, f);
                }
                Console.WriteLine($"  FirmFillEstimate({sec}) range: [{lo:P0} .. {hi:P0}]");
            }

            Console.WriteLine($"\nAUDIT priced={LandAccounting.AuditTotalPriced} excess={LandAccounting.AuditExcessCalls} "
                + $"dustZero={LandAccounting.AuditDustZero}");

            // ---------- 7. tax parity: per-use charge decomposition ----------
            Console.WriteLine("\n--- charge decomposition (mean over built parcels of each use) ---");
            Console.WriteLine("use        S/unit  structTax/unit  phi*LR/unit  unitAssess  escrowEarmark?");
            for (int u = 0; u < 6; u++)
            {
                double sS = 0, sT = 0, sL = 0, sA = 0; int n = 0, ear = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || (int)pl.Use - 1 != u || pl.Units == 0) continue;
                    sS += LandAccounting.SPerUnit(pl.Level, pl.Condition, p);
                    sT += LandAccounting.StructureTaxPerUnit(pl, p);
                    sL += p.CaptureFraction * pl.AssessedLR / pl.Units;
                    sA += LandAccounting.UnitAssessment(pl, p);
                    if (w.Clusters[pl.Cluster].WedgeEarmark && p.WedgeEarmarkDefault) ear++;
                    n++;
                }
                if (n == 0) continue;
                Console.WriteLine($"{un[u],-8} {sS / n,8:F3} {sT / n,14:F3} {sL / n,12:F3} {sA / n,11:F3} {ear}/{n}");
            }
            // ---------- 8. tax base: whose LR is it, and is it occupied? ----------
            Console.WriteLine("\n--- assessed land rent aggregate ---");
            double totLR = 0; var lrU = new double[6]; var lrVacU = new double[6];
            var tgt = new double[6]; var tgtN = new int[6];
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                int u = (int)pl.Use - 1; if (u < 0 || u > 5) continue;
                lrU[u] += pl.AssessedLR; totLR += pl.AssessedLR;
                tgt[u] += pl.TargetLevel; tgtN[u]++;
                bool empty = pl.IsResidential ? pl.OccupantHouseholds.Count == 0 : pl.OccupantFirm < 0;
                if (empty) lrVacU[u] += pl.AssessedLR;
            }
            for (int u = 0; u < 6; u++)
                Console.WriteLine($"{un[u],-8} ΣLR={lrU[u],10:F0} ({lrU[u] / Math.Max(1e-9, totLR),6:P1} of base)"
                    + $"  ΣLR on EMPTY parcels={lrVacU[u],10:F0} ({(lrU[u] > 1e-9 ? lrVacU[u] / lrU[u] : 0),6:P1})"
                    + $"  meanTargetLevel={(tgtN[u] > 0 ? tgt[u] / tgtN[u] : 0),4:F2}");

            // ---------- 9. per-level anatomy of a firm bid vs a residential bid ----------
            Console.WriteLine("\n--- per-level flow (bid−S)×units at one industrial and one residential cluster ---");
            int cInd = Enumerable.Range(0, C).OrderByDescending(c => stockU[3][c]).First();
            int uInd = LandAccounting.UnitsFor(ZoneKind.Industrial);
            for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
            {
                double b = LandAccounting.FirmBidPerSlot(acc, trade, cInd, ZoneKind.Industrial, lvl, p);
                double s = LandAccounting.SPerUnit(lvl, 1.0, p);
                Console.WriteLine($"  ind c={cInd} lvl {lvl}: bid={b,8:F3} S={s,7:F3} flow=(b−S)*{uInd}={(b - s) * uInd,9:F1} RC={p.RC(lvl, uInd),9:F0}");
            }
            int cRes = Enumerable.Range(0, C).OrderByDescending(c => stockU[0][c]).First();
            int uRes = LandAccounting.UnitsFor(ZoneKind.ResidentialLow);
            for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
            {
                double b = LandAccounting.ResidentialBidPerUnit(acc, cRes, ZoneKind.ResidentialLow, lvl, pres, p, minSupply: uRes);
                double s = LandAccounting.SPerUnit(lvl, 1.0, p);
                Console.WriteLine($"  res c={cRes} lvl {lvl}: bid={b,8:F3} S={s,7:F3} flow=(b−S)*{uRes}={(b - s) * uRes,9:F1} RC={p.RC(lvl, uRes),9:F0}");
            }
            // ---------- 10. extractor: the Assess path passes workCluster: null ----------
            Console.WriteLine("\n--- extractor bid: with geology (workCluster) vs the Assess path (null → suit 0.5) ---");
            int shownE = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Use != ZoneKind.Extractor) continue;
                if (shownE++ >= 8) break;
                double withGeo = LandAccounting.FirmBidPerSlot(acc, trade, pl.Cluster, ZoneKind.Extractor, pl.Level, p, out Res r1, w.Clusters);
                double assessPath = LandAccounting.FirmBidPerSlot(acc, trade, pl.Cluster, ZoneKind.Extractor, pl.Level, p);
                var suit = w.Clusters[pl.Cluster].ResourceSuitability;
                double best = 0; for (int rr = 0; rr < ResourceCatalog.RawCount; rr++) best = Math.Max(best, suit[rr]);
                Console.WriteLine($"  parcel {pl.Id,4} c={pl.Cluster,3} lvl={pl.Level} bestSuit={best:F2}: "
                    + $"withGeo={withGeo,7:F3} ({r1})  assessPath(null)={assessPath,7:F3}  ratio={(withGeo > 1e-9 ? assessPath / withGeo : 0),6:F2}x"
                    + $"  LR/unit={pl.AssessedLR / Math.Max(1, pl.Units),7:F2} occupied={(pl.OccupantFirm >= 0 ? "Y" : "N")}");
            }
            return 0;
        }
    }
}
