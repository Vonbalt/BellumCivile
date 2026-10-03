using System;

namespace BellumCivile
{
    internal static class SuccessionChallengeSchedule
    {
        internal const int IntervalDays = 7;
        internal static bool IsDue(string realmId, int day, int lastDay)
        {
            if (string.IsNullOrEmpty(realmId) || day < 0 || lastDay >= day) return false;
            // Stable across processes/save loads; string.GetHashCode is not a save contract.
            int slot = 0;
            foreach (char c in realmId) slot = (slot * 31 + c) % IntervalDays;
            return day % IntervalDays == slot && (long)day - lastDay >= IntervalDays;
        }
    }
}
