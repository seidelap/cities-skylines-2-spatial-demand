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

## verify (41 checks)

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

**AT THE TWO-TRACK MERGE** (housing: outside-access anchor, per-cluster ties
and calibration, owner doors; goods: the Weber investigation, per-cluster goods
prices and its fix round) the registry's rows are re-derived from runs on the
MERGED tree, because neither branch's rows could be textually correct about the
combination — each track healed reds the other left standing, and one red exists
only in the combination. Measured here: verify **41 checks**; seed 1 40/41, seed
9 41/41, seed 13 40/41 (the two rows below; the fingerprint red at merge time is
the expected lane move, accepted in this commit). `canary` 39/39 and
`laborcanary` 39/39 — both markets' equilibrium guarantees survive the union
untouched. `webersweep` 24/26 (red 0, 5 — both on the diversity leg) against 13
red at the flip. `occsweep --seeds 50` 51/57. The per-branch numbers each row
retains are kept for ATTRIBUTION, not as current state.

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 0, 5 | Weber: extraction follows geology; recipes follow input sourcing | red | MEASURED ON THE TWO-TRACK MERGE, which is the shipping state and supersedes every per-branch tally below: `webersweep` (seeds 0-25) reads **24/26**, red only on 0 and 5, against 22/26 {0,4,7,16} on the goods track alone and reds on 9 and 13 on the housing track alone (all three sweeps run by the orchestrator on their own trees). Seeds 1, 9 and 13 are GREEN on the merge — each track healed part of what the other left red, which is why no branch's row could be textually correct about the combination. **Seed 5 fails on the DIVERSITY PRECONDITION ALONE**: 10 extractors at 100% on the best raw and 7 of 7 single-input industrials on the cheapest-sourced recipe — the Weber property the check exists to assert holds PERFECTLY — and the verdict is red because the city produces 1 distinct industrial output against a >=2 bar. That is a fixture-adequacy failure, not an economics failure, and it is the shape the whole Weber history has been pointing at: the alignment legs now pass wherever the premise is delivered. Seed 0 is a real miss on the extraction leg (10 extractors, 80% on best raw vs the >=90% bar) with recipes passing at 67% of 6 and the same 1-output diversity fail. THE OWED INVESTIGATION is therefore re-scoped by measurement: it is no longer 'why do recipes misalign' (localized prices answered that - see Closed) but 'when is one industrial output the correct answer for a small city, and should the diversity leg be a precondition rather than an assertion'. Unowned. PER-BRANCH HISTORY, retained for attribution: New with the DEFAULT FLIP: on the auction-default world, 6 of 13 single-input industrials sit on the cheapest-sourced recipe (46% vs the ≥55% bar; the margin is 2 firms) while the extraction leg is clean at 100% and the sector is bigger and more diverse than required (13 firms, 4 outputs). At the FLIP commit it was seed-13-specific — the same check read 100% on flip seeds 0-1 and passed 2, 3, 9, 25 (posted-arm seed 13 green); that cohort statement is DATED: at the outside-anchor commit the extraction leg is red on seeds 0, 9 and 25 (fix-round measurement — class row below). Precedent: Weber was the red on auction-arm seed 0 at `c0c584d` — auction-world Weber fragility moves seeds, it is not new. Wants its own investigation: which 7 firms, their delivered-cost gaps, and whether entry timing under prospect-driven population growth outruns trade-price settling. Unowned. AT THE OUTSIDE-ANCHOR COMMIT (#42) the same row stays red with different numbers — recipes 14% of 14 industrials, extraction still 100% — the anchored outside re-equilibrates the whole default world (baseline churn, population, entry timing), and Weber's verdict re-rolls with it. AT THE PER-CLUSTER-MEMORY COMMIT (#34) seed 13 is GREEN (extraction 100%, recipes 85% of 13) — the tie-driven admission re-roll moves the verdict again, in Weber's documented seed-moving way; the class row below carries the standing red. AT THE OWNER-DOOR COMMIT (#41) seed 13 is RED again, and on the fixture's POPULATION PRECONDITION rather than on any alignment: 4 extractors against the `extract >= 5` minimum, with extraction 100% on best raw and recipes passing at 69% of 13. Measured against the branch tip in the same session (tip 209e232: 6 extractors, 100%, recipes 85% of 13, GREEN). #41 touches no pricing, recipe or siting rule — its diff is the housing auction, the owner tag and the checks; LandAccounting changes only its header comment and Trade is untouched — so this is the same re-equilibration channel #42 and #34 moved the verdict through, now reaching the industrial sector's SIZE. | || AND THE EXTRACTION-LEG CLASS: NEW at the outside-anchor commit (#42), on the EXTRACTION leg — seed 9 registered at that commit; seeds 0 and 25 found red at the SAME commit by the refuter's wider sweep (finding F1) and registered at the fix round, which reproduced them (fix-round verify runs, seeds 0 and 25; both are FULLY GREEN at base 554b56a, extraction 100%). Numbers at the fix commit: seed 9 — 7 of 10 extractors on the best raw (70% vs the ≥90% bar), recipe leg passes at 80%; seed 0 — 8 of 9 extractors (89%), the margin one extractor, recipes pass at 82% of 11; seed 25 — 7 of 8 extractors (88%), recipes pass at 82% of 17. Seeds 2 and 5 pass in full. Tally across the seeds measured in the two sessions (0, 1, 2, 5, 9, 13, 25): Weber red on 4 of 7 — 0, 9, 25 on extraction, 13 on recipes — against 1 of 7 at base; the owed Weber investigation is that much bigger than a single-seed row suggested. Same fragility class as the seed-13 row — the anchored outside option changes the default world every fixture equilibrates in (churn, entry timing, trade-price settling), and Weber's seed-dependent verdict moves seeds with it, exactly as it moved 0 → 13 at the flip. No pricing/recipe rule is touched by #42 (the diff is Prospects/HousingAuction/Access outside-door legs only; posted fingerprint lanes are hash-identical). Belongs to the same owed Weber investigation as the row above. Unowned. AT THE PER-CLUSTER-MEMORY COMMIT (#34) the class re-rolls again: seeds 0 and 9 GREEN (extraction 100% both, recipes 90%/78%), seed 25 red and DEEPER — extraction 44% of 9 extractors (4 on best raw) vs the ≥90% bar, recipes pass at 81% of 16 (#34 gate runs, verify seeds 0, 9, 25). Tally on the measured cohort {0, 1, 9, 13, 25}: 1 of 5 red at #34 against 4 of 7 at #42. The owed investigation is unchanged in shape and now owns seed 25's deep extraction miss. AT THE OWNER-DOOR COMMIT (#41) seed 9 is RED again on this leg — 6 of 7 extractors on the best raw (86% vs the ≥90% bar, the margin one extractor), recipes passing at 80% of 10 — against a tip-209e232 run in the same session that reads 9 extractors, 100%, GREEN. Same channel as the rows above: the auction-default world re-equilibrates and Weber's verdict re-rolls with it; #41 touches no pricing or recipe rule. |
| 13 | clearing price: quantity responds (population-collapse leg) | red | NEW AT THE TWO-TRACK MERGE, and an INTERACTION - it is green on each track alone, measured by the orchestrator on all three trees: housing tip 01c7543 reads bid 2.96 -> 2.51 after 638 citywide exits (PASS, verify 38/39), the goods track reads verify 35/35 at seed 13, and the merge reads bid 2.15 -> **2.56** after 695 exits - the price RISES where the leg requires it to fall. The supply and demand legs stay monotone and correct on the merge (supply x{0.5,2,8} -> 8.56/0.52/0.52; demand x{0.5,1,2} -> 0.52/2.15/8.56; 25-point sweep monotone), so what fails is only the population-collapse direction. HYPOTHESIS, NOT MEASURED: per-parcel owner asks (#41) hold occupied doors at their floors while the survivors of a collapse re-sort upward into the best stock, so the probed submarket's marginal bidder can end up richer than before the collapse - the composition effect outrunning the scarcity effect. That is a guess and must be measured before anyone acts on it: the honest next step is to re-run the collapse leg with owner asks disabled (`--owner-ask 0`) and with localized goods prices mutated off (`--mutant-citywide-goods`) and see which one restores the fall. Unowned.
| 1, 20, 22, 28, 30, 41 (was 9, 41, 910 at base) | occupancy channel: realized vacancy softens rent (price responds on a cleared submarket) | red | MEASURED ON THE TWO-TRACK MERGE: `occsweep --seeds 50` reads **51/57**, red on {1, 20, 22, 28, 30, 41}, against 54/57 {9, 41, 910} at base 554b56a, 52/57 {23, 41, 43, 46, 48} on the housing track alone and 48/57 on the goods track alone. Only seed 41 is red in all four sets: the failing SET re-rolls almost completely every time the default world moves, which is the strongest evidence yet that this check's verdict is decided by which cluster its first-cleared selection lands on rather than by the transmission it names. The check is PINNED POSTED and its transmission is provably untouched by both tracks (the probed function takes no price context and no goods price can reach it; the goods round measured 0 of 57 detail lines byte-identical while the substantive price leg's median response DEEPENED). Count 3 -> 6 of 57. Still unowned; the owed work is now specific - make the selection deterministic-in-mechanism or make the leg measure the channel without depending on which cluster clears first. HISTORY: PINNED POSTED at the flip commit (the check tests the posted path's own transmission; its world and verdicts are unchanged by the flip). Predates `2eeefc3`; fails identically at `2eeefc3`, `7dcaf08`, `ffd9a03`, `6104275`, `694fbd3`, HEAD. Not an auction-era regression. Unowned. THE CHECK WAS REWRITTEN at the check-debt commit — dead fallback arm and never-firing vacancy disjunct deleted, cleared-submarket selection made a required leg — and the rewrite changed no verdict: per-seed verdicts identical on all 57 occsweep seeds (0–49, 138, 208, 271, 327, 549, 910, 6550), 54 pass, with 9, 910 red as before and 41 red under both forms (newly observed by that sweep, not newly caused; failure mode on all three: the first-cleared cluster's bid does not move ≥ 5 % when its FillEma is dropped 1.0 → 0.2, while later clusters on the same seeds respond strongly — a selection-composition question, not a channel-severed one). AT THE OWNER-DOOR COMMIT (#41) THE RED SET MOVES, in both directions: `occsweep --seeds 50` reads 54/57 red {9, 41, 910} at tip 209e232 and 52/57 red {23, 41, 43, 46, 48} at #41 (both sweeps run in the same session, tip worktree and item worktree). This row's own seed 9 goes GREEN — bid 1.653 at full occupancy, responding, and `verify --seed 9` agrees — while four seeds that passed at tip go red; 41 stays red throughout. Neither direction is a channel fix or a channel break: #41 draws every household's owner disposition at birth from the id hash instead of the posted arrival loop's own RNG roll, so the posted world re-rolls and the cluster the check's first-cleared selection lands on re-rolls with it — the same selection-composition fragility the rewrite paragraph describes, now measured on a wider set. Count 3 → 5 of 57. Still unowned, still unexplained. |

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

## scenarios (default scenario seed, measured at the flip commit)

State under the AUCTION DEFAULT: 7/10. Two heals worth naming — `vacancy`
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
| levels | `Spearman(realized level, ℓ*) spatial 0.27 vs vanilla 0.03` (bar ≥0.35 absolute; the ≥+0.15 relative leg is beaten at +0.24) | Auction-default measurement with the FORECAST ℓ* oracle — `realized:true` was built and measured WRONG at the flip round (0.27 → −0.21; the auction posts prices only for stock that exists, so the argmax mixed suppressed realized prices with forecasts — see the scenario's comment). The 0.35 bar and 2600-tick horizon were tuned on posted-path transients; wants a re-measured horizon before any bar move. Unowned. |
| tradebend | `bend 30 %` vs the ≥30 % bar (marginal 1.83 vs flat 2.60) | Pre-existing near-bar red at this seed on BOTH arms (posted sustained 78/tick, auction 69) — not flip-caused. Unowned. |
| boombust | `migration-margin response +0 vs departure response +434` (2554 arrivals realized, outside-anchor commit) | MECHANISM HALF RESOLVED at the outside-anchor commit (task #42); the scenario's verdict stands red for reasons that are now measured rather than structural. The come/stay margin now prices the outside region as ONE MORE DOOR — `EconParams.OutsideAccessValue` through `AccessState.OutsidePremium`, the same clamp/exponent/scale rule every city door uses — at both margins (prospect reservation in `Prospects.Step`, resident `_outside` in `HousingAuction.BuildHouseholds`), and the previously-dead Declined outcome is live (`QuoteOutsider` splits attainability at surplus > 0 from the reservation gate). The DECISION margin responds: verify's uniform-pulse check (paired batches on one world, prices frozen) reads admitted share +0.037..+0.070 under a uniform pulse on 16/16 sweep seeds, mutant arm ±0.006. What the SCENARIO measures still does not go green, two measured causes (anatomy runs at this commit, anchor 20): (a) on a live sim the boom's own admissions raise incumbent bids (their outside fell with the premium, 1.27→1.11) and fill stock, so later lookers land in Priced/Declined — windowed margin 1383→1362 while admitted 1351→1203 and priced 32→159: the pulse goes into PRICES, not the windowed quantity; (b) the departure side's "+434" is baseline churn, not response — the scenario assumes `baseOut = 0` and never measures it; base-window decline exits are 456 vs bust-window 434, so the bust lifts nothing above baseline. `OutsideAccessValue` swept {14, 17, 20, 23, 26} on this fixture: windowed inflow response −45/−15/−21/−10/+17 against raw bust decline exits 456/424/434/422/378 — no anchor makes this margin beat 1.5× a number that is mostly churn. Wants: the scenario's margins restated (difference the outflow against a measured base window; separate the decision margin from absorption) — a scenario-side change, deliberately not smuggled into the mechanism commit. Unowned. AT THE PER-CLUSTER-MEMORY COMMIT (#34) the numbers re-roll (+0 vs +422, 2572 arrivals realized) with verdict and mechanism reading unchanged — the tie bonus and the diaspora term in prominence change who looks and where admits land, not the windowed-margin arithmetic this row already owns. |

## Closed

| What | Was | Resolution |
|---|---|---|
| seeds 20, 22, 23, 26, 28, 208, auction equilibrium | red (`unsold-above-reserve`, the CutVacancies indifference defect, bisected to `694fbd3`, resized 2→6 by the canary's first sweep) | Fixed by making every repair round a full re-clear from the reserve and deleting CutVacancies, the vacancy chains and the wait queues outright. The invariant "a non-full door posts its reserve" is now structural (SetPrices clamps the non-full branch; nobody mid-build holds a slot while bidding). Canary 33/39 → 38/39; LP-optimality gap 0.81–1.61% → 0.01–0.03% of LP*. Fiscal shift recorded in the fingerprint log: sumLR −0.95% (high −17.5%, low −13.2%), meanRent −7.7%, treasury −13.5%, household money +5.7% — phantom scarcity leaving the tax base. |
| seed 1, clearing price: quantity responds (population-collapse leg) | red at the outside-anchor commit (#42): after the 60% citywide cull the posted-curve bid read 2.90 → 2.95 (+1.7%), missing the `after < before×0.98` fall while fill stayed 1.00 → 1.00 (the vacancy disjunct fires on 0/300 seeds — the check's own comment); the composition-drift class the row named held 12/300 seeds before seed 1 joined it | Green at the per-cluster-memory commit (#34) — supply legs 9.52/0.83/0.83, all legs pass (#34 gate run, verify seed 1) — but NOT a targeted fix: the tie-driven admission re-roll changes WHO is present at the cull, the same composition channel that redded it at #42. The 12/300-seed class and the owed fix (a cull that actually produces vacancy on this fixture) are untouched; a later re-red on this leg belongs to that class. |
| seed 28, housing canary: auction equilibrium (improving-swap leg) | red at the per-cluster-memory commit (#34): one pair with price-free swap gain 0.173 against the leg's ε_i+ε_j band 0.166 (canary 38/39); registered there as a shortlist conditional-efficiency knife-edge | The registered mechanism reading was WRONG — the fix-round dissection (single-seed `canary --from 28` run, pair hh285/sub252 ↔ hh1609/sub767) decomposes the gain by the identity gain = envy_i + envy_j + (entry−posted gap at each door): 0.173 = 0.037 + 0.120 + 0.016 + 0.000, with each envy inside the envy leg's own band (0.167 / 0.165) — an ε-residual of the finite auction, fully inside the mechanism's stated guarantee, not value the shortlisted solve left on the table (one of the two doors even had 19 free rooms — nothing was hidden from anybody). The swap band was UNDER-DERIVED: it granted a PAIR of envies the allowance the envy leg's two-sided accounting grants ONE envy — the same arithmetic class as the envy band's own recorded fix ("1–8 households per seed at 1.3–2.0% against a 1.0% bar, all of them this arithmetic"). Band restated to compose the two envy bands (ε of each of the four valuations in the trade); the entry−posted gaps stay OUT of the band — with them in, the leg is a near-consequence of the envy sweep and loses its independent failure mode, and resting-price gaps are exactly how restricted competition shows. Falsifiability re-proven under the restated band: M5 (AuctionRepairRounds = 0) reds the swap leg on 11/11 seeds run — 0, 1, 2, 28, 138, 208, 271, 327, 549, 910, 6550, swaps 1 each, gains 0.076–0.336, every one above its restated band (fix-round mutant run, reverted). Canary 39/39 at the fix commit. The "shortlist under-covers on recomposed worlds" investigation this row previously named is NOT owed: the measured decomposition shows no under-coverage, and the M5 signature (gap-channel gains) remains gated. |
| seed 25, housing auction is a competitive equilibrium | red at `70ef971` (knife-edge ε-residual: envy 2 households, worst 1.50% vs the ~1% band, converged True, clean True; the one residual of canary 38/39) | Green at the per-cluster prospect-odds commit — canary 39/39, verify 29/29 — but NOT a targeted fix: undiluted prospect budgets change the admission mix on that fixture and the ε-residual falls back inside the band. The knife-edge CLASS is untouched; if seed 25 (or a neighbor) re-reds on later auction work, attribute to that class, not to the prospect-odds change. |
| boombust `--auction` printed "0 arrivals realized" | telemetry artifact at `70ef971`, not a fact | The scenario summed only `LastFlows` arrivals, which the auction path structurally zeroes; prospect admits live in `LastProspects`. Measured with admits counted: HEAD mechanism realizes 2507 arrivals on the same fixture (local-odds change: 2569). The scenario's ASSERTED margin still reads `DesiredBySegment` (+0 on the auction path) — that assertion dies with the posted path, not with prospect work. |
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
