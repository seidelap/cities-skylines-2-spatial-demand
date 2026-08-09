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

        // ---- telemetry -----------------------------------------------------
        public int Rounds, Bids, Evictions, Unassigned;
        public bool Converged;

        // ---- working state -------------------------------------------------
        private int[] _shortStart = Array.Empty<int>();  // per household, into _shortItems
        private int[] _shortCount = Array.Empty<int>();
        private int[] _shortItems = Array.Empty<int>();  // (k*C+c) keys, price-free ranked
        private double[] _base0 = Array.Empty<double>(); // per household: bid base, Low
        private double[] _base1 = Array.Empty<double>(); // per household: bid base, High
        private double[] _homeBonus = Array.Empty<double>();
        private int[] _homeKC = Array.Empty<int>();      // (k*C+c) of current home, −1
        private int[] _seg = Array.Empty<int>();
        private double[][] _premium = Array.Empty<double[]>(); // [segment][cluster]
        private readonly List<int> _queue = new List<int>();
        /// <summary>Per submarket, the bids currently holding its slots. Kept as
        /// a plain list because capacity is small (a submarket is one density at
        /// one cluster at one level); the min is found by scan.</summary>
        private List<(double bid, int hh)>[] _slots = Array.Empty<List<(double, int)>>();

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
            BuildShadow(w, p, nSub);
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
                Assignment = new int[nh]; WinningBid = new double[nh];
                _shortStart = new int[nh]; _shortCount = new int[nh];
                _base0 = new double[nh]; _base1 = new double[nh];
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

            int K = Math.Max(2, p.AuctionShortlist);
            int nh = w.Households.Count;
            if (_shortItems.Length != nh * K) _shortItems = new int[nh * K];

            // Scratch for the shortlist selection: a K-sized insertion list.
            Span<double> bestV = stackalloc double[64];
            Span<int> bestKC = stackalloc int[64];
            if (K > 64) K = 64;

            for (int i = 0; i < nh; i++)
            {
                var h = w.Households[i];
                _shortStart[i] = i * K; _shortCount[i] = 0;
                Assignment[i] = -1; WinningBid[i] = 0;
                if (h.ExitedTick >= 0) { _homeKC[i] = -1; continue; }

                var seg = Segment.All[h.Segment];
                _seg[i] = h.Segment;
                double income = Math.Max(1e-6, AccessState.HouseholdIncomeEstimate(h, seg, p));
                double budget = h.RentShare * income;
                _base0[i] = budget * h.DensityAppeal(ZoneKind.ResidentialLow);
                _base1[i] = budget * h.DensityAppeal(ZoneKind.ResidentialHigh);
                _homeKC[i] = h.HomeParcel >= 0 && (uint)w.Parcels[h.HomeParcel].Cluster < (uint)C
                    ? Key(w.Parcels[h.HomeParcel].Use == ZoneKind.ResidentialHigh ? 1 : 0,
                          w.Parcels[h.HomeParcel].Cluster, C)
                    : -1;
                // Moving cost as a per-tick flow, so it is commensurate with a
                // rent: the lump this household would pay to move, spread over
                // how long it expects to stay. Written as a lump against a
                // per-tick budget it came out two orders of magnitude too big.
                _homeBonus[i] = h.MovingCostDraw / Math.Max(1, p.MoveAmortTicks);

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
                        double v = bse * _premium[h.Segment][c] + Taste(h.Id, kc, bse, p);
                        if (kc == _homeKC[i]) v += _homeBonus[i];
                        // Anything worth less than leaving the city is not worth
                        // tracking — the caller's own suggestion, and it is what
                        // bounds K honestly instead of by a magic number. It
                        // also gives emigration a real trigger: a household with
                        // an EMPTY shortlist has nowhere in this city it would
                        // rather be than gone.
                        if (v <= p.OutsideOption) continue;
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
                if (_homeKC[i] >= 0)
                {
                    bool have = false;
                    for (int q = 0; q < cnt; q++) if (bestKC[q] == _homeKC[i]) { have = true; break; }
                    if (!have)
                    {
                        if (cnt < K) { bestKC[cnt] = _homeKC[i]; cnt++; }
                        else
                        {
                            worst = bestV[0]; worstAt = 0;
                            for (int q = 1; q < K; q++) if (bestV[q] < worst) { worst = bestV[q]; worstAt = q; }
                            bestKC[worstAt] = _homeKC[i];
                        }
                    }
                }
                for (int q = 0; q < cnt; q++) _shortItems[i * K + q] = bestKC[q];
                _shortCount[i] = cnt;
            }
        }

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
            return v;
        }

        // ------------------------------------------------------------------
        private void RunAuction(WorldState w, EconParams p, int nSub)
        {
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
            double eps = Math.Max(1e-6, p.AuctionEpsilon);
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
                double bestSur = double.NegativeInfinity, nextSur = p.OutsideOption;
                int bestSub = -1; double bestVal = 0;
                int st = _shortStart[i], n = _shortCount[i];
                for (int q = 0; q < n; q++)
                {
                    int kc = _shortItems[st + q];
                    for (int l = 1; l <= Levels; l++)
                    {
                        int sub = Sub(kc, l, C);
                        if (Capacity[sub] <= 0) continue;   // priced, but nothing to let
                        double val = ValueAt(i, sub, p);
                        double sur = val - Price[sub];
                        if (sur > bestSur) { nextSur = bestSur; bestSur = sur; bestSub = sub; bestVal = val; }
                        else if (sur > nextSur) nextSur = sur;
                    }
                }

                // Nothing in this city beats leaving it.
                if (bestSub < 0 || bestSur <= p.OutsideOption) { Assignment[i] = -1; Unassigned++; continue; }

                // The bid: what this household will pay here given what it
                // gives up by not taking its next best. This is the quantity the
                // old code was missing — it ranked raw willingness to pay, which
                // ignores that a household with a good second choice will not
                // chase its first very far.
                double bid = bestVal - Math.Max(nextSur, p.OutsideOption) + eps;

                var slot = _slots[bestSub];
                if (slot.Count < Capacity[bestSub])
                {
                    slot.Add((bid, i));
                    Assignment[i] = bestSub; WinningBid[i] = bid;
                    if (slot.Count == Capacity[bestSub]) SetPrices(bestSub);
                    continue;
                }

                // Full: displace the weakest holder if this bid beats it.
                int weakAt = 0; double weak = slot[0].bid;
                for (int q = 1; q < slot.Count; q++) if (slot[q].bid < weak) { weak = slot[q].bid; weakAt = q; }
                if (bid > weak)
                {
                    int loser = slot[weakAt].hh;
                    slot[weakAt] = (bid, i);
                    Assignment[i] = bestSub; WinningBid[i] = bid;
                    Assignment[loser] = -1; WinningBid[loser] = 0;
                    Evictions++;
                    SetPrices(bestSub, weak);
                    _queue.Add(loser);                      // it bids again, elsewhere
                }
                else
                {
                    // It cannot win here at this price. Record it as the queue
                    // behind the door and re-bid: the loop above will now pick
                    // its next best, because Price[bestSub] has risen past it.
                    SetPrices(bestSub, bid);
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
        private void SetPrices(int sub, double rejected = double.NegativeInfinity)
        {
            var slot = _slots[sub];
            double min = double.PositiveInfinity;
            for (int q = 0; q < slot.Count; q++) if (slot[q].bid < min) min = slot[q].bid;
            Admitted[sub] = slot.Count > 0 ? min : Reserve[sub];
            if (slot.Count >= Capacity[sub] && Capacity[sub] > 0)
            {
                double post = Math.Max(Reserve[sub], rejected);
                if (post > Price[sub]) { Price[sub] = Math.Min(post, Admitted[sub]); Excluded[sub]++; }
            }
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

        public int SubOf(int cluster, ZoneKind kind, int level)
        {
            if (C <= 0 || (uint)cluster >= (uint)C) return -1;
            if (kind != ZoneKind.ResidentialLow && kind != ZoneKind.ResidentialHigh) return -1;
            int sub = Sub(Key(kind == ZoneKind.ResidentialHigh ? 1 : 0, cluster, C),
                          Math.Min(Levels, Math.Max(1, level)), C);
            return (uint)sub < (uint)Price.Length ? sub : -1;
        }

        // ---- the queue behind the door ------------------------------------
        public const int ShadowDepth = 16;
        /// <summary>[sub*ShadowDepth + r] the (r+1)-th highest bid a household
        /// NOT already holding a slot there would make for one more unit, at
        /// final prices. This is what an added unit would actually fetch, and it
        /// exists for submarkets with no stock at all — which is precisely the
        /// signal a developer needs and the realized price cannot give, because
        /// a submarket with nothing standing has no realized price.</summary>
        public double[] Shadow = Array.Empty<double>();
        public int[] ShadowCount = Array.Empty<int>();

        /// <summary>What one more unit here would let for, given the queue. Rank
        /// is 1-based: the 3rd added unit goes to the 3rd-best waiting bid.</summary>
        public double ShadowAt(int sub, int rank)
        {
            if ((uint)sub >= (uint)ShadowCount.Length) return 0;
            int r = Math.Min(ShadowDepth, Math.Max(1, rank)) - 1;
            if (r >= ShadowCount[sub]) return 0;      // queue shorter than the ask
            return Shadow[sub * ShadowDepth + r];
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
                double best = p.OutsideOption, second = p.OutsideOption;
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
                        double alt = Math.Max(sub == bestSub ? second : best, p.OutsideOption);
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
