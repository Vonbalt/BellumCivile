using System;

namespace BellumCivile
{
    // Accumulates already-classified terms for one realm; no campaign data is changed.
    internal sealed class DefensivePactTreatyThreat
    {
        internal double Gain { get; private set; }
        internal double Loss { get; private set; }

        internal void GainFief(int warScoreCost, bool hasClaimOrDeJure)
        {
            Gain += FiefGain(warScoreCost, hasClaimOrDeJure);
        }

        internal void LoseFief(int warScoreCost)
        {
            Loss += QuarterCost(warScoreCost);
        }

        internal void GainForcedTerm(int warScoreCost)
        {
            Gain += QuarterCost(warScoreCost);
        }

        internal double CappedGain => Math.Min(DefensivePactThreatRules.MaximumTreatyGain, Gain);
        internal double NetChange => CappedGain - Loss;
        internal double Project(double current) => DefensivePactThreatRules.ApplyDelta(current, NetChange);

        internal static double FiefGain(int warScoreCost, bool hasClaimOrDeJure)
            => QuarterCost(warScoreCost) * (hasClaimOrDeJure ? 1d : 2d);

        private static double QuarterCost(int cost)
        {
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
            return cost / 4d;
        }
    }
}
