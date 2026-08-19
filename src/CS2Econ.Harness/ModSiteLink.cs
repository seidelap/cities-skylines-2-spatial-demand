// THE FIRST OUT-OF-GAME COVERAGE THE MOD ARM HAS. TestRunner's coverage note
// (item 9, "THE MOD ADAPTERS (CS2Econ.Mod) AND CityImport ARE NOT COVERED")
// is still true of everything else in CS2Econ.Mod and stays true here: this
// fixture covers ONE thing, the firm↔site link EconReader maintains, and it
// covers it because that link was found broken in three places and the failure
// mode is silent.
//
// WHY IT IS A SEPARATE COMMAND AND NOT A `verify` CHECK. The reader's real body
// lives behind `#if !OUT_OF_GAME_BUILD` and is not compiled on this machine at
// all (there are no shims — the OUT_OF_GAME_BUILD half of EconReader.cs is a
// stub that throws). What CAN be driven here is the part of the reader that was
// deliberately hoisted out of the #if for exactly this reason:
// EconSiteLink — the same methods, the same code, the shipped ones. That is the
// same discipline EconWriter.EconAssess and EconSeams already follow. What is
// NOT covered is the surrounding ECS traversal: whether AddFirm calls Attach on
// the right entity, whether SyncParcels really runs before SyncFirms. Those are
// read-and-reason and the bring-up log (EconBridgeSystem.StatusLine prints the
// audit).
//
// THE FIXTURE. A real synthetic city, run far enough that the ENGINE has sited
// firms on its own, and then the three reader paths replayed over that world:
// a new company claiming a site (AddFirm), and a building despawning under its
// occupants (SyncParcels). Both legs assert EconSiteLink.Audit reads zero, and
// each is paired with a floor leg that counts the population it quantifies
// over — an audit over no firms is zero for the wrong reason.
//
// MUTANTS (Program.cs `modsync --mutant-…`), each restoring one of the shapes
// that was actually in EconReader:
//   --mutant-site-steal              EconSiteLink.MutantSiteStealing
//   --mutant-demolition-keeps-site   EconSiteLink.MutantDemolitionKeepsFirmSite
// Measured on this container, seed 20260806 (the harness default), at the
// commit that introduced this file — 6 checks, 3 assertions and 3 paired
// floors:
//   modsync                                  6/6 PASS
//   modsync --mutant-site-steal              5/6  contest leg FAILS: 0/59
//                                                 claims refused, audit rises
//                                                 by 59 orphaned firms
//   modsync --mutant-demolition-keeps-site   5/6  demolition leg FAILS: 0/59
//                                                 evicted firms unsited, audit
//                                                 rises by 59 orphaned firms
// Each mutant reds exactly ONE assertion leg: the legs assert on the audit
// DELTA they themselves caused, so the second leg does not inherit the first
// leg's damage. No floor leg moves under either mutant, which is what says the
// floors are measuring the population and not the defect.
//
// THE FLOOR BOUND (20) IS MEASURED, NOT PICKED. Sited live firms after 120
// ticks, this fixture, nine seeds: 0→64, 1→63, 9→55, 13→61, 25→67, 138→56,
// 549→63, 910→56, 20260806→59. Every seed is 6/6. The bound sits well under
// the observed minimum of 55 so it fails on a fixture that stopped producing
// firms, not on ordinary seed variation.

using System;
using System.Collections.Generic;
using CS2Econ.Core;
using CS2Econ.Mod;

namespace CS2Econ.Harness
{
    public static class ModSiteLink
    {
        /// <summary>Ticks before the reader paths are replayed. Long enough that
        /// the engine's own entry/relocation/exit machinery has run many times
        /// over the fixture's firms — the point of using a live world rather
        /// than a hand-built one is that the population under audit is the
        /// engine's, not the test's.</summary>
        private const int Ticks = 120;

        private static readonly List<(string name, bool pass, string detail)> Results
            = new List<(string, bool, string)>();

        private static void Check(string name, bool pass, string detail = "")
        {
            Results.Add((name, pass, detail));
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
        }

        public static int Run(ulong seed)
        {
            Results.Clear();
            Console.WriteLine($"=== mod site-link invariant (seed {seed}, {Ticks} ticks) ===");

            var p = new EconParams();
            var flags = new FeatureFlags();
            var cfg = new SyntheticCity.Config { Cols = 8, Rows = 8, SeedHouseholds = 1500, Seed = seed };
            var sim = Sim.Create(cfg, p, flags);
            sim.Run(Ticks);
            var w = sim.W;

            // ---- LEG 0: the world the legs below start from is clean --------
            // The engine maintains the link itself (entry at EconomyEngine.cs:1838,
            // relocation at :1874-1876, exit at :1791 all write both ends), so a
            // non-zero audit here would mean the fixture, not the reader, is the
            // thing under test. Paired with the population the audit ran over:
            // "no violations" over no firms is a vacuous zero.
            //
            // This leg has no mutant of its own — the code it would have to
            // break lives in EconomyEngine.cs. What makes it a real assertion
            // rather than a formality is that the SAME Audit call is driven to
            // 59 by both mutants below, so the instrument is demonstrably able
            // to report a violation on this fixture; this leg is the statement
            // that it does not report one before the reader's paths run.
            int sited = 0, liveFirms = 0;
            foreach (var f in w.Firms) { if (f.Dead) continue; liveFirms++; if (f.Parcel >= 0) sited++; }
            int before = EconSiteLink.Audit(w, out int beforeFirms, out int beforeSites);
            Check("site-link floor: the engine's own world has a real sited-firm population to audit",
                  sited >= 20,
                  $"{sited} of {liveFirms} live firms hold a site after {Ticks} ticks "
                  + $"(bound 20; the two legs below quantify over these)");
            Check("site-link: the engine alone keeps every firm and every site agreeing",
                  before == 0,
                  $"{beforeFirms} live firms point at a site they do not hold, "
                  + $"{beforeSites} sites name a firm that is not standing on them (bound 0/0)");

            // ---- LEG 1: a newcomer cannot take a site out from under a firm --
            // The AddFirm shape: a company entity resolves through ParcelIndex
            // onto a parcel and a Firm is created for it. When that parcel is
            // already held, the claim must be REFUSED and the newcomer left
            // unsited — writing the parcel's OccupantFirm anyway leaves the
            // incumbent live and still pointing at a parcel that names somebody
            // else, which is invisible to `f.Dead || f.Parcel < 0`.
            var contested = new List<(Firm incumbent, int parcel)>();
            foreach (var pl in w.Parcels)
            {
                if (pl.OccupantFirm < 0) continue;
                var inc = w.Firms[pl.OccupantFirm];
                if (inc.Dead || inc.Parcel != pl.Id) continue;
                contested.Add((inc, pl.Id));
            }
            int refused = 0, incumbentsKept = 0, newcomersUnsited = 0;
            foreach (var (inc, pid) in contested)
            {
                // Exactly what AddFirm now does: register first, then claim.
                var newcomer = new Firm
                {
                    Id = w.Firms.Count, Sector = w.Parcels[pid].Use, Parcel = -1,
                    Money = p.FirmSeedCapital, JobSlots = 1, EnteredTick = w.Tick,
                };
                w.Firms.Add(newcomer);
                if (!EconSiteLink.Attach(w, newcomer, pid)) refused++;
                if (inc.Parcel == pid && w.Parcels[pid].OccupantFirm == inc.Id) incumbentsKept++;
                if (newcomer.Parcel < 0) newcomersUnsited++;
            }
            // The audit is a whole-world count, so each leg asserts on its own
            // DELTA: a leg that ran after damage another leg did would otherwise
            // inherit that damage and both legs would red on one mutant, which
            // would say nothing about which of them tests what.
            int afterContest = EconSiteLink.Audit(w, out int contestFirms, out int contestSites);
            int contestDelta = afterContest - before;
            Check("site-link floor: the contest leg really had occupied sites to contest",
                  contested.Count >= 20,
                  $"{contested.Count} sites held by a live firm were claimed by a second company "
                  + $"(bound 20)");
            Check("site-link: a second company claiming a held site is refused, and nothing is left half-linked",
                  contestDelta == 0 && refused == contested.Count
                  && incumbentsKept == contested.Count && newcomersUnsited == contested.Count,
                  $"{refused}/{contested.Count} claims refused, {incumbentsKept}/{contested.Count} incumbents "
                  + $"kept their site, {newcomersUnsited}/{contested.Count} newcomers left unsited; audit rose by "
                  + $"{contestDelta} over this leg to {contestFirms} orphaned firms / {contestSites} orphaned "
                  + $"sites (bound: no rise)");

            // ---- LEG 2: a despawned building releases its firm --------------
            // The SyncParcels shape: the building entity is gone, the parcel is
            // emptied. Clearing only the parcel end left the firm pointing at a
            // demolished, Units == 0 parcel — still inside every engine loop,
            // including the one its own bankruptcy check sits in.
            var evicted = new List<Firm>();
            foreach (var pl in w.Parcels)
            {
                if (pl.OccupantFirm < 0) continue;
                var f = w.Firms[pl.OccupantFirm];
                if (f.Dead || f.Parcel != pl.Id) continue;
                evicted.Add(f);
                // Verbatim the order SyncParcels' !em.Exists branch runs in.
                foreach (int hid in pl.OccupantHouseholds) w.Households[hid].HomeParcel = -1;
                pl.OccupantHouseholds.Clear();
                EconSiteLink.ReleaseSite(w, pl);
                pl.State = ParcelState.Empty;
                pl.Units = 0;
            }
            int released = 0;
            foreach (var f in evicted) if (f.Parcel < 0) released++;
            int afterEvict = EconSiteLink.Audit(w, out int evictFirms, out int evictSites);
            int evictDelta = afterEvict - afterContest;
            Check("site-link floor: the demolition leg really had occupied buildings to demolish",
                  evicted.Count >= 20,
                  $"{evicted.Count} standing buildings with a live occupant firm were despawned (bound 20)");
            Check("site-link: a despawned building leaves its firm unsited, not pointing at the rubble",
                  evictDelta == 0 && released == evicted.Count,
                  $"{released}/{evicted.Count} evicted firms ended unsited; audit rose by {evictDelta} over this "
                  + $"leg to {evictFirms} firms still pointing at a site they do not hold / {evictSites} sites "
                  + $"naming an absent firm (bound: no rise)");

            int failed = 0;
            foreach (var r in Results) if (!r.pass) failed++;
            Console.WriteLine($"  {Results.Count - failed}/{Results.Count} passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
