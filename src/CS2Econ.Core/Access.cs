using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Tier B (design §4.2): all spatial scoring uses access weights
    /// w(p,q) = e^(−θ·c(p,q)) on cached cluster costs with mode-appropriate θ.
    /// This class owns the weight matrices, the labor market balancing, the
    /// commercial capture machinery (phantom entrant), office agglomeration, and
    /// the residual-demand fields that everything downstream consumes.</summary>
    public sealed class AccessState
    {
        public int C;                                  // cluster count
        private int _costVersion = -1;

        // Weight matrices per purpose (row = origin/home, col = destination)
        public double[,] WCommute = new double[0, 0];
        public double[,] WShop = new double[0, 0];
        public double[,] WFreight = new double[0, 0];
        public double[,] WOffice = new double[0, 0];

        // Masses per cluster
        public double[][] JobsByClass = Array.Empty<double[]>();     // [class][cluster] slots
        public double[][] WorkersByClass = Array.Empty<double[]>();  // [class][cluster] participating residents
        public double[] CommercialMass = Array.Empty<double>();      // shopping attractiveness units
        public double[] SpendMass = Array.Empty<double>();           // consumption budget originating
        public double[] OfficeJobs = Array.Empty<double>();
        /// <summary>How many office slots of each specialization sit in each
        /// cluster, and the localization multiplier each kind earns from its
        /// OWN kind's neighbours. The pooled OfficeJobs / OfficeAgglomMult
        /// above stay exactly as they were: they are what the flag-off world
        /// reads, and what OfficeAgglom() returns for every kind when the flag
        /// is off, so the two paths cannot silently diverge.</summary>
        public const int OfficeKindCount = 3;
        public double[][] OfficeJobsByKind = Array.Empty<double[]>();
        public double[][] OfficeAgglomByKind = Array.Empty<double[]>();
        /// <summary>The agglomeration an office of `kind` earns at cluster `c`.
        /// THE ONLY read site for office agglomeration outside this class, so
        /// that "specializations off" is one branch rather than a convention
        /// every caller has to remember.</summary>
        public double OfficeAgglom(OfficeKind kind, int c, EconParams p)
            => OfficeAgglomByKind.Length == OfficeKindCount
               && (uint)c < (uint)OfficeAgglomMult.Length
                ? OfficeAgglomByKind[(int)kind][c]
                : ((uint)c < (uint)OfficeAgglomMult.Length ? OfficeAgglomMult[c] : 1.0);

        // Labor market outcomes (doubly-constrained balancing, §4.2)
        public double[][] EmploymentRate = Array.Empty<double[]>();  // [class][home cluster]

        // ---- the demand side, as REAL HOUSEHOLDS ----------------------------
        // [kind 0=Low,1=High][segment] → every living household of that segment,
        // as its own bid base `RentShare_h × income_h × densityAppeal_h(kind)`,
        // sorted DESCENDING. Nothing here is a fabricated distribution: it is
        // the population, counted. A household's willingness to pay at a given
        // (cluster, level) is its own bid base times one per-segment location
        // multiplier, so this ladder is all the pricing path needs and the
        // observed income spread is whatever the individuals happen to be.
        //
        // This replaced a parametric per-(segment, cluster) income distribution
        // — a binomial convolution over a geometric job ladder, quantile-binned
        // — that the clearing price walked instead of the actual people. Only
        // PERSONAL ATTRIBUTES may come from a distribution, and only once, at
        // birth (Household.DrawAtBirth).
        public double[][][] BidLadder = Array.Empty<double[][]>();
        /// <summary>[segment*C + cluster] mean income of the segment's actual
        /// households living there (citywide segment mean where a cluster holds
        /// none) — an emergent summary for migration and construction, computed
        /// by adding individuals up rather than by evaluating a formula.</summary>
        public double[] IncomeMean = Array.Empty<double>();

        /// <summary>Rebuild the household bid ladders and the emergent income
        /// means. Once per refresh; the pricing hot path only reads them.</summary>
        /// <summary>Living households per segment — a count, rebuilt with the
        /// ladders so the posted-price pass has a presence vector.</summary>
        public double[] presenceScratch = new double[Segment.Count];

        public void RebuildHouseholdLadders(WorldState w, EconParams p)
        {
            int nSeg = Segment.Count;
            if (BidLadder.Length != 2)
            {
                BidLadder = new double[2][][];
                for (int k = 0; k < 2; k++) BidLadder[k] = new double[nSeg][];
            }
            if (IncomeMean.Length != nSeg * C) IncomeMean = new double[nSeg * C];

            var perSeg = new List<double>[2][];
            for (int k = 0; k < 2; k++)
            {
                perSeg[k] = new List<double>[nSeg];
                for (int s = 0; s < nSeg; s++) perSeg[k][s] = new List<double>();
            }
            var sumBySegCluster = new double[nSeg * C];
            var cntBySegCluster = new double[nSeg * C];
            var sumBySeg = new double[nSeg];
            var cntBySeg = new double[nSeg];

            Array.Clear(presenceScratch, 0, presenceScratch.Length);
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0) continue;
                presenceScratch[h.Segment]++;
                var seg = Segment.All[h.Segment];
                double income = LadderIncome(h, seg, p);
                double baseBid = h.RentShare * income;
                perSeg[0][h.Segment].Add(baseBid * h.DensityAppeal(ZoneKind.ResidentialLow));
                perSeg[1][h.Segment].Add(baseBid * h.DensityAppeal(ZoneKind.ResidentialHigh));
                sumBySeg[h.Segment] += income; cntBySeg[h.Segment]++;
                if (h.HomeParcel >= 0)
                {
                    int c = w.Parcels[h.HomeParcel].Cluster;
                    sumBySegCluster[h.Segment * C + c] += income;
                    cntBySegCluster[h.Segment * C + c]++;
                }
            }

            for (int k = 0; k < 2; k++)
                for (int s = 0; s < nSeg; s++)
                {
                    var arr = perSeg[k][s].ToArray();
                    Array.Sort(arr);
                    Array.Reverse(arr);                     // descending
                    BidLadder[k][s] = arr;
                }
            for (int s = 0; s < nSeg; s++)
            {
                double segMean = cntBySeg[s] > 0 ? sumBySeg[s] / cntBySeg[s] : 0;
                for (int c = 0; c < C; c++)
                {
                    int i = s * C + c;
                    IncomeMean[i] = cntBySegCluster[i] > 0 ? sumBySegCluster[i] / cntBySegCluster[i] : segMean;
                }
            }
        }

        /// <summary>The income a household enters the bid ladder with. Extracted
        /// from RebuildHouseholdLadders (which now calls it) so that anything
        /// wanting to rank households the way the ladder ranks them — an
        /// overlay, a check's counterfactual — reads the ladder's OWN rule
        /// rather than a re-implementation of it. A re-implementation is not a
        /// hypothetical hazard here: the obvious one drops the prospective-income
        /// line below and so misprices every unhoused zero-earner household.
        /// Measured in the 8×8 / 2500-household / 80-tick clearing-price fixture
        /// over seeds 0–299, that branch covers 10.69% (seed 160) to 18.65%
        /// (seed 254) of live households, and it strictly RAISES the rung for
        /// 9.48% (seed 158) to 18.65% (seed 254) of them — so a proxy ranking
        /// would put a tenth to a fifth of the population in the wrong place.
        ///
        /// An unhoused household bids on what it would earn once housed — its
        /// OWN earners and job level, valued at the market's employment odds.
        /// Employment odds are a market outcome, not a personal attribute, so
        /// reading them here is legitimate.</summary>
        public double LadderIncome(Household h, Segment seg, EconParams p)
        {
            double income = AccessState.HouseholdIncomeEstimate(h, seg, p);
            if (h.HomeParcel < 0 && seg.Adults > 0 && h.Earners == 0)
                income = Math.Max(income, HouseholdProspectiveIncome(h, seg, p));
            return income;
        }

        /// <summary>This household's own rung of the bid ladder for a density
        /// kind — exactly the value RebuildHouseholdLadders sorts.</summary>
        public double LadderBase(Household h, ZoneKind kind, EconParams p)
            => h.RentShare * LadderIncome(h, Segment.All[h.Segment], p) * h.DensityAppeal(kind);

        /// <summary>MUTANT SWITCH, harness-only (`--mutant-citywide-odds`):
        /// restores the defect this work removed — the prospect's employment
        /// odds as the UNWEIGHTED mean of EmploymentRate over ALL clusters,
        /// including the structural zeros of worker-less clusters. It exists so
        /// the prospect-localization check stays falsifiable: that check must
        /// fail whenever this is on. Never set outside the harness.</summary>
        public static bool MutantCitywideProspectOdds = false;

        /// <summary>MUTANT SWITCH, harness-only (`--mutant-fill-blind`): severs
        /// realized occupancy from the location decision — the attraction term
        /// in <see cref="RebuildDemandShares"/> reads raw capacity, so a
        /// submarket standing empty looks exactly as good to a household as one
        /// that is full. This is the channel the occupancy check names, and the
        /// switch is what keeps that check falsifiable: both of its legs must
        /// fail on every seed whenever this is on (measured, `occsweep
        /// --seeds 50`: 0/57 pass with it, 57/57 without). Never set outside the
        /// harness.</summary>
        public static bool MutantFillBlindShares = false;

        /// <summary>MUTANT SWITCH, harness-only: breaks the OTHER half of the
        /// occupancy channel — what FillEma is a smoothed average OF. The
        /// occupancy check's leg (a) is a differential oracle against an
        /// independently recomputed per-submarket fill target, and each value
        /// here is a way that oracle must be able to catch:
        ///   1 `--mutant-fill-pooled` — the citywide fill of the density kind,
        ///     so every submarket is told the average city's vacancy;
        ///   2 `--mutant-fill-alpha` — a different smoothing constant;
        ///   3 `--mutant-fill-frozen` — never updated after the first build;
        ///   4 `--mutant-fill-shift` — the neighbouring cluster's fill.
        /// Measured: 0/57 seeds pass `occsweep --seeds 50` under any of the
        /// four; the shipped path passes 57/57. Never set outside the
        /// harness.</summary>
        public static int MutantFillEmaSource = 0;

        /// <summary>The employment odds a newcomer would hear about the city
        /// from outside: the WORKER-WEIGHTED citywide rate for a labor class —
        /// total matched over total supplied, i.e. the rate the average actual
        /// worker experiences. This is the shrinkage bench in ProspectLocalOdds.
        /// It is legitimate under the model rules as a PRIOR the agent could
        /// hold: what one hears about a city is what its workers experience,
        /// which no map square dilutes. The old unweighted mean over all
        /// clusters was not that — on the 10×10/3000-household auction fixture
        /// (seed 0, t=40..240) 15–62 of 100 clusters hold no workers of a given
        /// class and contribute structural zeros, dragging the mean to
        /// 0.34/0.25/0.11 by class against a worker-weighted 0.42/0.34/0.31.</summary>
        public double ProspectBenchOdds(int labor)
        {
            if (EmploymentRate.Length != 3 || WorkersByClass.Length != 3) return 0;
            if (_prospectBench.Length == 3 && _prospectBenchVersion == _employmentVersion)
                return _prospectBench[labor];
            if (_prospectBench.Length != 3) _prospectBench = new double[3];
            for (int cl = 0; cl < 3; cl++)
            {
                double matched = 0, workers = 0;
                for (int c = 0; c < C; c++)
                {
                    double wk = WorkersByClass[cl][c];
                    matched += wk * EmploymentRate[cl][c];
                    workers += wk;
                }
                _prospectBench[cl] = workers > 1e-9 ? matched / workers : 0;
            }
            _prospectBenchVersion = _employmentVersion;
            return _prospectBench[labor];
        }
        // Cache: the bench only changes when the labor matching reruns, but a
        // prospect batch asks for it once per cluster per prospect — O(C²)
        // per prospect without this.
        private double[] _prospectBench = Array.Empty<double>();
        private long _prospectBenchVersion = -1;
        private long _employmentVersion;

        /// <summary>The odds a prospect prices AT A SPECIFIC CLUSTER: the local
        /// rate, shrunk toward the citywide worker-weighted bench by a small
        /// prior weight n0 = p.ProspectOddsPriorWeight:
        ///
        ///     odds_c = (workers_c · rate_c + n0 · bench) / (workers_c + n0)
        ///
        /// The LOCAL term is what the specific place offers — the market
        /// outcome its own workers experience — and the BENCH is the prior a
        /// newcomer would actually hold before local evidence, which is why a
        /// citywide read is legitimate here and only here on the prospect path.
        /// Shrinkage also fixes the zero-dilution defect for free: a cluster
        /// with no workers contributes no zero, it just shrinks fully to bench.
        /// n0 is calibrated against the measured per-cluster worker-count
        /// distribution on the auction reference fixture (10×10, 3000
        /// households, seeds 0–1, t=40..240): occupied-cluster counts run
        /// min 0.5–2, p25 2–5, median 4–9, p90 13–60, max 84 by class and
        /// tick; 10–62 of 100 clusters hold no workers of a given class.
        /// n0 = 4 sits at the p25–median: a 1–2-worker cluster is mostly bench
        /// (its one hire or miss is not evidence), a median cluster is an even
        /// split, and a p90 job centre is 75–95% its own local rate.</summary>
        public double ProspectLocalOdds(int labor, int cluster, EconParams p)
        {
            if (EmploymentRate.Length != 3 || (uint)cluster >= (uint)C) return 0;
            if (MutantCitywideProspectOdds)
            {
                // The restored defect, verbatim: unweighted over all clusters,
                // structural zeros included.
                double sum = 0; int n = 0;
                for (int c = 0; c < C; c++) { sum += EmploymentRate[labor][c]; n++; }
                return n > 0 ? sum / n : 0;
            }
            double bench = ProspectBenchOdds(labor);
            double wk = WorkersByClass[labor][cluster];
            double n0 = Math.Max(1e-9, p.ProspectOddsPriorWeight);
            return (wk * EmploymentRate[labor][cluster] + n0 * bench) / (wk + n0);
        }

        /// <summary>What somebody who does not live here yet would earn if it
        /// came to CLUSTER `cluster` and found work at that place's odds
        /// (ProspectLocalOdds). Same construction as HouseholdProspectiveIncome,
        /// which an unhoused resident already uses — employment odds are a
        /// clearing outcome, not a personal attribute, so a prospect may read
        /// them. Cluster-specific because the citywide version made a city with
        /// a booming district and a dead one price identically to a uniformly
        /// mediocre city at every door.</summary>
        public double ProspectIncome(Segment seg, byte jobLevel, int cluster, EconParams p)
            => ProspectIncomeAtOdds(seg, jobLevel, ProspectLocalOdds((int)seg.Labor, cluster, p), p);

        /// <summary>The income a prospect would keep by NOT coming: its own
        /// adults and job level, valued at the outside region's employment odds
        /// (p.OutsideEmploymentOdds — a property of the outside world, not of
        /// this city). The prospect's reservation is based on this, so no
        /// citywide statistic of THIS city enters what the prospect compares
        /// the city against — the default is staying outside, and the outside
        /// is not made better or worse by how this city is doing.</summary>
        public double ProspectOutsideIncome(Segment seg, byte jobLevel, EconParams p)
            => ProspectIncomeAtOdds(seg, jobLevel, p.OutsideEmploymentOdds, p);

        /// <summary>The location premium of the OUTSIDE region's door — the
        /// same rule every city door is priced by (HousingAuction._premium:
        /// clamp((access/MeanAccess)^PremiumExponent) × BidAccessScale),
        /// evaluated at the outside region's own access level
        /// (p.OutsideAccessValue, a property of the outside world). Scales the
        /// two legs that compare a city offer against staying outside: the
        /// prospect's reservation (Prospects.Step) and a resident's outside
        /// option (HousingAuction.BuildHouseholds).
        ///
        /// MeanAccess enters ONLY as the normalizer that also scales city
        /// doors: within the city it cancels across doors, which is exactly
        /// why a uniform improvement was invisible to the come/stay margin
        /// until the outside door shared it (measured, boombust at the flip
        /// commit: inflow-margin response +0 against decline-exit response
        /// +440 under a uniform amenity pulse). No city vacancy, price or
        /// other statistic may enter here — offer size stays region-side.</summary>
        public double OutsidePremium(EconParams p)
        {
            // The mutant pins the outside door's access to the city's own mean
            // (relative premium ≡ 1): the restored defect, kept for the
            // uniform-pulse check's mutant arm.
            double anchor = p.MutantRelativeOutsideAccess ? MeanAccess : p.OutsideAccessValue;
            double rel = anchor / MeanAccess;
            return MathUtil.Clamp(Math.Pow(Math.Max(0.05, rel), p.PremiumExponent), 0.2, 4.0)
                   * p.BidAccessScale;
        }

        private double ProspectIncomeAtOdds(Segment seg, byte jobLevel, double rate, EconParams p)
        {
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            int levels = Income.JobLevels(seg, p, lw, lwage);
            double expectedEarners = seg.Adults * MathUtil.Clamp(seg.Participation * rate, 0, 1);
            double wage = expectedEarners * lwage[Math.Min(jobLevel, levels - 1)]
                          * (1 - p.IncomeTax(seg.Labor));
            double benefit = Math.Max(0, seg.Adults - expectedEarners) * p.UnemploymentBenefit
                             * MathUtil.Clamp(seg.Participation, 0, 1);
            return Math.Max(p.ResidentialMinimumEarnings, wage + benefit + seg.Transfer);
        }

        /// <summary>What this household would earn if it found work at the
        /// market's current odds for its labor class — its own job level and
        /// adult count, priced by an emergent market rate.</summary>
        private double HouseholdProspectiveIncome(Household h, Segment seg, EconParams p)
        {
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            int levels = Income.JobLevels(seg, p, lw, lwage);
            double rate = 0; int n = 0;
            for (int c = 0; c < C; c++) { rate += EmploymentRate[(int)seg.Labor][c]; n++; }
            rate = n > 0 ? rate / n : 0;
            double expectedEarners = seg.Adults * MathUtil.Clamp(seg.Participation * rate, 0, 1);
            double wage = expectedEarners * lwage[Math.Min(h.JobLevel, levels - 1)]
                          * (1 - p.IncomeTax(seg.Labor));
            double benefit = Math.Max(0, seg.Adults - expectedEarners) * p.UnemploymentBenefit
                             * MathUtil.Clamp(seg.Participation, 0, 1);
            return Math.Max(p.ResidentialMinimumEarnings, wage + benefit + seg.Transfer);
        }

        public double[][] JobFillRate = Array.Empty<double[]>();     // [class][job cluster]
        public double[][] ResidualJobs = Array.Empty<double[]>();    // unfilled positions after balancing

        // ---- realized labor rates (Flags.LaborAuction) ----------------------
        // On the labor-auction path the Sinkhorn MODEL retires: after each
        // labor clear the engine stores what actually happened — employed
        // earners / supply per class × home cluster, filled / slots per class
        // × job cluster — and Refresh serves THOSE as EmploymentRate /
        // JobFillRate at the NEXT refresh. One-refresh lag: an observation of
        // the market, not a model of it. Everything downstream (prospective
        // income, land accounting's staffing expectation) then forecasts from
        // observed market outcomes. Null until the first clear, when the
        // Sinkhorn cold start covers the one refresh with nothing to observe.
        public double[][]? RealizedEmployment;
        public double[][]? RealizedFill;
        public double[][]? RealizedResidualJobs;

        public void ObserveLaborOutcome(double[][] employment, double[][] fill, double[][] residual)
        { RealizedEmployment = employment; RealizedFill = fill; RealizedResidualJobs = residual; }

        // Composite consumer access value per segment per cluster
        public double[][] AccessValue = Array.Empty<double[]>();
        public double MeanAccess = 1;

        /// <summary>[segment][0=Low,1=High][cluster] → share of that segment
        /// that would CHOOSE this (density kind, cluster), normalized over
        /// BOTH dimensions together so each segment's shares sum to 1 across
        /// the whole feasible market.
        ///
        /// Normalizing over kind AND cluster is load-bearing. A per-cluster
        /// share compared against per-KIND stock counts the same household at
        /// full weight in the low queue and again in the high queue while each
        /// queue faces only its own stock — which made every high-density
        /// cluster price as a permanent overhang at 100% occupancy and drove
        /// all apartment land rent to exactly zero (adversarial review,
        /// confirmed HIGH by three independent lenses).
        ///
        /// Weighted by CAPACITY (standing + buildable), not by access alone: a
        /// destination-choice model without a size term spreads demand evenly
        /// over clusters while stock is concentrated, so the demand/stock ratio
        /// is not a tightness measure. With it, demand lands where housing is
        /// or could be, and tightness reduces to relative attractiveness × the
        /// citywide occupancy ratio. Price-free by construction: affordability
        /// must not enter, since price is the unknown being solved for.</summary>
        public double[][][] SegmentKindShare = Array.Empty<double[][]>();
        /// <summary>[0=Low,1=High][cluster] → standing residential units. The
        /// quantity the clearing price has to fill.</summary>
        public double[][] HousingStock = Array.Empty<double[]>();
        /// <summary>[0=Low,1=High][cluster] → standing + buildable capacity
        /// (built units, zoned-empty capacity, pipeline). The attraction term
        /// of SegmentKindShare.</summary>
        public double[][] HousingCapacity = Array.Empty<double[]>();
        /// <summary>[0=Low,1=High][cluster] → smoothed realized fill rate
        /// (occupied / standing units). This is the OCCUPANCY CHANNEL: it
        /// weights the attraction term so a submarket that is actually sitting
        /// empty sheds estimated demand and prices down, which a purely
        /// potential-demand field cannot do. Smoothed so a single tick's
        /// churn cannot move rents, and it reads occupancy — never any
        /// parcel's realized RENT, which is what the §3 circularity guard
        /// forbids. Neutral (1.0) where no stock stands.</summary>
        public double[][] FillEma = Array.Empty<double[]>();
        /// <summary>Floor under the fill weight: a fully empty submarket keeps
        /// this share of its attraction. NOTE the clearing price responds
        /// SUPERLINEARLY to a multiplier on demand mass while the submarket
        /// CLEARS — shrinking the mass walks the marginal bidder down the
        /// WTP ladder (measured elasticity ~1.3–1.8, not 1.0; adversarial
        /// review, pre-flat-tail). Since the flat-tail excess rule, the
        /// price cannot fall below the deepest positive bidder's WTP: once
        /// a shrinking mass tips the submarket into excess, further
        /// shrinkage moves expected FILL, not price. So this floors the
        /// ATTRACTION at 0.25; the price floor is the flat tail, and the
        /// remaining fall to it is still steeper than linear — do not read
        /// the value as a price floor when recalibrating.</summary>
        public const double OccupancyFloor = 0.25;
        /// <summary>Smoothing on FillEma (per refresh).</summary>
        public const double FillEmaAlpha = 0.25;
        /// <summary>Weight of zoned-but-empty capacity in the attraction term,
        /// relative to standing stock. See the use site. Same superlinearity
        /// caveat as OccupancyFloor: 0.15 on attraction cuts a virgin
        /// cluster's clearing bid to roughly 5–20% of full weight, not 15% —
        /// the value is chosen for the measured price response.</summary>
        public const double ZonedEmptyAttraction = 0.15;

        // Commercial capture (Layer-3 phantom entrant machinery, §4.2)
        public double[] IncumbentShopWeight = Array.Empty<double>(); // per origin: Σ_j wShop·mass_j
        public double[] CaptureIncumbentPerMass = Array.Empty<double>(); // spending captured per unit mass at c

        // Office agglomeration A(p)^γ
        public double[] OfficeAgglomMult = Array.Empty<double>();

        /// <summary>The solved housing assignment market, when
        /// FeatureFlags.HousingAuction is on; null otherwise. Set by the engine
        /// after Refresh, read by LandAccounting for realized rents.</summary>
        public HousingAuction? Auction;

        public void EnsureWeights(IAccessCosts costs, EconParams p)
        {
            if (_costVersion == costs.Version && C == costs.ClusterCount) return;
            C = costs.ClusterCount;
            WCommute = new double[C, C];
            WShop = new double[C, C];
            WFreight = new double[C, C];
            WOffice = new double[C, C];
            for (int i = 0; i < C; i++)
                for (int j = 0; j < C; j++)
                {
                    WCommute[i, j] = Math.Exp(-p.ThetaCommute * costs.Cost(i, j, AccessPurpose.Commute));
                    WShop[i, j] = Math.Exp(-p.ThetaShopping * costs.Cost(i, j, AccessPurpose.Shopping));
                    WFreight[i, j] = Math.Exp(-p.ThetaFreight * costs.Cost(i, j, AccessPurpose.Freight));
                    WOffice[i, j] = Math.Exp(-p.ThetaOffice * costs.Cost(i, j, AccessPurpose.Commute));
                }
            _costVersion = costs.Version;
        }

        /// <summary>Full Tier B refresh: masses → labor balancing → capture →
        /// composite access. Runs at refresh cadence, not per tick.</summary>
        public void Refresh(WorldState w, IAccessCosts costs, EconParams p, FeatureFlags? flags = null)
        {
            EnsureWeights(costs, p);
            int nc = Segment.Count;

            // ---- masses -----------------------------------------------------
            JobsByClass = NewJagged(3, C);
            WorkersByClass = NewJagged(3, C);
            CommercialMass = new double[C];
            SpendMass = new double[C];
            OfficeJobs = new double[C];
            OfficeJobsByKind = NewJagged(OfficeKindCount, C);

            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Parcel < 0) continue;
                var parcel = w.Parcels[f.Parcel];
                int c = parcel.Cluster;
                double cond = Math.Max(0.2, parcel.Condition);
                switch (f.Sector)
                {
                    case ZoneKind.Commercial:
                        JobsByClass[0][c] += f.JobSlots * 0.7; JobsByClass[1][c] += f.JobSlots * 0.3;
                        CommercialMass[c] += f.JobSlots * cond * p.Quality(parcel.Level);
                        break;
                    case ZoneKind.Industrial:
                        JobsByClass[0][c] += f.JobSlots * 0.6; JobsByClass[1][c] += f.JobSlots * 0.4;
                        break;
                    case ZoneKind.Extractor:
                        JobsByClass[0][c] += f.JobSlots;
                        break;
                    case ZoneKind.Office:
                        JobsByClass[1][c] += f.JobSlots * 0.3; JobsByClass[2][c] += f.JobSlots * 0.7;
                        OfficeJobs[c] += f.JobSlots;
                        OfficeJobsByKind[(int)f.Office][c] += f.JobSlots;
                        break;
                }
            }

            foreach (var h in w.Households)
            {
                var seg = Segment.All[h.Segment];
                if (h.HomeParcel >= 0)
                {
                    int c = w.Parcels[h.HomeParcel].Cluster;
                    // Labor SUPPLY is per-adult participation × adults — the same
                    // quantity the household's wage income is paid on, so the
                    // matched-jobs wage bill charged to firms and the wages paid to
                    // households stay in balance (paying per-earner while supplying
                    // one worker per household double-charged every firm and killed
                    // all industry — measured).
                    if (seg.Participation > 0 && seg.Adults > 0)
                        WorkersByClass[(int)seg.Labor][c] += seg.Participation * seg.Adults;
                }
                // SpendMass counts SETTLED residents only, while ChooseShops
                // lets the unhoused spend too. That asymmetry is deliberate.
                // SpendMass is what a developer capitalizes into a building that
                // will stand for decades, and the shelter population is the most
                // volatile thing on the map — it empties whenever arrivals stop
                // being absorbed. Counting it in the entry field was tried:
                // shops chase job centres on a catchment that evaporates, and
                // commercial deaths went 187 → 217 over 2000 ticks while three
                // more seeds fell out of the suite (measured). Unhoused
                // spending is therefore upside a shop may earn, never a promise
                // the entry decision is made on.
                if (h.HomeParcel < 0) continue;
                int origin = w.Parcels[h.HomeParcel].Cluster;
                if ((uint)origin >= (uint)C) continue;
                // Rent comes out first. SpendMass used gross income, which was
                // near enough while the median clearing price was 0.19 against a
                // median income of 10 — under 2% of the paycheque. Once the
                // housing market cleared properly that gap became ~40% of every
                // household's income, so the field promised developers half
                // again as much retail spending as households can actually do,
                // and they built shops that could not be fed. Housing is
                // deducted at the household's OWN rent share — its personal
                // attribute — not at its charged assessment, so this stays clear
                // of the §3 guard: no realized rent enters the demand field.
                // Rent comes out first — but only on the store-level path.
                // SpendMass using GROSS income was near enough while the median
                // clearing price was 0.19 against a median income of 10, under
                // 2% of the paycheque. Once the housing market cleared properly
                // that gap became ~40% of income, so the field promised
                // developers half again as much retail spending as households
                // can actually do and they built shops that could not be fed:
                // deducting it took commercial deaths from 217 to 94 over 2000
                // ticks. It is tied to the flag because on the POOLED path the
                // same deduction is a pure ~30% cut to the entry signal that the
                // pooled calibration was set against, and it costs three seeds
                // in the suite. Housing is deducted at the household's OWN rent
                // share — a personal attribute — not at its charged assessment,
                // so no realized rent enters the demand field (§3 guard).
                double income = HouseholdIncomeEstimate(h, seg, p);
                double afterHousing = flags != null && flags.StoreLevelSpending
                    ? 1 - MathUtil.Clamp(h.RentShare, 0, 0.9) : 1.0;
                SpendMass[origin] += income * afterHousing * p.BaseConsumptionShare;
            }

            // ---- labor market: one balanced matching per labor class --------
            _employmentVersion++;              // invalidates the prospect-bench cache
            EmploymentRate = NewJagged(3, C);
            JobFillRate = NewJagged(3, C);
            ResidualJobs = NewJagged(3, C);
            if (flags != null && flags.LaborAuction && RealizedEmployment != null
                && RealizedEmployment.Length == 3 && RealizedEmployment[0].Length == C)
            {
                // Labor-auction path: serve what the last clear OBSERVED (see
                // the field comment). The Sinkhorn model below runs only on
                // the flag-off path and on the cold-start refresh before any
                // clear has happened.
                for (int cl = 0; cl < 3; cl++)
                    for (int c = 0; c < C; c++)
                    {
                        EmploymentRate[cl][c] = RealizedEmployment[cl][c];
                        JobFillRate[cl][c] = RealizedFill![cl][c];
                        ResidualJobs[cl][c] = RealizedResidualJobs![cl][c];
                    }
            }
            else
            {
                double slackW = Math.Exp(-p.ThetaCommute * p.LaborSlackMinutes);
                for (int cl = 0; cl < 3; cl++)
                {
                    Balancing.Match(WorkersByClass[cl], JobsByClass[cl], WCommute, slackW,
                                    p.IpfIterations, out var rowMatched, out var colMatched);
                    for (int i = 0; i < C; i++)
                    {
                        double sup = WorkersByClass[cl][i];
                        EmploymentRate[cl][i] = sup > 1e-9 ? MathUtil.Clamp(rowMatched[i] / sup, 0, 1) : 0;
                    }
                    for (int j = 0; j < C; j++)
                    {
                        double dem = JobsByClass[cl][j];
                        JobFillRate[cl][j] = dem > 1e-9 ? MathUtil.Clamp(colMatched[j] / dem, 0, 1) : 0;
                        ResidualJobs[cl][j] = Math.Max(0, dem - colMatched[j]);
                    }
                }
            }

            // ---- commercial capture base ------------------------------------
            // The shopping logit includes an outside option (out-of-city big box):
            // uncaptured spending leaks to the outside world, which keeps total
            // capture ≤ total spending (money conservation) and gives new
            // commercial mass someone to win share from.
            IncumbentShopWeight = new double[C];
            CaptureIncumbentPerMass = new double[C];
            double wOutside = Math.Exp(-p.ThetaShopping * p.OutsideShopMinutes) * p.OutsideShopMass;
            for (int i = 0; i < C; i++)
            {
                double s = wOutside + 1e-9;
                for (int j = 0; j < C; j++) s += WShop[i, j] * CommercialMass[j];
                IncumbentShopWeight[i] = s;
            }
            for (int c = 0; c < C; c++)
            {
                double captured = 0;
                for (int i = 0; i < C; i++)
                    captured += SpendMass[i] * WShop[i, c] / IncumbentShopWeight[i];
                CaptureIncumbentPerMass[c] = captured; // per unit of mass at c, at current incumbency
            }

            // ---- office agglomeration ---------------------------------------
            OfficeAgglomMult = new double[C];
            for (int c = 0; c < C; c++)
            {
                double a = 0;
                for (int j = 0; j < C; j++) a += WOffice[c, j] * OfficeJobs[j];
                OfficeAgglomMult[c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
            }
            // PER SPECIALIZATION: the identical law, read over one kind's jobs
            // instead of all offices pooled. A cluster thick with software jobs
            // is a good place for software and says nothing about media, which
            // is the whole point — it makes "the best office that could stand
            // here" a question with a different answer in different places, and
            // gives a failed office parcel somewhere else to go.
            //
            // Off, every kind reads the pooled multiplier, so max-over-kinds is
            // the old single value and nothing downstream can tell the
            // difference. The 1/400 scale and the gamma are the pooled law's
            // own, unchanged and not re-tuned: splitting the jobs between kinds
            // lowers each kind's `a`, and that is the mechanism, not a defect
            // to calibrate away.
            OfficeAgglomByKind = NewJagged(OfficeKindCount, C);
            for (int k = 0; k < OfficeKindCount; k++)
                for (int c = 0; c < C; c++)
                {
                    if (flags == null || !flags.OfficeSpecializations)
                    { OfficeAgglomByKind[k][c] = OfficeAgglomMult[c]; continue; }
                    double a = 0;
                    for (int j = 0; j < C; j++) a += WOffice[c, j] * OfficeJobsByKind[k][j];
                    OfficeAgglomByKind[k][c] = Math.Pow(1.0 + a / 400.0, p.OfficeAgglomGamma);
                }

            // ---- composite consumer access ----------------------------------
            AccessValue = NewJagged(nc, C);
            var jobAccess = NewJagged(3, C);
            var shopAccess = new double[C];
            var schoolAccess = new double[C];
            var healthAccess = new double[C];
            for (int c = 0; c < C; c++)
            {
                for (int cl = 0; cl < 3; cl++)
                {
                    double s = 0;
                    for (int j = 0; j < C; j++) s += WCommute[c, j] * JobsByClass[cl][j];
                    jobAccess[cl][c] = s;
                }
                double shop = 0, school = 0, health = 0;
                for (int j = 0; j < C; j++)
                {
                    shop += WShop[c, j] * CommercialMass[j];
                    school += WShop[c, j] * w.Clusters[j].School;
                    health += WShop[c, j] * w.Clusters[j].Health;
                }
                shopAccess[c] = shop; schoolAccess[c] = school; healthAccess[c] = health;
            }

            double sumAccess = 0; int cnt = 0;
            for (int s = 0; s < nc; s++)
            {
                var seg = Segment.All[s];
                int cl = (int)seg.Labor;
                double wageScale = p.Wage(seg.Labor) / p.WageBasic;
                for (int c = 0; c < C; c++)
                {
                    var ci = w.Clusters[c];
                    double v =
                        seg.JobAccessW * Math.Log(1 + jobAccess[cl][c]) * wageScale
                        + seg.GoodsAccessW * Math.Log(1 + shopAccess[c])
                        + seg.SchoolAccessW * Math.Log(1 + schoolAccess[c])
                        + seg.AmenityW * ci.Amenity
                        + seg.HealthW * Math.Log(1 + healthAccess[c])
                        - seg.PollutionW * (ci.Pollution + ci.Noise);
                    AccessValue[s][c] = v;
                    sumAccess += v; cnt++;
                }
            }
            MeanAccess = cnt > 0 ? Math.Max(0.5, sumAccess / cnt) : 1;

            // ---- inputs to the market-clearing bid (design §4.3) -------------
            // Standing stock (the quantity a price must fill) and capacity
            // (standing + buildable — the attraction term). [0] = Low, [1] = High.
            bool fillFirstBuild = FillEma.Length != 2;
            if (HousingStock.Length != 2)
            {
                HousingStock = new double[2][];
                HousingCapacity = new double[2][];
            }
            if (fillFirstBuild) FillEma = new double[2][];
            var filled = new double[2][];
            for (int k = 0; k < 2; k++)
            {
                if (HousingStock[k] == null || HousingStock[k].Length != C) HousingStock[k] = new double[C];
                else Array.Clear(HousingStock[k], 0, C);
                if (HousingCapacity[k] == null || HousingCapacity[k].Length != C) HousingCapacity[k] = new double[C];
                else Array.Clear(HousingCapacity[k], 0, C);
                if (FillEma[k] == null || FillEma[k].Length != C)
                {
                    FillEma[k] = new double[C];
                    for (int c = 0; c < C; c++) FillEma[k][c] = 1.0;   // neutral until observed
                }
                filled[k] = new double[C];
            }
            foreach (var pl in w.Parcels)
            {
                if ((uint)pl.Cluster >= (uint)C) continue;
                if (pl.State == ParcelState.Built && pl.IsResidential)
                {
                    int k = pl.Use == ZoneKind.ResidentialHigh ? 1 : 0;
                    HousingStock[k][pl.Cluster] += pl.Units;
                    HousingCapacity[k][pl.Cluster] += pl.Units;
                    filled[k][pl.Cluster] += Math.Min(pl.Units, pl.OccupantHouseholds.Count);
                }
                else if (pl.State == ParcelState.UnderConstruction
                         && (pl.Use == ZoneKind.ResidentialLow || pl.Use == ZoneKind.ResidentialHigh))
                    HousingCapacity[pl.Use == ZoneKind.ResidentialHigh ? 1 : 0][pl.Cluster] += pl.Units;
                else if (pl.State == ParcelState.Empty
                         && (pl.Zoned == ZoneKind.ResidentialLow || pl.Zoned == ZoneKind.ResidentialHigh))
                    // Zoned-empty land is NOT housing — nobody can live on it —
                    // so it earns only a token share of the attraction. Enough
                    // that a virgin cluster still draws the demand a developer
                    // needs to justify the first building there; small enough
                    // that a city with more empty lots than homes does not
                    // drain demand away from the stock that actually exists
                    // and collapse every clearing price.
                    HousingCapacity[pl.Zoned == ZoneKind.ResidentialHigh ? 1 : 0][pl.Cluster]
                        += ZonedEmptyAttraction * LandAccounting.UnitsFor(pl.Zoned);
            }

            // Occupancy channel: smooth the realized fill rate per submarket.
            // Clusters holding no stock stay neutral so a greenfield site is
            // not pre-judged empty.
            for (int k = 0; k < 2; k++)
            {
                double pooledStock = 0, pooledFilled = 0;
                if (MutantFillEmaSource == 1)
                    for (int c = 0; c < C; c++) { pooledStock += HousingStock[k][c]; pooledFilled += filled[k][c]; }
                for (int c = 0; c < C; c++)
                {
                    int src = MutantFillEmaSource == 4 ? (c + 1) % C : c;
                    double stock = HousingStock[k][src];
                    double target = stock > 0 ? MathUtil.Clamp(filled[k][src] / stock, 0, 1) : 1.0;
                    if (MutantFillEmaSource == 1)
                        target = pooledStock > 0 ? MathUtil.Clamp(pooledFilled / pooledStock, 0, 1) : 1.0;
                    double alpha = MutantFillEmaSource == 2 ? 0.5 : FillEmaAlpha;
                    if (MutantFillEmaSource == 3 && !fillFirstBuild) continue;
                    FillEma[k][c] = fillFirstBuild ? target
                                                   : MathUtil.Ema(FillEma[k][c], target, alpha);
                }
            }

            // Household ladders BEFORE demand shares: the shares read
            // AccessValue only, but ExpectedIncome (used downstream by
            // migration and construction in the same tick) must already be
            // backed by this refresh's population.
            RebuildHouseholdLadders(w, p);
            // Post the price each submarket cleared at, so households judge
            // affordability against a real posted price rather than against
            // nothing. Read from the ladders just rebuilt; the intents built
            // next consume it. Previous-refresh prices for the parcels being
            // priced now — tâtonnement, and the loop is negative.
            bool firstPost = PostedPrice.Length != 2;
            if (firstPost)
            {
                PostedPrice = new double[2][];
                for (int k = 0; k < 2; k++) PostedPrice[k] = new double[C];
            }
            for (int k = 0; k < 2; k++)
            {
                if (PostedPrice[k].Length != C) { PostedPrice[k] = new double[C]; firstPost = true; }
                var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
                for (int c = 0; c < C; c++)
                {
                    double post = LandAccounting.ResidentialBidPerUnit(
                        this, c, kind, 2, presenceScratch, p, realized: true);
                    PostedPrice[k][c] = firstPost
                        ? post : MathUtil.Ema(PostedPrice[k][c], post, p.PostedPriceAlpha);
                }
            }
            RebuildDemandShares(w, p);
        }

        /// <summary>Recompute SegmentKindShare from AccessValue, capacity,
        /// fill and density appeal. Called at the end of Refresh; exposed so
        /// tests can perturb one input (e.g. FillEma) and re-derive the
        /// demand field without re-running a whole refresh.</summary>
        /// <summary>Count where households actually WANT to live, by asking
        /// each of them. Every living household evaluates each (density kind,
        /// cluster) with its OWN attributes — its own density tolerance, its
        /// own affordability against the posted price, and its own
        /// idiosyncratic taste for that specific place — and picks its argmax.
        /// SegmentKindShare is then the COUNT of those choices, normalized per
        /// segment.
        ///
        /// This replaced a per-segment logit evaluated from segment constants
        /// (`exp(AccessValue/1.5) * capacity * fill`, normalized), which
        /// allocated a real headcount by a formula rather than by asking
        /// anybody: measured 31–53% total-variation distance from where the
        /// population actually was, and it disagreed with the per-household
        /// location choice the allocator was already making.
        ///
        /// The idiosyncratic term is a Gumbel draw keyed to (household, kind,
        /// cluster) — stable forever, so it is a draw at birth in the sense
        /// that matters: an individual's taste for a place never re-rolls.
        /// Counting argmaxes of (systematic utility + Gumbel) reproduces the
        /// multinomial logit IN EXPECTATION, which is the point: the share is
        /// now the emergent consequence of individuals choosing, not an
        /// assumed functional form, and it carries the finite-sample texture a
        /// closed form smooths away.
        ///
        /// Price enters each household's own affordability, so this is a real
        /// market feedback: dear places lose bidders, which lowers their
        /// clearing price. It reads the PREVIOUS refresh's posted price
        /// (tâtonnement), and the loop is negative — the §3 guard forbids the
        /// self-reinforcing kind, not stabilizing price↔quantity adjustment.</summary>
        public void RebuildDemandShares(WorldState w, EconParams p)
        {
            int C = this.C, nc = Segment.Count;
            if (SegmentKindShare.Length != nc)
            {
                SegmentKindShare = new double[nc][][];
                for (int s = 0; s < nc; s++) SegmentKindShare[s] = new double[2][];
            }
            for (int s = 0; s < nc; s++)
                for (int k = 0; k < 2; k++)
                {
                    if (SegmentKindShare[s][k] == null || SegmentKindShare[s][k].Length != C)
                        SegmentKindShare[s][k] = new double[C];
                    else Array.Clear(SegmentKindShare[s][k], 0, C);
                }

            // Systematic part of the location term, shared by everyone of a
            // segment because geography and the segment's weighting of it are
            // shared; what differs per household is tolerance, affordability
            // and taste.
            var sizeTerm = new double[2][];
            for (int k = 0; k < 2; k++)
            {
                sizeTerm[k] = new double[C];
                for (int c = 0; c < C; c++)
                {
                    double attract = HousingCapacity[k][c]
                                     * (MutantFillBlindShares ? 1.0
                                        : OccupancyFloor + (1 - OccupancyFloor) * FillEma[k][c]);
                    sizeTerm[k][c] = attract > 1e-9 ? Math.Log(attract) : -50;
                }
            }

            var counts = new double[nc];
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0) continue;
                int s = h.Segment;
                var seg = Segment.All[s];
                double income = Math.Max(1e-6, HouseholdIncomeEstimate(h, seg, p));
                // Where this household currently lives.
                int homeC = h.HomeParcel >= 0 ? w.Parcels[h.HomeParcel].Cluster : -1;
                int homeK = h.HomeParcel >= 0
                    ? (w.Parcels[h.HomeParcel].Use == ZoneKind.ResidentialHigh ? 1 : 0) : -1;
                // Moving cost expressed in the same units as the taste shock
                // (Gumbel, σ = π/√6 ≈ 1.28), scaled by this household's own
                // draw. Written as MovingCostDraw/budget it divided a lump-sum
                // cost by a per-tick flow and came out at 1–40 against σ = 1.28,
                // which pinned every tenant to its home: the occupancy channel
                // then moved a submarket's demand by 9% where the old segment
                // logit moved it by 60%, and the price stopped responding to
                // vacancy at all (measured).
                double stayBonus = p.MoveInertia
                                   * (h.MovingCostDraw / Math.Max(1e-6, p.MovingCostMean));

                // Two bids, both discrete acts by this one household.
                //
                //   RENEWAL — a sitting tenant is demand for its own unit. It
                //   will pay to stay rather than be homeless, whatever it would
                //   prefer. Counting only first choices lost this: at cluster
                //   20, 14 households lived in 14 units and only 9 named it, so
                //   a fully-let submarket read as excess supply and priced at
                //   its poorest bidder (measured).
                //
                //   SHOPPING — it also bids on the one place it would rather
                //   be, if any place beats home by more than its own moving
                //   cost. That bid is real: it is what a landlord there sees in
                //   the queue, and it is why a desirable submarket is
                //   over-subscribed and clears above its own sitting tenants.
                //
                // Over-subscription is therefore endogenous — mass is the
                // population plus however many households are actually restless
                // — rather than a search-breadth knob.
                counts[s] += 1;                       // households, not bids
                if (homeC >= 0) SegmentKindShare[s][homeK][homeC] += 1;

                double homeU = double.NegativeInfinity;
                double bestU = double.NegativeInfinity; int bestK = 0, bestC = -1;
                for (int k = 0; k < 2; k++)
                {
                    double appealTerm = Math.Log(Math.Max(1e-6, h.DensityAppeal(k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow)));
                    for (int c = 0; c < C; c++)
                    {
                        if (sizeTerm[k][c] <= -49) continue;      // nothing there to want
                        // Own affordability against the price posted last
                        // refresh. Rent comes out of income before anything
                        // else, so what is left to live on is (income − rent),
                        // and its log is this household's own consumption
                        // value: mildly negative while rent is a small share of
                        // the paycheque, steeply negative as it eats it, and a
                        // place whose rent exceeds this household's income is
                        // not a place it can live at all. That curvature is
                        // what sorts households across price levels; a flat
                        // −λ·price/budget penalty could not.
                        double price = PostedPrice.Length == 2 && PostedPrice[k].Length > c ? PostedPrice[k][c] : 0;
                        bool home = k == homeK && c == homeC;
                        double left = 1 - price / income;
                        double afford;
                        if (left <= 0.02) { if (!home) continue; afford = -50; }
                        else afford = p.ConsumptionWeight * Math.Log(left);
                        double u = AccessValue[s][c] / 1.5 + sizeTerm[k][c] + appealTerm + afford;
                        // This household's own permanent taste for this exact
                        // place (Gumbel via the inverse-CDF of a stable hash).
                        double e = SplitMix64.Hash01((ulong)h.Id * 1000003UL + (ulong)k * 7919UL + (ulong)c * 31UL + 5);
                        u += -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
                        if (home) { homeU = u + stayBonus; continue; }
                        if (u > bestU) { bestU = u; bestK = k; bestC = c; }
                    }
                }
                // Unhoused: homeU is −∞, so its best place always wins and it
                // bids there. Housed: it bids away only where home loses.
                if (bestC >= 0 && bestU > homeU) SegmentKindShare[s][bestK][bestC] += 1;
            }

            // Per HOUSEHOLD, not per bid: segmentPresence downstream is a
            // headcount, so the share must be "bids per head of this segment".
            // Normalizing by bids would divide the over-subscription straight
            // back out and leave every submarket at demand ≈ occupancy — the
            // degenerate case where no price can exceed the sitting tenants'.
            for (int s = 0; s < nc; s++)
            {
                if (counts[s] <= 0) continue;
                for (int k = 0; k < 2; k++)
                {
                    var row = SegmentKindShare[s][k];
                    for (int c = 0; c < C; c++) row[c] /= counts[s];
                }
            }
        }

        /// <summary>[0=Low,1=High][cluster] the clearing price posted at the
        /// last refresh — what households read when judging affordability, so
        /// the market adjusts by tâtonnement rather than by a formula.</summary>
        public double[][] PostedPrice = Array.Empty<double[]>();

        // ---- counted shop intents (task #20) --------------------------------
        /// <summary>[cluster][bin] money of counted individual shop intents.
        /// Bin 0 is "wins at any size we would build"; bins 1..IntentProbeBins
        /// partition log M* over [Lo, Hi]; the last bin is overflow and is never
        /// read. Region-side, exactly where the charter puts offer/size signals;
        /// NO household reads it — its only consumers are
        /// LandAccounting.FirmBidPerSlot's commercial leg and, through it, the
        /// developer's construction signal and the per-parcel firm-entry
        /// probability.</summary>
        public double[][] IntentSpendByBin = Array.Empty<double[]>();
        /// <summary>[cluster][bin] HEADCOUNT behind each bin of
        /// IntentSpendByBin. A read is the money in the bins it sums; this is
        /// how many individual households put it there, which is what the
        /// evidence floor is measured against. Counting heads per CLUSTER
        /// instead would not discriminate — WShop is strictly positive
        /// everywhere, so every household is "reachable" from every cluster and
        /// the count is the population (measured: 1690–3772 of ~8000 at every
        /// cluster, `shopprobe --seeds 4`, task #20). What is genuinely thin is
        /// the evidence behind a read AT A SIZE.</summary>
        public double[][] IntentHeadsByBin = Array.Empty<double[]>();
        /// <summary>[cluster] households with a finite M* here at all. Telemetry
        /// only; see IntentHeadsByBin for why it is not the evidence measure.</summary>
        public double[] IntentHeads = Array.Empty<double>();
        /// <summary>[cluster] households whose M* fell past the top of the
        /// histogram — no shop of a size this city would build could win them.
        /// A legitimate and expected population (remote clusters), NOT an error:
        /// what would be an error is a READ landing there, and no read does —
        /// the entry probe asks at log-mass 1.79–2.83 against a range top of
        /// IntentProbeLogMassHi. ShopProbe prints both.</summary>
        public double[] IntentOverflow = Array.Empty<double>();
        /// <summary>Whether the counted field has been built. Built only on the
        /// StoreLevelSpending path; where it has not been, every consumer falls
        /// through to the pooled PhantomCommercialCapture — the same data-driven
        /// dispatch Refresh already uses for RealizedEmployment.</summary>
        public bool HasCountedIntents;
        private double _intentLo, _intentStep, _intentHeadFloor;

        /// <summary>Count who would come, one household at a time.
        ///
        /// Each settled household has just chosen (ChooseShops), and bestU[h] is
        /// what it actually GETS on its own scale: the shop it picked including
        /// its own permanent taste for that store, or the out-of-town option
        /// including its own taste for that, whichever won. A hypothetical shop
        /// of mass M at cluster c would beat that for this household iff
        /// log(WShop[o,c] × M) + ε_hc &gt; bestU[h], i.e. iff
        /// M &gt; M* = exp(bestU[h] − ε_hc) / WShop[o,c], where ε_hc is that
        /// household's own permanent taste for a new store at c. So the counted
        /// object is a demand curve in SIZE, stored as a histogram over log M*
        /// rather than as an H × C table.
        ///
        /// BOTH SIDES CARRY A DRAW, and that is what makes the count a count of
        /// individual argmaxes rather than a comparison between a drawn quantity
        /// and an undrawn one. See EconomyEngine.ChooseShops for the three
        /// formulations measured and why the two one-sided ones fail in opposite
        /// directions. Nothing here takes a Gumbel's expectation analytically —
        /// that expectation IS the logit share, i.e. the defect being removed.
        ///
        /// This replaces PhantomCommercialCapture as the store-level entry
        /// signal. That field is
        ///   Σ_i SpendMass[i] × wNew_i / (IncumbentShopWeight[i] + wNew_i),
        /// and its denominator is a citywide convolution over every destination
        /// cluster: the POOLED ALLOCATION RULE re-served as one developer's
        /// forecast. On the store-level path the market that actually runs is
        /// discrete — each household walks into ONE shop — so that forecast
        /// predicts a market that does not exist, and developers keep building
        /// shops the discrete market cannot feed. A COUNT of individual
        /// comparisons has no such denominator: every term is one household's
        /// own comparison from its own information, and the sum's spatial reach
        /// is set by WShop decay rather than by fiat. It is the same object
        /// RebuildDemandShares already ships, one += per household argmax.
        ///
        /// Two properties, both deliberate:
        ///
        /// SATURATION. Once a shop of mass M stands at c, every household near c
        /// has bestU ≥ log(WShop[o,c] × M), so its M* jumps above M and it
        /// stops counting for another shop of that size there. The counted
        /// intents collapse to the households a second shop would genuinely
        /// serve better. The pooled field does no such thing — it stays strictly
        /// positive forever and hands every entrant a share of the same money.
        ///
        /// WHAT IS STILL APPROXIMATE. ε_hc is a taste for a PLACE, not for a
        /// specific store: two entrants at the same cluster would inherit the
        /// same draw from the same household, so the probe cannot tell them
        /// apart. That is the honest residue of not having a firm id, and it is
        /// MEASURED against realized takings (the entry-signal check's
        /// CALIBRATION leg) rather than modelled away.
        ///
        /// The intent weight is the same per-household quantity SpendMass sums —
        /// income × (1 − own rent share) × BaseConsumptionShare — and NOT the
        /// realized spend from ConsumptionFlows, which nets ChargedAssessment:
        /// realized rent entering a valuation field is the §3 circularity
        /// violation. Housing comes out at the household's OWN RentShare, a
        /// personal attribute. Settled households only, inheriting SpendMass's
        /// measured asymmetry verbatim (see its site comment): unhoused spending
        /// is upside a shop may earn, never a promise the entry decision is made
        /// on.</summary>
        public void CountShopIntents(WorldState w, EconParams p, double[] bestU)
        {
            int B = Math.Max(2, p.IntentProbeBins);
            if (IntentSpendByBin.Length != C)
            {
                IntentSpendByBin = new double[C][];
                IntentHeadsByBin = new double[C][];
                for (int c = 0; c < C; c++)
                { IntentSpendByBin[c] = new double[B + 2]; IntentHeadsByBin[c] = new double[B + 2]; }
                IntentHeads = new double[C];
                IntentOverflow = new double[C];
            }
            for (int c = 0; c < C; c++)
            {
                if (IntentSpendByBin[c].Length != B + 2) IntentSpendByBin[c] = new double[B + 2];
                else Array.Clear(IntentSpendByBin[c], 0, B + 2);
                if (IntentHeadsByBin[c].Length != B + 2) IntentHeadsByBin[c] = new double[B + 2];
                else Array.Clear(IntentHeadsByBin[c], 0, B + 2);
            }
            Array.Clear(IntentHeads, 0, C);
            Array.Clear(IntentOverflow, 0, C);
            _intentLo = p.IntentProbeLogMassLo;
            _intentStep = (p.IntentProbeLogMassHi - _intentLo) / B;
            _intentHeadFloor = p.IntentHeadFloor;
            if (_intentStep <= 0) { HasCountedIntents = false; return; }
            double hi = p.IntentProbeLogMassHi;

            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                int o = w.Parcels[h.HomeParcel].Cluster;
                if ((uint)o >= (uint)C) continue;
                if ((uint)h.Id >= (uint)bestU.Length) continue;
                var seg = Segment.All[h.Segment];
                double wh = HouseholdIncomeEstimate(h, seg, p)
                            * (1 - MathUtil.Clamp(h.RentShare, 0, 0.9)) * p.BaseConsumptionShare;
                if (wh <= 0) continue;
                double bs = MutantIntentUnsaturated ? double.NegativeInfinity : bestU[h.Id];
                for (int c = 0; c < C; c++)
                {
                    double wsc = WShop[o, c];
                    if (wsc <= 1e-12) continue;      // unreachable: no size wins
                    // This household's own permanent taste for a new store AT
                    // THIS PLACE. Keyed on (household, cluster) — never on a
                    // firm id, which a shop that does not exist cannot have —
                    // exactly as RebuildDemandShares keys a household's taste
                    // for a (density, cluster) it does not live in. It makes the
                    // comparison symmetric: the threshold bestU carries the
                    // household's realized draw for what it already has, so the
                    // newcomer must carry one too or the count is a comparison
                    // between a drawn quantity and an undrawn one. Permanent, so
                    // an individual's taste for a place never re-rolls.
                    double eps = Gumbel((ulong)h.Id * 2246822519UL + (ulong)c * 40503UL + 7717UL);
                    double logMstar = bs - eps - Math.Log(wsc);
                    if (logMstar >= hi) { IntentOverflow[c] += 1; continue; }
                    IntentHeads[c] += 1;
                    int b = logMstar <= _intentLo ? 0
                          : 1 + (int)((logMstar - _intentLo) / _intentStep);
                    if (b > B) b = B;
                    IntentSpendByBin[c][b] += wh;
                    IntentHeadsByBin[c][b] += 1;
                }
            }
            HasCountedIntents = true;
        }

        /// <summary>Counted intent spending a shop of the given mass at c would
        /// win: the CONSERVATIVE (lower) cumulative — only bins whose whole
        /// range is beaten. A developer never over-counts, which is the safe
        /// direction given the defect being removed was over-promising; the
        /// discretization bias is measured (IntentProbeBins) rather than
        /// assumed. Zero below the evidence floor.</summary>
        public double CountedIntent(int c, double mass) => CountedIntent(c, mass, out _);

        /// <summary>The headcount a read AT THIS SIZE rests on, with the
        /// evidence floor NOT applied — what the floor is set from, and what a
        /// thin-evidence check must count before asking what the gated read
        /// returned.</summary>
        public double IntentHeadsFor(int c, double mass)
        {
            if (!HasCountedIntents || (uint)c >= (uint)C || IntentHeadsByBin.Length != C) return 0;
            double logM = Math.Log(Math.Max(1e-12, mass));
            if (logM <= _intentLo) return 0;
            var hrow = IntentHeadsByBin[c];
            int upto = (int)Math.Floor((logM - _intentLo) / _intentStep);
            if (upto > hrow.Length - 2) upto = hrow.Length - 2;
            double heads = hrow[0];
            for (int i = 1; i <= upto; i++) heads += hrow[i];
            return heads;
        }

        /// <summary>As above, also returning the HEADCOUNT that produced the
        /// read — how many individual households a place's retail forecast
        /// rests on.</summary>
        public double CountedIntent(int c, double mass, out double heads)
        {
            heads = 0;
            if (!HasCountedIntents || (uint)c >= (uint)C || IntentSpendByBin.Length != C) return 0;
            double logM = Math.Log(Math.Max(1e-12, mass));
            if (logM <= _intentLo) return 0;
            var row = IntentSpendByBin[c];
            var hrow = IntentHeadsByBin[c];
            int last = row.Length - 2;              // the final entry is overflow, never read
            int upto = (int)Math.Floor((logM - _intentLo) / _intentStep);
            if (upto > last) upto = last;
            double sum = row[0];
            heads = hrow[0];
            for (int i = 1; i <= upto; i++) { sum += row[i]; heads += hrow[i]; }
            // Thin evidence: one household's basket is not a place's retail
            // forecast. The read is zeroed, not shrunk toward a prior, because
            // the safe direction here is under-promising — the defect being
            // removed was a field that promised every entrant a share.
            if (heads < _intentHeadFloor) { heads = 0; return 0; }
            return sum;
        }

        /// <summary>What a developer reads: counted individual intents where
        /// they have been built, the pooled phantom-entrant field where they
        /// have not (flag off, or a host that never calls CountShopIntents).
        /// The pooled field stays reachable and pinned, exactly as the posted
        /// housing curve stays reachable behind `--posted`.</summary>
        public double CommercialCapture(int c, double mass)
            => HasCountedIntents && !MutantPooledEntry
               ? CountedIntent(c, mass) : PhantomCommercialCapture(c, mass);

        /// <summary>MUTANT SWITCH (`--mutant-pooled-entry`): restores
        /// PhantomCommercialCapture as the store-level entry signal — the flag
        /// comment's measured defect, verbatim. The entry-signal check's
        /// CALIBRATION and SATURATION legs must go red under it. Never a
        /// shipping mode.</summary>
        public static bool MutantPooledEntry;

        /// <summary>MUTANT SWITCH (`--mutant-intent-unsaturated`): counts every
        /// reachable household regardless of what it already settled for, i.e.
        /// drops bestSys from the probe. That IS the pooled defect in counted
        /// clothing — the signal stops saturating when a shop is built — which
        /// is why it is the right mutant for the SATURATION leg. Never a
        /// shipping mode.</summary>
        public static bool MutantIntentUnsaturated;

        /// <summary>Gumbel(0,1) from a stable hash — the same inverse-CDF trick
        /// the shop and location choices use, so a household's taste for a
        /// specific place never re-rolls.</summary>
        private static double Gumbel(ulong key)
        {
            double e = SplitMix64.Hash01(key);
            return -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
        }

        /// <summary>Phantom entrant (design §4.2): expected spending capture of a
        /// hypothetical new commercial firm of given mass at cluster c, inserted
        /// into the shopping logit against incumbents — the bucket machinery run
        /// hypothetically. O(C). Reached on the pooled path, and by
        /// CommercialCapture wherever the counted field was never built.</summary>
        public double PhantomCommercialCapture(int c, double newMass)
        {
            double captured = 0;
            for (int i = 0; i < C; i++)
            {
                double wNew = WShop[i, c] * newMass;
                captured += SpendMass[i] * wNew / (IncumbentShopWeight[i] + wNew);
            }
            return captured;
        }

        /// <summary>Expected household income at a cluster: the MEAN of the
        /// within-segment income distribution (Income.Build) — employment-
        /// weighted wages net of income tax, plus benefits and transfers.
        /// Net-of-tax is what makes the §4.3 incidence chain live: taxes reduce
        /// disposable income → lower bids → lower land values and LVT base
        /// (scrutiny finding #3). Never reads realized rents (§3 guard).
        ///
        /// Reading the distribution's own mean (rather than recomputing a
        /// closed form) keeps every aggregate consumer — migration's income
        /// term, construction's affordability proxy — exactly consistent with
        /// the tranches the clearing price walks.</summary>
        public double ExpectedIncome(int segment, int cluster, EconParams p)
        {
            int idx = segment * C + cluster;
            if (IncomeMean.Length > idx) return IncomeMean[idx];
            // Pre-refresh fallback (same distribution, computed on the spot).
            Span<double> bi = stackalloc double[Income.Bins];
            Span<double> bw = stackalloc double[Income.Bins];
            var seg0 = Segment.All[segment];
            double emp0 = seg0.Participation > 0 && EmploymentRate.Length == 3
                ? EmploymentRate[(int)seg0.Labor][cluster] : 0;
            return Income.Build(seg0, emp0, p, bi, bw);
        }

        /// <summary>Per-household income: EARNERS × the wage of the job level
        /// this household actually holds, plus benefit for its non-earning
        /// adults, plus transfers, floored at m_ResidentialMinimumEarnings.
        /// This is the individual draw from the same distribution the clearing
        /// price aggregates — allocation and pricing now share one income
        /// model (they did not before: pricing used a segment point estimate
        /// while allocation used a per-household bool).</summary>
        public static double HouseholdIncomeEstimate(Household h, Segment seg, EconParams p)
        {
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            int levels = Income.JobLevels(seg, p, lw, lwage);
            int lvl = Math.Min(h.JobLevel, levels - 1);
            int earners = Math.Min(h.Earners, Math.Max(0, seg.Adults));
            double wage = earners * lwage[lvl] * (1 - p.IncomeTax(seg.Labor));
            // The benefit stops at the allowance limit — so a long-term
            // unemployed household's affordability, stress and exit decisions
            // all see the cliff (CS2's labor-market clearing mechanism).
            double benefit = h.UnemployedTicks <= p.UnemploymentAllowanceTicks
                ? Math.Max(0, seg.Adults - earners) * p.UnemploymentBenefit
                  * MathUtil.Clamp(seg.Participation, 0, 1) : 0;
            return Math.Max(p.ResidentialMinimumEarnings, wage + benefit + seg.Transfer);
        }

        /// <summary>Income flow plus annuitized savings — what housing decisions
        /// (search caps, exit thresholds) compare against assessments.</summary>
        public static double EffectiveIncome(Household h, Segment seg, EconParams p)
            => HouseholdIncomeEstimate(h, seg, p) + Math.Max(0, h.Money) / p.WealthDrawdownTicks;

        private static double[][] NewJagged(int a, int b)
        {
            var r = new double[a][];
            for (int i = 0; i < a; i++) r[i] = new double[b];
            return r;
        }
    }
}
