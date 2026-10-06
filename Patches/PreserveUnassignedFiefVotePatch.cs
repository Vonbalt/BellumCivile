using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDecision), nameof(KingdomDecision.ShouldBeCancelled))]
    internal static class PreserveUnassignedFiefVotePatch
    {
        private sealed class DiagnosticState { internal string Reason; }
        private static readonly ConditionalWeakTable<SettlementClaimantDecision, DiagnosticState> Diagnostics =
            new ConditionalWeakTable<SettlementClaimantDecision, DiagnosticState>();
        private static readonly System.Reflection.MethodInfo InternalCancellation =
            AccessTools.Method(typeof(KingdomDecision), "ShouldBeCancelledInternal");

        internal static bool IsNativeAllocation(KingdomDecision decision) =>
            decision?.GetType() == typeof(SettlementClaimantDecision)
            && ((SettlementClaimantDecision)decision).Settlement?.Town?.IsOwnerUnassigned == true;

        private static void Postfix(KingdomDecision __instance, ref bool __result)
        {
            if (!__result || !IsNativeAllocation(__instance)) return;
            var decision = (SettlementClaimantDecision)__instance;
            string reason = Diagnose(decision);
            bool preserve = reason == "proposer_abstained";
            var state = Diagnostics.GetOrCreateValue(decision);
            if (state.Reason != reason)
            {
                state.Reason = reason;
                BellumCivileLogger.Log($"{(preserve ? "Preserved" : "Cancelled")} unassigned fief ballot {decision.Kingdom?.StringId}|{decision.Settlement.StringId}; reason={reason}; proposer={decision.ProposerClan?.StringId}; influence={decision.ProposerClan?.Influence:0.##}; reserve={NpcInfluenceBudgetService.GetRoleReserve(decision.ProposerClan):0.##}.");
            }
            // Land still needs an owner even if its proposer cannot afford a paid endorsement.
            if (preserve) __result = false;
        }

        private static string Diagnose(SettlementClaimantDecision decision)
        {
            try
            {
                var realm = decision.Kingdom;
                var proposer = decision.ProposerClan;
                if (realm == null || realm.IsEliminated) return "kingdom_invalid";
                if (proposer == null || proposer.IsEliminated || proposer.Kingdom != realm) return "proposer_invalid";
                if (decision.Settlement.MapFaction != realm) return "settlement_changed_realm";
                if (!decision.IsAllowed()) return "not_allowed";
                if (InternalCancellation == null || (bool)InternalCancellation.Invoke(decision, null)) return "internal_cancel";
                if (proposer == Clan.PlayerClan) return "other_cancellation";

                var initial = decision.DetermineInitialCandidates()?.ToMBList();
                if (initial == null || initial.Count == 0) return "no_candidates";
                var outcomes = decision.NarrowDownCandidates(initial, 3);
                if (outcomes == null || outcomes.Count == 0) return "no_shortlist";
                decision.DetermineSponsors(outcomes);
                if (outcomes.Any(o => o == null || o.SponsorClan == null || o.SponsorClan.IsEliminated
                    || o.SponsorClan.Kingdom != realm)) return "invalid_sponsor";
                if (!outcomes.Any(o => o.SponsorClan == proposer)) return "other_cancellation";
                if (proposer.Influence < decision.GetInfluenceCostOfSupport(proposer, Supporter.SupportWeights.SlightlyFavor) * 1.5f)
                    return "other_cancellation";

                var selected = decision.DetermineSupportOption(new Supporter(proposer), outcomes, out var weight, true);
                return selected == null || weight == Supporter.SupportWeights.StayNeutral
                    ? "proposer_abstained" : "other_cancellation";
            }
            catch (Exception ex)
            {
                return "diagnosis_failed_" + ex.GetType().Name;
            }
        }

        internal static void LogRemoval(SettlementClaimantDecision decision)
        {
            string reason = Diagnostics.TryGetValue(decision, out var state) ? state.Reason : "not_observed";
            BellumCivileLogger.Log($"Removing unassigned fief ballot {decision.Kingdom?.StringId}|{decision.Settlement.StringId}; last_cancellation_check={reason}; trigger_day={decision.TriggerTime.ToDays:0.00}; trigger_elapsed_days={decision.TriggerTime.ElapsedDaysUntilNow:0.00}; needs_player_resolution={decision.NeedsPlayerResolution}; ruler_in_siege={decision.Kingdom?.RulingClan?.Leader?.PartyBelongedTo?.SiegeEvent != null}.");
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.RemoveDecision))]
    internal static class FiefAllocationRemovalDiagnosticsPatch
    {
        private static void Prefix(KingdomDecision __0)
        {
            if (PreserveUnassignedFiefVotePatch.IsNativeAllocation(__0))
                PreserveUnassignedFiefVotePatch.LogRemoval((SettlementClaimantDecision)__0);
        }
    }
}
