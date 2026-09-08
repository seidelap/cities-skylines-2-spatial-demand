# In-game acceptance: housing smoke tests passed; business and construction tests pending

## Version 0.4 — market quotes and optional diagnostics

Portable tests cover checkout/freight costing, buyer cash versus travel preferences,
stock reservations, fixed-basket shopping choices and incremental labor bidding.
The shopping and labor game systems are read-only diagnostics, default off. No
shopping destination, worker contract, salary, tax or ownership is changed by them.
All **68 portable check groups passed on Mac and Windows**. The actual game build
and official postprocessing succeeded with zero warnings/errors at 21:49:51 UTC on
September 8, 2026, from clean revision `74c066fb39a57a039c1f67daab5808c480798807`.
DLL SHA256: `C0DF14B3681D251870AC2B505E21AE227FC877607EFFDA144EC48342D6C08BED`.
The installed Game.dll hash remains `721E7E17BF74299AA2B988C1BD07E90874BB8BC72D263229500C4BF639E7E4EE`.
This build is staged in `D:\src\economy-review\artifacts\SpatialDemand`, **not
installed or tested live**. The final staged run preserved the installed 0.3 hash
and the same running game process, 9248. Do not terminate that process or overwrite
its files without preserving the user's city.

The build script accepts `-NoDeploy`: the official toolchain's deployment target
is replaced by a small explicit copy target that verifies an isolated staging
directory before compiling. Compilation and postprocessing remain official.
Verify the installed DLL hash stays unchanged before calling this a staged build.

The first 0.4 staging attempt compiled and postprocessed but the official target
ignored a `DeployDir` override, removed unlocked installed files and failed on the
loaded native library. Missing files were restored from the verified 0.3 build;
all six installed mod files then matched that original build. The game was not
restarted. The corrected target fails closed unless staging or installation is
explicitly selected and has a read-only destination preflight.
The corrected staged build passed and preserved the installed DLL. A separate
read-only negative test confirmed an unspecified destination is rejected before
copying, with the intended request for an explicit stage/install setting.

After installing with the game closed, check the 0.4 startup marker. Enable the
shopping and labor diagnostics separately: compare quoted checkout prices with
game purchases; verify incomplete routes never become offers, and confirm the
logs say `applied=0`. Check warehouse/outside stock and outgoing-truck reservations
against game data. Freight distances and future commutes remain straight-line
estimates; no reachable-path or wage-equilibrium validation follows from these logs.
For labor, test no seekers, excess seekers, competing slots, education restrictions,
cash limits and vacancy winners. The sampled wage forecasts do not alter payroll.

## Version 0.3 — tenant-backed construction

On September 8, 2026, all 59 portable tests passed on Mac and Windows, including 16
development checks. The real game build and official postprocessor succeeded with
zero warnings/errors and deployed at 20:52:08 UTC. Source revision:
`d5e4245cc7c0b1580e23aebc791479634d338ef9` (clean checkout). Binary SHA256:
`A7D3543B86F755B88525C27C0E68F7348F9151494B3A4021464C4559B33ADB8C`.
The Game.dll hash remains the version 0.2 test's recorded installed assembly.
**Construction Apply behavior is not yet tested in-game.** Both construction switches
default off. For the pending live check, the saved mod configuration explicitly
sets ConstructionEnabled=true and ApplyConstruction=false; existing housing Apply
remains true. Its prior configuration was backed up on the VM.
The VM stopped at its authorized 20:19:19 UTC deadline before this work. The user
authorized another 90-minute session, with a new STOP deadline of 22:14:36 UTC;
its first start attempt failed because the zone had no GPU capacity. The retry
succeeded and the cloud STOP setting was verified. Version 0.3 is now deployed,
and the city later loaded. Windows App automation initially timed out
on both reading and acting on its disconnection notice; the secure RDP tunnel was
confirmed listening, and the user was asked to reconnect and load the saved city.
Follow the separate
[construction acceptance procedure](construction-model.md#diagnostics-and-acceptance);
earlier household and business evidence below does not validate these new hooks.

The 0.3 startup marker was logged at 20:58:43 UTC. After the user bulldozed and
allowed rebuilding, Observe recorded **30 proposals**, valid settings, Apply=false,
and zero funded/rejected/completed mod projects by 21:07 UTC. This validates startup
and proposal interception in Observe. The old logging interval missed individual
quote explanations; 0.4 retains them. No Apply cancellation, accepted construction
or permit persistence has been exercised.
Steady idle status samples took 0.10–0.12 ms in this small city. Housing remained
unfaulted, and business Observe continued rejecting its sampled premises for
missing input stock. The user was asked to add residential and commercial zoning.
Selecting the remote session through Windows App's Window menu restored native
menu control and screenshots. Keyboard Escape reached the game, but coordinate
clicks still failed with `noWindowsAvailable`, including after leaving macOS full
screen; the desktop-control issue is not resolved.

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
- Spatial Demand loads and its housing, business, construction and diagnostic options appear with English labels.
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
