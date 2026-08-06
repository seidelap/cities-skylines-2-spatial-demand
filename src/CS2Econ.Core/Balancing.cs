using System;

namespace CS2Econ.Core
{
    /// <summary>Doubly-constrained balancing (design §4.2): jobs claimed equal
    /// jobs available and workers claimed equal workers available.
    ///
    /// Formulation: each supply unit distributes over destinations plus an
    /// outside option (reservation weight e^(−θ·slackMinutes)); destination
    /// scale factors b_j are then rationed down (Sinkhorn-style multiplicative
    /// projection) until no destination exceeds its capacity. Matched flows
    /// respect BOTH marginals as inequalities and the two sides see the same
    /// matched total; the slack magnitudes ARE the residuals (friction
    /// unemployment and unfilled positions) that Tier B carries everywhere.</summary>
    public static class Balancing
    {
        /// <summary>Returns matched flows T[i,j] with rowMatched[i] ≤ supply[i],
        /// colMatched[j] ≤ demand[j], Σrow = Σcol.</summary>
        public static double[,] Match(
            double[] supply, double[] demand, double[,] w, double slackWeight,
            int iterations, out double[] rowMatched, out double[] colMatched)
        {
            int n = supply.Length, m = demand.Length;
            var b = new double[m];
            for (int j = 0; j < m; j++) b[j] = 1;
            var col = new double[m];

            for (int it = 0; it < iterations; it++)
            {
                Array.Clear(col, 0, m);
                for (int i = 0; i < n; i++)
                {
                    if (supply[i] <= 0) continue;
                    double denom = slackWeight;
                    for (int j = 0; j < m; j++) denom += w[i, j] * b[j];
                    if (denom <= 0) continue;
                    double scale = supply[i] / denom;
                    for (int j = 0; j < m; j++) col[j] += scale * w[i, j] * b[j];
                }
                for (int j = 0; j < m; j++)
                    if (col[j] > demand[j] && col[j] > 1e-12)
                        b[j] *= demand[j] / col[j];   // ration oversubscribed destinations
            }

            var t = new double[n, m];
            rowMatched = new double[n];
            colMatched = new double[m];
            for (int i = 0; i < n; i++)
            {
                if (supply[i] <= 0) continue;
                double denom = slackWeight;
                for (int j = 0; j < m; j++) denom += w[i, j] * b[j];
                if (denom <= 0) continue;
                double scale = supply[i] / denom;
                for (int j = 0; j < m; j++)
                {
                    double f = scale * w[i, j] * b[j];
                    t[i, j] = f;
                    rowMatched[i] += f;
                    colMatched[j] += f;
                }
            }
            // A final exact pass: rationing can leave tiny overshoot on the last
            // iteration's columns; clamp proportionally so the inequality is hard.
            for (int j = 0; j < m; j++)
            {
                if (colMatched[j] <= demand[j] || colMatched[j] <= 1e-12) continue;
                double k = demand[j] / colMatched[j];
                for (int i = 0; i < n; i++)
                {
                    double delta = t[i, j] * (1 - k);
                    t[i, j] -= delta;
                    rowMatched[i] -= delta;
                }
                colMatched[j] = demand[j];
            }
            return t;
        }
    }
}
