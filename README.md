# Spatial Demand — Cities: Skylines II

A small implementation of household housing choice, new business activity
selection and experimental tenant-backed construction. Households compare reachable homes. Prospective firms compare compatible
activities at vacant premises using buyers, supplier stock and costs. The game
performs tenancy settlement and runs the resulting companies.

**Status: experimental prototype.** Housing has passed live observe, apply and
city-reload smoke checks on game 1.6.0f1. Individual preference persistence, difficult
settlement cases and large-city performance are still unvalidated. Version 0.2 adds
business entry and richer diagnostics; its business gameplay acceptance is separate
from the earlier housing results. Version 0.3 adds construction selection for
residential and business buildings. Its 59 portable model tests pass on Mac and
Windows; the real game build and official postprocessor pass with zero warnings
or errors. Construction behavior has not yet been validated live.
Both construction switches start off. There is no multiplayer implementation.

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
- Evaluates commercial, industrial and office-compatible company templates with
  one shared business choice model. Buyers compare delivered quotes; candidate
  firms procure inputs cheapest first and select the greatest positive surplus.
- Has separate business observe/apply switches. Business Apply adds at most one
  entrant per game day using vanilla initialization; vanilla entry remains active.
- Reports settlement immediately and periodic status even without new successful
  choices, with housing delegation reasons and business rejection reasons.
- Quotes prospective household/business rents and compares development projects
  with waiting. Construction Apply bypasses vanilla's demand threshold for its
  proposal shortlist and removes unfunded definitions before buildings are created.
- Persists one active project at a time, with a one-game-day entry limit, and reports
  actual completion/occupancy separately from forecasts. See the
  [construction model, assumptions and outstanding acceptance](docs/construction-model.md).

## Deliberate limits

This is **housing-choice replacement, experimental business entry and a construction forecast gate**, not yet
the complete spatial economy mod. See [business decisions and limits](docs/business-model.md).
Vanilla still discovers candidate routes and controls immigration, jobs, rent,
upkeep, construction execution, trade, shelters and departure. The developer chooses
among vanilla-generated projects; it does not enumerate every possible design/site.
There is no replacement demand bar, actual financing, advance lease, endogenous rent
auction or overlay. Buildings can still finish vacant when forecasts do not settle.

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
passed for versions 0.2 and 0.3. The new 0.3 construction hooks have not passed
runtime acceptance yet. Compilation does not establish gameplay behavior.

## Where to read the code

| File | Responsibility |
| --- | --- |
| `src/SpatialDemand.Core/Housing.cs` | Offers, household preferences, the one valuation |
| `src/SpatialDemand.Core/HousingMarket.cs` | Sequential choice and capacity reservations |
| `src/SpatialDemand.Mod/HousingChoiceSystem.cs` | Read real searches, submit and reconcile moves |
| `src/SpatialDemand.Mod/SavedPreferences.cs` | Saved household seed and pending move receipt |
| `src/SpatialDemand.Core/BusinessMarket.cs` | Buyer/supplier choices and business surplus |
| `src/SpatialDemand.Core/DevelopmentMarket.cs` | Tenant bids, posted-rent choice and project argmax |
| `src/SpatialDemand.Mod/ConstructionHousingSystem.cs` | Household construction quotes and existing-home alternatives |
| `src/SpatialDemand.Mod/ConstructionProposalSystem.cs` | Temporarily bypass the vanilla proposal demand threshold |
| `src/SpatialDemand.Mod/ConstructionChoiceSystem.cs` | Preconstruction gate, saved permit and completion diagnostics |
| `src/SpatialDemand.Mod/Mod.cs`, `Settings.cs` | Entry point and Options UI |
| `tests/SpatialDemand.Tests/Program.cs` | Portable economic checks |

## Next implementation steps

1. Compile and run on the game machine; verify route units, rent/income periods,
   save/load, settlement and performance before expanding behavior.
2. Make residential candidate discovery and the incumbent baseline independent of
   vanilla property scoring, using game route queries and the same evaluator.
3. Add individual outside offers for migration and routed future-home quotes.
4. Validate the new construction adapter, then replace its one-project limit with
   explicit pipeline claims if multiple simultaneous projects are needed.
5. Validate the business entry adapter separately for retail, manufacturing and
   offices. Replace distance estimates with routed quotes, observe demand over a
   shared horizon and add imports/pipeline commitments before replacing vanilla entry.
