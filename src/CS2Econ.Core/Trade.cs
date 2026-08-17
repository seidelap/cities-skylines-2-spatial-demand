using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Result of clearing one resource for one tick.</summary>
    public struct ClearResult
    {
        public double Exported, ExportRevenue;   // net of haul (haul goes to OutsideWorld)
        public double Imported, ImportCost;      // delivered cost incl. haul
        public double Unsold;                    // surplus no exit would take at positive net
        public double LocalVolume;               // cluster-to-cluster lots delivered this tick
        public double LocalFreight;              // Σ q·haul over local lots — paid to OutsideWorld
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
    /// concurrently active exits hold marginal prices locked together.
    ///
    /// CLUSTER-IDENTIFIED LOTS (task #30): every transaction has a place. Local
    /// demand fills from the cheapest source FOR THAT DESTINATION (a producing
    /// cluster at its opportunity-cost ask plus routed haul, or an import exit at
    /// its live law plus haul); remaining supply exports on its own best route.
    /// The per-(resource, cluster) price statistics below are averages of what
    /// firms at a place actually paid and received — never a modeled surface: a
    /// cluster with no transactions holds the citywide realized prior and
    /// nothing else.</summary>
    public sealed class TradeSystem : IPriceContext
    {
        // Seed prices (0.95·anchor; Services 1.0). With TierD off no transaction
        // ever updates a statistic, so every read returns exactly this seed —
        // the flags-off world is unchanged by the localization.
        private double[] _seedPrice = new double[ResourceCatalog.Count];
        // Per (res, cluster): realized-transaction statistics. Price EMAs update
        // only on ticks with volume at that cluster (a quiet tick is no
        // evidence; prices do not decay on silence); volume EMAs update every
        // tick, zeros included (a market that went quiet thins) at the same
        // rate as the exits' sustained position.
        private double[][] _emaDelivered = Array.Empty<double[]>();
        private double[][] _emaOrigin = Array.Empty<double[]>();
        private double[][] _emaDeliveredQty = Array.Empty<double[]>();
        private double[][] _emaOriginQty = Array.Empty<double[]>();
        // This tick's realized flows per (res, cluster) — written by ClearTick,
        // read by the engine's settlement immediately after (single-threaded
        // tick), folded into the EMAs and cleared by EndTick.
        private double[][] _tickDeliveredPaid = Array.Empty<double[]>();
        private double[][] _tickDeliveredQty = Array.Empty<double[]>();
        private double[][] _tickOriginRev = Array.Empty<double[]>();
        private double[][] _tickOriginQty = Array.Empty<double[]>();
        // Citywide realized priors: VOLUME-WEIGHTED over the per-cluster EMAs
        // (an unweighted mean would be diluted by every empty map square — the
        // prospect-odds lesson, Access.cs ProspectBenchOdds). Recomputed once
        // per tick in EndTick.
        private double[] _cityDelivered = Array.Empty<double>();
        private double[] _cityOrigin = Array.Empty<double>();
        // Per (res): producing clusters and their haul rows (freight weight
        // included), for the DeliveredCost quote's local leg. Refresh cadence.
        private int[][] _producerCluster = new int[ResourceCatalog.Count][];
        private double[][][] _producerHaul = new double[ResourceCatalog.Count][][];
        private double[][] _haulToExit = Array.Empty<double[]>();   // [exitIdx][cluster]
        // ClearTick scratch (reused; sized to cluster count)
        private double[] _supRemaining = Array.Empty<double>();
        private double[] _demRemaining = Array.Empty<double>();
        private double[] _askOf = Array.Empty<double>();
        private double[] _pj = Array.Empty<double>();
        private readonly List<int> _srcClusters = new List<int>();
        private readonly List<(int dst, int src, double q, double cost)> _localLots
            = new List<(int, int, double, double)>();
        private WorldState _w = null!;
        private IAccessCosts _costs = null!;
        public double FreightCostPerMinute = 0.012;                 // per unit per minute of routed haul
        public double InducedProcessingSlots;                       // export-base feedback (§4.5)

        /// <summary>EMA rate of the per-cluster price statistics — the rate the
        /// old citywide LocalPrice EMA used.</summary>
        private const double PriceEmaRate = 0.25;

        /// <summary>MUTANT SWITCH, harness-only (`--mutant-citywide-goods`):
        /// restores the defect task #30 removed — every consumer prices goods
        /// at ONE citywide scalar (here the volume-weighted prior for all
        /// clusters alike). It exists so the goods-price localization check
        /// stays falsifiable: that check must fail whenever this is on. Never
        /// set outside the harness.</summary>
        public static bool MutantCitywideGoodsPrice = false;

        /// <summary>Per-lot settlement telemetry for the goods-settlement
        /// reconciliation check (harness-only; null in shipping paths). One
        /// record per (resource, tick) with local flow this tick; the engine
        /// adds its realized firm credit/debit after settling.</summary>
        public sealed class SettleRecord
        {
            public Res Resource;
            public double OriginRevSum, DeliveredPaidSum;
            public double ExportRevenue, ImportCost, LocalFreight;
            public double FirmCredit, FirmDebit;
            /// <summary>Worst per-lot band violation: max over local lots of
            /// (lot cost − live import alternative at its destination when one
            /// existed) and (lot cost − P_j). Both ≤ 0 when the clearing is
            /// correct: a lot is only taken when it is the cheapest available
            /// source, and P_j is the max lot cost at its destination.</summary>
            public double WorstBandViolation = double.NegativeInfinity;
            /// <summary>Max over phase-2 lots of ask_i − realized export net:
            /// how much a seller could regret having sold locally at a price
            /// held to the phase-1 ask instead of exporting, given phase-2
            /// draws deepen the exit position past the sustained point the ask
            /// was computed at. The lot-granularity bound of the "no seller
            /// regrets skipping phase 2" property.</summary>
            public double WorstPhase2Regret = double.NegativeInfinity;
        }
        public static List<SettleRecord>? SettleTelemetry;
        public SettleRecord? CurrentRecord;

        public void Bind(WorldState w, IAccessCosts costs)
        {
            _w = w; _costs = costs;
            int C = costs.ClusterCount, R = ResourceCatalog.Count;
            for (int r = 0; r < R; r++)
                _seedPrice[r] = ResourceCatalog.Anchor[r] * 0.95;
            _seedPrice[(int)Res.Services] = 1.0;
            double[][] Grid(double[]? seed)
            {
                var g = new double[R][];
                for (int r = 0; r < R; r++)
                {
                    g[r] = new double[C];
                    if (seed != null) for (int c = 0; c < C; c++) g[r][c] = seed[r];
                }
                return g;
            }
            // Price EMAs seed at the old citywide scalar's seed so the first
            // tick reads exactly what it read before the localization.
            _emaDelivered = Grid(_seedPrice); _emaOrigin = Grid(_seedPrice);
            _emaDeliveredQty = Grid(null); _emaOriginQty = Grid(null);
            _tickDeliveredPaid = Grid(null); _tickDeliveredQty = Grid(null);
            _tickOriginRev = Grid(null); _tickOriginQty = Grid(null);
            _cityDelivered = (double[])_seedPrice.Clone();
            _cityOrigin = (double[])_seedPrice.Clone();
            _supRemaining = new double[C]; _demRemaining = new double[C];
            _askOf = new double[C]; _pj = new double[C];
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
            for (int r = 0; r < ResourceCatalog.Count; r++)
            {
                var res = (Res)r;
                if (!ResourceCatalog.IsTradable(res))
                { _producerCluster[r] = null!; _producerHaul[r] = null!; continue; }
                var producers = new List<int>();
                foreach (var f in _w.Firms)
                    if (!f.Dead && f.Parcel >= 0 && f.Output == res
                        && (f.Sector == ZoneKind.Extractor || f.Sector == ZoneKind.Industrial))
                    {
                        int c = _w.Parcels[f.Parcel].Cluster;
                        if (!producers.Contains(c)) producers.Add(c);
                    }
                producers.Sort();
                double wgt = ResourceCatalog.Weight[r];
                var haul = new double[producers.Count][];
                for (int k = 0; k < producers.Count; k++)
                {
                    haul[k] = new double[C];
                    for (int c = 0; c < C; c++)
                        haul[k][c] = _costs.Cost(producers[k], c, AccessPurpose.Freight)
                                     * FreightCostPerMinute * wgt;
                }
                _producerCluster[r] = producers.ToArray();
                _producerHaul[r] = haul;
            }
        }

        private double GroupSustained(Res r, TradeExit e)
        {
            double total = 0;
            foreach (var x in _w.Exits)
                if (x.Resource == r && RegionGroupOf(x) == RegionGroupOf(e)) total += x.SustainedQ;
            return total;
        }

        /// <summary>Adjacent-exit coupling at the fundamental (§4.5): road exits
        /// share the surrounding region's market; each rail corridor is its OWN
        /// one-dimensional market (a second corridor is the design's relief valve
        /// for rail saturation — scrutiny finding #9); sea/air baths stand alone.
        /// Hosts may override via TradeExit.RegionGroup.</summary>
        private static int RegionGroupOf(TradeExit e) => e.RegionGroup >= 0 ? e.RegionGroup
            : e.Mode == ExitMode.Road ? 0
            : 1000 + e.Id;

        // Positions are NET sustained flow (exports − imports): a region the city
        // imports from is undersupplied — marginal exports into it fetch the
        // anchor, they are not depressed by the import volume. Depth builds only
        // in the direction actually pushed.
        // The sustained EMA already contains the routine daily volume, so the
        // within-tick position is max(sustained, q) — a routine day trades at the
        // sustained marginal p(Q); only volume beyond the routine position pushes
        // deeper (no p(2Q) double count — scrutiny finding #10). Bursts ride the
        // transient layer.
        public double ExportMarginal(TradeExit e, double qInTick, EconParams p)
        {
            double eff = Math.Max(0, Math.Max(GroupSustained(e.Resource, e), qInTick)
                                     + p.TradeTransientBeta * e.TransientB);
            double depth = double.IsInfinity(e.D) ? 0 : e.T * Math.Pow(eff / e.Rho, 1.0 / e.D);
            return e.Anchor - e.PerUnitHandling - depth;
        }

        public double ImportMarginal(TradeExit e, double qInTick, EconParams p)
        {
            double eff = Math.Max(0, Math.Max(-GroupSustained(e.Resource, e), qInTick)
                                     + p.TradeTransientBeta * e.TransientB);
            double depth = double.IsInfinity(e.D) ? 0 : e.T * Math.Pow(eff / e.Rho, 1.0 / e.D);
            return e.Anchor + e.PerUnitHandling + depth;
        }

        /// <summary>A capacity-capped exit running at ≥90% sustained utilization
        /// cannot price the margin for Tier B/C bids or the parity overlay.</summary>
        private static bool Saturated(TradeExit e)
            => e.Capacity > 0 && Math.Abs(e.SustainedQ) >= 0.9 * e.Capacity;

        // ---- IPriceContext (what Tier B/C read) -----------------------------

        /// <summary>Statistic of what buyers AT cluster c actually paid per
        /// delivered unit of r, shrunk toward the citywide realized prior with
        /// prior weight n0 = TradePricePriorVolume in volume units: a cluster
        /// with no local trade history reads what participants in this city
        /// actually pay (hearsay from real transactions), a thick market reads
        /// mostly its own. Never a modeled surface — no law is evaluated at a
        /// hypothetical volume here.</summary>
        public double DeliveredStat(Res r, int cluster)
        {
            if (MutantCitywideGoodsPrice) return _cityDelivered[(int)r];
            double n = _emaDeliveredQty[(int)r][cluster];
            double n0 = Math.Max(0, _p.TradePricePriorVolume);
            if (n + n0 <= 1e-12) return _cityDelivered[(int)r];
            return (n * _emaDelivered[(int)r][cluster] + n0 * _cityDelivered[(int)r]) / (n + n0);
        }

        /// <summary>Statistic of what sellers AT cluster c actually netted per
        /// unit of r (local sales at the destination price minus haul, plus
        /// export lots at their route's net). Same shrinkage as DeliveredStat.</summary>
        public double OriginStat(Res r, int cluster)
        {
            if (MutantCitywideGoodsPrice) return _cityOrigin[(int)r];
            double n = _emaOriginQty[(int)r][cluster];
            double n0 = Math.Max(0, _p.TradePricePriorVolume);
            if (n + n0 <= 1e-12) return _cityOrigin[(int)r];
            return (n * _emaOrigin[(int)r][cluster] + n0 * _cityOrigin[(int)r]) / (n + n0);
        }

        /// <summary>Citywide volume-weighted realized delivered price — the
        /// shrinkage prior. Overlay/telemetry read; consumers price their own
        /// cluster via DeliveredStat.</summary>
        public double CityDelivered(Res r) => _cityDelivered[(int)r];
        public double CityOrigin(Res r) => _cityOrigin[(int)r];
        /// <summary>Sustained transacted-volume evidence behind DeliveredStat
        /// at (r, c) — what the shrinkage weighs the local statistic by.</summary>
        public double DeliveredEvidence(Res r, int cluster) => _emaDeliveredQty[(int)r][cluster];
        public double OriginEvidence(Res r, int cluster) => _emaOriginQty[(int)r][cluster];

        public double DeliveredCost(Res r, int cluster)
        {
            // The standing QUOTE: the marginal alternative a buyer at c could
            // check right now — cheapest of (a producing cluster's realized
            // origin price plus its routed haul) and import parity. The local
            // leg prices each producing cluster at its OWN OriginStat; no
            // citywide scalar enters.
            var producers = _producerCluster[(int)r];
            double local = double.PositiveInfinity;
            if (producers != null)
            {
                var haul = _producerHaul[(int)r];
                for (int k = 0; k < producers.Length; k++)
                {
                    double v = OriginStat(r, producers[k]) + haul[k][cluster];
                    if (v < local) local = v;
                }
            }
            double wgt = ResourceCatalog.Weight[(int)r];
            double import = double.PositiveInfinity;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r || Saturated(x)) continue;
                double m = ImportMarginal(x, 0, _p) + _haulToExit[e][cluster] * wgt;
                if (m < import) import = m;
            }
            double best = Math.Min(local, import);
            return double.IsInfinity(best) ? _cityDelivered[(int)r] * 2 : best;
        }

        public double BestExportNet(Res r, int cluster)
        {
            double wgt = ResourceCatalog.Weight[(int)r];
            double best = 0;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r || Saturated(x)) continue;
                double m = ExportMarginal(x, 0, _p) - _haulToExit[e][cluster] * wgt;
                if (m > best) best = m;
            }
            return best;
        }

        private EconParams _p = new EconParams();
        public void SetParams(EconParams p) => _p = p;

        /// <summary>This tick's realized flows for the resource just cleared —
        /// the engine's settlement reads these immediately after ClearTick.
        /// DeliveredPaid[c]: what buyers at c owe (local lots at their
        /// destination's clearing price P_c plus import lots at delivered
        /// cost). OriginRev[c]: what sellers at c are owed (local sales at
        /// P_dst − haul plus export lots at their net).</summary>
        public double[] TickDeliveredPaid(Res r) => _tickDeliveredPaid[(int)r];
        public double[] TickDeliveredQty(Res r) => _tickDeliveredQty[(int)r];
        public double[] TickOriginRev(Res r) => _tickOriginRev[(int)r];
        public double[] TickOriginQty(Res r) => _tickOriginQty[(int)r];

        /// <summary>Clear one tradable resource for one tick, with every lot
        /// carrying its origin and destination cluster.
        ///
        /// Phase 1 — demand fills: round-robin over demand clusters in index
        /// order, one lot per pass, each lot drawn from the cheapest source
        /// FOR THAT DESTINATION — a producing cluster at ask_i + haul_ij (ask_i
        /// is the seller's opportunity cost: best export net from i at the
        /// sustained position, floored at 0 — a seller with no exit worth using
        /// sells at any positive price rather than hold unsold) or an import
        /// exit at its live marginal plus haul. Round-robin, not
        /// cluster-0-first, so near-tied destinations split a cheap source
        /// instead of the lowest index taking all of it. All of a destination's
        /// phase-1 lots settle at ONE uniform price P_j = the most expensive
        /// lot cost delivered to j this tick (the clearing price at the margin
        /// — the fills-all pattern the housing auction established): in
        /// shortage the marginal lot is an import and P_j rides import parity
        /// at j; in surplus it is a local ask and P_j rides delivered export
        /// parity. Sellers net P_j − haul ≥ ask_i on every route used, so no
        /// seller regrets skipping phase 2 up to lot granularity (phase-2
        /// positions deepen below the ask computed at the sustained position;
        /// measured by goodsprobe on the reference fixture, seeds 0–3,
        /// t≤300: worst ask_i − realized phase-2 net = 1.41, i.e. the exits'
        /// within-tick depth at burst volumes, not a pricing error).
        ///
        /// Phase 2 — remaining supply exports: each lot picks the best (source,
        /// exit) pair by ExportMarginal − haul on the lot's ACTUAL route (the
        /// volume-weighted MeanHaul this replaced priced every source at the
        /// same average). Unsold is what no pair nets positive for — and it now
        /// remains AT specific clusters: a remote high-haul producer's unsold
        /// output is ITS cluster's lost revenue, not a citywide haircut.
        ///
        /// Determinism: no RNG; fixed iteration order; ties break by lower
        /// cluster index, then lower exit id (strict inequality on candidates
        /// scanned in that order).</summary>
        public ClearResult ClearTick(Res r, double supply, double demand,
                                     double[] supplyByCluster, double[] demandByCluster, EconParams p)
        {
            var result = new ClearResult();
            int ri = (int)r;
            int C = _supRemaining.Length;
            foreach (var x in _w.Exits)
                if (x.Resource == r) { x.DrawnThisTick = 0; x.ExportedThisTick = 0; x.ImportedThisTick = 0; }
            // Defensive re-clear for fixtures that drive ClearTick directly
            // more than once between EndTicks; the engine calls once per
            // resource per tick and EndTick clears after folding.
            Array.Clear(_tickDeliveredPaid[ri], 0, C);
            Array.Clear(_tickDeliveredQty[ri], 0, C);
            Array.Clear(_tickOriginRev[ri], 0, C);
            Array.Clear(_tickOriginQty[ri], 0, C);

            SettleRecord? rec = null;
            if (SettleTelemetry != null)
            { rec = new SettleRecord { Resource = r }; SettleTelemetry.Add(rec); CurrentRecord = rec; }

            double wgt = ResourceCatalog.Weight[ri];
            _srcClusters.Clear();
            _localLots.Clear();
            for (int c = 0; c < C; c++)
            {
                _supRemaining[c] = supplyByCluster[c];
                _demRemaining[c] = demandByCluster[c];
                if (supplyByCluster[c] > 1e-9)
                {
                    _srcClusters.Add(c);
                    // Opportunity-cost ask at the sustained position (q=0).
                    double best = 0;
                    for (int e = 0; e < _w.Exits.Count; e++)
                    {
                        var x = _w.Exits[e];
                        if (x.Resource != r) continue;
                        double net = ExportMarginal(x, 0, p) - _haulToExit[e][c] * wgt;
                        if (net > best) best = net;
                    }
                    _askOf[c] = best;
                }
                _pj[c] = 0;
            }

            // ---- phase 1: demand fills, cheapest source per destination ------
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int j = 0; j < C; j++)
                {
                    if (_demRemaining[j] <= 1e-6) continue;
                    double lot = Math.Min(p.LotSize, _demRemaining[j]);
                    int bestSrc = -1; double bestCost = double.PositiveInfinity;
                    foreach (int i in _srcClusters)
                    {
                        if (_supRemaining[i] <= 1e-9) continue;
                        double cost = _askOf[i]
                                      + _costs.Cost(i, j, AccessPurpose.Freight) * FreightCostPerMinute * wgt;
                        if (cost < bestCost) { bestCost = cost; bestSrc = i; }
                    }
                    int bestE = -1; double bestImport = double.PositiveInfinity;
                    for (int e = 0; e < _w.Exits.Count; e++)
                    {
                        var x = _w.Exits[e];
                        if (x.Resource != r) continue;
                        if (x.Capacity > 0 && x.DrawnThisTick + lot > x.Capacity) continue;
                        double cost = ImportMarginal(x, x.DrawnThisTick, p) + _haulToExit[e][j] * wgt;
                        if (cost < bestImport) bestImport = cost;
                        if (cost < bestCost) { bestCost = cost; bestSrc = -1; bestE = e; }
                    }
                    if (bestSrc < 0 && bestE < 0)
                    { _demRemaining[j] = 0; continue; }   // no source at any price: shortage persists
                    if (rec != null && !double.IsInfinity(bestImport))
                        rec.WorstBandViolation = Math.Max(rec.WorstBandViolation, bestCost - bestImport);
                    if (bestSrc >= 0)
                    {
                        double q = Math.Min(lot, _supRemaining[bestSrc]);
                        _supRemaining[bestSrc] -= q;
                        _demRemaining[j] -= q;
                        _localLots.Add((j, bestSrc, q, bestCost));
                    }
                    else
                    {
                        var ex = _w.Exits[bestE];
                        ex.DrawnThisTick += lot;
                        ex.ImportedThisTick += lot;
                        _demRemaining[j] -= lot;
                        result.Imported += lot;
                        result.ImportCost += lot * bestCost;
                        // Import lots settle at their own delivered cost — the
                        // full amount leaves to OutsideWorld, so pricing them
                        // at P_j would strand the difference.
                        _tickDeliveredPaid[ri][j] += lot * bestCost;
                        _tickDeliveredQty[ri][j] += lot;
                    }
                    if (bestCost > _pj[j]) _pj[j] = bestCost;
                    progress = true;
                }
            }

            // The destination's clearing price P_j reads the EXCLUDED
            // CHALLENGER — the cheapest source j did NOT use for one more lot,
            // at end-of-phase-1 positions — never below its dearest used lot.
            // Reading it at the used lots alone degenerates when a
            // destination's whole demand is under one lot (the common case:
            // measured on the plain reference fixture, per-cluster restocking
            // demand runs ~20 units against LotSize 25): every P_j collapses
            // to its own lot's ask-anchored cost, local sellers in citywide
            // shortage realize their opportunity-cost floor while unserved
            // neighbors import at parity, and the industrial sector's realized
            // revenue halves (measured, indprobe seed 0: 0 industrial firms
            // alive from t=15 under the lots-only read vs 9–13 sustained over
            // t=100..300 at the excluded-challenger read; the pre-localization
            // world sustains 11–12). With the challenger read, in
            // shortage the challenger is an import and P_j rides import parity
            // at j; in surplus it is the next local ask and P_j rides
            // delivered export parity — the §4.5 band, from realized books.
            for (int j = 0; j < C; j++)
            {
                if (demandByCluster[j] <= 1e-9) continue;
                double challenger = double.PositiveInfinity;
                foreach (int i in _srcClusters)
                {
                    if (_supRemaining[i] <= 1e-9) continue;
                    double cost = _askOf[i]
                                  + _costs.Cost(i, j, AccessPurpose.Freight) * FreightCostPerMinute * wgt;
                    if (cost < challenger) challenger = cost;
                }
                for (int e = 0; e < _w.Exits.Count; e++)
                {
                    var x = _w.Exits[e];
                    if (x.Resource != r) continue;
                    if (x.Capacity > 0 && x.DrawnThisTick + p.LotSize > x.Capacity) continue;
                    double cost = ImportMarginal(x, x.DrawnThisTick, p) + _haulToExit[e][j] * wgt;
                    if (cost < challenger) challenger = cost;
                }
                if (!double.IsInfinity(challenger) && challenger > _pj[j]) _pj[j] = challenger;
            }

            // Settle local lots at their destination's uniform clearing price.
            // Conservation is per-lot: q·P_j out of the buyer = q·(P_j − haul)
            // to the seller + q·haul to OutsideWorld.
            foreach (var (dst, src, q, cost) in _localLots)
            {
                double haul = cost - _askOf[src];
                double pj = _pj[dst];
                _tickDeliveredPaid[ri][dst] += q * pj;
                _tickDeliveredQty[ri][dst] += q;
                _tickOriginRev[ri][src] += q * (pj - haul);
                _tickOriginQty[ri][src] += q;
                result.LocalFreight += q * haul;
                result.LocalVolume += q;
                if (rec != null)
                    rec.WorstBandViolation = Math.Max(rec.WorstBandViolation, cost - pj);
            }

            // ---- phase 2: remaining supply exports on its own best route -----
            while (true)
            {
                int bi = -1, be = -1; double bestNet = 0, bestLot = 0;
                foreach (int i in _srcClusters)
                {
                    if (_supRemaining[i] <= 1e-6) continue;
                    double lot = Math.Min(p.LotSize, _supRemaining[i]);
                    for (int e = 0; e < _w.Exits.Count; e++)
                    {
                        var x = _w.Exits[e];
                        if (x.Resource != r) continue;
                        if (x.Capacity > 0 && x.DrawnThisTick + lot > x.Capacity) continue;
                        double net = ExportMarginal(x, x.DrawnThisTick, p) - _haulToExit[e][i] * wgt;
                        if (net > bestNet) { bestNet = net; bi = i; be = e; bestLot = lot; }
                    }
                }
                if (bi < 0) break;   // nobody pays a positive net price: unsold
                var exx = _w.Exits[be];
                exx.DrawnThisTick += bestLot;
                exx.ExportedThisTick += bestLot;
                _supRemaining[bi] -= bestLot;
                result.Exported += bestLot;
                result.ExportRevenue += bestLot * bestNet;
                _tickOriginRev[ri][bi] += bestLot * bestNet;
                _tickOriginQty[ri][bi] += bestLot;
                if (rec != null)
                    rec.WorstPhase2Regret = Math.Max(rec.WorstPhase2Regret, _askOf[bi] - bestNet);
            }
            foreach (int i in _srcClusters) result.Unsold += Math.Max(0, _supRemaining[i]);

            if (rec != null)
            {
                for (int c = 0; c < C; c++)
                {
                    rec.OriginRevSum += _tickOriginRev[ri][c];
                    rec.DeliveredPaidSum += _tickDeliveredPaid[ri][c];
                }
                rec.ExportRevenue = result.ExportRevenue;
                rec.ImportCost = result.ImportCost;
                rec.LocalFreight = result.LocalFreight;
            }
            return result;
        }

        /// <summary>Slow layer: sustained EMAs, transient decay, group coupling,
        /// the export-base feedback into Tier B (§4.5), and the fold of this
        /// tick's realized flows into the per-(resource, cluster) price and
        /// volume statistics.</summary>
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
                if (ResourceCatalog.IsRaw(x.Resource)) rawExportSustained += Math.Max(0, x.SustainedQ);
            }
            // Export-base multiplier: sustained raw exports induce local
            // processing demand (Weber pull re-enters via Construction).
            InducedProcessingSlots = 0.35 * rawExportSustained / 3.0;

            int C = _supRemaining.Length;
            for (int r = 0; r < ResourceCatalog.Count; r++)
            {
                if (!ResourceCatalog.IsTradable((Res)r)) continue;
                double dW = 0, dP = 0, oW = 0, oP = 0;
                for (int c = 0; c < C; c++)
                {
                    double dq = _tickDeliveredQty[r][c], oq = _tickOriginQty[r][c];
                    if (dq > 1e-9)
                        _emaDelivered[r][c] = MathUtil.Ema(_emaDelivered[r][c],
                                                           _tickDeliveredPaid[r][c] / dq, PriceEmaRate);
                    if (oq > 1e-9)
                        _emaOrigin[r][c] = MathUtil.Ema(_emaOrigin[r][c],
                                                        _tickOriginRev[r][c] / oq, PriceEmaRate);
                    _emaDeliveredQty[r][c] = MathUtil.Ema(_emaDeliveredQty[r][c], dq, p.TradeSustainAlpha);
                    _emaOriginQty[r][c] = MathUtil.Ema(_emaOriginQty[r][c], oq, p.TradeSustainAlpha);
                    dW += _emaDeliveredQty[r][c]; dP += _emaDelivered[r][c] * _emaDeliveredQty[r][c];
                    oW += _emaOriginQty[r][c]; oP += _emaOrigin[r][c] * _emaOriginQty[r][c];
                    _tickDeliveredPaid[r][c] = 0; _tickDeliveredQty[r][c] = 0;
                    _tickOriginRev[r][c] = 0; _tickOriginQty[r][c] = 0;
                }
                // A city with no realized volume anywhere keeps the seed prior.
                if (dW > 1e-9) _cityDelivered[r] = dP / dW;
                if (oW > 1e-9) _cityOrigin[r] = oP / oW;
            }
        }

        /// <summary>Overlay: parity band for a resource at a cluster (§4.7).</summary>
        public (double exportParity, double importParity) ParityBand(Res r, int cluster)
        {
            double lo = 0, hi = double.PositiveInfinity;
            for (int e = 0; e < _w.Exits.Count; e++)
            {
                var x = _w.Exits[e];
                if (x.Resource != r || Saturated(x)) continue;
                double wgt = ResourceCatalog.Weight[(int)r];
                lo = Math.Max(lo, ExportMarginal(x, 0, _p) - _haulToExit[e][cluster] * wgt);
                hi = Math.Min(hi, ImportMarginal(x, 0, _p) + _haulToExit[e][cluster] * wgt);
            }
            return (lo, hi);
        }
    }
}
