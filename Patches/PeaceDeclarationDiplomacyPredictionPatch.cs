using System;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class PeaceDeclarationSupportLabelPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(KingdomDiplomacyVM).GetMethod(
                "CalculatePeaceSupport",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        private static bool Prefix(
            IFaction faction,
            int dailyTributeToBePaid,
            int durationInDays,
            ref TextObject __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || Clan.PlayerClan == null || !(faction is Kingdom target))
                return true;

            MakePeaceKingdomDecision decision = new MakePeaceKingdomDecision(
                Clan.PlayerClan,
                target,
                dailyTributeToBePaid,
                durationInDays);
            ForeignPolicyCouncilEvaluation evaluation = WarDeclarationCouncilService.EvaluatePeacePreliminary(decision);
            __result = WarDeclarationCouncilService.GetSupportLabel(evaluation.Tally);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class PeaceDeclarationDiplomacyHintPatch
    {
        private static readonly FieldInfo ExplanationTextField = AccessTools.Field(
            typeof(KingdomDiplomacyProposalActionItemVM),
            "_explanationText");

        private static MethodBase TargetMethod()
        {
            return typeof(KingdomDiplomacyVM).GetMethod(
                "OnSetWarItem",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPostfix]
        private static void Postfix(KingdomDiplomacyVM __instance, KingdomWarItemVM item)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || __instance == null
                || !(item?.Faction2 is Kingdom target) || target.Leader?.Clan == null)
                return;

            int durationInDays;
            int dailyTribute = Campaign.Current.Models.DiplomacyModel.GetDailyTributeToPay(
                Clan.PlayerClan,
                target.Leader.Clan,
                out durationInDays);
            dailyTribute = 10 * (dailyTribute / 10);

            MakePeaceKingdomDecision decision = new MakePeaceKingdomDecision(
                Clan.PlayerClan,
                target,
                dailyTribute,
                durationInDays);
            ForeignPolicyCouncilEvaluation evaluation = WarDeclarationCouncilService.EvaluatePeacePreliminary(decision);
            TextObject support = WarDeclarationCouncilService.GetSupportLabel(evaluation.Tally);
            string oldExplanation = BuildVanillaExplanation(dailyTribute, durationInDays, support).ToString();
            KingdomDiplomacyProposalActionItemVM action = __instance.Actions
                .FirstOrDefault(candidate => string.Equals(candidate?.Explanation, oldExplanation, StringComparison.Ordinal));
            if (action == null)
                return;

            TextObject explanation = new TextObject("{=BC_PeaceCouncil_ProposalExplanation}Consider making peace ({SUPPORT})");
            explanation.SetTextVariable("SUPPORT", support);
            ExplanationTextField?.SetValue(action, explanation);
            action.Explanation = explanation.ToString();

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (StorylineWarProtectionHelper.TryGetPeaceBlock(playerKingdom, target, out TextObject storylineReason))
            {
                action.IsEnabled = false;
                action.Hint = new HintViewModel(storylineReason);
                return;
            }

        }

        private static TextObject BuildVanillaExplanation(int dailyTribute, int durationInDays, TextObject support)
        {
            TextObject explanation = dailyTribute == 0
                ? GameTexts.FindText("str_propose_peace_explanation")
                : dailyTribute > 0
                    ? GameTexts.FindText("str_propose_peace_explanation_pay_tribute")
                    : GameTexts.FindText("str_propose_peace_explanation_get_tribute");
            explanation.SetTextVariable("SUPPORT", support);
            explanation.SetTextVariable("TRIBUTE_AMOUNT", Math.Abs(dailyTribute));
            explanation.SetTextVariable("TRIBUTE_DURATION", durationInDays);
            return explanation;
        }
    }
}
