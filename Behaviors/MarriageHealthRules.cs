using System;

namespace BellumCivile.Behaviors
{
    // Pure scoring rules; callers supply a snapshot taken once per house/search.
    internal static class MarriageHealthRules
    {
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
