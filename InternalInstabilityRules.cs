using System;
using System.Collections.Generic;

namespace BellumCivile
{
    internal static class InternalInstabilityRules
    {
        internal static double ScaleChance(double probability, int percent)
        {
            if (double.IsNaN(probability) || double.IsInfinity(probability)) return 0;
            return Math.Min(1, Math.Max(0, Math.Min(1, probability)) * Math.Max(0, Math.Min(200, percent)) / 100d);
        }

        // Both court routes share a saved decision, including certainty and zero-chance results.
        internal static bool CourtWindowAllows(string key, double now, double nextTerm, int percent,
            Dictionary<string, double> until, Dictionary<string, bool> allowed, Func<double> roll)
        {
            if (until.TryGetValue(key, out double end) && end == nextTerm && now <= end && allowed.TryGetValue(key, out bool decision))
                return decision;
            if (double.IsNaN(nextTerm) || double.IsInfinity(nextTerm) || nextTerm <= now) return false;
            bool result = percent >= 100 || percent > 0 && roll() < percent / 100d;
            until[key] = nextTerm;
            allowed[key] = result;
            return result;
        }
    }
}
