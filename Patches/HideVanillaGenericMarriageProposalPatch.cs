using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Hides vanilla's generic "propose an alliance between our families" branch.
    /// Bellum replaces it with a specific spouse-selection branch while leaving
    /// normal personal courtship dialogue intact.
    /// </summary>
    [HarmonyPatch]
    internal static class HideVanillaGenericMarriageProposalPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(RomanceCampaignBehavior),
                "conversation_discuss_marriage_alliance_on_condition");
        }

        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }
}
