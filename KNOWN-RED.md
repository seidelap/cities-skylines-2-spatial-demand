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

## verify (34 checks)

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
−0.006..+0.005 against the 0.02 bar.

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

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 13 | Weber: extraction follows geology; recipes follow input sourcing | red | New with the DEFAULT FLIP: on the auction-default world, 6 of 13 single-input industrials sit on the cheapest-sourced recipe (46% vs the ≥55% bar; the margin is 2 firms) while the extraction leg is clean at 100% and the sector is bigger and more diverse than required (13 firms, 4 outputs). Seed-13-specific: the same check reads 100% on flip seeds 0-1 and passes 2, 3, 9, 25; posted-arm seed 13 was green. Precedent: Weber was the red on auction-arm seed 0 at `c0c584d` — auction-world Weber fragility moves seeds, it is not new. Wants its own investigation: which 7 firms, their delivered-cost gaps, and whether entry timing under prospect-driven population growth outruns trade-price settling. Unowned. AT THE OUTSIDE-ANCHOR COMMIT (#42) the same row stays red with different numbers — recipes 14% of 14 industrials, extraction still 100% — the anchored outside re-equilibrates the whole default world (baseline churn, population, entry timing), and Weber's verdict re-rolls with it. |
| 9 | Weber: extraction follows geology; recipes follow input sourcing | red | NEW at the outside-anchor commit (#42), on the EXTRACTION leg: 7 of 10 extractors on the best raw (70% vs the ≥90% bar); the recipe leg passes at 80%. Same fragility class as the seed-13 row — the anchored outside option changes the default world every fixture equilibrates in (churn, entry timing, trade-price settling), and Weber's seed-dependent verdict moves seeds with it, exactly as it moved 0 → 13 at the flip. No pricing/recipe rule is touched by #42 (the diff is Prospects/HousingAuction/Access outside-door legs only; posted fingerprint lanes are hash-identical). Belongs to the same owed Weber investigation as the row above. Unowned. |
| 1 | clearing price: quantity responds (population-collapse leg) | red | NEW at the outside-anchor commit (#42), in the check's own documented composition-drift class: after the 60% citywide cull the posted-curve bid reads 2.90 → 2.95 (+1.7%), inside the 20% drift band but missing the `after < before×0.98` fall the softens leg needs, while fill stays 1.00 → 1.00 so the vacancy disjunct cannot fire (that disjunct fires on 0/300 seeds — the check's own comment). The leg's anchor is a low QUANTILE of the households present; #42's baseline churn changes WHO is present at the cull on this fixture, not how anything is priced (the posted pricing rule is untouched; posted fingerprint lanes hash-identical). Pre-existing class: 12/300 seeds red on this same leg before #42 (44, 54, 77, 125, 128, 138, 206, 233, 236, 242, 288, 298); seed 1 joins it, seeds 9 and 13 pass. The owed fix is the one the check's comment already names: a cull that actually produces vacancy in this fixture. Unowned. |
| 9 | occupancy channel: realized vacancy softens rent (price responds on a cleared submarket) | red | PINNED POSTED at the flip commit (the check tests the posted path's own transmission; its world and verdicts are unchanged by the flip). Predates `2eeefc3`; fails identically at `2eeefc3`, `7dcaf08`, `ffd9a03`, `6104275`, `694fbd3`, HEAD. Not an auction-era regression. Unowned. THE CHECK WAS REWRITTEN at the check-debt commit — dead fallback arm and never-firing vacancy disjunct deleted, cleared-submarket selection made a required leg — and the rewrite changed no verdict: per-seed verdicts identical on all 57 occsweep seeds (0–49, 138, 208, 271, 327, 549, 910, 6550), 54 pass, with 9, 910 red as before and 41 red under both forms (newly observed by that sweep, not newly caused; failure mode on all three: the first-cleared cluster's bid does not move ≥ 5 % when its FillEma is dropped 1.0 → 0.2, while later clusters on the same seeds respond strongly — a selection-composition question, not a channel-severed one). |

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
| boombust | `migration-margin response +0 vs departure response +434` (2554 arrivals realized, outside-anchor commit) | MECHANISM HALF RESOLVED at the outside-anchor commit (task #42); the scenario's verdict stands red for reasons that are now measured rather than structural. The come/stay margin now prices the outside region as ONE MORE DOOR — `EconParams.OutsideAccessValue` through `AccessState.OutsidePremium`, the same clamp/exponent/scale rule every city door uses — at both margins (prospect reservation in `Prospects.Step`, resident `_outside` in `HousingAuction.BuildHouseholds`), and the previously-dead Declined outcome is live (`QuoteOutsider` splits attainability at surplus > 0 from the reservation gate). The DECISION margin responds: verify's uniform-pulse check (paired batches on one world, prices frozen) reads admitted share +0.037..+0.070 under a uniform pulse on 16/16 sweep seeds, mutant arm ±0.006. What the SCENARIO measures still does not go green, two measured causes (anatomy runs at this commit, anchor 20): (a) on a live sim the boom's own admissions raise incumbent bids (their outside fell with the premium, 1.27→1.11) and fill stock, so later lookers land in Priced/Declined — windowed margin 1383→1362 while admitted 1351→1203 and priced 32→159: the pulse goes into PRICES, not the windowed quantity; (b) the departure side's "+434" is baseline churn, not response — the scenario assumes `baseOut = 0` and never measures it; base-window decline exits are 456 vs bust-window 434, so the bust lifts nothing above baseline. `OutsideAccessValue` swept {14, 17, 20, 23, 26} on this fixture: windowed inflow response −45/−15/−21/−10/+17 against raw bust decline exits 456/424/434/422/378 — no anchor makes this margin beat 1.5× a number that is mostly churn. Wants: the scenario's margins restated (difference the outflow against a measured base window; separate the decision margin from absorption) — a scenario-side change, deliberately not smuggled into the mechanism commit. Unowned. |

## Closed

| What | Was | Resolution |
|---|---|---|
| seeds 20, 22, 23, 26, 28, 208, auction equilibrium | red (`unsold-above-reserve`, the CutVacancies indifference defect, bisected to `694fbd3`, resized 2→6 by the canary's first sweep) | Fixed by making every repair round a full re-clear from the reserve and deleting CutVacancies, the vacancy chains and the wait queues outright. The invariant "a non-full door posts its reserve" is now structural (SetPrices clamps the non-full branch; nobody mid-build holds a slot while bidding). Canary 33/39 → 38/39; LP-optimality gap 0.81–1.61% → 0.01–0.03% of LP*. Fiscal shift recorded in the fingerprint log: sumLR −0.95% (high −17.5%, low −13.2%), meanRent −7.7%, treasury −13.5%, household money +5.7% — phantom scarcity leaving the tax base. |
| seed 25, housing auction is a competitive equilibrium | red at `70ef971` (knife-edge ε-residual: envy 2 households, worst 1.50% vs the ~1% band, converged True, clean True; the one residual of canary 38/39) | Green at the per-cluster prospect-odds commit — canary 39/39, verify 29/29 — but NOT a targeted fix: undiluted prospect budgets change the admission mix on that fixture and the ε-residual falls back inside the band. The knife-edge CLASS is untouched; if seed 25 (or a neighbor) re-reds on later auction work, attribute to that class, not to the prospect-odds change. |
| boombust `--auction` printed "0 arrivals realized" | telemetry artifact at `70ef971`, not a fact | The scenario summed only `LastFlows` arrivals, which the auction path structurally zeroes; prospect admits live in `LastProspects`. Measured with admits counted: HEAD mechanism realizes 2507 arrivals on the same fixture (local-odds change: 2569). The scenario's ASSERTED margin still reads `DesiredBySegment` (+0 on the auction path) — that assertion dies with the posted path, not with prospect work. |
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
