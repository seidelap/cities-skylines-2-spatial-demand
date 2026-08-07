# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs
against the vanilla-baseline mode on identical seeds.
Seed: 20260806.

## Correctness verification (17 checks, 17 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35 (clear/brute per exit: e0:150/150 e1:175/175 e2:275/275)
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 11 extractors (100 % on best raw); 10 single-input industrials (90 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 111/111 multi-tenant parcels uniform; worst |charged − market|/market = 3.0E-004
- ✅ **circularity guard: assessment blind to own realized rent**: LR 1.5964 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.80E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 40 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 695 (sane bounds), drift 3.4E-008
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=FB8E833D70A915C1 twice, h(seed+1)=BEB64F2130C151ED

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy localization ≥10:1 (vanilla ≈1:1) | ✅ | spatial: spatial-excess suppression interior 4.7/cluster vs beyond-spillover -1.3 (raw 7.9/1.1, level effect 3.2/2.4) → 47:1; unit-starts interior 5→5, beyond 26→29 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 8.2/cluster vs beyond-spillover -2.1 (raw 10.6/-0.1, level effect 2.4/1.9) → 82:1; unit-starts interior 108→0, beyond 176→0 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.41 vs vanilla 0.04 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +30.2% vs control -1.1% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ❌ | sustained 68/tick, marginal 2.03 vs flat 2.60 → bend 22 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | arrival response +23 vs departure response +1 over equal windows/pulse |
| no synchronized displacement: exit times form a distribution | ✅ | 4039 displacement exits, worst single tick 0.4 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 38 projects abandoned mid-build after demand collapse (282 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 201 realized-vs-predicted observations; factor range [0.70,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 3.9 ms at 8 parcels/cluster vs 4.2 ms at 16 → ratio 1.09 (cluster count fixed) |

