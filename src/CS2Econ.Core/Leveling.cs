using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Tier C′ (design §4.4): renovation-in-place on the escrow funding
    /// clock; downgrade as the same mechanism sign-flipped (underfunded S decays
    /// condition — no separate decay subsystem); scrape-and-rebuild gated on
    /// physical vacancy via warehousing; owner-tag consent gates.</summary>
    public static class Leveling
    {
        public static void Step(WorldState w, AccessState acc, IPriceContext prices,
                                EconParams p, FeatureFlags flags, List<string>? events = null)
        {
            foreach (var pl in w.Parcels)
            {
                if (pl.State != ParcelState.Built) continue;

                // ---- stalled-redevelopment consolation: escrow drains into
                // condition when the wedge has collapsed (keeps the ledger honest).
                if (pl.Wedge <= 1e-9 && pl.Escrow > 0 && pl.Condition < 0.999)
                {
                    double rc = p.RC(pl.Level, pl.Units);
                    double amt = Math.Min(pl.Escrow, p.EscrowToConditionRate * rc * (1 - pl.Condition));
                    if (amt > 0)
                    {
                        pl.Escrow -= amt;
                        pl.Condition = Math.Min(1.0, pl.Condition + amt / rc);
                        w.Ledger.Transfer(Account.Escrow, Account.OutsideWorld, amt); // renewal materials
                    }
                }

                if (!flags.TierC2_Leveling) continue;

                // ---- renovation in place: same use, higher supported level ----
                if (!pl.TargetIsScrape && pl.TargetUse == pl.Use && pl.TargetLevel > pl.Level)
                {
                    double cost = Math.Max(0, p.RC(pl.TargetLevel, pl.Units)
                                              - pl.Condition * p.RC(pl.Level, pl.Units));
                    bool ownerGate = pl.OwnerOccupied && pl.OccupantHouseholds.Count > 0;
                    if (!ownerGate && pl.Escrow >= cost && cost > 0)
                    {
                        pl.Escrow -= cost;
                        w.Ledger.Transfer(Account.Escrow, Account.PhantomDeveloper, cost);
                        w.Ledger.Transfer(Account.PhantomDeveloper, Account.OutsideWorld, cost);
                        pl.Level = pl.TargetLevel;
                        pl.Condition = 1.0;
                        // Everyone re-anchors at the new cost basis (§4.4)
                        // immediately. Redundant with the co-op re-rate later
                        // this tick, but keeps the renovation self-contained:
                        // the new basis is visible to anything reading charges
                        // between here and RerateAndRelocation.
                        double a = LandAccounting.UnitAssessment(pl, p);
                        foreach (int hid in pl.OccupantHouseholds)
                            w.Households[hid].ChargedAssessment = a;
                        w.RenovationsTotal++;
                        events?.Add($"renovate parcel {pl.Id} -> L{pl.Level}");
                    }
                }

                // ---- scrape-and-rebuild: needs sustained gap + physical vacancy
                if (pl.TargetIsScrape)
                {
                    double vNow = pl.Condition * p.RC(pl.Level, pl.Units);
                    int newUnits = LandAccounting.UnitsFor(pl.TargetUse);
                    double cost = Math.Max(0, p.DemolitionPerUnit * pl.Units + p.RC(pl.TargetLevel, newUnits)
                                  - p.SalvageFraction * vNow);
                    bool pressure = pl.Wedge > 0.15 * Math.Max(1e-9, Math.Abs(pl.CurrentResidual) + pl.Wedge);
                    pl.ScrapePressure = pressure ? pl.ScrapePressure + 1 : 0;

                    bool ownerGate = pl.OwnerOccupied && pl.OccupantHouseholds.Count > 0;
                    if (!ownerGate && pl.Escrow >= cost && pl.ScrapePressure >= p.ScrapePressureTicks)
                        pl.Warehousing = true;   // vacated units stop re-letting; forgone rent is the brake

                    if (pl.Warehousing && pl.OccupantHouseholds.Count == 0 && pl.OccupantFirm < 0)
                    {
                        if (pl.Escrow < cost)
                        {
                            // The redevelopment attempt failed to stay funded
                            // (vacancy drain while empty): cancel rather than
                            // deadlock an unlettable ruin (scrutiny finding #17).
                            pl.Warehousing = false;
                            pl.ScrapePressure = 0;
                        }
                        else
                        {
                            pl.Escrow -= cost;
                            w.Ledger.Transfer(Account.Escrow, Account.PhantomDeveloper, cost);
                            w.Ledger.Transfer(Account.PhantomDeveloper, Account.OutsideWorld, cost);
                            pl.State = ParcelState.UnderConstruction;
                            pl.Use = pl.TargetUse;
                            pl.Level = pl.TargetLevel;
                            pl.Units = newUnits;
                            pl.BuildProgress = 0;
                            pl.BuildTotal = p.ConstructionLag;
                            pl.CommittedCost = 0;   // already paid from escrow
                            pl.Warehousing = false;
                            pl.ScrapePressure = 0;
                            w.Claims.Add(pl.Cluster, pl.Use, newUnits);
                            w.ScrapesTotal++;
                            events?.Add($"scrape parcel {pl.Id} -> {pl.Use} L{pl.Level}");
                        }
                    }
                    else if (!pressure && pl.Warehousing)
                    {
                        pl.Warehousing = false;   // gap collapsed: resume letting
                    }
                }
                else if (pl.Warehousing) pl.Warehousing = false;
            }
        }
    }
}
