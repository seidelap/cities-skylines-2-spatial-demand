using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace SpatialDemand.Mod
{
    [FileLocation("SpatialDemand")]
    [SettingsUIGroupOrder("Housing", "Business", "Construction", "Diagnostics")]
    [SettingsUIShowGroupName("Housing", "Business", "Construction", "Diagnostics")]
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
        [SettingsUISection("Main", "Diagnostics")]
        public bool ShoppingEnabled { get; set; }
        [SettingsUISection("Main", "Diagnostics")]
        public bool ApplyShoppingChoices { get; set; }
        [SettingsUISection("Main", "Diagnostics")]
        public bool LaborEnabled { get; set; }
        [SettingsUISection("Main", "Diagnostics")]
        public bool ApplyNegotiatedWages { get; set; }
        [SettingsUISection("Main", "Diagnostics")]
        public float ShoppingTimeValuePerHour { get; set; }

        [SettingsUISection("Main", "Construction")]
        public bool ConstructionEnabled { get; set; }
        [SettingsUISection("Main", "Construction")]
        public bool ApplyConstruction { get; set; }
        [SettingsUISection("Main", "Construction")]
        public int ConstructionCostPerCell { get; set; }
        [SettingsUISection("Main", "Construction")]
        public int ConstructionPaybackDays { get; set; }
        [SettingsUISection("Main", "Construction")]
        public float ConstructionTravelKph { get; set; }

        public override void SetDefaults()
        {
            Enabled = true; ApplyChoices = false; BusinessEnabled = true;
            ApplyBusinessChoices = false; BusinessRangeMetres = 0;
            ShoppingEnabled = false; LaborEnabled = false; ShoppingTimeValuePerHour = 1;
            ApplyShoppingChoices = false; ApplyNegotiatedWages = false;
            ConstructionEnabled = false; ApplyConstruction = false;
            ConstructionCostPerCell = 1000; ConstructionPaybackDays = 32; ConstructionTravelKph = 30;
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
            [settings.GetOptionGroupLocaleID("Diagnostics")] = "Shopping and labor diagnostics",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ShoppingEnabled))] = "Explain shopping prices and trips",
            [settings.GetOptionDescLocaleID(nameof(Settings.ShoppingEnabled))] = "Request comparable walking routes to stocked shops and compare basket price plus travel time. Observe leaves the existing shopping trip unchanged.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyShoppingChoices))] = "Choose walking shopping trips (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyShoppingChoices))] = "For supported full-basket pedestrian searches, compare up to eight real routes and hand the complete winning route to the game's purchase system. One held search at a time; unsupported or interrupted searches retain the original route. Requires shopping evaluation.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.LaborEnabled))] = "Evaluate hypothetical wage offers",
            [settings.GetOptionDescLocaleID(nameof(Settings.LaborEnabled))] = "Compare workers' outside options with employers' hiring budgets, current wages and the option of leaving a job empty. Job changes are forecasts only; the separate wage option can apply qualified retention raises.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyNegotiatedWages))] = "Apply negotiated retention wages (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyNegotiatedWages))] = "Save affordable raises for current local private-company employees when competing bids justify retention. Original game payroll settles money and taxes. Required wage-reading jobs run synchronously, which may slow large cities. Turning this off restores vanilla salaries; saved agreements remain dormant. New agreements require labor evaluation.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ShoppingTimeValuePerHour))] = "Estimated value of travel time (currency/hour)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ShoppingTimeValuePerHour))] = "Shared time preference for shopping, business retail forecasts and labor diagnostics. This is not a fare or cash debit. Estimates use the configured commute speed; actual game routing remains in control.",
            [settings.GetOptionGroupLocaleID("Construction")] = "Construction (experimental)",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ConstructionEnabled))] = "Evaluate construction projects",
            [settings.GetOptionDescLocaleID(nameof(Settings.ConstructionEnabled))] = "Forecast tenant-supported rents before a zoned building is created. Observe leaves construction unchanged. Both construction switches start off; this adapter still requires in-game validation.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyConstruction))] = "Require tenant-backed construction (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyConstruction))] = "Bypass the demand threshold for zoned proposals, then allow only the best project whose forecast rents repay its costs. At most one project at a time and per game day. Forecasts do not guarantee occupancy or change actual rents. Existing households and local stocks are required; an empty city may wait indefinitely. Use an established disposable test city.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ConstructionCostPerCell))] = "Fallback construction cost per zoning cell",
            [settings.GetOptionDescLocaleID(nameof(Settings.ConstructionCostPerCell))] = "Planning assumption used when a building prefab has no positive construction cost. No money is withdrawn by this prototype.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ConstructionPaybackDays))] = "Required payback horizon (game days)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ConstructionPaybackDays))] = "Forecast rent after upkeep must repay construction within this horizon. Business current orders are treated as a daily sales proxy, not measured recurring demand.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ConstructionTravelKph))] = "Estimated travel speed (km/h)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ConstructionTravelKph))] = "Shared straight-line estimate for construction home comparisons, retail forecasts and labor diagnostics. It does not establish road access or predict the eventual routed choice.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.BusinessEnabled))] = "Evaluate new businesses",
            [settings.GetOptionDescLocaleID(nameof(Settings.BusinessEnabled))] = "Compare compatible activities at vacant premises using current buyers and local supplier stock. Log proposals and reasons for rejection.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyBusinessChoices))] = "Open selected businesses (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyBusinessChoices))] = "Create at most one additional company per game day using its chosen activity. Vanilla entry remains active. Use a disposable test city.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.BusinessRangeMetres))] = "Optional forecast distance limit (metres; 0 means none)",
            [settings.GetOptionDescLocaleID(nameof(Settings.BusinessRangeMetres))] = "Default zero compares the bounded supplier/buyer sample without a hard distance cutoff. Offers compete on cost including transport. Positive values limit the forecast only, not actual cim shopping.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.Enabled))] = "Evaluate housing choices",
            [settings.GetOptionDescLocaleID(nameof(Settings.Enabled))] = "Compare reachable homes using each household's preferences. Results are recorded in the mod log.",
            [settings.GetOptionLabelLocaleID(nameof(Settings.ApplyChoices))] = "Apply housing choices (experimental)",
            [settings.GetOptionDescLocaleID(nameof(Settings.ApplyChoices))] = "Let supported searches use the new choices. Off observes only. This first version still needs in-game validation; use a test city."
        };
        public void Unload() { }
    }
}
