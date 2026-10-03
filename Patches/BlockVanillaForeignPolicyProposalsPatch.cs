using System.Collections.Generic;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    /// <summary>
    /// When Bellum's war and peace revamp is enabled, replace Bannerlord's private random
    /// AI war/peace target rolls with Bellum's War Will and target-ranking proposals.
    /// </summary>
    [HarmonyPatch]
    internal static class BlockVanillaForeignPolicyProposalsPatch
    {
        private const string ProposalBehaviorType =
            "TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase randomWar = AccessTools.Method(ProposalBehaviorType + ":GetRandomWarDecision");
            MethodBase randomPeace = AccessTools.Method(ProposalBehaviorType + ":GetRandomPeaceDecision");
            if (randomWar != null)
                yield return randomWar;
            if (randomPeace != null)
                yield return randomPeace;
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod, Clan clan, ref KingdomDecision __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (behavior == null)
                return true;

            bool isWarRoll = __originalMethod?.Name == "GetRandomWarDecision";
            bool isPeaceRoll = __originalMethod?.Name == "GetRandomPeaceDecision";
            bool created = isWarRoll
                ? behavior.TryCreateWarDecision(clan, out __result, out _)
                : isPeaceRoll && behavior.TryCreatePeaceDecision(clan, out __result, out _);
            if (!created)
                __result = null;

            return false;
        }
    }
}
