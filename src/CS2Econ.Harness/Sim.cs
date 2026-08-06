using System;
using System.Collections.Generic;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>One simulation run: synthetic city + engine (+ vanilla spawner in
    /// baseline mode) + telemetry series the scenarios assert against.</summary>
    public sealed class Sim
    {
        public WorldState W = null!;
        public GridAccess Access = null!;
        public EconomyEngine Engine = null!;
        public EconParams P = null!;
        public FeatureFlags Flags = null!;
        public VanillaSpawner? Vanilla;

        // Telemetry series (per tick)
        public readonly List<int> Population = new List<int>();
        public readonly List<double> Treasury = new List<double>();
        public readonly List<double> LandRevenue = new List<double>();
        public readonly List<int> Sheltered = new List<int>();
        public readonly List<(long tick, int parcel, int cluster)> Starts = new List<(long, int, int)>();

        private int _lastStartedTotal;

        public static Sim Create(SyntheticCity.Config cfg, EconParams p, FeatureFlags flags, bool vanillaMode = false)
        {
            var sim = new Sim { P = p, Flags = flags };
            (sim.W, sim.Access) = SyntheticCity.Build(cfg, p);
            sim.Engine = new EconomyEngine(sim.W, sim.Access, p, flags);
            if (vanillaMode)
            {
                flags.ConstructionRewire = false;
                flags.TierC2_Leveling = false;
                sim.Vanilla = new VanillaSpawner();
            }
            return sim;
        }

        public void Run(int ticks, Action<Sim>? perTick = null)
        {
            for (int t = 0; t < ticks; t++) Step(perTick);
        }

        public void Step(Action<Sim>? perTick = null)
        {
            // Record construction starts with their location (for localization tests).
            var beforeStates = _pendingStartScan ? CaptureUnderConstruction() : null;

            Engine.Step();
            Vanilla?.Step(W, P);

            if (_pendingStartScan && beforeStates != null)
            {
                foreach (var pl in W.Parcels)
                    if (pl.State == ParcelState.UnderConstruction && !beforeStates.Contains(pl.Id))
                        Starts.Add((W.Tick, pl.Id, pl.Cluster));
            }

            int pop = 0, shel = 0;
            foreach (var h in W.Households)
                if (h.ExitedTick < 0) { pop++; if (h.Stage == InsolvencyStage.Sheltered) shel++; }
            Population.Add(pop);
            Sheltered.Add(shel);
            Treasury.Add(W.Ledger.Balance(Account.Treasury));
            LandRevenue.Add(Engine.LandRevenueThisTick);
            _lastStartedTotal = Engine.Construction.StartedTotal;
            perTick?.Invoke(this);
        }

        private bool _pendingStartScan = true;
        private HashSet<int> CaptureUnderConstruction()
        {
            var s = new HashSet<int>();
            foreach (var pl in W.Parcels)
                if (pl.State == ParcelState.UnderConstruction) s.Add(pl.Id);
            return s;
        }

        public ulong TelemetryHash()
        {
            ulong h = 1469598103934665603UL;
            void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
            foreach (var hh in W.Households)
            {
                Mix((ulong)hh.HomeParcel + 7);
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(hh.Money, 6)));
            }
            foreach (var pl in W.Parcels)
            {
                Mix((ulong)pl.Level); Mix((ulong)pl.State);
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(pl.Escrow, 6)));
            }
            Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(W.Ledger.Balance(Account.Treasury), 6)));
            return h;
        }
    }
}
