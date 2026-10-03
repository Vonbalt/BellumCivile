using System;

namespace BellumCivile
{
    internal static class CourtRoyalPeaceRules
    {
        internal const string Kind = "royal_peace_decree";
        internal static bool Substantial(double available, double enemy)
        { return Finite(available) && Finite(enemy) && available >= 0 && enemy > 0 && enemy >= available; }
        internal static double Chance(double available, double enemy, double diverted, int mercy, int calculating, int valor)
        {
            if (!Substantial(available, enemy) || !Finite(diverted) || diverted <= 0) return 0;
            double ratio = enemy / Math.Max(1, available);
            double share = diverted / Math.Max(1, available + diverted);
            return Math.Max(5, Math.Min(95, 25 + Math.Min(30, Math.Max(0, ratio - 1) * 20)
                + 40 * share + 5 * mercy + 5 * calculating - 5 * valor));
        }
        internal static double ExecutionDay(double now, int deliberationDays) { return now + Math.Max(1, deliberationDays); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
