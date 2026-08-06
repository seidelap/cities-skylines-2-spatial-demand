using System;
using System.Collections.Generic;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>Grid-of-clusters access provider: exact Dijkstra over the cluster
    /// graph stands in for the routing rebuild's cached CCH (PLAN §1 — the port
    /// keeps "no independent distance computations" honest; cost fidelity is the
    /// routing repo's problem, economy logic is this repo's).</summary>
    public sealed class GridAccess : IAccessCosts
    {
        public int Cols, Rows;
        public double BaseHopMinutes;
        private double[,] _edgeMult;          // [cluster, direction 0..3] congestion multipliers
        private readonly List<(int a, int b, double minutes)> _expressLinks = new List<(int, int, double)>();
        private double[][] _dist = Array.Empty<double[]>();
        private int _version;
        private bool _dirty = true;

        public GridAccess(int cols, int rows, double baseHopMinutes = 3.0)
        {
            Cols = cols; Rows = rows; BaseHopMinutes = baseHopMinutes;
            _edgeMult = new double[cols * rows, 4];
            for (int i = 0; i < cols * rows; i++)
                for (int d = 0; d < 4; d++) _edgeMult[i, d] = 1.0;
        }

        public int ClusterCount => Cols * Rows;
        public int Version { get { EnsureComputed(); return _version; } }

        public (int x, int y) Pos(int c) => (c % Cols, c / Cols);
        public int At(int x, int y) => y * Cols + x;

        public void SetCongestion(int cluster, double multiplier)
        {
            for (int d = 0; d < 4; d++) _edgeMult[cluster, d] = multiplier;
            _dirty = true;
        }

        /// <summary>An express link (transit line / highway upgrade): a direct
        /// low-cost edge between two clusters. The fiscal-loop scenario adds these
        /// along a corridor.</summary>
        public void AddExpressLink(int a, int b, double minutes)
        {
            _expressLinks.Add((a, b, minutes));
            _dirty = true;
        }

        public double Cost(int a, int b, AccessPurpose purpose)
        {
            EnsureComputed();
            double d = _dist[a][b];
            return purpose == AccessPurpose.Freight ? d * 1.2 : d;
        }

        private void EnsureComputed()
        {
            if (!_dirty) return;
            int n = ClusterCount;
            _dist = new double[n][];
            var adj = BuildAdjacency();
            for (int s = 0; s < n; s++) _dist[s] = Dijkstra(s, adj);
            _version++;
            _dirty = false;
        }

        private List<(int to, double w)>[] BuildAdjacency()
        {
            int n = ClusterCount;
            var adj = new List<(int, double)>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<(int, double)>();
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    int c = At(x, y);
                    if (x + 1 < Cols) Link(adj, c, At(x + 1, y), 0);
                    if (y + 1 < Rows) Link(adj, c, At(x, y + 1), 1);
                }
            foreach (var (a, b, m) in _expressLinks)
            {
                adj[a].Add((b, m)); adj[b].Add((a, m));
            }
            return adj;
        }

        private void Link(List<(int, double)>[] adj, int a, int b, int dir)
        {
            double w = BaseHopMinutes * Math.Max(_edgeMult[a, dir], _edgeMult[b, dir]);
            adj[a].Add((b, w)); adj[b].Add((a, w));
        }

        private double[] Dijkstra(int source, List<(int to, double w)>[] adj)
        {
            int n = ClusterCount;
            var dist = new double[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
            dist[source] = 0;
            var pq = new SortedSet<(double d, int v)>() { (0, source) };
            while (pq.Count > 0)
            {
                var (d, v) = pq.Min; pq.Remove(pq.Min);
                if (done[v]) continue;
                done[v] = true;
                foreach (var (to, w) in adj[v])
                {
                    double nd = d + w;
                    if (nd < dist[to]) { pq.Remove((dist[to], to)); dist[to] = nd; pq.Add((nd, to)); }
                }
            }
            return dist;
        }
    }

    /// <summary>Builds the synthetic city: zoned parcel layout with a center-out
    /// gradient (commercial/office core, high-density ring, low-density fringe,
    /// industry near the edge, extractors at resource corners), seeded population
    /// and firms, road exits, optional rail/sea.</summary>
    public static class SyntheticCity
    {
        public sealed class Config
        {
            public int Cols = 14, Rows = 14;
            public int ParcelsPerCluster = 10;
            public int SeedHouseholds = 9000;
            public double PrebuiltShare = 0.45;   // parcels standing at t0
            public bool RailTerminal = false;
            public bool SeaExit = false;
            public bool ExtractorHeavy = false;   // monoculture-export runs (§6 trade targets)
            public ulong Seed = 20260806;
        }

        public static (WorldState w, GridAccess access) Build(Config cfg, EconParams p)
        {
            var access = new GridAccess(cfg.Cols, cfg.Rows);
            var w = new WorldState { Rng = new SplitMix64(cfg.Seed) };
            int C = access.ClusterCount;
            double cx = (cfg.Cols - 1) / 2.0, cy = (cfg.Rows - 1) / 2.0;
            double maxR = Math.Sqrt(cx * cx + cy * cy);

            // ---- clusters ----------------------------------------------------
            w.Clusters = new ClusterInfo[C];
            for (int c = 0; c < C; c++)
            {
                var (x, y) = access.Pos(c);
                double r = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxR;
                w.Clusters[c] = new ClusterInfo
                {
                    Id = c, X = x, Y = y,
                    District = (x * 2 / cfg.Cols) + 2 * (y * 2 / cfg.Rows),   // quadrants 0..3
                    Amenity = 0.6 * (1 - r) + 0.25 * SplitMix64.Hash01((ulong)c * 977),
                    School = 20 + 40 * SplitMix64.Hash01((ulong)c * 1289),
                    Health = 15 + 30 * SplitMix64.Hash01((ulong)c * 2039),
                };
            }

            // ---- zoning template by radius -----------------------------------
            ZoneKind ZoneFor(int c, ref SplitMix64 rng)
            {
                var (x, y) = access.Pos(c);
                double r = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxR;
                int cornerSize = cfg.ExtractorHeavy ? 4 : 2;
                bool resourceCorner = (x < cornerSize && y < cornerSize)
                                      || (x >= cfg.Cols - cornerSize && y >= cfg.Rows - cornerSize);
                double roll = rng.NextDouble();
                if (resourceCorner)
                    return cfg.ExtractorHeavy
                        ? (roll < 0.6 ? ZoneKind.Extractor : (roll < 0.9 ? ZoneKind.ResidentialLow : ZoneKind.Commercial))
                        : (roll < 0.6 ? ZoneKind.Extractor : ZoneKind.Industrial);
                if (r < 0.18) return roll < 0.5 ? ZoneKind.Commercial : ZoneKind.Office;
                if (r < 0.40) return roll < 0.6 ? ZoneKind.ResidentialHigh : (roll < 0.8 ? ZoneKind.Commercial : ZoneKind.ResidentialLow);
                if (r < 0.75) return roll < 0.7 ? ZoneKind.ResidentialLow : (roll < 0.85 ? ZoneKind.ResidentialHigh : ZoneKind.Commercial);
                if (cfg.ExtractorHeavy)
                    return roll < 0.8 ? ZoneKind.ResidentialLow : ZoneKind.Commercial;
                return roll < 0.55 ? ZoneKind.ResidentialLow : (roll < 0.8 ? ZoneKind.Industrial : ZoneKind.Commercial);
            }

            // ---- parcels -----------------------------------------------------
            for (int c = 0; c < C; c++)
                for (int k = 0; k < cfg.ParcelsPerCluster; k++)
                {
                    var kind = ZoneFor(c, ref w.Rng);
                    var pl = new Parcel { Id = w.Parcels.Count, Cluster = c, Zoned = kind, State = ParcelState.Empty };
                    if (w.Rng.NextDouble() < cfg.PrebuiltShare)
                    {
                        pl.State = ParcelState.Built;
                        pl.Use = kind;
                        pl.Level = 1 + w.Rng.NextInt(2);
                        pl.Condition = 0.75 + 0.25 * w.Rng.NextDouble();
                        pl.Units = LandAccounting.UnitsFor(kind);
                        pl.OwnerOccupied = kind == ZoneKind.ResidentialLow && w.Rng.NextDouble() < 0.5;
                    }
                    w.Parcels.Add(pl);
                }

            // ---- firms in prebuilt firm parcels ------------------------------
            double firmMoney = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential) continue;
                if (w.Rng.NextDouble() < 0.8)
                {
                    var f = new Firm
                    {
                        Id = w.Firms.Count, Sector = pl.Use, Parcel = pl.Id,
                        Money = 300, JobSlots = pl.Units,
                    };
                    w.Firms.Add(f);
                    pl.OccupantFirm = f.Id;
                    firmMoney += f.Money;
                }
            }

            // ---- households into prebuilt residential ------------------------
            var vacancies = new List<int>();
            for (int i = 0; i < w.Parcels.Count; i++)
            {
                var pl = w.Parcels[i];
                if (pl.State == ParcelState.Built && pl.IsResidential)
                    for (int u = 0; u < pl.Units; u++) vacancies.Add(i);
            }
            // Shuffle deterministically.
            for (int i = vacancies.Count - 1; i > 0; i--)
            {
                int j = w.Rng.NextInt(i + 1);
                (vacancies[i], vacancies[j]) = (vacancies[j], vacancies[i]);
            }

            double hhMoney = 0;
            int nSeed = Math.Min(cfg.SeedHouseholds, (int)(vacancies.Count * 0.92));
            // Segment mix: families 45%, singles 25%, students 12%, seniors 18%.
            double[] segShare = { 0.12, 0.13, 0.12, 0.16, 0.16, 0.13, 0.10, 0.08 };
            for (int i = 0; i < nSeed; i++)
            {
                double roll = w.Rng.NextDouble(), acc2 = 0;
                int seg = 0;
                for (int s = 0; s < segShare.Length; s++) { acc2 += segShare[s]; if (roll < acc2) { seg = s; break; } }
                int parcelId = vacancies[i];
                var pl = w.Parcels[parcelId];
                var h = new Household
                {
                    Id = w.Households.Count, Segment = seg, Money = 40 + 40 * w.Rng.NextDouble(),
                    HomeParcel = parcelId, TenureStart = 0,
                    MovingCostDraw = p.MovingCostMean * (0.4 + 1.2 * w.Rng.NextDouble())
                                     * (pl.OwnerOccupied ? p.OwnerMovingCostMult : 1.0),
                };
                pl.OccupantHouseholds.Add(h.Id);
                w.Households.Add(h);
                hhMoney += h.Money;
            }

            // ---- trade exits -------------------------------------------------
            void AddExit(ExitMode mode, int cluster, Res res, double anchor, double t, double d,
                         double handling = 0, double capacity = 0)
            {
                w.Exits.Add(new TradeExit
                {
                    Id = w.Exits.Count, Mode = mode, Cluster = cluster, Resource = res,
                    Anchor = anchor, T = t, Rho = p.RegionSize / 400.0, D = d,
                    PerUnitHandling = handling, Capacity = capacity,
                });
            }
            int westExit = access.At(0, cfg.Rows / 2);
            int eastExit = access.At(cfg.Cols - 1, cfg.Rows / 2);
            foreach (var res in new[] { Res.Raw, Res.Goods })
            {
                double anchor = res == Res.Raw ? 2.6 : 5.4;
                AddExit(ExitMode.Road, westExit, res, anchor, t: 0.62, d: 2);
                AddExit(ExitMode.Road, eastExit, res, anchor, t: 0.62, d: 2);
                if (cfg.RailTerminal)
                    AddExit(ExitMode.Rail, eastExit, res, anchor, t: 0.10, d: 1, handling: 0.50);
                if (cfg.SeaExit)
                    AddExit(ExitMode.Sea, access.At(cfg.Cols / 2, cfg.Rows - 1), res,
                            anchor, t: 0, d: double.PositiveInfinity, handling: 0.55, capacity: 260);
            }

            w.Ledger = new Ledger(hhMoney, firmMoney, initialTreasury: 3000);
            w.RebuildIndices();
            return (w, access);
        }

        public static void AddRailTerminal(WorldState w, GridAccess access, EconParams p)
        {
            int cluster = access.At(access.Cols - 1, access.Rows / 2);
            foreach (var res in new[] { Res.Raw, Res.Goods })
            {
                double anchor = res == Res.Raw ? 2.6 : 5.4;
                w.Exits.Add(new TradeExit
                {
                    Id = w.Exits.Count, Mode = ExitMode.Rail, Cluster = cluster, Resource = res,
                    Anchor = anchor, T = 0.10, D = 1, PerUnitHandling = 0.50,
                    Rho = p.RegionSize / 400.0,
                });
            }
        }
    }
}
