using System;

namespace CS2Econ.Core
{
    // ---------------------------------------------------------------------
    // Ports (PLAN §1). The economy core communicates with its host ONLY
    // through these interfaces. In-game the adapters live in CS2Econ.Mod;
    // in the harness, SyntheticCity implements them.
    // ---------------------------------------------------------------------

    /// <summary>Cluster-level generalized travel costs — THE routing-rebuild
    /// dependency (design §3: all access terms come from cached CCH machinery;
    /// no independent distance computations in-game). Version bumps and dirty
    /// pairs ride the corridor dirty flags.</summary>
    public interface IAccessCosts
    {
        int ClusterCount { get; }
        /// <summary>Generalized cost (minutes) from cluster a to b for a purpose
        /// mode (commute / shopping / freight). Symmetric hosts may ignore purpose.</summary>
        double Cost(int a, int b, AccessPurpose purpose);
        /// <summary>Monotone stamp: bumped whenever any cost changed since last
        /// query — the engine refreshes affected matrices lazily.</summary>
        int Version { get; }
    }

    public enum AccessPurpose : byte { Commute, Shopping, Freight }

    /// <summary>Telemetry out (overlay logging, scenario recorders, UI bindings).</summary>
    public interface ITelemetrySink
    {
        void Metric(string name, double value);
    }

    public sealed class NullTelemetry : ITelemetrySink
    {
        public static readonly NullTelemetry Instance = new NullTelemetry();
        public void Metric(string name, double value) { }
    }
}
