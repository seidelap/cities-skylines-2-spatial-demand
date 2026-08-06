# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a 4-core Linux container,
.NET 8, single machine. Correctness checks are deterministic; acceptance scenarios
follow the design's §6 targets, several as A/Bs against the vanilla-baseline mode on
identical seeds. Seed: 20260806.
Full design: [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md) ·
plan: [`PLAN.md`](PLAN.md).

## Headline results

| design claim | measured |
|---|---|
| §2(c) construction/leveling respond to residual submarket demand | realized level map rank-correlates with ℓ* at **0.36** (vanilla grind: 0.04); construction spawns at the residual-maximizing level; 6 projects abandoned mid-build in the engineered bust |
| §2(d) Georgist core closes the fiscal loop | a transit corridor raises land-rent revenue along itself **+42%** within 60 ticks (control row +9.5%); circularity guard holds bit-exactly under a 17.5× realized-rent perturbation |
| §2(e) finite-depth trade kills export spam | sustained monoculture bends the marginal received price **41% below** the flat-price baseline at equal volume; truck→rail→backstop progression emerges by volume (road 75% → rail 59% → sea 30%) with concurrent marginal prices equalized to **0.0%** |
| §2(a) migration margin with boom/bust asymmetry | symmetric amenity pulse: arrival response **+2,178** vs departure response **+86** over equal windows — inflow fast, outflow slow (attachment) |
| §3 anti-synchronization | 25,235 displacement exits with the worst single tick carrying **0.3%** (a synchronized cliff would be ≫5%); assessment anniversaries uniform |
| money conservation | double-entry ledger drift ≤ **4×10⁻⁶** over 300 ticks across all scenario economies |

## Correctness verification (15 checks, all passing)

- ✅ **IPF: marginals respected** — max row/col violation 0.0
- ✅ **IPF: two-sided consistency** — |jobsClaimed − workersClaimed| = 1.1×10⁻¹³
- ✅ **annuity operator round-trips** — the single lump↔flow bridge is exact
- ✅ **supported level ℓ\* is an interior optimum rising with access** — ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**
- ✅ **multimodal composition = horizontal summation** — clearing equals brute-force marginal merge to 10⁻⁶
- ✅ **transient impact layer decays at resilience rate**
- ✅ **assessment anniversaries uniform, never synchronized (§3)** — 20k households, bucket range [616,716] vs mean 667
- ✅ **circularity guard: assessment blind to own realized rent** — LR bit-identical under 17.5× perturbation
- ✅ **ledger conservation** — max |drift| 4.1×10⁻⁶ over 300 ticks
- ✅ **shadow accounting: assessed + logged, nothing levied** — stage-3 contract, behaviorally inert
- ✅ **insolvency pipeline: staged, ordered, terminates** — Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population** — vanilla truncated-queue bug-class regression
- ✅ **feature flags: vanilla-mode fallback runs** — every tier revertible, sane population bounds
- ✅ **determinism** — same seed → identical telemetry hash

## Acceptance scenarios (§6 targets: 9 of 10 passing)

| §6 target | result | measured |
|---|---|---|
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.36 vs vanilla 0.04 over 1300 ticks |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +42.2% vs control +9.5% within 60 ticks of the express links landing |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 173/tick; marginal 1.54 vs flat 2.60 → 41% bend |
| truck→rail→backstop progression; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 75% at low volume → 22% at ≥600; rail 25%→59% at mid; sea 30% at top; best concurrent marginal gap 0.0% |
| boom/bust asymmetry | ✅ | symmetric amenity pulse: +2,178 arrivals vs +86 departures over equal windows |
| no synchronized displacement | ✅ | 25,235 exits, worst single tick 0.3% of the total |
| stalled construction appears in engineered busts | ✅ | 6 projects abandoned mid-build after demand collapse (461 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 305 realized-vs-predicted observations; factors within [0.70,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | 5.2 ms → 9.1 ms when parcels double at fixed cluster count (matrix work dominates) |
| vacancy localization ≥10:1 (vanilla ≈1:1) | ❌ | not yet demonstrated — see below |

**On the open target.** The mechanism §6 asks about is verified at the unit level:
vacancies subtract from residual demand, absorption has no floor that lets bid
strength override a dead submarket, and the claims ledger meters the pipeline. What
the scenario could not yet produce is the *regime* the experiment presumes — a
soft-landed city with steady marginal residential construction. The harness's
current calibration yields either a growth boom (a shock of ~700 vacancies is
swamped by a several-thousand-seeker inflow; suppression ratio ≈ 1 in both modes)
or a mature stall (zero baseline starts to shift, because standing vacancies plus
the land charge floor price out the marginal seeker — construction for the poor
does not clear the phantom developer's hurdle, which is itself faithful economics).
Reaching the mid-growth equilibrium is a calibration project (Tier A pacing against
absorption), not a missing mechanism; the A/B scaffolding (shock-vs-control on
identical seeds, scrape-events excluded from the start metric) is in place for it.

## Adversarial review

After the suite first went green-ish, an adversarial workflow (36 agents: six
review lenses — land accounting, ledger closure, trade, migration/allocation,
construction/leveling, anti-synchronization — each finding then attacked by an
independent refutation agent) confirmed **24 findings** (6 refuted). All confirmed
findings were fixed except one accepted-and-documented harness simplification
(fixed-cadence Tier B refresh; in-game it rides the routing rebuild's dirty flags).
The worst:

- **Ledger/entity divergence** — wage bills and captured consumer spending could
  move in the ledger without a real counterparty entity (now rerouted to the
  outside world when no firm can pay/receive).
- **Trade positions conflated directions** — imports depressed export prices
  through a shared sustained scalar; rail corridors map-wide shared one market,
  deleting the design's second-corridor relief; steady state paid p(2Q) instead of
  p(Q).
- **Shadow accounting levied behaviorally** — stage-3 "logged, not levied" still
  drove consumption cuts, displacement, and migration through charged assessments.
- **Synchronized employment epochs** — a citywide re-roll every 60 ticks violated
  §3; now offset per household by hash.
- **Warehousing deadlock** — a vacated scrape candidate whose escrow fell below
  cost became an unlettable ruin forever.
- **Severed incidence** — bids used gross wages, so the income-tax slider never
  reached land values; τ_S revenue leaked into the upgrade escrow it is documented
  to stall.
- **Extractor zones could never develop** — residual demand had no extractor
  channel, so extraction only existed where pre-seeded.

## Reading the numbers

The harness is a compact synthetic economy (196 clusters, ~2,000 parcels, 8–20k
households) behind the same `IAccessCosts` port the in-game adapter will implement
over the routing rebuild's CCH. Architectural properties (localization ratios,
rank correlations, emergent mode splits, asymmetries, conservation) are what
transfer; absolute magnitudes are harness-scale. Several §6 demonstrations
(monoculture, export ramp, engineered bust, gentrification boost) are deliberately
constructed stress scenarios, per the design's own framing of its benchmark set.
