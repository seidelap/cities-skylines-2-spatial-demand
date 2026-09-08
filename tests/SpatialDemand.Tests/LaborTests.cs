using System;
using System.Linq;
using SpatialDemand.Core;

internal static class LaborTests
{
    internal static void Run()
    {
        var worker = new LaborWorker { Id = 1, OutsideOption = 20 };
        var first = new LaborJob { Id = 1, Employer = 1, MarginalValue = 100, WageBudget = 100 };
        var second = new LaborJob { Id = 2, Employer = 2, MarginalValue = 80, WageBudget = 80 };
        var abundant = LaborMarket.Clear(new[] { worker, new LaborWorker { Id = 2, OutsideOption = 20 } },
            new[] { first }, 1000, 0);
        var scarce = LaborMarket.Clear(new[] { worker }, new[] { first, second }, 1000, 0);
        Check(abundant.Converged && scarce.Converged, "Labor auctions should finish.");
        Check(abundant.Assignments.Single().GrossWage == 21, "An available worker can accept near their outside option.");
        Check(scarce.Assignments.Single().GrossWage > abundant.Assignments.Single().GrossWage,
            "Competing employers must raise pay for scarce labor.");
        Check(scarce.Assignments.Single().Job.Id == first.Id && scarce.UnfilledJobs.Single() == second.Id,
            "The lower-value employer should stop bidding and leave its slot empty.");

        var unaffordable = LaborMarket.Clear(new[] { worker }, new[] {
            new LaborJob { Id = 1, MarginalValue = 1000, WageBudget = 19 },
            new LaborJob { Id = 2, MarginalValue = 20, WageBudget = 1000 } }, 1000, 0);
        Check(unaffordable.Assignments.Count == 0 && unaffordable.UnfilledJobs.Count == 2,
            "Hiring must beat vacancy and fit both productivity and cash limits.");
        var costlyCommute = LaborMarket.Clear(new[] { worker }, new[] {
            new LaborJob { Id = 1, X = 100, MarginalValue = 100, WageBudget = 100 } }, 1000, 1);
        Check(costlyCommute.Assignments.Count == 0, "A nominally good wage can lose to the worker's commute and outside option.");

        var taxed = new LaborJob { Id = 1, MarginalValue = 100, WageBudget = 100, TakeHomeRate = 0.5, TaxFreeAllowance = 10 };
        var takeHome = LaborMarket.Clear(new[] { worker }, new[] { taxed }, 1000, 0).Assignments.Single();
        Check(takeHome.GrossWage == 32 && takeHome.TakeHomeIncome == 21,
            "Worker decisions must use take-home pay with the tax-free allowance.");
        var far = new LaborWorker { Id = 1, X = 100, OutsideOption = 20 };
        var near = new LaborWorker { Id = 2, OutsideOption = 20 };
        Check(LaborMarket.Clear(new[] { far, near }, new[] { first }, 1000, 0.1).Assignments.Single().Worker.Id == near.Id,
            "Employer argmax should select the affordable worker with the lower required commute compensation.");
        Check(LaborMarket.Clear(new[] { far }, new[] { first }, 99, 0).Assignments.Count == 0,
            "An unreachable worker is not available labor.");
        Check(LaborMarket.Clear(new[] { worker }, new[] {
            new LaborJob { Id = 1, RequiredEducation = 1, MarginalValue = 100, WageBudget = 100 } }, 1000, 0).Assignments.Count == 0,
            "Workers must qualify for their jobs.");

        var people = Enumerable.Range(1, 5).Select(i => new LaborWorker { Id = i, OutsideOption = i * 5, X = i * 10 }).ToArray();
        var jobs = Enumerable.Range(1, 8).Select(i => new LaborJob { Id = i, Employer = i, MarginalValue = 70 + i,
            WageBudget = 55 + i, X = i * 20 }).ToArray();
        var forward = LaborMarket.Clear(people, jobs, 1000, 0.1);
        var backward = LaborMarket.Clear(people.Reverse(), jobs.Reverse(), 1000, 0.1);
        Check(forward.Converged && backward.Converged && Signature(forward) == Signature(backward),
            "Entity enumeration order must not alter matching or wages.");
        Check(forward.Assignments.Select(a => a.Worker.Id).Distinct().Count() == forward.Assignments.Count &&
            forward.Assignments.Select(a => a.Job.Id).Distinct().Count() == forward.Assignments.Count,
            "A person or employment slot cannot be matched twice.");
        Check(forward.Assignments.All(a => a.WorkerUtility > a.Worker.OutsideOption &&
            a.GrossWage <= a.Job.WageBudget && a.EmployerSurplus > 0), "Every accepted contract must improve both individual choices.");
        Check(forward.Assignments.Count + forward.UnfilledJobs.Count == jobs.Length &&
            forward.Assignments.Count + forward.OutsideWorkers.Count == people.Length, "All agents require explicit outcomes.");
        // A final empty slot cannot profitably outbid a worker's current alternative by one net increment.
        foreach (var job in jobs.Where(j => forward.UnfilledJobs.Contains(j.Id)))
            foreach (var person in people)
            {
                var incumbent = forward.Assignments.FirstOrDefault(a => a.Worker.Id == person.Id);
                double utility = incumbent == null ? person.OutsideOption : incumbent.WorkerUtility;
                double commute = Math.Abs(job.X - person.X) * 0.1;
                Check(utility + commute + 1 >= job.MarginalValue || utility + commute + 1 > job.WageBudget,
                    "A converged vacant job must have no profitable affordable improving bid.");
            }
        Check(!LaborMarket.Clear(new[] { worker }, new[] { first, second }, 1000, 0, 1, 1).Converged,
            "A stopped auction must expose its unsettled state.");
        Throws(() => LaborMarket.Clear(new[] { worker, worker }, new[] { first }, 1000, 0));
        Throws(() => LaborMarket.Clear(new[] { worker }, new[] { first, first }, 1000, 0));
        Throws(() => LaborMarket.Clear(new[] { worker }, new[] { first }, 1000, 0, 0));
        Throws(() => LaborMarket.Clear(new[] { worker }, new[] { new LaborJob { Id = 1, TakeHomeRate = 0 } }, 1000, 0));
    }

    private static string Signature(LaborResult result) => string.Join(";", result.Assignments.Select(a =>
        a.Job.Id + ":" + a.Worker.Id + ":" + a.GrossWage.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid labor inputs should be rejected.");
    }
}
