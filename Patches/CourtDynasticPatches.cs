using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(MarriageAction), nameof(MarriageAction.Apply))]
    internal static class CourtDynasticMarriageReceiptPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Hero firstHero, Hero secondHero) => CourtAgendaBehavior.Current?.OnCourtMarriageCompleted(firstHero, secondHero);
    }

    [HarmonyPatch(typeof(DefaultAllianceModel), nameof(DefaultAllianceModel.GetSupportScoreOfStartingAllianceForClan))]
    internal static class CourtDynasticAllianceSupportPatch
    {
        private static void Postfix(Kingdom querierKingdom, Kingdom queriedKingdom, Clan evaluatingClan,
            ref TextObject explanationText, bool includeDescriptions, ref float __result)
        {
            float bonus = CourtAgendaBehavior.Current?.DynasticAllianceBonus(querierKingdom, queriedKingdom, evaluatingClan) ?? 0;
            if (bonus == 0) return;
            __result += bonus;
            if (includeDescriptions) explanationText = new TextObject("{=BC_CourtDynasticAllianceReason}{REASON}\nRoyal marriage initiative: +{BONUS} alliance support.")
                .SetTextVariable("REASON", explanationText ?? TextObject.GetEmpty()).SetTextVariable("BONUS", bonus);
        }
    }

    [HarmonyPatch(typeof(DefaultAllianceModel), "IsThereMarriageBetweenClans")]
    internal static class CourtDynasticMarriageRecognitionPatch
    {
        private static void Postfix(Clan clan1, Clan clan2, ref bool __result)
        {
            if (__result || clan1 == null || clan2 == null || clan1 == clan2) return;
            if (clan1.Kingdom?.RulingClan != clan1 || clan2.Kingdom?.RulingClan != clan2) return;
            if (CourtAgendaBehavior.Current?.HasDynasticAllianceContext(clan1.Kingdom, clan2.Kingdom) == true)
                __result = MarriageAllianceHelper.HasMarriageAlliance(clan1, clan2);
        }
    }
}
