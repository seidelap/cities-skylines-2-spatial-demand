# Game integration references

Implementation is original; these references establish API names and ownership.
No game assemblies or decompiled game source are distributed in this repository.

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
