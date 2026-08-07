# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a 4-core Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs against the
vanilla-baseline mode on identical seeds. Seed: 20260806. Numbers below are from
the **resource-level core** with the **spatial vacancy kernel** and **instant
co-op re-rate** (see "What the mechanisms are" below).
Full design: [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md) ·
plan: [`PLAN.md`](PLAN.md) (§4.4 records deviations from the design) ·
in-game bring-up: [`MOD-BRINGUP.md`](MOD-BRINGUP.md).

**18/18 correctness checks · 10/10 acceptance targets.**

## Headline results

| design claim | measured |
|---|---|
| §2(c) construction/leveling respond to residual submarket demand | realized level map rank-correlates with ℓ* at **0.40** (vanilla grind: −0.01); vacancy suppression is **44:1** more concentrated in the treated interior than beyond spillover reach |
| §2(d) Georgist core closes the fiscal loop | a transit corridor raises land-rent revenue along itself **+27.5%** within 60 ticks (control row −5.4%); circularity guard holds bit-exactly under a 17.5× realized-rent perturbation |
| §2(e) finite-depth trade kills export spam | sustained monoculture bends the marginal received price **49% below** the flat-price baseline at 168/tick; truck→rail→backstop progression emerges by volume (road 72% → rail 59% → sea 30%) with concurrent marginal prices equalized to **0.0%** |
| §4.2 location choice is a real Weber problem | 100% of extractor entrants pick their cluster's best raw by geology; 63% of single-input industrial entrants pick the recipe whose input is cheapest-sourced *at their location* |
| §2(a) migration margin with boom/bust asymmetry | symmetric amenity pulse: arrival response **+23** vs departure response **+1** over equal windows |
| §3 anti-synchronization | 5,626 displacement exits with the worst single tick carrying **0.3%**; co-tenant charges bit-identical across 114/114 multi-tenant parcels |
| money conservation | double-entry ledger drift ≤ **2.8×10⁻⁷** over 300 ticks across all scenario economies |

## Correctness verification (18 checks, 18 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ\* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 9 extractors (100 % on best raw); 19 single-input industrials (63 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: worst per-unit gap 1.7E-016; local retention claim 27 % vs vacancy 27 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 114/114 multi-tenant parcels uniform; worst |charged − market|/market = 3.2E-004
- ✅ **circularity guard: assessment blind to own realized rent**: LR 1.5565 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.83E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: escrow balance 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 25 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 715, drift −1.7E-008
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=B2F9514ED8C501B7 twice, h(seed+1)=8C452D656913CE0F

## Acceptance scenarios (10 targets, 10 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy localization ≥10:1 (vanilla ≈1:1) | ✅ | spatial-excess suppression interior 4.4/cluster vs beyond-spillover −1.0 → **44:1** (raw 7.6/1.2, level effect 3.2/2.2) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.40 vs vanilla −0.01 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +27.5% vs control −5.4% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 168/tick, marginal 1.32 vs flat 2.60 → bend 49 % |
| truck→rail→backstop progression; concurrent marginals equalize | ✅ | ramp 8→950/tick: road 72 %→22 %; rail 28 %→59 %; sea 30 % at top; marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | arrival response +23 vs departure response +1 |
| no synchronized displacement: exit times form a distribution | ✅ | 5626 exits, worst single tick 0.3 % |
| stalled construction appears in engineered busts | ✅ | 7 projects abandoned mid-build (261 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 202 observations; factor range [0.70,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | 3.3 ms at 8 parcels/cluster vs 3.9 ms at 16 (cluster count fixed) |

### How vacancy localization is measured, and what it does *not* claim

The §6 target is about suppression of construction **demand**, so that is what
the test gates on, via a triple difference: a disk shock (95% of a 2.5 km
residential disk evicted) against a no-shock control against a **same-headcount
uniform-exit arm**, with a 2λ exclusion buffer so the reference region is clear
of the kernel's tail. The uniform arm nets out the level effect any mass exit
causes; what remains is the *spatial* excess, 4.4 units/cluster inside vs −1.0
beyond reach.

Realized starts are **reported but not gated**, and they do not follow the
field: interior unit-starts go 18 (concentrated) vs 12 (same-size uniform exits)
vs 7 (no shock). Two couplings break the monotonicity, both real:

1. the shock's own vacancy **raises the citywide absorption budget**
   (hazard × vacant stock), admitting more migrants everywhere;
2. a hole punched in an otherwise-intact city **refills by spill-in** through
   the same kernel, whereas uniform exits thin every catchment at once.

Both are properties of Tier A arrival pacing, not of the suppression mechanism.
Calling this target met on the demand field is a deliberate, narrower claim than
"a vacancy shock reduces observed construction in that district," which is
**not** true under absorption pacing and should not be quoted as if it were.

## What the mechanisms are

**Vacancy kernel.** Each vacant unit's competitive weight spreads over nearby
substitutable supply as e^(−d/λ) with λ = 800 m of straight-line (walking-proxy)
distance, normalized per emitter so **V = 1**: one vacancy cancels exactly one
unit of demand citywide, and λ only decides where it lands. `MetersPerUnit` maps
synthetic-grid units, TNTP state-plane feet and in-game metres onto one real
scale. Exposure includes the *buildable margin* (zoned-empty and pipeline
capacity), and residential channels cross-substitute on the same shares the
seeker allocation uses — a vacant tower unit competes for the seekers a would-be
duplex courts.

**Pipeline claims share that footprint.** A unit under construction and a
finished vacant unit are the same competitive object, so both route through one
`Spread()`. This is load-bearing, not tidiness: netting claims locally while
smearing vacancy made *completing an empty building raise its own cluster's
residual demand*, a ratchet that started buildings beside standing empties on a
ConstructionLag cycle. The `claim↔vacancy wash` check pins the two footprints
equal to 1.7×10⁻¹⁶.

**Absorption-paced arrivals.** In-migration is capped at
`VacancyFillHazard` × feasible vacant stock, counted **per segment** over the
density each segment can legally occupy (a tower overhang cannot lease to Family
segments). Pre-cap *desired* inflow still feeds residual demand, so deferred
arrivals queue as construction pressure instead of deadlocking a young city.

**Instant co-op re-rate.** Every housed household is charged its parcel's live
market unit assessment every tick — one price per unit, bit-identical across
co-tenants, no anniversaries and no phase-in. Staleness moved from the household
to the parcel (the 1/`AssessSlices` reassessment slice), which is why co-tenant
uniformity is exact. Anti-synchronization survives by other means: the re-rate is
smooth rather than a calendar step, and relocation search is a memoryless hazard.
A one-time `GoLiveRampTicks` window, staggered per household, covers the regime
change when Tier C first starts levying so flipping the mod out of shadow mode
cannot re-rate a city in a single tick.

## The resource-level economy

An 11-resource catalog (4 raws with geology suitability fields, 5 recipe-produced
goods, services, office output), per-resource freight weights, per-(resource ×
exit) price laws, and a consumption basket driving commercial restocking. Firm
entry solves a real Weber problem — extractors argmax suitability × price over
local geology, industrials argmax recipe margin net of delivered input costs and
output haul — pinned against brute-force oracles. Transport cost attaches to
*sourcing a specific resource from a specific place*, which is what makes the
monoculture bend, the mode progression and the parity bands mean something.

## Real-map run

`harness map` runs the same economy on the Chicago Regional road network
(TNTP → `data/chicago-regional.cs2city`, 11,189 nodes / 35,436 edges, bucketed to
240 clusters): geology over real coordinates, zoning layered by access,
per-resource exits at the network edge. Deterministic; the pre-game shakedown for
the import pipeline the in-game `ClusterAccessProvider` mirrors.

## Adversarial review

Two rounds, each finding attacked by an independent refutation agent before any
fix.

**Round 1 (36 agents, core):** 24 confirmed of 30. Worst: ledger/entity
divergence (money moving without a counterparty), trade positions conflating
import and export direction, shadow accounting levying *behaviorally*,
synchronized employment epochs, a warehousing deadlock, severed tax incidence,
and extractor zones that could never develop.

**Round 2 (15 agents, the kernel + co-op diff):** 4 confirmed of 12, two HIGH —
the claim↔vacancy wash break described above, and Tier C go-live re-rating an
entire city in one tick once the anniversary machinery was removed. Also fixed:
the absorption budget counting vacancy arriving segments cannot legally occupy,
and rent-write amplification at the ECS boundary (the co-op re-rate moves every
household every tick, inverting the writer's "most writes are no-ops" premise —
now deadbanded at 1%/1 unit).

## Known gaps

- **Vacancy suppresses construction but not rent.** The kernel feeds residual
  demand, never the bid; sitting tenants pay the full presence-weighted market
  assessment however soft the local market is. Defensible under uniform
  assessment (you are assessed on the location's potential), but it means busts
  show up as stalled cranes rather than falling rents. The tractable fix is an
  occupancy-conditioned bid using the same kernel-smoothed field.
- **Money accumulates** — no dividend or treasury sink, and office output is an
  effectively infinite tap.
- **A structural unhoused pool persists**; departure response is far weaker than
  arrival response, so dying cities drain slowly.
- **Aggregation gap unquantified.** The engine is a factored approximation of the
  Shapley–Shubik assignment market (cluster × segment × level cells rather than
  person × unit pairs). Correlating it against an exact Demange–Gale–Sotomayor
  auction on a small city would bound the error; not yet done.

## Reading the numbers

The harness is a compact synthetic economy (196 clusters, ~2,000 parcels, 8–20k
households) behind the same `IAccessCosts` port the in-game adapter implements.
Architectural properties — localization ratios, rank correlations, emergent mode
splits, asymmetries, conservation — are what transfer; absolute magnitudes are
harness-scale. Several §6 demonstrations are deliberately constructed stress
scenarios, per the design's own framing of its benchmark set.

*`harness all` regenerates the two generated sections (correctness list, scenario
table) and overwrites this file — re-merge the narrative when refreshing numbers.*
