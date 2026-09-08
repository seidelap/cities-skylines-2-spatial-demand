using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace SpatialDemand.Mod
{
    [FileLocation("SpatialDemand")]
    [SettingsUIGroupOrder("Housing")]
    [SettingsUIShowGroupName("Housing")]
    public sealed class Settings : ModSetting
    {
        public Settings(IMod mod) : base(mod) { SetDefaults(); }

        [SettingsUISection("Main", "Housing")]
        public bool Enabled { get; set; }

        [SettingsUISection("Main", "Housing")]
        public bool ApplyChoices { get; set; }

        public override void SetDefaults() { Enabled = true; ApplyChoices = false; }
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
            [settings.GetOptionLabelLocaleID(nameof(Settings.Enabled))] = "Evaluate housing choices",
            [settings.GetOptionDescLocaleID(nameof(Settings.Enabled))] = "Compare reachable homes using each household's preferences. Results are recorded in the mod log.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyChoices))] = "Apply housing choices (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyChoices))] = "Let supported searches use the new choices. Off observes only. This first version still needs in-game validation; use a test city."
        };
        public void Unload() { }
    }
}
