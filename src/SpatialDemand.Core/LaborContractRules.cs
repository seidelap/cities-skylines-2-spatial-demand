using System;

namespace SpatialDemand.Core
{
    // Vanilla divides daily wages into integer payroll slices. A contract promises
    // an exactly payable daily amount, rather than losing a remainder every slice.
    public static class LaborContractRules
    {
        public static bool TryAgree(LaborAssignment proposal, int currentDailyWage,
            double availableRaiseCash, int paymentsPerDay, out int dailyWage)
        {
            dailyWage = 0;
            if (proposal == null || currentDailyWage < 0 || paymentsPerDay <= 0 ||
                !Finite(availableRaiseCash) || availableRaiseCash < 0 || !Finite(proposal.GrossWage) ||
                proposal.GrossWage <= currentDailyWage) return false;
            double rounded = Math.Ceiling(proposal.GrossWage / paymentsPerDay) * paymentsPerDay;
            if (!Finite(rounded) || rounded > int.MaxValue || rounded <= currentDailyWage ||
                rounded > proposal.Job.WageBudget || rounded >= proposal.Job.MarginalValue ||
                rounded - currentDailyWage > availableRaiseCash ||
                proposal.Job.TakeHome(rounded) - proposal.CommuteCost <= proposal.Worker.OutsideOption) return false;
            dailyWage = (int)rounded;
            return true;
        }

        // Same float multiplication and ties-to-even rounding as the game's income
        // helper. The actual tax debit remains TaxSystem's responsibility.
        public static int NetDailyIncome(int gross, int minimumEarnings, int taxRate)
        {
            int taxable = Math.Max(0, gross - minimumEarnings);
            return gross - (int)Math.Round((double)((float)taxable * ((float)taxRate / 100f)), MidpointRounding.ToEven);
        }

        public static int PeriodWage(int dailyWage, int paymentsPerDay, bool commuter, float commuterMultiplier)
        {
            if (paymentsPerDay <= 0 || dailyWage < 0 || !Finite(commuterMultiplier) || commuterMultiplier < 0)
                throw new ArgumentException("Invalid payroll quote.");
            int gross = commuter ? (int)Math.Round((double)((float)dailyWage * commuterMultiplier), MidpointRounding.ToEven) : dailyWage;
            return gross / paymentsPerDay;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
