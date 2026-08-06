// Game-facing entry point (PLAN §5, stage 7). Compiles as documented stubs in
// the default OUT_OF_GAME_BUILD; the in-game build (-p:InGame=true) is done on
// the machine that owns CS2, per the version-pinning discipline.
//
// Stage-7 wiring order (research notes §9, PLAN §5):
//   1. Save round-trip spike BEFORE any tier persists state.
//   2. Boot-time enumeration of Demand|LandValue|Rent|Upkeep|Trade systems
//      (never hardcode decompile guesses).
//   3. Observe-only: engine runs in shadow mode (ShadowAccountingOnly), overlay
//      predictions logged against vanilla outcomes — stage 1 of the build order
//      cannot corrupt a save.
//   4. Replacement: vanilla systems disabled per feature flag, engine authoritative,
//      results written into VANILLA components (LandValue, BuildingCondition,
//      PropertyRenter.m_Rent, level-via-prefab-swap) so flags-off and
//      mod-removed saves stay coherent.

#if !OUT_OF_GAME_BUILD
using Game;
using Game.Modding;
using Game.SceneFlow;
#endif
using CS2Econ.Core;

namespace CS2Econ.Mod
{
#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked. The
    /// real implementation (below, #if !OUT_OF_GAME_BUILD) follows the IMod
    /// entry shape verified against shipped mods (krzychu124/Traffic).</summary>
    public sealed class Mod
    {
        public static readonly EconParams Params = new EconParams();
        public static readonly FeatureFlags Flags = new FeatureFlags { ShadowAccountingOnly = true };
    }
#else
    public sealed class Mod : IMod
    {
        public static readonly EconParams Params = new EconParams();
        public static readonly FeatureFlags Flags = new FeatureFlags { ShadowAccountingOnly = true };

        public void OnLoad(UpdateSystem updateSystem)
        {
            // 1. Enumerate the systems we may later replace — empirical, logged.
            //    (see EconAdapters.EnumerateEconomySystems)
            // 2. Register the observe-only bridge system at a modification phase:
            //    updateSystem.UpdateAt<EconBridgeSystem>(SystemUpdatePhase.Modification4);
            // Replacement of ResidentialDemandSystem / LandValueSystem /
            // RentAdjustSystem / BuildingUpkeepSystem / ZoneSpawnSystem is gated
            // behind Flags, one tier at a time (PLAN §3 build order).
        }

        public void OnDispose() { }
    }
#endif
}
