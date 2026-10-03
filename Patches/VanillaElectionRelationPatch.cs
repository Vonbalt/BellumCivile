using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Scales down vanilla's player election relation gains/losses from spending
    /// influence, without touching Bellum's own post-vote political fallout.
    /// </summary>
    [HarmonyPatch(typeof(KingdomElection), "GetRelationChangeWithSponsor")]
    public static class VanillaElectionRelationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Hero opposerOrSupporter, Supporter.SupportWeights supportWeight, bool isOpposingSides, ref int __result)
        {
            if (__result == 0)
                return;

            float multiplier = C.VanillaElectionRelationMultiplier;
            if (multiplier < 0f)
                multiplier = 0f;

            __result = (int)Math.Round(__result * multiplier, MidpointRounding.AwayFromZero);
        }
    }
}
