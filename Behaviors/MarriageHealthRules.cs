using System;

namespace BellumCivile.Behaviors
{
    // Pure scoring rules; callers supply a snapshot taken once per house/search.
    internal static class MarriageHealthRules
    {
        private static float Unit(float value) => Math.Max(0, Math.Min(1, value));

        internal static float MixedStatusCost(bool family, bool spouseFamily, float dynastyRisk) =>
            family && !spouseFamily ? BellumCivileConstants.MarriageScoreRetainerPenalty * (1 - Unit(dynastyRisk)) : 0;

        internal static float HouseholdMarriageValue(bool spouseFamily) => BellumCivileConstants.MarriageHouseholdMarriageValue
            + (spouseFamily ? 0 : BellumCivileConstants.MarriageHouseholdSameStatusValue);

        internal static float StaffShortage(int adults, int demand) => demand <= 0 ? 0 : Unit((demand - adults) / (float)demand);

        internal static int ServiceCapacity(int freeAdults, int endowed, int unendowed, int vacancies) =>
            Math.Max(0, freeAdults) + Math.Max(0, endowed) + Math.Min(Math.Max(0, unendowed), Math.Max(0, vacancies));

        internal static float HouseholdNeed(float shortage, int children, float coverage, bool canEndow) =>
            canEndow ? Unit(shortage) * (children > 0 ? .75f : 1) * (1 - .5f * Unit(coverage)) : 0;

        internal static float HouseholdChance(float need) => BellumCivileConstants.MarriageHouseholdAnnualChance
            + (BellumCivileConstants.MarriageHouseholdMaximumAnnualChance - BellumCivileConstants.MarriageHouseholdAnnualChance) * Unit(need);

        internal static float StaffDepartureCost(int before, int after, int demand) => after < before
            ? BellumCivileConstants.MarriageHouseholdStaffLoss * StaffShortage(after, demand) : 0;

        internal static float Risk(int adults, int children, int prospects, float reproductiveCoverage)
        {
            float risk = .5f + (adults <= 2 ? .25f : 0) + (children == 0 ? .25f : 0);
            risk -= Math.Min(3, Math.Max(0, prospects - 1)) * .1f;
            if (children > 0) risk = Math.Min(risk, children >= 2 ? .25f : .5f);
            return Math.Max(0, risk) * (1 - Math.Max(0, Math.Min(1, reproductiveCoverage)));
        }

        internal static float ReproductiveCoverage(float age) => age <= 35 ? 1
            : Math.Max(0, Math.Min(1, (45 - age) / 10));
        internal static float PreferenceScale(float risk) => 1 - .75f * risk;
        internal static float StrategyScale(float risk) => 1 - .25f * risk;
        internal static float ForeignDistanceCost(float risk, bool leaves) => 5 + (leaves ? 30 * risk : 0);
        internal static float Continuity(float risk, bool fertile, bool receives, bool blood) => !fertile ? 0
            : receives ? 20 + 100 * risk : blood ? 10 * (1 - risk) : 0;
        internal static float Prestige(int ownTier, int otherTier) => Math.Min(30, Math.Max(0, otherTier - ownTier) * 10);
        internal static float OutgoingKinshipValue(float risk, bool fertile, bool blood, bool leaves) =>
            fertile && blood && leaves ? 30 * (1 - Math.Max(0, Math.Min(1, risk))) : 0;
    }
}
