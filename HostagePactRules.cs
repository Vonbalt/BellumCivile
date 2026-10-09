using System;

namespace BellumCivile
{
    internal enum HostageDisposition { Release, Retain, Execute }

    // Pure rules only. Custody and transaction integration must validate candidates separately.
    internal static class HostagePactRules
    {
        internal const int LegacyDurationDays = 100;
        internal const int DefaultDurationDays = 50;
        internal const int MinimumNegotiatedDurationDays = 30;
        internal const int DurationStepDays = 10;
        internal const int MaximumDurationDays = 1000;
        internal static bool IsValidDuration(int days) => days >= 0 && days <= MaximumDurationDays;
        internal static bool IsNegotiableDuration(int days) => days >= MinimumNegotiatedDurationDays
            && days <= MaximumDurationDays && days % DurationStepDays == 0;

        internal static int GetNegotiatedCost(int tier, int days)
        {
            if (!IsNegotiableDuration(days)) throw new ArgumentOutOfRangeException(nameof(days));
            return GetTreatyCost(tier) + (days - DefaultDurationDays) / DurationStepDays;
        }

        internal static bool IsValidPrice(int tier, int days, int cost, bool durationPriced)
            => tier >= 1 && tier <= 4 && (durationPriced
                ? IsNegotiableDuration(days) && cost == GetNegotiatedCost(tier, days)
                : IsValidDuration(days) && cost == GetTreatyCost(tier));

        internal static float GetDurationPenalty(int days)
            => .2f * Math.Min(50, Math.Max(0, days - 50))
                + .4f * Math.Min(100, Math.Max(0, days - 100))
                + .6f * Math.Max(0, days - 200);

        // Exhaustion already encourages peace. This small extra benefit stops growing at 100 days.
        internal static float GetRecoveryUtility(int days, float enthusiasm)
            => 15f * (Math.Min(50, Math.Max(0, days - DefaultDurationDays)) / 50f)
                * (float)Clamp((10 - enthusiasm) / 10.0, 0, 1);

        internal static int GetPreferredAiDuration(float enthusiasm, float warDays)
        {
            if (enthusiasm <= 0 && warDays >= 400) return 200;
            if (enthusiasm <= 0 && warDays >= 200) return 150;
            return enthusiasm < 10 ? 100 : DefaultDurationDays;
        }

        internal static int[] GetAiDurations(float enthusiasm, float warDays)
        {
            int preferred = GetPreferredAiDuration(enthusiasm, warDays);
            if (preferred == 200) return new[] { 200, 150, 100, 50, 30 };
            if (preferred == 150) return new[] { 150, 100, 50, 30 };
            if (preferred == 100) return new[] { 100, 50, 30 };
            return new[] { 50, 30 };
        }

        internal static float GetAiDurationAdjustment(int days, float enthusiasm, float warDays)
            => -Math.Abs(days - GetPreferredAiDuration(enthusiasm, warDays)) / 10f
                - GetDurationPenalty(days) * .1f;

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
