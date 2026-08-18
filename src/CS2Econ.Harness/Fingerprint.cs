using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CS2Econ.Core;

namespace CS2Econ.Harness
{
    /// <summary>THE MODEL IS UNCHANGED, OR THE CHANGE IS RECORDED.
    ///
    /// WHY THIS EXISTS. `determinism` runs the SAME BUILD twice and asserts
    /// h1 == h2 &amp;&amp; h1 != h3. That tests that the model is REPRODUCIBLE; it
    /// says nothing about whether it is the same model as yesterday, because
    /// both halves of the comparison move together. Measured at c0c584d: three
    /// separate one-line pricing edits (drop the clearing band from the read
    /// point; widen the flat tail's dust guard 0.005 → 0.02; run the clearing
    /// bisection 34 → 40 iterations) each changed the telemetry hash and each
    /// passed 22/22. The first of those was independently measured to move mean
    /// charged rent +34 % and the tax base +6 %.
    ///
    /// WHY NOT A GOLDEN HASH. One number that must be updated on every
    /// intentional model change gets bumped without being read — the classic
    /// cry-wolf failure, and worse than no check because it launders a real
    /// change through a green suite. Four devices push against that here, in
    /// descending order of how much they actually do:
    ///
    ///   1. LANES, NOT ONE NUMBER. Thirteen of them: demography / built /
    ///      price / money on three arms (default flags, posted, labor), plus the
    ///      labor arm's cleared-market lane. The maintainer reads a claim about
    ///      their own diff — "price and money moved, demography and built did
    ///      not" — and a claim that contradicts what they think they changed is
    ///      a finding. This does most of the work.
    ///   2. THE DELTA REPORT IS COMPUTED, NEVER TYPED. `--accept` diffs the new
    ///      scalars against the previous stanza and writes the result into the
    ///      file, so "sumLR 17770.00 → 15765.00 (−11.28 %)" lands in the commit
    ///      diff where a reviewer sees the magnitude even when the stated reason
    ///      is "update baseline".
    ///   3. THE STANZA IS SIGNED (FNV-1a over its own text, deltas included), so
    ///      hand-editing a hash — or softening a recorded delta — fails with
    ///      "hand-edited", and running the command is the path of least
    ///      resistance.
    ///   4. The reason must be ≥ 20 characters and differ from the previous one.
    ///      This is the WEAKEST of the four and is stated as such: twenty
    ///      characters of noise defeats it. It is here to make a thoughtless
    ///      accept take one extra deliberate act, not to stop a determined one.
    ///
    /// ALL FOUR DEVICES WERE TESTED, not just asserted — an untested anti-tamper
    /// device is exactly the kind of claim this file exists to stop. Measured on
    /// a scratch copy of the baseline:
    ///   · flip ONE hex digit in a gating lane's hash  → "BASELINE HAND-EDITED",
    ///     exit 1. (device 3)
    ///   · soften a recorded DELTA line, touching no hash → same failure, exit 1.
    ///     The signature covers the deltas, so the magnitude cannot be edited
    ///     down after the fact either. (device 3)
    ///   · delete the baseline entirely → "A missing baseline is a failure, not
    ///     a skip", exit 1 — it does NOT silently pass. (device 3)
    ///   · `--accept --reason "too short"` → exit 2; a reason identical to the
    ///     previous stanza's → exit 2. (device 4)
    ///   · DEVICE 2, the one that matters most, demonstrated end to end: MUT-C
    ///     (AssessedLR × 0.958) was applied and accepted with the deliberately
    ///     uninformative reason "Routine calibration refresh, nothing
    ///     significant expected here". The stanza that landed in the file reads
    ///     `sumLR 17770.106 -> 15944.522 (-10.27%)`, plus moves in built, pop
    ///     and treasury. The author said nothing; the file said −10.27 % of the
    ///     tax base, in the commit diff, computed. That is the whole design.
    ///     (The −10.27 % here is larger than the −4.2 % the original round
    ///     measured because over 120 ticks the assessment cut feeds back through
    ///     construction and leveling; the point is the reporting, not the size.)
    ///
    /// WHAT IT COSTS, MEASURED PER COMMIT. Every fingerprint-era commit of this
    /// branch (0b5b658..88fc88c, 24 commits, 23 transitions) was rebuilt and
    /// `fingerprint` run at each, and every lane compared with its predecessor.
    /// Per arm: transitions in which any of its lanes moved, and how many of
    /// those moves were the FALSE-ALARM signature documented below — hash moved,
    /// every printed scalar unchanged.
    ///
    ///   arm       born      transitions   fired    rate   false alarms
    ///   default   0b5b658            23      11   0.478              0
    ///   auction   0b5b658             9       2   0.222              0   (retired 554b56a)
    ///   posted    554b56a            13       6   0.462              0
    ///   labor     25b24b1            15      10   0.667              0
    ///
    /// Following each WORLD across the flip's rename rather than the arm's name:
    /// the auction world fired 12/23 (0.522) and the posted world 6/23 (0.261).
    /// The flip transition itself moved neither — the new default arm is
    /// hash-identical to the retired auction arm, and the new posted arm to the
    /// retired default — so the rename cost nothing and the earlier claim to
    /// that effect (stanza 6) is confirmed by measurement.
    ///
    /// THE OLD PROMOTION RULE CANNOT BE EXECUTED, and this replaces it. It read:
    /// "after 10 auction-touching commits, measure how often the auction lanes
    /// actually fired; promote them to gating if fewer than 1 in 3." Three
    /// things stop it, each measured above:
    ///   1. ITS SUBJECT IS GONE. The auction arm was retired at 554b56a with 9
    ///      transitions behind it — one short of its own window — and that world
    ///      gates today under the name `default`, promoted by the flag flip
    ///      rather than by this rule.
    ///   2. ITS MEASUREMENT CANNOT COME FROM THE LOG. A report-only lane's move
    ///      never forces a stanza, so the log records a report arm's moves only
    ///      when they ride along with a gating accept, and undercounts by
    ///      construction. The numbers above needed all 24 commits rebuilt.
    ///   3. ITS CRITERION CONTRADICTS THIS FILE. Applied to the arm that gates
    ///      today, default fires 0.478 — well over 1 in 3 — so the rule would
    ///      DEMOTE the gate. Firing is not the cost; a fire that says nothing
    ///      is, and across 29 arm-fires in this history ZERO were that.
    ///
    /// THE PROMOTION RULE, restated so it can be run. An arm GATES unless its
    /// fires are uninformative.
    ///   MEASUREMENT: rebuild every commit since the arm appeared, run
    ///     `fingerprint` at each, and per transition record (i) whether any lane
    ///     of the arm moved and (ii) whether that move left every printed scalar
    ///     unchanged (`drift` excepted — it is the reconciliation residual, not
    ///     a model scalar). Do NOT read this off the log; see (2) above.
    ///   TRIGGER: a gating arm is demoted to report-only when its false-alarm
    ///     rate over its last 10 or more transitions reaches 1 in 3; a
    ///     report-only arm is promoted when it falls below that. Re-measure
    ///     every 20 commits, whenever an arm is added, and whenever a
    ///     FeatureFlags default changes.
    ///   DECISION at 88fc88c: every arm measures 0 false alarms, so the posted
    ///     and labor arms are PROMOTED and all thirteen lanes now gate. The
    ///     accept burden that adds is measured at ZERO: all 6 posted fires and
    ///     all 10 labor fires landed on commits where the default arm fired too,
    ///     so the same single `--accept` covers them. The limit of that number
    ///     is stated rather than hidden: no commit in this window moved posted
    ///     or labor WITHOUT moving default, so it bounds nothing about a future
    ///     commit that touches only the labor auction — that case would add one
    ///     accept, and the re-measurement trigger above is what catches it if
    ///     such commits become common.
    ///
    /// KNOWN FALSE-ALARM MODE, measured. Math.Pow/Exp/Log are library code, so a
    /// runtime or CPU change can move last bits and light a lane. MUT-3
    /// (bisection 34 → 40 iterations) is what that looks like from the outside,
    /// measured: default.price and default.money go red, and every scalar behind
    /// them is unchanged to printed precision — sumLR 17770.106, meanRent
    /// 4.2415514, treasury 233363.42, all identical — with only `drift` moving,
    /// at 1E-8. That signature — hashes moved, no scalar moved — is the tell,
    /// and a bare golden hash could not have said even that much.
    ///
    /// THE FIXTURE IS PINNED and ignores --seed, --auction and --store-level. A
    /// baseline that means different things depending on how the harness was
    /// invoked is not a baseline. The cost is stated in the suite's known
    /// limits: one seed, one city shape, 120 ticks — a defect that needs a
    /// bigger city or another seed moves no lane here.
    ///
    /// WHAT THE SIGNATURE DOES NOT COVER, found by reading this code rather than
    /// by a mutant: only the LAST stanza is ever verified. Check() reads the
    /// tail of the file, and Accept() appends without validating what came
    /// before, so an edit to a HISTORICAL stanza is undetected. That does not
    /// let anyone launder a hash — the gate only ever compares against the last
    /// stanza, and Accept always recomputes from the live model — but it does
    /// mean the "append-only log" is tamper-EVIDENT only at its tip, and the
    /// recorded history of past deltas is not. Verifying every stanza on read is
    /// a few lines; it was not done here, and this note is the reason a future
    /// reader should not assume it was.</summary>
    public static class Fingerprint
    {
        public const string FileName = "model-fingerprint.txt";

        // Pinned fixture. Deliberately the same shape as the determinism
        // fixture (8×8, 1500 households, 120 ticks) so the two cost the same and
        // the comparison between "reproducible" and "unchanged" is like for like.
        private const ulong FixtureSeed = 1;
        private const int Cols = 8, Rows = 8, SeedHouseholds = 1500, Ticks = 120;

        public sealed class Lane
        {
            public string Name = "";
            public ulong Hash;
            public List<(string key, double value)> Scalars = new List<(string, double)>();
            public bool Gating;          // every arm gates since the promotion measurement
        }

        // ---- hashing -----------------------------------------------------
        private struct Fnv
        {
            private ulong _h;
            public static Fnv New() => new Fnv { _h = 1469598103934665603UL };
            public void Mix(ulong v) { _h ^= v; _h *= 1099511628211UL; }
            /// <summary>6 dp on the same rationale as Sim.TelemetryHash: the last
            /// bits of a Math.Pow chain are not a model change, and hashing them
            /// converts every library update into a red lane.</summary>
            public void MixD(double v) => Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(v, 6)));
            public ulong Value => _h;
        }

        public static List<Lane> Compute()
        {
            var lanes = new List<Lane>();
            // The default arm IS the auction path since the flip
            // (FeatureFlags.HousingAuction = true). Its lanes at the flip
            // commit are hash-identical to the previously accepted auction-arm
            // values (verified in the flip inventory before the flip landed),
            // so the gating baseline carried over a measured state rather than
            // a fresh one. The old "auction" arm would now duplicate default
            // lane for lane, asserting nothing — replaced by a POSTED arm so
            // the retired-in-default path keeps an invariance instrument for
            // as long as it ships via --posted at all.
            lanes.AddRange(Arm("default", new FeatureFlags(), gating: true));
            // PROMOTED to gating by the measurement at the head of this file:
            // 0 of 6 fires were the false-alarm signature, and all 6 landed on
            // commits where the default arm fired anyway, so the accept this
            // costs is one already being written.
            lanes.AddRange(Arm("posted", new FeatureFlags { HousingAuction = false }, gating: true));
            // Also promoted, on the same measurement (0 of 10 fires false, 0
            // extra accepts). HousingAuction spelled explicitly so this arm does
            // not silently change worlds if the default ever moves again.
            lanes.AddRange(Arm("labor", new FeatureFlags { HousingAuction = true, LaborAuction = true },
                               gating: true));
            // REPORT-ONLY, and the pair exists for the reason the posted arm
            // exists: whichever consumption path is NOT the default must keep a
            // hash while it still ships. The `pooled` arm spells
            // StoreLevelSpending = false, so it stays the pooled path whatever
            // the default becomes; the `storelevel` arm is the experimental
            // path and is the one that carries information today. Pre-flip
            // `pooled` duplicates `default` lane for lane and asserts nothing —
            // that is the cost of having the pin in place BEFORE the flip
            // rather than remembering it during one.
            lanes.AddRange(Arm("pooled", new FeatureFlags { HousingAuction = true, StoreLevelSpending = false },
                               gating: false));
            lanes.AddRange(Arm("storelevel", new FeatureFlags { HousingAuction = true, StoreLevelSpending = true },
                               gating: false));
            return lanes;
        }

        private static IEnumerable<Lane> Arm(string arm, FeatureFlags flags, bool gating)
        {
            // Built directly rather than through Sim.Create: Create honours the
            // process-wide --auction / --store-level overrides, and this fixture
            // must not move when the harness is invoked differently.
            var p = new EconParams();
            var cfg = new SyntheticCity.Config
            { Cols = Cols, Rows = Rows, SeedHouseholds = SeedHouseholds, Seed = FixtureSeed };
            var sim = new Sim { P = p, Flags = flags };
            (sim.W, sim.Access) = SyntheticCity.Build(cfg, p);
            sim.Engine = new EconomyEngine(sim.W, sim.Access, p, flags);
            sim.Run(Ticks);
            var w = sim.W;

            // --- demography: who is here and where they live ---------------
            var dh = Fnv.New();
            int pop = 0, housed = 0, sheltered = 0;
            foreach (var h in w.Households)
            {
                dh.Mix((ulong)(h.Segment + 1));
                dh.Mix((ulong)(h.HomeParcel + 2));
                dh.Mix((ulong)h.Stage + 3);
                dh.Mix(h.ExitedTick >= 0 ? 5UL : 7UL);
                if (h.ExitedTick >= 0) continue;
                pop++;
                if (h.HomeParcel >= 0) housed++;
                if (h.Stage == InsolvencyStage.Sheltered) sheltered++;
            }
            yield return new Lane
            {
                Name = arm + ".demography",
                Hash = dh.Value,
                Gating = gating,
                Scalars = { ("pop", pop), ("housed", housed), ("sheltered", sheltered) },
            };

            // --- built stock: what stands, at what level -------------------
            var bh = Fnv.New();
            int built = 0, units = 0; double levelSum = 0;
            foreach (var pl in w.Parcels)
            {
                bh.Mix((ulong)pl.State + 1);
                bh.Mix((ulong)pl.Use + 2);
                bh.Mix((ulong)pl.Level + 3);
                bh.Mix((ulong)pl.Units + 4);
                bh.MixD(pl.Condition);
                if (pl.State != ParcelState.Built) continue;
                built++; units += pl.Units; levelSum += pl.Level;
            }
            yield return new Lane
            {
                Name = arm + ".built",
                Hash = bh.Value,
                Gating = gating,
                Scalars =
                {
                    ("built", built),
                    ("meanLevel", built > 0 ? levelSum / built : 0),
                    ("units", units),
                },
            };

            // --- price: the tax base and what tenants are charged ----------
            var ph = Fnv.New();
            double sumLr = 0, sumLrHigh = 0, sumLrLow = 0, rentSum = 0; int rentN = 0;
            foreach (var pl in w.Parcels)
            {
                ph.MixD(pl.AssessedLR);
                ph.MixD(pl.CurrentResidual);
                sumLr += pl.AssessedLR;
                if (pl.Use == ZoneKind.ResidentialHigh) sumLrHigh += pl.AssessedLR;
                else if (pl.Use == ZoneKind.ResidentialLow) sumLrLow += pl.AssessedLR;
            }
            foreach (var h in w.Households)
                if (h.ExitedTick < 0 && h.HomeParcel >= 0) { rentSum += h.ChargedAssessment; rentN++; }
            yield return new Lane
            {
                Name = arm + ".price",
                Hash = ph.Value,
                Gating = gating,
                Scalars =
                {
                    ("sumLR", sumLr), ("sumLR.high", sumLrHigh), ("sumLR.low", sumLrLow),
                    ("meanRent", rentN > 0 ? rentSum / rentN : 0),
                },
            };

            // --- money: who holds it ---------------------------------------
            var mh = Fnv.New();
            double hhMoney = 0, firmMoney = 0, escrow = 0;
            foreach (var h in w.Households) { mh.MixD(h.Money); hhMoney += h.Money; }
            foreach (var f in w.Firms) { mh.MixD(f.Money); firmMoney += f.Money; }
            foreach (var pl in w.Parcels) { mh.MixD(pl.Escrow); escrow += pl.Escrow; }
            // EVERY ledger account, phantoms included, and this is deliberate.
            // The reconciliation check can only cover the three sectors that
            // have an independent entity-level record (households, firms,
            // per-parcel escrow); Treasury, PhantomDeveloper, OutsideWorld,
            // PhantomBank, SinkingFund and NationalCounterparty have none, so a
            // defect confined to a pair of ledger-only accounts is invisible to
            // it — MUT-L5 (a renovation recording its developer→outside hop for
            // twice the cost) passes reconciliation and always will. Hashing the
            // account vector here does not give those accounts a mirror, but it
            // does mean such a defect moves SOMETHING in the suite — provided its
            // code path runs in this pinned fixture. MEASURED, both halves:
            //   MUT-L5b (the same defect shape in Construction.cs, which does run
            //     here) → red, and red in default.money + auction.money ONLY,
            //     which is the signature of a ledger-only defect: no entity moved.
            //   MUT-L5 (the renovation branch, which does NOT fire in 120 ticks
            //     on this 8×8/1500 fixture) → all eight lanes still match, i.e.
            //     STILL A MISS after this change. Reported, not hidden.
            // Narrow, and better than nothing.
            double phantoms = 0;
            foreach (Account acct in Enum.GetValues(typeof(Account)))
            {
                double bal = w.Ledger.Balance(acct);
                mh.MixD(bal);
                if (acct != Account.Households && acct != Account.Firms
                    && acct != Account.Escrow && acct != Account.Treasury) phantoms += bal;
            }
            double treasury = w.Ledger.Balance(Account.Treasury);
            yield return new Lane
            {
                Name = arm + ".money",
                Hash = mh.Value,
                Gating = gating,
                Scalars =
                {
                    ("treasury", treasury), ("hhMoney", hhMoney),
                    ("firmMoney", firmMoney), ("escrow", escrow),
                    ("phantoms", phantoms), ("drift", w.Ledger.Drift()),
                },
            };

            // --- labor market: the cleared assignment and its comps --------
            // Only on the labor arm. Wage share of marginal product
            // (meanToverCap) and the dividend/comp ratio are the split the
            // total-comp design lets EMERGE, so they are the scalars a
            // reviewer should read on a labor diff.
            //
            // HOW TO READ unempShare, because its zero is not what it looks
            // like: LaborAuction.Why calls an unmatched worker `Outside`
            // whenever its outside net beats its own leisure floor, and on this
            // fixture that is 11.24 against 2.77 — so the zero says the outside
            // wage beats sitting at home, not that city work was available.
            // Every unmatched worker here holds a shortlisted door worth more
            // than its own default price-free (median surplus 30.4); they lose
            // on price, against a door supply of 0.52 slots per worker. Read
            // outsideShare against slotsPerWorker below, never alone.
            if (flags.LaborAuction)
            {
                var a = sim.Engine.Labor;
                var lh = Fnv.New();
                double demanded = 0, employed = 0, outsideE = 0, unempE = 0;
                foreach (var h in w.Households)
                {
                    if (h.ExitedTick >= 0 || !a.ActiveWorker(h.Id)) continue;
                    int e = a.EarnersOf(h.Id);
                    // One bidder per EARNER: hash and count each earner's own
                    // outcome (a household's earners can differ).
                    for (int s = 0; s < e; s++)
                    {
                        int wk = a.WorkerOf(h.Id, s);
                        lh.Mix((ulong)(a.Assignment[wk] + 2));
                        lh.Mix((ulong)a.Why[wk] + 5);
                        demanded += 1;
                        if (a.Assignment[wk] >= 0) employed += 1;
                        else if (a.Why[wk] == LaborAuction.Outcome.Outside) { employed += 1; outsideE += 1; }
                        else unempE += 1;
                    }
                    lh.MixD(h.BaseComp);
                }
                var tSum = new double[3]; var tN = new double[3];
                double usedT = 0, usedCap = 0, divPart = 0, compSum = 0, slots = 0;
                for (int d = 0; d < a.D; d++)
                {
                    lh.MixD(a.Price[d]); lh.MixD(a.Cap[d]); lh.Mix((ulong)a.Used[d] + 1);
                    double t = a.CompMember(d);
                    int cls = a.DoorClass[d];
                    tSum[cls] += t * a.Used[d]; tN[cls] += a.Used[d];
                    usedT += t * a.Used[d]; usedCap += a.Cap[d] * a.Used[d];
                    slots += a.Capacity[d];
                    double divEma = w.Firms[a.DoorFirm[d]].DividendPerEarnerEma;
                    divPart += Math.Min(Math.Max(0, t), divEma) * a.Used[d];
                    compSum += Math.Max(0, t) * a.Used[d];
                }
                yield return new Lane
                {
                    Name = arm + ".labor",
                    Hash = lh.Value,
                    Gating = gating,
                    Scalars =
                    {
                        ("empRate", demanded > 0 ? employed / demanded : 0),
                        ("outsideShare", demanded > 0 ? outsideE / demanded : 0),
                        ("unempShare", demanded > 0 ? unempE / demanded : 0),
                        ("meanT.basic", tN[0] > 0 ? tSum[0] / tN[0] : 0),
                        ("meanT.skilled", tN[1] > 0 ? tSum[1] / tN[1] : 0),
                        ("meanT.educated", tN[2] > 0 ? tSum[2] / tN[2] : 0),
                        ("meanToverCap", usedCap > 0 ? usedT / usedCap : 0),
                        ("divCompRatio", compSum > 0 ? divPart / compSum : 0),
                        // WITHOUT THIS THE OUTSIDE SHARE CANNOT BE READ, and it
                        // was read wrong for six stanzas. Door slots per worker
                        // is the ration the labor market clears against: where
                        // it is below 1 the outside share is bounded below by
                        // the shortfall whatever the outside wage is. Measured
                        // across grid sizes on the same fixture shape
                        // (`laborprobe`, this commit): 8×8 0.52 slots/worker →
                        // outside 0.589, 12×12 0.50 → 0.589, 16×16 0.78 →
                        // 0.452, 20×20 1.16 → 0.231. It is a scalar, not a hash
                        // term, so adding it moves no lane hash and needs no
                        // accept; it lands in the next stanza as "(new)".
                        ("slotsPerWorker", demanded > 0 ? slots / demanded : 0),
                    },
                };
            }
        }

        // ---- the baseline file -------------------------------------------
        public sealed class Stanza
        {
            public int Index;
            public string Reason = "";
            public string Recorded = "";
            public readonly Dictionary<string, (ulong hash, List<(string, double)> scalars)> Lanes
                = new Dictionary<string, (ulong, List<(string, double)>)>();
            public string Sig = "";
            public string SigOver = "";     // the text the signature covers
        }

        /// <summary>The file lives at the repo root; the harness is normally run
        /// from there. Searched upward a few levels so running from src/ is not
        /// a silent skip — a MISSING baseline must fail, never pass.</summary>
        public static string? Locate()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
            {
                string cand = Path.Combine(dir.FullName, FileName);
                if (File.Exists(cand)) return cand;
            }
            return null;
        }

        private static ulong Sign(string text)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        public static List<Stanza> Read(string path)
        {
            var list = new List<Stanza>();
            Stanza? cur = null;
            var body = new StringBuilder();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("=== stanza", StringComparison.Ordinal))
                {
                    // Normalized the same way on both sides (see Sign call sites)
                    // so a blank line separating stanzas in the file cannot
                    // change what the signature covers.
                    if (cur != null) { cur.SigOver = Normalize(body.ToString()); list.Add(cur); }
                    cur = new Stanza { Index = list.Count + 1 };
                    body.Clear();
                    body.Append(line).Append('\n');
                    continue;
                }
                if (cur == null) continue;                     // header/comment prologue
                if (line.StartsWith("sig: ", StringComparison.Ordinal))
                {
                    cur.Sig = line.Substring(5).Trim();
                    continue;                                   // NOT part of the signed text
                }
                body.Append(line).Append('\n');
                if (line.StartsWith("reason: ", StringComparison.Ordinal)) cur.Reason = line.Substring(8).Trim();
                else if (line.StartsWith("recorded: ", StringComparison.Ordinal)) cur.Recorded = line.Substring(10).Trim();
                else if (line.StartsWith("lane ", StringComparison.Ordinal))
                {
                    var parts = line.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    string name = parts[0];
                    ulong hash = ulong.Parse(parts[1].Replace("hash=", ""), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture);
                    var scal = new List<(string, double)>();
                    for (int i = 2; i < parts.Length; i++)
                    {
                        int eq = parts[i].IndexOf('=');
                        if (eq <= 0) continue;
                        scal.Add((parts[i].Substring(0, eq),
                                  double.Parse(parts[i].Substring(eq + 1), CultureInfo.InvariantCulture)));
                    }
                    cur.Lanes[name] = (hash, scal);
                }
            }
            if (cur != null) { cur.SigOver = Normalize(body.ToString()); list.Add(cur); }
            return list;
        }

        private static string Normalize(string s) => s.TrimEnd('\n') + "\n";

        public sealed class Verdict
        {
            public bool BaselineFound, SignatureOk;
            public string Path = "";
            public string Reason = "", Recorded = "";
            public readonly List<string> GatingMismatch = new List<string>();
            public readonly List<string> ReportMismatch = new List<string>();
            public readonly List<string> Missing = new List<string>();
            /// <summary>Missing lanes that GATE. Was written as
            /// `Missing.Count(m =&gt; m.StartsWith("default."))` — the gating arm's
            /// name spelled into the pass condition, which silently stops
            /// covering any arm promoted later. It is the lane's own Gating flag
            /// that decides, so the promotion of the posted and labor arms
            /// (measured; see the promotion rule at the head of this file) does
            /// not leave a hole where a gating lane could vanish from the
            /// baseline unnoticed.</summary>
            public readonly List<string> MissingGating = new List<string>();
            public bool GatePass => BaselineFound && SignatureOk && GatingMismatch.Count == 0
                                    && MissingGating.Count == 0;
        }

        public static Verdict Check(List<Lane> lanes)
        {
            var v = new Verdict();
            string? path = Locate();
            if (path == null) return v;
            v.BaselineFound = true;
            v.Path = path;
            var stanzas = Read(path);
            if (stanzas.Count == 0) return v;
            var last = stanzas[^1];
            v.Reason = last.Reason; v.Recorded = last.Recorded;
            v.SignatureOk = string.Equals(last.Sig, Sign(last.SigOver).ToString("X16"), StringComparison.OrdinalIgnoreCase);
            foreach (var lane in lanes)
            {
                if (!last.Lanes.TryGetValue(lane.Name, out var rec))
                {
                    v.Missing.Add(lane.Name);
                    if (lane.Gating) v.MissingGating.Add(lane.Name);
                    continue;
                }
                if (rec.hash == lane.Hash) continue;
                (lane.Gating ? v.GatingMismatch : v.ReportMismatch).Add(lane.Name);
            }
            return v;
        }

        /// <summary>Per-scalar before → after, computed here and written into the
        /// file. The point of device #2: the maintainer never types a magnitude,
        /// and the reviewer reads one in the diff.</summary>
        private static string DeltaLines(Stanza? prev, List<Lane> lanes)
        {
            var sb = new StringBuilder();
            foreach (var lane in lanes)
            {
                if (prev == null || !prev.Lanes.TryGetValue(lane.Name, out var rec))
                { sb.Append($"delta: {lane.Name} (new lane)\n"); continue; }
                if (rec.hash == lane.Hash) { sb.Append($"delta: {lane.Name} =\n"); continue; }
                var parts = new List<string>();
                foreach (var (k, val) in lane.Scalars)
                {
                    double old = 0; bool have = false;
                    foreach (var (k2, v2) in rec.scalars) if (k2 == k) { old = v2; have = true; break; }
                    if (!have) { parts.Add($"{k} (new)"); continue; }
                    if (Math.Abs(old - val) <= 1e-9 * Math.Max(1, Math.Abs(old))) { parts.Add($"{k} ="); continue; }
                    double pct = Math.Abs(old) > 1e-12 ? (val - old) / Math.Abs(old) * 100 : double.NaN;
                    parts.Add(double.IsNaN(pct)
                        ? $"{k} {old:G8} -> {val:G8}"
                        : $"{k} {old:G8} -> {val:G8} ({pct:+0.00;-0.00}%)");
                }
                sb.Append($"delta: {lane.Name} hash moved; {string.Join("; ", parts)}\n");
            }
            return sb.ToString();
        }

        public static int Accept(List<Lane> lanes, string reason)
        {
            if (reason.Trim().Length < 20)
            {
                Console.WriteLine("fingerprint: --reason must be at least 20 characters "
                                  + "(say what changed in the model and why).");
                return 2;
            }
            string? path = Locate();
            if (path == null) path = Path.Combine(Directory.GetCurrentDirectory(), FileName);
            var stanzas = File.Exists(path) ? Read(path) : new List<Stanza>();
            var prev = stanzas.Count > 0 ? stanzas[^1] : null;
            if (prev != null && string.Equals(prev.Reason.Trim(), reason.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("fingerprint: --reason is identical to the previous stanza's. "
                                  + "Two different model changes do not have the same reason.");
                return 2;
            }

            var body = new StringBuilder();
            body.Append($"=== stanza {stanzas.Count + 1}\n");
            body.Append($"recorded: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            body.Append($"reason: {reason.Trim()}\n");
            foreach (var lane in lanes)
            {
                body.Append($"lane {lane.Name} hash={lane.Hash:X16}");
                foreach (var (k, val) in lane.Scalars)
                    body.Append($" {k}={val.ToString("G10", CultureInfo.InvariantCulture)}");
                body.Append('\n');
            }
            body.Append(DeltaLines(prev, lanes));
            string text = Normalize(body.ToString());
            string stanza = text + $"sig: {Sign(text):X16}\n";

            if (!File.Exists(path)) File.WriteAllText(path, Header());
            File.AppendAllText(path, "\n" + stanza);
            Console.WriteLine($"fingerprint: appended stanza {stanzas.Count + 1} to {path}");
            Console.Write(stanza);
            return 0;
        }

        private static string Header() =>
            "# MODEL FINGERPRINT — append-only baseline for the acceptance suite.\n"
          + "#\n"
          + "# Each stanza records what the pinned fixture produced at one commit:\n"
          + "# thirteen lanes (demography / built / price / money on the default,\n"
          + "# posted and labor arms, plus the labor arm's cleared-market lane),\n"
          + "# each an entity-level hash plus the human-readable scalars behind it.\n"
          + "# `verify` gates on all of them: the promotion rule at the head of\n"
          + "# Fingerprint.cs was measured over this branch's whole fingerprint era\n"
          + "# and promoted the two report-only arms.\n"
          + "#\n"
          + "# DO NOT HAND-EDIT. Each stanza is signed over its own text, deltas\n"
          + "# included, so an edited hash fails as 'hand-edited' rather than\n"
          + "# passing quietly. To record an intentional model change:\n"
          + "#\n"
          + "#   dotnet run --project src/CS2Econ.Harness -c Release -- \\\n"
          + "#       fingerprint --accept --reason \"<what changed in the model, and why>\"\n"
          + "#\n"
          + "# The delta lines are COMPUTED by that command, never typed. They are\n"
          + "# the part a reviewer should read: they state the magnitude of what the\n"
          + "# commit moved, in the commit's own diff.\n";

        public static int Run(string[] args)
        {
            bool check = args.Contains("--check"), accept = args.Contains("--accept");
            string reason = "";
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--reason") reason = args[i + 1];

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lanes = Compute();
            sw.Stop();

            if (accept) return Accept(lanes, reason);

            Console.WriteLine($"model fingerprint (pinned fixture: seed {FixtureSeed}, {Cols}×{Rows}, "
                              + $"{SeedHouseholds} households, {Ticks} ticks; {sw.Elapsed.TotalSeconds:F1}s)");
            foreach (var lane in lanes)
            {
                string scal = string.Join(" ", lane.Scalars.Select(s => $"{s.key}={s.value:G8}"));
                Console.WriteLine($"  {lane.Name,-20} {lane.Hash:X16} {(lane.Gating ? "[gating]" : "[report]")} {scal}");
            }
            if (!check) return 0;

            var v = Check(lanes);
            if (!v.BaselineFound)
            {
                Console.WriteLine($"fingerprint: NO BASELINE ({FileName} not found from {Directory.GetCurrentDirectory()}). "
                                  + "A missing baseline is a failure, not a skip.");
                return 1;
            }
            if (!v.SignatureOk)
            {
                Console.WriteLine($"fingerprint: BASELINE HAND-EDITED — the last stanza's signature does not "
                                  + $"match its own text ({v.Path}). Re-record it with --accept.");
                return 1;
            }
            Console.WriteLine($"fingerprint: baseline {v.Path}, recorded {v.Recorded}, reason \"{v.Reason}\"");
            if (v.GatingMismatch.Count == 0 && v.ReportMismatch.Count == 0 && v.Missing.Count == 0)
            { Console.WriteLine("fingerprint: all lanes match."); return 0; }
            foreach (var m in v.GatingMismatch) Console.WriteLine($"  MISMATCH [gating] {m}");
            foreach (var m in v.ReportMismatch) Console.WriteLine($"  MISMATCH [report] {m}");
            foreach (var m in v.Missing) Console.WriteLine($"  MISSING  {m} (not in the baseline stanza)");
            return v.GatePass ? 0 : 1;
        }
    }
}
