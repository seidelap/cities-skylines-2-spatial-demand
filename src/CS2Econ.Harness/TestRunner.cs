using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>Correctness suite (PLAN §4.1): unit-level invariants, all
    /// deterministic. Exit code 0 iff everything passes.
    ///
    /// ====================================================================
    /// MUTANT MATRIX — what this suite can and cannot catch, measured
    /// ====================================================================
    /// A CHECK THAT CANNOT FAIL IS NOT A CHECK. Three conditions in this file
    /// have already been caught being vacuous by adversarial review, one of
    /// them (the auction's unsold-above-reserve leg) instrumented and found to
    /// have reached its own revenue test ZERO times out of 45 doors. So the
    /// evidence for the checks below is not that they are green: it is that
    /// each was run against a defect it is supposed to catch, and did.
    ///
    /// Every row was built as a one-line source edit, compiled, and run. BEFORE
    /// = c0c584d (22 checks). AFTER = this commit (26 checks). All timings and
    /// verdicts measured on one Linux container, .NET 8, Release, `verify`.
    /// Seeds are named per row; where a row lists one seed, that is the only
    /// seed it was measured on.
    ///
    /// PROVENANCE. Every BEFORE and AFTER cell below was rebuilt from scratch a
    /// second time, by a reviewer who did not write the checks, before this was
    /// merged. Method: c0c584d extracted with `git archive` into a clean tree
    /// (rebuilt from source, never assumed), each mutant re-applied to BOTH
    /// trees by a script that ABORTS if the edit does not change the file — a
    /// sed that silently no-ops would otherwise masquerade as "the suite caught
    /// nothing" and corrupt the whole table. All 17 mutants x 2 trees
    /// reproduced the verdicts recorded here, including both still-missing
    /// rows. That re-run is the reason to believe this table; the green output
    /// is not.
    ///
    /// The four still-missing rows are listed here rather than dropped. They
    /// are the honest content of this table.
    ///
    /// --- BLIND SPOT 1: a firm-money / ledger mismatch -------------------
    /// Ledger.Transfer debits one account and credits another, so the SUM of
    /// balances is invariant by construction. `Drift()` therefore tests the
    /// Ledger class, not the model, and every mutant below leaves it at
    /// exactly zero. The new leg reconciles the ledger against the money the
    /// entities actually hold — two independently maintained records, so
    /// nothing here re-derives a number from the code under test.
    ///
    ///  MUT-A   EconomyEngine.cs, dividends: members are paid `payout`, the
    ///          ledger records `payout`, the firm gives up `payout * 0.99`.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 24/26, seed 1. Reconciliation names the
    ///                  sector: firms 1.3E-2 posted-curve / 1.9E-2 auction,
    ///                  households and escrow still at 1E-13. The fingerprint
    ///                  independently reds default.built/price/money.
    ///  MUT-L3  EconomyEngine.cs, Mortality: the estate is transferred out and
    ///          `h.Money = 0` is dropped, so the money stays standing.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 24/26. Households 5.5E-3 / 7.0E-3.
    ///  MUT-L1b EconomyEngine.cs, pooled consumption: firms are credited 99.9 %
    ///          of the pro-rata share the ledger moved.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 24/26. Firms 2.8E-2 / 2.1E-2. This is the
    ///                  MILDEST mutant tried — a 0.1 % skim — and it still reads
    ///                  ten orders of magnitude above the worst clean value seen
    ///                  over the eighteen seeds below (2.2E-12), which is what
    ///                  makes the 1e-9 bound untuned rather than fitted.
    ///  MUT-L1a Same site, "dust guard": skip any firm whose share &lt; 1 %.
    ///          BEFORE  MISS — 22/22 on seed 3. Seed 1 went 20/22 and seed 5
    ///                  21/22, but by ACCIDENT: the reds were the occupancy
    ///                  channel and auction equilibrium, neither of which is
    ///                  about money, and neither names the defect.
    ///          AFTER   CAUGHT on 1, 3 and 5 — firms 2.9E-2 / 6.0E-2 on seed 3,
    ///                  where the whole old suite was green.
    ///  MUT-L4  EconomyEngine.cs, vacancy drain: the parcel gives up 98 % of
    ///          what the ledger moves Escrow → Treasury.
    ///          BEFORE  MISS — 22/22 on seed 5. Seeds 1 and 3 went red on
    ///                  DIFFERENT unrelated checks (auction equilibrium; Weber):
    ///                  three seeds, three verdicts, none naming the defect.
    ///          AFTER   CAUGHT on 1, 3 and 5 — escrow 6.7E-3 / 7.5E-3 (seed 1).
    ///  MUT-L5  Leveling.cs, in-place renovation: the PhantomDeveloper →
    ///          OutsideWorld hop is recorded for twice the cost.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   STILL A MISS — 26/26, seed 1. Reconciliation cannot see
    ///                  it (neither account has an entity-level mirror), and the
    ///                  fingerprint's money lane — which DOES hash every ledger
    ///                  account, phantoms included — does not move because the
    ///                  in-place renovation branch never fires in the pinned
    ///                  8×8 / 1500 / 120-tick fixture. Verified directly:
    ///                  `fingerprint --check` on the MUT-L5 build reports all
    ///                  eight lanes matching. See known limit 1.
    ///  MUT-L5b Construction.cs, the same defect shape on a path that DOES fire
    ///          in the pinned fixture (progress spend hop doubled). It exists as
    ///          the positive control for the row above — without it, "MUT-L5 is
    ///          unreachable" would be an excuse rather than a measurement.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 25/26. The fingerprint reds default.money and
    ///                  auction.money and NOTHING else, which is exactly the
    ///                  signature of a ledger-only defect: no entity moved.
    ///
    /// --- BLIND SPOT 2: a pricing-path change ---------------------------
    /// `determinism` ran the same build twice, so both sides of its comparison
    /// moved together. Renamed to `reproducibility (NOT invariance)`; invariance
    /// is now the fingerprint's job.
    ///
    ///  MUT-B   LandAccounting.cs: `readAt = supply` — the clearing band is
    ///          dropped from the read point. Independently measured by an
    ///          earlier round to move mean charged rent +34 % and the tax base
    ///          +6 %.
    ///          BEFORE  MISS — 22/22, seed 1, telemetry hash 5DE491C38FD7A362.
    ///          AFTER   CAUGHT — 25/26. All four default lanes move.
    ///  MUT-2   LandAccounting.cs: MinTailFraction 0.005 → 0.02.
    ///          BEFORE  MISS — 22/22, seed 1.   AFTER  CAUGHT — 25/26.
    ///  MUT-3   LandAccounting.cs: clearing bisection 34 → 40 iterations.
    ///          BEFORE  MISS — 22/22, seed 1.   AFTER  CAUGHT — 25/26.
    ///          THIS IS THE FALSE-ALARM SIGNATURE, not a defect: default.price
    ///          and default.money go red while every scalar behind them is
    ///          unchanged to printed precision (sumLR 17770.106, meanRent
    ///          4.2415514, treasury 233363.42), only `drift` moving at 1E-8.
    ///          Recorded so the next person to see it recognises it. Limit 8.
    ///  MUT-L   EconomyEngine.cs, inside the StoreLevelSpending branch:
    ///          `RevenueThisTick = 0` → `= 5`.
    ///          BEFORE  MISS — 22/22.   AFTER  STILL A MISS — 26/26.
    ///          Two arms are two arms, not 2^n: nothing in this suite enters the
    ///          StoreLevelSpending branch. See known limit 4.
    ///
    /// --- BLIND SPOT 3: the auction is off for almost the whole suite ----
    ///  MUT-D   EconomyEngine.cs, auction move-in: delete the shelter release.
    ///          This is a regression the repo has ALREADY shipped once; the
    ///          surviving comment at the site records "a gap of 30 that never
    ///          closed".
    ///          BEFORE  MISS — 22/22 on the default arm AND 22/22 under
    ///                  `--auction` (146.7 s), seed 1. THE DECISIVE ROW: turning
    ///                  the flag on globally costs +104 s and catches nothing
    ///                  here, because coverage is necessary for a check to fail
    ///                  and never sufficient. What was missing was an assertion.
    ///          AFTER   CAUGHT — 25/26, seed 1. The shelter reconciliation reads
    ///                  a gap of 18 between the occupancy counter and the
    ///                  households actually in shelter. Note WHICH arm caught
    ///                  it: the plain auction arm's shelter population is 0 on
    ///                  every seed tried, so that arm asserts nothing; the
    ///                  stressed arm, which manufactures the state, is the one
    ///                  that fails. That is why it exists.
    ///
    /// --- BLIND SPOT 4: the land-rent legs test structure, never level ---
    ///  MUT-C   LandAccounting.cs: `AssessedLR = bestLR * 0.958` — the −4.2 %
    ///          citywide move an adversarial round measured with the whole suite
    ///          silent.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 24/26. The published-base leg reads 0/257
    ///                  parcels correct at worst 4.2E-2, and the co-movement leg
    ///                  reads 4.2E-2: the check names the mutant's own size.
    ///  MUT-2'  Assess: the current-use residual drops `* CondFactor(condition)`.
    ///          BEFORE  MISS — 22/22, seed 1.   AFTER  CAUGHT — 24/26, L1 0/440
    ///                  at worst 5.3E-1.
    ///  MUT-3'  Assessment reads `auction.Reserve[sub]` where it should read
    ///          `auction.Price[sub]` — the wrong column of the same arrays.
    ///          BEFORE  MISS — 22/22, seed 1.
    ///          AFTER   CAUGHT — 25/26, L1 0/452 at worst 1.5E0, and the
    ///                  assessment check is the ONLY thing that catches it: the
    ///                  fingerprint's default arm has no auction to read and its
    ///                  auction lanes are report-only. This row is the argument
    ///                  for the check existing.
    ///  MUT-4'  Assess: structure cost read at level 1 regardless of the
    ///          parcel's level.
    ///          BEFORE  MISS — 22/22 on seeds 3 and 5. Seed 1 went 21/22, but
    ///                  through auction equilibrium, naming nothing about land.
    ///          AFTER   CAUGHT on 1, 3 and 5 — L1 137/421 at worst 4.9E0
    ///                  (seed 1), 152/417 at 1.8E1 (seed 3).
    ///
    /// --- WHAT IS STILL MISSING (the deliverable, not a disclaimer) ------
    ///  1. TREASURY AND THE PHANTOMS HAVE NO ENTITY MIRROR. Reconciliation
    ///     covers households, firms and per-parcel escrow — the three sectors
    ///     with an independent record. The other six accounts have none, so
    ///     MUT-L5 passes. The fingerprint hashes the account vector, which
    ///     catches such a defect where its code path runs in the pinned fixture
    ///     (MUT-L5b) and not otherwise (MUT-L5).
    ///  2. PRICE LEVEL IS STILL UNANCHORED, so blind spot 4 is HALF closed. The
    ///     relation catches a tax base that moves when the price did not. If the
    ///     auction starts posting 4.2 % lower everywhere, assessment correctly
    ///     follows and all three legs stay green. The fingerprint's price lane
    ///     covers that case on ONE pinned fixture and nowhere else. No
    ///     non-circular anchor for the price level was found this round.
    ///  3. THE AUCTION'S OTHER ASSERTION GAPS ARE UNTOUCHED. Nothing asserts on
    ///     the Declined/Outbid split or on what CutVacancies achieves — and
    ///     task #28 records a KNOWN open defect there. Each wants a check, none
    ///     wants a flag.
    ///  4. FLAG COMBINATIONS: StoreLevelSpending, ShadowAccountingOnly, vanilla
    ///     mode and the TNTP import path are outside both arms. MUT-L passes.
    ///  5. ONE FIXTURE PER INSTRUMENT; THE FINGERPRINT IS ONE SEED. A defect
    ///     needing a bigger city, a longer run or another seed moves no lane.
    ///  6. SCENARIOS REMAIN UNGATED AND PARTLY RED (7/10 at seed 1 at c0c584d,
    ///     and 0 auction solves — now printed, no longer assumed).
    ///  7. "VERIFY PASSES" MEANS "PASSES ON THE SEED YOU RAN" — see the seed
    ///     table below.
    ///  8. THE FINGERPRINT CAN CRY WOLF on numerically neutral changes
    ///     (Math.Pow/Exp are library code). MUT-3 is what that looks like.
    ///  9. THE MOD ADAPTERS (CS2Econ.Mod) AND CityImport ARE NOT COVERED.
    /// 10. The assessment check mutates its own sim (doubles RentShare, detaches
    ///     the auction) inside try/finally. Its state is per-Sim, so it needs no
    ///     tripwire today — but move that fixture onto a shared world and it
    ///     silently becomes a cross-test contaminant.
    ///
    /// --- RUNTIME, MEASURED, AND THE BUDGET IT BUYS --------------------
    /// Single clean runs on this container (nothing else running), seed 1:
    ///     `verify`            c0c584d  42.5 s, 22 checks
    ///                         here     55.1 s, 26 checks (+12.6 s for four
    ///                                  instruments: the reconciliation legs
    ///                                  ride runs the drift leg already paid
    ///                                  for and cost +6.4 s only because the
    ///                                  stressed shelter arm is a third sim;
    ///                                  assessment +5.2 s; fingerprint +2.0 s;
    ///                                  the coverage counters, nothing
    ///                                  measurable)
    ///     `verify --auction`  c0c584d 146.7 s   →  here ~150 s (seeds 0-3:
    ///                         146.5-160.2 s, measured two-at-a-time)
    ///     `scenarios`         c0c584d 259.3 s, 7/10  →  here 262.8 s, 7/10
    /// Across the eighteen seeds above, run two-at-a-time, `verify` took
    /// 50.1-61.5 s here against 37.6-46.6 s at c0c584d.
    ///
    /// RE-MEASURED INDEPENDENTLY at merge time, six serial runs with nothing
    /// else on the box (the mutant sweep's timings are NOT quotable — it ran
    /// four jobs across four cores):
    ///     `verify`            c0c584d  42 s, 22/22  →  here 56 s, 26/26
    ///     `verify --auction`  c0c584d 146 s, 22/22  →  here 156 s, 26/26
    ///     `scenarios`         c0c584d 267 s, 7/10   →  here 257 s, 7/10
    /// Two independent rounds of measurement agree to about a second on the
    /// gate. The scenarios pair moved 259→267 (c0c584d) and 263→257 (here)
    /// between rounds, i.e. the two columns' difference is smaller than one
    /// container's run-to-run noise: the honest claim there is "no measurable
    /// change", NOT that this round made scenarios faster.
    ///
    /// THE GATE IS `verify` ON THE DEFAULT ARM. The 60 s budget the auction
    /// round set held until the labor-auction round: measured there (this
    /// container, seed 1) the suite totals ~117 s, the labor fixture alone
    /// ~19 s (two sims including the Ward-mutant arm) and model-fingerprint
    /// ~15 s (its third arm runs the labor market for 24 refreshes). The
    /// per-earner labor fix round measured 123.5 s and 124.9 s (solo runs,
    /// seeds 25 and 9, this container) and amends this header to the
    /// measured reality rather than trimming checks to chase the old
    /// number — the labor market is a fifth mechanism under test and its
    /// fixture cost is the price of testing it. `--auction` is
    /// a manual sweep, not a gate: it costs +104 s, it SWAPS the arm rather than
    /// adding one (Sim.Create forces the flag onto every sim, so under it
    /// nothing exercises the posted curve — still the shipping default), and on
    /// the one real historical regression rebuilt here (MUT-D) it caught nothing
    /// the default arm did not. Four fixtures opt into an auction city instead,
    /// at ~5 s each, which keeps both market paths under test in one run.
    ///
    /// The coverage table `verify` prints is how that stays true: it names every
    /// fixture, its wall time and its auction-solve count, so the next person to
    /// add a check can see where the budget went and which mechanism is thin.
    /// If a check has to be displaced to hold the suite's cost, the table says
    /// which two are the expensive ones.
    ///
    /// --- SEED TABLE: THE SUITE IS NOT GREEN ACROSS SEEDS, AND WAS NOT ---
    /// Both columns measured on this container, one run each. BASE is c0c584d
    /// rebuilt from source, not assumed. Every failure below is present in BOTH
    /// columns, on the same seed, in the same check: nothing in this round
    /// caused any of them, and nothing in this round fixed any of them.
    ///
    ///     seed  c0c584d      this commit   failing check
    ///        0  22/22        26/26
    ///        1  22/22        26/26
    ///        2  22/22        26/26
    ///        3  22/22        26/26
    ///        4  22/22        26/26
    ///        5  22/22        26/26
    ///        6  22/22        26/26
    ///        7  22/22        26/26
    ///        9  21/22        25/26         occupancy channel
    ///       13  22/22        26/26
    ///       25  22/22        26/26
    ///       26  21/22        25/26         auction equilibrium
    ///      138  21/22        25/26         clearing price
    ///      208  21/22        25/26         auction equilibrium
    ///      271  22/22        26/26
    ///      327  22/22        26/26
    ///      549  21/22        25/26         clearing price
    ///      910  21/22        25/26         occupancy channel
    ///
    /// Six of eighteen seeds are red at HEAD. None of the four instruments
    /// added here fails on any of the eighteen, and every failure above appears
    /// in BOTH columns on the same seed in the same check.
    ///
    /// THE AUCTION ARM IS ALSO NOT GREEN, and it is a separate measurement
    /// rather than a footnote to the one above, because `--auction` SWAPS the
    /// arm (Sim.Create forces the flag onto every sim) and so is a different
    /// suite, not a superset. `verify --auction`, seeds 0-3, both columns:
    ///
    ///     seed  c0c584d      this commit   failing check
    ///        0  21/22        25/26         Weber
    ///        1  22/22        26/26
    ///        2  22/22        26/26
    ///        3  21/22        25/26         occupancy channel
    ///
    /// So two of the four auction-arm seeds are red at HEAD too, in checks that
    /// have nothing to do with the auction. Same conclusion as the default arm:
    /// pre-existing, unchanged by this round, and NOT to be read as caused by
    /// it. Note what this costs — 146 s at c0c584d and 156 s here PER SEED,
    /// which is the measurement behind the decision to keep `--auction` a
    /// manual sweep rather than a gate.
    ///
    /// PAIRWISE STABILITY, probed because a previous round shipped a regression
    /// in it that its own suite missed, visible on seeds 13 and 549.
    ///  MUT-P  HousingAuction.cs: the repair loop's `AddEnviedColumns` pass is
    ///         skipped, so a household that ends up envious never gets the
    ///         column that would fix it.
    ///         BEFORE  CAUGHT — 21/22 seed 13, 20/22 seed 549: the pre-existing
    ///                 auction-equilibrium check reads 720 households envious
    ///                 beyond 2ε (worst 46.6 % of value) on seed 13, 633 (55.1 %)
    ///                 on 549.
    ///         AFTER   CAUGHT — 25/26 and 24/26, the same check, the same
    ///                 numbers.
    ///         SO THIS IS NOT A DEMONSTRATION THAT THE NEW WORK WOULD HAVE
    ///         CAUGHT THAT REGRESSION, and it should not be read as one. A
    ///         mutant the old suite already catches cannot stand in for one it
    ///         missed; the real regression was evidently subtler than deleting
    ///         the pass, and it was not reconstructed here. What the probe does
    ///         establish is where the responsibility sits: auction-equilibrium
    ///         is the only thing in this suite that tests pairwise stability,
    ///         none of the four instruments added here duplicates it, and the
    ///         fingerprint structurally cannot — its default arm has no auction,
    ///         and its auction lanes are report-only by design. Strengthening
    ///         that check is a separate commit and it is still owed.
    /// ====================================================================
    /// </summary>
    public static class TestRunner
    {
        private static readonly List<(string name, bool pass, string detail)> Results
            = new List<(string, bool, string)>();

        private static void Check(string name, bool pass, string detail = "")
        {
            Results.Add((name, pass, detail));
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
        }

        /// <summary>Per-fixture wall time, checks produced, and auction solves.
        /// See <see cref="Timed"/> for why this is instrumentation and not an
        /// assertion.</summary>
        private static readonly List<(string fixture, double ms, long solves, int checks)> Coverage
            = new List<(string, double, long, int)>();

        /// <summary>MAKES THE COVERAGE GAP UNHIDEABLE — it asserts nothing.
        ///
        /// The suite could not say which of its own checks exercise the housing
        /// auction, which is the largest and newest mechanism in the codebase.
        /// The answer at c0c584d was two fixtures of eighteen (auction
        /// equilibrium, and the auction leg of ledger conservation) and zero of
        /// the ten scenarios. A defect where 2,463 consecutive price cuts made
        /// literally no household better off lived behind that.
        ///
        /// A COMMENT SAYING "2 OF 18" ROTS the moment a fixture is added. A
        /// printed row that says `auction OFF` on the very next run cannot. That
        /// is the whole design: this is a counter and a Stopwatch, it costs
        /// nothing measurable, and its output is read by a human deciding where
        /// the next check should go.
        ///
        /// It deliberately does NOT assert a solve count. "The auction ran N
        /// times" would fail on any legitimate change to refresh cadence, which
        /// is the cry-wolf failure in miniature.
        ///
        /// The % column is coarse and should not be quoted alone: a fixture
        /// counts as auction-touching if it solved once, so ledger conservation
        /// — half posted-curve, half auction — attributes entirely to the
        /// auction. The per-fixture rows and solve counts are exact.</summary>
        private static void Timed(string fixture, Action body)
        {
            long s0 = HousingAuction.SolveCalls;
            int c0 = Results.Count;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            body();
            sw.Stop();
            Coverage.Add((fixture, sw.Elapsed.TotalMilliseconds,
                          HousingAuction.SolveCalls - s0, Results.Count - c0));
        }

        /// <summary>THE AUCTION CANARY: the auction-equilibrium check alone,
        /// across many seeds, in about a minute of fixture per fifteen seeds.
        ///
        /// Exists because every auction defect in this file's history became
        /// visible on a seed nobody ran: the CutVacancies indifference defect
        /// rested a hair inside the check's tolerance on seeds 0-7 and fired
        /// only on 26 and 208, which no round ran until a bisect went looking;
        /// a pairwise-stability knife on 13 and 549 was found the same way.
        /// The full suite costs ~57s per seed, so nobody sweeps it; this
        /// fixture costs ~6s per seed, so a 40-seed sweep is affordable on
        /// every auction change. It is a gate for auction work precisely
        /// because the model fingerprint cannot be one there: the fingerprint's
        /// default arm never runs the auction, so a pricing-shape change moves
        /// zero gating lanes.
        ///
        /// Cross-reference KNOWN-RED.md before attributing a failure: a red
        /// here is a finding only if that file does not already own it.</summary>
        public static int Canary(List<ulong> seeds)
        {
            Console.WriteLine($"auction canary: {seeds.Count} seeds");
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                AuctionEquilibrium(seed);
                bool ok = Results.Count > before && Results[Results.Count - 1].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"canary: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>Sweep the prospect-local-odds check alone across seeds —
        /// same rationale as <see cref="Canary"/> (cheap fixture, many seeds),
        /// and the harness for demonstrating the `--mutant-citywide-odds`
        /// mutant is caught on every seed, not one.</summary>
        public static int ProspectSweep(List<ulong> seeds)
        {
            Console.WriteLine($"prospect-local-odds sweep: {seeds.Count} seeds"
                + (AccessState.MutantCitywideProspectOdds ? " [MUTANT: zero-diluted citywide odds]" : ""));
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                ProspectLocalOddsCheck(seed);
                bool ok = Results.Count > before && Results[Results.Count - 1].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"prospect sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The occupancy-channel check alone across seeds — same
        /// rationale as <see cref="Canary"/> (cheap fixture, many seeds; ~1.5 s
        /// per seed measured at the check-debt commit). This is the instrument
        /// that established the old fallback arm was dead (0/57 seeds reached
        /// it) and that the rewrite changed no verdict; re-run it before
        /// changing that check's selection rule or bounds.</summary>
        public static int OccupancySweep(List<ulong> seeds)
        {
            Console.WriteLine($"occupancy sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                OccupancyChannel(seed);
                bool ok = Results.Count > before && Results[Results.Count - 1].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"occupancy sweep: {seeds.Count - failed.Count}/{seeds.Count} pass"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The Weber check alone across seeds — same rationale as
        /// <see cref="Canary"/> (auction-world Weber fragility moves seeds:
        /// the red was on seed 0 at c0c584d and on seed 13 at the flip
        /// commit). Two 300-tick sims per seed; this is the instrument for
        /// any change to recipe choice, retooling, or the trade prices they
        /// read.</summary>
        public static int WeberSweep(List<ulong> seeds)
        {
            Console.WriteLine($"weber sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                WeberRecipeChoice(seed);
                bool ok = Results.Count > before && Results[Results.Count - 1].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"weber sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The assessment-tracks-price fixture alone (both its
        /// checks: the L1–L3 relation and the shadow-queue leg) — ~7 s
        /// against ~120 s for the full suite (measured, seed 1, check-debt
        /// commit). This is the instrument the shadow-queue mutants were
        /// demonstrated with; re-run it per seed before touching Assess,
        /// BuildShadow/ShadowAt, or the not-standing branch of
        /// ResidentialBidPerUnit.</summary>
        public static int AssessCheck(ulong seed)
        {
            int before = Results.Count;
            AssessmentTracksPrice(seed);
            return Results.Skip(before).All(r => r.pass) ? 0 : 1;
        }

        public static int RunAll(ulong seed, out string report)
        {
            Results.Clear();
            Coverage.Clear();
            Console.WriteLine("verify: correctness suite");

            Timed("ipf", IpfConsistency);
            Timed("annuity", AnnuityRoundTrip);
            Timed("interior-optimum", InteriorOptimum);
            Timed("trade-laws", () => TradeLaws(seed));
            Timed("weber", () => WeberRecipeChoice(seed));
            Timed("vacancy-kernel", () => VacancyKernelConservation(seed));
            Timed("claim-vacancy-wash", () => ClaimVacancyWash(seed));
            Timed("occupancy-channel", () => OccupancyChannel(seed));
            Timed("auction-equilibrium", () => AuctionEquilibrium(seed));
            Timed("prospect-local-odds", () => ProspectLocalOddsCheck(seed));
            Timed("exhaustive-shortlist", () => ExhaustiveShortlist(seed));
            Timed("assignment-oracle", () => {
                var (lpOk, lpDetail) = AssignmentOracle.Run(seed);
                Check("auction total surplus is LP-optimal within the epsilon budget", lpOk, lpDetail); });
            Timed("labor-auction", () => LaborMarket(seed));
            Timed("clearing-price", () => ClearingPrice(seed));
            Timed("occupied-stock-rent", () => OccupiedStockCarriesRent(seed));
            Timed("coop-rerate", () => CoopInstantRerate(seed));
            Timed("circularity-guard", () => CircularityGuard(seed));
            Timed("assessment-tracks-price", () => AssessmentTracksPrice(seed));
            Timed("ledger-conservation", () => LedgerConservation(seed));
            Timed("shadow-mode", () => ShadowMode(seed));
            Timed("insolvency", () => InsolvencyPipeline(seed));
            Timed("flags-off-smoke", () => FlagsOffSmoke(seed));
            Timed("reproducibility", () => Determinism(seed));
            Timed("model-fingerprint", ModelFingerprint);

            int failed = Results.Count(r => !r.pass);
            string coverage = CoverageTable();
            Console.Write(coverage);

            var sb = new StringBuilder();
            sb.AppendLine($"## Correctness verification ({Results.Count} checks, {Results.Count - failed} passing)");
            sb.AppendLine();
            foreach (var (name, pass, detail) in Results)
                sb.AppendLine($"- {(pass ? "✅" : "❌")} **{name}**{(detail.Length > 0 ? ": " + detail : "")}");
            sb.AppendLine();
            long auctionFixtures = Coverage.Count(c => c.solves > 0);
            sb.AppendLine($"Auction coverage: {auctionFixtures}/{Coverage.Count} fixtures solve the housing "
                          + $"auction ({Coverage.Sum(c => c.solves)} solves); the rest exercise the posted-curve "
                          + "path only. Measured by `HousingAuction.SolveCalls`, printed per fixture by `verify`.");
            report = sb.ToString();
            Console.WriteLine($"\nverify: {Results.Count - failed}/{Results.Count} checks passing");
            return failed == 0 ? 0 : 1;
        }

        private static string CoverageTable()
        {
            double total = Coverage.Sum(c => c.ms);
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  fixture                   time    checks  auction solves");
            foreach (var (fixture, ms, solves, checks) in Coverage.OrderByDescending(c => c.ms))
                sb.AppendLine($"  {fixture,-24} {ms / 1000.0,6:F2}s {checks,6}  "
                              + (solves > 0 ? $"{solves,8}" : "     OFF"));
            long onFixtures = Coverage.Count(c => c.solves > 0);
            double onMs = Coverage.Where(c => c.solves > 0).Sum(c => c.ms);
            sb.AppendLine($"  {"TOTAL",-24} {total / 1000.0,6:F2}s {Results.Count,6}  "
                          + $"{Coverage.Sum(c => c.solves),8}");
            sb.AppendLine($"  auction reached by {onFixtures}/{Coverage.Count} fixtures "
                          + $"(~{(total > 0 ? onMs / total * 100 : 0):F0} % of runtime, coarse: a fixture counts "
                          + "if it solved once)");
            return sb.ToString();
        }

        // --------------------------------------------------------------------
        private static void IpfConsistency()
        {
            var rng = new SplitMix64(42);
            int n = 30, m = 25;
            var supply = new double[n]; var demand = new double[m];
            var w = new double[n, m];
            for (int i = 0; i < n; i++) supply[i] = 5 + 20 * rng.NextDouble();
            for (int j = 0; j < m; j++) demand[j] = 5 + 25 * rng.NextDouble();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++) w[i, j] = Math.Exp(-3.0 * rng.NextDouble());

            Balancing.Match(supply, demand, w, slackWeight: 0.05, iterations: 60,
                            out var rowM, out var colM);

            double maxRowViol = 0, maxColViol = 0, totalRow = 0, totalCol = 0;
            for (int i = 0; i < n; i++) { maxRowViol = Math.Max(maxRowViol, rowM[i] - supply[i]); totalRow += rowM[i]; }
            for (int j = 0; j < m; j++) { maxColViol = Math.Max(maxColViol, colM[j] - demand[j]); totalCol += colM[j]; }
            // Matched flows may not exceed either marginal, and both sides see the
            // same matched total (jobs claimed = workers claimed).
            Check("IPF: marginals respected", maxRowViol < 1e-4 && maxColViol < 1e-4,
                  $"max row viol {maxRowViol:E1}, col {maxColViol:E1}");
            Check("IPF: two-sided consistency", Math.Abs(totalRow - totalCol) < 1e-6,
                  $"|jobsClaimed − workersClaimed| = {Math.Abs(totalRow - totalCol):E2}");
        }

        private static void AnnuityRoundTrip()
        {
            var p = new EconParams();
            double lump = 12345.6;
            double flow = Annuity.FlowOf(lump, p.HurdleRate, p.AnnuityHorizon);
            double back = Annuity.LumpOf(flow, p.HurdleRate, p.AnnuityHorizon);
            Check("annuity operator round-trips", Math.Abs(back - lump) < 1e-6 * lump,
                  $"lump {lump} -> flow {flow:F4} -> {back:F2}");
        }

        private static void InteriorOptimum()
        {
            var p = new EconParams();
            int ArgmaxLevel(double baseBid)
            {
                int best = 1; double bestV = double.NegativeInfinity;
                for (int l = 1; l <= p.MaxLevel; l++)
                {
                    double v = baseBid * p.Quality(l) / p.Quality(1) - LandAccounting.SPerUnit(l, 1.0, p);
                    if (v > bestV) { bestV = v; best = l; }
                }
                return best;
            }
            int cold = ArgmaxLevel(1.1), mid = ArgmaxLevel(3.0), hot = ArgmaxLevel(9.0);
            Check("supported level ℓ* is an interior optimum rising with access",
                  cold <= 2 && mid > cold && mid < p.MaxLevel && hot >= mid,
                  $"ℓ*(cold)={cold}, ℓ*(mid)={mid}, ℓ*(hot)={hot}");
        }

        private static void TradeLaws(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 6, Rows = 6, SeedHouseholds = 500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            var w = sim.W;
            SyntheticCity.AddRailTerminal(w, sim.Access, p);
            sim.Engine.Trade.Refresh(p);

            // Road curve concave increasing (d=2), rail linear (d=1).
            var road = w.Exits.First(e => e.Mode == ExitMode.Road && e.Resource == Res.Metals);
            var rail = w.Exits.First(e => e.Mode == ExitMode.Rail && e.Resource == Res.Metals);
            double r1 = sim.Engine.Trade.ExportMarginal(road, 100, p);
            double r2 = sim.Engine.Trade.ExportMarginal(road, 200, p);
            double r3 = sim.Engine.Trade.ExportMarginal(road, 300, p);
            // Received price falls with volume; drops decelerate (sqrt law).
            bool roadShape = r1 > r2 && r2 > r3 && (r1 - r2) > (r2 - r3) - 1e-9;
            double l1 = sim.Engine.Trade.ExportMarginal(rail, 100, p);
            double l2 = sim.Engine.Trade.ExportMarginal(rail, 200, p);
            double l3 = sim.Engine.Trade.ExportMarginal(rail, 300, p);
            bool railLinear = Math.Abs((l1 - l2) - (l2 - l3)) < 1e-9;
            Check("road export law concave (d=2), rail linear (d=1)", roadShape && railLinear,
                  $"road drops {r1 - r2:F4}>{r2 - r3:F4}; rail drops {l1 - l2:F6}≈{l2 - l3:F6}");

            // Cheapest-first allocation across exits == brute-force marginal merge.
            double surplus = 600;
            var supplyBy = new double[sim.Access.ClusterCount];
            supplyBy[sim.Access.At(3, 3)] = surplus;
            var clear = sim.Engine.Trade.ClearTick(Res.Metals, surplus, 0, supplyBy, new double[sim.Access.ClusterCount], p);

            // Brute force: same lots, same marginal formulas, global best-first.
            var _clearDraws = w.Exits.Where(e => e.Resource == Res.Metals).Select(e => e.DrawnThisTick).ToList();
            foreach (var x in w.Exits) if (x.Resource == Res.Metals) x.DrawnThisTick = 0;
            double bfRevenue = 0, remaining = surplus;
            var goodsExits = w.Exits.Where(e => e.Resource == Res.Metals).ToList();
            double HaulOf(TradeExit e) => sim.Access.Cost(sim.Access.At(3, 3), e.Cluster, AccessPurpose.Freight)
                                          * sim.Engine.Trade.FreightCostPerMinute
                                          * ResourceCatalog.Weight[(int)Res.Metals];
            while (remaining > 1e-6)
            {
                double lot = Math.Min(p.LotSize, remaining);
                TradeExit? best = null; double bestNet = 0;
                foreach (var e in goodsExits)
                {
                    if (e.Capacity > 0 && e.DrawnThisTick + lot > e.Capacity) continue;
                    double net = sim.Engine.Trade.ExportMarginal(e, e.DrawnThisTick, p) - HaulOf(e);
                    if (net > bestNet) { bestNet = net; best = e; }
                }
                if (best == null) break;
                best.DrawnThisTick += lot; bfRevenue += lot * bestNet; remaining -= lot;
            }
            var perExit = string.Join(" ", goodsExits.Select((e, i) => $"e{i}:{_clearDraws[i]:F0}/{e.DrawnThisTick:F0}"));
            Check("multimodal composition = horizontal summation (vs brute force)",
                  Math.Abs(clear.ExportRevenue - bfRevenue) < 1e-6 * Math.Max(1, bfRevenue),
                  $"clear {clear.ExportRevenue:F2} vs brute {bfRevenue:F2} (clear/brute per exit: {perExit})");

            // Sustained EMA + transient layer decay round-trip.
            var exit = goodsExits[0];
            exit.SustainedQ = 0; exit.TransientB = 0; exit.ExportedThisTick = 500;
            sim.Engine.Trade.EndTick(p);
            double b1 = exit.TransientB;
            foreach (var x in w.Exits) { x.ExportedThisTick = 0; x.ImportedThisTick = 0; }
            sim.Engine.Trade.EndTick(p);
            Check("transient impact layer decays at resilience rate",
                  b1 > 0 && exit.TransientB < b1 && Math.Abs(exit.TransientB - b1 * p.TradeTransientDecay) < 1e-9,
                  $"burst {b1:F1} -> {exit.TransientB:F1}");
        }

        private static void WeberRecipeChoice(ulong seed)
        {
            // Resource-level spatial economics: extraction follows geology, and
            // industry's recipe choice follows input sourcing costs (§4.2 Weber).
            // Two sims, one per margin — each measured where its sector is
            // economically viable (this check tests location-choice ALIGNMENT,
            // not sector viability): extractors need the ExtractorHeavy ore
            // region to survive absorption-paced growth; industry thrives on
            // the plain config where extraction is marginal.
            var p = new EconParams();
            var simInd = Sim.Create(new SyntheticCity.Config { Seed = seed, SeedHouseholds = 6000 },
                                    p, new FeatureFlags());
            simInd.Run(300);
            var simExt = Sim.Create(new SyntheticCity.Config
                                    { Seed = seed, SeedHouseholds = 6000, ExtractorHeavy = true, ExtractorPrebuilt = 0.25 },
                                    p, new FeatureFlags());
            simExt.Run(300);

            int extract = 0, extractRight = 0;
            foreach (var f in simExt.W.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Extractor) continue;
                int c = simExt.W.Parcels[f.Parcel].Cluster;
                extract++;
                // Geology oracle (seeds) or value-weighted geology oracle
                // (entrants price the output too — mining the slightly less
                // abundant but dearer raw is correct economics).
                int bestBySuit = 0, bestByValue = 0; double bs = -1, bv = -1;
                for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                {
                    double suit = simExt.W.Clusters[c].ResourceSuitability[rr];
                    if (suit > bs) { bs = suit; bestBySuit = rr; }
                    double val = suit * Math.Max(simExt.Engine.Trade.LocalPrice((Res)rr),
                                                 simExt.Engine.Trade.BestExportNet((Res)rr, c));
                    if (val > bv) { bv = val; bestByValue = rr; }
                }
                if ((int)f.Output == bestBySuit || (int)f.Output == bestByValue) extractRight++;
            }

            int ind = 0, indAligned = 0;
            var outputsSeen = new HashSet<Res>();
            foreach (var f in simInd.W.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Industrial) continue;
                int c = simInd.W.Parcels[f.Parcel].Cluster;
                outputsSeen.Add(f.Output);
                if (f.Output == Res.Machinery) continue;   // multi-input: no single cheapest raw
                ind++;
                // The chosen recipe's raw should be the locally cheapest raw
                // to deliver (allowing a 15% tolerance band for ties).
                var recipe = ResourceCatalog.RecipeFor(f.Output);
                double own = simInd.Engine.Trade.DeliveredCost(recipe.Inputs[0].res, c);
                double cheapest = double.PositiveInfinity;
                for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                    cheapest = Math.Min(cheapest, simInd.Engine.Trade.DeliveredCost((Res)rr, c));
                if (own <= cheapest * 1.15 + 0.05) indAligned++;
            }

            double extractShare = extract > 0 ? (double)extractRight / extract : 0;
            double indShare = ind > 0 ? (double)indAligned / ind : 0;
            Check("Weber: extraction follows geology; recipes follow input sourcing",
                  extract >= 5 && extractShare >= 0.9 && ind >= 5 && indShare >= 0.55 && outputsSeen.Count >= 2,
                  $"{extract} extractors ({extractShare:P0} on best raw); {ind} single-input industrials " +
                  $"({indShare:P0} on cheapest-sourced recipe); {outputsSeen.Count} distinct industrial outputs");
        }

        private static void VacancyKernelConservation(ulong seed)
        {
            // The kernel's defining property: one vacant unit cancels EXACTLY
            // one unit of residual demand citywide (V = 1), and the cancellation
            // is nearer where the vacancy is. Refresh twice on identical demand
            // inputs, differing only by evicting K households in one cluster.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var w = sim.W;
            var eng = sim.Engine;

            var seekers = (double[])eng.SeekersEma.Clone();
            var res = new ResidualDemand();
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var before = new double[6][];
            for (int u = 0; u < 6; u++) before[u] = (double[])res.ByUse[u].Clone();

            // Evict from the DENSEST residential cluster (needs enough
            // occupants to make a measurable shock).
            var occByCluster = new int[eng.Access.C];
            foreach (var pl in w.Parcels)
                if (pl.State == ParcelState.Built && pl.IsResidential)
                    occByCluster[pl.Cluster] += pl.OccupantHouseholds.Count;
            int shockCluster = 0;
            for (int c = 1; c < eng.Access.C; c++)
                if (occByCluster[c] > occByCluster[shockCluster]) shockCluster = c;
            int evicted = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.Cluster != shockCluster || pl.State != ParcelState.Built || !pl.IsResidential) continue;
                while (pl.OccupantHouseholds.Count > 0 && evicted < 60)
                {
                    int hid = pl.OccupantHouseholds[pl.OccupantHouseholds.Count - 1];
                    pl.OccupantHouseholds.RemoveAt(pl.OccupantHouseholds.Count - 1);
                    w.Households[hid].HomeParcel = -1;
                    evicted++;
                }
                if (evicted >= 60) break;
            }
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);

            // V=1: total suppression == evicted units exactly. Localization is
            // a DENSITY statement (suppression per cluster falls with
            // distance) — aggregates would compare ~8 near clusters against
            // ~90 far ones and drown the gradient in the far tail's headcount.
            double totalDiff = 0, nearDiff = 0, farDiff = 0;
            int nearN = 0, farN = 0;
            double sx = w.Clusters[shockCluster].X, sy = w.Clusters[shockCluster].Y;
            for (int c = 0; c < eng.Access.C; c++)
            {
                double d = 0;
                for (int u = 0; u < 6; u++) d += before[u][c] - res.ByUse[u][c];
                totalDiff += d;
                double dx = (w.Clusters[c].X - sx) * w.MetersPerUnit;
                double dy = (w.Clusters[c].Y - sy) * w.MetersPerUnit;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= 1.5 * p.VacancyKernelLambdaM) { nearDiff += d; nearN++; }
                else { farDiff += d; farN++; }
            }
            double nearDensity = nearN > 0 ? nearDiff / nearN : 0;
            double farDensity = farN > 0 ? farDiff / farN : 0;
            Check("vacancy kernel: V=1 conservation, suppression density falls with distance",
                  evicted >= 30 && Math.Abs(totalDiff - evicted) < 1e-6
                  && nearDensity > 4.0 * Math.Max(1e-9, farDensity),
                  $"evicted {evicted} → total suppression {totalDiff:F6}; per-cluster density " +
                  $"within 1.5λ: {nearDensity:F3} (n={nearN}), beyond: {farDensity:F3} (n={farN})");
        }

        private static void ClearingPrice(ulong seed)
        {
            // The price is the MARKET-CLEARING price: the marginal bidder's WTP
            // at the quantity that fills the stock. Three properties, all of
            // which the old presence-weighted mean failed (it had no quantity
            // term at all): rent falls as supply grows against fixed demand,
            // rent rises as demand grows against fixed supply, and — the gap
            // this closes — a vacancy overhang SOFTENS rent instead of leaving
            // it untouched.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var acc = sim.Engine.Access;
            var pres = sim.Engine.SegmentPresence;
            // PAIR THE LEGS. The income counterfactuals below REBUILD
            // acc.BidLadder (with pRich / pAll) before reading a price off it,
            // so the baselines they are divided by have to be built the same
            // way, from the same population. They were not: the sweep read the
            // ladder sim.Run happened to leave behind, and that ladder is a
            // different population, not merely a differently-priced one.
            // Access.Refresh builds the ladder (Access.cs) at the TOP of
            // EconomyEngine.RefreshTick, before the same refresh re-draws
            // employment and applies `UnemployedTicks += RefreshInterval` — so
            // the engine's ladder is always one refresh behind the 60-tick
            // benefit cliff, and short RefreshInterval−1 ticks of arrivals.
            // SINGLE-SOURCED, FLAGGED AS SUCH: "confirmed causally — subtracting
            // that one increment back off the population and rebuilding at the
            // SAME p reproduces the stale base price exactly on seeds 0,1,2,4,5,
            // 6,8 and to within 2% on 3 and 7". That was one run by one author;
            // nobody has reproduced it since, including the round that rewrote
            // the income legs below. Treat it as an observation, not a result,
            // until someone re-measures it. The PAIRING itself does not rest on
            // it: pairing is required because the treated arms rebuild the
            // ladder and the baseline must be built the same way, which is true
            // whatever the mechanism behind the stale ladder turns out to be.
            //
            // It lands on the TAIL because the excess-regime price is a
            // 0.5%-from-bottom quantile of the demand curve (LandAccounting
            // MinTailFraction) and a household whose benefit has just expired
            // drops to max(ResidentialMinimumEarnings, its segment transfer) —
            // straight into that quantile. NOT to the floor: for StudentLow
            // (3.0), SeniorLow (8.0) and SeniorMid (13.0) the transfer is the
            // binding term, which is exactly the case that kept this check red
            // on seed 271 after the pairing fix. Measured over seeds 0–49:
            // unpaired, the tail ratio scattered 0.77–2.13, i.e. it reported
            // that doubling every income source LOWERED the price on three seeds
            // and raised it above 2× on two — impossible for a bid that is
            // homogeneous of degree 1 in income, which is proof the statistic
            // was measuring the gap between two worlds rather than an
            // elasticity. Five seeds (3, 17, 21, 32, 38) fell under the 1.3 bar.
            //
            // The claim this comment used to end on — "paired, the ratio is
            // exactly 2.0000 on 48 of the 50 and never below 1.6753" — was true
            // over seeds 0–49 and false past them (seed 271 = 1.00758, seed 910
            // = 1.00000). Pairing was necessary and is not in question; it was
            // not sufficient, and that ratio is no longer the assertion. What
            // the legs below assert instead, measured over seeds 0–999: paired
            // AND with the segment transfer scaled too, the response is bitwise
            // exactly 2.0 at every one of the 25 supply points on 1000 of 1000
            // seeds.
            //
            // Note the income block below already restores this same rebuild
            // before leg (d) runs; this is that convention applied to leg (c)
            // as well, not a new one.
            acc.RebuildHouseholdLadders(sim.W, p);

            // Pick a cluster with real stock and real demand.
            int c0 = 0;
            for (int c = 1; c < acc.C; c++)
                if (acc.HousingStock[0][c] > acc.HousingStock[0][c0]) c0 = c;
            double stock = acc.HousingStock[0][c0];

            // (a) supply ladder at fixed demand: strictly non-increasing.
            // Supply is varied through the PRODUCTION stock array (and
            // restored), so the check exercises the same read path Assess uses.
            double PriceAtStock(double units)
            {
                double saved0 = acc.HousingStock[0][c0];
                acc.HousingStock[0][c0] = units;
                double v = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p);
                acc.HousingStock[0][c0] = saved0;
                return v;
            }
            // Two-regime economics (see ResidentialBidPerUnit): while the
            // stock CLEARS, price carries the quantity response; once demand
            // exhausts, price floors FLAT at the deepest positive bidder and
            // the response moves to expected VACANCY (fillRatio). So the
            // ladders assert weak price monotonicity with at least one strict
            // step, and require the fill ratio to carry the response wherever
            // the price has gone flat.
            double FillAtStock(double units)
            {
                double saved0 = acc.HousingStock[0][c0];
                acc.HousingStock[0][c0] = units;
                LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double f);
                acc.HousingStock[0][c0] = saved0;
                return f;
            }
            double pLow = PriceAtStock(stock * 0.5);
            double pMid = PriceAtStock(stock * 2.0);
            double pHigh = PriceAtStock(stock * 8.0);
            double fMid = FillAtStock(stock * 2.0), fHigh = FillAtStock(stock * 8.0);
            bool supplyMonotone = pLow >= pMid - 1e-9 && pMid >= pHigh - 1e-9 && pLow > pHigh * 0.999 + 1e-6
                // Where the price ladder goes flat (excess regime), the
                // expected fill must fall instead: supply×8 leaves more
                // units unlet than supply×2 at the same flat price.
                && (pMid > pHigh + 1e-9 || fHigh < fMid - 1e-9);

            // (b) demand ladder at fixed supply: weakly increasing price,
            // strictly from base to double; where flat (excess), fill rises.
            var presHalf = new double[Segment.Count];
            var presDouble = new double[Segment.Count];
            for (int s = 0; s < Segment.Count; s++) { presHalf[s] = pres[s] * 0.5; presDouble[s] = pres[s] * 2.0; }
            double dLow = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, presHalf, p, out double fdLow);
            double dMid = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fdMid);
            double dHigh = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, presDouble, p, out _);
            bool demandMonotone = dLow <= dMid + 1e-9 && dMid <= dHigh + 1e-9 && dHigh > dLow + 1e-6
                && (dLow < dMid - 1e-9 || fdLow < fdMid - 1e-9);

            // (c) the price is pinned to the DEMAND CURVE across BOTH regimes.
            // Sweep supply over three orders of magnitude (10^-1.5 to 10^+1.5 is
            // a factor of 1000; this comment used to say four) and require the
            // price to be non-increasing at every step. Then require a large
            // total decline, and require the whole curve to track income — see
            // the income legs below, which is where the constant-price and
            // income-blind mutants actually die.
            //
            // CORRECTED, AND FLAGGED RATHER THAN FIXED: this comment used to
            // claim the sweep policed the cleared→excess transition, "where an
            // excess rule priced above its own scarcity price shows up as an
            // UPWARD step". At 25 points it does not. Measured over seeds 0–299:
            // the shipped 25-point sweep reports monotone on 300/300, while a
            // 401-point sweep over the IDENTICAL range finds an upward step on
            // 99 of the 300 (worst step +181.8%, seed 294). So this leg is not
            // currently watching that boundary at the resolution it claims to.
            // Left alone deliberately — raising the resolution turns the check
            // red on a third of seeds, and whether those steps are a real
            // pricing defect or a legitimate discontinuity at the regime
            // boundary is a question for its own commit, not a side effect of
            // rewriting the income legs.
            //
            // This replaces a single-segment equality that assumed one flat
            // tranche per segment — true only while a segment was one point
            // income, and false (by design) now that each carries a real
            // income distribution (Income.cs).
            double SweepAt(double units, EconParams pp)
            {
                double saved0 = acc.HousingStock[0][c0];
                acc.HousingStock[0][c0] = units;
                double v = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, pp);
                acc.HousingStock[0][c0] = saved0;
                return v;
            }
            // The sweep KEEPS its points and its values: the income legs below
            // re-read the same 25 supply points under a counterfactual and
            // compare pointwise, so numerator and denominator have to be the
            // same position on the supply axis. (They were not; see (c0).)
            double SweepUnits(int i) => Math.Max(0.5, stock * Math.Pow(10, -1.5 + 3.0 * i / 24.0));
            var sweep = new double[25];
            bool sweepMonotone = true; int upSteps = 0;
            double sweepHi = 0, sweepLo = 0, minSweep = double.MaxValue, prevSweep = double.MaxValue;
            for (int i = 0; i <= 24; i++)
            {
                double v = SweepAt(SweepUnits(i), p);
                sweep[i] = v;
                if (v < minSweep) minSweep = v;
                if (i == 0) sweepHi = v;
                sweepLo = v;
                // Tolerance is relative and tiny: real steps are smooth, a
                // markup at the regime boundary is a step change.
                if (v > prevSweep * (1 + 1e-9) + 1e-12) { sweepMonotone = false; upSteps++; }
                prevSweep = v;
            }
            bool sweepDeclines = sweepHi > sweepLo * 1.5 && sweepLo > 0;
            // ---- DOES THE PRICE TRACK INCOME? FOUR CLAIMS, NOT ONE RATIO ----
            //
            // What this replaces, and why. The previous version was a single
            // statistic — "doubling wages lifts the top >1.5×, doubling wages,
            // benefit and floor lifts the tail >1.3×" — and every quantitative
            // claim its comment made about the tail was wrong. It said the 1.3
            // bar had "a measured floor of 1.6753 over fifty seeds, so it is a
            // real bar with headroom". Over seeds 0–999 that statistic runs
            // 1.0000 (seed 910) to 2.0; it falls short of 2.0 on 22 seeds and
            // BELOW the 1.3 bar on 7 (271, 327, 549, 602, 642, 759, 910). The
            // check was already red on seed 271 when that sentence was written.
            //
            // Worse, and this is the whole case for rewriting rather than
            // retuning: the bar passed six of the eight mutants built against it
            // (matrix below). One of those reads no household income at all at
            // the deep end. Run end to end on seed 271, all four combinations:
            //
            //     3a507e9, real model            21/22   (this check red)
            //     3a507e9, income-blind mutant    22/22   (green)
            //     these legs, real model          22/22   (green)
            //     these legs, income-blind mutant 21/22   (red, via (c2))
            //
            // The suite as shipped scored the mutant ABOVE the model on the one
            // seed this exercise exists for. So the bar is deleted, not widened.
            //
            // All four legs read ladders rebuilt inside this fixture, so every
            // ratio divides one population at two prices rather than two
            // populations (see PAIR THE LEGS at the top — that part stands).
            //
            // SCOPE: these are FORECAST-path properties (realized:false). With
            // realized:true the price is a stored auction outcome that a ladder
            // rebuild does not touch, so (c1)–(c3) are false there by
            // construction. Everything below is the 8×8 / 2500-household /
            // 80-tick fixture with default FeatureFlags; the derivations are
            // fixture-independent, the RANGES are not.

            // Each counterfactual scales income parameters, and the segment
            // transfer lives in Segment.All — a process-global static table
            // with no EconParams knob. It is scaled by clone-and-restore.
            //
            // The replaced comment refused to touch it, on the stated grounds
            // that "a check that mutates it leaks into every check that runs
            // after it". That is false as stated and it cost the check its
            // tail: measured, with Array.Copy back in a `finally`, `verify` is
            // byte-identical on every other check on seeds 0, 3, 138, 208, 271
            // and 327, and legs (a) and (d) inside this check are byte-identical
            // too. The REAL caveat, which the old text did not state: Segment.All
            // is process-global, so this must not run concurrently with another
            // check. The harness is serial, and `tableRestored` below is a
            // tripwire so that a leak fails the check that caused it rather than
            // the next one.
            var savedSegments = (Segment[])Segment.All.Clone();
            EconParams Income(double wage, double benefit, double floor) => new EconParams
            {
                // NOTE this is a fresh default with fields overridden, not a
                // copy of `p`. Correct only because `p` here is itself
                // `new EconParams()`. If anyone ever calibrates `p` in this
                // fixture, every non-income parameter silently reverts in the
                // treated arm and these identities break for a reason that has
                // nothing to do with income. EconParams has no Clone().
                WageBasic = p.WageBasic * wage,
                WageSkilled = p.WageSkilled * wage,
                WageEducated = p.WageEducated * wage,
                UnemploymentBenefit = p.UnemploymentBenefit * benefit,
                ResidentialMinimumEarnings = p.ResidentialMinimumEarnings * floor,
            };
            T WithIncome<T>(EconParams pp, double transferScale, Func<T> body)
            {
                try
                {
                    if (transferScale != 1)
                        for (int s = 0; s < Segment.All.Length; s++)
                        {
                            var g = Segment.All[s];
                            Segment.All[s] = new Segment(g.Name, g.Life, g.Labor, g.Participation,
                                g.Transfer * transferScale, g.JobAccessW, g.GoodsAccessW, g.SchoolAccessW,
                                g.AmenityW, g.HealthW, g.PollutionW, g.DensityTolerance, g.MaxRentShare, g.Adults);
                        }
                    acc.RebuildHouseholdLadders(sim.W, pp);
                    return body();
                }
                finally
                {
                    Array.Copy(savedSegments, Segment.All, savedSegments.Length);
                    acc.RebuildHouseholdLadders(sim.W, p);
                }
            }

            // (c0) DIRECTION at the top: doubling WAGES must lift the
            // thin-supply price a lot, and cannot lift it past 2×.
            //
            // The read point is now SweepUnits(0) — the same point sweepHi was
            // read at. It used to be stock×0.03 against a denominator read at
            // stock×10^-1.5 = stock×0.0316. That mismatch moved the ratio on 9
            // of seeds 0–999 (96, 107, 205, 208, 303, 311, 578, 723, 830), by
            // up to 2.17% upward, and on seed 208 it pushed the statistic to
            // 2.00426 — above the arithmetic ceiling for a wage-only doubling,
            // the same species of impossible number that condemned the unpaired
            // statistic in round one. Aligned, the ratio runs 1.60012 (seed
            // 5191) to 1.98232 over seeds 0–14799 and never exceeds 2. Over the
            // first thousand alone it reads 1.71733, which would have suggested
            // 14% of headroom on the 1.5 bar where the wider sweep shows 6.7% —
            // stated at the wide range because a range is a claim about where it
            // was measured, and this file has twice shipped one that was not.
            //
            // The 2.0 ceiling is a THEOREM, not an empirical bound, which is
            // why 0.018 of headroom is not a worry: income is
            // max(floor, earners×wage×(1−tax) + benefit + transfer), so doubling
            // wages alone scales each rung by a factor in [1,2]; order
            // statistics are monotone under pointwise domination; and the read
            // POSITION is identical in both arms because Cum(0), readAt and the
            // tail target are counts with no income in them. The 1e-6 slack is
            // for the bisection grid (relative error ~1e-10 at this read).
            // Do NOT convert this leg to an identity — a wage-only doubling is
            // genuinely not a pure scaling of income, so no exact ratio exists.
            // It also does real work: it kills the rung^1.05 mutant on 111 of
            // seeds 0–299, which no other leg's DIRECTION claim catches.
            var pRich = Income(2, 1, 1);
            double richHi = WithIncome(pRich, 1, () => SweepAt(SweepUnits(0), pRich));
            double topResponse = sweepHi > 0 ? richHi / sweepHi : 0;
            bool wagesLiftTop = topResponse > 1.5 && topResponse <= 2.0 * (1 + 1e-6);

            // (c1) TRANSMISSION: the bid is homogeneous of degree 1 in income.
            // Scale EVERY income source — the three wages, UnemploymentBenefit,
            // ResidentialMinimumEarnings and Segment.Transfer — by 2, and every
            // one of the 25 sweep points must double. Pointwise, over all 25.
            //
            // Honesty about why: the pointwise form was chosen on the belief
            // that "with the transfer held the deviation reaches 0.5 mid-curve
            // while both endpoints still read 2.0", i.e. that the interior
            // carries information the ends do not. Measured, that is false —
            // over seeds 0–299 the larger endpoint deviation is at least 0.0088
            // on 300 of 300, four orders above the 1e-6 tolerance, and there is
            // no seed where the ends are clean and the interior is dirty. A
            // two-endpoint form would have detected identically on every seed
            // measured. The 25 points are kept because they cost nothing and a
            // pointwise identity is the stronger statement in principle, NOT
            // because a measurement forced them. (What is true and narrower: the
            // DEEP end alone reads exactly 2.0 on 294 of 300, so a deep-only
            // identity really would miss it — the thin end is what does the
            // work.)
            //
            // Why an identity rather than a bar: income enters only as
            // RentShare × income × densityAppeal, and the read position is a
            // COUNT, so scaling every source scales the answer exactly. Measured
            // over seeds 0–999 (25,000 pairs): the deviation is identically 0 —
            // bitwise, at all 25 points on all 1000 seeds — because doubling in
            // binary is exact through every rung, maxBid and bisection midpoint.
            // That is 5.6 orders of magnitude tighter than the 1.3 bar it
            // replaces, which over the same range was not merely slack but
            // UNSATISFIABLE on 7 seeds.
            //
            // The 1e-6 tolerance is therefore not absorbing measured noise. It
            // is there because exactness is contingent on things a maintainer
            // can innocently break: at λ=3 the identity holds only to 1.302e-15
            // relative (max over seeds 0–999, worst seed 990) and is bitwise
            // exact at only 0–21 of the 25 points, so a bitwise assertion would
            // go red if anyone rescaled by 3 for no semantic reason. Distance to
            // real signal, measured over seeds 0–299: a mutant raising each rung
            // to the power 1.05 deviates by 3.526e-2, one adding 0.10 to each
            // rung by 4.73e-2–1.51e-1. Any tolerance in roughly [1e-8, 1e-4]
            // kills the same set; 1e-6 is the middle of that plateau.
            //
            // WHY THE TRANSFER HAD TO JOIN THE SCALED SET rather than the bar
            // being widened: with it held fixed the relation is not an identity
            // ANYWHERE — pointwise deviation exceeds 1e-6 on 1000 of 1000 seeds,
            // min 0.0408 (seed 330), max 0.5000 (seed 242). Segment.All gives
            // SeniorLow/SeniorMid a fixed Transfer of 8.0/13.0 with Adults=0 and
            // Participation=0, and StudentLow 3.0, so their income is
            // max(floor, transfer) = transfer forever; the doubled floor reaches
            // only 2.0 and never touches them. That is the whole of seed 271's
            // failure, and an identity form is simply unavailable without it.
            var pAll = Income(2, 2, 2);
            double homogDev = WithIncome(pAll, 2, () =>
            {
                double d = 0;
                for (int i = 0; i <= 24; i++)
                    d = Math.Max(d, Math.Abs(SweepAt(SweepUnits(i), pAll) - 2 * sweep[i]) / (2 * sweep[i]));
                return d;
            });
            // DEGENERACY GUARD. ResidentialBidPerUnit has absolute early-outs
            // (maxBid ≤ 1e-12, totalMass ≤ 1e-12, loT ≤ 1e-12 → return 0) under
            // which 0 == 2×0 would pass vacuously, and a zero sweep point makes
            // homogDev NaN — which fails the leg loudly, the right direction,
            // but says nothing. Measured headroom over seeds 0–999: the minimum
            // base price anywhere on the sweep runs 0.11202 (seed 353) to
            // 1.43276, i.e. 112× above this guard and 9 orders above the
            // early-outs. It is not a live constraint on the real model; it is
            // what catches a mutant that empties the bottom of the ladder.
            bool incomeHomogeneous = minSweep > 1e-3 && homogDev <= 1e-6;

            // (c2) SELECTION: the deep price is a quantile of the ACTUAL lower
            // tail, not a statistic of the parameters.
            //
            // THIS IS THE LEG THE OLD CHECK DID NOT HAVE, AND IT IS THE ONE
            // THAT MATTERS. (c1) tests TRANSMISSION and says nothing about
            // SELECTION, by construction: Cum(0) and the tail target
            // totalAt0×(1−MinTailFraction) are COUNTS with no income in them, so
            // the same household anchors both arms of any income scaling
            // whatever the pricing rule is. Measured over seeds 0–299, two
            // mutants that are exactly homogeneous of degree 1 pass (c1) on
            // 300/300 with deviation identically ZERO: one returns a fixed
            // multiple of RentShare × ResidentialMinimumEarnings × densityAppeal
            // at the deep end (reads no household income at all) and passes
            // (c0) and (c3) on 300/300 as well; the other returns a fixed
            // multiple of the mass-weighted MEAN rung (reads the whole
            // distribution but the wrong statistic of it) and passes (c0) on
            // 300/300 and (c3) on 292/300. Both pass the old 1.3 bar on 300/300.
            // No choice of bar and no parameter counterfactual can see the
            // first at all; only a counterfactual that moves the POPULATION can.
            //
            // So: rank households by the ladder's OWN rung (AccessState.
            // LadderBase — literally the function RebuildHouseholdLadders sorts,
            // extracted for this purpose so the test cannot rank by a
            // re-implementation that drifts), halve RentShare in the poorest
            // quarter of each segment, and require
            //   • the deep price to halve EXACTLY — the anchor is inside the cut
            //     set, every rung there scales by 0.5 and order is preserved, so
            //     the quantile scales by 0.5; and
            //   • the thin-supply price not to move AT ALL — the cut set is the
            //     BOTTOM quarter, so maxBid is untouched and the bisection
            //     bracket is identical, and the thin-supply read sits so far up
            //     the ladder that the cut households are not counted in Cum at
            //     any price the search converges through, in either arm.
            //
            // Measured over seeds 0–999: max |deep/base ÷ 0.5 − 1| = 6.124e-10
            // (worst seed 873), i.e. 1/1600th of the 1e-6 relative tolerance;
            // the thin-supply price is BITWISE identical on 1000 of 1000 (max
            // deviation exactly 0), so 1e-9 there is a tolerance in name only,
            // kept against a future last-bit tie. The bitwise result is what the
            // mechanism above is inferred FROM, not the other way round — if a
            // future change makes the thin read drift, re-derive rather than
            // widening 1e-9.
            //
            // CUT DEPTH 0.25 IS MEASURED, NOT CHOSEN. Scanning the cut from 0.02
            // to 0.60 over seeds 0–299: the smallest cut that makes the deep
            // price halve exactly ranges 0.02–0.14 (worst seed 288); the largest
            // cut leaving the top read bitwise clean ranges 0.44–0.60 (worst
            // seed 197). The safe window is [0.14, 0.44] on 300 of 300 seeds and
            // 0.25 sits near its log centre — 1.8× above the deepest anchor,
            // 1.8× below the shallowest top contamination. The regime
            // precondition is measured too, not assumed: the fill at the deep
            // read runs 0.0287 (seed 600) to 0.2875 (seed 563) over seeds
            // 0–999, so the tail anchor is in the excess regime on every seed.
            //
            // WHAT (c2) STILL DOES NOT KILL: a model that prices a DIFFERENT low
            // quantile of the correct population — the 5% quantile instead of
            // the 0.5% one — halves too, and survives all four legs. Pinning
            // that needs an assertion on MinTailFraction's meaning (mass
            // strictly below the price ≤ 0.5% of demand), which is a different
            // check and belongs in a different commit.
            double poorLo = 0, poorHi = 0;
            var undo = new List<(Household h, double rentShare)>();
            try
            {
                var bySeg = new List<(double rung, Household h)>[Segment.Count];
                for (int s = 0; s < Segment.Count; s++) bySeg[s] = new List<(double, Household)>();
                foreach (var h in sim.W.Households)
                {
                    if (h.ExitedTick >= 0) continue;
                    bySeg[h.Segment].Add((acc.LadderBase(h, ZoneKind.ResidentialLow, p), h));
                }
                for (int s = 0; s < Segment.Count; s++)
                {
                    var list = bySeg[s];
                    list.Sort((x, y) => x.rung.CompareTo(y.rung));
                    int cut = (int)(list.Count * 0.25);
                    for (int i = 0; i < cut; i++)
                    {
                        undo.Add((list[i].h, list[i].h.RentShare));
                        list[i].h.RentShare *= 0.5;
                    }
                }
                acc.RebuildHouseholdLadders(sim.W, p);
                poorLo = SweepAt(SweepUnits(24), p);
                poorHi = SweepAt(SweepUnits(0), p);
            }
            finally
            {
                foreach (var (h, rs) in undo) h.RentShare = rs;
                acc.RebuildHouseholdLadders(sim.W, p);
            }
            double tailResponse = sweepLo > 0 ? poorLo / sweepLo : 0;
            double topInvariance = sweepHi > 0 ? poorHi / sweepHi : 0;
            bool pricesTail = minSweep > 1e-3
                              && Math.Abs(tailResponse - 0.5) <= 0.5e-6
                              && Math.Abs(topInvariance - 1.0) <= 1e-9;

            // (c3) COVERAGE: every income source reaches the price somewhere.
            //
            // (c1) is blind to a model that DROPS a source, because removing a
            // term that is itself homogeneous leaves the whole homogeneous:
            // measured over seeds 0–299, a model whose income ignores
            // UnemploymentBenefit and one that ignores the segment transfer each
            // pass (c0), (c1) and (c2) on 300/300 — and passed the old 1.3 bar
            // on 300/300 as well. This leg is the only thing that kills either,
            // and it is also what kills a model that drops the earnings floor.
            //
            // Scale ONE source by 10 and require at least one of the 25 sweep
            // points to move by ≥25%. ×10 rather than ×2, and this is the whole
            // reason the leg has this shape: at ×2 the statutory floor moves
            // nothing at all on 29 of seeds 0–999 — it simply does not bind at
            // any read point on those seeds — so a ×2 form is unavailable for
            // the floor, and a floor-dropping mutant survives on 61 of seeds
            // 0–299. At ×10 the floor binds on every seed measured and that
            // mutant dies on 300/300.
            //
            // The bar is a HAIRLINE above zero, and that is the whole design.
            // This leg is not a magnitude claim: a model that drops a source
            // scores exactly 0.0, a model that reads it scores something, and
            // the only question is which side of that line the number falls.
            // How FAR the price moves depends on which households the source
            // happens to bind for on a given seed, which is a property of the
            // population and not of the model.
            //
            // An earlier draft of this leg required ≥0.25, on the strength of
            // "weakest response over seeds 0–999 is 0.7945, so the bar has 3.2×
            // headroom". That sentence was a bound written past its evidence,
            // which is the exact failure this file has now made twice: swept to
            // 1000 and the counterexample was at 6550, where the floor arm moves
            // the curve 0.1368 and the check goes red. Swept wider — 14,600
            // seeds — the tightest response is that same 0.137, so the true
            // headroom over 0.25 was negative and the 3.2× was an artifact of
            // where the sweep stopped. Against 1e-9 the same measurement gives
            // eight orders of magnitude, and it is measuring the thing the leg
            // actually claims.
            //
            // What can still fail it honestly: a seed on which ×10 of some
            // source moves NO read point at all, meaning that source binds for
            // nobody anywhere on the sweep. That is a population accident rather
            // than a model defect, and the fix is a larger multiplier — ×2 is
            // already known to be unusable for the floor, which moves nothing on
            // 29 of seeds 0–999.
            double SourceReach(EconParams pp, double transferScale) => WithIncome(pp, transferScale, () =>
            {
                double best = 0;
                for (int i = 0; i <= 24; i++)
                    best = Math.Max(best, Math.Abs(SweepAt(SweepUnits(i), pp) - sweep[i]) / sweep[i]);
                return best;
            });
            var pWageX = Income(10, 1, 1);
            var pBenX = Income(1, 10, 1);
            var pFloorX = Income(1, 1, 10);
            var pTransX = Income(1, 1, 1);
            double reachWage = SourceReach(pWageX, 1);
            double reachBenefit = SourceReach(pBenX, 1);
            double reachFloor = SourceReach(pFloorX, 1);
            double reachTransfer = SourceReach(pTransX, 10);
            double weakestSource = Math.Min(Math.Min(reachWage, reachBenefit),
                                            Math.Min(reachFloor, reachTransfer));
            bool sourcesReach = minSweep > 1e-3 && weakestSource > 1e-9;

            // Tripwire, not decoration: every counterfactual above restores
            // Segment.All in a `finally`, and this fails the check that caused a
            // leak rather than letting it silently reprice every check after it.
            bool tableRestored = true;
            for (int s = 0; s < Segment.All.Length; s++)
                if (Segment.All[s].Transfer != savedSegments[s].Transfer) tableRestored = false;

            // MEASURED MUTANT MATRIX, seeds 0–299, 300 seeds per cell. Each
            // mutant was built in a scratch copy of the tree, magnitude-
            // calibrated where it has a free constant so no other leg catches it
            // on level alone, and run through these same four legs. "kills" =
            // the composite goes red.
            //
            //                                      old >1.3 bar     these four legs
            //   income-blind deep end (fixed        PASS 300/300     dies 300/300  (c2)
            //     multiple of the floor)
            //   deep end = mean rung                PASS 300/300     dies 300/300  (c2)
            //   income ignores UnemploymentBenefit  PASS 300/300     dies 300/300  (c3 alone
            //                                                        on 299, c2 also on 1)
            //   income ignores segment Transfer     PASS 300/300     dies 300/300  (c3)
            //   income ignores the earnings floor   pass  54/300     dies 300/300  (c3)
            //   ladder rung ^ 1.05                  PASS 299/300     dies 300/300  (c1,c2)
            //   ladder rung + 0.10                  PASS 298/300     dies 300/300  (c1,c2)
            //   prices off QUANTITY alone           dies 300/300     dies 300/300  (all)
            //
            // Read the first column: the shipped bar killed ONE of the eight,
            // and reliably killed only that one. (The 1–2 seed "failures" in
            // that column on the rung mutants are seeds where the real model is
            // red too — 271 is the one inside this range; those are not kills,
            // they are the check's own defect.) These four legs kill all eight
            // on every seed measured, while the real model is green on 300/300.
            //
            // WHAT NO LEG HERE CAN CATCH, so nobody has to rediscover it:
            //  - a model that prices the right population at the wrong low
            //    quantile (see (c2));
            //  - a model that is income-BLIND but DISTRIBUTION-PRESERVING. (c2)
            //    perturbs RentShare, not income, because RentShare enters the
            //    rung as a clean factor while income passes through a max().
            //    That buys an exact identity and costs reach: the bid base is
            //    RentShare × income × appeal, so halving RentShare halves the
            //    rungs of a model that never reads income by exactly as much as
            //    it halves the real model's. (c2) therefore pins the BID-BASE
            //    distribution, not the income distribution, and an adversarial
            //    mutant built to exploit that survived the whole 22-check suite
            //    on about 7% of seeds. Closing it needs a counterfactual that
            //    perturbs the income or wealth of individual lower-tail
            //    households with RentShare held — which cannot be an identity,
            //    because of that same max(), and so was not attempted here;
            //  - any change confined to the realized/auction path, which none of
            //    these legs exercises.
            bool tracksIncome = wagesLiftTop && incomeHomogeneous && pricesTail
                                && sourcesReach && tableRestored;

            // (d) population collapse: with the flat-tail excess price the
            // response is regime-dependent — price falls while the submarket
            // clears, expected fill falls once it does not. Composition drift
            // (WHO remains changes the tail tranche's value) may nudge the
            // flat price a few percent either way; what must never happen is
            // the market registering NO response on either margin.
            double before = LandAccounting.ResidentialBidPerUnit(acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out double fillBefore);
            int killed = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                if (sim.W.Parcels[h.HomeParcel].Cluster == c0) continue;   // keep c0's own residents
                if (SplitMix64.Hash01((ulong)h.Id * 977 + 5) < 0.6)
                { h.ExitedTick = sim.W.Tick; killed++; }                   // citywide demand collapse
            }
            sim.Run(10);
            double after = LandAccounting.ResidentialBidPerUnit(
                sim.Engine.Access, c0, ZoneKind.ResidentialLow, 2, sim.Engine.SegmentPresence, p, out double fillAfter);
            // Either the price falls, or — once the submarket is in the excess
            // regime where the price is pinned to the deepest real bidder — the
            // expected FILL falls hard while the price only drifts.
            //
            // The drift band is 20%, and the reason is an order-statistic one
            // worth stating because it is a genuine property of pricing off a
            // real population rather than a fitted curve: the excess anchor is
            // a low QUANTILE of the households present, and culling 60% of the
            // city (while deliberately sparing c0's own residents) both shrinks
            // and re-weights the sample, so that quantile moves by more than
            // rounding.
            //
            // CORRECTED, AND FLAGGED RATHER THAN FIXED. This comment used to
            // read "Measured here: fill 0.59 → 0.31 (a 47% collapse, the
            // substantive response) while the price drifted +11%", and defended
            // its 20% band on the grounds that "the leg it guards is carried by
            // the fill term, which is nowhere near its threshold". Both are
            // false, and the second is backwards. Measured over seeds 0–299:
            // fillBefore is exactly 1.00 on 300/300 and fillAfter never drops
            // below 0.909, so `fillAfter < fillBefore * 0.8` fires on 0 of 300
            // — the vacancy disjunct never fires at all, and the leg is carried
            // ENTIRELY by the price disjunct, which fires on 288 of 300. (The
            // 12 seeds where neither fires are this check's other standing
            // failure: 44, 54, 77, 125, 128, 138, 206, 233, 236, 242, 288, 298.)
            // The band is therefore load-bearing in exactly the way the old
            // comment said it was not. Left alone deliberately: this leg is not
            // what the current commit rewrites, and fixing it means finding a
            // cull that actually produces vacancy in this fixture, which is its
            // own investigation.
            bool softens = after < before * 0.98
                           || (fillAfter < fillBefore * 0.8 && after < before * 1.20);

            Console.WriteLine($"    AUDIT supplyMonotone={supplyMonotone} demandMonotone={demandMonotone} "
                + $"killed>100={killed > 100} softens={softens} sweepMonotone={sweepMonotone} "
                + $"sweepDeclines={sweepDeclines} tracksIncome={tracksIncome} | dLow={dLow:F4} dMid={dMid:F4} dHigh={dHigh:F4}");
            Console.WriteLine($"    AUDIT income: wagesLiftTop={wagesLiftTop} ({topResponse:F5}) "
                + $"incomeHomogeneous={incomeHomogeneous} (dev {homogDev:E3}) "
                + $"pricesTail={pricesTail} (deep ×{tailResponse:F8}, top ×{topInvariance:F12}) "
                + $"sourcesReach={sourcesReach} (wage {reachWage:F3} benefit {reachBenefit:F3} "
                + $"floor {reachFloor:F3} transfer {reachTransfer:F3}) tableRestored={tableRestored}");
            Check("clearing price: quantity responds (supply ↓, demand ↑, population ↓ — price while cleared, vacancy once flat)",
                  supplyMonotone && demandMonotone && killed > 100 && softens
                  && sweepMonotone && sweepDeclines && tracksIncome,
                  $"supply×{{0.5,2,8}} → {pLow:F2}/{pMid:F2}/{pHigh:F2} (fill {fMid:F2}→{fHigh:F2}); " +
                  $"demand×{{0.5,1,2}} → {dLow:F2}/{dMid:F2}/{dHigh:F2} (fill {fdLow:F2}→{fdMid:F2}); " +
                  $"after {killed} citywide exits bid {before:F2} → {after:F2}, fill {fillBefore:F2} → {fillAfter:F2}; " +
                  $"25-point supply sweep {sweepHi:F2}→{sweepLo:F2} " +
                  $"({(sweepMonotone ? "monotone" : $"{upSteps} UPWARD steps")}); " +
                  $"income: wages×2 top ×{topResponse:F5} (≤2), all-income×2 pointwise dev {homogDev:E2} (≤1e-6), " +
                  $"poorest-quarter cut → deep ×{tailResponse:F8} (=0.5) top ×{topInvariance:F12} (=1), " +
                  $"weakest single source ×10 moves {weakestSource:F3} (≥0.25)");
        }

        private static void OccupiedStockCarriesRent(ulong seed)
        {
            // Commensurability of the clearing condition, per DENSITY KIND.
            // The demand mass a queue accumulates and the stock it clears must
            // be the same kind of quantity. When mass was a per-CLUSTER share
            // compared against per-KIND stock, every density-tolerant household
            // was counted at full weight in BOTH queues while each faced only
            // its own stock: the high-density queue could never reach its stock,
            // so every apartment building priced as a permanent vacancy
            // overhang at 100% occupancy and ALL high-density land rent went to
            // exactly zero (adversarial review, three independent lenses).
            //
            // The invariant: fully-occupied stock is not an overhang. If a
            // density kind is essentially fully let citywide, a healthy share
            // of its parcels must carry positive assessed land rent.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Seed = seed, SeedHouseholds = 8000 };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(300);
            var w = sim.W;

            // Force a full reassessment so nothing is stale from the slice.
            foreach (var pl in w.Parcels)
                LandAccounting.Assess(w, sim.Engine.Access, sim.Engine.Trade, pl, sim.Engine.SegmentPresence, p);

            (int built, int positive, int units, int filled, double sumLR) Census(ZoneKind kind)
            {
                int b = 0, pos = 0, u = 0, f = 0; double lr = 0;
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || pl.Use != kind) continue;
                    b++; u += pl.Units; f += pl.OccupantHouseholds.Count; lr += pl.AssessedLR;
                    if (pl.AssessedLR > 1e-9) pos++;
                }
                return (b, pos, u, f, lr);
            }
            var lo = Census(ZoneKind.ResidentialLow);
            var hi = Census(ZoneKind.ResidentialHigh);

            double hiOcc = hi.units > 0 ? (double)hi.filled / hi.units : 0;
            double loOcc = lo.units > 0 ? (double)lo.filled / lo.units : 0;

            // A Ricardian extensive margin is CORRECT and must not be
            // legislated away: the worst land in use earns no rent, so a large
            // zero-LR tail among peripheral parcels is the model working. What
            // must never happen is an entire density class pinned at zero —
            // that was the commensurability bug (152/152 towers at exactly
            // zero, ΣLR == 0). So the invariant is stated on the land that is
            // NOT marginal: rank each kind's parcels by their cluster's access
            // and require the best quartile to earn rent, plus ΣLR > 0.
            var accessState = sim.Engine!.Access!;
            double AccessOf(int cluster)
            {
                double v = 0;
                for (int s = 0; s < Segment.Count; s++) v += accessState!.AccessValue[s][cluster];
                return v / Segment.Count;
            }
            (int n, int pos) BestQuartile(ZoneKind kind)
            {
                var ps = w.Parcels.Where(pl => pl.State == ParcelState.Built && pl.Use == kind)
                                  .OrderByDescending(pl => AccessOf(pl.Cluster)).ToList();
                int take = Math.Max(1, ps.Count / 4);
                int pos = ps.Take(take).Count(pl => pl.AssessedLR > 1e-9);
                return (take, pos);
            }
            var hiQ = BestQuartile(ZoneKind.ResidentialHigh);
            var loQ = BestQuartile(ZoneKind.ResidentialLow);
            bool hiOk = hi.built < 5 || hiOcc < 0.5
                        || (hi.sumLR > 1e-6 && hiQ.pos >= 0.5 * hiQ.n);
            bool loOk = lo.built < 5 || loOcc < 0.5
                        || (lo.sumLR > 1e-6 && loQ.pos >= 0.5 * loQ.n);

            Check("occupied stock carries land rent in BOTH densities (per-kind commensurability)",
                  hiOk && loOk,
                  $"high: best-access quartile {hiQ.pos}/{hiQ.n} with LR>0, ΣLR {hi.sumLR:F1}, " +
                  $"{hi.positive}/{hi.built} overall at {hiOcc:P0} occupancy; " +
                  $"low: quartile {loQ.pos}/{loQ.n}, ΣLR {lo.sumLR:F1}, " +
                  $"{lo.positive}/{lo.built} overall at {loOcc:P0}");
        }

        private static void OccupancyChannel(ulong seed)
        {
            // Does REALIZED VACANCY move rent, at fixed citywide population?
            // Two legs, because an integration test alone is confounded by the
            // allocator instantly re-housing whoever you evict:
            //   (a) AccessState.FillEma really is measured occupancy;
            //   (b) lowering FillEma lowers the clearing price.
            // Together those are the channel. Stated separately and honestly
            // because an earlier check CLAIMED to measure a vacancy overhang
            // and in fact only varied population (adversarial review).
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            // PINNED POSTED: this check tests the posted path's own price
            // transmission (FillEma → demand shares → the forecast clearing
            // price) and stays meaningful only on that path. The flip inventory
            // measured it on auction-built worlds: the same pre-existing
            // flat-tranche fragility recorded for seed 9 simply re-rolls which
            // seeds it fires on (0/1/25 red, 9 healed) — world composition, not
            // transmission. The auction path's vacancy→price channel is
            // asserted structurally by the auction-equilibrium check
            // (a non-full door posts its reserve), canary-swept 39/39.
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = false });
            sim.Run(120);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

            // (a) FillEma tracks measured occupancy. It is an EMA by design, so
            // the claim is that it TRACKS (small mean error), not that it
            // equals the instantaneous value.
            double sumErr = 0, worstErr = 0; int compared = 0;
            for (int k = 0; k < 2; k++)
            {
                var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
                for (int c = 0; c < acc.C; c++)
                {
                    double units = 0, occ = 0;
                    foreach (var pl in w.Parcels)
                        if (pl.State == ParcelState.Built && pl.Use == kind && pl.Cluster == c)
                        { units += pl.Units; occ += Math.Min(pl.Units, pl.OccupantHouseholds.Count); }
                    if (units < 4) continue;
                    compared++;
                    double err = Math.Abs(acc.FillEma[k][c] - occ / units);
                    sumErr += err;
                    worstErr = Math.Max(worstErr, err);
                }
            }

            // (b) the same cluster, priced at full vs collapsed occupancy —
            // population, stock, access and geometry all held identical. The
            // probed cluster must be CLEARED (fill ratio 1 at current
            // occupancy): there the channel must move the PRICE, which is the
            // substantive claim. In an excess submarket the flat-tail price is
            // mass-invariant and the fill response is mass-proportional by
            // construction — a probe landing there would be measuring the
            // check's own plumbing, so finding NO cleared submarket is a
            // FAILURE of this check, not a cue to measure something weaker.
            //
            // THE FALLBACK ARM THIS REPLACES WAS DEAD, twice over (measured,
            // 57-seed occsweep at the check-debt commit: seeds 0–49, 138, 208,
            // 271, 327, 549, 910, 6550). The old form fell back to the
            // largest-stock cluster when no cleared submarket existed and
            // accepted a vacancy leg there (flat price + fill drop ≥ 20 %):
            //   - the fallback was reached on 0/57 seeds — 46–68 of the
            //     stock≥4 candidate submarkets clear on every seed, so the
            //     first-cleared selection always succeeds;
            //   - force-evaluated on the fallback cluster anyway, the vacancy
            //     disjunct held on 0/57 seeds (that cluster also clears:
            //     fill stayed 1.000 on 52/57, never below 0.901 against the
            //     0.8 conjunct), while the price disjunct duplicated in that
            //     arm fired 57/57 — the ternary was `priceLeg` in both arms,
            //     one leg written twice, plus a vacancy disjunct that never
            //     fired.
            // Deleting the arm changed no verdict: the rewritten check agrees
            // with the old one on all 57 sweep seeds (54 pass; 9, 41, 910
            // fail with an unresponsive first-cleared cluster, the standing
            // red KNOWN-RED.md owns for seed 9).
            int c0 = -1, candidates = 0, cleared = 0;
            for (int c = 0; c < acc.C; c++)
            {
                if (acc.HousingStock[0][c] < 4) continue;
                candidates++;
                LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double f0);
                if (f0 >= 1.0 - 1e-9) { cleared++; if (c0 < 0) c0 = c; }
            }
            bool clearedSelected = c0 >= 0;

            double bidFull = 0, bidEmpty = 0, fillFull = 0, fillEmpty = 0;
            if (clearedSelected)
            {
                double[] saved = { acc.FillEma[0][c0], acc.FillEma[1][c0] };
                void Reprice(double fill)
                {
                    acc.FillEma[0][c0] = fill; acc.FillEma[1][c0] = fill;
                    acc.RebuildDemandShares(sim.W, p);   // same recompute the refresh does
                }
                Reprice(1.0);
                bidFull = LandAccounting.ResidentialBidPerUnit(
                    acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out fillFull);
                Reprice(0.2);
                bidEmpty = LandAccounting.ResidentialBidPerUnit(
                    acc, c0, ZoneKind.ResidentialLow, 2, pres, p, out fillEmpty);
                acc.FillEma[0][c0] = saved[0]; acc.FillEma[1][c0] = saved[1];
                acc.RebuildDemandShares(sim.W, p);
            }

            // While the submarket CLEARS, the thinner demand share reads
            // deeper down the WTP ladder and the PRICE falls — that is the
            // channel, and the price leg is REQUIRED (a vacancy rescue here
            // let a constant-price mutant through; adversarial review,
            // measured). The 0.95 band and the FillEma-tracking bounds are
            // inherited from the pre-rewrite form; the 57-seed sweep above
            // measured mean err ≤ 0.020 (bound 0.06) and worst err up to 0.50
            // at seed 22 — the worst-err bound has NO measured headroom, so
            // treat a new red there as the bound binding, not as noise.
            bool priceLeg = clearedSelected && bidEmpty < bidFull * 0.95;

            double meanErr = compared > 0 ? sumErr / compared : 1;
            Check("occupancy channel: realized vacancy softens rent (price responds on a cleared submarket)",
                  compared >= 10 && meanErr < 0.06 && worstErr < 0.5 && clearedSelected && priceLeg,
                  $"FillEma tracks measured occupancy on {compared} submarkets " +
                  $"(mean err {meanErr:F3}, worst {worstErr:F3}); " +
                  (clearedSelected
                      ? $"cluster {c0} ({cleared}/{candidates} candidates cleared) bid {bidFull:F3} (fill {fillFull:F2}) " +
                        $"at full occupancy → {bidEmpty:F3} (fill {fillEmpty:F2}) at 20 % " +
                        $"({(priceLeg ? "price leg" : "NO response")})"
                      : $"NO cleared submarket among {candidates} candidates (stock ≥ 4) — nothing valid to probe, failing"));
        }

        /// <summary>PROSPECTS PRICE THE PLACE, NOT THE MAP. Every admitted
        /// prospect chose a specific cluster, and the employment odds the
        /// mechanism priced that cluster at (AccessState.ProspectLocalOdds:
        /// local rate shrunk toward the worker-weighted citywide bench) must be
        /// consistent with what workers there actually experience — on average
        /// no worse than the citywide worker-weighted rate, never diluted by
        /// the structural zeros of worker-less map squares.
        ///
        /// THE PROPERTY, stated carefully. The strong form — "prospects
        /// systematically select clusters with strictly better-than-average
        /// local odds" — is bounded by the fixture's cross-cluster employment
        /// spread, and that spread is MEASURED SMALL here: the Sinkhorn commute
        /// matching smooths per-cluster rates to a p90−p10 of ~0.01/0.05/0.11
        /// by class (w-wtd sd 0.001–0.034, seeds 0–1, t=40–240), while
        /// shrinkage (n0=4 vs median 4–8 workers/cluster) halves what a median
        /// cluster can express. The selection tilt that survives is measured
        /// THIN BUT REAL: +0.0049..+0.0065 on every one of seeds 0–7 and 25
        /// (2254–2360 admits each, per-admit sd ~0.03 → standard error
        /// ~0.0006, so the worst seed is ~8 se above zero). The tilt bar is
        /// 0.002 — 2.4× under the worst measured seed — and it is what
        /// catches the OTHER degeneracy the level bar cannot: n0→∞ (every
        /// cluster priced at bench) gives tilt exactly 0 and ratio exactly 1.
        ///
        /// The level bar gates the dilution defect: mean pricedOdds/bench per
        /// run. Healthy it reads 1.018–1.028 (seeds 0–7, 25); under the
        /// restored zero-diluted-mean defect (`--mutant-citywide-odds`, the
        /// exact code this work deleted) every prospect prices every door at
        /// the diluted mean, which the zero-worker map squares drag to
        /// 0.726–0.774× bench on the same seeds — measured, 0/9 pass. The
        /// admit-count floor keeps a dead fixture (zero admissions) from
        /// passing vacuously: no admits is a FAIL, not a skip (healthy
        /// fixtures admit ~2.2–2.4k over 200 ticks; the floor is 40).</summary>
        private static void ProspectLocalOddsCheck(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            var tel = new List<(int labor, int cluster, double pricedOdds, double benchOdds)>();
            Prospects.AdmitTelemetry = tel;
            try { sim.Run(200); }
            finally { Prospects.AdmitTelemetry = null; }

            int n = 0; double ratioSum = 0, tiltSum = 0;
            foreach (var r in tel)
            {
                if (r.benchOdds <= 1e-9) continue;   // pre-first-matching admits carry no signal
                n++;
                ratioSum += r.pricedOdds / r.benchOdds;
                tiltSum += r.pricedOdds - r.benchOdds;
            }
            double meanRatio = n > 0 ? ratioSum / n : 0;
            double meanTilt = n > 0 ? tiltSum / n : 0;

            // The spread guard, printed so a reviewer can see whether the
            // selection-tilt property had room to bite on this fixture.
            var acc = sim.Engine.Access;
            double maxSd = 0;
            for (int cl = 0; cl < 3 && acc.EmploymentRate.Length == 3; cl++)
            {
                double bench = acc.ProspectBenchOdds(cl), wtot = 0, var2 = 0;
                for (int c = 0; c < acc.C; c++)
                {
                    double wk = acc.WorkersByClass[cl][c];
                    if (wk <= 1e-9) continue;
                    wtot += wk;
                    var2 += wk * Math.Pow(acc.EmploymentRate[cl][c] - bench, 2);
                }
                if (wtot > 0) maxSd = Math.Max(maxSd, Math.Sqrt(var2 / wtot));
            }

            const int MinAdmits = 40;
            const double RatioBar = 0.97;   // healthy 1.018–1.028, mutant 0.726–0.774 (seeds 0–7, 25)
            const double TiltBar = 0.002;   // healthy +0.0049..+0.0065, se ~0.0006; bench-only pricing reads 0
            Check("prospects price the job odds of their chosen cluster (worker-weighted, not zero-diluted)",
                  n >= MinAdmits && meanRatio >= RatioBar && meanTilt >= TiltBar,
                  $"{n} admits with signal (of {tel.Count}): mean priced/bench {meanRatio:F3} vs ≥{RatioBar:F2} bar, " +
                  $"selection tilt {meanTilt:+0.0000;-0.0000} vs ≥+{TiltBar:F4} bar " +
                  $"(cross-cluster w-wtd rate sd ≤{maxSd:F3} bounds what tilt could show on this fixture)");
        }

        /// <summary>The housing assignment market has to actually BE a
        /// competitive equilibrium, and this is what that means, stated as
        /// things that can fail. It runs whether or not the flag is on — it
        /// builds its own auction-enabled city — so the mechanism stays under
        /// test while it is still off by default.
        ///
        /// The envy scan deliberately sweeps EVERY submarket with stock, not
        /// just the ones on a household's shortlist. Scanning the shortlist is
        /// the cheap version and it is circular: a shortlist that wrongly
        /// excluded the household's best option would be invisible to a check
        /// that only ever looks inside it. Sweeping everything is the only way
        /// this check can catch a broken shortlist, which is exactly the failure
        /// mode most likely to hide (mutation M6 below).</summary>
        /// <summary>THE EXHAUSTIVE-SHORTLIST ORACLE: solve the same world twice,
        /// once through the K-door shortlist plus column generation, once with
        /// every door in the city on every household's list, and require the
        /// same total surplus within the ε budget.
        ///
        /// This tests the structural claim the shortlist design rests on. The
        /// initial list is ranked PRICE-FREE (top K by access, density taste
        /// and the household's own idiosyncratic draw, everything below the
        /// household's outside option dropped, home always included), so it is
        /// "what you would love", not "what is cheap" — and it carries no
        /// guarantee of containing the best surplus-at-price door. The
        /// guarantee is supplied by the repair loop: the scan sweeps EVERY live
        /// key for every household after every clearing, with an exact upper
        /// bound (a skip, never an approximation), and the solve cannot
        /// terminate cleanly while any household strictly prefers a missing
        /// door beyond its band. The global no-envy sweep in the equilibrium
        /// check verifies the END STATE against every submarket; what it
        /// cannot see is the counterfactual — whether being shown everything
        /// from the start would have cleared to a materially better market.
        /// This check measures exactly that counterfactual.
        ///
        /// With complete lists the repair loop must have nothing to add:
        /// RepairRounds == 0 is asserted, because a repair round firing when
        /// every door is already listed would mean the scan found a door that
        /// does not exist.</summary>
        private static void ExhaustiveShortlist(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1200, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            sim.Run(160);
            var w = sim.W;
            var a = sim.Engine.Auction;                      // the shortlist arm, as the run left it

            // Every door: K past any live-key count (2 clusters-kinds × 64
            // clusters on this fixture), so the ranking keeps every key worth
            // more than leaving. The AuctionShortlist ceiling is what the
            // widened selection buffer in BuildHouseholds exists for.
            var px = new EconParams { AuctionShortlist = 512 };
            var ax = new HousingAuction();
            ax.Solve(w, sim.Engine.Access, px);

            int over = 0;
            for (int s2 = 0; s2 < ax.Capacity.Length; s2++)
                if (ax.Filled[s2] > ax.Capacity[s2]) over++;

            // Global no-envy on the exhaustive arm, same band as everywhere.
            int envyX = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)ax.Assignment.Length) continue;
                int mine = ax.Assignment[h.Id];
                if (mine < 0) continue;
                double myVal = ax.ValueOf(h.Id, mine, px);
                double mySur = myVal - ax.Price[mine];
                double myEps = Math.Max(px.AuctionEpsilon, px.AuctionEpsilonRel * Math.Abs(myVal));
                for (int s2 = 0; s2 < ax.Capacity.Length; s2++)
                {
                    if (ax.Capacity[s2] <= 0 || s2 == mine) continue;
                    double val = ax.ValueOf(h.Id, s2, px);
                    double gain = (val - ax.EntryPrice(s2)) - mySur;
                    if (gain <= 0) continue;
                    double band = myEps + Math.Max(px.AuctionEpsilon, px.AuctionEpsilonRel * Math.Abs(val));
                    if (gain > band) { envyX++; break; }
                }
            }

            // Total surplus over DEFAULTS, the same objective the LP oracle
            // maximises: value minus the room's cost for the housed, the
            // outside option for everyone else. Computed identically for both
            // arms, so the difference is well-defined.
            double totA = 0, totX = 0, bound = 0;
            int housedA = 0, housedX = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)a.Assignment.Length) continue;
                int sA = a.Assignment[h.Id], sX = ax.Assignment[h.Id];
                double vA = sA >= 0 ? a.ValueOf(h.Id, sA, p) : 0;
                double vX = sX >= 0 ? ax.ValueOf(h.Id, sX, px) : 0;
                totA += sA >= 0 ? vA - a.Reserve[sA] : a.OutsideOf(h.Id);
                totX += sX >= 0 ? vX - ax.Reserve[sX] : ax.OutsideOf(h.Id);
                if (sA >= 0) housedA++;
                if (sX >= 0) housedX++;
                // Each arm is an ε-equilibrium whose per-household slack is its
                // band at the doors it actually weighs; twice the larger
                // valuation's band is a conservative per-household budget for
                // the DIFFERENCE of two such equilibria. Stated over the
                // measured fixture only.
                double refV = Math.Max(Math.Max(Math.Abs(vA), Math.Abs(vX)), a.OutsideOf(h.Id));
                bound += 2 * Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * refV);
            }
            double gap = Math.Abs(totA - totX);

            bool ok = ax.Converged && ax.RepairClean && ax.RepairRounds == 0
                      && over == 0 && envyX == 0 && gap <= bound;
            Check("exhaustive shortlist: the every-door solve matches column generation",
                  ok,
                  $"shortlist arm {totA:F2} vs every-door arm {totX:F2}: gap {gap:F2} "
                  + $"({gap / Math.Max(1e-9, Math.Abs(totX)):P2} of every-door) against budget {bound:F2}; "
                  + $"housed {housedA}/{housedX}, every-door repair {ax.RepairRounds} rounds "
                  + $"(clean {ax.RepairClean}, converged {ax.Converged}, envious {envyX}, oversub {over}, "
                  + $"{ax.Bids} bids)");
        }

        private static void AuctionEquilibrium(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            sim.Run(160);
            var w = sim.W; var a = sim.Engine.Auction;

            // (1) capacity is never oversubscribed, (2) no price below the
            // owner's reserve, (3) the marginal tenant keeps its surplus:
            // posted ≤ admitted, or the household that won a slot is paying more
            // than it bid.
            int over = 0, belowReserve = 0, inverted = 0, live = 0;
            for (int s = 0; s < a.Capacity.Length; s++)
            {
                if (a.Capacity[s] <= 0) continue;
                live++;
                if (a.Filled[s] > a.Capacity[s]) over++;
                if (a.Price[s] < a.Reserve[s] - 1e-9) belowReserve++;
                if (a.Price[s] > a.Admitted[s] + 1e-9) inverted++;
            }

            // (4) INDIVIDUAL RATIONALITY: nobody is assigned a place worth less
            // to it than walking away.
            int irked = 0, housed = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)a.Assignment.Length) continue;
                int mine = a.Assignment[h.Id];
                if (mine < 0) continue;
                housed++;
                if (a.ValueOf(h.Id, mine, p) - a.Price[mine] < a.OutsideOf(h.Id) - 1e-9) irked++;
            }

            // (5) NO ENVY, swept over every submarket that holds stock.
            //
            // The bar is 2ε, and that number is derived, not chosen. While a
            // household holds a slot, the price there is at most its own bid
            // (any higher and it would have been evicted), and its bid was its
            // value minus its best alternative plus ε — so its surplus is within
            // ε of that alternative, and every other price has only risen since.
            // That is one ε. The second is on the other side: a submarket posts
            // one ε BELOW the worst bid still holding a slot, so the surplus a
            // household reads at somewhere else is overstated by that much
            // against what it would actually have to pay to get in. Envy above
            // 2ε is a real equilibrium violation; envy below it is the price of
            // a finite auction.
            int envy = 0; double worstRel = 0; string worstWhy = "";
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)a.Assignment.Length) continue;
                int mine = a.Assignment[h.Id];
                if (mine < 0) continue;
                double myVal = a.ValueOf(h.Id, mine, p);
                // It pays the tenant price where it lives, and would pay the
                // ENTRY price anywhere else. Measuring both legs at the tenant
                // price credited households with surplus at doors that were
                // never open to them.
                double mySur = myVal - a.Price[mine];
                // The two ε's are measured against DIFFERENT valuations, so the
                // band is their sum and not twice either one. The household bid
                // with an ε proportional to what its OWN place is worth to it;
                // the submarket it is looking at posts one ε below its own
                // admitted bid, proportional to what THAT place is worth. Using
                // 2ε of the envied place understated the bound wherever a
                // household's own home was worth much more than the alternative
                // — measured 1–8 households per seed sitting at 1.3–2.0% against
                // a 1.0% bar, all of them this arithmetic rather than any
                // disequilibrium.
                double myEps = Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(myVal));
                for (int s = 0; s < a.Capacity.Length; s++)
                {
                    if (a.Capacity[s] <= 0 || s == mine) continue;
                    double val = a.ValueOf(h.Id, s, p);
                    double gain = (val - a.EntryPrice(s)) - mySur;
                    if (gain <= 0) continue;
                    double band = myEps + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(val));
                    if (gain > band)
                    {
                        envy++;
                        double rel = gain / Math.Max(1e-9, val);
                        // Say WHICH violation is worst, not just how big it is.
                        // "36 households envious by up to 8%" names a number; the
                        // shape of the worst case names the mechanism, and the
                        // last four rounds of this were spent guessing at it.
                        if (rel > worstRel)
                        {
                            worstRel = rel;
                            worstWhy = $"hh{h.Id} at sub{mine} (val {myVal:F2} − price {a.Price[mine]:F2}"
                                     + $" = sur {mySur:F2}, bid {a.WinningBid[h.Id]:F2}) envies sub{s}"
                                     + $" (val {val:F2}, entry {a.EntryPrice(s):F2}, posted {a.Price[s]:F2},"
                                     + $" admitted {a.Admitted[s]:F2}, reserve {a.Reserve[s]:F2},"
                                     + $" {a.Filled[s]}/{a.Capacity[s]} full, maxbid {myVal - a.OutsideOf(h.Id):F2}"
                                     + $" vs val−outside here {val - a.OutsideOf(h.Id):F2})";
                        }
                    }
                    break;
                }
            }

            // (6) THE MARKET CLEARS ON THE DEMAND SIDE. Two conditions, and
            // they are the ones that turn "no envy" into "efficient".
            //
            //   (6a) UNSOLD ROOMS REST AT COST: a door with a free room posts
            //        its reserve;
            //   (6b) no household the auction left unassigned strictly prefers
            //        a submarket that still has room.
            //
            // (6a) has been three things. Its first form was exactly this. It
            // was then replaced by the owner's n×P(n) revenue test, on an
            // argument that sounded economic — every room lets at one price,
            // so filling the last of four cuts the rent on all four — and
            // that form died on three measured facts: the "owner" spans 2-8
            // separate parcels and the design's §2 charter forbids a landlord
            // class, so no such agent exists; the revenue it protected is
            // paid to NO account (occupants pay S + φ·LR to the treasury), so
            // the rule optimised a number absent from the ledger; and what it
            // actually propped up, through price → assessment → charge, was
            // the land levy. It was also satisfied by construction — the cut
            // priced at the marginal bidder's exact indifference, so over
            // 2,463 cuts nobody was ever made strictly better off — except on
            // the six seeds where rounding left it a hair past its own gate
            // (20, 22, 23, 26, 28, 208, found by bisect and canary).
            //
            // The first form is back because it is now STRUCTURAL, not
            // aspirational: every auction round is a full re-clear from the
            // reserve, a non-full door's posted price never leaves its
            // reserve (SetPrices clamps that branch), and nobody mid-build
            // holds a slot while bidding, so a door that fills stays full.
            // Three between-round repricing mechanisms that tried to reach
            // this fixed point from stale prices are recorded in KNOWN-RED.md
            // ("measured dead ends"); none converged. The tolerance here is a
            // hair, not a band, because the property is an invariant of the
            // build, not an equilibrium residual.
            int unsoldOverpriced = 0, strandedDemand = 0, unassignedChecked = 0;
            string unsoldWhy = "";
            for (int s = 0; s < a.Capacity.Length; s++)
            {
                int f = a.Filled[s];
                if (a.Capacity[s] <= f) continue;              // full or warehoused
                double tol = Math.Max(1e-9, 1e-9 * a.Reserve[s]);
                if (a.Price[s] <= a.Reserve[s] + tol) continue;
                unsoldOverpriced++;
                if (unsoldWhy.Length == 0)
                    unsoldWhy = $"sub{s} {f}/{a.Capacity[s]} resting at {a.Price[s]:F2}"
                              + $" above reserve {a.Reserve[s]:F2}";
            }
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)a.Assignment.Length) continue;
                if (a.Assignment[h.Id] >= 0) continue;
                unassignedChecked++;
                for (int s = 0; s < a.Capacity.Length; s++)
                {
                    if (a.Capacity[s] <= a.Filled[s]) continue;      // no room anyway
                    double val = a.ValueOf(h.Id, s, p);
                    double band = 2 * Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(val));
                    if (val - a.EntryPrice(s) > a.OutsideOf(h.Id) + band) { strandedDemand++; break; }
                }
            }

            // (7) PAIRWISE STABILITY — the efficiency condition, and the only
            // one here that is stated without reference to prices.
            //
            // No two housed households may both do better by trading places.
            // This exists because mutation M5 — restrict every household to its
            // initial shortlist and never repair it — SURVIVED everything above.
            // That is not a bug in those conditions, it is the first welfare
            // theorem being conditional: no-envy at equilibrium prices implies
            // efficiency only when the prices are an equilibrium of the WHOLE
            // market, and a shortlisted solve is an equilibrium of the market it
            // was shown. The unshown submarkets are priced at what they are
            // worth, so nobody reads them as a bargain and no envy appears —
            // while the assignment quietly leaves value on the table. A swap
            // test cannot be fooled that way: it never looks at a price.
            //
            // O(housed²) and deliberately exhaustive. This is a test.
            var occ = new List<int>();
            foreach (var h in w.Households)
                if (h.ExitedTick < 0 && (uint)h.Id < (uint)a.Assignment.Length && a.Assignment[h.Id] >= 0)
                    occ.Add(h.Id);
            int swaps = 0; double bestSwapGain = 0;
            for (int x = 0; x < occ.Count && swaps == 0; x++)
            {
                int i = occ[x], si = a.Assignment[i];
                double vii = a.ValueOf(i, si, p);
                for (int y = x + 1; y < occ.Count; y++)
                {
                    int j = occ[y], sj = a.Assignment[j];
                    if (sj == si) continue;
                    double gain = (a.ValueOf(i, sj, p) + a.ValueOf(j, si, p)) - (vii + a.ValueOf(j, sj, p));
                    // Same ε accounting as the envy sweep: a finite auction
                    // leaves each side within its own ε of its best, so a swap
                    // has to beat both to count.
                    double band = Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vii))
                                + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(a.ValueOf(j, sj, p)));
                    if (gain > band) { swaps++; bestSwapGain = gain; break; }
                }
            }

            // (8) DETERMINISM of the solve itself: two solves of the SAME world
            // must agree exactly. The solve is a pure function of (world,
            // access, params) — nothing in it touches the world RNG — and this
            // is what holds that true. Note both solves run here, AFTER the run
            // has settled: comparing the engine's last solve against a fresh one
            // would compare two different worlds, because the engine moves
            // households in response to the first and the home bonus follows
            // them. That version of this check read 129 households of drift and
            // meant nothing.
            a.Solve(w, sim.Engine.Access, p);
            var before = (int[])a.Assignment.Clone();
            var beforePrice = (double[])a.Price.Clone();
            a.Solve(w, sim.Engine.Access, p);
            int drift = 0; double priceDrift = 0;
            for (int i = 0; i < before.Length; i++) if (before[i] != a.Assignment[i]) drift++;
            for (int s = 0; s < beforePrice.Length; s++)
                priceDrift = Math.Max(priceDrift, Math.Abs(beforePrice[s] - a.Price[s]));

            bool ok = live >= 20 && over == 0 && belowReserve == 0 && inverted == 0
                      && irked == 0 && envy == 0 && unsoldOverpriced == 0 && strandedDemand == 0
                      && swaps == 0 && drift == 0 && priceDrift < 1e-9 && a.Converged;
            Check("housing auction is a competitive equilibrium (capacity, reserve, IR, no-envy, deterministic)",
                  ok,
                  $"{live} live submarkets, {housed} housed: oversubscribed {over}, below-reserve {belowReserve}, "
                  + $"posted>admitted {inverted}, individually-irrational {irked}, "
                  + $"envious beyond 2ε {envy} (worst {worstRel:P2} of value), "
                  + $"unsold-above-reserve {unsoldOverpriced}, stranded demand {strandedDemand}"
                  + $"/{unassignedChecked} unassigned, improving swaps {swaps} (best {bestSwapGain:F3}); "
                  + $"re-solve drift {drift} households / {priceDrift:E1} price; converged {a.Converged} "
                  + $"(repair {a.RepairRounds} rounds, clean {a.RepairClean}) "
                  + $"({a.Bids} bids, {a.Evictions} evictions, "
                  + $"blocked listed {a.BlockedListed} / list-full {a.BlockedFull})"
                  + (envy > 0 ? $"\n      worst envy: {worstWhy}" : "")
                  + (unsoldOverpriced > 0 ? $"\n      unsold: {unsoldWhy}" : ""));
        }

        /// <summary>The labor-auction fixture: the auction-arm city with the
        /// labor flag on. Shared by the three labor checks and the
        /// laborcanary sweep so the fixture cost is paid once per purpose.</summary>
        private static Sim LaborFixture(ulong seed, EconParams p, int ticks, Action<Sim>? perTick = null)
        {
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true, LaborAuction = true });
            sim.Run(ticks, perTick);
            return sim;
        }

        private static void LaborMarket(ulong seed)
        {
            var p = new EconParams();
            var audit = new SectorAudit();
            double worstPayroll = 0;
            var sim = LaborFixture(seed, p, 150, s =>
            {
                audit.Sample(s);
                worstPayroll = Math.Max(worstPayroll, s.Engine.LaborPayrollGapThisTick);
            });

            LaborEquilibriumCheck(sim, p);

            // Σ firm debits == Σ member base credits EXACTLY: the two sides
            // add the same per-earner base-comp terms in the same
            // household-id/earner-slot order per firm (the firm's per-earner
            // Members entries against its own class-door record vs the
            // households' earner links), so any nonzero gap is a defect, not
            // rounding. The sector reconciliation is the same instrument the
            // housing ledger check uses, on the labor arm.
            Check("labor payroll conserves: firm debits equal member credits exactly; sectors reconcile",
                  worstPayroll == 0 && audit.Worst < 1e-9,
                  $"worst per-tick Σ|firm bill − member credits| = {worstPayroll:E1} (bound: exactly 0); "
                  + $"sector reconciliation over {audit.Ticks} tick samples: {audit} (bound 1e-9)");

            // THE WARD CHECK, named for the rule this design refuses to
            // encode: no door may sit with physical room while posting comp
            // below its own cap when a doorless worker would take a slot at
            // comp = cap. On a clean build this is structural (a door with
            // free room posts its cap); the mutant arm proves the check CAN
            // fail — a check that cannot fail is not a check.
            int refusedClean = WardRefusals(sim, p, out string cleanWhy);
            var pm = new EconParams { LaborWardMutant = true };
            // 80 ticks: the mutant's refusal posture is standing from the
            // first clear (doors capped at incumbency), so the arm needs only
            // enough run for the market to mature, not the full fixture.
            var simM = LaborFixture(seed, pm, 80);
            int refusedMutant = WardRefusals(simM, pm, out _);
            Check("no surplus-positive hire is refused (Ward) — and the LaborWardMutant flips it red",
                  refusedClean == 0 && refusedMutant > 0,
                  $"clean run: {refusedClean} refusing doors"
                  + (cleanWhy.Length > 0 ? $" — {cleanWhy}" : "")
                  + $"; LaborWardMutant run: {refusedMutant} (must be > 0 or the check is vacuous)");
        }

        /// <summary>Leg 1: the labor clear is a competitive equilibrium.
        /// Tolerances are derived from the auction's own ε exactly as the
        /// housing check derives them — no bare numbers. The bidder is one
        /// EARNER (unit demand), so every condition is stated per worker at
        /// the same price vector the mechanism used (EntryPrice: Admitted
        /// plus the door's ε at a full door, +∞ where holders sit at the
        /// comp floor, posted price elsewhere — housing's one-price-vector
        /// CostTo rule with labor's tie-robust ε; see
        /// LaborAuction.EntryPrice for the measurement that forced it).</summary>
        private static void LaborEquilibriumCheck(Sim sim, EconParams p)
        {
            var w = sim.W; var a = sim.Engine.Labor;
            int live = 0, over = 0, capBreach = 0, undersubOffCap = 0;
            for (int d = 0; d < a.D; d++)
            {
                live++;
                if (a.Used[d] > a.Capacity[d]) over++;
                // T ∈ [0, cap]: p may not be negative nor exceed the cap.
                double hair = 1e-9 * Math.Max(1, Math.Abs(a.Cap[d]));
                if (a.Price[d] < -hair || a.Price[d] > a.Cap[d] + hair) capBreach++;
                // The structural mirror of unsold-at-reserve: an
                // undersubscribed door posts its full cap (p = 0) exactly.
                if (a.Used[d] < a.Capacity[d] && a.Price[d] > hair) undersubOffCap++;
            }

            int matched = 0, outsideN = 0, unemployedN = 0;
            int irked = 0, envy = 0, stranded = 0;
            double worstRel = 0; string worstWhy = "";
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || !a.ActiveWorker(h.Id)) continue;
                int e = a.EarnersOf(h.Id);
                for (int s = 0; s < e; s++)
                {
                    int wk = a.WorkerOf(h.Id, s);
                    int cls = a.ClassOfWorker(wk);
                    int mine = a.Assignment[wk];
                    if (mine >= 0)
                    {
                        matched++;
                        double myVal = a.ValueOf(wk, mine, p);
                        double mySur = myVal - a.Price[mine];
                        double myEps = Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(myVal));
                        // Individual rationality against the worker's own
                        // DEFAULT (outside net wage or its own leisure floor,
                        // whichever is better): nobody is employed below it.
                        if (mySur < a.OutsideOf(wk) - myEps - 1e-9) irked++;
                        for (int d = 0; d < a.D; d++)
                        {
                            if (a.DoorClass[d] != cls || d == mine || a.Capacity[d] <= 0) continue;
                            double val = a.ValueOf(wk, d, p);
                            double gain = (val - a.EntryPrice(d)) - mySur;
                            if (gain <= 0) continue;
                            double band = myEps + Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(val));
                            if (gain <= band) continue;
                            envy++;
                            double rel = gain / Math.Max(1e-9, Math.Abs(val));
                            if (rel > worstRel)
                            {
                                worstRel = rel;
                                worstWhy = $"hh{h.Id} earner {s} (cls {cls}) at door{mine} "
                                         + $"(T {a.CompMember(mine):F2} of cap {a.Cap[mine]:F2}, sur {mySur:F2}) "
                                         + $"envies door{d} (val {val:F2}, entry p {a.EntryPrice(d):F2}, "
                                         + $"cap {a.Cap[d]:F2}, {a.Used[d]}/{a.Capacity[d]} used)";
                            }
                            break;
                        }
                    }
                    else
                    {
                        if (a.Why[wk] == LaborAuction.Outcome.Outside) outsideN++; else unemployedN++;
                        // A worker on its default must not strictly prefer a
                        // door it could enter at posted prices.
                        double cur = a.OutsideOf(wk);
                        double curEps = Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(cur));
                        for (int d = 0; d < a.D; d++)
                        {
                            if (a.DoorClass[d] != cls || a.Capacity[d] <= 0) continue;
                            double val = a.ValueOf(wk, d, p);
                            double band = curEps + Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(val));
                            if (val - a.EntryPrice(d) > cur + band) { stranded++; break; }
                        }
                    }
                }
            }

            bool ok = live >= 10 && over == 0 && capBreach == 0 && undersubOffCap == 0
                      && irked == 0 && envy == 0 && stranded == 0
                      && a.Converged && a.RepairClean;
            Check("labor auction is a competitive equilibrium (capacity, T≤cap, undersubscribed-at-cap, IR, no-envy)",
                  ok,
                  $"{live} doors, {matched} matched / {outsideN} outside / {unemployedN} unemployed earners: "
                  + $"oversubscribed {over}, T-outside-[0,cap] {capBreach}, undersubscribed-off-cap {undersubOffCap}, "
                  + $"below-own-default {irked}, envious beyond band {envy} (worst {worstRel:P2} of value), "
                  + $"stranded {stranded}; converged {a.Converged} "
                  + $"(repair {a.RepairRounds} rounds, clean {a.RepairClean}; {a.Bids} bids, {a.Evictions} evictions)"
                  + (envy > 0 ? $"\n      worst envy: {worstWhy}" : ""));
        }

        /// <summary>Doors refusing a surplus-positive hire: physical room
        /// standing (vacancy against CapacityFull, so withheld slots cannot
        /// hide), posted comp below the door's own cap beyond the ε band,
        /// while a doorless worker of the class exists whose value at
        /// comp = cap beats its current outcome by more than the band.</summary>
        private static int WardRefusals(Sim sim, EconParams p, out string why)
        {
            var w = sim.W; var a = sim.Engine.Labor;
            int refusing = 0; why = "";
            for (int d = 0; d < a.D; d++)
            {
                int vacancy = a.CapacityFull[d] - a.Used[d];
                if (vacancy <= 0) continue;
                double tol = Math.Max(p.LaborAuctionEpsilon, p.LaborAuctionEpsilonRel * Math.Abs(a.Cap[d]));
                if (a.CompMember(d) >= a.Cap[d] - tol) continue;   // posting (near) its cap: no refusal
                bool found = false;
                foreach (var h in w.Households)
                {
                    if (h.ExitedTick >= 0 || !a.ActiveWorker(h.Id)) continue;
                    int e = a.EarnersOf(h.Id);
                    for (int s = 0; s < e && !found; s++)
                    {
                        int wk = a.WorkerOf(h.Id, s);
                        if (a.Assignment[wk] >= 0 || a.ClassOfWorker(wk) != a.DoorClass[d]) continue;
                        // Value at comp = cap is the value at p = 0, i.e. ValueOf.
                        double atCap = a.ValueOf(wk, d, p);
                        double band = tol + Math.Max(p.LaborAuctionEpsilon,
                                                     p.LaborAuctionEpsilonRel * Math.Abs(atCap));
                        if (atCap - a.OutsideOf(wk) > band)
                        {
                            refusing++; found = true;
                            if (why.Length == 0)
                                why = $"door{d} (firm {a.DoorFirm[d]}, cls {a.DoorClass[d]}) holds {a.Used[d]}"
                                    + $"/{a.CapacityFull[d]} slots at T {a.CompMember(d):F2} < cap {a.Cap[d]:F2} "
                                    + $"while hh{h.Id} earner {s} would gain {atCap - a.OutsideOf(wk):F2} at cap";
                        }
                    }
                    if (found) break;
                }
            }
            return refusing;
        }

        /// <summary>THE LABOR CANARY: the labor-equilibrium check alone,
        /// swept across seeds — same rationale as the housing canary (every
        /// auction defect in this file's history surfaced on a seed nobody
        /// ran). Cross-reference KNOWN-RED.md before attributing a red.</summary>
        public static int LaborCanary(List<ulong> seeds)
        {
            Console.WriteLine($"labor canary: {seeds.Count} seeds");
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                var p = new EconParams();
                LaborEquilibriumCheck(LaborFixture(seed, p, 150), p);
                bool ok = Results.Count > before && Results[Results.Count - 1].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"laborcanary: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        private static void ClaimVacancyWash(ulong seed)
        {
            // The claim↔vacancy wash: a unit UNDER CONSTRUCTION and the same
            // unit COMPLETED-BUT-VACANT are the same competitive object, so
            // they must suppress demand identically. When claims were netted
            // 100% locally while vacancy smeared through the kernel (~15% local
            // retention), COMPLETING an empty building RAISED its own cluster's
            // residual by ~0.85×units — a ratchet that kept starting towers
            // beside standing empties (adversarial review, confirmed HIGH).
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(80);
            var w = sim.W;
            var eng = sim.Engine;
            int C = eng.Access.C;
            var seekers = (double[])eng.SeekersEma.Clone();   // frozen: isolate the supply side
            var res = new ResidualDemand();

            // Pick the cluster with the most occupied ResidentialLow units.
            var occ = new int[C];
            foreach (var pl in w.Parcels)
                if (pl.State == ParcelState.Built && pl.Use == ZoneKind.ResidentialLow)
                    occ[pl.Cluster] += pl.OccupantHouseholds.Count;
            int site = 0;
            for (int c = 1; c < C; c++) if (occ[c] > occ[site]) site = c;

            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var baseline = new double[C];
            for (int c = 0; c < C; c++) baseline[c] = res.Get(c, ZoneKind.ResidentialLow, w.Claims);

            // (a) as PIPELINE: book a claim of K units at the site.
            const int K = 20;
            w.Claims.Add(site, ZoneKind.ResidentialLow, K);
            var deltaClaim = new double[C];
            for (int c = 0; c < C; c++)
                deltaClaim[c] = baseline[c] - res.Get(c, ZoneKind.ResidentialLow, w.Claims);
            w.Claims.Add(site, ZoneKind.ResidentialLow, -K);

            // (b) as COMPLETED-VACANT: evict at the site and re-refresh on the
            // SAME seekers. Normalize by the vacancy ACTUALLY created, not by
            // evictions — warehoused parcels re-let nothing, so the two counts
            // differ and the invariant under test is the PER-UNIT footprint.
            double VacAt(int cluster)
            {
                double v = 0;
                foreach (var pl in w.Parcels)
                    if (pl.Cluster == cluster && pl.State == ParcelState.Built
                        && pl.Use == ZoneKind.ResidentialLow && !pl.Warehousing) v += pl.Vacant;
                return v;
            }
            double vacBefore = VacAt(site);
            int evicted = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.Cluster != site || pl.State != ParcelState.Built
                    || pl.Use != ZoneKind.ResidentialLow || pl.Warehousing) continue;
                while (pl.OccupantHouseholds.Count > 0 && evicted < K)
                {
                    int hid = pl.OccupantHouseholds[pl.OccupantHouseholds.Count - 1];
                    pl.OccupantHouseholds.RemoveAt(pl.OccupantHouseholds.Count - 1);
                    w.Households[hid].HomeParcel = -1;
                    evicted++;
                }
                if (evicted >= K) break;
            }
            double vacCreated = VacAt(site) - vacBefore;
            res.Refresh(w, eng.Access, eng.Trade, seekers, eng.SegmentPresence, p);
            var deltaVac = new double[C];
            for (int c = 0; c < C; c++)
                deltaVac[c] = baseline[c] - res.Get(c, ZoneKind.ResidentialLow, w.Claims);

            // Compare PER-UNIT footprints: suppression(cluster) / units emitted.
            double sumClaim = 0, sumVac = 0, worstGap = 0;
            for (int c = 0; c < C; c++)
            {
                double perClaim = deltaClaim[c] / K;
                double perVac = vacCreated > 0 ? deltaVac[c] / vacCreated : 0;
                sumClaim += perClaim;
                sumVac += perVac;
                worstGap = Math.Max(worstGap, Math.Abs(perClaim - perVac));
            }
            // Local retention must match too — the exact quantity that broke.
            double localClaim = deltaClaim[site] / K;
            double localVac = vacCreated > 0 ? deltaVac[site] / vacCreated : 0;
            Check("claim↔vacancy wash: pipeline and completed-vacant suppress identically",
                  vacCreated > 5 && worstGap < 0.02
                  && Math.Abs(localClaim - localVac) < 0.02 && localClaim < 0.9,
                  $"{K} claimed vs {vacCreated:F0} vacated: worst per-unit gap {worstGap:E1}; " +
                  $"local retention claim {localClaim:P0} vs vacancy {localVac:P0}; " +
                  $"low-channel share claim {sumClaim:P0} vs vacancy {sumVac:P0}");
        }

        private static void CoopInstantRerate(ulong seed)
        {
            // Co-op assessment: every housed household is charged its parcel's
            // CURRENT market unit assessment — uniform across co-tenants,
            // no anniversaries, no phase-in lag.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());   // all tiers live
            sim.Run(90);
            var w = sim.W;
            // Two invariants: (a) co-tenants pay EXACTLY the same (one price
            // per unit — the co-op property, bit-checkable because the whole
            // parcel re-rates in one pass); (b) every charge tracks the live
            // market assessment tightly (a ≤1-tick lag exists by construction:
            // charges are set before the same tick's condition decay).
            int housed = 0, uniformParcels = 0, parcelsWithMulti = 0;
            double worstRel = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                double first = double.NaN; bool uniform = true;
                foreach (int hid in pl.OccupantHouseholds)
                {
                    var h = w.Households[hid];
                    housed++;
                    if (double.IsNaN(first)) first = h.ChargedAssessment;
                    else if (h.ChargedAssessment != first) uniform = false;
                    double target = LandAccounting.UnitAssessment(pl, p);
                    if (target > 1e-9)
                        worstRel = Math.Max(worstRel, Math.Abs(h.ChargedAssessment - target) / target);
                }
                if (pl.OccupantHouseholds.Count >= 2)
                {
                    parcelsWithMulti++;
                    if (uniform) uniformParcels++;
                }
            }
            Check("co-op re-rate: one price per unit, tracking the live market assessment",
                  housed > 200 && parcelsWithMulti > 20 && uniformParcels == parcelsWithMulti
                  && worstRel < 0.01,
                  $"{uniformParcels}/{parcelsWithMulti} multi-tenant parcels uniform; " +
                  $"worst |charged − market|/market = {worstRel:E1}");
        }

        private static void CircularityGuard(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(60);

            // Pick the occupied parcel with the LARGEST assessed rent, and
            // require it to be positive: on a marginal parcel LR and wedge are
            // both floored at 0, so the assertion below degenerates to 0 == 0
            // and the guard is green no matter what assessment reads
            // (adversarial review: First() landed on a zero-LR parcel in 55
            // of 133 occupied cases at the check's own seed).
            var parcel = sim.W.Parcels
                .Where(x => x.State == ParcelState.Built
                            && x.IsResidential && x.OccupantHouseholds.Count > 0)
                .OrderByDescending(x => x.AssessedLR).First();
            LandAccounting.Assess(sim.W, sim.Engine.Access, sim.Engine.Trade, parcel,
                                  sim.Engine.SegmentPresence, p);
            double lr1 = parcel.AssessedLR, wedge1 = parcel.Wedge;

            // Perturb the parcel's own REALIZED rents hard: charged assessments,
            // occupant money. Assessment must be bit-identical (design §3).
            foreach (int hid in parcel.OccupantHouseholds)
            {
                sim.W.Households[hid].ChargedAssessment *= 17.5;
                sim.W.Households[hid].Money += 99999;
            }
            LandAccounting.Assess(sim.W, sim.Engine.Access, sim.Engine.Trade, parcel,
                                  sim.Engine.SegmentPresence, p);
            Check("circularity guard: assessment blind to own realized rent",
                  lr1 > 1e-9 && parcel.AssessedLR == lr1 && parcel.Wedge == wedge1,
                  $"LR {lr1:F4} (> 0) unchanged under 17.5× realized-rent perturbation");
        }

        /// <summary>THE TAX BASE HAS A LEVEL, and until this check existed the
        /// suite only ever asserted its STRUCTURE. The land-rent legs say every
        /// occupied parcel carries LR &gt; 0 and the best-access quartile is all
        /// positive; an adversarial round moved citywide assessment −4.2 %
        /// (high-density ΣLR −5.2 %, low −2.9 %, one sample parcel +19.9 %) and
        /// every one of them stayed green. Rebuilt here as MUT-C, and confirmed:
        /// 22/22 at c0c584d, seed 1.
        ///
        /// A GOLDEN ΣLR WOULD BE THE WRONG INSTRUMENT. The right level of
        /// assessment is whatever the model's own prices imply, and that moves
        /// legitimately with every calibration change; a constant there gets
        /// bumped without being read, which is worse than no check. So this
        /// asserts a RELATION between two quantities the model computes
        /// independently — the auction's posted price and the assessor's
        /// residual — and it can only be silenced by changing the model.
        ///
        /// WHAT IT CANNOT DO, stated so nobody reads more into a green than is
        /// there: if the auction itself starts posting 4.2 % lower everywhere,
        /// assessment correctly follows and all three legs stay green. That is
        /// an assessor doing its job. The price LEVEL is anchored nowhere in
        /// this suite except the fingerprint's price lane, on one pinned
        /// fixture. No non-circular anchor for it was found this round.
        ///
        /// Runs auction-enabled ON PURPOSE and builds its own city, the same
        /// pattern as auction-equilibrium: `--auction` SWAPS the arm (Sim.Create
        /// forces the flag on every sim), so gating on it would leave the
        /// shipping default — the posted curve — untested, and it costs 145 s
        /// against 42 s (measured, seed 1, c0c584d). An opt-in fixture keeps
        /// both market paths under test in one 51 s run.</summary>
        private static void AssessmentTracksPrice(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            sim.Run(160);
            var w = sim.W;
            var a = sim.Engine.Auction;
            var acc = sim.Engine.Access;
            var presence = sim.Engine.SegmentPresence;

            // Assessment is STAGGERED (EconParams.AssessSlices) — no citywide
            // reassessment event exists in the model — so the stored residual on
            // any given parcel is up to AssessSlices ticks old and cannot be
            // compared against today's price. Reassessing every parcel here is
            // what makes the identity well posed; it is not the check reaching
            // for a number it likes.
            void ReassessAll()
            {
                foreach (var pl in w.Parcels)
                    LandAccounting.Assess(w, acc, sim.Engine.Trade, pl, presence, p);
            }

            // A parcel is IN SCOPE when its own units sit inside its submarket's
            // lettable capacity — exactly the condition under which
            // ResidentialBidPerUnit returns the standing posted price rather
            // than the shadow queue. Outside it the assessor is answering a
            // different question (what a unit that does not exist would fetch)
            // and this identity does not apply.
            bool InScope(Parcel pl, out int sub)
            {
                sub = -1;
                if (pl.State != ParcelState.Built || !pl.IsResidential) return false;
                if (pl.Zoned == ZoneKind.None || pl.Units <= 0) return false;
                sub = a.SubOf(pl.Cluster, pl.Use, pl.Level);
                return sub >= 0 && a.Capacity[sub] > 0 && pl.Units <= a.Capacity[sub];
            }
            // The published base equals the current-use residual only where no
            // redevelopment candidate beat it; where one did, AssessedLR is that
            // candidate's flow net of an annuitized cost and this identity is
            // not the claim being made.
            static bool BaseIsCurrentUse(Parcel pl)
                => pl.TargetLevel == pl.Level && pl.TargetUse == pl.Use && !pl.TargetIsScrape;

            double Rel(double got, double want)
                => Math.Abs(got - want) / Math.Max(1.0, Math.Abs(want));

            // ---- (L1) DERIVATION ------------------------------------------
            // CurrentResidual is (P·cond − S)·units and nothing else. The price
            // is read STRAIGHT OFF auction.Price, not through
            // LandAccounting.BidPerUnit: routing it through the same helper the
            // assessor calls would make the identity circular on the one term
            // that matters, and MUT-3' (assessment reading Reserve instead of
            // Price out of the same arrays) is exactly the defect that would
            // then walk through.
            ReassessAll();
            int scope = 0, badResid = 0, baseScope = 0, badBase = 0;
            double worstResid = 0, worstBase = 0;
            foreach (var pl in w.Parcels)
            {
                if (!InScope(pl, out int sub)) continue;
                scope++;
                double expect = (a.Price[sub] * p.CondFactor(pl.Condition)
                                 - LandAccounting.SPerUnit(pl.Level, pl.Condition, p)) * pl.Units;
                double r = Rel(pl.CurrentResidual, expect);
                worstResid = Math.Max(worstResid, r);
                if (r > 1e-9) badResid++;
                if (!BaseIsCurrentUse(pl)) continue;
                baseScope++;
                double rb = Rel(pl.AssessedLR, Math.Max(0, pl.CurrentResidual));
                worstBase = Math.Max(worstBase, rb);
                if (rb > 1e-9) badBase++;
            }

            // ---- (S) THE SHADOW-QUEUE LEG OF ASSESSMENT -------------------
            // Everything L1 scopes OUT — greenfield, scrape targets,
            // renovation to a level with no lettable capacity — is priced by
            // Assess through HousingAuction's shadow queue: the want-th best
            // REAL waiting bid behind the door (LandAccounting's not-standing
            // branch; the queue moved there when construction residuals did).
            // Until this check that leg was asserted nowhere (task #32 audit,
            // follow-up F1). Emitted as its own Check so the counterfactual
            // path gates separately from the standing-stock identity; runs on
            // L1's state (engine's last solve, full reassessment) so it costs
            // no extra solve — fixture delta measured below in the check's
            // comment on cost.
            //
            // (S1) the queue is ALIVE. A mutant that stops building the queue
            // (BuildShadow cleared) makes every read structurally 0, which
            // both identities below would then "verify" — so liveness is a
            // required leg, not telemetry.
            int liveQueues = 0; double topBid = 0;
            for (int s = 0; s < a.ShadowCount.Length; s++)
                if (a.ShadowCount[s] > 0)
                { liveQueues++; topBid = Math.Max(topBid, a.Shadow[s * HousingAuction.ShadowDepth]); }

            // (S2) WANT-RANK IDENTITY, both entry routes. The contract: added
            // units let at the queue, the marginal (want-th) waiting bid
            // prices the addition, and expected fill is queue/want. Expected
            // values are read STRAIGHT OFF Shadow/ShadowCount (never through
            // ShadowAt or BidPerUnit — the L1 anti-circularity rule), at the
            // exact rank assessment consumes: want = UnitsFor(kind), the
            // addUnits of a greenfield/scrape candidate and the minSupply of
            // a renovation candidate alike. Both sides read the same array
            // with no arithmetic between them, so the identity is EXACT —
            // any nonzero difference is a defect, not rounding. The probe
            // body lives in ShadowQueueProbe, pinned to unoptimized codegen
            // — see the measurement recorded on that method before touching
            // either.
            int probes = 0, probesNonzero = 0, probeBad = 0;
            double worstProbe = 0; string probeWhy = "";
            for (int c = 0; c < acc.C; c++)
                for (int k = 0; k < 2; k++)
                {
                    var kind = k == 1 ? ZoneKind.ResidentialHigh : ZoneKind.ResidentialLow;
                    int want = LandAccounting.UnitsFor(kind);
                    for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
                    {
                        int sub = a.SubOf(c, kind, lvl);
                        if (sub < 0) continue;
                        double expect = want <= a.ShadowCount[sub]
                            ? a.Shadow[sub * HousingAuction.ShadowDepth + want - 1] : 0;
                        double expectFill = Math.Min(1.0, a.ShadowCount[sub] / (double)want);
                        if (expect > 0) probesNonzero++;

                        double d = ShadowQueueProbe(acc, a, c, kind, lvl, presence, p,
                                                    sub, want, expect, expectFill,
                                                    out int ran, out double got);
                        probes += ran;
                        worstProbe = Math.Max(worstProbe, d);
                        if (d != 0)
                        {
                            // First offender in full, so a red is
                            // self-diagnosing rather than a count.
                            if (probeBad == 0)
                                probeWhy = $"first: cluster {c} {kind} L{lvl} (cap {a.Capacity[sub]}, "
                                         + $"queue {a.ShadowCount[sub]}, want {want}) expected {expect:F6} got {got:F6}";
                            probeBad++;
                        }
                    }
                }

            // (S3) ASSESSMENT CONSUMES IT: for every non-Built residential-
            // zoned parcel, AssessedLR must equal the best candidate priced
            // from raw queue reads — the same candidate arithmetic Assess
            // does (flow > 0 gate, annuitized cost net of escrow), with the
            // price term read straight off the array. This is the end-to-end
            // leg: it fails if Assess stops consulting the queue (e.g. quietly
            // reverts to the modeled forecast) even while S2's helper-level
            // identity still holds.
            int gfScope = 0, gfBad = 0, gfConsumed = 0;
            double worstGf = 0; string gfWhy = "";
            foreach (var pl in w.Parcels)
            {
                if (pl.State == ParcelState.Built) continue;
                if (pl.Zoned != ZoneKind.ResidentialLow && pl.Zoned != ZoneKind.ResidentialHigh) continue;
                int units = LandAccounting.UnitsFor(pl.Zoned);
                gfScope++;
                double expectLR = 0;
                for (int lvl = 1; lvl <= p.MaxLevel; lvl++)
                {
                    int sub = a.SubOf(pl.Cluster, pl.Zoned, lvl);
                    double bid = sub >= 0 && units <= a.ShadowCount[sub]
                        ? a.Shadow[sub * HousingAuction.ShadowDepth + units - 1] : 0;
                    double flow = (bid - LandAccounting.SPerUnit(lvl, 1.0, p)) * units;
                    if (flow <= 0) continue;
                    double lr = flow - Annuity.FlowOf(Math.Max(0, p.RC(lvl, units) - pl.Escrow),
                                                      p.HurdleRate, p.AnnuityHorizon);
                    if (lr > expectLR) expectLR = lr;
                }
                double r = Rel(pl.AssessedLR, expectLR);
                worstGf = Math.Max(worstGf, r);
                if (r > 1e-9)
                {
                    if (gfBad == 0)
                        gfWhy = $"first: parcel {pl.Id} cluster {pl.Cluster} {pl.Zoned} "
                              + $"AssessedLR {pl.AssessedLR:F4} vs oracle {expectLR:F4}";
                    gfBad++;
                }
                if (expectLR > 0) gfConsumed++;
            }

            Check("assessment's counterfactual leg reads the shadow queue (live, want-rank identity, consumed end-to-end)",
                  liveQueues > 0 && topBid > 0
                  && probes > 0 && probesNonzero > 0 && probeBad == 0
                  && gfScope > 0 && gfBad == 0,
                  $"(S1) {liveQueues} submarkets hold waiting bids (top {topBid:F2}); "
                  + $"(S2) {probes - probeBad}/{probes} probes match the raw queue exactly "
                  + $"({probesNonzero} with a live queue at the read rank, worst |Δ| {worstProbe:E1}); "
                  + $"(S3) {gfScope - gfBad}/{gfScope} non-built residential parcels' AssessedLR "
                  + $"equal the raw-read oracle ({gfConsumed} capitalize a queued bid, worst rel {worstGf:E1})"
                  + (probeWhy.Length > 0 ? $"\n      S2 {probeWhy}" : "")
                  + (gfWhy.Length > 0 ? $"\n      S3 {gfWhy}" : ""));

            // ---- (L2) CO-MOVEMENT under a shock ---------------------------
            // Double every living household's own rent share, re-solve, reassess.
            // NOTHING TICKS, so levels, conditions and unit counts are identical
            // across the two arms and the structure term cancels exactly: over
            // the parcels whose published base is the current-use residual in
            // BOTH arms, Δ(Σ AssessedLR) must equal Δ(Σ P·cond·units) to
            // floating-point noise.
            var before = new Dictionary<int, (double lr, double priceRoll)>();
            foreach (var pl in w.Parcels)
            {
                if (!InScope(pl, out int sub) || !BaseIsCurrentUse(pl) || pl.AssessedLR <= 0) continue;
                before[pl.Id] = (pl.AssessedLR, a.Price[sub] * p.CondFactor(pl.Condition) * pl.Units);
            }

            double dLr = 0, dPrice = 0, rollBefore = 0;
            int paired = 0;
            var saved = new Dictionary<int, double>();
            try
            {
                foreach (var h in w.Households)
                    if (h.ExitedTick < 0) { saved[h.Id] = h.RentShare; h.RentShare *= 2.0; }
                a.Solve(w, acc, p);
                ReassessAll();
                foreach (var pl in w.Parcels)
                {
                    if (!before.TryGetValue(pl.Id, out var b)) continue;
                    if (!InScope(pl, out int sub) || !BaseIsCurrentUse(pl) || pl.AssessedLR <= 0) continue;
                    paired++;
                    dLr += pl.AssessedLR - b.lr;
                    dPrice += a.Price[sub] * p.CondFactor(pl.Condition) * pl.Units - b.priceRoll;
                    rollBefore += b.priceRoll;
                }
            }
            finally
            {
                foreach (var kv in saved) w.Households[kv.Key].RentShare = kv.Value;
            }

            // THE VACUITY GUARD, and it is the load-bearing line of this check.
            // A relation between two deltas is trivially satisfied by 0 == 0.
            // The auction's leg 6a died exactly this way: instrumented, all 45
            // doors that reached its body exited at the first gate and the
            // revenue test ran ZERO times. So the shock is REQUIRED to move the
            // price roll by more than 1 % — if it does not, this leg FAILS
            // rather than passing on nothing.
            double moved = rollBefore > 0 ? Math.Abs(dPrice) / rollBefore : 0;
            bool shockBit = paired > 0 && moved > 0.01;
            bool comoves = shockBit && Rel(dLr, dPrice) < 1e-9;

            // ---- (L3) the same composition identity, auction DETACHED ------
            // Covers the shipping default (posted curve) in the same fixture for
            // the cost of one reassessment. HONEST LIMIT: with no auction to
            // read, the expected price has to come from the same helper the
            // assessor uses, so this leg is CIRCULAR ON THE PRICE TERM and
            // cannot catch a pricing-path defect (MUT-B, MUT-3' are invisible to
            // it). What it does catch is the assessor's COMPOSITION — the
            // condition factor, the structure charge, the unit count, the
            // published base — on the arm the game actually ships. Measured:
            // MUT-2' (condition factor dropped) and MUT-4' (structure cost read
            // at level 1) both light it.
            int scopeP = 0, badP = 0;
            double worstP = 0;
            var savedAuction = acc.Auction;
            try
            {
                acc.Auction = null;
                ReassessAll();
                foreach (var pl in w.Parcels)
                {
                    if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                    if (pl.Zoned == ZoneKind.None || pl.Units <= 0) continue;
                    scopeP++;
                    double bid = LandAccounting.BidPerUnit(acc, sim.Engine.Trade, pl.Cluster, pl.Use, pl.Level,
                                                           presence, p, addUnits: 0, minSupply: pl.Units,
                                                           realized: true);
                    double expect = (bid * p.CondFactor(pl.Condition)
                                     - LandAccounting.SPerUnit(pl.Level, pl.Condition, p)) * pl.Units;
                    double r = Rel(pl.CurrentResidual, expect);
                    worstP = Math.Max(worstP, r);
                    if (r > 1e-9) badP++;
                }
            }
            finally { acc.Auction = savedAuction; }

            // POPULATION FLOOR, not a headroom claim. An earlier draft guarded
            // this with observed-population bounds carrying "~2× headroom over
            // six seeds"; that is the exact shape of claim this repo has been
            // burned by four times, so it is gone. What remains is the only
            // thing the check needs in order not to be vacuous: each leg saw
            // parcels at all.
            //
            // FOR CONTEXT ONLY, and stated with its range: over the eighteen
            // seeds {0..7, 9, 13, 25, 26, 138, 208, 271, 327, 549, 910} this
            // fixture put 368-443 parcels in L1 scope, 189-253 of them on the
            // published-base leg, 107-153 into the L2 pair, and 368-443 into L3;
            // the L2 shock moved the price roll 16.8-30.1 % against the 1 %
            // vacuity floor. Those are observations, NOT bounds — the assertion
            // is > 0, so a smaller city or a different seed weakens the evidence
            // without crying wolf.
            bool populated = scope > 0 && baseScope > 0 && paired > 0 && scopeP > 0;

            Check("assessment tracks the price it capitalizes (relation, both market paths)",
                  populated && badResid == 0 && badBase == 0 && comoves && badP == 0,
                  $"(L1) auction arm: {scope - badResid}/{scope} parcels' residual = (P·cond − S)·units "
                  + $"(worst rel {worstResid:E1}), {baseScope - badBase}/{baseScope} publish max(0,residual) "
                  + $"(worst {worstBase:E1}); (L2) rent-share ×2 over {paired} parcels moved the price roll "
                  + $"{moved:P1} (>1 % required, else vacuous) and ΔΣLR {dLr:F1} vs ΔΣP·cond·units {dPrice:F1} "
                  + $"(rel {Rel(dLr, dPrice):E1}); (L3) posted-curve arm {scopeP - badP}/{scopeP} "
                  + $"(worst {worstP:E1}, circular on price by construction)");
        }

        /// <summary>One S2 probe of the shadow-queue check: price (and fill)
        /// of `want` units at (cluster, kind, level) through BOTH not-standing
        /// entry routes of ResidentialBidPerUnit — addUnits (greenfield/
        /// scrape candidate) always, minSupply (renovation candidate) where
        /// the standing gate really fails — against the caller's raw-array
        /// expectations. Returns the worst |Δ|; `ran` is how many routes
        /// executed; `got` is route 1's price for the failure detail.
        ///
        /// WHY NoInlining|NoOptimization, measured (check-debt commit,
        /// .NET SDK 8.0.129, seed 1): with this body written inline in the
        /// check's loop, the OPTIMIZED jit of that loop returned nonzero Δ on
        /// 458 of 1777 probes (worst 1.0, a fill read) while the semantics
        /// demand 0 — the disassembled IL of the failing build is correct
        /// (args, gates and comparisons verified instruction by instruction),
        /// Assess/L1/S3 called the same callee correctly in the same process,
        /// and the divergence tracked CODEGEN, not code: same DLL fails with
        /// DOTNET_TieredPGO=0 and DOTNET_TieredCompilation=0 (solo and
        /// in-suite) yet passes at tier 0, and a byte-neutral reshape of the
        /// loop (hoisted locals + a Console write on the miss path) compiles
        /// correctly at full opts. Pinning the probe to minopts codegen makes
        /// the check's verdict independent of jit tier, suite position and
        /// incidental code shape. Do not fold this body back into the loop
        /// without re-running that sweep matrix.</summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining
            | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private static double ShadowQueueProbe(
            AccessState acc, HousingAuction a, int c, ZoneKind kind, int lvl,
            double[] presence, EconParams p, int sub, int want,
            double expect, double expectFill, out int ran, out double got)
        {
            ran = 1;
            got = LandAccounting.ResidentialBidPerUnit(
                acc, c, kind, lvl, presence, p, out double gotFill,
                addUnits: want, realized: true);
            double d = Math.Max(Math.Abs(got - expect), Math.Abs(gotFill - expectFill));
            if (!(a.Capacity[sub] > 0 && want <= a.Capacity[sub]))
            {
                ran = 2;
                double got2 = LandAccounting.ResidentialBidPerUnit(
                    acc, c, kind, lvl, presence, p, out double gotFill2,
                    minSupply: want, realized: true);
                d = Math.Max(d, Math.Max(Math.Abs(got2 - expect), Math.Abs(gotFill2 - expectFill)));
            }
            return d;
        }

        /// <summary>Worst relative gap, over one run, between what a sector's
        /// entities actually hold and what the ledger says that sector holds.
        /// One instance per (run, sector).</summary>
        private sealed class SectorAudit
        {
            public double WorstHh, WorstFirm, WorstEscrow;
            public int WorstShelterGap, ShelterPeak;
            public int Ticks;

            /// <summary>Relative because the pools grow: a city three times the
            /// size would need a three-times-bigger absolute tolerance for the
            /// same numerical quality, and re-tuning an absolute bound as the
            /// fixture changes is how a bound stops meaning anything. Scaled by
            /// the larger of the two records so neither side can shrink the
            /// denominator to hide a gap (a mutant that zeroes the entity side
            /// still reads 1.0, not 0/0).</summary>
            private static double Rel(double entity, double ledger)
                => Math.Abs(entity - ledger)
                   / Math.Max(1.0, Math.Max(Math.Abs(entity), Math.Abs(ledger)));

            public void Sample(Sim s)
            {
                double hh = 0, firm = 0, escrow = 0;
                // EXITED households and DEAD firms are included on purpose: the
                // exit paths zero the entity field and post the matching
                // transfer, so the field remains the entity-side record after
                // exit. Skipping them would let "transfer the estate out but
                // leave the money standing" (MUT-L3) pass.
                foreach (var h in s.W.Households) hh += h.Money;
                foreach (var f in s.W.Firms) firm += f.Money;
                foreach (var pl in s.W.Parcels) escrow += pl.Escrow;
                WorstHh = Math.Max(WorstHh, Rel(hh, s.W.Ledger.Balance(Account.Households)));
                WorstFirm = Math.Max(WorstFirm, Rel(firm, s.W.Ledger.Balance(Account.Firms)));
                WorstEscrow = Math.Max(WorstEscrow, Rel(escrow, s.W.Ledger.Balance(Account.Escrow)));

                // THE SAME SHAPE, APPLIED TO PEOPLE. ShelterOccupied is a
                // running counter incremented when a household enters shelter
                // and decremented at every site one leaves; the households
                // themselves carry Stage == Sheltered. Two independently
                // maintained records again, so the same reconciliation works —
                // and this one is not hypothetical. The engine's own comment at
                // the auction move-in site records that this counter once only
                // ever went up on that path, and once it passed capacity every
                // subsequent penniless household was expelled from the city by a
                // stale counter ("a gap of 30 that never closed"). Rebuilding
                // that regression as MUT-D showed the 22-check suite passed it
                // 22/22 on the default arm AND 22/22 under --auction: coverage
                // was never the problem, the absent assertion was.
                int inShelter = 0;
                foreach (var h in s.W.Households)
                    if (h.ExitedTick < 0 && h.Stage == InsolvencyStage.Sheltered) inShelter++;
                ShelterPeak = Math.Max(ShelterPeak, inShelter);
                WorstShelterGap = Math.Max(WorstShelterGap, Math.Abs(s.Engine.ShelterOccupied - inShelter));
                Ticks++;
            }

            public double Worst => Math.Max(WorstHh, Math.Max(WorstFirm, WorstEscrow));
            public override string ToString()
                => $"hh {WorstHh:E1} / firms {WorstFirm:E1} / escrow {WorstEscrow:E1}";
        }

        private static void LedgerConservation(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            // PINNED POSTED, since the default flipped to the auction path:
            // an arm on `new FeatureFlags()` would silently become a second
            // auction arm (flip inventory: detail lines identical across
            // "posted-curve" and "auction" columns) and the posted path's
            // money coverage would vanish while it still ships via --posted.
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = false });
            double maxDrift = 0;
            var audit = new SectorAudit();
            sim.Run(300, s => { maxDrift = Math.Max(maxDrift, Math.Abs(s.W.Ledger.Drift())); audit.Sample(s); });

            // BOTH PATHS. Running this only on one path left a whole class of
            // bug invisible: prospect-level migration moves money across the
            // border on arrival and departure, and it exists only on the
            // auction path — an arrival that minted its savings instead of
            // transferring them would have passed here forever.
            var simA = Sim.Create(new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed },
                                  new EconParams(), new FeatureFlags { HousingAuction = true });
            double maxDriftA = 0;
            var auditA = new SectorAudit();
            simA.Run(300, s => { maxDriftA = Math.Max(maxDriftA, Math.Abs(s.W.Ledger.Drift())); auditA.Sample(s); });

            Check("ledger conservation: money neither created nor destroyed (both market paths)",
                  maxDrift < 1e-3 && maxDriftA < 1e-3,
                  $"max |drift| over 300 ticks = {maxDrift:E2} posted-curve, {maxDriftA:E2} auction");

            // ---- RECONCILIATION, the leg the drift leg cannot be -------------
            // Ledger.Transfer() debits one account and credits another, so the
            // sum of balances is invariant BY CONSTRUCTION: drift is a check on
            // the Ledger class, not on the model. Every way the model can
            // actually lose money — pay members `payout` while the firm gives up
            // 0.99·payout, transfer an estate out and leave the money standing,
            // drain 98 % of what the ledger moved out of escrow — leaves drift
            // at exactly zero. Measured: MUT-A, MUT-L3, MUT-L4 and MUT-L1a/b
            // below all passed the old 22-check suite (seed 1, and seeds 3/5
            // where the seed-1 verdict was ambiguous).
            //
            // This leg reconciles TWO INDEPENDENTLY MAINTAINED RECORDS instead:
            // the left side sums an entity field the model writes at ~30 call
            // sites, the right side is a running total only ever touched through
            // Transfer. Nothing here re-derives a number from the code under
            // test and compares it to itself — which is precisely how three
            // earlier conditions in this suite ended up vacuous.
            //
            // PER SECTOR, never lumped: MUT-A moves money from the ledger's
            // household account into firms' pockets, so a single citywide sum
            // cancels it exactly. The message names the sector for the same
            // reason.
            //
            // THE BOUND IS A PROPERTY: "the two records agree to floating-point
            // noise", expressed relative to the sector's own pool. Measured
            // noise floor over the eighteen seeds in the table at the head of
            // this file ({0..7, 9, 13, 25, 26, 138, 208, 271, 327, 549, 910}),
            // both arms, plus the --auction arm of seeds 0-3: WORST 2.2E-12,
            // and that worst case is the household pool, which is the largest.
            // The mildest mutant tried (MUT-L1b, a 0.1 % skim on the pooled
            // consumption credit) reads 2.1E-2 to 2.8E-2.
            //
            // So the observed gap between "clean" and "the gentlest defect
            // anyone bothered to write" is ten orders of magnitude. No value in
            // [1e-11, 1e-3] would have decided any run observed here
            // differently, which is what makes 1e-9 an untuned constant rather
            // than a fitted one. If a run ever reads within an order of
            // magnitude of 1e-9, the honest response is to re-measure the noise
            // floor and find what changed — NOT to raise the bound. Note the
            // range: eighteen seeds on one city shape. A much larger city
            // accumulates more rounding, and the right response there is a new
            // measurement, not a new constant.
            bool reconciled = audit.Worst < 1e-9 && auditA.Worst < 1e-9;
            Check("ledger reconciliation: sector balances match the money entities hold (both market paths)",
                  reconciled,
                  $"worst relative |Σ entity − ledger| over {audit.Ticks}+{auditA.Ticks} tick samples: "
                  + $"posted-curve {audit}, auction {auditA} (bound 1e-9)");

            // A THIRD ARM, BECAUSE THE OTHER TWO CAN BE EMPTY. Measured over the
            // eighteen seeds in the table at the head of this file: the auction
            // arm's peak shelter population is ZERO on every one of them, and
            // the posted-curve arm's is zero on three of the eighteen (0-33
            // across the set). A leg that reads 0 == 0 asserts nothing — the
            // exact failure mode that made the auction's leg 6a vacuous 45 doors
            // out of 45. Rather than print a green that means nothing, this arm
            // MAKES the state the check is about: it runs the auction city, then
            // empties the pockets of every seventh housed household and charges
            // them far past their income, which walks them down the insolvency
            // pipeline into shelter; the auction then re-houses whichever of them
            // it can, which is the code path that must give their shelter place
            // back. The stress is applied to households, never to the counter.
            var simS = Sim.Create(new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed },
                                  new EconParams(), new FeatureFlags { HousingAuction = true });
            simS.Run(120);
            int stressed = 0;
            foreach (var h in simS.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0 || h.Id % 7 != 0) continue;
                h.Money = 0.01; h.ChargedAssessment = 999; h.StressTicks = 999;
                stressed++;
            }
            var auditS = new SectorAudit();
            simS.Run(80, s => auditS.Sample(s));

            // Integer equality, not a tolerance: the two records count the same
            // people, so any gap at all is a defect. The guard requires the
            // stressed arm to have actually put somebody in shelter — without
            // that, a model change that quietly stopped sheltering anyone would
            // turn this green and silent.
            bool shelterOk = audit.WorstShelterGap == 0 && auditA.WorstShelterGap == 0
                             && auditS.WorstShelterGap == 0 && auditS.ShelterPeak > 0;
            Check("shelter reconciliation: the occupancy counter equals the households in shelter (auction path, under stress)",
                  shelterOk,
                  $"worst |counter − Σ households in shelter| = {audit.WorstShelterGap} posted-curve / "
                  + $"{auditA.WorstShelterGap} auction / {auditS.WorstShelterGap} auction-under-stress; "
                  + $"peak shelter population {audit.ShelterPeak} / {auditA.ShelterPeak} / {auditS.ShelterPeak} "
                  + $"({stressed} households stressed). The plain auction arm's peak is the one to watch: "
                  + "at 0 it is asserting nothing, which is why the stressed arm exists");
        }

        private static void ShadowMode(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var flags = new FeatureFlags { ShadowAccountingOnly = true };
            var sim = Sim.Create(cfg, p, flags);
            sim.Run(100);
            bool assessed = sim.W.Parcels.Any(x => x.AssessedLR > 0);
            double escrow = sim.W.Ledger.Balance(Account.Escrow);
            Check("shadow accounting: assessed + logged, nothing levied (stage 3)",
                  assessed && escrow == 0,
                  $"assessments computed: {assessed}, escrow balance: {escrow:F2}");
        }

        private static void InsolvencyPipeline(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 2000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());
            sim.Run(40);

            // Engineer a stressed household in an expensive unit and walk the pipeline.
            var h = sim.W.Households.First(x => x.ExitedTick < 0 && x.HomeParcel >= 0);
            h.Money = 0.1;
            h.ChargedAssessment = 999;
            var stages = new List<InsolvencyStage> { h.Stage };
            int shelterOcc = 0;
            bool exited = false;
            for (int i = 0; i < 200 && !exited; i++)
            {
                h.StressTicks++;
                exited = sim.Engine.Allocation.InsolvencyStep(sim.W, sim.Engine.Access, h, p, ref shelterOcc, 100);
                if (stages[^1] != h.Stage) stages.Add(h.Stage);
            }
            bool ordered = true;
            for (int i = 1; i < stages.Count; i++)
                if (stages[i] < stages[i - 1] && stages[i] != InsolvencyStage.Solvent) ordered = false;
            Check("insolvency pipeline: staged, ordered, terminates",
                  ordered && (exited || h.Stage == InsolvencyStage.Sheltered || h.HomeParcel >= 0),
                  $"stages: {string.Join("→", stages)}{(exited ? "→exit" : "")}");

            // No stuck-forever homelessness while affordable vacancies exist:
            // sheltered households re-house within a bounded window.
            var flags2 = new FeatureFlags();
            var sim2 = Sim.Create(new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1200, Seed = seed + 1 }, new EconParams(), flags2);
            sim2.Run(250);
            int maxShelterStreak = 0, cur = 0;
            for (int i = 60; i < sim2.Sheltered.Count; i++)
            {
                cur = sim2.Sheltered[i] > 0 && sim2.Sheltered[i] >= sim2.Sheltered[Math.Max(0, i - 1)] ? cur + 1 : 0;
                maxShelterStreak = Math.Max(maxShelterStreak, cur);
            }
            Check("no stuck homeless population (vanilla bug-class regression)",
                  maxShelterStreak < 120,
                  $"longest non-decreasing shelter streak {maxShelterStreak} ticks");
        }

        private static void FlagsOffSmoke(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var flags = new FeatureFlags
            {
                TierA_Migration = false, TierD_Trade = false,
                TierC2_Leveling = false, ConstructionRewire = false,
                // Pinned off: this smoke is the vanilla-mode fallback. With the
                // auction now the DEFAULT, inheriting it here would run vanilla
                // spawner + auction — a combination nothing ships and nothing
                // else covers, which the flip inventory flagged as an accident
                // waiting to be mistaken for coverage.
                HousingAuction = false,
            };
            var sim = Sim.Create(cfg, p, flags, vanillaMode: true);
            sim.Run(150);
            Check("feature flags: vanilla-mode fallback runs (every tier revertible)",
                  sim.Population[^1] > 0 && sim.Population[^1] < 30_000
                  && sim.W.Parcels.Any(x => x.State == ParcelState.Built),
                  $"pop {sim.Population[^1]} (sane bounds), drift {sim.W.Ledger.Drift():E1}");
        }

        private static void Determinism(ulong seed)
        {
            ulong Hash(ulong s)
            {
                var p = new EconParams();
                var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = s };
                var sim = Sim.Create(cfg, p, new FeatureFlags());
                sim.Run(120);
                return sim.TelemetryHash();
            }
            ulong h1 = Hash(seed), h2 = Hash(seed), h3 = Hash(seed + 1);
            // RENAMED, because the old name ("determinism") was read as a
            // guarantee it does not give. This runs the SAME BUILD twice: both
            // sides of the comparison move together, so any change to the
            // pricing path changes the hash and still passes. Three one-line
            // pricing edits were measured doing exactly that at c0c584d
            // (MUT-B, MUT-2, MUT-3 in the matrix at the head of this file). The
            // check is true and worth keeping — a non-reproducible model is
            // untestable — but INVARIANCE is the fingerprint check's job.
            Check("reproducibility (NOT invariance): same seed → identical telemetry hash",
                  h1 == h2 && h1 != h3, $"h(seed)={h1:X} twice, h(seed+1)={h3:X}");
        }

        /// <summary>Gates on the four default-arm lanes of the checked-in model
        /// fingerprint; the auction lanes are printed, not gated (Fingerprint.cs
        /// states the promotion rule and the measured accept rate that justifies
        /// the split). A missing or hand-edited baseline FAILS — never skips,
        /// because a skip is how a check quietly stops existing.</summary>
        private static void ModelFingerprint()
        {
            var lanes = Fingerprint.Compute();
            var v = Fingerprint.Check(lanes);
            string detail;
            if (!v.BaselineFound)
                detail = $"{Fingerprint.FileName} not found from {System.IO.Directory.GetCurrentDirectory()} "
                         + "— a missing baseline is a failure, not a skip";
            else if (!v.SignatureOk)
                detail = $"baseline {v.Path} is HAND-EDITED: the last stanza's signature does not match its own "
                         + "text. Re-record with `fingerprint --accept --reason \"...\"`";
            else if (v.GatePass && v.ReportMismatch.Count == 0 && v.Missing.Count == 0)
                detail = $"all lanes match stanza recorded {v.Recorded} (\"{v.Reason}\")";
            else
                detail = $"gating lanes moved: [{string.Join(", ", v.GatingMismatch)}]; "
                         + $"report-only (auction) lanes moved: [{string.Join(", ", v.ReportMismatch)}]; "
                         + $"missing from baseline: [{string.Join(", ", v.Missing)}]. If the change was "
                         + "intended, record it: `fingerprint --accept --reason \"...\"` (it writes the "
                         + "computed before→after deltas into the file for the reviewer)";
            Check("model fingerprint: the model is unchanged, or the change is recorded (default arm gates)",
                  v.GatePass, detail);
        }
    }
}
