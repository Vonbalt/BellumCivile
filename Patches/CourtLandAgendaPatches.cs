using BellumCivile.Behaviors;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(SettlementClaimantDecision), "GetSupportDescription")]
    internal static class CourtGrantSupportTextPatch
    {
        private static void Postfix(SettlementClaimantDecision __instance, ref TextObject __result)
        {
            if (CourtAgendaBehavior.Current?.IsGrantElection(__instance) != true) return;
            __result = new TextObject("{=BC_CrownGrantElectionDescription}The crown has offered {SETTLEMENT} to the realm. The lord who commands the greatest support of the assembled houses shall receive it.");
            __result.SetTextVariable("SETTLEMENT", __instance.Settlement.Name);
        }
    }

    [HarmonyPatch(typeof(SettlementClaimantDecision), "GetChooseDescription")]
    internal static class CourtGrantChooseTextPatch
    {
        private static void Postfix(SettlementClaimantDecision __instance, ref TextObject __result)
        {
            if (CourtAgendaBehavior.Current?.IsGrantElection(__instance) != true) return;
            __result = new TextObject("{=BC_CrownGrantRatificationDescription}The crown shall honor the support of the assembled houses. Their leading candidate will receive {SETTLEMENT}; where support is equal, the crown may settle the tie.");
            __result.SetTextVariable("SETTLEMENT", __instance.Settlement.Name);
        }
    }

    [HarmonyPatch(typeof(KingdomElection), "ApplyChosenOutcome")]
    internal static class CourtGrantMajorityPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(KingdomElection __instance, KingdomDecision ____decision, ref DecisionOutcome ____chosenOutcome)
        {
            if (CourtAgendaBehavior.Current?.IsGrantElection(____decision) != true) return;
            var outcomes = __instance.PossibleOutcomes.Where(o => FiefVoteAIPatch.GetCandidateClan(o) is Clan clan
                && CourtAgendaBehavior.Eligible(clan, ____decision.Kingdom)
                && clan != ((SettlementClaimantDecision)____decision).ClanToExclude).ToList();
            if (outcomes.Count == 0) return;
            // Ties retain the selected outcome; otherwise use a stable clan identity.
            var selected = ____chosenOutcome;
            ____chosenOutcome = outcomes.OrderByDescending(o => o.TotalSupportPoints)
                .ThenByDescending(o => o == selected)
                .ThenBy(o => FiefVoteAIPatch.GetCandidateClan(o).StringId, System.StringComparer.Ordinal).First();
        }
    }

    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    internal static class CourtRevocationAllocationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(KingdomDecision kingdomDecision) => !(kingdomDecision is SettlementClaimantDecision allocation)
            || CourtAgendaBehavior.Current?.RouteRevocationAllocation(allocation) != true;
    }

    [HarmonyPatch(typeof(SettlementClaimantPreliminaryDecision), "ApplyChosenOutcome")]
    internal static class CourtRevocationResultPatch
    {
        private static void Postfix(SettlementClaimantPreliminaryDecision __instance, DecisionOutcome chosenOutcome) =>
            CourtAgendaBehavior.Current?.ConcludeExecutiveVote(__instance, RevocationVoteAIPatch.ResolveShouldSettlementOwnerChange(chosenOutcome));
    }

    [HarmonyPatch(typeof(SettlementClaimantDecision), "ApplyChosenOutcome")]
    internal static class CourtLandResultPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SettlementClaimantDecision __instance, DecisionOutcome chosenOutcome)
        {
            var winner = FiefVoteAIPatch.GetCandidateClan(chosenOutcome);
            if (winner != null && winner != __instance.ClanToExclude && __instance.Settlement.OwnerClan == winner)
                CourtAgendaBehavior.Current?.OnCourtClaimAllocationResolved(__instance.Settlement, winner);
            CourtAgendaBehavior.Current?.ConcludeExecutiveVote(__instance,
                winner != null && winner != __instance.ClanToExclude && __instance.Settlement.OwnerClan == winner);
        }
    }
}
