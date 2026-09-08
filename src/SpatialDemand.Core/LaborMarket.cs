using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialDemand.Core
{
    public sealed class LaborWorker
    {
        public long Id;
        public int Education;
        public double X, Z, OutsideOption;
    }

    public sealed class LaborJob
    {
        public long Id, Employer;
        public int RequiredEducation;
        public double X, Z, MarginalValue;
        // Cash reserved exclusively for this one slot for the planning horizon.
        // The caller must not reserve the same employer cash for multiple slots.
        public double WageBudget;
        // Tax follows the job's education bracket in the game, including when
        // an overqualified person takes a lower-level job.
        public double TaxFreeAllowance, TakeHomeRate = 1;
        public double TakeHome(double gross) => gross <= TaxFreeAllowance ? gross :
            TaxFreeAllowance + (gross - TaxFreeAllowance) * TakeHomeRate;
        internal double GrossFor(double net) => net <= TaxFreeAllowance ? net :
            TaxFreeAllowance + (net - TaxFreeAllowance) / TakeHomeRate;
    }

    public sealed class LaborAssignment
    {
        public LaborJob Job = null!;
        public LaborWorker Worker = null!;
        public double GrossWage, TakeHomeIncome, CommuteCost;
        public double WorkerUtility => TakeHomeIncome - CommuteCost;
        public double EmployerSurplus => Job.MarginalValue - GrossWage;
    }

    public sealed class LaborResult
    {
        public readonly List<LaborAssignment> Assignments = new List<LaborAssignment>();
        public readonly List<long> UnfilledJobs = new List<long>();
        public readonly List<long> OutsideWorkers = new List<long>();
        public int Bids;
        public bool Converged;
    }

    // One worker per slot, one slot per worker. All money values use one horizon.
    // Employers choose their highest positive surplus at current wage quotes;
    // workers accept only a bid improving their take-home pay net of commute.
    // Losing employers can raise bids or leave the slot empty (surplus zero).
    // This is a deterministic incremental auction, not an exact equilibrium claim.
    // It plans contracts only: the caller owns employment and payroll settlement.
    public static class LaborMarket
    {
        public static LaborResult Clear(IEnumerable<LaborWorker> workers, IEnumerable<LaborJob> jobs,
            double range, double commuteCostPerMetre, double netBidIncrement = 1, int maxBids = 100000)
        {
            if (!Nonnegative(range) || !Nonnegative(commuteCostPerMetre) ||
                !Finite(netBidIncrement) || netBidIncrement <= 0 || maxBids <= 0)
                throw new ArgumentException("Invalid labor market assumptions.");
            var people = workers.OrderBy(w => w.Id).ToArray();
            var slots = jobs.OrderBy(j => j.Id).ToArray();
            if (people.Select(w => w.Id).Distinct().Count() != people.Length ||
                slots.Select(j => j.Id).Distinct().Count() != slots.Length)
                throw new ArgumentException("Worker and job IDs must each be unique.");
            foreach (var w in people)
                if (w.Education < 0 || !Finite(w.X) || !Finite(w.Z) || !Nonnegative(w.OutsideOption))
                    throw new ArgumentException("Invalid worker.");
            foreach (var j in slots)
                if (j.RequiredEducation < 0 || !Finite(j.X) || !Finite(j.Z) ||
                    !Nonnegative(j.MarginalValue) || !Nonnegative(j.WageBudget) ||
                    !Nonnegative(j.TaxFreeAllowance) || !Finite(j.TakeHomeRate) || j.TakeHomeRate <= 0)
                    throw new ArgumentException("Invalid job.");

            var utility = people.Select(w => w.OutsideOption).ToArray();
            var employedBy = Enumerable.Repeat(-1, people.Length).ToArray();
            var matches = new LaborAssignment?[slots.Length];
            var queue = new Queue<int>(Enumerable.Range(0, slots.Length));
            var result = new LaborResult();
            // Cache travel/qualification once; bids do not change either assumption.
            var commute = new double[slots.Length, people.Length];
            for (int j = 0; j < slots.Length; j++)
                for (int w = 0; w < people.Length; w++)
                {
                    double dx = slots[j].X - people[w].X, dz = slots[j].Z - people[w].Z;
                    double distance = Math.Sqrt(dx * dx + dz * dz);
                    commute[j, w] = people[w].Education >= slots[j].RequiredEducation && distance <= range ?
                        distance * commuteCostPerMetre : double.PositiveInfinity;
                }

            while (queue.Count > 0 && result.Bids < maxBids)
            {
                int j = queue.Dequeue(), best = -1;
                double bestSurplus = 0, bestWage = 0;
                var job = slots[j];
                for (int w = 0; w < people.Length; w++)
                {
                    if (!Finite(commute[j, w])) continue;
                    double targetUtility = utility[w] + netBidIncrement;
                    double wage = job.GrossFor(targetUtility + commute[j, w]);
                    double surplus = job.MarginalValue - wage;
                    // Vacancy wins zero-profit ties; lower worker ID wins equal profits.
                    if (!Finite(wage) || wage > job.WageBudget || surplus <= bestSurplus ||
                        !(targetUtility > utility[w])) continue;
                    best = w; bestWage = wage; bestSurplus = surplus;
                }
                if (best < 0) continue;
                int displaced = employedBy[best];
                if (displaced >= 0)
                {
                    matches[displaced] = null;
                    queue.Enqueue(displaced);
                }
                var match = new LaborAssignment { Job = job, Worker = people[best], GrossWage = bestWage,
                    TakeHomeIncome = job.TakeHome(bestWage), CommuteCost = commute[j, best] };
                matches[j] = match;
                employedBy[best] = j;
                utility[best] = match.WorkerUtility;
                result.Bids++;
            }
            result.Converged = queue.Count == 0;
            for (int j = 0; j < slots.Length; j++)
                if (matches[j] != null) result.Assignments.Add(matches[j]!);
                else result.UnfilledJobs.Add(slots[j].Id);
            for (int w = 0; w < people.Length; w++)
                if (employedBy[w] < 0) result.OutsideWorkers.Add(people[w].Id);
            return result;
        }

        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static bool Nonnegative(double n) => Finite(n) && n >= 0;
    }
}
