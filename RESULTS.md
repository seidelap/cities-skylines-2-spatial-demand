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
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 10 extractors (100 % on best raw); 20 single-input industrials (75 % on cheapest-sourced recipe); 3 distinct industrial outputs
- ✅ **vacancy kernel: V=1 conservation, suppression density falls with distance**: evicted 60 → total suppression 60.000000; per-cluster density within 1.5λ: 4.382 (n=9), beyond: 0.226 (n=91)
- ✅ **claim↔vacancy wash: pipeline and completed-vacant suppress identically**: 20 claimed vs 14 vacated: worst per-unit gap 1.3E-016; local retention claim 27 % vs vacancy 27 %; low-channel share claim 83 % vs vacancy 83 %
- ✅ **occupancy channel: realized vacancy softens rent (price while cleared, deeper vacancy once floored)**: FillEma tracks measured occupancy on 89 submarkets (mean err 0.000, worst 0.01); cluster 32 (cleared) bid 2.185 (fill 1.00) at full occupancy → 0.650 (fill 0.47) at 20 % (price leg)
- ✅ **clearing price: quantity responds (supply ↓, demand ↑, population ↓ — price while cleared, vacancy once flat)**: supply×{0.5,2,8} → 2.61/1.42/1.42 (fill 0.30→0.07); demand×{0.5,1,2} → 1.42/1.42/2.61 (fill 0.30→0.59); after 419 citywide exits bid 1.42 → 1.43, fill 0.59 → 0.31; 25-point supply sweep 30.17→1.42 (monotone), wages×2 → 58.23/2.20
- ✅ **occupied stock carries land rent in BOTH densities (per-kind commensurability)**: high: best-access quartile 48/48 with LR>0, ΣLR 4656.5, 191/193 overall at 99 % occupancy; low: quartile 97/97, ΣLR 456.4, 344/391 overall at 100 %
- ✅ **co-op re-rate: one price per unit, tracking the live market assessment**: 133/133 multi-tenant parcels uniform; worst |charged − market|/market = 0.0E+000
- ✅ **circularity guard: assessment blind to own realized rent**: LR 40.6463 (> 0) unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 1.60E-006
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 0 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 1706 (sane bounds), drift -2.7E-006
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=F1D4B32C3DE32E0 twice, h(seed+1)=596895E5E0C41763

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| vacancy suppression concentrates at the shock (vanilla: uniform) | ❌ | spatial: spatial-excess suppression interior 0.2/cluster vs beyond-spillover -0.2 → contrast 0.2 units/cluster (raw 2.5/1.5, level 2.3/1.7); interior unit-starts 6 concentrated vs 48 same-size uniform exits (16 no-shock); beyond 0→85 (26 treated / 64 buffered); vanilla: spatial-excess suppression interior 1.3/cluster vs beyond-spillover -0.8 → contrast 1.3 units/cluster (raw 1.3/-0.3, level -0.1/0.5); interior unit-starts 30 concentrated vs 44 same-size uniform exits (142 no-shock); beyond 434→154 (26 treated / 64 buffered) |
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.47 vs vanilla 0.01 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +51.4% vs control -11.9% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 125/tick, marginal 1.63 vs flat 2.60 → bend 37 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | migration-margin response +1267 vs departure response +1 over equal windows/pulse (2233 arrivals realized after absorption) |
| no synchronized displacement: exit times form a distribution | ✅ | 8800 exits (57 housed displaced, 8607 failed arrivals, 136 relocations), worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 63 abandoned mid-build after demand collapse; at bust 18 in flight of which 6 early-stage (243 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 127 realized-vs-predicted observations; factor range [1.00,1.16], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 4.3 ms at 8 parcels/cluster vs 5.5 ms at 16 → ratio 1.30 (cluster count fixed) |

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
quantile carrying the within-segment dispersion story, the scale the
absolute calibration. At product 0.75 the level-map Spearman fell to
0.28 and the monoculture bend to 29%; at product 1.0 they are 0.50 and
30%. (An interim cut of the quantile to 0.55, tuned against the
misdiagnosed "turnstile", halved assessments and stalled construction —
reverted once churnprobe disproved its justification.)

**Superseded**: `MarginalIncomeQuantile` has since been retired — the
within-segment dispersion it stood in for is now carried by a real income
distribution (see the last section). `BidAccessScale` = 1.33 is the whole
anchor, and it needed no re-tuning because the distribution is built at
constant mean.

## The one open target

**Vacancy localization** remains unmet, and the instrument is the
suspect: the shock arms build almost no interior starts in the
measurement window, so the triple-difference has little baseline to
difference against and the level correction dominates the contrast.
The kernel's direct dose-response evidence is discussed under the
λ-sweep section below — under the final pricing, single-seed variance
dominates and a multi-seed design is needed before the §6 "≥10:1" bar
can be treated as a verdict on the mechanism. Both the bar and the
instrument need a redesign (suppression measured against a
construction-active baseline, not a built-out one).

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

## Second adversarial review: the flat tail, hardened

A fresh review pass (three lenses, independent refuters, mutation
testing) on the flat-tail pricing confirmed six findings; all are fixed
or documented:

- **The presence≥1 gate broke the monotonicity story** (HIGH, measured):
  scaling presence down, the excess price sat flat for three orders of
  magnitude of demand mass, then jumped 1.08 → 2.69 → 4.30 → 7.34 →
  21.96 as segments crossed the gate — demand falling, price rising
  20×. The tail anchor was a pure VALUE with no mass requirement.
  Fixed: the anchor now requires a small minimum of cumulative mass
  behind it (`MinTailMass` = 0.05, a dust guard), so a segment's last
  remnant near the gate cannot anchor and its disappearance moves
  nothing. A first attempt at 0.5 was itself caught by the suite —
  real thin tranches carry mass 0.1–1, and the bigger floor skipped
  them, breaking boundary continuity and the demand ladder — which is
  why the guard is calibrated to dust, not to robustness.
- **Zero-mass tranches could anchor** (MEDIUM, measured): a
  zero-capacity cluster (every share exactly 0) still priced at 3.49
  and assessed paper LR 4.88 on a market with no demand at all. Fixed:
  zero-mass entries are filtered from the queue, and a curve with less
  than the dust threshold of total mass prices at 0.
- **Two mutants survived the previous suite** (HIGH): replacing the
  tail with `return 1.0` (price decoupled from every bidder) and with
  `wtp[n-1] * 1.05` (the reverted glut-above-scarcity inversion, in
  miniature) both passed 21/21 — the restated checks accepted a
  fill-only response wherever the price was flat. Fixed with a
  single-segment probe: in a one-segment market the demand curve is one
  flat tranche, so the cleared read and the excess anchor must be
  EXACTLY equal (kills the markup) and must differ across segments of
  different means (kills the constant). Shipped: pinned on 2 segments,
  tail spanning 3.26–5.41.
- **The occupancy check's leg preference lived only in cluster
  selection** (HIGH): the vacancy leg could still rescue a failed price
  leg on the preferred cleared cluster — the constant mutant passed
  that way. Fixed: on a cleared-selected cluster the price leg is
  required; the vacancy leg is valid only on the fallback. Shipped
  margin is now real: cluster 32 prices 1.271 → 1.006 (−21%) as
  occupancy collapses.
- **`addUnits` is a no-op in the excess regime** (MEDIUM): the flat
  tail is supply-invariant, so adding units into a glut does not lower
  the candidate's price — only its expected fill. Currently harmless
  (tail bids sit below structure cost, and construction has a separate
  absorption gate); documented as a caveat where the addUnits doc
  overstated the discipline.
- One finding was refuted by its verifier (the softens-leg tolerance
  being half-consumed at baseline) and left as-is.

Remaining known property, documented rather than engineered away: with
8 discrete segments the excess price is the WTP surface of the poorest
segment with real mass, so extinction of such a segment still re-rates
excess submarkets together (measured tranche gaps: +3% to +107%), and
the instant co-op re-rate delivers that in one tick. The structural fix
would be intra-segment WTP dispersion (each tranche a declining line,
making the demand curve strictly decreasing and the anchor move
continuously) — a design upgrade consistent with the
MarginalIncomeQuantile story, deliberately not bolted on in this pass.

## λ-sweep under the final pricing: variance now dominates

Re-run on the shipped build (single seed): 2.3:1 (λ=200m), 5.4:1
(800m, shipped), 3.3:1 (3.2km), 3.5:1 (12.8km), 3.3:1 (no kernel).
Shipped-vs-none still reads ~1.6×, but λ=200 reads BELOW no-kernel —
per-seed noise now swamps the gradient that looked monotone on earlier
builds. The honest statement: the kernel's contribution is real but
modest, and establishing (or refuting) the dose-response now requires a
multi-seed design; the single-seed table is no longer evidence of a
monotone gradient.

## Segments carry an income distribution, built from CS2's own income model

The discrete-jump behavior the previous pass documented — the excess price
being the poorest *segment's* WTP, so a segment's disappearance re-rated
every glut submarket at once — was not a harness artifact. `Segment.All`
ships in Core, and the mod adapter (`ResourceMap.SegmentFor`) folds CS2's
5 education tiers × 4 age groups many-to-one into those 8 buckets. The
discreteness was ours, imposed on a game whose population is smoother
than our representation of it — and the adapter was already reading the
smoothness and discarding it (earner count collapsed to a bool, job level
unread, household savings stored but unused for pricing).

Each segment now carries a within-segment household income
**distribution** (`Income.cs`), built from the fields CS2 actually
exposes (`Game.Prefabs.EconomyParameterData`, research notes §3):

- `m_Wage0..m_Wage4` — wages by **job level**, i.e.
  `Game.Citizens.Worker.m_Level`, the job actually held rather than the
  citizen's education. Over-qualification is structural in CS2 because
  `FreeWorkplaces` is per-education-tier and runs out.
- `m_UnemploymentBenefit` — what a working-age adult with no job
  receives. Previously **missing entirely**, which is why unemployed
  single households had an income of exactly zero and the clearing price
  needed a hand-written zero-WTP filter.
- `m_ResidentialMinimumEarnings` — floor under household earnings.
- Earner count 0..Adults, from the household's actual `Worker` members.

Measured distributions (seed 20260806, t=300, best-stocked cluster):

| segment | adults | mean | spread | bins (income @ share) |
|---|---|---|---|---|
| StudentLow | 1 | 7.89 | 2.9× | 12.9 11.6 6.0 4.5 4.5 |
| SingleBasic | 1 | 8.43 | 1.9× | 9.9 9.9 9.9 7.3 5.3 |
| SingleSkill | 1 | 10.97 | 5.6× | 16.9 16.6 12.1 6.2 3.0 |
| FamilyBasic | 2 | 10.79 | 4.8× | 19.1 12.4 11.5 6.9 4.0 |
| FamilySkill | 2 | 13.05 | 6.4× | 25.8 18.9 12.6 4.0 4.0 |
| FamilyEdu | 2 | 24.85 | 12.3× | 49.2 31.0 26.5 13.5 4.0 |
| SeniorLow | 0 | 8.00 | 1.0× | pension only |
| SeniorMid | 0 | 13.00 | 1.0× | pension only |

The within-segment spread (up to 12×) now exceeds the between-segment
spread, which is the point: it was always there in the population and the
model was averaging it away. The demand curve at a cluster went from 8
steps to **40 strictly-decreasing tranches**, so the clearing price slides
continuously with quantity instead of jumping when a segment leaves the
queue.

**Dispersion added at constant mean.** The job-level ladder is normalized
so its weighted mean is exactly `Wage(class)`, and per-adult
`Participation` is halved for the 2-adult Family segments so household
labor supply is unchanged. Nothing calibrated on the aggregates moved:
the same fixture reads supply×{0.5,2,8} → 2.61/1.42/1.42 against
2.71/1.36/1.36 before. What did change is the **slope** — a real
distribution makes the curve much steeper, so scarcity bites harder and
gluts price softer than a point estimate allowed.

`MarginalIncomeQuantile` is **retired**. It was a scalar ("the marginal
member earns ~75% of the segment mean") standing in for exactly this
distribution; the marginal bidder is now found by walking the real curve.
Its calibration history is the cautionary tale: it was cut 0.75 → 0.55 to
damp an emigration "turnstile" that churnprobe later showed was an
absorption-budget bug, and the cut had meanwhile halved assessments and
stalled construction. A parameter standing in for a missing mechanism
attracts exactly that kind of misattributed tuning.

**A real bug this surfaced.** Paying per-earner while the labor market
still counted one worker per household double-charged every firm's wage
bill and killed all industry (Weber: 0 industrials). Labor supply is now
`Adults × Participation` — the same quantity the wage income is paid on —
so the matched-jobs wage bill and the wages paid stay in balance.

**Effects measured.** Housed insolvency displacement fell from 54 to **9**
per 300 ticks: poor households are no longer charged against a segment
mean they never earned, and non-earning adults now receive the benefit
CS2 pays them. Population 4,112.

**Check strengthened.** The mutant-killing "single-segment glut ==
scarcity price" assertion assumed one flat tranche per segment — true only
while a segment was one point income. It is replaced by a 25-point supply
sweep that must be monotone non-increasing across the cleared→excess
transition (an excess markup shows up as an upward step), must decline
substantially end to end, and must scale with wages. Shipped: sweep
30.17 → 1.42 monotone, wages×2 → 58.23/2.20.

**What CS2 does NOT give us**, checked rather than assumed: there is no
per-citizen wage field. Wages come from the `m_Wage0..m_Wage4` ladder
indexed by worker level, so our 3-class table still supplies the level;
the dispersion comes from earners × employment × job level. And the other
per-citizen statistics (`m_WellBeing`, `m_Health`, `m_LeisureCounter`) are
outcomes of location quality feeding `CitizenHappinessSystem`, not
preference parameters — the preference axes CS2 actually differentiates on
are education and age group, both of which the segment table already
carries. One `VERIFY-INGAME` marker was added: `Worker.m_Level` is
dump-listed as a byte but its range (0..4 job level vs a progress counter)
and whether the wage is indexed by it rather than by education both need a
decompile check on the game machine.
