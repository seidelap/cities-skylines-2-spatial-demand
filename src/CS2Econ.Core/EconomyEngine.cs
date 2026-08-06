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
        public int ShelterOccupied;
        public int ShelterCapacity;

        // Telemetry (per tick)
        public double LandRevenueThisTick, IncomeTaxThisTick, ServiceCostThisTick;
        public double[] LandRevenueByCluster = Array.Empty<double>();
        public readonly List<(long tick, int household)> DisplacementExits = new List<(long, int)>();
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
            AnniversariesAndRelocation();
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
            Access.Refresh(W, Costs, P);
            Trade.Refresh(P);

            Array.Clear(SegmentPresence, 0, SegmentPresence.Length);
            foreach (var h in W.Households)
                if (h.ExitedTick < 0) SegmentPresence[h.Segment]++;

            // Employment materialization: fixed per-household draw against the
            // balanced rate — persistent identity, smooth response to rate moves.
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                if (h.HomeParcel < 0 || seg.Participation <= 0) { h.Employed = false; continue; }
                int c = W.Parcels[h.HomeParcel].Cluster;
                double rate = Access.EmploymentRate[(int)seg.Labor][c] * seg.Participation;
                // Epoch-hashed draw: employment persists ~60 ticks, then the job
                // search re-rolls — a bad draw is a spell, not a life sentence.
                // The epoch boundary is offset per household (hashed), so there is
                // no citywide re-roll tick (design §3; scrutiny finding #21).
                long offset = (long)(SplitMix64.Hash((ulong)h.Id * 13UL) % 60UL);
                ulong epoch = (ulong)((W.Tick + offset) / 60);
                h.Employed = SplitMix64.Hash01((ulong)h.Id * 7919UL + epoch * 104729UL + 3) < rate;
            }

            // Seekers = unhoused + sheltered now, EMA-smoothed (expected near-term demand).
            var seekersNow = new double[Segment.Count];
            foreach (var h in W.Households)
                if (h.ExitedTick < 0 && h.HomeParcel < 0) seekersNow[h.Segment]++;
            for (int s = 0; s < Segment.Count; s++)
            {
                seekersNow[s] += LastFlows.ArrivalsBySegment != null ? LastFlows.ArrivalsBySegment[s] * P.RefreshInterval : 0;
                SeekersEma[s] = MathUtil.Ema(SeekersEma[s], seekersNow[s], 0.15);
            }

            Residuals.Refresh(W, Access, Trade, SeekersEma, P);

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

        private void IncomeAndTaxes()
        {
            // Households receive wages/transfers; firms are charged their wage
            // bill pro-rata to filled slots (exact conservation on the household side).
            var wageByClass = new double[3];
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                double wage = h.Employed ? P.Wage(seg.Labor) : 0;
                double transfer = seg.Transfer;
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
            f.WorkersFilled = 0;
            for (int cl = 0; cl < 3; cl++)
            {
                f.FilledByClass[cl] = f.JobSlots * mix[cl] * Access.JobFillRate[cl][c];
                f.WorkersFilled += f.FilledByClass[cl];
            }
        }

        private void ConsumptionFlows()
        {
            // Spending = share of income after taxes-and-housing; captured share
            // goes to commercial firms (refresh-vintage capture shares, normalized
            // for exact conservation), the rest leaks to the outside option.
            double totalCaptured = 0, totalLeaked = 0;
            double wOutside = Math.Exp(-P.ThetaShopping * P.OutsideShopMinutes) * P.OutsideShopMass;
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0) continue;
                var seg = Segment.All[h.Segment];
                double income = (h.Employed ? P.Wage(seg.Labor) * (1 - P.IncomeTax(seg.Labor)) : 0) + seg.Transfer;
                double disposable = Math.Max(0, income - h.ChargedAssessment);
                double cut = h.Stage >= InsolvencyStage.CutConsumption ? P.ConsumptionCutFactor : 1.0;
                double spend = Math.Min(h.Money, P.BaseConsumptionShare * disposable * cut);
                if (spend <= 0) continue;
                h.Money -= spend;
                int c = h.HomeParcel >= 0 ? W.Parcels[h.HomeParcel].Cluster : 0;
                double capShare = Access.IncumbentShopWeight.Length > c
                    ? 1.0 - wOutside / Access.IncumbentShopWeight[c] : 0.5;
                totalCaptured += spend * capShare;
                totalLeaked += spend * (1 - capShare);
            }
            // Distribute captured spending to commercial firms pro-rata to their
            // capture strength (mass × per-mass capture at their cluster). With no
            // commercial firm alive the "captured" share leaks outward too —
            // credited money must land on real entities (scrutiny finding #5).
            double weightSum = 0;
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                int c = W.Parcels[f.Parcel].Cluster;
                weightSum += f.JobSlots * Access.CaptureIncumbentPerMass[c];
            }
            if (weightSum <= 1e-9) { totalLeaked += totalCaptured; totalCaptured = 0; }
            W.Ledger.Transfer(Account.Households, Account.OutsideWorld, totalLeaked);
            W.Ledger.Transfer(Account.Households, Account.Firms, totalCaptured);
            if (weightSum > 1e-9)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                    int c = W.Parcels[f.Parcel].Cluster;
                    double share = f.JobSlots * Access.CaptureIncumbentPerMass[c] / weightSum;
                    f.Money += totalCaptured * share;
                    f.RevenueThisTick = totalCaptured * share;
                }
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

        private void ProductionAndTrade()
        {
            int C = Costs.ClusterCount;
            var rawSupplyByCluster = new double[C];
            var rawDemandByCluster = new double[C];
            var goodsSupplyByCluster = new double[C];
            var goodsDemandByCluster = new double[C];
            double rawSupply = 0, rawDemand = 0, goodsSupply = 0, goodsDemand = 0;

            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                var pl = W.Parcels[f.Parcel];
                int c = pl.Cluster;
                double cond = Math.Max(0.2, pl.Condition);
                switch (f.Sector)
                {
                    case ZoneKind.Extractor:
                        f.OutputThisTick = f.WorkersFilled * P.ExtractorOutputPerSlot * cond;
                        rawSupplyByCluster[c] += f.OutputThisTick; rawSupply += f.OutputThisTick;
                        break;
                    case ZoneKind.Industrial:
                        f.OutputThisTick = f.WorkersFilled * P.IndOutputPerSlot * cond * P.Quality(pl.Level) / P.Quality(1);
                        double need = f.OutputThisTick * P.IndRawPerOutput;
                        rawDemandByCluster[c] += need; rawDemand += need;
                        goodsSupplyByCluster[c] += f.OutputThisTick; goodsSupply += f.OutputThisTick;
                        break;
                    case ZoneKind.Commercial:
                        double gNeed = f.RevenueThisTick * 0.3 / Math.Max(0.5, Trade.LocalPrice(Res.Goods));
                        f.InputNeedThisTick = gNeed;
                        goodsDemandByCluster[c] += gNeed; goodsDemand += gNeed;
                        break;
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

            var rawClear = Flags.TierD_Trade
                ? Trade.ClearTick(Res.Raw, rawSupply, rawDemand, rawSupplyByCluster, rawDemandByCluster, P)
                : FlatClear(Res.Raw, rawSupply, rawDemand);
            var goodsClear = Flags.TierD_Trade
                ? Trade.ClearTick(Res.Goods, goodsSupply, goodsDemand, goodsSupplyByCluster, goodsDemandByCluster, P)
                : FlatClear(Res.Goods, goodsSupply, goodsDemand);

            SettleResource(Res.Raw, rawClear, rawSupply, rawDemand,
                sellerOf: f => f.Sector == ZoneKind.Extractor,
                buyerOf: f => f.Sector == ZoneKind.Industrial,
                sellerVolume: f => f.OutputThisTick,
                buyerVolume: f => f.OutputThisTick * P.IndRawPerOutput);
            SettleResource(Res.Goods, goodsClear, goodsSupply, goodsDemand,
                sellerOf: f => f.Sector == ZoneKind.Industrial,
                buyerOf: f => f.Sector == ZoneKind.Commercial,
                sellerVolume: f => f.OutputThisTick,
                buyerVolume: f => f.InputNeedThisTick);
        }

        /// <summary>Vanilla-style flat-price fallback (TierD off): infinite depth
        /// at the anchor price of the first matching exit.</summary>
        private ClearResult FlatClear(Res r, double supply, double demand)
        {
            double anchor = 0;
            foreach (var x in W.Exits) if (x.Resource == r) { anchor = x.Anchor; break; }
            var res = new ClearResult { LocalPrice = anchor };
            double surplus = supply - demand;
            if (surplus > 0) { res.Exported = surplus; res.ExportRevenue = surplus * anchor; }
            else { res.Imported = -surplus; res.ImportCost = -surplus * anchor; }
            return res;
        }

        /// <summary>Money settlement for one resource: local trades net between
        /// firm groups; exports arrive from OutsideWorld; imports leave to it.
        /// Pro-rata across firms; conservation exact by construction.</summary>
        private void SettleResource(Res r, ClearResult clear, double supply, double demand,
                                    Func<Firm, bool> sellerOf, Func<Firm, bool> buyerOf,
                                    Func<Firm, double> sellerVolume, Func<Firm, double> buyerVolume)
        {
            double price = clear.LocalPrice;
            double localVolume = Math.Min(supply - clear.Exported - clear.Unsold, demand - clear.Imported);
            localVolume = Math.Max(0, localVolume);

            // Sellers: local sales at local price + exports at marginal net revenue.
            double sellerRevenue = localVolume * price + clear.ExportRevenue;
            // Buyers: local buys at local price + imports at delivered cost.
            double buyerCost = localVolume * price + clear.ImportCost;

            if (supply > 1e-9 && sellerRevenue > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || !sellerOf(f)) continue;
                    double share = sellerVolume(f) / supply;
                    f.Money += sellerRevenue * share;
                    f.RevenueThisTick += sellerRevenue * share;
                }
            if (demand > 1e-9 && buyerCost > 0)
                foreach (var f in W.Firms)
                {
                    if (f.Dead || !buyerOf(f)) continue;
                    f.Money -= buyerCost * (buyerVolume(f) / demand);
                }
            // Net external flow: export revenue in, import cost out.
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
                f.RevenueThisTick = 0; f.InputNeedThisTick = 0; f.OutputThisTick = 0;

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
                double bid = LandAccounting.FirmBidPerSlot(Access, Trade, pl.Cluster, pl.Use, pl.Level, P)
                             * P.CondFactor(pl.Condition);
                double assess = LandAccounting.UnitAssessment(pl, P);
                double excess = bid - assess;
                if (excess <= 0) continue;
                double prob = MathUtil.Clamp(P.FirmEntryElasticity * excess * pl.Units * 10, 0, 0.5);
                if (W.Rng.NextDouble() < prob)
                {
                    var firm = new Firm
                    {
                        Id = W.Firms.Count, Sector = pl.Use, Parcel = pl.Id,
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
                    if (Allocation.InsolvencyStep(W, Access, h, P, ref ShelterOccupied, ShelterCapacity))
                        DisplacementExits.Add((W.Tick, h.Id));
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

        private void MigrationStep()
        {
            int pop = 0, sheltered = 0;
            foreach (var h in W.Households)
                if (h.ExitedTick < 0) { pop++; if (h.Stage == InsolvencyStage.Sheltered) sheltered++; }
            double homelessShare = pop > 0 ? (double)sheltered / pop : 0;

            LastFlows = Migration.Step(W, Access, AvgRentBySeg, homelessShare, P, Flags.TierA_Migration);

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

        private void AnniversariesAndRelocation()
        {
            if (!Levying)
            {
                // Nothing is charged: assessments are computed and logged but must
                // not drive consumption, stress, or displacement (stage-3 shadow
                // contract; scrutiny findings #2/#7/#18).
                foreach (var h in W.Households) h.ChargedAssessment = 0;
                return;
            }
            int period = P.AssessmentPeriod;
            int phaseNow = (int)(W.Tick % period);
            foreach (var h in W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if (h.AnniversaryPhase(period) != phaseNow) continue;

                var pl = W.Parcels[h.HomeParcel];
                double target = LandAccounting.UnitAssessment(pl, P);
                bool protectedTenant = W.Clusters[pl.Cluster].TenantProtection && W.Tick - h.TenureStart > period;
                if (protectedTenant)
                {
                    // Phased assessment for sitting tenants; new leases clear at market.
                    h.ChargedAssessment += (target - h.ChargedAssessment)
                                           * MathUtil.Clamp(P.TenantProtectionRate * period / 30.0, 0.02, 0.5);
                }
                else h.ChargedAssessment = target;

                // Exit timing: each household compares the converged assessment to
                // its own affordability plus its heterogeneous moving margin —
                // displacement is gradual by construction (§4.4).
                var seg = Segment.All[h.Segment];
                double income = AccessState.EffectiveIncome(h, seg, P);
                double affordable = seg.MaxRentShare * Math.Max(0.1, income);
                double margin = 1.0 + h.MovingCostDraw / 150.0;
                if (h.ChargedAssessment > affordable * margin)
                {
                    int cheaper = Allocation.FindHome(W, Access, h, P, affordable, income);
                    if (cheaper >= 0 && cheaper != h.HomeParcel)
                    {
                        Allocation.Vacate(W, h);
                        Allocation.MoveIn(W, h, cheaper, P);
                        DisplacementExits.Add((W.Tick, h.Id));
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
