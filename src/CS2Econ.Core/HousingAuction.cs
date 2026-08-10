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
        private readonly List<int> _liveKc = new List<int>();
        private readonly List<int> _liveKcStart = new List<int>();
        private readonly List<int> _liveLevels = new List<int>();

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
            C = acc.C;
            int nSub = S;
            EnsureArrays(w, nSub);

            BuildSubmarkets(w, p, nSub);
            BuildHouseholds(w, acc, p);
            RunAuction(w, p, nSub);
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
                int added = AddEnviedColumns(w, p, nSub);
                if (added == 0) { RepairClean = true; break; }
                RepairRounds++;
                RunAuction(w, p, nSub);
            }
            if (p.AuctionRepairRounds <= 0) RepairClean = true;   // repair disabled on purpose
            BuildShadow(w, p, nSub);
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
            Span<double> bestV = stackalloc double[64];
            Span<int> bestKC = stackalloc int[64];
            if (K > 64) K = 64;

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

        /// <summary>Value at a submarket named by SHORTLIST SLOT rather than by
        /// index — the auction's hot path. Identical arithmetic to ValueAt, in
        /// the same order, reading the frozen premium and taste instead of
        /// recomputing a hash and two logs per bid.</summary>
        private double ValueAtSlot(int i, int slot, int level, EconParams p)
            => SoftCap(_slotPrem[slot] * (p.Quality(level) / p.Quality(1)) + _slotTaste[slot], _cap[i]);

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
            double v = bse * _premium[_seg[i]][c] * (p.Quality(LevelOf(sub)) / p.Quality(1))
                       + Taste(i, kc, bse, p);
            if (kc == _homeKC[i]) v += _homeBonus[i];
            return SoftCap(v, _cap[i]);
        }

        // ------------------------------------------------------------------
        private void RunAuction(WorldState w, EconParams p, int nSub)
        {
            // Each round starts from an empty market. The repair rounds call
            // straight back in here, and leaving the previous round's slots
            // standing meant every household bid a second time for a market
            // already holding it — households evicting themselves, the loser
            // bookkeeping unwinding, and the city emptying: 1817 housed became
            // 264 (measured). A round is a clean clearing at enriched
            // shortlists, not an increment on the last one.
            for (int s = 0; s < nSub; s++)
            {
                _slots[s].Clear();
                Price[s] = Reserve[s];
                Admitted[s] = double.PositiveInfinity;
            }
            for (int i = 0; i < w.Households.Count; i++)
            { Assignment[i] = -1; WinningBid[i] = 0; Why[i] = Outcome.Outbid; }
            _queue.Clear();
            for (int i = 0; i < w.Households.Count; i++)
                if (_shortCount[i] > 0) _queue.Add(i);
            Unassigned = 0; Bids = 0; Evictions = 0; Rounds = 0; Converged = false;

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
                        if (_slots[sub].Count >= Capacity[sub]
                            && val - _outside[i] <= Admitted[sub]) continue;
                        double sur = val - Price[sub];
                        if (sur > bestSur) { nextSur = bestSur; bestSur = sur; bestSub = sub; bestVal = val; }
                        else if (sur > nextSur) nextSur = sur;
                    }
                }

                // Nothing in this city beats leaving it.
                if (bestSub < 0 || bestSur <= _outside[i])
                {
                    Assignment[i] = -1; Unassigned++;
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
                    if (slot.Count == Capacity[bestSub]) SetPrices(bestSub, band: eps);
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
                if (_slots[s].Count == 0) { Admitted[s] = Reserve[s]; Price[s] = Reserve[s]; }
            }
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
            Admitted[sub] = slot.Count > 0 ? min : Reserve[sub];
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
            else if (Capacity[sub] > 0) Price[sub] = Reserve[sub];
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
                        double val = SoftCap(prem * (p.Quality(l) / p.Quality(1)) + taste, cap);
                        if (Filled[sub] >= Capacity[sub] && val - reservation <= Admitted[sub]) continue;
                        anyAttainable = true;
                        double sur = val - Price[sub];
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
        public double ValueOf(int hid, int sub, EconParams p) => ValueAt(hid, sub, p);
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
        /// its shortlist. Returns how many were added; zero means the current
        /// shortlists already contain everybody's best option, i.e. the solve is
        /// a true equilibrium over the whole market and not just over what each
        /// household happened to be offered.</summary>
        private int AddEnviedColumns(WorldState w, EconParams p, int nSub)
        {
            int added = 0;
            for (int i = 0; i < w.Households.Count; i++)
            {
                int n = _shortCount[i];
                if (n <= 0) continue;
                int mine = Assignment[i];
                double myVal = mine >= 0 ? ValueAt(i, mine, p) : 0;
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
                for (int q = 0; q < _liveKc.Count; q++)
                {
                    int kc = _liveKc[q];
                    int k = kc / C, c = kc - k * C;
                    double bse = k == 1 ? _base1[i] : _base0[i];
                    if (bse <= 0) continue;
                    double prem = bse * _premium[_seg[i]][c];
                    double taste = Taste(i, kc, bse, p) + (kc == _homeKC[i] ? _homeBonus[i] : 0);
                    int lo = _liveKcStart[q], hi = _liveKcStart[q + 1];
                    for (int t = lo; t < hi; t++)
                    {
                        int l = _liveLevels[t];
                        int sub = Sub(kc, l, C);
                        if (sub == mine) continue;
                        double val = SoftCap(prem * (p.Quality(l) / p.Quality(1)) + taste, _cap[i]);
                        double gain = (val - Price[sub]) - mySur;
                        double band = myEps + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(val));
                        if (gain <= band || gain <= bestGain) continue;
                        bestGain = gain; bestKC = kc;
                    }
                }
                if (bestKC < 0) continue;

                int st = _shortStart[i];
                bool have = false;
                for (int q = 0; q < n; q++) if (_shortItems[st + q] == bestKC) { have = true; break; }
                if (have) continue;                       // already listed, just outbid
                if (n >= _stride) continue;               // list is full; nothing safe to drop
                _shortItems[st + n] = bestKC; _shortCount[i] = n + 1;
                FreezeSlot(i, n, p);
                added++;
            }
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
                        double sur = ValueAt(i, sub, p) - Price[sub];
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
