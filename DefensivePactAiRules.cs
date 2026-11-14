using System;

namespace BellumCivile
{
    internal static class DefensivePactAiRules
    {
        internal const double BaseMonthlyChance = .02d;
        internal const double UnderstrengthBonus = .02d;
        internal const double RelativeWeaknessBonus = .01d;

        // Called only after eligibility has been checked; inputs are strength relative to the target.
        internal static double JoinChance(double realmRatio, double pactRatio)
        {
            if (!DefensivePactThreatRules.Finite(realmRatio) || realmRatio <= 0 || realmRatio >= .8d
                || !DefensivePactThreatRules.Finite(pactRatio) || pactRatio < 0) return 0d;
            double understrength = Math.Max(0d, Math.Min(1d, (.7d - pactRatio) / .7d));
            double weakness = Math.Max(0d, Math.Min(1d, (.8d - realmRatio) / .8d));
            return BaseMonthlyChance + UnderstrengthBonus * understrength + RelativeWeaknessBonus * weakness;
        }
    }
}
