// Game-facing entry point (PLAN §5, stage 7). Compiles as a documented stub in
// the default OUT_OF_GAME_BUILD; the in-game build (-p:InGame=true) is done on
// the machine that owns CS2, per the version-pinning discipline.
//
// Stage-7 wiring order (research notes §9, PLAN §5):
//   1. Save round-trip spike BEFORE any tier persists state (EconSerialization).
//   2. Boot-time enumeration of Demand|LandValue|Rent|Upkeep|Trade|Spawn systems
//      into the log — EconBridgeSystem.OnCreate (never hardcode decompile guesses).
//   3. Observe-only: engine runs in shadow mode (ShadowAccountingOnly), overlay
//      predictions logged against vanilla outcomes — stage 1 of the build order
//      cannot corrupt a save.
//   4. Replacement: vanilla systems disabled per feature flag, engine authoritative,
//      results written into VANILLA components (LandValue, BuildingCondition,
//      PropertyRenter.m_Rent, level-via-prefab-swap) so flags-off and
//      mod-removed saves stay coherent.
//
// IMod entry shape + UpdateAt registration verified in notes §1 (krzychu124/
// Traffic). SystemUpdatePhase.GameSimulation is NOT among the phase names the
// notes verified (§1: Deserialize, Modification3/4/5, ModificationEnd,
// ToolUpdate, ApplyTool, Rendering, UIUpdate) — so the bridge registers at
// Modification5 per the contract's fallback.

using CS2Econ.Core;
#if !OUT_OF_GAME_BUILD
using System;
using System.Text.RegularExpressions;
using Game;
using Game.Modding;
using Unity.Entities;
using UnityEngine;
#endif

namespace CS2Econ.Mod
{
#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game placeholder keeping the seam type-checked. The
    /// real implementation (below, #if !OUT_OF_GAME_BUILD) follows the IMod
    /// entry shape verified against shipped mods (notes §1).</summary>
    public sealed class Mod
    {
        public static readonly EconParams Params = new EconParams();
        public static readonly FeatureFlags Flags = new FeatureFlags { ShadowAccountingOnly = true };
        public static readonly EconModSettings Settings = new EconModSettings();
    }
#else
    public sealed class Mod : IMod
    {
        /// <summary>The one EconParams/FeatureFlags pair the whole mod shares —
        /// EconBridgeSystem constructs the engine from these; the settings
        /// screen mutates them via EconModSettings.ApplyTo.</summary>
        public static readonly EconParams Params = new EconParams();
        public static readonly FeatureFlags Flags = new FeatureFlags { ShadowAccountingOnly = true };
        public static EconModSettings? Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Debug.Log("[CS2Econ] OnLoad");

            Settings = new EconModSettings(this);
            Verify_RegisterSettings(this, Settings);
            Settings.ApplyTo(Flags, Params);
            Debug.Log($"[CS2Econ] flags: shadow={Flags.ShadowAccountingOnly} A={Flags.TierA_Migration} "
                      + $"B={Flags.TierB_Allocation} C={Flags.TierC_LandAccounting} C2={Flags.TierC2_Leveling} "
                      + $"D={Flags.TierD_Trade} constr={Flags.ConstructionRewire}");

            // GameSimulation is not a notes-verified phase name (§1) → Modification5.
            // The bridge itself paces the engine (Settings.EngineTickFrames game
            // frames per engine tick), so the exact phase only fixes ordering
            // relative to vanilla modification passes.
            updateSystem.UpdateAt<EconBridgeSystem>(SystemUpdatePhase.Modification5);
        }

        public void OnDispose()
        {
            ReenableVanillaEconomySystems();
            Debug.Log("[CS2Econ] OnDispose");
        }

        /// <summary>Undo the bridge's replacement seam: re-enable every disabled
        /// Game.* system matching the same name filter the bridge used to
        /// disable them (empirical enumeration over world.Systems — notes §1).
        /// Deliberately filter-based rather than list-based so disposal works
        /// even if the bridge never finished OnCreate; the (accepted, logged)
        /// tradeoff is re-enabling a matching system some OTHER mod disabled.</summary>
        private static void ReenableVanillaEconomySystems()
        {
            var world = World.DefaultGameObjectInjectionWorld;   // notes §1
            if (world == null || !world.IsCreated) return;
            var filter = new Regex("Demand|LandValue|Rent|Upkeep|Trade|Spawn");
            foreach (var sys in world.Systems)
            {
                if (sys == null) continue;
                string name = sys.GetType().FullName ?? string.Empty;
                if (!name.StartsWith("Game.", StringComparison.Ordinal)) continue;   // vanilla only, never our own
                if (!filter.IsMatch(name)) continue;
                if (!sys.Enabled)
                {
                    sys.Enabled = true;
                    Debug.Log($"[CS2Econ] OnDispose re-enabled {name}");
                }
            }
        }

        private static void Verify_RegisterSettings(Mod mod, EconModSettings settings)
        {
            // VERIFY-INGAME: ModSetting is verified (notes §5.1) but the
            // REGISTRATION calls are template knowledge, not in the notes:
            //   settings.RegisterInOptionsUI()        — Game.Settings.ModSetting
            //   AssetDatabase.global.LoadSettings(...) — Colossal.IO.AssetDatabase
            // Check the official csiimod template / River-Mochi sample on the
            // game machine and fix ONLY this method if either name differs.
            settings.RegisterInOptionsUI();
            Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(
                "CS2EconSpatialDemand", settings, new EconModSettings(mod));
        }
    }
#endif
}
