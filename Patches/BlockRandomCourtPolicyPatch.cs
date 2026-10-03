using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class BlockRandomCourtPolicyPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            "TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior:GetRandomPolicyDecision");

        private static bool Prefix(ref KingdomDecision __result)
        {
            if (CourtAgendaBehavior.Current == null) return true;
            __result = null;
            return false;
        }
    }
}
