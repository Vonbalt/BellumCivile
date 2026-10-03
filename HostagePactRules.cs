using System;

namespace BellumCivile
{
    internal enum HostageDisposition { Release, Retain, Execute }

    // Pure rules only. Custody and transaction integration must validate candidates separately.
    internal static class HostagePactRules
    {
        internal const int DefaultDurationDays = 100;
        internal const int MaximumDurationDays = 1000;
        internal static bool IsValidDuration(int days) => days >= 0 && days <= MaximumDurationDays;

        internal static int GetTier(int rightfulSuccessionPosition)
            => rightfulSuccessionPosition <= 0 ? 4 : Math.Min(4, rightfulSuccessionPosition);

        internal static int GetTreatyCost(int tier) => 36 - 6 * ValidateTier(tier);

        internal static double GetHouseReluctance(int tier, int mercy, int relationToHostage)
        {
            int[] bases = { 60, 45, 30, 20 };
            return bases[ValidateTier(tier) - 1]
                * Clamp(1 + .1 * Trait(mercy) + .2 * Relation(relationToHostage), .65, 1.5);
        }

        internal static double GetWarDeterrence(int tier, int honor, int mercy,
            bool realmSuppliedHostage, bool ownHouseSuppliedHostage)
        {
            ValidateTier(tier);
            if (ownHouseSuppliedHostage && !realmSuppliedHostage)
                throw new ArgumentException("House supplier must belong to the supplying realm.");
            double oath = 20 * (1 + .2 * Trait(honor));
            if (!realmSuppliedHostage) return oath;
            return oath + (ownHouseSuppliedHostage
                ? (85 - 15 * tier) * (1 + .15 * Trait(mercy) + .1 * Trait(honor))
                : 12 - 2 * tier);
        }

        internal static double GetCouncilDeterrence(int tier, int honor, int mercy,
            bool realmSuppliedHostage, bool ownHouseSuppliedHostage)
            => .6 * GetWarDeterrence(tier, honor, mercy, realmSuppliedHostage, ownHouseSuppliedHostage);

        // Call only after establishing which party voluntarily breached. Unknown/forced wars
        // must not be represented as a supplying ruler's betrayal.
        internal static double[] GetDispositionProbabilities(int tier, int honor, int mercy,
            int calculating, int relationToOtherRuler, bool supplierBreached, bool executionAllowed)
        {
            ValidateTier(tier);
            double hostility = -Relation(relationToOtherRuler);
            double release = Math.Max(1, 30 + 20 * Trait(honor) + 25 * Trait(mercy)
                + (supplierBreached ? 0 : 25) - 10 * hostility);
            double retain = Math.Max(1, 60 + 20 * Trait(calculating) + 5 * (4 - tier));
            double execute = executionAllowed
                ? Math.Max(1, 5 - 15 * Trait(honor) - 20 * Trait(mercy)
                    + (supplierBreached ? 10 : -5) + 10 * hostility) : 0;
            double total = release + retain + execute;
            return new[] { release / total, retain / total, execute / total };
        }

        internal static HostageDisposition ChooseDisposition(int tier, int honor, int mercy,
            int calculating, int relationToOtherRuler, bool supplierBreached, bool executionAllowed,
            double roll)
        {
            if (double.IsNaN(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll));
            double[] probabilities = GetDispositionProbabilities(tier, honor, mercy, calculating,
                relationToOtherRuler, supplierBreached, executionAllowed);
            if (roll < probabilities[0]) return HostageDisposition.Release;
            if (!executionAllowed || roll < probabilities[0] + probabilities[1])
                return HostageDisposition.Retain;
            return HostageDisposition.Execute;
        }

        private static int ValidateTier(int tier)
        {
            if (tier < 1 || tier > 4) throw new ArgumentOutOfRangeException(nameof(tier));
            return tier;
        }
        private static int Trait(int value) => Math.Max(-2, Math.Min(2, value));
        private static double Relation(int value) => Clamp(value / 100.0, -1, 1);
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    }
}
