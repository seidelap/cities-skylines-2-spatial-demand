# Tenant-backed construction prototype (0.3)

**Implementation status:** the portable model passes its tests. The new game
adapter has not yet been compiled against the installed game or tested in-game.
The previously deployed and smoke-tested build is 0.2. Both construction options
default off. No claim of validated construction behavior follows from housing's
earlier live tests.

## Decision

A developer compares proposed buildings with doing nothing. Households offer the
highest rent at which the existing `Housing.Evaluate` still prefers the proposed
home after moving costs. Their baseline includes staying, waiting and a sample of
existing vacant homes. Firms offer the surplus from the existing `BusinessMarket`
after wages and input costs, retaining one currency cent of operating surplus.
Compatible commercial, manufacturing and office activities use that same model.

For each design, the developer tries each possible number of tenants at the
marginal accepted rent and chooses the greatest rental revenue. It then checks:

```
project surplus = (daily rent receipts - daily upkeep) * payback days - capital cost
```

The greatest positive project surplus wins the round. This is a financial budget
identity and a finite argmax, not another population/demand curve. Household bids
reuse the existing valuation via bisection; there is no second housing utility
formula. A project can be feasible with partial occupancy; every forecast occupied
unit must have an affordable tenant bid. Zero tenants cannot justify construction.

This is a **forecast gate**, not a financing or advance-lease implementation. No
money is debited, actual rents are still vanilla, and proposed tenants remain free
to change their minds. A completed building can therefore still have vacancies.
The quoted business activity is recorded for diagnostics; actual business entry
reevaluates the market at completion through the separate business feature. Vanilla
companies may occupy it first. Forecast activity is not a binding advance tenancy.

## Before construction, not demolition after construction

Observe evaluates vanilla's proposed buildings without changing them. Apply uses
the existing `ZoneSpawnSystem` to find geometrically eligible lot/building
proposals, temporarily setting its public `debugFastSpawn` flag while jobs capture
their inputs. A matching system restores the previous value immediately after
that update. Both use the game's 16-frame interval and offset 13.

The gate runs before `GenerateObjectsSystem` in `Modification1`. It reads the
temporary `CreationDefinition` and `ObjectDefinition` entities, which carry
`Updated` **and `Deleted`**. Rejected definitions and their matching
`OwnerDefinition` children are removed before world objects are generated. No
completed building is demolished by this feature. A latch retains ownership of a
forced batch if Apply is switched off before definitions arrive; that batch is
cancelled. If no definitions were produced, this conservative latch can cancel the
next eligible vanilla batch after switching off, then clears.

The demand threshold is bypassed in Apply, but vanilla's shortlist still ranks and
samples candidates using its own scores. This is not exhaustive optimization over
every possible zoned lot or building. The engine API and phase ordering above need
verification against the installed build. Other mods that create matching growable
construction definitions may also be intercepted; there is no engine-provided
source tag distinguishing those definitions.

## Simple pipeline reservation

Only **one project at a time, and at most one per game day**, is authorized. A
standalone serialized `DevelopmentPermit` survives the temporary definition and
tracks its prefab, location, building, forecast activity, rent and tenant count.
While it is outstanding, another mod project cannot pledge the same demand or
supplier stock; the mod's additional business entry is also held. Vanilla economic
transactions continue and can invalidate a forecast.

The permit matches a generated building by prefab and position, then waits until
construction finishes or it is demolished. A definition that produces no building
within 16,384 simulation frames is reported as failed. A real building under
construction does not time out and release its reservation prematurely. Completed
or cancelled permits remain until the one-day cooldown ends. Reconciliation runs
even with evaluation off. Save/load must verify entity remapping and continuation;
the portable tests do not exercise the game serializer.

One project is deliberately restrictive: it replaces a large pipeline ledger with
an easily inspected first implementation. It limits growth, and residential and
business projects compete with each other in each sampled round.

## Observable inputs and explicit assumptions

- Housing uses up to 512 existing households per batch, rotating through them,
  and up to 32 existing vacancies for alternatives. Household income uses the
  game's existing income function. Saved preference seeds have the same meaning
  as in the housing choice feature; a newly committed household's seed is saved.
- Work locations are real entities, but future-home travel is the longest member
  commute estimated by straight-line distance at **30 km/h**, configurable. The
  same estimate is used for the current home and vacancies. Unknown work locations
  or unsupported current housing cause delegation, not zero-time assumptions.
  A household without workers has no modeled work commute. Schools, services and
  route connectivity are not included. Actual move-in still uses game routes.
- Business uses the existing capped buyer/order and supplier/stock snapshot.
  Current orders are treated as a **daily sales proxy**, then held constant over
  the payback horizon. This is not measured recurring demand. Inputs must be
  visible locally; imports, extractors and storage activities are not supported.
- Capital cost uses a positive prefab construction cost when present, otherwise
  **1,000 currency per zoning cell**, configurable. Upkeep uses the prefab's
  consumption data. The default payback horizon is **32 game days**. These are
  planning assumptions, not calibrated investment behavior or charges to the city.
- Up to 16 proposals are evaluated in a round. In Apply, unevaluated, unsupported,
  unfunded and pipeline-blocked proposals wait. Mixed-use housing must pay for the
  whole building from residential bids; commercial rent is not added twice.
- Demand bars continue to display vanilla values. Immigration is still vanilla.
  An empty city with no existing households or a region without observable input
  suppliers can stop growing under strict Apply. Use an established test city;
  outside household offers and import quotes are future work, not invented demand.

## Diagnostics and acceptance

Periodic `construction quote` rows show forecast tenants/capacity, proposed rent,
capital cost and source, upkeep, payback, surplus and business rejection reasons.
`construction status` reports observe/apply, forced batches, invalid settings,
pipeline blocking, proposal/permit/rejection counts and elapsed time. Settlement
reports actual building identity and renter count separately from the forecast.

Required Windows/game checks (all outstanding for 0.3):

1. Compile/postprocess against the installed game; inspect phase order and confirm
   the temporary flag is restored to its previous value, including unload.
2. Observe: proposals are logged and every vanilla definition remains intact.
3. Apply with no acceptable bids: newly zoned land stays empty, including attached
   subareas/subnets. Existing buildings, player tools and unrelated definitions
   remain intact. Test low/zero demand bars to prove proposal generation bypass.
4. Supply an established household market and a viable business input chain.
   Verify a positive residential project and commercial/industrial/office projects
   can each win in separate cases. Changing income, existing rents, buyer orders,
   supplier costs or quoted construction costs should change the decision.
5. One accepted project blocks another until completion and cooldown. Confirm
   prefab/position matching and that actual occupancy is reported truthfully.
6. Save/reload with a permit both before and during construction. Demolish a
   pending building, toggle off after a forced batch, and test a failed definition.
   No duplicate project, abandoned child definition or stuck permanent bypass.
7. Check actual rent/income/upkeep periods and larger-city performance before
   calibrating defaults or increasing sample/pipeline limits.
