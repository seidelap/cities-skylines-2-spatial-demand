# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs
against the vanilla-baseline mode on identical seeds.
Seed: 20260806.

## Correctness verification (21 checks, 21 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35 (clear/brute per exit: e0:150/150 e1:175/175 e2:275/275)
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 11 extractors (100 % on best raw); 14 single-input industrials (86 % on cheapest-sourced recipe); 4 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 1.3E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **occupancy channel: realized vacancy softens rent at fixed population**: FillEma tracks measured occupancy on 96 submarkets (mean err 0.017, worst 0.37); cluster 47 bid 2.424 at full occupancy → 0.516 at 20 % (−79 %)
- ✅ **clearing price: rent responds to quantity (supply ↓, demand ↑, population ↓)**: supply×{0.5,2,8} → 0.65/0.32/0.08; demand×{0.5,1,2} → 0.32/0.65/2.52; after 421 citywide exits bid 0.65 → 0.36
- ✅ **occupied stock carries land rent in BOTH densities (per-kind commensurability)**: high: best-access quartile 48/48 with LR>0, ΣLR 3794.7, 129/194 overall at 99 % occupancy; low: quartile 110/110, ΣLR 857.2, 220/442 overall at 99 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 133/133 multi-tenant parcels uniform; worst |charged − market|/market = 0.0E+000
- ✅ **circularity guard: assessment blind to own realized rent**: LR 0.0000 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.85E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 22 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 1730 (sane bounds), drift -1.7E-006
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=F43F273383A19FA twice, h(seed+1)=BE9766F033469F14

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy localization ≥10:1 (vanilla ≈1:1) | ❌ | spatial: spatial-excess suppression interior 0.7/cluster vs beyond-spillover -0.3 (raw 3.1/2.0, level effect 2.4/2.3) → 7:1; interior unit-starts 70 concentrated vs 157 same-size uniform exits (57 no-shock); beyond 114→186 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 3.1/cluster vs beyond-spillover -0.7 (raw 5.4/1.7, level effect 2.3/2.4) → 31:1; interior unit-starts 150 concentrated vs 154 same-size uniform exits (192 no-shock); beyond 402→478 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.48 vs vanilla 0.01 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +84.9% vs control -39.2% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 133/tick, marginal 1.74 vs flat 2.60 → bend 33 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | migration-margin response +1220 vs departure response +1 over equal windows/pulse (2945 arrivals realized after absorption) |
| no synchronized displacement: exit times form a distribution | ✅ | 9748 displacement exits, worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 46 abandoned mid-build after demand collapse; at bust 8 in flight of which 3 early-stage (294 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 149 realized-vs-predicted observations; factor range [0.77,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 2.7 ms at 8 parcels/cluster vs 3.6 ms at 16 → ratio 1.34 (cluster count fixed) |


## Current open target: vacancy localization, and a suspect instrument

`vacancy localization` measures **7:1** against a 10:1 bar — but the number to
look at is the control: the **vanilla arm scores 31:1**. Vanilla drives
construction from a single global scalar with no geography at all, so it cannot
localize anything; a vanilla arm out-scoring the spatial arm means the
*instrument*, not the mechanism, is what moved.

The likely cause is arithmetic fragility rather than economics. The statistic is
a triple difference divided by a near-zero denominator
(`spatialIn / max(0.05, spatialFar)`), and raising the price level roughly
doubled every quantity feeding it: raw suppression interior 3.1 vs beyond 2.0
with a level effect of 2.4/2.3 nearly cancelling both, leaving 0.7 against −0.3.
Two large numbers differencing to a small one, over a floored denominator, is not
a measurement that should be trusted in either direction — including the 90:1 and
110:1 it reported before.

This is recorded as open rather than tuned. The honest next step is to rebuild
the statistic on a ratio that does not divide by a difference of differences —
e.g. regressing per-cluster suppression on distance-to-shock and reporting the
decay coefficient with a standard error, which degrades gracefully instead of
exploding when the denominator approaches zero.

## Known regression risk from the price-level change

Raising `BidAccessScale` 0.47 → 1.0 (removing a double discount that
marginal-bidder clearing already provides) is theoretically motivated and
recovered the level-map target — Spearman 0.32 → **0.48** against vanilla 0.01,
its best measured value — but it roughly doubles rents citywide and visibly
changed the regime everywhere else (displacement exits 186 → 9,748). Every other
target still passes, and verify is 21/21, but a change of that reach deserves
the adversarial pass that is running against it rather than a clean bill of
health from the suite alone.
