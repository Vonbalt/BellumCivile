using System;

namespace BellumCivile
{
    internal static class UltimatumAcceptanceRules
    {
        // Shared with ordinary factions; the caller supplies projected or actual pledged power.
        internal static double Calculate(double faction, double loyalist, bool externalWar, int calculating, int valor, int mercy)
        {
            if (!SuccessionChallengeRules.Finite(faction) || !SuccessionChallengeRules.Finite(loyalist)
                || faction <= 0 || loyalist < 0 || faction < loyalist) return 0;
            double chance = loyalist == 0 ? 0.5 : Math.Min(0.5, 0.1 + Math.Floor((faction / loyalist - 1) / 0.5) * 0.1);
            if (externalWar) chance += 0.1;
            if (calculating == -2) chance -= 0.1;
            else if (calculating == -1) chance -= 0.05;
            else if (calculating == 1) chance += 0.05;
            else if (calculating >= 2) chance += 0.1;
            if (valor == 1) chance -= 0.05;
            else if (valor >= 2) chance -= 0.1;
            if (mercy == 1) chance += 0.05;
            else if (mercy >= 2) chance += 0.1;
            return Math.Max(0, Math.Min(1, chance));
        }
    }
}
