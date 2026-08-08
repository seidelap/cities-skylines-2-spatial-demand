using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Household allocation (design §4.2): arriving/relocating households
    /// score clusters by segment access minus price and take the cheapest feasible
    /// vacant unit where they land — the bucket-style property search that removes
    /// the truncated-queue bug class. Also the bottom-of-the-market insolvency
    /// pipeline: cut consumption → re-sort down the price gradient → funded
    /// emigration → sheltered homelessness with real capacity.</summary>
    public sealed class AllocationSystem
    {
        // Rebuilt each tick by the engine: residential parcels with lettable vacancies.
        public readonly List<int>[] VacantByCluster;
        private readonly int _clusterCount;

        public AllocationSystem(int clusterCount)
        {
            _clusterCount = clusterCount;
            VacantByCluster = new List<int>[clusterCount];
            for (int i = 0; i < clusterCount; i++) VacantByCluster[i] = new List<int>();
        }

        public void RebuildVacancies(WorldState w)
        {
            for (int i = 0; i < _clusterCount; i++) VacantByCluster[i].Clear();
            for (int i = 0; i < w.Parcels.Count; i++)
            {
                var pl = w.Parcels[i];
                if (pl.State == ParcelState.Built && pl.IsResidential && !pl.Warehousing && pl.Vacant > 0)
                    VacantByCluster[pl.Cluster].Add(i);
            }
        }

        /// <summary>Effective price a segment is willing to see for a unit of
        /// this density: the posted assessment divided by the segment's
        /// preference weight. A density-averse household is not BARRED from an
        /// apartment — it simply experiences the same rent as worse value, so
        /// it takes one only when it is cheap enough or nothing else is left.
        /// (Replaces the old hard DensityFeasible gate; see Segment
        /// .DensityAppeal for why the gate was a category error.)</summary>
        public static double PerceivedCost(Segment seg, ZoneKind kind, double assessment)
            => assessment / Math.Max(1e-6, seg.DensityAppeal(kind));

        /// <summary>Find a home: logit over clusters on (access value − rent
        /// burden), then cheapest feasible unit within the chosen cluster.
        /// maxAssessment caps what the household will take (insolvency re-sorts
        /// pass a tighter cap). Returns parcel id or −1.</summary>
        public int FindHome(WorldState w, AccessState acc, Household h, EconParams p,
                            double maxAssessment, double income)
        {
            var seg = Segment.All[h.Segment];
            int bestCluster = -1;
            Span<double> utils = stackalloc double[8];
            Span<int> clusters = stackalloc int[8];
            int nCand = 0;
            double worstKept = double.NegativeInfinity;

            for (int c = 0; c < _clusterCount; c++)
            {
                var list = VacantByCluster[c];
                if (list.Count == 0) continue;
                // Cheapest unit as this segment PERCEIVES it (density-discounted),
                // but affordability is tested against the cash actually owed.
                double cheapest = double.PositiveInfinity;
                for (int k = 0; k < list.Count; k++)
                {
                    var pl = w.Parcels[list[k]];
                    double cash = LandAccounting.UnitAssessment(pl, p);
                    if (cash > maxAssessment) continue;
                    double a = PerceivedCost(seg, pl.Use, cash);
                    if (a < cheapest) cheapest = a;
                }
                if (double.IsInfinity(cheapest)) continue;
                double burden = income > 0.1 ? cheapest / (seg.MaxRentShare * income) : 2.0;
                double u = acc.AccessValue[h.Segment][c] - 1.2 * burden;

                if (nCand < 8) { clusters[nCand] = c; utils[nCand] = u; nCand++; worstKept = Math.Min(worstKept == double.NegativeInfinity ? u : worstKept, u); }
                else
                {
                    int worst = 0;
                    for (int i = 1; i < 8; i++) if (utils[i] < utils[worst]) worst = i;
                    if (u > utils[worst]) { utils[worst] = u; clusters[worst] = c; }
                }
            }
            if (nCand == 0) return -1;

            // Logit choice among the top candidate clusters (heterogeneity via rng).
            double mu = 0.25;
            double max = double.NegativeInfinity;
            for (int i = 0; i < nCand; i++) if (utils[i] > max) max = utils[i];
            double sum = 0;
            Span<double> wgt = stackalloc double[8];
            for (int i = 0; i < nCand; i++) { wgt[i] = Math.Exp((utils[i] - max) / mu); sum += wgt[i]; }
            double r = w.Rng.NextDouble() * sum, accum = 0;
            for (int i = 0; i < nCand; i++) { accum += wgt[i]; if (r < accum) { bestCluster = clusters[i]; break; } }
            if (bestCluster < 0) bestCluster = clusters[nCand - 1];

            // Best-value unit in the chosen cluster, ranked by PERCEIVED cost
            // (so a density-averse household takes the apartment only when it
            // is cheap enough to outweigh the discount), affordability still
            // gated on the cash assessment.
            int bestParcel = -1; double bestA = double.PositiveInfinity;
            foreach (int pi in VacantByCluster[bestCluster])
            {
                var pl = w.Parcels[pi];
                if (pl.Vacant == 0) continue;
                double cash = LandAccounting.UnitAssessment(pl, p);
                if (cash > maxAssessment) continue;
                double a = PerceivedCost(seg, pl.Use, cash);
                if (a < bestA) { bestA = a; bestParcel = pi; }
            }
            return bestParcel;
        }

        public void MoveIn(WorldState w, Household h, int parcelId, EconParams p)
        {
            var pl = w.Parcels[parcelId];
            pl.OccupantHouseholds.Add(h.Id);
            h.HomeParcel = parcelId;
            h.TenureStart = w.Tick;
            h.ChargedAssessment = LandAccounting.UnitAssessment(pl, p);
            if (h.Stage == InsolvencyStage.Sheltered) h.Stage = InsolvencyStage.CutConsumption;
            w.HouseholdCountByCluster[pl.Cluster]++;
        }

        public void Vacate(WorldState w, Household h)
        {
            if (h.HomeParcel < 0) return;
            var pl = w.Parcels[h.HomeParcel];
            pl.OccupantHouseholds.Remove(h.Id);
            w.HouseholdCountByCluster[pl.Cluster]--;
            h.HomeParcel = -1;
            // Every unit actually freed passes through here — the measured
            // turnover flow the migration absorption budget reads (an EMA in
            // the engine), replacing the assumed-constant turnover forecast
            // that admitted ~50× more arrivals than real slots (churnprobe:
            // 98% of exits were arrivals that never found a unit).
            if (pl.IsResidential)
                w.FreedUnitsThisTick[pl.Use == ZoneKind.ResidentialHigh ? 1 : 0] += 1;
        }

        /// <summary>One insolvency-pipeline step for a stressed household
        /// (design §4.2). Stages fire only when the previous is exhausted.
        /// Returns true if the household left the city.</summary>
        public bool InsolvencyStep(WorldState w, AccessState acc, Household h, EconParams p,
                                   ref int shelterOccupied, int shelterCapacity)
        {
            var seg = Segment.All[h.Segment];
            double income = AccessState.EffectiveIncome(h, seg, p);

            if (h.StressTicks <= p.InsolvencyGraceTicks) return false;

            if (h.Stage == InsolvencyStage.Solvent) { h.Stage = InsolvencyStage.CutConsumption; return false; }

            if (h.Stage == InsolvencyStage.CutConsumption && h.StressTicks > 2 * p.InsolvencyGraceTicks)
            {
                // Re-sort down the price gradient: cheaper submarkets genuinely
                // exist because S is charged on V (decayed stock is cheap, §4.3).
                double cap = Math.Max(0.4 * h.ChargedAssessment, 0.7 * seg.MaxRentShare * Math.Max(income, seg.Transfer));
                int target = FindHome(w, acc, h, p, cap, income);
                if (target >= 0 && (h.HomeParcel < 0 || target != h.HomeParcel))
                {
                    Vacate(w, h);
                    MoveIn(w, h, target, p);
                    h.StressTicks = p.InsolvencyGraceTicks;   // partial relief
                    return false;
                }
                h.Stage = InsolvencyStage.SortDown;
                return false;
            }

            if ((h.Stage == InsolvencyStage.SortDown || h.Stage == InsolvencyStage.CutConsumption)
                && h.StressTicks > 3 * p.InsolvencyGraceTicks)
            {
                if (h.Money >= p.EmigrationMoveCost)
                {
                    // Funded emigration: wealth leaves with the household.
                    Vacate(w, h);
                    w.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                    h.Money = 0; h.ExitedTick = w.Tick;
                    return true;
                }
                if (shelterOccupied < shelterCapacity)
                {
                    Vacate(w, h);
                    h.Stage = InsolvencyStage.Sheltered;
                    shelterOccupied++;
                    return false;
                }
                // No funds, no shelter: forced exit with whatever remains.
                Vacate(w, h);
                w.Ledger.Transfer(Account.Households, Account.OutsideWorld, Math.Max(0, h.Money));
                h.Money = 0; h.ExitedTick = w.Tick;
                return true;
            }
            return false;
        }
    }
}
