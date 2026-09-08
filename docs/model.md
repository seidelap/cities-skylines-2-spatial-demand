# Housing model

The unit of choice is a household because Cities: Skylines II rents dwellings to
households. Individual citizens supply the seed when a household is first observed;
the resulting seed is saved on the household when application is enabled. The seed
does not change as members arrive, leave or die. Observe mode writes no components.

## The decision

Reject invalid routes, unavailable units and rents above the household's budget.
For the remaining offers:

```
utility = adequate-space benefit - rent / income - travel cost - moving cost
```

Adequate-space benefit rises linearly to one at the household's desired amount of
space and then stops increasing. Travel cost is route duration in hours multiplied
by the household's saved sensitivity. Moving cost applies only to changing homes.
The outside option has utility zero and means defer this search. For existing
residents, this does not remove their current tenancy.

`Housing.Evaluate` is the only valuation. Its returned terms also provide an
explanation; no separate display formula is needed. Affordability is a constraint,
not a penalty that an unusually attractive house can overcome.

Price and income periods must match. The adapter uses vanilla asking rent and
`EconomyUtils.GetHouseholdIncome`, as the reference property search does. Their
interpretation and path duration units must be checked against the installed game.

## Competition

The market is sequential choice at posted prices. It is not a Dutch auction and
does not claim a competitive equilibrium. Adding an auction before controlling
the game's rent-setting and collection would create inconsistent prices.

Within a batch, a reproducible rotating priority orders households. Each takes its
best remaining offer. Ties favor staying, then a stable transient property key;
ties with the outside option do not force a move. Entity keys include the version
to distinguish recycled indices. These keys are not preference seeds or a promise
of identical allocations across game save/load remapping.

Taking a vacancy immediately reserves it in this batch. Keeping an incumbent's
home does not consume another vacancy. Giving up a home does not create a vacancy
until the game has actually processed the move. Multiple units in one building
share a posted asking rent; incumbents may have a different existing rent.

The vanilla tenancy processor remains the final capacity arbiter against other
systems' submissions. A queue submission is not reported as a completed move.
The adapter checks the actual tenancy on a later update; a refused submission
re-enables searching. The pending receipt is serialized with the household.

## Boundaries

There is one replacement choice path for supported completed residential searches.
Unsupported cases use vanilla as a whole. The adapter does not inject results into
vanilla's score cache, invent current-home travel times, or disable system families.
It clears/consumes handled path results, so the vanilla scorer cannot make another
choice from that same search.

No other economy is simulated alongside the game. There are no duplicate household
cash balances, shadow rent payments, synthetic immigration rates or formula-driven
construction starts. Those responsibilities remain vanilla until a complete
replacement can be implemented and verified.

The first adapter is a main-thread implementation and completes outstanding ECS jobs
before accessing buffers and the shared queue. This favors understandable ordering
over performance; large-city profiling is a required gate, not assumed success.
