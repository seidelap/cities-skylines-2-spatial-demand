# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs
against the vanilla-baseline mode on identical seeds.
Seed: 20260806.

## Correctness verification (20 checks, 19 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35 (clear/brute per exit: e0:150/150 e1:175/175 e2:275/275)
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 12 extractors (100 % on best raw); 15 single-input industrials (87 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.400 (n=9), beyond: 0.224 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 3.3E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **clearing price: rent responds to quantity (supply ↓, demand ↑, population ↓)**: supply×{0.5,2,8} → 0.83/0.32/0.08; demand×{0.5,1,2} → 0.32/0.83/3.86; after 410 citywide exits bid 0.83 → 0.25
- ❌ **occupied stock carries land rent in BOTH densities (per-kind commensurability)**: high 6/152 parcels with LR>0 at 100 % occupancy; low 360/511 at 83 % — failing on purpose, see "The one red check" below
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 123/123 multi-tenant parcels uniform; worst |charged − market|/market = 2.2E-003
- ✅ **circularity guard: assessment blind to own realized rent**: LR 0.5600 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.46E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 26 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 704 (sane bounds), drift -1.3E-007
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=BDBD923C025E6C5A twice, h(seed+1)=7E84DF3DD51AC565

## Acceptance scenarios (10 targets, 10 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy localization ≥10:1 (vanilla ≈1:1) | ✅ | spatial-excess suppression interior 8.7/cluster vs beyond-spillover −2.4 → **87:1**; interior unit-starts 0 concentrated vs 3 uniform vs 6 no-shock |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.42 vs vanilla −0.11 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +57.5% vs control −42.5% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 141/tick, marginal 1.29 vs flat 2.60 → bend 51 % |
| truck→rail→backstop progression; concurrent marginals equalize | ✅ | ramp 8→950/tick: road 72 %→22 %; rail 28 %→59 %; sea 30 % at top; marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | arrival response +53 vs departure response +1 |
| no synchronized displacement: exit times form a distribution | ✅ | 2000 exits, worst single tick 0.4 % |
| stalled construction appears in engineered busts | ✅ | 33 abandoned mid-build on the HOUSEHOLD-demand channel alone; at bust 6 in flight, 4 early-stage (237 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 175 observations; factor range [0.74,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | 2.8 ms at 8 parcels/cluster vs 3.3 ms at 16 (cluster count fixed) |


## The one red check, and why it stays red

`occupied stock carries land rent in BOTH densities` **fails**: high-density
parcels are 6/152 carrying rent at 100% occupancy. It is failing on a real
defect that the market-clearing price *exposed* rather than caused, and it is
left red on purpose rather than patched.

`Segment.DensityTolerance` is documented as a 0–1 **preference** ("1 = happy in
ResidentialHigh"), but `AllocationSystem.DensityFeasible` uses it as a hard
**permission** gate at ≥0.5. Every Family segment (0.28–0.35) is therefore
barred from apartments outright, so high-density stock (1,824 units) structurally
exceeds the population allowed to occupy it (1,263 density-tolerant households)
and the high-density bidder queue can never clear. Meanwhile the allocator and
the synthetic seeding fill those units anyway — 1,824/1,824 occupied — so
allocation and pricing disagree about who may live where.

The fix is to treat tolerance as a willingness-to-pay discount rather than an
exclusion, in **both** allocation and pricing. That is a real economic change
touching allocation everywhere, and it wants its own review pass.

## Adversarial review, round 3 (the clearing price)

24 agents, four lenses, each finding attacked by an independent refuter:
**8 confirmed of 20**. It caught a regression the whole suite was blind to.

- **Three HIGH, one root cause.** Demand mass was a per-*cluster* share compared
  against per-*kind* stock, so every density-tolerant household was counted at
  full weight in the low queue *and again* in the high queue while each faced
  only its own stock. The high queue could never reach its stock: every
  apartment building priced as a permanent overhang at 100% occupancy and all
  high-density land rent went to exactly zero (152/152 towers; land revenue
  −30%; reproduced on three seeds). Fixed by normalizing the share over kind
  **and** cluster jointly and weighting it by capacity rather than access alone.
- **Three HIGH + one medium: the tests were theater.** Replacing the entire
  access-logit share array with a constant, or tripling `HousingStock`, both
  still passed 19/19 — price depends only on the mass/supply *ratio*, so any
  uniform rescaling is invisible. The "vacancy overhang softens rent" leg
  created no vacancy and merely re-measured the population leg. And
  `StalledConstruction`'s `OfficeOutputPrice` cut was a goalpost move: the poke
  *alone* produced abandonments with no exodus at all.
- **Aftermath.** The poke is reverted and the target now passes on the
  household-demand transmission alone (33 abandonments), which the broken
  pricing had been suppressing. `OccupiedStockCarriesRent` was added as the
  guard that would have caught the core bug.

## Correction to an earlier claim

An earlier revision of this file said the clearing price made a vacancy overhang
soften rent. **That was wrong.** `ResidentialBidPerUnit` reads segment presence,
an access/capacity share, and total *built* stock — none of which is an
occupancy term — so a cluster emptying out at fixed citywide population does not
by itself soften its rent. The price responds to **population** and to **stock**,
which is a real and useful quantity channel, but the occupancy channel remains
an open gap (see Known gaps).
