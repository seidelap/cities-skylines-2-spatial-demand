using System;
using SpatialDemand.Core;

internal static class LaborPayrollTests
{
    internal static void Run()
    {
        var incumbent = new LaborWorker { Id = 9, OutsideOption = 96 };
        var incumbentJob = new LaborJob { Id = 9, Employer = 9, MarginalValue = 300, WageBudget = 300,
            IncumbentWorker = 9, IncumbentGrossWage = 96 };
        var held = LaborMarket.Clear(new[] { incumbent }, new[] { incumbentJob }, 1000, 0);
        Check(held.Converged && held.Bids == 0 && held.Assignments[0].GrossWage == 96,
            "An incumbent agreement must not ratchet upward without a competing offer.");
        var sameFirm = new LaborJob { Id = 10, Employer = 9, MarginalValue = 200, WageBudget = 200 };
        var internalCompetition = LaborMarket.Clear(new[] { incumbent }, new[] { incumbentJob, sameFirm }, 1000, 0);
        Check(internalCompetition.Bids == 0 && internalCompetition.UnfilledJobs.Count == 1,
            "A firm's own vacant slot must not bid up its own existing worker.");
        sameFirm.Employer = 10;
        var retention = LaborMarket.Clear(new[] { incumbent }, new[] { incumbentJob, sameFirm }, 1000, 0);
        Check(retention.Converged && retention.Assignments[0].Job.Id == 9 && retention.Assignments[0].GrossWage > 96 &&
            retention.Assignments[0].RetentionAfterCompetition,
            "An outside employer's credible bid can produce a higher retention wage.");
        var worker = new LaborWorker { Id = 1, OutsideOption = 90 };
        var job = new LaborJob { Id = 1, Employer = 1, MarginalValue = 300, WageBudget = 260,
            TakeHomeRate = 0.8, TaxFreeAllowance = 20 };
        var proposal = new LaborAssignment { Job = job, Worker = worker, GrossWage = 141,
            TakeHomeIncome = job.TakeHome(141), CommuteCost = 5 };
        Check(LaborContractRules.TryAgree(proposal, 128, 100, 32, out int daily) && daily == 160,
            "A daily contract must be rounded to an exactly payable payroll amount.");
        int companyMoney = 1000, householdMoney = 200, taxable = 0;
        for (int i = 0; i < 32; i++)
        {
            int payment = LaborContractRules.PeriodWage(daily, 32, false, 1);
            companyMoney -= payment; householdMoney += payment;
            taxable += Math.Max(0, payment - 32 / 32);
        }
        Check(companyMoney == 840 && householdMoney == 360 && companyMoney + householdMoney == 1200,
            "A full day of contract payments must transfer exactly the promised wage once.");
        Check(taxable == 128, "Taxable income must use each actual payment and the per-slice allowance.");
        Check(LaborContractRules.NetDailyIncome(160, 32, 25) == 128,
            "Income forecasts must use the same negotiated gross wage and income-tax bracket.");
        Check(LaborContractRules.NetDailyIncome(160, 32, -10) == 173,
            "Negative residential tax rates are subsidies, not an invalid wage.");
        Check(LaborContractRules.PeriodWage(159, 32, false, 1) == 4 &&
            LaborContractRules.PeriodWage(159, 32, true, 0.5f) == 2,
            "Uncovered vanilla wages and commuter rounding must retain their existing integer semantics.");
        Check(!LaborContractRules.TryAgree(proposal, 160, 100, 32, out _), "Renewal cannot quietly cut or repeat an existing salary.");
        Check(!LaborContractRules.TryAgree(proposal, 141, 100, 32, out _),
            "Rounding an unchanged standing wage must not manufacture a raise.");
        Check(!LaborContractRules.TryAgree(proposal, 128, 31, 32, out _), "Quantization cannot overspend the employer's remaining raise budget.");
        job.WageBudget = 159;
        Check(!LaborContractRules.TryAgree(proposal, 128, 100, 32, out _), "Rounding up cannot exceed the slot's reserved cash.");
        job.WageBudget = 260; job.MarginalValue = 160;
        Check(!LaborContractRules.TryAgree(proposal, 128, 100, 32, out _), "A zero-surplus contract must lose to vacancy.");
        job.MarginalValue = 300; worker.OutsideOption = 1000;
        Check(!LaborContractRules.TryAgree(proposal, 128, 100, 32, out _), "The worker's actual outside alternative must still be respected.");
        Check(LaborContractRules.NetDailyIncome(10, 32, 30) == 10, "Income below the tax allowance is untaxed.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
