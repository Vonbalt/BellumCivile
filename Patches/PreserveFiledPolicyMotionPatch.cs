using System.Linq;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To keep an already-filed policy motion on the council floor even when its proposer no
    /// longer prefers the likely outcome. Bannerlord normally withdraws that motion before the
    /// vote; Bellum court factions deliberately let the council vote it down. Real relevance
    /// cancellations remain intact (invalid realm/proposer, resolved policy, or broken sponsor).
    /// </summary>
    [HarmonyPatch(typeof(KingdomDecision), "ShouldBeCancelled")]
    public static class PreserveFiledPolicyMotionPatch
    {
        internal enum PolicyDecisionExecutionStatus
        {
            Pending,
            Concluded,
            Cancelled
        }

        private static readonly Dictionary<KingdomPolicyDecision, PolicyDecisionExecutionStatus> TrackedDecisions =
            new Dictionary<KingdomPolicyDecision, PolicyDecisionExecutionStatus>();

        private static readonly System.Reflection.MethodInfo ShouldBeCancelledInternalMethod =
            AccessTools.Method(typeof(KingdomDecision), "ShouldBeCancelledInternal");

        internal static void BeginTracking(KingdomPolicyDecision decision)
        {
            if (decision != null)
                TrackedDecisions[decision] = PolicyDecisionExecutionStatus.Pending;
        }

        internal static void MarkConcluded(KingdomPolicyDecision decision)
        {
            if (decision != null && TrackedDecisions.ContainsKey(decision))
                TrackedDecisions[decision] = PolicyDecisionExecutionStatus.Concluded;
        }

        internal static PolicyDecisionExecutionStatus EndTracking(KingdomPolicyDecision decision)
        {
            if (decision == null || !TrackedDecisions.TryGetValue(decision, out PolicyDecisionExecutionStatus status))
                return PolicyDecisionExecutionStatus.Pending;

            TrackedDecisions.Remove(decision);
            return status;
        }

        public static void Postfix(KingdomDecision __instance, ref bool __result)
        {
            if (!__result)
                return;

            if (!(__instance is KingdomPolicyDecision policyDecision))
                return;

            PolicyCancellationDiagnostics diagnostics = DiagnosePolicyCancellation(policyDecision);

            if (diagnostics.ShouldPreserveFiledMotion)
            {
                BellumCivileLogger.Log(
                    $"Preserved filed policy motion for council vote {policyDecision.Kingdom?.StringId ?? "null"}|{diagnostics.PolicyId}; proposer={policyDecision.ProposerClan?.StringId ?? "null"} reason=proposer_withdrew_support influence={diagnostics.CurrentInfluence:0.##} selected_weight={diagnostics.SelectedSupportWeight}.");

                __result = false;
                return;
            }

            BellumCivileLogger.Log(
                $"Vanilla cancelled live policy vote {policyDecision.Kingdom?.StringId ?? "null"}|{diagnostics.PolicyId}; proposer={policyDecision.ProposerClan?.StringId ?? "null"} reasons={string.Join(",", diagnostics.Reasons)} influence={diagnostics.CurrentInfluence:0.##} selected_weight={diagnostics.SelectedSupportWeight}.");

            if (TrackedDecisions.ContainsKey(policyDecision))
                TrackedDecisions[policyDecision] = PolicyDecisionExecutionStatus.Cancelled;
            CourtAgendaBehavior.Current?.CancelFiledMotion(policyDecision.Kingdom, policyDecision.Policy?.StringId,
                policyDecision.ProposerClan?.StringId, PolicyRefundRules.IsTechnical(diagnostics.Reasons)
                    ? diagnostics.Reasons[0] : "live_vote_political_or_unknown_cancellation");
        }

        private static PolicyCancellationDiagnostics DiagnosePolicyCancellation(KingdomPolicyDecision decision)
        {
            PolicyCancellationDiagnostics diagnostics = new PolicyCancellationDiagnostics
            {
                PolicyId = decision?.Policy?.StringId ?? "null"
            };

            if (decision == null)
            {
                diagnostics.Reasons.Add("decision_null");
                return diagnostics;
            }

            Kingdom kingdom = decision.Kingdom;
            Clan proposer = decision.ProposerClan;
            diagnostics.CurrentInfluence = proposer?.Influence ?? 0f;

            if (decision.Policy == null)
            {
                diagnostics.Reasons.Add("policy_missing");
                return diagnostics;
            }

            if (kingdom == null || kingdom.IsEliminated)
                diagnostics.Reasons.Add("kingdom_invalid");

            if (proposer == null || proposer.IsEliminated || proposer.Kingdom != kingdom)
                diagnostics.Reasons.Add("proposer_invalid");

            try
            {
                if (!decision.IsAllowed())
                    diagnostics.Reasons.Add("not_allowed");
            }
            catch
            {
                diagnostics.Reasons.Add("is_allowed_exception");
            }

            if (ShouldBeCancelledInternal(decision))
                diagnostics.Reasons.Add("internal_cancel");

            if (diagnostics.Reasons.Count > 0)
                return diagnostics;

            MBList<DecisionOutcome> possibleOutcomes;
            try
            {
                possibleOutcomes = decision.DetermineInitialCandidates()?.ToMBList();
            }
            catch
            {
                diagnostics.Reasons.Add("initial_outcomes_exception");
                return diagnostics;
            }

            if (possibleOutcomes == null || possibleOutcomes.Count == 0)
            {
                diagnostics.Reasons.Add("no_initial_outcomes");
                return diagnostics;
            }

            try
            {
                possibleOutcomes = decision.NarrowDownCandidates(possibleOutcomes, 3);
            }
            catch
            {
                diagnostics.Reasons.Add("narrow_outcomes_exception");
                return diagnostics;
            }

            if (possibleOutcomes == null || possibleOutcomes.Count == 0)
            {
                diagnostics.Reasons.Add("no_narrowed_outcomes");
                return diagnostics;
            }

            DecisionOutcome queriedOutcome;
            try
            {
                queriedOutcome = decision.GetQueriedDecisionOutcome(possibleOutcomes);
                decision.DetermineSponsors(possibleOutcomes);
            }
            catch
            {
                diagnostics.Reasons.Add("sponsor_resolution_exception");
                return diagnostics;
            }

            if (queriedOutcome == null)
                diagnostics.Reasons.Add("queried_outcome_missing");

            bool hasInvalidSponsor = possibleOutcomes.Any(outcome =>
                outcome?.SponsorClan != null &&
                (outcome.SponsorClan.IsEliminated || outcome.SponsorClan.Kingdom != kingdom));

            if (hasInvalidSponsor)
                diagnostics.Reasons.Add("invalid_sponsor");

            bool proposerSponsorsOutcome = possibleOutcomes.Any(outcome => outcome?.SponsorClan == proposer);
            if (!proposerSponsorsOutcome)
                diagnostics.Reasons.Add("missing_proposer_sponsor");

            if (diagnostics.Reasons.Count > 0)
                return diagnostics;

            Supporter proposerSupporter = decision.DetermineSupporters()
                .FirstOrDefault(supporter => supporter?.Clan == proposer);

            if (proposerSupporter == null)
            {
                diagnostics.Reasons.Add("missing_proposer_supporter");
                return diagnostics;
            }

            Supporter.SupportWeights supportWeight;
            DecisionOutcome selectedOutcome;
            try
            {
                selectedOutcome = decision.DetermineSupportOption(
                    proposerSupporter,
                    possibleOutcomes,
                    out supportWeight,
                    true);
            }
            catch
            {
                diagnostics.Reasons.Add("support_option_exception");
                return diagnostics;
            }

            diagnostics.SelectedSupportWeight = supportWeight.ToString();
            diagnostics.CurrentInfluence = proposer.Influence;
            int minimumCommittedCost = decision.GetInfluenceCostOfSupport(
                proposer,
                Supporter.SupportWeights.SlightlyFavor);
            bool hasVanillaCommitmentReserve = diagnostics.CurrentInfluence >= minimumCommittedCost * 1.5f;
            bool neutralOrOpposed = selectedOutcome == null
                || supportWeight == Supporter.SupportWeights.StayNeutral
                || selectedOutcome != queriedOutcome;

            if (hasVanillaCommitmentReserve && neutralOrOpposed)
            {
                diagnostics.Reasons.Add("proposer_withdrew_support");
                diagnostics.ShouldPreserveFiledMotion = true;
            }

            if (diagnostics.Reasons.Count == 0)
                diagnostics.Reasons.Add("unknown_cancel");

            return diagnostics;
        }

        private static bool ShouldBeCancelledInternal(KingdomDecision decision)
        {
            if (decision == null || ShouldBeCancelledInternalMethod == null)
                return true;

            try
            {
                return (bool)ShouldBeCancelledInternalMethod.Invoke(decision, null);
            }
            catch
            {
                return true;
            }
        }

        private sealed class PolicyCancellationDiagnostics
        {
            public string PolicyId { get; set; }
            public float CurrentInfluence { get; set; }
            public string SelectedSupportWeight { get; set; } = "Unknown";
            public bool ShouldPreserveFiledMotion { get; set; }
            public List<string> Reasons { get; } = new List<string>();
        }
    }
}
