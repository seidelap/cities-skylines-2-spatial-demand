using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace SpatialDemand.Mod
{
    [FileLocation("SpatialDemand")]
    [SettingsUIGroupOrder("Housing", "Business")]
    [SettingsUIShowGroupName("Housing", "Business")]
    public sealed class Settings : ModSetting
    {
        public Settings(IMod mod) : base(mod) { SetDefaults(); }

        [SettingsUISection("Main", "Housing")]
        public bool Enabled { get; set; }

        [SettingsUISection("Main", "Housing")]
        public bool ApplyChoices { get; set; }

        [SettingsUISection("Main", "Business")]
        public bool BusinessEnabled { get; set; }
        [SettingsUISection("Main", "Business")]
        public bool ApplyBusinessChoices { get; set; }
        [SettingsUISection("Main", "Business")]
        public int BusinessRangeMetres { get; set; }
        [SettingsUISection("Main", "Business")]
        public float DeliveryCostPerUnitKm { get; set; }

        public override void SetDefaults()
        {
            Enabled = true; ApplyChoices = false; BusinessEnabled = true;
            ApplyBusinessChoices = false; BusinessRangeMetres = 2000; DeliveryCostPerUnitKm = 1;
        }
    }

    public sealed class Locale : IDictionarySource
    {
        private readonly Settings settings;
        public Locale(Settings settings) { this.settings = settings; }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts) => new Dictionary<string, string>
        {
            [settings.GetSettingsLocaleID()] = "Spatial Demand",
            [settings.GetOptionTabLocaleID("Main")] = "Main",
            [settings.GetOptionGroupLocaleID("Housing")] = "Housing",
            [settings.GetOptionGroupLocaleID("Business")] = "Business entry",
            [settings.GetOptionLabelLocaleID(nameof(Settings.BusinessEnabled))] = "Evaluate new businesses",
            [settings.GetOptionDescLocaleID(nameof(Settings.BusinessEnabled))] = "Compare compatible activities at vacant premises using current buyers and local supplier stock. Log proposals and reasons for rejection.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyBusinessChoices))] = "Open selected businesses (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyBusinessChoices))] = "Create at most one additional company per game day using its chosen activity. Vanilla entry remains active. Use a disposable test city.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.BusinessRangeMetres))] = "Business search radius (metres)",
            [settings.GetOptionDescLocaleID(nameof(Settings.BusinessRangeMetres))] = "Straight-line approximation for buyers and input suppliers; this does not prove a road connection exists.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.DeliveryCostPerUnitKm))] = "Estimated delivery cost per unit per kilometre",
            [settings.GetOptionDescLocaleID(nameof(Settings.DeliveryCostPerUnitKm))] = "Explicit planning assumption, not a measured transport charge. Actual purchases use the game's routing.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.Enabled))] = "Evaluate housing choices",
            [settings.GetOptionDescLocaleID(nameof(Settings.Enabled))] = "Compare reachable homes using each household's preferences. Results are recorded in the mod log.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyChoices))] = "Apply housing choices (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyChoices))] = "Let supported searches use the new choices. Off observes only. This first version still needs in-game validation; use a test city."
        };
        public void Unload() { }
    }
}
