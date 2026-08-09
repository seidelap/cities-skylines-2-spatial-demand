using System;

namespace CS2Econ.Core
{
    /// <summary>Within-segment household income DISTRIBUTION, built from CS2's
    /// own income model (research notes §3, Game.Prefabs.EconomyParameterData):
    ///
    ///   m_Wage0..m_Wage4       wages by JOB LEVEL. The paid level is
    ///                          Game.Citizens.Worker.m_Level, which is the job
    ///                          held — not the citizen's education: a citizen
    ///                          who cannot find an opening at their tier takes a
    ///                          lower one (Game.Companies.FreeWorkplaces is
    ///                          per-tier and runs out), and is paid for the job.
    ///   m_UnemploymentBenefit  what a working-age adult with no job receives.
    ///   m_Pension              retired transfer     } folded into Segment.Transfer
    ///   m_FamilyAllowance      per-child transfer   } — see that field's doc.
    ///   m_ResidentialMinimumEarnings  floor under household earnings.
    ///
    /// A household's income is therefore a SUM over its adults, each employed or
    /// not and, if employed, holding one of several job levels. A segment is not
    /// one income — it is a distribution with three real dispersion sources:
    /// earner count (0..Adults), job level, and employment. All three are read
    /// straight off the game (Game.Citizens.HouseholdMember buffer for adults,
    /// Worker component for earners, Worker.m_Level for the level).
    ///
    /// This replaces the old point estimate — segment mean × a hand-tuned
    /// MarginalIncomeQuantile, a scalar standing in for exactly this
    /// distribution. Consequences: the demand curve becomes strictly decreasing
    /// instead of an 8-step staircase, so the clearing price slides continuously
    /// with quantity rather than jumping when a whole segment leaves the queue
    /// (measured before this change: 1.08 → 2.69 → 4.30 → 7.34 → 21.96 as
    /// segments crossed the presence gate); the marginal bidder is FOUND rather
    /// than approximated; and the within-segment spread that dominates the
    /// between-segment spread — at ~55% employment a segment is roughly half
    /// wage-earners and half benefit recipients — is finally represented.</summary>
    public static class Income
    {
        /// <summary>Quantile bins per (segment, cluster). Each bin is a real
        /// sub-population with its own income, so 8 segments × 5 bins gives a
        /// 40-tranche demand curve. Bins are equal-PROBABILITY (not equal-width)
        /// so the bimodal employed/unemployed shape is resolved where the mass
        /// actually is.</summary>
        public const int Bins = 5;

        /// <summary>Max adults per household the enumeration supports. CS2
        /// households can be larger, but the adapter caps earners at 2 for the
        /// distribution (a third earner adds little spread and squares the
        /// outcome space); the CAP is on the distribution's shape, not on the
        /// simulated household's actual earnings.</summary>
        public const int MaxAdults = 2;

        /// <summary>Job levels available to a labor class: the top level it can
        /// hold, with everything below reachable through over-qualification.
        /// Mirrors CS2's five FreeWorkplaces tiers collapsed onto our three
        /// labor classes (the IPF matching dimension).</summary>
        public static int TopJobLevel(LaborClass c) => c switch
        {
            LaborClass.Basic => 1,
            LaborClass.Skilled => 3,
            _ => 4,
        };

        /// <summary>Job-level weights and per-level wages for a segment, written
        /// into the caller's spans; returns the number of levels (T+1).
        ///
        /// Weights decay downward at p.JobLevelDownshift — most workers hold the
        /// best job their tier allows, some are over-qualified. Wages rise by
        /// p.JobLevelSpread per level, then are NORMALIZED so the weighted mean
        /// equals EconParams.Wage(class) exactly. That is deliberate: the
        /// dispersion is added at CONSTANT MEAN, so no calibrated aggregate
        /// (migration's income term, construction's affordability proxy, the
        /// firm wage bill) moves when this replaces the point income.</summary>
        public static int JobLevels(Segment seg, EconParams p, Span<double> weight, Span<double> wage)
        {
            int top = TopJobLevel(seg.Labor);
            int n = top + 1;
            double d = MathUtil.Clamp(p.JobLevelDownshift, 0.05, 1.0);
            double g = Math.Max(1.0, p.JobLevelSpread);
            double sumW = 0, sumWF = 0;
            for (int l = 0; l <= top; l++)
            {
                weight[l] = Math.Pow(d, top - l);        // top level heaviest
                wage[l] = Math.Pow(g, l - top);          // relative to the top job
                sumW += weight[l];
                sumWF += weight[l] * wage[l];
            }
            // Normalize weights to 1 and rescale wages so Σ w·wage = Wage(class).
            double mean = sumWF / sumW;
            double scale = p.Wage(seg.Labor) / mean;
            for (int l = 0; l <= top; l++)
            {
                weight[l] /= sumW;
                wage[l] *= scale;
            }
            return n;
        }

        /// <summary>Build the household income distribution for a segment at a
        /// cluster with the given employment rate, as `Bins` equal-probability
        /// quantile bins sorted DESCENDING by income. Returns the distribution
        /// mean (which is what ExpectedIncome reports, so pricing and every
        /// aggregate consumer read the same distribution).
        ///
        /// Two simplifications, documented: (a) labor-force participation
        /// below 1 (only StudentLow) is folded into the per-adult employment
        /// probability, and a non-participating adult collects no benefit —
        /// exact on the wage leg, an expectation on the small benefit leg;
        /// (b) the enumeration draws each earner's job level INDEPENDENTLY,
        /// while a realized household carries one JobLevel for all its earners
        /// — the means agree exactly, realized incomes are slightly more
        /// dispersed than the priced curve (assortative households).</summary>
        public static double Build(Segment seg, double empRate, EconParams p,
                                   Span<double> binInc, Span<double> binWt)
        {
            Span<double> lw = stackalloc double[5];
            Span<double> lwage = stackalloc double[5];
            int levels = JobLevels(seg, p, lw, lwage);
            double net = 1.0 - p.IncomeTax(seg.Labor);

            // Outcome space: earner count × job levels. ≤ 1 + 5 + 15 = 21.
            Span<double> inc = stackalloc double[32];
            Span<double> wt = stackalloc double[32];
            int n = 0;

            int adults = Math.Min(MaxAdults, seg.Adults);
            double e = MathUtil.Clamp(seg.Participation * empRate, 0, 1);
            double ub = p.UnemploymentBenefit * MathUtil.Clamp(seg.Participation, 0, 1);

            // Static local function: capturing the stackalloc spans in a closure
            // is illegal, and passing them explicitly keeps this allocation-free.
            static void Add(Span<double> inc_, Span<double> wt_, ref int n_,
                            double income, double weight, double floor)
            {
                if (weight <= 0) return;
                inc_[n_] = Math.Max(floor, income);
                wt_[n_] = weight;
                n_++;
            }
            double fl = p.ResidentialMinimumEarnings;

            if (adults <= 0)
            {
                Add(inc, wt, ref n, seg.Transfer, 1.0, fl);        // retired: transfer only
            }
            else if (adults == 1)
            {
                Add(inc, wt, ref n, ub + seg.Transfer, 1 - e, fl);
                for (int l = 0; l < levels; l++)
                    Add(inc, wt, ref n, lwage[l] * net + seg.Transfer, e * lw[l], fl);
            }
            else
            {
                Add(inc, wt, ref n, 2 * ub + seg.Transfer, (1 - e) * (1 - e), fl);
                double pOne = 2 * e * (1 - e);
                for (int l = 0; l < levels; l++)
                    Add(inc, wt, ref n, lwage[l] * net + ub + seg.Transfer, pOne * lw[l], fl);
                double pTwo = e * e;
                for (int a = 0; a < levels; a++)
                    for (int b = a; b < levels; b++)
                        Add(inc, wt, ref n, (lwage[a] + lwage[b]) * net + seg.Transfer,
                            pTwo * lw[a] * lw[b] * (a == b ? 1.0 : 2.0), fl);
            }

            // Sort outcomes DESCENDING by income (insertion sort; n ≤ 21).
            for (int i = 1; i < n; i++)
            {
                double ki = inc[i], kw = wt[i];
                int j = i - 1;
                while (j >= 0 && inc[j] < ki) { inc[j + 1] = inc[j]; wt[j + 1] = wt[j]; j--; }
                inc[j + 1] = ki; wt[j + 1] = kw;
            }

            double total = 0, meanAll = 0;
            for (int i = 0; i < n; i++) { total += wt[i]; meanAll += wt[i] * inc[i]; }
            if (total <= 1e-12)
            {
                for (int k = 0; k < Bins; k++) { binInc[k] = 0; binWt[k] = 0; }
                return 0;
            }
            meanAll /= total;

            // Equal-probability quantile bins, each carrying its probability-
            // weighted mean income. Outcomes are split across bin boundaries, so
            // Σ binWt·binInc == meanAll exactly — the constant-mean guarantee.
            double per = total / Bins;
            int oi = 0;
            double remaining = n > 0 ? wt[0] : 0;
            for (int k = 0; k < Bins; k++)
            {
                double need = per, acc = 0, accInc = 0;
                while (need > 1e-15 && oi < n)
                {
                    double take = Math.Min(need, remaining);
                    acc += take;
                    accInc += take * inc[oi];
                    remaining -= take;
                    need -= take;
                    if (remaining <= 1e-15)
                    {
                        oi++;
                        remaining = oi < n ? wt[oi] : 0;
                    }
                }
                binWt[k] = acc / total;                       // shares sum to 1
                binInc[k] = acc > 1e-15 ? accInc / acc : 0;
            }
            return meanAll;
        }
    }
}
