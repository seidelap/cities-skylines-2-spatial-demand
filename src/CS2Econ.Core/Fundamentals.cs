using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Deterministic PRNG (same discipline as the routing repo): all
    /// stochastic draws derive from SplitMix64 streams seeded by entity ids, so
    /// identical seeds reproduce identical runs bit-for-bit.</summary>
    public struct SplitMix64
    {
        private ulong _s;
        public SplitMix64(ulong seed) { _s = seed; }

        public ulong NextULong()
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0,1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public int NextInt(int maxExclusive) => (int)(NextULong() % (ulong)maxExclusive);

        /// <summary>Stateless hash — used for per-entity phases (assessment
        /// anniversaries, employment sampling) so staggering needs no stored state
        /// and no shared clock (design §3 anti-synchronization).</summary>
        public static ulong Hash(ulong x)
        {
            x ^= x >> 33; x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33; x *= 0xC4CEB9FE1A85EC53UL;
            x ^= x >> 33; return x;
        }

        public static double Hash01(ulong x) => (Hash(x) >> 11) * (1.0 / 9007199254740992.0);
    }

    /// <summary>The single lump↔flow bridge (design §4.3): one annuity operator
    /// a(h,L) used in exactly three places — the structure charge, the conversion
    /// deduction, and write-offs. Converts a lump sum into an equivalent per-tick
    /// flow over horizon L at hurdle rate h (per tick).</summary>
    public static class Annuity
    {
        /// <summary>Flow equivalent of a lump: lump * h / (1 - (1+h)^-L).</summary>
        public static double FlowOf(double lump, double hPerTick, int horizonTicks)
        {
            if (lump == 0) return 0;
            if (hPerTick <= 0) return lump / horizonTicks;
            double f = 1.0 - Math.Pow(1.0 + hPerTick, -horizonTicks);
            return lump * hPerTick / f;
        }

        /// <summary>Lump equivalent of a flow (exact inverse of FlowOf).</summary>
        public static double LumpOf(double flow, double hPerTick, int horizonTicks)
        {
            if (flow == 0) return 0;
            if (hPerTick <= 0) return flow * horizonTicks;
            double f = 1.0 - Math.Pow(1.0 + hPerTick, -horizonTicks);
            return flow * f / hPerTick;
        }
    }

    /// <summary>Money accounts. Aggregate entity pools (households, firms) plus the
    /// treasury and the four sanctioned phantoms (design §3 open-economy
    /// boundaries). Phantom balances may go negative — they are taps and drains;
    /// everything else circulates.</summary>
    public enum Account : byte
    {
        Households,          // Σ of all household balances
        Firms,               // Σ of all firm balances
        Treasury,            // city budget: land tax, income tax, structure tax
        Escrow,              // Σ of all parcel upgrade escrows (earmarked wedge)
        SinkingFund,         // structure charges held against condition renewal
        PhantomDeveloper,    // expected-zero P&L construction counterparty
        OutsideWorld,        // trade, migration wealth carried in/out, materials
        PhantomBank,         // loans, capital charge h·V
        NationalCounterparty // pensions/transfers in, terminal escheat out (design §3)
    }

    /// <summary>Double-entry money ledger (PLAN §2). Every flow has a named source
    /// and sink; there is no way to create or destroy money except by construction
    /// error, which <see cref="Drift"/> exposes. The harness asserts Drift ≈ 0
    /// every scenario tick (correctness test #3).</summary>
    public sealed class Ledger
    {
        private readonly double[] _balance = new double[9];
        private readonly double _initialTotal;
        public readonly double[,] FlowByPair = new double[9, 9]; // cumulative, for telemetry

        public Ledger(double initialHouseholds, double initialFirms, double initialTreasury)
        {
            _balance[(int)Account.Households] = initialHouseholds;
            _balance[(int)Account.Firms] = initialFirms;
            _balance[(int)Account.Treasury] = initialTreasury;
            _initialTotal = initialHouseholds + initialFirms + initialTreasury;
        }

        public double Balance(Account a) => _balance[(int)a];

        public void Transfer(Account from, Account to, double amount)
        {
            if (amount == 0) return;
            if (amount < 0) { Transfer(to, from, -amount); return; }
            if (double.IsNaN(amount) || double.IsInfinity(amount))
                throw new InvalidOperationException($"ledger: non-finite transfer {from}->{to}");
            _balance[(int)from] -= amount;
            _balance[(int)to] += amount;
            FlowByPair[(int)from, (int)to] += amount;
        }

        /// <summary>Deviation of total money from the initial closed-system total.
        /// Must stay ~0 (floating-point tolerance) — phantoms are inside the sum.</summary>
        public double Drift()
        {
            double total = 0;
            for (int i = 0; i < _balance.Length; i++) total += _balance[i];
            return total - _initialTotal;
        }
    }

    public static class MathUtil
    {
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Logit choice over utilities with spread mu; returns index.
        /// Deterministic given rng state.</summary>
        public static int LogitChoice(IReadOnlyList<double> utils, double mu, ref SplitMix64 rng)
        {
            int n = utils.Count;
            if (n == 1) return 0;
            double max = double.NegativeInfinity;
            for (int i = 0; i < n; i++) if (utils[i] > max) max = utils[i];
            double sum = 0;
            Span<double> w = n <= 64 ? stackalloc double[n] : new double[n];
            for (int i = 0; i < n; i++) { w[i] = Math.Exp((utils[i] - max) / mu); sum += w[i]; }
            double r = rng.NextDouble() * sum, acc = 0;
            for (int i = 0; i < n; i++) { acc += w[i]; if (r < acc) return i; }
            return n - 1;
        }

        /// <summary>Exponential moving average step with per-tick smoothing alpha.</summary>
        public static double Ema(double prev, double sample, double alpha) => prev + alpha * (sample - prev);
    }
}
