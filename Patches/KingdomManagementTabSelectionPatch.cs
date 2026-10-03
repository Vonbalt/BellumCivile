using System.Collections.Generic;
using System.Reflection;
using BellumCivile.UI.VanillaTabs.Kingdoms.Factions;
using BellumCivile.ViewModelMixin;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class KingdomManagementTabSelectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            string[] methodNames =
            {
                "ExecuteShowClan",
                "ExecuteShowFiefs",
                "ExecuteShowPolicies",
                "ExecuteShowArmy",
                "ExecuteShowDiplomacy"
            };

            foreach (string methodName in methodNames)
            {
                MethodInfo method = AccessTools.DeclaredMethod(typeof(KingdomManagementVM), methodName);
                if (method != null)
                    yield return method;
            }
        }

        private static void Postfix(KingdomManagementVM __instance, MethodBase __originalMethod)
        {
            BellumFactionsKingdomTabState.Clear(__instance);

            if (__originalMethod?.Name == "ExecuteShowPolicies")
            {
                __instance?.Policy?.RefreshValues();
                KingdomPoliciesSuccessionVMMixin.SelectSuccessionLaws(__instance?.Policy);
            }
        }
    }
}
