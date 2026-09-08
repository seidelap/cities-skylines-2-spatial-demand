# In-game acceptance: startup passed, simulation checks pending

## Build evidence — September 8, 2026

Revision `e7f069909ad971aa80ef49f2e63fec557169198e` passed all 25 portable
tests on Windows and compiled against installed game 1.6.0f1. The official
postprocessor and Burst step completed with zero warnings and errors, and deployed
the mod to the user's local Mods directory. This required the registered Windows
x64 .NET 6 runtime in addition to the .NET 10 SDK. Generated postprocessor
`Library/` files accounted for the build manifest's dirty-working-tree flag.

After restarting the game, Modding.log recorded successful loading of SpatialDemand
and its additional Burst library at 18:49:41 UTC. SpatialDemand.log recorded the
prototype's OnLoad message, and SceneFlow.log confirmed the main menu was reached.
This establishes startup only. Options, settlement, serialization and performance
checks below still require execution in the game.

On the disposable Sunshine Peninsula city, observe mode processed its first
supported household search at 19:04:55 UTC. It evaluated one household, proposed a
move, selected a home with utility `0.574` (space `1.000`, rent `0.371`, travel
`0.005`), and queued no move because Apply was off. The same batch delegated 122
unsupported searches to the game's normal process. This confirms that the mod reads
live paths and makes an observable choice without mutating the city. It does not
validate applying, settlement, persistence, or the semantic correctness of the
game's supplied duration and rent units.

Use a disposable test city or a copy of a save. Keep a record of the game version,
the generated build manifest, enabled mods and observations. Start without other
mods that replace household search or property processing.

## Build and load

- `tools/build-windows.ps1` succeeds against the installed game's real assemblies.
- Spatial Demand loads and its two options appear with English labels.
- Observe mode reports evaluated searches without changing entities or queues.
- Confirm `m_Duration` is measured in seconds and that income and asking rent use
  the same period. Check values against the game's own search and budget displays.
- Confirm our system executes before `HouseholdFindPropertySystem` and after the
  path buffers it reads are ready. The code skips pending, failed and obsolete routes.

## Choice and settlement

- Enable Apply on a test city. Confirm queued moves become actual property renters
  and appear in the destination renter buffer exactly once.
- Compare two otherwise similar homes at different rents and distances. Inspect
  the three returned valuation terms to explain the decision.
- Create two competing households and one vacant unit. At most one moves in;
  anyone whose queue submission is refused becomes eligible to search again.
- A household staying in a full building retains its existing unit without using
  another vacancy. Commercial renters in mixed-use buildings do not consume the
  residential quota.
- Verify demolishing/condemning a selected building cannot leave a disabled seeker
  stranded. The receipt must be retried or discarded if the household has left.
- Confirm unsupported current-home routes and insolvent incumbents continue through
  vanilla. Track the delegated share: a high share means coverage needs improvement.
- Confirm supported searches with no acceptable home defer rather than being
  redirected by the vanilla scorer from the same shortlist. Check that vanilla
  behavior later re-enables searches and that homelessness/departure still progress.

## Persistence and removal

- Save/reload after preferences were first recorded. Confirm the stored seed and
  derived traits remain identical despite entity remapping.
- Save/reload around a submitted move. Confirm its receipt is either acknowledged
  or re-enables searching; no extra renter or duplicate rent appears.
- Turn evaluation off with pending moves: reconciliation still runs.
- Save after all receipts settle, disable the mod, and reload. Confirm vanilla
  search, existing tenancies and budgets work. Test unknown mod-component handling
  rather than assuming the serializer can safely ignore removed mod types.

## Performance

Measure simulation speed, time in HousingChoiceSystem, completed searches, delegated
searches and rejected submissions on small and large cities. There is no proven
population/performance target yet. Main-thread job completion and allocations per
batch are known costs to measure before increasing coverage.

The local randomized tests do not substitute for any of these checks.
