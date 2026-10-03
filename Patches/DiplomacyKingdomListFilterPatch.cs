using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    public class DiplomacyKingdomListFilterPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter("Diplomacy.Extensions.KingdomExtensions:AllActiveKingdoms");
        }

        public static void Postfix(ref MBReadOnlyList<Kingdom> __result)
        {
            if (__result == null)
                return;

            __result = new MBReadOnlyList<Kingdom>(
                __result.Where(k => !BellumKingdomVisibilityHelper.ShouldHideFromKingdomLists(k)).ToList());
        }
    }
}
