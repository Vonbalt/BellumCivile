using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class ElectionLobbyingRules
    {
        internal static double Bound(double n, double lo, double hi) => Math.Max(lo, Math.Min(hi, n));
        internal static double Resistance(double preferred, double requested, bool self) =>
            Math.Max(self ? 50 : 0, Bound(preferred - requested, 0, 100));
        internal static double Openness(double relation, double gap, int honor, int mercy, int generosity, int calculating) =>
            C.FiefBribeOpennessBase + Bound(relation, -100, 100) * C.FiefBribePlayerRelationScale - gap * .25
            + (honor > 0 ? -honor * C.FiefBribeHonorPenalty : -honor * C.FiefBribeDishonorBonus)
            + (mercy > 0 ? -mercy * C.FiefBribeMercyPenalty : -mercy * C.FiefBribeCrueltyBonus)
            + (generosity > 0 ? -generosity * C.FiefBribeGenerosityPenalty : -generosity * C.FiefBribeGreedBonus)
            + (calculating > 0 ? calculating * C.FiefBribeCalculatingBonus : calculating * C.FiefBribeHotheadPenalty);
        internal static int Price(double share, double gap, double relation, int honor, int generosity) => (int)Math.Round(
            50000 * (1 + Math.Min(Bound(share, 0, 100) / 10, 2)) * (1 + Bound(gap, 0, 100) / 50)
            * Bound(1 - Bound(relation, -100, 100) * .003 + honor * .25 - generosity * .1, .25, 2));
        // -3 is ExtremelyEasy, 0 Normal, +3 ExtremelyHard. Trust uses relation once, without a fit bonus.
        internal static int Difficulty(double gap, double relation, int fit, bool self, bool trust)
        {
            int steps = gap > 60 ? 3 : gap > 30 ? 2 : gap > 10 ? 1 : 0;
            steps -= relation >= 90 ? 2 : relation >= 60 ? 1 : 0;
            if (!trust) steps -= (int)Bound(fit, -1, 1);
            return (int)Bound(Math.Max(self ? 1 : -3, steps), -3, 3);
        }
    }
}
