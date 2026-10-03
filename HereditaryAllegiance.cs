using System;

namespace BellumCivile
{
    internal static class HereditaryAllegiance
    {
        internal const float KinshipWeight = 15;
        internal const float MarriageWeight = 20;

        internal static float Chance(float intent, int heirRelation, int rulerRelation,
            bool heirKin, bool rulerKin, bool heirMarriage, bool rulerMarriage)
        {
            if (float.IsNaN(intent) || float.IsInfinity(intent)) return 0;
            float score = Math.Max(0, Math.Min(100, intent));
            score += Math.Max(-25, Math.Min(25, ((float)heirRelation - rulerRelation) * 0.25f));
            score += ((heirKin ? 1 : 0) - (rulerKin ? 1 : 0)) * KinshipWeight;
            score += ((heirMarriage ? 1 : 0) - (rulerMarriage ? 1 : 0)) * MarriageWeight;
            return Math.Max(0, Math.Min(100, score)) / 100;
        }
    }
}
