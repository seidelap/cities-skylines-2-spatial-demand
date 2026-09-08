# Game integration references

Implementation is original; these references establish API names and ownership.
No game assemblies or decompiled game source are distributed in this repository.

Construction 0.3 additionally uses the pinned snapshot's
[ZoneSpawnSystem](https://github.com/bworthy89/roadmod/blob/5b49a4fc0c572f2b5133df83083ebb4afe2f76a6/New%20folder/Game.Simulation/ZoneSpawnSystem.cs),
`Game.Common.SystemOrder`, `Game.Tools.GenerateObjectsSystem`, `CreationDefinition`,
`ObjectDefinition`, `OwnerDefinition`, `BuildingConstructionSystem`, `Worker`,
`PlaceableObjectData` and `ConsumptionData`. These establish the demand bypass flag,
16-frame/offset-13 cadence, temporary definition ownership, Modification1 ordering,
construction completion component and observable cost/workplace fields. This new
integration still needs compilation and runtime verification on the installed game.

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
