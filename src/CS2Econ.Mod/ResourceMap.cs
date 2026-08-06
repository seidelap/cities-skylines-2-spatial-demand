// Index mapping between CS2's resource/citizen taxonomies and the economy
// core's compact enums (Res, Segment.All). Pure lookup tables — no ECS access —
// so everything here compiles and is testable out-of-game; the ONE piece that
// needs real game names (the Resource flags-enum member list) is isolated in
// Verify_BuildGameResourceTable per the Verify_ discipline.
//
// Research-note citations:
//   §3 (companies)  — Game.Companies.FreeWorkplaces { m_Uneducated, m_PoorlyEducated,
//                     m_Educated, m_WellEducated, m_HighlyEducated } → education is
//                     five-tier 0..4.
//   §3 (resources)  — Game.Prefabs.ResourceData { m_ChildWeight/m_TeenWeight/
//                     m_AdultWeight/m_ElderlyWeight } → age is four-group 0..3.
//   §3 (resources)  — the Resource TYPE is verified (BuildingPropertyData
//                     m_AllowedSold : Resource); its MEMBERS are not in the dump,
//                     hence the Verify_ method below.

using System;
using CS2Econ.Core;

namespace CS2Econ.Mod
{
    /// <summary>Static two-way mapping: game resource index ↔ core Res, and
    /// (education, age, retired, student) → Segment.All index. Many-to-one on
    /// the resource side: CS2's ~40 resources fold into the core's 11 buckets
    /// (design §4.2's spatial skeleton); ToGameIndex returns each bucket's
    /// canonical (primary) game resource.</summary>
    public static class ResourceMap
    {
        // Resource is a flags enum with well under 64 members; indices come from
        // the game's flag→index helper (see Verify_BuildGameResourceTable).
        private const int MaxGameResources = 64;

        // game index → core bucket (untracked indices sink to Res.Services and
        // report IsTracked=false so callers can skip Money/Mail/Garbage/etc.)
        private static readonly Res[] _coreOfGame = new Res[MaxGameResources];
        private static readonly bool[] _tracked = new bool[MaxGameResources];
        // core bucket → canonical game index (-1 = no game analog)
        private static readonly int[] _gameIndexOfCore = new int[ResourceCatalog.Count];

        static ResourceMap()
        {
#if OUT_OF_GAME_BUILD
            // Out-of-game placeholder: identity over the 11 core resources.
            // Nothing consumes game indices out-of-game (the harness speaks Res
            // directly); the placeholder just keeps the class total and testable.
            for (int g = 0; g < MaxGameResources; g++) { _coreOfGame[g] = Res.Services; _tracked[g] = false; }
            for (int i = 0; i < ResourceCatalog.Count; i++)
            {
                _gameIndexOfCore[i] = i;
                _coreOfGame[i] = (Res)i;
                _tracked[i] = true;
            }
#else
            // In-game: start from a CLEAN slate — every index untracked, every
            // core bucket analog-less — so only Verify_BuildGameResourceTable's
            // explicit Map lines populate the tables. (The identity placeholder
            // above must never leak in-game: game indices 0..10 are arbitrary
            // members of the Resource flags enum, not our buckets.)
            for (int g = 0; g < MaxGameResources; g++) { _coreOfGame[g] = Res.Services; _tracked[g] = false; }
            for (int i = 0; i < ResourceCatalog.Count; i++) _gameIndexOfCore[i] = -1;
            Verify_BuildGameResourceTable();
#endif
        }

        /// <summary>Core bucket for a game resource index. Untracked resources
        /// (Money, mail, garbage, ...) fold into Res.Services — check
        /// IsTracked first when the distinction matters.</summary>
        public static Res ToCore(int gameResourceIndex)
            => gameResourceIndex >= 0 && gameResourceIndex < MaxGameResources
               ? _coreOfGame[gameResourceIndex] : Res.Services;

        /// <summary>True when the game resource participates in the core economy
        /// (i.e. it folds into one of the 11 buckets on purpose, not by default).</summary>
        public static bool IsTracked(int gameResourceIndex)
            => gameResourceIndex >= 0 && gameResourceIndex < MaxGameResources
               && _tracked[gameResourceIndex];

        /// <summary>Canonical game resource index for a core bucket (the primary
        /// member of the fold, e.g. Res.Metals → Resource.Metals even though
        /// Steel/Concrete also fold in). -1 when no game analog exists
        /// (Res.Services is local-only; ResourceCatalog.IsTradable already
        /// excludes it from trade).</summary>
        public static int ToGameIndex(Res r) => _gameIndexOfCore[(int)r];

        /// <summary>Map citizen attributes to a Segment.All index (EconTypes.cs).
        ///   educationLevel: 0..4 (five tiers, notes §3 FreeWorkplaces fields)
        ///   ageGroup:       0..3 Child/Teen/Adult/Elderly (notes §3 ResourceData weights)
        ///
        /// Ambiguity resolution (documented contract): this call site has no
        /// household-composition info, so adults land in the Single* segments;
        /// EconReader promotes to the Family* twin via PromoteToFamily when the
        /// household has multiple members (HouseholdMember buffer). Education 4
        /// maps straight to FamilyEdu — the only Educated-labor segment — so
        /// labor-class fidelity wins over lifecycle there.</summary>
        public static int SegmentFor(int educationLevel, int ageGroup, bool retired, bool student)
        {
            if (student) return 0;                        // StudentLow
            if (retired || ageGroup >= 3)                 // Elderly — the ≥2 split mirrors the
                return educationLevel >= 2 ? 7 : 6;       // SingleSkill threshold: SeniorMid : SeniorLow
            if (ageGroup <= 1) return 0;                  // child/teen dependents ride the low-bid segment
            if (educationLevel >= 4) return 5;            // FamilyEdu (sole Educated-labor segment)
            return educationLevel >= 2 ? 2 : 1;           // SingleSkill : SingleBasic
        }

        /// <summary>Lifecycle promotion for multi-member households (see
        /// SegmentFor doc): SingleBasic→FamilyBasic, SingleSkill→FamilySkill;
        /// everything else already carries its lifecycle.</summary>
        public static int PromoteToFamily(int segment)
            => segment == 1 ? 3 : segment == 2 ? 4 : segment;

#if !OUT_OF_GAME_BUILD
        /// <summary>Replaces the placeholder tables with real game indices.</summary>
        private static void Verify_BuildGameResourceTable()
        {
            // VERIFY-INGAME: the research notes (§3) verify the Resource TYPE but
            // NOT its member names, and EconomyUtils.GetResourceIndex(Resource)
            // is decompile knowledge, not dump-verified. On the game machine:
            // decompile Game.Economy.Resource (flags enum) and Game.Economy.
            // EconomyUtils (flag→index helper; if it is named differently, e.g.
            // a ResourceIterator, adapt ONLY this method). Fix member names
            // below to match the decompile; unknown members are simply dropped
            // from the fold (their spending leaks like other untracked goods).
            void Map(Game.Economy.Resource flag, Res core, bool primary = false)
            {
                int gi = Game.Economy.EconomyUtils.GetResourceIndex(flag);
                if (gi < 0 || gi >= MaxGameResources) return;
                _coreOfGame[gi] = core;
                _tracked[gi] = true;
                if (primary) _gameIndexOfCore[(int)core] = gi;
            }

            // Fold discipline (it is load-bearing, not taste): RAW game
            // resources fold into raw buckets (extractor outputs index the
            // RawCount-sized suitability fields), PROCESSED game resources into
            // processed buckets (industrial output must resolve a recipe via
            // ResourceCatalog.RecipeFor or the firm produces zero). Hence
            // Minerals/Concrete→Metals (processed mineral goods, NOT raw Ore),
            // Pharmaceuticals→Plastics (petrochem chain, NOT Machinery), and
            // Meals→Food (restaurants restock food through freight, keeping
            // them in the spatial economy rather than local-only Services).

            // raws (extractor outputs) — the Weber anchors
            Map(Game.Economy.Resource.Grain, Res.Grain, primary: true);
            Map(Game.Economy.Resource.Vegetables, Res.Grain);
            Map(Game.Economy.Resource.Livestock, Res.Grain);
            Map(Game.Economy.Resource.Cotton, Res.Grain);
            Map(Game.Economy.Resource.Fish, Res.Grain);    // raw food input (fisheries)
            Map(Game.Economy.Resource.Wood, Res.Wood, primary: true);
            Map(Game.Economy.Resource.Ore, Res.Ore, primary: true);
            Map(Game.Economy.Resource.Coal, Res.Ore);
            Map(Game.Economy.Resource.Stone, Res.Ore);
            Map(Game.Economy.Resource.Oil, Res.Oil, primary: true);

            // processed chains
            Map(Game.Economy.Resource.Food, Res.Food, primary: true);
            Map(Game.Economy.Resource.ConvenienceFood, Res.Food);
            Map(Game.Economy.Resource.Meals, Res.Food);
            Map(Game.Economy.Resource.Beverages, Res.Food);
            Map(Game.Economy.Resource.Timber, Res.Timber, primary: true);
            Map(Game.Economy.Resource.Paper, Res.Timber);
            Map(Game.Economy.Resource.Furniture, Res.Timber);
            Map(Game.Economy.Resource.Metals, Res.Metals, primary: true);
            Map(Game.Economy.Resource.Steel, Res.Metals);
            Map(Game.Economy.Resource.Minerals, Res.Metals);
            Map(Game.Economy.Resource.Concrete, Res.Metals);
            Map(Game.Economy.Resource.Plastics, Res.Plastics, primary: true);
            Map(Game.Economy.Resource.Petrochemicals, Res.Plastics);
            Map(Game.Economy.Resource.Chemicals, Res.Plastics);
            Map(Game.Economy.Resource.Pharmaceuticals, Res.Plastics);
            Map(Game.Economy.Resource.Textiles, Res.Plastics);
            Map(Game.Economy.Resource.Machinery, Res.Machinery, primary: true);
            Map(Game.Economy.Resource.Vehicles, Res.Machinery);
            Map(Game.Economy.Resource.Electronics, Res.Machinery);

            // office output (near-exogenous price, design §4.2)
            Map(Game.Economy.Resource.Software, Res.OfficeOutput, primary: true);
            Map(Game.Economy.Resource.Telecom, Res.OfficeOutput);
            Map(Game.Economy.Resource.Financial, Res.OfficeOutput);
            Map(Game.Economy.Resource.Media, Res.OfficeOutput);

            // local-only services (never traded — ResourceCatalog.IsTradable)
            Map(Game.Economy.Resource.Entertainment, Res.Services);
            Map(Game.Economy.Resource.Recreation, Res.Services);
            Map(Game.Economy.Resource.Lodging, Res.Services);
            _gameIndexOfCore[(int)Res.Services] = -1;      // no single game analog
        }
#endif
    }
}
