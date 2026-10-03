using System;
namespace BellumCivile
{
    internal static class CrownFavoritismMath
    {
        private static double Clamp(double x, double min, double max) => Math.Max(min, Math.Min(max, x));
        internal static double Score(float personality, int index, int[] houses, double[] moods, int eligible)
        {
            if (eligible <= 0 || houses[index] <= 0) return double.NegativeInfinity;
            double share = (double)houses[index] / eligible;
            double score = Clamp(personality / 4.0, -10, 10) + 5 * share + 10 * share * Clamp(-moods[index] / 60, 0, 1);
            for (int j = 0; j < houses.Length; j++)
                if (j != index) score -= 5.0 * houses[j] / eligible * Clamp((-moods[j] - 40) / 20, 0, 1);
            return score;
        }
    }
}
