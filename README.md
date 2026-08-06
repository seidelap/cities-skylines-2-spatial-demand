# CS2 Spatial Demand & Land Economy

Implementation of the design in [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md):
a replacement for Cities: Skylines II's demand, migration, land value, rent, building-leveling,
and trade-pricing systems with a spatially disaggregated economy under Georgist land accounting,
shipped as a code mod sharing infrastructure with the routing rebuild
([cities-skylines-2-path-optimization](https://github.com/seidelap/cities-skylines-2-path-optimization)).

Vanilla CS2 drives construction from a single global demand scalar, destroys location rents on
payment, trades at flat infinite-depth prices, and grinds every building toward max level. This
rebuild replaces that with three spatial tiers — migration against a weakly endogenous outside
world, allocation by market access with doubly-constrained balancing, and local absorption where
construction and leveling respond to *residual* submarket demand — on a land-accounting core
that closes the fiscal loop: the treasury is the residual claimant on location value.

Supporting documents:

- [`cs2-spatial-demand-economy.md`](cs2-spatial-demand-economy.md) — the design
- [`cs2-economy-mod-research-notes.md`](cs2-economy-mod-research-notes.md) — CS2 modding surface research (verified components, systems, precedent mods, UI routes)
- [`PLAN.md`](PLAN.md) — implementation & test plan (module map, build order, acceptance tests)
- [`RESULTS.md`](RESULTS.md) — measured harness results against the design's §6 targets
- [`MOD-BRINGUP.md`](MOD-BRINGUP.md) — in-game bring-up runbook: building `-p:InGame=true` on the game machine, the expected-compile-fix sites, the §9 verification checklist, and the shadow→live tier flip order

## Layout

```
src/CS2Econ.Core/      Pure economy core — NO game assembly references
src/CS2Econ.Mod/       The ONLY code allowed to touch Colossal Order assemblies
                       (default build stubs game touchpoints: OUT_OF_GAME_BUILD)
src/CS2Econ.Harness/   Standalone validation outside the game: synthetic city,
                       vanilla-baseline A/B, benchmark scenarios, acceptance tests
```

## Running

```bash
dotnet run -c Release --project src/CS2Econ.Harness -- verify     # correctness suite
dotnet run -c Release --project src/CS2Econ.Harness -- scenarios  # §6 acceptance scenarios
dotnet run -c Release --project src/CS2Econ.Harness -- all        # everything → RESULTS.md
dotnet run -c Release --project src/CS2Econ.Harness -- map        # economy on the real Chicago road network
```

To build and install the actual mod on a machine that owns the game, follow
[`MOD-BRINGUP.md`](MOD-BRINGUP.md) (`dotnet build src/CS2Econ.Mod -c Release
-p:InGame=true` plus the Verify_ compile-and-fix pass).
