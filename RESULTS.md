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
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 10 extractors (100 % on best raw); 15 single-input industrials (80 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 1.8E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **occupancy channel: realized vacancy softens rent (price while cleared, deeper vacancy once floored)**: FillEma tracks measured occupancy on 89 submarkets (mean err 0.001, worst 0.03); cluster 50 bid 0.808 (fill 0.50) at full occupancy → 0.808 (fill 0.20) at 20 % (vacancy leg)
- ✅ **clearing price: quantity responds (supply ↓, demand ↑, population ↓ — price while cleared, vacancy once flat)**: supply×{0.5,2,8} → 2.33/1.24/1.24 (fill 0.30→0.07); demand×{0.5,1,2} → 1.24/1.24/2.33 (fill 0.30→0.59); after 420 citywide exits bid 1.24 → 1.30, fill 0.59 → 0.31
- ✅ **occupied stock carries land rent in BOTH densities (per-kind commensurability)**: high: best-access quartile 50/50 with LR>0, ΣLR 3994.4, 196/203 overall at 99 % occupancy; low: quartile 94/96, ΣLR 327.1, 306/384 overall at 100 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 133/133 multi-tenant parcels uniform; worst |charged − market|/market = 2.7E-004
- ✅ **circularity guard: assessment blind to own realized rent**: LR 35.9090 (> 0) unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 5.10E-007
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 3 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 1699 (sane bounds), drift -1.8E-006
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=56D326255265C9BA twice, h(seed+1)=A6EBC104F59E17D2

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy suppression concentrates at the shock (vanilla: uniform) | ❌ | spatial: spatial-excess suppression interior 2.1/cluster vs beyond-spillover -0.9 → contrast 2.1 units/cluster (raw 4.2/0.9, level 2.1/1.7); interior unit-starts 0 concentrated vs 0 same-size uniform exits (37 no-shock); beyond 60→1 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 3.2/cluster vs beyond-spillover -1.2 → contrast 3.2 units/cluster (raw 2.0/-0.3, level -1.2/0.9); interior unit-starts 14 concentrated vs 58 same-size uniform exits (246 no-shock); beyond 470→142 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.51 vs vanilla 0.03 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +90.7% vs control -33.5% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 126/tick, marginal 1.75 vs flat 2.60 → bend 33 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | migration-margin response +1156 vs departure response +1 over equal windows/pulse (2184 arrivals realized after absorption) |
| no synchronized displacement: exit times form a distribution | ✅ | 8991 exits (141 housed displaced, 8641 failed arrivals, 209 relocations), worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 36 abandoned mid-build after demand collapse; at bust 29 in flight of which 5 early-stage (163 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 124 realized-vs-predicted observations; factor range [0.95,1.09], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 3.7 ms at 8 parcels/cluster vs 4.3 ms at 16 → ratio 1.16 (cluster count fixed) |

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

## The one open target

**Vacancy localization** remains unmet, and the honest reading changed
with the audit. The instrument is degenerate on this build: the shock
arms build zero interior starts in the measurement window, so the
triple-difference has no baseline to difference against and the level
correction dominates the contrast. (The monoculture bend, which briefly
failed the ≥30% inequality at the fourth decimal under the interim
calibration, reads 33% again.) The direct dose-response experiment
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
built-out one) before the number is treated as a verdict on the
mechanism.

## Excess-supply pricing: the revenue-max rule, measured to its stable form

The proposal: instead of special-casing worthless bidders, price each
submarket at the fill that maximizes total rent collected — n* =
argmax n × P(n), "4 × $6 beats 5 × $4, so with 5 units we fill 4." Three
forms were implemented and measured on the same seeds:

1. **Full strength (revenue-max in all regimes).** The demand curve's top
   tranche is flat (one segment's WTP), so the argmax rides it to its edge
   and every submarket prices at its RICHEST tranche: bid 22.25 invariant
   to supply×8, demand×2 and 397 exits; 2,187 units held vacant; housed
   insolvency evictions ×20; one segment (FamilyBasic) driven extinct.
   This is a cartel — and the measurement is exactly why competition
   between parcel owners makes it a non-equilibrium wherever the stock can
   actually fill: an owner holding at 22 while the market clears at 6 is
   undercut by a neighbor who steals the tenant.
2. **Two regimes, unconstrained (clearing while stock fills, revenue-max
   in excess).** Withholding is uncontested in a glut — nobody is coming
   for the marginal unit — but the unconstrained revenue point sits HIGH
   on the curve, so tipping into excess jumped the price UP: supply×2
   priced at 2.71 against 1.77 at supply×0.5, and rents ROSE as
   population fled. More supply must never raise price.
3. **Flat tail (shipped).** Any excess price above the cleared-boundary
   price breaks supply-monotonicity, and that boundary price is the
   deepest positive bidder's WTP — so "revenue-max capped for
   monotonicity" collapses to a clean rule: in excess supply the price
   floors FLAT at the marginal positive bidder and does not decay chasing
   demand that does not exist (cutting below the last real bidder buys no
   tenant — pure revenue loss, which is the revenue-max argument at the
   one point it binds monotonically). Zero-WTP tranches cannot drag the
   floor down: they are filtered from the queue (a bidder at zero is not
   demand). The shortfall surfaces as VACANCY, reported by the new
   fillRatio output of ResidentialBidPerUnit.

Measured consequences of the shipped rule (same seeds as above):

- verify 21/21. The two price checks were restated to the two-regime
  economics and now read it directly: supply×{0.5,2,8} → 2.33/1.24/1.24
  with expected fill 0.30→0.07 (price carries the response while cleared,
  vacancy once floored); demand×{0.5,1,2} → 1.24/1.24/2.33 with fill
  0.30→0.59; after 420 citywide exits the price drifts 1.24→1.30
  (composition of the remaining tail tranche) while expected fill falls
  0.59→0.31 — the quantity margin is the real response.
- churnprobe: pop 4,049 (higher than under the old decay rule), 41 housed
  insolvency exits per 300 ticks, 6 relocations, vacancy 1/17 units L/H —
  the settled city is stable and the floor did not re-open a turnstile.
- The old proportional-decay branch (price → 0 chasing absent demand) and
  its zero-anchor pathology are gone; an emptied submarket now bottoms
  out at its poorest real bidder's WTP instead of zero, which is also a
  saner refill incentive.
