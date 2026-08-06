// Cluster-level generalized access costs over the live road graph — the
// in-game IAccessCosts host (design §3; PLAN §5 stage 7). Standalone by
// requirement: the econ mod must run WITHOUT the routing mod, so this provider
// builds its own coarse cluster graph from Game.Net entities:
//
//   * ~targetClusters spatial buckets over Game.Net.Node positions (grid
//     bucketing; non-empty cells become clusters; count never exceeds target).
//   * Cluster graph from Game.Net.Edge connectivity (m_Start/m_End — verified
//     in the routing repo); arc weight = min free-flow seconds over parallel
//     edges (Game.Net.Curve.m_Length / speed; the SPEED member is not in the
//     component dump, isolated in Verify_EdgeFreeFlowSpeed).
//   * Live congestion multiplier per edge from Game.Net.LaneFlow
//     { float4 m_Duration, m_Distance } (research notes §8: live speed =
//     Σdistance / Σduration — the game's own realized-travel measurement).
//     Lanes are attributed to their edge in Verify_LaneOwnerEdge.
//   * All-pairs cluster costs in MINUTES by Dijkstra over the cluster graph.
//     Version bumps whenever topology is rebuilt or live costs drift >7% on
//     any cluster pair (exhaustive pair scan — a strict superset of sampling;
//     ~200² comparisons is trivial at the engine-tick cadence).
//
// OPTIONAL CS2Path CCH HOOKUP (documented seam, not wired): when the routing
// mod is installed, its ClusterCache maintains nested-dissection cells and CCH
// cluster costs with corridor dirty flags (the EconAdapters.cs provenance:
// "Clusters ← nested-dissection cells from the routing rebuild's ClusterCache;
// IAccessCosts is implemented over its CCH cluster costs + corridor dirty
// flags — design §3: all access terms come from cached CCH machinery"). To
// hook it up, implement IAccessCosts over CS2Path's cluster costs — same
// ClusterOf / Version / Cost semantics — and hand that instance to
// EconomyEngine instead of this provider; EconReader and EconBridgeSystem
// need no changes.

using System;
using System.Collections.Generic;
using CS2Econ.Core;

#if !OUT_OF_GAME_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
#endif

namespace CS2Econ.Mod
{
#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked (the
    /// harness implements IAccessCosts via SyntheticCity, never this). Flat
    /// costs make accidental out-of-game use harmless and visible.</summary>
    public sealed class ClusterAccessProvider : IAccessCosts
    {
        private readonly int _target;
        public ClusterAccessProvider(int targetClusters) { _target = Math.Max(1, targetClusters); }

        public int ClusterCount => _target;
        public int Version { get; private set; } = 1;
        public double Cost(int a, int b, AccessPurpose purpose) => a == b ? 4.0 : 15.0;
        public void MarkDirty() { }
        /// <summary>Mirrors the in-game surface EconBridgeSystem consumes
        /// (topology-churn probe); never dirty out-of-game.</summary>
        public bool Dirty => false;
        /// <summary>In-game this takes the EntityManager (LaneFlow congestion
        /// refresh); out-of-game there is nothing to refresh.</summary>
        public void UpdateLiveCosts() { }
        public int ClusterOf(float x, float z) => 0;
        public (float X, float Z) ClusterCentroid(int cluster) => (0f, 0f);
        public void RebuildClusters() =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
    }
#else
    public sealed class ClusterAccessProvider : IAccessCosts
    {
        // ---- knobs (calibration comments, not verified names) ----------------
        private const double UnreachableMinutes = 480.0; // e^(−θ·480) ≈ 0 for every θ in EconParams
        private const double DefaultSpeedMps = 13.9;     // ~50 km/h fallback free-flow
        private const double MaxCongestionMult = 8.0;    // clamp on LaneFlow-derived slowdowns
        private const double FreightFactor = 1.25;       // heavier vehicles / curb legs (AccessPurpose.Freight)
        private const double DriftThreshold = 0.07;      // >7% on any pair ⇒ Version bump
        private const double MissingEdgeDirtyShare = 0.10; // topology churn ⇒ self-MarkDirty

        private readonly int _target;
        private int _c;                        // built cluster count (≤ _target)
        private int _version;                  // IAccessCosts.Version — monotone
        private bool _dirty = true;            // needs a full RebuildClusters

        // Cluster geometry (plain arrays: no game types leak into state).
        private float[] _cx = Array.Empty<float>(), _cz = Array.Empty<float>();
        private double[] _intraMin = Array.Empty<double>();   // within-cluster minutes

        // ClusterOf lookup grid (every cell mapped to its nearest cluster).
        private float _minX, _minZ, _cellW, _cellH;
        private int _gx, _gz;
        private int[] _cellCluster = Array.Empty<int>();

        // Undirected cluster graph. Arc = unordered cluster pair; adjacency in
        // CSR form; per-edge records let live updates re-price arcs in place.
        private int[] _adjStart = Array.Empty<int>();
        private int[] _adjNbr = Array.Empty<int>();
        private int[] _adjArc = Array.Empty<int>();
        private double[] _arcLiveSec = Array.Empty<double>();
        private int _arcCount;

        private readonly List<Entity> _edgeEnt = new List<Entity>();
        private readonly List<double> _edgeFreeSec = new List<double>();
        private readonly List<double> _edgeLen = new List<double>();
        private readonly List<int> _edgeArc = new List<int>();
        private readonly Dictionary<Entity, int> _edgeIndex = new Dictionary<Entity, int>();
        private double[] _edgeMult = Array.Empty<double>();

        // Cost matrices, minutes. _published is the matrix at the last Version
        // bump — the drift comparison baseline.
        private double[] _cost = Array.Empty<double>();
        private double[] _published = Array.Empty<double>();

        public ClusterAccessProvider(int targetClusters) { _target = Math.Max(1, targetClusters); }

        // ---- IAccessCosts ----------------------------------------------------
        /// <summary>Actual built cluster count. 0 until the first
        /// RebuildClusters — construct EconomyEngine only AFTER that.</summary>
        public int ClusterCount => _c;
        public int Version => _version;

        public double Cost(int a, int b, AccessPurpose purpose)
        {
            if (_c == 0) return 20.0;                       // pre-rebuild guard
            double m = a == b ? _intraMin[a] : _cost[a * _c + b];
            return purpose == AccessPurpose.Freight ? m * FreightFactor : m;
        }

        // ---- public control surface -----------------------------------------
        /// <summary>Request a full topology rebuild at the bridge's next
        /// opportunity (roads added/removed; also self-set when >10% of the
        /// known edges vanished between live updates).</summary>
        public void MarkDirty() => _dirty = true;
        public bool Dirty => _dirty;

        public int ClusterOf(float x, float z)
        {
            if (_c == 0) return 0;
            int gx = (int)((x - _minX) / _cellW); if (gx < 0) gx = 0; if (gx >= _gx) gx = _gx - 1;
            int gz = (int)((z - _minZ) / _cellH); if (gz < 0) gz = 0; if (gz >= _gz) gz = _gz - 1;
            return _cellCluster[gz * _gx + gx];
        }

        public (float X, float Z) ClusterCentroid(int cluster) => (_cx[cluster], _cz[cluster]);

        /// <summary>Full rebuild: bucket Game.Net.Node positions, rebuild the
        /// cluster graph from Game.Net.Edge, recompute free-flow + live costs.
        /// Always bumps Version (topology changed by definition).</summary>
        public void RebuildClusters(EntityManager em)
        {
            // ---- 1. gather node positions (Game.Net.Node; field guess is
            //         isolated in Verify_NodePosition) ------------------------
            var nodeQ = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.Node>());
            using var nodeEnts = nodeQ.ToEntityArray(Allocator.Temp);
            int n = nodeEnts.Length;
            if (n == 0) { _c = 0; _dirty = false; return; }

            var nx = new float[n]; var nz = new float[n];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float3 p = Verify_NodePosition(em.GetComponentData<Game.Net.Node>(nodeEnts[i]));
                nx[i] = p.x; nz[i] = p.z;
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            float w = Math.Max(1f, maxX - minX), h = Math.Max(1f, maxZ - minZ);

            // ---- 2. grid bucketing: shrink dims until non-empty cells ≤ target
            double aspect = w / h, scale = Math.Sqrt(_target * 2.0);
            int gx = 0, gz = 0, nonEmpty;
            int[] cellCount = Array.Empty<int>();
            for (int iter = 0; iter < 24; iter++)
            {
                gx = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(scale * scale * aspect)));
                gz = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(scale * scale / aspect)));
                cellCount = new int[gx * gz];
                for (int i = 0; i < n; i++)
                {
                    int cx = (int)((nx[i] - minX) / w * gx); if (cx >= gx) cx = gx - 1;
                    int cz = (int)((nz[i] - minZ) / h * gz); if (cz >= gz) cz = gz - 1;
                    cellCount[cz * gx + cx]++;
                }
                nonEmpty = 0;
                foreach (int c0 in cellCount) if (c0 > 0) nonEmpty++;
                if (nonEmpty <= _target) break;
                scale *= 0.92;
            }
            _gx = gx; _gz = gz; _minX = minX; _minZ = minZ; _cellW = w / gx; _cellH = h / gz;

            // Non-empty cells → cluster ids; centroids from member nodes.
            var cellToCluster = new int[gx * gz];
            int cCount = 0;
            for (int i = 0; i < cellToCluster.Length; i++)
                cellToCluster[i] = cellCount[i] > 0 ? cCount++ : -1;
            _c = cCount;
            _cx = new float[_c]; _cz = new float[_c];
            var members = new int[_c];
            var nodeCluster = new int[n];
            for (int i = 0; i < n; i++)
            {
                int cx = (int)((nx[i] - minX) / w * gx); if (cx >= gx) cx = gx - 1;
                int cz = (int)((nz[i] - minZ) / h * gz); if (cz >= gz) cz = gz - 1;
                int cl = cellToCluster[cz * gx + cx];
                nodeCluster[i] = cl;
                _cx[cl] += nx[i]; _cz[cl] += nz[i]; members[cl]++;
            }
            for (int c1 = 0; c1 < _c; c1++) { _cx[c1] /= members[c1]; _cz[c1] /= members[c1]; }

            // Intra-cluster minutes from mean spread around the centroid.
            var spread = new double[_c];
            for (int i = 0; i < n; i++)
            {
                int cl = nodeCluster[i];
                double dx = nx[i] - _cx[cl], dz = nz[i] - _cz[cl];
                spread[cl] += Math.Sqrt(dx * dx + dz * dz);
            }
            _intraMin = new double[_c];
            for (int c2 = 0; c2 < _c; c2++)
                _intraMin[c2] = Math.Max(1.0, 2.0 * (spread[c2] / members[c2]) / DefaultSpeedMps / 60.0);

            // Every grid cell (incl. empty) → nearest cluster, so ClusterOf is
            // total over the map (buildings sit off the node lattice).
            _cellCluster = new int[gx * gz];
            for (int cz = 0; cz < gz; cz++)
                for (int cx = 0; cx < gx; cx++)
                {
                    int cell = cz * gx + cx;
                    if (cellToCluster[cell] >= 0) { _cellCluster[cell] = cellToCluster[cell]; continue; }
                    float px = minX + (cx + 0.5f) * _cellW, pz = minZ + (cz + 0.5f) * _cellH;
                    int best = 0; double bestD = double.MaxValue;
                    for (int c3 = 0; c3 < _c; c3++)
                    {
                        double dx = px - _cx[c3], dz = pz - _cz[c3], d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = c3; }
                    }
                    _cellCluster[cell] = best;
                }

            // Node entity → cluster (edges reference nodes by entity).
            var nodeClusterOf = new Dictionary<Entity, int>(n);
            for (int i = 0; i < n; i++) nodeClusterOf[nodeEnts[i]] = nodeCluster[i];

            // ---- 3. cluster graph from Game.Net.Edge + Game.Net.Curve --------
            // Edge { m_Start, m_End } verified in the routing repo; Curve is a
            // verified component (notes §4: LandValueSystem reads it), m_Length
            // per the shared contract. Free-flow speed guess is isolated in
            // Verify_EdgeFreeFlowSpeed.
            _edgeEnt.Clear(); _edgeFreeSec.Clear(); _edgeLen.Clear(); _edgeArc.Clear(); _edgeIndex.Clear();
            var arcId = new Dictionary<(int, int), int>();
            var adj = new List<(int nbr, int arc)>[_c];
            for (int c4 = 0; c4 < _c; c4++) adj[c4] = new List<(int, int)>();

            var edgeQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadOnly<Game.Net.Curve>());
            using (var edgeEnts = edgeQ.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < edgeEnts.Length; i++)
                {
                    var e = edgeEnts[i];
                    var edge = em.GetComponentData<Game.Net.Edge>(e);
                    if (!nodeClusterOf.TryGetValue(edge.m_Start, out int a)) continue;
                    if (!nodeClusterOf.TryGetValue(edge.m_End, out int b)) continue;
                    if (a == b) continue;                    // intra covered by _intraMin
                    double len = em.GetComponentData<Game.Net.Curve>(e).m_Length;
                    if (len <= 0.1) continue;
                    double freeSec = len / Verify_EdgeFreeFlowSpeed(em, e);

                    var key = a < b ? (a, b) : (b, a);
                    if (!arcId.TryGetValue(key, out int arc))
                    {
                        arc = arcId.Count;
                        arcId[key] = arc;
                        adj[a].Add((b, arc));
                        adj[b].Add((a, arc));
                    }
                    _edgeIndex[e] = _edgeEnt.Count;
                    _edgeEnt.Add(e); _edgeFreeSec.Add(freeSec); _edgeLen.Add(len); _edgeArc.Add(arc);
                }
            }
            _arcCount = arcId.Count;
            _arcLiveSec = new double[_arcCount];
            _edgeMult = new double[_edgeEnt.Count];
            for (int i = 0; i < _edgeMult.Length; i++) _edgeMult[i] = 1.0;

            // Flatten adjacency to CSR.
            _adjStart = new int[_c + 1];
            for (int c5 = 0; c5 < _c; c5++) _adjStart[c5 + 1] = _adjStart[c5] + adj[c5].Count;
            _adjNbr = new int[_adjStart[_c]]; _adjArc = new int[_adjStart[_c]];
            for (int c6 = 0; c6 < _c; c6++)
                for (int k = 0; k < adj[c6].Count; k++)
                { _adjNbr[_adjStart[c6] + k] = adj[c6][k].nbr; _adjArc[_adjStart[c6] + k] = adj[c6][k].arc; }

            _cost = new double[_c * _c];
            _published = Array.Empty<double>();              // force the bump below
            UpdateLiveCosts(em);                             // prices arcs + Dijkstra + Version++
            _dirty = false;
        }

        /// <summary>Cheap periodic refresh over FIXED topology: re-derive each
        /// edge's congestion multiplier from Game.Net.LaneFlow, re-price arcs,
        /// re-run Dijkstra, and bump Version only if any pair drifted >7% since
        /// the last published matrix. Call every engine tick or slower.</summary>
        public void UpdateLiveCosts(EntityManager em)
        {
            if (_c == 0) return;

            // ---- live speed per edge from LaneFlow (notes §8, verified fields):
            //      live m/s = Σ m_Distance / Σ m_Duration over the edge's lanes.
            int E = _edgeEnt.Count;
            var sumDist = new double[E]; var sumDur = new double[E];
            var laneQ = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.LaneFlow>());
            using (var lanes = laneQ.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    Entity owner = Verify_LaneOwnerEdge(em, lanes[i]);
                    if (owner == Entity.Null || !_edgeIndex.TryGetValue(owner, out int ei)) continue;
                    var lf = em.GetComponentData<Game.Net.LaneFlow>(lanes[i]);
                    sumDist[ei] += math.csum(lf.m_Distance);
                    sumDur[ei] += math.csum(lf.m_Duration);
                }
            }
            int missing = 0;
            for (int i = 0; i < E; i++)
            {
                if (!em.Exists(_edgeEnt[i])) { missing++; _edgeMult[i] = 1.0; continue; }
                if (sumDur[i] > 1e-3 && sumDist[i] > 1e-1)
                {
                    double liveSpeed = sumDist[i] / sumDur[i];
                    double liveSec = _edgeLen[i] / Math.Max(0.5, liveSpeed);
                    double mult = liveSec / _edgeFreeSec[i];
                    _edgeMult[i] = mult < 1.0 ? 1.0 : mult > MaxCongestionMult ? MaxCongestionMult : mult;
                }
                // else: no measured flow — keep the previous multiplier (EMA-like
                // persistence; LaneFlow windows can be empty on quiet edges).
            }
            if (E > 0 && missing > E * MissingEdgeDirtyShare) _dirty = true;   // topology churned

            // ---- arc pricing: min over parallel edges of freeSec × multiplier.
            for (int a = 0; a < _arcCount; a++) _arcLiveSec[a] = double.MaxValue;
            for (int i = 0; i < E; i++)
            {
                if (!em.Exists(_edgeEnt[i])) continue;
                double s = _edgeFreeSec[i] * _edgeMult[i];
                if (s < _arcLiveSec[_edgeArc[i]]) _arcLiveSec[_edgeArc[i]] = s;
            }

            DijkstraAllPairs();

            // ---- version bump on drift: exhaustive pair scan against the last
            //      published matrix ("any cluster pair" — superset of sampling).
            bool bump = _published.Length != _cost.Length;
            if (!bump)
                for (int i = 0; i < _cost.Length; i++)
                {
                    double p = _published[i], q = _cost[i];
                    if (Math.Abs(q - p) > DriftThreshold * Math.Max(1e-6, p)) { bump = true; break; }
                }
            if (bump)
            {
                if (_published.Length != _cost.Length) _published = new double[_cost.Length];
                Array.Copy(_cost, _published, _cost.Length);
                _version++;
            }
        }

        /// <summary>O(C²) Dijkstra per source — no heap needed at C ≈ 200.
        /// Access/egress legs are folded in as half the intra cost each side.</summary>
        private void DijkstraAllPairs()
        {
            var dist = new double[_c];
            var done = new bool[_c];
            for (int src = 0; src < _c; src++)
            {
                for (int i = 0; i < _c; i++) { dist[i] = double.MaxValue; done[i] = false; }
                dist[src] = 0;
                for (int round = 0; round < _c; round++)
                {
                    int u = -1; double best = double.MaxValue;
                    for (int i = 0; i < _c; i++)
                        if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                    if (u < 0) break;
                    done[u] = true;
                    for (int k = _adjStart[u]; k < _adjStart[u + 1]; k++)
                    {
                        double ws = _arcLiveSec[_adjArc[k]];
                        if (ws >= double.MaxValue) continue;
                        double nd = dist[u] + ws;
                        int v = _adjNbr[k];
                        if (nd < dist[v]) dist[v] = nd;
                    }
                }
                for (int dst = 0; dst < _c; dst++)
                    _cost[src * _c + dst] = src == dst ? _intraMin[src]
                        : dist[dst] >= double.MaxValue ? UnreachableMinutes
                        : dist[dst] / 60.0 + 0.5 * (_intraMin[src] + _intraMin[dst]);
            }
        }

        // ---- Verify_ isolation cells (research-notes contract: every name NOT
        //      in the verified dump lives in one of these, so a wrong guess is
        //      one localized compile error on the game machine) ---------------

        /// <summary>// VERIFY-INGAME: the dump does not list Game.Net.Node's
        /// fields — check the decompile that Node carries "float3 m_Position"
        /// (world position of the net node).</summary>
        private static float3 Verify_NodePosition(Game.Net.Node node) => node.m_Position;

        /// <summary>// VERIFY-INGAME: free-flow speed member is NOT in the
        /// component dump (notes §8 warns exactly this class of guess). Guess:
        /// edge PrefabRef → Game.Prefabs.RoadData.m_SpeedLimit. Check (a) the
        /// field exists on road net prefabs, (b) its UNITS — if km/h, divide by
        /// 3.6 here. Non-road edges (paths, tracks) fall back to the default.</summary>
        private static double Verify_EdgeFreeFlowSpeed(EntityManager em, Entity edge)
        {
            if (em.HasComponent<Game.Prefabs.PrefabRef>(edge))
            {
                Entity prefab = em.GetComponentData<Game.Prefabs.PrefabRef>(edge).m_Prefab;
                if (prefab != Entity.Null && em.HasComponent<Game.Prefabs.RoadData>(prefab))
                {
                    float s = em.GetComponentData<Game.Prefabs.RoadData>(prefab).m_SpeedLimit;
                    if (s > 0.5f) return s;
                }
            }
            return DefaultSpeedMps;
        }

        /// <summary>// VERIFY-INGAME: LaneFlow rides LANE entities, not edges.
        /// Guess: Game.Common.Owner { Entity m_Owner } on the lane points at
        /// its owning edge. Check the decompile (SubLane buffer walk from the
        /// edge is the alternative if Owner is absent on lanes).</summary>
        private static Entity Verify_LaneOwnerEdge(EntityManager em, Entity lane)
            => em.HasComponent<Game.Common.Owner>(lane)
                ? em.GetComponentData<Game.Common.Owner>(lane).m_Owner
                : Entity.Null;
    }
#endif
}
