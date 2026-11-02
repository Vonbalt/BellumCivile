using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DefaultClanFinanceModel), nameof(DefaultClanFinanceModel.CalculateVillageIncome))]
    internal static class LordshipVillageIncomePatch
    {
        private static void Postfix(Clan clan, Village village, int __result) => LordshipIncome.Observe(clan, village, __result);
    }

    [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty")]
    internal static class LordshipSurplusPatch
    {
        private static bool Prefix(MobileParty party, Clan clan, ref int __result)
        {
            if (LordshipBehavior.Instance?.Ready != true || party?.IsLordParty != true || party.LeaderHero == null || party.LeaderHero == clan?.Leader
                || LordshipBehavior.Instance?.GetGrant(party.LeaderHero) == null) return true;
            __result = 0;
            return false;
        }
    }
}
