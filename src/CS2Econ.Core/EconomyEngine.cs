using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Facade + tick orchestration (PLAN §2). Fast tick: payments,
    /// production, allocation, construction progress, insolvency. Refresh tick
    /// (dirty/staggered): access matrices → balancing → residuals → assessments.
    /// Slow: migration scalars, trade EMAs, calibration. No citywide synchronized
    /// event exists anywhere (design §3): per-entity phases come from hashed ids.</summary>
    public sealed class EconomyEngine
    {
        public readonly WorldState W;
        public readonly IAccessCosts Costs;
        public readonly EconParams P;
        public readonly FeatureFlags Flags;
        public readonly AccessState Access = new AccessState();
        public readonly TradeSystem Trade = new TradeSystem();
        public readonly ResidualDemand Residuals = new ResidualDemand();
        public readonly ConstructionSystem Construction = new ConstructionSystem();
        public AllocationSystem Allocation;

        public double[] SegmentPresence = new double[Segment.Count];
        public double[] MeanExpectedIncome = new double[Segment.Count];
        public double[] SeekersEma = new double[Segment.Count];
        public double[] AvgRentBySeg = new double[Segment.Count];
        /// <summary>[0=Low,1=High] EMA of residential units actually freed per
        /// tick (Allocation.Vacate) — the measured churn flow the migration
        /// absorption budget reads instead of an assumed turnover constant.</summary>
        public double[] TurnoverEma = new double[2];
        public int ShelterOccupied;
        public int ShelterCapacity;

        // Telemetry (per tick)
        public double LandRevenueThisTick, IncomeTaxThisTick, ServiceCostThisTick;
        public double[] LandRevenueByCluster = Array.Empty<double>();
        /// <summary>(tick, household, reason): reason 0 = voluntary cost-driven
        /// relocation within the city, 1 = insolvency-pipeline emigration.</summary>
        public readonly List<(long tick, int household, int reason)> DisplacementExits
            = new List<(long, int, int)>();
        /// <summary>Tick Tier C last STARTED levying (−1 while not levying) —
        /// anchors the one-time go-live ramp in RerateAndRelocation.</summary>
        private long _levyingSince = -1;
        public Migration.Flows LastFlows;

        private readonly List<int> _unhoused = new List<int>();
        private readonly List<int> _aliveScratch = new List<int>();

        /// <summary>True when Tier C actually charges money. Shadow mode and
        /// TierC-off must be behaviorally inert: no charged assessments, no
        /// assessment-driven displacement, no S-underpayment condition decay.</summary>
        public bool Levying => Flags.TierC_LandAccounting && !Flags.ShadowAccountingOnly;

        public EconomyEngine(WorldState w, IAccessCosts costs, EconParams p, FeatureFlags flags)
        {
            W = w; Costs = costs; P = p; Flags = flags;
            Allocation = new AllocationSystem(costs.ClusterCount);
            Trade.Bind(w, costs);
            Trade.SetParams(p);
            LandRevenueByCluster = new double[costs.ClusterCount];
        }

        public void Step()
        {
            // Zeroed every tick, set inside the refresh-tick market solve: a
            // per-tick consumer (the boombust margin) then counts each decline
            // exit exactly once instead of RefreshInterval times.
            DeclineExitsThisTick = 0;
            bool refresh = W.Tick % P.RefreshInterval == 0;
            if (refresh) RefreshTick();

            Array.Clear(LandRevenueByCluster, 0, LandRevenueByCluster.Length);
            LandRevenueThisTick = IncomeTaxThisTick = ServiceCostThisTick = 0;
            foreach (var pl in W.Parcels) { pl.PaidTickS = 0; pl.OwedTickS = 0; }

            AssessmentSlice();
            IncomeAndTaxes();
            ConsumptionFlows();
            HousingPayments();
            ProductionAndTrade();
            FirmLifecycle();
            AllocationAndInsolvency();
            MigrationStep();
            RerateAndRelocation();
            Construction.Step(W, Access, Trade, Residuals, SegmentPresence, P, Flags);
            Construction.ObserveCompletions(W, P);
            Leveling.Step(W, Access, Trade, P, Flags);
            ConditionDecay();
            ServiceCosts();
            Mortality();
            Trade.EndTick(P);
            W.Tick++;
        }

        // ------------------------------------------------------------------
        private void RefreshTick()
        {
            W.RebuildIndices();
            Access.Refresh(W, Costs, P, Flags);
            Trade.Refresh(P);

            Array.Clear(SegmentPresence, 0, SegmentPresence.Length);
            foreach (var h in W.Households)
                if (h.ExitedTick < 0) SegmentPresence[h.Segment]++;

            // Employment materialization: fixed per-household draw against the
            // balanced rate — persistent identity, smooth response to rate moves.
            // NOTE a Markov chain (separation/finding hazards with the balanced
            // rate as stationary point) was tried here and REVERTED: seeded at
            // the cold-start rate it converged at only ~0.3/epoch, so for the
            // first ~180 ticks employment lagged far below the warming-up
            // balanced rate, firms ran unstaffed (industrial revenue/firm 19
            // vs 171 at t=50, A/B), and every seeded industrial firm died —
            // while the churn the chain was meant to stop turned out to be
            // failed ARRIVALS (see churnprobe), not spell-bankrupted tenants.
            // The i.i.d. epoch draw tracks the current rate immediately, which
            // is what a young or recovering labor market needs.
            if (Flags.LaborAuction)
            {
                // Labor-auction path: the participation decision stays exactly
                // as drawn (who is in the labor force this epoch); employment
                // AMONG participants comes from the auction.
                MaterializeParticipation();
                SolveLaborMarket();
            }
            else
            {
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                if (h.HomeParcel < 0 || seg.Participation <= 0 || seg.Adults <= 0)
                { h.Employed = false; h.Earners = 0; continue; }
                int c = W.Parcels[h.HomeParcel].Cluster;
                double rate = Access.EmploymentRate[(int)seg.Labor][c] * seg.Participation;
                // Epoch-hashed draw: employment persists ~60 ticks, then the job
                // search re-rolls — a bad draw is a spell, not a life sentence.
                // The epoch boundary is offset per household (hashed), so there is
                // no citywide re-roll tick (design §3; scrutiny finding #21).
                long offset = (long)(SplitMix64.Hash((ulong)h.Id * 13UL) % 60UL);
                ulong epoch = (ulong)((W.Tick + offset) / 60);
                // Each ADULT draws independently, so a two-adult household can
                // be fully employed, half employed, or out of work — the earner
                // count is the dispersion the segment distribution aggregates.
                int earners = 0;
                for (int a = 0; a < seg.Adults; a++)
                    if (SplitMix64.Hash01((ulong)h.Id * 7919UL + epoch * 104729UL + (ulong)a * 31UL + 3) < rate)
                        earners++;
                h.Earners = (byte)earners;
                h.Employed = earners > 0;
                // Unemployment spell length: what the benefit's expiry reads.
                h.UnemployedTicks = earners > 0 ? 0 : h.UnemployedTicks + P.RefreshInterval;
                // Job level: stable per household (a career, not a lottery each
                // epoch), drawn from the segment's job-level distribution.
                int levels = Income.JobLevels(seg, P, lw, lwage);
                double u = SplitMix64.Hash01((ulong)h.Id * 6151UL + 17UL);
                int lvl = levels - 1;
                double cumL = 0;
                for (int l = levels - 1; l >= 0; l--)
                {
                    cumL += lw[l];
                    if (u <= cumL) { lvl = l; break; }
                }
                h.JobLevel = (byte)lvl;
            }

            AssignWorkplaces();
            }
            if (Flags.StoreLevelSpending) ChooseShops();
            if (Flags.HousingAuction) SolveHousingMarket();

            // Seekers = unhoused + sheltered now, EMA-smoothed (expected near-term demand).
            var seekersNow = new double[Segment.Count];
            foreach (var h in W.Households)
                if (h.ExitedTick < 0 && h.HomeParcel < 0) seekersNow[h.Segment]++;
            for (int s = 0; s < Segment.Count; s++)
            {
                // DESIRED (pre-cap) inflow, not realized: arrivals the
                // absorption budget deferred still queue as construction
                // pressure — otherwise no-vacancy → no-arrivals → no-seekers
                // → no-construction deadlocks a young city.
                seekersNow[s] += LastFlows.DesiredBySegment != null ? LastFlows.DesiredBySegment[s] * P.RefreshInterval : 0;
                SeekersEma[s] = MathUtil.Ema(SeekersEma[s], seekersNow[s], 0.15);
            }

            Residuals.Refresh(W, Access, Trade, SeekersEma, SegmentPresence, P);

            // Segment average charged rents (migration's rent term).
            var sums = new double[Segment.Count]; var counts = new double[Segment.Count];
            foreach (var h in W.Households)
                if (h.ExitedTick < 0 && h.HomeParcel >= 0) { sums[h.Segment] += h.ChargedAssessment; counts[h.Segment]++; }
            for (int s = 0; s < Segment.Count; s++)
                AvgRentBySeg[s] = counts[s] > 0 ? sums[s] / counts[s] : 0;

            int pop = 0; foreach (var h in W.Households) if (h.ExitedTick < 0) pop++;
            ShelterCapacity = (int)(P.ShelterCapacityShare * Math.Max(200, pop));

            // City-mean expected income per segment, population-weighted so empty
            // map corners cannot dilute it (scrutiny finding #15) — what a
            // newcomer can expect to earn once housed.
            for (int s2 = 0; s2 < Segment.Count; s2++)
            {
                double sum = 0, wsum = 0;
                for (int c = 0; c < Access.C; c++)
                {
                    double wgt = 1 + W.HouseholdCountByCluster[c];
                    sum += Access.ExpectedIncome(s2, c, P) * wgt;
                    wsum += wgt;
                }
                MeanExpectedIncome[s2] = wsum > 0 ? sum / wsum : 0;
            }
        }

        private void AssessmentSlice()
        {
            // Staggered by parcel id — no citywide reassessment event exists (§3).
            int slice = (int)(W.Tick % P.AssessSlices);
            foreach (var pl in W.Parcels)
            {
                if (pl.Id % P.AssessSlices != slice) continue;
                if (Flags.TierC_LandAccounting)
                    LandAccounting.Assess(W, Access, Trade, pl, SegmentPresence, P);
                else { pl.AssessedLR = 0; pl.Wedge = 0; pl.CurrentResidual = 0; pl.TargetLevel = pl.Level; pl.TargetUse = pl.Use; pl.TargetIsScrape = false; }
            }
        }

        /// <summary>Place each employed household at a SPECIFIC firm. The
        /// employment RATE is a market outcome (the IPF balance of workers
        /// against slots); which employer you end up at is a decision the
        /// household makes, weighted by its own commute from its own home and
        /// bounded by the slots a firm actually has for its labor class.
        ///
        /// This link is what a worker collective needs — a firm's surplus is
        /// payable to its own members only — and it is read straight off the
        /// game in-mod (Game.Citizens.Worker.m_Workplace), which the adapter
        /// was already reading and throwing away.</summary>
        public readonly HousingAuction Auction = new HousingAuction();

        /// <summary>The labor assignment market (Flags.LaborAuction). See
        /// LaborAuction for the mechanism; this class only feeds it the
        /// participation draw and applies its outcome.</summary>
        public readonly LaborAuction Labor = new LaborAuction();
        /// <summary>Per household: participating adults this refresh (labor-
        /// auction path). 0 both for a drawn-out participant and for a
        /// household the participation guard excluded; _inMarket separates
        /// them because only the former accrues an unemployment spell.</summary>
        private byte[] _participants = Array.Empty<byte>();
        private bool[] _inMarket = Array.Empty<bool>();
        /// <summary>Adult slots per household the per-earner state is sized
        /// for — the segment table's maximum, derived, never assumed.</summary>
        private readonly int _maxAdults = LaborAuction.MaxAdults();
        /// <summary>Per EARNER [hid × _maxAdults + slot]: the firm employing
        /// that earner this window, −1 for outside/none — a household's
        /// earners may work at different firms. Written only by the labor
        /// clear; the income pass credits by THIS link while firms bill by
        /// their Members list, so a firm that entered a dead firm's parcel
        /// mid-window cannot be credited for members it never admitted.
        /// Also the auction's prior-employer state: the stay bonus attaches
        /// to the EARNER's own current firm.</summary>
        private int[] _earnerFirm = Array.Empty<int>();
        /// <summary>Per EARNER: base pay per tick (members: the door's base
        /// comp; outside workers: the own outside net wage; else 0).</summary>
        private double[] _earnerBase = Array.Empty<double>();
        /// <summary>Per (firm × 3 + class): the base comp the labor clear set
        /// at that firm's class door this window — what the firm side of the
        /// payroll pass bills per member ENTRY, independently of the
        /// households' earner links.</summary>
        private double[] _firmClassBase = Array.Empty<double>();
        /// <summary>Per tick on the labor path: Σ per-firm |own-members bill −
        /// the credits its members booked| — two independently maintained
        /// records (the firm's Members list vs the households' workplace
        /// links) that must agree exactly. Read by the payroll check.</summary>
        public double LaborPayrollGapThisTick;
        public double LaborFirmDebitsThisTick, LaborOutsideCreditsThisTick;

        /// <summary>The participation half of the flag-off employment draw,
        /// alone: the same per-adult epoch hash compared against the
        /// segment's participation instead of participation × employment
        /// rate, so an adult employed under the flag-off draw is always a
        /// participant here — the labor-force decision is unchanged, only
        /// employment among participants moves to the market. The career
        /// (JobLevel) draw is identical to the flag-off path.</summary>
        private void MaterializeParticipation()
        {
            int nh = W.Households.Count;
            if (_participants.Length < nh)
            {
                int oldLen = _earnerFirm.Length;
                Array.Resize(ref _participants, nh);
                Array.Resize(ref _inMarket, nh);
                Array.Resize(ref _earnerFirm, nh * _maxAdults);
                Array.Resize(ref _earnerBase, nh * _maxAdults);
                // −1 = no employer; a zero default would read as firm 0.
                for (int i = oldLen; i < _earnerFirm.Length; i++) _earnerFirm[i] = -1;
            }
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                if (h.HomeParcel < 0 || seg.Participation <= 0 || seg.Adults <= 0)
                {
                    h.Employed = false; h.Earners = 0; h.WorkplaceParcel = -1;
                    h.OutsideWorker = false; h.BaseComp = 0;
                    _participants[h.Id] = 0; _inMarket[h.Id] = false;
                    for (int s = 0; s < _maxAdults; s++)
                    { _earnerFirm[h.Id * _maxAdults + s] = -1; _earnerBase[h.Id * _maxAdults + s] = 0; }
                    continue;
                }
                long offset = (long)(SplitMix64.Hash((ulong)h.Id * 13UL) % 60UL);
                ulong epoch = (ulong)((W.Tick + offset) / 60);
                int part = 0;
                for (int a = 0; a < seg.Adults; a++)
                    if (SplitMix64.Hash01((ulong)h.Id * 7919UL + epoch * 104729UL + (ulong)a * 31UL + 3) < seg.Participation)
                        part++;
                _participants[h.Id] = (byte)part; _inMarket[h.Id] = true;
                int levels = Income.JobLevels(seg, P, lw, lwage);
                double u = SplitMix64.Hash01((ulong)h.Id * 6151UL + 17UL);
                int lvl = levels - 1;
                double cumL = 0;
                for (int l = levels - 1; l >= 0; l--)
                {
                    cumL += lw[l];
                    if (u <= cumL) { lvl = l; break; }
                }
                h.JobLevel = (byte)lvl;
            }
        }

        /// <summary>Clear the labor market and make the outcome true on the
        /// ground: membership, workplace links, earner counts, base comp —
        /// and store the realized rates Access serves next refresh in place
        /// of the Sinkhorn model (an observation, not a model; one-refresh
        /// lag).</summary>
        private void SolveLaborMarket()
        {
            Labor.Solve(W, Access, Costs, Trade, P, _participants, _earnerFirm, _maxAdults);
            foreach (var f in W.Firms) f.Members.Clear();

            // The base comp each firm's class door cleared at, snapshotted
            // per (firm, class): the firm side of the payroll pass bills per
            // member ENTRY from this record while households credit by their
            // earner links — two records that must agree exactly.
            if (_firmClassBase.Length < W.Firms.Count * 3)
                Array.Resize(ref _firmClassBase, W.Firms.Count * 3);
            Array.Clear(_firmClassBase, 0, _firmClassBase.Length);
            for (int d = 0; d < Labor.D; d++)
            {
                var f = W.Firms[Labor.DoorFirm[d]];
                _firmClassBase[f.Id * 3 + Labor.DoorClass[d]]
                    = Math.Max(0, Labor.CompMember(d) - f.DividendPerEarnerEma);
            }

            int C = Access.C;
            var supply = new double[3][]; var employed = new double[3][];
            for (int cl = 0; cl < 3; cl++) { supply[cl] = new double[C]; employed[cl] = new double[C]; }

            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)_inMarket.Length || !_inMarket[h.Id]) continue;
                int e = _participants[h.Id];
                int at = h.Id * _maxAdults;
                if (e == 0)
                {
                    // Out of the labor force this epoch: the same outcome the
                    // flag-off draw gives an adult whose hash misses, spell
                    // clock included.
                    h.Earners = 0; h.Employed = false; h.WorkplaceParcel = -1;
                    h.OutsideWorker = false; h.BaseComp = 0;
                    for (int s = 0; s < _maxAdults; s++) { _earnerFirm[at + s] = -1; _earnerBase[at + s] = 0; }
                    h.UnemployedTicks += P.RefreshInterval;
                    continue;
                }
                int cls = (int)Segment.All[h.Segment].Labor;
                int home = W.Parcels[h.HomeParcel].Cluster;
                supply[cls][home] += e;

                // PER EARNER: each participating adult carries its own
                // outcome; a household's earners may work at different firms.
                // The auction's own earner count guards the worker lookup —
                // a household the solve excluded (defensive guards) has no
                // workers to read and lands on the unemployed path below.
                int ae = Labor.EarnersOf(h.Id);
                int matchedE = 0, outsideE = 0, primaryFirm = -1;
                double baseSum = 0;
                for (int s = 0; s < _maxAdults; s++)
                {
                    _earnerFirm[at + s] = -1; _earnerBase[at + s] = 0;
                    if (s >= e || s >= ae) continue;
                    int wk = Labor.WorkerOf(h.Id, s);
                    int d = Labor.Assignment[wk];
                    if (d >= 0)
                    {
                        var f = W.Firms[Labor.DoorFirm[d]];
                        _earnerFirm[at + s] = f.Id;
                        // Total comp T is what the market cleared; the base/
                        // dividend split is bookkeeping against the firm's
                        // own dividend forecast.
                        _earnerBase[at + s] = _firmClassBase[f.Id * 3 + cls];
                        f.Members.Add(h.Id);        // one entry PER EARNER
                        matchedE++;
                        if (primaryFirm < 0) primaryFirm = f.Id;
                    }
                    else if (Labor.Why[wk] == LaborAuction.Outcome.Outside)
                    {
                        _earnerBase[at + s] = Math.Max(0, Labor.OutsideNetOf(wk));
                        outsideE++;
                    }
                    // else: voluntarily unemployed earner — its own
                    // reservation beat every door and the border.
                    baseSum += _earnerBase[at + s];
                }

                int working = matchedE + outsideE;
                h.Earners = (byte)working;
                h.Employed = working > 0;
                // The household flag means "works outside ONLY": a household
                // with any in-region member has a real workplace.
                h.OutsideWorker = matchedE == 0 && outsideE > 0;
                // PRIMARY workplace = the first matched earner's firm — an
                // approximation kept only for the shopping-origin and commute
                // consumers of WorkplaceParcel; payroll runs per earner.
                h.WorkplaceParcel = primaryFirm >= 0 ? W.Firms[primaryFirm].Parcel : -1;
                h.BaseComp = baseSum;
                if (working > 0) { h.UnemployedTicks = 0; employed[cls][home] += working; }
                else h.UnemployedTicks += P.RefreshInterval;
            }

            // Realized rates, per class: employment at the home cluster
            // (outside work counts — those earners are employed), fill at the
            // job cluster against PHYSICAL slots, so a mechanism that
            // withheld slots would read as low fill rather than hiding them.
            var empRate = new double[3][]; var fillRate = new double[3][]; var resid = new double[3][];
            var fillNum = new double[3][]; var fillDen = new double[3][];
            for (int cl = 0; cl < 3; cl++)
            {
                empRate[cl] = new double[C]; fillRate[cl] = new double[C]; resid[cl] = new double[C];
                fillNum[cl] = new double[C]; fillDen[cl] = new double[C];
            }
            for (int d = 0; d < Labor.D; d++)
            {
                fillNum[Labor.DoorClass[d]][Labor.DoorCluster[d]] += Labor.Used[d];
                fillDen[Labor.DoorClass[d]][Labor.DoorCluster[d]] += Labor.CapacityFull[d];
            }
            for (int cl = 0; cl < 3; cl++)
                for (int c = 0; c < C; c++)
                {
                    empRate[cl][c] = supply[cl][c] > 1e-9
                        ? MathUtil.Clamp(employed[cl][c] / supply[cl][c], 0, 1) : 0;
                    fillRate[cl][c] = fillDen[cl][c] > 1e-9
                        ? MathUtil.Clamp(fillNum[cl][c] / fillDen[cl][c], 0, 1) : 0;
                    resid[cl][c] = Math.Max(0, fillDen[cl][c] - fillNum[cl][c]);
                }
            Access.ObserveLaborOutcome(empRate, fillRate, resid);
        }

        /// <summary>Clear the housing market as one assignment problem, then
        /// make the assignment true on the ground.
        ///
        /// The auction hands back a submarket per household. Turning that into
        /// parcels has one rule that matters: a household assigned to the
        /// submarket it ALREADY lives in does not move. Submarket granularity is
        /// (density, cluster, level), and units inside one are interchangeable,
        /// so re-winning your own submarket is renewing your own lease — not a
        /// move to an identical flat next door. Without that the market would
        /// report thousands of relocations a refresh that no household actually
        /// experiences, and every one of them would wash through the vacancy
        /// kernel and the turnover EMA.</summary>
        private void SolveHousingMarket()
        {
            Access.Auction = Auction;
            Auction.Solve(W, Access, P);

            // Who has to leave the unit they are in: assigned somewhere else, or
            // assigned nowhere. Vacate first, all of them, so the units they free
            // are available to the households moving in.
            Allocation.RebuildVacancies(W);
            var movers = new List<int>();
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if ((uint)h.Id >= (uint)Auction.Assignment.Length) continue;
                int want = Auction.Assignment[h.Id];
                var pl = W.Parcels[h.HomeParcel];
                int have = Auction.SubOf(pl.Cluster, pl.Use, pl.Level);
                if (want == have && have >= 0) continue;         // renewed in place
                Allocation.Vacate(W, h);
                if (want >= 0) movers.Add(h.Id);
            }

            // DEPARTURE, as an individual decision. A household the auction
            // marked Declined found something it could have had and judged that
            // none of it beat its OWN reservation. That is somebody choosing to
            // leave, and it is the whole of out-migration on this path — no
            // citywide elasticity, no lagged signal, no Poisson draw. Being
            // Outbid is the other thing entirely and stays in the queue.
            //
            // It waits first. Its patience is its own moving cost: the household
            // that would find moving expensive puts up with more before going,
            // which is the same draw that keeps it in its home in the auction.
            DeclineExitsThisTick = 0;
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)Auction.Why.Length) continue;
                if (Auction.Why[h.Id] != HousingAuction.Outcome.Declined)
                { h.DeclineTicks = 0; continue; }
                h.DeclineTicks += P.RefreshInterval;
                double patience = P.DeclinePatienceTicks
                                  * (0.5 + h.MovingCostDraw / Math.Max(1e-6, P.MovingCostMean));
                if (h.DeclineTicks < patience) continue;
                if (h.HomeParcel >= 0) Allocation.Vacate(W, h);
                if (h.Stage == InsolvencyStage.Sheltered)
                    ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                // It takes its money with it, back across the border.
                if (h.Money > 0) W.Ledger.Transfer(Account.Households, Account.OutsideWorld, h.Money);
                h.Money = 0;
                h.ExitedTick = W.Tick;
                DisplacementExits.Add((W.Tick, h.Id, 1));
                DeclineExitsThisTick++;
            }
            // Unhoused households the auction placed also move in.
            foreach (var h in W.Households)
                if (h.ExitedTick < 0 && h.HomeParcel < 0
                    && (uint)h.Id < (uint)Auction.Assignment.Length
                    && Auction.Assignment[h.Id] >= 0)
                    movers.Add(h.Id);

            Allocation.RebuildVacancies(W);
            foreach (int hid in movers)
            {
                var h = W.Households[hid];
                if (h.HomeParcel >= 0) continue;
                int sub = Auction.Assignment[hid];
                int target = FirstVacantIn(sub);
                // The auction sized every submarket by its lettable units, so a
                // won slot has a unit behind it. It can still come up empty when
                // stock changed between the solve and here (a completion, a
                // scrape); the household simply stays unhoused this refresh.
                if (target >= 0)
                {
                    // Housing somebody who was in shelter must free their shelter
                    // place. The other move-in site does this; this one did not,
                    // so on the auction path ShelterOccupied only ever went up.
                    // Once it passed capacity, Allocation's "is there a shelter
                    // bed" test could never fire again and every subsequent
                    // penniless household was expelled from the city by a stale
                    // counter (measured: a gap of 30 that never closed).
                    bool wasSheltered = h.Stage == InsolvencyStage.Sheltered;
                    Allocation.MoveIn(W, h, target, P);
                    if (wasSheltered) ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                }
                else Auction.Assignment[hid] = -1;
            }
        }

        private int FirstVacantIn(int sub)
        {
            int kc = HousingAuction.KcOf(sub), lvl = HousingAuction.LevelOf(sub);
            int k = kc / Access.C, c = kc - k * Access.C;
            var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
            foreach (int pi in Allocation.VacantByCluster[c])
            {
                var pl = W.Parcels[pi];
                if (pl.Use == kind && pl.Level == lvl && !pl.Warehousing && pl.Vacant > 0) return pi;
            }
            return -1;
        }

        /// <summary>Every household picks the shop it uses. It ranks the real
        /// commercial firms on the map by how easy each is to reach from its own
        /// home and how much shop is there (slots × condition × level quality),
        /// against the out-of-town option, and adds its own permanent taste for
        /// each specific store; it goes to the best one. The choice is remade at
        /// refresh cadence and kept in between, because people have a usual shop
        /// and go back to it until something changes.
        ///
        /// This replaced a pool: every household's spending was summed citywide
        /// and handed back out pro-rata to (slots × cluster capture strength), so
        /// a household in one corner of the map funded a shop in the other and
        /// every firm of a given size at a given cluster booked identical
        /// takings — measured revenue-per-slot varied by 1.8e-10 across the whole
        /// commercial sector, i.e. not at all. A shop's takings are now the
        /// people who actually walk into it, so two identical shops in the same
        /// cluster can have different fortunes, and a shop with no catchment
        /// dies.
        ///
        /// The Layer-3 capture field (CaptureIncumbentPerMass) stays as it was:
        /// that is the expectation a PROSPECTIVE entrant forms about a location
        /// before it exists, which is legitimately an aggregate about a firm that
        /// has no customers yet. Realized takings are these households.</summary>
        private void ChooseShops()
        {
            // Candidate shops, with the mass each offers a shopper.
            var idx = new List<int>(); var mass = new List<double>(); var cl = new List<int>();
            for (int i = 0; i < W.Firms.Count; i++)
            {
                var f = W.Firms[i];
                if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                var pl = W.Parcels[f.Parcel];
                if ((uint)pl.Cluster >= (uint)Access.C) continue;
                double m = f.JobSlots * Math.Max(0.2, pl.Condition) * P.Quality(pl.Level);
                if (m <= 1e-9) continue;
                idx.Add(i); mass.Add(m); cl.Add(pl.Cluster);
            }
            // The out-of-town option, on the same scale: a real alternative the
            // household can always take, which is what makes leakage a CHOICE
            // rather than a fixed fraction skimmed off the top.
            double outU = Math.Log(Math.Max(1e-9,
                Math.Exp(-P.ThetaShopping * P.OutsideShopMinutes) * P.OutsideShopMass));

            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) { h.ShopFirm = -1; continue; }
                // Shopping origin: home, or the workplace for a household that
                // has not found housing yet. An unhoused household still eats,
                // and it is in the city — sending it home-less straight to the
                // outside option leaked its whole basket out of town. That was
                // 19% of the population here, and it took the commercial sector
                // from 107 firms to 70 with 61% of commercial parcels standing
                // empty (measured). Where there is neither home nor job, it
                // shops on size alone: it is somewhere, just nowhere we track.
                int origin = h.HomeParcel >= 0 ? W.Parcels[h.HomeParcel].Cluster
                           : h.WorkplaceParcel >= 0 ? W.Parcels[h.WorkplaceParcel].Cluster : -1;
                if ((uint)origin >= (uint)Access.C) origin = -1;

                double bestU = outU + Gumbel((ulong)h.Id * 2246822519UL + 7919UL);
                int best = -1;
                for (int j = 0; j < idx.Count; j++)
                {
                    double w = (origin >= 0 ? Access.WShop[origin, cl[j]] : 1.0) * mass[j];
                    if (w <= 1e-12) continue;
                    double u = Math.Log(w)
                               + Gumbel((ulong)h.Id * 2246822519UL + (ulong)W.Firms[idx[j]].Id * 40503UL + 13UL);
                    // You keep going to your usual shop unless another one is
                    // clearly better. Without this every household re-shops from
                    // scratch each refresh, so one new large store can take a
                    // whole catchment at once and the incumbents it starves take
                    // the goods market down with them — measured as extractors
                    // mis-siting on 2 of 6 seeds through the price signal they
                    // read at entry.
                    if (idx[j] == h.ShopFirm) u += P.ShopLoyalty;
                    if (u > bestU) { bestU = u; best = idx[j]; }
                }
                h.ShopFirm = best;
            }
        }

        /// <summary>Gumbel(0,1) from a stable hash — the same inverse-CDF trick
        /// the location choice uses, so a household's taste for a specific place
        /// or shop never re-rolls.</summary>
        private static double Gumbel(ulong key)
        {
            double e = SplitMix64.Hash01(key);
            return -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
        }

        private void AssignWorkplaces()
        {
            foreach (var f in W.Firms) f.Members.Clear();
            // Remaining slots per firm, by labor class.
            var freeByFirm = new double[W.Firms.Count][];
            var byCluster = new List<int>[Access.C];
            for (int i = 0; i < W.Firms.Count; i++)
            {
                var f = W.Firms[i];
                if (f.Dead || f.Parcel < 0) continue;
                double[] mix = f.Sector switch
                {
                    ZoneKind.Commercial => new[] { 0.7, 0.3, 0.0 },
                    ZoneKind.Industrial => new[] { 0.6, 0.4, 0.0 },
                    ZoneKind.Office => new[] { 0.0, 0.3, 0.7 },
                    _ => new[] { 1.0, 0.0, 0.0 },
                };
                freeByFirm[i] = new[] { f.JobSlots * mix[0], f.JobSlots * mix[1], f.JobSlots * mix[2] };
                int fc = W.Parcels[f.Parcel].Cluster;
                (byCluster[fc] ??= new List<int>()).Add(i);
            }

            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                if (h.Earners == 0 || h.HomeParcel < 0) { h.WorkplaceParcel = -1; continue; }
                int cl = (int)Segment.All[h.Segment].Labor;
                int home = W.Parcels[h.HomeParcel].Cluster;

                // Keep a job you already hold if it still has room for you —
                // people do not re-shop their employer every refresh.
                if (h.WorkplaceParcel >= 0)
                {
                    int held = W.Parcels[h.WorkplaceParcel].OccupantFirm;
                    if (held >= 0 && !W.Firms[held].Dead && freeByFirm[held] != null
                        && freeByFirm[held][cl] >= h.Earners)
                    {
                        freeByFirm[held][cl] -= h.Earners;
                        W.Firms[held].Members.Add(h.Id);
                        continue;
                    }
                    h.WorkplaceParcel = -1;
                }

                // Otherwise search: softmax over the household's OWN commute
                // weight across clusters that still have room for its class.
                double best = -1; int bestFirm = -1;
                double total = 0;
                for (int c = 0; c < Access.C; c++)
                {
                    var lst = byCluster[c];
                    if (lst == null) continue;
                    double w = Access.WCommute[home, c];
                    if (w <= 1e-9) continue;
                    foreach (int fi in lst)
                    {
                        if (freeByFirm[fi] == null || freeByFirm[fi][cl] < h.Earners) continue;
                        // Reservoir-style weighted pick, deterministic per household.
                        total += w;
                        double u = SplitMix64.Hash01((ulong)h.Id * 40503UL + (ulong)fi * 97UL + (ulong)W.Tick / 60UL);
                        double key = u <= 0 ? 0 : w / u;      // exponential-race weighted sampling
                        if (key > best) { best = key; bestFirm = fi; }
                    }
                }
                if (bestFirm >= 0)
                {
                    freeByFirm[bestFirm][cl] -= h.Earners;
                    W.Firms[bestFirm].Members.Add(h.Id);
                    h.WorkplaceParcel = W.Firms[bestFirm].Parcel;
                }
                else
                {
                    // No slot anywhere for this class: the household holds a job
                    // by the market rate but has no employer to be a member of.
                    h.WorkplaceParcel = -1;
                }
            }
        }

        private void IncomeAndTaxes()
        {
            if (Flags.LaborAuction) { LaborIncomeAndTaxes(); return; }
            // Households receive wages/transfers; firms are charged their wage
            // bill pro-rata to filled slots (exact conservation on the household side).
            var wageByClass = new double[3];
            Span<double> lwIT = stackalloc double[5];
            Span<double> lwageIT = stackalloc double[5];
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                // Gross wage bill: earners × the wage of the job level held.
                int levelsIT = Income.JobLevels(seg, P, lwIT, lwageIT);
                double wage = h.Earners > 0
                    ? h.Earners * lwageIT[Math.Min(h.JobLevel, levelsIT - 1)] : 0;
                // Non-earning adults in the labor force draw the benefit
                // (CS2 m_UnemploymentBenefit) — a national-counterparty tap —
                // but only until the allowance runs out. After that the
                // household is on its transfers alone and the insolvency
                // pipeline decides whether it sorts down or leaves, which is
                // how CS2 clears a labor surplus.
                double transfer = seg.Transfer
                    + (h.UnemployedTicks <= P.UnemploymentAllowanceTicks
                        ? Math.Max(0, seg.Adults - h.Earners) * P.UnemploymentBenefit
                          * MathUtil.Clamp(seg.Participation, 0, 1) : 0);
                if (wage > 0)
                {
                    double tax = wage * P.IncomeTax(seg.Labor);
                    h.Money += wage - tax;
                    wageByClass[(int)seg.Labor] += wage;
                    IncomeTaxThisTick += tax;
                }
                if (transfer > 0)
                {
                    h.Money += transfer;
                    W.Ledger.Transfer(Account.NationalCounterparty, Account.Households, transfer);
                }
            }
            W.Ledger.Transfer(Account.Households, Account.Treasury, IncomeTaxThisTick);

            // Charge firms: per class, pro-rata to filled slots. A class with no
            // firm capacity behind it (stale rates after deaths) is paid by the
            // outside world instead — the ledger and entity views must never
            // diverge (scrutiny findings #4/#23).
            var filledTotals = new double[3];
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                FillFirm(f);
                for (int cl = 0; cl < 3; cl++) filledTotals[cl] += f.FilledByClass[cl];
            }
            for (int cl = 0; cl < 3; cl++)
            {
                if (wageByClass[cl] <= 0) continue;
                if (filledTotals[cl] > 1e-9)
                    W.Ledger.Transfer(Account.Firms, Account.Households, wageByClass[cl]);
                else
                    W.Ledger.Transfer(Account.OutsideWorld, Account.Households, wageByClass[cl]);
            }
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                for (int cl = 0; cl < 3; cl++)
                    if (filledTotals[cl] > 1e-9)
                        f.Money -= wageByClass[cl] * (f.FilledByClass[cl] / filledTotals[cl]);
            }
        }

        /// <summary>The labor-auction income pass, PER EARNER: each earner is
        /// credited its own door's base comp (outside earners: the own
        /// outside net wage, paid by Account.OutsideWorld — the ledger leg
        /// the stale-rates fallback already used). Each firm is debited
        /// EXACTLY its own members' base comp, one Members entry per earner:
        /// the citywide pro-rata pooling — the illegitimate global standing
        /// in for firm-local payroll — dies on this path. Income tax applies
        /// to comp as to wages. Benefits and transfers are the flag-off
        /// formulas verbatim.
        ///
        /// Two independently maintained records meet here on purpose: the
        /// household side credits by its earner links (_earnerFirm), the
        /// firm side bills by its membership list against _firmClassBase,
        /// and LaborPayrollGapThisTick carries their disagreement. Both
        /// sides route by the SAME liveness predicate (!Dead && Parcel ≥ 0)
        /// and add the same terms in the same household-id order per firm,
        /// so a correct build reads exactly zero — a split predicate would
        /// let an alive parcel-less firm be credited but never billed, with
        /// the gap instrument blind to it because the gap is summed inside
        /// the firm loop.</summary>
        private void LaborIncomeAndTaxes()
        {
            int nf = W.Firms.Count;
            var creditByFirm = new double[nf];
            double outsideCredits = 0;
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                double wage = 0;
                if (h.Earners > 0)
                {
                    int at = h.Id * _maxAdults;
                    for (int s = 0; s < _maxAdults; s++)
                    {
                        double b = _earnerBase[at + s];
                        if (b <= 0) continue;
                        wage += b;
                        int of = _earnerFirm[at + s];
                        if (of >= 0 && of < nf && !W.Firms[of].Dead && W.Firms[of].Parcel >= 0)
                            creditByFirm[of] += b;
                        // Outside earners, and members whose firm died (or
                        // lost its parcel) between refreshes, are paid across
                        // the border.
                        else outsideCredits += b;
                    }
                }
                double transfer = seg.Transfer
                    + (h.UnemployedTicks <= P.UnemploymentAllowanceTicks
                        ? Math.Max(0, seg.Adults - h.Earners) * P.UnemploymentBenefit
                          * MathUtil.Clamp(seg.Participation, 0, 1) : 0);
                if (wage > 0)
                {
                    double tax = wage * P.IncomeTax(seg.Labor);
                    h.Money += wage - tax;
                    IncomeTaxThisTick += tax;
                }
                if (transfer > 0)
                {
                    h.Money += transfer;
                    W.Ledger.Transfer(Account.NationalCounterparty, Account.Households, transfer);
                }
            }
            W.Ledger.Transfer(Account.Households, Account.Treasury, IncomeTaxThisTick);

            double firmDebits = 0, firmCredits = 0, gap = 0;
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;   // the SAME predicate the credit routing used
                FillFirm(f);
                double bill = 0;
                foreach (int hid in f.Members)
                {
                    var hh = W.Households[hid];
                    if (hh.ExitedTick >= 0 || hh.Earners == 0) continue;
                    // One entry per earner; every earner at this firm is at
                    // its (firm, class) door, so the entry's base comp is the
                    // door's — the firm's own record, not the household's.
                    bill += _firmClassBase[f.Id * 3 + (int)Segment.All[hh.Segment].Labor];
                }
                f.Money -= bill;
                firmDebits += bill;
                firmCredits += creditByFirm[f.Id];
                gap += Math.Abs(bill - creditByFirm[f.Id]);
            }
            LaborPayrollGapThisTick = gap;
            LaborFirmDebitsThisTick = firmDebits;
            LaborOutsideCreditsThisTick = outsideCredits;
            if (firmCredits > 0) W.Ledger.Transfer(Account.Firms, Account.Households, firmCredits);
            if (outsideCredits > 0) W.Ledger.Transfer(Account.OutsideWorld, Account.Households, outsideCredits);
        }

        private void FillFirm(Firm f)
        {
            int c = W.Parcels[f.Parcel].Cluster;
            double[] mix = f.Sector switch
            {
                ZoneKind.Commercial => new[] { 0.7, 0.3, 0.0 },
                ZoneKind.Industrial => new[] { 0.6, 0.4, 0.0 },
                ZoneKind.Office => new[] { 0.0, 0.3, 0.7 },
                _ => new[] { 1.0, 0.0, 0.0 },
            };
            // Staff from the households that ACTUALLY work here. The firm's
            // workforce used to be JobSlots × mix × JobFillRate[class][cluster]
            // — a cluster average, so every firm in a cluster was staffed
            // identically and no worker was ever matched to a workplace, while
            // the same aggregate was independently disaggregated onto
            // households by a second unlinked draw. Members is the assignment
            // (EconomyEngine.AssignWorkplaces), so the two sides now agree.
            // On the labor-auction path Members carries one entry PER EARNER
            // (the household id repeated when two of its earners work here —
            // they may also work at different firms), so an entry counts 1;
            // flag-off entries are whole households counting their Earners.
            Array.Clear(f.FilledByClass, 0, 3);
            f.WorkersFilled = 0;
            foreach (int hid in f.Members)
            {
                var hh = W.Households[hid];
                if (hh.ExitedTick >= 0 || hh.Earners == 0) continue;
                int cl = (int)Segment.All[hh.Segment].Labor;
                int n = Flags.LaborAuction ? 1 : hh.Earners;
                f.FilledByClass[cl] += n;
                f.WorkersFilled += n;
            }
            // Never claim more staff than the structure holds.
            if (f.WorkersFilled > f.JobSlots && f.WorkersFilled > 0)
            {
                double scale = f.JobSlots / f.WorkersFilled;
                for (int cl = 0; cl < 3; cl++) f.FilledByClass[cl] *= scale;
                f.WorkersFilled = f.JobSlots;
            }
        }

        private void ConsumptionFlows()
        {
            // Spending = share of income after taxes-and-housing. Where it lands
            // depends on FeatureFlags.StoreLevelSpending: at each household's own
            // chosen shop, or pooled and handed back out pro-rata (see the flag).
            double totalCaptured = 0, totalLeaked = 0;
            double wOutside = Math.Exp(-P.ThetaShopping * P.OutsideShopMinutes) * P.OutsideShopMass;
            bool perStore = Flags.StoreLevelSpending;
            if (perStore)
                foreach (var f in W.Firms)
                    if (!f.Dead && f.Sector == ZoneKind.Commercial) f.RevenueThisTick = 0;

            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                double income = AccessState.HouseholdIncomeEstimate(h, seg, P);
                double disposable = Math.Max(0, income - h.ChargedAssessment);
                double cut = h.Stage >= InsolvencyStage.CutConsumption ? P.ConsumptionCutFactor : 1.0;
                double spend = Math.Min(h.Money, P.BaseConsumptionShare * disposable * cut);
                if (spend <= 0) continue;
                h.Money -= spend;

                if (!perStore)
                {
                    int c = h.HomeParcel >= 0 ? W.Parcels[h.HomeParcel].Cluster : 0;
                    double capShare = Access.IncumbentShopWeight.Length > c
                        ? 1.0 - wOutside / Access.IncumbentShopWeight[c] : 0.5;
                    totalCaptured += spend * capShare;
                    totalLeaked += spend * (1 - capShare);
                    continue;
                }

                // Its own shop takes the whole basket, or it goes out of town.
                Firm? shop = null;
                if ((uint)h.ShopFirm < (uint)W.Firms.Count)
                {
                    var cand = W.Firms[h.ShopFirm];
                    if (!cand.Dead && cand.Sector == ZoneKind.Commercial && cand.Parcel >= 0) shop = cand;
                    else h.ShopFirm = -1;      // it closed; re-picked next refresh
                }
                if (shop == null) { totalLeaked += spend; continue; }
                shop.Money += spend;
                shop.RevenueThisTick += spend;
                totalCaptured += spend;
            }

            if (!perStore)
            {
                // Pooled path: distribute captured spending to commercial firms
                // pro-rata to their capture strength (mass × per-mass capture at
                // their cluster). With no commercial firm alive the "captured"
                // share leaks outward too — credited money must land on real
                // entities (scrutiny finding #5).
                double weightSum = 0;
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                    weightSum += f.JobSlots * Access.CaptureIncumbentPerMass[W.Parcels[f.Parcel].Cluster];
                }
                if (weightSum <= 1e-9) { totalLeaked += totalCaptured; totalCaptured = 0; }
                else
                    foreach (var f in W.Firms)
                    {
                        if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                        double share = f.JobSlots
                            * Access.CaptureIncumbentPerMass[W.Parcels[f.Parcel].Cluster] / weightSum;
                        f.Money += totalCaptured * share;
                        f.RevenueThisTick = totalCaptured * share;
                    }
            }
            W.Ledger.Transfer(Account.Households, Account.OutsideWorld, totalLeaked);
            W.Ledger.Transfer(Account.Households, Account.Firms, totalCaptured);
        }

        private void HousingPayments()
        {
            if (!Levying) return;   // shadow: assessed + logged, nothing levied
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                var pl = W.Parcels[h.HomeParcel];
                double owed = h.ChargedAssessment;
                double sOwed = LandAccounting.SPerUnit(pl.Level, pl.Condition, P);
                double structTaxOwed = LandAccounting.StructureTaxPerUnit(pl, P);
                pl.OwedTickS += sOwed;
                double pay = Math.Min(Math.Max(0, h.Money), owed);
                h.Money -= pay;
                if (pay < owed - 1e-9) h.StressTicks++;
                else if (h.StressTicks > 0 && h.Money > 0) h.StressTicks--;

                // Split the payment: S first (condition funding), then the
                // structure tax (treasury — split-rate leg, §4.3), then land.
                double sPaid = Math.Min(pay, sOwed);
                pl.PaidTickS += sPaid;
                double structPaid = Math.Min(pay - sPaid, structTaxOwed);
                double land = pay - sPaid - structPaid;
                RouteStructureCharge(pl, sPaid);
                if (structPaid > 0) W.Ledger.Transfer(Account.Households, Account.Treasury, structPaid);
                RouteLandCharge(pl, land);
            }
        }

        private void RouteStructureCharge(Parcel pl, double sPaid)
        {
            if (sPaid <= 0) return;
            double total = P.HurdleRate + P.Depreciation + P.MaintenanceRate;
            double hShare = P.HurdleRate / total;
            // Capital charge → phantom bank; renewal + maintenance → materials.
            W.Ledger.Transfer(Account.Households, Account.PhantomBank, sPaid * hShare);
            W.Ledger.Transfer(Account.Households, Account.OutsideWorld, sPaid * (1 - hShare));
        }

        private void RouteFirmStructureCharge(Parcel pl, double sPaid)
        {
            if (sPaid <= 0) return;
            double total = P.HurdleRate + P.Depreciation + P.MaintenanceRate;
            double hShare = P.HurdleRate / total;
            W.Ledger.Transfer(Account.Firms, Account.PhantomBank, sPaid * hShare);
            W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, sPaid * (1 - hShare));
        }

        private void RouteLandCharge(Parcel pl, double land, bool fromFirm = false)
        {
            if (land <= 0) return;
            var src = fromFirm ? Account.Firms : Account.Households;
            double landTotal = Math.Max(1e-9, pl.CurrentResidual > 0 ? pl.CurrentResidual + pl.Wedge : pl.Wedge);
            double wedgeShare = MathUtil.Clamp(pl.Wedge / landTotal, 0, 1);
            bool earmark = W.Clusters[pl.Cluster].WedgeEarmark && P.WedgeEarmarkDefault;
            double toEscrow = earmark ? land * wedgeShare : 0;
            double toTreasury = land - toEscrow;
            if (toEscrow > 0)
            {
                W.Ledger.Transfer(src, Account.Escrow, toEscrow);
                pl.Escrow += toEscrow;
            }
            W.Ledger.Transfer(src, Account.Treasury, toTreasury);
            LandRevenueThisTick += toTreasury;
            LandRevenueByCluster[pl.Cluster] += land;
        }

        // Per-resource scratch reused each tick
        private double[][] _supplyByCluster = Array.Empty<double[]>();
        private double[][] _demandByCluster = Array.Empty<double[]>();
        private readonly double[] _supplyTotal = new double[ResourceCatalog.Count];
        private readonly double[] _demandTotal = new double[ResourceCatalog.Count];

        private void ProductionAndTrade()
        {
            int C = Costs.ClusterCount;
            int R = ResourceCatalog.Count;
            if (_supplyByCluster.Length != R)
            {
                _supplyByCluster = new double[R][];
                _demandByCluster = new double[R][];
                for (int r = 0; r < R; r++) { _supplyByCluster[r] = new double[C]; _demandByCluster[r] = new double[C]; }
            }
            for (int r = 0; r < R; r++)
            {
                Array.Clear(_supplyByCluster[r], 0, C);
                Array.Clear(_demandByCluster[r], 0, C);
                _supplyTotal[r] = 0; _demandTotal[r] = 0;
            }

            // ---- production and input demand, per resource -------------------
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                Array.Clear(f.InputNeedByRes, 0, R);
                var pl = W.Parcels[f.Parcel];
                int c = pl.Cluster;
                double cond = Math.Max(0.2, pl.Condition);
                switch (f.Sector)
                {
                    case ZoneKind.Extractor:
                    {
                        // Output scales with the cluster's geology for THIS raw.
                        double suit = W.Clusters[c].ResourceSuitability[(int)f.Output];
                        f.OutputThisTick = f.WorkersFilled * P.ExtractorOutputPerSlot * suit * cond;
                        _supplyByCluster[(int)f.Output][c] += f.OutputThisTick;
                        _supplyTotal[(int)f.Output] += f.OutputThisTick;
                        break;
                    }
                    case ZoneKind.Industrial:
                    {
                        var recipe = ResourceCatalog.RecipeFor(f.Output);
                        if (recipe.Inputs == null) { f.OutputThisTick = 0; break; }
                        f.OutputThisTick = f.WorkersFilled * recipe.OutputPerSlot * P.RecipeOutputScale
                                           * cond * P.Quality(pl.Level) / P.Quality(1);
                        foreach (var (res, qty) in recipe.Inputs)
                        {
                            double need = f.OutputThisTick * qty;
                            f.InputNeedByRes[(int)res] = need;
                            _demandByCluster[(int)res][c] += need;
                            _demandTotal[(int)res] += need;
                        }
                        _supplyByCluster[(int)f.Output][c] += f.OutputThisTick;
                        _supplyTotal[(int)f.Output] += f.OutputThisTick;
                        break;
                    }
                    case ZoneKind.Commercial:
                    {
                        // Restocking: the consumption basket behind captured spending.
                        foreach (var (res, share) in ResourceCatalog.Basket)
                        {
                            double need = f.RevenueThisTick * share / Math.Max(0.5, Trade.LocalPrice(res));
                            f.InputNeedByRes[(int)res] = need;
                            _demandByCluster[(int)res][c] += need;
                            _demandTotal[(int)res] += need;
                        }
                        break;
                    }
                    case ZoneKind.Office:
                    {
                        double rev = f.WorkersFilled * P.OfficeOutputPerSlot * P.OfficeOutputPrice
                                     * Access.OfficeAgglomMult[c];
                        f.Money += rev; f.RevenueThisTick = rev;
                        W.Ledger.Transfer(Account.OutsideWorld, Account.Firms, rev);
                        break;
                    }
                }
            }

            // ---- clear and settle each tradable resource ---------------------
            for (int r = 0; r < R; r++)
            {
                var res = (Res)r;
                if (!ResourceCatalog.IsTradable(res)) continue;
                if (_supplyTotal[r] <= 1e-9 && _demandTotal[r] <= 1e-9) continue;
                var clear = Flags.TierD_Trade
                    ? Trade.ClearTick(res, _supplyTotal[r], _demandTotal[r],
                                      _supplyByCluster[r], _demandByCluster[r], P)
                    : FlatClear(res, _supplyTotal[r], _demandTotal[r]);
                SettleResource(res, clear, _supplyTotal[r], _demandTotal[r]);
            }
        }

        /// <summary>Vanilla-style flat-price fallback (TierD off): infinite depth
        /// at the world anchor.</summary>
        private ClearResult FlatClear(Res r, double supply, double demand)
        {
            double anchor = ResourceCatalog.Anchor[(int)r];
            var res = new ClearResult { LocalPrice = anchor };
            double surplus = supply - demand;
            if (surplus > 0) { res.Exported = surplus; res.ExportRevenue = surplus * anchor; }
            else { res.Imported = -surplus; res.ImportCost = -surplus * anchor; }
            return res;
        }

        /// <summary>Money settlement for one resource: local trades net between
        /// producing and consuming firms; exports arrive from OutsideWorld,
        /// imports leave to it. Pro-rata across firms by their actual volumes;
        /// conservation exact by construction.</summary>
        private void SettleResource(Res r, ClearResult clear, double supply, double demand)
        {
            double price = clear.LocalPrice;
            double localVolume = Math.Min(supply - clear.Exported - clear.Unsold, demand - clear.Imported);
            localVolume = Math.Max(0, localVolume);

            double sellerRevenue = localVolume * price + clear.ExportRevenue;
            double buyerCost = localVolume * price + clear.ImportCost;

            if (supply > 1e-9 && sellerRevenue > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.Output != r || f.OutputThisTick <= 0) continue;
                    double share = f.OutputThisTick / supply;
                    f.Money += sellerRevenue * share;
                    f.RevenueThisTick += sellerRevenue * share;
                }
            if (demand > 1e-9 && buyerCost > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.InputNeedByRes[(int)r] <= 0) continue;
                    f.Money -= buyerCost * (f.InputNeedByRes[(int)r] / demand);
                }
            W.Ledger.Transfer(Account.OutsideWorld, Account.Firms, clear.ExportRevenue);
            W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, clear.ImportCost);
        }

        private void FirmLifecycle()
        {
            // Firm assessments + profit tracking + bankruptcy + entry.
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                var pl = W.Parcels[f.Parcel];
                if (Levying)
                {
                    double owed = LandAccounting.UnitAssessment(pl, P) * pl.Units;
                    double sOwed = LandAccounting.SPerUnit(pl.Level, pl.Condition, P) * pl.Units;
                    double structTaxOwed = LandAccounting.StructureTaxPerUnit(pl, P) * pl.Units;
                    pl.OwedTickS += sOwed;
                    double pay = Math.Min(Math.Max(0, f.Money), owed);
                    f.Money -= pay;
                    double sPaid = Math.Min(pay, sOwed);
                    pl.PaidTickS += sPaid;
                    double structPaid = Math.Min(pay - sPaid, structTaxOwed);
                    RouteFirmStructureCharge(pl, sPaid);
                    if (structPaid > 0) W.Ledger.Transfer(Account.Firms, Account.Treasury, structPaid);
                    RouteLandCharge(pl, pay - sPaid - structPaid, fromFirm: true);
                }
                f.ProfitEma = MathUtil.Ema(f.ProfitEma, f.RevenueThisTick, 0.05);
                f.RevenueThisTick = 0; f.OutputThisTick = 0;

                // Worker-collective distribution: surplus above the working
                // capital reserve is paid to THIS firm's own members, split by
                // the earners each household contributes. No pool, no citywide
                // spread — the people who produced the surplus receive it, and
                // the money re-enters household circulation where it can be
                // spent, which is what closes the loop back to commercial
                // demand and therefore to jobs.
                if (f.Members.Count > 0)
                {
                    double wageBill = 0;
                    for (int cl = 0; cl < 3; cl++) wageBill += f.FilledByClass[cl] * P.Wage((LaborClass)cl);
                    double reserve = Math.Max(P.FirmSeedCapital, wageBill * P.FirmWorkingCapitalTicks);
                    double surplus = f.Money - reserve;
                    // A Members entry is one EARNER on the labor-auction path
                    // (household ids repeat) and one household of Earners
                    // adults flag-off — either way the pro-rata weights are
                    // earner counts.
                    double totalEarners = 0;
                    foreach (int hid in f.Members)
                        totalEarners += Flags.LaborAuction ? 1 : Math.Max(1, (int)W.Households[hid].Earners);
                    double paidPerEarner = 0;
                    if (surplus > 0)
                    {
                        double payout = surplus * MathUtil.Clamp(P.FirmDividendRate, 0, 1);
                        if (totalEarners > 0 && payout > 0)
                        {
                            foreach (int hid in f.Members)
                            {
                                double sharePaid = payout
                                    * (Flags.LaborAuction ? 1 : Math.Max(1, (int)W.Households[hid].Earners))
                                    / totalEarners;
                                W.Households[hid].Money += sharePaid;
                            }
                            f.Money -= payout;
                            f.DividendsPaid += payout;
                            W.Ledger.Transfer(Account.Firms, Account.Households, payout);
                            paidPerEarner = payout / totalEarners;
                        }
                    }
                    // The firm's own dividend forecast, from its own payouts —
                    // what the labor path subtracts from cleared total comp to
                    // get base pay. Zero-payout ticks are observations too.
                    if (totalEarners > 0)
                        f.DividendPerEarnerEma = MathUtil.Ema(f.DividendPerEarnerEma, paidPerEarner, 0.05);
                }

                if (f.Money < P.CompanyBankruptcyLimit)
                {
                    f.Dead = true;
                    double residual = Math.Max(0, f.Money);
                    if (residual > 0) W.Ledger.Transfer(Account.Firms, Account.PhantomBank, residual);
                    else W.Ledger.Transfer(Account.PhantomBank, Account.Firms, -f.Money); // written-off debt
                    f.Money = 0;
                    pl.OccupantFirm = -1;
                }
            }

            // Entry into existing vacant firm parcels (aggregate residual profit).
            foreach (var pl in W.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.OccupantFirm >= 0) continue;
                if (pl.Use == ZoneKind.None || pl.Warehousing) continue;
                double bid = LandAccounting.FirmBidPerSlot(Access, Trade, pl.Cluster, pl.Use, pl.Level, P,
                                                           out Res chosen, W.Clusters)
                             * P.CondFactor(pl.Condition);
                double assess = LandAccounting.UnitAssessment(pl, P);
                double excess = bid - assess;
                if (excess <= 0) continue;
                double prob = MathUtil.Clamp(P.FirmEntryElasticity * excess * pl.Units * 10, 0, 0.5);
                if (W.Rng.NextDouble() < prob)
                {
                    // The entrant fixes its output here: extractors mine what the
                    // geology supports, industry commits to the recipe whose
                    // input sourcing is cheapest from THIS location (Weber).
                    var firm = new Firm
                    {
                        Id = W.Firms.Count, Sector = pl.Use, Parcel = pl.Id,
                        Output = pl.Use == ZoneKind.Commercial ? Res.Services : chosen,
                        Money = P.FirmSeedCapital, JobSlots = pl.Units, EnteredTick = W.Tick,
                    };
                    W.Firms.Add(firm);
                    pl.OccupantFirm = firm.Id;
                    W.Ledger.Transfer(Account.PhantomBank, Account.Firms, P.FirmSeedCapital);
                }
            }
        }

        private void AllocationAndInsolvency()
        {
            Allocation.RebuildVacancies(W);

            _unhoused.Clear();
            foreach (var h in W.Households)
                if (h.ExitedTick < 0 && h.HomeParcel < 0) _unhoused.Add(h.Id);

            foreach (int hid in _unhoused)
            {
                var h = W.Households[hid];
                var seg = Segment.All[h.Segment];
                // Arrivals search on EXPECTED income at destination (they will
                // work once housed), floored by their actual effective income.
                double income = Math.Max(AccessState.EffectiveIncome(h, seg, P), MeanExpectedIncome[h.Segment]);
                double cap = seg.MaxRentShare * Math.Max(income, seg.Transfer + 1) * 1.1;
                int target = Allocation.FindHome(W, Access, h, P, cap, income);
                if (target >= 0)
                {
                    bool wasSheltered = h.Stage == InsolvencyStage.Sheltered;
                    Allocation.MoveIn(W, h, target, P);
                    if (wasSheltered) ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                }
                else
                {
                    // Failing to find housing IS housing stress: the floor state
                    // must couple back (§4.2) — unhoused households escalate the
                    // pipeline (shelter or funded emigration) rather than pooling
                    // outside the market forever.
                    h.StressTicks++;
                }
            }

            // Insolvency pipeline for stressed households.
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                if (h.StressTicks == 0 && h.Stage != InsolvencyStage.Solvent && h.Money > 0)
                    h.Stage = InsolvencyStage.Solvent;   // recovered
                if (h.StressTicks > 0 && h.Stage != InsolvencyStage.Sheltered)
                {
                    // Reason 1 = a HOUSED resident driven out by insolvency
                    // (true displacement); reason 2 = an unhoused arrival that
                    // never landed a unit and gave up (a failed arrival, not a
                    // displacement — the distinction churnprobe reports).
                    bool housedAtExit = h.HomeParcel >= 0;
                    if (Allocation.InsolvencyStep(W, Access, h, P, ref ShelterOccupied, ShelterCapacity))
                        DisplacementExits.Add((W.Tick, h.Id, housedAtExit ? 1 : 2));
                }
                else if (h.Stage == InsolvencyStage.Sheltered
                         && W.Tick - h.ArrivedTick > 90 && h.Money >= P.EmigrationMoveCost
                         && W.Rng.NextDouble() < 0.03)
                {
                    // Long-sheltered households give up on the city (funded exit).
                    ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                    W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                    h.Money = 0; h.ExitedTick = W.Tick;
                }
            }
        }

        /// <summary>Arrivals and departures from prospect telemetry (auction
        /// path). Counts only, for the harness — the decisions themselves are in
        /// Prospects.Step and in HousingAuction.Why.</summary>
        public Prospects.Result LastProspects;
        public int DeclineExitsThisTick;

        private void MigrationStep()
        {
            // AUCTION PATH: migration is not computed here at all. The region
            // offers individuals, each one reads posted prices and decides for
            // itself, and departures are households whose own reservation beat
            // everything the city could offer them. No citywide attractiveness
            // scalar exists on this path.
            if (Flags.HousingAuction)
            {
                LastProspects = Prospects.Step(W, Access, Auction, P);
                LastFlows = default;
                LastFlows.ArrivalsBySegment = new int[Segment.Count];
                LastFlows.DeparturesBySegment = new int[Segment.Count];
                LastFlows.DesiredBySegment = new double[Segment.Count];
                return;
            }

            // Distress signal for migration, not just literal homelessness:
            // solvent-but-stressed households take the FUNDED-emigration exit
            // in InsolvencyStep before they ever reach Sheltered, so a city
            // running an insolvency conveyor (churnprobe: 2631/2631 exits
            // were emigrations of unemployed households, 0 sheltered) showed
            // homelessShare ≈ 0 and migration kept refilling the vacated
            // units — a turnstile. Households in any insolvency stage count
            // at half weight: word of economic distress travels even when
            // the distressed leave under their own steam.
            int pop = 0, sheltered = 0, stressed = 0;
            foreach (var h in W.Households)
                if (h.ExitedTick < 0)
                {
                    pop++;
                    if (h.Stage == InsolvencyStage.Sheltered) sheltered++;
                    else if (h.Stage != InsolvencyStage.Solvent) stressed++;
                }
            double distressShare = pop > 0 ? (sheltered + 0.5 * stressed) / pop : 0;

            // Measured turnover: EMA of units actually freed per tick (raw
            // signal accumulated in Allocation.Vacate). This is the churn term
            // of the absorption budget — see Migration.Step.
            for (int k = 0; k < 2; k++)
            {
                TurnoverEma[k] = MathUtil.Ema(TurnoverEma[k], W.FreedUnitsThisTick[k], 0.05);
                W.FreedUnitsThisTick[k] = 0;
            }

            // Citywide unemployment among housed households WITH working-age
            // adults — the labor force, not the population. Vanilla's demand
            // reads the same quantity (m_NeutralUnemployment is a rate).
            double laborForce = 0, jobless = 0;
            foreach (var h in W.Households)
            {
                if (h.ExitedTick < 0 && h.HomeParcel >= 0 && Segment.All[h.Segment].Adults > 0)
                { laborForce++; if (h.Earners == 0) jobless++; }
            }
            double unemploymentRate = laborForce > 0 ? jobless / laborForce : 0;

            LastFlows = Migration.Step(W, Access, AvgRentBySeg, distressShare, unemploymentRate,
                                       TurnoverEma, P, Flags.TierA_Migration);

            for (int s = 0; s < Segment.Count; s++)
            {
                for (int k = 0; k < LastFlows.ArrivalsBySegment[s]; k++)
                {
                    double savings = Math.Max(5, P.ArrivalSavingsMean + P.ArrivalSavingsSd * (W.Rng.NextDouble() * 2 - 1));
                    var seg = Segment.All[s];
                    bool owner = seg.Life == Lifecycle.Family && W.Rng.NextDouble() < 0.35;
                    var h = new Household
                    {
                        Id = W.Households.Count, Segment = s, Money = savings,
                        ArrivedTick = W.Tick,
                        MovingCostDraw = P.MovingCostMean * (0.4 + 1.2 * W.Rng.NextDouble())
                                         * (owner ? P.OwnerMovingCostMult : 1.0),
                    };
                    h.DrawAtBirth(seg);
                    W.Households.Add(h);
                    W.Ledger.Transfer(Account.OutsideWorld, Account.Households, savings);
                }
                // Departures: draw uniformly from the ALIVE members of the
                // segment (an append-only list with exited households would bias
                // and truncate the flow as the run ages — scrutiny findings #13/#24).
                int departures = LastFlows.DeparturesBySegment[s];
                if (departures > 0)
                {
                    _aliveScratch.Clear();
                    foreach (var h in W.Households)
                        if (h.ExitedTick < 0 && h.Segment == s) _aliveScratch.Add(h.Id);
                    int attempts = 0;
                    while (departures > 0 && _aliveScratch.Count > 0 && attempts++ < 40)
                    {
                        var h = W.Households[_aliveScratch[W.Rng.NextInt(_aliveScratch.Count)]];
                        if (h.ExitedTick >= 0) continue;
                        if (h.StressTicks == 0 && W.Rng.NextDouble() < 0.6) continue; // attachment
                        Allocation.Vacate(W, h);
                        if (h.Stage == InsolvencyStage.Sheltered) ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                        W.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                        h.Money = 0; h.ExitedTick = W.Tick;
                        departures--;
                    }
                }
            }
        }

        private void RerateAndRelocation()
        {
            if (!Levying)
            {
                // Nothing is charged: assessments are computed and logged but must
                // not drive consumption, stress, or displacement (stage-3 shadow
                // contract; scrutiny findings #2/#7/#18).
                foreach (var h in W.Households) h.ChargedAssessment = 0;
                _levyingSince = -1;      // a later flip re-arms the go-live ramp
                return;
            }
            if (_levyingSince < 0) _levyingSince = W.Tick;
            long sinceGoLive = W.Tick - _levyingSince;
            // Co-op re-rate: EVERY housed household is charged its parcel's
            // current market unit assessment, every tick — one price per unit,
            // uniform across co-tenants, no anniversaries, no phase-in. The
            // land residual (total value − structure value) moves instantly;
            // §3's no-synchronized-shock property now rests on the assessment
            // moving smoothly plus the two frictions below, not on staggering
            // the re-rate itself.
            double searchHazard = 1.0 / Math.Max(1, P.MoveSearchPeriod);
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                var pl = W.Parcels[h.HomeParcel];
                double market = LandAccounting.UnitAssessment(pl, P);
                // Steady state: charge IS the live market assessment, exactly
                // and uniformly per parcel. Only the one-time go-live window
                // interpolates, staggered per household so the transition is
                // not a citywide step (§3).
                long window = (long)(P.GoLiveRampTicks * (0.5 + SplitMix64.Hash01((ulong)h.Id * 8563UL + 29)));
                if (sinceGoLive >= window || h.ChargedAssessment <= 0)
                    h.ChargedAssessment = market;
                else
                    h.ChargedAssessment += (market - h.ChargedAssessment) / Math.Max(1, window - sinceGoLive);

                // Exit timing: heterogeneous moving margins (draw at arrival)
                // plus a memoryless search hazard keep displacement a
                // distribution — you notice the re-rate instantly, you move
                // when a search comes up AND finds somewhere cheaper (§4.4).
                var seg = Segment.All[h.Segment];
                double income = AccessState.EffectiveIncome(h, seg, P);
                double affordable = seg.MaxRentShare * Math.Max(0.1, income);
                double margin = 1.0 + h.MovingCostDraw / 150.0;
                if (h.ChargedAssessment > affordable * margin
                    && W.Tick - h.TenureStart >= P.MinLeaseTicks
                    && W.Rng.NextDouble() < searchHazard)
                {
                    int cheaper = Allocation.FindHome(W, Access, h, P, affordable, income);
                    if (cheaper >= 0 && cheaper != h.HomeParcel)
                    {
                        Allocation.Vacate(W, h);
                        Allocation.MoveIn(W, h, cheaper, P);
                        DisplacementExits.Add((W.Tick, h.Id, 0));
                    }
                    else h.StressTicks++;
                }
            }
        }

        private void ConditionDecay()
        {
            foreach (var pl in W.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Units == 0) continue;
                double owedFull = LandAccounting.SPerUnit(pl.Level, pl.Condition, P) * pl.Units;
                // When Tier C levies nothing, S is treated as implicitly funded
                // (vanilla-analog behavior): no decay from unpaid charges.
                double paidFrac = !Levying ? 1
                    : owedFull > 1e-9 ? MathUtil.Clamp(pl.PaidTickS / owedFull, 0, 1) : 1;
                // Paying S holds condition; underpayment decays it (§4.3). The
                // sinking fund is continuous renewal, so full payment = no decay.
                pl.Condition = Math.Max(0.05, pl.Condition
                    - P.Depreciation * (1 - paidFrac) * P.ConditionDecayScale);

                // Vacancy drain (§4.3 anti-speculation tooth): the parcel owes land
                // tax regardless of occupancy — pre-funded escrow bleeds to the
                // treasury while units sit empty.
                double vacantShare = pl.IsResidential
                    ? (double)pl.Vacant / pl.Units
                    : (pl.OccupantFirm < 0 ? 1.0 : 0.0);
                if (vacantShare > 0 && pl.Escrow > 0 && Levying)
                {
                    double drain = Math.Min(pl.Escrow,
                        P.CaptureFraction * pl.AssessedLR * vacantShare);
                    if (drain > 0)
                    {
                        pl.Escrow -= drain;
                        W.Ledger.Transfer(Account.Escrow, Account.Treasury, drain);
                        LandRevenueThisTick += drain;
                        LandRevenueByCluster[pl.Cluster] += drain;
                    }
                }
            }
        }

        private void ServiceCosts()
        {
            double cost = 0;
            foreach (var pl in W.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                if (pl.IsResidential) cost += pl.OccupantHouseholds.Count * P.ServiceCostPerHousehold;
                else if (pl.OccupantFirm >= 0) cost += pl.Units * P.ServiceCostPerFirmSlot;
            }
            ServiceCostThisTick = cost;
            W.Ledger.Transfer(Account.Treasury, Account.OutsideWorld, cost);
        }

        private void Mortality()
        {
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                if (Segment.All[h.Segment].Life != Lifecycle.Senior) continue;
                if (W.Rng.NextDouble() < P.SeniorMortalityPerTick)
                {
                    // Terminal balances escheat OUTWARD to the national
                    // counterparty — the death-drain pairs with the pension tap
                    // at one referent; no fiscal windfall from mortality (§3).
                    Allocation.Vacate(W, h);
                    if (h.Stage == InsolvencyStage.Sheltered) ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                    W.Ledger.Transfer(Account.Households, Account.NationalCounterparty, Math.Max(0, h.Money));
                    h.Money = 0; h.ExitedTick = W.Tick;
                }
            }
        }
    }
}
