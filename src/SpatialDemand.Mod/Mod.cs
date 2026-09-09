using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;

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
            updateSystem.UpdateAt<BusinessChoiceSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ShoppingChoiceSystem, ResourceBuyerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ShoppingChoiceSystem>(SystemUpdatePhase.PreDeserialize);
            updateSystem.UpdateAfter<LaborChoiceSystem, WorkProviderSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ConstructionProposalSystem, ZoneSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<ConstructionProposalRestoreSystem, ZoneSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ConstructionChoiceSystem, GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            NegotiatedPayrollHooks.Install();
            Log.Info("Spatial Demand 0.5: comparable pedestrian shopping routes and optional negotiated retention wages. Purchase and payroll transactions remain game-owned. Shopping, wages and construction Apply are experimental.");
        }

        public void OnDispose()
        {
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<ConstructionProposalSystem>()?.Restore();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<ShoppingChoiceSystem>()?.Restore();
            World.DefaultGameObjectInjectionWorld?.EntityManager.CompleteAllTrackedJobs();
            NegotiatedPayrollHooks.Uninstall();
            Settings?.UnregisterInOptionsUI();
            Settings = null;
            // No vanilla system is disabled; restore the temporary proposal flag above.
        }
    }
}
