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
| 26 | housing auction is a competitive equilibrium | red | **Bisected to `694fbd3`** (vacancy chains / CutVacancies). Signature: `unsold-above-reserve 1`, envy 0, swaps 0 — the CutVacancies indifference defect (cut price equals current price to two decimals; `sub681 34/36 at 1.52, cutting to 1.52 would let 35`). Fix in flight; these two seeds are its natural canary. |
| 208 | housing auction is a competitive equilibrium | red | Same as seed 26: **bisected to `694fbd3`**, same signature (`sub210 1/4 at 1.89, cutting to 1.88 would let 2`). |

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
