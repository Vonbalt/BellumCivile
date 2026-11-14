using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class DefensivePactWarRisk
    {
        internal static float PowerScore(float sourceStrength, float oppositionStrength)
        {
            float source = Math.Max(1f, sourceStrength);
            float target = Math.Max(1f, oppositionStrength);
            float ratio = source / target;
            if (ratio >= 1.25f)
                return Math.Min(C.WarPeaceRevampPowerOpportunityCap, (ratio - 1f) * C.WarPeaceRevampPowerScoreSlope);
            if (ratio <= .85f)
                return -Math.Min(C.WarPeaceRevampPowerDangerCap, (1f / Math.Max(.01f, ratio) - 1f) * C.WarPeaceRevampPowerScoreSlope);
            return 0f;
        }

        internal static float AdditionalTargetPenalty(float source, float target, float additionalDefenders)
        {
            if (float.IsNaN(source) || float.IsInfinity(source) || source <= 0f
                || float.IsNaN(target) || float.IsInfinity(target) || target <= 0f
                || float.IsNaN(additionalDefenders) || float.IsInfinity(additionalDefenders) || additionalDefenders <= 0f)
                return 0f;
            double combined = Math.Min(float.MaxValue, (double)target + additionalDefenders);
            return Math.Max(0f, PowerScore(source, target) - PowerScore(source, (float)combined));
        }

        internal static float AppliedToClampedScore(float scoreAfterPenalty, float targetPenalty)
        {
            if (float.IsNaN(scoreAfterPenalty) || float.IsInfinity(scoreAfterPenalty)
                || float.IsNaN(targetPenalty) || float.IsInfinity(targetPenalty) || targetPenalty <= 0f) return 0f;
            float Clamp(float value) => Math.Max(0f, Math.Min(100f, value));
            return Math.Max(0f, Clamp(scoreAfterPenalty + targetPenalty) - Clamp(scoreAfterPenalty));
        }

        // Only restore the part hidden by the score cap or by the ranking floor.
        internal static float CouncilPenalty(float targetPenalty, float appliedToTargetScore)
        {
            if (float.IsNaN(targetPenalty) || float.IsInfinity(targetPenalty)
                || float.IsNaN(appliedToTargetScore) || float.IsInfinity(appliedToTargetScore)) return 0f;
            return Math.Max(0f, targetPenalty - Math.Max(0f, appliedToTargetScore)) * .35f;
        }
    }
}
