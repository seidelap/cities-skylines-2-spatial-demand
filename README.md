# Spatial Demand — Cities: Skylines II

A fresh, small implementation of household housing choice. Each household compares
reachable homes using its own preferences. Vacant units are reserved as households
choose; the game performs the actual move and continues collecting rent.

**Status: first implementation, not an in-game-verified release.** The portable
model passes its tests on Mac and Windows. The real mod compiled and passed the
official postprocessor against game 1.6.0f1 on Windows on September 8, 2026, with
zero warnings and errors. In-game behavior remains unverified. There is no
multiplayer implementation in this branch.

The previous economy prototype is preserved at commit
[`67f4b1c`](https://github.com/seidelap/cities-skylines-2-spatial-demand/tree/67f4b1cf31567196ac230f119c398e94db31f970)
and on `claude/cs2-mod-design-plan-wxqpk7`. This branch deliberately replaces its
source, synthetic city data and historical experiment harness.

## What this version does

- Reads completed residential pathfinding candidates before vanilla evaluates them.
- Uses one household valuation: adequate space, rent burden and travel time.
- Applies a hard affordability limit. Waiting and staying are valid choices.
- Gives households persistent preferences; there is no per-choice random reroll.
- Allocates available units sequentially at the game's posted rents, rotating priority.
- Submits choices through `PropertyProcessingSystem` and checks whether they settled.
- Recovers rejected submissions, including receipts restored from a save.
- Provides two options: evaluate choices, and apply choices. Applying is off by
  default until the game validation below is complete.

## Deliberate limits

This is **housing-choice replacement**, not yet the complete spatial economy mod.
Vanilla still discovers candidate routes and controls immigration, jobs, rent,
upkeep, construction, trade, shelters and departure. There is no replacement demand
bar, developer optimizer, endogenous rent auction or overlay yet.

The available options are the game's pathfinding shortlist, not every home in the
city. Routes reflect vanilla's chosen search origin/destination, which may be a
workplace, school or current location; they are not a complete household travel plan.
The game's shortlist generation may itself depend on its existing property scores.

For residents, the current home must appear with a comparable route in the results.
Otherwise that search stays with vanilla. Insolvent incumbents and special housing
also stay with vanilla. Supported searches with no acceptable offer wait for a
later search; another scorer does not choose from their rejected shortlist.

The preferences are transparent starting assumptions, not calibrated estimates:
rent budget 30–50% of income, adequate space 1–4 game apartment-size units per
person, travel disutility 0.05–0.20 per pathfinding hour. Moving costs 0.05 utility.
Services, pollution, density preference and joint home/job choices are not modeled
yet. See [the model](docs/model.md).

## Run local checks

Install .NET SDK 10, then:

```sh
dotnet run --project tests/SpatialDemand.Tests -c Release
```

The portable core targets **.NET Standard 2.1**. The mod compiles the same core
source using the official toolchain's target (**.NET Framework 4.8** in the tested
version). Only the test runner uses .NET 10. It checks choices, capacity competition, outside options,
reproducibility, the saved preference mapping, and 300 randomized markets.
It does not pretend to test ECS, game serialization, pathfinding or in-game effects.

## Build and test the actual mod

Use your Windows/GCP test machine with a licensed game installation. The script
below checks and builds an existing installation; it does not provision a VM,
install Steam, sign in, or publish anything to Paradox Mods.

1. Finish installing the game and its modding toolchain. Set `CSII_TOOLPATH` if the
   installer has not made it available in the current shell. Install .NET SDK 10.
   The tested official postprocessor also requires the Windows x64 .NET 6 runtime,
   installed alongside the SDK. If using a portable SDK, ensure its directory comes
   first in this shell's PATH; a runtime-only installation cannot run SDK commands.
2. Check out this branch on that machine.
3. Run:

   ```powershell
   .\tools\build-windows.ps1 -GameDirectory 'D:\Steam\steamapps\common\Cities Skylines II'
   ```

   The script runs core tests, builds the **real** mod, and creates a local package
   plus a manifest recording the installed game assembly version and hash. The
   official toolchain may also deploy the build into the local Mods directory.
4. Follow [the in-game acceptance procedure](docs/game-validation.md) on a test city,
   first observing and then applying choices. Record the game version and results.

Direct game build:

```powershell
dotnet build src/SpatialDemand.Mod -c Release -p:CSIIToolPath='C:\path\to\toolchain'
```

The current adapter was checked against the source references listed in
[API references](docs/api-references.md), including a November 2025 game source
snapshot. Compilation and official postprocessing against game 1.6.0f1 have now
passed; this does not establish runtime behavior or compatibility with later updates.

## Where to read the code

| File | Responsibility |
| --- | --- |
| `src/SpatialDemand.Core/Housing.cs` | Offers, household preferences, the one valuation |
| `src/SpatialDemand.Core/HousingMarket.cs` | Sequential choice and capacity reservations |
| `src/SpatialDemand.Mod/HousingChoiceSystem.cs` | Read real searches, submit and reconcile moves |
| `src/SpatialDemand.Mod/SavedPreferences.cs` | Saved household seed and pending move receipt |
| `src/SpatialDemand.Mod/Mod.cs`, `Settings.cs` | Entry point and Options UI |
| `tests/SpatialDemand.Tests/Program.cs` | Portable economic checks |

## Next implementation steps

1. Compile and run on the game machine; verify route units, rent/income periods,
   save/load, settlement and performance before expanding behavior.
2. Make residential candidate discovery and the incumbent baseline independent of
   vanilla property scoring, using game route queries and the same evaluator.
3. Add individual outside offers for migration and explicit residential bids.
4. Derive spatial demand and developer project choices from those bids. Re-evaluate
   opportunities after commitments, rather than adding a parallel demand formula.
5. Extend shared decision principles to labor and firms only after the housing and
   construction loop works in-game.
