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
        private readonly Func<long, double, double, bool, double>? transportQuote;

        public BusinessMarket(IEnumerable<Purchase> buyers, IEnumerable<StockOffer> sellers,
            double range, double deliveryPerUnitMetre,
            Func<long, double, double, bool, double>? transportQuote = null)
        {
            if (!Valid(range) || !Valid(deliveryPerUnitMetre)) throw new ArgumentException("Invalid delivery assumptions.");
            this.buyers = buyers.OrderBy(b => b.Id).ToList();
            this.sellers = sellers.OrderBy(s => s.Id).ToList();
            buyersByResource = this.buyers.ToLookup(b => Tuple.Create(b.Resource, b.Retail));
            sellersByResource = this.sellers.ToLookup(s => Tuple.Create(s.Resource, s.Retail));
            demand = this.buyers.ToDictionary(b => b.Id, b => Valid(b.Quantity) ? b.Quantity : 0);
            stock = this.sellers.ToDictionary(s => s.Id, s => Valid(s.Quantity) ? s.Quantity : 0);
            this.range = range; this.deliveryPerUnitMetre = deliveryPerUnitMetre;
            this.transportQuote = transportQuote;
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
                        !double.IsInfinity(Delivered(s.Price, s.Resource, Math.Min(stock[s.Id], a.Capacity * input.Value), false, s.X, s.Z, a.X, a.Z))).Sum(s => stock[s.Id]) / input.Value);
            if (capacity <= 0) { result.Reason = "missing-input-stock"; return result; }
            var competingStock = new Dictionary<long, double>(stock);
            foreach (var b in buyersByResource[Tuple.Create(a.Output, a.Retail)])
            {
                if (result.Quantity >= capacity) break;
                if (b.Resource != a.Output || b.Retail != a.Retail || !Valid(b.BudgetPerUnit)) continue;
                double unmet = demand[b.Id];
                while (unmet > 0 && result.Quantity < capacity)
                {
                    double amount = Math.Min(unmet, capacity - result.Quantity);
                    double quote = Delivered(a.Price, a.Output, amount, a.Retail, a.X, a.Z, b.X, b.Z);
                    if (!Affordable(a.Price, quote, b)) quote = double.PositiveInfinity;
                    StockOffer? bestSeller = null;
                    foreach (var incumbent in sellersByResource[Tuple.Create(b.Resource, b.Retail)])
                    {
                        double supplied = Math.Min(unmet, competingStock[incumbent.Id]);
                        double alternative = Delivered(incumbent.Price, incumbent.Resource, supplied, incumbent.Retail,
                            incumbent.X, incumbent.Z, b.X, b.Z);
                        if (!Affordable(incumbent.Price, alternative, b)) continue;
                        // Incumbents win ties with entry, then stable seller order.
                        if (alternative < quote || (alternative == quote &&
                            (bestSeller == null || incumbent.Id < bestSeller.Id)))
                        { bestSeller = incumbent; quote = alternative; amount = supplied; }
                    }
                    if (double.IsInfinity(quote)) break;
                    if (bestSeller != null) competingStock[bestSeller.Id] -= amount;
                    else
                    {
                        result.Sales.TryGetValue(b.Id, out double prior);
                        result.Sales[b.Id] = prior + amount;
                        result.Quantity += amount;
                    }
                    unmet -= amount;
                    // Requote each feasible shipment after stock/demand changes. A trip
                    // cannot be amortized over the original order when only part is sold.
                }
            }
            if (result.Quantity <= 0) return result;
            foreach (var input in a.Inputs)
            {
                double needed = result.Quantity * input.Value;
                while (needed > 1e-8)
                {
                    StockOffer? bestSeller = null;
                    double bestPrice = double.PositiveInfinity, bestAmount = 0;
                    foreach (var s in sellersByResource[Tuple.Create(input.Key, false)])
                    {
                        result.Purchases.TryGetValue(s.Id, out double reserved);
                        double amount = Math.Min(stock[s.Id] - reserved, needed);
                        double price = Delivered(s.Price, s.Resource, amount, false, s.X, s.Z, a.X, a.Z);
                        if (price < bestPrice || (price == bestPrice && bestSeller != null && s.Id < bestSeller.Id))
                        { bestSeller = s; bestPrice = price; bestAmount = amount; }
                    }
                    if (bestSeller == null) break;
                    result.Purchases.TryGetValue(bestSeller.Id, out double prior);
                    result.Purchases[bestSeller.Id] = prior + bestAmount;
                    result.InputCost += bestAmount * bestPrice;
                    needed -= bestAmount;
                    // Shipment charges depend on quantity. Reordering only once before
                    // sourcing would retain stale unit quotes for the smaller remainder.
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

        private double Delivered(double price, long resource, double quantity, bool retail, double x, double z, double toX, double toZ)
        {
            if (!Valid(price) || !Valid(quantity) || quantity <= 0 || !Finite(x) || !Finite(z) || !Finite(toX) || !Finite(toZ)) return double.PositiveInfinity;
            double distance = Math.Sqrt((x - toX) * (x - toX) + (z - toZ) * (z - toZ));
            // Zero means no artificial radius; candidate snapshot size still bounds work.
            if (!Finite(distance) || (range > 0 && distance > range)) return double.PositiveInfinity;
            double extra = transportQuote == null ? distance * deliveryPerUnitMetre : transportQuote(resource, quantity, distance, retail) / quantity;
            return Valid(extra) && Finite(price + extra) ? price + extra : double.PositiveInfinity;
        }
        private bool Affordable(double price, double delivered, Purchase buyer)
        {
            // BudgetPerUnit remains the conservative ceiling for the original order.
            // Retail time affects preference; it is not another withdrawal of cash.
            double payment = buyer.Retail && transportQuote != null ? price : delivered;
            return Finite(delivered) && payment <= buyer.BudgetPerUnit;
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static bool Valid(double n) => Finite(n) && n >= 0;
    }
}
