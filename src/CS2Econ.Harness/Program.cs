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
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--store-level") { Sim.ForceStoreLevelSpending = true; continue; }
                if (args[i] == "--auction") { Sim.ForceHousingAuction = true; continue; }
                if (args[i] == "--posted") { Sim.ForcePosted = true; continue; }
                // MUTANT SWITCH (see AccessState.MutantCitywideProspectOdds):
                // restores the zero-diluted citywide prospect odds so the
                // prospect-local-odds check can be shown to fail. Never a
                // shipping mode.
                if (args[i] == "--mutant-citywide-odds") { AccessState.MutantCitywideProspectOdds = true; continue; }
                if (i + 1 >= args.Length) continue;
                if (args[i] == "--seed") seed = ulong.Parse(args[i + 1]);
                if (args[i] == "--only") only = args[i + 1];
            }

            switch (cmd)
            {
                case "verify":
                    return TestRunner.RunAll(seed, out _);
                case "canary":
                {
                    int n = 32, from = 0;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                        // `--from K` sweeps [K, n) alone, without the pinned
                        // extras: the instrument for dissecting one failing
                        // seed (a full canary pays ~9s a seed; a knife-edge
                        // dissection needs one).
                        if (args[i] == "--from") from = int.Parse(args[i + 1]);
                    }
                    var set = new List<ulong>();
                    for (ulong s = (ulong)from; s < (ulong)n; s++) set.Add(s);
                    if (from > 0) return TestRunner.Canary(set);

                    foreach (ulong s in new ulong[] { 138, 208, 271, 327, 549, 910, 6550 })
                        if (!set.Contains(s)) set.Add(s);
                    return TestRunner.Canary(set);
                }
                case "laborcanary":
                {
                    // The labor-equilibrium check alone, across the same seed
                    // list as the housing canary.
                    int n = 32;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    foreach (ulong s in new ulong[] { 138, 208, 271, 327, 549, 910, 6550 })
                        if (!set.Contains(s)) set.Add(s);
                    return TestRunner.LaborCanary(set);
                }
                case "prospectsweep":
                {
                    int n = 9;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)(n > 8 ? 8 : n); s++) set.Add(s);
                    if (n >= 9) set.Add(25);      // the knife-edge seed rides along
                    for (ulong s = 26; set.Count < n; s++) set.Add(s);
                    return TestRunner.ProspectSweep(set);
                }
                case "occsweep":    // occupancy-channel check alone across seeds
                {
                    int n = 32;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    foreach (ulong s in new ulong[] { 138, 208, 271, 327, 549, 910, 6550 })
                        if (!set.Contains(s)) set.Add(s);
                    return TestRunner.OccupancySweep(set);
                }
                case "assesscheck": // assessment-tracks-price fixture alone (see TestRunner.AssessCheck)
                    return TestRunner.AssessCheck(seed);
                case "pulsesweep": // uniform-pulse check alone across seeds (see TestRunner.PulseSweep)
                case "tiesweep":   // tie-channel check alone across seeds (see TestRunner.TieSweep)
                case "calibsweep": // per-cell calibration check alone across seeds (see TestRunner.CalibSweep)
                {
                    int n = 8;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                        if (args[i] == "--tie-bonus") TestRunner.TieBonusOverride = double.Parse(args[i + 1]);
                    }
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    foreach (ulong s in new ulong[] { 9, 13 })   // the verify-pinned seeds ride along
                        if (!set.Contains(s)) set.Add(s);
                    return cmd == "pulsesweep" ? TestRunner.PulseSweep(set)
                         : cmd == "tiesweep" ? TestRunner.TieSweep(set)
                         : TestRunner.CalibSweep(set);
                }
                case "scenarios":
                    return Scenarios.RunAll(seed, out _, only);
                case "fingerprint":
                    // Print the lanes; --check compares against the checked-in
                    // baseline; --accept --reason "..." records a new stanza.
                    // The fixture is PINNED and ignores --seed on purpose.
                    return Fingerprint.Run(args);
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
                case "churnprobe":
                {
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.ChurnProbe(seed, ticks);
                }
                case "lambdasweep":
                {
                    int ticks = 200;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.LambdaSweep(seed, ticks);
                }
                case "indprobe":
                {
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.IndProbe(seed, ticks);
                }
                case "incomeprobe":
                {
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.IncomeProbe(seed, ticks);
                }
                case "priceprobe":
                {
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.PriceProbe(seed, ticks);
                }
                case "auctionprobe":
                {
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.AuctionProbe(seed, ticks);
                }
                case "scanoracle":
                {
                    // The repair scan's differential oracle: the same fixture as
                    // auctionprobe, with every scan run a second time by the
                    // untouched reference implementation and the two answers
                    // compared per household and as a set. Slow on purpose — it
                    // does the scan twice — and the number that matters is that
                    // every mismatch counter reads zero.
                    int ticks = 300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    HousingAuction.ScanOracle = true;
                    return Debugging.AuctionProbe(seed, ticks);
                }
                case "jobspread":
                {
                    int ticks = 240;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.JobSpread(seed, ticks);
                }
                case "levelprobe":
                {
                    int ticks = 1300;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.LevelProbe(seed, ticks);
                }
                case "vacprobe":
                {
                    int ticks = 150;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.VacProbe(seed, ticks);
                }
                case "firmdiag":
                {
                    int ticks = 800;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                    return Debugging.FirmDiag(seed, ticks);
                }
                case "map":
                {
                    // Real-map run: economy on an imported .cs2city road graph
                    // (default: the committed Chicago Regional network, converted
                    // from TNTP on first use).
                    int ticks = 400;
                    string file = Path.Combine("data", "chicago-regional.cs2city");
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--file") file = args[i + 1];
                    }
                    return Debugging.MapRun(seed, ticks, file);
                }
                default:
                    Console.WriteLine("usage: harness [verify|scenarios|all|fingerprint|debug|map] [--seed N] [--ticks N] [--file path.cs2city]");
                    Console.WriteLine("       fingerprint [--check | --accept --reason \"...\"]");
                    return 2;
            }
        }
    }
}
