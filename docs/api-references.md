# Game integration references

Implementation is original; these references establish API names and ownership.
No game assemblies or decompiled game source are distributed in this repository.

Version 0.4 also uses the pinned snapshot's
[ResourceBuyerSystem](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Simulation/ResourceBuyerSystem.cs),
`ResourcePathfindSetup`, `EconomyUtils`, `TradeCost`, `ServiceAvailable`,
`StorageCompanyData` and `VehicleUtils`. Checkout adds the seller's embedded buy
cost and applies the retail service multiplier. New freight uses the resource's
weight and shipment quantity. Its distance is an explicit forecast approximation.
Shopping's single `PathInformation` winner is not housing's candidate buffer;
substituting a seller would invalidate the already computed route.

`PayWageSystem`, `TaxSystem`, `WorkProviderSystem`, `FreeWorkplaces` and
`CompanyDividendSystem` establish the labor/ownership boundary: vanilla payroll
owns company debits, household credits and tax accrual; household income estimates
also use its education wage table. `Worker` and `Employee` have no per-person
salary field. The original labor auction therefore only reports hypothetical
contracts. Vanilla company dividends already transfer surplus to employees'
households; a second ownership ledger would not be a harmless extra feature.
The 0.4 adapter compiled and passed official postprocessing against the installed
1.6.0f1 assemblies at revision `74c066f`; this verifies API compatibility, not the
meaning of route/time units or negotiated-wage gameplay.

Construction 0.3 additionally uses the pinned snapshot's
[ZoneSpawnSystem](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Simulation/ZoneSpawnSystem.cs),
`Game.Common.SystemOrder`, `Game.Tools.GenerateObjectsSystem`, `CreationDefinition`,
`ObjectDefinition`, `OwnerDefinition`, `BuildingConstructionSystem`, `Worker`,
`PlaceableObjectData` and `ConsumptionData`. These establish the demand bypass flag,
16-frame/offset-13 cadence, temporary definition ownership, Modification1 ordering,
construction completion component and observable cost/workplace fields. Version 0.3
compiled against the installed 1.6.0f1 assemblies after resolving the `MovingAway`
namespace to `Game.Agents`; runtime phase-order and behavioral checks remain pending.

Business integration also consulted the same pinned game snapshot's
`CommercialSpawnSystem`, `IndustrialSpawnSystem`, `Game.Citizens.CompanyInitializeSystem`,
`BuildingPropertyData`, `ResourceBuyer`, `IndustrialProcessData`, `ResourceSystem`
and `EconomyUtils`. These establish template archetypes, initialization and initial
rent submission, allowed resource masks, current buyer orders and the game's own
wage/production calculations. The version 0.2 adapter compiled against the installed
1.6.0f1 assemblies; runtime business acceptance remains separate.

- [Official-template snapshots](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/tree/main/dotnet):
  IMod entry, settings registration/localization, CSII_TOOLPATH and Mod.props/targets.
- [HouseholdFindPropertySystem reference](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Simulation/HouseholdFindPropertySystem.cs):
  enableable PropertySeeker, PathInformations, direction of property results,
  GetHouseholdIncome, rent queue submission and 16-frame update interval.
- [PropertyProcessingSystem reference](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Simulation/PropertyProcessingSystem.cs):
  GetRentActionQueue, writer dependency, capacity checking and actual tenancy writes.
- [PropertyUtils reference](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Buildings/PropertyUtils.cs):
  apartment space from lot dimensions, space multiplier and residential count.
- [PropertySeeker reference](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Agents/PropertySeeker.cs):
  enableable state, best property and last-search frame.

The referenced game snapshot dates to November 2025, not the user's installed game.
Its signatures must be verified by compiling the real mod and running acceptance
checks. In particular, the older 2023-era component dumps used by the previous
prototype have a different PropertySeeker layout and are not sufficient here.
