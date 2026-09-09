# Comparable shopping routes and negotiated wages

Version 0.5 keeps two small decisions: a shopper minimizes basket price plus time
cost; an employer compares a worker's contribution less wage with an empty slot.
The adapter code is larger than these decisions because the game owns routes,
employment records, cash, taxes, save/load and scheduled simulation jobs.

Both Apply options are experimental and off by default. The portable tests check
the choice models and payroll arithmetic, not game transactions or serialization.
See [current acceptance evidence](game-validation.md) before enabling either.

## Shopping

The adapter captures one completed vanilla shopping search and its current
building. It discovers up to eight stocked sellers from a bounded 512-seller
sample: the incumbent, low checkout-price shops and nearby shops. Every candidate,
including the incumbent, receives a new game-network route from the same captured
origin using the same resident's pedestrian speed, path weights and permissions.
Geometry selects a shortlist; completed route duration determines travel cost.
There is no claim to search every shop or optimize across every travel mode.

Only ordinary local households buying a full affordable basket of physical goods
are supported. Tourists, commuters, students, active car/bicycle owners, existing
trips and special transport orders remain with vanilla. A one-currency-unit/hour
time value is a configurable preference, not a fare or cash debit. Duration is
interpreted as seconds and still needs live calibration.

Observe allows four independent route comparisons without holding purchases.
Apply holds at most one supported search, storing the original route and order in
serializable recovery components. It rechecks order, origin, affordability, seller
stock and price before using the result. A changed winner must have a valid route,
and the incumbent must also have a comparable valid route. Complete path elements
and their matching metadata are copied together; vanilla then executes checkout.
No mod purchase, stock subtraction or money transfer is issued.

After 512 frames, cancellation, changed settings, a changed order, unload or
reload, the original held route is restored when still applicable. A stale order
loses the obsolete route so vanilla can search again. Proxy paths are discarded.
The logs distinguish `started`, `completed`, `held`, `handedOff` and `restored`.
`handedOff` proves a route submission, not arrival or a completed purchase.

## Wages

The bounded auction contains up to 128 people and 128 positions at 64 employers.
Standing agreements enter without bidding against themselves. A company's own
vacancy cannot bid up its own worker. An outside employer can make an improving
offer; the incumbent employer may respond if retaining the worker has greater
surplus than leaving the slot empty. No artificial scarcity multiplier is added.

Apply currently settles only a retention raise: the worker keeps the same actual
employer and job level, and the model must record a competing external offer.
Cross-employer matches remain forecasts; the game retains routed job search and
employment changes. Local private processing/service company employees are
eligible; extractors, storage, public/outside employment and special households
are outside this first version. Qualification and reciprocal Worker/Employee
records are rechecked before accepting an agreement.

Employer cash reserves cover current payroll, rent and a full input-cost estimate
before spare cash is divided across actual positions. Contribution is a
full-sales technology estimate, not measured marginal revenue; commute is an
explicit straight-line round-trip estimate, not proof that another job is reachable.
Those approximations can produce economically poor retention bids. Do not treat
this bounded auction as a citywide equilibrium or calibrated labor market.

A saved agreement contains the citizen's employer, level, daily gross salary and
acceptance frame. A proposed raise must fit the slot's contribution and cash
limits and improve take-home income. Daily gross is rounded upward to a multiple
of the game's 32 payroll slices, then all limits are checked again. Rounding an
unchanged wage cannot create a raise. Falling employer cash blocks new raises
without silently cutting an existing valid wage.

The game still performs the original household credit, queued company debit and
taxable-income accrual. The mod substitutes the agreed salary input for that call
and restores the local parameter copy even on an exception. It also updates the
shared household-income and actual-employee wage-expense helpers. There is no
second money ledger and no tax deducted by the mod.

These salary consumers must all see the agreement. When Apply is active, their
original job bodies execute through a verified managed runner:

| Job owner | Why it needs the agreed wage |
| --- | --- |
| PayWageSystem | Household credit, company payment queue, taxable income |
| HouseholdBehaviorSystem | Household income, needs and departure |
| HouseholdFindPropertySystem | Housing affordability |
| CitizenPathfindSetup | Affordability while discovering home routes |
| SicknessCheckSystem | Household income in health decisions |
| RentAdjustSystem | Residential rent capacity |
| CompanyEconomyStatisticSystem | Wage expense and operational profit |
| ProcessingCompanySystem | Company taxable profit |

All required patch points are checked together. If one is absent, the integration
rolls back and contracts cannot activate. Inactive mode retains original job
scheduling. Active mode completes input dependencies and snapshots valid contracts
before managed execution, preserving original chunk masks and query indices.
It never reruns a partly executed job after an exception. The cumulative managed
timing includes dependency waiting, snapshot preparation and execution; this
synchronous path requires a large-city performance test.

Hypothetical capacity wages, education opportunity costs and public-service wages
remain the game's baseline ladder. They lack an individual agreement to resolve.
No transferable ownership, wage cuts, layoffs, financing or property ledger is added.

Turning labor evaluation off stops new negotiations while valid agreements can
remain active. Turning wage Apply off uses vanilla salaries and leaves agreements
dormant. Invalid employer/level/roster relationships are ignored immediately when
read and removed by periodic reconciliation, including while evaluation is off.
An unobserved leave-and-rehire between checks is not an employment-history model;
that lifecycle edge remains an acceptance limitation.

## Required live checks

1. Load with both new Apply options off; verify patch preflight succeeds and the
   city runs without new exceptions. Verify the built DLL and full package hashes.
2. Enable shopping Observe: obtain multiple valid routes from one origin, including
   the incumbent, and verify actual price/time tradeoffs. Unsupported travel modes
   must continue normally. Failed or timed-out proxies must disappear.
3. Enable shopping Apply on the backed-up city: observe a changed-route handoff,
   actual arrival and exactly one vanilla purchase. Change the order or seller
   stock, toggle off, and save/reload with a hold; no stuck order or duplicate sale.
4. Enable wage Apply: observe an external bid and a saved retention agreement.
   `labor payroll receipt` compares actual household credit and taxable accrual
   with the expected slice. It explicitly does not claim the later company debit
   has settled. Verify that debit separately, plus household forecasts, company
   wage/profit/tax figures and unchanged payments for uncovered employees.
5. Run a complete game day and reload mid-cycle. Confirm no duplicate payments,
   correct employer remapping, invalidation on employer/level/roster changes,
   dormant/reactivated settings behavior and measured simulation performance.

Construction Apply is a separate acceptance track. See
[its positive, rejection and persistence cases](construction-model.md#diagnostics-and-acceptance).
