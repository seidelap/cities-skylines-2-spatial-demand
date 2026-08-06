# MOD-BRINGUP — in-game bring-up runbook (GCP machine)

The repo's default build (`OUT_OF_GAME_BUILD`, netstandard2.1, no game assemblies)
compiles everywhere and runs the full harness suite. This document is the other
half: turning `src/CS2Econ.Mod` into a working Cities: Skylines II mod on the
machine that owns the game. **The in-game build is designed to be a
compile-and-fix job, not a writing job** — every guess about a game API that the
research notes could not verify is isolated in a tiny `Verify_*` method (or an
attribute declared as an expected-fix site), so a wrong name is one localized
compile error, never a silent bug.

Ground truth throughout: `cs2-economy-mod-research-notes.md` (cited as §n).

---

## 1. Machine setup

The GCP Windows + NVIDIA workstation recipe from the routing repo (`deploy/gcp`:
idle shutdown, cost caps, hibernation) is the intended box; any Windows machine
with the game works.

1. Install Cities: Skylines II. **Pin the game version** (§1.1 version
   discipline): note the exact version string, put it in
   `src/CS2Econ.Mod/Properties/PublishConfiguration.xml` → `GameVersion`, and do
   not accept game patches mid-bringup.
2. Launch the game → **Options → Modding → install the modding toolchain**.
   This sets the user environment variable `CSII_TOOLPATH` (§1.1). Restart the
   terminal afterwards and verify:
   ```powershell
   echo $env:CSII_TOOLPATH        # must point at the toolchain folder containing Mod.props / Mod.targets
   ```
3. Install the .NET SDK (8.x) and git; clone this repo.
4. Optional (UI phase only, not needed for bring-up): Node.js for
   `npm x create-csii-ui-mod` (§1.1, §8).
5. Recommended companion mod: **SceneExplorer** (krzychu124) for live ECS
   inspection while verifying component writes (§6, §8).

## 2. Build commands

```powershell
# sanity: out-of-game build + harness must stay green on this machine too
dotnet build -c Release
dotnet run -c Release --project src/CS2Econ.Harness -- verify

# the in-game build (flips off OUT_OF_GAME_BUILD, imports Mod.props/Mod.targets):
dotnet build src/CS2Econ.Mod/CS2Econ.Mod.csproj -c Release -p:InGame=true
```

`Mod.targets` deploys the built mod to the local `Mods/` folder automatically
(§1.1). Launch the game with `--developerMode` for runtime inspectors; add
`--uiDeveloperMode` later for the UI phase (§1.2).

## 3. Where compile errors are EXPECTED (fix here, nowhere else)

First run of `-p:InGame=true` **should** fail in the places below. Fix each at
its site; do not restructure. The authoritative list is always:

```powershell
git grep -n "VERIFY-INGAME" -- src/CS2Econ.Mod
```

| File | Site | What to check in the decompile |
|---|---|---|
| `ResourceMap.cs` | `Verify_BuildGameResourceTable` | `Game.Economy.Resource` flags-enum member names and the flag→index helper (`EconomyUtils.GetResourceIndex` assumed). The Resource *type* is §3-verified; its members are not. Unknown members: delete the line (that good simply stays untracked). |
| `Mod.cs` | `Verify_RegisterSettings` | `ModSetting.RegisterInOptionsUI()` and `Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(...)` — registration calls are template knowledge (§5.1 verifies only the ModSetting base + attribute-driven pattern). Compare against the official `dotnet new csiimod` template output. |
| `EconModSettings.cs` | whole file (attributes) | `[FileLocation]`, `[SettingsUISection]`, `[SettingsUISlider]`, `[SettingsUIGroupOrder]`, `[SettingsUIShowGroupName]` names/signatures, and the `ModSetting(IMod)` ctor + `SetDefaults()` override. Attributes cannot live in a `Verify_` method — this file is a declared expected-fix site. |
| `EconSerialization.cs` | in-game usings + generic constraints | `Colossal.Serialization.Entities` namespace is §7-confirmed; `IWriter`/`IReader` constraint names are decompile knowledge (the assembly reference is already in the csproj's InGame item group, alongside `Colossal.IO.AssetDatabase`). Buffer-element serialization (does the framework write the buffer length?) is part of the §9-item-1 spike — both `ExitEconState` (per-resource buffer on the connection entity) and the `EconGlobalState` singleton depend on it. |
| `EconReader.cs` | `Verify_*` (natural-resource suitability) | The game's natural-resource / map-feature data feeding `ClusterInfo.ResourceSuitability` — **not covered by the notes at all**; find the component in the decompile (SceneExplorer helps locate it on extractor-viable cells). |
| `EconWriter.cs` | `Verify_SwapLevelPrefab` | Level lives on the PREFAB (`SpawnableBuildingData.m_Level`, §3) — renovation = prefab swap. §9 item 7: do renters survive an externally triggered swap? If not, use the documented evict-rehouse fallback. |
| `ClusterAccessProvider.cs` | CS2Path CCH hookup point (comment) | Optional: bind to CS2Path's CCH cluster costs instead of the built-in Dijkstra-over-`Game.Net` rebuild. Off by default; the built-in path uses only §3/§8-verified components (`Game.Net.LaneFlow` etc.). |
| `EconBridgeSystem.cs` | any `Verify_*` present | System names are §4-verified, but the boot enumeration (§1) is the arbiter — check the log line listing `Demand|LandValue|Rent|Upkeep|Trade|Spawn` matches before trusting any disable call. |

Everything NOT listed above is written against §3/§4-verified names and should
compile clean; if it does not, the game patched — run the decompile diff
(§1.1 maintenance discipline) before touching code.

## 4. In-game verification checklist (run IN THIS ORDER — §9)

Ordered by architectural risk; each gate changes the plan if it fails.

1. **Save round-trip spike** (§7, §9 item 1) — before any tier persists state.
   The wiring already exists: `EconStatePersistence.Capture` (called by the
   bridge every `CaptureEveryTicks` engine ticks, **only when levying** — shadow
   sessions leave zero footprint in a save) stamps `ParcelEconState` components,
   per-resource `ExitEconState` buffers, and the `EconGlobalState` singleton;
   `EconStatePersistence.Restore` pulls them back inside
   `EconReader.BuildInitial`. The spike: flip one tier live in a throwaway city,
   save, reload, verify escrow/EMA values survive. Then load the same save
   **without the mod**: unknown component data must be skipped without
   corruption. Negative result → sidecar-file fallback (§7) before proceeding.
2. **Decompile diff / boot enumeration** (§9 item 2) — start a city, read
   `EconBridgeSystem`'s OnCreate log: the enumerated
   `Demand|LandValue|Rent|Upkeep|Trade|Spawn` systems. Confirm post-2.0
   leveling lives in `BuildingUpkeepSystem` (the dump predates Economy 2.0, §2
   caveat); enumerate `Game.UI.InGame.*` binding names.
3. **Demand-bar binding contract** (§9 item 3) — can replacement demand values
   feed the vanilla bars, or does the mod ship its own bar cluster?
4. **Decompile `OverlayInfomodeSystem`** (§9 item 4) — overlay route 2 prep.
5. **`PrefabSystem.AddPrefab` infoview injection** (§9 item 5) — route 3
   feasibility; skip without blocking if it fails.
6. **`Game.Net.LandValue` write semantics** (§9 item 6) — with
   `LandValueSystem` disabled, write assessments via `EconWriter` and watch
   (SceneExplorer) who else reads/overwrites, and at what cadence.
7. **Prefab-swap renovation** (§9 item 7) — `Verify_SwapLevelPrefab` with
   tenants in place; fallback: evict-rehouse.
8. **Post-2.0 price plumbing** (§9 item 8) — where companies read prices
   (`ResourceSystem` prefab table vs `Game.Economy.ResourceInfo` instances):
   the Tier D injection point.
9. **UI toolchain end-to-end** (§9 item 9) — template → webpack → Mods folder
   → binding visible in-game. UI phase only.

## 5. Flipping from shadow to live, one tier at a time

The mod loads with **ShadowAccountingOnly = ON** (Options → CS2Econ): the engine
runs `SyncTick → Step` every `EngineTickFrames` game frames, assesses and logs
everything, and `EconWriter.Apply` is **skipped** — vanilla systems stay
authoritative and the save cannot be corrupted by economics. Run a real city
this way first and compare logged assessments/residuals against vanilla
outcomes (stage-3 observe-only).

Then go live **one tier per session, saving before each flip** (each flag is
independently revertible — design §3; a tier's live state is what disables its
vanilla system group in `EconBridgeSystem`):

| Order | Flip | Vanilla seam replaced | Watch for (first sim-day) |
|---|---|---|---|
| 1 | `ShadowAccountingOnly` OFF with only **Tier B** live | demand scalars feed from residuals (`ResidentialDemandSystem` etc.) | demand bars move by district, not citywide; no rent changes yet |
| 2 | **Tier D** | trade/price plumbing (`TradeSystem`, exporter/buyer prices) | import/export prices bend with volume; no flat-price teleporting |
| 3 | **Tier C** | `LandValueSystem`, `RentAdjustSystem` (LandValueOverhaul precedent, §6) | rents re-assess gradually (staggered anniversaries — no citywide jump tick); `LandValue` on net edges tracks assessments |
| 4 | **ConstructionRewire** | `ZoneSpawnSystem` site selection | construction localizes to residual hotspots (vacancy-localization behavior from the harness) |
| 5 | **Tier C2** | leveling/condition (`BuildingUpkeepSystem` seam) | levels drift toward access geography; condition decays only where S is unpaid |
| 6 | **Tier A** | `HouseholdSpawnSystem` / `HouseholdMoveAwaySystem` | inflow/outflow asymmetry; no population cliff on flip |

**Rollback at any point:** turn the flag off (vanilla system re-enabled next
update) or remove the mod — `Mod.OnDispose` re-enables everything it matches,
and all authoritative state lives in vanilla components (§7 architecture), so
the save stays coherent.

## 6. After a game patch

Re-pin: decompile, diff the components/systems named in §3/§4 against the
adapter files (`EconReader/EconWriter/EconBridgeSystem/ResourceMap`), repair,
re-run the out-of-game harness, then repeat §4 items 1–2 before trusting the
patched build (§1.1 maintenance discipline).
