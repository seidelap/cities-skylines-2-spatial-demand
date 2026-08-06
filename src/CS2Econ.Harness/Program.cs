using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>Standalone harness (PLAN §4): `verify` runs the correctness
    /// suite; `scenarios` runs the §6 acceptance targets (several as A/Bs against
    /// the vanilla baseline); `all` runs everything and writes RESULTS.md.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "all";
            ulong seed = 20260806;
            string? only = null;
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                if (args[i] == "--seed") seed = ulong.Parse(args[i + 1]);
                if (args[i] == "--only") only = args[i + 1];
            }

            switch (cmd)
            {
                case "verify":
                    return TestRunner.RunAll(seed, out _);
                case "scenarios":
                    return Scenarios.RunAll(seed, out _, only);
                case "all":
                {
                    int v = TestRunner.RunAll(seed, out var verifyMd);
                    Console.WriteLine();
                    int s = Scenarios.RunAll(seed, out var scenarioMd);
                    var md = new StringBuilder();
                    md.AppendLine("# CS2 Spatial Demand & Land Economy — Harness Results");
                    md.AppendLine();
                    md.AppendLine("Measured by `CS2Econ.Harness` (see README for commands) on a Linux");
                    md.AppendLine("container, .NET 8, single machine. Correctness checks are deterministic;");
                    md.AppendLine("acceptance scenarios follow the design's §6 targets, several as A/Bs");
                    md.AppendLine("against the vanilla-baseline mode on identical seeds.");
                    md.AppendLine($"Seed: {seed}.");
                    md.AppendLine();
                    md.AppendLine(verifyMd);
                    md.AppendLine(scenarioMd);
                    File.WriteAllText("RESULTS.md", md.ToString());
                    Console.WriteLine("\nwrote RESULTS.md");
                    return v != 0 || s != 0 ? 1 : 0;
                }
                case "debug":
                {
                    int ticks = 800;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.Run(seed, ticks);
                }
                default:
                    Console.WriteLine("usage: harness [verify|scenarios|all|debug] [--seed N] [--ticks N]");
                    return 2;
            }
        }
    }
}
