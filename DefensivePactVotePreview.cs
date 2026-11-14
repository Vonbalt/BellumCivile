using System;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile
{
    internal static class DefensivePactVotePreview
    {
        internal static bool TryEvaluate(DefensivePactBehavior owner, Clan sponsor, Kingdom target, bool departure,
            out KingdomElection.ElectionOutcomeSupport support)
        {
            support = KingdomElection.ElectionOutcomeSupport.LowSupport;
            var member = sponsor?.Kingdom;
            if (owner == null || member == null || target == null) return false;
            try
            {
                double threat;
                int threshold;
                if (departure)
                {
                    if (!owner.TryGetDepartureContext(member, target, out threat, out threshold)) return false;
                }
                else
                {
                    var assessment = owner.Assess(member, target);
                    if (assessment?.IsEligible != true) return false;
                    threat = assessment.Threat; threshold = assessment.JoiningThreshold;
                }
                var decision = new DefensivePactJoinDecision(sponsor, target, threat, threshold, departure);
                support = KingdomElection.GetElectionOutcomeSupport(decision, sponsor);
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Defensive pact vote forecast unavailable: " + ex);
                return false;
            }
        }
    }
}
