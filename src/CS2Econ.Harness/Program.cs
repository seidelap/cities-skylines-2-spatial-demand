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
                // MUTANT SWITCH (see AccessState.MutantFillBlindShares): severs
                // realized occupancy from the location decision, so the
                // occupancy check's price leg can be shown to fail. Never a
                // shipping mode.
                if (args[i] == "--mutant-fill-blind") { AccessState.MutantFillBlindShares = true; continue; }
                // MUTANT SWITCH (see AccessState.MutantFillEmaSource): the
                // occupancy EMA stops reading its own submarket's realized
                // fill, so the occupancy check's tracking leg can be shown to
                // fail. Never a shipping mode.
                if (args[i] == "--mutant-fill-pooled") { AccessState.MutantFillEmaSource = 1; continue; }
                if (args[i] == "--mutant-fill-alpha") { AccessState.MutantFillEmaSource = 2; continue; }
                if (args[i] == "--mutant-fill-frozen") { AccessState.MutantFillEmaSource = 3; continue; }
                if (args[i] == "--mutant-fill-shift") { AccessState.MutantFillEmaSource = 4; continue; }
                // MUTANT SWITCH (see EconParams.MutantRelativeOutsideAccess):
                // anchors the outside door on the city's own MeanAccess, so a
                // spatially uniform improvement is invisible to the come/stay
                // margin — how the boom/bust scenario's response leg is shown
                // to fail. Never a shipping mode.
                if (args[i] == "--mutant-relative-outside")
                { EconParams.MutantRelativeOutsideAccessDefault = true; continue; }
                // MUTANT SWITCH (see TradeSystem.MutantCitywideGoodsPrice):
                // restores the one-scalar citywide goods price so the
                // goods-price localization check can be shown to fail. Never a
                // shipping mode.
                if (args[i] == "--mutant-citywide-goods") { TradeSystem.MutantCitywideGoodsPrice = true; continue; }
                // MUTANT SWITCH (see LaborAuction.MutantRevenueEmaCap):
                // restores the revenue-EMA commercial door cap on the
                // store-level path, so the staffing check's door-cap CEILING
                // leg can be shown to fail. Never a shipping mode.
                if (args[i] == "--mutant-revenue-ema-cap") { LaborAuction.MutantRevenueEmaCap = true; continue; }
                // MUTANT SWITCH (see AccessState.MutantPooledEntry): restores
                // the pooled phantom-entrant field as the store-level entry
                // signal — the flag comment's measured defect, verbatim.
                if (args[i] == "--mutant-pooled-entry") { AccessState.MutantPooledEntry = true; continue; }
                // MUTANT SWITCH (see LaborAuction.MutantServedCap): drives the
                // commercial door cap from served volume instead of presented
                // custom, so the staffing check's STARVATION leg can be shown
                // to fail.
                if (args[i] == "--mutant-served-cap") { LaborAuction.MutantServedCap = true; continue; }
                // MUTANT SWITCH (see LaborAuction.MutantUncappedUtil): drops the
                // technology ceiling from the commercial door cap, so it rises
                // without limit in the shop's traffic.
                if (args[i] == "--mutant-uncapped-util") { LaborAuction.MutantUncappedUtil = true; continue; }
                // MUTANT SWITCH (see AccessState.MutantIntentUnsaturated): the
                // intent probe stops comparing against what each household
                // already settled for, so the counted signal stops saturating.
                if (args[i] == "--mutant-intent-unsaturated") { AccessState.MutantIntentUnsaturated = true; continue; }
                // MUTANT SWITCH (see TestRunner.MutantSpareProbedCluster):
                // restores the collapse leg's exemption for the probed
                // cluster's own residents, so the leg's non-degeneracy floor
                // can be shown to fail. Never a shipping mode.
                if (args[i] == "--mutant-spare-probed-cluster") { TestRunner.MutantSpareProbedCluster = true; continue; }
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
                case "clearsweep":  // clearing-price check alone across seeds (see TestRunner.ClearSweep)
                {
                    int n = 32;
                    for (int i = 1; i + 1 < args.Length; i++)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    foreach (ulong s in new ulong[] { 1, 9, 13 })   // the verify-gate seeds ride along
                        if (!set.Contains(s)) set.Add(s);
                    return TestRunner.ClearSweep(set);
                }
                case "webersweep":  // Weber check alone across seeds (see TestRunner.WeberSweep)
                {
                    int n = 26;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    return TestRunner.WeberSweep(set);
                }
                case "goodssweep":  // both task #30 goods checks across seeds (see TestRunner.GoodsSweep)
                {
                    int n = 4;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)(n > 4 ? 4 : n); s++) set.Add(s);
                    foreach (ulong s in new ulong[] { 9, 13, 25 })   // the verify-gate seeds ride along
                        if (set.Count < n) set.Add(s);
                    for (ulong s = 4; set.Count < n; s++) if (!set.Contains(s)) set.Add(s);
                    return TestRunner.GoodsSweep(set);
                }
                case "shopsweep":   // both task #20 commerce checks across seeds (see TestRunner.ShopSweep)
                {
                    int n = 4;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    var set = new List<ulong>();
                    for (ulong s = 0; s < (ulong)n; s++) set.Add(s);
                    return TestRunner.ShopSweep(set);
                }
                case "shopprobe":
                {
                    // Task #20 measurement aid (see Debugging.ShopProbe): every
                    // bound this item ships is set from this command's output.
                    // --seed is the START of the sweep; --seeds its length.
                    int ticks = 300, n = 4;
                    ulong start = 0;
                    double svc = 0;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--seed") start = ulong.Parse(args[i + 1]);
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                        // Overrides CommercialServicePerSlot for THIS run only —
                        // how the pick is swept, and how the uncapped
                        // presented-per-filled-slot distribution the pick is set
                        // from is measured (set it high enough never to bind).
                        if (args[i] == "--service") svc = double.Parse(args[i + 1],
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return Debugging.ShopProbe(start, n, ticks, svc);
                }
                case "boomprobe":
                {
                    // Boom/bust scenario measurement aid (see Debugging.BoomProbe):
                    // the migration split per sub-window, with the region-side
                    // offer count printed next to it.
                    int win = 120, sub = 20;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--window") win = int.Parse(args[i + 1]);
                        if (args[i] == "--sub") sub = int.Parse(args[i + 1]);
                    }
                    return Debugging.BoomProbe(seed, win, sub);
                }
                case "leveltraj":
                {
                    // Levels-scenario measurement aid (see Debugging.LevelTrajectory):
                    // where the §6 rank correlation settles and how long it takes,
                    // on both arms. The scenario's horizon and bar are set from it.
                    int ticks = 6000, every = 200;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--every") every = int.Parse(args[i + 1]);
                    }
                    return Debugging.LevelTrajectory(seed, ticks, every);
                }
                case "occprobe":
                {
                    // Occupancy-check measurement aid (see Debugging.OccProbe):
                    // every bound the rewritten occupancy check ships is set
                    // from this command's output. --seed is the START of the
                    // sweep; --seeds its length.
                    int ticks = 120, n = 8;
                    ulong start = 0;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--seed") start = ulong.Parse(args[i + 1]);
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                    }
                    return Debugging.OccProbe(start, n, ticks);
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
                    double ownerAsk = 1.0;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        // The item-#41 A/B: 0 = asks disabled (every owner door
                        // folds; structure-only arm), 1 = shipped.
                        if (args[i] == "--owner-ask") ownerAsk = double.Parse(args[i + 1]);
                    }
                    return Debugging.AuctionProbe(seed, ticks, ownerAsk);
                }
                case "collapseprobe":
                {
                    // T3a instrument (see Debugging.CollapseProbe): the
                    // clearing-price check's population-collapse leg alone,
                    // decomposed. `--owner-ask 0` is the item-#41 arm;
                    // `--mutant-citywide-goods` the item-#30 one.
                    double ownerAsk = 1.0; bool spareC0 = true;
                    for (int i = 1; i + 1 < args.Length; i++)
                    {
                        if (args[i] == "--owner-ask") ownerAsk = double.Parse(args[i + 1],
                            System.Globalization.CultureInfo.InvariantCulture);
                        if (args[i] == "--spare-c0") spareC0 = args[i + 1] != "0";
                    }
                    return Debugging.CollapseProbe(seed, ownerAsk, spareC0);
                }
                case "laborprobe":
                {
                    // T3c instrument (see Debugging.LaborProbe): the outside
                    // share against door capacity, the border commute and the
                    // parameters that set the outside level. Defaults are the
                    // fingerprint's pinned labor fixture.
                    int cols = 8, rows = 8, hh = 1500, ticks = 120;
                    double mult = -1, cost = -1;
                    for (int i = 1; i + 1 < args.Length; i++)
                    {
                        if (args[i] == "--cols") cols = int.Parse(args[i + 1]);
                        if (args[i] == "--rows") rows = int.Parse(args[i + 1]);
                        if (args[i] == "--households") hh = int.Parse(args[i + 1]);
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--outside-mult") mult = double.Parse(args[i + 1],
                            System.Globalization.CultureInfo.InvariantCulture);
                        if (args[i] == "--commute-cost") cost = double.Parse(args[i + 1],
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return Debugging.LaborProbe(seed, cols, rows, hh, ticks, mult, cost);
                }
                case "weberprobe":
                {
                    // T3b instrument (see Debugging.WeberProbe): the distinct
                    // industrial output census and the entrant's own argmax at
                    // every cluster. --seed is the START of the sweep.
                    ulong start = 0; int n = 26, hh = 6000;
                    for (int i = 1; i + 1 < args.Length; i++)
                    {
                        if (args[i] == "--seed") start = ulong.Parse(args[i + 1]);
                        if (args[i] == "--seeds") n = int.Parse(args[i + 1]);
                        if (args[i] == "--households") hh = int.Parse(args[i + 1]);
                    }
                    return Debugging.WeberProbe(start, n, hh);
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
                case "goodsprobe":
                {
                    // Task #30 measurement aid (see Debugging.GoodsProbe):
                    // volume-EMA distribution for the shrinkage prior, per-lot
                    // bounds, end-state stat spreads. --seed is the START of a
                    // four-seed sweep (default 0..3, the reference set).
                    int ticks = 300;
                    ulong start = 0;
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (args[i] == "--ticks") ticks = int.Parse(args[i + 1]);
                        if (args[i] == "--seed") start = ulong.Parse(args[i + 1]);
                    }
                    return Debugging.GoodsProbe(start, ticks);
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
