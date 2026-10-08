using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(MarriageBarterable), "GetUnitValueForFaction")]
    public class MarriageDowryPatch
    {
        [HarmonyPostfix]
        public static void GetUnitValueForFactionPostfix(MarriageBarterable __instance, IFaction faction, ref int __result)
        {
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return;
            if (faction == quote.Agreement.OtherHouse
                || faction == quote.Agreement.OtherHouse.Kingdom && faction != quote.Agreement.PlayerHouse.Kingdom)
                __result = -quote.Compensation;
        }
    }
}
