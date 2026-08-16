using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>The housing market as ONE assignment problem, solved as an
    /// ascending auction.
    ///
    /// WHY THIS EXISTS. The model used to run two mechanisms that did not talk
    /// to each other. Prices came from inverting a demand curve — count who
    /// would bid at each submarket, read the curve at supply×(1+band). Homes
    /// came from AllocationSystem.FindHome, which walked the unhoused in
    /// HOUSEHOLD-ID ORDER and gave each the best unit still vacant, ranking
    /// clusters by SEGMENT-level preferences and a fresh RNG draw. So the
    /// household with the highest bid could lose the unit to whoever had a
    /// lower id, and nothing in the price path ever found out. Nobody was ever
    /// outbid, because there was no contest — just an auction that set numbers
    /// and a queue that handed out keys.
    ///
    /// WHAT REPLACES IT. Every household holds simultaneous bids on every
    /// submarket it would consider, INCLUDING the one it already lives in, and
    /// prices rise wherever more households want a place than it has room for,
    /// until each household is left wanting exactly one. That is the
    /// Shapley–Shubik assignment game. Because money transfers, a stable
    /// matching here IS a competitive equilibrium and IS the core — Gale–Shapley
    /// is the wrong analogue, since deferred acceptance solves the
    /// NON-transferable case where you cannot outbid anyone.
    ///
    /// WHY IT TERMINATES. Each household wants exactly one home, which makes
    /// preferences satisfy GROSS SUBSTITUTES (Kelso–Crawford, Gul–Stacchetti):
    /// raising one submarket's price can only raise demand for the others. That
    /// guarantees a Walrasian equilibrium exists and that an ascending auction
    /// reaches it. This is emphatically not a general combinatorial auction —
    /// those are hard; unit demand is the special case that is not.
    ///
    /// THE PRICE IT POSTS is the highest bid that did NOT get a slot — the first
    /// excluded challenger. That is the minimum competitive equilibrium price
    /// (Demange–Gale–Sotomayor), the buyer-optimal point of the equilibrium
    /// lattice, and it coincides with the VCG payment. The old ClearingBand
    /// heuristic was reaching for exactly this and getting it roughly right; the
    /// difference is that a bid here is COMPETITION-ADJUSTED — what a household
    /// will pay at a place net of the surplus it gives up by not taking its next
    /// best — rather than its raw willingness to pay.
    ///
    /// EXCESS SUPPLY STOPS BEING A SPECIAL CASE. A submarket nobody fills simply
    /// never rises above the owner's reserve. The two-regime CLEARED/EXCESS
    /// split, the flat-tail rule and its dust guard all existed because the old
    /// path inverted a curve by hand and had to decide what to do when the curve
    /// ran out. An auction just stops bidding.</summary>
    public sealed class HousingAuction
    {
        public const int Levels = 5;

        public int C;
        /// <summary>Submarket = (density kind, cluster, level). Index is
        /// ((k*C)+c)*Levels + (level−1).</summary>
        public int S => 2 * C * Levels;

        // ---- per submarket -------------------------------------------------
        public int[] Capacity = Array.Empty<int>();     // lettable units standing
        public double[] Reserve = Array.Empty<double>();// owner's floor: S per unit
        /// <summary>Posted price: the first EXCLUDED challenger's bid, floored at
        /// the reserve. What a tenant pays and what the assessment reads.</summary>
        public double[] Price = Array.Empty<double>();
        /// <summary>Lowest bid that DID get a slot. Price ≤ Admitted always, and
        /// the gap is the marginal tenant's surplus.</summary>
        public double[] Admitted = Array.Empty<double>();
        public int[] Filled = Array.Empty<int>();
        /// <summary>How many households wanted this submarket and were turned
        /// away at the final prices — the queue behind the door.</summary>
        public int[] Excluded = Array.Empty<int>();

        // ---- per household -------------------------------------------------
        /// <summary>household id → submarket index, −1 = took the outside
        /// option (no home in this city at these prices).</summary>
        public int[] Assignment = Array.Empty<int>();
        /// <summary>What the assigned household bid — its competition-adjusted
        /// willingness to pay at the place it won.</summary>
        public double[] WinningBid = Array.Empty<double>();

        public enum Outcome : byte { Housed = 0, Declined = 1, Outbid = 2 }
        /// <summary>WHY a household ended up with no home, which the assignment
        /// alone cannot say. Two very different things were collapsed into −1:
        ///   Declined — nothing in this city beat ITS OWN reservation. This
        ///              household should leave; the city simply is not worth it
        ///              to them, and that is a decision they made.
        ///   Outbid   — it wanted a place and could not win one. This household
        ///              stays and keeps looking; it is queue, not exit.
        /// Treating them alike is what forces migration to be modelled from the
        /// top: with the difference recorded, departure is just the first case
        /// and needs no citywide out-migration elasticity at all.</summary>
        public Outcome[] Why = Array.Empty<Outcome>();

        // ---- telemetry -----------------------------------------------------
        public int Rounds, Bids, Evictions, Unassigned;
        /// <summary>Column-generation rounds actually used, and whether the loop
        /// ended because nothing was left to repair (true) or because it ran out
        /// of rounds (false).</summary>
        public int RepairRounds;
        public bool RepairClean;
        public bool Converged;
        /// <summary>Households the repair scan found a strictly better door for
        /// and could not act on: because the door was already on their list and
        /// they had simply lost there, or because their list was full. Both are
        /// counted over the last scan only.</summary>
        public int BlockedListed, BlockedFull;

        /// <summary>Where the solve's time actually goes, accumulated across
        /// every call. Added because a previous optimization round predicted a
        /// 4.4x speedup from narrowing the repair scan and measured 1.15x: the
        /// term that was narrowed was not the dominant one. Guessing at a
        /// profile is how that happens twice.
        ///
        /// CutVacancies is timed SEPARATELY from the scan, and that split is
        /// not cosmetic. The two ran under one `repairScan` number, and every
        /// design written against that number inherited an assumed 80/20 split
        /// between them rather than a measured one. Split and then measured, on
        /// the reference run (auctionprobe --ticks 300, seed 20260806, 8000
        /// seed households): scan 56.0 s, cut 13.8 s — an 80/20 that happened
        /// to be the assumption, and was worth an hour to stop assuming. A cut
        /// is a full pass over every household for every partially-filled door,
        /// which is a cross product in its own right and deserves its own
        /// line.</summary>
        public static double MsSubmarkets, MsHouseholds, MsAuction, MsRepairScan,
                             MsShadow;
        public static long CallsValueSlot, CallsValueAt, CallsSoftCap;

        /// <summary>How many times Solve() has run in this process. Exists for
        /// one reason: the suite could not say which of its own checks touch
        /// this mechanism at all, and the answer turned out to be two of
        /// twenty-two (measured at c0c584d: only the auction-equilibrium and
        /// ledger-conservation fixtures build an auction-enabled city). A prose
        /// claim about that rots the moment a fixture is added or a flag
        /// flipped; a counter the runner prints per fixture cannot. Read by
        /// TestRunner's coverage table — never by the model, and never
        /// asserted on as a magnitude (a check on "how many solves" would fail
        /// on any legitimate change to refresh cadence).</summary>
        public static long SolveCalls;

        /// <summary>DIFFERENTIAL ORACLE. When set, every repair scan is run
        /// TWICE: once by the production path and once by RefScanBest, which is
        /// the pre-optimization scan copied verbatim — a Math.Pow pair per cell,
        /// an EntryPrice call per cell, the List<int> key walk. The two are
        /// compared per household on the chosen column, on the gain that chose
        /// it (bit-exactly, `!=` on doubles), and on the resulting dirty SET.
        ///
        /// It exists because the only defensible claim about a change to this
        /// scan is a checked one. The optimization shipped here is bit-exact by
        /// construction, so the oracle should report zero on every counter over
        /// a whole simulation, and any non-zero is a bug rather than an
        /// acceptable divergence. It is also the harness the deferred NARROWING
        /// needs: a narrowing changes the answer by proof rather than by
        /// construction, and this is where that proof gets tested.</summary>
        public static bool ScanOracle;
        public static long OracleScans, OracleHhScans, OracleKcMismatch,
                           OracleGainMismatch, OracleSetMismatch;

        /// <summary>quality(ℓ)/quality(1), the level uplift, for every level.
        ///
        /// WHAT THIS IS WORTH, measured before it was written because it is the
        /// cheapest claim in this file to falsify. `p.Quality(l)/p.Quality(1)`
        /// is TWO Math.Pow calls and a divide, it sits in the innermost cell of
        /// the repair scan, and it is evaluated UNCONDITIONALLY — before the
        /// pre-exp bound test that the scan's own comment credits with the
        /// saving. Over a 300-tick reference run that is ~866M evaluations of a
        /// function of one small integer. Hoisting it into this six-entry table,
        /// on its own and changing no decision anywhere: scan 56034 → 19665 ms
        /// (2.85×), repair phase 69824 → 27645 ms (2.53×), wall 87.1 → 43.0 s
        /// (2.03×), every output bit identical. Ten lines, and more than the
        /// staged narrowing this file's history predicted at 2.0–2.2× and
        /// measured at 1.15×.
        ///
        /// It is bit-exact and not merely close. Math.Pow is a deterministic
        /// pure function of two doubles, p.Quality(1) is Math.Pow(1, α) = 1.0
        /// exactly, and dividing by 1.0 is the identity — so the cached double
        /// IS the double the per-cell expression produced.
        ///
        /// Keyed on the alpha VALUE rather than on a dirty flag, because
        /// LevelBidAlpha is a public mutable field on a shared EconParams and
        /// the harness sweeps it. Deliberately `==` and not `.Equals`:
        /// double.NaN.Equals(double.NaN) is true in .NET, which would serve an
        /// unbuilt NaN table forever; `==` is false against NaN and rebuilds.</summary>
        private readonly double[] _qf = new double[Levels + 1];
        private double _qfAlpha = double.NaN;

        /// <summary>The repair scan's doors, flattened, rebuilt once per SCAN.
        /// Parallel to _liveLevels: for door t, the submarket index, its level
        /// uplift, and the entry price a challenger faces there. _dKc/_dHigh/
        /// _dCluster/_dStart are the per-KEY half, parallel to _liveKc.
        ///
        /// PER SCAN, NOT PER SOLVE, and that is a correctness condition rather
        /// than a detail. CutVacancies writes Price[s] and runs immediately
        /// BEFORE the scan, precisely so that a household sees the post-cut
        /// price (see the comment at the call site — the other order left 885
        /// households envious of doors nobody had been told about). A table
        /// built per solve would advertise the stale, higher price and lose
        /// exactly the household the cut was made for.
        ///
        /// A snapshot taken at the top of the scan is exact for the whole scan.
        /// EntryPrice reads Capacity, _slots.Count, Admitted and Price; every
        /// write to all four is in BuildSubmarkets, RunAuction or SetPrices,
        /// and none of those is reachable from
        /// AddEnviedColumns, which writes only the shortlist and the
        /// frozen slot caches. The full/non-full branch is CAPTURED rather than
        /// assumed: the table stores whichever of Admitted and Price is live at
        /// snapshot time, so a door filling up (a price RISE for a challenger)
        /// or emptying (a FALL) is recorded as the number it actually is.</summary>
        private int[] _dSub = Array.Empty<int>();
        private double[] _dQf = Array.Empty<double>();
        private double[] _dEntry = Array.Empty<double>();
        private int[] _dKc = Array.Empty<int>();
        private bool[] _dHigh = Array.Empty<bool>();
        private int[] _dCluster = Array.Empty<int>();
        private int[] _dStart = Array.Empty<int>();

        // ---- working state -------------------------------------------------
        private int[] _shortStart = Array.Empty<int>();  // per household, into _shortItems
        private int[] _shortCount = Array.Empty<int>();
        private int[] _shortItems = Array.Empty<int>();  // (k*C+c) keys, price-free ranked
        /// <summary>Per (household, shortlist slot): the level-INDEPENDENT part
        /// of the valuation — own bid base × the segment's location premium, and
        /// the household's own permanent taste for that place. The inner auction
        /// loop re-evaluated both for every bid at every level, paying a hash and
        /// two logs for a Gumbel that never changes. Cached here, the loop is
        /// arithmetic. Computed with the same operations in the same order as
        /// ValueAt, so the two agree bit for bit and the equilibrium checks are
        /// still checking what the auction actually did.</summary>
        private double[] _slotPrem = Array.Empty<double>();
        private double[] _slotTaste = Array.Empty<double>();
        private double[] _base0 = Array.Empty<double>(); // per household: bid base, Low
        private double[] _base1 = Array.Empty<double>(); // per household: bid base, High
        private double[] _homeBonus = Array.Empty<double>();
        private int[] _homeKC = Array.Empty<int>();      // (k*C+c) of current home, −1
        private int[] _seg = Array.Empty<int>();
        private int _stride;
        // Previous refresh's shortlists, for the warm start.
        private int[] _prevItems = Array.Empty<int>();
        private int[] _prevCount = Array.Empty<int>();
        private int _prevStride;
        private double[] _cap = Array.Empty<double>();   // ability to pay
        /// <summary>Each household's OWN outside option in money per tick — what
        /// it could get by living elsewhere in the region, drawn at birth as a
        /// share of its own budget. This used to be one shared constant, so the
        /// entire city's willingness to walk away moved as a block.</summary>
        private double[] _outside = Array.Empty<double>();
        private double[][] _premium = Array.Empty<double[]>(); // [segment][cluster]
        private readonly List<int> _queue = new List<int>();
        /// <summary>Per submarket, the bids currently holding its slots. Kept as
        /// a plain list because capacity is small (a submarket is one density at
        /// one cluster at one level); the min is found by scan.</summary>
        private List<(double bid, int hh)>[] _slots = Array.Empty<List<(double, int)>>();

        /// <summary>The (density, cluster) keys that hold ANY lettable stock,
        /// and for each the levels that do. The submarket space is
        /// 2 × clusters × 5 levels — about 1960 on the reference city — and
        /// roughly three quarters of it is empty at any moment. The repair
        /// scan is the one place that walks all of it per household per round,
        /// so it walked ~1500 dead entries every time to `continue` on them.</summary>
        /// <summary>Per (household, live key): the taste term the repair scan
        /// needs. It is a pure function of the pair and never changes within a
        /// solve, but the scan recomputed it — a hash and two logs — once per
        /// key per household PER ROUND, up to twelve times over. Held as float
        /// because it is a taste shock, not money: the band it is compared
        /// against is half a percent of value, which is four orders of magnitude
        /// above float precision here.</summary>
        private float[] _scanTaste = Array.Empty<float>();
        private int _scanStride;
        private readonly List<int> _liveKc = new List<int>();
        private readonly List<int> _liveKcStart = new List<int>();
        private readonly List<int> _liveLevels = new List<int>();
        /// <summary>The differential oracle's own copy of the scan's answer —
        /// which households the untouched reference scan would have marked for
        /// re-decision, in the order it would have marked them.</summary>
        private readonly List<int> _refDirty = new List<int>();

        public static int Key(int k, int c, int C) => k * C + c;
        public static int Sub(int kc, int level, int C) => kc * Levels + (level - 1);
        public static int KcOf(int sub) => sub / Levels;
        public static int LevelOf(int sub) => (sub % Levels) + 1;

        /// <summary>Solve the market. Reads the world and the access field;
        /// writes prices and an assignment. Does not move anybody — that is the
        /// caller's job, so the solve stays a pure function of its inputs and
        /// can be run twice on the same state in a test.</summary>
        public void Solve(WorldState w, AccessState acc, EconParams p)
        {
            SolveCalls++;
            C = acc.C;
            int nSub = S;
            EnsureArrays(w, nSub);
            EnsureLevelFactors(p);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            BuildSubmarkets(w, p, nSub);
            MsSubmarkets += sw.Elapsed.TotalMilliseconds; sw.Restart();
            BuildHouseholds(w, acc, p);
            MsHouseholds += sw.Elapsed.TotalMilliseconds; sw.Restart();
            BuildScanTaste(w, p);
            MsHouseholds += sw.Elapsed.TotalMilliseconds; sw.Restart();
            RunAuction(w, p, nSub);
            MsAuction += sw.Elapsed.TotalMilliseconds; sw.Restart();
            // COLUMN GENERATION. A shortlist ranked price-free is stable and
            // cheap, and on its own it is not enough: a household whose top
            // places are all dear never SEES the cheap submarket where it would
            // be much better off, so it settles for less and the outcome is not
            // an equilibrium at all. Swept over every submarket with stock, 865
            // of 1817 housed households strictly preferred somewhere they had
            // never been shown, the worst by 43% of what the place was worth to
            // them — and a no-envy check that only looks inside the shortlist
            // cannot see any of it, which is precisely why the acceptance check
            // sweeps everything.
            //
            // So: solve, then ask each household what it wishes it had been
            // shown, put that on its list, and solve again. This is column
            // generation, and it converges fast because each round only has to
            // repair households that are actually envious. Rounds are capped;
            // if the cap binds, Converged says so rather than the result quietly
            // pretending to be an equilibrium.
            //
            // The loop must END on a CLEAN SCAN, not on a solve. Testing for
            // violations before each re-solve and then stopping on a round cap
            // leaves the last solve's own envy unmeasured — measured 2–6
            // households per seed still envious by up to 2% of value, all of it
            // created by a final round nobody looked at. RepairClean records
            // whether the loop actually ran out of violations or just ran out of
            // rounds, and Converged carries it, so a solve that gave up says so
            // instead of presenting itself as an equilibrium.
            RepairRounds = 0; RepairClean = false;
            for (int round = 0; round < Math.Max(0, p.AuctionRepairRounds); round++)
            {
                // Every round is a FULL RE-CLEAR from the reserve. The
                // resumed-round world this replaces kept stale prices standing
                // between rounds and needed a between-round repricing pass
                // (CutVacancies) to walk them down; three such mechanisms were
                // built and measured, and none converged — undershooting
                // ping-pongs door pairs at ε granularity, overshooting mints
                // bargains out of double-claimed demand, and the feasible
                // middle is a limit cycle that a round cap of 200 did not
                // close (KNOWN-RED.md, "measured dead ends"). A monotone
                // ascent from below is the case the ε-auction's termination
                // theorem actually covers. The cost, measured over a 300-tick
                // auctionprobe rather than predicted: the auction phase rises
                // 1.3s -> 7.3s (every round re-bids everybody), the deleted
                // cut pass gives back 3.5s, and the net solve is ~10% dearer
                // (phase total 26.4s -> 29.2s at the shipped cap of 16) —
                // not the "few percent" first claimed for this change, and
                // recorded here because this file has believed its own cost
                // stories before. What the 8% buys is convergence (canary
                // 33/39 -> 38/39, the one residual a knife-edge ε case, not a
                // mechanism defect) and a STRUCTURAL invariant: in a pure
                // build a non-full door's posted price never leaves its
                // reserve, so a stranded-high vacancy cannot exist at the end
                // of a round.
                int added = AddEnviedColumns(w, p, nSub);
                MsRepairScan += sw.Elapsed.TotalMilliseconds; sw.Restart();
                if (added == 0) { RepairClean = true; break; }
                RepairRounds++;
                RunAuction(w, p, nSub);
                MsAuction += sw.Elapsed.TotalMilliseconds; sw.Restart();
            }
            if (p.AuctionRepairRounds <= 0) RepairClean = true;   // repair disabled on purpose
            sw.Restart();
            BuildShadow(w, p, nSub);
            MsShadow += sw.Elapsed.TotalMilliseconds;
            Converged = Converged && RepairClean;
            // Keep this refresh's lists (repair columns included) for the next.
            if (_prevItems.Length != _shortItems.Length) _prevItems = new int[_shortItems.Length];
            Array.Copy(_shortItems, _prevItems, _shortItems.Length);
            if (_prevCount.Length != _shortCount.Length) _prevCount = new int[_shortCount.Length];
            Array.Copy(_shortCount, _prevCount, _shortCount.Length);
            _prevStride = _stride;
        }

        private void EnsureArrays(WorldState w, int nSub)
        {
            if (Capacity.Length != nSub)
            {
                Capacity = new int[nSub]; Reserve = new double[nSub];
                Price = new double[nSub]; Admitted = new double[nSub];
                Filled = new int[nSub]; Excluded = new int[nSub];
                _slots = new List<(double, int)>[nSub];
                for (int s = 0; s < nSub; s++) _slots[s] = new List<(double, int)>();
            }
            int nh = w.Households.Count;
            if (Assignment.Length != nh)
            {
                Assignment = new int[nh]; WinningBid = new double[nh]; Why = new Outcome[nh];
                _shortStart = new int[nh]; _shortCount = new int[nh];
                _base0 = new double[nh]; _base1 = new double[nh]; _cap = new double[nh];
                _outside = new double[nh];
                _homeBonus = new double[nh]; _homeKC = new int[nh]; _seg = new int[nh];
            }
        }

        private void BuildSubmarkets(WorldState w, EconParams p, int nSub)
        {
            Array.Clear(Capacity, 0, nSub);
            Array.Clear(Filled, 0, nSub);
            Array.Clear(Excluded, 0, nSub);
            var condSum = new double[nSub];
            var condCnt = new int[nSub];
            for (int s = 0; s < nSub; s++) _slots[s].Clear();

            foreach (var pl in w.Parcels)
            {
                if ((uint)pl.Cluster >= (uint)C) continue;
                if (pl.State == ParcelState.Built && pl.IsResidential)
                {
                    // Warehoused stock is off the market by the owner's choice:
                    // it holds no capacity, but it must still be PRICED, because
                    // the scrape decision compares its residual to the
                    // alternative. Zero capacity with live bids is exactly how
                    // an auction says "there is demand here and no supply".
                    int k = pl.Use == ZoneKind.ResidentialHigh ? 1 : 0;
                    int lvl = Math.Min(Levels, Math.Max(1, pl.Level));
                    int sub = Sub(Key(k, pl.Cluster, C), lvl, C);
                    if (!pl.Warehousing) Capacity[sub] += pl.Units;
                    condSum[sub] += pl.Condition * pl.Units; condCnt[sub] += pl.Units;
                }
            }
            _liveKc.Clear(); _liveKcStart.Clear(); _liveLevels.Clear();
            for (int kc = 0; kc < 2 * C; kc++)
            {
                int start = _liveLevels.Count;
                for (int l = 1; l <= Levels; l++)
                    if (Capacity[Sub(kc, l, C)] > 0) _liveLevels.Add(l);
                if (_liveLevels.Count > start) { _liveKc.Add(kc); _liveKcStart.Add(start); }
            }
            _liveKcStart.Add(_liveLevels.Count);          // sentinel

            for (int s = 0; s < nSub; s++)
            {
                double cond = condCnt[s] > 0 ? condSum[s] / condCnt[s] : 1.0;
                // The owner's floor. Below S the structure is not worth
                // operating and the unit is warehoused rather than let, so no
                // price in this market may fall under it.
                Reserve[s] = LandAccounting.SPerUnit(LevelOf(s), cond, p);
                Price[s] = Reserve[s];
                Admitted[s] = double.PositiveInfinity;
            }
        }

        private void BuildHouseholds(WorldState w, AccessState acc, EconParams p)
        {
            int nSeg = Segment.Count;
            if (_premium.Length != nSeg)
            {
                _premium = new double[nSeg][];
                for (int s = 0; s < nSeg; s++) _premium[s] = new double[C];
            }
            for (int s = 0; s < nSeg; s++)
            {
                if (_premium[s].Length != C) _premium[s] = new double[C];
                for (int c = 0; c < C; c++)
                {
                    double rel = acc.AccessValue[s][c] / acc.MeanAccess;
                    _premium[s][c] = MathUtil.Clamp(
                        Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0)
                        * p.BidAccessScale;
                }
            }

            // Stride carries slack for the repair rounds, so a column added
            // later APPENDS rather than displacing something — and in
            // particular never displaces the household's own assignment, which
            // would make the next round thrash instead of converge.
            int K = Math.Max(2, p.AuctionShortlist);
            _stride = K + Math.Max(0, p.AuctionRepairRounds);
            int nh = w.Households.Count;
            // WARM START. The columns the repair rounds discovered last refresh
            // are still the ones worth showing this refresh — the city moves a
            // little between solves, not a lot — so the previous shortlists are
            // carried in and seeded FIRST, and the price-free ranking then tops
            // each list up. Cold-starting them threw that work away every time
            // and made the repair loop rediscover the same columns from scratch,
            // which is the whole cost of the solve: each round is a full
            // re-clearing.
            if (_shortItems.Length != nh * _stride)
            {
                var grown = new int[nh * _stride];
                int copyStride = _prevStride > 0 ? Math.Min(_prevStride, _stride) : 0;
                if (copyStride > 0 && _prevItems.Length > 0)
                    for (int i = 0; i < nh && i * _prevStride < _prevItems.Length; i++)
                        Array.Copy(_prevItems, i * _prevStride, grown, i * _stride, copyStride);
                _shortItems = grown;
            }
            if (_slotPrem.Length != nh * _stride)
            { _slotPrem = new double[nh * _stride]; _slotTaste = new double[nh * _stride]; }
            // Household ids are stable and append-only, so a previous list is
            // valid for any id the previous solve saw. Keying this on exact
            // array-length equality meant it was false on every refresh of a
            // growing city — the warm start was dead code wherever it mattered.
            bool warm = p.AuctionWarmStart && _prevStride == _stride && _prevCount.Length > 0;

            // Scratch for the shortlist selection: a K-sized insertion list.
            // Stack for every real configuration; heap when K exceeds it, which
            // only the exhaustive-shortlist oracle check does — it sets K past
            // the number of live keys so the "shortlist" is every door worth
            // more than leaving, and the column-generation solve can be
            // compared against a solve that was shown everything.
            Span<double> bestV = K <= 64 ? stackalloc double[64] : new double[K];
            Span<int> bestKC = K <= 64 ? stackalloc int[64] : new int[K];

            for (int i = 0; i < nh; i++)
            {
                var h = w.Households[i];
                _shortStart[i] = i * _stride;
                _shortCount[i] = 0;
                Assignment[i] = -1; WinningBid[i] = 0;
                if (h.ExitedTick >= 0) { _homeKC[i] = -1; continue; }

                var seg = Segment.All[h.Segment];
                _seg[i] = h.Segment;
                double income = Math.Max(1e-6, AccessState.HouseholdIncomeEstimate(h, seg, p));
                double budget = h.RentShare * income;
                _base0[i] = budget * h.DensityAppeal(ZoneKind.ResidentialLow);
                _base1[i] = budget * h.DensityAppeal(ZoneKind.ResidentialHigh);
                // The budget constraint. A bidder cannot bid what it cannot pay,
                // and this is the one place the model has ever had to say so out
                // loud: under the old curve the marginal bidder was always poor,
                // so the realized price never approached the top of the
                // multiplier stack and nobody noticed that
                // base × premium × BidAccessScale × quality reaches ~2.3× a
                // household's own declared housing budget. An auction charges
                // what the winner bids, so it surfaced immediately — p90 across
                // live submarkets came out at 41 per tick against a median
                // household income of 10, rents at 3× income, every winner
                // instantly insolvent. This is the standard bid-rent ceiling:
                // whatever a place is worth to you, you bid at most what your
                // income can carry.
                _cap[i] = p.MaxRentOfIncome * income;
                _outside[i] = h.Reservation(budget);
                _homeKC[i] = h.HomeParcel >= 0 && (uint)w.Parcels[h.HomeParcel].Cluster < (uint)C
                    ? Key(w.Parcels[h.HomeParcel].Use == ZoneKind.ResidentialHigh ? 1 : 0,
                          w.Parcels[h.HomeParcel].Cluster, C)
                    : -1;
                // Moving cost as a per-tick flow, so it is commensurate with a
                // rent: the lump this household would pay to move, spread over
                // how long it expects to stay. Written as a lump against a
                // per-tick budget it came out two orders of magnitude too big.
                _homeBonus[i] = h.MovingCostDraw / Math.Max(1, p.MoveAmortTicks);

                // Seed ONLY the columns the repair rounds discovered last time
                // — the tail of the previous list, past the K the price-free
                // ranking produces — and never more than the slack. Seeding the
                // whole previous list instead let the lists ossify: they filled
                // to the stride with historical entries, the price-free top-K
                // could no longer get in, and households went on considering
                // places the city had moved past. The market flattened (price
                // p10/p90 spread 3.6× → 1.5×) and the solve got slower, not
                // faster, because every bid now scanned a longer list.
                int maxSeed = Math.Max(0, _stride - K);
                int seeded = 0;
                if (warm && i < _prevCount.Length)
                {
                    int prevN = Math.Min(_prevCount[i], _prevStride);
                    for (int q = K; q < prevN && seeded < maxSeed; q++)
                    {
                        int kc = _prevItems[i * _prevStride + q];
                        if ((uint)kc >= (uint)(2 * C)) continue;
                        bool dup = false;
                        for (int r = 0; r < seeded; r++) if (_shortItems[_shortStart[i] + r] == kc) { dup = true; break; }
                        if (!dup) _shortItems[_shortStart[i] + seeded++] = kc;
                    }
                }

                // ---- shortlist: the K best places IGNORING price ------------
                // Price is deliberately not in this ranking. Shortlisting on the
                // current price would drop a submarket that is about to get
                // cheaper — the auction could then never discover it. The
                // price-free part (access, own density taste, own idiosyncratic
                // taste for the place) moves slowly, so the shortlist is stable
                // and the price loop reorders WITHIN it.
                int cnt = 0; double worst = double.PositiveInfinity; int worstAt = 0;
                for (int k = 0; k < 2; k++)
                {
                    double bse = k == 1 ? _base1[i] : _base0[i];
                    if (bse <= 0) continue;
                    for (int c = 0; c < C; c++)
                    {
                        int kc = Key(k, c, C);
                        double v = SoftCap(bse * _premium[h.Segment][c] + Taste(h.Id, kc, bse, p), _cap[i])
                                   + (kc == _homeKC[i] ? _homeBonus[i] : 0);
                        // Anything worth less than leaving the city is not worth
                        // tracking — the caller's own suggestion, and it is what
                        // bounds K honestly instead of by a magic number. It
                        // also gives emigration a real trigger: a household with
                        // an EMPTY shortlist has nowhere in this city it would
                        // rather be than gone.
                        if (v <= _outside[i]) continue;
                        if (cnt < K)
                        {
                            bestV[cnt] = v; bestKC[cnt] = kc; cnt++;
                            if (cnt == K) { worst = bestV[0]; worstAt = 0;
                                for (int q = 1; q < K; q++) if (bestV[q] < worst) { worst = bestV[q]; worstAt = q; } }
                        }
                        else if (v > worst)
                        {
                            bestV[worstAt] = v; bestKC[worstAt] = kc;
                            worst = bestV[0]; worstAt = 0;
                            for (int q = 1; q < K; q++) if (bestV[q] < worst) { worst = bestV[q]; worstAt = q; }
                        }
                    }
                }
                // The place it already lives is always on the list, whatever it
                // ranks: a sitting tenant is a bidder for its own home and has
                // to be able to renew.

                // Top up around the seeded columns, skipping anything already
                // carried over, until the list is full.
                int outp = seeded;
                for (int q = 0; q < cnt && outp < _stride; q++)
                {
                    bool dup = false;
                    for (int r = 0; r < outp; r++) if (_shortItems[_shortStart[i] + r] == bestKC[q]) { dup = true; break; }
                    if (!dup) _shortItems[_shortStart[i] + outp++] = bestKC[q];
                }
                // The place it already lives is always on the list, whatever it
                // ranks: a sitting tenant is a bidder for its own home and has
                // to be able to renew.
                if (_homeKC[i] >= 0)
                {
                    bool have = false;
                    for (int r = 0; r < outp; r++) if (_shortItems[_shortStart[i] + r] == _homeKC[i]) { have = true; break; }
                    if (!have)
                    {
                        if (outp < _stride) _shortItems[_shortStart[i] + outp++] = _homeKC[i];
                        else _shortItems[_shortStart[i] + _stride - 1] = _homeKC[i];
                    }
                }
                _shortCount[i] = outp;

                // Freeze the level-independent half of every listed valuation.
                for (int q = 0; q < outp; q++) FreezeSlot(i, q, p);
            }
        }

        /// <summary>Freeze the level-independent half of one shortlist slot.
        /// EVERY path that writes _shortItems must call this, including the
        /// repair rounds: they append columns and then re-run the auction, and
        /// when they did not refresh the cache the auction valued the newly
        /// offered submarket with whatever was left in the slot. The household
        /// therefore never bid there and stayed envious, while the repair saw
        /// the column already on the list, added nothing, and reported the solve
        /// clean — 664 envious households, worst by 44% of value, behind a
        /// "converged, nothing left to repair".</summary>
        private void FreezeSlot(int i, int q, EconParams p)
        {
            int slot = _shortStart[i] + q;
            int kc = _shortItems[slot];
            int kk = kc / C, cc = kc - kk * C;
            double bs = kk == 1 ? _base1[i] : _base0[i];
            _slotPrem[slot] = bs * _premium[_seg[i]][cc];
            _slotTaste[slot] = Taste(i, kc, bs, p) + (kc == _homeKC[i] ? _homeBonus[i] : 0);
        }

        /// <summary>Build the level-uplift table, if the parameter it is a
        /// function of has moved. Every PUBLIC entry point that can reach a
        /// valuation calls this: Solve, plus ValueOf and QuoteOutsider, which
        /// the acceptance checks and the migration path call from outside a
        /// solve. ValueAt and ValueAtSlot are private and are only reachable
        /// through those three.</summary>
        private void EnsureLevelFactors(EconParams p)
        {
            if (_qfAlpha == p.LevelBidAlpha) return;
            double q1 = p.Quality(1);
            for (int l = 0; l <= Levels; l++) _qf[l] = p.Quality(Math.Max(1, l)) / q1;
            _qfAlpha = p.LevelBidAlpha;
        }

        /// <summary>Value at a submarket named by SHORTLIST SLOT rather than by
        /// index — the auction's hot path. Identical arithmetic to ValueAt, in
        /// the same order, reading the frozen premium and taste instead of
        /// recomputing a hash and two logs per bid.</summary>
        private double ValueAtSlot(int i, int slot, int level, EconParams p)
        { CallsValueSlot++; return SoftCapT(_slotPrem[slot] * _qf[level] + _slotTaste[slot], _cap[i]); }

        private static double SoftCapT(double v, double cap) { CallsSoftCap++; return SoftCap(v, cap); }

        /// <summary>Squash an unconstrained valuation against ability to pay.
        /// A HARD min was tried first and is wrong in a way worth recording: it
        /// makes the value function FLAT for every household whose valuation
        /// exceeds its ceiling, so every rich household values every good
        /// location identically and the whole top of the market prices at one
        /// number — measured p10 3.38, p50 7.16, p90 7.19, a 2.1× spread with
        /// the geography squeezed out of it. cap·(1 − e^(−v/cap)) is monotone
        /// everywhere, is v itself while v is small against the ceiling, and
        /// approaches the ceiling without reaching it, so ordering survives all
        /// the way up. What is left driving rent geography is then income
        /// sorting — which marginal bidder ends up at which door — and that is
        /// the mechanism a land-value model wants doing the work.</summary>
        private static double SoftCap(double v, double cap)
            => cap <= 1e-9 ? 0 : cap * (1 - Math.Exp(-Math.Max(0, v) / cap));

        /// <summary>This household's own permanent premium for this exact place,
        /// in money. Gumbel via the inverse CDF of a stable hash, so it never
        /// re-rolls, and scaled by the household's own budget so a rich
        /// household's idiosyncratic premium is bigger in cash as well as in
        /// utility.</summary>
        private static double Taste(int hid, int kc, double bse, EconParams p)
        {
            double e = SplitMix64.Hash01((ulong)hid * 1000003UL + (ulong)kc * 31UL + 5);
            double g = -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
            return p.AuctionTasteScale * bse * g;
        }

        private double ValueAt(int i, int sub, EconParams p)
        {
            int kc = KcOf(sub);
            int k = kc / C, c = kc - k * C;
            double bse = k == 1 ? _base1[i] : _base0[i];
            if (bse <= 0) return 0;
            double v = bse * _premium[_seg[i]][c] * _qf[LevelOf(sub)]
                       + Taste(i, kc, bse, p);
            if (kc == _homeKC[i]) v += _homeBonus[i];
            return SoftCap(v, _cap[i]);
        }

        // ------------------------------------------------------------------
        /// <summary>Clear the market and bid it out from scratch. Every call —
        /// the opening build and every repair round alike — starts all prices
        /// at the reserve and lets the ascent find the level under live bids.
        ///
        /// This is deliberately the ONLY way prices form. A round that
        /// resumed the standing market kept prices no live bid supported —
        /// a door whose tenants churned away held yesterday's number — and
        /// walking those stale prices down again from outside the ascent was
        /// tried three measured ways and never converged (KNOWN-RED.md,
        /// "measured dead ends"). Monotone ascent from below is the case the
        /// ε-auction's termination theorem covers; nothing else here is.
        ///
        /// Two structural consequences the acceptance checks lean on. A
        /// non-full door's posted price never leaves its reserve — SetPrices
        /// clamps the non-full branch — so a room that never fills is priced
        /// at cost by construction. And nobody mid-build holds a slot while
        /// bidding (winners leave the queue, losers re-enter holding
        /// nothing), so a door that fills can only churn tenants by eviction
        /// and stays full: the stranded-high vacancy cannot be produced.
        ///
        /// The counters (bids, evictions) accumulate across rounds, so the
        /// bid budget bounds the SOLVE rather than each round of it, and a
        /// solve that spends it says it did not converge.</summary>
        private void RunAuction(WorldState w, EconParams p, int nSub)
        {
            for (int s = 0; s < nSub; s++)
            {
                _slots[s].Clear();
                Price[s] = Reserve[s];
                Admitted[s] = double.PositiveInfinity;
            }
            for (int i = 0; i < w.Households.Count; i++)
            { Assignment[i] = -1; WinningBid[i] = 0; Why[i] = Outcome.Outbid; }
            Bids = 0; Evictions = 0; Rounds = 0;
            Unassigned = 0; Converged = false;
            _queue.Clear();
            for (int i = 0; i < w.Households.Count; i++)
                if (_shortCount[i] > 0) _queue.Add(i);

            // ε keeps the ascent finite: every eviction lifts a submarket's
            // admitted price by at least this much, and prices are bounded above
            // by the richest bidder, so the auction cannot run forever. The
            // price of ε is that the result is an ε-equilibrium — no household
            // envies another's place by more than ε — which is the standard
            // Bertsekas trade and is far below the granularity anything
            // downstream can see.
            double epsAbs = Math.Max(1e-9, p.AuctionEpsilon);
            long budget = (long)p.AuctionBidBudget * Math.Max(1, w.Households.Count);

            int head = 0;
            while (head < _queue.Count)
            {
                if (Bids >= budget) break;                 // out of budget: ε-equilibrium not reached
                int i = _queue[head++];
                Bids++;
                if (head > 1_000_000) { _queue.RemoveRange(0, head); head = 0; }

                // Best and runner-up SURPLUS over this household's shortlist,
                // at the prices standing right now.
                double bestSur = double.NegativeInfinity, nextSur = _outside[i];
                int bestSub = -1; double bestVal = 0;
                int st = _shortStart[i], n = _shortCount[i];
                for (int q = 0; q < n; q++)
                {
                    int kc = _shortItems[st + q];
                    for (int l = 1; l <= Levels; l++)
                    {
                        int sub = Sub(kc, l, C);
                        if (Capacity[sub] <= 0) continue;   // priced, but nothing to let
                        double val = ValueAtSlot(i, st + q, l, p);
                        // Do not queue at a door you cannot open. To take a slot
                        // that is already full you must beat the WORST BID still
                        // holding one, and the most you can bid is your value net
                        // of walking away — so if val − outside does not clear
                        // Admitted, this submarket is not available to you and
                        // reading its posted price as if it were is a fiction.
                        //
                        // Without this the solve livelocks. A household whose bid
                        // fell short would re-queue, the price was already pinned
                        // at admitted − ε so its failed bid moved nothing, its
                        // surplus was unchanged, and the argmax handed it the same
                        // submarket again. It burned the entire bid budget: 400200
                        // bids against 72819 evictions, i.e. 327k attempts that
                        // changed nothing, and the solve reported not converged.
                        // Rank by the surplus it would ACTUALLY get: the entry
                        // price at a door it would have to win, the price it
                        // already pays at the one it holds. The explicit "do not
                        // queue at a door you cannot open" filter that used to
                        // sit here is now implied and has been deleted — if this
                        // household cannot clear Admitted then val − Admitted ≤
                        // its outside option, so the door cannot be its best
                        // choice, and the livelock that filter was patching
                        // cannot arise in the first place.
                        double sur = val - (Assignment[i] == sub ? Price[sub] : EntryPrice(sub));
                        if (sur > bestSur) { nextSur = bestSur; bestSur = sur; bestSub = sub; bestVal = val; }
                        else if (sur > nextSur) nextSur = sur;
                    }
                }

                // Nothing in this city beats leaving it.
                if (bestSub < 0 || bestSur <= _outside[i])
                {
                    Assignment[i] = -1; WinningBid[i] = 0;
                    // Nothing it could win at all is being OUTBID; something it
                    // could win but would rather not have is DECLINING.
                    Why[i] = bestSub < 0 ? Outcome.Outbid : Outcome.Declined;
                    continue;
                }

                // The bid: what this household will pay here given what it
                // gives up by not taking its next best. This is the quantity the
                // old code was missing — it ranked raw willingness to pay, which
                // ignores that a household with a good second choice will not
                // chase its first very far.
                // ε is RELATIVE to what is being bid for. As a flat absolute it
                // has to climb from the owner's reserve to a bid of 170 in steps
                // of 0.002, and the ascent pays for every one of them: 371k bids
                // and 189k evictions for 5.5k households, i.e. each household
                // thrown out ~34 times. Proportional ε bounds the residual envy
                // at a fixed FRACTION of the bid instead of a fixed number of
                // currency units, which is both cheaper and the more meaningful
                // guarantee — nobody envies another's place by more than that
                // fraction of what the place is worth to them.
                double eps = Math.Max(epsAbs, p.AuctionEpsilonRel * Math.Abs(bestVal));
                // Never above what the place is worth to you net of walking
                // away. Without the cap the +ε can push a bid past its own
                // value when the household has no alternative worth anything —
                // and then it holds a slot at a price above its own valuation,
                // which is an individually irrational assignment (measured: 2 of
                // 1817). ε buys termination; it must not buy a bad trade.
                double bid = Math.Min(bestVal - _outside[i],
                                      bestVal - Math.Max(nextSur, _outside[i]) + eps);

                var slot = _slots[bestSub];
                if (slot.Count < Capacity[bestSub])
                {
                    slot.Add((bid, i));
                    Assignment[i] = bestSub; WinningBid[i] = bid; Why[i] = Outcome.Housed;
                    SetPrices(bestSub, band: eps);
                    continue;
                }

                // Full: displace the weakest holder if this bid beats it.
                int weakAt = 0; double weak = slot[0].bid;
                for (int q = 1; q < slot.Count; q++) if (slot[q].bid < weak) { weak = slot[q].bid; weakAt = q; }
                if (bid > weak)
                {
                    int loser = slot[weakAt].hh;
                    slot[weakAt] = (bid, i);
                    Assignment[i] = bestSub; WinningBid[i] = bid; Why[i] = Outcome.Housed;
                    Assignment[loser] = -1; WinningBid[loser] = 0; Why[loser] = Outcome.Outbid;
                    Evictions++;
                    SetPrices(bestSub, weak, eps);
                    _queue.Add(loser);                      // it bids again, elsewhere
                }
                else
                {
                    // It cannot win here at this price. Record it as the queue
                    // behind the door and re-bid: the loop above will now pick
                    // its next best, because Price[bestSub] has risen past it.
                    SetPrices(bestSub, bid, eps);
                    _queue.Add(i);
                }
                Rounds++;
            }
            Converged = Bids < budget;

            // Anything still holding a slot is housed; everyone else took the
            // outside option. Count the queue behind each door at final prices.
            Array.Clear(Filled, 0, nSub);
            for (int s = 0; s < nSub; s++)
            {
                Filled[s] = _slots[s].Count;
                // A pure build already leaves every non-full door posted at its
                // reserve (SetPrices clamps that branch), so only the Admitted
                // of never-filled doors needs settling: nothing was admitted,
                // and the honest number for a door with every room open is the
                // floor a taker would actually pay.
                if (_slots[s].Count == 0 && Capacity[s] > 0)
                { Admitted[s] = Reserve[s]; Price[s] = Reserve[s]; }
            }
            // Counted from the standing assignment rather than tallied in the
            // loop. Households with no shortlist never bid and are not part of
            // this market at all.
            Unassigned = 0;
            for (int i = 0; i < w.Households.Count; i++)
                if (_shortCount[i] > 0 && Assignment[i] < 0) Unassigned++;
        }

        /// <summary>Recompute a submarket's posted and admitted prices. Posted
        /// is the best bid that did NOT get in (floored at the reserve);
        /// admitted is the worst that did. An arriving challenger raises the
        /// posted price the moment it is turned away, which is what makes the
        /// posted number the FIRST EXCLUDED bid rather than a guess at one.</summary>
        private void SetPrices(int sub, double rejected = double.NegativeInfinity, double band = 0)
        {
            var slot = _slots[sub];
            double min = double.PositiveInfinity;
            for (int q = 0; q < slot.Count; q++) if (slot[q].bid < min) min = slot[q].bid;
            Admitted[sub] = slot.Count > 0 ? min : double.PositiveInfinity;
            if (slot.Count >= Capacity[sub] && Capacity[sub] > 0)
            {
                // What a challenger must actually beat is the WORST BID STILL
                // HOLDING A SLOT. Posting the last recorded rejection instead
                // let the two drift apart: a submarket that turned somebody away
                // early and then filled up with much stronger bids kept posting
                // the stale low number, so households read a price they could
                // never have obtained a unit at and the no-envy sweep lit up —
                // 31 households envious by up to 4.2% of value, all of it this
                // gap rather than any real disequilibrium.
                //
                // Posting `admitted` exactly would leave the marginal tenant
                // with no surplus at all, which is the textbook competitive
                // outcome but loses the design's property that every admitted
                // tenant is strictly better off inside than out. One ε below it
                // keeps that and is the honest estimate of the first excluded
                // challenger, which in an ε-auction sits within ε of the last
                // admitted one.
                double post = Math.Max(Reserve[sub], Math.Max(rejected, Admitted[sub] - band));
                Price[sub] = Math.Min(post, Admitted[sub]);
                if (rejected > double.NegativeInfinity) Excluded[sub]++;
            }
            else if (Capacity[sub] > 0)
            {
                // Room to spare, so nobody is excluded by capacity. The ask does
                // NOT fall back to the reserve: the door is re-letting a room at
                // what the room was going for, and the challengers it turned
                // away are still queued behind it.
                //
                // The floor matters more than it looks. A posted price that can
                // FALL is exactly what an ascending auction's termination
                // argument forbids — every eviction is supposed to lift some
                // price by at least ε so that no state of the market can recur.
                // One collapsing door re-opens every state the market had
                // already left, and the repair loop then oscillates instead of
                // converging. Measured, three ways: dropping to the reserve gave
                // 19 households still envious after 12 rounds; walking the price
                // down the queue as offers were declined (which is what a
                // landlord would actually do, and is defensible in isolation)
                // gave 40, 139, 12 and 43 on four seeds, every one of them
                // 12 rounds and not clean. Holding it gives an ascent again.
                //
                // During the opening build this is always the reserve and the
                // clamp is inert: the price starts there and a door can only
                // become non-full by losing a tenant, which the build never
                // does.
                // Never above what a sitting tenant bid — that is what makes
                // the assignment individually rational — and never below the
                // floor. An empty door admits nobody, so the cap is vacuous
                // there and the price simply holds until the round ends.
                Price[sub] = Math.Max(Reserve[sub], Math.Min(Price[sub], Admitted[sub]));
            }
        }

        /// <summary>Quote the city to somebody who does not live here.
        ///
        /// Scans every submarket and returns the one that gives THIS individual
        /// the most surplus at the posted price, subject to the same rule a
        /// resident faces: you may only count a full submarket if your value net
        /// of your own reservation clears what the worst sitting tenant is
        /// paying, because that is what it would take to get in. Returns −1 when
        /// no door is open to it, with `anyAttainable` distinguishing "there was
        /// something I could have had and none of it was worth it" from "nothing
        /// here was ever going to be mine" — the difference between declining a
        /// city and being priced out of it, which the arrival telemetry keeps
        /// separate because they mean opposite things to a player.
        ///
        /// No aggregate of the city enters this. The prospect reads posted
        /// prices, one submarket at a time, exactly as a resident does.</summary>
        public int QuoteOutsider(int segment, double budget, double densityTol, double reservation,
                                 ulong tasteKey, EconParams p,
                                 out double bestSurplus, out bool anyAttainable)
        {
            bestSurplus = double.NegativeInfinity; anyAttainable = false;
            int best = -1;
            EnsureLevelFactors(p);            // public entry: may be called outside a solve
            if (C <= 0 || (uint)segment >= (uint)_premium.Length) return -1;
            double cap = p.MaxRentOfIncome * (budget / Math.Max(1e-9, 1.0)) / Math.Max(1e-9, 1.0);
            // Ability to pay is derived the same way it is for a resident: from
            // income, not from the housing budget. budget = rentShare × income,
            // so income = budget / rentShare — but the caller already knows the
            // income it used, and passing the cap explicitly would be one more
            // thing to keep in step. Recover it from the same identity the
            // resident path uses.
            cap = p.MaxRentOfIncome * budget / Math.Max(1e-6, p.ProspectRentShareForCap);

            for (int k = 0; k < 2; k++)
            {
                double appeal = k == 1
                    ? Segment.DensityFloor + (1 - Segment.DensityFloor) * MathUtil.Clamp(densityTol, 0, 1)
                    : 1.0;
                double bse = budget * appeal;
                if (bse <= 0) continue;
                for (int c = 0; c < C; c++)
                {
                    int kc = Key(k, c, C);
                    double prem = bse * _premium[segment][c];
                    double taste = p.AuctionTasteScale * bse * Gumbel01(tasteKey * 1000003UL + (ulong)kc * 31UL + 5);
                    for (int l = 1; l <= Levels; l++)
                    {
                        int sub = Sub(kc, l, C);
                        if (Capacity[sub] <= 0) continue;
                        double val = SoftCap(prem * _qf[l] + taste, cap);
                        // A newcomer holds nothing, so every door costs it the
                        // entry price. The old explicit filter is implied by that,
                        // exactly as in the resident path.
                        double sur = val - EntryPrice(sub);
                        if (sur <= reservation) continue;
                        anyAttainable = true;
                        if (sur > bestSurplus) { bestSurplus = sur; best = sub; }
                    }
                }
            }
            return best;
        }

        private static double Gumbel01(ulong key)
        {
            double e = SplitMix64.Hash01(key);
            return -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
        }

        /// <summary>What it would COST THIS HOUSEHOLD to be at a submarket —
        /// the price it would actually pay, which is not the same number for
        /// everybody.
        ///
        /// A door has two prices and the distinction is deliberate. `Price` is
        /// the first-excluded challenger's bid and is what a SITTING TENANT
        /// pays: below the worst accepted bid, so every admitted tenant keeps
        /// strictly positive surplus, and it is what the assessment capitalizes.
        /// `Admitted` is the worst bid still holding a slot, and it is what a
        /// CHALLENGER must beat to get in at a full submarket. Where there is a
        /// free unit there is nobody to outbid and the asking price is the
        /// posted one.
        ///
        /// Everything that measures OPPORTUNITY has to read the challenger's
        /// number. Reading the tenant's number instead was the root defect: the
        /// band that separates the two is scaled by the LAST BIDDER's value
        /// (SetPrices' `band`), while every consumer scaled its tolerance by the
        /// READER's value. A rich household rejected at a door left the gap at
        /// ~0.5% of ITS value; a poorer household later read that gap as surplus
        /// it could capture and compared it against a tolerance ~0.5% of its own,
        /// much smaller, value. It registered as envious of a price that was
        /// never offered to anyone, and no amount of re-bidding could fix it,
        /// because its maximum bid could not clear Admitted. That is the residual
        /// "2 households envious at 1.5%" the repair loop chased forever, and the
        /// improving swap that came with it: swap-freeness follows from envy-
        /// freeness only when both sides are measured against ONE price vector,
        /// and there were two.</summary>
        public double CostTo(int sub, int hid)
            => Assignment[hid] == sub ? Price[sub] : EntryPrice(sub);

        /// <summary>What a challenger pays to get in here.</summary>
        public double EntryPrice(int sub)
            => Capacity[sub] > 0 && _slots[sub].Count >= Capacity[sub] ? Admitted[sub] : Price[sub];

        /// <summary>Price per unit at a submarket, for callers that ask about a
        /// (cluster, kind, level) rather than an index.</summary>
        public double PriceAt(int cluster, ZoneKind kind, int level)
        {
            int sub = SubOf(cluster, kind, level);
            return sub >= 0 ? Price[sub] : 0;
        }

        /// <summary>This household's value for a submarket, exposed so a checker
        /// can re-derive the equilibrium conditions from the same numbers the
        /// solve used rather than from a reimplementation of them.</summary>
        public double ValueOf(int hid, int sub, EconParams p)
        { EnsureLevelFactors(p); return ValueAt(hid, sub, p); }
        public int ShortlistCount(int hid)
            => (uint)hid < (uint)_shortCount.Length ? _shortCount[hid] : 0;
        public int ShortlistAt(int hid, int q) => _shortItems[_shortStart[hid] + q];
        /// <summary>This household's own outside option, for checks that have to
        /// state individual rationality against the right number.</summary>
        public double OutsideOf(int hid)
            => (uint)hid < (uint)_outside.Length ? _outside[hid] : 0;

        public int SubOf(int cluster, ZoneKind kind, int level)
        {
            if (C <= 0 || (uint)cluster >= (uint)C) return -1;
            if (kind != ZoneKind.ResidentialLow && kind != ZoneKind.ResidentialHigh) return -1;
            int sub = Sub(Key(kind == ZoneKind.ResidentialHigh ? 1 : 0, cluster, C),
                          Math.Min(Levels, Math.Max(1, level)), C);
            return (uint)sub < (uint)Price.Length ? sub : -1;
        }

        // ---- the queue behind the door ------------------------------------
        public const int ShadowDepth = 32;
        /// <summary>[sub*ShadowDepth + r] the (r+1)-th highest bid a household
        /// NOT already holding a slot there would make for one more unit, at
        /// final prices. This is what an added unit would actually fetch, and it
        /// exists for submarkets with no stock at all — which is precisely the
        /// signal a developer needs and the realized price cannot give, because
        /// a submarket with nothing standing has no realized price.</summary>
        public double[] Shadow = Array.Empty<double>();
        public int[] ShadowCount = Array.Empty<int>();

        /// <summary>How many households are waiting behind this door who would
        /// pay more than it costs to operate the unit — the queue that a new
        /// building here could actually let to. Real households with real
        /// competition-adjusted bids, counted; the truncation at ShadowDepth is
        /// the one approximation and it is a floor, never an overstatement.</summary>
        public int QueueAbove(int sub, double reserve)
        {
            if ((uint)sub >= (uint)ShadowCount.Length) return 0;
            int n = ShadowCount[sub], at = sub * ShadowDepth, k = 0;
            while (k < n && Shadow[at + k] > reserve) k++;   // Shadow is descending
            return k;
        }

        /// <summary>What one more unit here would let for, given the queue. Rank
        /// is 1-based: the 3rd added unit goes to the 3rd-best waiting bid.</summary>
        public double ShadowAt(int sub, int rank)
        {
            if ((uint)sub >= (uint)ShadowCount.Length) return 0;
            int r = Math.Min(ShadowDepth, Math.Max(1, rank)) - 1;
            if (r >= ShadowCount[sub]) return 0;      // queue shorter than the ask
            return Shadow[sub * ShadowDepth + r];
        }

        /// <summary>For each household, find the submarket it would most like
        /// to switch to at current prices and is not being shown, and add it to
        /// its shortlist, and mark it for re-decision. Returns how many
        /// households have anything left to do; zero means every household's
        /// best option is one it has been shown AND is the one it holds, i.e.
        /// the solve is a true equilibrium over the whole market and not just
        /// over what each household happened to be offered.</summary>
        /// <summary>Freeze the repair scan's taste term for every household
        /// against every key that holds stock. One pass per solve replaces one
        /// pass per round.</summary>
        private void BuildScanTaste(WorldState w, EconParams p)
        {
            int nh = w.Households.Count;
            _scanStride = _liveKc.Count;
            long need = (long)nh * _scanStride;
            if (need <= 0) { _scanTaste = Array.Empty<float>(); return; }
            if (_scanTaste.Length != need) _scanTaste = new float[need];
            for (int i = 0; i < nh; i++)
            {
                if (_shortCount[i] <= 0) continue;
                int at = i * _scanStride;
                for (int q = 0; q < _scanStride; q++)
                {
                    int kc = _liveKc[q];
                    int k = kc / C;
                    double bse = k == 1 ? _base1[i] : _base0[i];
                    _scanTaste[at + q] = (float)(Taste(i, kc, bse, p)
                                                 + (kc == _homeKC[i] ? _homeBonus[i] : 0));
                }
            }
        }

        /// <summary>Snapshot the live doors for one scan: the flat arrays the
        /// scan cell reads instead of a divide, two List<int> indexer calls
        /// and EntryPrice's capacity/slot-count/pointer chase. Called from the
        /// top of AddEnviedColumns, AFTER CutVacancies has written its prices —
        /// see the field comment for why that ordering is load bearing.</summary>
        private void BuildDoorTable()
        {
            int nk = _liveKc.Count, nd = _liveLevels.Count;
            if (_dKc.Length != nk)
            { _dKc = new int[nk]; _dHigh = new bool[nk]; _dCluster = new int[nk]; }
            if (_dStart.Length != nk + 1) _dStart = new int[nk + 1];
            if (_dSub.Length != nd)
            { _dSub = new int[nd]; _dQf = new double[nd]; _dEntry = new double[nd]; }
            for (int q = 0; q < nk; q++)
            {
                int kc = _liveKc[q];
                int k = kc / C;
                _dKc[q] = kc; _dHigh[q] = k == 1; _dCluster[q] = kc - k * C;
                _dStart[q] = _liveKcStart[q];
                for (int t = _liveKcStart[q]; t < _liveKcStart[q + 1]; t++)
                {
                    int l = _liveLevels[t];
                    int sub = Sub(kc, l, C);
                    _dSub[t] = sub; _dQf[t] = _qf[l]; _dEntry[t] = EntryPrice(sub);
                }
            }
            _dStart[nk] = nd;                       // the sentinel, same as _liveKcStart's
        }

        /// <summary>THE ORACLE'S REFERENCE. The repair scan exactly as it was
        /// before this file's arithmetic was cheapened: two Math.Pow per cell,
        /// an EntryPrice call per cell, the List<int> key walk. Nothing here
        /// may be optimized — its whole value is that it is an independent
        /// second opinion on the same state.</summary>
        private int RefScanBest(int i, int mine, double mySur, double myEps, EconParams p,
                               out double bestGainOut)
        {
            double bestGain = 0; int bestKC = -1;
            for (int q = 0; q < _liveKc.Count; q++)
            {
                int kc = _liveKc[q];
                int k = kc / C, c = kc - k * C;
                double bse = k == 1 ? _base1[i] : _base0[i];
                if (bse <= 0) continue;
                double prem = bse * _premium[_seg[i]][c];
                double taste = _scanTaste[i * _scanStride + q];
                int lo = _liveKcStart[q], hi = _liveKcStart[q + 1];
                for (int t = lo; t < hi; t++)
                {
                    int l = _liveLevels[t];
                    int sub = Sub(kc, l, C);
                    if (sub == mine) continue;
                    double raw = prem * (p.Quality(l) / p.Quality(1)) + taste;
                    double upper = Math.Min(Math.Max(raw, 0), _cap[i]);
                    double entry = EntryPrice(sub);
                    double gainUpper = (upper - entry) - mySur;
                    if (gainUpper <= bestGain || gainUpper <= myEps + p.AuctionEpsilon) continue;
                    double val = SoftCap(raw, _cap[i]);
                    double gain = (val - entry) - mySur;
                    double band = myEps + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(val));
                    if (gain <= band || gain <= bestGain) continue;
                    bestGain = gain; bestKC = kc;
                }
            }
            bestGainOut = bestGain;
            return bestKC;
        }

        private int AddEnviedColumns(WorldState w, EconParams p, int nSub)
        {
            BuildDoorTable();
            int added = 0;
            var oracleAdded = ScanOracle ? new List<int>() : null;
            if (ScanOracle) { _refDirty.Clear(); OracleScans++; }
            BlockedListed = 0; BlockedFull = 0;
            int nKeys = _liveKc.Count;
            for (int i = 0; i < w.Households.Count; i++)
            {
                int n = _shortCount[i];
                if (n <= 0) continue;
                int mine = Assignment[i];
                double myVal = mine >= 0 ? ValueAt(i, mine, p) : 0;
                // What it pays where it is, against what it would pay to move.
                double mySur = mine >= 0 ? myVal - Price[mine] : _outside[i];
                // The repair's threshold has to BE the equilibrium threshold.
                // Chasing anything stricter means the loop never runs out of
                // work: it kept finding "violations" inside the band the result
                // is allowed to sit in, added a column, re-solved, and found
                // more — 12 rounds with zero real envy and still reporting it
                // had not finished. Same band here as in the acceptance check,
                // and the two ε's are measured against different valuations, so
                // it is their sum.
                double myEps = Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(myVal));

                // Walk only the keys that hold stock, and hoist the part of a
                // valuation that does not vary with level out of the level loop:
                // the location premium and this household's own permanent taste
                // for the place are per (density, cluster), while the level only
                // scales the premium. The old form paid a hash and two logs for
                // that taste once per LEVEL per submarket, over the whole
                // 2×clusters×5 space including the three quarters of it that is
                // empty — on the reference city, 1960 evaluations per household
                // per repair round where 90 will do.
                double bestGain = 0; int bestKC = -1;
                // Hoisted out of the key loop: the household's ability to pay,
                // the smallest band any door could produce for it, its
                // segment's premium row, and the base of its taste row. Each
                // was re-loaded or re-added once per KEY, and there are 257
                // live keys per scan.
                double cap = _cap[i];
                double bandMin = myEps + p.AuctionEpsilon;
                double[] prow = _premium[_seg[i]];
                int tasteAt = i * _scanStride;
                for (int q = 0; q < nKeys; q++)
                {
                    double bse = _dHigh[q] ? _base1[i] : _base0[i];
                    if (bse <= 0) continue;
                    double prem = bse * prow[_dCluster[q]];
                    double taste = _scanTaste[tasteAt + q];
                    int hi = _dStart[q + 1];
                    for (int t = _dStart[q]; t < hi; t++)
                    {
                        int sub = _dSub[t];
                        if (sub == mine) continue;
                        // SoftCap is monotone and bounded above by both its
                        // argument and the cap, so min(max(raw,0), cap) is an
                        // EXACT upper bound on the value — and therefore on the
                        // gain. If even that bound cannot beat the best gain
                        // found so far, or cannot clear the smallest band the
                        // real value could produce, the answer is already known
                        // and the exp() is wasted work. This is a skip, not an
                        // approximation: nothing that could have won is dropped.
                        //
                        // THE EXP IS NOT THE SCAN, which is what the comment
                        // that used to sit here claimed. Measured, with
                        // CutVacancies split out of the fused repairScan number
                        // for the first time: of the 56.0 s the scan cost on a
                        // 300-tick reference run, `p.Quality(l)/p.Quality(1)` —
                        // TWO Math.Pow and a divide, computed unconditionally,
                        // ABOVE the bound test — ran in every one of the ~866M
                        // cells, while the exp below it ran only in the cells
                        // the bound test let through. Hoisting that one line
                        // into _qf took the scan to 19.7 s and the repair phase
                        // from 69824 to 27645 ms, with no decision anywhere
                        // changed, which is 2.5× for ten lines against the 1.15×
                        // the last narrowing of this loop actually delivered.
                        // Reading the doors from a per-scan snapshot instead of
                        // calling EntryPrice per cell took it to 15.0 s on top
                        // of that. The bound test is still worth having; it was
                        // simply never the dominant term, and this comment
                        // asserted otherwise for three rounds of optimization
                        // that all read it and believed it.
                        double raw = prem * _dQf[t] + taste;
                        double upper = Math.Min(Math.Max(raw, 0), cap);
                        double entry = _dEntry[t];
                        double gainUpper = (upper - entry) - mySur;
                        if (gainUpper <= bestGain || gainUpper <= bandMin) continue;

                        double val = SoftCap(raw, cap);
                        double gain = (val - entry) - mySur;
                        double band = myEps + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(val));
                        if (gain <= band || gain <= bestGain) continue;
                        bestGain = gain; bestKC = _dKc[q];
                    }
                }
                // The differential oracle, off in production. Same state, same
                // household, the untouched reference scan — compared on the
                // column chosen, on the gain that chose it (`!=` on doubles,
                // because this change is bit-exact and anything else would be a
                // bug), and below on the dirty SET the two produce. The
                // reference's own dirty decision is read from the shortlist as
                // it stands NOW, before the append below, which is the same
                // state the production decision reads.
                if (ScanOracle)
                {
                    OracleHhScans++;
                    int refKC = RefScanBest(i, mine, mySur, myEps, p, out double refGain);
                    if (refKC != bestKC) OracleKcMismatch++;
                    if (refGain != bestGain) OracleGainMismatch++;
                    if (refKC >= 0)
                    {
                        bool refListed = false;
                        for (int q = 0; q < n; q++)
                            if (_shortItems[_shortStart[i] + q] == refKC) { refListed = true; break; }
                        if (!refListed && n < _stride) _refDirty.Add(i);
                    }
                }
                if (bestKC < 0) continue;

                int st = _shortStart[i];
                bool have = false;
                for (int q = 0; q < n; q++) if (_shortItems[st + q] == bestKC) { have = true; break; }
                // Two ways a household that WANTS somewhere else gets no help.
                // Both were silent, and between them they are the whole
                // residual: a repair loop that reports "nothing left to add"
                // while households are still envious is not describing an
                // equilibrium, it is describing its own blind spot. Counted, so
                // the next question is which one.
                if (have)
                {
                    // NOT a missing column — a missing DECISION. A submarket
                    // holds several slots at one price, so its price can rise
                    // around a sitting tenant without ever evicting it: the
                    // other slots fill with stronger bids, admitted climbs, and
                    // the marginal tenant quietly pays more for the same room.
                    // In a one-slot assignment auction that cannot happen, which
                    // is why the textbook never mentions it; here it is the
                    // whole residual. Measured worst case: a household bid into
                    // an empty submarket at its reserve, watched the price climb
                    // to 2.30 as three neighbours moved in, and ended up
                    // preferring the submarket ONE LEVEL DOWN — 6.7% of value
                    // better, admitted 1.54 against a bid it could take to 3.11
                    // — with no way to say so, because that submarket shares its
                    // (density, cluster) key and the key was already listed.
                    //
                    // With full re-clear rounds this needs NO queueing: the
                    // next round re-decides everyone from the reserve, so a
                    // household envious of a door already on its list gets its
                    // contest by construction. The counter stays as the
                    // diagnostic that says how often that case arises.
                    BlockedListed++;
                    continue;
                }
                if (n >= _stride) { BlockedFull++; continue; }  // list full; nothing safe to drop
                _shortItems[st + n] = bestKC; _shortCount[i] = n + 1;
                FreezeSlot(i, n, p);
                added++;
                oracleAdded?.Add(i);
            }
            // THE SET, not just the per-household answer: same households, same
            // order. This is the invariant the brief asks for, and it is
            // checked on every scan of every solve rather than at the end of a
            // run, so a divergence is attributed to the scan that caused it.
            if (ScanOracle)
            {
                bool same = _refDirty.Count == oracleAdded.Count;
                for (int q = 0; same && q < oracleAdded.Count; q++)
                    if (_refDirty[q] != oracleAdded[q]) same = false;
                if (!same) OracleSetMismatch++;
            }
            // Counting ADDED COLUMNS is the loop's correct stopping condition
            // again, because a full re-clear re-decides every household every
            // round: the only way progress can still be possible is a door
            // somebody has never been shown. (Under resumed rounds this
            // counted re-decisions instead, because a tenant repriced in
            // place needed asking again — that case no longer exists.)
            return added;
        }


        private void BuildShadow(WorldState w, EconParams p, int nSub)
        {
            if (Shadow.Length != nSub * ShadowDepth)
            { Shadow = new double[nSub * ShadowDepth]; ShadowCount = new int[nSub]; }
            Array.Clear(Shadow, 0, Shadow.Length);
            Array.Clear(ShadowCount, 0, nSub);

            for (int i = 0; i < w.Households.Count; i++)
            {
                int n = _shortCount[i];
                if (n <= 0) continue;
                // This household's best and runner-up ATTAINABLE surplus at the
                // final prices — what it gives up by taking a new unit instead.
                double best = _outside[i], second = _outside[i];
                int bestSub = -1;
                int st = _shortStart[i];
                for (int q = 0; q < n; q++)
                    for (int l = 1; l <= Levels; l++)
                    {
                        int sub = Sub(_shortItems[st + q], l, C);
                        if (Capacity[sub] <= 0) continue;
                        double sur = ValueAt(i, sub, p) - CostTo(sub, i);
                        if (sur > best) { second = best; best = sur; bestSub = sub; }
                        else if (sur > second) second = sur;
                    }

                for (int q = 0; q < n; q++)
                    for (int l = 1; l <= Levels; l++)
                    {
                        int sub = Sub(_shortItems[st + q], l, C);
                        if (Assignment[i] == sub) continue;    // already housed there
                        double alt = Math.Max(sub == bestSub ? second : best, _outside[i]);
                        double bid = ValueAt(i, sub, p) - alt;
                        if (bid <= Reserve[sub]) continue;     // would not cover the owner's floor
                        Insert(sub, bid);
                    }
            }
        }

        private void Insert(int sub, double bid)
        {
            int at = sub * ShadowDepth;
            int n = ShadowCount[sub];
            int pos = n < ShadowDepth ? n : ShadowDepth - 1;
            if (n == ShadowDepth && bid <= Shadow[at + ShadowDepth - 1]) return;
            while (pos > 0 && Shadow[at + pos - 1] < bid) { Shadow[at + pos] = Shadow[at + pos - 1]; pos--; }
            Shadow[at + pos] = bid;
            if (n < ShadowDepth) ShadowCount[sub] = n + 1;
        }
    }
}
