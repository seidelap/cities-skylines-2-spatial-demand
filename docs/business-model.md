# Business entry prototype

The same small market evaluator serves retail, manufacturing and office activities.
At each vacant compatible property it chooses the activity with the greatest
positive projected surplus. No entry is an explicit option. Existing companies
keep their original templates, employees, stock and accounts.

## Decisions and accounting

The adapter reads current `ResourceBuyer` orders, their payer's available cash,
company output stock, resource prices, premises asking rent and compatible company
recipes. Retail and producer buyers use separate markets. Buyers prefer the lowest
delivered quote within the configured radius; incumbents win equal-price ties and
their stock is finite. Inputs are purchased cheapest first. Missing inputs cap
output; a missing input with no stock prevents the activity.

Projected surplus is sales revenue minus delivered input cost, rent and wages.
The game supplies production capacity and wage calculations, rather than a second
production formula in this mod. Resource amounts are converted by the template's
recipe. Candidate evaluation does not mutate the planning book. Committing a choice
reserves its buyers and inputs before another site is evaluated.

With business Apply enabled, the adapter creates a company using the selected
template's actual archetype and initial property. The game's company initialization
owns stock, brand, employment setup and the initial rent action. The mod verifies
the company's appearance in the destination renter buffer. A refused entrant is
returned to vanilla property search with its chosen activity. The receipt and
entry frame are saved components. Reconciliation runs even with evaluation disabled.

## Explicit first-version limits

- Entry is **additional to vanilla**, with a cap of one mod-created entrant per
  game day. It does not yet replace vanilla firm entry, exit, exports or warehouses.
- Geography uses straight-line distance, a 2 km default radius and an exposed
  delivery estimate of 1 currency unit per unit per kilometre. These are assumptions,
  not measured routes or prices. Actual trading still uses the game. A nearby site
  across an unconnected river is not proved reachable by this forecast.
- Demand consists of current orders. It is not extrapolated into a daily demand
  curve. The forecast conservatively asks those orders to cover a day's rent and
  wages and caps output by a day's estimated production. Demand observation over a
  full day and verification of rent/wage periods remain necessary for calibration.
- Quoted prices start at the game's resource prices. Service availability pricing,
  taxes, utilities, financing, hiring delays and adaptive selling prices are not
  included. Full staffing is assumed when quoting capacity and wages.
- Only existing local output stock supplies inputs. No invented unlimited imports,
  speculative future stock or global demand-bar multiplier is used. A city without
  an observable supply chain can legitimately generate no entry proposals.
- The snapshot takes at most 512 buyer entities and 512 companies and scans 16
  rotating premises per update. Large-city coverage and performance are unvalidated.
- Reservations are planning reservations within a batch, not actual purchases or
  persistent customer contracts. The entry cooldown bounds repeat commitments
  across snapshots; it does not replace a future persistent pipeline ledger.
- Industrial templates producing weightless resources are reported as office
  activities. Actual compatibility is controlled by the building's resource masks,
  not by that diagnostic sector label. Extractors, storage and signature buildings
  are delegated to vanilla.

## Diagnostics and acceptance

`business status` reports observed buyers, stock offers, sampled premises,
compatible activities, rejection reasons, proposals, submissions, settlements,
recoveries, cooldown and elapsed time. `business choice` identifies the activity,
resource, premises and every projected cost term. `business settlement` is emitted
when a receipt is checked; it does not depend on another business being proposed.

Start in observe mode. Test retail, industrial and office premises separately.
Change buyer demand, competitor stock, input availability and delivery costs and
check the ranking. Then enable business Apply on a disposable save; verify template,
stock, workforce and renter registration after initialization. Test rejection,
save/reload during entry, and disabling evaluation with a receipt outstanding.
Do not infer a successful company spawn from a proposal log or compilation.

The portable tests cover choice, the no-entry option, affordability, local input
availability, finite competition, partial output, shared reservations and stable
ties. They do not test game initialization or serialization.
