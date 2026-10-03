using System;

namespace BellumCivile
{
    internal static class CourtCouncilWeights
    {
        internal static CourtObjectiveWeight Calculate(bool vacant, double vacancyDays, double controversy,
            double meritGap, bool preferredOffice)
        {
            return new CourtObjectiveWeight(vacant ? 1 : .75,
                urgency: vacant ? Math.Min(1.5, Math.Max(0, vacancyDays - 7) / 30)
                    : Math.Min(.75, Math.Max(0, controversy) / 100 * .75),
                benefit: vacant ? 0 : Math.Min(.5, Math.Max(0, meritGap) / 40),
                preference: preferredOffice ? .25 : 0);
        }
    }
}
