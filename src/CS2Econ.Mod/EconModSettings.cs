// Options-screen settings: per-tier feature toggles plus the two self-teaching
// sliders (CaptureFraction φ and the structure-tax split-rate leg τ_S — design
// §4.3) and the engine cadence. Subclasses Game.Settings.ModSetting, the
// attribute-driven Options UI verified in notes §5.1 (River-Mochi/CS2-Templates
// working sample). The BASE CLASS is verified; the exact ATTRIBUTE names below
// are template/decompile knowledge — attributes cannot live inside a Verify_
// method, so this file is itself a declared expected-fix site (MOD-BRINGUP.md):
// a wrong attribute name is a localized compile error here and nowhere else.
//
// ApplyTo() is the single mapping from UI state to the core's FeatureFlags/
// EconParams — pure logic, identical in both builds.

using CS2Econ.Core;
#if !OUT_OF_GAME_BUILD
using Colossal.IO.AssetDatabase;   // VERIFY-INGAME: [FileLocation] attribute namespace (template knowledge, not in notes)
using Game.Modding;
using Game.Settings;               // ModSetting base verified (notes §5.1); SettingsUI* attribute names are template knowledge
#endif

namespace CS2Econ.Mod
{
#if OUT_OF_GAME_BUILD
    /// <summary>Out-of-game twin: same properties and ApplyTo mapping, no
    /// ModSetting base. Keep the property list in sync with the in-game
    /// class below (same file, same order).</summary>
    public sealed class EconModSettings
    {
        public bool ShadowAccountingOnly { get; set; } = true;
        public bool TierAMigration { get; set; } = true;
        public bool TierBAllocation { get; set; } = true;
        public bool TierCLandAccounting { get; set; } = true;
        public bool TierC2Leveling { get; set; } = true;
        public bool TierDTrade { get; set; } = true;
        public bool ConstructionRewire { get; set; } = true;
        public int CaptureFractionPct { get; set; } = 95;      // φ ×100
        public int StructureTaxPctPerYear { get; set; } = 0;   // τ_S, % of RC per sim-year
        public int EngineTickFrames { get; set; } = 16;        // game frames per engine tick

        public void SetDefaults()
        {
            ShadowAccountingOnly = true;
            TierAMigration = TierBAllocation = TierCLandAccounting = true;
            TierC2Leveling = TierDTrade = ConstructionRewire = true;
            CaptureFractionPct = 95;
            StructureTaxPctPerYear = 0;
            EngineTickFrames = 16;
        }

        /// <summary>UI state → core knobs. 365 ticks/sim-year matches the
        /// EconParams.HurdleRate comment's calendar.</summary>
        public void ApplyTo(FeatureFlags f, EconParams p)
        {
            f.ShadowAccountingOnly = ShadowAccountingOnly;
            f.TierA_Migration = TierAMigration;
            f.TierB_Allocation = TierBAllocation;
            f.TierC_LandAccounting = TierCLandAccounting;
            f.TierC2_Leveling = TierC2Leveling;
            f.TierD_Trade = TierDTrade;
            f.ConstructionRewire = ConstructionRewire;
            p.CaptureFraction = CaptureFractionPct / 100.0;
            p.StructureTaxRate = StructureTaxPctPerYear / 100.0 / 365.0;
        }
    }
#else
    [FileLocation("ModsSettings/CS2Econ/CS2Econ")]                       // VERIFY-INGAME: attribute + path convention
    [SettingsUIGroupOrder(kTierGroup, kLandGroup, kEngineGroup)]         // VERIFY-INGAME: attribute name
    [SettingsUIShowGroupName(kTierGroup, kLandGroup, kEngineGroup)]      // VERIFY-INGAME: attribute name
    public sealed class EconModSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kTierGroup = "Feature tiers";
        public const string kLandGroup = "Land accounting";
        public const string kEngineGroup = "Engine";

        public EconModSettings(IMod mod) : base(mod) => SetDefaults();

        // ---- staging master switch (stage 3 of the wiring order) ----------
        /// <summary>Assess and log everything, levy nothing; vanilla systems
        /// stay authoritative. The safe default for a first load on any save.</summary>
        [SettingsUISection(kSection, kTierGroup)]
        public bool ShadowAccountingOnly { get; set; } = true;

        // ---- per-tier toggles (FeatureFlags mirror; each independently
        //      revertible — design §3) ------------------------------------
        [SettingsUISection(kSection, kTierGroup)]
        public bool TierAMigration { get; set; } = true;

        [SettingsUISection(kSection, kTierGroup)]
        public bool TierBAllocation { get; set; } = true;

        [SettingsUISection(kSection, kTierGroup)]
        public bool TierCLandAccounting { get; set; } = true;

        [SettingsUISection(kSection, kTierGroup)]
        public bool TierC2Leveling { get; set; } = true;

        [SettingsUISection(kSection, kTierGroup)]
        public bool TierDTrade { get; set; } = true;

        [SettingsUISection(kSection, kTierGroup)]
        public bool ConstructionRewire { get; set; } = true;

        // ---- the two self-teaching sliders (design §4.3) ------------------
        /// <summary>φ ×100: share of assessed land rent captured as the levy.</summary>
        [SettingsUISlider(min = 0, max = 100, step = 1)]   // VERIFY-INGAME: slider attribute signature
        [SettingsUISection(kSection, kLandGroup)]
        public int CaptureFractionPct { get; set; } = 95;

        /// <summary>τ_S as % of replacement cost per sim-year (default 0 — the
        /// split-rate lever players can pull to feel the deadweight).</summary>
        [SettingsUISlider(min = 0, max = 10, step = 1)]    // VERIFY-INGAME: slider attribute signature
        [SettingsUISection(kSection, kLandGroup)]
        public int StructureTaxPctPerYear { get; set; } = 0;

        // ---- cadence ------------------------------------------------------
        /// <summary>Game frames per engine tick (EconBridgeSystem.OnUpdate).</summary>
        [SettingsUISlider(min = 4, max = 64, step = 4)]    // VERIFY-INGAME: slider attribute signature
        [SettingsUISection(kSection, kEngineGroup)]
        public int EngineTickFrames { get; set; } = 16;

        public override void SetDefaults()
        {
            ShadowAccountingOnly = true;
            TierAMigration = TierBAllocation = TierCLandAccounting = true;
            TierC2Leveling = TierDTrade = ConstructionRewire = true;
            CaptureFractionPct = 95;
            StructureTaxPctPerYear = 0;
            EngineTickFrames = 16;
        }

        /// <summary>UI state → core knobs. 365 ticks/sim-year matches the
        /// EconParams.HurdleRate comment's calendar. (Keep in sync with the
        /// out-of-game twin above.)</summary>
        public void ApplyTo(FeatureFlags f, EconParams p)
        {
            f.ShadowAccountingOnly = ShadowAccountingOnly;
            f.TierA_Migration = TierAMigration;
            f.TierB_Allocation = TierBAllocation;
            f.TierC_LandAccounting = TierCLandAccounting;
            f.TierC2_Leveling = TierC2Leveling;
            f.TierD_Trade = TierDTrade;
            f.ConstructionRewire = ConstructionRewire;
            p.CaptureFraction = CaptureFractionPct / 100.0;
            p.StructureTaxRate = StructureTaxPctPerYear / 100.0 / 365.0;
        }
    }
#endif
}
