using System.Reflection;
using BellumCivile.ViewModelMixin;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class KingdomPoliciesSuccessionSelectionPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(KingdomPoliciesVM), "OnPolicySelect");
        }

        private static void Postfix(
            KingdomPoliciesVM __instance,
            [HarmonyArgument(0)] KingdomPolicyItemVM policy)
        {
            KingdomPoliciesSuccessionVMMixin.NotifyVanillaPolicySelected(__instance, policy);
        }
    }

    [HarmonyPatch(typeof(KingdomManagementVM), "SetSelectedCategory")]
    internal static class KingdomPoliciesSuccessionDefaultSelectionPatch
    {
        private static void Postfix(
            KingdomManagementVM __instance,
            [HarmonyArgument(0)] int index)
        {
            if (index == 2)
                KingdomPoliciesSuccessionVMMixin.SelectSuccessionLaws(__instance?.Policy);
        }
    }
}
