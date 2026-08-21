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

## verify (64 checks)

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
pooled field, the crowding response, and — until the entrant-survival commit —
calibration against realized takings), added at the real-staffing commit.
`shopsweep` runs both fixtures
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

The 52nd to 60th are the task #19 non-residential parity legs — one fixture,
four properties each asserted TWICE, plus a conservation leg on the parity arm: once as it stands on the shipped default
(the leg states the defect, and IS the non-degeneracy floor for its partner)
and once with `EconParams.NonResLandParity` on (the leg states the property).
`paritysweep` runs the fixture alone across seeds; `parityprobe` is the census
every number in the item was measured from. The four: extractor land assessed
on its own cluster's geology rather than a flat 0.5; a one-seller (output,
cluster) cell never admitted as its own comparable (the §3 circularity guard's
firm analog); office production carrying the level and condition terms its
assessment prices it on; and an unmet land charge reaching a stated outcome
(grace, sort down, release the parcel) instead of being forgiven forever; the
ninth leg reconciles the parity arm's money, because the arrears exit is the
only new way a firm can leave and the suite's ledger fixture never reaches it.

**THE PARITY SWITCH DID NOT FLIP, AND THE BLOCKER IS MEASURED.**
`EconParams.NonResLandParity` ships FALSE and the default world is BYTE-
IDENTICAL to 88fc88c — `fingerprint --check` reads all 13 lanes matching with
no accept, and `webersweep` reproduces the standing seed-0 red with the same
numbers (10 extractors, 80% on best raw, 1 distinct output). Every one of the
four makes the assessment MORE accurate, and the non-residential base cannot
carry an accurate assessment: ℓ* sits at the CORNER for every firm sector
(mean TargetLevel 5.00 for office and industrial against standing levels 3.06
and 2.00), because the firm bid is a per-slot margin formula scaled by
Quality(ℓ) with nothing on the other side to bound it, where the residential
ladder is bounded by the auction's posted price and its shadow queue. At 95%
capture that exceeds what a firm below ℓ* can earn, and a vacated parcel does
not re-let either, because firm entry compares a bid at the parcel's CURRENT
level against an assessment priced at ℓ*. Measured ON: uncollected firm share
64% → 21% with no firm past the clock, but `webersweep --seeds 8` reads 0/8
against 6/8 — every failure on the extractor POPULATION precondition (1 to 4
against the ≥5 bar) with the alignment legs at 100% — standing offices 62 → 8,
and verify seed 9 loses the same check (48/51). Attributed by running
`webersweep --seeds 2` once per mutant: the collapse is the GEOLOGY correction
alone (10 and 8 extractors with it removed and the other three on, against 3
and 1 with all four). WHAT WOULD UNBLOCK IT: a realized comparable per
(cluster, sector, level) over OTHER firms, which is the bound the residential
ladder has and the same assessment-comparables pattern this item's guard
installs on the goods side. Unowned.

AT THE PARITY COMMIT the gate is the base's, plus nine green: verify **59/60**
on seed 1 (the standing occupancy red, unchanged leg), **60/60** on seed 9 and
**59/60** on seed 13 (the standing clearing-price population-collapse red) —
so every one of the nine new legs is green on all three gate seeds, and no
standing red moved in either direction. `canary` 39/39, `laborcanary` 39/39,
`paritysweep --seeds 4` 6/6 (seeds 0-3, 9, 13), `webersweep --seeds 8` 6/8 red
{0, 5}, `fingerprint --check` all 13 lanes matching with no accept, and the
`fiscal` scenario 1/1 (corridor +15.7% vs control −2.8%). `occsweep`,
`goodssweep`, `shopsweep` and the rest of the scenario battery were NOT re-run:
the default world is byte-identical, which the fingerprint and the reproduced
Weber verdicts measure rather than assume.

THE OWNER-DOOR QUESTION (item #19's fourth) IS OUT OF SCOPE, MEASURED RATHER
THAN ASSUMED. `Allocation.cs`'s owner-tagging gate (`pl.Use ==
ZoneKind.ResidentialLow && h.OwnerMinded`) admits no other zone kind, and
nothing else in the tree ever sets `Parcel.OwnerHousehold` on non-residential
land: `parityprobe` seed 1, 400 ticks reads 367 built non-residential parcels,
0 with `OwnerHousehold >= 0`, 0 with a standing ask. That is not a missing
flag — the residential owner/door/ask apparatus (`HousingAuction`'s reserve,
fold and no-ratchet rules) models a household's choice between renting and
owning CAPITAL EQUITY in its home, drawn at birth (`Household.DrawAtBirth`,
`OwnerMinded`) and cleared through the housing auction's own door market. A
firm has no analog of any of that: every firm today occupies speculatively
built stock (the same generic `Construction`/`Assess` residual-flow logic
that builds residential stock, with no distinct "developer-landlord" role)
and pays S + the land levy on it exactly like a residential RENTER, never an
owner; there is no firm-side auction (`FirmLifecycle`'s entry is a bid-vs-
assessment probability draw, not a competitive door market), no firm analog
of `OwnerMinded`, and no firm financing model for a capital purchase, so
nothing on the other side of a firm's building could ever bid to buy it. Land
and tax parity — the thing #19 was asked to fix — does not require this: the
levy already falls on firms and households alike regardless of tenure (design
§3/§4.3's whole point). Modeling a firm that owns its own building outright
would be a firm-side capital-ownership market invented from a standing start,
which is a separate, larger design question than this item's mandate. Left
unowned rather than retrofitted here.

**AT THE ENTRANT-SURVIVAL COMMIT the check count is UNCHANGED at 51: one leg
retired, one added.** Retired: the counted-intents fixture's CALIBRATION leg
(the counted read at entry against realized takings, band [0.4, 4.0]). It read
GREEN on all 26 seeds at the #20 commit while 88 % of mid-run entrants were
dying inside 40 ticks, because its sample accumulates from age 6 and needs five
ticks of trading — it only ever contained entrants that lived past age 11, in a
population whose defining feature was that it mostly died. Selection can make a
leg vacuous exactly as construction can (#30's F1); this is the second instance
in the file. Added in its place: **entrant survival**, a two-arm Ward leg in the
same fixture — the clean arm bounds the COUNT of commercial firms born after
tick 20, observable to age 40, that die inside it; the embedded
`MutantEntryReferenceMass` arm restores the defect verbatim and must produce the
mode or the leg asserts nothing. First measured `shopsweep --seeds 6` at this
commit, both arms 320 ticks: clean **0, 0, 0, 1, 1, 0** on seeds 0-5 against
mutant **25, 33, 45, 33, 34, 26**; bound 8, floor 12 from that sample. THE FULL
26-SEED SWEEP BROKE THAT FLOOR — this item's own history precedent (item #20's
order-dependent rationing hiding its own capacity mutant) applies to a bound as
much as to a leg: a number set from 6 seeds is untested past its sweep. Seeds
15 and 18 read mutant deaths of **7** and **9**, below the 6-seed floor of 12.
RE-MEASURED over the full `shopsweep --seeds 26`: mutant range **7**-**45**
(worst at seed 15), clean arm never above 1 across the same 26 seeds. Floor
reset to **5** — below the new worst (7) with margin, still 5× the clean arm's
worst reading. Same run exposed the SAME thinning in two checks this item does
not own but whose passing world this item's fix reshapes: `BindFloor` (the
commercial-staffing fixture's capacity-binds-at-all floor, item #20's own) fell
from a measured worst of 28 to **15** at seed 21 — fewer entrants sited where
they cannot be served means fewer capacity-bound firm-ticks on some seeds, a
direct and expected consequence of fixing the entry forecast, not a new defect
— reset from 20 to **10**. `ThinFloor` (the counted-intents fixture's evidence
floor, also item #20's) fell from a measured worst of ≥4 to **3** at seeds 3
and 15 — different parcels get occupied, which shifts which households' own
`bestSys` backs which cluster's evidence count at the margin — reset from 4 to
**3**. All three resets are recorded at their sites in TestRunner.cs with the
full 26-seed distribution that produced them. Re-measured after the resets:
`shopsweep --seeds 26` **26/26** (see gate table).

**THE ENTRANT DEATH MODE WAS A DEFECT AND IS FIXED.** Reproduced at the merge
(`shopprobe --seeds 4`, 300 ticks): **137 of 153** shops born mid-run died
within 40 ticks on the store-level arm against **0 of 40** pooled.
`entrydiag --seeds 4` separated the causes on the same run: not a cold start
(130 of the 132 had taken custom; first custom at age p50 5, which is the
refresh grid — and the A3 cold-start rules are unreachable in the shipping
calibration, both living in `LaborAuction.BuildDoors` while `LaborAuction`
ships false), not crowding (dying and surviving entrants alike arrive at
clusters with a median of 0 other shops; only 28 of 143 shared a refresh window
with another entrant), but the FORECAST: the entry decision read the counted
field at a fixed cluster reference mass — a 6-slot condition-1 shop — while the
market that generates the catchment scores the building the firm would occupy,
and entrants take over standing buildings whose condition has decayed. The
reference read is **1.63×** the own-mass read at the median and never smaller
(own/reference p10 0.374, p50 0.612, p90 1.000), and over-predicts realized
custom **3.4×** (p50 0.293) against the own-mass read's **2.05×** (p50 0.488).
The fix reads the field at this parcel's own mass and divides by this parcel's
own slots, with condition priced once. Measured on the same command: entrants
dying inside 40 ticks **137/153 → 7/13**, commercial deaths **77.5 → 42.5**,
alive **101.5 → 100.2**, vacancy **40 % → 41 %**. `fingerprint --check` before
the accept: all thirteen pre-existing lanes hash-identical, zero MISMATCH —
flag-off is untouched.

**WHAT THE FIX EXPOSES, and it is unowned:** a commercial parcel that falls
vacant is an absorbing state. It pays no S, so `ConditionDecay` walks it to the
0.05 floor, and the vacancy drain empties the escrow `Leveling` would have
restored it from — measured at this commit, commercial parcels standing vacant
have condition p50 **0.05** (`entrydiag`, 192-279 such parcels per run). Under
the dishonest read those buildings were continuously re-occupied by firms that
died at age p50 9; under the honest one nobody takes them and they stay vacant.
That is why the vacancy number does not close, and it is a defect of the
condition/renovation path rather than of the entry decision. Unowned.

**THE UNBLOCK LIST WAS WORKED TO A DECISION: THE FLAG STAYS FALSE.** Decision
rule (stated before measuring): flip only if all six items are met AND the ON
arm's census is no worse than the pooled arm's on BOTH numbers AND no canary
moves. Per item, this commit:
1. The two commerce checks green across `shopsweep --seeds 26` on the ON
   arm — MET (see above; the sweep itself already runs `StoreLevelSpending=
   true` unconditionally, so this was never gated on the flag's own default).
2. `webersweep` on the ON arm no worse than the OFF arm's record — **NOT
   MET**. `webersweep --seeds 26 --store-level`: **21/26**, red {0, 2, 5, 9,
   21}, against the OFF arm's own record at base, **24/26**, red {0, 5}
   (independently re-run on seeds 0-9 this commit: 6/10, red {0, 2, 5, 9} —
   an exact match confirming the full-26 number).
   WORSE — three seeds fail on this path that do not fail off it. This clause
   fails on its own, decisively.
3. The census gap explained per number — MET as explanation, **NOT MET** as
   the rule's own "no worse than pooled on both numbers" clause: ALIVE 100.2
   vs pooled 127.0 and VACANCY 41% vs pooled 27% are both worse, not one.
4. Both canaries 39/39 on the ON arm — MET. `canary --store-level` 39/39
   (268s); `laborcanary --seeds 32 --store-level` 39/39 (329s), this commit.
5. Ledger conservation and reconciliation exact on both arms, pooled arm
   added — MET (`verify --seed 1/9/13`, this commit: three arms — posted,
   auction, store-level — all inside the 1e-9 bound, unmoved).
6. The mechanical pins — MET: `--pooled`, `vanillaMode`
   (`StoreLevelSpending=false`), flags-off smoke (same), the occupancy pin
   (`OccupancyChannel` explicitly `StoreLevelSpending=false`), and a
   report-only `storelevel` fingerprint arm alongside the pre-existing
   `pooled` one (stanza 12; `fingerprint --check`: all lanes match, no
   accept needed).

Items 1, 4, 5, 6 are met; item 2 and the census-vs-pooled half of item 3 are
not, and either alone is decisive per the stated rule. **`StoreLevelSpending`
stays FALSE.** The unblock list is rewritten to exactly what remains:
1. Close or reverse the `webersweep` ON-arm regression — specifically seeds
   2, 9 and 21, which pass off the flag and fail on it (0 and 5 were already
   red off the flag and are not this item's to explain).
2. Close the census against the POOLED arm, not merely narrow it against its
   own prior number. The named lever is the absorbing vacant-commercial-parcel
   state above: no S is paid on a vacant parcel, so `ConditionDecay` walks it
   to the floor and the vacancy drain empties the escrow `Leveling` would
   refill it from — plausibly the same land-use-mix change implicated in item
   1's Weber regression, since a city with more permanently-vacant commercial
   land feeds a different signal to the industrial/extractor siting decisions
   Weber checks. Unowned by this item.

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


**THE 61st TO 63rd ARE TASK #49's DISPLACED-FIRM LEGS**, added at the
displaced-firm commit: a floor (the displacement event is real and both
outcomes occur), a settlement leg (no live firm holds no site, and every
displacement exit settled its books), and a link leg (a firm and its site never
disagree about holding each other). One fixture, one injected displacement
event, through `EconomyEngine.DisplaceFirm` — the entry point `EconReader` and
task #48's site market both use, not a private test path.

WHY THREE AND NOT ONE — AND A CORRECTION. The first version of this paragraph
said `--mutant-half-unlink` "leaves ZERO siteless firms and passes the settlement
leg clean, and is caught only by the link leg". That was INFERRED from
`displaceprobe`'s siteless count and never run against the check. Run
(`verify --seed 0` under each mutant) it is wrong in both directions:

|                       | floor | settlement | link |
|---|---|---|---|
| MUT-49a no-exit       | FAIL  | FAIL       | **PASS** |
| MUT-49b half-unlink   | FAIL  | FAIL       | FAIL |

MUT-49a reds settlement at 29 siteless firms holding 80,489 against a bound of 0,
and the LINK leg passes it — a firm cleanly unlinked and then abandoned is still
consistent with its parcel; nobody is lying. MUT-49b reds the link leg at 28
breaks AND the settlement leg, on the conjunct the first version did not think
through: the pass reports seeing **0 of 29** displacements, because a firm that
kept its parcel never becomes siteless and is never counted. That is the
`seen == injected` cross-record identity working exactly as intended. So the LINK
leg is the discriminator — clean under one defect, red under the other — and the
check is stronger than the paragraph that described it. Writing a mutant-to-leg
mapping without running it is the same class of error as the seed-0 count above,
from the same commit.

WHAT THE FLOOR IS FOR, MEASURED. `displaceprobe`, seeds {0, 1, 5, 9, 13}: 29/18/
13/22/14 firms displaced at t=150, re-sited 26/12/11/12/4, exits 3/6/2/10/10,
siteless at t=300 **0 on every seed**. (The seed-0 count read 13 here when this
paragraph was first committed at 3861683 — a transcription error, caught by a
reader checking it against the engine's own `Displaced == Resited + Exits`
identity, which 26 + 3 satisfies at 29 and not at 13. The other four agreed.) Both branches are exercised on every seed,
which is what the floor's `>= 1` of each asserts and why "displace nobody" or
"exit everything" cannot pass it.

**THE FINDING THIS ITEM EXISTS FOR: A MISSING FIRM EXIT IS INVISIBLE TO
CONSERVATION, AND THAT IS STRUCTURAL, NOT AN OVERSIGHT.** Under
`--mutant-displaced-no-exit` the same runs read 13–29 stranded live firms holding
27,325–80,489 of frozen money while ledger drift reads **1.7E-7 to 4.3E-6** — the
same order as the clean arm — and the per-sector reconciliation is untouched.
`Ledger.Transfer` debits and credits in one statement, so a firm that never exits
posts no transfer and drift cannot move; and `SectorAudit.Sample`
(`TestRunner.cs:5158-5175`) sums every firm with no `Dead`/`Parcel` filter, so the
stranded firm's money is on BOTH sides of the reconciliation at once. Conservation
is an invariant about arithmetic; this is an invariant about agency, and it needed
its own check. Do not "strengthen conservation" to cover this — it cannot be done
from that side.

TWO BLIND LEGS FOUND BY THE SAME AUDIT AND FIXED IN THIS COMMIT. (1) The nonres
parity ARREARS leg skipped live `Parcel < 0` firms (`if (f.Parcel < 0) continue;`)
on the live arm while its DEAD arm read the same field as evidence of release —
one branch treating the fact as data, the other as absence. `stuck` feeds an UPPER
bound (`aOn.stuck == 0`), so the skip moved the verdict toward GREEN: a firm
stranded past the arrears clock satisfied "no firm sits past the clock" by not
being counted. Now counted as a fifth state and asserted at 0 in both legs.
(2) The ENTRANT-SURVIVAL leg gated both its birth and its death registration on
`Parcel >= 0`, so a cohort member that lost its site kept `deathAge == -1` and was
scored a SURVIVOR against an upper bound — the exact selection its own comment
warns about, reintroduced through a different door, and it would have got WORSE
once displacement existed, because a `DiedOfDisplacement` exit also has
`Parcel < 0`. The site test now gates the birth branch only, on both the clean and
the mutant arm. Neither change moves a number on the shipping arm today, because
nothing in the pure simulation strands a firm (`Leveling.cs:76` refuses to
redevelop an occupied parcel; `Construction.cs:396` only touches parcels under
construction, which no firm occupies) — they are tripwires against a state
`EconReader` can already reach and `#48` will produce by design.

AT THE DISPLACED-FIRM COMMIT the gate is: verify **62/63** on seed 0, **63/63**
on seed 1, **62/63** on seed 5, **63/63** on seed 9, **63/63** on seed 13 — and
both failures are the standing reds in the table below, named in the run output
rather than assumed (seed 0 Weber; seed 5 `nonres parity: office product`). All
three new legs are green on all five, and the two amended legs (the arrears floor
and its partner, now carrying the `siteless == 0` conjunct) are green on all five.
`fingerprint --check` reads **all 13 lanes matching with no accept**, which is the
claim that matters: `ResolveDisplacedFirms` is a no-op on a world with no siteless
firms, and the default world has none, so the model is byte-identical.
`canary` 39/39, `laborcanary` 39/39.

THE ENTRANT-SURVIVAL CONSTANTS WERE RE-MEASURED, NOT ASSUMED. Moving the site
test off that leg's DEATH branch changes its cohort composition (seed 0's clean
arm reads 0 of 1 where the pre-change tree read 0 of 0), and `EntrantYoungDeathMax`
and `EntrantMutantFloor` are measured constants — a measured constant whose
population has moved has to be re-measured. `shopsweep` **4/4** at this commit,
clean arm 1 of 1 against the bound of 8 and the mutant arm 33 against its floor of
5. Note for whoever touches this leg next: the clean cohort is tiny by design (the
leg's own comment says the population is nearly empty because the mechanism works),
so the mutant arm is carrying the falsifiability, not the clean bound.

**THE MOD ARM'S HALF, merged from the site-link track.** The core pass can only
resolve a firm the world admits is siteless, so the reader had to stop lying about
which firms hold which sites. Five desync paths, all in `EconReader.cs`: `AddFirm`
and the `SyncFirms` re-link both claimed a parcel UNCONDITIONALLY (so a second
company could take a held site, leaving the incumbent pointing at a parcel naming
the newcomer); the company-despawn branch cleared the parcel end and left the dead
firm naming a parcel that may since have been re-let; the entity-despawn branch in
`SyncParcels` evicted households but never cleared `f.Parcel`. THE FIFTH IS THE ONE
THAT MATTERED AND WAS NOT IN THE BRIEF: `ParcelIndex` was never pruned of despawned
entities, and `SyncTick` runs `SyncParcels` → `SyncHouseholds` → `SyncFirms`
(`EconReader.cs:159-162`), both of the latter re-linking renters through that index
— so clearing a firm's parcel on demolition was UNDONE ONE PASS LATER, ON THE SAME
TICK. Without that, the fourth fix was inert. All five now route through a new
`EconSiteLink` (leaving clears both ends; taking is REFUSED when a live firm holds
the site, and the loser ends unsited — the state the core pass can see — rather
than half-linked; a claim held by a dead or absent firm is stale and is taken over).

`modsync`, a new command, is the coverage: a real synthetic city run 120 ticks so
the audited population is the ENGINE's, then the reader's claim and demolition
paths replayed over it. **6/6**, with `--mutant-site-steal` and
`--mutant-demolition-keeps-site` each reddening exactly ONE leg (0/59 refused and
0/59 unsited respectively, audit +59 in both) and no floor moving. Floor bound 20
is measured: sited firms across seeds {0, 1, 9, 13, 25, 138, 549, 910, 20260806}
read 55-67, so the bound sits under the observed minimum of 55.

TWO THINGS ABOUT THAT TRACK THAT CORRECT THIS REPO'S OWN FOLKLORE. (1) **The mod
project has no shims and `dotnet build` type-checks none of it.**
`CS2Econ.Mod.csproj:22` defines `OUT_OF_GAME_BUILD` and every game-facing class is
`#if OUT_OF_GAME_BUILD` → a stub that THROWS / `#else` → the real body, so the
default build compiles the stub halves only. The in-game halves were type-checked
here through a scratch project compiling them against hand-written stubs of the
Colossal/Unity shapes, whose own ability to fail was demonstrated by swapping two
arguments until it errored. (2) **`ParcelIndex.Remove` has no automated check** —
it lives inside the `#if` and its consequence is only observable in-game. Recorded
as a known gap, not as coverage.

THE FIX DOES REACH THE MOD ARM, VERIFIED RATHER THAN ASSUMED: `EconBridgeSystem.cs:217`
calls `_engine.Step()`, and `Step()` (`EconomyEngine.cs:114`) is the tick method
containing `FirmLifecycle()`. So `ResolveDisplacedFirms` runs on the arm the defect
was actually found on, which was the open question when the two halves were split.

AT THE MERGED TREE: verify **63/63** on seed 1, `fingerprint --check` all 13 lanes
matching with no accept, `modsync` 6/6.

## verify (64 checks) — the displaced-firm follow-ups

**THE MERGED PAIR HAD A REGRESSION ON THE MOD ARM, FOUND BY ADVERSARIAL REVIEW OF
THE COMMIT ABOVE AND NOT BY ANY CHECK.** `ResolveDisplacedFirms` treated every
live `Parcel < 0` firm as displaced. In the pure simulation that is sound —
`DisplaceFirm` is the only producer of the state. On the mod arm it is not:
`EconReader.AddFirm`'s own comment names three other causes (a prefab without
`SpawnableBuildingData`, a building spawned since the last sync, a claim
`EconSiteLink.Attach` refused because another firm holds the site), and in every
one of them the company is STILL STANDING IN ITS BUILDING and only the adapter
lost track. The pass relocated it or killed it — and the kill is PERMANENT, since
`SyncFirms` skips a dead firm forever and `FirmIndex.ContainsKey` blocks
re-creation. That is an agent moved against its own default on the strength of the
engine's own ignorance, which the defaults rule forbids: a firm whose building the
reader could not resolve has "stay put" as its default.

THE FIX IS A DISTINCTION, NOT A GRACE PERIOD. `Firm.SiteLostTick` records the tick
a firm lost a site IT HELD; the pass resolves only those, and counts the rest in
`FirmUnplacedSeenTotal` so the state it declines to act on is never silent. Of the
four ways the reader can leave a firm unsited, exactly ONE is an economic event —
`EconSiteLink.ReleaseSite`, the demolished building — and it is the only one that
sets the mark.

MEASURED, `modsync` (which now runs one engine tick between the two populations it
creates): **8/8**, 59 unplaceable firms all alive and all exactly where the engine
found them, 59 site-losers all resolved. Under `--mutant-resolve-unplaced`, which
drops the distinction and restores the shipped behaviour: **58 of 59 unplaceable
firms are killed** (1 survives, re-sited) — the regression measured rather than
argued.

TWO MORE FROM THE SAME REVIEW. (1) The #49 commit message claimed every firm loop
opens `if (f.Dead || f.Parcel < 0) continue;`. FALSE at `SettleResourceFlat`
(`EconomyEngine.cs:1679/1687`, the `TierD_Trade = false` path), whose two loops
guarded on `f.Dead` alone while its sibling `SettleResourceClustered` (`:1643`)
guards on both. The seller half is inert (`OutputThisTick` is zeroed at `:1736` in
the last sited tick and `ProductionAndTrade` never re-sets it for a siteless firm),
but the buyer half was live: `InputNeedByRes` is cleared only inside
`ProductionAndTrade`'s own Parcel-guarded loop, so a siteless firm kept paying for
inputs to a factory it no longer had, and since `_demandTotal` accumulates over
sited firms only the debits exceed `buyerCost` — money destroyed off-ledger against
`Account.Firms`. Fixed in both places AND at the source (`DisplaceFirm` now clears
the per-tick flows). Reachable only with `TierD_Trade` off plus a displacement, a
combination no harness arm runs; recorded as a coverage gap rather than claimed as
covered. (2) `displaceprobe`'s option parser strode by two over raw argv, so any
`--mutant-*` flag before `--ticks`/`--rate`/`--at` silently discarded them. No
measurement in this file was affected (every mutant run used defaults), but a
future one would have been, silently.

GATE AT THE FOLLOW-UP COMMIT, COMPLETE: verify **64/64** on seed 1 and **63/64**
on seed 0 (the standing Weber red, unchanged), `fingerprint --check` all 13 lanes
matching with no accept, `modsync` **8/8**. The two #49 mutants re-run against the
64-check suite reproduce the corrected table exactly — MUT-49a floor FAIL /
settlement FAIL / link **PASS**, MUT-49b floor FAIL / settlement FAIL / link FAIL —
so the link leg is confirmed as the discriminator on a second measurement.

WHERE THE NEW GUARD LEG'S FALSIFIER LIVES. `displaced firm guard` passes under
BOTH #49 mutants and under `--mutant-resolve-unplaced`, because nothing in the pure
simulation produces an unplaced firm: it reads 0 == 0 by construction and is a
tripwire, not a check. The property it names is asserted where the population is
real — `ModSiteLink` leg 3, which steps the engine over 59 unplaceable and 59
site-losing firms and is red by `--mutant-resolve-unplaced` at 58 kills. A reader
looking for the evidence should read that leg and not this one; the code comment
says so at the site.

## THE FIX, MEASURED: the labor auction is what stops the churn

Item (1) of the fix ordering below, tested. `marginprobe --labor`, seeds 1/9/13,
400 ticks, against the shipped default:

| | seed 1 | seed 9 | seed 13 |
|---|---|---|---|
| extractor deaths | 183 → **0** | 131 → **1** | 374 → **1** |
| extractor cash flow p50 | −15.09 → **−0.86** | −16.59 → **−1.22** | −1.40 → **−0.33** |
| extractor alive | 5 → **39** | 2 → **59** | 13 → **65** |
| industrial deaths | 58 → **5** | 13 → **0** | 52 → **6** |
| industrial alive | 1 → **25** | 2 → **16** | 4 → **27** |
| VACANCY | 50.8 → **13.5 %** | 50.0 → **9.5 %** | 53.8 → **12.1 %** |

Commercial on seed 1: 64 alive at +59.54 with 9 deaths → **85 alive at +207.02
with 0**. Office: −426.69 → **−258.66**, still 100 % under water with zero
deaths — which is the predicted result, since office's deficit is the ℓ5-corner
BILL and no labor mechanism reaches it.

**THE MECHANISM, and it is the diagnosis's item (2) confirmed exactly.** On the
default path `AssignWorkplaces` fills a firm's slots by commute softmax and the
firm pays class wages whatever its product — nothing in the assignment asks
whether the hire pays. The auction's door cap is that missing question: it
refuses a hire above the firm's own marginal-revenue forecast, so an extractor
whose product is 1.9/slot stops paying 10/slot for labor it cannot cover.
Extraction becomes a real sector sitting at BREAKEVEN (−0.33 to −1.22), which
is what free entry into competition should produce, rather than a mill that
killed 374 firms in 400 ticks.

**WHAT IT DOES NOT FIX, stated because the numbers say so.** Industrial stops
DYING everywhere but stays 81–94 % under water, and on seed 9 its cash flow gets
WORSE (−34.13 → −64.44) even as deaths go 13 → 0. That is a shift from CHURN
mode to ZOMBIE mode: the capped wage bill bleeds the firm slowly enough that it
never reaches `CompanyBankruptcyLimit`. Which is precisely the population task
#55's exit margin exists to resolve, and is why the two land together rather
than separately.

Aligning the condition floors on top of the labor auction (`--cond-bid-floor
0.2`) is SECOND-ORDER once the auction is in: extractor −0.86 → −0.32,
industrial −2.01 → −2.27, vacancy unchanged. The churn that arm was fixing was
mostly labor-driven, so the ordering in the diagnosis was right about (1) coming
first and wrong to expect much separately from (2).

### CORRECTION: the VACANCY row above measures the wrong thing, and it flatters

The table's vacancy row counts a parcel as occupied whenever a firm names it.
Following it up exposed that this is not the same as the parcel being in use,
and the gap is not small. Extending `marginprobe` with a staffing census
(`sites: ... EFFECTIVELY IDLE`, this round):

| `--labor`, 400 ticks | seed 1 | seed 9 | seed 13 |
|---|---|---|---|
| parcels with no firm ("VACANT" above) | 13.6 % | 9.5 % | 12.1 % |
| further parcels holding a ZERO-STAFF firm | 48 | 50 | 55 |
| **effectively idle** | **37.9 %** | **33.3 %** | **35.1 %** |

Seed 1, per sector: of 39 live extractors, **33 employ nobody** — 0 workers
filled across 198 slots — and of 25 live industrials, 15 do. The 6 staffed
extractors run 35/36 slots at **+50.91**. Nothing sits in between. So the "−0.86
median extractor" the table above reports as *breakeven* is not a firm trading
at the margin; it is an EMPTY BUILDING paying the land charge, and its cash-flow
dispersion (`CashFlowMadEma` p50 = **0.12**) says so — a flat line, no trading
at all.

The labor auction's real effect is therefore narrower than claimed above: it
stopped extractors from **dying**, but it did so by making non-operation cheap
rather than by making extraction work. Item (1) of the ordering stands as the
right first move; the claim that it makes extraction "a real sector sitting at
breakeven" does not.

### The exit margin is NOT destructive; the vacancy number was hiding the damage

`--labor --exit-margin` reads as a catastrophe on the old measure (13.6 % → 42.9
% vacant, seed 1) and as something much smaller on the honest one:

| effectively idle | seed 1 | seed 9 |
|---|---|---|
| `--labor` | 37.9 % | 33.3 % |
| `--labor --exit-margin` | 45.4 % | 52.2 % |

The margin retires the shells (seed 1: extractor 39 → 8 alive, 52 margin exits;
survivors' p50 **+50.96**, only 1 of 8 under water). Most of the emptiness it
"creates" was already there. The genuine increment — +7.5 points on seed 1,
+18.9 on seed 9 — is firms with real deficits exiting and **nothing re-entering**,
which is task #55's own "with re-entry" clause and #56, not a defect of the
margin itself.

### A dispersion inaction band: implemented, measured, NO-OP

Dixit's trigger is not "expected profit < 0" but "negative by more than the
option value of waiting", and with nothing to liquidate that option value is the
VARIANCE of the forecast. The clock asymmetry documented as the band cannot be
one: it reads an EMA at 0.05, which does not flip sign tick to tick, so the
decrement branch is unreachable for exactly the marginal firm. Added
`Firm.CashFlowMadEma` and made the trigger `CashFlowEma < −MadEma`
(`--mutant-no-band` restores the old behaviour).

Measured: **no effect.** Seed 1 margin exits 95 → 96, seed 9 105 → 112; idle
42.6 → 42.9 % and 53.0 → 51.7 %. The reason is the finding above — the firms it
was meant to protect have MAD ≈ 0.12, because they are not trading. The band is
correct and kept (it is the right rule for a firm that *is* trading, and the
mutant proves it is reachable), but it is **not** the fix for this population,
and it should not be described as one.

## THE ROOT CAUSE: developers build where nobody will work, and the fill floor is why

Chasing the zero-staff shells to their source. Three measurements, each ruling
out the previous hypothesis:

1. **Not the door cap.** `LaborAuction` skips a firm whose forecast marginal
   revenue is ≤ 0 ("a door with nothing to pay is not a door"), so the obvious
   explanation is a zeroed cap — no geology, no local price, negative recipe
   margin. Measured across seeds 1/9/13: **0, 0, 0.** Every idle firm posts a
   POSITIVE cap. Their doors reach the market.

2. **Nobody lists them.** A worker shortlists a door only where
   `Cap − commute` beats that worker's OWN outside option. Counting doors that
   appear on no shortlist at all (`LaborAuction.DoorsUnlisted`): **67/303
   (22.1 %) seed 1, 50/321 (15.6 %) seed 9, 57/355 (16.1 %) seed 13.** These are
   real jobs, at real wages, that no worker in the city will take once their own
   commute is netted out. That is a correct verdict at the worker's margin — a
   remote deposit is remote — and it is invisible in every other number the
   auction reports.

3. **The developer had the signal and was forbidden from using it.**
   `FirmBidPerSlot` does forecast staffing, via `FirmFillEstimate`, which ends
   `Clamp(0.35 + 0.65 * fill, 0.35, 1.0)`. Measured at the clusters holding idle
   firms: `FirmFillEstimate` p10 = p50 = **0.350** — pinned exactly on the floor
   — while realized fill there is **0.000**. At staffed firms it reads **1.000**.
   The signal separates perfectly. The floor is the only thing between the
   developer and the truth, and it lets a rich remote deposit clear its hurdle on
   a third of a roster it will never get.

**Why the floor exists, and why it cannot simply be deleted.** `JobFillRate` is
`colMatched / dem` when `dem > 1e-9` and **0 otherwise** — so a cluster where
nothing has ever been posted is indistinguishable from one where 200 slots went
begging. At t = 0 nothing is staffed anywhere; a hard-zero floor would mean
nothing is ever built. The floor is a prior standing in for missing evidence,
and it is applied even where the evidence is overwhelming. That conflation is
the defect, not the floor's existence.

## THE FIX: let the prior yield to evidence (`EconParams.FillEvidenceWeighting`)

Ships OFF; `--fill-evidence`; mutant `--mutant-fill-prior`. With it off,
`FirmFillEstimate` is the old `Clamp(0.35 + 0.65 * fill, 0.35, 1.0)` bit for bit,
and `fingerprint --check` at this commit reports **all lanes match** — thirteen
lanes, gating and report alike.

    weight = posted / (posted + FillEvidenceSlots)      // FillEvidenceSlots = 20
    est    = weight * realized fill + (1 − weight) * 0.35

An untried cluster still reads 0.35, a long record of full doors still reads
1.0, and a cluster that has posted hundreds of slots and filled none now reads
~0 — which it could not before. The prior is kept for the bootstrap it exists to
serve and bought out everywhere the evidence is real.

**Composed arm (`--labor --exit-margin --fill-evidence`, 400 ticks) against
`--labor` alone:**

| | seed 1 | seed 9 | seed 13 |
|---|---|---|---|
| doors on NO shortlist | 22.1 → **6.0 %** | 15.6 → **2.5 %** | 16.1 → **1.7 %** |
| zero-staff shells | 48 → **5** | 50 → **7** | 55 → **4** |
| industrial p50 | −2.01 → **+31.44** | −64.44 → **+118.72** | — → **+112.96** |
| industrial under water | 80 → **12.5 %** | 93.8 → **9.1 %** | — → **0 %** |
| extractor p50 | −0.86 → **+56.32** | −1.22 → −0.35 | −0.33 → **+6.31** |
| office p50 | −258.66 → **−42.57** | −262.41 → −121.39 | — → −95.78 |
| commercial p50 | +207.02 → **+237.39** | +200.65 → **+242.76** | — → **+226.28** |

The unlisted-door collapse is the mechanism confirmed end to end: jobs nobody
would take are no longer being CREATED. Industrial, extractor and commercial are
profitable at the median on every seed, which is most of task #57's stated goal.

### THE MUTANT RAN, AND IT SAYS THE FIX IS WRONG FOR OFFICE

`--mutant-fill-prior` (evidence never accumulates, so the prior stands whatever
the record) on the composed arm, seed 1. It is a valid falsifier — doors on no
shortlist 6.0 → 7.5 %, shells 5 → 8, built parcels 184 → 180 — so the mechanism
is load-bearing and reachable. But one column moves the WRONG WAY and it is not
a rounding artifact:

| seed 1, composed arm | as shipped | `--mutant-fill-prior` |
|---|---|---|
| office alive | 1 | **10** |
| office cash flow p50 | **−42.57** | **+319.03** |
| office under water | 100 % | **0 %** |
| office margin deaths | 10 | **0** |

Across all four arms office reads 22 @ −258.66, 10 @ −116.36, 1 @ −42.57,
10 @ **+319.03** — not monotone in count, so "fewer offices is better" is not
the story. Isolating the two flags: with the exit margin ON and the prior
restored, ten offices are strongly profitable; with evidence weighting ON, entry
is choked to one and that one is under water. **The exit margin alone does the
work for office, and the evidence weighting over-restricts it.**

### The arm's third verify failure was a SOLVER BUDGET, and it is resolved

`verify --seed 1 --labor --fill-evidence` first read 61/64 against 62/64 for
`--labor` alone. The extra failure was the housing-auction CE check, and every
STRUCTURAL leg in it was clean — capacity 0, reserve 0, IR 0, posted>admitted 0,
unsold-above-reserve 0, stranded 0, improving swaps 0, re-solve drift 0. What
failed was `converged False (repair 20 rounds, clean False)` with 3 envies worth
1.36 % of value, while the LABOR auction's CE check passed on the same run.

Re-run at `--repair-rounds 64` (the budget the labor auction already carries):
**PASS — converged True, repair 12 rounds, clean True, envious beyond 2ε 0**,
and the suite reads **62/64, identical to the `--labor` control** (Weber and
entrants-can-trade, both pre-existing). The fix introduces no new check failure.

It converged in FEWER rounds with a LARGER budget, which is the tell:
`HousingAuction` line 575 is `_stride = K + AuctionRepairRounds`, so the
parameter sizes each bidder's shortlist capacity as well as capping the loop. At
20 it was not a round cap that bound but the room a bidder has to hold
alternative doors, and a harder market simply needs more. `AuctionRepairRounds =
20` against `LaborAuctionRepairRounds = 64` is an unexamined asymmetry; raising
it is a shipping-default change and has not been made.

The obvious hypothesis is office's increasing returns — `OfficeAgglomMult` means
an office's revenue depends on how many other offices stand near it, so a rule
that thins entry attacks the very term that makes the sector viable, in a way it
cannot for the three constant-returns sectors. **That hypothesis is UNTESTED.**
It is the reason this flag must not be flipped on as it stands: it is measured
good for three sectors and measured bad for the fourth, and the mechanism of the
harm is not yet established. Testing it means holding office entry fixed while
varying the fill rule, which no current probe arm does.

### The isolating arm RAN, and both hypotheses above are REFUTED

`--fill-office-prior` (`EconParams.FillEvidenceOfficeExempt`): evidence
weighting for commercial/industrial/extractor, the old prior for office only —
the missing cell of the 2×2. Seeds 1/9, composed arm, with the agglomeration
multiplier now printed rather than presumed:

| office | full evidence | office-exempt | full `--mutant-fill-prior` |
|---|---|---|---|
| alive (s1 / s9) | 1 / 2 | 5 / 7 | 10 / 15 |
| cash p50 | −42.57 / −121.39 | **−262.56 / −324.86** | +319.03 / +319.80 |
| agglom mult p50 | 1.003 / 1.005 | 1.010 / 1.014 | 1.019 / 1.028 |

Two refutations in one table. **Increasing returns:** the multiplier rises with
count exactly as the hypothesis says — and tops out at a ~1.6–2.8 % output
term, against a cash-flow swing of ~580/tick. Wrong magnitude by two orders.
**Entry thinning:** restoring office entry makes the losses WORSE per firm, not
better. More offices under the same price regime just die harder.

The per-firm anatomy names the real channel. Staffing is IDENTICAL across arms
(13–14/14 — labor is not the difference) and gross revenue at full staff is the
same ~442 everywhere. What differs is the LAND BILL and the BUILDING: the
profitable mutant offices are the seeded t = 0 cohort in L1/L2 buildings billed
~123/tick; the dying evidence-arm offices are entrants in developer-built L5
towers billed **460–471/tick against a gross revenue ceiling of ~442** —
insolvent at zero wages, by construction, before a single hire.

### THE ROOT CAUSE: the assessor prices a production function the world does not run

`FirmBidPerSlot`'s office branch multiplies revenue by `quality` =
Quality(ℓ)/Quality(1) UNCONDITIONALLY (`LandAccounting.cs`, office case), and
the extractor branch does the same. But the ENGINE scales those two sectors'
realized output by Quality(ℓ) **only under `NonResLandParity`** — which ships
off — and the extractor production comment says in words why the gate exists:
"the land is charged for a level premium the production function does not
deliver." The production side got the parity gate; the forecast side never got
the matching condition. Industrial and commercial are consistent (their realized
output is quality-scaled unconditionally), which is why only office and
extractor bled.

**The fix** (`EconParams.AssessDeliverableQuality`, `--assess-deliverable`,
ships OFF; `fingerprint --check` flag-off: all thirteen lanes match): office and
extractor forecasts carry Quality(ℓ) only where parity makes production deliver
it. With parity ON it is a no-op by design. Measured, composed arm + fix, seeds
1/9:

| | before | after |
|---|---|---|
| office alive @ p50 (s1) | 1 @ −42.57, 100 % under water | **10 @ +372.28, 0 %** |
| office alive @ p50 (s9) | 2 @ −121.39, 100 % under water | **15 @ +370.29, 0 %** |
| office margin deaths | 10 / 20 | **0 / 0** |
| office land bills | 267–471/tick | 60–83/tick |
| extractor under water | 33.3 / 53.8 % | **12.5 / 0 %** |
| extractor margin deaths | 45 / 70 | 32 / 51 |
| vacancy | 39.1 / 43.5 % | 34.2 / 37.5 % |
| effectively idle | 41.8 / 47.1 % | 37.0 / 38.0 % |
| re-entry gate p10 | −4.87 / −5.59 | **−0.98 / −2.89** |

**This is the first arm on which all four sectors are simultaneously viable**:
commercial ~+240 with zero deaths, industrial +19/+116 p50, office +372/+370
with zero deaths, extractor +55/+35 with 0–1 firm under water. The office
objection to `FillEvidenceWeighting` is hereby withdrawn as misattributed: the
fill rule only decided WHICH offices existed; the quality wedge decided that
entrant offices could not live. And the re-entry gate's deep tail (p10 ≈ −5)
was this same wedge pricing vacant land at an L5 config nobody could run — the
median miss (−0.035…−0.053) remains and is #56/#48's residual.

Vacant-condition p90 collapsed to 0.05 on the fix arm: what still stands empty
is uniformly rotted stock, which is #56's redevelopment loop, not a pricing
error at entry.

### The residual re-entry miss is SPerUnit on a ruin, exactly — and entry is RIGHT to refuse

The median (bid − assess) sat at −0.035 or −0.053 on every arm measured, which
is too constant to be a market outcome. It is arithmetic:

    SPerUnit(ℓ=1, cond=0.05) = (h + δ + m) · cond · RC1PerUnit
                             = (0.0004 + 0.00035 + 0.00025) · 0.05 · 700 = 0.035
    SPerUnit(ℓ=2, cond=0.05) = 0.035 · LevelCostGamma(1.5)              = 0.0525

Both observed medians, to the digit. A derelict building produces ~nothing (its
bid → 0 as condition → floor) and still owes the structure charge, and
`UnitAssessment = s + land + structureTax ≥ s > 0` always. So the gate cannot
open — and it SHOULD NOT. Nobody should occupy a ruin. **The entry gate is not
the defect; the missing mechanism is demolition.**

### #56, corrected: the registry's account of it is wrong

This file previously said the loop is that `Assess` prices a ruin HIGH because
redevelopment pays, and `ExpectedFlow` then deducts that land charge so the
hurdle is never cleared. Measured, that is not what happens.

There IS a demolition path — `Leveling.cs` scrape-and-rebuild, Built →
UnderConstruction (not → Empty, which is why a grep for `ParcelState.Empty`
misses it). It needs four things at once. On the composed+uniform arm, seed 1,
over the 65 derelict vacant parcels:

| gate | count | verdict |
|---|---|---|
| `TargetIsScrape` | **0 / 65** | assessment NEVER names a scrape |
| sustained wedge pressure | 19 / 65 | partially met |
| `Escrow ≥ cost` | **0 / 65** | mean escrow **0.0** vs mean cost **12,418.9** |
| physically vacant | 65 / 65 | fully met |

Two gates are hard-shut and either is fatal alone. They share one cause, and it
is the opposite of "priced high": a derelict parcel's assessed land value is
~ZERO, because the entrant bid that would price it is ~zero. Zero land value
means zero wedge, which means escrow never accumulates (0.0 against a 12,419
bill — a gap, not a margin), and it means the scrape candidate's residual never
beats the standing configuration, so `TargetIsScrape` is never set and
`Leveling`'s scrape block is never entered for these parcels at all.

So the self-defeating loop is real but runs the other way: **a parcel must fund
its own redevelopment out of a land value it cannot have while it is derelict.**
Any fix has to break that circularity — the redevelopment claim on a ruin cannot
be financed by the ruin's own wedge.

## Do the four sectors run one system? No — and unifying them did NOT produce a fair fight

Audited across the THREE sites that price a slot — the land bid
(`FirmBidPerSlot`), the labor auction's door cap (per-sector MRP), and realized
production (`EconomyEngine`) — the four sectors do not run one system or two:

| sector | bid | door cap | production |
|---|---|---|---|
| Commercial | quality ✓ cond ✓ | own tech ceiling | mass = units × cond × Quality(ℓ) ✓ |
| **Industrial** | quality ✓ | cond ✓ Quality(ℓ) ✓ | cond ✓ Quality(ℓ) ✓ |
| Office | quality ✓ | **neither term** | cond × Quality(ℓ), gated on parity |
| Extractor | quality ✓ | cond only | cond ✓, Quality(ℓ) gated on parity |

**Industrial is the only sector coherent end to end.** Office gives three
different answers at its three sites; on the shipping default an office's output
ignores its own building's CONDITION entirely (a derelict tower produces exactly
what a new one does) and its door cap prices every tower identically.

`EconParams.UniformSiteProductivity` (`--uniform-productivity`, ships OFF) puts
cond × Quality(ℓ)/Quality(1) at every site for every sector. It fixes those
inconsistencies and it keeps the level premium REAL — measured on the composed
arm, surviving offices become L5 towers earning ~900/tick rather than L1/L2
boxes earning ~442 (seed 1: 5 alive @ +615.56, 0 % under water; seed 9: 8 @
+575.33). That is the opposite direction from `AssessDeliverableQuality`, which
made the books balance by zeroing the premium instead of by delivering it.

### THE FAIR-FIGHT TEST, and it fails on BOTH wirings

`zonefight` prices all four sectors at the SAME parcel — same cluster, level and
condition, each given the entrant read its own entry path uses — and reports who
would win. Seed 1, 400 ticks:

| wiring | L1 | L2 | L5 | totals | sectors winning NOTHING |
|---|---|---|---|---|---|
| default | ind 62 / off 36 | ind 51 / off 56 | ind 10 / off 13 | com 0, ind 125, off 105, ext 0 | **2 of 4** |
| uniform | off 99 | off 107 | off 17 | com 0, ind 0, **off 224**, ext 0 | **3 of 4** |

**Unifying the productivity terms made the fight LESS fair, not more.** The
answer to "can all four use one system so they'd fight fairly" is: one system is
necessary and is now built, but it is NOT sufficient, and this measurement is
why the claim must not be made from the code alone.

The blocker is not the level term. It is that the sectors' per-slot bids live on
different SCALES — at L5, mean bid ext 1.42, com 13.27, ind 16.02, off 24.99 —
and that **office is very nearly location-invariant**: `OfficeAgglomMult` spans
1.003–1.028 across live sites, a ~2 % range, against Weber input-sourcing spreads
for industrial and geology spreads for extractor that are order-of-magnitude.
A near-flat high bid wins everywhere. For a mod whose premise is that decisions
are driven by LOCAL effects, office is the sector with almost no local variation,
and that is the finding to carry forward.

**This hypothetical is not currently exercised anywhere.** `Assess` maxes over
LEVELS within `parcel.Zoned` (`zonedUse = parcel.Zoned`; the loop is `for lvl`)
— there is no cross-sector maximum in the model, so today only one sector ever
bids for a given plot. Making the fight real means (a) `Assess` ranging over
permitted USES, and (b) the bid scales above being commensurable first. (a) is
cheap and (b) is not; doing (a) without (b) would hand every plot to office.

### A firm's cash payroll can clear to ZERO, and office's does

Reconstructing a wage bill as `FilledByClass × Wage(class)` reports the POSTED
class wage, which under the labor auction is not what the firm pays. `Firm.
PayrollLastTick` now records the DEBITED payroll, and for office it reads
**0.00 against a posted 324** — members are paid entirely through the
worker-collective dividend, which is deliberately excluded from the cash-flow
measure as a distribution of surplus rather than an avoidable cost.

Consequences worth holding: office cash flow is revenue minus RENT only (906.28
− 255.35 = 650.93 ≈ the 648.11 measured), the exit margin for such a firm never
sees labor at all, and "office at +615" means +615 of surplus distributed to
workers, not retained profit. An earlier office table in this file printed the
posted wage in a column labelled `wages`; the 460–471-bill-vs-442-gross finding
it supported does not involve wages and stands unchanged.

**Named reds of the `--assess-deliverable` arm** (`verify --seed 1 --labor
--fill-evidence --assess-deliverable --repair-rounds 64` reads 60/64: Weber and
entrants-can-trade pre-existing, plus these two — the SHIPPING default is
untouched, fingerprint all lanes match and plain `verify --seed 1` reads 64/64):

1. `nonres parity: assessment never rests on a one-seller cell's own record` —
   7/33 re-priced against a ≥ 1/3 bound. The MECHANISM fires (largest move
   126.078 → 183.657); what moved is the population. With extractor forecasts
   level-free, the argmax configuration at more one-seller parcels is another
   cell's, which the check's own docstring names as legitimate non-movers. The
   1/3 bar encodes the old world's configuration mix.
2. `nonres parity floor: staffed offices carry an assessed level term, and the
   default does not produce it` — **REWRITTEN, and now green on every arm.**

### The office FLOOR leg, rewritten two-sided (the #55 blocker)

The old leg hard-coded the old world: it asserted the default arm's realized
office product misses `cond × Quality(ℓ)` by > 25 %. Three flags now move that
question and they do not move together, so the leg was measuring one cell of a
table it did not know existed. It now reads the two rules SEPARATELY —

- production carries `cond × Quality(ℓ)` ⟸ `NonResLandParity` **or** `UniformSiteProductivity`
- the bid prices `Quality(ℓ)` ⟸ **not** `AssessDeliverableQuality`, or either of the above

— compares realized product against **the term the bid actually priced**, and
asserts a LARGE mismatch where the two rules disagree and a SMALL one where they
agree. The scope filter reads the building's LEVEL, which no flag can flatten;
scoping on a flagged term would let a fix empty the population and the leg would
pass by being vacuous, which is the one failure mode this file exists to refuse.

Measured, `verify --seed 1 --only "nonres parity"`, all three arms PASS and each
names its own regime:

| arm | production | bid | regime | median | bound |
|---|---|---|---|---|---|
| default | drops | prices | **MISMATCHED** | 51.5 % | > 25 % |
| `--assess-deliverable` | drops | drops | ALIGNED | 0.0 % | < 25 % |
| `--uniform-productivity` | carries | prices | ALIGNED | 4.4 % | < 25 % |

The 4.4 % residual on the uniform arm is the cond asymmetry, and it is real: the
bid applies condition through `CondFactor` OUTSIDE `FirmBidPerSlot` while
production applies raw `cond`. The two agree on the level term exactly and on
condition only approximately. Not worth a flag of its own yet; recorded so the
next reader does not mistake 4.4 % for noise.

### Leg 1 (one-seller cells) — STILL OPEN, and one strengthening attempt failed

`assessment never rests on a one-seller cell's own record` passes on all three
narrow arms (1/2, 1/1, 3/4 against a ≥ 1/3 bar) and reds on the FULL composed
arm at **7/33**. The mechanism works there — largest move 126.078 → 183.657 —
so the red is the BAR, not the behaviour: "a third of one-seller parcels must
re-price" makes the verdict depend on the configuration MIX, and the check's own
docstring already names two reasons a parcel is legitimately priced off some
other cell.

**The right fix and why the obvious implementation of it is wrong.** Keep only
the parcels whose parity assessment actually READS this (output, cluster) cell,
then require ALL of them to move — no slack bar, no mix dependence. Selecting
that subset by the argmax output of `FirmBidPerSlot` at the parcel's CURRENT
level does NOT identify it: `Assess` maxes over EVERY level, so the winning
configuration can sit at another level with another argmax output. Measured on
the default arm, that filter kept the one parcel that did not move and excluded
the one that did — **0/1 filtered against 1/2 unfiltered**, i.e. it selected
exactly backwards. A second exclusion tried at the same time (drop parcels where
exit parity already beats the origin read) is wrong on its own terms: admitting a
second seller RAISES `OriginComparable`, which can overtake `BestExportNet`, so
the pre-admission comparison does not predict the post-admission one — it emptied
the population to 0/0 and the non-degeneracy guard caught it.

Reverted rather than shipped. The subset has to come from the assessment's own
winning configuration, which `Assess` does not currently expose; exposing it
(alongside `TargetLevel`/`TargetUse`, which it already writes) is the actual
prerequisite. Recorded here so the next attempt does not re-derive the same two
dead ends.

**How close the bar actually is, per arm** (shipping bar, ≥ 1/3):

| composed arm | one-seller leg | office FLOOR leg | Weber |
|---|---|---|---|
| `--assess-deliverable` | 7/33 = **21 %** | ALIGNED 0.0 % | 50 % on cheapest-sourced |
| `--uniform-productivity` | 9/31 = **29 %** | ALIGNED **0.1 %** | **38 %** on cheapest-sourced |

Both are 61/64 with the same three fails, but the margins differ and they cut
both ways. Uniform is nearly at the one-seller bar (one more parcel would clear
it) and is the cleaner of the two on the office identity — 0.1 % over 12 offices
in level-scope, and 0.000 % over the 5 on the parity arm. Against that, **Weber
is measurably worse under uniform** (38 % vs 50 % of single-input industrials on
the cheapest-sourced recipe).

That last one is not explained by the level term directly: `quality` multiplies
every recipe's `(outNet − inputCost)` identically, so it cannot reorder the
argmax. It is an INDIRECT effect — uniform changes office and extractor output,
which changes the goods prices industrial firms source at, which moves which
recipe is cheapest where. Worth understanding before the flip, because Weber is
the mechanism the whole industrial location story rests on, and "our fix made
industrial location less Weber-like" is the kind of thing that should be
explained rather than absorbed.

**A seeding artifact, found on the way, that bounds what this fix could do.**
`SyntheticCity.SeedFirms` places a firm on 80 % of pre-built non-residential
parcels at t = 0 **consulting no bid at all**. That is why `--fill-evidence`
alone left extractor counts identical (39/59 on seeds 1/9, both arms) while
moving every developer-built sector: the extractor shells were never a
developer's decision to begin with. Only the exit margin reaches them, which is
why the two flags have to be measured together and why the composed arm is the
one that matters. In-game there is no such seeding — every building is
player-zoned and company-spawned — so this bounds the FIXTURE, not the model.

**WHAT REMAINS, and it is now the only thing.** Effectively-idle sites go
37.9/33.3/35.1 % → 41.8/47.1/47.7 %. The shells are gone; the parcels they held
are now honestly VACANT (72/83/102) and nothing re-enters them. Every other
symptom traced this round resolves; this one does not, and it is exactly #56
(redevelopment self-defeating) plus #55's own "with re-entry" clause. The
buildings stand empty because `Assess` prices a ruin off the redevelopment that
would pay, and `ExpectedFlow` then deducts that same land charge, so the hurdle
is never cleared.

## THE DIAGNOSIS: why three sectors are unprofitable, and why rate tweaks cannot fix it

**MEASURED, not argued — four experiment arms** (`marginprobe --seed 1`, 400
ticks, probe-scoped overrides, no default touched):

| arm | industrial p50 | extractor p50 | deaths ind/ext |
|---|---|---|---|
| shipped default | −10.3 | −15.1 | 58 / 183 |
| `--recipe-scale 1.5` | **−88.2 (worse)** | −2.2 | 42 / 178 |
| `--extract-slot 15` | −82.0 | **−62.1 (worse)** | 49 / 236 |
| `--cond-bid-floor 0.2` | — | −2.0 | **33 / 163 (churn −⅓)** |
| all three | −92.6 | −38.5 | 36 / 191 |

**Raising productivity makes the sector WORSE, because the land bill is priced
off the same forecast being tuned.** Raise `ExtractorOutputPerSlot` and the
entry bid, the assessment, and the entrant flow all rise proportionally, while
realized output still carries `Math.Max(0.2, cond)` and ACTUAL suitability. The
office asymptotic argument — bill and revenue both linear in the price knob, so
the gap ratio tends to Quality(5) ≈ 2.06 forever — is thereby measured to
generalize to every sector. There is no rate, price, or output constant whose
adjustment fixes this, and arms B/C are the proof.

THE STRUCTURE, three interlocking mechanisms:

1. **THREE FORMULAS FOR ONE NUMBER.** A firm's marginal product is computed
   three ways that disagree: the ENTRY bid (`FirmBidPerSlot`: flat 0.5
   suitability for extractors, `Quality(ℓ)` uplift, `CondBidFloor = 0.45`), the
   LABOR door cap (`LaborAuction.BuildDoors`: actual suitability, actual
   condition floored at 0.2, no quality off-parity), and REALIZED production
   (actual suit × cond, no quality off-parity). Entry believes the most
   optimistic formula, the bill is assessed on it, and production delivers the
   least. Every gap pushes the same direction on exactly the parcels that get
   entered. This is the same forecast/realization disease as the office ladder
   and #56, at the sector's front door.

2. **THE DEFAULT LABOR PATH HIRES WITH NO PRICE AT THE DOOR.**
   `FeatureFlags.LaborAuction = false` ships, so `AssignWorkplaces` fills slots
   by commute softmax and the firm pays class wages regardless of product: an
   extractor whose marginal product is 1.9/slot pays 10/slot because nothing in
   the assignment asks. The labor auction's door cap IS the fix — it refuses
   hires above the firm's own marginal-revenue forecast — and it ships off.
   Even the LEVEL of the constants says extraction should mostly not exist:
   `6 × suit × cond × price ≥ wage 10` fails for Grain (anchor 1.6) at PERFECT
   suitability and PRISTINE condition — a perfect grain field cannot pay one
   basic wage — and industrial value-added per slot at anchors (Food 9.2,
   Timber 9.6, Metals 11.5, Plastics 11.9, Machinery 9.3) sits below the
   blended wage 12.4 for every recipe at ℓ1. On an arm with honest doors these
   firms would not hire; on the shipped arm they hire and bleed.

3. **THE CONDITION-FLOOR MISMATCH IS THE CHURN ENGINE.** `CondBidFloor = 0.45`
   in the entry forecast against `Math.Max(0.2, cond)` in production: an
   entrant into the derelict stock (cond 0.05, which is 44–50 % of built
   non-res) forecasts 0.478× pristine and realizes 0.2× — a 2.4× over-forecast
   for every entrant, every time, while scarcity holds `OriginStat` above
   anchor over the dead sector so the forecast stays positive. Arm D aligns the
   floors and cuts churn deaths by a third on both sectors — the one measured
   improvement available — but it is also a citywide default (CondFactor prices
   residential too), so even it is a flag-and-fingerprint change, not a tweak.

**WHAT ACTUALLY FIXES IT, in dependency order:** (a) the labor auction ON, so
wages track the firm's own product and unprofitable doors go unfilled instead of
draining the firm; (b) the condition floors aligned, so entry stops
over-forecasting ruins; (c) the bill decoupled from the tuned forecast — either
production parity (production delivers what the bill assumes) or task #48's
realized comparable (the bill reads what comparable sites actually pay). Only
AFTER (c) does tuning the constants mean anything, because only then does a
productivity knob move revenue without moving the bill in lockstep. Office needs
(c) alone — its operating margin is already +8/slot; extractor and industrial
need all three.

## THE FINDING UNDER ALL OF THESE: three of four sectors do not pay for themselves

`marginprobe`, **shipping default**, seeds 1/9/13, 400 ticks:

| sector | cash flow p50 | underwater | capital deaths |
|---|---|---|---|
| Commercial | **+59.5 / +68.9 / +47.9** | 0 % | 9 / 6 / 17 |
| Industrial | −10.3 / −34.1 / −93.8 | 100 / 50 / 100 % | 58 / 13 / 52 |
| Office | −426.7 / −420.4 / −408.8 | 100 % | **0** |
| Extractor | −15.1 / −16.6 / −1.4 | 100 / 100 / 85 % | **183 / 131 / 374** |

**Only commercial pays for itself.** The other three are structurally
unprofitable on the arm that ships, and they fail in two different ways: office
ZOMBIFIES — 100 % under water and not one death in 400 ticks, because it carries
no input debits and must burn a war chest of `reserve + 50m` before
`CompanyBankruptcyLimit` can fire — while industrial and extractor CHURN, 374
extractor deaths on seed 13 being a mill of firms entering, losing money and
dying.

**THIS IS UPSTREAM OF EVERYTHING THIS REGISTRY CALLS A BLOCKER.** Parity does
not collapse a healthy sector; it removes the last survivors of sectors already
dying. Measured directly at 120 ticks, seed 0: the DEFAULT arm already has 7
industrial firms alive, all 100 % under water at −71/tick, with 19 dead of
capital — parity's arm reads 0 alive, 20 capital deaths and 3 arrears. That is
an accelerant, not a cause. Every plan of the form "unblock parity and office
becomes viable" is aimed at the wrong layer, and so was the plan that preceded
it ("fix geology and parity unblocks").

Office and industrial each have a NAMED gap (the bid carries `Quality(ℓ)` where
production does not, off parity). Extractor at −1 to −17 with 131–374 deaths
looks like a third mode and is **unattributed** — nothing in this file explains
it yet.

## The geology collapse: NOT fixed, but the attribution is now wrong in the registry

**WHAT THIS ENTRY USED TO SAY:** "the collapse is the GEOLOGY correction alone
(10 and 8 extractors with it removed and the other three on, against 3 and 1
with all four)". Measured again, on the INDUSTRIAL population this time
(`weberprobe --seed 0 --seeds 6`), that is true of a bit over half the seeds and
false of the rest:

| seed | parity OFF | parity ON | parity ON + `--mutant-flat-geology` |
|---|---|---|---|
| 0 | 6 | 1 | **11** |
| 1 | 8 | 1 | 1 |
| 2 | 10 | 0 | 1 |
| 3 | 10 | 0 | **7** |
| 4 | 12 | 1 | 1 |
| 5 | 7 | 0 | **2** |

Restoring flat geology recovers industry on seeds 0, 3, 5 and does nothing on
1, 2, 4. **So parity has a SECOND collapse channel that geology does not
explain**, and every plan built on "fix geology and parity unblocks" is
planning against one of two causes.

The second channel is visible in the same run and is circular. Parity switches
the industrial output price from `OriginStat` to `OriginComparable` — the §3
circularity guard's firm analog, which refuses a cell's own seller as its own
comparable. With industry thin, most cells have NO other seller, so the
comparable has nothing to read and the entrant argmax **flattens to `Food×196`
on every seed**, against two distinct recipes competing on most seeds with
parity off. A price guard that needs a population to price against, applied to a
population it is thinning, starves itself.

GEOLOGY'S OWN CHANNEL RUNS THROUGH THE SUPPLY CHAIN, not directly:
`--mutant-flat-geology` only touches `Assess`'s extractor read, so it can reach
industry only via extractor land bills → extractor deaths → local raw supply →
industrial input costs. Extractors are the mechanism, industry is the casualty.

**A CANDIDATE FIX WAS BUILT, MEASURED INERT, AND REVERTED.** Vacant derelict
non-residential parcels admitted as developer redevelopment candidates — cost
`demolition + RC − salvage`, funded from developer capital like a greenfield
start, which is the obvious answer to the escrow trap (Escrow is written only in
`RouteLandCharge`, whose only callers are occupant-payment loops, so a parcel
with no occupant can never fund the scrape that would clear it). It fired **ZERO
times over 400 ticks on seed 1** and vacancy was unchanged at 95/187. Not
shipped: a flag that changes nothing is the same fault as a check that cannot
fail.

**WHY IT FIRED ZERO TIMES IS THE REAL FINDING, AND IT IS A SELF-DEFEATING LOOP.**
`Assess` values a derelict site HIGH exactly because redevelopment would pay
(`lr = flow − aOp(cost − Escrow)`). `Construction.ExpectedFlow` then DEDUCTS the
land charge from the developer's return. So the more the assessment says a ruin
is worth rebuilding, the less the developer's flow, and the rebuild never clears
`bestRet > HurdleRate * 1.5` — while the sitting occupant is billed on the
assessment's view and taxed out. The escrow gate is not the deepest cause;
underneath it, even a fully funded developer is refused by the hurdle. Filed as
its own item. Resolving it means reconciling two different forecasts of the same
quantity: `Assess` uses `(bid − SPerUnit(lvl,1.0)) × units`, `ExpectedFlow` uses
predicted rent × absorption − structure − land, calibration-corrected.

## Company types: office specializations and retail lines (both off by default)

**THE ASSESSMENT ALWAYS CLAIMED TO TAKE A MAXIMUM OVER COMPANY TYPES. FOR TWO
SECTORS THE SET HAD SIZE ONE.** `LandAccounting.cs:705-716` sets
`AssessedLR = bestLR`, the max over the current configuration and every
candidate the zoning permits, and `UnitAssessment` charges `s + φ·AssessedLR/Units`
— so an occupant is billed on the best alternative use of its site, not on its
own performance, and `Parcel.Wedge` names the gap. That is the design. Industrial
maximizes over recipes (Weber) and extractor over the raws the geology supports.
Office was one hardcoded `Res.OfficeOutput`; commercial's `cogsIndex` summed the
WHOLE basket, so every shop faced identical costs and sold identical goods.

**OFFICE — three cuts, two measured no-ops.** (1) Each kind drawing on its own
kind's jobs: every firm starts at `Office = default`, so one kind held ALL the
jobs and had exactly the pooled multiplier while the others sat at 1.0;
`max(pooled, 1, 1)` is pooled and the arm measured identical to flag-off down to
the vacancy count. (2) A flat-field tie-break: never fired, because the seeded
city already has offices, all of one kind. **The lesson both taught: slicing ONE
pool three ways can only make each share smaller — it can never make a different
kind better somewhere.** (3) Different DRIVERS per kind — software follows
educated workers, finance follows other offices, media follows commercial mass —
which was still wrong until each driver was measured in units of its own citywide
mean, because educated workers run in thousands where office jobs run in hundreds
and at a shared 1/400 software won everywhere (financial took 0 of 22). Measured
after: software 7 / financial 15 (seed 1), 14 / 16 (seed 9). Media wins nowhere
on either seed, so the effective set is TWO, not three — stated, not hidden.
Office cash flow −426.69 → −405.81.

**RETAIL — the degeneracy was predicted from the constants BEFORE building,
which is the only reason it took one cut instead of three.** Basket shares span
5× (food 0.16 against timber 0.03 of a 0.30 basket) while the delivered-cost term
varies ~13% across clusters, so specializing on share alone puts every shop in
the city on food. Capture is therefore partitioned by line: a line's spending
goes only to the shops selling it, so a catchment thick with grocers has an
unserved machinery line whose incumbent weight is essentially the outside option
alone. A line nobody sells LEAKS, into the leak total the pooled path already
keeps.

One no-op here too, diagnosed rather than shipped: choosing at entry reached
almost nobody, because mid-run commercial entry is about ONE firm per run
(`shopsweep`'s own cohort leg reads 1) and essentially every shop is seeded at
t = 0 — 56 live shops, 56 still whole-basket. Shops that never chose now choose,
staggered on the relocation hazard, because at t = 0 every line looks equally
served and simultaneous choice commits a whole cluster to one line.

MEASURED, seeds 1/9: food 27/44, machinery 15/16, plastics 8/8, timber 5/5, and
29/40, 16/17, 6/6, 7/7. **Live shares land at 49/27/15/9 % against basket shares
of 53/27/10/10 %** — the free-entry equilibrium arriving on its own. The death
pattern is the tell: food loses 17 of 44 while machinery loses 1 and both niches
lose none. Commercial cash flow p50 **59.54 → 113.12**.

**NEITHER FIXES OFFICE VIABILITY, AND THAT IS THE POINT OF MEASURING IT.** With
all three arms on (seed 1): retail lines all four populated, commercial p50
63.49 — and office still 0 alive, 7 margin + 62 capital exits, vacancy 66.9 %.
Specialization buys the RE-ENTRY VARIETY the exit margin needs to turn churn into
turnover. It cannot touch a sector billed 489 against 428 of revenue. Gate at
both commits: `fingerprint --check` all 13 lanes matching with no accept, verify
**64/64** seed 1.

NOT COVERED, named rather than half-done: the store-level consumption path
(`StoreLevelSpending`, off) routes each household to ONE shop rather than
distributing a pool, so per-line capture there is a different change and is not
attempted here.

## The firm exit margin (task #55) — and what turning it on exposed

**THE ZOMBIE FIRMS ARE REAL, THEY ARE OFFICES, AND THEY WERE NEVER MEASURED.**
`marginprobe --seed 1`, shipped default: 22 standing offices, **100 % of them
underwater on their own cash-flow read, p50 = −426.69/tick, and zero exits of
any kind over 400 ticks**. Every office in the city loses 427 a tick forever and
nothing ever asks it to leave. Extractor is 100 % underwater at −15.09; industrial
is the single standing firm, underwater. Commercial is healthy at **+59.54**.

That last number is why this went unseen. The one zombie metric this repo had
(`FirmDiag`: `EMA(revenue) < wageBill`) is **commercial-only**, and it reads
**0 % on every seed** — a true statement about shops and silence about the sector
the parity note already indicts. A metric scoped to the healthy sector cannot
report the sick one.

The arithmetic closes against a defect already on the books. `EconTypes.cs:465`
records offices billed **489/tick against 428 of GROSS revenue**, because the
office BID carries `Quality(ℓ)` while office PRODUCTION does not on the shipping
arm. Add ~366 of wages and the margin is −427. Offices here are not marginal,
they are structurally unviable, and the working-capital floor never finds out:
`CompanyBankruptcyLimit = −150` against a reserve of
`max(200, wageBill × 20)` means a firm must first burn a war chest of `50 m`
(the dividend fixed point, `FirmDividendRate = 0.02`) — at m = −10/tick, ~115
ticks of zombie before the existing trigger can fire.

**WHAT THE MARGIN DOES, AND WHAT IT EXPOSES.** With `--exit-margin` on seed 1:
office 22 alive → **0**, industrial 1 → 0, 11 margin exits and 4 margin
relocations. The rule fires and it is right to. But vacancy goes
**50.8 % → 64.4 %**, and offices do not stop dying — **65 are born and die over
the run against 22 that simply persisted before.** A stable zombie becomes a
revolving one.

The cause is re-entry, and it is structural: `Sector = pl.Use`
(`EconomyEngine.cs`, the entry rule) and `pl.Use` is only ever written
`= pl.Zoned`. **A parcel zoned office can only ever receive another office**,
which fails the same way for the same reason. Vanilla's "the building gets
replaced with another industry which is more viable" does not exist here — so
the exit margin ALONE converts zombie firms into zombie buildings, which is the
worse failure and the one this registry should have said out loud first.

**SO IT SHIPS OFF.** `FeatureFlags.FirmExitMargin = false`. `fingerprint --check`
reads **all 13 lanes matching with no accept**, and verify is **64/64** on seed 1
— the off arm is byte-identical, which is the whole point of shipping it off.
The flip needs three things it does not have yet: sector freedom on re-entry, the
office production parity that makes offices viable at all, and a rewrite of the
nonres-parity OFF-arm FLOOR leg, whose `aOff.stuck >= 1` population is exactly
what a cash-flow exit kills.

**TWO PREMISES OF THE BRIEF WERE WRONG, BOTH VERIFIED AGAINST THE CODE.**
(1) "Vacancy reprices the assessment downward by construction, so we cannot have
vanilla's price-side absorbing state." `LandAccounting.cs:610-617`: `BidPerUnit`
routes residential kinds to `ResidentialBidPerUnit(… addUnits, minSupply,
realized)` and everything else to `FirmBidPerSlot`, whose three signatures take
NONE of those. The repricing channel is **residential-only** — absent from
exactly the parcels an insolvency pipeline produces. Measured: 44/50/46 % of
built non-res parcels already vacant on seeds 1/9/13, vacant condition a point
mass at the floor (p10 = p50 = p90 = 0.05 against 0.91 occupied), entrant excess
positive on **3 of 162, 1 of 193, 7 of 167**.
(2) The margin's land term must be **`owed`, not `pay`**. The levy takes
`min(money, owed)`, so realized rent can never fall short and a margin built on
realized cash reads a zero shortfall precisely when the firm is broke. This was
wrong in the first cut of the design and would have shipped a trigger blind to
the only cost measured to strand firms here.

**CUTCOSTS WAS DROPPED ON PRINCIPLE, NOT EFFICIENCY.** A stress-triggered
"shrink the payroll" stage is `LaborWardMutant` with a trigger, and
`LaborAuction.cs:55-58` says what that is: *"NO income-per-member hiring test
exists anywhere — that Ward rule is the thing this design refuses to encode, and
EconParams.LaborWardMutant exists so the refusal check can prove it would notice
one."* A firm has no door lever regardless (doors are `JobSlots × mix`;
`JobSlots` is the building's unit count), and where production is linear in labor
shedding a worker sheds its output too. Shipped as
`Solvent → SeekCheaperSite → Exit`.

**NO SUNK COST EXISTS TO BUILD AN INACTION BAND FROM.** `FirmSeedCapital` is
drawn from `PhantomBank` at entry and returned to it on every exit path; the
`Firm` record holds no asset field. So Dixit's sunk and liquidation terms are
both literally zero here. The band is the clock's asymmetry instead
(`CashFlowShortTicks` rises on a bad tick and FALLS on a good one), and patience
scales with the working-capital reserve — the firm's own arithmetic over its own
roster, and an honest substitute stated as such rather than a sunk cost we do not
model. Note the deliberate divergence from `LevyShortTicks`, which RESETS on a
paid tick: that clock asks "in arrears right now", this one asks "is this
business viable", and one good tick answers the first but not the second.

## firmprobe Q12 and Q13 — measured, seeds 1/9/13

**Q13, OPTION L AT PARCEL GRAIN.** Share of BUILT parcels whose ℓ\* stands above
their own column's standing max — the parcels Option L would actually pull down:

| sector | seed 1 | seed 9 | seed 13 |
|---|---|---|---|
| Office | 0/64 (0.0 %) | 5/58 (8.6 %) | 0/58 (0.0 %) |
| Industrial | 54/54 (100 %) | 62/62 (100 %) | 55/55 (100 %) |
| Commercial | 74/178 (41.6 %) | 83/180 (46.1 %) | 58/177 (32.8 %) |
| Extractor | 4/71 (5.6 %) | 52/83 (62.7 %) | 36/76 (47.4 %) |

Two corrections to v3, in opposite directions. Industrial is stronger than the
cluster mean implied — Option L moves EVERY built industrial parcel on every
seed. Commercial is weaker: v3 says "binds hard", and at parcel grain it moves a
third to a half. And **extractor's benefit is a property of the seed, not of the
mechanism** — 5.6 % against 62.7 % on the same fixture at a different seed, while
the per-cluster mean reads 1.71 on both and hides it completely. v3's
"binds hard (→1.7–1.8)" must not be quoted as a stable sector result.

**Q12, THE CROSS-CLUSTER CRITERION** (`value_c(5) − A ≤ S(5,1.0)`, A excluding the
door under test). Two things, and the second is the one that decides P1.

FIRST: almost every cluster reads bounded in every sector under both scopes, and
the COUNT is not the information — the MARGIN is. `A1` and `A2` sit within
0.05–7 % of each other, so the value table is nearly flat at the top and
excluding one door barely lowers the alternative. Margins of `v5 − A − S(5,1)`:

| sector | p50 (seeds 1/9/13) | p90 (seeds 1/9/13) |
|---|---|---|
| Commercial | −8.560 / −8.171 / −10.161 | −1.174 / −0.694 / −1.420 |
| Industrial | −13.468 / −13.485 / −12.407 | −0.775 / −0.988 / −0.420 |
| Extractor | −9.545 / −15.774 / −13.595 | −5.374 / −13.513 / −7.453 |
| **Office** | **−0.330 / −0.262 / −0.306** | **−0.020 / −0.019 / −0.046** |

Office is bounded by this criterion and the bound is WORTHLESS: p90 margins of
0.02–0.05 against an `S(5,1.0)` of 3.544 — six tenths of one percent to one and
a third percent of the structure floor. Anything perturbs it. Report office as
knife-edge, never as bounded; and note that this criterion and Option L are two
DIFFERENT proposed bounds giving opposite office answers (Q13: 0 % moved; Q12:
11 of 12 clusters bounded), so a design must not quote one as support for the
other.

SECOND, AND THIS IS THE ANSWER TO THE `alt` SCOPE QUESTION: **the scope is a real
fork, and only for industrial.** Bounded parcels, all-doors vs standing-doors:

| sector | seed 1 | seed 9 | seed 13 |
|---|---|---|---|
| Office | 57/64 vs 57/64 | 53/58 vs 53/58 | 52/58 vs 52/58 |
| Commercial | 175/178 vs 173/178 | 178/180 vs 178/180 | 176/177 vs 174/177 |
| Extractor | 70/71 vs 66/71 | 81/83 vs 81/83 | 74/76 vs 74/76 |
| **Industrial** | **52/54 vs 30/54** | **61/62 vs 47/62** | **53/55 vs 32/55** |

Office is IDENTICAL under both scopes on all three seeds (`A1(all)` equals
`A1(standing)` exactly); commercial and extractor differ by at most a few
parcels. Industrial changes verdict on ~40 % of its parcels, on every seed. The
mechanism is visible in block D: industrial stands only at rungs 1–2, so the
standing-door set excludes the high rungs where the surplus is, and
`A1(standing)` reads about half `A1(all)` (8.732 vs 16.609; 8.783 vs 16.687;
7.988 vs 15.485). **P1 cannot pick the `alt` scope freely.** It is not a
distinction without a difference, it is the industrial verdict.

THE FIRST CUT OF Q12 WAS DEGENERATE AND ITS NUMBERS WERE NEVER RECORDED. A was
computed as a max over a door set INCLUDING the door under test, which makes the
criterion an identity — true for every cluster, with the argmax failing only on
floating point. It printed 11/12, 111/112, 31/31: shapes that read as findings.
Caught from the run output before anything was written down, fixed at
`4462e29` by excluding the tested door (top-two, so the excluded max falls to the
runner-up). Both `A1` and `A2` are now printed under each scope precisely so a
reader can see how close the criterion sits to the degenerate case — which, per
the first point above, is closer than is comfortable.

A FOURTH FINDING IS FILED AND NOT FIXED: `RelocateFirm`'s `BidAt` discards
`FirmBidPerSlot`'s `out chosen`, so for industrial and extractor a firm is moved on
a bid for the destination cluster's BEST output rather than the one it produces
(`f.Output` is unchanged by the move). Pre-existing on the arrears path; #49
extended it to displacement. It violates the models principle — the forecast is not
one the agent could hold about its own business — and it is fingerprint-safe to fix,
since the arrears path needs `NonResLandParity` (off) and displacement never occurs
in the pure sim. Unowned.

**RECONCILED AT THE REGISTRY-FIX COMMIT.** The two-track and four-track merges each carried their own branch's row table into this file, and a union merge left BOTH standing — two tables making contradictory claims about the same seeds (one said seed 5's Weber leg was red, the other that it was green; one carried an occupancy row and a seed-13 collapse row that two other tracks had already fixed). That is precisely the failure this file exists to prevent, and it was self-inflicted by the merge, not by any item. Every row below is now re-derived from runs on the MERGED tree `234d1c3`: `verify` seeds 0/1/5/9/13, `occsweep --seeds 50`, `clearsweep --seeds 300`, and `webersweep`. What those runs read at `234d1c3`: verify 60/60 on seeds 1, 9 and 13 and 59/60 on seeds 0 (Weber) and 5 (nonres parity); `occsweep` **57/57**, which CLOSED the occupancy row; `clearsweep --seeds 300` **291/300** red on exactly {4, 6, 96, 100, 148, 266, 268, 272, 297}, which is the union of the two clearing-price rows below and confirms both; `webersweep` **25/26** red on **seed 0 alone**, which confirms the Weber row and settles the contradiction — seed 5's Weber leg is genuinely GREEN, the diversity-leg demotion having done what it claimed. Rows the measurement closed moved to Closed with the run that closed them; rows it confirmed kept their original attribution text, which is the per-item history and is worth more than a restatement.

| Seed | Check | Status | Attribution |
|---|---|---|---|
| 0 | Weber: extraction follows geology; recipes follow input sourcing | red | **SEED 5 IS GREEN AT THE T3 INVESTIGATION COMMIT; THE DIVERSITY LEG IS NOW A PRECONDITION.** The owed investigation this row named — "when is one industrial output the correct answer for a small city, and should the diversity leg be a precondition rather than an assertion" — is DONE, and the answer is precondition, on evidence from `weberprobe --seed 0 --seeds 26` (the entrant's own argmax, `FirmBidPerSlot`'s industrial leg, evaluated at every one of the 196 clusters). Three findings. (1) One output is often the profit-maximizing answer for EVERY entrant: on seed 5 Food is the argmax at **195 of 196 clusters** with the runner-up trailing by a median **0.941** per filled slot, so a second output there requires an entrant to choose against its own margin, which the defaults rule forbids the mechanism to make anyone do. (2) The realized count is ANTI-correlated with the spatial signal on this fixture: **17 of 26** seeds realize two or three outputs while a SINGLE recipe is argmax at all 196 clusters (2, 3, 6, 7, 10, 11, 13, 14, 15, 17, 18, 19, 20, 21, 22, 24, 25), and BOTH seeds the leg failed on — 0 and 5 — are seeds where two recipes ARE argmax somewhere. What the count records is entry TIMING against a moving price path, the same "freeze artifact" the retooling dead end below already named. (3) It is a small-sample statistic: 6-13 industrial firms per seed, median 9. IMPLEMENTED: `outputsSeen.Count >= 2` leaves the per-seed verdict and is reported as a discrimination premise for the alignment leg, counted over SINGLE-INPUT outputs because the alignment leg skips Machinery (both counts print; over seeds 0-25 the two definitions agree, premise met on 24 of 26, not met on 0 and 5). The paired non-degeneracy floor lives in `WeberSweep`, where the distributional statement belongs: the premise must hold on more than half the swept seeds, else the alignment leg is never asked to separate places and nothing says so; the regime the retooling experiment produced (premise on 6 of 26) fails it. SEED 0 STAYS RED and on the leg it always was: 80% of extractors on the best raw against the >=90% bar. PER-COMMIT HISTORY, retained for attribution: MEASURED ON THE TWO-TRACK MERGE, which is the shipping state and supersedes every per-branch tally below: `webersweep` (seeds 0-25) reads **24/26**, red only on 0 and 5, against 22/26 {0,4,7,16} on the goods track alone and reds on 9 and 13 on the housing track alone (all three sweeps run by the orchestrator on their own trees). Seeds 1, 9 and 13 are GREEN on the merge — each track healed part of what the other left red, which is why no branch's row could be textually correct about the combination. **Seed 5 fails on the DIVERSITY PRECONDITION ALONE**: 10 extractors at 100% on the best raw and 7 of 7 single-input industrials on the cheapest-sourced recipe — the Weber property the check exists to assert holds PERFECTLY — and the verdict is red because the city produces 1 distinct industrial output against a >=2 bar. That is a fixture-adequacy failure, not an economics failure, and it is the shape the whole Weber history has been pointing at: the alignment legs now pass wherever the premise is delivered. Seed 0 is a real miss on the extraction leg (10 extractors, 80% on best raw vs the >=90% bar) with recipes passing at 67% of 6 and the same 1-output diversity fail. THE OWED INVESTIGATION is therefore re-scoped by measurement: it is no longer 'why do recipes misalign' (localized prices answered that - see Closed) but 'when is one industrial output the correct answer for a small city, and should the diversity leg be a precondition rather than an assertion'. Unowned. PER-BRANCH HISTORY, retained for attribution: New with the DEFAULT FLIP: on the auction-default world, 6 of 13 single-input industrials sit on the cheapest-sourced recipe (46% vs the ≥55% bar; the margin is 2 firms) while the extraction leg is clean at 100% and the sector is bigger and more diverse than required (13 firms, 4 outputs). At the FLIP commit it was seed-13-specific — the same check read 100% on flip seeds 0-1 and passed 2, 3, 9, 25 (posted-arm seed 13 green); that cohort statement is DATED: at the outside-anchor commit the extraction leg is red on seeds 0, 9 and 25 (fix-round measurement — class row below). Precedent: Weber was the red on auction-arm seed 0 at `c0c584d` — auction-world Weber fragility moves seeds, it is not new. Wants its own investigation: which 7 firms, their delivered-cost gaps, and whether entry timing under prospect-driven population growth outruns trade-price settling. Unowned. AT THE OUTSIDE-ANCHOR COMMIT (#42) the same row stays red with different numbers — recipes 14% of 14 industrials, extraction still 100% — the anchored outside re-equilibrates the whole default world (baseline churn, population, entry timing), and Weber's verdict re-rolls with it. AT THE PER-CLUSTER-MEMORY COMMIT (#34) seed 13 is GREEN (extraction 100%, recipes 85% of 13) — the tie-driven admission re-roll moves the verdict again, in Weber's documented seed-moving way; the class row below carries the standing red. AT THE OWNER-DOOR COMMIT (#41) seed 13 is RED again, and on the fixture's POPULATION PRECONDITION rather than on any alignment: 4 extractors against the `extract >= 5` minimum, with extraction 100% on best raw and recipes passing at 69% of 13. Measured against the branch tip in the same session (tip 209e232: 6 extractors, 100%, recipes 85% of 13, GREEN). #41 touches no pricing, recipe or siting rule — its diff is the housing auction, the owner tag and the checks; LandAccounting changes only its header comment and Trade is untouched — so this is the same re-equilibration channel #42 and #34 moved the verdict through, now reaching the industrial sector's SIZE. | || AND THE EXTRACTION-LEG CLASS: NEW at the outside-anchor commit (#42), on the EXTRACTION leg — seed 9 registered at that commit; seeds 0 and 25 found red at the SAME commit by the refuter's wider sweep (finding F1) and registered at the fix round, which reproduced them (fix-round verify runs, seeds 0 and 25; both are FULLY GREEN at base 554b56a, extraction 100%). Numbers at the fix commit: seed 9 — 7 of 10 extractors on the best raw (70% vs the ≥90% bar), recipe leg passes at 80%; seed 0 — 8 of 9 extractors (89%), the margin one extractor, recipes pass at 82% of 11; seed 25 — 7 of 8 extractors (88%), recipes pass at 82% of 17. Seeds 2 and 5 pass in full. Tally across the seeds measured in the two sessions (0, 1, 2, 5, 9, 13, 25): Weber red on 4 of 7 — 0, 9, 25 on extraction, 13 on recipes — against 1 of 7 at base; the owed Weber investigation is that much bigger than a single-seed row suggested. Same fragility class as the seed-13 row — the anchored outside option changes the default world every fixture equilibrates in (churn, entry timing, trade-price settling), and Weber's seed-dependent verdict moves seeds with it, exactly as it moved 0 → 13 at the flip. No pricing/recipe rule is touched by #42 (the diff is Prospects/HousingAuction/Access outside-door legs only; posted fingerprint lanes are hash-identical). Belongs to the same owed Weber investigation as the row above. Unowned. AT THE PER-CLUSTER-MEMORY COMMIT (#34) the class re-rolls again: seeds 0 and 9 GREEN (extraction 100% both, recipes 90%/78%), seed 25 red and DEEPER — extraction 44% of 9 extractors (4 on best raw) vs the ≥90% bar, recipes pass at 81% of 16 (#34 gate runs, verify seeds 0, 9, 25). Tally on the measured cohort {0, 1, 9, 13, 25}: 1 of 5 red at #34 against 4 of 7 at #42. The owed investigation is unchanged in shape and now owns seed 25's deep extraction miss. AT THE OWNER-DOOR COMMIT (#41) seed 9 is RED again on this leg — 6 of 7 extractors on the best raw (86% vs the ≥90% bar, the margin one extractor), recipes passing at 80% of 10 — against a tip-209e232 run in the same session that reads 9 extractors, 100%, GREEN. Same channel as the rows above: the auction-default world re-equilibrates and Weber's verdict re-rolls with it; #41 touches no pricing or recipe rule. |
| 5 | nonres parity: office product carries the level and condition terms its land is assessed on | red | NEW, found by extending `paritysweep` past its claimed sweep (seeds 0-3, 9, 13 all green, matching the item's commit) to seeds 4-5: `paritysweep --seeds 6` (which resolves to seeds 0-5, 9, 13) reads 7/8, red only on seed 5, on this leg's own non-degeneracy floor — 0 staffed offices on the parity arm carry an assessed level×condition term ≥ 1.15, so `oOn.n >= 1` fails outright (the leg cannot even be evaluated, let alone pass). This is not a new mechanism defect: it is the SAME ℓ*-corner population collapse the item's own commit measures and ships the switch off for (turning `NonResLandParity` on starves the office/extractor sectors on some seeds) — seed 5 is simply a seed where that collapse empties the exact sub-population this leg counts. Seed 4 (also untested by the original claim) passes in full. Unowned; the fix is the same owed work as the switch's own blocker (a realized comparable per (cluster, sector, level) over other firms to bound ℓ*), not a fix to the check. |
| 4, 6, 96, 100, 148, 268, 272, 297 | clearing price: quantity responds (population-collapse leg) | red | **RE-ATTRIBUTED AND RE-SCOPED AT THE T3 INVESTIGATION COMMIT; seed 13 is GREEN.** The registry's hypothesis below was measured and is DEAD, and both mutants it named restore the fall, so neither identifies a channel. `collapseprobe --seed 13` decomposes the seed-13 rise by holding each pricing input at its pre-collapse value: holding the demand SHARE field moves the post price 2.59 -> 0.52, holding the ladder moves it to 2.74, holding stock or access moves it not at all. The cause is the check's OWN CULL, which exempted the probed cluster's residents - and those residents' renewal bids were 18 of the 23 bids the submarket faced, so the bid count at the probed submarket read **23.00 -> 23.00, exactly unchanged, while 695 of 1317 households exited**. The survivors re-sorted up-market into it (SingleBasic 2.90 -> 0.00 bids, FamilyBasic 5.21 -> 7.75, SeniorMid 2.07 -> 5.46) and the price correctly ROSE on an unchanged quantity. The leg was not measuring a collapse. FIXED: the cull no longer exempts the probed cluster, and the leg gained the paired non-degeneracy floor the charter requires - the bid count at the probed submarket must fall below 0.80x, else the check reds because its own stimulus failed. `--mutant-spare-probed-cluster` restores the exemption and is the RUN mutant that reds the floor. Measured, `clearsweep --seeds 300` both arms at this commit: fixed check **291/300** with **zero** floor-only failures; mutant arm **73/300** (227 red: 180 on the floor alone, 47 on floor and softening together), and on gate seed 13 the mutant reads bids 23 -> 23, demandFell False, softens False - the `88fc88c` defect reproduced on demand. THE STANDING RED WAS 47 SEEDS, NOT ONE: reconstructing the base check's verdict on this tree from the mutant arm's audit lines (its cull is `88fc88c`'s exactly) gives red on 47 of 300 - 3, 4, 13, 14, 25, 67, 69, 70, 75, 76, 81, 82, 96, 100, 105, 112, 115, 116, 118, 123, 125, 130, 138, 144, 145, 148, 153, 157, 176, 182, 184, 226, 228, 232, 239, 254, 259, 260, 266, 268, 272, 273, 275, 279, 288, 291, 297 - all on the softening leg. The row named only seed 13 because only seed 13 had been swept. The 9 residual reds are a strict improvement on that 47 and are DIFFERENT failures: on 4, 6, 96, 100, 148 and 272 the demand collapse is real (bids 17 -> 2 on seed 6, 16 -> 3 on 96, 17 -> 7 on 148) and the price still does not soften - that is the genuine open question this leg now asks cleanly, and it is unowned. On 268 and 297 the cull barely reaches the submarket (bids 10 -> 9 and 16 -> 15) AND the price does not soften; both legs fail together. Seed 266 fails on the DEMAND-ladder and 25-point-sweep legs (dLow 0.5192 > dMid 0.4492), unchanged by this commit and unrelated to the collapse leg - it is red on the mutant arm with identical numbers, and is listed in its own row below. Gate seeds after the fix: 1 (bids 21 -> 11, softens), 9 (24 -> 13, softens), 13 (23 -> 7, price 2.15 -> 0.88, fill 1.00 -> 0.35) all pass. THE VACANCY DISJUNCT NOW FIRES: the leg's own comment recorded that `fillAfter < fillBefore*0.8` fired on 0 of 300 seeds and left "find a cull that actually produces vacancy in this fixture" as an open investigation; the exemption was the answer. PER-COMMIT HISTORY, retained for attribution: NEW AT THE TWO-TRACK MERGE, and an INTERACTION - it is green on each track alone, measured by the orchestrator on all three trees: housing tip 01c7543 reads bid 2.96 -> 2.51 after 638 citywide exits (PASS, verify 38/39), the goods track reads verify 35/35 at seed 13, and the merge reads bid 2.15 -> **2.56** after 695 exits - the price RISES where the leg requires it to fall. The supply and demand legs stay monotone and correct on the merge (supply x{0.5,2,8} -> 8.56/0.52/0.52; demand x{0.5,1,2} -> 0.52/2.15/8.56; 25-point sweep monotone), so what fails is only the population-collapse direction. HYPOTHESIS, NOT MEASURED: per-parcel owner asks (#41) hold occupied doors at their floors while the survivors of a collapse re-sort upward into the best stock, so the probed submarket's marginal bidder can end up richer than before the collapse - the composition effect outrunning the scarcity effect. That is a guess and must be measured before anyone acts on it: the honest next step is to re-run the collapse leg with owner asks disabled (`--owner-ask 0`) and with localized goods prices mutated off (`--mutant-citywide-goods`) and see which one restores the fall. DONE, AND THE HYPOTHESIS IS DEAD: BOTH restore it, so neither is the channel. `collapseprobe --seed 13 --owner-ask 0` reads bid 5.4617 -> 1.1227 with the probed submarket's bid count 26 -> 19; `--mutant-citywide-goods` reads 2.8994 -> 2.6494 with 24 -> 22; the shipped arm reads 2.1510 -> 2.5576 with 23 -> 23. The covariate is the bid count, not either feature: each mutant perturbs the survivors' re-sort enough that the count falls too, and where the count is flat the price rises. The re-attribution and the fix are in this row's header.
| 266 | clearing price: quantity responds (DEMAND-ladder and 25-point-sweep legs) | red | NEW ROW, NOT A NEW RED — found by the first sweep this check has ever had (`clearsweep --seeds 300`, added at the T3 investigation commit) and PRE-EXISTING: it reads identically on the `--mutant-spare-probed-cluster` arm, whose cull is `88fc88c`'s. The demand ladder is non-monotone at half presence — `demand×{0.5,1,2} -> 0.52/0.45/3.23`, i.e. dLow 0.5192 ABOVE dMid 0.4492 — and the 25-point supply sweep has an upward step. Both legs run BEFORE the population cull and are untouched by that commit's change. This is the boundary the leg's own comment already flags as unwatched at 25 points ("a 401-point sweep over the IDENTICAL range finds an upward step on 99 of the 300"); seed 266 is the case where 25 points is enough to see it and the demand ladder inverts with it. Unowned. |


## Recorded findings — measured, not red, and easy to misread

### The labor market's outside-worker share is a door RATION, not a preference

Investigated at the T3 investigation commit. Not a red; recorded because the
labor fingerprint arm exists to watch this number and nothing said how to read
it. Instrument: `laborprobe` (see Debugging.cs).

The scalars at `88fc88c` are **outsideShare 0.589**, unempShare 0.000,
meanToverCap 0.366 (an earlier brief quoted 0.53/0.36 — that is stanza 6; the
share has drifted 0.516 → 0.532 → 0.572 → 0.575 → 0.560 → 0.571 → 0.589 across
the campaign).

**It is not a near-border artifact.** `laborprobe --commute-cost {0.00, 0.05,
0.20, 0.50}`: making the border FREE — the strongest possible "the border is
two minutes away" case — moves the share from 0.589 to 0.604, i.e. 1.5 points.
Across grid sizes the median border commute TRIPLES (9 → 30 minutes, 8×8 →
20×20, which makes the outside option WORSE) while the share falls.

**It is a door-supply artifact of the fixture.** Door slots per worker and the
outside share move together on every arm measured, and the identity
`outsideShare = 1 − matched/workers` holds exactly because unemployment is
zero: 8×8 0.52 slots/worker → 0.589; 12×12 0.50 → 0.589; 16×16 0.78 → 0.452;
20×20 1.16 → 0.231; 14×14/6000 at 300 ticks 0.56 → 0.529. The pinned 8×8
fixture builds job slots for 52% of its workers, so at least 48% of them are
outside at ANY outside wage that beats leisure. `slotsPerWorker` is now a
scalar on the labor lane so the share is never read without it (a scalar, not
a hash term — it moves no lane hash and needed no accept; `fingerprint --check`
all 13 lanes match at this commit).

**`unempShare = 0.00` carries no information at the shipped level.**
`LaborAuction.Why` labels every unmatched worker `Outside` whenever its outside
net beats its own leisure floor — 11.24 against 2.77 here — so the scalar is
pinned to zero for any `OutsideWageMult` above about 0.55. It reads 0.215 at
0.30 and 0.051 at 0.45. Every unmatched worker on every arm measured holds a
shortlisted door worth more than its own default price-free (median surplus
25.8–32.5): they are rationed, not choosing.

**The level was never swept; it is now, and it is NOT changed.**
`laborprobe --outside-mult {0.30, 0.45, 0.55, 0.70, 0.85}` gives outside share
0.065 / 0.338 / 0.490 / 0.589 / 0.621 and wage share of marginal product 0.238
/ 0.305 / 0.333 / 0.366 / 0.402 — a strong dose-response, and T/cap rising with
the outside wage is the bargain working, not a defect. Left at 0.70 on the
charter's globals rule: the outside wage is a property of the outside world, so
calibrating it to make the CITY's unemployment look right would be fitting a
global to a local outcome. What it would need is an outside-world anchor, and
none is measured. Both tables now live in the parameters' own comments
(`EconParams.OutsideWageMult`, `EconParams.CommuteCostPerMinute`), which no
longer say "Default unswept".

**Downstream**, measured from `LaborFirmDebitsThisTick` against
`LaborOutsideCreditsThisTick`: 70–80% of wage income on the shipped fixture is
paid by `OutsideWorld` rather than by any firm (76% at 8×8, 80% at 12×12, 70%
on the 14×14/6000/300 arm), falling to 39% at 20×20 where doors are abundant.
Firm staffing runs 65–85% of built slots throughout. So most household wage
income on the pinned fixture enters without any firm's output behind it — a
real property of a commuter city, and one that a reader of the tax base and
the commercial takings needs to know is there.

WHAT WOULD SETTLE THE REMAINING QUESTION, since it was not settled here: the
open item is not the outside wage but why the city builds doors for only half
its workers on the small fixtures. That is a construction/entry question, and
the run that would start it is `laborprobe` at 20×20 (where the ration lifts)
against the entry and firm-bid paths, not another sweep of this parameter.

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
| occupancy channel: realized vacancy softens rent (a PINNED-POSTED check, 51 of the suite) | red on {1, 20, 22, 28, 30, 41} of 57 at `88fc88c`, and on a DIFFERENT set at every world change before that ({9, 41, 910} at base 554b56a, {23, 41, 43, 46, 48} on the housing track, 48/57 on the goods track) | BOTH LEGS REWRITTEN; THE RED SET IS EMPTY. `occsweep --seeds 50` reads **57/57** at the instruments commit against **51/57** at base `88fc88c` (both run this session, base in its own worktree). Six verdicts change, all red -> green, and the base run says which leg each was: seeds 1, 20 and 22 failed the TRACKING leg's `worstErr < 0.5` at exactly 0.500 and seed 28 at 0.548 — a bound sitting ON the sweep's own maximum, with the price leg PASSING on all four — while seeds 30 and 41 failed the PRICE leg with the probed cluster's bid unmoved to three decimals (1.492 -> 1.492 and 1.582 -> 1.582). So 4 of the 6 were the un-headroomed bound the check's own comment warned about and 2 were the selection lottery; the brief that owned this row named only the second. NEITHER LEG NOW DEPENDS ON WORLD COMPOSITION. (a) is an exact EMA-recursion differential oracle against an independently recomputed per-submarket fill target — residual measured **0.0 on all 57 seeds** — carrying its own paired non-degeneracy floor: the same identity read against the NEXT submarket's inputs must break on >= 40 submarkets (measured 65-100). (b) reprices EVERY cleared candidate rather than the lowest-indexed one and asserts the MEDIAN response (measured 0.246-0.490 against a 0.70 bar) plus the share of them that respond at all (0.841-0.984 against 0.60); both bars sit between the measured range and the value a severed channel gives, never at the observed number. THE REWRITE IS NOT A LOOSENING, and that is measured with five mutants that each sever one half of the channel, `occsweep --seeds 50` under each: `--mutant-fill-blind` (the attraction term ignores FillEma — the exact channel the check names) **0/57**, and `--mutant-fill-pooled`, `--mutant-fill-alpha`, `--mutant-fill-frozen`, `--mutant-fill-shift` **0/57** each. THE REWRITE IS ALSO STRICTLY STRONGER WHERE IT LOOKS WEAKER, measured on the same five mutant sweeps: the OLD leg (a) bound `worstErr < 0.5` is SATISFIED - i.e. the old form would not have fired at all - on 51/57 seeds under `--mutant-fill-pooled`, 54/57 under `--mutant-fill-alpha`, 41/57 under `--mutant-fill-shift`, 11/57 under `--mutant-fill-frozen` and 52/57 under `--mutant-fill-blind`, while the new identity leg reds every one of the 57 on each with residuals 0.17-0.25 (0.0 under the blind mutant, where leg (b) does the work instead). WHERE IT IS MORE TOLERANT, stated plainly: on the CLEAN arm six seeds move red -> green. Four of them (1, 20, 22, 28) because the `worstErr < 0.5` bound was DELETED, and that deletion is the item - the bound sat at the sweep's own maximum (0.500 measured against a 0.5 bar) and could not have been tightened either, since the EMA's own step response admits |FillEma - target| up to (1 - alpha) = 0.75 after a single full-swing tick, so any bound under 0.75 asserts how fast this fixture's occupancy moves rather than anything about the mechanism. Two of them (30, 41) because the price leg no longer rests on whichever cluster happens to be lowest-indexed among the 47-70 that qualify; both of those clusters are measurably mute (no demand-share movement at all under the perturbation), which is a property of the world and not of the channel. The attribution separates cleanly, which is the point: the four FillEma-source mutants red leg (a) with residual 8.3E-2..1.2E-1 while leg (b)'s median still reads 0.363-0.465, and the blind mutant reds leg (b) with median 1.000 and 0 of 55 responding while leg (a)'s residual stays 0.0. A MUTE CLUSTER IS REAL, and here is how that was established rather than assumed: across the 57-seed `occprobe`, 154 of 3374 cleared submarkets do not move at all, and for 133 of them the demand SHARE does not move either — no household names that cluster as its best alternative, so its whole demand share is sitting tenants renewing, which `RebuildDemandShares` counts unconditionally by design. The other 21 lose share and still do not move the price: the demand ladder is flat where they read it. Both are properties of the world, which is why the verdict no longer rests on one draw from it. The pre-rewrite row's full four-world history is in the git history of this file. |
| seeds 20, 22, 23, 26, 28, 208, auction equilibrium | red (`unsold-above-reserve`, the CutVacancies indifference defect, bisected to `694fbd3`, resized 2→6 by the canary's first sweep) | Fixed by making every repair round a full re-clear from the reserve and deleting CutVacancies, the vacancy chains and the wait queues outright. The invariant "a non-full door posts its reserve" is now structural (SetPrices clamps the non-full branch; nobody mid-build holds a slot while bidding). Canary 33/39 → 38/39; LP-optimality gap 0.81–1.61% → 0.01–0.03% of LP*. Fiscal shift recorded in the fingerprint log: sumLR −0.95% (high −17.5%, low −13.2%), meanRent −7.7%, treasury −13.5%, household money +5.7% — phantom scarcity leaving the tax base. |
| seed 1, clearing price: quantity responds (population-collapse leg) | red at the outside-anchor commit (#42): after the 60% citywide cull the posted-curve bid read 2.90 → 2.95 (+1.7%), missing the `after < before×0.98` fall while fill stayed 1.00 → 1.00 (the vacancy disjunct fires on 0/300 seeds — the check's own comment); the composition-drift class the row named held 12/300 seeds before seed 1 joined it | Green at the per-cluster-memory commit (#34) — supply legs 9.52/0.83/0.83, all legs pass (#34 gate run, verify seed 1) — but NOT a targeted fix: the tie-driven admission re-roll changes WHO is present at the cull, the same composition channel that redded it at #42. The 12/300-seed class and the owed fix (a cull that actually produces vacancy on this fixture) are untouched; a later re-red on this leg belongs to that class. |
| seed 28, housing canary: auction equilibrium (improving-swap leg) | red at the per-cluster-memory commit (#34): one pair with price-free swap gain 0.173 against the leg's ε_i+ε_j band 0.166 (canary 38/39); registered there as a shortlist conditional-efficiency knife-edge | The registered mechanism reading was WRONG — the fix-round dissection (single-seed `canary --from 28` run, pair hh285/sub252 ↔ hh1609/sub767) decomposes the gain by the identity gain = envy_i + envy_j + (entry−posted gap at each door): 0.173 = 0.037 + 0.120 + 0.016 + 0.000, with each envy inside the envy leg's own band (0.167 / 0.165) — an ε-residual of the finite auction, fully inside the mechanism's stated guarantee, not value the shortlisted solve left on the table (one of the two doors even had 19 free rooms — nothing was hidden from anybody). The swap band was UNDER-DERIVED: it granted a PAIR of envies the allowance the envy leg's two-sided accounting grants ONE envy — the same arithmetic class as the envy band's own recorded fix ("1–8 households per seed at 1.3–2.0% against a 1.0% bar, all of them this arithmetic"). Band restated to compose the two envy bands (ε of each of the four valuations in the trade); the entry−posted gaps stay OUT of the band — with them in, the leg is a near-consequence of the envy sweep and loses its independent failure mode, and resting-price gaps are exactly how restricted competition shows. Falsifiability re-proven under the restated band: M5 (AuctionRepairRounds = 0) reds the swap leg on 11/11 seeds run — 0, 1, 2, 28, 138, 208, 271, 327, 549, 910, 6550, swaps 1 each, gains 0.076–0.336, every one above its restated band (fix-round mutant run, reverted). Canary 39/39 at the fix commit. The "shortlist under-covers on recomposed worlds" investigation this row previously named is NOT owed: the measured decomposition shows no under-coverage, and the M5 signature (gap-channel gains) remains gated. |
| seed 25, housing auction is a competitive equilibrium | red at `70ef971` (knife-edge ε-residual: envy 2 households, worst 1.50% vs the ~1% band, converged True, clean True; the one residual of canary 38/39) | Green at the per-cluster prospect-odds commit — canary 39/39, verify 29/29 — but NOT a targeted fix: undiluted prospect budgets change the admission mix on that fixture and the ε-residual falls back inside the band. The knife-edge CLASS is untouched; if seed 25 (or a neighbor) re-reds on later auction work, attribute to that class, not to the prospect-odds change. |
| boombust `--auction` printed "0 arrivals realized" | telemetry artifact at `70ef971`, not a fact | The scenario summed only `LastFlows` arrivals, which the auction path structurally zeroes; prospect admits live in `LastProspects`. Measured with admits counted: HEAD mechanism realizes 2507 arrivals on the same fixture (local-odds change: 2569). The scenario's ASSERTED margin still reads `DesiredBySegment` (+0 on the auction path) — that assertion dies with the posted path, not with prospect work. |
| seed 3, clearing price / tracksIncome | red before `3a507e9` | Fixed by pairing the income legs (`3a507e9`) and the four-leg restatement (`c0c584d`). |
| seeds 271, 327, clearing price / tracksIncome | red before `c0c584d` | Transfer-anchored tail; fixed by doubling the transfer through a save/restore clone (`c0c584d`). |
