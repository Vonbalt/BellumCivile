using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Hides vanilla's generic marriage proposal only when Bellum supplies its
    /// own spouse-selection branch. Personal courtship is unaffected.
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
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic)
                return true;

            __result = false;
            return false;
        }
    }
}
