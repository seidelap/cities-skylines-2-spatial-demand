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

## verify (63 checks)

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

WHY THREE AND NOT ONE. The two defects have different shapes and one leg cannot
see both. `--mutant-displaced-no-exit` (no resolution pass — the shipped state
before this item) leaves 13–29 live firms holding no site; `--mutant-half-unlink`
(the parcel's pointer cleared, the firm's left naming the site — the exact shape
`EconReader.SyncParcels` shipped for a despawned building) leaves ZERO siteless
firms and passes the settlement leg clean, and is caught only by the link leg, at
12–28 breaks against a bound of 0.

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
