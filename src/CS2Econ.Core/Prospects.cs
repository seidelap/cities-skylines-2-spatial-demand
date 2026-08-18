using System;
using System.Collections.Generic;

namespace CS2Econ.Core
{
    /// <summary>Migration as individual decisions.
    ///
    /// WHAT THIS REPLACES. Migration.Attractiveness built ONE number per
    /// population segment out of five citywide aggregates — population-weighted
    /// mean access, population-weighted mean expected income, the segment's
    /// average rent, a citywide distress share, and the citywide unemployment
    /// rate — turned it into a Poisson rate, and drew an arrival count. Nobody
    /// decided anything. A city with a wonderful district and a terrible one
    /// looked, to every prospective mover, exactly like a city with two mediocre
    /// districts.
    ///
    /// WHAT HAPPENS INSTEAD. The region decides how many people LOOK. Each
    /// looker decides for itself whether to COME, by asking the only question a
    /// mover actually asks: is there a specific place here, at its posted price,
    /// that beats what I already have? Its attributes — how much of its income
    /// it will spend on rent, how it feels about density, its taste for
    /// particular places, and its own reservation — are drawn once, at birth,
    /// from the same distributions residents are drawn from. Nothing about the
    /// city as a whole enters the decision. The only channel through which the
    /// city's condition reaches a prospect is the posted price of individual
    /// submarkets, which is what a price is for.
    ///
    /// THE ANTI-SMUGGLING RULE, stated so a reviewer can check it mechanically:
    /// the expression that sizes the offer must not read AccessValue,
    /// ExpectedIncome, AvgRentBySeg, insolvency stages, employment rates, or
    /// vacancy. Sizing by city quality is the Poisson rate with extra steps;
    /// sizing by city vacancy is the absorption budget again, and that one
    /// deadlocks (no vacancy → no arrivals → no construction → no vacancy).
    /// Offer size is region-side only. Selection is individual.
    ///
    /// WHO COMES is therefore not modelled at all. The segment mix of arrivals
    /// is not an input: offers are spread evenly across segments and the market
    /// decides which of them can actually win somewhere. If the city has cheap
    /// apartments, singles arrive; if it has expensive houses and good schools,
    /// families do. That composition is an OUTPUT.</summary>
    public static class Prospects
    {
        public struct Result
        {
            public int Offered;      // how many looked
            public int Admitted;     // how many found something worth coming for
            public int Declined;     // looked, nothing beat their own reservation
            public int Priced;       // looked, wanted something, could not have won it
        }

        /// <summary>Harness-only telemetry: when non-null, every ADMITTED
        /// prospect appends (labor class, cluster it chose, the employment odds
        /// the mechanism priced that cluster at, the citywide worker-weighted
        /// bench at that moment). The prospect-localization check reads this;
        /// nothing in the mechanism does. Set it around a run and null it
        /// after — it is static, so a leaked list would record every later sim
        /// in the process.</summary>
        public static List<(int labor, int cluster, double pricedOdds, double benchOdds)>? AdmitTelemetry;

        /// <summary>Harness-only telemetry: when non-null, every ADMITTED
        /// prospect appends (its drawn tie cluster or −1, the cluster it
        /// chose). The tie-channel check reads this; nothing in the mechanism
        /// does. Same leak discipline as AdmitTelemetry.</summary>
        public static List<(int tie, int chosen)>? TieTelemetry;

        /// <summary>Offer the city to a batch of individuals and admit the ones
        /// who choose to come. Returns what happened, for telemetry.</summary>
        public static Result Step(WorldState w, AccessState acc, HousingAuction a, EconParams p)
        {
            var res = new Result();
            if (a == null || a.C != acc.C || a.Price.Length == 0) return res;

            int cityPop = 0;
            foreach (var h in w.Households) if (h.ExitedTick < 0) cityPop++;

            // The per-cluster chain-migration stock: decay the whole field one
            // batch's worth before this batch adds to it (see the field's own
            // anti-smuggling guard in MigrationState).
            var ties = w.Migration.NetworkTies;
            if (ties.Length != acc.C)
            {
                // The serializer packs only nonzero clusters, so a loaded
                // array is routinely shorter than C (any save made before the
                // top-index cluster's first remembered arrival); the resize
                // must CARRY THE STOCK OVER — a fresh array here silently
                // zeroed every neighborhood's chain-migration memory on the
                // first post-load tick (mod load path only; the harness never
                // round-trips globals, so no check reaches this).
                var resized = new double[acc.C];
                Array.Copy(ties, resized, Math.Min(ties.Length, acc.C));
                ties = w.Migration.NetworkTies = resized;
            }
            double tiesTotal = 0;
            for (int c = 0; c < acc.C; c++) { ties[c] *= p.NetworkTieDecay; tiesTotal += ties[c]; }

            // HOW MANY LOOK. Region-side only: a base rate times how visible the
            // city is. Prominence is a property of the city's SIZE, not of
            // whether it is any good — a big city is heard of, a bad big city is
            // still heard of, and the people who hear of it then look and mostly
            // decline. That is the honest split between "who considers you" and
            // "who chooses you", and it is the whole of what stays global here.
            // The diaspora counts toward being heard of at the same scale as
            // residents: each unit of the tie stock is one remembered past
            // arrival — an outside person with a link here. It is a COUNT of
            // past admits, not a quality read, so offer size stays blind to
            // how good the city is; a link made in a boom keeps recruiting
            // lookers through the bust until it decays.
            double prominence = 1.0 + (cityPop + tiesTotal) / Math.Max(1.0, p.ProminenceScale);
            double offers = p.RegionOfferRate * prominence * p.RefreshInterval;
            int nOffer = (int)offers;
            if (SplitMix64.Hash01((ulong)w.Tick * 6364136223846793005UL + 17UL) < offers - nOffer) nOffer++;
            if (nOffer <= 0) return res;

            int nSeg = Segment.Count;
            // The outside region's door premium — its access level priced by
            // the same rule as every city door (AccessState.OutsidePremium).
            // One value for the whole batch: the outside world is the same
            // place for everyone looking from it.
            double outsidePrem = acc.OutsidePremium(p);
            // Scratch for the per-cluster evaluation below; one allocation per
            // batch, reused across the batch's prospects.
            var budgetByCluster = new double[acc.C];
            // This batch's admits by chosen cluster, folded into the tie stock
            // only after the batch: a batch must not recruit from itself, and
            // every prospect in it draws from the same pre-batch stock (a live
            // mutation would skew the cumulative walk toward low-index
            // clusters, since the draw is scaled by the pre-batch total).
            var admitsByCluster = new double[acc.C];
            for (int q = 0; q < nOffer; q++)
            {
                res.Offered++;
                // Every prospect is one individual with its own draws, keyed off
                // a stream that never repeats, so two prospects in the same tick
                // are different people rather than the same person sampled twice.
                ulong key = (ulong)w.Tick * 1000003UL + (ulong)q * 2654435761UL + 991UL;
                int s = (int)(SplitMix64.Hash(key * 31UL) % (ulong)nSeg);
                var seg = Segment.All[s];

                // Attributes drawn at birth, from the same distributions a
                // resident is drawn from — a person who moves here is not a
                // different kind of creature from a person already here.
                double u = SplitMix64.Hash01(key * 2654435761UL + 11UL);
                double v = SplitMix64.Hash01(key * 2654435761UL + 29UL);
                double r = SplitMix64.Hash01(key * 2654435761UL + 47UL);
                double rentShare = seg.MaxRentShare * (0.75 + 0.5 * u);
                double densTol = MathUtil.Clamp(seg.DensityTolerance + 0.4 * (v - 0.5), 0, 1);
                double reservationShare = 0.5 * r * r;

                // What it would earn AT EACH SPECIFIC PLACE if it came: its own
                // adults and job level, valued at that cluster's employment
                // odds (local rate shrunk toward the citywide worker-weighted
                // bench — see AccessState.ProspectLocalOdds for why the bench
                // is a legitimate prior and the local term is the market
                // outcome of the place itself). Employment odds are a clearing
                // outcome rather than a personal attribute, which is why
                // reading them is legitimate — the same reason the bid ladder
                // reads them for a resident who has not found work yet. The
                // old single citywide number made every door in a city with a
                // booming district and a dead one price like a uniformly
                // mediocre city — and, being an unweighted mean over all
                // clusters, it was also diluted by every empty map square's
                // structural zero.
                byte jobLevel = (byte)(SplitMix64.Hash(key * 6151UL + 17UL) % 5UL);
                double maxBudget = 0;
                for (int c = 0; c < acc.C; c++)
                {
                    budgetByCluster[c] = rentShare * acc.ProspectIncome(seg, jobLevel, c, p);
                    if (budgetByCluster[c] > maxBudget) maxBudget = budgetByCluster[c];
                }
                // The reservation is what NOT coming is worth, so both its
                // legs are priced at the OUTSIDE region's own levels — income
                // at the outside employment odds (#31), access at the outside
                // region's access level (#42, the outsidePrem factor) — the
                // default is staying outside, and no statistic of this city
                // can change what the outside is worth. MeanAccess inside
                // outsidePrem is not a citywide read of the kind that rule
                // forbids: it is the shared normalizer every city door's
                // premium carries too, so it is the unit the comparison is
                // stated in — and it is exactly what lets a uniformly better
                // city win the comparison at every door at once.
                double reservation = reservationShare
                                     * rentShare * acc.ProspectOutsideIncome(seg, jobLevel, p)
                                     * outsidePrem;
                if (maxBudget <= 0) continue;

                // FAMILIARITY. This individual may have ties to one specific
                // neighborhood — where its own predecessors landed. Which one
                // is a birth draw ∝ the per-cluster stock (a bigger diaspora
                // in a cluster means a looker is more likely to know someone
                // there), and the tie adds a bonus to THAT cluster's value in
                // this prospect's own evaluation — an individual's taste for
                // a place it has ties to, like the taste term. The stock
                // enters the prospect's decision through this one channel and
                // nowhere else.
                int tieCluster = -1;
                if (tiesTotal > 1e-9)
                {
                    double draw = SplitMix64.Hash01(key * 2654435761UL + 71UL) * tiesTotal, accum = 0;
                    for (int c = 0; c < acc.C; c++)
                    { accum += ties[c]; if (draw < accum) { tieCluster = c; break; } }
                }
                double tieBonusScale = p.MutantZeroTieBonus ? 0 : p.NetworkTieBonusScale;

                int bestSub = a.QuoteOutsider(s, maxBudget, budgetByCluster, densTol, reservation,
                                              key, p, tieCluster, tieBonusScale,
                                              out double bestSurplus, out bool anyAttainable);
                if (bestSub < 0)
                {
                    // It wanted something and could not have won it at any door.
                    // It does not come — and, importantly, it does not queue
                    // either. The old model admitted these and let them stand at
                    // the border as "failed arrivals"; they are simply people who
                    // looked at the prices and stayed where they were.
                    if (anyAttainable) res.Declined++; else res.Priced++;
                    continue;
                }
                if (bestSurplus <= reservation) { res.Declined++; continue; }

                // It comes. From here it is an ordinary unhoused household and
                // the auction places it — the decision to move and the contest
                // for a specific unit stay separate, one refresh apart, which is
                // also how it works: you decide on advertised prices and then
                // compete for the flat.
                var hh = new Household
                {
                    Id = w.Households.Count,
                    Segment = s,
                    Money = Math.Max(5, p.ArrivalSavingsMean
                                        + p.ArrivalSavingsSd * (2 * SplitMix64.Hash01(key * 7919UL) - 1)),
                    ArrivedTick = w.Tick,
                    JobLevel = jobLevel,
                    MovingCostDraw = p.MovingCostMean * (0.4 + 1.2 * SplitMix64.Hash01(key * 104729UL)),
                };
                hh.DrawAtBirth(seg, p);
                w.Households.Add(hh);
                // The savings it brings are the outside world's money crossing
                // the border. Omitting this transfer mints currency, and no
                // check would have caught it: ledger conservation runs on the
                // flag-off path, where prospects do not exist.
                w.Ledger.Transfer(Account.OutsideWorld, Account.Households, hh.Money);
                res.Admitted++;
                {
                    // One more remembered arrival at the cluster it chose —
                    // the stock the NEXT batches' tie draws are proportional to.
                    // Through ClusterOf, not key arithmetic: the chosen door
                    // may be an owner parcel's, whose pseudo-key carries no
                    // cluster in its digits.
                    int chosen = a.ClusterOf(bestSub);
                    admitsByCluster[chosen] += 1.0;
                    AdmitTelemetry?.Add(((int)seg.Labor, chosen,
                                         acc.ProspectLocalOdds((int)seg.Labor, chosen, p),
                                         acc.ProspectBenchOdds((int)seg.Labor)));
                    TieTelemetry?.Add((tieCluster, chosen));
                }
            }
            for (int c = 0; c < acc.C; c++) ties[c] += admitsByCluster[c];
            return res;
        }
    }
}
