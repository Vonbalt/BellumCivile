using System;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDecision), nameof(KingdomDecision.DetermineSupportOption))]
    internal static class WarDeclarationSupportOptionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            KingdomDecision __instance,
            Supporter supporter,
            MBReadOnlyList<DecisionOutcome> possibleOutcomes,
            ref Supporter.SupportWeights supportWeightOfSelectedOutcome,
            ref DecisionOutcome __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(__instance is DeclareWarDecision decision))
                return true;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            Clan clan = supporter?.Clan;
            if (behavior == null || clan == null
                || !behavior.TryEvaluateWarSupport(decision, clan, true, out float utility))
                return true;

            int commitment = PoliticalInfluenceVoteHelper.GetCommitment(utility, clan, out TreatyCouncilVoteStance stance);
            supportWeightOfSelectedOutcome = WarDeclarationCouncilService.GetWeight(commitment);
            if (stance == TreatyCouncilVoteStance.Abstain || commitment <= 0)
            {
                __result = null;
                return false;
            }

            bool supportsWar = stance == TreatyCouncilVoteStance.Yay;
            __result = possibleOutcomes?.FirstOrDefault(outcome =>
                WarDeclarationCouncilService.IsWarOutcome(outcome) == supportsWar);
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomDecision), nameof(KingdomDecision.GetInfluenceCostOfSupport))]
    internal static class WarDeclarationInfluenceCostPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            KingdomDecision __instance,
            Supporter.SupportWeights supportWeight,
            ref int __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(__instance is DeclareWarDecision))
                return;

            __result = WarDeclarationCouncilService.GetCommitment(supportWeight);
        }
    }

    [HarmonyPatch(typeof(KingdomElection), nameof(KingdomElection.DetermineOfficialSupport))]
    internal static class WarDeclarationOfficialSupportPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(KingdomElection __instance, KingdomDecision ____decision)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(____decision is DeclareWarDecision))
                return true;

            PoliticalInfluenceVoteTally tally = WarDeclarationCouncilService.CalculateFromOutcomes(
                ____decision,
                __instance.PossibleOutcomes);
            WarDeclarationCouncilService.ApplyOfficialSupport(__instance.PossibleOutcomes, tally);
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomElection), nameof(KingdomElection.GetWinChanceWithPlayerSupport))]
    internal static class WarDeclarationPlayerPreviewPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            KingdomElection __instance,
            KingdomDecision ____decision,
            DecisionOutcome supportedOutcome,
            Supporter.SupportWeights supportWeight,
            ref float __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(____decision is DeclareWarDecision)
                || supportedOutcome == null || !__instance.PossibleOutcomes.Contains(supportedOutcome))
                return true;

            PoliticalInfluenceVoteTally tally = WarDeclarationCouncilService.CalculateFromOutcomes(
                ____decision,
                __instance.PossibleOutcomes,
                supportedOutcome,
                WarDeclarationCouncilService.GetCommitment(supportWeight));

            int yes = tally.YayInfluence;
            int no = tally.NayInfluence;
            if (!tally.IsRatified)
                no = Math.Max(no, yes + BellumCivileConstants.TreatyCouncilMinimumVoteStep);
            int total = Math.Max(1, yes + no);
            __result = WarDeclarationCouncilService.IsWarOutcome(supportedOutcome)
                ? yes / (float)total
                : no / (float)total;
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomElection), "GetAiChoice")]
    internal static class WarDeclarationRulerChoicePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            KingdomElection __instance,
            KingdomDecision ____decision,
            Clan ____chooser,
            MBReadOnlyList<DecisionOutcome> possibleOutcomes,
            ref DecisionOutcome __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(____decision is DeclareWarDecision decision)
                || ____chooser == null)
                return true;

            __instance.DetermineOfficialSupport();
            DecisionOutcome popular = possibleOutcomes.OrderByDescending(outcome => outcome.TotalSupportPoints).FirstOrDefault();
            __result = popular;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (!____decision.IsKingsVoteAllowed || behavior == null
                || !behavior.TryEvaluateWarSupport(decision, ____chooser, true, out float utility))
                return false;

            int commitment = PoliticalInfluenceVoteHelper.GetCommitment(utility, ____chooser, out TreatyCouncilVoteStance stance);
            if (stance == TreatyCouncilVoteStance.Abstain || commitment < BellumCivileConstants.TreatyCouncilStrongCommitment)
                return false;

            bool rulerWantsWar = stance == TreatyCouncilVoteStance.Yay;
            DecisionOutcome preferred = possibleOutcomes.FirstOrDefault(outcome =>
                WarDeclarationCouncilService.IsWarOutcome(outcome) == rulerWantsWar);
            if (preferred == null || preferred == popular)
                return false;

            int overrideCost = Campaign.Current.Models.ClanPoliticsModel
                .GetInfluenceRequiredToOverrideKingdomDecision(popular, preferred, ____decision);
            int totalCost = commitment + overrideCost;
            if (NpcInfluenceBudgetService.CanAfford(
                ____chooser,
                totalCost,
                NpcInfluenceExpenseKind.CouncilCommitment))
            {
                __result = preferred;
            }
            else
            {
                NpcInfluenceBudgetService.RecordBlocked(
                    ____chooser,
                    totalCost,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    "war_council_override");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomElection), "HandleInfluenceCosts")]
    internal static class WarDeclarationInfluencePaymentPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            KingdomDecision ____decision,
            MBList<DecisionOutcome> ____possibleOutcomes,
            DecisionOutcome ____chosenOutcome,
            Clan ____chooser)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !(____decision is DeclareWarDecision))
                return true;

            DecisionOutcome popular = ____possibleOutcomes.OrderByDescending(outcome => outcome.TotalSupportPoints).FirstOrDefault();
            foreach (DecisionOutcome outcome in ____possibleOutcomes)
            {
                foreach (Supporter supporter in outcome.SupporterList)
                {
                    int cost = WarDeclarationCouncilService.GetCommitment(supporter.SupportWeight);
                    if (cost > 0)
                    {
                        NpcInfluenceBudgetService.SpendUpToReserve(
                            supporter.Clan,
                            cost,
                            NpcInfluenceExpenseKind.CouncilCommitment,
                            "war_council_commitment",
                            BellumCivileConstants.TreatyCouncilMinimumVoteStep,
                            BellumCivileConstants.TreatyCouncilMildCommitment,
                            BellumCivileConstants.TreatyCouncilStrongCommitment);
                    }
                }
            }

            if (popular != null && ____chosenOutcome != null && popular != ____chosenOutcome && ____chooser != null)
            {
                int overrideCost = Campaign.Current.Models.ClanPoliticsModel
                    .GetInfluenceRequiredToOverrideKingdomDecision(popular, ____chosenOutcome, ____decision);
                if (overrideCost > 0)
                {
                    NpcInfluenceBudgetService.TrySpend(
                        ____chooser,
                        overrideCost,
                        NpcInfluenceExpenseKind.CouncilCommitment,
                        "war_council_override");
                }
            }

            return false;
        }
    }
}
