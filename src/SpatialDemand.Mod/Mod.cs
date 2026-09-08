using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;

namespace SpatialDemand.Mod
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger("SpatialDemand").SetShowsErrorsInUI(false);
        internal static Settings? Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Settings = new Settings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new Locale(Settings));
            AssetDatabase.global.LoadSettings("SpatialDemand", Settings, new Settings(this));
            updateSystem.UpdateBefore<HousingChoiceSystem, HouseholdFindPropertySystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("Spatial Demand: housing choice prototype loaded; see Options for observe/apply mode.");
        }

        public void OnDispose()
        {
            Settings?.UnregisterInOptionsUI();
            Settings = null;
            // No vanilla system is disabled, so there is nothing to re-enable.
        }
    }
}
