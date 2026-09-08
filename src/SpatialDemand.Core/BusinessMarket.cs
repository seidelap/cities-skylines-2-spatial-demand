using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialDemand.Core
{
    public sealed class Purchase
    {
        public long Id, Resource;
        public double Quantity, BudgetPerUnit, X, Z;
        public bool Retail;
    }

    public sealed class StockOffer
    {
        public long Id, Resource;
        public double Quantity, Price, X, Z;
        public bool Retail;
    }

    public sealed class BusinessActivity
    {
        public long Id, Site, Output;
        public bool Retail;
        public double X, Z, Price, Capacity, FixedCost;
        public readonly Dictionary<long, double> Inputs = new Dictionary<long, double>();
    }

    public sealed class BusinessChoice
    {
        public BusinessActivity Activity = null!;
        public double Quantity, Revenue, InputCost, Profit;
        public string Reason = "no-buyers";
        internal readonly Dictionary<long, double> Sales = new Dictionary<long, double>();
        internal readonly Dictionary<long, double> Purchases = new Dictionary<long, double>();
    }

    // A planning book, never a replacement for the game's actual stock or money ledger.
    // Quantities share one planning horizon. Location cost/range are explicit adapter inputs.
    public sealed class BusinessMarket
    {
        private readonly List<Purchase> buyers;
        private readonly List<StockOffer> sellers;
        private readonly ILookup<Tuple<long, bool>, Purchase> buyersByResource;
        private readonly ILookup<Tuple<long, bool>, StockOffer> sellersByResource;
        private readonly Dictionary<long, double> demand, stock;
        private readonly double range, deliveryPerUnitMetre;

        public BusinessMarket(IEnumerable<Purchase> buyers, IEnumerable<StockOffer> sellers,
            double range, double deliveryPerUnitMetre)
        {
            if (!Valid(range) || !Valid(deliveryPerUnitMetre)) throw new ArgumentException("Invalid delivery assumptions.");
            this.buyers = buyers.OrderBy(b => b.Id).ToList();
            this.sellers = sellers.OrderBy(s => s.Id).ToList();
            buyersByResource = this.buyers.ToLookup(b => Tuple.Create(b.Resource, b.Retail));
            sellersByResource = this.sellers.ToLookup(s => Tuple.Create(s.Resource, s.Retail));
            demand = this.buyers.ToDictionary(b => b.Id, b => Valid(b.Quantity) ? b.Quantity : 0);
            stock = this.sellers.ToDictionary(s => s.Id, s => Valid(s.Quantity) ? s.Quantity : 0);
            this.range = range; this.deliveryPerUnitMetre = deliveryPerUnitMetre;
        }

        public BusinessChoice Evaluate(BusinessActivity a)
        {
            var result = new BusinessChoice { Activity = a };
            if (!Valid(a.Price) || !Valid(a.Capacity) || !Valid(a.FixedCost) ||
                !Finite(a.X) || !Finite(a.Z) || a.Inputs.Any(p => !Valid(p.Value)))
            { result.Reason = "invalid-activity"; return result; }
            double capacity = a.Capacity;
            foreach (var input in a.Inputs)
                if (input.Value > 0)
                    capacity = Math.Min(capacity, sellersByResource[Tuple.Create(input.Key, false)].Where(s =>
                        !double.IsInfinity(Delivered(s.Price, s.X, s.Z, a.X, a.Z))).Sum(s => stock[s.Id]) / input.Value);
            if (capacity <= 0) { result.Reason = "missing-input-stock"; return result; }
            var competingStock = new Dictionary<long, double>(stock);
            foreach (var b in buyersByResource[Tuple.Create(a.Output, a.Retail)])
            {
                if (b.Resource != a.Output || b.Retail != a.Retail || !Valid(b.BudgetPerUnit)) continue;
                double quote = Delivered(a.Price, a.X, a.Z, b.X, b.Z);
                if (quote > b.BudgetPerUnit || double.IsInfinity(quote)) continue;
                // The incumbent wins equal-price ties. A new firm must improve the offer.
                double unmet = demand[b.Id];
                foreach (var incumbent in sellersByResource[Tuple.Create(b.Resource, b.Retail)]
                    .Where(s => Delivered(s.Price, s.X, s.Z, b.X, b.Z) <= quote)
                    .OrderBy(s => Delivered(s.Price, s.X, s.Z, b.X, b.Z)).ThenBy(s => s.Id))
                {
                    double supplied = Math.Min(unmet, competingStock[incumbent.Id]);
                    competingStock[incumbent.Id] -= supplied;
                    unmet -= supplied;
                    if (unmet <= 0) break;
                }
                double amount = Math.Min(unmet, capacity - result.Quantity);
                if (amount <= 0) continue;
                result.Sales[b.Id] = amount;
                result.Quantity += amount;
            }
            if (result.Quantity <= 0) return result;
            foreach (var input in a.Inputs)
            {
                double needed = result.Quantity * input.Value;
                foreach (var s in sellersByResource[Tuple.Create(input.Key, false)]
                    .OrderBy(s => Delivered(s.Price, s.X, s.Z, a.X, a.Z)).ThenBy(s => s.Id))
                {
                    double price = Delivered(s.Price, s.X, s.Z, a.X, a.Z);
                    if (double.IsInfinity(price)) continue;
                    double amount = Math.Min(stock[s.Id], needed);
                    if (amount <= 0) continue;
                    result.Purchases[s.Id] = amount;
                    result.InputCost += amount * price;
                    needed -= amount;
                    if (needed <= 1e-8) break;
                }
                if (needed > 1e-8) { result.Reason = "missing-input-stock"; return result; }
            }
            result.Revenue = result.Quantity * a.Price;
            result.Profit = result.Revenue - result.InputCost - a.FixedCost;
            result.Reason = result.Profit > 0 && Finite(result.Profit) ? "profitable" : "not-profitable";
            return result;
        }

        public BusinessChoice? Choose(IEnumerable<BusinessActivity> activities)
        {
            BusinessChoice? best = null;
            foreach (var a in activities.OrderBy(a => a.Id))
            {
                var choice = Evaluate(a);
                if (choice.Reason == "profitable" && (best == null || choice.Profit > best.Profit)) best = choice;
            }
            return best; // No entry is a real outside option.
        }

        public void Commit(BusinessChoice choice)
        {
            if (choice.Reason != "profitable") throw new InvalidOperationException("Cannot reserve rejected activity.");
            if (choice.Sales.Any(p => demand[p.Key] < p.Value) || choice.Purchases.Any(p => stock[p.Key] < p.Value))
                throw new InvalidOperationException("Stale business choice must be reevaluated.");
            foreach (var p in choice.Sales) demand[p.Key] -= p.Value;
            foreach (var p in choice.Purchases) stock[p.Key] -= p.Value;
        }

        private double Delivered(double price, double x, double z, double toX, double toZ)
        {
            if (!Valid(price) || !Finite(x) || !Finite(z) || !Finite(toX) || !Finite(toZ)) return double.PositiveInfinity;
            double distance = Math.Sqrt((x - toX) * (x - toX) + (z - toZ) * (z - toZ));
            return distance <= range ? price + distance * deliveryPerUnitMetre : double.PositiveInfinity;
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static bool Valid(double n) => Finite(n) && n >= 0;
    }
}
