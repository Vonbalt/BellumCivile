using System;

namespace BellumCivile
{
    internal static class ClientLiberationRules
    {
        internal static float EffectivePowerMultiplier(float desire)
        { return desire >= 60f ? 1f : desire >= 40f ? 0.75f : 0.5f; }

        internal static float BlocContribution(float power, bool ally, bool client, float allyShare, float clientShare)
        { return power * (client ? clientShare : ally ? allyShare : 0f); }

        internal static float Readiness(float clientPower, float blocPower, float requiredRatio)
        {
            return Math.Max(0f, Math.Min(200f, blocPower <= 0f
                ? 200f : clientPower / blocPower / Math.Max(0.01f, requiredRatio) * 100f));
        }
    }
}
