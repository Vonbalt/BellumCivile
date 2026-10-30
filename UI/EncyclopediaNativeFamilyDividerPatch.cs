using System.Reflection;
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;

namespace BellumCivile.UI
{
    [HarmonyPatch]
    internal static class EncyclopediaNativeFamilyDividerPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets").GetType(
                "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Encyclopedia.EncyclopediaDividerButtonWidget", true), "OnLateUpdate");

        private static bool Prefix(Widget __instance)
        {
            if (__instance.Id != "FamilyDivider" || !EncyclopediaFamilyLayoutCompatibility.Enabled) return true;
            // Native late update would undo the prefab's hidden flag whenever its grid has children.
            __instance.IsHidden = true;
            return false;
        }
    }
}
