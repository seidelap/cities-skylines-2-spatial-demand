using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialDemand.Core
{
    public sealed class TenantBid
    {
        public long Tenant;
        public double Rent;
    }

    public sealed class DevelopmentProject
    {
        public long Id, Site;
        public int Units;
        public double ConstructionCost, UpkeepPerDay, PaybackDays;
        public IReadOnlyList<TenantBid> Bids = Array.Empty<TenantBid>();
    }

    public sealed class DevelopmentChoice
    {
        public DevelopmentProject Project = null!;
        public double RentPerUnit, DailyRevenue, Surplus;
        public IReadOnlyList<long> Tenants = Array.Empty<long>();
        public string Reason = "no-tenants";
    }

    // Developers choose between explicit offers. No demand bars or population growth curve.
    // Costs and payback horizon are quotes supplied by the adapter, not inferred here.
    public sealed class DevelopmentMarket
    {
        private readonly HashSet<long> committedTenants, committedSites;
        public DevelopmentMarket(IEnumerable<long>? tenants = null, IEnumerable<long>? sites = null)
        {
            committedTenants = new HashSet<long>(tenants ?? Array.Empty<long>());
            committedSites = new HashSet<long>(sites ?? Array.Empty<long>());
        }

        public DevelopmentChoice Evaluate(DevelopmentProject project)
        {
            var result = new DevelopmentChoice { Project = project };
            if (project.Units <= 0 || !Valid(project.ConstructionCost) || !Valid(project.UpkeepPerDay) ||
                !Valid(project.PaybackDays) || project.PaybackDays <= 0)
            { result.Reason = "invalid-project"; return result; }
            if (committedSites.Contains(project.Site)) { result.Reason = "site-committed"; return result; }
            var bids = project.Bids.Where(b => !committedTenants.Contains(b.Tenant) && Valid(b.Rent) && b.Rent > 0)
                .GroupBy(b => b.Tenant).Select(g => g.OrderByDescending(b => b.Rent).First())
                .OrderByDescending(b => b.Rent).ThenBy(b => b.Tenant).ToArray();
            // Every occupied unit pays the marginal accepted bid. Trying each possible
            // occupancy is the exact posted-rent revenue optimum for this finite bid list.
            double bestRevenue = -1;
            for (int count = 1; count <= Math.Min(project.Units, bids.Length); count++)
            {
                double rent = bids[count - 1].Rent;
                double revenue = count * rent;
                if (revenue <= bestRevenue) continue;
                bestRevenue = revenue;
                result.RentPerUnit = rent;
                result.DailyRevenue = revenue;
                result.Tenants = bids.Take(count).Select(b => b.Tenant).ToArray();
            }
            if (result.Tenants.Count == 0) return result;
            result.Surplus = (result.DailyRevenue - project.UpkeepPerDay) * project.PaybackDays - project.ConstructionCost;
            result.Reason = Valid(result.Surplus) && result.Surplus > 0 ? "funded" : "costs-not-covered";
            return result;
        }

        public DevelopmentChoice? Choose(IEnumerable<DevelopmentProject> projects)
        {
            DevelopmentChoice? best = null;
            foreach (var project in projects.OrderBy(p => p.Id))
            {
                var choice = Evaluate(project);
                if (choice.Reason == "funded" && (best == null || choice.Surplus > best.Surplus)) best = choice;
            }
            return best;
        }

        public void Commit(DevelopmentChoice choice)
        {
            if (choice.Reason != "funded" || committedSites.Contains(choice.Project.Site) || choice.Tenants.Any(committedTenants.Contains))
                throw new InvalidOperationException("Project must be reevaluated before commitment.");
            committedSites.Add(choice.Project.Site);
            foreach (var tenant in choice.Tenants) committedTenants.Add(tenant);
        }

        public static double HousingBid(Household household, HomeOffer proposed, double outsideUtility = 0, double movingCost = 0.05)
        {
            if (!Valid(outsideUtility) || !Valid(movingCost)) return 0;
            double low = 0, high = household.Income * household.Preferences.MaxRentShare;
            if (!Valid(high)) return 0;
            bool Accepts(double rent)
            {
                var value = Housing.Evaluate(household, new HomeOffer(proposed.Id, rent, proposed.Space, proposed.TravelSeconds, 1));
                return value.Feasible && value.Utility - movingCost > outsideUtility;
            }
            if (!Accepts(0)) return 0;
            // The existing household evaluator remains the authority; do not duplicate its formula.
            for (int i = 0; i < 40; i++)
            {
                double middle = (low + high) / 2;
                if (Accepts(middle)) low = middle; else high = middle;
            }
            return low;
        }

        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}
