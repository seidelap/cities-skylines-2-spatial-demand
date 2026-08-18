# Known-red registry

Every currently-failing check or scenario, with its attribution. The rule this
file enforces: **before attributing a failure to a change, look here; when a red
changes state, update this file in the same commit.** Two commit messages have
already mis-attributed failures ("pre-existing" that wasn't; "clean on all
eight seeds" that was true of eight and false of twenty) because this lookup
did not exist.

A failure is *bisected* when the introducing commit is known, *bounded* when
only a range is known. "Predates 2eeefc3" means it fails at the oldest commit
tested and the true origin is older — bounded, not explained.

## verify (51 checks)

29 at the per-cluster prospect-odds commit (28 plus prospect-local-odds;
seeds 0–7 and 25 measured 29/29 there, canary 39/39) plus the three
labor-auction checks (equilibrium, Ward refusal + mutant, payroll). At the
per-earner labor fix commit: verify 32/32 on seeds 0, 1, 2, 3, 13, 25
(seed 9 stays 31/32 on the pre-existing occupancy red — all three labor
checks green there), `laborcanary` 39/39, housing `canary` 39/39.

The 33rd is the shadow-queue assessment check (the counterfactual leg of
`Assess` priced from `HousingAuction.Shadow`, previously asserted nowhere —
task #32 audit follow-up F1), added at the check-debt commit inside the
assessment-tracks-price fixture. Measured green there on seeds 0, 1, 2, 3
and 9; mutants that zero the queue build, zero the not-standing read, or
corrupt the read rank by one all flip it red (same commit's measurement
runs, seeds 0–1).

The 34th is the uniform-pulse monotonicity check (task #42: the come/stay
margin anchored on the outside region's access), with its mutant arm wired
in Ward-style — `EconParams.MutantRelativeOutsideAccess` restores the
relative-outside defect and must push the check red. Measured at the
outside-anchor commit (`pulsesweep`, seeds 0–15, the verify-pinned 9 and 13
among them): clean rise +0.037..+0.070 of offered on all 16, mutant arm
−0.006..+0.005 against the 0.02 bar. The wider band (refuter F3, reproduced
at the fix round's `pulsesweep --seeds 24`, seeds 0–23): mutant readings
reach −0.011..+0.009 on the unswept seeds 16–23 while clean stays
+0.037..+0.071 — the bar's real margin is ~1.8–2.2x each way, not the 4x
the 16-seed band suggested; re-run the 24-seed sweep before tightening.

The 35th is the RESIDENT leg of the same uniform-pulse fixture (item-#42
fix round, refuter finding F2: severing the resident margin — `_outside[i]`
without the outside premium — flipped no semantic check, only the
fingerprint drift alarm, because the equilibrium/oracle/IR checks read the
same `_outside` the solve used and are self-consistent under ANY scaling of
it). The standing solve's `_outside` is paired against its re-derivation on
the pulsed field through `HousingAuction.Solve` (no tick advances, so every
per-household input is bit-identical and the ratio isolates the premium
factor); the geometric-mean ratio must fall ≥ 0.04, mutant arm under the
bar. Measured at the fix commit (`pulsesweep --seeds 24`, seeds 0–23):
clean fall 0.161..0.176, tracking the premium ratio to three digits on
every seed, mutant arm 0.000 exactly on all 24. The severed-resident-leg source mutant reads the
mutant's number on the clean arm and flips this leg red while the prospect
leg stays green (fix-round mutant run, `pulsesweep --seeds 1`) — the exact
regression the leg exists to catch. The clean arm takes the mutant switch
at its EconParams default, so a shipped default flip now reds both legs
(refuter F4).

The 36th is the tie-channel check (item #34 half A: chain migration as
per-cluster ties — `MigrationState.NetworkTies`, prospects draw a tie
cluster ∝ the stock and carry a familiarity bonus at that one door), with
its mutant arm wired in Ward-style — `EconParams.MutantZeroTieBonus` zeroes
the bonus while the draw and the stock keep running, and must hold the
tie-landing lift under the bar. The statistic is a lift over the batch's
own independence baseline, so a dead channel cannot pass on geography
alone. Measured at the per-cluster-memory commit (`tiesweep --seeds 16`,
seeds 0–15, the verify-pinned 9 and 13 among them, shipped bonus 0.15):
clean lift +0.027..+0.052 on 16/16, mutant arm −0.004..+0.006 against the
0.015 bar (~1.8x under the worst clean seed, ~2.5x over the worst mutant
reading). Bonus dose-response {0.05, 0.10, 0.15, 0.20} → worst-seed lift
+0.008/+0.024/+0.032/+0.059 (`tiesweep --seeds 4 --tie-bonus X`, same
commit). Re-run the 16-seed sweep before tightening. What the bar does
NOT reliably gate (fix round, both weakening variants measured): the
∝-stock draw itself — a uniform tie draw with the bonus alive read
clean lift +0.012..+0.019 on seeds 0/9/13, caught only at seed 9
(fix-round weakening run, reverted), while the adversarial round's
variant of the same weakening read +0.010..+0.012 and was caught 3/3 —
the bonus alone manufactures tie-landing dependence, so the check gates
the bonus channel and only brushes the draw.

The 37th is the per-cell construction-calibration check (item #34 half B:
`CalibrationState.ByCell`, per-(use, cluster) realized-vs-predicted shrunk
toward the use factor by n_c/(n_c + 2)). Three legs: deep divergent cells
closer to their own mean than the use factor, thin cells the reverse, and
the construction forecast (`ExpectedFlow` predictedRent) re-derived as
bid × the per-cell factor at the deepest divergent cell. Clean bands
measured at the FIX ROUND's commit (`calibsweep --seeds 4`, seeds 0–3 +
9, 13, shipped bonus 0.15): 6/6 green, 13–27 deep / 12–25 thin divergent
cells per seed over 72–91 cells / 191–256 completions, call-site rel err
0 exactly. (The bands first registered here — 13–25 deep / 14–21 thin
over 71–86 cells / 184–238 completions — were measured at bring-up,
BEFORE the tie bonus moved 0.10 → 0.15, and were stale by the item
commit: the admission re-roll moves the fixture's completion mix.
Refuter finding at the #34 review; verdict-bearing content unchanged.)
Source mutants both run and reverted at the item commit: citywide-only
restored reds legs 1 and 3 on 6/6 (convergence 0/14..0/27, factor ≡ use
factor at the call site), zero shrinkage prior reds exactly leg 2 on 6/6
(shrinkage 0/14..0/19). n0 = 2 picked from the measured per-cell
completion depths (min 1, p25 1–2, median 2–3, p90 4–5, max 6–8 —
bring-up and fix-round sweeps agree on the distribution;
EconParams.CalibClusterShrinkN0).

The 38th is the owner-door leg of the auction-equilibrium fixture (item
#41: per-parcel owner doors — partition, reserve = max(own condition
floor, ask) bitwise, fold-rule coherence both ways, owner IR including the
unhoused case the envy sweep skips, door↔parcel move integrity, and the
no-ratchet EXACT WRITE IDENTITY A = min(scale·share·R, R) re-derived
against the same frozen solve state PostOwnerAsks wrote from; plus an
engineered-ask arm so no seed is vacuous). The canary now gates EVERY
result the fixture emits (previously only the last), so this leg sweeps
the same 39 seeds. Source mutants, every one run red and reverted at the
item commit (runs named in the impl summary): capacity added to BOTH
doors → partition 36/35 bad cells on seeds 1–2; the ask dropped from the
reserve max → reserve 63/49 bad; the fold rule dropped so every standing
ask opens a door → fold 48/46 incoherent; a between-round reserve
repricing → reserve 44/40 bad; FirstVacantIn's owner branch returning any
cluster-mate → door↔parcel 2 astray on seed 3 (seeds 1–2 land on the
right parcel by accident of list order, so that mutant is caught per
sweep, not per seed); PostOwnerAsks writing 1.1×Price[own door] → 74/70
asks off the identity. And the exhaustive-shortlist fixture's owner-key
listing (its arm forbids repair rounds, so it must be SHOWN the doors)
has its own: withholding the listing leaves the every-door arm needing 3
repair rounds → that check red.

TWO MEASURED FACTS behind this leg's shape, stated rather than hidden.
(a) The A ≤ R BOUND form of the no-ratchet leg measures ZERO violations
under the same 1.1×Price mutant — which is why the leg asserts the write
identity instead: an exploded ask prices its own owner out, the owner
leaves, Vacate clears the ask, and the asks standing at any instant are
all young. (b) Eviction-rule weakenings are ABSORBED by the full re-clear
and re-bid loop — `bid > weak − ε` and even `bid > 0.9·weak` leave canary
seeds 1–3 green on every leg — so a weakened-eviction mutant does not
falsify owner IR; what does is severing BOTH incumbency channels at once
(home-door listing for owner keys AND repair discovery): owner-IR 24
violations, worst 2.94, on seed 1 and 29, worst 2.56, on seed 2. Either
channel alone heals the other, which is itself a measured property of the
mechanism. The exact-tie eviction case (`>` vs `>=` at the eviction site)
is carried by code shape plus the leg's band — a bit-exact tie on doubles
is measure-zero here and no canary seed produced one; an honest limit.

The 39th is the ask-invariance leg of the assessment-tracks-price fixture
(item #41's thesis, the §3 guard at the ask): each probed owner parcel's
OwnerAskPerUnit ×10, Assess re-run on the SAME auction state, every
published field bit-identical, restore. Zero asks get a nonzero
perturbation so the leg cannot go vacuous. Mutants at the item commit,
both run red and reverted: the folklore's feared bug planted as Assess's
currentResidual leg reading the parcel's own ask — 0/24 probes come back
identical, worst |ΔLR| 1.9E+002; and the same file's Price[DoorOf(parcel)]
form, which is INERT on this leg by construction (the check freezes the
auction state precisely to isolate the accounting path, and a frozen
posted price cannot move with the ask) and is caught instead by the L1
relation leg of the same fixture, red there at 410 of 421 parcels.

The 34th and 35th are the task #30 goods checks (goods-price localization
level/tilt/thin, and per-resource goods-settlement reconciliation), added
at the local-goods-prices commit; `goodssweep` runs both alone across
seeds. Measured verdicts and their mutant runs are recorded in the checks'
own comments (TestRunner.cs) and in the rows below where they moved
standing reds. The settlement check's band legs were REBUILT at the fix-30
commit: the landed pair compared P_j against quantities P_j is built from
and passed a mutant that settled every local lot at double the clearing
price; the legs now compare the price buyers were actually debited against
a destination alternative rebuilt from the posted exit laws, on a second
capacity-capped arm as well, and P_j×2 / P_j×½ each turn one red.

**THE DEFAULT FLIPPED at the flip commit**: `FeatureFlags.HousingAuction`
ships TRUE — migration as prospects, housing as the assignment market. The
decision was made on a full measured inventory of both arms (all raw runs in
the session record): one real defect exposed (Weber, seed 13 — row below),
everything else either pinned (posted-mechanism checks now run the posted
world explicitly: occupancy channel, the ledger/reconciliation/shelter
posted arms, flags-off smoke — coverage preserved, verdicts unchanged),
re-baselined with the reason recorded (fingerprint: default lanes carried
over hash-identical to the previously ACCEPTED auction values), or measured
unchanged (both canaries 39/39). At the flip commit: verify 33/33 on seed 1,
32/33 on seed 9 (the standing posted-arm occupancy red, pinned check,
unchanged numbers), 32/33 on seed 13 (Weber). The posted path stays
reachable via `--posted` and covered by the pinned arms while it ships.

At the LOCAL-GOODS-PRICES commit (task #30): verify 35/35 on seeds 1, 9
and 13 — the seed-13 Weber red and the seed-9 occupancy red both healed,
rows below — and 34/35 on seed 25 (occupancy channel, newly observed
there; row below). Canary 39/39, laborcanary 39/39. Fingerprint default
lanes accepted with the reason ("goods prices localized to realized
per-cluster transactions; local freight now paid to OutsideWorld").

At the FIX-30 commit (adversarial review F1/F2 on task #30): behavior
unchanged from the local-goods-prices commit — the diff is the goods
settlement check's band legs (the landed pair could not fail; rebuilt
against an independently recomputed destination alternative, with a
capacity-capped second arm), comment/doc corrections, and the registry
rows below. Same verdicts: verify 35/35 on seeds 1, 9, 13 and 34/35 on
seed 25, canary 39/39, laborcanary 39/39, fingerprint lanes unmoved (no
new stanza). The occupancy row below now carries the 57-seed `occsweep`
inventory the previous row said had not been run.

The 36th to 45th are the task #20 commerce legs — five in the commercial-staffing
fixture (capacity bound + its binding floor, the door-cap technology ceiling,
staffless-shop bidding with its embedded MutantServedCap arm, the defaults rule,
and store-level money conservation) and five in the counted-shop-intents fixture
(the commercial-side circularity guard, thin evidence, discrimination against the
pooled field, the crowding response, and calibration against realized takings),
added at the real-staffing commit. `shopsweep` runs both fixtures
alone across seeds; `shopprobe` is the census every bound in them was set from.
They are the first assertions in this suite that name the commercial sector at
all — measured at the start of that item, `grep -c Commercial TestRunner.cs`
read **0**, with no occurrence of "shop", "retail" or "capture" either, against
26 for "residential". At that commit: verify **45/45** on seeds 1, 9 and 13 and
**44/45** on seed 25 (the standing occupancy red, unchanged leg and unchanged
numbers — FillEma worst err 0.663, price leg responding 2.333 → 0.534), canary
39/39, laborcanary 39/39, goodssweep 7/7, and `fingerprint --check` all lanes
matching with no accept. Cost: the two fixtures add ~50 s per seed to a suite
the labor round measured at ~120 s. That is the price of testing a fifth
mechanism, stated the way the labor fixture's was.

**THE STORE-LEVEL FLAG DID NOT FLIP, AND THE BLOCKER IS MEASURED.**
`FeatureFlags.StoreLevelSpending` still ships FALSE. Task #20 landed both
mechanisms the flag's own doc comment was waiting on — commercial production
became linear in the staff the shop has, and the pooled phantom-entrant entry
field was replaced on that path by counted individual shop intents — and the
census gap narrowed without closing: on the store-level path commercial firms
alive **98.0 → 103.5** and commercial-parcel vacancy **42% → 39%**
(`shopprobe --seeds 4`, 300 ticks, against `firmdiag` at 58cab48 on the same
seeds), against a pooled path this item leaves BIT-IDENTICAL at 130.2 alive and
26% vacancy. It was not free: commercial deaths rose **44.2 → 77.5**,
concentrated entirely in entry — **141 of 161** shops born mid-run die within 40
ticks against **0 of 48** on the pooled path. That is a NEW measured fact about
this path and it is the first thing the inventory has to classify. The flip decision was not taken on that number. It was not taken
because a flip inventory classifies changed verdicts, and until this commit
nothing in the suite could see a commercial verdict change at all — every one
would have classified as "unchanged by construction". WHAT WOULD UNBLOCK IT,
in order: (1) the two new checks green across the 26-seed `shopsweep` on the ON
arm, (2) `webersweep` on the ON arm no worse than the OFF arm's record on the
same seeds — the flag comment's own stated blocker, still unmeasured on the ON
arm here, (3) the census gap closed or explained per number rather than as one
band over two numbers that move in opposite directions, (4) both canaries 39/39
on the ON arm, (5) ledger conservation and per-sector reconciliation exact on
both arms with a pooled arm added to those checks, (6) the mechanical pins:
`--pooled`, `vanillaMode`, flags-off smoke, the occupancy pin, a pooled
fingerprint arm.

**AT THE TWO-TRACK MERGE** (housing: outside-access anchor, per-cluster ties
and calibration, owner doors; goods: the Weber investigation, per-cluster goods
prices and its fix round, commerce) the registry's rows are re-derived from runs
on the MERGED tree, because neither branch's rows could be textually correct
about the combination — each track healed reds the other left standing, and one
red exists only in the combination. Measured at the campaign's final tree:
verify **51 checks** — seed 9 **51/51** (a seed that carried a standing red
since before the auction era), seed 1 50/51 and seed 13 50/51, both on the rows
below. `canary` 39/39 and `laborcanary` 39/39: both markets' equilibrium
guarantees survive the union untouched. `webersweep` 24/26 (red 0, 5 — both on
the diversity leg) against 13 red at the flip. `occsweep --seeds 50` 51/57.
`fingerprint --check` all lanes match at the commerce merge with no accept,
which is the flag-gating of task #20 measured rather than asserted. The
per-branch numbers each row retains are kept for ATTRIBUTION, not as current
state.

**AT THE INSTRUMENTS COMMIT** the suite is still **51 checks** — one check was
replaced by one check, not added to. Both legs of the occupancy channel were
rewritten to measure the channel instead of world composition, and the red set
that check owned is now EMPTY: `occsweep --seeds 50` reads **57/57** against
51/57 at base `88fc88c` (both sweeps run in this session, base in its own
worktree). Six verdicts change and every one is red → green; the closed row
below carries the per-leg attribution of all six and the five severing mutants
that each read **0/57**.

**EVERY FINGERPRINT ARM NOW GATES, and the promotion rule was measured rather
than restated.** The rule at the head of Fingerprint.cs had stood unexecuted
since `0b5b658`. Running it meant rebuilding all 24 fingerprint-era commits
(`0b5b658..88fc88c`, 23 transitions) and comparing every lane with its
predecessor — per arm, transitions fired / rate / fires carrying the documented
false-alarm signature (hash moved, every printed scalar unchanged):

| arm | born | transitions | fired | rate | false alarms | fires without a default fire |
|---|---|---|---|---|---|---|
| default (gating) | 0b5b658 | 23 | 11 | 0.478 | 0 | — |
| auction (retired 554b56a) | 0b5b658 | 9 | 2 | 0.222 | 0 | 2 |
| posted | 554b56a | 13 | 6 | 0.462 | 0 | **0** |
| labor | 25b24b1 | 15 | 10 | 0.667 | 0 | **0** |

Following each WORLD across the flip's rename instead of the arm's name: the
auction world fired 12/23 (0.522), the posted world 6/23 (0.261), and the flip
transition moved NEITHER renamed arm — stanza 6's claim that the rename carried
a measured state over is confirmed. THE OLD RULE CANNOT BE EXECUTED AS WRITTEN,
three ways, each of them measured: its subject (the auction arm) was retired one
transition short of its own 10-commit window and that world gates today as
`default`; its measurement cannot come from the log at all, because a
report-only lane's move never forces a stanza, so the log records report-arm
moves only when they ride along with a gating accept; and its criterion, applied
to the arm that gates today, reads 0.478 and would DEMOTE the gate. It is
replaced by a rule stating its own measurement procedure and a two-sided
trigger on the FALSE-ALARM rate, which is the cost the original was actually
worried about. Decision at this commit: 0 false alarms everywhere, so posted and
labor are PROMOTED and all thirteen lanes gate. The measured accept burden that
adds is ZERO — all 6 posted fires and all 10 labor fires landed on commits where
default fired too — with the limit of that number stated rather than hidden: no
commit in the window moved posted or labor WITHOUT moving default, so it bounds
nothing about a future labor-only commit. The promotion was demonstrated, not
asserted: a scratch labor-only model change (`LaborShortlist` 12 → 14) reads
`MISMATCH [report]` and exit **0** under the old split and `MISMATCH [gating]`
and exit **1** after the promotion. Neither this nor the occupancy rewrite moves
the model: `fingerprint --check` reads all thirteen lanes matching stanza 11
with no accept.

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 0, 5 | Weber: extraction follows geology; recipes follow input sourcing | red | MEASURED ON THE TWO-TRACK MERGE, which is the shipping state and supersedes every per-branch tally below: `webersweep` (seeds 0-25) reads **24/26**, red only on 0 and 5, against 22/26 {0,4,7,16} on the goods track alone and reds on 9 and 13 on the housing track alone (all three sweeps run by the orchestrator on their own trees). Seeds 1, 9 and 13 are GREEN on the merge — each track healed part of what the other left red, which is why no branch's row could be textually correct about the combination. **Seed 5 fails on the DIVERSITY PRECONDITION ALONE**: 10 extractors at 100% on the best raw and 7 of 7 single-input industrials on the cheapest-sourced recipe — the Weber property the check exists to assert holds PERFECTLY — and the verdict is red because the city produces 1 distinct industrial output against a >=2 bar. That is a fixture-adequacy failure, not an economics failure, and it is the shape the whole Weber history has been pointing at: the alignment legs now pass wherever the premise is delivered. Seed 0 is a real miss on the extraction leg (10 extractors, 80% on best raw vs the >=90% bar) with recipes passing at 67% of 6 and the same 1-output diversity fail. THE OWED INVESTIGATION is therefore re-scoped by measurement: it is no longer 'why do recipes misalign' (localized prices answered that - see Closed) but 'when is one industrial output the correct answer for a small city, and should the diversity leg be a precondition rather than an assertion'. Unowned. PER-BRANCH HISTORY, retained for attribution: New with the DEFAULT FLIP: on the auction-default world, 6 of 13 single-input industrials sit on the cheapest-sourced recipe (46% vs the ≥55% bar; the margin is 2 firms) while the extraction leg is clean at 100% and the sector is bigger and more diverse than required (13 firms, 4 outputs). At the FLIP commit it was seed-13-specific — the same check read 100% on flip seeds 0-1 and passed 2, 3, 9, 25 (posted-arm seed 13 green); that cohort statement is DATED: at the outside-anchor commit the extraction leg is red on seeds 0, 9 and 25 (fix-round measurement — class row below). Precedent: Weber was the red on auction-arm seed 0 at `c0c584d` — auction-world Weber fragility moves seeds, it is not new. Wants its own investigation: which 7 firms, their delivered-cost gaps, and whether entry timing under prospect-driven population growth outruns trade-price settling. Unowned. AT THE OUTSIDE-ANCHOR COMMIT (#42) the same row stays red with different numbers — recipes 14% of 14 industrials, extraction still 100% — the anchored outside re-equilibrates the whole default world (baseline churn, population, entry timing), and Weber's verdict re-rolls with it. AT THE PER-CLUSTER-MEMORY COMMIT (#34) seed 13 is GREEN (extraction 100%, recipes 85% of 13) — the tie-driven admission re-roll moves the verdict again, in Weber's documented seed-moving way; the class row below carries the standing red. AT THE OWNER-DOOR COMMIT (#41) seed 13 is RED again, and on the fixture's POPULATION PRECONDITION rather than on any alignment: 4 extractors against the `extract >= 5` minimum, with extraction 100% on best raw and recipes passing at 69% of 13. Measured against the branch tip in the same session (tip 209e232: 6 extractors, 100%, recipes 85% of 13, GREEN). #41 touches no pricing, recipe or siting rule — its diff is the housing auction, the owner tag and the checks; LandAccounting changes only its header comment and Trade is untouched — so this is the same re-equilibration channel #42 and #34 moved the verdict through, now reaching the industrial sector's SIZE. | || AND THE EXTRACTION-LEG CLASS: NEW at the outside-anchor commit (#42), on the EXTRACTION leg — seed 9 registered at that commit; seeds 0 and 25 found red at the SAME commit by the refuter's wider sweep (finding F1) and registered at the fix round, which reproduced them (fix-round verify runs, seeds 0 and 25; both are FULLY GREEN at base 554b56a, extraction 100%). Numbers at the fix commit: seed 9 — 7 of 10 extractors on the best raw (70% vs the ≥90% bar), recipe leg passes at 80%; seed 0 — 8 of 9 extractors (89%), the margin one extractor, recipes pass at 82% of 11; seed 25 — 7 of 8 extractors (88%), recipes pass at 82% of 17. Seeds 2 and 5 pass in full. Tally across the seeds measured in the two sessions (0, 1, 2, 5, 9, 13, 25): Weber red on 4 of 7 — 0, 9, 25 on extraction, 13 on recipes — against 1 of 7 at base; the owed Weber investigation is that much bigger than a single-seed row suggested. Same fragility class as the seed-13 row — the anchored outside option changes the default world every fixture equilibrates in (churn, entry timing, trade-price settling), and Weber's seed-dependent verdict moves seeds with it, exactly as it moved 0 → 13 at the flip. No pricing/recipe rule is touched by #42 (the diff is Prospects/HousingAuction/Access outside-door legs only; posted fingerprint lanes are hash-identical). Belongs to the same owed Weber investigation as the row above. Unowned. AT THE PER-CLUSTER-MEMORY COMMIT (#34) the class re-rolls again: seeds 0 and 9 GREEN (extraction 100% both, recipes 90%/78%), seed 25 red and DEEPER — extraction 44% of 9 extractors (4 on best raw) vs the ≥90% bar, recipes pass at 81% of 16 (#34 gate runs, verify seeds 0, 9, 25). Tally on the measured cohort {0, 1, 9, 13, 25}: 1 of 5 red at #34 against 4 of 7 at #42. The owed investigation is unchanged in shape and now owns seed 25's deep extraction miss. AT THE OWNER-DOOR COMMIT (#41) seed 9 is RED again on this leg — 6 of 7 extractors on the best raw (86% vs the ≥90% bar, the margin one extractor), recipes passing at 80% of 10 — against a tip-209e232 run in the same session that reads 9 extractors, 100%, GREEN. Same channel as the rows above: the auction-default world re-equilibrates and Weber's verdict re-rolls with it; #41 touches no pricing or recipe rule. |
| 13 | clearing price: quantity responds (population-collapse leg) | red | NEW AT THE TWO-TRACK MERGE, and an INTERACTION - it is green on each track alone, measured by the orchestrator on all three trees: housing tip 01c7543 reads bid 2.96 -> 2.51 after 638 citywide exits (PASS, verify 38/39), the goods track reads verify 35/35 at seed 13, and the merge reads bid 2.15 -> **2.56** after 695 exits - the price RISES where the leg requires it to fall. The supply and demand legs stay monotone and correct on the merge (supply x{0.5,2,8} -> 8.56/0.52/0.52; demand x{0.5,1,2} -> 0.52/2.15/8.56; 25-point sweep monotone), so what fails is only the population-collapse direction. HYPOTHESIS, NOT MEASURED: per-parcel owner asks (#41) hold occupied doors at their floors while the survivors of a collapse re-sort upward into the best stock, so the probed submarket's marginal bidder can end up richer than before the collapse - the composition effect outrunning the scarcity effect. That is a guess and must be measured before anyone acts on it: the honest next step is to re-run the collapse leg with owner asks disabled (`--owner-ask 0`) and with localized goods prices mutated off (`--mutant-citywide-goods`) and see which one restores the fall. Unowned.

## Measured dead ends — do not retry blind

Three replacements for the CutVacancies revenue rule were built and measured
in one session. All three keep the right *goal* (unsold rooms rest at cost;
no landlord exists to protect revenue for — §2 charter, and n·P(n)'s revenue
is paid to no account) and all three fail on the same class of problem: a
between-round administrative repricing cannot reach the competitive fixed
point in this architecture.

1. **Cut one band below the best challenger's bid.** Undershoots: transfers
   one room at the highest price that moves anybody, leaving the next
   comparison marginal again. Pairs of doors ping-pong movers at ε
   granularity, walking a 0.9 price gap in 0.02 steps. Canary 0/39, every
   seed `converged False` at cap 16.
2. **Cut to the k-th best challenger bid (clearing level), independent
   quotes per door.** Overshoots: one household is the marginal challenger
   at dozens of doors at once; every one cuts to just below its bid; it
   takes one room and the rest are open bargains. Measured: 1,542 of 3,615
   households envious, worst gain 1.129 (7× the ε band). Canary 0/39.
3. **Clearing-level cut with exclusive claims** (each household backs at
   most one door's cut per pass, so the cut system is simultaneously
   satisfiable). Feasible but still a limit cycle: cuts pull prices down,
   the resumed auction bids them back up, churn frees new rooms at raised
   prices, repeat. At cap **200**: `converged False (repair 200 rounds,
   clean False)` on 8 of 8 fixtures measured, unsold 39–53 throughout.

The old rule "converges" only because its cuts close no deals — the loop
runs dry trivially.

**RESOLVED** — the down-phase moved inside the auction in the simplest
possible way: every repair round is a full re-clear from the reserve, so a
stale price cannot exist for anything to walk down. The accepted-standing-
bid design sketched at the time was never needed. Cost, measured: net solve
~10% dearer (phase total 26.4s → 29.2s over a 300-tick probe at the
shipped cap of 16). Bought: canary 33/39 → 38/39, `unsold-above-reserve`
structurally zero, and an LP-optimality gap of 0.01–0.03% of LP* where the
resumed regime measured 0.81–1.61%.

Seeds 0–7 and 13 are 27/27 at the fix commit; seed 25 is the one canary red.

### Labor: household-atomic multi-earner demand with exact-set displacement

The first labor-auction build (`02a976f`) bid whole HOUSEHOLDS — one bidder
demanding 1 or 2 slots — and invented an exact-set displacement ({one
1-earner}, {one 2-earner}, {two 1-earners}, priced at the dearest member of
the cheapest set) to keep doors full. Do not retry blind: multi-unit demand
is outside the assignment-LP theorem the housing machinery rests on, and the
exact-set entry price is NOT MONOTONE — a door's cheapest exact set can get
CHEAPER as its membership recomposes, so a settled worker that passed on a
door is never re-triggered when it becomes attainable, and the deterministic
full re-clear reproduces that state forever (added == 0 with standing envy is
a fixed point the repair loop terminates on, reporting Converged &&
RepairClean). Measured with the shipped `laborcanary` at `02a976f`: red on
**8 of 39** default-list seeds (8, 9, 13, 15, 16, 20, 22, 29), 83–109
households beyond the ε band (worst envy 28.9% of value against the ~1%
band on the dissected seed 13), 160–205 workers stranded on their defaults,
BlockedListed 263 on seed 13's terminating "clean" scan.

**RESOLVED** — the bidding unit became the EARNER (unit demand, one slot
each; a household's earners may work at different firms), which deletes the
exact-set machinery outright and restores housing-style single-evictee
displacement. One labor-specific deviation from the housing form survived
measurement: labor values carry no idiosyncratic taste term, so identical
workers tie exactly, and the bare-Admitted entry price livelocks on the tie
(measured at this fix round's bring-up: seed 0 burned its whole bid budget,
796,000 bids against 48,955 evictions, converged False). A full door's
entry price is therefore Admitted + the door's ε (+∞ at or above the cap —
comp cannot go below zero), which makes a just-failed door read a full ε
below its alternatives and every attempted eviction succeed. Entry prices
are monotone within a re-clear round again; laborcanary 39/39 at the fix
commit (clean scans in 30-41 repair rounds, 13-17 bids/worker).

### Industrial recipe re-evaluation (retooling) for the seed-13 Weber red

Built and measured at the webersweep-commit round; REVERTED — this dead end
is NOT resolved, it is parked behind task #30 (patch preserved in the
session record; the sweep and verify numbers below are re-measured from it
per seed at the registry-correction commit — the persistence-window and
retool-count numbers are the build round's own probe measurements). The
mechanism was charter-clean: each industrial firm re-ran its own Weber
comparison from its own location at current prices each tick and switched
recipe when another beat its own, net of an amortized retooling wedge (the
household moving-cost form — a per-firm
hash draw over an amortization horizon, no cash gate: worker-collective
firms measured money 0 to −17 against a ~196 lump, so a paid lump can never
clear), after a measured persistence window (transient dominance
self-reverses ≤13 consecutive ticks; genuine dominance persists 26–86+; at
persistence 20 one firm retooled every ~20 ticks — the near-flat
cross-recipe margins let the argmax rotate — at 40 churn damped to
18 retools/16 firms/300 ticks). Result: continuous re-evaluation walks
firms toward the one profit argmax the citywide LocalPrice statistic plus
one-sided basket demand pick (Food nearly everywhere — on the seed-13
fixture at check time it is the re-run argmax of EVERY single-input
industrial), and the check gets worse, not better. Measured: Weber check
6/26 seeds pass with the mechanism (fails 0, 4–6, 8–15, 17–22, 24, 25) vs
23/26 without (fails 4, 6, 13 — 4 and 6 are the extraction-leg reds in the
row above, red on BOTH arms). The failure mode is MIXED, not the uniform
"100%-aligned Food monoculture" first recorded here: 17 of the 20 failing
seeds do read 1 distinct industrial output, but 3 fail with 2 outputs
(0, 4, 9); alignment reads 100% on 13 failing seeds and 40–73% on the
other 7 (40% on seed 5, 50% on 4 and 9, 57–73% on 0, 14, 17, 20); and 7
failing seeds (0, 4, 6, 15, 21, 22, 24) read fewer than 5 single-input
industrials at check time — under re-evaluation the single-input
population itself thins (firms end on the multi-input Machinery recipe or
dead; not decomposed which), failing the check's population floor. Seed-13
verify 31/33 with the mechanism (diversity leg red AND the seed-13
pairwise-stability knife-edge re-red: improving swaps 1, best 0.194) vs
32/33 at base. The baseline's output diversity is partly a FREEZE
ARTIFACT — entrants at different ticks locked in different argmaxes. Do
not retry re-evaluation until local prices are per-cluster (task #30):
with a citywide price there is one argmax, and any re-evaluation
mechanism, whatever its damping, walks firms toward it.

### Labor: household-atomic multi-earner demand with exact-set displacement

The first labor-auction build (`02a976f`) bid whole HOUSEHOLDS — one bidder
demanding 1 or 2 slots — and invented an exact-set displacement ({one
1-earner}, {one 2-earner}, {two 1-earners}, priced at the dearest member of
the cheapest set) to keep doors full. Do not retry blind: multi-unit demand
is outside the assignment-LP theorem the housing machinery rests on, and the
exact-set entry price is NOT MONOTONE — a door's cheapest exact set can get
CHEAPER as its membership recomposes, so a settled worker that passed on a
door is never re-triggered when it becomes attainable, and the deterministic
full re-clear reproduces that state forever (added == 0 with standing envy is
a fixed point the repair loop terminates on, reporting Converged &&
RepairClean). Measured with the shipped `laborcanary` at `02a976f`: red on
**8 of 39** default-list seeds (8, 9, 13, 15, 16, 20, 22, 29), 83–109
households beyond the ε band (worst envy 28.9% of value against the ~1%
band on the dissected seed 13), 160–205 workers stranded on their defaults,
BlockedListed 263 on seed 13's terminating "clean" scan.

**RESOLVED** — the bidding unit became the EARNER (unit demand, one slot
each; a household's earners may work at different firms), which deletes the
exact-set machinery outright and restores housing-style single-evictee
displacement. One labor-specific deviation from the housing form survived
measurement: labor values carry no idiosyncratic taste term, so identical
workers tie exactly, and the bare-Admitted entry price livelocks on the tie
(measured at this fix round's bring-up: seed 0 burned its whole bid budget,
796,000 bids against 48,955 evictions, converged False). A full door's
entry price is therefore Admitted + the door's ε (+∞ at or above the cap —
comp cannot go below zero), which makes a just-failed door read a full ε
below its alternatives and every attempted eviction succeed. Entry prices
are monotone within a re-clear round again; laborcanary 39/39 at the fix
commit (clean scans in 30-41 repair rounds, 13-17 bids/worker).

## scenarios (default scenario seed)

**RE-MEASURED AT `88fc88c`, the full battery, and the previous 7/10 was stale.**
The number below it had not been re-run since the flip commit, which predates
the entire six-item campaign. Measured now, in a base worktree at `88fc88c`:
**8/10**, red on `tradebend` (bend 24 % against the ≥30 % bar; marginal 1.97 vs
flat 2.60, sustained 58/tick) and `boombust`. Every other row passes, and TWO
of the registry's standing claims are falsified by the run:

- `levels` is **GREEN at Spearman 0.37 against the 0.35 bar** (vanilla 0.03),
  not the 0.27 the row below recorded at the flip. The row's premise — an
  auction-arm gap to the bar — no longer holds; what is true instead is that
  0.37 against 0.35 is **two points of headroom**, the same shape of defect the
  occupancy check's `worstErr < 0.5` bound had, and the instruments commit
  re-derives the bar from a measured trajectory rather than leaving it there.
- `boombust`'s red is NOT what the row below says either: see that row.

The battery still costs ~12× the posted era (every refresh solves the auction;
`levels` is the long pole) — budget accordingly — and the vanilla arms of every
scenario stay PINNED to the vanilla market (`Sim.Create` vanillaMode) so
"vs vanilla" keeps meaning vanilla.

HISTORY, at the flip commit: 7/10. Two heals worth naming — `vacancy`
(red since before the auction era) passes with suppression concentrated at
the shock (contrast 6.0 vs bar 3.0; true-vanilla arm 2.7), and `perf` reads
1.62–1.65 against the <1.8 bar (the posted-arm "ratio ≈2" seed-1 red retires
with the posted default; timing-sensitive, keep watching). The battery costs
~12× the posted battery (every refresh solves the auction; `levels` alone is
the long pole) — budget accordingly, and note the vanilla arms of every
scenario are PINNED to the vanilla market (`Sim.Create` vanillaMode) so
"vs vanilla" keeps meaning vanilla.

Pre-flip history for these rows: vacancy/levels/perf failed byte-identically
at `2eeefc3`, `6104275`, and the posted-default HEADs (seed-1 runs; true
origins untested further back).

| Scenario | Failing line | Attribution |
|---|---|---|
| levels | GREEN at `88fc88c` and green at the instruments commit; the row is kept because the BAR was the defect, not the reading | **RE-MEASURED AND THE ROW'S PREMISE IS GONE.** The full battery at `88fc88c` reads `Spearman 0.37 spatial vs 0.03 vanilla` and PASSES the 0.35 bar — not the 0.27 recorded at the flip, which had never been re-run and is now retired. What is wrong is the bar: 0.37 against 0.35 is two hundredths of headroom, and the trajectory says that gap is inside the statistic's own wobble. Measured (`leveltraj --seed 20260806 --every 250`, both arms scored on the same run, auction default): spatial 0.315 / 0.310 / 0.293 / 0.338 / 0.339 / 0.344 / 0.340 / 0.319 / 0.366 at t = 250..2250, vanilla 0.031 / 0.010 / 0.023 / 0.004 / -0.027 / -0.025 / -0.005 / -0.020 / 0.011 on the same horizons. THE SERIES DOES NOT CLIMB on this path — it is inside 0.29-0.37 from the earliest horizon measured, with no trend beyond +/-0.03 — which falsifies the scenario comment's stated reason for the 2600-tick horizon (0.29 at 1300 -> 0.42 at 2600 -> 0.43 at 4000; that was measured on the POSTED path). So 0.35 sat INSIDE the band: 2 of the 10 readings clear it and 8 do not, and the verdict was decided by which horizon the run happened to sample. The bar is re-based to **0.15**, placed between the measured healthy band (worst reading 0.293) and this scenario's own null, the vanilla arm at -0.027..+0.031 — 0.14 clear below the worst healthy reading and 0.12 clear above the null's top — and NOT at the observed number. The horizon stays at 2600 because the same trajectory says nothing measurable is bought past ~1000, so there is no evidence for moving it either way. Non-degeneracy floors added in the same change (scored parcels >= 200 against 790 measured, >= 2 distinct realized levels and >= 2 distinct l* against 5 and 4) because Spearman returns 0 on a degenerate input and a 0 would have read as 'no geography' rather than 'nothing to correlate'. WHAT IS STILL OWED, named: the trajectory is ONE seed, and the band the bar is placed against is only as good as that. The run that would settle it is `leveltraj` on two or three more seeds; the same run would also settle whether the horizon can be cut from 2600, which is worth real battery time since `levels` is the long pole. HISTORY: the flip-era row read `spatial 0.27 vs vanilla 0.03` and blamed an auction-arm gap to the bar; `realized:true` for the l* oracle was built and MEASURED WRONG there (0.27 -> -0.21) and that door stays closed. |
| tradebend | `bend 30 %` vs the ≥30 % bar (marginal 1.83 vs flat 2.60) | Pre-existing near-bar red at this seed on BOTH arms (posted sustained 78/tick, auction 69) — not flip-caused. Unowned. |
| boombust | GREEN at the default seed at the instruments commit (`inflow rate 0.8204 control -> 0.8308 pulsed, +1.3 %` against a +0.85 % bar); RED on sweep seed 7, on the asymmetry leg, by one tenth of a point | **THE SCENARIO IS RESTATED AND ITS RED IS NOW A DIFFERENT, MEASURED THING.** #42 fixed the mechanism and deliberately deferred the scenario side; this is that change. Two defects, the second found by measuring rather than by reading: (1) the inflow side was differenced against a measured base window while the outflow side was compared with a hard-coded `baseOut = 0`, which #42's anatomy had already measured false (this fixture carries ~450 decline exits per window with no pulse at all); (2) BEFORE-AND-AFTER IS NOT A CONTROL - the three windows ran back to back on ONE sim, so every difference mixed the pulse with drift. Measured (`boomprobe --seed 20260806`, 120-tick windows in 20-tick sub-windows): the offer budget is FLAT at 1619-1627 per window (`Prospects.Step` sizes it from population and the tie stock alone, region-side and blind to city quality by its own anti-smuggling rule), while every other quantity moves MONOTONICALLY across base -> boom -> bust, which a +/-D pulse cannot explain: want share 83.96 -> 83.08 -> 80.21 %, declined share 16.04 -> 16.92 -> 19.79 %, priced-out share 3.69 -> 12.48 -> 12.17 %, decline exits 659 -> 655 -> 600. The priced-out share more than triples and does NOT come back when the pulse reverses. That is drift, it is bigger than the pulse, and the old arithmetic counted it as response. THE FIX is the design the `vacancy` row above already uses: a PAIRED unpulsed control built from the same seed and advanced through the same windows, with the pairing asserted (the unpulsed first window must be identical on both arms) rather than assumed, and the inflow margin divided by `Offered` so the region-side budget is not reported as the decision. MEASURED, at the instruments commit, `scenarios --only boombust` clean and with `--mutant-relative-outside` (the switch that restores the defect #42 removed - the outside door anchored on the city's OWN MeanAccess, so a spatially uniform improvement is structurally invisible to the come/stay margin - newly wired to a CLI flag so a whole fixture can be run mutated): seed 20260806 clean **+1.3 %** inflow (600 -> 600 departures, 0.0 %) PASS vs mutant **-0.3 %** (618 -> 635, +2.8 %) FAIL on both legs; seed 1 clean **+2.5 %** (593 -> 586, -1.2 %) PASS vs mutant **-0.2 %** (614 -> 638, +3.9 %) FAIL on both legs; seed 7 clean **+1.7 %** (607 -> 614, +1.2 %) **FAIL on the asymmetry leg** - 1.7 % against the 1.5x1.2 % = 1.8 % it needed, a tenth of a point - vs mutant **+0.46 %** (604 -> 596, -1.3 %) FAIL. The bar on the response leg is **+0.85 %**, the midpoint of the separating interval the three seeds measure - worst clean +1.3 %, best mutant +0.46 % - so it sits 0.45 points under the clean band and 0.39 over the mutant one, and is not set at either. It was +0.5 % while only the default seed had been run; seed 7's mutant at +0.46 % is exactly why a bar wants more than one seed's two arms under it. REMAINING RED, honestly: seed 7's asymmetry leg. That is the §6 property genuinely nearly failing on a fixture, not a bookkeeping artefact - the treated arm's bust window really does lose 1.2 % more residents than its control - and it is recorded rather than tuned away. The run that would settle whether it is a knife-edge or a real limit is `scenarios --only boombust` across a wider seed set; three seeds is what this round could afford. |

## Closed

| What | Was | Resolution |
|---|---|---|
| occupancy channel: realized vacancy softens rent (a PINNED-POSTED check, 51 of the suite) | red on {1, 20, 22, 28, 30, 41} of 57 at `88fc88c`, and on a DIFFERENT set at every world change before that ({9, 41, 910} at base 554b56a, {23, 41, 43, 46, 48} on the housing track, 48/57 on the goods track) | BOTH LEGS REWRITTEN; THE RED SET IS EMPTY. `occsweep --seeds 50` reads **57/57** at the instruments commit against **51/57** at base `88fc88c` (both run this session, base in its own worktree). Six verdicts change, all red -> green, and the base run says which leg each was: seeds 1, 20 and 22 failed the TRACKING leg's `worstErr < 0.5` at exactly 0.500 and seed 28 at 0.548 — a bound sitting ON the sweep's own maximum, with the price leg PASSING on all four — while seeds 30 and 41 failed the PRICE leg with the probed cluster's bid unmoved to three decimals (1.492 -> 1.492 and 1.582 -> 1.582). So 4 of the 6 were the un-headroomed bound the check's own comment warned about and 2 were the selection lottery; the brief that owned this row named only the second. NEITHER LEG NOW DEPENDS ON WORLD COMPOSITION. (a) is an exact EMA-recursion differential oracle against an independently recomputed per-submarket fill target — residual measured **0.0 on all 57 seeds** — carrying its own paired non-degeneracy floor: the same identity read against the NEXT submarket's inputs must break on >= 40 submarkets (measured 65-100). (b) reprices EVERY cleared candidate rather than the lowest-indexed one and asserts the MEDIAN response (measured 0.246-0.490 against a 0.70 bar) plus the share of them that respond at all (0.841-0.984 against 0.60); both bars sit between the measured range and the value a severed channel gives, never at the observed number. THE REWRITE IS NOT A LOOSENING, and that is measured with five mutants that each sever one half of the channel, `occsweep --seeds 50` under each: `--mutant-fill-blind` (the attraction term ignores FillEma — the exact channel the check names) **0/57**, and `--mutant-fill-pooled`, `--mutant-fill-alpha`, `--mutant-fill-frozen`, `--mutant-fill-shift` **0/57** each. The attribution separates cleanly, which is the point: the four FillEma-source mutants red leg (a) with residual 8.3E-2..1.2E-1 while leg (b)'s median still reads 0.363-0.465, and the blind mutant reds leg (b) with median 1.000 and 0 of 55 responding while leg (a)'s residual stays 0.0. A MUTE CLUSTER IS REAL, and here is how that was established rather than assumed: across the 57-seed `occprobe`, 154 of 3374 cleared submarkets do not move at all, and for 133 of them the demand SHARE does not move either — no household names that cluster as its best alternative, so its whole demand share is sitting tenants renewing, which `RebuildDemandShares` counts unconditionally by design. The other 21 lose share and still do not move the price: the demand ladder is flat where they read it. Both are properties of the world, which is why the verdict no longer rests on one draw from it. The pre-rewrite row's full four-world history is in the git history of this file. |
| seeds 20, 22, 23, 26, 28, 208, auction equilibrium | red (`unsold-above-reserve`, the CutVacancies indifference defect, bisected to `694fbd3`, resized 2→6 by the canary's first sweep) | Fixed by making every repair round a full re-clear from the reserve and deleting CutVacancies, the vacancy chains and the wait queues outright. The invariant "a non-full door posts its reserve" is now structural (SetPrices clamps the non-full branch; nobody mid-build holds a slot while bidding). Canary 33/39 → 38/39; LP-optimality gap 0.81–1.61% → 0.01–0.03% of LP*. Fiscal shift recorded in the fingerprint log: sumLR −0.95% (high −17.5%, low −13.2%), meanRent −7.7%, treasury −13.5%, household money +5.7% — phantom scarcity leaving the tax base. |
| seed 1, clearing price: quantity responds (population-collapse leg) | red at the outside-anchor commit (#42): after the 60% citywide cull the posted-curve bid read 2.90 → 2.95 (+1.7%), missing the `after < before×0.98` fall while fill stayed 1.00 → 1.00 (the vacancy disjunct fires on 0/300 seeds — the check's own comment); the composition-drift class the row named held 12/300 seeds before seed 1 joined it | Green at the per-cluster-memory commit (#34) — supply legs 9.52/0.83/0.83, all legs pass (#34 gate run, verify seed 1) — but NOT a targeted fix: the tie-driven admission re-roll changes WHO is present at the cull, the same composition channel that redded it at #42. The 12/300-seed class and the owed fix (a cull that actually produces vacancy on this fixture) are untouched; a later re-red on this leg belongs to that class. |
| seed 28, housing canary: auction equilibrium (improving-swap leg) | red at the per-cluster-memory commit (#34): one pair with price-free swap gain 0.173 against the leg's ε_i+ε_j band 0.166 (canary 38/39); registered there as a shortlist conditional-efficiency knife-edge | The registered mechanism reading was WRONG — the fix-round dissection (single-seed `canary --from 28` run, pair hh285/sub252 ↔ hh1609/sub767) decomposes the gain by the identity gain = envy_i + envy_j + (entry−posted gap at each door): 0.173 = 0.037 + 0.120 + 0.016 + 0.000, with each envy inside the envy leg's own band (0.167 / 0.165) — an ε-residual of the finite auction, fully inside the mechanism's stated guarantee, not value the shortlisted solve left on the table (one of the two doors even had 19 free rooms — nothing was hidden from anybody). The swap band was UNDER-DERIVED: it granted a PAIR of envies the allowance the envy leg's two-sided accounting grants ONE envy — the same arithmetic class as the envy band's own recorded fix ("1–8 households per seed at 1.3–2.0% against a 1.0% bar, all of them this arithmetic"). Band restated to compose the two envy bands (ε of each of the four valuations in the trade); the entry−posted gaps stay OUT of the band — with them in, the leg is a near-consequence of the envy sweep and loses its independent failure mode, and resting-price gaps are exactly how restricted competition shows. Falsifiability re-proven under the restated band: M5 (AuctionRepairRounds = 0) reds the swap leg on 11/11 seeds run — 0, 1, 2, 28, 138, 208, 271, 327, 549, 910, 6550, swaps 1 each, gains 0.076–0.336, every one above its restated band (fix-round mutant run, reverted). Canary 39/39 at the fix commit. The "shortlist under-covers on recomposed worlds" investigation this row previously named is NOT owed: the measured decomposition shows no under-coverage, and the M5 signature (gap-channel gains) remains gated. |
| seed 25, housing auction is a competitive equilibrium | red at `70ef971` (knife-edge ε-residual: envy 2 households, worst 1.50% vs the ~1% band, converged True, clean True; the one residual of canary 38/39) | Green at the per-cluster prospect-odds commit — canary 39/39, verify 29/29 — but NOT a targeted fix: undiluted prospect budgets change the admission mix on that fixture and the ε-residual falls back inside the band. The knife-edge CLASS is untouched; if seed 25 (or a neighbor) re-reds on later auction work, attribute to that class, not to the prospect-odds change. |
| boombust `--auction` printed "0 arrivals realized" | telemetry artifact at `70ef971`, not a fact | The scenario summed only `LastFlows` arrivals, which the auction path structurally zeroes; prospect admits live in `LastProspects`. Measured with admits counted: HEAD mechanism realizes 2507 arrivals on the same fixture (local-odds change: 2569). The scenario's ASSERTED margin still reads `DesiredBySegment` (+0 on the auction path) — that assertion dies with the posted path, not with prospect work. |
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
