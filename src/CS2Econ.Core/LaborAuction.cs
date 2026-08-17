using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>The labor market as ONE assignment problem, solved by the same
    /// ascending-auction machinery as HousingAuction — the theory transfer is
    /// the point.
    ///
    /// WHY THIS EXISTS. Employment was the last system-computed allocation:
    /// each adult drawn i.i.d. against a Sinkhorn-balanced rate, placement by
    /// commute-weighted reservoir sample, wages identical at every firm, and
    /// firms charged the CITYWIDE wage bill pro-rata to filled slots — firm
    /// A's actual members' pay never touched firm A's costs. No price existed
    /// anywhere in it. Here, who works where and at what TOTAL comp come out
    /// of one market.
    ///
    /// THE TRANSFORMATION. One exact change of variable avoids every sign
    /// error: the machinery runs in ASCENDING-PRICE space with
    /// p_d = Cap_d − T_d ≥ 0, where T_d is total comp per earner per tick at
    /// door d and Cap_d is the firm's own forecast of marginal revenue per
    /// slot. A door's reserve is p = 0, i.e. comp at its CAP: "unsold rooms
    /// rest at cost" becomes UNFILLED SLOTS REST AT MARGINAL PRODUCT — an
    /// undersubscribed door posts its full cap, structurally. Ascending p is
    /// descending comp: workers compete for an oversubscribed door by
    /// accepting lower T until demand equals capacity. Excess labor pushes
    /// comp toward reservations; scarce labor rests comp near caps because
    /// rival doors bid workers away. The wage/dividend split EMERGES from
    /// competition rather than being decreed.
    ///
    /// A DOOR is a (firm, labor class) pair; its capacity is JobSlots × the
    /// sector's class mix, rounded once with a deterministic per-(firm,class)
    /// hash on the fractional part. A BIDDER is one EARNER — a participating
    /// adult — demanding exactly one slot. UNIT DEMAND is load-bearing, not a
    /// convenience: the assignment-LP equivalence the housing build proved
    /// covers unit-demand bidders, and unit demand is what keeps a full door
    /// full (every eviction is one-for-one), which makes a door's entry price
    /// monotone within a re-clear round — the property the termination
    /// argument needs. The previous build bid whole HOUSEHOLDS (a bidder
    /// demanding 1 or 2 slots) and invented an exact-set displacement to
    /// cope; its entry price was NOT monotone (a door's cheapest exact set
    /// can get cheaper as its membership recomposes), so a settled worker
    /// that had passed on a door was never re-triggered when the door became
    /// attainable, and the deterministic re-clear reproduced that state
    /// forever. Measured at the 02a976f laborcanary run (39-seed default
    /// list): red on 8 of 39 seeds, worst envy 28.9% of value against a ~1%
    /// band, 263 blocked-listed workers on the terminating "clean" scan.
    /// Recorded in KNOWN-RED's measured dead ends; a household's earners may
    /// now end up at different firms, which is more real, not a regression.
    ///
    /// FIRMS ARE WORKER COLLECTIVES (design §2): the cap is the firm's own
    /// forecast from its own locally observable facts (an agent forecast —
    /// legitimate), never a citywide statistic. The auction clears T; the
    /// firm pays base = max(0, T − DividendPerEarnerEma) and the dividend
    /// pass tops the rest up. NO income-per-member hiring test exists
    /// anywhere — that Ward rule is the thing this design refuses to encode,
    /// and EconParams.LaborWardMutant exists so the refusal check can prove
    /// it would notice one.
    ///
    /// THE DEFAULT each worker holds: keep the job you have, work outside
    /// the region at the world price of labor net of your own commute to the
    /// border, or your own leisure floor. The mechanism may only IMPROVE on
    /// that default for each individual; nobody is ever assigned below it.
    ///
    /// Convergence machinery is copied from housing: every repair round is a
    /// FULL RE-CLEAR from the reserve (stale prices cannot exist), an
    /// envied-columns scan returns how many workers still wish they had been
    /// shown a door, the loop must end on a clean scan, and Converged /
    /// RepairClean say whether it did rather than letting the result pretend
    /// to be an equilibrium.</summary>
    public sealed class LaborAuction
    {
        // ---- per door ------------------------------------------------------
        public int D;
        public int[] DoorFirm = Array.Empty<int>();
        public int[] DoorClass = Array.Empty<int>();
        public int[] DoorCluster = Array.Empty<int>();
        /// <summary>Earner slots the door OFFERS this solve. Equal to
        /// CapacityFull except under the harness Ward mutant.</summary>
        public int[] Capacity = Array.Empty<int>();
        /// <summary>Physical earner slots (JobSlots × class mix, rounded) —
        /// what the refusal check measures vacancy against, so a mutant that
        /// withholds slots cannot also hide the room it is withholding.</summary>
        public int[] CapacityFull = Array.Empty<int>();
        /// <summary>The firm's own marginal-revenue-per-slot forecast — the
        /// comp ceiling. The slot's product does not depend on the class
        /// filling it in this technology, so a firm's class doors share it.</summary>
        public double[] Cap = Array.Empty<double>();
        /// <summary>Ascending price p = Cap − T posted to sitting members:
        /// a member's comp is Cap[d] − Price[d]. Zero at an undersubscribed
        /// door (comp at cap), positive where workers compete.</summary>
        public double[] Price = Array.Empty<double>();
        /// <summary>Worst (lowest) bid still holding a slot — what a
        /// challenger must beat at a full door. Price ≤ Admitted always.</summary>
        public double[] Admitted = Array.Empty<double>();
        /// <summary>Earner slots currently held (= the door's bid count:
        /// every holder is one earner).</summary>
        public int[] Used = Array.Empty<int>();
        public int[] Excluded = Array.Empty<int>();
        /// <summary>The door's ε, precomputed from the auction bands at the
        /// door's own cap — what EntryPrice adds above Admitted at a full
        /// door, bidder-independent so mechanism, repair scan and checks all
        /// read ONE price vector.</summary>
        private double[] _doorEps = Array.Empty<double>();

        // ---- per worker ----------------------------------------------------
        /// <summary>Bidders this solve: one per participating EARNER. Worker
        /// ids are dense; a household's workers are contiguous from
        /// WorkerOf(hid, 0).</summary>
        public int Wn;
        /// <summary>worker id → household id.</summary>
        public int[] WorkerHh = Array.Empty<int>();
        /// <summary>worker id → door index, −1 = no door (outside or
        /// voluntarily unemployed; Why says which).</summary>
        public int[] Assignment = Array.Empty<int>();
        public enum Outcome : byte { Matched = 0, Outside = 1, Unemployed = 2 }
        /// <summary>The default a doorless worker actually took: Outside when
        /// the border wage net of its own commute beats its own leisure
        /// floor, Unemployed when its own reservation beats everything —
        /// a decision the individual made, not a rate it was sampled from.</summary>
        public Outcome[] Why = Array.Empty<Outcome>();

        // ---- telemetry -----------------------------------------------------
        public int Rounds, Bids, Evictions;
        public int RepairRounds;
        public bool RepairClean, Converged;
        public int BlockedListed, BlockedFull;
        public static long SolveCalls;

        // ---- working state -------------------------------------------------
        private int _C;
        /// <summary>Per household: participating adults; 0 = not in this
        /// market. The household → worker-range map lives beside it.</summary>
        private int[] _earners = Array.Empty<int>();
        private int[] _wStart = Array.Empty<int>();
        private int[] _cls = Array.Empty<int>();          // per worker
        private int[] _home = Array.Empty<int>();         // per worker
        private int[] _stayDoor = Array.Empty<int>();     // per worker
        /// <summary>Per worker: max(outside net wage, own reservation) —
        /// the default the auction must beat, per earner per tick.</summary>
        private double[] _outside = Array.Empty<double>();
        private double[] _outsideNet = Array.Empty<double>();
        private double[] _reservation = Array.Empty<double>();
        private double[] _stayBonus = Array.Empty<double>();
        private int[] _shortStart = Array.Empty<int>();
        private int[] _shortCount = Array.Empty<int>();
        private int[] _shortItems = Array.Empty<int>();
        private int _stride;
        private double[] _minutes = Array.Empty<double>();   // [home*C + doorCluster]
        private int _minutesVersion = -1;
        private double[] _borderMin = Array.Empty<double>();
        private readonly List<int>[] _doorsByClass = { new List<int>(), new List<int>(), new List<int>() };
        private List<(double bid, int wk)>[] _slots = Array.Empty<List<(double, int)>>();
        private readonly List<int> _queue = new List<int>();
        private int[] _doorOfFirmClass = Array.Empty<int>(); // firm*3+class → door, −1

        /// <summary>Adult slots the per-earner state must be sized for —
        /// read off the segment table, never assumed. The engine's flat
        /// household × adult-slot arrays and the solver's worker layout both
        /// derive from this one number.</summary>
        public static int MaxAdults()
        {
            int m = 1;
            foreach (var s in Segment.All) if (s.Adults > m) m = s.Adults;
            return m;
        }

        /// <summary>Solve the market. Reads the world, the access field, cost
        /// minutes and local prices; writes prices and an assignment. Does not
        /// move anybody — the caller applies the outcome, so the solve stays a
        /// pure function of its inputs. `participants[hid]` is the household's
        /// participating adult count this refresh (0 = not in this market);
        /// `priorFirm[hid * maxAdults + slot]` is the firm that employed the
        /// household's slot-th earner last window (−1 none) — what the stay
        /// bonus attaches to, per EARNER. Slot identity is positional across
        /// windows (the participation draw returns a count, not named
        /// adults), which at worst mis-attaches a stay bonus when a
        /// household's participation count changes.</summary>
        public void Solve(WorldState w, AccessState acc, IAccessCosts costs, TradeSystem trade,
                          EconParams p, byte[] participants, int[] priorFirm, int maxAdults)
        {
            SolveCalls++;
            _C = acc.C;
            BuildDoors(w, acc, trade, p, priorFirm, maxAdults);
            BuildWorkers(w, costs, p, participants, priorFirm, maxAdults);
            RunAuction(p);
            // COLUMN GENERATION, exactly as in housing: solve, ask each worker
            // what it wishes it had been shown, put that on its list, solve
            // again — and every round is a FULL RE-CLEAR from the reserve, so
            // a stale price cannot exist between rounds. The loop must end on
            // a CLEAN SCAN; a cap that binds is reported, not papered over.
            // A worker envious only of doors already on its list gets its
            // contest by construction in the next full re-clear — with unit
            // demand that argument holds here exactly as it does in housing
            // (the blocked-listed fixed point the household-atomic build
            // measured cannot form when full doors stay full).
            RepairRounds = 0; RepairClean = false;
            for (int round = 0; round < Math.Max(0, p.LaborAuctionRepairRounds); round++)
            {
                int added = AddEnviedColumns(p);
                if (added == 0) { RepairClean = true; break; }
                RepairRounds++;
                RunAuction(p);
            }
            if (p.LaborAuctionRepairRounds <= 0) RepairClean = true;
            Converged = Converged && RepairClean;
        }

        private static readonly double[][] Mix =
        {
            // Indexed by ZoneKind offset below; same table as AssignWorkplaces
            // and Access.Refresh — the class composition of a sector's slots.
            new[] { 0.7, 0.3, 0.0 },   // Commercial
            new[] { 0.6, 0.4, 0.0 },   // Industrial
            new[] { 0.0, 0.3, 0.7 },   // Office
            new[] { 1.0, 0.0, 0.0 },   // Extractor
        };

        private static double[] MixFor(ZoneKind sector) => sector switch
        {
            ZoneKind.Commercial => Mix[0],
            ZoneKind.Industrial => Mix[1],
            ZoneKind.Office => Mix[2],
            _ => Mix[3],
        };

        /// <summary>Share of a commercial firm's takings that restocking
        /// claims: need = rev × share / price at qty, cost = need × price, so
        /// the local price cancels and the margin share is 1 − Σ shares.</summary>
        private static readonly double BasketShare = SumBasket();
        private static double SumBasket()
        {
            double s = 0;
            foreach (var (_, share) in ResourceCatalog.Basket) s += share;
            return s;
        }

        private void BuildDoors(WorldState w, AccessState acc, TradeSystem trade, EconParams p,
                                int[] priorFirm, int maxAdults)
        {
            int nf = w.Firms.Count;
            if (_doorOfFirmClass.Length != nf * 3) _doorOfFirmClass = new int[nf * 3];
            Array.Fill(_doorOfFirmClass, -1);
            for (int cl = 0; cl < 3; cl++) _doorsByClass[cl].Clear();

            // The mutant's incumbency table, built from the standing per-
            // earner links — who actually works where right now, before this
            // solve moves anybody.
            int[]? incumbent = null;
            if (p.LaborWardMutant)
            {
                incumbent = new int[nf * 3];
                foreach (var h in w.Households)
                {
                    if (h.ExitedTick >= 0) continue;
                    int cl = (int)Segment.All[h.Segment].Labor;
                    int at = h.Id * maxAdults;
                    if (at < 0 || at + maxAdults > priorFirm.Length) continue;
                    for (int s = 0; s < maxAdults; s++)
                    {
                        int of = priorFirm[at + s];
                        if (of < 0 || of >= nf || w.Firms[of].Dead) continue;
                        incumbent[of * 3 + cl]++;
                    }
                }
            }

            var firmIdx = new List<int>();
            var clsIdx = new List<int>();
            var cluIdx = new List<int>();
            var capSlots = new List<int>();
            var capFull = new List<int>();
            var capMoney = new List<double>();

            for (int fi = 0; fi < nf; fi++)
            {
                var f = w.Firms[fi];
                if (f.Dead || f.Parcel < 0) continue;
                var pl = w.Parcels[f.Parcel];
                if ((uint)pl.Cluster >= (uint)_C) continue;
                double cond = Math.Max(0.2, pl.Condition);

                // The firm's OWN forecast of marginal revenue per slot, from
                // its own locally observable facts — an agent forecast,
                // legitimate; never a citywide statistic.
                double mrp;
                switch (f.Sector)
                {
                    case ZoneKind.Extractor:
                        // Marginal revenue at what producers AT ITS CLUSTER
                        // actually netted — the firm's forecast from its own
                        // market's realized sales, not a citywide scalar.
                        mrp = p.ExtractorOutputPerSlot
                              * w.Clusters[pl.Cluster].ResourceSuitability[(int)f.Output]
                              * cond * trade.OriginStat(f.Output, pl.Cluster);
                        break;
                    case ZoneKind.Industrial:
                    {
                        var recipe = ResourceCatalog.RecipeFor(f.Output);
                        if (recipe.Inputs == null) { mrp = 0; break; }
                        // Its own cluster's realized sell and buy prices cap
                        // what its doors can pay.
                        double margin = trade.OriginStat(f.Output, pl.Cluster);
                        foreach (var (res, qty) in recipe.Inputs)
                            margin -= qty * trade.DeliveredStat(res, pl.Cluster);
                        mrp = recipe.OutputPerSlot * p.RecipeOutputScale * cond
                              * p.Quality(pl.Level) / p.Quality(1) * Math.Max(0, margin);
                        break;
                    }
                    case ZoneKind.Office:
                        mrp = p.OfficeOutputPerSlot * p.OfficeOutputPrice
                              * acc.OfficeAgglomMult[pl.Cluster];
                        break;
                    default:
                        // Commercial: labor does not enter the production
                        // function yet (task #20), so the firm forecasts its
                        // margin per slot from its OWN realized takings — the
                        // EMA of RevenueThisTick net of restocking cost, per
                        // slot, floored at 0. Restock cost is rev × basket
                        // share at any local price (the price cancels; see
                        // BasketShare). With per-cluster realized prices
                        // (task #30) that cancellation is no longer exact —
                        // the restocking forecast and the realized delivered
                        // price at this store's cluster can diverge — but
                        // this margin is task #20's fight, untouched here.
                        // Firm-local and simple until #20 makes commercial
                        // labor structurally productive.
                        mrp = Math.Max(0, f.ProfitEma * (1 - BasketShare)) / Math.Max(1, f.JobSlots);
                        break;
                }
                if (mrp <= 0) continue;      // a door with nothing to pay is not a door

                var mix = MixFor(f.Sector);
                for (int cl = 0; cl < 3; cl++)
                {
                    double raw = f.JobSlots * mix[cl];
                    if (raw <= 0) continue;
                    // Rounded once with a deterministic per-(firm,class) hash
                    // on the fractional part, so expected slots match the mix
                    // without a citywide rounding direction.
                    int slots = (int)Math.Floor(raw);
                    double frac = raw - slots;
                    if (frac > 1e-12 && SplitMix64.Hash01((ulong)f.Id * 9176UL + (ulong)cl * 131UL + 71UL) < frac)
                        slots++;
                    if (slots <= 0) continue;

                    int offered = slots;
                    if (incumbent != null)
                        offered = Math.Min(slots, Math.Max(1, incumbent[fi * 3 + cl]));

                    _doorOfFirmClass[fi * 3 + cl] = firmIdx.Count;
                    _doorsByClass[cl].Add(firmIdx.Count);
                    firmIdx.Add(fi); clsIdx.Add(cl); cluIdx.Add(pl.Cluster);
                    capSlots.Add(offered); capFull.Add(slots); capMoney.Add(mrp);
                }
            }

            D = firmIdx.Count;
            if (DoorFirm.Length < D)
            {
                DoorFirm = new int[D]; DoorClass = new int[D]; DoorCluster = new int[D];
                Capacity = new int[D]; CapacityFull = new int[D]; Cap = new double[D];
                Price = new double[D]; Admitted = new double[D]; Used = new int[D];
                Excluded = new int[D]; _doorEps = new double[D];
                _slots = new List<(double, int)>[D];
                for (int d = 0; d < D; d++) _slots[d] = new List<(double, int)>();
            }
            for (int d = 0; d < D; d++)
            {
                DoorFirm[d] = firmIdx[d]; DoorClass[d] = clsIdx[d]; DoorCluster[d] = cluIdx[d];
                Capacity[d] = capSlots[d]; CapacityFull[d] = capFull[d]; Cap[d] = capMoney[d];
                _doorEps[d] = Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(Cap[d]));
                if (_slots[d] == null) _slots[d] = new List<(double, int)>();
            }
        }

        private void BuildWorkers(WorldState w, IAccessCosts costs, EconParams p, byte[] participants,
                                  int[] priorFirm, int maxAdults)
        {
            // Commute minutes, cached per cost version: the auction needs the
            // raw generalized minutes, not the exp weight Access holds.
            if (_minutesVersion != costs.Version || _minutes.Length != _C * _C)
            {
                _minutes = new double[_C * _C];
                for (int i = 0; i < _C; i++)
                    for (int j = 0; j < _C; j++)
                        _minutes[i * _C + j] = costs.Cost(i, j, AccessPurpose.Commute);
                _minutesVersion = costs.Version;
                _borderMin = Array.Empty<double>();
            }
            // Minutes to the nearest trade exit — the commute an outside
            // worker actually pays. No exits means no border: the outside
            // option then cannot exist and only the leisure floor remains.
            if (_borderMin.Length != _C)
            {
                _borderMin = new double[_C];
                for (int c = 0; c < _C; c++)
                {
                    double best = double.PositiveInfinity;
                    foreach (var x in w.Exits)
                        if ((uint)x.Cluster < (uint)_C)
                            best = Math.Min(best, _minutes[c * _C + x.Cluster]);
                    _borderMin[c] = best;
                }
            }

            int nh = w.Households.Count;
            if (_earners.Length < nh)
            {
                _earners = new int[nh]; _wStart = new int[nh];
            }

            // Worker layout first: one bidder per participating earner,
            // contiguous per household, so household → worker lookups are a
            // start index plus a slot.
            int wn = 0;
            for (int i = 0; i < nh; i++)
            {
                var h = w.Households[i];
                _wStart[i] = wn;
                int e = i < participants.Length ? participants[i] : 0;
                if (h.ExitedTick >= 0 || e <= 0 || h.HomeParcel < 0) { _earners[i] = 0; continue; }
                int home = w.Parcels[h.HomeParcel].Cluster;
                if ((uint)home >= (uint)_C) { _earners[i] = 0; continue; }
                _earners[i] = e;
                wn += e;
            }
            Wn = wn;
            if (WorkerHh.Length < wn)
            {
                WorkerHh = new int[wn]; Assignment = new int[wn]; Why = new Outcome[wn];
                _cls = new int[wn]; _home = new int[wn]; _stayDoor = new int[wn];
                _outside = new double[wn]; _outsideNet = new double[wn];
                _reservation = new double[wn]; _stayBonus = new double[wn];
                _shortStart = new int[wn]; _shortCount = new int[wn];
            }

            int K = Math.Max(2, p.LaborShortlist);
            _stride = K + Math.Max(0, p.LaborAuctionRepairRounds) + 1;   // +1: the held door always fits
            if (_shortItems.Length < wn * _stride) _shortItems = new int[wn * _stride];

            // One scratch pair for the whole build — stackalloc never sits
            // inside a loop (CA2014; stackalloc memory is not released until
            // the method returns, so a loop's worth would accumulate).
            Span<double> bestV = K <= 64 ? stackalloc double[64] : new double[K];
            Span<int> bestD = K <= 64 ? stackalloc int[64] : new int[K];

            for (int i = 0; i < nh; i++)
            {
                int e = _earners[i];
                if (e <= 0) continue;
                var h = w.Households[i];
                var seg = Segment.All[h.Segment];
                int cl = (int)seg.Labor;
                int home = w.Parcels[h.HomeParcel].Cluster;

                // The DEFAULT, per individual: the world price of labor
                // outside the region net of this household's own commute to
                // the border, or its own leisure floor — whichever is better.
                // Household-level facts (home, draws) are every earner's
                // facts; the stay bonus alone is per earner, at the EARNER's
                // own current firm.
                double wage = p.Wage((LaborClass)cl);
                double outsideNet = double.IsInfinity(_borderMin[home])
                    ? double.NegativeInfinity
                    : wage * p.OutsideWageMult - p.CommuteCostPerMinute * _borderMin[home];
                double reservation = h.WorkReservationShare * wage;
                // Both the worker's own switching cost and the firm's
                // onboarding cost, folded worker-side as a per-tick flow at
                // the current firm only: the auction decides only the
                // allocation, and under quasilinearity the incidence of a
                // match-specific friction does not change it.
                double stayBonus = (h.MovingCostDraw * p.JobSwitchCostMult + p.OnboardingCostMean)
                                   / Math.Max(1, p.MoveAmortTicks);

                for (int s = 0; s < e; s++)
                {
                    int wk = _wStart[i] + s;
                    WorkerHh[wk] = i;
                    Assignment[wk] = -1;
                    Why[wk] = Outcome.Unemployed;
                    _cls[wk] = cl; _home[wk] = home;
                    _outsideNet[wk] = outsideNet;
                    _reservation[wk] = reservation;
                    _outside[wk] = Math.Max(outsideNet, reservation);
                    _stayBonus[wk] = stayBonus;
                    _stayDoor[wk] = -1;
                    int at = i * maxAdults + s;
                    if ((uint)at < (uint)priorFirm.Length && priorFirm[at] >= 0)
                    {
                        int of = priorFirm[at];
                        if (of * 3 + cl < _doorOfFirmClass.Length)
                            _stayDoor[wk] = _doorOfFirmClass[of * 3 + cl];
                    }

                    // ---- shortlist: the K best doors, price-free ------------
                    // "Price-free" in the transformed space means at p = 0 —
                    // comp at cap — which is exactly the door's standing offer
                    // to an undersubscribed market; the ascent reorders WITHIN
                    // the list. Anything worth less than this worker's own
                    // default is never listed, which is what bounds K honestly.
                    int cnt = 0; double worst = double.PositiveInfinity; int worstAt = 0;
                    var doors = _doorsByClass[cl];
                    for (int q = 0; q < doors.Count; q++)
                    {
                        int d = doors[q];
                        if (Capacity[d] <= 0) continue;
                        double v = Value(wk, d, p);
                        if (v <= _outside[wk]) continue;
                        if (cnt < K)
                        {
                            bestV[cnt] = v; bestD[cnt] = d; cnt++;
                            if (cnt == K)
                            {
                                worst = bestV[0]; worstAt = 0;
                                for (int r = 1; r < K; r++) if (bestV[r] < worst) { worst = bestV[r]; worstAt = r; }
                            }
                        }
                        else if (v > worst)
                        {
                            bestV[worstAt] = v; bestD[worstAt] = d;
                            worst = bestV[0]; worstAt = 0;
                            for (int r = 1; r < K; r++) if (bestV[r] < worst) { worst = bestV[r]; worstAt = r; }
                        }
                    }
                    _shortStart[wk] = wk * _stride;
                    int outp = 0;
                    for (int q = 0; q < cnt; q++) _shortItems[_shortStart[wk] + outp++] = bestD[q];
                    // The job it already holds is always on the list, whatever
                    // it ranks: a sitting member is a bidder for its own slot
                    // and has to be able to renew — the home analog.
                    if (_stayDoor[wk] >= 0 && Capacity[_stayDoor[wk]] > 0)
                    {
                        bool have = false;
                        for (int r = 0; r < outp; r++) if (_shortItems[_shortStart[wk] + r] == _stayDoor[wk]) { have = true; break; }
                        if (!have) _shortItems[_shortStart[wk] + outp++] = _stayDoor[wk];
                    }
                    _shortCount[wk] = outp;
                }
            }
        }

        /// <summary>Worker wk's value for door d in the ascending space:
        /// Ṽ = Cap − commute cost + stay bonus. Surplus at price p is Ṽ − p,
        /// identically T − commute + stay in comp space.</summary>
        private double Value(int wk, int d, EconParams p)
            => Cap[d] - p.CommuteCostPerMinute * _minutes[_home[wk] * _C + DoorCluster[d]]
               + (d == _stayDoor[wk] ? _stayBonus[wk] : 0);

        /// <summary>What a challenger pays to get in here: at a full door the
        /// worst bid still holding a slot PLUS the door's ε, the posted price
        /// otherwise. Unit demand is what licenses the housing form: a full
        /// door stays full (every eviction one-for-one), so Admitted is
        /// monotone within a re-clear round and a worker that passed on a
        /// door can rely on the price it read. The +ε departs from housing's
        /// bare Admitted for a reason housing never faces: labor values carry
        /// no idiosyncratic term, so identical workers produce EXACT bid
        /// ties, and a challenger that reads entry = Admitted, ranks the door
        /// strictly best, bids the tie and loses to the incumbent is handed
        /// an unchanged market — measured (this fix round, bring-up at the
        /// bare-Admitted form): seed 0 burned its whole budget, 796,000 bids
        /// against 48,955 evictions, converged False. Charging ε above the
        /// displaced bid makes the read surplus of a just-failed door lower
        /// than the alternative by a full ε band — an FP-robust strict
        /// preference — and makes every attempted eviction succeed (the bid
        /// against a strictly-best door clears Admitted + ε by construction).
        /// An entry at or above the cap is +∞, not a number: comp cannot go
        /// below zero, so a door whose holders sit at the comp floor cannot
        /// be entered and no feasible deviation to it exists (the CE checks
        /// read the same vector).</summary>
        public double EntryPrice(int d)
        {
            if (Capacity[d] <= 0 || _slots[d].Count < Capacity[d]) return Price[d];
            double entry = Admitted[d] + _doorEps[d];
            return entry >= Cap[d] ? double.PositiveInfinity : entry;
        }

        /// <summary>Clear the market and bid it out from scratch — every call
        /// starts all prices at the reserve (p = 0, comp at cap) and lets the
        /// ascent find the level under live bids, exactly as in housing.</summary>
        private void RunAuction(EconParams p)
        {
            for (int d = 0; d < D; d++)
            {
                _slots[d].Clear(); Used[d] = 0; Excluded[d] = 0;
                Price[d] = 0; Admitted[d] = double.PositiveInfinity;
            }
            for (int wk = 0; wk < Wn; wk++) Assignment[wk] = -1;
            Bids = 0; Evictions = 0; Rounds = 0; Converged = false;
            _queue.Clear();
            for (int wk = 0; wk < Wn; wk++) if (_shortCount[wk] > 0) _queue.Add(wk);

            double epsAbs = Math.Max(1e-9, p.LaborAuctionEpsilon);
            long budget = (long)p.LaborBidBudget * Math.Max(1, _queue.Count);

            int head = 0;
            while (head < _queue.Count)
            {
                if (Bids >= budget) break;                 // ε-equilibrium not reached
                int wk = _queue[head++];
                Bids++;
                if (head > 1_000_000) { _queue.RemoveRange(0, head); head = 0; }

                // Best and runner-up SURPLUS over this worker's shortlist, at
                // the prices standing right now. Rank by what it would
                // ACTUALLY pay: the entry price at a door it would have to
                // win, the posted price at the one it holds — one price
                // vector, the housing lesson.
                double bestSur = double.NegativeInfinity, nextSur = _outside[wk];
                int bestDoor = -1; double bestVal = 0;
                int st = _shortStart[wk], n = _shortCount[wk];
                for (int q = 0; q < n; q++)
                {
                    int d = _shortItems[st + q];
                    if (Capacity[d] <= 0) continue;
                    double val = Value(wk, d, p);
                    double sur = val - (Assignment[wk] == d ? Price[d] : EntryPrice(d));
                    if (sur > bestSur) { nextSur = bestSur; bestSur = sur; bestDoor = d; bestVal = val; }
                    else if (sur > nextSur) nextSur = sur;
                }

                // Nothing on the list beats this worker's own default.
                if (bestDoor < 0 || bestSur <= _outside[wk]) { Assignment[wk] = -1; continue; }

                double eps = Math.Max(epsAbs, p.LaborAuctionEpsilonRel * Math.Abs(bestVal));
                double bid = Math.Min(bestVal - _outside[wk],
                                      bestVal - Math.Max(nextSur, _outside[wk]) + eps);
                // Comp cannot go below zero: a door where workers would pay
                // to work clears at zero comp instead.
                if (bid > Cap[bestDoor]) bid = Cap[bestDoor];

                var slot = _slots[bestDoor];
                if (slot.Count < Capacity[bestDoor])
                {
                    slot.Add((bid, wk)); Used[bestDoor] = slot.Count;
                    Assignment[wk] = bestDoor;
                    SetPrices(bestDoor, band: eps);
                    continue;
                }

                // Full: displace the weakest holder iff strictly outbid —
                // one-for-one, tiebreak to the incumbent, evictee re-queues.
                // With entry = Admitted + ε a door ranked strictly best
                // always yields bid > Admitted (see EntryPrice), so the
                // failed branch below is a backstop, not a path — and a
                // worker it ever did catch re-queues as in housing, reading
                // a door it just failed at a full ε below its alternatives.
                int weakAt = 0; double weak = slot[0].bid;
                for (int q = 1; q < slot.Count; q++) if (slot[q].bid < weak) { weak = slot[q].bid; weakAt = q; }
                if (bid > weak)
                {
                    int loser = slot[weakAt].wk;
                    slot[weakAt] = (bid, wk);
                    Assignment[wk] = bestDoor;
                    Assignment[loser] = -1;
                    Evictions++;
                    SetPrices(bestDoor, weak, eps);
                    _queue.Add(loser);                      // it bids again, elsewhere
                }
                else
                {
                    SetPrices(bestDoor, bid, eps);
                    _queue.Add(wk);
                }
                Rounds++;
            }
            Converged = Bids < budget;

            // A pure build leaves every non-full door posted at the reserve
            // (p = 0, comp at cap): the non-full SetPrices branch never
            // raises a price, and one-for-one displacement keeps a full door
            // full, so a door cannot become non-full at an elevated price. An
            // undersubscribed door therefore posts its full cap by
            // construction; only never-filled Admitted needs settling.
            for (int d = 0; d < D; d++)
                if (_slots[d].Count == 0) Admitted[d] = 0;

            // The default each doorless worker actually takes, named — the
            // difference between leaving for the border and declining to work
            // is the difference the income path and the checks both need.
            for (int wk = 0; wk < Wn; wk++)
            {
                Why[wk] = Assignment[wk] >= 0 ? Outcome.Matched
                       : _outsideNet[wk] >= _reservation[wk] ? Outcome.Outside
                       : Outcome.Unemployed;
            }
        }

        /// <summary>Posted and admitted prices for one door, same contract as
        /// housing's SetPrices: posted is the best rejected bid, floored at
        /// the reserve (0), never above admitted; a non-full door's posted
        /// price never leaves the floor during a pure build.</summary>
        private void SetPrices(int d, double rejected = double.NegativeInfinity, double band = 0)
        {
            var slot = _slots[d];
            double min = double.PositiveInfinity;
            for (int q = 0; q < slot.Count; q++) if (slot[q].bid < min) min = slot[q].bid;
            Admitted[d] = slot.Count > 0 ? min : double.PositiveInfinity;
            if (slot.Count >= Capacity[d] && Capacity[d] > 0)
            {
                double post = Math.Max(0, Math.Max(rejected, Admitted[d] - band));
                Price[d] = Math.Min(post, Admitted[d]);
                if (rejected > double.NegativeInfinity) Excluded[d]++;
            }
            else if (Capacity[d] > 0)
            {
                Price[d] = Math.Max(0, Math.Min(Price[d], Admitted[d]));
            }
        }

        /// <summary>The repair scan: for each worker, the door it would most
        /// like at current prices and is not being shown. Returns how many
        /// workers got a new column; zero means the solve is an equilibrium
        /// over the whole market, not just over what each worker was offered.
        /// The threshold IS the equilibrium band — anything stricter never
        /// runs out of work (housing's measured lesson).</summary>
        private int AddEnviedColumns(EconParams p)
        {
            int added = 0;
            BlockedListed = 0; BlockedFull = 0;
            for (int wk = 0; wk < Wn; wk++)
            {
                int n = _shortCount[wk];
                if (n <= 0) continue;
                int mine = Assignment[wk];
                double myVal = mine >= 0 ? Value(wk, mine, p) : 0;
                double mySur = mine >= 0 ? myVal - Price[mine] : _outside[wk];
                double myEps = Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(myVal));

                double bestGain = 0; int bestDoor = -1;
                var doors = _doorsByClass[_cls[wk]];
                for (int q = 0; q < doors.Count; q++)
                {
                    int d = doors[q];
                    if (d == mine || Capacity[d] <= 0) continue;
                    double val = Value(wk, d, p);
                    double entry = EntryPrice(d);
                    double gain = (val - entry) - mySur;
                    if (gain <= bestGain) continue;
                    double band = myEps + Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(val));
                    if (gain <= band) continue;
                    bestGain = gain; bestDoor = d;
                }
                if (bestDoor < 0) continue;

                int st = _shortStart[wk];
                bool have = false;
                for (int q = 0; q < n; q++) if (_shortItems[st + q] == bestDoor) { have = true; break; }
                if (have) { BlockedListed++; continue; }   // full re-clear re-decides it next round
                if (n >= _stride) { BlockedFull++; continue; }
                _shortItems[st + n] = bestDoor; _shortCount[wk] = n + 1;
                added++;
            }
            return added;
        }

        // ---- what the caller and the checks read ---------------------------
        /// <summary>Total comp per earner per tick a sitting member of door d
        /// receives — the T the market cleared.</summary>
        public double CompMember(int d) => Cap[d] - Price[d];
        public bool ActiveWorker(int hid)
            => (uint)hid < (uint)_earners.Length && _earners[hid] > 0;
        public int EarnersOf(int hid) => (uint)hid < (uint)_earners.Length ? _earners[hid] : 0;
        /// <summary>Worker id of household hid's slot-th participating earner
        /// (slot &lt; EarnersOf(hid)).</summary>
        public int WorkerOf(int hid, int slot) => _wStart[hid] + slot;
        public int ClassOfWorker(int wk) => _cls[wk];
        /// <summary>Worker's value at a door, exposed so a checker re-derives
        /// the equilibrium conditions from the numbers the solve used.</summary>
        public double ValueOf(int wk, int d, EconParams p) => Value(wk, d, p);
        public double OutsideOf(int wk) => _outside[wk];
        public double OutsideNetOf(int wk) => _outsideNet[wk];
        public double ReservationOf(int wk) => _reservation[wk];
    }
}
