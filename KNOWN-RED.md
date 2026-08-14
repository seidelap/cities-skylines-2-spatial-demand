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

## verify (26 checks)

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 9 | occupancy channel: realized vacancy softens rent | red | Predates `2eeefc3`; fails identically at `2eeefc3`, `7dcaf08`, `ffd9a03`, `6104275`, `694fbd3`, HEAD. Not an auction-era regression. Unowned. |
| 26 | housing auction is a competitive equilibrium | red | **Bisected to `694fbd3`** (vacancy chains / CutVacancies). Signature: `unsold-above-reserve 1`, envy 0, swaps 0 — the CutVacancies indifference defect (cut price equals current price to two decimals; `sub681 34/36 at 1.52, cutting to 1.52 would let 35`). |
| 208 | housing auction is a competitive equilibrium | red | Same as seed 26: **bisected to `694fbd3`**, same signature (`sub210 1/4 at 1.89, cutting to 1.88 would let 2`). |
| 20, 22, 23, 28 | housing auction is a competitive equilibrium | red | Same defect family as 26/208, found by the canary's very first baseline sweep (`canary` at `0035ded`: 33/39, FAILED 20, 22, 23, 26, 28, 208). The known-red set for this defect was 2 seeds by bisect and is 6 by sweep. |

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
runs dry trivially. Conclusion recorded for the next design: the down-phase
must live *inside* the auction, not between its rounds — e.g. a clearance
step where free rooms **accept their standing wait-queue bids as
admissions** (the taker enters at its recorded bid, `Admitted` drops to
that bid, so the door closes behind the taker and no posted bargain is ever
visible to third parties). That is also the individual-decisions answer: a
household's own standing bid is accepted, rather than an ownerless door
computing a price.

Seeds 0–7, 13, 25 are 26/26 at HEAD `0b5b658`.

## scenarios (seed 1)

All three fail **byte-identically** at `2eeefc3`, `6104275`, and HEAD — the
detail lines do not differ by a digit — so none is a recent regression. All
predate the auction era; true origins untested further back.

| Scenario | Failing line | Notes |
|---|---|---|
| vacancy | (unchanged across all commits tested) | Unowned. |
| levels | `Spearman(realized level, ℓ*) spatial 0.34 vs vanilla −0.02` | The mod beats vanilla by a wide margin but sits under the absolute bar. The 0.34 is identical at every commit tested. Unowned. |
| perf | `Tier B refresh 30.0 ms at 8 parcels/cluster vs 58.8 ms at 16 → ratio ≈2 (cluster count fixed)` | Refresh scales with parcel count where the target is cluster-count scaling. A real, old property violation. Unowned. |

## Closed

| What | Was | Resolution |
|---|---|---|
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
