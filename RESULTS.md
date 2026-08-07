# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs
against the vanilla-baseline mode on identical seeds.
Seed: 20260806.

## Correctness verification (18 checks, 18 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35 (clear/brute per exit: e0:150/150 e1:175/175 e2:275/275)
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 9 extractors (100 % on best raw); 19 single-input industrials (63 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 1.7E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 114/114 multi-tenant parcels uniform; worst |charged − market|/market = 3.2E-004
- ✅ **circularity guard: assessment blind to own realized rent**: LR 1.5565 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.83E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 25 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 715 (sane bounds), drift -1.7E-008
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=B2F9514ED8C501B7 twice, h(seed+1)=8C452D656913CE0F

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy localization ≥10:1 (vanilla ≈1:1) | ❌ | spatial: spatial-excess suppression interior 4.4/cluster vs beyond-spillover -1.0 (raw 7.6/1.2, level effect 3.2/2.2) → 44:1; unit-starts interior 7→18, beyond 49→51 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 7.4/cluster vs beyond-spillover -1.9 (raw 9.0/-0.7, level effect 1.6/1.2) → 74:1; unit-starts interior 118→0, beyond 390→0 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.40 vs vanilla -0.01 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +27.5% vs control -5.4% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 168/tick, marginal 1.32 vs flat 2.60 → bend 49 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | arrival response +23 vs departure response +1 over equal windows/pulse |
| no synchronized displacement: exit times form a distribution | ✅ | 5626 displacement exits, worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 7 projects abandoned mid-build after demand collapse (261 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 202 realized-vs-predicted observations; factor range [0.70,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 3.5 ms at 8 parcels/cluster vs 4.1 ms at 16 → ratio 1.17 (cluster count fixed) |

