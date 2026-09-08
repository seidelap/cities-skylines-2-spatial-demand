# Business choices, shopping and labor diagnostics

The same small market evaluator serves retail, manufacturing and office activities.
At each vacant compatible property it chooses the activity with the greatest
positive projected surplus. No entry is an explicit option. Existing companies
keep their original templates, employees, stock and accounts.
Version 0.4 improves the observable price and stock inputs and adds two optional,
read-only diagnostics. Its Windows build and in-game acceptance are pending.

## Decisions and accounting

The adapter reads current `ResourceBuyer` orders, the payer's available cash,
stocked sellers, premises asking rent and compatible recipes. Retail and producer
buyers use separate markets. Buyers prefer the lowest quoted cost; incumbents win
equal-cost ties and their stock is finite. Inputs are purchased cheapest first.
Each partial shipment is quoted for its actual feasible quantity, and suppliers
are reranked after each allocation. This is a small greedy forecast, not a global
optimization over every possible basket split. Per-unit budgets conservatively
retain the original order's ceiling; retail cash excludes the game's shopping reserve.
Missing inputs cap output; no usable input stock prevents the activity.

Existing seller quotes share `ShoppingPriceQuote`: the game's market/industrial
price plus that seller's embedded buying cost, with the retail service multiplier
where applicable. This mirrors checkout price terms before total rounding. It does
not change prices. A prospective company has no trading history or service state,
so its own opening price remains the prefab resource price.

Eligible stocked warehouses and outside sellers can supply observed resources.
Offers respect stored/output resource masks and subtract resources already reserved
by buying trucks. They are finite observed stock, not an unlimited import backstop.
Unsupported sellers, inactive buildings and transport stops are excluded.

The new freight leg uses `EconomyUtils.GetTransportCost` with resource weight,
shipment quantity and straight-line distance. Embedded upstream buying cost is
already in the seller quote and is not added again. Retail buyers instead compare
checkout cost plus estimated trip time valued in currency; time affects preference,
not the cash affordability check. The shared time-value setting defaults to
1 currency/hour, and estimated travel speed to 30 km/h. Neither is a charged fare.
`BusinessRangeMetres` is an optional forecast limit: **0, the default, means no
distance cutoff** within the bounded snapshot. It does not limit actual shopping.

Projected surplus is sales revenue minus delivered input cost, rent and wages.
The game supplies production capacity and its existing education-based wage bill.
The hypothetical labor auction does not replace that bill. Resource amounts follow
the template's recipe. Candidate evaluation does not mutate the planning book. Committing a choice
reserves its buyers and inputs before another site is evaluated.

With business Apply enabled, the adapter creates a company using the selected
template's actual archetype and initial property. The game's company initialization
owns stock, brand, employment setup and the initial rent action. The mod verifies
the company's appearance in the destination renter buffer. A refused entrant is
returned to vanilla property search with its chosen activity. The receipt and
entry frame are saved components. Reconciliation runs even with evaluation disabled.

## Forecast limits

- Entry is **additional to vanilla**, with a cap of one mod-created entrant per
  game day. It does not replace vanilla firm entry, exit, exports or warehouse
  operations.
- Freight cost comes from the game, but distance is estimated. A nearby site across
  an unconnected river is not proved reachable. Actual routes, purchases, delivery,
  stock, money and trade-cost updates remain owned by the game.
- Demand consists of current orders. It is not extrapolated into a daily demand
  curve. The forecast conservatively asks those orders to cover a day's rent and
  wages and caps output by a day's estimated production. Demand observation over a
  full day and verification of rent/wage periods remain necessary for calibration.
- Taxes, utilities, financing, hiring delays and the entrant's future adaptive
  pricing are not included. Full staffing is assumed when quoting capacity and
  the vanilla wage bill.
- Only observable stock supplies inputs, including supported stocked outside offers.
  Future replenishment and persistent import commitments are not inferred. A city
  without an observable supply chain can legitimately produce no entry proposals.
- The snapshot takes at most 512 buyer entities and 512 seller entities and scans 16
  rotating premises per update. Large-city coverage and performance are unvalidated.
- Reservations are planning reservations within a batch, not actual purchases or
  persistent customer contracts. The entry cooldown bounds repeat commitments
  across snapshots; it does not replace a future persistent pipeline ledger.
- Industrial templates producing weightless resources are reported as office
  activities. Actual compatibility is controlled by the building's resource masks,
  not by that diagnostic sector label. Extractors, storage and signature buildings
  are delegated to vanilla.

## Optional shopping diagnostic

`ShoppingEnabled` defaults off. For ordinary local retail searches it observes
vanilla's completed winning route, quotes the selected seller, and checks a full
basket against stock and household cash after the game's minimum shopping reserve.
Logs separate price, embedded buying cost, service multiplier and estimated time
cost. Route duration is assumed to be seconds; trip fares are not quoted, and
checkout still applies its own rounding and stock changes.

The portable `ShoppingMarket` can compare alternative baskets and reserve finite
stock. The game adapter only receives one winning `PathInformation`, so it evaluates
that offer rather than replacing the destination. Actual route choice, purchases
and payments remain vanilla. Observation counters are not unique shopping trips.

## Optional labor diagnostic

`LaborEnabled` also defaults off. The portable `LaborMarket` uses individual bids:
a firm offers only when marginal value minus gross wage beats leaving the slot
empty at zero surplus, subject to the cash reserved for that slot. A worker accepts
only an improvement in take-home pay minus commute over their outside option.
Competing employers can raise wages; each worker and slot is matched at most once.
The deterministic auction uses one currency unit of net pay per bid increment and
reports whether its bounded run converged.

The read-only adapter samples at most 128 active unemployed adult seekers,
64 employers and 128 slots. Outside income is observed unemployment benefit only;
incumbent job changes, leisure preferences and external job offers are not modeled.
Employer cash is allocated once across vacancies after incumbent wages and rent,
then reduced by estimated new input costs. Value ceilings use production at full
efficiency and prefab prices, assuming output sells. They are not measured marginal
sales. Commute is an estimated daily round trip using the shared speed/time value.
These sampled forecasts are not a citywide equilibrium.

**Forecast wages are not cims' actual salaries.** `Worker`, `Employee` and
`WorkProvider` have no per-contract salary field in the inspected API. Vanilla's
private payroll job also handles company debits, household credits, benefits,
commuter rules, rounding and tax accrual; household/company income forecasts
separately read its wage table. Coherent negotiated payroll requires those paths
to agree. This diagnostic writes no jobs, wages or money and has no Apply switch.

## Diagnostics and acceptance

`business status` reports observed buyers, stock offers, sampled premises,
compatible activities, rejection reasons, proposals, submissions, settlements,
recoveries, cooldown and elapsed time. `business choice` identifies the activity,
resource, premises and every projected cost term. `business settlement` is emitted
when a receipt is checked; it does not depend on another business being proposed.

Start in observe mode. Test retail, industrial and office premises separately.
Change buyer demand, competitor stock, input availability and travel costs and
check the ranking. Then enable business Apply on a disposable save; verify template,
stock, workforce and renter registration after initialization. Test rejection,
save/reload during entry, and disabling evaluation with a receipt outstanding.
Do not infer a successful company spawn from a proposal log or compilation.

Shopping logs identify the one vanilla route and use `applied=0`. Labor logs show
hypothetical gross/net pay, commute, reserved cash, fill surplus and empty surplus,
with `payroll=vanilla` and `applied=0`. A non-converged run emits status without
presenting individual contracts as settled outcomes.

Portable checks cover business no-entry, affordability, finite stock, freight
quotes, shopping basket choice and labor scarcity/outside options. They do not test
game initialization, serialization or payroll settlement. Housing and construction
retain their separate [model](model.md), [construction notes](construction-model.md)
and [in-game acceptance procedure](game-validation.md).
