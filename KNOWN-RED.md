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

## verify (35 checks)

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

The 34th and 35th are the task #30 goods checks (goods-price localization
level/tilt/thin, and per-resource goods-settlement reconciliation), added
at the local-goods-prices commit; `goodssweep` runs both alone across
seeds. Measured verdicts and their mutant runs are recorded in the checks'
own comments (TestRunner.cs) and in the rows below where they moved
standing reds.

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

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 0 | Weber: extraction follows geology; recipes follow input sourcing | red | NEW STATE AT THE LOCAL-GOODS-PRICES COMMIT (task #30), and a different leg than the seed-13 red it replaces: the DIVERSITY leg (1 distinct industrial output vs the >=2 bar) with the recipe leg PERFECT (6/6 single-input industrials on the cheapest-sourced recipe, extraction 100%). The localization delivered the check's premise — recipes now follow input sourcing because entrants price output at realized per-cluster comparables — and in doing so removed the citywide-EMA wobble that had been manufacturing output diversity as a freeze artifact (the seed-13 investigation row measured that diversity was entrants locking in different snapshots of one oscillating scalar). On this fixture Food stays the margin-dominant recipe while the city remains a food importer, so every entrant honestly picks it; diversity must now come from real price feedback (local Food surplus depressing realized origin prices), which this small fixture does not reach in 300 ticks. webersweep at this commit, seeds 0-6: 5/7 pass — 0 red (this row), 4 red on the EXTRACTION leg (75% = 6/8, recipe 82%, 3 outputs — same mode and number as the pre-existing row, base also red there), 6 HEALED (was extraction-red at base, now 100%/80%/2 outputs), 13 healed (verify 35/35, see Closed). Unowned. |
| 25 | occupancy channel: realized vacancy softens rent (price responds on a cleared submarket) | red | PINNED POSTED check; the posted world's goods prices moved with task #30 (the trade tier is shared by both market paths), and the knife-edge moved seeds with it: at the local-goods-prices commit seed 9 (red since before 2eeefc3, through the flip) reads GREEN (verify 35/35), and seed 25 reads red — the same price-leg mode the occsweep triad documented (first-cleared cluster 0: bid moves 2.333 -> 0.534 with fill pinned at 1.00 while the leg requires the response on the cleared submarket's own terms). Seeds measured at this commit: 1, 9, 13 green; 25 red; the 57-seed occsweep inventory (9, 41, 910 red at the check-debt commit) has not been re-run since the localization — re-sweep before attributing any occupancy verdict to later work. Same selection-composition family as before: a channel question about WHICH submarket the check reads, not a severed channel. Unowned. |

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

Targeted re-measure at the LOCAL-GOODS-PRICES commit (full battery not
re-run — the orchestrator runs it at track landings): tradebend GREEN
(41% bend — Closed table), modes GREEN (progression and equalization
unchanged, the single-source invariance held as predicted), stalled GREEN
(29 abandoned mid-build at the bust), perf GREEN (ratio 1.61 vs <1.8),
boombust still red on the same line (task #42's row below). levels not
re-run.

| Scenario | Failing line | Attribution |
|---|---|---|
| levels | `Spearman(realized level, ℓ*) spatial 0.27 vs vanilla 0.03` (bar ≥0.35 absolute; the ≥+0.15 relative leg is beaten at +0.24) | Auction-default measurement with the FORECAST ℓ* oracle — `realized:true` was built and measured WRONG at the flip round (0.27 → −0.21; the auction posts prices only for stock that exists, so the argmax mixed suppressed realized prices with forecasts — see the scenario's comment). The 0.35 bar and 2600-tick horizon were tuned on posted-path transients; wants a re-measured horizon before any bar move. Unowned. |
| boombust | `migration-margin response +0 vs departure response +440` (2667 arrivals realized) | REAL mechanism finding, isolated at the flip commit once the margin telemetry was honest (it now sums posted Desired + prospect Admitted + Priced — the decision before the absorption budget, both paths): the auction path's come/stay decision reads only RELATIVE within-city access (`HousingAuction._premium` normalizes `AccessValue` by citywide `MeanAccess`), so a spatially UNIFORM amenity pulse moves neither premiums nor the reservation and is structurally invisible to the inflow decision — while a uniformly better city should win each individual's city-vs-outside comparison. The outflow side responds (+440 decline exits; re-measured +430 at the local-goods-prices commit — state unchanged) through slower channels. Wants: the OUTSIDE region's access level anchoring the come/stay margin (a genuine property of the outside world — the same demotion the outside wage and outside employment odds already got), not `MeanAccess`. Task #42. |

## Closed

| What | Was | Resolution |
|---|---|---|
| seed 13, Weber recipe leg | red since the flip commit (6/13 on the cheapest-sourced recipe, 46% vs >=55%; investigation row concluded the citywide LocalPrice statistic was the binding cause and parked the fix behind task #30) | Green at the local-goods-prices commit — verify --seed 13 reads 35/35. Exactly the predicted mechanism: entrants and the check's oracle now price output at realized per-cluster origin statistics, so the recipe argmax and the cheapest-input oracle agree where the raw-cost structure does. Not a targeted fix — the Weber knife-edge MOVED (seed 0 now red on the DIVERSITY leg, row above): localized prices deliver alignment and remove the wobble that manufactured diversity. |
| seed 9, occupancy channel (posted pinned) | red since before 2eeefc3 through the flip commit (price leg on the first-cleared cluster) | Green at the local-goods-prices commit (verify 35/35): the posted world's firm money and land bids moved with the per-cluster goods prices and the first-cleared-cluster composition changed. Not a targeted fix — the same knife-edge came up red on seed 25 (row above); the check family's selection-composition question stands. |
| tradebend scenario | near-bar red at the flip commit (bend 30% vs the >=30% bar, marginal 1.83 vs flat 2.60) | Green at the local-goods-prices commit: bend 41% (sustained 81/tick, marginal 1.53 vs flat 2.60). Phase-2 exports now draw cheapest-haul-first per (source, exit) pair instead of at a volume-weighted mean haul, so the busiest exit carries a deeper sustained position. The bar did not move — it was measured against the exit law, which is untouched. |
| seeds 20, 22, 23, 26, 28, 208, auction equilibrium | red (`unsold-above-reserve`, the CutVacancies indifference defect, bisected to `694fbd3`, resized 2→6 by the canary's first sweep) | Fixed by making every repair round a full re-clear from the reserve and deleting CutVacancies, the vacancy chains and the wait queues outright. The invariant "a non-full door posts its reserve" is now structural (SetPrices clamps the non-full branch; nobody mid-build holds a slot while bidding). Canary 33/39 → 38/39; LP-optimality gap 0.81–1.61% → 0.01–0.03% of LP*. Fiscal shift recorded in the fingerprint log: sumLR −0.95% (high −17.5%, low −13.2%), meanRent −7.7%, treasury −13.5%, household money +5.7% — phantom scarcity leaving the tax base. |
| seed 25, housing auction is a competitive equilibrium | red at `70ef971` (knife-edge ε-residual: envy 2 households, worst 1.50% vs the ~1% band, converged True, clean True; the one residual of canary 38/39) | Green at the per-cluster prospect-odds commit — canary 39/39, verify 29/29 — but NOT a targeted fix: undiluted prospect budgets change the admission mix on that fixture and the ε-residual falls back inside the band. The knife-edge CLASS is untouched; if seed 25 (or a neighbor) re-reds on later auction work, attribute to that class, not to the prospect-odds change. |
| boombust `--auction` printed "0 arrivals realized" | telemetry artifact at `70ef971`, not a fact | The scenario summed only `LastFlows` arrivals, which the auction path structurally zeroes; prospect admits live in `LastProspects`. Measured with admits counted: HEAD mechanism realizes 2507 arrivals on the same fixture (local-odds change: 2569). The scenario's ASSERTED margin still reads `DesiredBySegment` (+0 on the auction path) — that assertion dies with the posted path, not with prospect work. |
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
