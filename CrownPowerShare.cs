using System;

namespace BellumCivile
{
    internal sealed class CrownPowerShare
    {
        internal double Total { get; }
        internal double Loyalists { get; }
        internal double Rebels { get; }
        internal double Uncommitted => Math.Max(0, Total - Loyalists - Rebels);
        internal double LoyalistPercent => Total > 0 ? 100 * Loyalists / Total : 0;

        internal CrownPowerShare(double power, double rebelChance, double loyalty, bool ruler = false, bool undecidedPlayer = false)
        {
            Total = Finite(power) ? Math.Max(0, power) : 0;
            double chance = ruler || undecidedPlayer ? 0 : Clamp(rebelChance);
            Rebels = Total * chance;
            Loyalists = undecidedPlayer && !ruler ? 0 : Total * (1 - chance) * (ruler ? 1 : Clamp(loyalty));
        }

        private CrownPowerShare(CrownPowerShare first, CrownPowerShare second)
        {
            Total = first.Total + second.Total;
            Loyalists = first.Loyalists + second.Loyalists;
            Rebels = first.Rebels + second.Rebels;
        }
        internal CrownPowerShare Add(CrownPowerShare other) => new CrownPowerShare(this, other);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Clamp(double value) => Finite(value) ? Math.Max(0, Math.Min(1, value)) : 0;
    }
}
