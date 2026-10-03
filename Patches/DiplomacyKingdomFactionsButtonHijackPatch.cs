using System.Collections.Generic;
using System.Reflection;
using BellumCivile.UI.VanillaTabs.Kingdoms.Factions;
using HarmonyLib;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class DiplomacyKingdomFactionsButtonHijackPatch
    {
        public static IEnumerable<MethodBase> GetTargetMethods()
        {
            MethodBase standard = AccessTools.Method("Diplomacy.ViewModelMixin.KingdomManagementVMMixin:ExecuteShowFactions");
            if (standard != null)
                yield return standard;

            MethodBase naval = AccessTools.Method("DiplomacyNavalDLCPatch.ViewModelMixin.NavalKingdomManagementVMMixin:ExecuteShowFactions");
            if (naval != null)
                yield return naval;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return GetTargetMethods();
        }

        public static bool Prefix(object __instance)
        {
            object kingdomManagementVm = AccessTools.Property(__instance.GetType(), "ViewModel")?.GetValue(__instance);
            kingdomManagementVm = kingdomManagementVm ?? AccessTools.Field(__instance.GetType(), "ViewModel")?.GetValue(__instance);
            kingdomManagementVm = kingdomManagementVm ?? AccessTools.Field(__instance.GetType(), "_viewModel")?.GetValue(__instance);

            if (BellumFactionsKingdomTabState.Select(kingdomManagementVm))
                return false;

            return true;
        }
    }
}
