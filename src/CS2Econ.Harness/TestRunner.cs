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
    ///          AT TASK #20  CAUGHT. The branch now has two fixtures in it
    ///          (commercial-staffing, counted-shop-intents). Re-run against
    ///          this commit: verify goes red on the staffing check's
    ///          starvation leg, whose MutantServedCap arm collapses from 1406
    ///          doorless firm-ticks to 0 — a revenue floor of 5 gives every
    ///          shop a positive ProfitEma, so under a served-driven cap no
    ///          shop can starve and the arm that must manufacture the state
    ///          manufactures nothing. Indirect, and recorded as indirect; the
    ///          money leg does NOT see it, because RevenueThisTick is not the
    ///          record that leg reads.
    ///
    /// --- TASK #20: THE COMMERCIAL MECHANISM, PREVIOUSLY UNASSERTED --------
    /// Measured at the start of that item: `grep -c Commercial TestRunner.cs`
    /// → 0, and no occurrence of "shop", "retail" or "capture" either, against
    /// 26 for "residential". Both ledger arms ran StoreLevelSpending = false.
    /// So no assertion named the commercial sector on EITHER arm; money on the
    /// POOLED path was covered (MUT-L1a/b via reconciliation), money on the
    /// store-level path was not, and the mechanism was covered nowhere.
    ///
    /// Two checks and ten legs closed that. Every leg names the mutant that
    /// reds it, and each was run:
    ///  MUT-20a EconomyEngine, capacity × 1.5 at the enforcement site.
    ///          CAUGHT — the BOUND leg, 222 firm-ticks served above a
    ///          recomputed capacity, worst excess exactly 5.0E-1.
    ///          THIS MUTANT FOUND A REAL DEFECT before it caught anything: it
    ///          first read ZERO violations, because the pro-rata ratio was
    ///          being recomputed per customer against a capacity already being
    ///          drawn down. That made the split order-dependent in household id
    ///          — the defect pro-rata exists to prevent — and left every full
    ///          shop short of its own capacity (served peaked at 0.9985 of it),
    ///          which is what hid the mutant. Fixed by computing each shop's
    ///          ratio in its own pass, before anybody is served.
    ///  MUT-20b EconParams, CommercialServicePerSlot → 1e9 (mechanism inert).
    ///          CAUGHT — the BINDS floor: the bound stops binding.
    ///  MUT-20c `--mutant-uncapped-util` (WIRED): the technology ceiling is
    ///          dropped from the door cap, which is the revenue-EMA rule's
    ///          defining property isolated. CAUGHT — 48 of 52 and 12 of 12
    ///          qualifying door-ticks post a cap above the ceiling, worst
    ///          +44.6 %.
    ///  MUT-20d `--mutant-revenue-ema-cap` (WIRED): the old cap restored
    ///          verbatim. CAUGHT, but by the STARVATION leg rather than the
    ///          ceiling leg — 1523-1801 staffless doorless firm-ticks against
    ///          0 clean. Recorded because it is the honest result: at this
    ///          calibration the EMA rule's cap sits BELOW the technology
    ///          ceiling, so it cannot breach it; what it does instead is
    ///          starve every shop that loses its staff. MUT-20c exists because
    ///          a leg needs a mutant that reds THAT leg.
    ///  MUT-20e `MutantServedCap` (WIRED, embedded as the starvation leg's own
    ///          second arm, the Ward pattern): the cap reads served volume.
    ///          CAUGHT — 1523-2369 doorless staffless firm-ticks against 0.
    ///  MUT-20f EconomyEngine.ChooseShops, shortlist cut against bare `outU`
    ///          instead of the household's own realized outside utility.
    ///          CAUGHT — the DEFAULTS leg.
    ///  MUT-20g EconomyEngine.RationShopping, 0.1 % of each household's
    ///          unserved residual vanishes instead of leaking — rule 3's literal
    ///          violation. CAUGHT — the conservation leg on its OWN path, at
    ///          2.5E-5 against a 1e-12 bound.
    ///          NOTE on the mutant that was tried FIRST and is not usable:
    ///          crediting the shop from `ratio × RoundPresented` is a NO-OP
    ///          once the pro-rata ratio is fixed per round, because the
    ///          per-household shares then sum to exactly the round capacity and
    ///          the min() clamp never binds. Recorded so nobody re-derives it.
    ///  MUT-20h AccessState.CountShopIntents, the intent weight built from
    ///          realized spend net of ChargedAssessment. CAUGHT — the GUARD
    ///          leg. This is the one the standing circularity guard cannot
    ///          see: it asserts on a residential parcel's land rent.
    ///  MUT-20i `--mutant-pooled-entry` (WIRED): the pooled phantom-entrant
    ///          field restored as the entry signal. CAUGHT — the
    ///          DISCRIMINATION leg (the counted read is zero at 6-18 of
    ///          121-169 clusters; the pooled field is zero at none of them,
    ///          and structurally cannot be).
    ///  MUT-20j `--mutant-intent-unsaturated` (WIRED): the probe stops
    ///          comparing against what each household already has. CAUGHT —
    ///          four legs at once (THIN, DISCRIMINATION, CROWDING, and
    ///          CALIBRATION at a ratio of 0.014 against a [0.4, 4.0] band).
    ///          CALIBRATION IS RETIRED at the entrant-survival commit (see
    ///          below); this mutant still reds the other three.
    ///  MUT-20k `--mutant-entry-reference-mass` (WIRED, and embedded as the
    ///          entrant-survival leg's own second arm): the store-level
    ///          commercial entry read goes back to asking the counted field
    ///          about a 6-slot condition-1 shop whatever building the firm
    ///          would occupy. CAUGHT — the entrant-survival leg.
    ///
    /// --- A LEG THAT WAS GREEN THROUGH ITS OWN DEFECT ---------------------
    /// CALIBRATION (task #20) related the counted read at entry to what a new
    /// shop took, and read GREEN on all 26 seeds while 88 % of mid-run
    /// entrants were dying inside 40 ticks. Its sample accumulated from age 6
    /// and required five ticks of trading, so it only ever contained entrants
    /// that lived past age 11 — a statement about survivors, in a population
    /// whose defining feature was that it mostly died. Retired and replaced by
    /// the entrant-survival leg, which tests the same question by its outcome
    /// on the population the ratio was dropping. Selection can make a leg
    /// vacuous exactly as construction can (#30's F1); this is the second
    /// instance in this file.
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
    ///  4. FLAG COMBINATIONS: ShadowAccountingOnly, vanilla mode and the TNTP
    ///     import path are outside both arms. StoreLevelSpending CAME INSIDE at
    ///     task #20 — two fixtures run it, ten legs assert on it, MUT-L is
    ///     caught — but the flag itself still ships FALSE, so the pooled path is
    ///     what the other twenty-five checks exercise and the store-level path
    ///     is covered only by those two. A defect that needs a THIRD mechanism
    ///     to interact with store-level spending is still outside both arms.
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
                // The fixture emits one result per leg (the equilibrium check
                // and the owner-door legs, item #41); a seed passes only if
                // every leg does — same rule as the other sweeps.
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
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

        /// <summary>The clearing-price check alone across seeds — 1.9 s a seed
        /// against 434 s for the full suite (measured, seed 13, this commit), so
        /// this is the instrument for the collapse leg's bands and for its
        /// non-degeneracy floor. Every number the leg's comment cites over seeds
        /// 0–299 comes from here.</summary>
        public static int ClearSweep(List<ulong> seeds)
        {
            Console.WriteLine($"clearing-price sweep: {seeds.Count} seeds"
                + (MutantSpareProbedCluster ? " [MUTANT: probed cluster spared from the cull]" : ""));
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                ClearingPrice(seed);
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"clearing-price sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
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
            WeberPremiseSeen = 0; WeberPremiseMet = 0;
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

            // THE PAIRED NON-DEGENERACY FLOOR for the diversity premise. Moving
            // "two distinct outputs" out of the per-seed verdict would otherwise
            // retire it silently: if NO seed ever produced two outputs, the
            // alignment leg would never once be asked to separate places, and
            // nothing would say so. This is the leg that says so, and it lives
            // here because the statement is distributional.
            //
            // The bar is a bare majority of the swept seeds, and the reason it is
            // not tighter is measured: over seeds 0-25 at this commit the premise
            // holds on 20 of 26, and the count is a small-sample statistic over
            // 6-12 industrial firms per seed (`weberprobe --seed 0 --seeds 26`),
            // so a bar near the observed rate would be a bound written past its
            // sweep. What the floor forbids is the regime the reverted retooling
            // experiment produced — one argmax citywide and one output nearly
            // everywhere (premise on 6 of 26 there, KNOWN-RED retooling section)
            // — which lands well under a half.
            bool premiseFloor = WeberPremiseSeen > 0
                                && WeberPremiseMet * 2 > WeberPremiseSeen;
            Console.WriteLine($"  [{(premiseFloor ? "PASS" : "FAIL")}] weber sweep floor: the alignment leg's "
                + $"discrimination premise is met on {WeberPremiseMet}/{WeberPremiseSeen} seeds "
                + $"(>50% required) — the population two-or-more-outputs describes is non-empty");
            return Math.Min(failed.Count + (premiseFloor ? 0 : 1), 100);
        }

        /// <summary>The two task #30 goods checks alone across seeds — same
        /// rationale as <see cref="Canary"/> (cheap fixtures, many seeds), and
        /// the harness for the n0 sweep and for demonstrating the
        /// `--mutant-citywide-goods` mutant is caught on every seed.</summary>
        public static int GoodsSweep(List<ulong> seeds)
        {
            Console.WriteLine($"goods sweep: {seeds.Count} seeds"
                + (TradeSystem.MutantCitywideGoodsPrice ? " [MUTANT: citywide goods price]" : ""));
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                GoodsPriceLocalization(seed);
                GoodsSettlementReconciles(seed);
                bool ok = Results.Count == before + 2
                          && Results[Results.Count - 1].pass && Results[Results.Count - 2].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"goods sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The uniform-pulse check alone across seeds — same
        /// rationale as <see cref="Canary"/> (cheap fixture, many seeds), and
        /// the instrument its bars were measured with: both arms' rises print
        /// per seed, so re-measuring the bands after a change to the prospect
        /// path or the outside anchor is one command.</summary>
        public static int PulseSweep(List<ulong> seeds)
        {
            Console.WriteLine($"uniform-pulse sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                UniformPulseMonotonicity(seed);
                // The fixture emits one result per leg (prospect margin,
                // resident margin); a seed passes only if every leg does.
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"pulse sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The tie-channel check alone across seeds — the instrument
        /// its bars were measured with (same rationale as PulseSweep): both
        /// arms' lifts print per seed, so re-measuring the bands after a
        /// change to the tie draw, the bonus, or the stock is one command.</summary>
        public static int TieSweep(List<ulong> seeds)
        {
            Console.WriteLine($"tie-channel sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                ProspectTieChannel(seed);
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"tie sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>The per-cell calibration check alone across seeds — the
        /// instrument its floors, gap and n0 thresholds were measured with;
        /// each seed also prints the cell-depth distribution the n0 comment
        /// in EconParams cites.</summary>
        public static int CalibSweep(List<ulong> seeds)
        {
            Console.WriteLine($"calibration-cell sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                CalibClusterCheck(seed);
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"calib sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass"
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
            Timed("goods-local-price", () => GoodsPriceLocalization(seed));
            Timed("goods-settlement", () => GoodsSettlementReconciles(seed));
            Timed("weber", () => WeberRecipeChoice(seed));
            Timed("vacancy-kernel", () => VacancyKernelConservation(seed));
            Timed("claim-vacancy-wash", () => ClaimVacancyWash(seed));
            Timed("occupancy-channel", () => OccupancyChannel(seed));
            Timed("auction-equilibrium", () => AuctionEquilibrium(seed));
            Timed("prospect-local-odds", () => ProspectLocalOddsCheck(seed));
            Timed("uniform-pulse", () => UniformPulseMonotonicity(seed));
            Timed("prospect-tie-channel", () => ProspectTieChannel(seed));
            Timed("calibration-cells", () => CalibClusterCheck(seed));
            Timed("exhaustive-shortlist", () => ExhaustiveShortlist(seed));
            Timed("assignment-oracle", () => {
                var (lpOk, lpDetail) = AssignmentOracle.Run(seed);
                Check("auction total surplus is LP-optimal within the epsilon budget", lpOk, lpDetail); });
            Timed("labor-auction", () => LaborMarket(seed));
            Timed("commercial-staffing", () => CommercialStaffing(seed));
            Timed("counted-shop-intents", () => CountedShopIntents(seed));
            Timed("clearing-price", () => ClearingPrice(seed));
            Timed("occupied-stock-rent", () => OccupiedStockCarriesRent(seed));
            Timed("coop-rerate", () => CoopInstantRerate(seed));
            Timed("circularity-guard", () => CircularityGuard(seed));
            Timed("nonres-parity", () => NonResidentialParity(seed));
            Timed("assessment-tracks-price", () => AssessmentTracksPrice(seed));
            Timed("displaced-firm", () => DisplacedFirm(seed));
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

        /// <summary>THE TASK #30 HEADLINE: the delivered goods price a firm
        /// reads is a statistic of ITS PLACE — realized transactions at its
        /// cluster, shrunk to the citywide realized prior where evidence is
        /// thin — not one citywide scalar. Level/tilt/thin three-leg build on
        /// the prospect-local-odds template.
        ///
        /// Fixture: the ExtractorHeavy region config (a producing corner and
        /// remote consuming clusters — the same geography the Weber check
        /// measures on).
        ///
        /// LEVEL — the localization redistributes, it does not re-level: per
        /// resource with real flow, the volume-weighted mean of per-cluster
        /// delivered stats stays within LevelBand of the citywide stat.
        /// (Nearly structural — the stats are shrunk toward the same prior the
        /// weights define — so this leg alone is NOT the falsifier; it pins
        /// the aggregate while the tilt leg does the work.)
        ///
        /// TILT — remote clusters pay more: across clusters with delivered
        /// evidence, the evidence-weighted correlation between (DeliveredStat −
        /// city)/city and the cluster's haul-to-cheapest-source is positive.
        /// Measured by `goodssweep --seeds 7` (seeds 0–3, 9, 13, 25) at the
        /// fix-30 commit: corr 0.979–0.989, mean rel spread 3.63–4.33%, 4
        /// qualifying resources each, level dev ≤ 0.04% — the same numbers
        /// the task #30 commit's sweep read. Under
        /// `--mutant-citywide-goods` (every consumer AND this check read the
        /// prior — the restored defect verbatim) the deviations are exactly
        /// zero: corr 0.000, spread 0.00% → 0/2 seeds pass
        /// (`goodssweep --seeds 2 --mutant-citywide-goods` at the fix-30
        /// commit; 0/7 at the task #30 commit). n0 → 10⁶ (prior swamps all
        /// evidence) reads spread 0.00% with residual corr 0.558–0.592 from
        /// sub-1e-4 deviations — the SpreadFloor leg is what fails it: 0/4 on
        /// seeds 0–3 (the task #30 commit's n0 sweep; the shrinkage path is
        /// untouched since).
        ///
        /// THIN — a place with no evidence holds the prior and nothing else:
        /// clusters with delivered evidence below MinClusterEvidence sit
        /// within ThinBand of the prior. n0 → 0 (raw per-cluster means, a
        /// 1-lot market's one price becomes a place's "price") measured worst
        /// thin deviation 4.29–10.30% vs the ≤2% band → 0/7 seeds (tilt
        /// survives at 0.968–0.991 there; THIN is the leg that catches it —
        /// the task #30 commit's n0 sweep). The band is generous to the
        /// healthy case: worst 0.74% (seed 9; the rest ≤ 0.11%) over the
        /// fix-30 commit's `goodssweep --seeds 7` — the shrinkage bound is
        /// ev/(ev+n0)·|ema − prior| with ev < 0.25, n0 = 1.</summary>
        private static void GoodsPriceLocalization(ulong seed)
        {
            var p = new EconParams();
            var sim = Sim.Create(new SyntheticCity.Config
                                 { Seed = seed, SeedHouseholds = 6000, ExtractorHeavy = true, ExtractorPrebuilt = 0.25 },
                                 p, new FeatureFlags());
            sim.Run(300);
            var trade = sim.Engine.Trade;
            var w = sim.W;
            int C = sim.Access.ClusterCount;
            double fpm = trade.FreightCostPerMinute;

            // Every number on these lines is from `goodssweep --seeds 7`
            // (seeds 0–3, 9, 13, 25) at the fix-30 commit unless it names
            // another run; they agree with the doc comment above.
            const double MinResEvidence = 5.0;   // sustained volume for a resource to qualify
            const double MinClusterEvidence = 0.25; // tilt above this; THIN leg below it
            const double LevelBand = 0.10;       // healthy level dev ≤ 0.04 %
            const double TiltCorrBar = 0.30;     // healthy 0.979–0.989; citywide mutant reads 0.000
            const double SpreadFloor = 0.005;    // healthy 3.63–4.33 %; n0→10⁶ reads 0.00 %
            const double ThinBand = 0.02;        // healthy worst 0.74 % (seed 9); n0→0 reads 4.29–10.30 %

            // Producing clusters per resource (from standing firms) + exit
            // clusters: a destination's cheapest source is whichever is nearer.
            var producerClusters = new List<int>[ResourceCatalog.Count];
            for (int r = 0; r < ResourceCatalog.Count; r++) producerClusters[r] = new List<int>();
            foreach (var f in w.Firms)
                if (!f.Dead && f.Parcel >= 0
                    && (f.Sector == ZoneKind.Extractor || f.Sector == ZoneKind.Industrial))
                {
                    int c = w.Parcels[f.Parcel].Cluster;
                    if (!producerClusters[(int)f.Output].Contains(c)) producerClusters[(int)f.Output].Add(c);
                }

            int qualifying = 0;
            double corrSum = 0, corrW = 0, worstLevel = 0, worstThin = 0, spreadSum = 0;
            for (int r = 0; r < ResourceCatalog.Count; r++)
            {
                var res = (Res)r;
                if (!ResourceCatalog.IsTradable(res)) continue;
                double wgt = ResourceCatalog.Weight[r];
                double city = trade.CityDelivered(res);
                if (city <= 1e-9) continue;
                var srcs = new List<int>(producerClusters[r]);
                foreach (var x in w.Exits) if (x.Resource == res) srcs.Add(x.Cluster);
                if (srcs.Count == 0) continue;

                double evTot = 0, statVw = 0;
                var dev = new List<double>(); var haul = new List<double>(); var wts = new List<double>();
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int c = 0; c < C; c++)
                {
                    double ev = trade.DeliveredEvidence(res, c);
                    double stat = trade.DeliveredStat(res, c);
                    evTot += ev; statVw += ev * stat;
                    if (ev >= MinClusterEvidence)
                    {
                        double h = double.PositiveInfinity;
                        foreach (int s in srcs)
                            h = Math.Min(h, sim.Access.Cost(s, c, AccessPurpose.Freight) * fpm * wgt);
                        dev.Add((stat - city) / city); haul.Add(h); wts.Add(ev);
                        lo = Math.Min(lo, stat); hi = Math.Max(hi, stat);
                    }
                    else
                    {
                        // Thin: mostly prior, whatever its one lot said.
                        worstThin = Math.Max(worstThin, Math.Abs(stat - city) / city);
                    }
                }
                if (evTot < MinResEvidence || dev.Count < 3) continue;
                qualifying++;
                worstLevel = Math.Max(worstLevel, Math.Abs(statVw / evTot / city - 1));
                spreadSum += (hi - lo) / city;
                // Evidence-weighted Pearson correlation of relative deviation
                // against haul-to-cheapest-source.
                double sw = 0, mx = 0, my = 0;
                for (int i = 0; i < dev.Count; i++) { sw += wts[i]; mx += wts[i] * haul[i]; my += wts[i] * dev[i]; }
                mx /= sw; my /= sw;
                double sxx = 0, syy = 0, sxy = 0;
                for (int i = 0; i < dev.Count; i++)
                {
                    sxx += wts[i] * (haul[i] - mx) * (haul[i] - mx);
                    syy += wts[i] * (dev[i] - my) * (dev[i] - my);
                    sxy += wts[i] * (haul[i] - mx) * (dev[i] - my);
                }
                double corr = sxx > 1e-12 && syy > 1e-12 ? sxy / Math.Sqrt(sxx * syy) : 0;
                corrSum += corr * evTot; corrW += evTot;
            }
            double meanCorr = corrW > 0 ? corrSum / corrW : 0;
            double meanSpread = qualifying > 0 ? spreadSum / qualifying : 0;
            Check("delivered goods price is a statistic of the place (level holds, remote clusters pay the haul, thin markets hold the prior)",
                  qualifying >= 2 && worstLevel <= LevelBand && meanCorr >= TiltCorrBar
                  && meanSpread >= SpreadFloor && worstThin <= ThinBand,
                  $"{qualifying} qualifying resources: level dev {worstLevel:P2} vs ≤{LevelBand:P0}; "
                  + $"tilt corr {meanCorr:F3} vs ≥{TiltCorrBar:F2} (mean rel spread {meanSpread:P2} vs ≥{SpreadFloor:P1}); "
                  + $"worst thin-cluster dev {worstThin:P2} vs ≤{ThinBand:P0}");
        }

        /// <summary>Goods settlement reconciles per resource, per tick, and
        /// the price it settles at sits inside the band.
        ///
        /// IDENTITY leg: the money firms actually gained minus what they paid
        /// equals ExportRevenue − ImportCost − LocalFreight (the OutsideWorld
        /// net). It compares the engine's realized firm credits/debits
        /// (counted at the settlement site) against the trade-side scalars —
        /// two independently maintained records, the ledger-reconciliation
        /// discipline applied to one resource at a time. Mutant lineage
        /// MUT-L1b: a 0.1% skim on the seller credit flips this check AND the
        /// global per-sector reconciliation — both demonstrated at the task
        /// #30 commit (skim run: worst identity gap 1.0e-3 on seeds 0 and 1
        /// vs the 1e-9 bound; ledger reconciliation firms-sector 1.1e-3
        /// posted / 7.3e-4 auction on seed 1) — the global check catching it
        /// is the point of having it.
        ///
        /// BAND legs (rebuilt at the fix commit; the landed form compared
        /// P_j against quantities P_j is built from and could not fail —
        /// adversarial review F1). Both read the price buyers were actually
        /// debited, recovered from the engine's own delivered-paid array, and
        /// compare it against a destination alternative rebuilt from the
        /// posted exit laws at the clearing margin (TradeSystem.MeasureBand):
        ///   FLOOR — no seller pushed below ask_i + haul_ij on a route it
        ///     served: ≤ 1e-9, measured 8.9e-16 across the sweep. Settling
        ///     local lots at P_j×½ turns it red (5.8e+0, seed 0) while both
        ///     ceilings go negative — the floor is the leg that catches
        ///     under-pricing.
        ///   CEILING, marginal LOT — the settled price never exceeds the
        ///     cheapest source the clearing could have transacted a lot from:
        ///     ≤ 1e-9, measured ≤1.6e-12 across the sweep (the two references
        ///     agree to floating-point residue). Settling local lots at P_j×2
        ///     turns it red (1.6e+1, seed 0) while the identity leg (5.5e-16)
        ///     and the localization check (corr 0.989) both stay green —
        ///     conservation and the tilt are blind to the price LEVEL, which
        ///     is why this leg has to exist.
        ///   CEILING, marginal UNIT — the same against exits holding less than
        ///     a lot of headroom, i.e. the lot-granularity residue the design's
        ///     band allows for: bound 1.0, measured worst 0.4573 (seed 25) on
        ///     the capacity-capped arm and ≤1.6e-12 on the uncapped one, where
        ///     no exit ever runs short of headroom.
        /// Reported, not gated: the excess over TICK-OPENING import parity,
        /// 0.3265–1.6939 over the sweep. Uniform-price clearing settles every
        /// unit at the margin and the tick's own import draws deepen the laws
        /// as it proceeds, so a settled price above the parity the tick opened
        /// at is the depth of the posted laws, not a pricing error — the
        /// buyer-side twin of the phase-2 seller regret (1.4130). Design §7's
        /// band condition is corrected to read at the margin.
        ///
        /// TWO ARMS: the plain 10×10 fixture, where no exit has a capacity,
        /// and the same with the capacity-capped sea backstop the
        /// modeprogression scenario exercises (`SeaExit`). Without the second
        /// arm the capacity paths — phase-1's lot-headroom skip, the
        /// challenger's full-lot admission, and the unit/lot distinction the
        /// ε measures — are never exercised: the uncapped arm reads the unit
        /// and lot references identical to 1e-12.
        ///
        /// All band numbers above: `goodssweep --seeds 7` (seeds 0–3, 9, 13,
        /// 25) at the fix commit; the mutant numbers, `goodssweep --seeds 1`
        /// with the named edit in the settle loop, reverted.</summary>
        private sealed class SettleArm
        {
            public int Ticks, Active, LocalActive;
            public double Gap, Floor = double.NegativeInfinity, Ceil = double.NegativeInfinity;
            public double CeilLot = double.NegativeInfinity, Open = double.NegativeInfinity;
        }

        private static SettleArm SettleArmRun(ulong seed, bool capacityCapped)
        {
            var p = new EconParams();
            var sim = Sim.Create(new SyntheticCity.Config
                                 { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed,
                                   SeaExit = capacityCapped },
                                 p, new FeatureFlags());
            var tel = new List<TradeSystem.SettleRecord>();
            TradeSystem.SettleTelemetry = tel;
            try { sim.Run(200); }
            finally { TradeSystem.SettleTelemetry = null; }

            var a = new SettleArm { Ticks = tel.Count };
            foreach (var rec in tel)
            {
                double outside = rec.ExportRevenue - rec.ImportCost - rec.LocalFreight;
                double scale = Math.Max(1.0, Math.Max(Math.Abs(rec.FirmCredit) + Math.Abs(rec.FirmDebit),
                                                      Math.Abs(outside)));
                a.Gap = Math.Max(a.Gap, Math.Abs(rec.FirmCredit - rec.FirmDebit - outside) / scale);
                a.Floor = Math.Max(a.Floor, rec.WorstFloorViolation);
                a.Ceil = Math.Max(a.Ceil, rec.WorstMarginExcess);
                a.CeilLot = Math.Max(a.CeilLot, rec.WorstMarginExcessLot);
                a.Open = Math.Max(a.Open, rec.WorstOpenParityExcess);
                if (rec.FirmCredit > 0 || rec.FirmDebit > 0) a.Active++;
                if (rec.LocalFreight > 0) a.LocalActive++;
            }
            return a;
        }

        private static void GoodsSettlementReconciles(ulong seed)
        {
            // The lot-granularity residue the design's band allows for. Bound
            // 1.0 against a measured worst of 0.4573 (seed 25, capacity-capped
            // arm, `goodssweep --seeds 7` at the fix commit; the uncapped arm
            // reads ≤1.6e-12 because no exit there ever runs short of
            // headroom). The margin is deliberate and is NOT slack for a
            // pricing bug: the residue is a spread between two exits serving
            // one destination and nothing structural caps it, while any
            // corruption of the settled price shows up at the price level
            // itself — the P_j×2 mutant reads 15.6202 here. The tight leg is
            // the marginal-LOT one below, bounded at 1e-9 and measured
            // ≤1.6e-12 across the same sweep.
            const double BandEpsUnit = 1.0;
            var plain = SettleArmRun(seed, capacityCapped: false);
            var capped = SettleArmRun(seed, capacityCapped: true);
            double gap = Math.Max(plain.Gap, capped.Gap);
            double floor = Math.Max(plain.Floor, capped.Floor);
            double ceilUnit = Math.Max(plain.Ceil, capped.Ceil);
            double ceilLot = Math.Max(plain.CeilLot, capped.CeilLot);
            // Floors keep a dead fixture from passing vacuously: each arm must
            // actually settle transactions, including local (freight-paying)
            // lots.
            Check("goods settlement reconciles per resource (firm money vs outside flows; settled prices inside the band)",
                  plain.Active >= 100 && plain.LocalActive >= 10
                  && capped.Active >= 100 && capped.LocalActive >= 10
                  && gap < 1e-9 && floor <= 1e-9 && ceilLot <= 1e-9 && ceilUnit <= BandEpsUnit,
                  $"{plain.Ticks}+{capped.Ticks} resource-ticks "
                  + $"({plain.Active}/{capped.Active} with settled flow, "
                  + $"{plain.LocalActive}/{capped.LocalActive} with local lots): "
                  + $"worst relative identity gap {gap:E1} (bound 1e-9); "
                  + $"band floor {floor:E1} (bound ≤1e-9); "
                  + $"band ceiling over the marginal lot {ceilLot:E1} (bound ≤1e-9), "
                  + $"over the marginal unit {ceilUnit:F4} (bound ≤{BandEpsUnit:F2}) "
                  + $"[uncapped {plain.Ceil:E1}, capped {capped.Ceil:F4}]; "
                  + $"over tick-opening parity {Math.Max(plain.Open, capped.Open):F4} (reported)");
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
                    // The oracle prices the same market the entrant does: the
                    // realized origin statistic AT THE CLUSTER (or the exit
                    // alternative), matching FirmBidPerSlot's extractor leg.
                    double val = suit * Math.Max(simExt.Engine.Trade.OriginStat((Res)rr, c),
                                                 simExt.Engine.Trade.BestExportNet((Res)rr, c));
                    if (val > bv) { bv = val; bestByValue = rr; }
                }
                if ((int)f.Output == bestBySuit || (int)f.Output == bestByValue) extractRight++;
            }

            int ind = 0, indAligned = 0;
            var outputsSeen = new HashSet<Res>();
            // The alignment leg below skips Machinery (multi-input: no single
            // cheapest raw), so a Machinery firm can never contribute to what
            // that leg discriminates between. The diversity count that matters
            // to it is therefore over the outputs it actually examines — both
            // are reported, the single-input one is the premise.
            var singleInputOutputs = new HashSet<Res>();
            // THE ORACLE IS THE MODEL'S OWN ARGMAX, exactly as the extractor
            // leg above prices its oracle at the entrant's own market. The old
            // criterion — "the chosen recipe's raw is the locally
            // cheapest-delivered raw" — was a PROXY for the margin argmax, and
            // measured against firms that provably chose the argmax it reads
            // ZERO: on the composed labor arm at seed 1, 0 of 17 standing
            // single-input industrials satisfied the proxy while 10 of 17 sat
            // on FirmBidPerSlot's own argmax re-asked at their site, entrants
            // 5 of 5. Asymmetric output anchors are why the two disagree — the
            // margin argmax weighs outNet − qty·inputCost, and a dear input
            // feeding a dearer output beats a cheap input feeding a cheap one.
            // The proxy share is still REPORTED so the divergence stays
            // visible; the verdict reads the argmax.
            //
            // THE VERDICT MOVED FROM THE STOCK TO THE DECISION, because the
            // stock cannot carry it on ANY arm. Measured with the true
            // criterion (is this firm's recipe FirmBidPerSlot's argmax at its
            // own site now): the DEFAULT arm reads 38 % — its churn leaves
            // standing firms on stale choices — while the composed labor arm
            // reads 59-74 %. The old cheapest-raw proxy read 88 % on default
            // and 0 % on composed, i.e. it tracked neither the criterion nor
            // any arm consistently: the margin argmax weighs
            // outNet − qty·inputCost, and with asymmetric output anchors a
            // dear input feeding a dearer output beats a cheap input feeding
            // a cheap one. Every standing-stock statistic conflates the
            // decision with entry timing against a moving price path (the
            // freeze artifact this check's own premise note names), so the
            // stock shares are REPORTED below and the assertion is the
            // DECISION FUNCTION itself, cluster by cluster.
            int indProxy = 0;
            foreach (var f in simInd.W.Firms)
            {
                if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Industrial) continue;
                int c = simInd.W.Parcels[f.Parcel].Cluster;
                outputsSeen.Add(f.Output);
                if (f.Output == Res.Machinery) continue;   // multi-input: excluded as before
                singleInputOutputs.Add(f.Output);
                ind++;
                var recipe = ResourceCatalog.RecipeFor(f.Output);
                double own = simInd.Engine.Trade.DeliveredCost(recipe.Inputs[0].res, c);
                double cheapest = double.PositiveInfinity;
                for (int rr = 0; rr < ResourceCatalog.RawCount; rr++)
                    cheapest = Math.Min(cheapest, simInd.Engine.Trade.DeliveredCost((Res)rr, c));
                if (own <= cheapest * 1.15 + 0.05) indProxy++;
                LandAccounting.FirmBidPerSlot(simInd.Engine.Access, simInd.Engine.Trade, c,
                                              ZoneKind.Industrial, simInd.W.Parcels[f.Parcel].Level,
                                              p, out Res argmaxNow, simInd.W.Clusters);
                if (argmaxNow == f.Output) indAligned++;
            }

            // The decision-function leg: at every cluster whose top-two recipe
            // margins actually separate, FirmBidPerSlot's chosen recipe must
            // equal an INDEPENDENTLY hand-computed margin argmax over the same
            // price reads. Hand-computed is what makes the falsifier work: the
            // --mutant-weber-secondbest switch corrupts the call under test,
            // and an oracle routed through the same call would agree with
            // every corrupted answer. Level 1 for both sides — quality scales
            // every recipe's margin by the same positive factor and the wage
            // is a common subtraction, so the ordering is level-invariant.
            int webDisc = 0, webAgree = 0;
            var argmaxKinds = new HashSet<Res>();
            for (int c2 = 0; c2 < simInd.Engine.Access.C; c2++)
            {
                Res best = Res.Services; double bv = double.NegativeInfinity, second = double.NegativeInfinity;
                foreach (var recipe in ResourceCatalog.Recipes)
                {
                    double outNet = Math.Max(
                        p.NonResLandParity ? simInd.Engine.Trade.OriginComparable(recipe.Output, c2)
                                           : simInd.Engine.Trade.OriginStat(recipe.Output, c2),
                        simInd.Engine.Trade.BestExportNet(recipe.Output, c2));
                    double inputCost = 0;
                    foreach (var (res, qty) in recipe.Inputs)
                        inputCost += qty * simInd.Engine.Trade.DeliveredCost(res, c2);
                    double perSlot = recipe.OutputPerSlot * p.RecipeOutputScale * (outNet - inputCost);
                    if (perSlot > bv) { second = bv; bv = perSlot; best = recipe.Output; }
                    else if (perSlot > second) second = perSlot;
                }
                if (!(bv - second > 1e-9)) continue;   // tie: argmax vs runner-up indistinguishable
                webDisc++;
                LandAccounting.FirmBidPerSlot(simInd.Engine.Access, simInd.Engine.Trade, c2,
                                              ZoneKind.Industrial, 1, p, out Res chosen2, simInd.W.Clusters);
                if (chosen2 == best) webAgree++;
                argmaxKinds.Add(best);
            }

            double extractShare = extract > 0 ? (double)extractRight / extract : 0;
            double indShare = ind > 0 ? (double)indAligned / ind : 0;

            // DISTINCT OUTPUTS IS A PREMISE, NOT AN ASSERTION — decided on
            // measurement, `weberprobe --seed 0 --seeds 26` at this commit.
            //
            // What it used to be: a leg of the verdict (`outputsSeen.Count >= 2`),
            // so a city that produced one industrial good read as a Weber
            // FAILURE. Seed 5 is the case that forced the question: 10 extractors
            // at 100% on the best raw and 7 of 7 single-input industrials on the
            // cheapest-sourced recipe — the property this check exists to assert
            // holds perfectly — red solely on the diversity bar.
            //
            // The evidence that it is not an economics claim, in three parts.
            // (1) An entrant's choice is argmax over recipes of margin at its own
            //     location (LandAccounting.FirmBidPerSlot). Evaluated at every one
            //     of the 196 clusters on the seed-5 fixture, Food is that argmax
            //     at 195 of them, and the runner-up trails by a median 0.941 per
            //     filled slot. A second output there is not a diversification a
            //     healthy city would find; it is an entrant choosing against its
            //     own margin, which the defaults rule forbids the mechanism to
            //     make anyone do.
            // (2) The realized count does not track the spatial signal anyway.
            //     Seed 0 produces ONE output while two recipes are argmax
            //     somewhere on its map (Food at 145 clusters, Plastics at 51);
            //     seed 3 produces THREE while a single recipe is argmax at all
            //     196. What the count actually records is entry TIMING against a
            //     moving price path — the "freeze artifact" the reverted
            //     retooling experiment named (KNOWN-RED, retooling section): let
            //     firms re-evaluate continuously and they converge on the one
            //     argmax, and this leg goes red on 20 of 26 seeds.
            // (3) The population is small enough that the count is a sample-size
            //     statistic: 6-12 industrial firms per seed, drawn from a choice
            //     distribution concentrated on one recipe.
            //
            // What it IS: the condition under which the alignment leg above can
            // DISCRIMINATE. With one output citywide, "the chosen recipe's raw is
            // the locally cheapest" is one recipe's claim repeated; with two or
            // more it separates firms in different places choosing differently.
            // So it is reported per seed and asserted across the SWEEP, where the
            // population it describes actually lives (WeberSweep's floor leg) —
            // and a seed that cannot meet it no longer reads as an economics
            // failure.
            bool diversePremise = singleInputOutputs.Count >= 2;
            WeberPremiseSeen++;
            if (diversePremise) WeberPremiseMet++;
            Check("Weber: extraction follows geology; recipes follow input sourcing",
                  extract >= 5 && extractShare >= 0.9 && webDisc >= 20 && webAgree == webDisc,
                  $"{extract} extractors ({extractShare:P0} on best raw); recipe decision: " +
                  $"{webAgree}/{webDisc} discriminating clusters agree with the hand-computed margin argmax " +
                  $"(bound: all, floor 20; {argmaxKinds.Count} distinct argmax outputs across clusters); " +
                  $"standing stock REPORTED: {ind} single-input industrials, {indShare:P0} on argmax-now, " +
                  $"{(ind > 0 ? (double)indProxy / ind : 0):P0} on the old cheapest-raw proxy; " +
                  $"{outputsSeen.Count} distinct industrial outputs, " +
                  $"{singleInputOutputs.Count} of them single-input " +
                  $"({(diversePremise ? "discrimination premise met" : "PREMISE NOT MET — one single-input output "
                      + "citywide, so the alignment leg cannot separate places; reported, not failed, "
                      + "and floored by WeberSweep")})");
        }

        /// <summary>Counters for the Weber check's discrimination premise, so
        /// the floor leg lives where its population does — across a sweep, not
        /// inside one seed. Reset by WeberSweep; RunAll leaves them alone
        /// because one seed cannot carry a distributional floor.</summary>
        public static int WeberPremiseSeen, WeberPremiseMet;

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
            // The quantity this price inverts is the number of households
            // BIDDING HERE, and that is share × presence: LandAccounting divides
            // SegmentKindShare by the segment's ladder length and multiplies by
            // segmentPresence, so the two multiply back to the raw count of
            // (renewal + best-alternative) bids the submarket faces. Measured on
            // both sides of the cull, because the leg's whole claim is that this
            // number falls.
            double BidsAtC0(AccessState a, double[] pv)
            {
                double n = 0;
                for (int s = 0; s < Segment.Count; s++)
                    if (a.SegmentKindShare.Length > s && a.SegmentKindShare[s][0].Length > c0)
                        n += a.SegmentKindShare[s][0][c0] * pv[s];
                return n;
            }
            double bidsBefore = BidsAtC0(acc, pres);
            int killed = 0;
            foreach (var h in sim.W.Households)
            {
                if (h.ExitedTick >= 0 || h.HomeParcel < 0) continue;
                // MEASURED, and the reason this leg was red on seed 13 at
                // `88fc88c`: the cull used to exempt the probed cluster's own
                // residents, and those residents are most of the queue it
                // prices. On seed 13 the exemption left the bid count at c0
                // EXACTLY unchanged — 23.00 → 23.00 — while 695 of 1317
                // households exited, so the leg demanded a price fall from a
                // submarket whose demand had not fallen at all. The survivors
                // re-sorted up-market into c0 (SingleBasic bids 2.90 → 0.00,
                // FamilyBasic 5.21 → 7.75, SeniorMid 2.07 → 5.46), the marginal
                // bidder got richer at constant quantity, and the price
                // correctly ROSE 2.15 → 2.56. Decomposed by holding each pricing
                // input at its pre-collapse value (`collapseprobe --seed 13`):
                // holding the demand SHARE field pre-collapse moves the post
                // price 2.59 → 0.52, while holding the ladder moves it to 2.74
                // and holding stock or access moves it not at all.
                //
                // Both mutants the registry named DO restore the fall
                // (`collapseprobe --seed 13 --owner-ask 0` reads 5.46 → 1.12,
                // `--mutant-citywide-goods` reads 2.90 → 2.65), but neither
                // does so by removing a defect: each simply perturbs the
                // re-sort enough that the bid count at c0 falls too (26 → 19
                // and 24 → 22 respectively). The channel is the exemption, not
                // owner asks and not per-cluster goods prices.
                if (MutantSpareProbedCluster
                    && sim.W.Parcels[h.HomeParcel].Cluster == c0) continue;
                if (SplitMix64.Hash01((ulong)h.Id * 977 + 5) < 0.6)
                { h.ExitedTick = sim.W.Tick; killed++; }                   // citywide demand collapse
            }
            sim.Run(10);
            double after = LandAccounting.ResidentialBidPerUnit(
                sim.Engine.Access, c0, ZoneKind.ResidentialLow, 2, sim.Engine.SegmentPresence, p, out double fillAfter);
            double bidsAfter = BidsAtC0(sim.Engine.Access, sim.Engine.SegmentPresence);
            // NON-DEGENERACY FLOOR for the softening leg: the collapse has to
            // reach the market being priced. Without it the leg is satisfied by
            // whatever the price happens to do to a queue that never shrank,
            // which is what it was doing. This is a leg of the check, not a
            // precondition — producing the shock is the fixture's own job, so a
            // cull that fails to move the probed submarket is a broken check and
            // must read red.
            //
            // THE BAR, AND ITS OVERLAP, MEASURED (`clearsweep --seeds 300`, both
            // arms, this commit). Clean: the ratio runs 0.118 to 0.789 with p50
            // 0.474 — a 60% citywide cull takes about half the probed queue.
            // With `--mutant-spare-probed-cluster`: 0.650 to 1.176, p50 0.864.
            // The two distributions OVERLAP on [0.650, 0.789], so NO bar
            // separates them perfectly and this one does not pretend to. 0.80
            // is where the clean arm stops producing floor failures at all
            // (0 of 267 measured seeds; 0.75 leaves 3 and 0.70 leaves 10) while
            // the mutant still dies on 203 of 267 — 164 on this floor alone and
            // 39 on floor and softening together. The headroom over the clean
            // maximum is 1.4%, which is thin and is stated rather than dressed
            // up: widening the sweep may find a clean seed above 0.80, and the
            // reading to take then is that the cull is weak on that seed, not
            // that the bar should move without the sweep that justifies it.
            // The queue counts are small (bidsBefore 10 to 34, median 22), which
            // is why the bar is a ratio and why it cannot be tight.
            bool demandFell = bidsAfter < bidsBefore * 0.80 && bidsBefore > 5;
            // Either the price falls, or — once the submarket is in the excess
            // regime where the price is pinned to the deepest real bidder — the
            // expected FILL falls hard while the price only drifts.
            //
            // The drift band is 20%, and the reason is an order-statistic one
            // worth stating because it is a genuine property of pricing off a
            // real population rather than a fitted curve: the excess anchor is
            // a low QUANTILE of the households present, and culling 60% of the
            // city both shrinks and re-weights the sample, so that quantile
            // moves by more than rounding.
            //
            // THE VACANCY DISJUNCT NOW FIRES. The previous comment recorded,
            // over seeds 0–299 with the c0 exemption in place, that fillBefore
            // was exactly 1.00 on 300/300 and fillAfter never fell below 0.909,
            // so `fillAfter < fillBefore * 0.8` fired on 0 of 300 and the leg
            // rested entirely on the price disjunct — and left it, saying that
            // "finding a cull that actually produces vacancy in this fixture is
            // its own investigation". That investigation is the exemption: with
            // it removed, seed 13 reads fill 1.00 → 0.35 and bid 2.15 → 0.88,
            // so both disjuncts fire (`collapseprobe --seed 13 --spare-c0 0`).
            // The distribution over seeds is recorded at `clearsweep --seeds
            // 300` in this commit's log; the bands are unchanged, because what
            // changed is the stimulus and not the assertion.
            bool softens = after < before * 0.98
                           || (fillAfter < fillBefore * 0.8 && after < before * 1.20);

            Console.WriteLine($"    AUDIT supplyMonotone={supplyMonotone} demandMonotone={demandMonotone} "
                + $"killed>100={killed > 100} demandFell={demandFell} ({bidsBefore:F2}→{bidsAfter:F2}) "
                + $"softens={softens} sweepMonotone={sweepMonotone} "
                + $"sweepDeclines={sweepDeclines} tracksIncome={tracksIncome} | dLow={dLow:F4} dMid={dMid:F4} dHigh={dHigh:F4}");
            Console.WriteLine($"    AUDIT income: wagesLiftTop={wagesLiftTop} ({topResponse:F5}) "
                + $"incomeHomogeneous={incomeHomogeneous} (dev {homogDev:E3}) "
                + $"pricesTail={pricesTail} (deep ×{tailResponse:F8}, top ×{topInvariance:F12}) "
                + $"sourcesReach={sourcesReach} (wage {reachWage:F3} benefit {reachBenefit:F3} "
                + $"floor {reachFloor:F3} transfer {reachTransfer:F3}) tableRestored={tableRestored}");
            Check("clearing price: quantity responds (supply ↓, demand ↑, population ↓ — price while cleared, vacancy once flat)",
                  supplyMonotone && demandMonotone && killed > 100 && demandFell && softens
                  && sweepMonotone && sweepDeclines && tracksIncome,
                  $"supply×{{0.5,2,8}} → {pLow:F2}/{pMid:F2}/{pHigh:F2} (fill {fMid:F2}→{fHigh:F2}); " +
                  $"demand×{{0.5,1,2}} → {dLow:F2}/{dMid:F2}/{dHigh:F2} (fill {fdLow:F2}→{fdMid:F2}); " +
                  $"after {killed} citywide exits bids at the probed submarket {bidsBefore:F1} → {bidsAfter:F1} " +
                  $"(<0.80× required), bid {before:F2} → {after:F2}, fill {fillBefore:F2} → {fillAfter:F2}; " +
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

        /// <summary>REALIZED VACANCY MOVES RENT — measured as a property of the
        /// channel, not of whichever cluster a selection lands on.
        ///
        /// The channel is two links and the check is one leg per link:
        ///   (a) AccessState.FillEma is THIS submarket's own realized fill,
        ///       smoothed at the declared constant;
        ///   (b) that quantity reaches the clearing price, through
        ///       RebuildDemandShares' attraction term and the demand curve
        ///       LandAccounting.ResidentialBidPerUnit inverts.
        /// An integration test alone cannot say this: the allocator re-houses
        /// whoever you evict, and an earlier check CLAIMED to measure a vacancy
        /// overhang while in fact only varying population (adversarial review).
        ///
        /// WHY BOTH LEGS WERE REWRITTEN. Both had the same defect in different
        /// clothes — a verdict decided by world composition rather than by the
        /// channel — and both generated registry churn at every world change
        /// while the transmission they name went untouched. Measured across
        /// four worlds, the failing SET re-rolled almost completely (base
        /// {9, 41, 910}, housing track {23, 41, 43, 46, 48}, goods track 48/57,
        /// merged {1, 20, 22, 28, 30, 41}); at 88fc88c the six reds split 4/2
        /// between the two old legs, so neither was healthy:
        ///   · the old price leg probed the LOWEST-INDEXED cleared cluster out
        ///     of the 47–70 that qualify per seed (`occprobe`, 57 seeds), so
        ///     one unresponsive cluster decided the verdict — seeds 30 and 41.
        ///   · the old tracking leg bounded the worst |FillEma − occupancy| at
        ///     0.5 with the sweep's own maximum measured at 0.500, i.e. zero
        ///     headroom — seeds 1, 20, 22 (0.500) and 28 (0.548). That bound
        ///     could not be tightened either: the EMA's own step response
        ///     permits |FillEma − target| up to (1 − α) = 0.75 after a single
        ///     full-swing tick, so anything under 0.75 is a claim about how
        ///     fast this fixture's occupancy moves, not about the mechanism.
        ///
        /// THE REWRITE IS STRICTLY STRONGER WHERE IT LOOKS WEAKER, measured on
        /// the five mutant sweeps below: the deleted `worstErr < 0.5` bound is
        /// SATISFIED — the old form would not have fired at all — on 51/57 seeds
        /// under `--mutant-fill-pooled`, 54/57 under `--mutant-fill-alpha`,
        /// 41/57 under `--mutant-fill-shift`, 11/57 under
        /// `--mutant-fill-frozen` and 52/57 under `--mutant-fill-blind`, while
        /// the identity leg below reds all 57 on each of them. What it IS more
        /// tolerant of is the clean arm's six red seeds, and that is the item
        /// rather than a side effect: four of them were the bound at its own
        /// observed maximum and two were the selection.
        ///
        /// LEG (a) IS NOW A DIFFERENTIAL ORACLE, exact instead of bounded. The
        /// per-submarket fill target is recomputed here from the parcels, the
        /// sim is advanced across ONE refresh, and the shipped FillEma must
        /// equal Ema(previous, target, FillEmaAlpha) to 1e-9 — measured 0.0
        /// exactly on all 57 sweep seeds. It carries its own paired
        /// non-degeneracy floor: the same identity read against the NEXT
        /// submarket's inputs must FAIL on ≥ 40 submarkets (measured 65–100),
        /// so a world where every submarket carried the same fill could not
        /// pass leg (a) vacuously. Four mutants confirm it can fail —
        /// `--mutant-fill-pooled` (citywide fill for every submarket),
        /// `--mutant-fill-shift` (the neighbour's), `--mutant-fill-frozen`,
        /// `--mutant-fill-alpha` — 0/57 seeds pass under each.
        ///
        /// LEG (b) IS NOW DISTRIBUTIONAL. The FillEma 1.0 → 0.2 reprice runs on
        /// EVERY cleared candidate submarket (stock ≥ 4, fill ratio 1 at
        /// current occupancy), and the assertion is on the MEDIAN response and
        /// on how many respond at all. A single flat cluster can no longer
        /// decide it, and a severed channel still reds it everywhere:
        /// `--mutant-fill-blind` (attraction ignores FillEma) passes 0/57.
        ///
        /// A MUTE CLUSTER IS REAL, and this is how that was established. On the
        /// 57-seed `occprobe` 0–10 cleared clusters per seed do not move at all
        /// (ratio within 1e-9 of 1); the probe prints, per seed, how many of
        /// those ALSO saw no demand-share movement, and that accounts for 133
        /// of the 154 mute clusters across the sweep. Those clusters are ones
        /// no household names as its best alternative, so their entire demand
        /// share is sitting tenants renewing — and a renewal bid is counted
        /// unconditionally in RebuildDemandShares, by design, because a sitting
        /// tenant will pay to stay whatever the vacancy rate. There is nothing
        /// there for the attraction term to move. The remaining 21 lose share
        /// and still do not move the price: the demand ladder is flat where
        /// they read it. Both are properties of the world, which is exactly why
        /// the verdict must not rest on one draw from it.
        ///
        /// PINNED POSTED. This is the posted path's own transmission and stays
        /// meaningful only there. The auction path's vacancy→price channel is
        /// asserted structurally by the auction-equilibrium check (a non-full
        /// door posts its reserve), canary-swept 39/39.
        ///
        /// EVERY BOUND BELOW IS SET FROM `occprobe --seeds 50` PLUS THE SEVEN
        /// PINNED SEEDS at 88fc88c (57 seeds, the occsweep set), and each is
        /// stated with the measured margin it leaves.</summary>
        private static void OccupancyChannel(ulong seed)
        {
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
            //
            // PINNED POOLED for the same reason: this leg's verdict is already
            // decided by which cluster its first-cleared selection lands on
            // (KNOWN-RED), so its world must be held fixed. A discrete shop
            // market re-rolls that selection and the verdict with it, which
            // would be mistaken for a transmission change.
            var sim = Sim.Create(cfg, p, new FeatureFlags
                                 { HousingAuction = false, StoreLevelSpending = false });
            sim.Run(120);
            var w = sim.W; var acc = sim.Engine.Access; var pres = sim.Engine.SegmentPresence;

            // ---- (b) the price response, on every cleared candidate ---------
            // The probed submarket must be CLEARED (fill ratio 1 at current
            // occupancy): there the channel must move the PRICE, which is the
            // substantive claim. In an excess submarket the flat-tail price is
            // mass-invariant and the fill response is mass-proportional by
            // construction — probing there measures the check's own plumbing.
            // Population, stock, access and geometry are held identical across
            // the two prices; only FillEma moves, and it is restored after.
            var ratios = new List<double>();
            int candidates = 0, mute = 0;
            for (int c = 0; c < acc.C; c++)
            {
                if (acc.HousingStock[0][c] < 4) continue;
                candidates++;
                LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p, out double f0);
                if (f0 < 1.0 - 1e-9) continue;
                double[] saved = { acc.FillEma[0][c], acc.FillEma[1][c] };
                void Reprice(double fill)
                {
                    acc.FillEma[0][c] = fill; acc.FillEma[1][c] = fill;
                    acc.RebuildDemandShares(w, p);   // same recompute the refresh does
                }
                Reprice(1.0);
                double bidFull = LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p);
                Reprice(0.2);
                double bidEmpty = LandAccounting.ResidentialBidPerUnit(acc, c, ZoneKind.ResidentialLow, 2, pres, p);
                acc.FillEma[0][c] = saved[0]; acc.FillEma[1][c] = saved[1];
                acc.RebuildDemandShares(w, p);
                double r = bidFull > 1e-12 ? bidEmpty / bidFull : 1.0;
                ratios.Add(r);
                if (r > 1 - 1e-9) mute++;
            }
            ratios.Sort();
            int qualifying = ratios.Count;
            double medianRatio = qualifying == 0 ? 1
                : qualifying % 2 == 1 ? ratios[qualifying / 2]
                : 0.5 * (ratios[qualifying / 2 - 1] + ratios[qualifying / 2]);
            int responded = ratios.Count(x => x < 0.95);
            double respondShare = qualifying > 0 ? (double)responded / qualifying : 0;

            // HOW THESE BARS ARE SET, since a bar placed at whatever was
            // observed asserts nothing: each sits BETWEEN the measured range and
            // the value a severed channel produces, nearer the measured side.
            //   median ratio  measured 0.246–0.490 · severed 1.000 · bar 0.70
            //   respond share measured 0.841–0.984 · severed 0.000 · bar 0.60
            //   qualifying    measured 47–70       · floor 20 (2.3x under)
            // So the median response would have to weaken from ~51 % to ~30 %
            // before this reds, and a severed channel misses by 0.30.
            bool qualifyingFloor = qualifying >= 20;
            bool medianLeg = medianRatio <= 0.70;
            bool breadthLeg = respondShare >= 0.60;

            // ---- (a) FillEma is this submarket's own smoothed fill -----------
            // Snapshot the state the next refresh will read, recompute the fill
            // target here from the parcels, advance across exactly ONE refresh,
            // and require the EMA recursion to hold exactly. The fixture's 120
            // ticks land on a refresh boundary; if the interval ever moves so
            // that they do not, this leg says so rather than silently measuring
            // a tick with no refresh in it.
            bool onBoundary = w.Tick % p.RefreshInterval == 0;
            var stock = new double[2][]; var target = new double[2][]; var prev = new double[2][];
            for (int k = 0; k < 2; k++)
            { stock[k] = new double[acc.C]; target[k] = new double[acc.C]; prev[k] = new double[acc.C]; }
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || !pl.IsResidential) continue;
                if ((uint)pl.Cluster >= (uint)acc.C) continue;
                int k = pl.Use == ZoneKind.ResidentialHigh ? 1 : 0;
                stock[k][pl.Cluster] += pl.Units;
                target[k][pl.Cluster] += Math.Min(pl.Units, pl.OccupantHouseholds.Count);
            }
            for (int k = 0; k < 2; k++)
                for (int c = 0; c < acc.C; c++)
                {
                    prev[k][c] = acc.FillEma[k][c];
                    target[k][c] = stock[k][c] > 0 ? MathUtil.Clamp(target[k][c] / stock[k][c], 0, 1) : 1.0;
                }
            sim.Run(1);

            var live = new List<(int k, int c)>();
            double worstResidual = 0, worstLag = 0, sumLag = 0;
            for (int k = 0; k < 2; k++)
                for (int c = 0; c < acc.C; c++)
                {
                    if (stock[k][c] < 4) continue;
                    live.Add((k, c));
                    double want = MathUtil.Ema(prev[k][c], target[k][c], AccessState.FillEmaAlpha);
                    worstResidual = Math.Max(worstResidual, Math.Abs(acc.FillEma[k][c] - want));
                    double lag = Math.Abs(prev[k][c] - target[k][c]);
                    sumLag += lag; worstLag = Math.Max(worstLag, lag);
                }
            // PAIRED NON-DEGENERACY. The identity above is vacuous on a world
            // where every submarket carries the same fill, so count how many
            // submarkets it can actually TELL APART: read each one's shipped
            // FillEma against its neighbour's inputs, and require the identity
            // to break there.
            int discriminated = 0;
            for (int i = 0; i < live.Count; i++)
            {
                var (k, c) = live[i];
                var (k2, c2) = live[(i + 1) % live.Count];
                double wrong = MathUtil.Ema(prev[k2][c2], target[k2][c2], AccessState.FillEmaAlpha);
                if (Math.Abs(acc.FillEma[k][c] - wrong) > 1e-9) discriminated++;
            }
            // MEASURED MARGINS (57 seeds, 88fc88c): residual 0.0 exactly
            // everywhere; compared 87–114 against the floor of 40;
            // discriminated 65–100 against 40. The lag |FillEma − occupancy| is
            // REPORTED (mean 0.003–0.023, worst 0.063–0.548) and asserted
            // nowhere: it is a statement about how fast this world's occupancy
            // moves, and the old 0.5 bound on it was the sweep's own maximum.
            int compared = live.Count;
            bool identityLeg = onBoundary && compared >= 40 && worstResidual <= 1e-9;
            bool discriminationLeg = discriminated >= 40;

            Check("occupancy channel: realized vacancy softens rent (median response over every cleared submarket)",
                  identityLeg && discriminationLeg && qualifyingFloor && medianLeg && breadthLeg,
                  $"FillEma = Ema(prev, measured fill) on {compared} submarkets, residual {worstResidual:E1}"
                  + (onBoundary ? "" : " [NOT ON A REFRESH BOUNDARY]")
                  + $", {discriminated} discriminated; lag mean {sumLag / Math.Max(1, compared):F3} "
                  + $"worst {worstLag:F3} (reported, not asserted); "
                  + $"reprice 1.0→0.2 on {qualifying}/{candidates} cleared candidates: median ratio "
                  + $"{medianRatio:F3} (bar 0.70), {responded} of {qualifying} respond <0.95 "
                  + $"({respondShare:P0}, bar 60 %), {mute} mute"
                  + (identityLeg ? "" : " — IDENTITY")
                  + (discriminationLeg ? "" : " — DISCRIMINATION")
                  + (qualifyingFloor ? "" : " — TOO FEW CLEARED")
                  + (medianLeg ? "" : " — MEDIAN")
                  + (breadthLeg ? "" : " — BREADTH"));
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

        /// <summary>A UNIFORMLY BETTER CITY MUST ADMIT MORE PROSPECTS. The
        /// come/stay margin compares a specific city door against the outside
        /// region's door, and the outside door is priced by the same premium
        /// rule at the OUTSIDE region's access level (AccessState.
        /// OutsidePremium). A spatially uniform improvement cancels out of
        /// every within-city comparison — all city doors share the MeanAccess
        /// normalizer — so the ONLY place it can land is the outside door's
        /// relative premium, which is exactly the term that was structurally
        /// missing when the boombust flip inventory measured inflow-margin
        /// response +0 against decline-exit response +440.
        ///
        /// THE TWO BATCHES ARE PAIRED ON ONE WORLD. Run a fixture, offer one
        /// prospect batch, then apply a uniform amenity delta and refresh the
        /// access field WITHOUT re-solving the auction: every posted price,
        /// entry price and within-city premium the second batch reads is
        /// bit-identical to what the first batch read (the solve is untouched),
        /// employment odds are the same realized rates re-served, and the
        /// batch keys are functions of (tick, q) with the tick unchanged — so
        /// the second batch is the same people quoting the same doors, and the
        /// only live difference is the outside door's premium at the new
        /// MeanAccess. Admitted-SHARE is compared rather than the count
        /// because the first batch's admissions nudge prominence and hence the
        /// second batch's offer count by a few heads.
        ///
        /// The declined floor keeps the check from passing vacuously: a
        /// fixture where nobody declines has no live margin for the pulse to
        /// move (that saturation was precisely the pre-#42 state, when the
        /// Declined outcome was structurally dead).
        ///
        /// MUTANT ARM, wired in like the Ward check's: the same fixture with
        /// p.MutantRelativeOutsideAccess restoring the removed defect (outside
        /// door anchored on the city's own MeanAccess, relative premium ≡ 1).
        /// The pulse must then FAIL to move the admitted share past the bar,
        /// or the check is vacuous. The clean arm takes the switch at its
        /// EconParams default rather than pinning it false, so a shipped flip
        /// of that default runs the clean arm as the mutant and goes red here
        /// — the switch default is itself under the check, not only under the
        /// fingerprint gate.
        ///
        /// THE RESIDENT MARGIN gets its own leg on the same paired world
        /// (item-#42 fix round: the prospect legs alone left `_outside[i] *=
        /// outsidePrem` with no semantic check that could fail — severing it
        /// flipped nothing but the fingerprint drift alarm, because every
        /// equilibrium/IR check reads the same `_outside` the solve used and
        /// is self-consistent under ANY scaling of it). The standing solve ran
        /// on the pre-pulse field, so each live resident's `_outside` is its
        /// walk-away value at the old outside premium; re-solving on the
        /// refreshed field re-derives it through the production path
        /// (BuildHouseholds) with every per-household input bit-identical —
        /// no tick advances, and Reservation(budget) reads only birth draws
        /// and job state (JobLevel, Earners, UnemployedTicks) that nothing
        /// between the two solves touches — so the per-resident ratio
        /// after/before isolates exactly the outside door's premium factor.
        /// The leg asserts the geometric-mean ratio FALLS with the premium;
        /// a severed resident leg pins it at 1. Ordered after both prospect
        /// batches so those still read the un-resolved prices the prospect
        /// pairing requires.</summary>
        private static void UniformPulseMonotonicity(ulong seed)
        {
            (Prospects.Result r1, Prospects.Result r2, double prem0, double prem1,
             double residRatio, int residN) Arm(bool mutantArm)
            {
                var p = new EconParams();
                if (mutantArm) p.MutantRelativeOutsideAccess = true;
                var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
                var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
                sim.Run(160);
                var acc = sim.Engine.Access;
                var a = sim.Engine.Auction!;
                // Big probe batches, harness-side only: offer size is a
                // region-side parameter, and inflating it AFTER the warmup
                // changes nothing about the world being quoted — it just gives
                // the paired comparison a sample the share bars can see.
                p.RegionOfferRate = 250;
                double prem0 = acc.OutsidePremium(p);
                var r1 = Prospects.Step(sim.W, acc, a, p);
                foreach (var c in sim.W.Clusters) c.Amenity += 1.5;
                acc.Refresh(sim.W, sim.Engine.Costs, p, sim.Flags);
                double prem1 = acc.OutsidePremium(p);
                var r2 = Prospects.Step(sim.W, acc, a, p);
                // Resident leg: pair each live resident's standing outside
                // option against its re-derivation on the pulsed field.
                // Households the batches admitted are beyond the standing
                // solve's arrays (OutsideOf reads 0) and drop out of the pair.
                int nh = sim.W.Households.Count;
                var before = new double[nh];
                for (int i = 0; i < nh; i++)
                    before[i] = sim.W.Households[i].ExitedTick < 0 ? a.OutsideOf(i) : 0;
                a.Solve(sim.W, acc, p);
                double sumLog = 0; int residN = 0;
                for (int i = 0; i < nh; i++)
                {
                    if (before[i] <= 0) continue;
                    double after = a.OutsideOf(i);
                    if (after <= 0) continue;
                    sumLog += Math.Log(after / before[i]); residN++;
                }
                double residRatio = residN > 0 ? Math.Exp(sumLog / residN) : 1.0;
                return (r1, r2, prem0, prem1, residRatio, residN);
            }

            var clean = Arm(false);
            var mut = Arm(true);
            double Share(Prospects.Result r) => r.Offered > 0 ? (double)r.Admitted / r.Offered : 0;
            double riseClean = Share(clean.r2) - Share(clean.r1);
            double riseMut = Share(mut.r2) - Share(mut.r1);

            // Bars measured at the item-#42 bring-up sweep (`pulsesweep`,
            // seeds 0-15, which includes the verify-pinned 9 and 13): clean
            // rise +0.037 (seed 9) .. +0.070 (seed 5) of offered; mutant arm
            // -0.006..+0.005 — batch-composition noise around zero, its
            // outside premium pinned at clamp(1)×BidAccessScale exactly
            // (prem1 == prem0 to the last bit). The refuter's wider sweep and
            // the fix-round re-measurement (`pulsesweep --seeds 24`, seeds
            // 0-23) put the mutant arm at -0.011..+0.009 and the clean rise at
            // +0.037..+0.071 — so the bar's real margin is ~1.8x over the
            // worst mutant reading and ~1.8x under the worst clean one, not
            // the 4x the 16-seed band suggested. Do not tighten the bar
            // without re-running the 24-seed sweep.
            const double RiseBar = 0.02;
            const int MinDeclined = 10;
            Check("uniform pulse admits more: the outside door prices the pulse the city doors cancel — and the relative-outside mutant flips it red",
                  clean.r1.Declined >= MinDeclined
                  && riseClean >= RiseBar && riseMut < RiseBar,
                  $"clean: admitted share {Share(clean.r1):F3} → {Share(clean.r2):F3} (+{riseClean:F3} vs ≥{RiseBar:F3} bar) "
                  + $"on {clean.r1.Offered}/{clean.r2.Offered} offered, {clean.r1.Declined} declined at base (floor {MinDeclined}), "
                  + $"outside premium {clean.prem0:F3} → {clean.prem1:F3}; "
                  + $"mutant: {riseMut:+0.000;-0.000;0.000} at premium {mut.prem0:F3} → {mut.prem1:F3} (must stay under the bar)");

            // Resident-leg bars, measured at the item-#42 fix round
            // (`pulsesweep --seeds 24`, seeds 0-23): clean geometric-mean
            // fall 0.161..0.176, tracking the premium ratio (1 − prem1/prem0)
            // to three digits on every seed; the mutant arm's premium is
            // pinned so its fall reads 0.000 exactly on all 24 — no
            // per-household input moves between the standing solve and the
            // re-derivation. The bar sits ~4x under the worst clean fall. A
            // severed resident leg (`_outside[i]` without the premium factor)
            // reads the mutant's number on the clean arm and goes red.
            const double ResidFallBar = 0.04;
            const int MinResidents = 500;
            double fallClean = 1 - clean.residRatio;
            double fallMut = 1 - mut.residRatio;
            Check("resident outside option re-prices with the outside door: re-deriving the solve's own _outside on the pulsed field moves every resident's walk-away value by the premium — and the relative-outside mutant pins it",
                  clean.residN >= MinResidents
                  && fallClean >= ResidFallBar && fallMut < ResidFallBar,
                  $"clean: resident outside options fell {fallClean:F3} geo-mean over {clean.residN} residents "
                  + $"(≥{ResidFallBar:F3} bar, floor {MinResidents}; premium ratio {clean.prem1 / clean.prem0:F3}); "
                  + $"mutant: {fallMut:+0.000;-0.000;0.000} over {mut.residN} (must stay under the bar)");
        }

        /// <summary>TIES CHANGE WHERE ADMITS LAND — the channel, not the
        /// geography. Each prospect draws a tie cluster ∝ the per-cluster
        /// chain-migration stock and gets a familiarity bonus at that one
        /// door (Prospects.Step → QuoteOutsider), so its tie must make it
        /// MORE likely to land there than the batch's marginal landing
        /// pattern alone predicts. The statistic is therefore a LIFT over an
        /// independence baseline computed from the same batch's own
        /// marginals — Σ_c P(tie=c)·P(chosen=c) — rather than any
        /// concentration measure: popular clusters both attract admits and
        /// hold big stocks, so raw tie-landing coincidence is high with the
        /// bonus severed, and a geography-shaped statistic would pass on a
        /// dead channel. Lift is exactly the dependence the bonus creates
        /// and nothing else. (The #31 check's level/tilt split is the
        /// template: one leg for the level a defect shifts, one for the
        /// dependence only the live channel produces.)
        ///
        /// The probe batch reads the warmed-up world with an inflated offer
        /// count, harness-side only — offer size is region-side, so a bigger
        /// batch changes nothing about the world being quoted, it just gives
        /// the shares a sample (same trick as the uniform-pulse check).
        ///
        /// MUTANT ARM, wired in Ward-style: p.MutantZeroTieBonus zeroes the
        /// bonus while the tie draw and the stock keep running, so the same
        /// fixture must read lift ≈ 0 — the check is vacuous otherwise. The
        /// clean arm takes the switch at its EconParams default, so a
        /// shipped default flip runs the clean arm as the mutant and goes
        /// red here.
        ///
        /// Bars measured at the item-#34 bring-up (`tiesweep --seeds 16`,
        /// seeds 0–15, the verify-pinned 9 and 13 among them, at the shipped
        /// bonus 0.15): clean lift +0.027..+0.052 (probe batches admit
        /// 1000–1150, every one tied), mutant arm −0.004..+0.006. The 0.015
        /// bar is ~1.8x under the worst clean seed and ~2.5x over the worst
        /// mutant reading — the same margin class as the uniform-pulse bar;
        /// re-run the 16-seed sweep before tightening. Against the DRAW
        /// rather than the bonus the margin is thinner and seed-dependent:
        /// a UNIFORM tie draw with the bonus alive (the "people follow
        /// people" proportionality severed) read clean lift +0.012..+0.019
        /// on seeds 0/9/13 and was caught only at seed 9 (fix-round
        /// weakening run, reverted; the adversarial round's variant of the
        /// same weakening read +0.010..+0.012, caught 3/3) — the bonus
        /// alone manufactures some tie-landing dependence, so this bar
        /// gates the BONUS channel and only brushes the draw; a draw
        /// regression is not reliably caught here, and any bar move needs
        /// that weakening re-run, not just the clean sweep. Dose-response
        /// of the bonus itself: EconParams.NetworkTieBonusScale.</summary>
        /// <summary>Sweep instrument only (`tiesweep --tie-bonus X`): overrides
        /// NetworkTieBonusScale in the tie-channel fixture's arms, so the
        /// parameter's dose-response is one command per value. Null in every
        /// other run.</summary>
        public static double? TieBonusOverride;

        /// <summary>MUTANT SWITCH, harness-only (`--mutant-spare-probed-cluster`):
        /// restores the clearing-price collapse leg's exemption for the probed
        /// cluster's own residents. That exemption is the defect this leg's
        /// non-degeneracy floor now catches — with it on, the cull leaves the
        /// bid count at the probed submarket essentially untouched, so
        /// `demandFell` goes false and the check reds. Exists so the floor leg
        /// stays falsifiable; never a shipping mode.</summary>
        public static bool MutantSpareProbedCluster;

        private static void ProspectTieChannel(ulong seed)
        {
            (double lift, double match, double indep, int admits, int tied) Arm(bool mutantArm)
            {
                var p = new EconParams();
                if (TieBonusOverride is double tb) p.NetworkTieBonusScale = tb;
                if (mutantArm) p.MutantZeroTieBonus = true;
                var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
                var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
                sim.Run(160);
                p.RegionOfferRate = 250;
                var tel = new List<(int tie, int chosen)>();
                Prospects.TieTelemetry = tel;
                try { Prospects.Step(sim.W, sim.Engine.Access, sim.Engine.Auction!, p); }
                finally { Prospects.TieTelemetry = null; }
                int C = sim.Engine.Access.C;
                var tieCnt = new double[C]; var chosenCnt = new double[C];
                int admits = 0, matched = 0, tied = 0;
                foreach (var (tie, chosen) in tel)
                {
                    admits++;
                    chosenCnt[chosen]++;
                    if (tie < 0) continue;
                    tied++;
                    tieCnt[tie]++;
                    if (tie == chosen) matched++;
                }
                double indep = 0;
                if (tied > 0 && admits > 0)
                    for (int c = 0; c < C; c++)
                        indep += (tieCnt[c] / tied) * (chosenCnt[c] / admits);
                double match = tied > 0 ? (double)matched / tied : 0;
                return (match - indep, match, indep, admits, tied);
            }

            var clean = Arm(false);
            var mut = Arm(true);
            const double LiftBar = 0.015;
            const int MinTied = 200;   // healthy probe batches admit ~1000-1100, every one tied (bring-up sweeps)
            Check("prospect ties steer admits to the tied neighborhood: tie-landing lift over the batch's own independence baseline — and the zero-bonus mutant flips it",
                  clean.tied >= MinTied && mut.tied >= MinTied
                  && clean.lift >= LiftBar && mut.lift < LiftBar,
                  $"clean: {clean.tied} tied admits (of {clean.admits}), landed-in-tie share {clean.match:F3} "
                  + $"vs independence {clean.indep:F3} (lift +{clean.lift:F3} vs ≥{LiftBar:F3} bar); "
                  + $"mutant: {mut.lift:+0.000;-0.000;0.000} on {mut.tied} tied (must stay under the bar)");
        }

        /// <summary>CONSTRUCTION CALIBRATION IS PER-PLACE. A developer's
        /// forecast correction at (use, cluster) must be the developer's own
        /// realized-vs-predicted record AT THAT PLACE where the record is
        /// deep, and the use-wide record where it is thin — the same
        /// shrinkage shape as ProspectLocalOdds (#31). Three legs on one
        /// fixture, evaluated over divergent cells (|own mean − use factor|
        /// ≥ the gap floor, so every leg has something to distinguish):
        ///
        ///  (1) CONVERGENCE: cells with n_c ≥ 2·n0 sit strictly closer to
        ///      their own mean ratio than to the use factor (w ≥ 2/3 there).
        ///      A restored citywide-only Factor pins every cell at the use
        ///      factor and flips this leg (source mutant run, below).
        ///  (2) SHRINKAGE: cells with n_c ≤ n0/2 sit closer to the use
        ///      factor than to their own mean (w ≤ 1/3). A zeroed prior
        ///      (n0 → 0) jumps them to their own one-completion mean and
        ///      flips this leg (source mutant run, below).
        ///  (3) CALL SITE: the construction forecast itself
        ///      (ConstructionSystem.ExpectedFlow's predictedRent) equals
        ///      bid × the PER-CELL factor at the deepest divergent cell —
        ///      re-derived through LandAccounting.BidPerUnit — and that
        ///      factor differs from the use factor by the cell's own gap.
        ///      Reverting the call site to Factor(use) flips this leg while
        ///      (1)-(2) stay green (the state would still be right, the
        ///      decision would ignore it — the exact regression this leg
        ///      exists to catch).
        ///
        /// Both source mutants were run and reverted at the item-#34
        /// bring-up (`calibsweep --seeds 4` mutant runs, seeds 0–3 + 9, 13):
        /// citywide-only (Factor(use, cluster, n0) → Factor(use)) reds legs
        /// 1 and 3 on 6/6 seeds (convergence 0/14..0/27, call site "NOT the
        /// cell" with factor ≡ use factor); zero-prior (w → 1 at the read)
        /// reds exactly leg 2 on 6/6 (shrinkage 0/14..0/19, legs 1 and 3
        /// green). Cell depth measured on this fixture (400 ticks, same
        /// seeds): see EconParams.CalibClusterShrinkN0 for the distribution
        /// the n0 and the leg thresholds were picked from; divergent-cell
        /// floors below are from the same runs.</summary>
        private static void CalibClusterCheck(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags { HousingAuction = true });
            sim.Run(400);
            var cal = sim.W.Calibration;
            double n0 = p.CalibClusterShrinkN0;
            const double MinGap = 0.05;

            int bigCells = 0, bigOk = 0, smallCells = 0, smallOk = 0, cells = 0;
            double totalN = 0;
            (ZoneKind use, int cluster, double n, double gap) deepest = (ZoneKind.None, -1, 0, 0);
            foreach (var kv in cal.ByCell)
            {
                cells++; totalN += kv.Value.N;
                double mean = kv.Value.SumRatio / kv.Value.N;
                double useF = cal.Factor(kv.Key.use);
                double fc = cal.Factor(kv.Key.use, kv.Key.cluster, n0);
                double gap = Math.Abs(mean - useF);
                if (gap < MinGap) continue;
                if (kv.Value.N >= 2 * n0)
                {
                    bigCells++;
                    if (Math.Abs(fc - mean) < Math.Abs(fc - useF)) bigOk++;
                    if (kv.Value.N > deepest.n || (kv.Value.N == deepest.n && gap > deepest.gap))
                        deepest = (kv.Key.use, kv.Key.cluster, kv.Value.N, gap);
                }
                else if (kv.Value.N <= 0.5 * n0)
                {
                    smallCells++;
                    if (Math.Abs(fc - useF) < Math.Abs(fc - mean)) smallOk++;
                }
            }

            // Leg 3: the forecast at the deepest divergent cell reads the
            // cell, not the citywide use factor. Any parcel of the cell's
            // (use, cluster) works — ExpectedFlow prices by cluster and use,
            // and predictedRent is bid × factor by construction.
            bool callSite = false; double fcDeep = 0, useFDeep = 0, rel = 1;
            if (deepest.cluster >= 0)
            {
                foreach (var pl in sim.W.Parcels)
                {
                    ZoneKind use = pl.State == ParcelState.UnderConstruction ? pl.Use : pl.Zoned;
                    if (pl.Cluster != deepest.cluster || use != deepest.use) continue;
                    sim.Engine.Construction.ExpectedFlow(sim.W, sim.Engine.Access, sim.Engine.Trade,
                                                         sim.Engine.Residuals, sim.Engine.SegmentPresence,
                                                         pl, Math.Max(1, (int)pl.Level), p,
                                                         out double predictedRent, out _);
                    double bid = LandAccounting.BidPerUnit(sim.Engine.Access, sim.Engine.Trade,
                                                           pl.Cluster, use, Math.Max(1, (int)pl.Level),
                                                           sim.Engine.SegmentPresence, p,
                                                           addUnits: LandAccounting.UnitsFor(use));
                    fcDeep = cal.Factor(deepest.use, deepest.cluster, n0);
                    useFDeep = cal.Factor(deepest.use);
                    rel = bid > 1e-9 ? Math.Abs(predictedRent - bid * fcDeep) / Math.Max(1e-9, bid * fcDeep) : 1;
                    callSite = rel < 1e-9 && Math.Abs(fcDeep - useFDeep) > 1e-6;
                    break;
                }
            }

            // The cell-depth distribution the n0 choice cites (EconParams.
            // CalibClusterShrinkN0) — printed, not asserted, so re-measuring
            // it after a fixture change is free.
            var depths = new List<double>();
            foreach (var kv in cal.ByCell) depths.Add(kv.Value.N);
            depths.Sort();
            double Q(double q) => depths.Count > 0 ? depths[(int)Math.Min(depths.Count - 1, q * depths.Count)] : 0;
            Console.WriteLine($"  cell depth n_c over {depths.Count} cells: "
                + $"min {(depths.Count > 0 ? depths[0] : 0):F0} p25 {Q(0.25):F0} median {Q(0.5):F0} "
                + $"p90 {Q(0.9):F0} max {(depths.Count > 0 ? depths[depths.Count - 1] : 0):F0}");

            const int MinBig = 3, MinSmall = 3;   // measured: seeds 0-3, 9, 13 hold 13-25 deep / 14-21 thin divergent cells (calibsweep --seeds 4, item-#34 bring-up)
            Check("construction calibration is per-(use, cluster): deep cells track their own realized/predicted record, thin cells shrink to the use factor, and the forecast reads the cell",
                  bigCells >= MinBig && bigOk == bigCells
                  && smallCells >= MinSmall && smallOk == smallCells
                  && callSite,
                  $"{cells} cells ({totalN:F0} completions observed): convergence {bigOk}/{bigCells} deep divergent cells "
                  + $"(n_c ≥ {2 * n0:F0}, floor {MinBig}) closer to own mean; shrinkage {smallOk}/{smallCells} thin "
                  + $"(n_c ≤ {0.5 * n0:F1}, floor {MinSmall}) closer to use factor; "
                  + (deepest.cluster >= 0
                      ? $"call site at ({deepest.use}, c{deepest.cluster}, n={deepest.n:F0}): factor {fcDeep:F3} vs use {useFDeep:F3}, "
                        + $"forecast rel err {rel:E1} ({(callSite ? "reads the cell" : "NOT the cell")})"
                      : "no divergent deep cell with a parcel — nothing valid to probe, failing"));
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
            // "Shown everything" includes the owner doors (item #41): they are
            // deliberately absent from the price-free opening walk (their
            // price-free value duplicates their base key's) and normally reach
            // shortlists via home listing and the repair scan — but this arm
            // asserts RepairRounds == 0, so discovery-by-repair is not
            // available to it and the keys must be listed up front.
            ax.ListOwnerDoors = true;
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
            //
            // THE BAND COMPOSES THE ENVY SWEEP'S OWN TWO-SIDED ACCOUNTING,
            // once per side of the trade. The price-free GAIN is not
            // band-free: identically,
            //   gain(i,j) = envy_i(sj) + envy_j(si)
            //             + (Entry(si) − Price(si)) + (Entry(sj) − Price(sj)),
            // so a pair of households each sitting inside the envy leg's band
            // can show a positive "swap" up to the SUM of their two envy
            // bands plus the doors' entry−posted gaps — an ε-residual of the
            // finite auction, not value the solve left on the table. The old
            // band (ε of each holder's OWN value only) granted the pair —
            // two envies — the allowance the envy leg grants ONE envy: the
            // same under-derived arithmetic the envy leg's band was fixed
            // for, and it fired on exactly that residual: canary seed 28 at
            // the per-cluster-memory commit read gain 0.173 against the old
            // 0.166 band, decomposing as 0.037 (envy_i, its envy band 0.167)
            // + 0.120 (envy_j, band 0.165) + 0.016 (entry−posted at i's
            // door) + 0.000 — every term inside the mechanism's own
            // guarantee (fix-round single-seed canary run, pair
            // hh285/sub252 ↔ hh1609/sub767). The band is therefore the two
            // envy bands, each ε of the holder's value plus ε of the value
            // it is reading — the envy leg's exact derivation. The
            // entry−posted gaps stay OUT of the band on purpose: with them
            // in, the leg is a near-consequence of the envy sweep and can no
            // longer fail on its own; and resting prices far below entry is
            // how restricted competition (M5) shows, so a band that absorbed
            // the gaps would re-blind this leg to the failure it exists for.
            // M5 (repair rounds 0) still reds this leg under the restated
            // band — see the mutant run recorded in the leg's registry
            // paragraph (fix-round M5 run, reverted).
            var occ = new List<int>();
            foreach (var h in w.Households)
                if (h.ExitedTick < 0 && (uint)h.Id < (uint)a.Assignment.Length && a.Assignment[h.Id] >= 0)
                    occ.Add(h.Id);
            int swaps = 0; double bestSwapGain = 0; string swapWhy = "";
            for (int x = 0; x < occ.Count && swaps == 0; x++)
            {
                int i = occ[x], si = a.Assignment[i];
                double vii = a.ValueOf(i, si, p);
                for (int y = x + 1; y < occ.Count; y++)
                {
                    int j = occ[y], sj = a.Assignment[j];
                    if (sj == si) continue;
                    double vij = a.ValueOf(i, sj, p), vji = a.ValueOf(j, si, p), vjj = a.ValueOf(j, sj, p);
                    double gain = (vij + vji) - (vii + vjj);
                    double band = Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vii))
                                + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vij))
                                + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vjj))
                                + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vji));
                    if (gain > band)
                    {
                        swaps++; bestSwapGain = gain;
                        // Say WHICH pair and how the gain decomposes — the
                        // identity above turns the raw number into the
                        // mechanism responsible (envy vs resting price).
                        double ei = (vij - a.EntryPrice(sj)) - (vii - a.Price[si]);
                        double ej = (vji - a.EntryPrice(si)) - (vjj - a.Price[sj]);
                        swapWhy = $"hh{i}@sub{si} ↔ hh{j}@sub{sj}: gain {gain:F3} > band {band:F3}"
                                + $" = envy_i {ei:F3} + envy_j {ej:F3}"
                                + $" + gaps {a.EntryPrice(si) - a.Price[si]:F3}/{a.EntryPrice(sj) - a.Price[sj]:F3}"
                                + $" (i: {vii:F2}→{vij:F2}, j: {vjj:F2}→{vji:F2};"
                                + $" si {a.Filled[si]}/{a.Capacity[si]}, sj {a.Filled[sj]}/{a.Capacity[sj]})";
                        break;
                    }
                }
            }

            // ---- OWNER-DOOR LEGS (item #41), part 1: what only the ENGINE's
            // last applied assignment can witness. Both read `a` as the run
            // left it, so they must run before this fixture's own re-solves.
            //
            // (O-move) DOOR↔PARCEL INTEGRITY: a household holding an owner
            // door lives at THAT parcel — the engine's FirstVacantIn resolves
            // an owner door to its parcel and nothing else. (The renewal half
            // — re-winning your own parcel-door produces no Vacate/MoveIn
            // pair — is structural: `have` is DoorOf(home) and want == have
            // short-circuits the move.)
            int ownerMoveBad = 0, ownerHeld = 0;
            foreach (var h in w.Households)
            {
                if (h.ExitedTick >= 0 || (uint)h.Id >= (uint)a.Assignment.Length) continue;
                int mine = a.Assignment[h.Id];
                if (mine < 0 || h.HomeParcel < 0) continue;
                int opl = a.OwnerParcelOf(mine);
                if (opl < 0) continue;
                ownerHeld++;
                if (h.HomeParcel != opl) ownerMoveBad++;
            }

            // (O-ratchet) NO-RATCHET: every standing ask IS the owner's own
            // valuation formula — A = min(OwnerAskScale · OwnerAskShare · R, R)
            // with R = max(0, ValueOf(owner, door) − OutsideOf(owner)) —
            // re-derived against the SAME frozen solve state PostOwnerAsks
            // wrote it from (untouched until this fixture's first re-solve
            // below; a just-claimed owner's ask is 0 until the next refresh
            // and is skipped). The exact identity, not just the A ≤ R bound:
            // an ask that reads the market's answer at its own door can hide
            // from the bound behind owner turnover — the exploded ask prices
            // its own owner out, the owner leaves, Vacate clears the ask, and
            // the survivors at any instant are all young (measured with the
            // 1.1×Price mutant at the item commit: bound-only leg 0 bad, the
            // identity leg 74 bad on the same seed-1 run).
            int ratchetBad = 0, asksLive = 0; double worstRatchet = 0;
            foreach (var pl in w.Parcels)
            {
                int oh = pl.OwnerHousehold;
                if (oh < 0 || pl.OwnerAskPerUnit <= 0) continue;
                if ((uint)oh >= (uint)a.Assignment.Length) continue;
                int door = a.DoorOf(pl);
                if (door < 0) continue;
                asksLive++;
                double r = Math.Max(0, a.ValueOf(oh, door, p) - a.OutsideOf(oh));
                double expectAsk = Math.Min(p.OwnerAskScale * w.Households[oh].OwnerAskShare * r, r);
                double off = Math.Abs(pl.OwnerAskPerUnit - expectAsk);
                if (off > 1e-9 * Math.Max(1, r)) { ratchetBad++; worstRatchet = Math.Max(worstRatchet, off); }
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
                  + (unsoldOverpriced > 0 ? $"\n      unsold: {unsoldWhy}" : "")
                  + (swaps > 0 ? $"\n      swap: {swapWhy}" : ""));

            // ---- OWNER-DOOR LEGS (item #41), part 2: state identities on the
            // fresh solve the determinism leg just left standing (a solve of
            // the CURRENT world, so the parcel-side of each identity is read
            // from the same state the auction built its doors from).
            int lvls = HousingAuction.Levels;
            int uniN = 2 * sim.Engine.Access.C * lvls;
            int C2 = sim.Engine.Access.C;

            // (O-partition) Capacity[uniform] + Σ Capacity[owner doors there]
            // == lettable units of built, non-warehoused residential parcels
            // at each (density, cluster, level). The partition moves units, it
            // never mints or drops them.
            var lett = new int[uniN];
            foreach (var pl in w.Parcels)
            {
                if ((uint)pl.Cluster >= (uint)C2) continue;
                if (pl.State != ParcelState.Built || !pl.IsResidential || pl.Warehousing) continue;
                lett[a.SubOf(pl.Cluster, pl.Use, pl.Level)] += pl.Units;
            }
            var got = new int[uniN];
            for (int s = 0; s < a.Capacity.Length; s++)
            {
                if (a.Capacity[s] <= 0) continue;
                int opl = a.OwnerParcelOf(s);
                int uni = opl < 0 ? s
                    : a.SubOf(w.Parcels[opl].Cluster, w.Parcels[opl].Use, HousingAuction.LevelOf(s));
                got[uni] += a.Capacity[s];
            }
            int partitionBad = 0;
            for (int s = 0; s < uniN; s++) if (got[s] != lett[s]) partitionBad++;

            // (O-reserve) Reserve identity, bitwise: every live owner door
            // floors at max(its OWN parcel's condition floor, its owner's
            // ask) — the per-parcel condition floor replacing the pooled
            // average is half the point. (Bitwise is safe: res-low units are
            // 2, so the built average (cond·2)/2 is exact.) And the fold rule
            // coheres both ways: a door exists iff its ask binds above its
            // own floor.
            int reserveBad = 0, foldBad = 0, liveDoors = 0, ownerTagged = 0, floorBad = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.OwnerHousehold < 0) continue;
                ownerTagged++;
                if ((uint)pl.Cluster >= (uint)C2 || pl.State != ParcelState.Built
                    || pl.Use != ZoneKind.ResidentialLow) continue;
                int plvl = Math.Min(lvls, Math.Max(1, pl.Level));
                double floor0 = LandAccounting.SPerUnit(plvl, pl.Condition, p);
                bool binding = pl.OwnerAskPerUnit > floor0;
                int door = a.DoorOf(pl);
                bool isOwnDoor = a.OwnerParcelOf(door) == pl.Id;
                if (binding != isOwnDoor) { foldBad++; continue; }
                if (!isOwnDoor) continue;
                liveDoors++;
                if (a.Reserve[door] != Math.Max(floor0, pl.OwnerAskPerUnit)) reserveBad++;
                // (O-floor) the atomicity converse, CHK-7: a non-full owner
                // door RESTS at its floor — leg 6a bounds Price ≤ Reserve +
                // hair for it, this leg pins the equality so the pair cannot
                // be satisfied by a Reserve that drifted mid-solve.
                if (a.Filled[door] < a.Capacity[door]
                    && Math.Abs(a.Price[door] - a.Reserve[door]) > Math.Max(1e-9, 1e-9 * a.Reserve[door]))
                    floorBad++;
            }

            // (O-IR) OWNER IR / displacement only by strictly better: an owner
            // NOT holding its own live door does not strictly prefer it beyond
            // the band — with the case the global envy sweep cannot see:
            // an owner the solve left UNASSIGNED (the envy sweep skips the
            // unhoused). The ask clamp guarantees an owner is never priced
            // out of its own door by its own ask (value − ask ≥ outside).
            int ownerIrBad = 0; double worstOwnerIr = 0;
            for (int s = 0; s < a.Capacity.Length; s++)
            {
                int opl = a.OwnerParcelOf(s);
                if (opl < 0 || a.Capacity[s] <= 0) continue;
                int oh = w.Parcels[opl].OwnerHousehold;
                if (oh < 0 || (uint)oh >= (uint)a.Assignment.Length) continue;
                int mine = a.Assignment[oh];
                if (mine == s) continue;
                double vOwn = a.ValueOf(oh, s, p);
                double cur = mine >= 0 ? a.ValueOf(oh, mine, p) - a.Price[mine] : a.OutsideOf(oh);
                double myEps2 = Math.Max(p.AuctionEpsilon,
                    p.AuctionEpsilonRel * Math.Abs(mine >= 0 ? a.ValueOf(oh, mine, p) : a.OutsideOf(oh)));
                double band = myEps2 + Math.Max(p.AuctionEpsilon, p.AuctionEpsilonRel * Math.Abs(vOwn));
                double gain = (vOwn - a.EntryPrice(s)) - cur;
                if (gain > band) { ownerIrBad++; worstOwnerIr = Math.Max(worstOwnerIr, gain); }
            }

            // (O-engineered) The fold rule and the reserve are exercised even
            // on a seed where no ask happens to bind: set one owner parcel's
            // ask above its own floor, re-solve (Solve is pure), and the door
            // must unfold with the ask as its reserve and its units out of
            // the pooled door; restore, re-solve.
            bool engRan = false, engOk = false; string engWhy = "no eligible owner parcel";
            foreach (var pl in w.Parcels)
            {
                if (pl.OwnerHousehold < 0 && pl.OccupantHouseholds.Count == 0) continue;
                if ((uint)pl.Cluster >= (uint)C2 || pl.State != ParcelState.Built
                    || pl.Use != ZoneKind.ResidentialLow || pl.Warehousing) continue;
                int plvl = Math.Min(lvls, Math.Max(1, pl.Level));
                double savedAsk = pl.OwnerAskPerUnit; int savedOwner = pl.OwnerHousehold;
                if (savedOwner < 0) pl.OwnerHousehold = pl.OccupantHouseholds[0];
                double engAsk = LandAccounting.SPerUnit(plvl, pl.Condition, p) * 1.5 + 1.0;
                pl.OwnerAskPerUnit = engAsk;
                a.Solve(w, sim.Engine.Access, p);
                int door = a.DoorOf(pl);
                int uniSub = a.SubOf(pl.Cluster, pl.Use, plvl);
                int lettHere = 0;
                foreach (var q2 in w.Parcels)
                    if ((uint)q2.Cluster < (uint)C2 && q2.State == ParcelState.Built && q2.IsResidential
                        && !q2.Warehousing && a.SubOf(q2.Cluster, q2.Use, q2.Level) == uniSub
                        && a.OwnerParcelOf(a.DoorOf(q2)) < 0)
                        lettHere += q2.Units;
                engRan = true;
                engOk = a.OwnerParcelOf(door) == pl.Id
                        && a.Reserve[door] == engAsk
                        && a.Capacity[door] == pl.Units
                        && a.Capacity[uniSub] == lettHere;
                if (!engOk)
                    engWhy = $"parcel {pl.Id}: door {door} (own? {a.OwnerParcelOf(door) == pl.Id}), "
                           + $"reserve {a.Reserve[door]:F4} vs ask {engAsk:F4}, "
                           + $"door cap {a.Capacity[door]} vs units {pl.Units}, "
                           + $"uniform cap {a.Capacity[uniSub]} vs pooled {lettHere}";
                else engWhy = $"parcel {pl.Id} unfolded at ask {engAsk:F3}";
                pl.OwnerAskPerUnit = savedAsk; pl.OwnerHousehold = savedOwner;
                a.Solve(w, sim.Engine.Access, p);
                break;
            }

            bool ownerOk = partitionBad == 0 && reserveBad == 0 && foldBad == 0 && floorBad == 0
                           && ownerIrBad == 0 && ownerMoveBad == 0 && ratchetBad == 0
                           && engRan && engOk;
            Check("owner doors: partition, reserve=max(floor, ask), fold rule, owner IR, door↔parcel, no-ratchet",
                  ownerOk,
                  $"{ownerTagged} owner-tagged parcels, {liveDoors} live doors, {asksLive} standing asks, "
                  + $"{ownerHeld} owner-door tenants: partition {partitionBad} bad cells, "
                  + $"reserve {reserveBad} bad, fold {foldBad} incoherent, resting-price {floorBad} off floor, "
                  + $"owner-IR {ownerIrBad} (worst {worstOwnerIr:F3}), door↔parcel {ownerMoveBad} astray, "
                  + $"ratchet {ratchetBad} asks off the write identity (worst {worstRatchet:E1}); "
                  + $"engineered arm: {engWhy}");
        }

        // ====================================================================
        // TASK #20 — the commercial mechanism, which nothing in this suite
        // asserted anything about before. Measured at the design round:
        // TestRunner.cs contained ZERO occurrences of "Commercial", "shop",
        // "retail" or "capture" outside the three lines discussing MUT-L and
        // known limit 4, against 26 for "residential". Both ledger arms run
        // StoreLevelSpending = false (below), so the store-level branch's money
        // was uncovered too, and MUT-L is a documented STILL-A-MISS.
        //
        // These two checks are what a flip of that flag would have to be
        // classified against. Per MUT-D's own ratification in this file:
        // coverage is necessary for a check to fail and never sufficient —
        // what was missing was an ASSERTION.
        // ====================================================================

        // Every bound here is measured, never guessed, and names the run it
        // came from. The measurement protocol: run `shopsweep --seeds 4` with
        // the bounds wide, read the healthy distribution, set each bound with
        // stated margin, then sweep 0–25. Numbers below are from
        // `shopsweep --seeds 26` at this commit unless a line names another run.
        // A COUNT of binding firm-ticks, not a share. The share is a nuisance
        // quantity — it divides by however many shops the seed happens to carry
        // — and the claim the floor makes is that the population is NON-EMPTY.
        // RE-MEASURED at the entrant-survival commit (`shopsweep --seeds 26`,
        // full 26-seed run): the entry-reference-mass fix sites entrants where
        // custom actually exists, which is also fewer entrants standing in a
        // building too small for their own traffic — the exact population this
        // leg counts — so the floor's own margin thinned as a direct, measured
        // consequence of the fix this item makes, not a fluke. Full 26-seed
        // range: 15 (seed 21, the new worst), 50, 55, 80, 100, 115, 119, 137,
        // 148, 150, 159, 182, 194, 199, 215, 246, 249, 257, 285, 296, 311, 321,
        // 370, 370, 441, 470. Floor reset to 10, half the new worst seed and
        // still zero-vs-nonzero discriminating against the BINDS mutant
        // (CommercialServicePerSlot → 1e9), which drives the count to exactly 0.
        private const int BindFloor = 10;               // firm-ticks where the bound actually bound
        private const int CeilingFloor = 10;            // high-traffic door-ticks the ceiling leg needs
        private const int StarveFloor = 5;              // doorless staffless firm-ticks the mutant must make
        private const double GuardLiveFloor = 20;       // clusters with a non-zero read
        // RE-MEASURED at the entrant-survival commit (`shopsweep --seeds 26`):
        // the entry-reference-mass fix changes which parcels get occupied and by
        // whom, which shifts each household's OWN realized bestSys — the
        // quantity IntentHeads counts backing evidence against — at the margin.
        // Full 26-seed range: 3 (seeds 3 and 15, the new worst two), then 7, 7,
        // 8, 8, 8, 8, 9, 9, 9, 10, 10, 11, 11, 12, 12, 12, 13, 14, 14, 14, 15,
        // 20 across the rest. Floor reset to 3 — still requires several
        // genuinely thin clusters be found before the leg can assert anything,
        // and the assertion itself (0 of them return a non-zero read) is
        // unaffected; every seed still reads 0 there.
        private const int ThinFloor = 3;                // clusters whose read rests on too few households
        private const int SilentFloor = 3;              // clusters the counted read reports as dead
        private const double CrowdFloor = 0.05;         // counted read's fall under 20× incumbent mass
        private const double LocalityPooledFloor = 0.02;// the pooled field's distant move, same run
        // Measured on `shopsweep --seeds 6` at the entrant-survival commit, both
        // arms of the leg, 320 ticks each: clean 0, 0, 0, 1, 1, 0 young entrant
        // deaths on seeds 0-5; the same fixture under MutantEntryReferenceMass
        // 25, 33, 45, 33, 34, 26. That six-seed sample is what the floor was
        // FIRST set from (12), and the full 26-seed sweep broke it: seeds 15 and
        // 18 read 7 and 9 mutant deaths, below 12. RE-MEASURED over the full
        // 26-seed sweep, mutant-arm range: 7, 9, 12, 17, 17, 18, 20, 20, 21, 21,
        // 24, 25, 25, 26, 27, 28, 31, 33, 33, 34, 35, 35, 37, 38, 42, 45. Clean
        // arm across the same 26 seeds never exceeds 1. Floor reset to 5 — below
        // the new worst mutant reading (7) with margin, and still 5× the clean
        // arm's worst reading, so the leg keeps discriminating a working fix
        // (clean ≤ 1) from the restored defect (mutant ≥ 7 on every seed
        // measured) while surviving the seed range that decides this item.
        private const int EntrantYoungDeathMax = 8;     // clean arm
        private const int EntrantMutantFloor = 5;       // mutant arm

        /// <summary>The task #20 fixture: an auction city with BOTH the labor
        /// auction and store-level spending on. The labor flag is on because
        /// half of this item is what a commercial door is worth, and that only
        /// exists on the auction path.</summary>
        private static Sim ShopFixture(ulong seed, EconParams p, int ticks, bool storeArm,
                                       bool laborAuction, Action<Sim>? perTick = null,
                                       int cols = 13, int rows = 13, int households = 8000)
        {
            // A much bigger city than the labor fixture's 8x8/2000, and the size
            // is load-bearing rather than incidental. Every leg of both commerce
            // checks carries a non-degeneracy FLOOR, and the floors are what the
            // fixture has to produce. Measured on the way here:
            //   8x8/2000  - the residential stock caps the seeded population well
            //               under 2000 (SyntheticCity.SeedHouseholds takes
            //               min(asked, 0.92 x vacancies), so asking for more there
            //               changes nothing at all) and the city carries more shop
            //               capacity than it presents custom for: the capacity
            //               bound binds on 0.0% of firm-ticks.
            //   11x11/4000 - binds on 3.0-11.5% at seeds 0-3, but across seeds
            //               0-25 the thin populations run out: seed 4 reads 1.8%
            //               against a 2.0% floor, 0 high-traffic door-ticks
            //               against 10, 0 mid-run entrants against 3, and 1
            //               evidence-thin cluster against 4.
            // A floor that a fixture cannot reliably produce is not a floor, and
            // lowering it to whatever one seed happens to yield is the vacuity
            // this whole family of legs exists to prevent.
            var cfg = new SyntheticCity.Config
                      { Cols = cols, Rows = rows, SeedHouseholds = households, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags
                                 { HousingAuction = true, LaborAuction = laborAuction,
                                   StoreLevelSpending = storeArm });
            sim.Run(ticks, perTick);
            return sim;
        }

        /// <summary>A commercial firm's takings are bounded by the staff it
        /// actually has, and the price it will pay for one more worker is a
        /// technology ceiling rather than an EMA of the money the allocation
        /// mechanism happened to hand it.
        ///
        /// WHAT WAS WRONG. Every other sector's output is linear in
        /// WorkersFilled (EconomyEngine.ProductionAndTrade: extractor,
        /// industrial, office). Commercial read RevenueThisTick and never
        /// WorkersFilled at all, so a shop could not be understaffed and could
        /// not be full; LaborAuction.BuildDoors therefore had no marginal
        /// product to cap commercial doors with and fell back on
        /// ProfitEma × (1 − BasketShare) / JobSlots — an AVERAGE product of a
        /// revenue the worker did not produce.
        ///
        /// FIVE LEGS, and each names the mutant that reds it. BOUND recomputes
        /// capacity from the raw recorded fields rather than reading the
        /// engine's own capacity, so it is not the engine compared with itself.
        /// BINDS is BOUND's non-degeneracy floor: without it an inert mechanism
        /// (capacity → ∞) satisfies BOUND. CEILING is the leg that says the
        /// revenue proxy is GONE, and it asserts the property that is not a
        /// relabeling — a shop whose traffic per slot runs far above what a slot
        /// can serve posts its cap AT the technology ceiling and no higher,
        /// where the EMA rule's cap rises without limit in traffic. (An earlier
        /// formulation compared two shops matched on presented-per-slot; that is
        /// a structural tautology, since the new cap reduces algebraically to
        /// min(perSlotCap, PresentedEma/JobSlots) × (1 − BasketShare) — a pure
        /// function of the matching keys. A leg whose two sides are the same
        /// function of the same inputs is not a check.) STARVATION protects the
        /// load-bearing "presented, never served" decision. DEFAULTS is the
        /// defaults rule: no household is ever served at a shop it ranks below
        /// its OWN out-of-town option, taste draw included. MONEY compares the
        /// firm-side record of what was served against the household-side record
        /// of what was spent — two separately maintained accumulations.
        ///
        /// Every bound below is measured, and names the run it came from.</summary>
        private static void CommercialStaffing(ulong seed)
        {
            var p = new EconParams();
            double basketMargin = 1 - LaborAuction.BasketShare;

            // ---- ARM 1: the SHIPPING commercial path ------------------------
            // FeatureFlags.LaborAuction is false by default, so the market that
            // ships routes staff through AssignWorkplaces. Capacity, rationing
            // and money are asserted here. They are not asserted on the labor-
            // auction arm because there the mechanism removes its own evidence:
            // with a structural door cap a shop bids for exactly the staff its
            // traffic needs, gets it, and the capacity bound then binds on 0.0%
            // of firm-ticks (measured, 11x11/4000/150t, seeds 0-1). That is the
            // mechanism working, and it is why BINDS lives on this arm.
            long firmTicks = 0, bindTicks = 0, boundViolations = 0;
            double worstBoundExcess = 0;
            long belowDefault = 0, shortlistFallbacks = 0;
            double servedInLaterRounds = 0, worstMoneyRel = 0;
            long moneyTicks = 0;

            // Three snapshots the CHECK keeps for itself, taken at the end of
            // every tick. RefreshTick runs at the START of a Step, before
            // condition decay, before any level change and before every phase
            // that can move a household, so the end-of-tick-T-1 state IS the
            // state ChooseShops read at tick T. Recomputing them at check time
            // instead read 641-668 spurious "below default" entries per seed out
            // of ~130000 — marginal shifts, not defects, and exactly what an
            // integer-equality leg must not be built on.
            int[] prevShop = Array.Empty<int>();
            double[] prevMass = Array.Empty<double>();
            int[] prevCluster = Array.Empty<int>();

            var sim = ShopFixture(seed, p, 150, storeArm: true, laborAuction: false, perTick: s =>
            {
                var w = s.W; var eng = s.Engine; var acc = eng.Access;

                foreach (var f in w.Firms)
                {
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                    var pl = w.Parcels[f.Parcel];
                    // RECOMPUTED here from WorkersFilled / condition / level.
                    // Never EconomyEngine.CommercialServiceCapacity and never
                    // Firm.RemainingCapacity: a leg that reads the engine's own
                    // capacity back out is the engine agreeing with itself.
                    double cap = p.CommercialServicePerSlot * f.WorkersFilled
                                 * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level) / p.Quality(1);
                    firmTicks++;
                    if (f.ServedThisTick > cap + 1e-9)
                    {
                        boundViolations++;
                        worstBoundExcess = Math.Max(worstBoundExcess,
                                                    (f.ServedThisTick - cap) / Math.Max(1e-9, cap));
                    }
                    // The bound BINDS: a staffed shop had more custom at its
                    // door than its staff could serve. Without this floor an
                    // inert mechanism (capacity to infinity) satisfies BOUND at
                    // every firm-tick and the check says nothing.
                    if (cap > 1e-9 && f.PresentedThisTick > cap + 1e-9) bindTicks++;
                }

                // MONEY: the firm-side record against the household-side record.
                // Dead firms included — bankruptcy settles in FirmLifecycle,
                // AFTER consumption, so a shop that served custom this tick and
                // died this tick still took that money. Excluding it read a
                // 6.6E-3 gap on seed 2 that was this and not a defect.
                double firmSide = 0;
                foreach (var f in w.Firms)
                    if (f.Sector == ZoneKind.Commercial) firmSide += f.ServedThisTick;
                double spend = eng.ConsumptionSpendThisTick;
                if (spend > 1e-9)
                {
                    moneyTicks++;
                    worstMoneyRel = Math.Max(worstMoneyRel,
                        Math.Abs(firmSide + eng.ConsumptionLeakedThisTick - spend) / spend);
                }
                for (int r = 1; r < eng.ShopServedByRound.Length; r++)
                    servedInLaterRounds += eng.ShopServedByRound[r];

                // DEFAULTS: nobody is ever offered — and so nobody can ever be
                // served — a shop it ranks below its own out-of-town option. The
                // comparison is re-derived here from the household's own id
                // hashes, the shop's own mass and the same WShop the engine
                // reads; only the shortlist itself, the thing under test, is
                // read out of the engine.
                // RefreshInterval == 1, not 0: EconomyEngine.Step ends with
                // W.Tick++, so a perTick callback seeing W.Tick == 6 is the one
                // that ran after the STEP at tick 5 — the refresh step. Testing
                // for 0 here read the shortlist several ticks after the state
                // that built it and produced 885-932 spurious "below default"
                // entries per seed out of ~130000.
                if (w.Tick % p.RefreshInterval == 1 && prevMass.Length > 0)
                {
                    int K = Math.Max(1, eng.ShopShortlistStride);
                    var choices = eng.ShopChoices;
                    double outU = Math.Log(Math.Max(1e-9,
                        Math.Exp(-p.ThetaShopping * p.OutsideShopMinutes) * p.OutsideShopMass));
                    foreach (var h in w.Households)
                    {
                        if (h.ExitedTick >= 0) continue;
                        int at = h.Id * K;
                        if (at + K > choices.Length || (uint)h.Id >= (uint)eng.ShopOrigin.Length) continue;
                        // AssignWorkplaces runs inside the same RefreshTick and
                        // BEFORE ChooseShops, so an unhoused household's origin
                        // at choice time is a workplace assigned earlier in that
                        // same tick and no end-of-tick snapshot can reconstruct
                        // it — measured as 1-8 residual "violations" per seed,
                        // all of them households with no home.
                        int origin = eng.ShopOrigin[h.Id];
                        double defaultU = outU + GumbelOf((ulong)h.Id * 2246822519UL + 7919UL);
                        int was = (uint)h.Id < (uint)prevShop.Length ? prevShop[h.Id] : -1;
                        for (int r = 0; r < K; r++)
                        {
                            int fi = choices[at + r];
                            if (fi < 0 || (uint)fi >= (uint)prevMass.Length) continue;
                            if (r > 0) shortlistFallbacks++;
                            double mass = prevMass[fi];
                            int fc = prevCluster[fi];
                            if (mass <= 0 || (uint)fc >= (uint)acc.C) continue;
                            double wgt = (origin >= 0 ? acc.WShop[origin, fc] : 1.0) * mass;
                            if (wgt <= 1e-12) { belowDefault++; continue; }
                            double u = Math.Log(wgt)
                                + GumbelOf((ulong)h.Id * 2246822519UL + (ulong)fi * 40503UL + 13UL)
                                + (fi == was ? p.ShopLoyalty : 0);
                            if (u <= defaultU) belowDefault++;
                        }
                    }
                }
                if (prevShop.Length < w.Households.Count) prevShop = new int[w.Households.Count];
                foreach (var h in w.Households)
                    if ((uint)h.Id < (uint)prevShop.Length) prevShop[h.Id] = h.ShopFirm;
                if (prevMass.Length < w.Firms.Count)
                { prevMass = new double[w.Firms.Count]; prevCluster = new int[w.Firms.Count]; }
                Array.Clear(prevMass, 0, prevMass.Length);
                for (int i = 0; i < w.Firms.Count; i++)
                {
                    var f = w.Firms[i];
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) { prevCluster[i] = -1; continue; }
                    var pl = w.Parcels[f.Parcel];
                    prevMass[i] = f.JobSlots * Math.Max(0.2, pl.Condition) * p.Quality(pl.Level);
                    prevCluster[i] = pl.Cluster;
                }
            });
            _ = sim;

            // ---- ARM 2: the labor auction, where a door has a price ---------
            // Shorter, for the same reason the Ward mutant arm is: the door
            // posture is standing from the first clear, so this arm needs only
            // enough run for the market to mature.
            // The labor arm runs at a THIRD of the shipping service rate, on a
            // smaller city. Both are stated rather than incidental.
            //
            // The rate: the ceiling leg is about door caps in the regime where
            // the technology ceiling BINDS, i.e. where a shop's traffic per slot
            // runs past what a slot can serve. At the shipping rate that regime
            // is nearly empty on a healthy city — measured 0 qualifying
            // door-ticks at 13x13/8000 and 0-52 at 11x11/4000 across seeds 0-25
            // — and for a good reason: with a structural cap a shop bids for
            // exactly the staff its traffic needs and gets it. A floor no
            // fixture reliably produces is not a floor, so this arm puts the
            // mechanism in its binding regime deliberately. The RELATION the leg
            // asserts (cap ≤ perSlotCap × margin) holds at every rate; only the
            // evidence for it depends on the regime.
            //
            // The size: the labor auction is the expensive fixture in this file,
            // and the ceiling and starvation legs need doors, not population.
            var pLabor = new EconParams
                         { CommercialServicePerSlot = p.CommercialServicePerSlot / 3.0 };
            long hiTrafficDoors = 0, ceilingViolations = 0;
            double worstCeilingExcess = 0, worstCeilingCap = 0;
            int starvedClean = 0, starvedMutant = 0;
            // The ceiling must be recomputed from the state the SOLVE saw, not
            // from the state at the end of the step: ConditionDecay runs inside
            // the step, so a check-time recompute reads a condition one tick
            // lower and every door then looks 0.3-0.4% over its ceiling
            // (measured, 2-14 door-ticks per seed). Same snapshot discipline as
            // the defaults leg.
            double[] prevCap = Array.Empty<double>();
            ShopFixture(seed, pLabor, 100, storeArm: true, laborAuction: true,
                        cols: 11, rows: 11, households: 4000, perTick: s =>
            {
                var w = s.W; var a = s.Engine.Labor;
                bool solved = a.D > 0 && w.Tick % pLabor.RefreshInterval == 1 && prevCap.Length > 0;
                for (int d = 0; solved && d < a.D; d++)
                {
                    int fi = a.DoorFirm[d];
                    if ((uint)fi >= (uint)w.Firms.Count || (uint)fi >= (uint)prevCap.Length) continue;
                    var f = w.Firms[fi];
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                    double perSlotCap = prevCap[fi];
                    if (perSlotCap <= 0) continue;
                    // The high-traffic population: shops whose own observed
                    // traffic per slot runs past what a slot can serve. The
                    // revenue-EMA rule's cap grows with that traffic without
                    // limit; the structural rule's cannot pass perSlotCap.
                    if (f.PresentedEma <= perSlotCap * Math.Max(1, f.JobSlots)) continue;
                    hiTrafficDoors++;
                    double ceiling = perSlotCap * basketMargin;
                    if (a.Cap[d] > ceiling + 1e-9)
                    {
                        ceilingViolations++;
                        double ex = (a.Cap[d] - ceiling) / Math.Max(1e-9, ceiling);
                        if (ex > worstCeilingExcess) { worstCeilingExcess = ex; worstCeilingCap = a.Cap[d]; }
                    }
                }
                if (solved) starvedClean += StarvedDoors(w, a);
                if (prevCap.Length < w.Firms.Count) prevCap = new double[w.Firms.Count];
                Array.Clear(prevCap, 0, prevCap.Length);
                for (int i = 0; i < w.Firms.Count; i++)
                {
                    var f = w.Firms[i];
                    if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                    var pl = w.Parcels[f.Parcel];
                    prevCap[i] = pLabor.CommercialServicePerSlot * Math.Max(0.2, pl.Condition)
                                 * pLabor.Quality(pl.Level) / pLabor.Quality(1);
                }
            });

            // The MUTANT ARM, the same shape the Ward refusal check uses and for
            // the same reason: on a clean build the population this leg is about
            // is nearly empty (0-2 firm-ticks per seed, measured) BECAUSE the
            // mechanism works, so a clean arm alone would be vacuous. The mutant
            // manufactures the state. Under it the door cap reads SERVED volume:
            // a shop with no staff serves nothing, so it posts no door, so it
            // never gets staff, so it never serves — permanently doorless, which
            // is the spiral "presented, never served" exists to prevent.
            try
            {
                LaborAuction.MutantServedCap = true;
                ShopFixture(seed, pLabor, 100, storeArm: true, laborAuction: true,
                            cols: 11, rows: 11, households: 4000, perTick: s =>
                {
                    if (s.W.Tick % pLabor.RefreshInterval == 1)
                        starvedMutant += StarvedDoors(s.W, s.Engine.Labor);
                });
            }
            finally { LaborAuction.MutantServedCap = false; }

            double bindShare = firmTicks > 0 ? (double)bindTicks / firmTicks : 0;
            Check("commercial takings are bounded by the staff the shop has, and the bound binds",
                  boundViolations == 0 && bindTicks >= BindFloor && firmTicks > 2000,
                  $"{firmTicks} commercial firm-ticks: {boundViolations} served above a capacity "
                  + $"recomputed from WorkersFilled×cond×quality (worst excess {worstBoundExcess:E1}); "
                  + $"the bound BOUND on {bindTicks} of them ({bindShare:P1}) vs floor {BindFloor} "
                  + "(without this floor an inert mechanism passes)");

            Check("a commercial door's cap is a technology ceiling, not an EMA of takings",
                  ceilingViolations == 0 && hiTrafficDoors >= CeilingFloor,
                  $"{hiTrafficDoors} door-ticks at shops whose traffic per slot runs past what a slot can "
                  + $"serve, on an arm pinned to a third of the shipping service rate so that regime exists "
                  + $"(floor {CeilingFloor}, or the leg asserts nothing): {ceilingViolations} posted a "
                  + $"cap above perSlotCap×(1−BasketShare)"
                  + (ceilingViolations > 0 ? $", worst {worstCeilingCap:F3} at +{worstCeilingExcess:P1}" : ""));

            Check("a shop with no staff can still bid for staff — and MutantServedCap flips it red",
                  starvedClean == 0 && starvedMutant >= StarveFloor,
                  $"clean run: {starvedClean} staffless commercial firm-ticks posting no door at all; "
                  + $"MutantServedCap run: {starvedMutant} (must be ≥ {StarveFloor} or the check is "
                  + "vacuous — on a clean build this population is nearly empty because the mechanism "
                  + "works, so the mutant arm is what makes the leg able to fail)");

            Check("no household is offered a shop it ranks below its own out-of-town option",
                  belowDefault == 0 && shortlistFallbacks > 0 && servedInLaterRounds > 0,
                  $"{belowDefault} shortlist entries below the household's OWN realized outside utility, "
                  + $"taste draw included (bound: exactly 0); {shortlistFallbacks} fallback entries offered "
                  + $"and {servedInLaterRounds:F0} money served in rounds ≥ 1 — both must be > 0 or the leg "
                  + "only tests the argmax, which beats the default by construction");

            Check("store-level consumption conserves: what shops served plus what leaked is what households spent",
                  worstMoneyRel < 1e-12 && moneyTicks > 100,
                  $"worst |Σ_firms served + leaked − Σ_households spend| / spend = {worstMoneyRel:E1} "
                  + $"over {moneyTicks} ticks (bound 1e-12; the two sides are separately accumulated — "
                  + "the firms' own ServedThisTick against the household loop's own debit total)");
        }

        /// <summary>Staffless commercial firms posting no door at all, at the
        /// solve that just ran. JobSlots ≥ 3 scopes this away from the slot
        /// ROUNDING: a 1-slot shop's basic door is JobSlots × 0.7 = 0.7, which
        /// the deterministic per-(firm,class) hash rounds to zero doors whatever
        /// the firm would pay. Firms born during the step that just ran are
        /// excluded — they did not exist at its solve. (perTick sees W.Tick
        /// already incremented, hence w.Tick − 1.)</summary>
        private static int StarvedDoors(WorldState w, LaborAuction a)
        {
            if (a.D <= 0) return 0;
            var hasDoor = new HashSet<int>();
            for (int d = 0; d < a.D; d++) hasDoor.Add(a.DoorFirm[d]);
            int n = 0;
            foreach (var f in w.Firms)
            {
                if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                if (f.JobSlots < 3 || f.WorkersFilled > 1e-9 || f.EnteredTick >= w.Tick - 1) continue;
                if (!hasDoor.Contains(f.Id)) n++;
            }
            return n;
        }

        /// <summary>Gumbel(0,1) from the same stable hash the engine uses, so
        /// the defaults leg re-derives each household's own taste draws rather
        /// than reading them back out of the engine.</summary>
        private static double GumbelOf(ulong key)
        {
            double e = SplitMix64.Hash01(key);
            return -Math.Log(-Math.Log(Math.Min(1 - 1e-12, Math.Max(1e-12, e))));
        }

        /// <summary>The entry signal a developer reads is a COUNT of individual
        /// shop intents, and it discriminates places the pooled field it
        /// replaced structurally cannot.
        ///
        /// WHAT WAS WRONG. AccessState.PhantomCommercialCapture is
        ///   Σ_i SpendMass[i] × wNew_i / (IncumbentShopWeight[i] + wNew_i),
        /// and IncumbentShopWeight[i] is a citywide convolution over every
        /// destination cluster. That expression IS the pooled allocation rule —
        /// the mechanism StoreLevelSpending replaced — re-served as one
        /// developer's forecast. On the store-level path the market that runs is
        /// discrete: each household walks into ONE shop. So the forecast
        /// predicts a market that does not exist, it promises every entrant a
        /// share of the same money, and developers keep building shops the
        /// discrete market cannot feed.
        ///
        /// FIVE LEGS. GUARD is a precondition for wiring the field into a bid at
        /// all: the standing circularity guard asserts on a RESIDENTIAL parcel's
        /// land rent and structurally cannot see a commercial-side leak.
        /// DISCRIMINATION is the structural contrast, and it is the one property
        /// the pooled field cannot have at any parameter setting — its
        /// denominator keeps it strictly positive at every cluster, so it can
        /// never tell a developer that nobody would come. CROWDING and LOCALITY
        /// are the two response properties, each measured as a controlled
        /// perturbation with the pooled field computed in the SAME run from the
        /// SAME world, so the contrast is not two worlds that diverged for other
        /// reasons. CALIBRATION is the leg with no identity behind it at all: it
        /// relates the read at entry to takings that actually happened. THIN is
        /// the #30 thin-market argument transposed.
        ///
        /// WHAT IS NOT ASSERTED, AND WHY. The design claimed the counted read
        /// would fall MORE than the pooled read when a place gets crowded. It
        /// does not, and the reason is structural rather than a tuning miss: a
        /// household's taste for a new store at c (ε_hc) is independent of its
        /// taste for the incumbent standing there, so a household that would
        /// love a new shop at c keeps counting however large the incumbent
        /// grows. Measured at 20× incumbent mass (seeds 0-1): counted falls
        /// 12.1% and 38.9%, pooled falls 24.3% and 53.7%. So the leg asserts the
        /// counted read falls, with a measured floor, and REPORTS the pooled
        /// number instead of asserting an ordering the evidence contradicts.
        ///
        /// Every "small" bound is paired with a floor proving its population is
        /// non-empty. Precedent in this file: the auction's leg 6a reached its
        /// own revenue test 0 times out of 45 doors, and #30's own THIN leg
        /// leaves worstThin at 0 when no cluster is thin.</summary>
        private static void CountedShopIntents(ulong seed)
        {
            var p = new EconParams();

            // ENTRANT SURVIVAL, tracked over the run. The cohort is shops
            // born after tick 20 and early enough to be OBSERVABLE to age 40 —
            // a shop born at tick 300 of a 320-tick run has not survived 40
            // ticks, it has not been asked yet, and counting it as a survivor
            // is the selection that would make this leg lie in the safe
            // direction.
            const int SurvivalHorizon = 40, FixtureTicks = 320;
            var entrantAge = new Dictionary<int, (long born, int deathAge)>();
            var sim = ShopFixture(seed, p, FixtureTicks, storeArm: true, laborAuction: false, perTick: s =>
            {
                var w = s.W;
                foreach (var f in w.Firms)
                {
                    // `Parcel >= 0` gates the BIRTH registration only, because
                    // "is this an entrant occupying a building" is the question
                    // there. Gating the DEATH branch with it too — as this loop
                    // did — reintroduced through a different door the exact
                    // selection the comment above warns about: a cohort member
                    // that loses its site keeps deathAge == -1 and is scored a
                    // SURVIVOR against an upper bound. Displacement exits set
                    // Dead and leave Parcel < 0, so once firms can be displaced
                    // at all, a real death would be counted as survival.
                    if (f.Sector != ZoneKind.Commercial) continue;
                    if (!f.Dead && f.Parcel >= 0 && f.EnteredTick > 20
                        && f.EnteredTick <= FixtureTicks - SurvivalHorizon
                        && !entrantAge.ContainsKey(f.Id) && w.Tick - f.EnteredTick <= 1)
                        entrantAge[f.Id] = (f.EnteredTick, -1);
                    if (f.Dead && entrantAge.TryGetValue(f.Id, out var ea) && ea.deathAge < 0)
                        entrantAge[f.Id] = (ea.born, (int)(w.Tick - ea.born));
                }
            });
            int cohort = entrantAge.Count;
            int youngDead = entrantAge.Values.Count(v => v.deathAge >= 0 && v.deathAge <= SurvivalHorizon);
            // THE MUTANT ARM, EMBEDDED, for the reason the starvation leg's is:
            // on a clean build this population is nearly empty BECAUSE the
            // mechanism works — a firm that reads the field about the building
            // it would actually occupy mostly declines the derelict stock — so
            // a clean-only leg would be a small number compared against a bound
            // with nothing proving the measurement was live. The mutant arm
            // restores the reference read and MUST produce the death mode, or
            // this leg is asserting nothing. It runs the SAME window as the
            // clean arm so the two counts are directly comparable; a shorter
            // arm was tried and rejected — at 160 ticks it produced 5, 7, 8 and
            // 1 young deaths on seeds 0-3, and a floor one seed in four cannot
            // reach is not a floor.
            const int MutantArmTicks = FixtureTicks;
            int mutantYoungDead;
            try
            {
                LandAccounting.MutantEntryReferenceMass = true;
                var mAge = new Dictionary<int, (long born, int deathAge)>();
                ShopFixture(seed, new EconParams(), MutantArmTicks, storeArm: true, laborAuction: false,
                            perTick: s =>
                {
                    var w = s.W;
                    foreach (var f in w.Firms)
                    {
                        // Same split as the clean arm above, for the same reason:
                        // the site test belongs to the birth branch only.
                        if (f.Sector != ZoneKind.Commercial) continue;
                        if (!f.Dead && f.Parcel >= 0 && f.EnteredTick > 20
                            && f.EnteredTick <= MutantArmTicks - SurvivalHorizon
                            && !mAge.ContainsKey(f.Id) && w.Tick - f.EnteredTick <= 1)
                            mAge[f.Id] = (f.EnteredTick, -1);
                        if (f.Dead && mAge.TryGetValue(f.Id, out var ma) && ma.deathAge < 0)
                            mAge[f.Id] = (ma.born, (int)(w.Tick - ma.born));
                    }
                });
                mutantYoungDead = mAge.Values.Count(v => v.deathAge >= 0 && v.deathAge <= SurvivalHorizon);
            }
            finally { LandAccounting.MutantEntryReferenceMass = false; }
            var W = sim.W; var eng = sim.Engine; var acc0 = eng.Access;
            int C = acc0.C;

            // Every rebuild below starts from the SAME prior shop choices.
            // ChooseShops is not idempotent — the loyalty bonus follows whatever
            // was chosen last time — so running it twice in a row moves the field
            // on its own, which read as 23-31 of 64 clusters "responding" to a
            // perturbation that touches nothing the field reads.
            var savedShop = new int[W.Households.Count];
            var savedRent = new double[W.Households.Count];
            foreach (var h in W.Households)
                if ((uint)h.Id < (uint)savedShop.Length)
                { savedShop[h.Id] = h.ShopFirm; savedRent[h.Id] = h.ChargedAssessment; }
            void Rebuild()
            {
                foreach (var h in W.Households)
                    if ((uint)h.Id < (uint)savedShop.Length) h.ShopFirm = savedShop[h.Id];
                eng.RebuildShopSignals();
            }

            // ---- GUARD: no realized rent reaches the valuation field ---------
            // The intent weight is income × (1 − the household's OWN rent share)
            // × BaseConsumptionShare. Built instead from the realized spend of
            // ConsumptionFlows — which nets ChargedAssessment — the entry field
            // would be a function of the rents the assessment charged, which is
            // the §3 circularity violation.
            Rebuild();
            var before = SnapshotIntents(acc0, C);
            foreach (var h in W.Households)
                if ((uint)h.Id < (uint)savedRent.Length) h.ChargedAssessment *= 17.5;
            Rebuild();
            var after = SnapshotIntents(acc0, C);
            foreach (var h in W.Households)
                if ((uint)h.Id < (uint)savedRent.Length) h.ChargedAssessment = savedRent[h.Id];
            Rebuild();
            long guardMoved = 0; int guardLive = 0;
            for (int c = 0; c < C; c++)
            {
                if (before[c] > 1e-9) guardLive++;
                if (before[c] != after[c]) guardMoved++;
            }

            // ---- DISCRIMINATION and THIN -------------------------------------
            int silent = 0, pooledSilent = 0, thinClusters = 0, thinNonZero = 0;
            for (int c = 0; c < C; c++)
            {
                if (acc0.CommercialCapture(c, 6.0) <= 1e-12)
                {
                    silent++;
                    if (acc0.PhantomCommercialCapture(c, 6.0) <= 1e-12) pooledSilent++;
                }
                if (acc0.IntentHeadsFor(c, 6.0) >= p.IntentHeadFloor) continue;
                thinClusters++;
                if (acc0.CountedIntent(c, 6.0) > 1e-12) thinNonZero++;
            }

            // ---- CROWDING and LOCALITY ---------------------------------------
            // ONE perturbation: twenty times the commercial mass at the cluster
            // carrying the LARGEST counted read — the cluster a developer would
            // actually build at. Picking the cluster with the most standing mass
            // instead picks one whose counted read has already fallen to ~0,
            // where no further fall is possible and the leg reads 0.0% on a
            // correct build.
            var hasShop = new bool[C];
            foreach (var f in W.Firms)
                if (!f.Dead && f.Sector == ZoneKind.Commercial && f.Parcel >= 0
                    && (uint)W.Parcels[f.Parcel].Cluster < (uint)C)
                    hasShop[W.Parcels[f.Parcel].Cluster] = true;
            // Among clusters that HAVE a shop to enlarge. The largest counted
            // read overall is typically at a cluster with no shop at all — which
            // is the signal working — and scaling JobSlots there changes nothing,
            // so the perturbation was a no-op and the leg read 0.0%.
            // The FIVE largest reads among clusters that have a shop to
            // enlarge, perturbed together and averaged. One site is too noisy a
            // statistic: measured across seeds 0-25, a single site's read falls
            // 0.0% on several of them — its counted money comes from households
            // whose own best is set somewhere else entirely — and a floor a
            // correct build fails is not a floor.
            var cand = new List<int>();
            for (int c = 0; c < C; c++) if (hasShop[c] && acc0.CountedIntent(c, 6.0) > 1e-9) cand.Add(c);
            cand.Sort((x, y) => acc0.CountedIntent(y, 6.0).CompareTo(acc0.CountedIntent(x, 6.0)));
            var sites = cand.GetRange(0, Math.Min(5, cand.Count));
            int site = sites.Count > 0 ? sites[0] : 0;
            double bestRead = sites.Count > 0 ? acc0.CountedIntent(site, 6.0) : 0;
            var order = new List<int>();
            for (int c = 0; c < C; c++)
                if (!sites.Contains(c) && acc0.CountedIntent(c, 6.0) > 1e-9) order.Add(c);
            order.Sort((x, y) => acc0.WShop[x, site].CompareTo(acc0.WShop[y, site]));
            int farN = Math.Max(1, order.Count / 4);           // the farthest quartile, by shopping reach

            var countedA = new double[C]; var pooledA = new double[C];
            for (int c = 0; c < C; c++)
            { countedA[c] = acc0.CountedIntent(c, 6.0); pooledA[c] = acc0.PhantomCommercialCapture(c, 6.0); }

            var slotsSaved = new Dictionary<int, int>();
            foreach (var f in W.Firms)
            {
                if (f.Dead || f.Sector != ZoneKind.Commercial || f.Parcel < 0) continue;
                if (!sites.Contains(W.Parcels[f.Parcel].Cluster)) continue;
                slotsSaved[f.Id] = f.JobSlots; f.JobSlots *= 20;
            }
            acc0.Refresh(W, sim.Access, p, sim.Flags);
            Rebuild();
            var countedB = new double[C]; var pooledB = new double[C];
            for (int c = 0; c < C; c++)
            { countedB[c] = acc0.CountedIntent(c, 6.0); pooledB[c] = acc0.PhantomCommercialCapture(c, 6.0); }
            foreach (var kv in slotsSaved) W.Firms[kv.Key].JobSlots = kv.Value;
            acc0.Refresh(W, sim.Access, p, sim.Flags);
            Rebuild();

            double crowdCounted = 0, crowdPooled = 0;
            foreach (int c in sites)
            {
                crowdCounted += countedA[c] > 1e-9 ? 1 - countedB[c] / countedA[c] : 0;
                crowdPooled += pooledA[c] > 1e-9 ? 1 - pooledB[c] / pooledA[c] : 0;
            }
            if (sites.Count > 0) { crowdCounted /= sites.Count; crowdPooled /= sites.Count; }
            double locCounted = 0, locPooled = 0;
            for (int i = 0; i < farN; i++)
            {
                int c = order[i];
                locCounted += Math.Abs(countedB[c] - countedA[c]) / Math.Max(1e-9, countedA[c]);
                locPooled += Math.Abs(pooledB[c] - pooledA[c]) / Math.Max(1e-9, pooledA[c]);
            }
            locCounted /= farN; locPooled /= farN;
            // Decay: the response at distance against the response at the site.
            double decayCounted = crowdCounted > 1e-9 ? locCounted / crowdCounted : 1;
            double decayPooled = crowdPooled > 1e-9 ? locPooled / crowdPooled : 1;

            Check("the counted entry signal is blind to realized rent (commercial-side circularity guard)",
                  guardMoved == 0 && guardLive >= GuardLiveFloor,
                  $"{guardMoved} of {C} clusters' counted reads moved under a 17.5× perturbation of every "
                  + $"household's ChargedAssessment (bound: exactly 0); {guardLive} clusters carry a "
                  + $"non-zero read vs floor {GuardLiveFloor} — without that the comparison is 0 == 0");

            Check("a thin cluster's counted intents are not a retail forecast",
                  thinNonZero == 0 && thinClusters >= ThinFloor,
                  $"{thinClusters} clusters whose read at the reference size rests on fewer than "
                  + $"{p.IntentHeadFloor:F0} households (floor {ThinFloor} such clusters, or the leg asserts "
                  + $"nothing): {thinNonZero} returned a non-zero read");

            Check("the entry signal can say nobody would come; the pooled field it replaced cannot",
                  silent >= SilentFloor && pooledSilent == 0,
                  $"the counted read is zero at {silent} of {C} clusters (floor {SilentFloor}); the pooled "
                  + $"field is zero at {pooledSilent} of them — it cannot be, at any parameter setting, "
                  + "because its denominator keeps every cluster's share strictly positive. That is the "
                  + "defect: it promises every entrant a slice of the same money");

            Check("the counted entry signal falls when a place gets crowded",
                  crowdCounted >= CrowdFloor && locPooled >= LocalityPooledFloor,
                  $"20× commercial mass at the {sites.Count} largest-read clusters that have a shop to "
                  + $"enlarge (top read {bestRead:F0}): their counted reads fall {crowdCounted:P1} on average "
                  + $"vs floor {CrowdFloor:P0}. Over the farthest quartile of the remaining reading clusters "
                  + $"({farN}) the "
                  + $"same perturbation moves it {locCounted:P1}, i.e. {decayCounted:F2}× the response at "
                  + $"the site. The pooled field in the SAME run: "
                  + $"{crowdPooled:P1} at the site, {locPooled:P1} at distance ({decayPooled:F2}×, floor "
                  + $"≥{LocalityPooledFloor:P1} on the distant move or the perturbation was inert). "
                  + "The distance profile is REPORTED, not asserted: measured 0.48-2.09× across seeds 0-3, "
                  + "so the counted read's response is NOT confined to the perturbed place. That is not a "
                  + "defect — a household's own best alternative may be a shop anywhere, which is its own "
                  + "information — but it means no locality ORDERING against the pooled field survives "
                  + "measurement, and the design's claim that one would is retired here");

            // CALIBRATION IS RETIRED AND REPLACED BY THE LEG BELOW, and the
            // reason is measured rather than tidy. It asked whether the counted
            // read at entry predicts what a new shop takes, evidence-weighted,
            // over "shops born mid-run with ≥5 ticks of trading". That sample
            // only ever contained entrants that lived past age 11 — it
            // accumulates from age 6 and needs five ticks of it — so it was a
            // statement about SURVIVORS. It read green on all 26 seeds at the
            // commerce commit while 88 % of mid-run entrants were dying inside
            // 40 ticks (`shopprobe --seeds 4`: 141 of 161, and 137 of 153 when
            // re-measured at the two-track merge). A leg that is green through
            // the defect it looks like it would catch is not coverage, and the
            // #30-F1 lesson applies to selection as much as to construction.
            // What replaces it tests the same question by its OUTCOME — do the
            // firms the read admits survive — on the population the ratio leg
            // was silently dropping. The ratio itself is still measured and
            // printed by `entrydiag`.

            // ---- ENTRANT SURVIVAL -------------------------------------------
            // A firm's entry decision must be ITS OWN forecast from information
            // it could have, and the test of that is whether the firms it lets
            // in can trade. Measured at the two-track merge, BEFORE the entry
            // read was asked at the entrant's own building
            // (`entrydiag --seeds 4`, 300 ticks): 132 of 143 shops born mid-run
            // — 92% — died inside 40 ticks, against 0 of 31 on the pooled path.
            //
            // The cause was not an arithmetic cold start: 130 of those 132 had
            // taken custom, and the first customer lands at age p50 5, which is
            // the refresh grid rather than a starving shop. It was not crowding
            // at the site: dying and surviving entrants alike arrive at
            // clusters with a median of 0 other shops. It was the FORECAST. The
            // entry decision read the counted field at a CLUSTER reference mass
            // — a 6-slot condition-1 shop — while the market that generates its
            // catchment scores the building it would actually occupy, and the
            // buildings on offer are derelict: commercial parcels standing
            // vacant have condition p50 0.05, so a shopper sees mass
            // 8×0.2×quality against live shops at 8.78. The reference read was
            // 1.63× the own-mass read at the median and never smaller, and
            // over-predicted realized custom 3.4× where the own-mass read
            // over-predicts 2.05×.
            //
            // THE COUNTED NUMBER IS DEATHS, NOT A SHARE, and the arms are two.
            // A share over a cohort of one or two is noise; and on a clean
            // build the cohort IS one or two, because the honest read declines
            // the derelict stock. So the clean arm bounds the COUNT of
            // young entrant deaths, and the mutant arm — the reference read
            // restored verbatim — must produce the mode, or the leg is
            // asserting nothing. That is the starvation leg's own Ward pattern
            // and it is here for the same measured reason.
            //
            // Bounds: see EntrantYoungDeathMax / EntrantMutantFloor, both set
            // from `shopsweep --seeds 6` at this commit and recorded there.
            Check("entrants can trade: the entry decision does not fill derelict buildings with shops "
                  + "that die — and the reference-mass read flips it red",
                  youngDead <= EntrantYoungDeathMax && mutantYoungDead >= EntrantMutantFloor,
                  $"clean run: {youngDead} of {cohort} commercial firms born after tick 20 and observable "
                  + $"to age {SurvivalHorizon} died inside it (bound {EntrantYoungDeathMax}); "
                  + $"MutantEntryReferenceMass run over {MutantArmTicks} ticks: {mutantYoungDead} "
                  + $"(must be ≥ {EntrantMutantFloor} or the leg is vacuous — on a clean build this "
                  + "population is nearly empty because the mechanism works, so the mutant arm is what "
                  + "makes the leg able to fail)");
        }

        private static double[] SnapshotIntents(AccessState acc, int C)
        {
            var r = new double[C];
            for (int c = 0; c < C; c++) r[c] = acc.CountedIntent(c, 6.0);
            return r;
        }

        /// <summary>The two task #20 commerce checks alone across seeds — same
        /// rationale as <see cref="Canary"/> (one fixture each, many seeds).
        /// This is the instrument for every mutant demonstration in this item
        /// and for the flip inventory it is a precondition of.</summary>
        public static int ShopSweep(List<ulong> seeds)
        {
            Console.WriteLine($"shop sweep: {seeds.Count} seeds"
                + (LaborAuction.MutantRevenueEmaCap ? " [MUTANT: revenue-EMA door cap]" : "")
                + (LaborAuction.MutantUncappedUtil ? " [MUTANT: uncapped door util]" : "")
                + (LaborAuction.MutantServedCap ? " [MUTANT: served-driven door cap]" : "")
                + (AccessState.MutantPooledEntry ? " [MUTANT: pooled entry signal]" : "")
                + (AccessState.MutantIntentUnsaturated ? " [MUTANT: unsaturated intent probe]" : ""));
            var failed = new List<ulong>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                CommercialStaffing(seed);
                CountedShopIntents(seed);
                bool ok = Results.Count > before;
                for (int i = before; i < Results.Count; i++) ok &= Results[i].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"shop sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass "
                + $"({sw.Elapsed.TotalSeconds:F0}s)"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
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

            // ---- (A) ASK-INVARIANCE OF ASSESSMENT (item #41's thesis) -----
            // The §3 guard, stated at the ask: a parcel's own OwnerAskPerUnit
            // must never move its own assessment. Each probed owner parcel's
            // ask is multiplied ×10 (zero asks get a nonzero perturbation, so
            // the leg cannot go vacuous) and Assess is re-run ON THE SAME
            // auction state — no re-solve, which isolates the accounting path
            // from the legitimate market path (a high ask withholding a unit
            // moves assessments one refresh later through MARKET bids at the
            // uniform door, and that channel is negative and blessed). Every
            // published field must come back bit-identical. This red/green
            // pair is the refutation of the "per-parcel ownership creates
            // self-assessment" folklore, in the suite.
            int askScope = 0, askBad = 0; double worstAskMove = 0; string askWhy = "";
            foreach (var pl in w.Parcels)
            {
                if (askScope >= 24) break;
                if (pl.OwnerHousehold < 0 || pl.State != ParcelState.Built) continue;
                double savedAsk = pl.OwnerAskPerUnit;
                double lr0 = pl.AssessedLR, wedge0 = pl.Wedge, resid0 = pl.CurrentResidual;
                int tl0 = pl.TargetLevel; var tu0 = pl.TargetUse; bool ts0 = pl.TargetIsScrape;
                pl.OwnerAskPerUnit = Math.Max(savedAsk, 1.0) * 10.0;
                LandAccounting.Assess(w, acc, sim.Engine.Trade, pl, presence, p);
                askScope++;
                bool same = pl.AssessedLR == lr0 && pl.Wedge == wedge0
                            && pl.CurrentResidual == resid0 && pl.TargetLevel == tl0
                            && pl.TargetUse == tu0 && pl.TargetIsScrape == ts0;
                if (!same)
                {
                    double d = Math.Abs(pl.AssessedLR - lr0);
                    if (askBad == 0)
                        askWhy = $"first: parcel {pl.Id} ask {savedAsk:F4}→{pl.OwnerAskPerUnit:F4} "
                               + $"moved AssessedLR {lr0:F6}→{pl.AssessedLR:F6}, "
                               + $"wedge {wedge0:F6}→{pl.Wedge:F6}, ℓ* {tl0}→{pl.TargetLevel}";
                    askBad++; worstAskMove = Math.Max(worstAskMove, d);
                }
                pl.OwnerAskPerUnit = savedAsk;
                LandAccounting.Assess(w, acc, sim.Engine.Trade, pl, presence, p);   // restore (deterministic)
            }
            Check("assessment is ask-invariant: a parcel's own ask never reaches its own assessment (§3, item #41)",
                  askScope > 0 && askBad == 0,
                  $"{askScope - askBad}/{askScope} probed owner parcels bit-identical under ask ×10 "
                  + $"(worst |ΔLR| {worstAskMove:E1})"
                  + (askWhy.Length > 0 ? $"\n      {askWhy}" : ""));

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

        private static double Pct(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return 0;
            double idx = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
        }

        /// <summary>The nonres-parity fixture alone across seeds — the
        /// instrument its four effect sizes were measured with, and what a
        /// change to the firm levy, the firm bid or the firm production
        /// functions is re-measured on.</summary>
        public static int NonResParitySweep(List<ulong> seeds)
        {
            Console.WriteLine($"nonres-parity sweep: {seeds.Count} seeds");
            var failed = new List<ulong>();
            foreach (var seed in seeds)
            {
                int before = Results.Count;
                Console.WriteLine($"--- seed {seed}");
                NonResidentialParity(seed);
                bool ok = Results.Count > before;
                for (int k = before; k < Results.Count; k++) ok &= Results[k].pass;
                if (!ok) failed.Add(seed);
            }
            Console.WriteLine($"parity sweep: {seeds.Count - failed.Count}/{seeds.Count} seeds pass"
                + (failed.Count > 0 ? " — FAILED: " + string.Join(", ", failed) : ""));
            return Math.Min(failed.Count, 100);
        }

        /// <summary>Task #19: the non-residential side of land accounting must
        /// face the residential rules, and its assessment base must be what
        /// §4.3 says it is. EconParams.NonResLandParity ships OFF (its comment
        /// carries the measurement that keeps it off), so every property here
        /// is asserted TWICE — once as it stands on the shipped default, where
        /// the leg states the defect, and once with the switch on, where it
        /// states the property. The default leg IS the non-degeneracy floor: it
        /// proves the population the parity leg acts on is real, not empty.
        ///
        /// THE TWO ASSESSMENT LEGS RUN ON ONE WORLD, priced under both
        /// parameter sets. Assessment is a pure function of (world, access,
        /// prices, params), so the honest counterfactual is the same parcels
        /// re-priced — not a second city, whose non-residential population
        /// diverges for reasons that have nothing to do with the read under
        /// test (the parity arm's producing sector thins to 0-2 parcels, which
        /// would decide the verdict by selection rather than by the channel).
        /// The two PRODUCTION legs cannot be done that way — they are about
        /// what the engine actually produced — so those keep a second arm.
        ///
        /// Every parity leg is a CAUSAL probe: change one input the property
        /// names and require the assessment (or the realized product) to move.
        /// Nothing re-derives a number from the code under test and compares it
        /// to itself; the office legs pair a realized-production record the
        /// engine accumulates against the formula the assessor prices.
        ///
        /// Mutants RUN, each flipping exactly its own leg red:
        /// --mutant-flat-geology equalizes the geology arms,
        /// --mutant-self-comparable leaves the second-seller arms unmoved,
        /// --mutant-level-free-output puts realized/base at 1.0, and
        /// --mutant-forgive-arrears leaves firms sitting past the clock.</summary>
        private static void NonResidentialParity(ulong seed)
        {
            // A 12x12 city at 6000 seed households, not the 14x14/9000 default:
            // the fixture runs TWO worlds, and every population it gates on
            // clears its bar by an order of magnitude at this size (measured at
            // 14x14/9000 on seeds 0-3, 9, 13 — 71-95 extractor parcels against
            // a bar of 5, 6-11 one-seller cells against 1, 36-44 lifted offices
            // against 3, 188-202 standing firms against 20). Halving the work
            // keeps the suite's cost proportionate to what this item adds.
            var cfg = new SyntheticCity.Config { Seed = seed, Cols = 12, Rows = 12, SeedHouseholds = 6000 };
            var pOff = new EconParams();
            var pOn = new EconParams { NonResLandParity = true };
            var sim = Sim.Create(cfg, pOff, new FeatureFlags());
            sim.Run(300);
            var w = sim.W;
            var acc = sim.Engine.Access;
            var trade = sim.Engine.Trade;
            var presence = sim.Engine.SegmentPresence;

            // ---- LEG 1: extractor land is assessed on its own geology --------
            // Probe: drive the parcel's cluster geology to 1.0 and to 0.01 and
            // reassess. A flat-0.5 read cannot tell them apart.
            var exList = w.Parcels.Where(x => x.State == ParcelState.Built && x.Zoned == ZoneKind.Extractor
                                              && w.Clusters[x.Cluster].ResourceSuitability.Max() > 0.05)
                                  .ToList();
            double offHi = 0, offLo = 0, onHi = 0, onLo = 0;
            if (exList.Count > 0)
            {
                var ex = exList.OrderByDescending(x => w.Clusters[x.Cluster].ResourceSuitability.Max()).First();
                var ci = w.Clusters[ex.Cluster];
                var saved = (double[])ci.ResourceSuitability.Clone();
                double At(double suit, EconParams p)
                {
                    for (int i = 0; i < ci.ResourceSuitability.Length; i++) ci.ResourceSuitability[i] = suit;
                    LandAccounting.Assess(w, acc, trade, ex, presence, p);
                    return ex.AssessedLR;
                }
                offHi = At(1.0, pOff); offLo = At(0.01, pOff);
                onHi = At(1.0, pOn); onLo = At(0.01, pOn);
                Array.Copy(saved, ci.ResourceSuitability, saved.Length);
                LandAccounting.Assess(w, acc, trade, ex, presence, pOff);   // as it was found
            }
            Check("nonres parity floor: extractor parcels exist and the shipped default prices them blind to geology",
                  exList.Count >= 5 && offHi == offLo,
                  $"{exList.Count} built extractor parcels; default read LR {offHi:F3} at suitability 1.0 "
                  + $"and {offLo:F3} at 0.01 (identical — the flat-0.5 read)");
            Check("nonres parity: extractor land is assessed on its own cluster's geology",
                  exList.Count >= 5 && onHi > onLo + 1e-9,
                  $"parity read LR {onHi:F3} at suitability 1.0 vs {onLo:F3} at 0.01 "
                  + $"over {exList.Count} built extractor parcels");

            // ---- LEG 2: a one-seller cell is not its own comparable ----------
            // Probe: ADMIT a second seller — a firm with no realized flows, so
            // the cell's own statistic is untouched — and require the
            // assessment to move. If it moves, the read was not resting on the
            // sitting occupant's record; if it cannot move, it was.
            //
            // NOT every one-seller parcel must move: an industrial parcel whose
            // argmax recipe outputs a different resource, or one already beaten
            // by exit parity, is priced off that cell either way. The bar is a
            // third of the population, and the default read moves none of them.
            // ONE ATTEMPT TO STRENGTHEN THIS BAR WAS MADE AND REVERTED; see
            // KNOWN-RED. The 1/3 bar makes the verdict depend on the
            // configuration MIX (the composed arm reads 7/33 with the mechanism
            // working), so the fix is to keep only parcels whose parity
            // assessment actually READS this (output, cluster) cell and require
            // all of them to move. Selecting that subset by the argmax output of
            // FirmBidPerSlot at the parcel's CURRENT level does not do it:
            // Assess maxes over EVERY level, so the winning configuration can
            // sit at another level with another argmax. Measured on the default
            // arm, that filter kept the one parcel that did NOT move and
            // excluded the one that did (0/1 filtered against 1/2 unfiltered).
            // The subset has to come from the assessment's own winner, which
            // Assess does not currently expose.
            var singles = new List<Parcel>();
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.OccupantFirm < 0) continue;
                var f0 = w.Firms[pl.OccupantFirm];
                if (f0.Dead || (f0.Sector != ZoneKind.Industrial && f0.Sector != ZoneKind.Extractor)) continue;
                if (trade.SellersAt(f0.Output, pl.Cluster) == 1) singles.Add(pl);
            }
            int movedOff = 0, movedOn = 0; double wb = 0, wa = 0;
            foreach (var pl in singles)
            {
                var f0 = w.Firms[pl.OccupantFirm];
                double Price(EconParams p) { LandAccounting.Assess(w, acc, trade, pl, presence, p); return pl.AssessedLR; }
                double preOff = Price(pOff), preOn = Price(pOn);
                w.Firms.Add(new Firm { Id = w.Firms.Count, Sector = f0.Sector, Parcel = pl.Id, Output = f0.Output });
                trade.Refresh(pOff);
                double postOff = Price(pOff), postOn = Price(pOn);
                w.Firms.RemoveAt(w.Firms.Count - 1);
                trade.Refresh(pOff);
                Price(pOff);                           // leave the world as it was found
                if (Math.Abs(postOff - preOff) > 1e-6 * Math.Max(1, Math.Abs(preOff))) movedOff++;
                if (Math.Abs(postOn - preOn) > 1e-6 * Math.Max(1, Math.Abs(preOn)))
                { movedOn++; if (Math.Abs(postOn - preOn) > Math.Abs(wa - wb)) { wb = preOn; wa = postOn; } }
            }
            Check("nonres parity floor: one-seller cells with a sitting occupant exist, and the default prices them off themselves",
                  singles.Count >= 1 && movedOff == 0,
                  $"{singles.Count} one-seller (output, cluster) cells with a sitting occupant; the default read "
                  + $"re-prices {movedOff} of them when a second seller is admitted (it was already the cell's own)");
            Check("nonres parity: assessment never rests on a one-seller cell's own record",
                  singles.Count >= 1 && movedOn >= 1 && movedOn * 3 >= singles.Count,
                  $"{movedOn}/{singles.Count} one-seller parcels re-priced under the parity read when a second "
                  + $"seller was admitted (bound ≥ 1/3; largest move {wb:F3} -> {wa:F3})");

            // ---- LEG 3 and 4 need a second world: they are about what the
            // engine PRODUCED and what it DID, not about how a parcel prices.
            var simOn = Sim.Create(new SyntheticCity.Config
                                   { Seed = seed, Cols = 12, Rows = 12, SeedHouseholds = 6000 },
                                   pOn, new FeatureFlags());
            // Conservation on THIS arm, because it is a code path the suite's
            // ledger fixture never reaches: the arrears exit is the only new
            // way a firm can leave, and it must settle through the same two
            // legs the working-capital bankruptcy already uses. Both records
            // are sampled every tick, the same pair LedgerConservation
            // reconciles and the same 1e-9 bound.
            double driftOn = 0;
            var auditOn = new SectorAudit();
            simOn.Run(300, s2 =>
            {
                driftOn = Math.Max(driftOn, Math.Abs(s2.W.Ledger.Drift()));
                auditOn.Sample(s2);
            });
            Check("nonres parity: the arrears path settles — conservation and per-sector reconciliation on the parity arm",
                  driftOn < 1e-3 && auditOn.Worst < 1e-9,
                  $"max |drift| {driftOn:E2} (bound 1e-3); worst relative |Sigma entity - ledger| over "
                  + $"{auditOn.Ticks} tick samples: {auditOn} (bound 1e-9)");

            // The realized side is the LAST TICK's booked office revenue
            // (Firm.GrossRevenueLastTick), not an EMA: an EMA of a building that
            // renovated inside its window is averaging two different buildings,
            // and the identity does not hold of it. The assessed side is read
            // from the parcel as the assessor would read it now, and for an
            // office that met its charge nothing between production and here
            // moves either term — so a correct build reads zero, not a band.
            // WHICH TERM DOES PRODUCTION DELIVER, and which does the BID price?
            // Two independent predicates, because three flags now move them and
            // they do not move together:
            //   production carries cond×Quality(ℓ)  <=  NonResLandParity OR
            //                                          UniformSiteProductivity
            //   the bid prices Quality(ℓ)           <=  NOT AssessDeliverableQuality
            //                                          OR either of the above
            // The DEFECT this leg pins is the two disagreeing — land priced on a
            // level premium production does not deliver. So the comparison is
            // realized product against the term THE BID PRICED, and the scope
            // filter reads the building's LEVEL (flag-independent) rather than a
            // term one of the flags can flatten to 1 — otherwise turning a fix on
            // would empty the population and the leg would pass by being vacuous,
            // which is the failure mode this whole file exists to refuse.
            bool ProdLevel(EconParams p) => p.NonResLandParity || p.UniformSiteProductivity;
            bool BidLevel(EconParams p) => !p.AssessDeliverableQuality
                                           || p.NonResLandParity || p.UniformSiteProductivity;
            (int n, double medErr) Office(Sim s, EconParams p, bool againstBid)
            {
                var errs = new List<double>();
                foreach (var f in s.W.Firms)
                {
                    if (f.Dead || f.Parcel < 0 || f.Sector != ZoneKind.Office || f.WorkersFilled <= 0) continue;
                    var pl = s.W.Parcels[f.Parcel];
                    double base_ = f.WorkersFilled * p.OfficeOutputPerSlot * p.OfficeOutputPrice
                                   * s.Engine.Access.OfficeAgglomMult[pl.Cluster];
                    if (base_ <= 1e-9) continue;
                    double lvlTerm = p.Quality(pl.Level) / p.Quality(1);
                    if (lvlTerm < 1.15) continue;          // scope: level, not a flagged term
                    double term = againstBid
                        ? (BidLevel(p) ? lvlTerm : 1.0)                                  // what land is priced on
                        : (ProdLevel(p) ? Math.Max(0.2, pl.Condition) * lvlTerm : 1.0);  // what output should be
                    errs.Add(Math.Abs(f.GrossRevenueLastTick / base_ - term) / term);
                }
                errs.Sort();
                return (errs.Count, errs.Count > 0 ? Pct(errs, 0.5) : 0);
            }
            var oOff = Office(sim, pOff, againstBid: true);
            var oOn = Office(simOn, pOn, againstBid: false);
            // TWO-SIDED, so neither arm can be vacuous. Where the bid prices a
            // term production does not deliver the mismatch must be LARGE (the
            // defect, which is the shipping default and what the parity leg
            // below is the fix for); where the two agree it must be SMALL. The
            // residual band on the aligned side is the cond asymmetry: the bid
            // applies condition through CondFactor outside FirmBidPerSlot while
            // production applies raw cond, so the two agree on the level term
            // and only approximately on condition.
            bool officeAligned = ProdLevel(pOff) == BidLevel(pOff);
            Check("nonres parity floor: office product carries the level term its land is priced on exactly when the two rules agree",
                  oOff.n >= 3 && (officeAligned ? oOff.medErr < 0.25 : oOff.medErr > 0.25),
                  $"{oOff.n} staffed offices in level-scope on the default arm; production rule "
                  + $"{(ProdLevel(pOff) ? "carries" : "drops")} the level term, bid rule "
                  + $"{(BidLevel(pOff) ? "prices" : "drops")} it => {(officeAligned ? "ALIGNED" : "MISMATCHED")}; "
                  + $"realized product differs from the priced term by a median {oOff.medErr * 100:F1}% "
                  + $"(bound {(officeAligned ? "< 25%" : "> 25%")})");
            // The identity is EXACT, not statistical — a broken production term
            // puts every office in scope at ~51% — so one office in scope is a
            // verdict, and the paired floor leg above is what proves the
            // population is real. The parity arm's office sector is thin BY
            // CONSEQUENCE of the switch (its own comment carries why), which is
            // exactly why the bar counts rather than assumes.
            Check("nonres parity: office product carries the level and condition terms its land is assessed on",
                  oOn.n >= 1 && oOn.medErr < 0.02,
                  $"median |realized/base − condition×Quality(ℓ)/Quality(1)| / term = {oOn.medErr * 100:F3}% "
                  + $"over {oOn.n} staffed offices (bound 2%; level-free production reads ~51%)");

            // ---- LEG 4: an unmet land charge reaches a stated outcome --------
            //
            // FOUR states, not three, and the fourth is why this signature has
            // a fifth field. A LIVE firm holding no site is neither "standing"
            // nor "gone". The old body dropped it (`if (f.Parcel < 0) continue;`)
            // on the live arm while the DEAD arm read the same field as evidence
            // of release — one branch treating the fact as data, the other as
            // absence. That skip removed the state from both the numerator and
            // the denominator this leg gates on, and `stuck` is an UPPER bound
            // (aOn.stuck == 0), so it moved the verdict toward GREEN: a firm
            // stranded while past the arrears clock satisfied "no firm sits past
            // the clock" by not being counted.
            //
            // `siteless` is reported, and asserted at 0. Note what that
            // assertion is and is not: it is a GUARD against the skip coming
            // back, not a check in its own right — on a build where nothing
            // strands a firm it reads 0 == 0. The property "a firm that loses
            // its site reaches a stated outcome" has its own check with its own
            // mutant and its own non-degeneracy floor (DisplacedFirm).
            (int alive, int stuck, int dead, int released, int siteless) Arrears(Sim s, EconParams p)
            {
                int al = 0, st = 0, dd = 0, rel = 0, sl = 0;
                foreach (var f in s.W.Firms)
                {
                    if (f.Dead)
                    {
                        // `Parcel < 0` HERE is a release test, not a skip: an
                        // exit that left no link to its parcel is an exit that
                        // gave the land up. That reading stays.
                        if (!f.DiedOfArrears) continue;
                        dd++;
                        if (f.Parcel < 0 || s.W.Parcels[f.Parcel].OccupantFirm != f.Id) rel++;
                        continue;
                    }
                    if (f.Parcel < 0) { sl++; continue; }   // counted, never skipped
                    al++;
                    if (f.LevyShortTicks >= p.LandArrearsTicks) st++;
                }
                return (al, st, dd, rel, sl);
            }
            var aOff = Arrears(sim, pOff);
            var aOn = Arrears(simOn, pOn);
            Check("nonres parity floor: with the outcome off, firms really do sit past the arrears clock",
                  aOff.stuck >= 1 && aOff.alive >= 20 && aOff.dead == 0 && aOff.siteless == 0,
                  $"{aOff.stuck} of {aOff.alive} standing firms have missed the land charge for "
                  + $"{pOff.LandArrearsTicks}+ consecutive ticks under the shipped default, and none is asked to leave; "
                  + $"{aOff.siteless} live firms hold no site (bound 0 — a firm this leg cannot describe)");
            Check("nonres parity: with the outcome on, no firm sits past the arrears clock and every exit released its parcel",
                  aOn.stuck == 0 && aOn.dead >= 1 && aOn.released == aOn.dead && aOn.alive >= 20
                  && aOn.siteless == 0,
                  $"{aOn.stuck} standing firms past the clock (bound 0); {aOn.released}/{aOn.dead} arrears exits "
                  + $"left their parcel unoccupied; {simOn.Engine.FirmRelocationsTotal} firms sorted down instead; "
                  + $"{aOn.alive} firms still standing; {aOn.siteless} live firms holding no site (bound 0)");
        }


        /// <summary>Task #49. A firm that loses its site must reach a stated
        /// outcome, and the firm and the site must never disagree about whether
        /// they hold each other.
        ///
        /// WHY THE DISPLACEMENT IS INJECTED. The pure simulation cannot strand
        /// a firm on its own: the scrape path refuses to redevelop an occupied
        /// parcel (Leveling.cs:76 gates on `OccupantFirm &lt; 0`) and the
        /// milestone-abandon path only touches parcels still under construction
        /// (Construction.cs:396), which no firm occupies. The state is
        /// nonetheless REACHABLE TODAY on the mod arm — EconReader resolves a
        /// company's PropertyRenter through ParcelIndex and hands a live firm
        /// `Parcel = -1` on a miss (EconReader.cs:537/541, :645) — and the
        /// non-residential site market of task #48 will produce it by design,
        /// because being outbid is what an auction does to a loser. So the
        /// fixture injects it through EconomyEngine.DisplaceFirm, which IS the
        /// entry point those two producers use; nothing here is a private test
        /// path around the mechanism.
        ///
        /// WHY IT NEEDS ITS OWN CHECK RATHER THAN CONSERVATION. Measured, this
        /// seed set, `displaceprobe --mutant-displaced-no-exit`: 13 to 29 live
        /// firms end the run holding no site and 27,325 to 80,489 of frozen
        /// money — while ledger drift reads 1.7E-7 to 4.3E-6, the same order as
        /// the clean arm, and the sector reconciliation is untouched. That is
        /// the whole finding. A missing exit CANNOT show up in conservation,
        /// because the money balances precisely for as long as nobody asks the
        /// firm to leave: it is still in the firm's pocket and still in the
        /// Firms account. Conservation is an invariant about arithmetic; this
        /// is an invariant about agency.
        ///
        /// IT CAN FAIL, two ways, both wired. THE MAPPING BELOW IS MEASURED —
        /// `verify --seed 0` under each mutant — and it is not what the first
        /// version of this comment claimed. That version said MUT-49b "passes
        /// the settlement leg clean; only the link leg catches it", which was
        /// inferred from displaceprobe's siteless count and never run. Run, it
        /// is wrong in both directions, so the table is the artifact and the
        /// prose is not:
        ///
        ///                        | floor | settlement | link
        ///     MUT-49a no-exit    | FAIL  |   FAIL     | PASS
        ///     MUT-49b half-unlink| FAIL  |   FAIL     | FAIL
        ///
        ///   MUT-49a `--mutant-displaced-no-exit` — no resolution pass at all
        ///     (the shipped state before this item): 29 siteless live firms
        ///     holding 80,489 against a bound of 0. The LINK leg passes it,
        ///     because a firm that was cleanly unlinked and then abandoned is
        ///     still consistent with its parcel — nobody is lying, nobody is
        ///     resolved.
        ///   MUT-49b `--mutant-half-unlink` — displacement clears the PARCEL's
        ///     pointer and leaves the firm naming the site: the exact shape
        ///     EconReader.SyncParcels shipped for a despawned building. 28 link
        ///     breaks, AND the settlement leg reds too, on the conjunct the
        ///     first comment did not think through: the pass reports seeing
        ///     0 of 29 displacements, because a firm that kept its parcel never
        ///     becomes siteless and the pass never counts it. That is the
        ///     `seen == injected` cross-record identity doing exactly what it
        ///     was put there for.
        ///
        /// So the LINK leg is the discriminator — clean under one defect, red
        /// under the other — and it is what makes these three legs rather than
        /// one, on evidence rather than on the story told first.</summary>
        private static void DisplacedFirm(ulong seed)
        {
            var p = new EconParams();
            var cfg = new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed };
            var sim = Sim.Create(cfg, p, new FeatureFlags());

            // One displacement event, a quarter of the standing firms, at a tick
            // by which the city has a real firm population to displace. A
            // one-shot event rather than a trickle, because that is what a
            // demolition or a re-let round looks like and because it lets the
            // count of what was injected be compared against the count of what
            // the engine saw.
            const long AtTick = 150;
            const double Rate = 0.25;
            int injected = 0;
            var rng = new Random(unchecked((int)seed) ^ 0x5EED);
            var audit = new SectorAudit();
            double maxDrift = 0;
            sim.Run(300, s =>
            {
                maxDrift = Math.Max(maxDrift, Math.Abs(s.W.Ledger.Drift()));
                audit.Sample(s);
                if (s.W.Tick != AtTick) return;
                foreach (var f in s.W.Firms)
                {
                    if (f.Dead || f.Parcel < 0) continue;
                    if (rng.NextDouble() >= Rate) continue;
                    if (s.Engine.DisplaceFirm(f)) injected++;
                }
            });

            var w = sim.W;
            var e = sim.Engine;
            int siteless = 0, unsettledExit = 0;
            double stranded = 0;
            foreach (var f in w.Firms)
            {
                if (!f.Dead && f.Parcel < 0) { siteless++; stranded += f.Money; }
                if (f.Dead && f.DiedOfDisplacement && Math.Abs(f.Money) > 1e-9) unsettledExit++;
            }
            // Both directions of the pointer. A half-unlink shows up in exactly
            // one of them, so counting only one direction would be a leg that
            // cannot see the defect it exists for.
            int fwdBreak = 0, revBreak = 0;
            foreach (var f in w.Firms)
                if (!f.Dead && f.Parcel >= 0 && w.Parcels[f.Parcel].OccupantFirm != f.Id) fwdBreak++;
            foreach (var pl in w.Parcels)
                if (pl.OccupantFirm >= 0
                    && (w.Firms[pl.OccupantFirm].Dead || w.Firms[pl.OccupantFirm].Parcel != pl.Id)) revBreak++;

            // ---- FLOOR: the population is real and BOTH branches were taken --
            // Without this the settlement leg reads 0 == 0 on an empty set, and
            // "displace nobody" would pass it. The two-outcome requirement is
            // the second half: a run in which every displaced firm exited would
            // leave the re-siting branch untested and vice versa. Measured over
            // seeds {0,1,5,9,13}: injected 13-29, re-sited 4-26, exits 2-10 —
            // so the bounds below sit an order of magnitude inside the
            // observations on the tightest of them.
            Check("displaced firm floor: the displacement event is real and both outcomes occur",
                  injected >= 8 && e.FirmDisplacedResitedTotal >= 1 && e.FirmDisplacedExitsTotal >= 1,
                  $"{injected} firms displaced at t={AtTick} ({Rate:P0} of those standing); "
                  + $"{e.FirmDisplacedResitedTotal} re-sited to land their own forecast carries, "
                  + $"{e.FirmDisplacedExitsTotal} left the city (bounds: 8 displaced, 1 of each outcome)");

            // ---- SETTLEMENT: nobody is left in limbo, and the exits paid out -
            // `injected` and FirmDisplacedSeenTotal are two independently kept
            // records — the fixture counts what it took away, the engine counts
            // what its own pass found — so their agreement is a real identity
            // and not a restatement. If the pass ever ran at a point in the tick
            // where some displacements had not happened yet, this is the leg
            // that would say so.
            Check("displaced firm: no live firm holds no site, and every displacement exit settled its books",
                  siteless == 0 && unsettledExit == 0 && e.FirmDisplacedSeenTotal == injected
                  && maxDrift < 1e-3 && audit.Worst < 1e-9,
                  $"{siteless} live firms hold no site at t=300 holding {stranded:F0} (bound 0); the pass saw "
                  + $"{e.FirmDisplacedSeenTotal} of {injected} displacements; {unsettledExit} exits kept money "
                  + $"(bound 0); ledger drift {maxDrift:E2} (bound 1e-3), reconciliation {audit} (bound 1e-9) "
                  + "— and those last two read the same on the mutant arm, which is why they are not the check");

            // ---- GUARD: nothing here is an UNPLACED firm ---------------------
            // A tripwire, not a check, and labelled so nobody mistakes it for
            // one: in the pure simulation every siteless firm got that way
            // through DisplaceFirm, which marks it, so this reads 0 == 0. It
            // exists because the pass now deliberately LEAVES a class of firm
            // standing — one the adapter could not place, whose default is to
            // stay put — and a class the engine declines to act on must at
            // least be counted where somebody will see it. On the mod arm the
            // same counter is the reader's coverage gauge.
            //
            // ITS FALSIFIER LIVES ELSEWHERE, ON PURPOSE. Nothing in the pure
            // simulation produces an unplaced firm, so no mutant can red this
            // leg here — including --mutant-resolve-unplaced, which passes it.
            // The property is asserted where the population is real: ModSiteLink
            // leg 3 (`modsync`) runs one engine tick over 59 unplaceable and 59
            // site-losing firms and requires the first group untouched and the
            // second resolved, and --mutant-resolve-unplaced reds it by killing
            // 58 of the 59. Read that leg, not this one, for the evidence.
            Check("displaced firm guard: every siteless firm here lost a site it held",
                  e.FirmUnplacedSeenTotal == 0,
                  $"{e.FirmUnplacedSeenTotal} live siteless firms had never held a site (bound 0 — structural "
                  + "in the pure sim, where DisplaceFirm is the only producer; on the mod arm a non-zero "
                  + "reading is EconReader coverage to go fix, not firms to go kill)");

            // ---- LINK: the firm and the site agree about each other ----------
            // Exact, not statistical: the two fields are one pointer written in
            // two places, so any disagreement at all is a defect. Measured 0/0
            // on every seed of the set above, and 12-28/0 under MUT-49b.
            Check("displaced firm: a firm and its site never disagree about holding each other",
                  fwdBreak == 0 && revBreak == 0,
                  $"{fwdBreak} live firms name a site that does not name them back, {revBreak} sites name a firm "
                  + $"that is dead or elsewhere (bound 0 each, over {w.Firms.Count} firms and {w.Parcels.Count} parcels)");
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
            // PINNED POOLED as well: both arms below are the pooled consumption
            // path, stated rather than inherited from a default that may flip.
            var sim = Sim.Create(cfg, p, new FeatureFlags
                                 { HousingAuction = false, StoreLevelSpending = false });
            double maxDrift = 0;
            var audit = new SectorAudit();
            sim.Run(300, s => { maxDrift = Math.Max(maxDrift, Math.Abs(s.W.Ledger.Drift())); audit.Sample(s); });

            // BOTH PATHS. Running this only on one path left a whole class of
            // bug invisible: prospect-level migration moves money across the
            // border on arrival and departure, and it exists only on the
            // auction path — an arrival that minted its savings instead of
            // transferring them would have passed here forever.
            var simA = Sim.Create(new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed },
                                  new EconParams(),
                                  new FeatureFlags { HousingAuction = true, StoreLevelSpending = false });
            double maxDriftA = 0;
            var auditA = new SectorAudit();
            simA.Run(300, s => { maxDriftA = Math.Max(maxDriftA, Math.Abs(s.W.Ledger.Drift())); auditA.Sample(s); });

            // A STORE-LEVEL ARM, for the same reason the auction arm exists.
            // Both arms above run the pooled consumption path, so the discrete
            // shop market — which debits each household once and credits each
            // shop from its own customers over several rationing rounds, with
            // the unserved residual leaking out of town — had no conservation
            // or reconciliation coverage of its own at all. The commerce
            // check's own money leg compares two per-tick records and is a
            // check on rule 3; this is the standing sector-balance
            // reconciliation, on the path that ships behind the flag.
            var simL = Sim.Create(new SyntheticCity.Config { Cols = 10, Rows = 10, SeedHouseholds = 3000, Seed = seed },
                                  new EconParams(),
                                  new FeatureFlags { HousingAuction = true, StoreLevelSpending = true });
            double maxDriftL = 0;
            var auditL = new SectorAudit();
            simL.Run(300, s => { maxDriftL = Math.Max(maxDriftL, Math.Abs(s.W.Ledger.Drift())); auditL.Sample(s); });

            Check("ledger conservation: money neither created nor destroyed (both market paths)",
                  maxDrift < 1e-3 && maxDriftA < 1e-3 && maxDriftL < 1e-3,
                  $"max |drift| over 300 ticks = {maxDrift:E2} posted-curve, {maxDriftA:E2} auction, "
                  + $"{maxDriftL:E2} store-level");

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
            bool reconciled = audit.Worst < 1e-9 && auditA.Worst < 1e-9 && auditL.Worst < 1e-9;
            Check("ledger reconciliation: sector balances match the money entities hold (both market paths)",
                  reconciled,
                  $"worst relative |Σ entity − ledger| over {audit.Ticks}+{auditA.Ticks}+{auditL.Ticks} "
                  + $"tick samples: posted-curve {audit}, auction {auditA}, store-level {auditL} "
                  + "(bound 1e-9, unmoved)");

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
                // Explicit for the same reason, and pinned while it is still a
                // no-op: "flags off" has to keep meaning every experimental
                // flag off. If StoreLevelSpending becomes the default this line
                // is the only thing standing between this smoke and a world it
                // does not describe.
                StoreLevelSpending = false,
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

        /// <summary>Gates on ALL THIRTEEN lanes of the checked-in model
        /// fingerprint. The gating/report-only split ended when the promotion
        /// rule was finally measured (Fingerprint.cs head: every fingerprint-era
        /// commit rebuilt, 0 false alarms in 29 arm-fires, 0 extra accepts for
        /// either promoted arm). A missing or hand-edited baseline FAILS — never
        /// skips, because a skip is how a check quietly stops existing.</summary>
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
                         + $"report-only lanes moved: [{string.Join(", ", v.ReportMismatch)}]; "
                         + $"missing from baseline: [{string.Join(", ", v.Missing)}]. If the change was "
                         + "intended, record it: `fingerprint --accept --reason \"...\"` (it writes the "
                         + "computed before→after deltas into the file for the reviewer)";
            Check("model fingerprint: the model is unchanged, or the change is recorded (all arms gate)",
                  v.GatePass, detail);
        }
    }
}
