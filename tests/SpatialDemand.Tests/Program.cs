using System;
using System.Collections.Generic;
using System.Linq;
using SpatialDemand.Core;

internal static class Program
{
    private static int passed, failed;
    private static readonly Preferences Typical = new Preferences(0.5, 2, 0.1);
    private static Household Person(long id = 1, double income = 100) => new Household(id, 1, income, Typical);
    private static HomeOffer Home(long id, double rent = 20, double space = 2, double seconds = 0, int available = 1)
        => new HomeOffer(id, rent, space, seconds, available);
    private static HomeChoice Choose(params HomeOffer[] offers)
        => HousingMarket.Clear(new[] { new HomeSearch(Person(), offers) }, 0)[0];

    private static int Main()
    {
        BusinessTests();
        Test("cheaper otherwise-identical home wins", () => Equal(2L, Choose(Home(1, 30), Home(2, 20)).HomeId));
        Test("actual route duration affects choice", () => Equal(2L, Choose(Home(1, seconds: 7200), Home(2)).HomeId));
        Test("affordability is a hard constraint", () => Equal(Rejection.Unaffordable, Housing.Evaluate(Person(), Home(1, 51)).Rejection));
        Test("affordable boundary is inclusive", () => True(Housing.Evaluate(Person(), Home(1, 50)).Feasible));
        Test("outside option wins against bad offers", () => Equal(null, Choose(Home(1, seconds: 50000)).HomeId));
        Test("empty market is a valid no-move decision", () => Equal(null, Choose().HomeId));
        Test("unreachable and invalid offers are rejected", () =>
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            {
                Equal(Rejection.InvalidOffer, Housing.Evaluate(Person(), Home(1, seconds: bad)).Rejection);
                Equal(Rejection.InvalidOffer, Housing.Evaluate(Person(), Home(1, rent: bad)).Rejection);
                Equal(Rejection.InvalidOffer, Housing.Evaluate(Person(), Home(1, space: bad)).Rejection);
            }
        });
        Test("zero income can choose free housing without NaN", () =>
        {
            True(Housing.Evaluate(Person(income: 0), Home(1, rent: 0)).Feasible);
            Equal(1d, Housing.Evaluate(Person(income: 0), Home(1, rent: 0)).Utility);
            Equal(Rejection.Unaffordable, Housing.Evaluate(Person(income: 0), Home(1)).Rejection);
        });
        Test("larger family values adequate space", () =>
        {
            var offers = new[] { Home(1, 15, 2), Home(2, 25, 4) };
            Equal(1L, HousingMarket.Clear(new[] { new HomeSearch(Person(), offers) }, 0)[0].HomeId);
            Equal(2L, HousingMarket.Clear(new[] { new HomeSearch(new Household(1, 2, 100, Typical), offers) }, 0)[0].HomeId);
        });
        Test("full home cannot accept a newcomer", () => Equal(null, Choose(Home(1, available: 0)).HomeId));
        Test("incumbent keeps its occupied unit", () =>
        {
            var choice = HousingMarket.Clear(new[] { new HomeSearch(Person(), new[] { Home(1, available: 0) }, currentHome: 1) }, 0)[0];
            Equal(1L, choice.HomeId); True(!choice.Moved);
        });
        Test("moving cost discourages trivial relocation", () =>
        {
            var choice = HousingMarket.Clear(new[] { new HomeSearch(Person(), new[] { Home(1, 20), Home(2, 19) }, currentHome: 1) }, 0)[0];
            Equal(1L, choice.HomeId);
        });
        Test("one vacancy is assigned once", () =>
        {
            var searches = Enumerable.Range(1, 30).Select(i => new HomeSearch(Person(i), new[] { Home(99) })).ToArray();
            Equal(1, HousingMarket.Clear(searches, 0).Count(c => c.Moved));
        });
        Test("losing bidder can take its next-best home", () =>
        {
            var searches = Enumerable.Range(1, 2).Select(i => new HomeSearch(Person(i), new[] { Home(1), Home(2, 30) })).ToArray();
            Equal(2, HousingMarket.Clear(searches, 0).Select(c => c.HomeId).Distinct().Count());
            True(HousingMarket.Clear(searches, 0).All(c => c.Moved));
        });
        Test("input order cannot change allocations", () =>
        {
            var searches = Enumerable.Range(1, 20).Select(i => new HomeSearch(Person(i), new[] { Home(1, available: 3), Home(2, 30, available: 2) })).ToArray();
            Equal(Signature(HousingMarket.Clear(searches, 9)), Signature(HousingMarket.Clear(searches.Reverse().ToArray(), 9)));
        });
        Test("offer order cannot change ties", () => Equal(1L, Choose(Home(2), Home(1)).HomeId));
        Test("tie with outside does not force a move", () =>
        {
            var result = HousingMarket.Clear(new[] { new HomeSearch(Person(), new[] { Home(1, 50) }, outsideUtility: 0.5, movingCost: 0) }, 0);
            Equal(null, result[0].HomeId);
        });
        Test("incumbent wins an equal-utility tie", () =>
        {
            var result = HousingMarket.Clear(new[] { new HomeSearch(Person(), new[] { Home(1), Home(2) }, currentHome: 2, movingCost: 0) }, 0);
            Equal(2L, result[0].HomeId);
        });
        Test("departing unit is not offered before settlement", () =>
        {
            var search = new[] {
                new HomeSearch(Person(1), new[] { Home(1, 40, available: 0), Home(2) }, currentHome: 1),
                new HomeSearch(Person(2), new[] { Home(1, 40, available: 0) }) };
            Equal(null, HousingMarket.Clear(search, 0).Single(c => c.HouseholdId == 2).HomeId);
        });
        Test("different incumbent lease and asking rent coexist", () =>
        {
            var searches = new[] { new HomeSearch(Person(1), new[] { Home(1, 10) }, currentHome: 1),
                new HomeSearch(Person(2), new[] { Home(1, 20) }) };
            Equal(2, HousingMarket.Clear(searches, 0).Count(c => c.HomeId == 1));
        });
        Test("duplicate households cannot acquire two homes", () => Throws(() => HousingMarket.Clear(new[] {
            new HomeSearch(Person(), new[] { Home(1) }), new HomeSearch(Person(), new[] { Home(2) }) }, 0)));
        Test("contradictory capacity fails before any settlement", () => Throws(() => HousingMarket.Clear(new[] {
            new HomeSearch(Person(1), new[] { Home(1) }), new HomeSearch(Person(2), new[] { Home(1, available: 2) }) }, 0)));
        Test("round priority does not permanently favor one bidder", () =>
        {
            var bidders = Enumerable.Range(1, 4).Select(i => new HomeSearch(Person(i), new[] { Home(9) })).ToArray();
            var winners = Enumerable.Range(0, 100).Select(r => HousingMarket.Clear(bidders, (uint)r).Single(c => c.Moved).HouseholdId);
            Equal(4, winners.Distinct().Count());
        });
        Test("saved preference seed has a frozen version-one interpretation", () =>
        {
            var p = Preferences.FromSeed(1234);
            True(Math.Abs(p.MaxRentShare - 0.3354095755227398) < 1e-14);
            True(Math.Abs(p.SpacePerPerson - 2.1745648850161965) < 1e-14);
            True(Math.Abs(p.TravelCostPerHour - 0.09615947856711211) < 1e-14);
        });
        Test("random markets respect capacity, budgets and each sequential argmax", CheckRandomMarkets);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void CheckRandomMarkets()
    {
        var random = new Random(17);
        for (uint round = 0; round < 300; round++)
        {
            var offers = Enumerable.Range(1, 12).Select(i => Home(i, random.Next(0, 101), random.Next(1, 8), random.Next(0, 7200), random.Next(0, 4))).ToArray();
            var people = Enumerable.Range(1, 30).Select(i => new Household(i, random.Next(1, 5), random.Next(0, 201), Preferences.FromSeed((uint)i))).ToArray();
            var searches = people.Select(p => new HomeSearch(p, offers)).ToArray();
            var capacity = offers.ToDictionary(o => o.Id, o => o.Available);
            foreach (var choice in HousingMarket.Clear(searches, round))
            {
                var search = searches.Single(s => s.Household.Id == choice.HouseholdId);
                double best = 0;
                // Independent exhaustive scan checks the allocation against all still available offers.
                foreach (var offer in offers)
                {
                    if (capacity[offer.Id] == 0 || offer.Rent > search.Household.Income * search.Household.Preferences.MaxRentShare) continue;
                    var p = search.Household.Preferences;
                    double space = Math.Min(1, offer.Space / (search.Household.People * p.SpacePerPerson));
                    double rent = offer.Rent == 0 ? 0 : offer.Rent / search.Household.Income;
                    best = Math.Max(best, space - rent - offer.TravelSeconds / 3600 * p.TravelCostPerHour - search.MovingCost);
                }
                True(Math.Abs(best - choice.Utility) < 1e-10);
                if (!choice.HomeId.HasValue) continue;
                var selected = offers.Single(o => o.Id == choice.HomeId.Value);
                True(--capacity[selected.Id] >= 0);
                True(selected.Rent <= search.Household.Income * search.Household.Preferences.MaxRentShare);
            }
        }
    }

    private static string Signature(IReadOnlyList<HomeChoice> choices)
        => string.Join(";", choices.OrderBy(c => c.HouseholdId).Select(c => $"{c.HouseholdId}:{c.HomeId}"));

    private static void BusinessTests()
    {
        Purchase Buyer(long id = 1, long resource = 1, double quantity = 10) =>
            new Purchase { Id = id, Resource = resource, Quantity = quantity, BudgetPerUnit = 100, Retail = true };
        StockOffer Supplier(double price = 2, double quantity = 100) =>
            new StockOffer { Id = 1, Resource = 2, Price = price, Quantity = quantity };
        BusinessActivity Shop(long id = 1, double price = 10, double fixedCost = 5)
        {
            var a = new BusinessActivity { Id = id, Output = 1, Retail = true, Price = price, Capacity = 10, FixedCost = fixedCost };
            a.Inputs[2] = 1; return a;
        }
        BusinessMarket Book(Purchase[]? buyers = null, StockOffer[]? suppliers = null) =>
            new BusinessMarket(buyers ?? new[] { Buyer() }, suppliers ?? new[] { Supplier() }, 1000, 0.01);
        Test("business accounts for sales inputs and fixed costs", () => Equal(75d, Book().Evaluate(Shop()).Profit));
        Test("business no-entry beats a loss", () => Equal(null, Book().Choose(new[] { Shop(fixedCost: 1000) })));
        Test("business chooses highest surplus activity", () => Equal(2L, Book().Choose(new[] { Shop(), Shop(2, 12) })!.Activity.Id));
        Test("business only sells a requested resource", () => Equal(null, Book(new[] { Buyer(resource: 9) }).Choose(new[] { Shop() })));
        Test("business needs available inputs", () => Equal("missing-input-stock", Book(suppliers: Array.Empty<StockOffer>()).Evaluate(Shop()).Reason));
        Test("business can operate below demand when inputs are limited", () => Equal(4d, Book(suppliers: new[] { Supplier(quantity: 4) }).Evaluate(Shop()).Quantity));
        Test("business lower delivered input cost improves surplus", () => True(Book(suppliers: new[] { Supplier(1) }).Evaluate(Shop()).Profit > Book().Evaluate(Shop()).Profit));
        Test("business buyers prefer a cheaper incumbent", () =>
        {
            var rival = new StockOffer { Id = 2, Resource = 1, Quantity = 100, Price = 9, Retail = true };
            Equal(null, Book(suppliers: new[] { Supplier(), rival }).Choose(new[] { Shop() }));
        });
        Test("business supplier beyond range is unavailable", () =>
        { var supplier = Supplier(); supplier.X = 1001; Equal("missing-input-stock", Book(suppliers: new[] { supplier }).Evaluate(Shop()).Reason); });
        Test("business can meet demand beyond a competitor's finite stock", () =>
        {
            var rival = new StockOffer { Id = 2, Resource = 1, Quantity = 3, Price = 9, Retail = true };
            Equal(7d, Book(suppliers: new[] { Supplier(), rival }).Evaluate(Shop()).Quantity);
        });
        Test("industrial and office activities use producer buyers and recipes", () =>
        {
            var buyer = Buyer(); buyer.Retail = false;
            var factory = Shop(); factory.Retail = false;
            Equal(75d, Book(new[] { buyer }).Choose(new[] { factory })!.Profit);
        });
        Test("business buyer budget is binding", () =>
        { var buyer = Buyer(); buyer.BudgetPerUnit = 9; Equal(null, Book(new[] { buyer }).Choose(new[] { Shop() })); });
        Test("business commitment prevents duplicate buyer allocation", () =>
        { var book = Book(); book.Commit(book.Choose(new[] { Shop() })!); Equal(null, book.Choose(new[] { Shop(2) })); });
        Test("business commitment reserves input stock across products", () =>
        {
            var book = Book(new[] { Buyer(), Buyer(2, 3) }, new[] { Supplier(quantity: 10) });
            book.Commit(book.Choose(new[] { Shop() })!);
            var second = Shop(2); second.Output = 3;
            Equal(null, book.Choose(new[] { second }));
        });
        Test("business evaluating alternatives does not reserve stock", () =>
        { var book = Book(); book.Evaluate(Shop()); Equal(10d, book.Evaluate(Shop(2)).Quantity); });
        Test("business distinguishes retail and producer buyers", () =>
        { var buyer = Buyer(); buyer.Retail = false; Equal(null, Book(new[] { buyer }).Choose(new[] { Shop() })); });
        Test("business rejects invalid prices", () => Equal("invalid-activity", Book().Evaluate(Shop(price: double.NaN)).Reason));
        Test("business tie-break is independent of option ordering", () => Equal(1L, Book().Choose(new[] { Shop(2), Shop(1) })!.Activity.Id));
    }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected ArgumentException."); }
    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
    }
}
