# Implementation & Test Plan — CS2 Spatial Demand & Land Economy

This plan turns [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md) (the design)
and [`cs2-economy-mod-research-notes.md`](cs2-economy-mod-research-notes.md) (the modding
research) into code, following the conventions proven in the companion repo
[`cities-skylines-2-path-optimization`](https://github.com/seidelap/cities-skylines-2-path-optimization):
**harness-first, ports-and-adapters, every tier feature-flagged, out-of-game builds on any machine.**

---

## 1. Architecture

Three projects, mirroring `CS2Path.{Core,Mod,Harness}`:

```
src/CS2Econ.Core/      Pure economy core — NO game or Unity references.
                       netstandard2.1 (game-loadable) + net8.0 (harness).
src/CS2Econ.Mod/       The ONLY code allowed to touch Colossal Order assemblies.
                       Default build defines OUT_OF_GAME_BUILD (documented stubs);
                       in-game build via -p:InGame=true + CSII_TOOLPATH Mod.props/targets.
src/CS2Econ.Harness/   Standalone validation: synthetic city, vanilla-baseline A/B,
                       benchmark scenarios, §6 acceptance tests. verify | scenarios | all
                       → RESULTS.md, same shape as the routing harness.
```

**The routing dependency is a port, not a reference.** The design mandates that all access
terms come from the routing rebuild's cached CCH at cluster granularity. In code that is
`IAccessProvider` (cluster→cluster generalized costs + dirty-flag events + version stamp).
In-game it is backed by CS2Path's `ClusterCache`/CCH machinery; in the harness it is backed
by exact Dijkstra over the synthetic city's cluster graph. What this repo tests is the
*economy* logic — cost fidelity is the routing repo's problem, and the port keeps the
"no independent distance computations" constraint honest by construction.

### Ports (`Ports.cs`)

| Port | Direction | In-game backing | Harness backing |
|---|---|---|---|
| `IAccessProvider` | in | CS2Path CCH cluster cache + corridor dirty flags | Dijkstra on synthetic cluster graph |
| `IParcelWorld` | in | ECS adapters (zoning blocks, buildings, households, companies) | `SyntheticCity` |
| `IEconomyWriter` | out | writes **vanilla components** (`LandValue`, `BuildingCondition`, `PropertyRenter.m_Rent`, level-via-prefab-swap, spawn requests) so saves stay coherent without the mod | applies to synthetic state |
| `ITelemetrySink` | out | logging/UI bindings | scenario recorders |

`FeatureFlags`: one boolean per tier (A, B, C, C′, D, construction), each independently
revertible to vanilla behavior, plus the τ_S/τ_L sliders and per-district policy toggles.

---

## 2. Module map (design § → file)

| Design | File | Contents |
|---|---|---|
| §4.2 Tier B | `Access.cs` | w(p,q)=e^(−θc) weights; per-segment consumer access (jobs by tier, goods, schools, amenities, −pollution/noise); firm-side terms (commercial phantom-entrant capture, industrial exit-parity pricing + Weber input haul, office agglomeration A(p)^γ) |
| §4.2 | `Balancing.cs` | Sinkhorn/IPF doubly-constrained balancing on cluster access matrices; **residual demand** per (cluster, type) net of incumbents *and pipeline ledger* |
| §4.2 | `Insolvency.cs` | bottom-of-market pipeline: cut consumption → re-sort down price gradient → funded emigration → sheltered homeless with capacity; re-housing via the same allocation machinery |
| §4.3 Tier C | `LandAccounting.cs` | S=(h+δ+m)·V with V=condition×RC; LR(p)=max over permitted configs of [Bid−S−a(h,L)·(transition cost−escrow)]; P_L=LR/(r+τ_L); split-rate taxes; wedge→escrow earmark (per-district toggle); the single annuity operator a(h,L); instant co-op re-rate (every occupant pays the parcel's live market unit assessment; see the deviation note below) |
| §4.4 Tier C′ | `Leveling.cs` | ℓ*=argmax_ℓ[Bid_ℓ−S_ℓ] with RC_ℓ=RC₁·γ^(ℓ−1); renovation escrow clock (fire at ΔRC, re-anchor, re-arm); downgrade = same mechanism sign-flipped (underfunded S → condition decay); scrape-and-rebuild warehousing; owner-tag consent gates, within-segment moving-cost distributions |
| §4.1 Tier A | `Migration.cs` | per-segment Rosen–Roback attractiveness (wages, rents, amenities); asymmetric lagged elasticity; outside-world scalars: reservation threshold (drawdown + replenish), prominence, network memory; firm entry on residual profit |
| §4.5 Tier D | `Trade.cs` | per-(resource×exit) p(Q)=a+t·(Q/ρ)^(1/d) laws (road d=2, rail d=1 + terminal intercept, sea/air flat capped); sustained-Q EMA + transient impact layer + permanent parameter shifts; parity bands from routed transport cost; quantized offers → cheapest-first procurement (emergent multimodal split); adjacent-exit coupling; export-base feedback |
| §4.6 | `Construction.cs` | developer return = predicted rent × predicted absorption − cost, hurdle-gated, softmax site selection; **claims ledger** (decrement residual at commitment); construction lag; milestone re-evaluation (abandon when E[return] < remaining cost); **calibration loop** (realized vs predicted-at-decision-time, shrunk correction factors) |
| §4.7 | `Overlays.cs` | pure projections: expected rent/sqft, time-to-fill, ℓ* + redevelopment pressure + escrow fill, net fiscal yield/sqft, price-vs-parity-band, residual-demand bars, S/tax/wedge decomposition |
| — | `EconomyEngine.cs` | facade + tick orchestration (below) |
| — | `EconTypes.cs`, `Params.cs` | segments (income × education × lifecycle), zone/resource enums, all named parameters with defaults and doc-refs |
| — | `Ledger.cs` | double-entry money accounting; every flow has a source and sink; the four sanctioned phantoms (developer, outside world, bank, national counterparty) are explicit accounts |

**Tick structure** (`EconomyEngine`): a *fast tick* (trade transient decay, construction
progress, escrow accrual, the 1/N slice of PARCELS whose assessment slice falls
now, per-household re-rate against it, insolvency steps); a *refresh tick* driven by access dirty flags plus a slow staggered
sweep (access matrices → IPF → residuals → ℓ* → LR for dirty clusters only); a *slow tick*
(migration scalars, sustained-Q EMAs, calibration factors). No citywide synchronized event
exists anywhere — the anti-synchronization constraint (design §3) is structural: per-entity
phases come from hashed ids, staggering from real heterogeneity draws.

**Determinism:** SplitMix64 throughout, seeds derived from entity ids — same discipline as
the routing repo, required for reproducible acceptance tests.

**Two invariants enforced in code, not by convention:**
- *Circularity guard* (§3): `LandAccounting` has no read path to a parcel's own realized
  rent — assessments take only market-access bids computed over the distribution. A unit
  test perturbs realized rent and asserts the assessment is bit-identical.
- *Ledger closure* (§3 open-economy boundaries): `Ledger` refuses flows without a
  counterparty; terminal household balances escheat to the national counterparty account.
  The harness asserts global conservation every scenario tick.

---

## 3. Build order (design §5) and what proves each stage

Each stage lands with its tests green before the next stage trusts its outputs. Stages 1–6
are harness work (this repo, any machine); stage 7 is the in-game phase.

| Stage | Work | Proven by |
|---|---|---|
| 0 | Scaffold, params, `SyntheticCity`, `Ledger`, ports | builds + ledger conservation smoke test |
| 1 | Tier B read-only: access matrices, IPF, residuals, overlays | IPF marginals match (jobs claimed = jobs available, workers = workers); residuals predict fill in engineered scenarios; overlay values finite and stable |
| 2 | Tier D trade scalars + parity bands | §6: monoculture bend ≥30%; truck→rail→backstop progression; concurrent-exit marginal-price equalization; parity band responds to congestion cost |
| 3 | Tier C shadow accounting (τ_L logged, not levied) | circularity-guard unit test; incidence chain (tax → bids → LR) moves in shadow; escrow ramp convexity |
| 4 | Construction rewiring on live residuals | §6: vacancy localization ≥10:1 vs vanilla baseline (A/B); cobweb damping with ledger on/off; stalled projects in engineered bust; calibration convergence |
| 5 | Tier C′ leveling/escrow live | §6: level map rank-correlates with access; renovation fill time ≈ ΔRC/(N·wedge); downgrade decay; no displacement cliffs (exit-time distribution test) |
| 6 | Tier A migration endogeneity | §6: boom/bust asymmetry; reservation-threshold drawdown; chain-migration momentum; homelessness damps arrivals via the rent term |
| 7 | In-game: adapters + UI + save | research-notes §9 checklist, below |

**Vanilla baseline for A/B:** the harness implements design §1.1's vanilla model as a
switchable mode — global scalars (happiness/unemployment/homeless/free-space), uniform
spawn on eligible cells, citywide vacancy suppression, uniform level grind. Targets that
read "vs vanilla" run both modes on identical scenarios and seeds.

---

## 4. Test strategy

### 4.1 Correctness suite (`harness verify`)

Unit-level invariants, all deterministic:

1. **IPF consistency** — after balancing, row/col sums match supplies/demands within 1e-4.
2. **Circularity guard** — perturbing realized rent leaves assessment bit-identical.
3. **Ledger conservation** — Σ(balances) + treasury + net phantom flow constant per tick;
   escheat routes to national counterparty; escrow drain-to-condition conserves.
4. **Annuity operator** — a(h,L) round-trips lump↔flow at the three sanctioned call sites.
5. **Interior optimum** — convex RC_ℓ against concave bids yields interior ℓ* for mid
   access; corner solutions only at extremes.
6. **Trade law shapes** — road curve concave increasing no asymptote; rail linear with
   intercept; horizontal summation = cheapest-first offer selection (verified against
   brute-force marginal-cost merge); EMA + transient decay round-trip.
7. **Vacancy kernel + co-op re-rate** — one vacant unit cancels exactly one unit of
   demand citywide (V=1) with suppression density falling with distance; pipeline claims
   and completed-vacant units suppress identically (the claim↔vacancy wash); co-tenants
   of a parcel pay a bit-identical charge tracking the live market assessment.
8. **Insolvency pipeline** — each step fires only when the previous is exhausted; sheltered
   population bounded; re-housing occurs when vacancies exist at affordable S (no stuck
   states — the vanilla truncated-queue bug class regression).
9. **Feature flags** — each tier off reproduces vanilla-mode behavior on the same seed.
10. **Determinism** — same seed, same city → identical telemetry hash.

### 4.2 Acceptance scenarios (`harness scenarios`) — §6 targets as numbers

| Test | Scenario | Pass condition |
|---|---|---|
| vacancy localization | vacancy shock in one district | construction shift ≥10:1 in-district vs distant; vanilla mode ≈1:1 |
| level geography | long steady run | Spearman(realized level, access) significantly positive; vanilla ≈ uniform max |
| fiscal loop | transit corridor added mid-run | LR revenue along corridor rises within a few sim-days; visible in budget telemetry |
| trade bend | monoculture export ramp | marginal received price ≥30% below flat-price baseline at equal volume |
| mode progression | export ramp with rail terminal | road-only → road+rail → backstop by volume; concurrent exits' marginal prices equal within 5% |
| boom/bust asymmetry | symmetric attractiveness pulse | inflow response constant < outflow response constant |
| no synchronization | gentrification frontier | per-household exit times form a spread distribution — no tick contains >5% of a cohort's exits |
| stalled construction | engineered bust mid-boom | milestone re-evaluation abandons some in-flight projects; none complete at a loss silently |
| overlay honesty | all scenarios | predicted/realized fill ratio converges to a stable band; shrunk correction factors bounded and mean-reverting |
| performance | scaling sweep | refresh work scales with dirty clusters, not parcel count (op counters, not wall clock) |

### 4.3 Adversarial review

After the suite is green, run the same discipline the routing repo used: an independent
multi-dimension review pass (economic-logic errors, conservation leaks, synchronization
channels, NaN/overflow paths, degenerate parameterizations), each finding verified before
fixing, worst findings regression-tested.

### 4.4 Deviations from the design document

The design doc is the specification; where the implementation knowingly departs from it,
the departure is recorded here rather than by editing the design.

- **§4.3/§4.4 staggered assessment anniversaries and tenant-protection phase-in → instant
  co-op re-rate.** The design set each household's assessment on a personal anniversary
  (period 30) with an optional per-district tenant-protection phase-in. Implemented
  instead: every housed household is charged its parcel's *current* market unit
  assessment every tick — one price per unit, bit-identical across co-tenants, which is
  the co-op semantics (rent = market rate; land value = total − structure). Staleness
  moved from the household to the parcel (the 1/`AssessSlices` reassessment slice), which
  is why co-tenant uniformity is exact.
  *Anti-synchronization (§3) is preserved by different means:* the re-rate is smooth
  (no step at a personal anniversary), relocation search is a memoryless per-tick hazard
  (`MoveSearchPeriod`) rather than a calendar event, and moving-cost draws stay
  heterogeneous. A one-time `GoLiveRampTicks` window, staggered per household, covers the
  regime change when Tier C first starts levying so flipping the mod out of shadow mode
  cannot re-rate a city in a single tick.
  *Dropped with it:* the per-district `TenantProtection` dial (design §4.4's
  preservation-vs-development lever). It has no implementation; reintroducing it would
  mean a phase-in on top of the co-op price, which conflicts with one-price-per-unit.
- **§4.2 vacancy netting → spatial kernel.** Vacancies were netted only within their own
  cluster. Implemented instead: a normalized kernel e^(−d/λ) over straight-line metres
  (`VacancyKernelLambdaM`, walking-distance proxy) with V = 1 conservation, applied
  identically to standing vacancy and to pipeline claims. In-migration is paced by an
  absorption budget (`VacancyFillHazard` × feasible vacant stock, per segment) instead of
  a flat citywide clamp.
- **§4.1 absorption budget: measured turnover floor + honest exit telemetry.** The
  budget's churn term was a bare assumed constant (`HousingTurnoverRate` × occupied);
  churnprobe's reason-tagged exits showed 98% of "displacement" exits were actually
  arrivals that waited out their patience at the door and never found a unit. The churn
  term is now max(measured EMA of units actually freed via `Allocation.Vacate`, the
  turnover prior) — the measurement lets post-shock churn open the door wider; the prior
  keeps a no-churn city bootstrappable. A stricter Little's-law congestion gate on the
  standing unhoused queue was tried and REVERTED: the queue is load-bearing (it stands in
  SegmentPresence, holding clearing prices up and making construction pencil), so gating
  it froze growth. Bounced arrivals are the honest cost, now labeled failed arrivals —
  not displacement — and they damp further arrivals through the migration distress term.
- **Labor: i.i.d. employment lottery → Markov chain.** Employment was re-drawn against
  the balanced rate every 60-tick epoch, putting 1−rate of the whole city into a fresh
  60-tick unemployment spell each epoch. Now a separation hazard (6%/epoch) and a finding
  hazard f = s·r/(1−r) whose stationary point is the balanced rate r: incumbents keep
  jobs; it is the marginal arrival's first draw that sours when the city saturates.
- **§4.1 migration distress signal.** Attractiveness' homelessness term reads sheltered
  households in full plus households in any insolvency stage at half weight: funded
  emigration exits the solvent-but-stressed before they ever shelter, so a pure homeless
  share was blind to economic distress.
- **§4.3 clearing price refinements (adversarial review).** Density appeal enters ONCE,
  on the WTP leg (weighting the jointly-normalized share as well renormalized the
  apartment discount into a house subsidy); zero-WTP segments are not bidders (they were
  anchoring the excess-supply price at exactly 0); the price is read at the excluded
  challenger (`ClearingBand`), the rule as originally specified.
- **§4.3 excess-supply price: proportional decay → flat tail.** Where demand exhausts
  before the stock fills, the price floors FLAT at the deepest positive bidder's WTP
  instead of decaying toward zero: cutting below the last real bidder buys no tenant
  that exists (the revenue-max "4×$6 beats 5×$4" argument at the one point it binds
  monotonically). Two stronger forms were measured and reverted — submarket-wide
  revenue-max is a cartel (price invariant at the richest tranche's WTP, mass vacancy,
  a segment extinct), and unconstrained revenue-max in excess only breaks supply
  monotonicity (more supply raised the price at the regime boundary). The shortfall
  surfaces as expected vacancy (`fillRatio` output); valuation stays price-based.

---

## 5. In-game phase (stage 7 — needs a machine with CS2)

Follows the research notes; the GCP Windows box recipe in the routing repo's `deploy/gcp`
is the dev loop. Ordered by architectural risk (research §9):

1. **Save round-trip spike** — custom `ISerializable` component + singleton blob; verify
   load-without-mod does not corrupt. Negative result → sidecar fallback, decided before
   any tier persists state. Mod-native state inventory is research §7 (escrow, owner tag, trade EMAs, migration scalars, claims ledger, calibration factors) —
   schema-versioned from day one.
2. **Decompile diff** — confirm post-2.0 leveling lives in `BuildingUpkeepSystem`; enumerate
   `Game.UI.InGame.*` demand/budget binding names; enumerate `Demand|LandValue|Rent|Upkeep|Trade`
   systems at boot (never hardcode).
3. **System replacement seams** — disable + replace, per the LandValueOverhaul precedent:
   demand systems (Tier B/residual bars), `ZoneSpawnSystem` (site selection §4.6),
   `HouseholdSpawnSystem`/`HouseholdMoveAwaySystem` (Tier A), `LandValueSystem`/
   `RentAdjustSystem`/`PropertyRenterSystem`/`BuildingUpkeepSystem` (Tier C/C′),
   trade/`ResourceBuyer`/`ResourceExporter` price plumbing (Tier D).
4. **UI** — InfoLoom-pattern `UISystemBase` bindings for panels and demand bars;
   `OverlayRenderSystem` parcel painting for the §4.7 overlays (decompile
   `OverlayInfomodeSystem` first); `TooltipSystemBase` for the S/tax/wedge decomposition;
   `ModSetting` for flags and district policies.
5. **Vanilla-authoritative writes** — results land in vanilla components wherever vanilla
   systems also read them, so flags-off and mod-removed saves stay coherent.

Version discipline: pin game version; per patch, decompile-diff touched systems, repair
adapters, re-run out-of-game suite, unpin.

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| Save persistence of mod-native state unproven | Spike is gate #1 of stage 7; sidecar fallback designed |
| Demand-bar binding contract unknown | Fallback: own bar cluster via `registry.extend` (InfoLoom precedent) |
| Prefab-swap renovation with tenants in place unverified | Isolated in one adapter method; fallback: evict-rehouse through the allocation machinery (design degrades gracefully) |
| IPF cost at city scale | Cluster-level matrices only (~10³ clusters), θ-decay truncation makes them sparse; refresh rides dirty flags — performance test asserts scaling shape |
| Cross-repo coupling to CS2Path internals | Coupling is one interface (`IAccessProvider`); harness never links CS2Path |
| Economy tuning drift vs game patches | All parameters named in `Params.cs` with design-doc references; calibration loop measures drift rather than assuming zero |
