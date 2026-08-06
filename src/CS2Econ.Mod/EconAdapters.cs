// The reader/writer adapters — the ONLY code that touches ECS components.
// Documented stubs out-of-game; each mapping cites the verified component from
// the research notes (§3) it reads or writes, so a game patch's repair diff is
// confined to this file.

using System;
using System.Collections.Generic;
using CS2Econ.Core;

namespace CS2Econ.Mod
{
    /// <summary>ECS → WorldState reader (stage-7). Mappings, all against
    /// [verified-dump] components unless noted:
    ///
    ///   Clusters      ← nested-dissection cells from the routing rebuild's
    ///                   ClusterCache (IAccessCosts is implemented over its CCH
    ///                   cluster costs + corridor dirty flags — design §3).
    ///   Parcel        ← Game.Zones.Block (m_Position/m_Size) + Game.Prefabs.
    ///                   SpawnableBuildingData (m_Level — level lives on the
    ///                   PREFAB; level change = prefab swap) + BuildingCondition.
    ///   Household     ← Game.Citizens.Household (m_Resources = money) +
    ///                   Citizen (wellbeing, age→lifecycle) + Worker
    ///                   (m_LastCommuteTime = realized commute telemetry).
    ///   Firm          ← Game.Companies.* (WorkProvider.m_MaxWorkers → JobSlots,
    ///                   Profitability, ResourceExporter/ResourceBuyer).
    ///   TradeExit     ← Game.Objects.OutsideConnection + OutsideTradeParameterData
    ///                   (per-mode weight/distance multipliers seed t; the
    ///                   region-size knob seeds ρ).
    ///   Segment       ← household wealth/education/lifecycle per the §4.2
    ///                   income × education × lifecycle decomposition.
    /// </summary>
    public static class EconReader
    {
#if OUT_OF_GAME_BUILD
        public static WorldState Read() =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
#endif
    }

    /// <summary>WorldState → ECS writer. Results land in VANILLA components
    /// wherever vanilla systems also read them (PLAN §5: a save opened without
    /// the mod stays coherent; feature-flags-off degrades cleanly):
    ///
    ///   assessments   → PropertyRenter.m_Rent (occupant payment), and the land
    ///                   component mirrored into Game.Net.LandValue for vanilla
    ///                   readers (LandValueSystem disabled — LandValueOverhaul
    ///                   precedent proves this seam post-Economy-2.0).
    ///   condition     → Game.Buildings.BuildingCondition.m_Condition.
    ///   level changes → prefab swap on SpawnableBuildingData (verification
    ///                   item #7: renters must survive the swap — fallback is
    ///                   evict-and-rehouse through the allocation machinery).
    ///   construction  → spawn requests into ZoneSpawnSystem's queue with our
    ///                   site selection; Game.Objects.UnderConstruction carries
    ///                   the lag (vanilla already models it).
    ///   trade prices  → the Tier D injection point is verification item #8:
    ///                   ResourceSystem prefab table vs per-instance
    ///                   Game.Economy.ResourceInfo — decompile decides.
    ///
    ///   Mod-native state (escrow, anniversary phase, owner tag, trade EMAs,
    ///   migration scalars, claims ledger, calibration factors) rides custom
    ///   ISerializable components — pending the §7 save spike, schema-versioned
    ///   from day one.
    /// </summary>
    public static class EconWriter
    {
#if OUT_OF_GAME_BUILD
        public static void Write(WorldState w) =>
            throw new NotSupportedException("in-game build only (dotnet build -p:InGame=true)");
#endif
    }
}
