using System;
using System.Collections.Generic;

namespace SpatialDemand.Core
{
    public sealed class HomeSearch
    {
        public Household Household { get; }
        public IReadOnlyList<HomeOffer> Offers { get; }
        public long? CurrentHome { get; }
        public double OutsideUtility { get; }
        public double MovingCost { get; }

        public HomeSearch(Household household, IReadOnlyList<HomeOffer> offers,
            long? currentHome = null, double outsideUtility = 0, double movingCost = 0.05)
        {
            if (double.IsNaN(outsideUtility) || double.IsInfinity(outsideUtility) || !Numbers.NonNegative(movingCost))
                throw new ArgumentOutOfRangeException(nameof(outsideUtility));
            Household = household; Offers = offers ?? throw new ArgumentNullException(nameof(offers));
            CurrentHome = currentHome; OutsideUtility = outsideUtility; MovingCost = movingCost;
        }
    }

    public readonly struct HomeChoice
    {
        public readonly long HouseholdId;
        public readonly long? HomeId;
        public readonly double Utility;
        public readonly Evaluation Evaluation;
        public readonly bool Moved;

        internal HomeChoice(long householdId, long? homeId, double utility, Evaluation evaluation, bool moved)
        { HouseholdId = householdId; HomeId = homeId; Utility = utility; Evaluation = evaluation; Moved = moved; }
    }

    // A sequential posted-price market, not a claimed equilibrium solver. Each household
    // takes its argmax over remaining offers. Vacancies are reserved immediately; departures
    // become available next batch, only after the game's tenancy system actually moves them.
    public static class HousingMarket
    {
        public static IReadOnlyList<HomeChoice> Clear(IReadOnlyList<HomeSearch> searches, uint round)
        {
            var remaining = new Dictionary<long, int>();
            var rents = new Dictionary<long, double>();
            var ids = new HashSet<long>();
            var ordered = new List<HomeSearch>(searches.Count);
            foreach (var search in searches)
            {
                if (!ids.Add(search.Household.Id)) throw new ArgumentException("Duplicate household.");
                ordered.Add(search);
                foreach (var offer in search.Offers)
                {
                    if (remaining.TryGetValue(offer.Id, out var capacity) && capacity != offer.Available)
                        throw new ArgumentException("Offers disagree about available capacity.");
                    // An incumbent may have a lease different from the asking rent for vacant units.
                    if (search.CurrentHome != offer.Id)
                    {
                        if (rents.TryGetValue(offer.Id, out var rent) && rent != offer.Rent)
                            throw new ArgumentException("Offers disagree about posted rent.");
                        rents[offer.Id] = offer.Rent;
                    }
                    remaining[offer.Id] = offer.Available;
                }
            }
            // Stable under input reordering, but priority rotates between rounds.
            ordered.Sort((a, b) => ComparePriority(a.Household.Id, b.Household.Id, round));
            var result = new List<HomeChoice>(ordered.Count);
            foreach (var search in ordered)
            {
                long? best = null;
                double bestUtility = search.OutsideUtility;
                Evaluation bestEvaluation = default;
                foreach (var offer in search.Offers)
                {
                    bool incumbent = search.CurrentHome == offer.Id;
                    var available = new HomeOffer(offer.Id, offer.Rent, offer.Space, offer.TravelSeconds,
                        incumbent ? 1 : remaining[offer.Id]);
                    var evaluation = Housing.Evaluate(search.Household, available);
                    if (!evaluation.Feasible) continue;
                    double utility = evaluation.Utility - (incumbent ? 0 : search.MovingCost);
                    bool tie = utility == bestUtility && best.HasValue &&
                        (incumbent || (best != search.CurrentHome && offer.Id < best.Value));
                    // On a tie with the outside option, do not force a move.
                    if (utility > bestUtility || tie)
                    { best = offer.Id; bestUtility = utility; bestEvaluation = evaluation; }
                }
                bool moved = best.HasValue && best != search.CurrentHome;
                if (moved) remaining[best!.Value]--;
                result.Add(new HomeChoice(search.Household.Id, best, bestUtility, bestEvaluation, moved));
            }
            return result;
        }

        private static int ComparePriority(long a, long b, uint round)
        {
            uint ah = StableHash.Mix(unchecked((uint)a ^ (uint)(a >> 32) ^ round));
            uint bh = StableHash.Mix(unchecked((uint)b ^ (uint)(b >> 32) ^ round));
            int comparison = ah.CompareTo(bh);
            return comparison != 0 ? comparison : a.CompareTo(b);
        }
    }
}
