# CS2 Spatial Demand & Land Economy — Harness Results

Measured by `CS2Econ.Harness` (see README for commands) on a 4-core Linux
container, .NET 8, single machine. Correctness checks are deterministic;
acceptance scenarios follow the design's §6 targets, several as A/Bs against the
vanilla-baseline mode on identical seeds. Seed: 20260806. Numbers below are from
the **resource-level core** (11-resource catalog, geology, recipes, per-resource
trade — the post-refactor economy).
Full design: [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md) ·
plan: [`PLAN.md`](PLAN.md) · in-game bring-up: [`MOD-BRINGUP.md`](MOD-BRINGUP.md).

## Headline results

| design claim | measured |
|---|---|
| §2(c) construction/leveling respond to residual submarket demand | realized level map rank-correlates with ℓ* at **0.51** (vanilla grind: 0.01); construction spawns at the residual-maximizing level; 44 projects abandoned mid-build in the engineered bust |
| §2(d) Georgist core closes the fiscal loop | a transit corridor raises land-rent revenue along itself **+32.3%** within 60 ticks (control row +3.7%); circularity guard holds bit-exactly under a 17.5× realized-rent perturbation |
| §2(e) finite-depth trade kills export spam | sustained monoculture bends the marginal received price **31% below** the flat-price baseline at 156/tick; truck→rail→backstop progression emerges by volume (road 72% → rail 59% → sea 30%) with concurrent marginal prices equalized to **0.0%** |
| §4.2 location choice is a real Weber problem | 93% of extractor entrants pick their cluster's best raw by geology; 83% of single-input industrial entrants pick the recipe whose input is cheapest-sourced *at their location*; 4 distinct industrial outputs coexist |
| §2(a) migration margin with boom/bust asymmetry | symmetric amenity pulse: arrival response **+2,133** vs departure response **+121** over equal windows — inflow fast, outflow slow (attachment) |
| §3 anti-synchronization | 24,582 displacement exits with the worst single tick carrying **0.3%** (a synchronized cliff would be ≫5%); assessment anniversaries uniform |
| money conservation | double-entry ledger drift ≤ **2.9×10⁻⁶** over 300 ticks across all scenario economies |

## Correctness verification (16 checks, 16 passing)

- ✅ **IPF: marginals respected**: max row viol 0.0E+000, col 0.0E+000
- ✅ **IPF: two-sided consistency**: |jobsClaimed − workersClaimed| = 1.14E-013
- ✅ **annuity operator round-trips**: lump 12345.6 -> flow 6.4325 -> 12345.60
- ✅ **supported level ℓ* is an interior optimum rising with access**: ℓ*(cold)=2, ℓ*(mid)=3, ℓ*(hot)=5
- ✅ **road export law concave (d=2), rail linear (d=1)**: road drops 0.5368>0.4119; rail drops 0.216000≈0.216000
- ✅ **multimodal composition = horizontal summation (vs brute force)**: clear 2502.35 vs brute 2502.35 (clear/brute per exit: e0:150/150 e1:175/175 e2:275/275)
- ✅ **transient impact layer decays at resilience rate**: burst 490.0 -> 416.5
- ✅ **Weber: extraction follows geology; recipes follow input sourcing**: 14 extractors (93 % on best raw); 18 single-input industrials (83 % on cheapest-sourced recipe); 4 distinct industrial outputs
- ✅ **assessment anniversaries uniform, never synchronized (§3)**: bucket range [616,716] vs mean 667
- ✅ **circularity guard: assessment blind to own realized rent**: LR 2.5881 unchanged under 17.5× realized-rent perturbation
- ✅ **ledger conservation: money neither created nor destroyed**: max |drift| over 300 ticks = 2.89E-006
- ✅ **shadow accounting: assessed + logged, nothing levied (stage 3)**: assessments computed: True, escrow balance: 0.00
- ✅ **insolvency pipeline: staged, ordered, terminates**: stages: Solvent→CutConsumption→SortDown→Sheltered
- ✅ **no stuck homeless population (vanilla bug-class regression)**: longest non-decreasing shelter streak 51 ticks
- ✅ **feature flags: vanilla-mode fallback runs (every tier revertible)**: pop 2473 (sane bounds), drift -2.4E-006
- ✅ **determinism: same seed → identical telemetry hash**: h(seed)=4CA22800A380D7DA twice, h(seed+1)=AE7EE693B7B5C2BF

## Acceptance scenarios (10 targets, 9 passing)

| §6 target | result | measured |
|---|---|---|
| level map correlates with access (rank corr vs ℓ*, not grind) | ✅ | Spearman(realized level, ℓ*) spatial 0.51 vs vanilla 0.01 |
| fiscal loop: transit raises LR revenue along its corridor | ✅ | corridor +32.3% vs control +3.7% within 60 ticks |
| monoculture export bends marginal price ≥30% below flat | ✅ | sustained 156/tick, marginal 1.79 vs flat 2.60 → bend 31 % |
| truck→rail→backstop progression by volume; concurrent marginals equalize | ✅ | export ramp 8→950/tick: road 72 % at <60 (n=50) → 22 % at ≥600; rail 28 %→59 % at mid (n=172); sea 30 % at top (n=336); concurrent marginal gap 0.0 % |
| boom/bust asymmetry: inflow reacts faster than outflow | ✅ | arrival response +2133 vs departure response +121 over equal windows/pulse |
| no synchronized displacement: exit times form a distribution | ✅ | 24582 displacement exits, worst single tick 0.3 % (cliff would be ≫5%) |
| stalled construction appears in engineered busts | ✅ | 44 projects abandoned mid-build after demand collapse (421 lifetime starts) |
| overlay honesty: correction factors bounded and settling | ✅ | 309 realized-vs-predicted observations; factor range [0.70,1.00], late swing 0.00 |
| Tier B refresh scales with clusters, not parcel count | ✅ | refresh 6.0 ms at 8 parcels/cluster vs 5.1 ms at 16 → ratio 0.85 (cluster count fixed) |
| vacancy localization ≥10:1 (vanilla ≈1:1) | ❌ | spatial: shocked clusters 21→17 starts, elsewhere 87→82, suppression localization 1.2:1; vanilla: shocked 83→84, elsewhere 366→366 (1.0:1) |

**On the open target.** The mechanism §6 asks about is verified at the unit
level: vacancies subtract from residual demand, absorption has no floor that
lets bid strength override a dead submarket, and the claims ledger meters the
pipeline. What the scenario cannot yet produce is a suppression *ratio* of 10:1,
because the shocked submarket refills faster than the measurement window: Tier A
arrivals are throttled by a citywide attractiveness signal, not paced by
absorption in the submarkets they land in (absorption-paced arrivals are the one
§4.1 element still stubbed). A ~700-unit vacancy shock is met by a
several-thousand-seeker inflow, so shocked-cluster starts dip (21→17) while
far-cluster starts barely move — directionally right, magnitude 1.2:1. The A/B
scaffolding (shock-vs-control on identical seeds, cluster-granular start
attribution, scrape-funded starts excluded) is in place; the remaining work is
Tier A pacing, not a missing mechanism.

## The resource-level economy

The refactor the calibration review demanded ("how do we assess spatial
economics without doing it at the resource level?") is in the core, not layered
on top: an 11-resource catalog (4 raws with geology suitability fields, 5
recipe-produced goods, services, office output), per-resource freight weights,
per-(resource × exit) price laws, and a consumption basket driving commercial
restocking. Firm entry solves a real Weber problem — extractors argmax
suitability × price over local geology, industrials argmax recipe margin net of
delivered input costs and output haul — and the Weber verification check pins
both margins against brute-force oracles. Transport cost is now attached to
*sourcing a specific resource from a specific place*, which is what makes the
monoculture bend, the mode progression, and the parity bands mean something.

## Real-map run

`harness map` runs the same economy on the Chicago Regional road network
(TNTP → `data/chicago-regional.cs2city`, 11,189 nodes / 35,436 edges, bucketed
to 240 clusters with per-cluster-representative Dijkstra costs): geology laid
over real coordinates, zoning layered by access, per-resource exits at the
network edge. Deterministic; used as the pre-game shakedown for the import
pipeline the in-game `ClusterAccessProvider` mirrors.

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

A second review pass (12 findings) ran against the game-facing adapter layer in
`src/CS2Econ.Mod` — duplicate divergent resource tables, entity↔engine-id
pairing that broke once the engine appended its own arrivals, an
un-re-enableable `Reinitialize`, unwired save-state codecs — all applied; the
reconciliation rationale is documented at the fold table in `ResourceMap.cs`.

## Reading the numbers

The harness is a compact synthetic economy (196 clusters, ~2,000 parcels, 8–20k
households) behind the same `IAccessCosts` port the in-game adapter implements
(`ClusterAccessProvider`: Dijkstra over `Game.Net` with LaneFlow congestion;
optionally the routing rebuild's CCH). Architectural properties (localization
ratios, rank correlations, emergent mode splits, asymmetries, conservation) are
what transfer; absolute magnitudes are harness-scale. Several §6 demonstrations
(monoculture, export ramp, engineered bust) are deliberately constructed stress
scenarios, per the design's own framing of its benchmark set.

*Note: `harness all` regenerates the two generated sections (correctness list,
scenario table) and overwrites this file — re-merge the narrative sections when
refreshing numbers.*
