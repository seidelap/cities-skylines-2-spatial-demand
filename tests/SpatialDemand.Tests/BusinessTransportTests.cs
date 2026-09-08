using System;
using SpatialDemand.Core;

internal static class BusinessTransportTests
{
    private static Purchase Buyer(bool retail = true) => new Purchase
        { Id = 1, Resource = 1, Quantity = 100, BudgetPerUnit = 1000, Retail = retail };
    private static BusinessActivity Firm(double capacity = 100, double price = 1, double x = 0, bool retail = true)
        => new BusinessActivity { Id = 1, Output = 1, Capacity = capacity, Price = price, X = x, Retail = retail };
    private static StockOffer Rival(long id, double stock, double price, double x = 0)
        => new StockOffer { Id = id, Resource = 1, Quantity = stock, Price = price, X = x, Retail = true };

    internal static void Run()
    {
        Check("input suppliers are reranked for residual shipments", () =>
        {
            var buyer = Buyer(); buyer.Quantity = 1000;
            var firm = Firm(capacity: 1000, price: 0.02); firm.Inputs[2] = 1;
            var sellers = new[] {
                new StockOffer { Id = 1, Resource = 2, Quantity = 900, Price = 0 },
                new StockOffer { Id = 2, Resource = 2, Quantity = 1000, Price = 0, X = 1000 },
                new StockOffer { Id = 3, Resource = 2, Quantity = 100, Price = 0.1 } };
            // A quantity-sensitive shipment charge: the large distant shipment starts
            // cheaper per unit, but the small remainder should be bought locally.
            var book = new BusinessMarket(new[] { buyer }, sellers, 0, 0,
                (r, q, d, retail) => retail ? 0 : Math.Round(d * 0.03 * (1 + Math.Floor(q / 1000))));
            var choice = book.Evaluate(firm);
            Near(10, choice.InputCost); Near(10, choice.Profit); Equal("profitable", choice.Reason);
            book.Commit(choice);
            Equal("no-buyers", book.Evaluate(firm).Reason);
        });
        Check("entrant trip uses its actual capacity", () =>
        {
            var book = new BusinessMarket(new[] { Buyer() }, new[] { Rival(2, 100, 3) }, 0, 0,
                (r, q, d, retail) => d);
            Equal("no-buyers", book.Evaluate(Firm(capacity: 1, x: 100)).Reason);
        });
        Check("incumbent trip uses its actual stock", () =>
        {
            var book = new BusinessMarket(new[] { Buyer() }, new[] { Rival(2, 1, 1, x: 100) }, 0, 0,
                (r, q, d, retail) => d);
            Near(100, book.Evaluate(Firm(price: 3)).Quantity);
        });
        Check("all customer offers are requoted after incumbent allocation", () =>
        {
            var rivals = new[] { Rival(2, 90, 0), Rival(3, 10, 4) };
            var book = new BusinessMarket(new[] { Buyer() }, rivals, 0, 0, (r, q, d, retail) => d);
            Equal("no-buyers", book.Evaluate(Firm(x: 100)).Reason);
        });
        Check("later entry quotes only demand remaining after commit", () =>
        {
            var book = new BusinessMarket(new[] { Buyer() }, new[] { Rival(2, 10, 4) }, 0, 0,
                (r, q, d, retail) => d);
            var first = book.Evaluate(Firm(capacity: 90));
            Near(90, first.Quantity); book.Commit(first);
            Equal("no-buyers", book.Evaluate(Firm(capacity: 10, x: 100)).Reason);
        });
        Check("freight cash check uses the actual shipment", () =>
        {
            var buyer = Buyer(retail: false); buyer.BudgetPerUnit = 2;
            var book = new BusinessMarket(new[] { buyer }, Array.Empty<StockOffer>(), 0, 0,
                (r, q, d, retail) => d);
            Equal("no-buyers", book.Evaluate(Firm(capacity: 1, x: 100, retail: false)).Reason);
        });
        Check("partial customer allocations cannot spend stock twice", () =>
        {
            var one = Buyer(); one.Quantity = 10;
            var two = Buyer(); two.Id = 2; two.Quantity = 10;
            var book = new BusinessMarket(new[] { one, two }, new[] { Rival(2, 15, 0) }, 0, 0,
                (r, q, d, retail) => 0);
            var choice = book.Evaluate(Firm());
            Near(5, choice.Quantity); book.Commit(choice);
            Equal("no-buyers", book.Evaluate(Firm()).Reason);
        });
    }

    private static void Check(string name, Action action)
    { try { action(); } catch (Exception error) { throw new Exception("Business transport: " + name, error); } }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > 1e-8) throw new Exception($"Expected {expected}; got {actual}."); }
}
