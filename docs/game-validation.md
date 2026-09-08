# In-game acceptance: housing smoke tests passed; business and construction tests pending

## Version 0.3 — tenant-backed construction

On September 8, 2026, all 59 portable tests passed locally, including 16 development
checks. The new construction adapter is implemented but **not yet compiled or
tested against the installed game**. Both construction switches default off.
The VM stopped at its authorized 20:19:19 UTC deadline before this work. The user
authorized another 90-minute session, with a new STOP deadline of 22:14:36 UTC;
its first start attempt failed because the zone had no GPU capacity. Version 0.2
remains the last deployed build. Follow the separate
[construction acceptance procedure](construction-model.md#diagnostics-and-acceptance);
earlier household and business evidence below does not validate these new hooks.

## Version 0.2 — business entry and diagnostics

On September 8, 2026, all 43 portable tests passed on Mac and Windows. The real
game build and official postprocessor succeeded with zero warnings/errors and
deployed at 20:05:37 UTC. Binary SHA256:
`E7E99BE446CD14F1E4784B71A53DC12BCD6CED3147F0B8C09FE8E9E69D0AE8FC`.
The Windows checkout used copied source changes, so its manifest correctly records
a dirty working tree on the older base revision. Do not treat that base revision
alone as the source of the new binary.

The 18 new business tests cover costs, activity selection, no-entry, demand,
finite competitor stock, input availability, partial production, buyer budgets,
shared reservations and deterministic ordering. New diagnostics report housing
settlements independently of new choices and explain delegated attempts. Business
reports include sampled buyers/sites, compatibility, reasons, costs and receipts.

Version 0.2 loaded in the saved city at 20:08:19 UTC. Its new housing status line
reported `faulted=False` and no pending receipts. Business observe mode ran at
20:08:53 UTC and thereafter: six stocked companies, one vacant sampled premises,
one compatible activity and no proposals because the required input stock was
unavailable. An active buyer request was observed in a later snapshot. After the
first 31.63 ms update, observed batch times were 0.21–2.69 ms in this small city.
This is startup and live evaluation evidence, not successful business entry or a
large-city performance result.

Business entry and settlement still need separate game evidence for each sector.
See [business model and acceptance checks](business-model.md). Earlier housing
results below do not establish those new business behaviors.

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

With Apply enabled on the same city at 19:10:33 UTC, a later batch reported four
evaluated searches, four proposed moves and one queued rent action. The selected
sample had utility `0.438` (space `0.669`, rent `0.175`, travel `0.006`). The next
search batch had not occurred when this evidence was captured, so neither settlement
nor recovery is claimed from this run. The local settings record persisted
`ApplyChoices: true`; this validates the option save only, not household preference
or pending-move serialization.

The city was then saved, the VM was stopped, and the same save was reloaded after
the next boot. Spatial Demand loaded again and Apply mode processed a fresh batch:
seven evaluated searches, seven queued moves, six settlements and zero retries.
This verifies save/reload continuity at the city level and sustained post-load
settlement. It does not identify individual preference seeds across the reload.

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
