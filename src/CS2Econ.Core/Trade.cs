using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Result of clearing one resource for one tick.</summary>
    public struct ClearResult
    {
        public double LocalPrice;
        public double Exported, ExportRevenue;   // net of haul (haul goes to OutsideWorld)
        public double Imported, ImportCost;      // delivered cost incl. haul
        public double Unsold;                    // surplus no exit would take at positive net
    }

    /// <summary>Tier D (design §4.5). Each exit carries its own supply/demand law
    /// p(Q) = anchor ± t·(Q/ρ)^(1/d): road d=2 (square-root law, no asymptote),
    /// rail d=1 (linear, handling intercept, shallow slope), sea/air d→∞ (flat,
    /// capacity-capped). Sustained position is an EMA of drawn volume; a transient
    /// burst layer rides on top and decays; exits into the same region share their
    /// sustained scalar (adjacent-exit coupling at the fundamental).
    ///
    /// Multimodal composition is EMERGENT: clearing allocates quantized lots
    /// cheapest-marginal-first across independent curves — horizontal summation —
    /// so road wins small volumes, rail amortizes at mid volumes, the linear rail
    /// law re-crosses the square-root road law at very large volumes, and
    /// concurrently active exits hold marginal prices locked together.</summary>
    public sealed class TradeSystem : IPriceContext
    {
        private readonly Dictionary<(Res, int), double> _groupSustained = new Dictionary<(Res, int), double>();
        private double[] _localPrice = new double[4];
        // Per (res, cluster): haul to cheapest producing cluster (freight-in term)
        private double[][] _localSourceHaul = new double[4][];
        private double[][] _haulToExit = Array.Empty<double[]>();   // [exitIdx][cluster]
        private WorldState _w = null!;
        private IAccessCosts _costs = null!;
        public double FreightCostPerMinute = 0.012;                 // per unit per minute of routed haul
        public double InducedProcessingSlots;                       // export-base feedback (§4.5)

        public void Bind(WorldState w, IAccessCosts costs)
        {
            _w = w; _costs = costs;
            _localPrice[(int)Res.Raw] = 2.4;
            _localPrice[(int)Res.Goods] = 5.0;
            _localPrice[(int)Res.Services] = 1.0;
            _localPrice[(int)Res.OfficeOutput] = 0;
        }

        /// <summary>Refresh haul caches (rides the same dirty cadence as access;
        /// congestion-inclusive haul is what makes the parity band move with
        /// traffic, design §4.5).</summary>
        public void Refresh(EconParams p)
        {
            int C = _costs.ClusterCount;
            _haulToExit = new double[_w.Exits.Count][];
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                _haulToExit[e] = new double[C];
                for (int c = 0; c < C; c++)
                    _haulToExit[e][c] = _costs.Cost(c, _w.Exits[e].Cluster, AccessPurpose.Freight)
                                        * FreightCostPerMinute;
            }
            for (int r = 0; r < 4; r++)
            {
                var res = (Res)r;
                var haul = new double[C];
                var producers = new List<int>();
                foreach (var f in _w.Firms)
                {
                    if (f.Dead || f.Parcel < 0) continue;
                    if ((res == Res.Raw && f.Sector == ZoneKind.Extractor)
                        || (res == Res.Goods && f.Sector == ZoneKind.Industrial))
                        producers.Add(_w.Parcels[f.Parcel].Cluster);
                }
                for (int c = 0; c < C; c++)
                {
                    double best = double.PositiveInfinity;
                    foreach (int src in producers)
                    {
                        double h = _costs.Cost(src, c, AccessPurpose.Freight) * FreightCostPerMinute;
                        if (h < best) best = h;
                    }
                    haul[c] = best;   // +inf when nothing produces locally
                }
                _localSourceHaul[r] = haul;
            }
        }

        private double GroupSustained(Res r, TradeExit e)
        {
            double total = 0;
            foreach (var x in _w.Exits)
                if (x.Resource == r && RegionGroupOf(x) == RegionGroupOf(e)) total += x.SustainedQ;
            return total;
        }

        private static int RegionGroupOf(TradeExit e) => e.Mode == ExitMode.Sea || e.Mode == ExitMode.Air
            ? 1000 + e.Id       // deep baths are their own group
            : (int)e.Mode;      // road exits share a region; rail exits share a corridor market

        // Positions are NET sustained flow (exports − imports): a region the city
        // imports from is undersupplied — marginal exports into it fetch the
        // anchor, they are not depressed by the import volume. Depth builds only
        // in the direction actually pushed.
        public double ExportMarginal(TradeExit e, double qInTick, EconParams p)
        {
            double eff = Math.Max(0, GroupSustained(e.Resource, e) + p.TradeTransientBeta * e.TransientB + qInTick);
            double depth = double.IsInfinity(e.D) ? 0 : e.T * Math.Pow(eff / e.Rho, 1.0 / e.D);
            return e.Anchor - e.PerUnitHandling - depth;
        }

        public double ImportMarginal(TradeExit e, double qInTick, EconParams p)
        {
            double eff = Math.Max(0, -GroupSustained(e.Resource, e) + p.TradeTransientBeta * e.TransientB + qInTick);
            double depth = double.IsInfinity(e.D) ? 0 : e.T * Math.Pow(eff / e.Rho, 1.0 / e.D);
            return e.Anchor + e.PerUnitHandling + depth;
        }

        // ---- IPriceContext (what Tier B/C read) -----------------------------

        public double LocalPrice(Res r) => _localPrice[(int)r];

        public double DeliveredCost(Res r, int cluster)
        {
            double local = _localSourceHaul[(int)r] != null && !double.IsInfinity(_localSourceHaul[(int)r][cluster])
                ? _localPrice[(int)r] + _localSourceHaul[(int)r][cluster]
                : double.PositiveInfinity;
            double import = double.PositiveInfinity;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r) continue;
                double m = ImportMarginal(x, 0, _p) + _haulToExit[e][cluster];
                if (m < import) import = m;
            }
            double v = Math.Min(local, import);
            return double.IsInfinity(v) ? _localPrice[(int)r] * 2 : v;
        }

        public double BestExportNet(Res r, int cluster)
        {
            double best = 0;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r) continue;
                double m = ExportMarginal(x, 0, _p) - _haulToExit[e][cluster];
                if (m > best) best = m;
            }
            return best;
        }

        private EconParams _p = new EconParams();
        public void SetParams(EconParams p) => _p = p;

        /// <summary>Clear one tradable resource for one tick. Surplus goes to
        /// exits cheapest-marginal-first in quantized lots; shortage draws from
        /// exits the same way. Local price lands inside the parity band whose
        /// width is the actual routed transport cost (design §4.5).</summary>
        public ClearResult ClearTick(Res r, double supply, double demand,
                                     double[] supplyByCluster, double[] demandByCluster, EconParams p)
        {
            var result = new ClearResult();
            foreach (var x in _w.Exits)
                if (x.Resource == r) { x.DrawnThisTick = 0; x.ExportedThisTick = 0; x.ImportedThisTick = 0; }

            // Volume-weighted mean haul for this tick's flows.
            double MeanHaul(int exitIdx, double[] byCluster, double total)
            {
                if (total <= 1e-9) return _haulToExit[exitIdx].Length > 0 ? _haulToExit[exitIdx][0] : 0;
                double s = 0;
                for (int c = 0; c < byCluster.Length; c++) s += byCluster[c] * _haulToExit[exitIdx][c];
                return s / total;
            }

            double surplus = supply - demand;
            double lastMarginalNet = double.NaN;

            if (surplus > 0)
            {
                double remaining = surplus;
                while (remaining > 1e-6)
                {
                    double lot = Math.Min(p.LotSize, remaining);
                    int bestE = -1; double bestNet = 0;
                    for (int e = 0; e < _w.Exits.Count; e++)
                    {
                        var x = _w.Exits[e];
                        if (x.Resource != r) continue;
                        if (x.Capacity > 0 && x.DrawnThisTick + lot > x.Capacity) continue;
                        double net = ExportMarginal(x, x.DrawnThisTick, p) - MeanHaul(e, supplyByCluster, supply);
                        if (net > bestNet) { bestNet = net; bestE = e; }
                    }
                    if (bestE < 0) break;   // nobody pays a positive net price: unsold
                    var ex = _w.Exits[bestE];
                    ex.DrawnThisTick += lot;
                    ex.ExportedThisTick += lot;
                    result.Exported += lot;
                    result.ExportRevenue += lot * bestNet;
                    lastMarginalNet = bestNet;
                    remaining -= lot;
                }
                result.Unsold = remaining;
                // Marginal unit is exported → local price sits at export parity.
                if (!double.IsNaN(lastMarginalNet))
                    _localPrice[(int)r] = MathUtil.Ema(_localPrice[(int)r], Math.Max(0.05, lastMarginalNet), 0.25);
            }
            else if (surplus < 0)
            {
                double remaining = -surplus;
                double lastMarginalCost = double.NaN;
                while (remaining > 1e-6)
                {
                    double lot = Math.Min(p.LotSize, remaining);
                    int bestE = -1; double bestCost = double.PositiveInfinity;
                    for (int e = 0; e < _w.Exits.Count; e++)
                    {
                        var x = _w.Exits[e];
                        if (x.Resource != r) continue;
                        if (x.Capacity > 0 && x.DrawnThisTick + lot > x.Capacity) continue;
                        double cost = ImportMarginal(x, x.DrawnThisTick, p) + MeanHaul(e, demandByCluster, demand);
                        if (cost < bestCost) { bestCost = cost; bestE = e; }
                    }
                    if (bestE < 0) break;   // no import capacity: shortage persists
                    var ex = _w.Exits[bestE];
                    ex.DrawnThisTick += lot;
                    ex.ImportedThisTick += lot;
                    result.Imported += lot;
                    result.ImportCost += lot * bestCost;
                    lastMarginalCost = bestCost;
                    remaining -= lot;
                }
                if (!double.IsNaN(lastMarginalCost))
                    _localPrice[(int)r] = MathUtil.Ema(_localPrice[(int)r], lastMarginalCost, 0.25);
            }

            result.LocalPrice = _localPrice[(int)r];
            return result;
        }

        /// <summary>Slow layer: sustained EMAs, transient decay, group coupling,
        /// and the export-base feedback into Tier B (design §4.5).</summary>
        public void EndTick(EconParams p)
        {
            double rawExportSustained = 0;
            foreach (var x in _w.Exits)
            {
                double net = x.ExportedThisTick - x.ImportedThisTick;
                double total = x.ExportedThisTick + x.ImportedThisTick;
                x.SustainedQ = MathUtil.Ema(x.SustainedQ, net, p.TradeSustainAlpha);
                x.TransientB = p.TradeTransientDecay * x.TransientB
                               + Math.Max(0, total - Math.Abs(x.SustainedQ));
                if (x.Resource == Res.Raw) rawExportSustained += Math.Max(0, x.SustainedQ);
            }
            // Export-base multiplier: sustained raw exports induce local
            // processing demand (Weber pull re-enters via Construction).
            InducedProcessingSlots = 0.35 * rawExportSustained / Math.Max(1, _p.IndOutputPerSlot);
        }

        /// <summary>Overlay: parity band for a resource at a cluster (§4.7).</summary>
        public (double exportParity, double importParity) ParityBand(Res r, int cluster)
        {
            double lo = 0, hi = double.PositiveInfinity;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r) continue;
                lo = Math.Max(lo, ExportMarginal(x, 0, _p) - _haulToExit[e][cluster]);
                hi = Math.Min(hi, ImportMarginal(x, 0, _p) + _haulToExit[e][cluster]);
            }
            return (lo, hi);
        }
    }
}
