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
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 14 extractors (100 % on best raw); 15 single-input industrials (87 % on cheapest-sourced recipe); 4 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 2.0E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **occupancy channel: realized vacancy softens rent at fixed population**: FillEma tracks measured occupancy on 89 submarkets (mean err 0.004, worst 0.20); cluster 50 bid 0.402 at full occupancy → 0.161 at 20 % (−60 %)
- ✅ **clearing price: rent responds to quantity (supply ↓, demand ↑, population ↓)**: supply×{0.5,2,8} → 2.47/0.40/0.10; demand×{0.5,1,2} → 0.40/0.80/2.47; after 420 citywide exits bid 0.80 → 0.41
- ✅ **occupied stock carries land rent in BOTH densities (per-kind commensurability)**: high: best-access quartile 49/49 with LR>0, ΣLR 4224.0, 126/198 overall at 99 % occupancy; low: quartile 93/96, ΣLR 267.2, 115/387 overall at 100 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 132/132 multi-tenant parcels uniform; worst |charged − market|/market = 0.0E+000
- ✅ **circularity guard: assessment blind to own realized rent**: LR 35.4398 (> 0) unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 1.17E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 21 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 1703 (sane bounds), drift -2.3E-006
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=95484797461D8852 twice, h(seed+1)=332D72E58216BEE

## Acceptance scenarios (10 targets, 8 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy suppression concentrates at the shock (vanilla: uniform) | ❌ | spatial: spatial-excess suppression interior 0.6/cluster vs beyond-spillover -0.5 → contrast 0.6 units/cluster (raw 3.1/1.3, level 2.4/1.8); interior unit-starts 12 concentrated vs 24 same-size uniform exits (0 no-shock); beyond 12→28 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 2.8/cluster vs beyond-spillover -1.0 → contrast 2.8 units/cluster (raw 3.6/-0.4, level 0.8/0.6); interior unit-starts 56 concentrated vs 90 same-size uniform exits (170 no-shock); beyond 526→256 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.50 vs vanilla 0.03 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +98.6% vs control -22.2% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ❌ | sustained 116/tick, marginal 1.82 vs flat 2.60 → bend 30 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | migration-margin response +1008 vs departure response +1 over equal windows/pulse (2199 arrivals realized after absorption) |
| no synchronized displacement: exit times form a distribution | ✅ | 9068 exits (146 housed displaced, 8731 failed arrivals, 191 relocations), worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 14 abandoned mid-build after demand collapse; at bust 29 in flight of which 3 early-stage (134 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 120 realized-vs-predicted observations; factor range [1.00,1.09], late swing 0.01 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 3.1 ms at 8 parcels/cluster vs 3.1 ms at 16 → ratio 1.03 (cluster count fixed) |

## What the audit changed (and what it caught)

An adversarial review of the clearing-price work (the commits that removed
cluster smoothing and priced at the marginal prospective renter) confirmed
four defects, each measured by A/B before fixing:

1. **Density appeal applied twice.** The apartment discount entered on the
   WTP leg *and* inside the jointly-normalized demand share, where the
   renormalization turned it into a +40% subsidy for houses. Appeal now
   prices the bid only; the share stays kind-neutral.
2. **Zero-income segments anchored prices.** The excess-supply branch read
   the deepest queued bidder, and a segment with zero expected income at a
   cluster has WTP exactly 0 — 41 fully-let parcels carried exactly zero
   land rent through that anchor. Zero-WTP entries are no longer bidders.
3. **A vacuous density gate.** The construction lean tested
   `DensityAppeal ≥ 0.5`, which the 0.6 floor makes true for every segment:
   all seeker mass leaned high-density (75% of construction demand to
   towers) and the low-density branch was dead code. The gate reads the raw
   tolerance again.
4. **A check that asserted 0 == 0.** The circularity guard sampled the
   first occupied parcel, usually at the Ricardian margin where LR and
   wedge are both floored at zero. It now pins the largest-LR parcel and
   requires it positive (LR 35.4 in the shipped run).

Pricing semantics were also aligned with the original rule: the price is
read at the *excluded challenger* (`ClearingBand` = 0.10 above supply), so
every sitting tenant keeps strictly positive surplus, and candidate
configurations price their own units into the supply they must fill
(`addUnits`/`minSupply`) instead of assuming they displace standing stock.

## The churn investigation: a misdiagnosis, instrumented and corrected

The nosync scenario's ~9–10k "displacement exits" looked like an
insolvency conveyor — households priced out by market-clearing rents. It
was not. Reason-tagged exit telemetry (`harness churnprobe`) splits the
exits three ways; in the shipped nosync run of 9,068 exits:

- **housed residents displaced by insolvency: 146** (~1% of the housed
  population over 700 ticks) — the settled city is stable;
- voluntary relocations: 191;
- **failed arrivals: 8,731** — migrants admitted at the border who waited
  out their patience (≈54 ticks) without landing a unit and left, never
  having been housed.

The revolving door at the border is the honest cost of a standing queue the
market can see: queued arrivals are counted in the pricing presence, where
they hold clearing prices up and feed the residual demand that makes
construction pencil. Two attempts to engineer the bounce away were tried
and **reverted, with the A/B evidence recorded in code comments**:

- A Little's-law congestion gate on admissions eliminated most bouncing
  but froze the city (starts 265 → 41; commercial and industrial
  construction to zero): the queue is load-bearing.
- A Markov employment chain (rare separations, search hazards) fixed a
  problem that did not exist — the churn was never spell-bankrupted
  tenants — and broke labor-market tracking during warm-up: seeded at the
  cold-start rate it converged at only ~0.3/epoch, firms ran unstaffed
  (industrial revenue/firm 19 vs 171 at t=50), and every seeded industrial
  firm died (the transient 0-industrials Weber failure).

What stayed: the absorption budget's churn term is now
max(measured freed-unit EMA, turnover prior) — the measurement lets a
post-shock market reopen wider than the prior, the prior keeps a saturated
no-churn city bootstrappable; migration's distress signal counts
insolvency-stage households at half weight (funded emigration used to exit
the distressed before the homeless share ever saw them); and exit telemetry
distinguishes displacement from failed arrival everywhere it is reported.

## Price-level calibration, stated plainly

The review fixes shifted the mean clearing bid about −25% at unchanged
parameters. They are mechanism corrections, not price-level decisions, so
the level was re-anchored where the §6 targets were validated:
`BidAccessScale (1.33) × MarginalIncomeQuantile (0.75) = 1.0` — the
quantile carries the within-segment dispersion story, the scale carries
the absolute calibration. At product 0.75 the level-map Spearman fell to
0.28 and the monoculture bend to 29%; at product 1.0 they are 0.50 and
30%. (An interim cut of the quantile to 0.55, tuned against the
misdiagnosed "turnstile", halved assessments and stalled construction —
reverted once churnprobe disproved its justification.)

## The two open targets

**Monoculture bend** reads 30% against a ≥30% bar and fails the strict
inequality at the fourth decimal (marginal 1.82 vs flat 2.60). Reported as
a miss rather than rounded into a pass; sustained export volume (116/tick)
sits slightly below the historical 133/tick under the corrected economy.

**Vacancy localization** remains unmet, and the honest reading changed
with the audit. The instrument is degenerate on this build: the no-shock
arm builds **zero** interior starts in the measurement window, so the
triple-difference has no baseline to difference against, and the level
correction dominates the contrast. The direct dose-response experiment
(`harness lambdasweep` — same disk shock, only the kernel reach λ varies)
on the corrected economy:

| λ (m) | interior suppression | beyond spillover | localization |
|---|---|---|---|
| 200 | 3.36 | 0.84 | 4.0:1 |
| 800 (shipped) | 3.45 | 0.83 | 4.2:1 |
| 3,200 | 2.73 | 0.84 | 3.3:1 |
| 12,800 | 2.87 | 1.20 | 2.4:1 |
| ∞ (no kernel) | 2.80 | 1.07 | 2.6:1 |

The kernel still contributes — localization falls ~1.6× when it is
removed, and the gradient is monotone through map scale — but the
dramatic ratios measured before the audit (314:1 at λ=200) were partly
artifacts of the pricing bugs this pass fixed. Under honest prices the
mechanism is real and modest, and the §6 "≥10:1" bar is not met on this
instrument. Both the bar and the instrument deserve a redesign
(suppression measured against a construction-active baseline, not a
built-out one) before the number is treated as a verdict on the mechanism.
