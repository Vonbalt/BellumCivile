using System;

namespace BellumCivile
{
    internal static class CourtMandateRules
    {
        internal const string Kind = "mandate_reform";
        private static readonly int[] Terms = { 1, 5, 10, 0 };
        internal static int? Next(int years, int direction)
        {
            int index = Array.IndexOf(Terms, years);
            int next = index + direction;
            return index >= 0 && Math.Abs(direction) == 1 && next >= 0 && next < Terms.Length ? (int?)Terms[next] : null;
        }
        internal static double Bound(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        internal static float Support(int direction, int factionLean, int relation, int honor, bool alignedCrown) =>
            (float)Bound(45 + direction * factionLean + Bound(relation * .1, -10, 10)
                - 5 * Math.Max(0, honor) + (alignedCrown ? 15 : 0), 0, 100);
        internal static double Resistance(double requested) => Bound(100 - 2 * requested, 0, 100);
        internal static double Openness(double gap, int relation, int honor, int mercy, int generosity, int calculating) =>
            50 + Bound(relation, -100, 100) * .4 - gap * .25
            - honor * (honor > 0 ? 25 : 20) - mercy * (mercy > 0 ? 15 : 10)
            - generosity * (generosity > 0 ? 10 : 20) + calculating * (calculating > 0 ? 10 : 5);
        internal static int Price(double gap, int relation, int honor, int generosity) => (int)(
            (gap > 50 ? 150000 : gap > 10 ? 100000 : 50000)
            * Bound(1 - Bound(relation, -100, 100) * .003 + honor * .25 - generosity * .1, .25, 2));
    }
}
