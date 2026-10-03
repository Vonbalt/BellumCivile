using System;

namespace BellumCivile
{
    internal static class CourtAppeasementRules
    {
        internal const string Kind = "crown_appeasement";
        internal const float Bonus = 20;
        internal static int Cost(int houses) => 100 + 25 * Math.Max(0, houses);
        internal static float Effective(float underlying, float bonus) => Math.Max(-100, Math.Min(100, underlying + bonus));
        internal static float SetEffective(float underlying, float desired, float bonus)
        {
            if (bonus == 0) return desired;
            // Reassigning a capped visible value must not erase the hidden underlying mood.
            if (desired == Effective(underlying, bonus)) return underlying;
            return Math.Max(-100, Math.Min(100, desired - bonus));
        }
    }
}
