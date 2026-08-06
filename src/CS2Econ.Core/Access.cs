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
        public double[][] JobFillRate = Array.Empty<double[]>();     // [class][job cluster]
        public double[][] ResidualJobs = Array.Empty<double[]>();    // unfilled positions after balancing

        // Composite consumer access value per segment per cluster
        public double[][] AccessValue = Array.Empty<double[]>();
        public double MeanAccess = 1;

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
                if (seg.Participation > 0) WorkersByClass[(int)seg.Labor][c] += seg.Participation;
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

        /// <summary>Expected household income at a cluster: employment-probability-
        /// weighted wage plus transfers. Used for bids and migration; never reads
        /// realized rents (§3 circularity guard).</summary>
        public double ExpectedIncome(int segment, int cluster, EconParams p)
        {
            var seg = Segment.All[segment];
            double emp = seg.Participation > 0 ? EmploymentRate[(int)seg.Labor][cluster] : 0;
            return seg.Participation * emp * p.Wage(seg.Labor) + seg.Transfer;
        }

        public static double HouseholdIncomeEstimate(Household h, Segment seg, EconParams p)
            => (h.Employed ? p.Wage(seg.Labor) : 0) + seg.Transfer;

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
