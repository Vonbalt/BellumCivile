using System;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DefaultTradeAgreementModel), nameof(DefaultTradeAgreementModel.GetScoreOfStartingTradeAgreement))]
    internal static class CourtTradeSupportPatch
    {
        private static void Postfix(Kingdom querierKingdom, Kingdom queriedKingdom, Clan clan,
            ref TextObject detailedBreakdownTooltip, bool includeExplanation, ref float __result)
        {
            if ((CourtAgendaBehavior.Current?.TradeBonus(querierKingdom, queriedKingdom, clan) ?? 0) == 0) return;
            float before = __result;
            __result = CourtTradeRules.Support(before, true);
            if (includeExplanation) detailedBreakdownTooltip = new TextObject("{=BC_CourtTradeSupportReason}{REASON}\nCourt trade initiative: +{BONUS} support.")
                .SetTextVariable("REASON", detailedBreakdownTooltip ?? TextObject.GetEmpty()).SetTextVariable("BONUS", __result - before);
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomTradeAgreementDecision")]
    internal static class CourtTradeProposalPatch
    {
        private static readonly System.Reflection.MethodInfo Consider = AccessTools.Method(typeof(KingdomDecisionProposalBehavior), "ConsiderTradeAgreement");
        private static bool Prefix(KingdomDecisionProposalBehavior __instance, Clan clan, ref KingdomDecision __result)
        {
            var target = CourtAgendaBehavior.Current?.PreferredTradeTarget(clan);
            if (target == null || Consider == null) return true;
            try
            {
                var realm = clan.Kingdom;
                int cost = Campaign.Current.Models.TradeAgreementModel.GetInfluenceCostOfProposingTradeAgreement(clan);
                if (realm.UnresolvedDecisions.Any(d => d is TradeAgreementDecision) || clan.Influence < cost
                    || !NpcInfluenceBudgetService.CanAfford(clan, cost, NpcInfluenceExpenseKind.Discretionary)
                    || !CourtTradeObjectiveSource.Legal(realm, target, false)
                    || !(bool)Consider.Invoke(__instance, new object[] { clan, realm, target })) return true;
                __result = new TradeAgreementDecision(clan, target);
                return false;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Court trade preference unavailable; using ordinary proposal selection: " + ex);
                return true;
            }
        }
    }
}
