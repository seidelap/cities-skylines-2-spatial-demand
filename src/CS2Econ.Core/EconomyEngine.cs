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
        /// <summary>Assessment billed and collected this tick, split by who is
        /// standing on the land. Both sides are the FULL bill (S + τ_S + φ·LR),
        /// so owed − paid is the tick's shortfall on that side. Telemetry only:
        /// nothing in the engine reads these, and the parity checks assert on
        /// them.</summary>
        public double HouseholdLevyOwedThisTick, HouseholdLevyPaidThisTick;
        public double FirmLevyOwedThisTick, FirmLevyPaidThisTick;
        /// <summary>Firms that released their parcel this tick because the land
        /// charge went unmet for LandArrearsTicks — kept apart from the
        /// working-capital bankruptcies at CompanyBankruptcyLimit so the two
        /// exits can never be confused for one another in a check.</summary>
        public int FirmArrearsExitsThisTick;
        public int FirmArrearsExitsTotal;
        /// <summary>Firms that moved to land they could carry rather than
        /// exiting — the firm side of the household pipeline's SortDown stage.</summary>
        public int FirmRelocationsTotal;
        /// <summary>Live firms observed holding no site, and how each of them
        /// resolved. Counted where the outcome is decided, so
        /// Displaced == Resited + Exits holds by construction every tick and a
        /// firm that reached NO outcome is the difference — which is exactly
        /// what the defect looked like before there was a pass to see it.</summary>
        public int FirmDisplacedSeenTotal;
        public int FirmDisplacedResitedTotal;
        public int FirmDisplacedExitsTotal;
        /// <summary>Live firms the pass found siteless that had NOT lost a site
        /// it held — the adapter could not place them. Left standing on
        /// purpose (Firm.SiteLostTick says why) and counted here so that
        /// "the engine is not modelling this company" is a number somebody can
        /// read rather than silence. Structurally 0 in the pure simulation,
        /// which is what the check's guard leg asserts; on the mod arm a rising
        /// count means the reader's coverage is the thing to go fix.</summary>
        public int FirmUnplacedSeenTotal;
        /// <summary>Firms that left because their own cash-flow read stayed
        /// under water past their patience (the exit margin), and firms that
        /// moved to a site they could carry rather than leaving. Kept apart
        /// from the arrears counters beside them because the two answer
        /// different questions: arrears is "did not pay the bill", the margin
        /// is "could not have paid it".</summary>
        public int FirmMarginExitsTotal;
        public int FirmMarginRelocationsTotal;
        /// <summary>Shops that picked a retail line rather than carrying the
        /// whole basket. Zero whenever CommercialLines is off, which is what
        /// makes a non-zero reading proof the mechanism reached the population
        /// rather than only the handful of mid-run entrants.</summary>
        public int FirmRetailChoicesTotal;
        /// <summary>Industrial recipe switches executed by FirmRetooling.</summary>
        public int FirmRetoolsTotal;

        /// <summary>MUTANT SWITCH: restores office and extractor production
        /// WITHOUT the level and condition terms their assessment prices them
        /// on — the pre-parity production functions. Never a shipping mode.</summary>
        public static bool MutantLevelFreeFirmOutput;
        /// <summary>MUTANT SWITCH: restores the silent forgiveness of an unmet
        /// land charge — the firm keeps the parcel however long it fails to
        /// pay. Never a shipping mode.</summary>
        public static bool MutantForgiveFirmArrears;
        /// <summary>MUTANT SWITCH: restores the state this codebase shipped
        /// before ResolveDisplacedFirms — a firm that loses its site reaches no
        /// outcome at all. Every firm loop in the engine is guarded by
        /// `f.Dead || f.Parcel &lt; 0`, so such a firm produces nothing, hires
        /// nobody, owes no land charge and — because the exit block itself sits
        /// inside one of those loops — cannot even go bankrupt, while its money
        /// stays on the books forever. Never a shipping mode.</summary>
        public static bool MutantDisplacedFirmNoExit;
        /// <summary>MUTANT SWITCH: DisplaceFirm unlinks only the PARCEL's side
        /// of the pointer and leaves the firm still naming the site. This is
        /// not a hypothetical — it is the exact shape EconReader.SyncParcels
        /// shipped when a building despawned under a company: the parcel went
        /// Empty with Units == 0 and OccupantFirm == -1, while the firm went on
        /// naming it. That firm is NOT siteless, so the displacement pass never
        /// sees it; it reads a demolished ruin, owes a levy on zero units, and
        /// the parcel is meanwhile free to be re-let to somebody else. Never a
        /// shipping mode.</summary>
        public static bool MutantHalfUnlinkDisplacement;
        /// <summary>MUTANT SWITCH: the pass acts on EVERY siteless firm, not
        /// only on one that lost a site it held — the regression task #49
        /// shipped before Firm.SiteLostTick existed. On the mod arm that
        /// relocates or PERMANENTLY kills a company the reader merely failed to
        /// place, which is an agent moved against its own default on the
        /// strength of the engine's own ignorance. Never a shipping mode.</summary>
        public static bool MutantResolveUnplacedFirms;
        /// <summary>MUTANT SWITCH: the exit margin does nothing while its flag
        /// is on — the zombie world, where a firm whose revenue never covers
        /// its costs keeps its site until the balance alone finishes it, which
        /// takes as long as its accumulated surplus lasts. Never a shipping
        /// mode.</summary>
        public static bool MutantFirmExitNoMargin;
        /// <summary>MUTANT SWITCH: every firm gets the same patience whatever
        /// its working capital, so a one-worker shop and a full office block
        /// are asked to prove themselves on the same clock. Never a shipping
        /// mode.</summary>
        public static bool MutantFirmFlatPatience;
        /// <summary>MUTANT SWITCH: the exit margin's inaction band collapses to
        /// zero, so a firm losing a tenth of a percent of its cost base is
        /// treated exactly like one losing its whole payroll. This is the
        /// pre-band behaviour and it is what the band was added to end; it is
        /// a mutant rather than a flag because no city should ship it. Never a
        /// shipping mode.</summary>
        public static bool MutantFirmNoInactionBand;
        /// <summary>MUTANT SWITCH (`--mutant-retool-free`): retooling costs
        /// nothing and needs no margin gain — a firm switches to the argmax
        /// recipe whenever it differs. The monoculture world the registry
        /// recorded (free continuous re-choice converged every firm on one
        /// recipe, 20 of 26 seeds red) restored on purpose, so the distinct-
        /// outputs census can prove the cost band is what prevents it. Never
        /// a shipping mode.</summary>
        public static bool MutantRetoolFree;
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
            LandAccounting.CommercialLinesActive = flags.CommercialLines;
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
            HouseholdLevyOwedThisTick = HouseholdLevyPaidThisTick = 0;
            FirmLevyOwedThisTick = FirmLevyPaidThisTick = 0;
            FirmArrearsExitsThisTick = 0;
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
            if (Flags.StoreLevelSpending) { ChooseShops(); Access.CountShopIntents(W, P, _shopBestU); }
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
            Labor.Solve(W, Access, Costs, Trade, P, Flags, _participants, _earnerFirm, _maxAdults);
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
            Auction.OwnerDoorsEnabled = Flags.OwnerDoors;
            // Owner tags whose household no longer lives there (exits that
            // bypass Vacate) are cleared before the solve reads them, so an
            // owner door always has a live agent behind its ask.
            foreach (var pl in W.Parcels)
            {
                int oh = pl.OwnerHousehold;
                if (oh < 0) continue;
                var owner = W.Households[oh];
                if (owner.ExitedTick >= 0 || owner.HomeParcel != pl.Id)
                { pl.OwnerHousehold = -1; pl.OwnerAskPerUnit = 0; }
            }
            Auction.Solve(W, Access, P);
            Auction.LastApplyTick = W.Tick;

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
                // The home is its DOOR: for a household at a live owner door,
                // re-winning that parcel-door is renewing in place, and
                // winning the uniform submarket it also stands in is a real
                // move to a different parcel.
                int have = Auction.DoorOf(pl);
                if (want == have && have >= 0) continue;         // renewed in place
                // Telemetry for the tenure-security question an owner's ask
                // raises: a household that does not own the parcel it lives in
                // leaving an owner-tagged parcel. Tagged, not door-live, so the
                // count is comparable across the OwnerAskScale arms (at scale 0
                // the tags stand and no door does).
                if (pl.OwnerHousehold >= 0 && pl.OwnerHousehold != h.Id) OwnerParcelTenantVacatesTotal++;
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
                // Telemetry for the item-#41 self-pricing bound: an owner
                // declining its own city is the case the ask's known
                // distortion could inflate (by at most ask − S when nobody
                // else queues); the counter makes the before/after measurable.
                if (h.HomeParcel >= 0 && W.Parcels[h.HomeParcel].OwnerHousehold == h.Id)
                    OwnerDeclineExitsTotal++;
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
                    h.PlacedByAuction = true;
                    if (wasSheltered) ShelterOccupied = Math.Max(0, ShelterOccupied - 1);
                }
                else Auction.Assignment[hid] = -1;
            }

            // Next solve's asks, from THIS solve's frozen public state — the
            // one price-forming input written between solves, read exactly
            // once per solve in BuildSubmarkets, with every repair round
            // re-clearing from it (task #36's atomicity, extended: nothing may
            // update an ask mid-solve, and no mechanism between rounds).
            PostOwnerAsks();
        }

        /// <summary>Write each owner's ask for its own parcel's units:
        ///
        ///     A = min(OwnerAskScale · OwnerAskShare · R, R),
        ///     R = max(0, ValueOf(owner, its door) − OutsideOf(owner))
        ///
        /// R is the owner's own reservation for its place — its valuation
        /// (level uplift, access premium, its own taste, its home bonus) net
        /// of its own outside option; money per unit per tick, a rent's
        /// units. The min-clamp makes A ≤ R an invariant under any swept
        /// OwnerAskScale, so no owner is ever priced out of its own door by
        /// its own ask.
        ///
        /// THE NO-RATCHET RULE (the ask-level mirror of §3): this must not
        /// read Price/Admitted at the owner's own door. The ask derives from
        /// the owner's VALUATION, never from the market's answer at its
        /// parcel — otherwise ask chases price chases nothing. The first
        /// solve of a run has no frozen state, so asks are 0 and every door
        /// floors at structure cost, exactly the pre-item semantics; asks
        /// appear from the second refresh.</summary>
        private void PostOwnerAsks()
        {
            if (!Flags.OwnerDoors) return;
            foreach (var pl in W.Parcels)
            {
                int oh = pl.OwnerHousehold;
                if (oh < 0) continue;
                if ((uint)oh >= (uint)Auction.Assignment.Length) continue;  // arrived after the solve
                int door = Auction.DoorOf(pl);
                if (door < 0) { pl.OwnerAskPerUnit = 0; continue; }
                double r = Math.Max(0, Auction.ValueOf(oh, door, P) - Auction.OutsideOf(oh));
                pl.OwnerAskPerUnit = Math.Min(P.OwnerAskScale * W.Households[oh].OwnerAskShare * r, r);
            }
        }

        private int FirstVacantIn(int sub)
        {
            // An owner door names its parcel: the won slot lands there and
            // nowhere else.
            int ownerParcel = Auction.OwnerParcelOf(sub);
            if (ownerParcel >= 0)
            {
                var opl = W.Parcels[ownerParcel];
                return !opl.Warehousing && opl.Vacant > 0 ? ownerParcel : -1;
            }
            int kc = HousingAuction.KcOf(sub), lvl = HousingAuction.LevelOf(sub);
            int k = kc / Access.C, c = kc - k * Access.C;
            var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
            foreach (int pi in Allocation.VacantByCluster[c])
            {
                var pl = W.Parcels[pi];
                // A parcel whose units sit at its own live door must not
                // satisfy a uniform-submarket win — its rooms are behind its
                // own reserve, not the pooled price.
                if (pl.Use == kind && pl.Level == lvl && !pl.Warehousing && pl.Vacant > 0
                    && Auction.OwnerParcelOf(Auction.DoorOf(pl)) < 0) return pi;
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
        /// has no customers yet. Realized takings are these households.
        ///
        /// Each household also keeps a SHORTLIST — the top ShopShortlist shops
        /// it ranks above its own out-of-town option, best first — so a shop
        /// that cannot serve it (task #20 capacity) hands it back to its own
        /// next-best choice rather than straight out of town.</summary>
        /// <summary>Engine-owned flat shortlist, households × ShopShortlist,
        /// best first. Entry −1 means "no shop here"; a household created since
        /// the last refresh has a whole row of −1 and leaks its basket, which is
        /// what Household.ShopFirm = −1 already meant.</summary>
        private int[] _shopChoices = Array.Empty<int>();
        private double[] _shopBestU = Array.Empty<double>();
        private double[] _shopRemaining = Array.Empty<double>();
        private readonly List<int> _shopActive = new List<int>();
        private double[] _shopTopU = Array.Empty<double>();
        private int[] _shopTopF = Array.Empty<int>();
        private int _shopK;

        /// <summary>Each household's own REALIZED utility of what it settled
        /// for — its chosen shop or the out-of-town option, taste draw included.
        /// What CountShopIntents compares a hypothetical shop against. Exposed
        /// for the harness's probes and checks, which rebuild the counted read
        /// independently of the engine's histogram.</summary>
        public double[] ShopBestUtility => _shopBestU;
        /// <summary>Shortlist stride and the flat shortlist itself, for the
        /// harness's defaults-rule leg: it re-derives every household's own
        /// realized outside utility and asserts nobody was served below it.</summary>
        public int ShopShortlistStride => _shopK;
        public int[] ShopChoices => _shopChoices;
        /// <summary>Each household's shopping ORIGIN cluster as of the last
        /// ChooseShops, or −1. Exposed because it cannot be reconstructed from
        /// outside: AssignWorkplaces runs inside the same RefreshTick, BEFORE
        /// ChooseShops, so an unhoused household's origin at choice time is a
        /// workplace assigned earlier in the same tick and no end-of-tick
        /// snapshot can see it. It is an input the shortlist was built from, not
        /// a verdict about the shortlist.</summary>
        public int[] ShopOrigin => _shopOrigin;
        private int[] _shopOrigin = Array.Empty<int>();
        /// <summary>Money served in each rationing round this tick. Round 0 is
        /// the household's own chosen shop; anything in a later round is a
        /// household improving on its default at its own next-best shop, and a
        /// check asserting the shortlist never places anyone BELOW their default
        /// is vacuous unless these are nonzero.</summary>
        public double[] ShopServedByRound = Array.Empty<double>();
        /// <summary>Per-tick consumption totals, recorded at the end of
        /// ConsumptionFlows: what households were debited, and the two
        /// destinations. The firm-side record (Firm.ServedThisTick) is
        /// accumulated separately, so the two can be compared.</summary>
        public double ConsumptionSpendThisTick, ConsumptionCapturedThisTick, ConsumptionLeakedThisTick;

        /// <summary>Rounds of rationing, clamped to the shortlist depth.</summary>
        private int ShopRounds => Math.Max(1, Math.Min(P.ShopRationingRounds, Math.Max(1, P.ShopShortlist)));

        /// <summary>PROBE-SCOPED (`--shop-stagger N`): households review their
        /// shop choice every N ticks on a household-staggered schedule instead
        /// of all re-choosing every tick. 0 ships. Exists to RE-RUN a stale
        /// measurement: staggering was tried and rejected when it read deaths
        /// 217 -> 680 ("new shops starve before they fill"), but that verdict
        /// predates the honest entry forecast that later solved entrant deaths
        /// by another route, and the anti-synchronization principle and the
        /// measured exception have been in unexamined tension since. Note the
        /// synchronization is already DAMPED by ShopLoyalty (an incumbent
        /// hysteresis band); this arm measures what stagger adds on top.
        /// Stale shortlist rows are safe: ShortlistShop guards dead,
        /// non-commercial and siteless ids, and ConsumptionFlows re-picks a
        /// household whose chosen shop closed.</summary>
        public static int ShopReviewStagger;

        private void ChooseShops()
        {
            int K = Math.Max(1, P.ShopShortlist);
            _shopK = K;
            int need = W.Households.Count * K;
            if (_shopChoices.Length < need) { _shopChoices = new int[need]; }
            // Under stagger the rows of non-reviewing households must SURVIVE
            // the tick — the global clear would erase the very persistence the
            // experiment measures. Recomputed rows are fully overwritten below.
            if (ShopReviewStagger <= 1) Array.Fill(_shopChoices, -1);
            if (_shopBestU.Length < W.Households.Count) _shopBestU = new double[W.Households.Count];
            if (_shopOrigin.Length < W.Households.Count) _shopOrigin = new int[W.Households.Count];
            if (_shopTopU.Length != K) { _shopTopU = new double[K]; _shopTopF = new int[K]; }
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
                if (h.ExitedTick >= 0) { h.ShopFirm = -1; _shopOrigin[h.Id] = -1; continue; }
                // A household whose shop is GONE re-picks immediately whatever
                // its review slot says. Without this the arm does not measure
                // staggering at all, it measures ORPHANING: a shop dies,
                // ConsumptionFlows clears ShopFirm, and the household leaks its
                // whole basket out of town for up to Stagger ticks until its
                // slot comes round — which feeds back as less revenue, more
                // shop deaths, more orphans. Measured with that confound in:
                // capture 93.9 % -> 20.8 %, commercial 71 -> 19 alive, 72
                // deaths. Any verdict on synchronization has to be taken with
                // this closed, or it is a verdict on the confound.
                bool orphaned = (uint)h.ShopFirm >= (uint)W.Firms.Count
                                || W.Firms[h.ShopFirm].Dead
                                || W.Firms[h.ShopFirm].Sector != ZoneKind.Commercial
                                || W.Firms[h.ShopFirm].Parcel < 0;
                if (ShopReviewStagger > 1 && !orphaned
                    && (int)(((ulong)h.Id + (ulong)W.Tick) % (ulong)ShopReviewStagger) != 0)
                    continue;   // keeps last tick's choice, shortlist and bestU
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
                _shopOrigin[h.Id] = origin;

                // THIS household's own realized value of its own default — the
                // out-of-town option INCLUDING its permanent taste for it. Every
                // shop on the shortlist must beat this, not the bare systematic
                // outU: a household with a large positive outside draw would
                // otherwise be handed, in a rationing round, a shop it ranks
                // BELOW its own default. The mechanism may only improve on each
                // individual's default.
                double defaultU = outU + Gumbel((ulong)h.Id * 2246822519UL + 7919UL);
                int prevShop = h.ShopFirm;
                double bestU = defaultU;
                int best = -1;
                // Top-K by utility, best first, ties to the lower firm id. K is
                // 3 by default, so an insertion is a handful of compares.
                for (int r = 0; r < K; r++) { _shopTopU[r] = double.NegativeInfinity; _shopTopF[r] = -1; }
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
                    if (idx[j] == prevShop) u += P.ShopLoyalty;
                    if (u > bestU) { bestU = u; best = idx[j]; }
                    if (u <= defaultU) continue;   // below its own default: never shortlisted
                    for (int r = 0; r < K; r++)
                    {
                        if (u <= _shopTopU[r] && !(u == _shopTopU[r] && idx[j] < _shopTopF[r])) continue;
                        for (int q = K - 1; q > r; q--) { _shopTopU[q] = _shopTopU[q - 1]; _shopTopF[q] = _shopTopF[q - 1]; }
                        _shopTopU[r] = u; _shopTopF[r] = idx[j];
                        break;
                    }
                }
                h.ShopFirm = best;
                // Position 0 is the argmax (the head of the list is
                // Household.ShopFirm, so the loyalty term and the closed-shop
                // reset in ConsumptionFlows keep working unchanged); positions
                // 1.. are the fallbacks a rationing round may reach.
                int at = h.Id * K;
                for (int r = 0; r < K; r++) _shopChoices[at + r] = _shopTopF[r];
                // What this household actually GETS: the shop it picked or the
                // out-of-town option, including its own permanent taste for
                // whichever won. This is the counted-intent probe's threshold —
                // the household's own realized default, the same object the
                // shortlist is cut against, so a counted intent means "this
                // person would really switch" and not "this person would switch
                // if nobody had tastes".
                //
                // The hypothetical shop is given its OWN draw on the other side
                // (AccessState.CountShopIntents), so both sides carry one.
                // Measured, `shopprobe --seeds 2 --service 1e9` at task #20,
                // against realized presented per filled slot of 33.3–46.9:
                //  - threshold = the SYSTEMATIC utility of the household's
                //    taste-argmax choice, hypothetical with no draw: read 155.9
                //    per filled slot, a 4.7× over-count — it counts switchers
                //    who would not switch;
                //  - threshold = realized bestU, hypothetical with no draw:
                //    read 0.00 at every cluster, the signal dead — it asks the
                //    newcomer to beat the max of ~130 draws with none of its own;
                //  - both sides systematic: read 0.00 at p90, max 66.2 — with
                //    ~130 similar shops standing, almost nobody's best on size
                //    and distance alone is one more mass-6 store.
                // Only the both-sides-drawn comparison is symmetric, and it is
                // the one this repo already blesses: a count of individual
                // argmaxes, each with that individual's own permanent taste.
                _shopBestU[h.Id] = bestU;
            }
        }

        /// <summary>Re-derive the shop choices and the counted-intent field
        /// without running a tick. Exposed for the same reason
        /// AccessState.RebuildDemandShares is: a check must be able to perturb
        /// ONE input — a cluster's commercial mass, a household's charged
        /// assessment — and re-read the field, instead of stepping a whole tick
        /// and comparing two worlds that have diverged for other reasons.</summary>
        public void RebuildShopSignals()
        {
            ChooseShops();
            Access.CountShopIntents(W, P, _shopBestU);
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
                    {
                        double w = wageByClass[cl] * (f.FilledByClass[cl] / filledTotals[cl]);
                        f.Money -= w;
                        f.OperatingCostThisTick += w;   // avoidable: the pooled-path wage bill
                        f.PayrollThisTick += w;
                    }
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
                f.OperatingCostThisTick += bill;   // avoidable: the labor-auction payroll
                f.PayrollThisTick += bill;
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
            double totalCaptured = 0, totalLeaked = 0, totalSpend = 0;
            double wOutside = Math.Exp(-P.ThetaShopping * P.OutsideShopMinutes) * P.OutsideShopMass;
            bool perStore = Flags.StoreLevelSpending;
            if (perStore)
            {
                if (_shopRemaining.Length < W.Households.Count) _shopRemaining = new double[W.Households.Count];
                _shopActive.Clear();
                foreach (var f in W.Firms)
                {
                    // Dead firms have their per-tick scratch cleared too. A firm
                    // that dies keeps whatever it last served forever otherwise,
                    // and anything summing the sector's takings then double-counts
                    // every shop that ever closed — measured as a 7.8E-2 to
                    // 1.6E-1 gap against what households were debited.
                    if (f.Sector != ZoneKind.Commercial) continue;
                    f.RevenueThisTick = 0; f.PresentedThisTick = 0; f.ServedThisTick = 0;
                    if (f.Dead) { f.RemainingCapacity = 0; continue; }
                    // What this shop can SERVE this tick: its own technology at
                    // its own building, times the staff it actually has. The
                    // same linear-in-labor form the other three sectors use
                    // (ProductionAndTrade); commercial was the only hole, which
                    // is why the labor auction had to cap its doors with an EMA
                    // of realized takings instead of a marginal product.
                    f.RemainingCapacity = f.Parcel >= 0
                        ? CommercialServiceCapacity(f, W.Parcels[f.Parcel], P) : 0;
                }
            }

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
                totalSpend += spend;

                if (!perStore)
                {
                    int c = h.HomeParcel >= 0 ? W.Parcels[h.HomeParcel].Cluster : 0;
                    double capShare = Access.IncumbentShopWeight.Length > c
                        ? 1.0 - wOutside / Access.IncumbentShopWeight[c] : 0.5;
                    totalCaptured += spend * capShare;
                    totalLeaked += spend * (1 - capShare);
                    continue;
                }

                // Its own shop serves what it can, then this household's own
                // next-best shop, then out of town. The rounds are run after
                // this loop so the pro-rata ratio is computed from totals before
                // anyone is served — nobody loses a basket to whoever had a
                // lower household id.
                if ((uint)h.ShopFirm < (uint)W.Firms.Count)
                {
                    var cand = W.Firms[h.ShopFirm];
                    if (cand.Dead || cand.Sector != ZoneKind.Commercial || cand.Parcel < 0)
                        h.ShopFirm = -1;      // it closed; re-picked next refresh
                }
                _shopRemaining[h.Id] = spend;
                _shopActive.Add(h.Id);
            }
            if (perStore) RationShopping(ref totalCaptured, ref totalLeaked);

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
                else if (!Flags.CommercialLines)
                    foreach (var f in W.Firms)
                    {
                        if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                        double share = f.JobSlots
                            * Access.CaptureIncumbentPerMass[W.Parcels[f.Parcel].Cluster] / weightSum;
                        f.Money += totalCaptured * share;
                        f.RevenueThisTick = totalCaptured * share;
                    }
                else
                {
                    // ONE POOL PER LINE. Each line's spending is distributed
                    // only among the shops that sell it, weighted the same way
                    // the single pool always was. This is what makes a niche
                    // pay: a line with one shop in a catchment hands that shop
                    // the line's whole local spend, where under one pool it
                    // would have taken a slots-share of everything and been
                    // indistinguishable from a grocer.
                    //
                    // A line nobody sells is captured by nobody and LEAKS,
                    // which is the honest outcome — the money goes out of town
                    // because the town does not stock the good. Reported into
                    // the same leak total the pooled path already keeps, so
                    // conservation is unchanged.
                    int lines = ResourceCatalog.Basket.Length;
                    double basketAll = 0;
                    foreach (var (_, sh) in ResourceCatalog.Basket) basketAll += sh;
                    foreach (var f in W.Firms)
                        if (!f.Dead && f.Sector == ZoneKind.Commercial && f.Parcel >= 0)
                            f.RevenueThisTick = 0;
                    for (int q = 0; q < lines; q++)
                    {
                        double lineShare = basketAll > 1e-12 ? ResourceCatalog.Basket[q].share / basketAll : 0;
                        double linePool = totalCaptured * lineShare;
                        if (linePool <= 0) continue;
                        double lw = 0;
                        foreach (var f in W.Firms)
                        {
                            if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                            if (f.Retail != Res.Services && AccessState.LineOf(f.Retail) != q) continue;
                            lw += f.JobSlots * Access.CaptureLine(q, W.Parcels[f.Parcel].Cluster);
                        }
                        if (lw <= 1e-9) { totalLeaked += linePool; totalCaptured -= linePool; continue; }
                        foreach (var f in W.Firms)
                        {
                            if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                            if (f.Retail != Res.Services && AccessState.LineOf(f.Retail) != q) continue;
                            double share = f.JobSlots
                                * Access.CaptureLine(q, W.Parcels[f.Parcel].Cluster) / lw;
                            f.Money += linePool * share;
                            f.RevenueThisTick += linePool * share;
                        }
                    }
                }
            }
            ConsumptionSpendThisTick = totalSpend;
            ConsumptionCapturedThisTick = totalCaptured;
            ConsumptionLeakedThisTick = totalLeaked;
            W.Ledger.Transfer(Account.Households, Account.OutsideWorld, totalLeaked);
            W.Ledger.Transfer(Account.Households, Account.Firms, totalCaptured);
        }

        /// <summary>What a commercial firm can serve this tick: its own
        /// technology (CommercialServicePerSlot) applied to its own roster
        /// (WorkersFilled, recomputed each tick by FillFirm from the real people
        /// placed there) at its own building (condition, level quality). Every
        /// term is a fact about this firm; nothing about other shops, the sector
        /// or the city enters. Units of Res.Services, which sits at the numeraire
        /// anchor 1.0, so this is money of sales per tick.</summary>
        public static double CommercialServiceCapacity(Firm f, Parcel pl, EconParams p)
            => p.CommercialServicePerSlot * f.WorkersFilled
               * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) / p.Quality(1);

        /// <summary>Rationing: what happens to custom a shop cannot serve.
        ///
        /// Each round, every household with money left presents it at the shop
        /// sitting at that position on its OWN shortlist; each shop then serves
        /// pro-rata to what it can. Pro-rata WITHIN a round is what keeps this
        /// free of the defect the housing auction exists to prevent — the ratio
        /// is computed from round totals before anyone is served, so nobody
        /// loses a basket to whoever had a lower household id. WHICH of a full
        /// shop's customers gets served is below this mechanism's resolution,
        /// exactly as within-cluster pro-rata is the goods statistic's grain.
        ///
        /// Rank priority across rounds IS an allocation rule and is stated
        /// rather than hidden: a shop serves its first-choice customers before
        /// another household's second choice, because round 0 runs first. It is
        /// order-free in household id, which is the property that matters.
        ///
        /// Households never learn a shop was full. The shopping logit gains no
        /// congestion term: a congestion field computed by the system and read
        /// inside an individual's choice is the shape the charter forbids. The
        /// legitimate version is the household's OWN experience of being
        /// rationed, and it is not added speculatively — anything that slows
        /// catchment adjustment has measured form here (staggered review took
        /// commercial deaths 217 → 680).</summary>
        private void RationShopping(ref double totalCaptured, ref double totalLeaked)
        {
            int K = ShopRounds, S = Math.Max(1, _shopK);
            if (ShopServedByRound.Length != K) ShopServedByRound = new double[K];
            Array.Clear(ShopServedByRound, 0, K);
            for (int r = 0; r < K; r++)
            {
                foreach (var f in W.Firms)
                    if (!f.Dead && f.Sector == ZoneKind.Commercial) f.RoundPresented = 0;
                // Pass 1: who shows up at whose door this round.
                foreach (int hid in _shopActive)
                {
                    double rem = _shopRemaining[hid];
                    if (rem <= 0) continue;
                    var shop = ShortlistShop(hid, r, S);
                    if (shop == null) continue;
                    shop.RoundPresented += rem;
                    shop.PresentedThisTick += rem;
                }
                // Pass 2: each shop's ratio, from THIS round's totals, fixed
                // BEFORE anybody is served. That is the whole point of pro-rata
                // and it has to be a separate pass: recomputing the ratio per
                // customer against a capacity that is already being drawn down
                // makes the split order-dependent — the household the loop
                // reaches last gets a smaller share than the one it reached
                // first, which is the defect the housing auction exists to
                // prevent, and it leaves the shop short of its own capacity
                // (measured: served peaked at 0.9985 of capacity where 1.0 was
                // available). It also masked a capacity mutant: at 1.5x capacity
                // the bound leg still read zero violations.
                //
                // RoundPresented, not PresentedThisTick: the latter is the
                // tick-cumulative figure the labor cap consumes, and dividing
                // capacity by it would under-serve every round after the first.
                foreach (var f in W.Firms)
                    if (!f.Dead && f.Sector == ZoneKind.Commercial)
                        f.RoundRatio = f.RoundPresented <= 0 || f.RemainingCapacity >= f.RoundPresented
                                       ? 1.0 : f.RemainingCapacity / f.RoundPresented;
                // Pass 3: serve.
                foreach (int hid in _shopActive)
                {
                    double rem = _shopRemaining[hid];
                    if (rem <= 0) continue;
                    var shop = ShortlistShop(hid, r, S);
                    if (shop == null) continue;
                    double ratio = shop.RoundRatio;
                    // One double, five uses. Never credit a shop from a
                    // separately-computed aggregate (ratio × presented): that is
                    // where a rounding residue would strand, and the shop's
                    // takings are meant to be literally the sum of its own
                    // customers' money.
                    double served = Math.Min(rem * ratio, shop.RemainingCapacity);
                    if (served <= 0) continue;
                    shop.Money += served;
                    shop.RevenueThisTick += served;
                    shop.ServedThisTick += served;
                    shop.RemainingCapacity -= served;
                    _shopRemaining[hid] -= served;
                    totalCaptured += served;
                    ShopServedByRound[r] += served;
                }
            }
            // Whatever no shop on this household's own shortlist could serve
            // takes the household's own default: the out-of-town option.
            foreach (int hid in _shopActive) totalLeaked += _shopRemaining[hid];
        }

        private Firm? ShortlistShop(int hid, int rank, int stride)
        {
            int fi = rank == 0 ? W.Households[hid].ShopFirm
                   : (uint)(hid * stride + rank) < (uint)_shopChoices.Length
                     ? _shopChoices[hid * stride + rank] : -1;
            if ((uint)fi >= (uint)W.Firms.Count) return null;
            var f = W.Firms[fi];
            return !f.Dead && f.Sector == ZoneKind.Commercial && f.Parcel >= 0 ? f : null;
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
                HouseholdLevyOwedThisTick += owed; HouseholdLevyPaidThisTick += pay;
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
                        // Level premium: the same Quality(ℓ) term the extractor
                        // BID is assessed on (LandAccounting.FirmBidPerSlot).
                        // Without it the land is charged for a level premium
                        // the production function does not deliver, which is
                        // the assessment pricing a configuration that does not
                        // exist. Industrial and commercial already carry it.
                        f.OutputThisTick = f.WorkersFilled * P.ExtractorOutputPerSlot * suit * cond
                                           * ((P.NonResLandParity || P.UniformSiteProductivity)
                                              && !MutantLevelFreeFirmOutput
                                              ? P.Quality(pl.Level) / P.Quality(1) : 1.0);
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
                        // Restocking: the consumption basket behind captured
                        // spending — units forecast at the price THIS store's
                        // deliveries actually cost (its cluster's realized
                        // delivered statistic; 0.5 floor kept).
                        // A specialized shop restocks ONE line, at that line's
                        // full basket weight — it is selling only that good, so
                        // all of its takings are that good's takings. A
                        // whole-basket shop restocks all four as before.
                        double basketAll = 0;
                        foreach (var (_, sh) in ResourceCatalog.Basket) basketAll += sh;
                        foreach (var (res, share) in ResourceCatalog.Basket)
                        {
                            bool sells = f.Retail == Res.Services || f.Retail == res;
                            if (!sells) { f.InputNeedByRes[(int)res] = 0; continue; }
                            double w = f.Retail == Res.Services ? share : basketAll;
                            double need = f.RevenueThisTick * w / Math.Max(0.5, Trade.DeliveredStat(res, c));
                            f.InputNeedByRes[(int)res] = need;
                            _demandByCluster[(int)res][c] += need;
                            _demandTotal[(int)res] += need;
                        }
                        break;
                    }
                    case ZoneKind.Office:
                    {
                        // Condition and level premium, the same two terms the
                        // office BID is assessed on and the same two the other
                        // three sectors' production already carries. Missing
                        // them, an office was billed Quality(ℓ)/Quality(1) more
                        // than it could ever earn: measured at 88fc88c
                        // (parityprobe seed 1, 400 ticks) the office sector's
                        // bill was 489/tick against 428 of GROSS revenue, and
                        // 54 of 62 offices had been short for 200+ consecutive
                        // ticks.
                        // The firm's OWN specialization's localization, not the
                        // pooled one. This is where the maximum the assessment
                        // took over kinds becomes a number some particular firm
                        // has to earn: an office that committed to the right
                        // kind for its site realizes what it was billed for, and
                        // one whose neighbourhood moved under it does not.
                        // NOTE the term this gate carries: cond AND the level
                        // premium. With the gate shut an office's product
                        // ignores the CONDITION of its own building, which no
                        // other sector does — a derelict tower produces exactly
                        // what a new one does. UniformSiteProductivity opens it
                        // unconditionally, which is what puts office on the
                        // same footing as the other three.
                        double rev = f.WorkersFilled * P.OfficeOutputPerSlot * P.OfficeOutputPrice
                                     * Access.OfficeAgglom(f.Office, c, P)
                                     * ((P.NonResLandParity || P.UniformSiteProductivity)
                                        && !MutantLevelFreeFirmOutput
                                        ? cond * P.Quality(pl.Level) / P.Quality(1) : 1.0);
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
                if (Flags.TierD_Trade)
                {
                    var clear = Trade.ClearTick(res, _supplyTotal[r], _demandTotal[r],
                                                _supplyByCluster[r], _demandByCluster[r], P);
                    SettleResourceClustered(res, clear);
                }
                else
                {
                    var clear = FlatClear(res, _supplyTotal[r], _demandTotal[r]);
                    SettleResourceFlat(res, clear, _supplyTotal[r], _demandTotal[r]);
                }
            }
        }

        /// <summary>Vanilla-style flat-price fallback (TierD off): infinite depth
        /// at the world anchor.</summary>
        private ClearResult FlatClear(Res r, double supply, double demand)
        {
            double anchor = ResourceCatalog.Anchor[(int)r];
            var res = new ClearResult();
            double surplus = supply - demand;
            if (surplus > 0) { res.Exported = surplus; res.ExportRevenue = surplus * anchor; }
            else { res.Imported = -surplus; res.ImportCost = -surplus * anchor; }
            return res;
        }

        /// <summary>Money settlement for one resource on the cluster-identified
        /// clearing (task #30): per-cluster pro-rata over REALIZED flows.
        /// Buyers at cluster c are debited what buyers there actually paid
        /// (DeliveredPaid[c]) by input-need share WITHIN c; sellers at c are
        /// credited what sellers there actually netted (OriginRev[c]) by output
        /// share within c. Local freight is a real cost paid to OutsideWorld
        /// like every other haul flow (RouteStructureCharge precedent) — local
        /// trades no longer ship for free. Conservation is per-lot by
        /// construction: Σc OriginRev − Σc DeliveredPaid = ExportRevenue −
        /// ImportCost − LocalFreight holds exactly.
        ///
        /// Two consequences, stated rather than hidden: (a) unsold pain
        /// localizes — a remote producer's unsold output is ITS cluster's lost
        /// revenue, not a citywide haircut; (b) WHICH firm within a cluster
        /// sold the unsold lot is below the mechanism's resolution —
        /// within-cluster pro-rata is the statistic's grain.</summary>
        private void SettleResourceClustered(Res r, ClearResult clear)
        {
            int ri = (int)r;
            var deliveredPaid = Trade.TickDeliveredPaid(r);
            var originRev = Trade.TickOriginRev(r);
            var supBy = _supplyByCluster[ri];
            var demBy = _demandByCluster[ri];
            var rec = TradeSystem.SettleTelemetry != null ? Trade.CurrentRecord : null;
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                int c = W.Parcels[f.Parcel].Cluster;
                if (f.Output == r && f.OutputThisTick > 0 && originRev[c] > 0 && supBy[c] > 1e-9)
                {
                    double credit = originRev[c] * (f.OutputThisTick / supBy[c]);
                    f.Money += credit;
                    f.RevenueThisTick += credit;
                    if (rec != null) rec.FirmCredit += credit;
                }
                if (f.InputNeedByRes[ri] > 0 && deliveredPaid[c] > 0 && demBy[c] > 1e-9)
                {
                    double debit = deliveredPaid[c] * (f.InputNeedByRes[ri] / demBy[c]);
                    f.Money -= debit;
                    f.OperatingCostThisTick += debit;   // avoidable: inputs bought to produce

                    if (rec != null) rec.FirmDebit += debit;
                }
            }
            W.Ledger.Transfer(Account.OutsideWorld, Account.Firms, clear.ExportRevenue);
            W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, clear.ImportCost);
            W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, clear.LocalFreight);
        }

        /// <summary>Flat-path settlement (TierD off): citywide pro-rata at the
        /// anchor, exactly the pre-localization behavior — the vanilla fallback
        /// world is unchanged by task #30.</summary>
        private void SettleResourceFlat(Res r, ClearResult clear, double supply, double demand)
        {
            double price = ResourceCatalog.Anchor[(int)r];
            double localVolume = Math.Min(supply - clear.Exported - clear.Unsold, demand - clear.Imported);
            localVolume = Math.Max(0, localVolume);

            double sellerRevenue = localVolume * price + clear.ExportRevenue;
            double buyerCost = localVolume * price + clear.ImportCost;

            // BOTH LOOPS TEST Parcel, as SettleResourceClustered above already
            // does. They did not, and that made them the exception to
            // "every firm loop skips a firm with no site" — the premise task
            // #49 was written on. `supply` and `demand` here are _supplyTotal
            // and _demandTotal, both accumulated in ProductionAndTrade over
            // SITED firms only, so a siteless firm taking a share of either
            // makes the shares sum past 1 against a ledger posting that did
            // not move: money appears or disappears off-ledger. The buyer half
            // was the live one — InputNeedByRes is cleared only inside
            // ProductionAndTrade's own Parcel-guarded loop, so a siteless firm
            // kept paying for inputs to a factory it no longer had. Fixed at
            // the source too (DisplaceFirm clears the flows), and here as well,
            // because a guard that matches its sibling is what stops the next
            // reader having to re-derive which of the two is authoritative.
            if (supply > 1e-9 && sellerRevenue > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Output != r || f.OutputThisTick <= 0) continue;
                    double share = f.OutputThisTick / supply;
                    f.Money += sellerRevenue * share;
                    f.RevenueThisTick += sellerRevenue * share;
                }
            if (demand > 1e-9 && buyerCost > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.InputNeedByRes[(int)r] <= 0) continue;
                    double inputCost = buyerCost * (f.InputNeedByRes[(int)r] / demand);
                    f.Money -= inputCost;
                    f.OperatingCostThisTick += inputCost;   // avoidable: inputs, flat path
                }
            W.Ledger.Transfer(Account.OutsideWorld, Account.Firms, clear.ExportRevenue);
            W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, clear.ImportCost);
        }

        private void FirmLifecycle()
        {
            // A firm that holds no site is resolved BEFORE the loop below,
            // because the loop below is one of the many that cannot see it.
            ResolveDisplacedFirms();

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
                    // OWED, not `pay`. The line above takes only what is there,
                    // so realized rent can never fall short and a margin built
                    // on it would read a zero shortfall precisely when the firm
                    // is broke. What a firm cannot afford is the thing that
                    // ends it, so the margin is charged the full bill.
                    f.OperatingCostThisTick += owed;
                    FirmLevyOwedThisTick += owed; FirmLevyPaidThisTick += pay;
                    f.LevyOwedCum += owed; f.LevyPaidCum += pay;
                    if (pay < owed - 1e-9) f.LevyShortTicks++; else f.LevyShortTicks = 0;
                    double sPaid = Math.Min(pay, sOwed);
                    pl.PaidTickS += sPaid;
                    double structPaid = Math.Min(pay - sPaid, structTaxOwed);
                    RouteFirmStructureCharge(pl, sPaid);
                    if (structPaid > 0) W.Ledger.Transfer(Account.Firms, Account.Treasury, structPaid);
                    RouteLandCharge(pl, pay - sPaid - structPaid, fromFirm: true);
                }
                f.ProfitEma = MathUtil.Ema(f.ProfitEma, f.RevenueThisTick, 0.05);
                if (Flags.StoreLevelSpending && f.Sector == ZoneKind.Commercial)
                {
                    // Rate inherited from ProfitEma's 0.05 — no new parameter.
                    // The FIRST strictly-positive observation seeds the EMA
                    // instead of being averaged against a zero history the firm
                    // never lived (Firm.PresentedObserved records why).
                    f.PresentedEma = f.PresentedObserved
                        ? MathUtil.Ema(f.PresentedEma, f.PresentedThisTick, 0.05)
                        : f.PresentedThisTick;
                    if (f.PresentedThisTick > 0) f.PresentedObserved = true;
                }
                // THE EXIT MARGIN CLOSES HERE, and only here. Every revenue and
                // every avoidable cost for this tick has landed by this line —
                // wages and inputs earlier in the tick, the land charge a few
                // lines above — and the worker-collective dividend below has
                // not yet fired, which is what keeps a DISTRIBUTION of surplus
                // out of a measure of whether there is any.
                double cashFlow = f.RevenueThisTick - f.OperatingCostThisTick;
                if (!f.CashFlowObserved) { f.CashFlowEma = cashFlow; f.CashFlowObserved = true; }
                else
                {
                    // Dispersion FIRST, against the prior mean — the deviation
                    // of this tick from what the firm expected before it. Same
                    // 0.05 as the mean it is measured against, so the two
                    // series see the same window and neither leads the other.
                    f.CashFlowMadEma = MathUtil.Ema(
                        f.CashFlowMadEma, Math.Abs(cashFlow - f.CashFlowEma), 0.05);
                    f.CashFlowEma = MathUtil.Ema(f.CashFlowEma, cashFlow, 0.05);
                }
                f.OperatingCostThisTick = 0;
                f.PayrollLastTick = f.PayrollThisTick; f.PayrollThisTick = 0;

                f.GrossRevenueLastTick = f.RevenueThisTick;
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

                // THE EXIT MARGIN (Flags.FirmExitMargin — ON by default since
                // the package flip; the flag's own comment carries the history).
                // Dixit: a firm leaves when
                // revenue persistently fails to cover its AVOIDABLE costs, and
                // the sunk ones are irrelevant to that decision. Here they are
                // not merely irrelevant, they are absent — a firm owns nothing
                // and forfeits nothing by leaving — so there is no liquidation
                // cost to build an inaction band from. The band is the clock's
                // asymmetry instead: CashFlowShortTicks rises on a bad tick and
                // FALLS on a good one, so a firm cannot flap on one bad tick
                // and cannot be rehabilitated by one good one.
                //
                // PATIENCE IS THE FIRM'S OWN, AND SCALES. A firm treats its
                // working-capital reserve as untouchable, so the firm that
                // holds more of it can wait longer — its own arithmetic over
                // its own roster, not a citywide clock. Flooring at
                // FirmSeedCapital makes the ratio at least 1, so an entrant
                // gets the base patience rather than none.
                //
                // NO STAGE CUTS STAFF, deliberately. The obvious middle stage —
                // shrink the payroll — cannot be written here without encoding
                // the income-per-member hiring rule LaborAuction refuses by
                // name (see its header: LaborWardMutant exists so the refusal
                // check can prove it would notice one). A firm has no door
                // lever in any case: doors are JobSlots x mix and JobSlots is
                // the building's unit count. And where production is linear in
                // labor, shedding a worker sheds its output too, so the margin
                // does not move. The middle stage is MOVING, which is the one
                // thing a firm here can actually do about its costs.
                // A SHOP THAT HAS NEVER CHOSEN A LINE CHOOSES ONE. Retailers
                // re-merchandise; a shop is not born knowing what it sells and
                // is not stuck with it forever if it never decided. Without
                // this the mechanism reaches almost nobody: the line is picked
                // at entry, and mid-run commercial entry is about ONE firm per
                // run (shopsweep's own cohort leg reads 1), so essentially
                // every shop in the city is seeded at t = 0 and would keep the
                // whole basket forever. Measured before this existed: 56 live
                // shops, 56 of them still whole-basket.
                //
                // STAGGERED ON THE SAME MEMORYLESS HAZARD the two relocation
                // paths above use, and for a sharper reason than tidiness. At
                // t = 0 every shop sells everything, so every line looks
                // equally served and the argmax is decided by delivered cost
                // alone — let them all choose at once and a whole cluster
                // commits to the same line in the same tick, which is the
                // simultaneity trap that would hand back exactly the
                // one-company-type world this is meant to end. Choosing a few
                // at a time lets each chooser see what the last one did.
                if (Flags.CommercialLines && f.Sector == ZoneKind.Commercial
                    && f.Retail == Res.Services
                    && W.Rng.NextDouble() < 1.0 / Math.Max(1, P.MoveSearchPeriod))
                {
                    LandAccounting.FirmBidPerSlot(
                        Access, Trade, pl.Cluster, ZoneKind.Commercial, pl.Level, P,
                        out Res pick, W.Clusters,
                        pl.Units * Math.Max(0.2, pl.Condition) * P.Quality(pl.Level),
                        pl.Units, pl.Condition);
                    if (pick != Res.Services) { f.Retail = pick; FirmRetailChoicesTotal++; }
                }

                // RETOOLING (Flags.FirmRetooling — the flag's comment carries
                // the design). The firm re-asks the SAME question it answered
                // at entry — which recipe pays best from THIS site — with its
                // own current staffing and its own site's productivity, and
                // acts only when the gain beats the annuitized machinery bill.
                // The wage drops out of the DIFFERENCE (same labor mix across
                // recipes), so the comparison is over gross per-slot margins.
                if (Flags.FirmRetooling && f.Sector == ZoneKind.Industrial
                    && f.Parcel >= 0 && f.WorkersFilled > 0
                    && W.Rng.NextDouble() < 1.0 / Math.Max(1, P.MoveSearchPeriod))
                {
                    int rc2 = pl.Cluster;
                    double ownM = double.NaN, bestM = double.NegativeInfinity;
                    Res bestOut = f.Output;
                    foreach (var recipe in ResourceCatalog.Recipes)
                    {
                        double outNet = Math.Max(
                            P.NonResLandParity ? Trade.OriginComparable(recipe.Output, rc2)
                                               : Trade.OriginStat(recipe.Output, rc2),
                            Trade.BestExportNet(recipe.Output, rc2));
                        double ic = 0;
                        foreach (var (res, qty) in recipe.Inputs)
                            ic += qty * Trade.DeliveredCost(res, rc2);
                        double m = recipe.OutputPerSlot * P.RecipeOutputScale * (outNet - ic);
                        if (recipe.Output == f.Output) ownM = m;
                        if (m > bestM) { bestM = m; bestOut = recipe.Output; }
                    }
                    if (bestOut != f.Output && !double.IsNaN(ownM))
                    {
                        // Gain at the firm's own staffing and its own site's
                        // realized productivity (industrial production is
                        // cond x Quality(l)/Quality(1), unconditionally).
                        double siteF = Math.Max(0.2, pl.Condition)
                                       * P.Quality(pl.Level) / P.Quality(1);
                        double gainPerTick = (bestM - ownM) * f.WorkersFilled * siteF;
                        double retoolFlow = Annuity.FlowOf(P.FirmSeedCapital, P.HurdleRate, P.AnnuityHorizon);
                        double wagesNow = 0;
                        for (int cl = 0; cl < 3; cl++)
                            wagesNow += f.FilledByClass[cl] * P.Wage((LaborClass)cl);
                        double reserve2 = Math.Max(P.FirmSeedCapital, wagesNow * P.FirmWorkingCapitalTicks);
                        bool afford = f.Money >= reserve2 + P.FirmSeedCapital;
                        if (MutantRetoolFree || (gainPerTick > retoolFlow && afford))
                        {
                            if (!MutantRetoolFree)
                            {
                                f.Money -= P.FirmSeedCapital;
                                // Machinery is bought from outside the region,
                                // like every capital good here.
                                W.Ledger.Transfer(Account.Firms, Account.OutsideWorld, P.FirmSeedCapital);
                            }
                            f.Output = bestOut;
                            Array.Clear(f.InputNeedByRes, 0, f.InputNeedByRes.Length);
                            FirmRetoolsTotal++;
                        }
                    }
                }

                bool marginOn = Levying && Flags.FirmExitMargin && !MutantFirmExitNoMargin;
                int patience = P.InsolvencyGraceTicks;
                if (marginOn)
                {
                    // THE INACTION BAND IS THE FIRM'S OWN DISPERSION, not zero.
                    // Dixit's trigger is not "expected profit is negative", it
                    // is "negative by more than the option value of waiting" —
                    // and with nothing to liquidate, that option value comes
                    // entirely from the VARIANCE of the forecast. A firm whose
                    // cash flow wobbles by tens either way and whose mean sits
                    // at −0.9 has a live upside and rationally waits; a firm
                    // whose mean sits at −260 with the same wobble does not.
                    //
                    // The clock's asymmetry (increment bad, decrement good)
                    // was written to be that band and CANNOT BE, because what
                    // it reads is an EMA at 0.05: a mean that has gone
                    // negative essentially never flips back tick to tick, so
                    // the decrement branch is unreachable for exactly the
                    // marginal firm it was meant to protect. Measured, before
                    // this existed: the combined labor+margin arm exited 95
                    // firms on seed 1 and 105 on seed 9, taking extractors
                    // whose median cash flow was −0.86 against a wage bill in
                    // the hundreds — a tenth of a percent of their cost base.
                    // Vacancy went 13.5 % → 42.6 %.
                    //
                    // MadEma is the firm's own history of its own deviations,
                    // so this adds no global and no tuned constant: the
                    // comparison is against ONE mean-absolute-deviation, i.e.
                    // "this loss is outside my ordinary range of variation."
                    double band = MutantFirmNoInactionBand ? 0.0 : f.CashFlowMadEma;
                    if (f.CashFlowObserved && f.CashFlowEma < -band) f.CashFlowShortTicks++;
                    else if (f.CashFlowShortTicks > 0) f.CashFlowShortTicks--;

                    double wageBillNow = 0;
                    for (int cl = 0; cl < 3; cl++)
                        wageBillNow += f.FilledByClass[cl] * P.Wage((LaborClass)cl);
                    double reserve = Math.Max(P.FirmSeedCapital, wageBillNow * P.FirmWorkingCapitalTicks);
                    double patienceScale = MutantFirmFlatPatience
                        ? 1.0 : reserve / Math.Max(1e-9, P.FirmSeedCapital);
                    // The household template's shape (0.5 + own/scale), clamped
                    // into the two clocks that already ship so this adds no new
                    // measured constant of its own.
                    patience = (int)MathUtil.Clamp(
                        P.InsolvencyGraceTicks * (0.5 + 0.5 * patienceScale),
                        P.InsolvencyGraceTicks, P.LandArrearsTicks);

                    // SEEK A CHEAPER SITE FIRST. The firm's default once it
                    // cannot carry this site is to be gone, so a move has to
                    // beat leaving on the firm's own forecast — which is what
                    // RelocateFirm tests. Same memoryless hazard the arrears
                    // path uses, so this is a distribution over ticks and not a
                    // citywide stampede, and it costs a scan only for firms
                    // actually under water.
                    if (f.CashFlowShortTicks >= patience
                        && f.CashFlowShortTicks < 2 * patience
                        && W.Rng.NextDouble() < 1.0 / Math.Max(1, P.MoveSearchPeriod)
                        && RelocateFirm(f, pl))
                    {
                        pl = W.Parcels[f.Parcel];
                        // Half the clock back, not all of it: the move is a
                        // real improvement on the firm's own numbers, but the
                        // business that could not carry the old site has not
                        // been proven viable yet, and a full reset would let a
                        // firm hop sites forever without ever being asked.
                        f.CashFlowShortTicks = patience / 2;
                        FirmMarginRelocationsTotal++;
                    }
                }
                // EXIT at twice the patience it took to start looking: the firm
                // has had a full patience window to find a site it could carry
                // and either did not look, did not find one, or found one and
                // still could not cover its costs there.
                bool cashFlowOut = marginOn && f.CashFlowObserved
                                   && f.CashFlowShortTicks >= 2 * patience;

                // LAND ARREARS (EconParams.NonResLandParity — OFF by default,
                // and that comment carries the measurement that keeps it off).
                // The levy takes min(money, bill), so it can never by itself
                // push a firm past CompanyBankruptcyLimit, and office and
                // extractor firms carry no input debits either: with the flag
                // off the unpaid remainder is forgiven every tick, forever.
                // With it on, the outcome is the firm analog of the household
                // floor and runs in the household pipeline's own order — grace,
                // then sort down, then release the land.
                //
                // SORT DOWN. A firm's default is its current site, so it moves
                // only where its OWN forecast of surplus beats its own forecast
                // here; the search is a memoryless hazard on the household's
                // own MoveSearchPeriod, so relocation is a distribution rather
                // than a citywide event, and it costs a scan only for the firms
                // actually in arrears.
                if (Levying && P.NonResLandParity && !MutantForgiveFirmArrears
                    && f.LevyShortTicks >= P.InsolvencyGraceTicks
                    && f.LevyShortTicks < P.LandArrearsTicks
                    && W.Rng.NextDouble() < 1.0 / Math.Max(1, P.MoveSearchPeriod)
                    && RelocateFirm(f, pl))
                { pl = W.Parcels[f.Parcel]; f.LevyShortTicks = P.InsolvencyGraceTicks; }

                // RELEASE. Past the clock with nowhere better to go, the firm
                // gives up the land and the entry rule below re-lets it to
                // whichever forecast can carry it.
                bool arrears = Levying && P.NonResLandParity && !MutantForgiveFirmArrears
                               && f.LevyShortTicks >= P.LandArrearsTicks;
                bool brokeCapital = f.Money < P.CompanyBankruptcyLimit;
                if (brokeCapital || arrears || cashFlowOut)
                {
                    f.Dead = true;
                    f.DiedOfArrears = arrears;
                    // Attribution, in the order the causes actually bind: an
                    // arrears release and a cash-flow exit both mean the firm
                    // gave up a site it could not carry, while the working-
                    // capital floor means it ran the balance down. A firm can
                    // satisfy more than one on the same tick, so the flags are
                    // exclusive by this precedence rather than independently
                    // set — a census that double-counts is worse than one that
                    // has to state its tie-break.
                    f.DiedOfCashFlow = cashFlowOut && !arrears;
                    f.DiedOfWorkingCapital = brokeCapital && !arrears && !cashFlowOut;
                    if (f.DiedOfCashFlow) FirmMarginExitsTotal++;
                    if (arrears) { FirmArrearsExitsThisTick++; FirmArrearsExitsTotal++; }
                    double residual = Math.Max(0, f.Money);
                    if (residual > 0) W.Ledger.Transfer(Account.Firms, Account.PhantomBank, residual);
                    else W.Ledger.Transfer(Account.PhantomBank, Account.Firms, -f.Money); // written-off debt
                    f.Money = 0;
                    pl.OccupantFirm = -1;
                }
            }

            // Entry into existing vacant firm parcels.
            //
            // TWO REGIMES. Flag off (the shipping default until measured): a
            // per-parcel Bernoulli — prob = elasticity x excess x units — which
            // is a RATE reading a margin, the same shape the old migration
            // elasticity had before prospects replaced it. Nobody decides:
            // the loop asks "would a firm take THIS parcel", never "which
            // parcel would THIS firm prefer", so no entrant ever compares two
            // sites and two entrants can never contest one good site.
            //
            // Flag on (FirmProspects): the SAME roll at the SAME parcel spawns
            // a LOOKER instead of entering in place. Arrival intensity is
            // therefore unchanged — the elasticity stops deciding entry and
            // only paces arrivals, exactly the household split (prominence
            // paces prospects; the market decides). Each looker then surveys
            // every still-vacant site of its sector with the same forecast
            // arithmetic and takes the ARGMAX if any is positive. Lookers run
            // serially, so a taken site is gone for the next one — the contest
            // households have had since the auction, extended to firms.
            var lookers = Flags.FirmProspects ? new List<ZoneKind>() : null;
            void EnterAt(Parcel epl, Res echosen)
            {
                // The entrant fixes its output here: extractors mine what the
                // geology supports, industry commits to the recipe whose
                // input sourcing is cheapest from THIS location (Weber).
                var firm = new Firm
                {
                    Id = W.Firms.Count, Sector = epl.Use, Parcel = epl.Id,
                    Output = epl.Use == ZoneKind.Commercial ? Res.Services : echosen,
                    // A commercial entrant commits to the line its own
                    // catchment is least well served in, exactly as an
                    // industrial entrant commits to a recipe.
                    Retail = epl.Use == ZoneKind.Commercial && Flags.CommercialLines
                        ? echosen : Res.Services,
                    Money = P.FirmSeedCapital, JobSlots = epl.Units, EnteredTick = W.Tick,
                    // An office entrant commits to the specialization THIS
                    // site's neighbourhood carries best — what makes a re-let
                    // building a different business and not the same one again.
                    Office = epl.Use == ZoneKind.Office
                        ? LandAccounting.BestOfficeKind(Access, epl.Cluster, P, epl.Id) : default,
                };
                W.Firms.Add(firm);
                epl.OccupantFirm = firm.Id;
                W.Ledger.Transfer(Account.PhantomBank, Account.Firms, P.FirmSeedCapital);
            }
            foreach (var pl in W.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.IsResidential || pl.OccupantFirm >= 0) continue;
                if (pl.Use == ZoneKind.None || pl.Warehousing) continue;
                // A firm deciding whether to take THIS building forecasts from
                // the building it would occupy. For commercial that matters
                // twice over, because the entry signal is a demand curve in
                // SIZE: the mass it must be read at is the one ChooseShops
                // scores — this parcel's units × its condition × its level
                // quality — and the per-slot division must be by this parcel's
                // own slots. Reading the cluster reference instead (a 6-slot
                // condition-1 shop) over-predicted the entrant's own catchment
                // 3.4× at the median and killed 92% of mid-run entrants inside
                // 40 ticks; see LandAccounting.FirmBidPerSlot for the run.
                // Other sectors pass nothing and keep the reference.
                double entrantMass = pl.Use == ZoneKind.Commercial
                    ? pl.Units * Math.Max(0.2, pl.Condition) * P.Quality(pl.Level) : 0;
                double bid = LandAccounting.FirmBidPerSlot(Access, Trade, pl.Cluster, pl.Use, pl.Level, P,
                                                           out Res chosen, W.Clusters, out bool condPriced,
                                                           entrantMass, entrantMass > 0 ? pl.Units : 0,
                                                           entrantMass > 0 ? pl.Condition : 0);
                // The generic condition discount, EXCEPT where the bid already
                // carries condition through the catchment and the service
                // ceiling. Charging both prices the same run-down building
                // twice.
                if (!condPriced) bid *= P.CondFactor(pl.Condition);
                double assess = LandAccounting.UnitAssessment(pl, P);
                double excess = bid - assess;
                if (excess <= 0) continue;
                double prob = MathUtil.Clamp(P.FirmEntryElasticity * excess * pl.Units * 10, 0, 0.5);
                if (W.Rng.NextDouble() < prob)
                {
                    if (lookers != null) { lookers.Add(pl.Use); continue; }
                    EnterAt(pl, chosen);
                }
            }
            if (lookers != null)
                foreach (var sector in lookers)
                {
                    Parcel? bestPl = null; Res bestChosen = Res.Services; double bestExcess = 0;
                    foreach (var pl in W.Parcels)
                    {
                        if (pl.State != ParcelState.Built || pl.IsResidential || pl.OccupantFirm >= 0) continue;
                        if (pl.Use != sector || pl.Warehousing) continue;
                        double em = pl.Use == ZoneKind.Commercial
                            ? pl.Units * Math.Max(0.2, pl.Condition) * P.Quality(pl.Level) : 0;
                        double b = LandAccounting.FirmBidPerSlot(Access, Trade, pl.Cluster, pl.Use, pl.Level, P,
                                                                 out Res ch, W.Clusters, out bool cp,
                                                                 em, em > 0 ? pl.Units : 0,
                                                                 em > 0 ? pl.Condition : 0);
                        if (!cp) b *= P.CondFactor(pl.Condition);
                        double ex = b - LandAccounting.UnitAssessment(pl, P);
                        if (ex > bestExcess) { bestExcess = ex; bestPl = pl; bestChosen = ch; }
                    }
                    if (bestPl != null) EnterAt(bestPl, bestChosen);
                }
        }

        /// <summary>The firm side of "re-sort down the price gradient" (§4.2):
        /// move to standing, vacant, same-use land whose charge this firm's own
        /// forecast can carry. The forecast is the firm's own — the entrant
        /// arithmetic it would itself run at a site (bid at the site's level and
        /// condition, less the site's bill) — and it must beat the same
        /// arithmetic AT ITS CURRENT SITE, so the mechanism can only improve on
        /// the firm's default. No citywide statistic enters, and nothing moves
        /// unless a strictly better site exists.
        ///
        /// Cheaper land genuinely exists for firms for the same reason it does
        /// for households: the charge is per parcel and the assessment varies by
        /// cluster (measured, parityprobe seed 1: vacant office land ran from a
        /// bill of 94.5 to 441 against 480 at the occupied median).
        ///
        /// `from` is null for a firm that holds no site. Then the value to beat
        /// is not a site's arithmetic but ZERO — the firm's default once it has
        /// been displaced is to leave, and a site only holds it if the firm's
        /// own forecast there beats leaving. That is the same rule with a
        /// different incumbent, not a second mechanism.</summary>
        private bool RelocateFirm(Firm f, Parcel? from)
        {
            // PRICED AS THIS FIRM'S BUSINESS, not as the best business the site
            // could hold. A relocating firm keeps its output across the move —
            // nothing here reassigns f.Output, and retooling is a separate,
            // flag-gated decision — so the unconstrained argmax valued each
            // candidate by a recipe the firm would never run, and could move a
            // Timber firm onto the city's best Plastics site on the strength of
            // Plastics. The firm's own forecast is the only one it is entitled
            // to; a site that cannot carry its business now bids 0 and it stays.
            double BidAt(Parcel q) => LandAccounting.FirmBidPerSlot(
                    Access, Trade, q.Cluster, q.Use, q.Level, P, out _, W.Clusters,
                    priceAs: f.Output)
                * P.CondFactor(q.Condition) * q.Units
                - LandAccounting.UnitAssessment(q, P) * q.Units;

            double here = from != null ? BidAt(from) : 0.0;
            Parcel? best = null; double bestV = here;
            foreach (var q in W.Parcels)
            {
                if (q.State != ParcelState.Built || q.OccupantFirm >= 0 || q.Warehousing) continue;
                if (q.Use != f.Sector || q.Units <= 0) continue;
                double v = BidAt(q);
                if (v > bestV) { bestV = v; best = q; }
            }
            if (best == null || bestV <= 0) return false;
            if (from != null) from.OccupantFirm = -1;
            best.OccupantFirm = f.Id;
            f.Parcel = best.Id;
            f.JobSlots = best.Units;
            FirmRelocationsTotal++;
            return true;
        }

        /// <summary>Take a live firm's site away. THE single entry point for
        /// displacement, so that every producer of it — the reader when the
        /// game demolishes or unlinks a building underneath a company, and the
        /// non-residential site market when a firm is outbid — leaves the world
        /// in one state that ResolveDisplacedFirms knows how to finish. The
        /// firm and the parcel are unlinked TOGETHER: half-unlinking is the
        /// shape the defect took (EconReader.SyncParcels cleared the parcel's
        /// OccupantFirm on a demolished building and left the firm pointing at
        /// it, so the firm went on reading a Units == 0 ruin and owed nothing on
        /// it, forever).</summary>
        public bool DisplaceFirm(Firm f)
        {
            if (f.Dead || f.Parcel < 0) return false;
            var pl = W.Parcels[f.Parcel];
            if (pl.OccupantFirm == f.Id) pl.OccupantFirm = -1;
            if (MutantHalfUnlinkDisplacement) return true;   // firm keeps naming the site
            f.Parcel = -1;
            // The mark that separates "lost a site it held" from "the adapter
            // cannot place it" (Firm.SiteLostTick carries why that matters).
            // Set HERE and not in the pass, because only the caller knows an
            // economic event happened; the pass sees only the empty field.
            f.SiteLostTick = W.Tick;
            // Per-tick flows belong to the site the firm no longer has. Left
            // standing they are read by loops that do NOT test Parcel —
            // SettleResourceFlat's buyer loop on the TierD_Trade-off path
            // debits from InputNeedByRes, which ProductionAndTrade clears only
            // for SITED firms — so a displaced firm went on paying for inputs
            // to a factory it had lost. Clearing them here is the fix at the
            // source: no stale flow can outlive the site that generated it.
            f.OutputThisTick = 0;
            Array.Clear(f.InputNeedByRes, 0, f.InputNeedByRes.Length);
            return true;
        }

        /// <summary>DISPLACEMENT: every live firm holding no site reaches a
        /// stated outcome, in the tick it loses one.
        ///
        /// This exists because "no outcome" was previously indistinguishable
        /// from "no such firm". Every firm loop in the engine opens
        /// `if (f.Dead || f.Parcel &lt; 0) continue;` — Access.cs:467,
        /// EconomyEngine.cs:770/903/1021/1035/1110/1238/1256/1263/1385/1481/1617/1673,
        /// LaborAuction.cs:317, Trade.cs:221, EconWriter.cs:244 — and the
        /// working-capital exit at the bottom of FirmLifecycle sits INSIDE the
        /// last of those, so a siteless firm could not produce, hire, be
        /// charged, or go bankrupt, while its money stayed on the books. A
        /// missing exit was therefore silent under ledger conservation: the
        /// money balanced precisely because nobody ever asked the firm to leave.
        ///
        /// The outcome follows the defaults rule the household pipeline already
        /// obeys. The default for a firm that has lost its premises is to be
        /// gone — a firm without premises is not a firm, and there is no
        /// analogue of sleeping rough — so the only direction to move from that
        /// default is a site the firm's OWN forecast says beats leaving.
        /// RelocateFirm with a null incumbent is exactly that test, and it is
        /// the same arithmetic the arrears path and a fresh entrant both run.
        ///
        /// It runs at the TOP of FirmLifecycle, before the levy, so a firm
        /// displaced earlier in the tick is charged at whatever site it ends the
        /// tick holding, and never charged for one it does not hold.</summary>
        private void ResolveDisplacedFirms()
        {
            // The mutant restores the pre-fix world wholesale: no pass at all.
            if (MutantDisplacedFirmNoExit) return;
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel >= 0) continue;
                // NOT EVERY SITELESS FIRM WAS DISPLACED. A firm the adapter
                // could not place is still standing in its building, and its
                // default is to stay there; resolving it would move it — or
                // permanently kill it — on the strength of the engine's own
                // ignorance. Counted so the state is never silent, which was
                // the original defect, but not acted on, which would be a
                // worse one. Firm.SiteLostTick carries the full reasoning.
                if (f.SiteLostTick < 0 && !MutantResolveUnplacedFirms)
                { FirmUnplacedSeenTotal++; continue; }
                FirmDisplacedSeenTotal++;
                if (RelocateFirm(f, null))
                {
                    // The clock is a record of failing to pay AT A SITE. The
                    // firm did not fail to pay here; it has not been billed
                    // here yet. Carrying the old site's clock over would let a
                    // displacement finish an arrears sentence the new site
                    // never handed down.
                    f.LevyShortTicks = 0;
                    FirmDisplacedResitedTotal++;
                    continue;
                }
                f.Dead = true;
                f.DiedOfDisplacement = true;
                FirmDisplacedExitsTotal++;
                // Identical settlement to the two exits already in the file:
                // whatever the firm still holds leaves with it, and a negative
                // balance is written off, so the exit does not open a hole in
                // the books. That is NOT the same as saying conservation would
                // have caught the missing exit — measured, it would not, and
                // could not: while a firm is merely stranded its money sits in
                // its own pocket AND in the Firms account, so both records
                // agree and nothing is out of balance. See the DisplacedFirm
                // check for the numbers.
                double residual = Math.Max(0, f.Money);
                if (residual > 0) W.Ledger.Transfer(Account.Firms, Account.PhantomBank, residual);
                else W.Ledger.Transfer(Account.PhantomBank, Account.Firms, -f.Money);
                f.Money = 0;
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
                    h.PlacedByAuction = false;
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
        /// <summary>Cumulative decline exits by households that owned the
        /// parcel they left — the item-#41 self-pricing-distortion telemetry
        /// (read by the harness probes; asserted nowhere).</summary>
        public int OwnerDeclineExitsTotal;
        /// <summary>Cumulative MARKET moves out of an owner-tagged parcel by a
        /// household that did not own it (the solve's move loop only — not
        /// decline or insolvency exits): the tenant side of the same item, an
        /// ask can push out a sitting tenant with no competing bid, and this
        /// is how much of that there is (probe telemetry, asserted
        /// nowhere).</summary>
        public int OwnerParcelTenantVacatesTotal;

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
                    var h = new Household
                    {
                        Id = W.Households.Count, Segment = s, Money = savings,
                        ArrivedTick = W.Tick,
                    };
                    // Owner disposition is a birth draw now (DrawAtBirth,
                    // OwnerMindedShare) rather than this loop's own roll, so
                    // the auction path's arrivals carry it too; the moving
                    // margin keys off the same draw, value unchanged.
                    h.DrawAtBirth(seg, P);
                    h.MovingCostDraw = P.MovingCostMean * (0.4 + 1.2 * W.Rng.NextDouble())
                                       * (h.OwnerMinded ? P.OwnerMovingCostMult : 1.0);
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
                        h.PlacedByAuction = false;
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
