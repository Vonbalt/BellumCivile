using System;

namespace BellumCivile
{
    internal static class RoyalHeirTribunalRules
    {
        internal static float ExecutionChance(float ordinaryChance, bool defeatedRoyalHeir) =>
            Math.Max(0f, Math.Min(1f, ordinaryChance)) * (defeatedRoyalHeir ? 0.25f : 1f);
    }
}
