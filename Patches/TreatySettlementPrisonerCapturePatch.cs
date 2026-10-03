using System.Reflection;
using HarmonyLib;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Treaty transfers are negotiated handovers, not conquests. Bannerlord normally captures
    /// partyless enemy nobles when settlement ownership changes during a war; Bellum applies the
    /// transfer immediately before peace, so that handler must not run inside the transfer scope.
    /// </summary>
    [HarmonyPatch]
    internal static class TreatySettlementPrisonerCapturePatch
    {
        private static MethodBase TargetMethod()
        {
            System.Type behaviorType = AccessTools.TypeByName(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.PrisonerCaptureCampaignBehavior");
            return behaviorType == null
                ? null
                : AccessTools.Method(behaviorType, "OnSettlementOwnerChanged");
        }

        private static bool Prefix()
        {
            return !BellumTreatyTransferContext.IsTreatyTransfer;
        }
    }
}
