using System;

namespace BellumCivile
{
    internal static class EducationAgeSchedule
    {
        public static int[] Normalize(int adulthood, params int[] ages)
        {
            int limit = Math.Max(16, Math.Min(21, adulthood));
            int[] result = new int[6];
            int previous = 0;
            for (int i = 0; i < result.Length; i++)
            {
                // Reserve one year for each remaining stage and the transition to adulthood.
                result[i] = Math.Max(previous + 1, Math.Min(limit - (6 - i), ages[i]));
                previous = result[i];
            }
            return result;
        }
    }
}
