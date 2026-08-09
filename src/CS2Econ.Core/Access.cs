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

        // Labor market outcomes (doubly-constrained balancing, §4.2)
        public double[][] EmploymentRate = Array.Empty<double[]>();  // [class][home cluster]

        // ---- within-segment income distribution (Income.cs) -----------------
        // Flat [(segment*C + cluster)*Income.Bins + bin]; bins are sorted
        // DESCENDING by income and their weights sum to 1 per (segment,
        // cluster). Rebuilt once per refresh from EmploymentRate — never in the
        // pricing hot path, which just reads them.
        public double[] IncomeBinInc = Array.Empty<double>();
        public double[] IncomeBinWt = Array.Empty<double>();
        /// <summary>[segment*C + cluster] mean of the distribution above — what
        /// ExpectedIncome reports, so aggregates and tranches never disagree.</summary>
        public double[] IncomeMean = Array.Empty<double>();

        /// <summary>Recompute the per-(segment, cluster) income distributions.
        /// Called at the end of Refresh, after EmploymentRate; exposed so tests
        /// can perturb employment and re-derive without a full refresh.</summary>
        public void RebuildIncomeDistributions(EconParams p)
        {
            int nSeg = Segment.Count, K = Income.Bins;
            int need = nSeg * C * K;
            if (IncomeBinInc.Length != need) { IncomeBinInc = new double[need]; IncomeBinWt = new double[need]; }
            if (IncomeMean.Length != nSeg * C) IncomeMean = new double[nSeg * C];
            Span<double> bi = stackalloc double[Income.Bins];
            Span<double> bw = stackalloc double[Income.Bins];
            for (int s = 0; s < nSeg; s++)
            {
                var seg = Segment.All[s];
                for (int c = 0; c < C; c++)
                {
                    double emp = seg.Participation > 0 ? EmploymentRate[(int)seg.Labor][c] : 0;
                    IncomeMean[s * C + c] = Income.Build(seg, emp, p, bi, bw);
                    int b0 = (s * C + c) * K;
                    for (int k = 0; k < K; k++) { IncomeBinInc[b0 + k] = bi[k]; IncomeBinWt[b0 + k] = bw[k]; }
                }
            }
        }
        public double[][] JobFillRate = Array.Empty<double[]>();     // [class][job cluster]
        public double[][] ResidualJobs = Array.Empty<double[]>();    // unfilled positions after balancing

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
        public void Refresh(WorldState w, IAccessCosts costs, EconParams p)
        {
            EnsureWeights(costs, p);
            int nc = Segment.Count;

            // ---- masses -----------------------------------------------------
            JobsByClass = NewJagged(3, C);
            WorkersByClass = NewJagged(3, C);
            CommercialMass = new double[C];
            SpendMass = new double[C];
            OfficeJobs = new double[C];

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
                        break;
                }
            }

            foreach (var h in w.Households)
            {
                if (h.HomeParcel < 0) continue;
                var seg = Segment.All[h.Segment];
                int c = w.Parcels[h.HomeParcel].Cluster;
                // Labor SUPPLY is per-adult participation × adults — the same
                // quantity the household's wage income is paid on, so the
                // matched-jobs wage bill charged to firms and the wages paid to
                // households stay in balance (paying per-earner while supplying
                // one worker per household double-charged every firm and killed
                // all industry — measured).
                if (seg.Participation > 0 && seg.Adults > 0)
                    WorkersByClass[(int)seg.Labor][c] += seg.Participation * seg.Adults;
                double income = HouseholdIncomeEstimate(h, seg, p);
                SpendMass[c] += income * p.BaseConsumptionShare;
            }

            // ---- labor market: one balanced matching per labor class --------
            EmploymentRate = NewJagged(3, C);
            JobFillRate = NewJagged(3, C);
            ResidualJobs = NewJagged(3, C);
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
                for (int c = 0; c < C; c++)
                {
                    double stock = HousingStock[k][c];
                    double target = stock > 0 ? MathUtil.Clamp(filled[k][c] / stock, 0, 1) : 1.0;
                    FillEma[k][c] = fillFirstBuild ? target
                                                   : MathUtil.Ema(FillEma[k][c], target, FillEmaAlpha);
                }

            // Income distributions BEFORE demand shares: the shares read
            // AccessValue only, but ExpectedIncome (used downstream by
            // migration and construction in the same tick) must already be
            // backed by this refresh's employment rates.
            RebuildIncomeDistributions(p);
            RebuildDemandShares();
        }

        /// <summary>Recompute SegmentKindShare from AccessValue, capacity,
        /// fill and density appeal. Called at the end of Refresh; exposed so
        /// tests can perturb one input (e.g. FillEma) and re-derive the
        /// demand field without re-running a whole refresh.</summary>
        public void RebuildDemandShares()
        {
            int C = this.C, nc = Segment.Count;
            // Where each segment WANTS to live, over (kind × cluster) jointly:
            // access logit × capacity, restricted to the densities that segment
            // can occupy, normalized so each segment's shares sum to 1 over the
            // whole feasible market. See SegmentKindShare's doc for why the
            // joint normalization and the capacity weight are both required.
            if (SegmentKindShare.Length != nc)
            {
                SegmentKindShare = new double[nc][][];
                for (int s = 0; s < nc; s++)
                    SegmentKindShare[s] = new double[2][];
            }
            for (int s = 0; s < nc; s++)
            {
                var seg = Segment.All[s];
                double tot = 0;
                for (int k = 0; k < 2; k++)
                {
                    if (SegmentKindShare[s][k] == null || SegmentKindShare[s][k].Length != C)
                        SegmentKindShare[s][k] = new double[C];
                    var row = SegmentKindShare[s][k];
                    // Density appeal deliberately does NOT weight the share:
                    // it lives on the WTP leg of ResidentialBidPerUnit only.
                    // With the joint (kind × cluster) normalization, an appeal
                    // weight here renormalizes the discounted high-density mass
                    // INTO the low-density rows — the apartment haircut becomes
                    // a house subsidy and the calibrated discount applies twice
                    // (adversarial review, measured A/B: low bids +40% from the
                    // renormalization alone).
                    for (int c = 0; c < C; c++)
                    {
                        // Fill-weighted capacity: demand follows housing that is
                        // ACTUALLY being taken up, so a submarket sitting vacant
                        // sheds estimated demand and its clearing price falls —
                        // the occupancy channel. Damped by the EMA and floored,
                        // and the loop is negative (dear → vacant → cheaper →
                        // refills), so it settles rather than spirals.
                        double fill = FillEma[k][c];
                        double attract = HousingCapacity[k][c]
                                         * (OccupancyFloor + (1 - OccupancyFloor) * fill);
                        row[c] = Math.Exp(AccessValue[s][c] / 1.5) * attract;
                        tot += row[c];
                    }
                }
                if (tot > 1e-12)
                    for (int k = 0; k < 2; k++)
                    {
                        var row = SegmentKindShare[s][k];
                        for (int c = 0; c < C; c++) row[c] /= tot;
                    }
            }
        }

        /// <summary>Phantom entrant (design §4.2): expected spending capture of a
        /// hypothetical new commercial firm of given mass at cluster c, inserted
        /// into the shopping logit against incumbents — the bucket machinery run
        /// hypothetically. O(C).</summary>
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
