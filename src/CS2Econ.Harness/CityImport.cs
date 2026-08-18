using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    // ---------------------------------------------------------------------
    // Real-map import: run the economy on a real road topology instead of
    // the uniform grid. Three pieces:
    //   CityGraph      — self-contained `.cs2city` reader/writer (nodes+edges)
    //   ImportedAccess — IAccessCosts over clustered real nodes
    //   CityImport     — layers the SyntheticCity economy template on top of
    //                    the imported geometry (zoning by radius, districts by
    //                    quadrant, exits at the periphery)
    // ---------------------------------------------------------------------

    /// <summary>Minimal `.cs2city` reader/writer, ported from the routing repo's
    /// src/CS2Path.Core/CityExport.cs (the format authority) — copied rather than
    /// referenced so this harness stays standalone. Only nodes and edges are
    /// consumed; the optional traffic/demand sections are checksum-verified with
    /// the rest of the payload and then skipped.
    ///
    /// Layout (little-endian): "CS2CITY\0" u32 version u32 flags, u32 nodeCount,
    /// u32 edgeCount, nodes (f32 x, f32 y), edges (u32 tail, u32 head,
    /// f32 timeFree seconds, f32 money, f32 comfort, f32 capacity,
    /// f32 jamCapacity), optional sections, u32 FNV-1a checksum.</summary>
    public sealed class CityGraph
    {
        public const uint Magic0 = 0x43325343; // "CS2C"
        public const uint Magic1 = 0x00595449; // "ITY\0"
        public const uint CurrentVersion = 1;

        public int NodeCount;
        public float[] X = Array.Empty<float>();
        public float[] Y = Array.Empty<float>();
        public int[] Tail = Array.Empty<int>();
        public int[] Head = Array.Empty<int>();
        public float[] TimeFree = Array.Empty<float>();   // seconds, free-flow
        public float[] Capacity = Array.Empty<float>();

        public int EdgeCount => Tail.Length;

        public static CityGraph Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic0 || r.ReadUInt32() != Magic1)
                throw new InvalidDataException("not a CS2CITY export");
            var rest = new MemoryStream();
            stream.CopyTo(rest);
            var all = rest.ToArray();
            if (all.Length < 4) throw new InvalidDataException("truncated export");
            int payloadLen = all.Length - 4;
            uint stored = (uint)(all[payloadLen] | (all[payloadLen + 1] << 8)
                               | (all[payloadLen + 2] << 16) | (all[payloadLen + 3] << 24));
            var payload = new byte[payloadLen];
            Array.Copy(all, payload, payloadLen);
            uint actual = Fnv1a(payload);
            if (stored != actual)
                throw new InvalidDataException($"export checksum mismatch (file corrupt or truncated): {stored:X8} != {actual:X8}");

            var g = new CityGraph();
            using (var pr = new BinaryReader(new MemoryStream(payload)))
            {
                uint version = pr.ReadUInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException($"export version {version} != supported {CurrentVersion}");
                pr.ReadUInt32();                          // flags — sections skipped below
                g.NodeCount = pr.ReadInt32();
                int m = pr.ReadInt32();
                g.X = new float[g.NodeCount]; g.Y = new float[g.NodeCount];
                for (int v = 0; v < g.NodeCount; v++) { g.X[v] = pr.ReadSingle(); g.Y[v] = pr.ReadSingle(); }
                g.Tail = new int[m]; g.Head = new int[m];
                g.TimeFree = new float[m]; g.Capacity = new float[m];
                for (int e = 0; e < m; e++)
                {
                    g.Tail[e] = pr.ReadInt32(); g.Head[e] = pr.ReadInt32();
                    g.TimeFree[e] = pr.ReadSingle();
                    pr.ReadSingle(); pr.ReadSingle();     // money, comfort — unused here
                    g.Capacity[e] = pr.ReadSingle();
                    pr.ReadSingle();                      // jamCapacity — unused here
                }
                // Traffic/demand sections (flags 1|2): already checksum-covered;
                // safe to ignore without parsing.
            }
            g.Validate();
            return g;
        }

        public void Write(string path)
        {
            var payload = new MemoryStream();
            using (var w = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(CurrentVersion);
                w.Write(0u);                              // flags: no traffic/demand
                w.Write(NodeCount);
                w.Write(EdgeCount);
                for (int v = 0; v < NodeCount; v++) { w.Write(X[v]); w.Write(Y[v]); }
                for (int e = 0; e < EdgeCount; e++)
                {
                    w.Write(Tail[e]); w.Write(Head[e]);
                    w.Write(TimeFree[e]);
                    w.Write(0f); w.Write(0f);             // money, comfort — not modeled
                    w.Write(Capacity[e]);
                    w.Write(Capacity[e]);                 // jamCapacity ≈ capacity
                }
            }
            var bytes = payload.ToArray();
            using var stream = File.Create(path);
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic0); w.Write(Magic1);
                w.Write(bytes);
                w.Write(Fnv1a(bytes));
            }
        }

        public void Validate()
        {
            if (NodeCount <= 0) throw new InvalidDataException("export has no nodes");
            if (X.Length != NodeCount || Y.Length != NodeCount)
                throw new InvalidDataException("coordinate arrays disagree with node count");
            for (int e = 0; e < EdgeCount; e++)
            {
                if (Tail[e] < 0 || Tail[e] >= NodeCount || Head[e] < 0 || Head[e] >= NodeCount)
                    throw new InvalidDataException($"edge {e} references node out of range");
                if (!(TimeFree[e] > 0) || !float.IsFinite(TimeFree[e]))
                    throw new InvalidDataException($"edge {e} has non-positive or non-finite free-flow time");
            }
        }

        private static uint Fnv1a(byte[] data)
        {
            uint h = 2166136261u;
            foreach (var b in data) { h ^= b; h *= 16777619u; }
            return h;
        }
    }

    /// <summary>Access provider over a real road graph: nodes are bucketed into
    /// ~200–260 clusters on a uniform x/y grid over the bounding box (empty
    /// buckets dropped, ids remapped dense); cluster-to-cluster cost is the
    /// shortest-path free-flow time in MINUTES between cluster representatives
    /// (node nearest each cluster centroid), one Dijkstra per representative over
    /// the full node graph.
    ///
    /// Congestion/express surface mirrors GridAccess:
    ///   SetCongestion(c, mult) rescales every node-graph edge incident to
    ///   cluster c (weight × max of the two endpoint clusters' multipliers, the
    ///   exact GridAccess rule) and triggers a node-level recompute;
    ///   AddExpressLink(a, b, minutes) adds a symmetric cluster-level shortcut,
    ///   folded in by relaxing the cached cluster matrix (cheap, no Dijkstras).
    /// Version bumps whenever either recompute actually runs.</summary>
    public sealed class ImportedAccess : IAccessCosts
    {
        private readonly int _n;                          // nodes
        private readonly float[] _x, _y;
        private readonly int[] _tail, _head;
        private readonly float[] _timeFree;               // seconds
        private readonly int[] _csrStart, _csrEdge;       // out-edges per node

        public int ClusterCount { get; }
        public int NodeCount => _n;
        public int EdgeCount => _tail.Length;
        public int[] ClusterOfNode;                       // node → cluster
        public double[] ClusterX, ClusterY;               // member centroid (input units)
        public int[] NodesInCluster;
        public int[] Representative;                      // node nearest centroid

        private readonly double[] _congestionMult;
        private readonly List<(int a, int b, double minutes)> _expressLinks = new List<(int, int, double)>();
        private double[][] _baseDist = Array.Empty<double[]>();  // minutes, congestion applied
        private double[][] _dist = Array.Empty<double[]>();      // + express relaxation
        private int _version;
        private bool _nodeDirty = true, _clusterDirty = true;

        public ImportedAccess(CityGraph g, int targetClustersLo = 200, int targetClustersHi = 260)
        {
            _n = g.NodeCount;
            _x = g.X; _y = g.Y;
            _tail = g.Tail; _head = g.Head; _timeFree = g.TimeFree;

            // CSR out-adjacency.
            _csrStart = new int[_n + 1];
            for (int e = 0; e < _tail.Length; e++) _csrStart[_tail[e] + 1]++;
            for (int v = 0; v < _n; v++) _csrStart[v + 1] += _csrStart[v];
            _csrEdge = new int[_tail.Length];
            var fill = new int[_n];
            for (int e = 0; e < _tail.Length; e++) _csrEdge[_csrStart[_tail[e]] + fill[_tail[e]]++] = e;

            // ---- grid-bucket nodes over the bounding box ---------------------
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int v = 0; v < _n; v++)
            {
                if (_x[v] < minX) minX = _x[v]; if (_x[v] > maxX) maxX = _x[v];
                if (_y[v] < minY) minY = _y[v]; if (_y[v] > maxY) maxY = _y[v];
            }
            double w = Math.Max(1e-9, maxX - minX), h = Math.Max(1e-9, maxY - minY);
            double longSide = Math.Max(w, h);
            int targetMid = (targetClustersLo + targetClustersHi) / 2;

            // Scan cell granularity for the non-empty bucket count nearest the
            // target band (prefer inside it). Deterministic, geometry-only.
            int bestG = 8, bestScore = int.MaxValue;
            for (int gCells = 8; gCells <= 96; gCells++)
            {
                int nonEmpty = CountNonEmpty(gCells, minX, minY, w, h, longSide, out _, out _);
                bool inBand = nonEmpty >= targetClustersLo && nonEmpty <= targetClustersHi;
                int score = Math.Abs(nonEmpty - targetMid) + (inBand ? 0 : 1000);
                if (score < bestScore) { bestScore = score; bestG = gCells; }
            }
            int nc = CountNonEmpty(bestG, minX, minY, w, h, longSide, out int gx, out int gy);

            // Dense cluster ids in row-major bucket order.
            var bucketOf = new int[_n];
            var denseId = new Dictionary<int, int>();
            for (int v = 0; v < _n; v++)
            {
                int bx = Math.Min(gx - 1, (int)((_x[v] - minX) / w * gx));
                int by = Math.Min(gy - 1, (int)((_y[v] - minY) / h * gy));
                bucketOf[v] = by * gx + bx;
            }
            var order = new List<int>();
            foreach (var b in bucketOf) if (!denseId.ContainsKey(b)) { denseId[b] = -1; }
            foreach (var b in SortedKeys(denseId)) { denseId[b] = order.Count; order.Add(b); }
            ClusterCount = nc;

            ClusterOfNode = new int[_n];
            NodesInCluster = new int[nc];
            ClusterX = new double[nc]; ClusterY = new double[nc];
            for (int v = 0; v < _n; v++)
            {
                int c = denseId[bucketOf[v]];
                ClusterOfNode[v] = c;
                NodesInCluster[c]++;
                ClusterX[c] += _x[v]; ClusterY[c] += _y[v];
            }
            for (int c = 0; c < nc; c++) { ClusterX[c] /= NodesInCluster[c]; ClusterY[c] /= NodesInCluster[c]; }

            // Representative: member node nearest the centroid.
            Representative = new int[nc];
            var bestD = new double[nc];
            for (int c = 0; c < nc; c++) bestD[c] = double.PositiveInfinity;
            for (int v = 0; v < _n; v++)
            {
                int c = ClusterOfNode[v];
                double dx = _x[v] - ClusterX[c], dy = _y[v] - ClusterY[c];
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD[c]) { bestD[c] = d2; Representative[c] = v; }
            }

            _congestionMult = new double[nc];
            for (int c = 0; c < nc; c++) _congestionMult[c] = 1.0;
        }

        private int CountNonEmpty(int gCells, float minX, float minY, double w, double h,
                                  double longSide, out int gx, out int gy)
        {
            double cell = longSide / gCells;
            gx = Math.Max(1, (int)Math.Ceiling(w / cell));
            gy = Math.Max(1, (int)Math.Ceiling(h / cell));
            var seen = new HashSet<int>();
            for (int v = 0; v < _n; v++)
            {
                int bx = Math.Min(gx - 1, (int)((_x[v] - minX) / w * gx));
                int by = Math.Min(gy - 1, (int)((_y[v] - minY) / h * gy));
                seen.Add(by * gx + bx);
            }
            return seen.Count;
        }

        private static IEnumerable<int> SortedKeys(Dictionary<int, int> d)
        {
            var keys = new List<int>(d.Keys);
            keys.Sort();
            return keys;
        }

        public int Version { get { EnsureComputed(); return _version; } }

        public double Cost(int a, int b, AccessPurpose purpose)
        {
            EnsureComputed();
            double d = _dist[a][b];
            return purpose == AccessPurpose.Freight ? d * 1.2 : d;
        }

        public void SetCongestion(int cluster, double multiplier)
        {
            _congestionMult[cluster] = multiplier;
            _nodeDirty = true;
        }

        public void AddExpressLink(int a, int b, double minutes)
        {
            _expressLinks.Add((a, b, minutes));
            _clusterDirty = true;
        }

        /// <summary>Nearest cluster to a point (exit placement helpers).</summary>
        public int NearestCluster(double x, double y)
        {
            int best = 0; double bestD = double.PositiveInfinity;
            for (int c = 0; c < ClusterCount; c++)
            {
                double dx = ClusterX[c] - x, dy = ClusterY[c] - y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD) { bestD = d2; best = c; }
            }
            return best;
        }

        private void EnsureComputed()
        {
            if (_nodeDirty)
            {
                int nc = ClusterCount;
                _baseDist = new double[nc][];
                for (int c = 0; c < nc; c++) _baseDist[c] = DijkstraMinutes(Representative[c]);
                // Directed one-ways can strand a representative even though the
                // converter keeps one weakly-connected component; cap unreachable
                // pairs at 3× the worst finite time so the economy stays finite.
                double maxFinite = 0;
                for (int i = 0; i < nc; i++)
                    for (int j = 0; j < nc; j++)
                        if (!double.IsPositiveInfinity(_baseDist[i][j]) && _baseDist[i][j] > maxFinite)
                            maxFinite = _baseDist[i][j];
                double cap = maxFinite * 3;
                for (int i = 0; i < nc; i++)
                    for (int j = 0; j < nc; j++)
                        if (double.IsPositiveInfinity(_baseDist[i][j])) _baseDist[i][j] = cap;
                _nodeDirty = false;
                _clusterDirty = true;
            }
            if (_clusterDirty)
            {
                int nc = ClusterCount;
                _dist = new double[nc][];
                for (int i = 0; i < nc; i++) _dist[i] = (double[])_baseDist[i].Clone();
                // Fold express links in at the cluster level; iterate so links
                // can chain (GridAccess gets this for free inside its Dijkstra).
                bool changed = true; int guard = 0;
                while (changed && guard++ < 8)
                {
                    changed = false;
                    foreach (var (a, b, m) in _expressLinks)
                        for (int i = 0; i < nc; i++)
                            for (int j = 0; j < nc; j++)
                            {
                                double viaAb = _dist[i][a] + m + _dist[b][j];
                                double viaBa = _dist[i][b] + m + _dist[a][j];
                                double best = Math.Min(viaAb, viaBa);
                                if (best < _dist[i][j]) { _dist[i][j] = best; changed = true; }
                            }
                }
                _version++;
                _clusterDirty = false;
            }
        }

        /// <summary>Single-source Dijkstra over the node graph; returns minutes to
        /// every representative's cluster (read off per cluster afterwards).
        /// Edge weight = timeFree × max of the endpoint clusters' congestion
        /// multipliers (the GridAccess rule).</summary>
        private double[] DijkstraMinutes(int source)
        {
            var dist = new double[_n];
            for (int i = 0; i < _n; i++) dist[i] = double.PositiveInfinity;
            dist[source] = 0;
            var heap = new BinaryHeap(_n);
            heap.Push(source, 0);
            var done = new bool[_n];
            while (heap.Count > 0)
            {
                heap.Pop(out int v, out double d);
                if (done[v]) continue;
                done[v] = true;
                for (int k = _csrStart[v]; k < _csrStart[v + 1]; k++)
                {
                    int e = _csrEdge[k];
                    int to = _head[e];
                    double mult = Math.Max(_congestionMult[ClusterOfNode[_tail[e]]],
                                           _congestionMult[ClusterOfNode[to]]);
                    double nd = d + _timeFree[e] / 60.0 * mult;
                    if (nd < dist[to]) { dist[to] = nd; heap.Push(to, nd); }
                }
            }
            var byCluster = new double[ClusterCount];
            for (int c = 0; c < ClusterCount; c++) byCluster[c] = dist[Representative[c]];
            return byCluster;
        }

        /// <summary>Array binary min-heap with lazy deletion — SortedSet is too
        /// slow for a few hundred Dijkstras over ~10⁴–10⁵ nodes.</summary>
        private sealed class BinaryHeap
        {
            private int[] _node; private double[] _key; private int _count;
            public BinaryHeap(int capacity) { _node = new int[capacity]; _key = new double[capacity]; }
            public int Count => _count;
            public void Push(int node, double key)
            {
                if (_count == _node.Length)
                {
                    Array.Resize(ref _node, _count * 2);
                    Array.Resize(ref _key, _count * 2);
                }
                int i = _count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_key[parent] <= key) break;
                    _node[i] = _node[parent]; _key[i] = _key[parent]; i = parent;
                }
                _node[i] = node; _key[i] = key;
            }
            public void Pop(out int node, out double key)
            {
                node = _node[0]; key = _key[0];
                int last = --_count;
                double lk = _key[last]; int ln = _node[last];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= last) break;
                    if (child + 1 < last && _key[child + 1] < _key[child]) child++;
                    if (_key[child] >= lk) break;
                    _node[i] = _node[child]; _key[i] = _key[child]; i = child;
                }
                _node[i] = ln; _key[i] = lk;
            }
        }
    }

    /// <summary>Builds a WorldState on an imported road graph: the SyntheticCity
    /// economy template (radial zoning from the node-weighted centroid, districts
    /// by quadrant, amenity falling with radius plus hash noise) layered onto
    /// real geometry; parcels per cluster follow node density; seeding is shared
    /// with SyntheticCity (Seeding class).</summary>
    public static class CityImport
    {
        public sealed class Options
        {
            public int TargetClustersLo = 200, TargetClustersHi = 260;
            public double MeanParcelsPerCluster = 10.0;   // per-cluster count ∝ nodes, clamped 4..16
            public int SeedHouseholds = 9000;
            public double PrebuiltShare = 0.45;
            public bool RailTerminal = false;
            public bool SeaExit = false;
            public ulong Seed = 20260806;
        }

        public static (WorldState w, ImportedAccess access) BuildWorld(string path, Options cfg, EconParams p)
        {
            var g = CityGraph.Read(path);
            var access = new ImportedAccess(g, cfg.TargetClustersLo, cfg.TargetClustersHi);
            // TNTP planar coordinates are state-plane FEET (Chicago Regional
            // bbox ≈ 439k × 606k units ≈ 134 × 185 km) — the vacancy kernel's
            // λ is in real meters.
            var w = new WorldState { Rng = new SplitMix64(cfg.Seed), MetersPerUnit = 0.3048 };
            int C = access.ClusterCount;

            // Node-weighted city centroid + per-cluster normalized radius.
            double cx = 0, cy = 0;
            for (int v = 0; v < g.NodeCount; v++) { cx += g.X[v]; cy += g.Y[v]; }
            cx /= g.NodeCount; cy /= g.NodeCount;
            var radius = new double[C];
            double maxR = 1e-9;
            for (int c = 0; c < C; c++)
            {
                double dx = access.ClusterX[c] - cx, dy = access.ClusterY[c] - cy;
                radius[c] = Math.Sqrt(dx * dx + dy * dy);
                if (radius[c] > maxR) maxR = radius[c];
            }

            // ---- clusters (same fields/formulas as SyntheticCity) ------------
            w.Clusters = new ClusterInfo[C];
            for (int c = 0; c < C; c++)
            {
                double r = radius[c] / maxR;
                w.Clusters[c] = new ClusterInfo
                {
                    Id = c, X = access.ClusterX[c], Y = access.ClusterY[c],
                    District = (access.ClusterX[c] > cx ? 1 : 0) + 2 * (access.ClusterY[c] > cy ? 1 : 0),
                    Amenity = 0.6 * (1 - r) + 0.25 * SplitMix64.Hash01((ulong)c * 977),
                    School = 20 + 40 * SplitMix64.Hash01((ulong)c * 1289),
                    Health = 15 + 30 * SplitMix64.Hash01((ulong)c * 2039),
                };
            }

            // Geology over real coordinates: deposit centers per raw with radial
            // falloff scaled to the bounding box (mirrors SyntheticCity's fields;
            // kept separate so the tuned synthetic RNG draw order is untouched).
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int v = 0; v < g.NodeCount; v++)
            {
                if (g.X[v] < minX) minX = g.X[v]; if (g.X[v] > maxX) maxX = g.X[v];
                if (g.Y[v] < minY) minY = g.Y[v]; if (g.Y[v] > maxY) maxY = g.Y[v];
            }
            double halfDiag = 0.5 * Math.Sqrt((maxX - minX) * (double)(maxX - minX)
                                              + (maxY - minY) * (double)(maxY - minY));
            var grng = new SplitMix64(cfg.Seed * 977 + 11);
            int[] centerCount = { 3, 3, 2, 2 };                       // Grain, Wood, Ore, Oil
            double[] centerRadiusFrac = { 0.42, 0.30, 0.18, 0.16 };   // of the half-diagonal
            for (int raw = 0; raw < ResourceCatalog.RawCount; raw++)
                for (int k = 0; k < centerCount[raw]; k++)
                {
                    double px = minX + grng.NextDouble() * (maxX - minX);
                    double py = minY + grng.NextDouble() * (maxY - minY);
                    double rad = centerRadiusFrac[raw] * halfDiag;
                    double strength = 0.75 + 0.25 * grng.NextDouble();
                    for (int c = 0; c < C; c++)
                    {
                        double dx = access.ClusterX[c] - px, dy = access.ClusterY[c] - py;
                        double v2 = strength * Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy) / rad);
                        if (v2 > w.Clusters[c].ResourceSuitability[raw]) w.Clusters[c].ResourceSuitability[raw] = v2;
                    }
                }

            // ---- zoning template by radius (SyntheticCity thresholds; extraction
            // follows the geology outside the urban core) ----------------------
            ZoneKind ZoneFor(int c, ref SplitMix64 rng)
            {
                double r = radius[c] / maxR;
                double roll = rng.NextDouble();
                double maxSuit = 0;
                for (int raw = 0; raw < ResourceCatalog.RawCount; raw++)
                    maxSuit = Math.Max(maxSuit, w.Clusters[c].ResourceSuitability[raw]);
                if (r > 0.35 && maxSuit > 0.45)
                {
                    double pExtract = 0.5 * maxSuit;
                    if (roll < pExtract) return ZoneKind.Extractor;
                    if (roll < pExtract + 0.25) return ZoneKind.ResidentialLow;   // workforce housing
                }
                if (r < 0.18) return roll < 0.5 ? ZoneKind.Commercial : ZoneKind.Office;
                if (r < 0.40) return roll < 0.6 ? ZoneKind.ResidentialHigh : (roll < 0.8 ? ZoneKind.Commercial : ZoneKind.ResidentialLow);
                if (r < 0.75) return roll < 0.7 ? ZoneKind.ResidentialLow : (roll < 0.85 ? ZoneKind.ResidentialHigh : ZoneKind.Commercial);
                return roll < 0.55 ? ZoneKind.ResidentialLow : (roll < 0.8 ? ZoneKind.Industrial : ZoneKind.Commercial);
            }

            // ---- parcels: count follows node density, clamped 4..16 ----------
            double scale = cfg.MeanParcelsPerCluster * C / g.NodeCount;
            for (int c = 0; c < C; c++)
            {
                int nParcels = Math.Min(16, Math.Max(4, (int)Math.Round(access.NodesInCluster[c] * scale)));
                for (int k = 0; k < nParcels; k++)
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
                        // Same owner-eligibility roll as SyntheticCity: the
                        // first household seeded into the parcel claims it.
                        pl.OwnerHousehold = kind == ZoneKind.ResidentialLow && w.Rng.NextDouble() < 0.5
                            ? Parcel.OwnerEligibleSeed : -1;
                    }
                    w.Parcels.Add(pl);
                }
            }

            // ---- firms + households (shared with SyntheticCity) --------------
            double firmMoney = Seeding.SeedFirms(w, access);
            double hhMoney = Seeding.SeedHouseholds(w, p, cfg.SeedHouseholds);

            // ---- trade exits: two most peripheral clusters on opposite sides -
            int exitA = 0;
            for (int c = 1; c < C; c++) if (radius[c] > radius[exitA]) exitA = c;
            double ax = access.ClusterX[exitA] - cx, ay = access.ClusterY[exitA] - cy;
            int exitB = -1;
            for (int c = 0; c < C; c++)
            {
                double dot = (access.ClusterX[c] - cx) * ax + (access.ClusterY[c] - cy) * ay;
                if (dot < 0 && (exitB < 0 || radius[c] > radius[exitB])) exitB = c;
            }
            if (exitB < 0) exitB = exitA == 0 ? C - 1 : 0;   // degenerate geometry fallback

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
            for (int ri = 0; ri < ResourceCatalog.Count; ri++)
            {
                var res = (Res)ri;
                if (!ResourceCatalog.IsTradable(res)) continue;
                double anchor = ResourceCatalog.Anchor[ri];
                AddExit(ExitMode.Road, exitA, res, anchor, t: 0.24 * anchor, d: 2);
                AddExit(ExitMode.Road, exitB, res, anchor, t: 0.24 * anchor, d: 2);
                if (cfg.RailTerminal)
                    AddExit(ExitMode.Rail, exitB, res, anchor, t: 0.04 * anchor, d: 1, handling: 0.19 * anchor);
                if (cfg.SeaExit)
                    AddExit(ExitMode.Sea, access.NearestCluster((minX + maxX) / 2.0, minY), res,
                            anchor, t: 0, d: double.PositiveInfinity, handling: 0.35 * anchor, capacity: 260);
            }

            w.Ledger = new Ledger(hhMoney, firmMoney, initialTreasury: 3000);
            w.RebuildIndices();
            return (w, access);
        }

        // ------------------------------------------------------------------
        // TNTP → .cs2city converter for bstabler/TransportationNetworks data
        // (real traffic-assignment networks; free-flow time in minutes, planar
        // node coordinates). Zone centroids (ids < FIRST THRU NODE) and their
        // connector links are synthetic accounting devices, not roads — both are
        // dropped; the largest weakly-connected component of what remains is
        // kept and remapped dense.
        //
        // Data source (data/chicago-regional/): Transportation Networks for
        // Research Core Team, https://github.com/bstabler/TransportationNetworks
        // — Chicago Regional network, developed by the Chicago Area
        // Transportation Study (CATS). Academic research use; cited per the
        // repository license.
        // ------------------------------------------------------------------
        public static void ConvertTntp(string netPath, string nodePath, string outPath)
        {
            // --- node coordinates ---------------------------------------------
            var xs = new Dictionary<int, float>();
            var ys = new Dictionary<int, float>();
            foreach (var line in File.ReadLines(nodePath))
            {
                var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3 || !int.TryParse(f[0], out int id)) continue;
                if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                xs[id] = x; ys[id] = y;
            }

            // --- links, dropping zone centroids/connectors --------------------
            int firstThru = 1;
            var tails = new List<int>(); var heads = new List<int>();
            var secs = new List<float>(); var caps = new List<float>();
            bool inMeta = true;
            foreach (var line in File.ReadLines(netPath))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (inMeta)
                {
                    if (t.StartsWith("<FIRST THRU NODE>"))
                    {
                        var f0 = t.Substring("<FIRST THRU NODE>".Length).Trim()
                                  .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (f0.Length > 0) int.TryParse(f0[0], out firstThru);
                    }
                    if (t.StartsWith("<END OF METADATA>")) inMeta = false;
                    continue;
                }
                if (t[0] == '~') continue;
                var f = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // init term capacity length ftime b power speed toll type ;
                if (f.Length < 8 || !int.TryParse(f[0], out int a) || !int.TryParse(f[1], out int b)) continue;
                if (a < firstThru || b < firstThru) continue;              // centroid connector
                if (!xs.ContainsKey(a) || !xs.ContainsKey(b)) continue;
                float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float capacity);
                float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float miles);
                float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float ftimeMin);
                float.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float mph);
                float sec = ftimeMin > 0 ? ftimeMin * 60f
                          : (mph > 0 && miles > 0 ? miles / mph * 3600f : 0f);
                if (sec <= 0) sec = 1f;                                    // format demands > 0
                tails.Add(a); heads.Add(b); secs.Add(sec); caps.Add(capacity);
            }
            if (tails.Count == 0) throw new InvalidDataException("no through-links parsed — is this a TNTP net file?");

            // --- largest weakly-connected component, remapped dense -----------
            var idOf = new Dictionary<int, int>();
            foreach (var v in tails) if (!idOf.ContainsKey(v)) idOf[v] = idOf.Count;
            foreach (var v in heads) if (!idOf.ContainsKey(v)) idOf[v] = idOf.Count;
            int n = idOf.Count;
            var adjHead = new int[n]; Array.Fill(adjHead, -1);
            var adjNext = new int[tails.Count * 2]; var adjTo = new int[tails.Count * 2];
            int ac = 0;
            void AddAdj(int a, int b) { adjTo[ac] = b; adjNext[ac] = adjHead[a]; adjHead[a] = ac++; }
            for (int i = 0; i < tails.Count; i++) { AddAdj(idOf[tails[i]], idOf[heads[i]]); AddAdj(idOf[heads[i]], idOf[tails[i]]); }
            var comp = new int[n]; Array.Fill(comp, -1);
            int best = -1, bestSize = 0, ncomp = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < n; s++)
            {
                if (comp[s] >= 0) continue;
                int size = 0;
                stack.Push(s); comp[s] = ncomp;
                while (stack.Count > 0)
                {
                    int v = stack.Pop(); size++;
                    for (int e = adjHead[v]; e >= 0; e = adjNext[e])
                        if (comp[adjTo[e]] < 0) { comp[adjTo[e]] = ncomp; stack.Push(adjTo[e]); }
                }
                if (size > bestSize) { bestSize = size; best = ncomp; }
                ncomp++;
            }

            var finalId = new Dictionary<int, int>();
            var fx = new List<float>(); var fy = new List<float>();
            foreach (var kv in idOf)
                if (comp[kv.Value] == best) { finalId[kv.Key] = -1; }
            var origIds = new List<int>(finalId.Keys);
            origIds.Sort();
            foreach (var orig in origIds) { finalId[orig] = fx.Count; fx.Add(xs[orig]); fy.Add(ys[orig]); }

            var oTail = new List<int>(); var oHead = new List<int>();
            var oSec = new List<float>(); var oCap = new List<float>();
            for (int i = 0; i < tails.Count; i++)
            {
                if (!finalId.TryGetValue(tails[i], out int a) || !finalId.TryGetValue(heads[i], out int b)) continue;
                oTail.Add(a); oHead.Add(b); oSec.Add(secs[i]); oCap.Add(caps[i]);
            }

            var g = new CityGraph
            {
                NodeCount = fx.Count,
                X = fx.ToArray(), Y = fy.ToArray(),
                Tail = oTail.ToArray(), Head = oHead.ToArray(),
                TimeFree = oSec.ToArray(), Capacity = oCap.ToArray(),
            };
            g.Validate();
            g.Write(outPath);
        }
    }
}
