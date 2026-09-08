using Game;
using Game.Simulation;

namespace SpatialDemand.Mod
{
    // Reuse vanilla's lot geometry and building compatibility shortlist. Temporarily
    // remove its demand threshold while its jobs capture inputs, then restore it.
    // This pair must share ZoneSpawnSystem's interval AND offset.
    public partial class ConstructionProposalSystem : GameSystemBase
    {
        private ZoneSpawnSystem spawn = null!;
        private ConstructionChoiceSystem gate = null!;
        private bool changed, previous;
        protected override void OnCreate()
        {
            base.OnCreate();
            spawn = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            gate = World.GetOrCreateSystemManaged<ConstructionChoiceSystem>();
        }
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => 13;
        protected override void OnUpdate()
        {
            Restore();
            if (!gate.CanGenerate) return;
            previous = spawn.debugFastSpawn;
            changed = true;
            gate.ForcedBatch = true;
            spawn.debugFastSpawn = true;
        }
        internal void Restore()
        {
            if (!changed) return;
            spawn.debugFastSpawn = previous;
            changed = false;
        }
        protected override void OnDestroy() { Restore(); base.OnDestroy(); }
    }

    public partial class ConstructionProposalRestoreSystem : GameSystemBase
    {
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => 13;
        protected override void OnUpdate() => World.GetOrCreateSystemManaged<ConstructionProposalSystem>().Restore();
    }
}
