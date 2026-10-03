using System;

namespace BellumCivile
{
    internal static class CourtLiberationRules
    {
        internal const string Kind = "crown_prepare_liberation";
        internal const float DesireBonus = 20f;
        internal static double Weight(double readiness, double crownDesire, int valor, int calculating)
        {
            if (double.IsNaN(readiness) || double.IsInfinity(readiness) || readiness < 100
                || double.IsNaN(crownDesire) || double.IsInfinity(crownDesire)) return 0;
            return Math.Max(0.15, Math.Min(1.25, 0.35 + Math.Min(0.45, (readiness - 100) * 0.0045)
                + Math.Max(0, Math.Min(0.20, (crownDesire - 40) / 300)) + 0.05 * valor + 0.05 * calculating));
        }
        internal static bool Active(bool activated, bool sameIdentity, double now, double start, double deadline)
        { return activated && sameIdentity && now >= start && now < deadline; }
    }
}
