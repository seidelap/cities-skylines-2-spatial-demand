using System;
using System.Collections.Generic;

namespace SpatialDemand.Core
{
    // One fixed basket per shopper. Budget is spendable cash; time is a preference,
    // not a second charge. Prices, trip charges and time valuation use the same currency.
    public sealed class ShoppingSearch
    {
        public long ShopperId { get; }
        public long Resource { get; }
        public int Quantity { get; }
        public double Budget { get; }
        public double TimeValuePerHour { get; }
        public long? IncumbentSeller { get; }
        public IReadOnlyList<ShopOffer> Offers { get; }

        public ShoppingSearch(long shopperId, long resource, int quantity, double budget,
            double timeValuePerHour, IReadOnlyList<ShopOffer> offers, long? incumbentSeller = null)
        {
            ShopperId = shopperId; Resource = resource; Quantity = quantity; Budget = budget;
            TimeValuePerHour = timeValuePerHour; IncumbentSeller = incumbentSeller;
            Offers = offers ?? throw new ArgumentNullException(nameof(offers));
        }
    }

    public readonly struct ShopOffer
    {
        public readonly long SellerId, Resource;
        public readonly int Stock;
        public readonly double UnitPrice, TravelSeconds, TripCharge;
        public readonly bool Reachable;

        // TravelSeconds must come from a comparable route quote (including whichever
        // trip legs the caller values). TripCharge is a separate quoted fare/delivery
        // charge, not a pathfinder's generalized cost or the value of the shopper's time.
        public ShopOffer(long sellerId, long resource, int stock, double unitPrice,
            double travelSeconds, double tripCharge = 0, bool reachable = true)
        {
            SellerId = sellerId; Resource = resource; Stock = stock; UnitPrice = unitPrice;
            TravelSeconds = travelSeconds; TripCharge = tripCharge; Reachable = reachable;
        }
    }

    public enum ShoppingRejection { None, InvalidSearch, InvalidOffer, WrongResource, Unreachable, InsufficientStock, Unaffordable, NoFeasibleOffer }

    public readonly struct ShoppingEvaluation
    {
        public readonly ShoppingRejection Rejection;
        public readonly double BasketPrice, TripCharge, TimeCost, Payment, Utility;
        public bool Feasible => Rejection == ShoppingRejection.None;

        internal ShoppingEvaluation(ShoppingRejection rejection, double basketPrice = 0,
            double tripCharge = 0, double timeCost = 0)
        {
            Rejection = rejection; BasketPrice = basketPrice; TripCharge = tripCharge;
            TimeCost = timeCost; Payment = basketPrice + tripCharge;
            Utility = rejection == ShoppingRejection.None ? -(Payment + timeCost) : double.NegativeInfinity;
        }
    }

    public readonly struct ShoppingChoice
    {
        public readonly long ShopperId;
        public readonly long? SellerId;
        public readonly int Quantity;
        public readonly ShoppingEvaluation Evaluation;
        public bool Selected => SellerId.HasValue;

        internal ShoppingChoice(long shopperId, long? sellerId, int quantity, ShoppingEvaluation evaluation)
        { ShopperId = shopperId; SellerId = sellerId; Quantity = quantity; Evaluation = evaluation; }
    }

    // A portable preference rule, not a game shopping replacement or transaction ledger.
    // The game currently exposes one winning shopping route, not a candidate shortlist.
    // A future adapter must obtain comparable routes and let vanilla settle any purchase.
    public static class ShoppingMarket
    {
        public static ShoppingEvaluation Evaluate(ShoppingSearch search, ShopOffer offer)
        {
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (search.Quantity <= 0 || !NonNegative(search.Budget) || !NonNegative(search.TimeValuePerHour))
                return new ShoppingEvaluation(ShoppingRejection.InvalidSearch);
            if (offer.Stock < 0 || !NonNegative(offer.UnitPrice) || !NonNegative(offer.TravelSeconds) || !NonNegative(offer.TripCharge))
                return new ShoppingEvaluation(ShoppingRejection.InvalidOffer);
            if (offer.Resource != search.Resource) return new ShoppingEvaluation(ShoppingRejection.WrongResource);
            if (!offer.Reachable) return new ShoppingEvaluation(ShoppingRejection.Unreachable);
            if (offer.Stock < search.Quantity) return new ShoppingEvaluation(ShoppingRejection.InsufficientStock);
            double basket = search.Quantity * offer.UnitPrice;
            double time = offer.TravelSeconds / 3600 * search.TimeValuePerHour;
            double payment = basket + offer.TripCharge;
            if (!NonNegative(payment) || !NonNegative(time) || !NonNegative(payment + time))
                return new ShoppingEvaluation(ShoppingRejection.InvalidOffer);
            if (payment > search.Budget) return new ShoppingEvaluation(ShoppingRejection.Unaffordable);
            // Same resource and quantity: their consumption benefit is constant. Thus
            // one argmax suffices; no distance radius or extra demand formula is needed.
            return new ShoppingEvaluation(ShoppingRejection.None, basket, offer.TripCharge, time);
        }

        public static ShoppingChoice Choose(ShoppingSearch search)
        {
            if (search == null) throw new ArgumentNullException(nameof(search));
            long? best = null;
            var evaluation = new ShoppingEvaluation(ShoppingRejection.NoFeasibleOffer);
            foreach (var offer in search.Offers)
            {
                var candidate = Evaluate(search, offer);
                if (!candidate.Feasible) continue;
                bool tie = candidate.Utility == evaluation.Utility && best.HasValue &&
                    (offer.SellerId == search.IncumbentSeller || (best != search.IncumbentSeller && offer.SellerId < best.Value));
                if (!best.HasValue || candidate.Utility > evaluation.Utility || tie)
                { best = offer.SellerId; evaluation = candidate; }
            }
            // This is an already desired basket. Defer only when no complete affordable
            // basket is feasible; comparing partial baskets would need a benefit model.
            return new ShoppingChoice(search.ShopperId, best, best.HasValue ? search.Quantity : 0, evaluation);
        }

        public static IReadOnlyList<ShoppingChoice> Clear(IReadOnlyList<ShoppingSearch> searches, uint round)
        {
            if (searches == null) throw new ArgumentNullException(nameof(searches));
            var remaining = new Dictionary<Tuple<long, long>, int>();
            var shoppers = new HashSet<long>();
            var ordered = new List<ShoppingSearch>(searches.Count);
            foreach (var search in searches)
            {
                if (search == null || !shoppers.Add(search.ShopperId)) throw new ArgumentException("One basket per shopper per batch is required.");
                ordered.Add(search);
                foreach (var offer in search.Offers)
                {
                    var key = Tuple.Create(offer.SellerId, offer.Resource);
                    if (remaining.TryGetValue(key, out int stock) && stock != offer.Stock)
                        throw new ArgumentException("Offers disagree about remaining stock.");
                    remaining[key] = offer.Stock;
                }
            }
            ordered.Sort((a, b) =>
            {
                uint ah = StableHash.Mix(unchecked((uint)a.ShopperId ^ (uint)(a.ShopperId >> 32) ^ round));
                uint bh = StableHash.Mix(unchecked((uint)b.ShopperId ^ (uint)(b.ShopperId >> 32) ^ round));
                int comparison = ah.CompareTo(bh);
                return comparison != 0 ? comparison : a.ShopperId.CompareTo(b.ShopperId);
            });
            var choices = new List<ShoppingChoice>(ordered.Count);
            foreach (var search in ordered)
            {
                var offers = new List<ShopOffer>(search.Offers.Count);
                foreach (var offer in search.Offers)
                    offers.Add(new ShopOffer(offer.SellerId, offer.Resource, remaining[Tuple.Create(offer.SellerId, offer.Resource)],
                        offer.UnitPrice, offer.TravelSeconds, offer.TripCharge, offer.Reachable));
                var choice = Choose(new ShoppingSearch(search.ShopperId, search.Resource, search.Quantity,
                    search.Budget, search.TimeValuePerHour, offers, search.IncumbentSeller));
                if (choice.Selected) remaining[Tuple.Create(choice.SellerId!.Value, search.Resource)] -= choice.Quantity;
                choices.Add(choice);
            }
            return choices; // Reservations last only for this planning batch; no money/stock writes.
        }

        private static bool NonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
