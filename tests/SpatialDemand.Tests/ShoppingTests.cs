using System;
using System.Linq;
using SpatialDemand.Core;

internal static class ShoppingTests
{
    private static ShopOffer Shop(long id, double price = 2, double seconds = 60, int stock = 10,
        double tripCharge = 0, bool reachable = true, long resource = 1)
        => new ShopOffer(id, resource, stock, price, seconds, tripCharge, reachable);

    private static ShoppingSearch Search(ShopOffer[] offers, long id = 1, int quantity = 10,
        double budget = 100, double timeValue = 36, long? incumbent = null)
        => new ShoppingSearch(id, 1, quantity, budget, timeValue, offers, incumbent);

    internal static void Run()
    {
        Check("same-price nearest route", () => Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1, seconds: 300), Shop(2) })).SellerId));
        Check("same-route cheaper shop", () => Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 3), Shop(2) })).SellerId));
        Check("cheap distant shop wins worthwhile saving", () =>
            Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 3, seconds: 0), Shop(2, price: 1, seconds: 1000) })).SellerId));
        Check("near expensive shop wins costly travel", () =>
            Equal(1L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 3, seconds: 0), Shop(2, price: 1, seconds: 3000) })).SellerId));
        Check("no radius preference or cutoff", () =>
            Equal(1L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 0, seconds: 200000) }, timeValue: 0)).SellerId));
        Check("one trip is charged once per basket", () =>
        {
            var search = Search(new[] { Shop(1, price: 1, tripCharge: 5) });
            var value = ShoppingMarket.Evaluate(search, search.Offers[0]);
            Equal(10d, value.BasketPrice); Equal(5d, value.TripCharge); Equal(15d, value.Payment);
            Near(0.6, value.TimeCost); Near(-15.6, value.Utility);
        });
        Check("fare can change selected shop", () =>
            Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 1, tripCharge: 15), Shop(2, price: 2) })).SellerId));
        Check("price and actual trip charge constrain cash", () =>
        {
            var search = Search(new[] { Shop(1, tripCharge: 2) }, budget: 21);
            Equal(ShoppingRejection.Unaffordable, ShoppingMarket.Evaluate(search, search.Offers[0]).Rejection);
            True(!ShoppingMarket.Choose(search).Selected);
            Equal(1L, ShoppingMarket.Choose(Search(new[] { Shop(1, tripCharge: 2) }, budget: 22)).SellerId);
        });
        Check("time valuation is not a cash withdrawal", () =>
        {
            var choice = ShoppingMarket.Choose(Search(new[] { Shop(1, seconds: 3600) }, budget: 20, timeValue: 100));
            Equal(1L, choice.SellerId); Equal(20d, choice.Evaluation.Payment); Equal(100d, choice.Evaluation.TimeCost);
        });
        Check("free basket with zero cash is finite", () =>
        {
            var choice = ShoppingMarket.Choose(Search(new[] { Shop(1, price: 0, seconds: 0) }, budget: 0));
            True(choice.Selected); Equal(0d, choice.Evaluation.Utility);
        });
        Check("unreachable shop cannot win cheap price", () =>
            Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1, price: 0, reachable: false), Shop(2) })).SellerId));
        Check("wrong resource and partial basket are excluded", () =>
        {
            var search = Search(new[] { Shop(1, resource: 2), Shop(2, stock: 9) });
            Equal(ShoppingRejection.WrongResource, ShoppingMarket.Evaluate(search, search.Offers[0]).Rejection);
            Equal(ShoppingRejection.InsufficientStock, ShoppingMarket.Evaluate(search, search.Offers[1]).Rejection);
            True(!ShoppingMarket.Choose(search).Selected);
        });
        Check("incumbent wins equal utility; stable fallback ties", () =>
        {
            Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(1), Shop(2) }, incumbent: 2)).SellerId);
            Equal(2L, ShoppingMarket.Choose(Search(new[] { Shop(2), Shop(1) }, incumbent: 2)).SellerId);
            Equal(1L, ShoppingMarket.Choose(Search(new[] { Shop(2), Shop(1) })).SellerId);
            Equal(1L, ShoppingMarket.Choose(Search(new[] { Shop(1), Shop(2, price: 3) }, incumbent: 2)).SellerId);
        });
        Check("invalid terms and overflowing totals are rejected", () =>
        {
            foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var valid = Search(new[] { Shop(1) });
                Equal(ShoppingRejection.InvalidOffer, ShoppingMarket.Evaluate(valid, Shop(1, price: bad)).Rejection);
                Equal(ShoppingRejection.InvalidOffer, ShoppingMarket.Evaluate(valid, Shop(1, seconds: bad)).Rejection);
                Equal(ShoppingRejection.InvalidOffer, ShoppingMarket.Evaluate(valid, Shop(1, tripCharge: bad)).Rejection);
                True(!ShoppingMarket.Choose(Search(new[] { Shop(1) }, budget: bad)).Selected);
                True(!ShoppingMarket.Choose(Search(new[] { Shop(1) }, timeValue: bad)).Selected);
            }
            True(!ShoppingMarket.Choose(Search(new[] { Shop(1, price: double.MaxValue) }, budget: double.MaxValue)).Selected);
            True(!ShoppingMarket.Choose(Search(new[] { Shop(1, seconds: double.MaxValue) }, timeValue: double.MaxValue)).Selected);
            True(!ShoppingMarket.Choose(Search(new[] { Shop(1, stock: -1) })).Selected);
            True(!ShoppingMarket.Choose(Search(new[] { Shop(1) }, quantity: 0)).Selected);
            True(!ShoppingMarket.Choose(Search(new[] { Shop(1) }, quantity: -1)).Selected);
        });
        Check("empty market defers without inventing a seller", () =>
        {
            var choice = ShoppingMarket.Choose(Search(Array.Empty<ShopOffer>()));
            True(!choice.Selected); Equal(0, choice.Quantity); Equal(ShoppingRejection.NoFeasibleOffer, choice.Evaluation.Rejection);
        });
        Check("stock reserved once with next-best fallback", () =>
        {
            var offers = new[] { Shop(1, price: 1), Shop(2, price: 2) };
            var searches = new[] { Search(offers, id: 1), Search(offers, id: 2), Search(offers, id: 3) };
            var choices = ShoppingMarket.Clear(searches, 0);
            Equal(2, choices.Count(c => c.Selected));
            Equal(1, choices.Count(c => c.SellerId == 1)); Equal(1, choices.Count(c => c.SellerId == 2));
            // A fresh planning batch reads the same supplied stock, not a hidden ledger.
            Equal(Signature(choices), Signature(ShoppingMarket.Clear(searches, 0)));
            Equal(Signature(choices), Signature(ShoppingMarket.Clear(searches.Reverse().ToArray(), 0)));
            Equal(10, offers[0].Stock);
        });
        Check("inconsistent stock and duplicate shopper rejected", () =>
        {
            Throws(() => ShoppingMarket.Clear(new[] { Search(new[] { Shop(1) }), Search(new[] { Shop(1) }) }, 0));
            Throws(() => ShoppingMarket.Clear(new[] { Search(new[] { Shop(1) }, id: 1), Search(new[] { Shop(1, stock: 20) }, id: 2) }, 0));
        });
    }

    private static string Signature(System.Collections.Generic.IReadOnlyList<ShoppingChoice> choices)
        => string.Join(";", choices.OrderBy(c => c.ShopperId).Select(c => $"{c.ShopperId}:{c.SellerId}"));
    private static void Check(string name, Action check)
    { try { check(); } catch (Exception error) { throw new Exception("Shopping: " + name, error); } }
    private static void True(bool value) { if (!value) throw new Exception("Expected true."); }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > 1e-8) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected ArgumentException."); }
}
