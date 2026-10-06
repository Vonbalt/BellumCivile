using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    internal static class NpcKingdomDecisionProposalBudgetPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(KingdomDecision kingdomDecision, ref bool ignoreInfluenceCost)
        {
            Clan proposer = kingdomDecision?.ProposerClan;
            if (ignoreInfluenceCost || proposer == null || proposer == Clan.PlayerClan)
                return true;

            int cost = kingdomDecision.GetInfluenceCost(proposer);
            if (cost <= 0)
                return true;

            if (!NpcInfluenceBudgetService.TrySpend(
                proposer,
                cost,
                NpcInfluenceExpenseKind.Discretionary,
                "kingdom_decision_proposal"))
            {
                return false;
            }

            // The centralized service has already charged the proposal.
            ignoreInfluenceCost = true;
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecision), nameof(KingdomDecision.DetermineSupportOption))]
    internal static class NpcKingdomDecisionSupportBudgetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            KingdomDecision __instance,
            Supporter supporter,
            MBReadOnlyList<DecisionOutcome> possibleOutcomes,
            ref Supporter.SupportWeights supportWeightOfSelectedOutcome,
            ref DecisionOutcome __result)
        {
            Clan clan = supporter?.Clan;
            if (clan == null
                || clan == Clan.PlayerClan
                || (WarPeaceRevampBehavior.IsRevampEnabled() && __instance is DeclareWarDecision))
            {
                return;
            }

            if (VotePledgeService.TryGetPledge(__instance, clan, possibleOutcomes, out string key, out var pledged))
            {
                VotePledgeService.ApplySupport(__instance, clan, key, pledged, ref supportWeightOfSelectedOutcome, ref __result);
                return;
            }

            while (supportWeightOfSelectedOutcome >= Supporter.SupportWeights.SlightlyFavor)
            {
                int cost = __instance.GetInfluenceCostOfSupport(clan, supportWeightOfSelectedOutcome);
                if (NpcInfluenceBudgetService.CanAfford(
                    clan,
                    cost,
                    NpcInfluenceExpenseKind.CouncilCommitment))
                {
                    break;
                }

                supportWeightOfSelectedOutcome--;
            }

            if (supportWeightOfSelectedOutcome < Supporter.SupportWeights.SlightlyFavor)
            {
                supportWeightOfSelectedOutcome = Supporter.SupportWeights.StayNeutral;
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(KingdomElection), "GetAiChoice")]
    internal static class NpcKingdomDecisionOverrideBudgetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            KingdomDecision ____decision,
            Clan ____chooser,
            MBReadOnlyList<DecisionOutcome> possibleOutcomes,
            ref DecisionOutcome __result)
        {
            if (____decision == null
                || ____chooser == null
                || ____chooser == Clan.PlayerClan
                || __result == null
                || (WarPeaceRevampBehavior.IsRevampEnabled() && ____decision is DeclareWarDecision))
            {
                return;
            }

            DecisionOutcome popular = possibleOutcomes?
                .OrderByDescending(outcome => outcome?.TotalSupportPoints ?? float.MinValue)
                .FirstOrDefault();
            if (popular == null || __result == popular)
                return;

            int overrideCost = Campaign.Current.Models.ClanPoliticsModel
                .GetInfluenceRequiredToOverrideKingdomDecision(popular, __result, ____decision);
            int ownCommitment = possibleOutcomes
                .Where(outcome => outcome != null)
                .SelectMany(outcome => outcome.SupporterList)
                .Where(supporter => supporter?.Clan == ____chooser)
                .Select(supporter => ____decision.GetInfluenceCostOfSupport(____chooser, supporter.SupportWeight))
                .DefaultIfEmpty(0)
                .Max();

            if (!NpcInfluenceBudgetService.CanAfford(
                ____chooser,
                overrideCost + ownCommitment,
                NpcInfluenceExpenseKind.CouncilCommitment))
            {
                NpcInfluenceBudgetService.RecordBlocked(
                    ____chooser,
                    overrideCost,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    "kingdom_decision_override");
                __result = popular;
            }
        }
    }

    [HarmonyPatch(typeof(KingdomElection), "HandleInfluenceCosts")]
    internal static class NpcKingdomDecisionPaymentBudgetPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            KingdomElection __instance,
            KingdomDecision ____decision,
            MBList<DecisionOutcome> ____possibleOutcomes,
            List<Supporter> ____supporters,
            DecisionOutcome ____chosenOutcome,
            Clan ____chooser)
        {
            if (____decision == null
                || ____possibleOutcomes == null
                || (WarPeaceRevampBehavior.IsRevampEnabled() && ____decision is DeclareWarDecision))
            {
                return true;
            }

            DecisionOutcome popular = ____possibleOutcomes
                .OrderByDescending(outcome => outcome?.TotalSupportPoints ?? float.MinValue)
                .FirstOrDefault();

            foreach (DecisionOutcome outcome in ____possibleOutcomes.Where(outcome => outcome != null))
            {
                foreach (Supporter supporter in outcome.SupporterList)
                {
                    Clan clan = supporter?.Clan;
                    if (clan == null)
                        continue;

                    int cost = ____decision.GetInfluenceCost(outcome, clan, supporter.SupportWeight);
                    if (____supporters?.Count == 1)
                        cost = 0;
                    if (____chosenOutcome != outcome)
                        cost /= 2;
                    if (outcome != ____chosenOutcome && clan.Leader.GetPerkValue(DefaultPerks.Charm.GoodNatured))
                        continue;

                    if (clan == Clan.PlayerClan)
                    {
                        if (cost > 0)
                            ChangeClanInfluenceAction.Apply(clan, -cost);
                    }
                    else
                    {
                        string pledgeKey = VotePledgePaymentPatch.GetPaymentKey(__instance, clan);
                        if (pledgeKey != null)
                            NpcInfluenceBudgetService.SpendPledgedVote(clan, cost, pledgeKey);
                        else NpcInfluenceBudgetService.SpendUpToReserve(
                            clan,
                            cost,
                            NpcInfluenceExpenseKind.CouncilCommitment,
                            "kingdom_decision_vote");
                    }
                }
            }

            if (popular != null && ____chosenOutcome != null && popular != ____chosenOutcome && ____chooser != null)
            {
                int overrideCost = Campaign.Current.Models.ClanPoliticsModel
                    .GetInfluenceRequiredToOverrideKingdomDecision(popular, ____chosenOutcome, ____decision);
                if (____chooser == Clan.PlayerClan)
                {
                    if (overrideCost > 0)
                        ChangeClanInfluenceAction.Apply(____chooser, -overrideCost);
                }
                else
                {
                    NpcInfluenceBudgetService.TrySpend(
                        ____chooser,
                        overrideCost,
                        NpcInfluenceExpenseKind.CouncilCommitment,
                        "kingdom_decision_override");
                }
            }

            return false;
        }
    }
}
