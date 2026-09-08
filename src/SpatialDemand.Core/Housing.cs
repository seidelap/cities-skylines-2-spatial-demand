using System;

namespace SpatialDemand.Core
{
    // Currency and income must cover the same period. Travel is in pathfinding seconds;
    // space is the game's apartment-size unit, not a claim about square metres.
    public readonly struct HomeOffer
    {
        public readonly long Id;
        public readonly double Rent, Space, TravelSeconds;
        public readonly int Available;

        public HomeOffer(long id, double rent, double space, double travelSeconds, int available)
        {
            Id = id; Rent = rent; Space = space; TravelSeconds = travelSeconds; Available = available;
        }
    }

    public readonly struct Household
    {
        public readonly long Id;
        public readonly int People;
        public readonly double Income;
        public readonly Preferences Preferences;

        public Household(long id, int people, double income, Preferences preferences)
        {
            if (people < 1 || !Numbers.NonNegative(income)) throw new ArgumentOutOfRangeException(nameof(income));
            Id = id; People = people; Income = income; Preferences = preferences;
        }
    }

    public readonly struct Preferences
    {
        public readonly double MaxRentShare, SpacePerPerson, TravelCostPerHour;

        public Preferences(double maxRentShare, double spacePerPerson, double travelCostPerHour)
        {
            if (!Numbers.NonNegative(maxRentShare) || maxRentShare > 1 ||
                !Numbers.Positive(spacePerPerson) || !Numbers.NonNegative(travelCostPerHour))
                throw new ArgumentOutOfRangeException(nameof(maxRentShare));
            MaxRentShare = maxRentShare; SpacePerPerson = spacePerPerson; TravelCostPerHour = travelCostPerHour;
        }

        // Draw once from a saved seed. No per-decision noise, entity-index seed, or global RNG.
        public static Preferences FromSeed(uint seed) => new Preferences(
            0.30 + 0.20 * Unit(seed ^ 0xA341316Cu),
            1.0 + 3.0 * Unit(seed ^ 0xC8013EA4u),
            0.05 + 0.15 * Unit(seed ^ 0xAD90777Du));

        private static double Unit(uint value) => StableHash.Mix(value) / (double)uint.MaxValue;
    }

    public enum Rejection { NotEvaluated, None, InvalidOffer, Full, Unaffordable }

    public readonly struct Evaluation
    {
        public readonly Rejection Rejection;
        public readonly double SpaceBenefit, RentCost, TravelCost;
        public bool Feasible => Rejection == Rejection.None;
        public double Utility => Feasible ? SpaceBenefit - RentCost - TravelCost : double.NegativeInfinity;

        internal Evaluation(Rejection rejection, double space = 0, double rent = 0, double travel = 0)
        { Rejection = rejection; SpaceBenefit = space; RentCost = rent; TravelCost = travel; }
    }

    public static class Housing
    {
        // The only housing valuation. The three terms are exposed for explanations and tests.
        public static Evaluation Evaluate(in Household household, in HomeOffer home)
        {
            if (!Numbers.NonNegative(home.Rent) || !Numbers.Positive(home.Space) ||
                !Numbers.NonNegative(home.TravelSeconds) || home.Available < 0)
                return new Evaluation(Rejection.InvalidOffer);
            if (home.Available == 0) return new Evaluation(Rejection.Full);
            if (home.Rent > household.Income * household.Preferences.MaxRentShare)
                return new Evaluation(Rejection.Unaffordable);

            double enoughSpace = household.People * household.Preferences.SpacePerPerson;
            double space = Math.Min(1, home.Space / enoughSpace);
            double rent = home.Rent == 0 ? 0 : home.Rent / household.Income;
            double travel = home.TravelSeconds / 3600 * household.Preferences.TravelCostPerHour;
            return new Evaluation(Rejection.None, space, rent, travel);
        }
    }

    internal static class Numbers
    {
        public static bool NonNegative(double value) => value >= 0 && !double.IsInfinity(value);
        public static bool Positive(double value) => value > 0 && !double.IsInfinity(value);
    }

    public static class StableHash
    {
        public static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7FEB352Du;
                value ^= value >> 15; value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }
    }
}
