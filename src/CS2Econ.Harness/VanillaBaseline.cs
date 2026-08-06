using System;
using System.Collections.Generic;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>The design-doc §1.1 vanilla model, as a switchable harness mode
    /// for A/B tests: construction driven by GLOBAL demand scalars (citywide
    /// vacancy suppresses demand everywhere one-for-one), sites chosen roughly
    /// UNIFORMLY among eligible zoned cells, buildings spawn at level 1 and grind
    /// uniformly toward max. Run with FeatureFlags.ConstructionRewire = false and
    /// TierC2_Leveling = false so the engine's residual-driven construction and
    /// ℓ*-driven leveling stand down; everything else (rents, allocation,
    /// payments) stays identical between modes so the A/B isolates the mechanism
    /// under test.</summary>
    public sealed class VanillaSpawner
    {
        public int MaxStartsPerTick = 3;
        public int LevelUpAgeTicks = 220;    // uniform grind cadence

        public void Step(WorldState w, EconParams p)
        {
            // ---- global demand scalar per broad type -------------------------
            int seekers = 0, resVacant = 0;
            foreach (var h in w.Households)
                if (h.ExitedTick < 0 && h.HomeParcel < 0) seekers++;
            double firmVacantSlots = 0;
            int firmDemandProxy = 0;
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;
                if (pl.IsResidential) resVacant += pl.Vacant;
                else if (pl.OccupantFirm < 0) firmVacantSlots += pl.Units;
            }
            foreach (var f in w.Firms) if (!f.Dead && f.ProfitEma > 0) firmDemandProxy++;

            // Vanilla's signature: vacancy ANYWHERE suppresses demand EVERYWHERE.
            double resDemand = seekers - 0.9 * resVacant;
            double firmDemand = firmDemandProxy * 0.15 - 0.5 * firmVacantSlots / 10.0;

            // ---- uniform site selection among eligible parcels ---------------
            var eligible = new List<int>();
            for (int i = 0; i < w.Parcels.Count; i++)
            {
                var pl = w.Parcels[i];
                if (pl.State != ParcelState.Empty || pl.Zoned == ZoneKind.None) continue;
                bool residential = pl.Zoned == ZoneKind.ResidentialLow || pl.Zoned == ZoneKind.ResidentialHigh;
                if (residential && resDemand <= 0) continue;
                if (!residential && firmDemand <= 0) continue;
                eligible.Add(i);
            }
            int starts = Math.Min(MaxStartsPerTick, eligible.Count);
            for (int k = 0; k < starts; k++)
            {
                int pick = eligible[w.Rng.NextInt(eligible.Count)];
                var pl = w.Parcels[pick];
                if (pl.State != ParcelState.Empty) continue;
                pl.State = ParcelState.UnderConstruction;
                pl.Use = pl.Zoned;
                pl.Level = 1;                       // vanilla: nothing decrees level 4
                pl.Units = LandAccounting.UnitsFor(pl.Zoned);
                pl.BuildProgress = 0;
                pl.BuildTotal = p.ConstructionLag;
                pl.CommittedCost = p.RC(1, pl.Units);
                w.Claims.Add(pl.Cluster, pl.Use, pl.Units);   // keep ledger coherent either way
            }

            // ---- uniform level grind -----------------------------------------
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built || pl.Level >= p.MaxLevel) continue;
                if (pl.CompletedTick < 0 || w.Tick - pl.CompletedTick < LevelUpAgeTicks) continue;
                if (pl.Condition < 0.5) continue;
                // Grind: age + occupancy is all it takes, geography-blind.
                bool occupied = pl.IsResidential ? pl.OccupantHouseholds.Count > 0 : pl.OccupantFirm >= 0;
                if (occupied && w.Rng.NextDouble() < 0.01)
                {
                    pl.Level++;
                    pl.CompletedTick = w.Tick;
                }
            }
        }
    }
}
