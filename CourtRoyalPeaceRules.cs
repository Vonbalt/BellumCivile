using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class CourtRoyalPeaceRules
    {
        internal const string Kind = "royal_peace_decree";
        internal const int ReviewDays = 30;
        internal static int Cost(int feuds, int wars, bool wartime)
        {
            feuds = Math.Max(0, feuds);
            wars = Math.Max(0, wars);
            if (feuds + wars == 0) return 0;
            // The base includes one conflict, preferring an armed one in a mixed realm.
            int cost = C.RoyalPeaceFeudBaseInfluenceCost + C.RoyalPeaceAdditionalFeudCost * feuds
                + C.RoyalPeaceAdditionalWarCost * wars - (wars > 0 ? C.RoyalPeaceAdditionalWarCost : C.RoyalPeaceAdditionalFeudCost);
            return wartime ? cost : cost * C.RoyalPeacePeacetimeCostMultiplier;
        }
        internal static int RelationPenalty(bool involved, bool wartime)
        { return (involved ? C.RoyalPeaceInvolvedRelationPenalty : C.RoyalPeaceRealmRelationPenalty)
                * (wartime ? 1 : C.RoyalPeacePeacetimeResentmentMultiplier); }
        internal static bool QuoteStillValid(bool quotedWartime, int quotedCost, bool wartime, int cost)
        { return quotedWartime == wartime && cost <= quotedCost; }
        internal static double RealmChance(bool wartime, int feuds, int wars, double oldestWarDays,
            double available, double enemy, double diverted, int mercy, int calculating, int valor)
        {
            if (feuds + wars <= 0 || !Finite(available) || !Finite(enemy) || !Finite(diverted)) return 0;
            double share = Math.Max(0, diverted) / Math.Max(1, Math.Max(0, available) + Math.Max(0, diverted));
            double traits = 5 * mercy + 5 * calculating - 5 * valor;
            if (!wartime)
            {
                if (wars < 2 && !(wars == 1 && oldestWarDays >= 14 && share >= .25) && !(wars == 0 && feuds >= 3)) return 0;
                return Math.Max(0, Math.Min(60, 5 + 8 * wars + 2 * feuds + 20 * share + traits));
            }
            if (enemy <= 0) return 0;
            double pressure = enemy / Math.Max(1, available);
            return Math.Max(5, Math.Min(95, 15 + Math.Min(30, 15 * pressure)
                + 35 * share + Math.Min(10, 2 * feuds + 3 * wars) + traits));
        }
        internal static double ExecutionDay(double now, int deliberationDays) { return now + Math.Max(1, deliberationDays); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
