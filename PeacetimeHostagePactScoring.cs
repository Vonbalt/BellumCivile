using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    internal static class PeacetimeHostagePactScoring
    {
        // Called when a ballot is prepared, never from each supporter's UI refresh.
        internal static bool TryScore(VoluntaryPactProposalRecord proposal, Kingdom evaluator, out float score, out float warTargetScore)
        {
            score = 0f;
            warTargetScore = 0f;
            if (proposal == null || !proposal.HasValidTerms(false)
                || (evaluator != proposal.Source && evaluator != proposal.Target)
                || proposal.Source.RulingClan != proposal.SourceHouse || proposal.Target.RulingClan != proposal.TargetHouse
                || proposal.Source.Leader == null || proposal.Target.Leader == null) return false;

            var sourceCandidates = TreatyHostageEligibility.GetCandidates(proposal.Source);
            var targetCandidates = TreatyHostageEligibility.GetCandidates(proposal.Target);
            var source = sourceCandidates.FirstOrDefault(c => c.Hero == proposal.SourceHostage);
            var target = proposal.TargetHostage == null
                ? targetCandidates.Where(c => c.Hero != null && c.Hero != Hero.MainHero && c.Hero != proposal.SourceHostage)
                    .OrderByDescending(c => c.Tier).FirstOrDefault()
                : targetCandidates.FirstOrDefault(c => c.Hero == proposal.TargetHostage);
            if (source == null || target == null || source.Hero == Hero.MainHero || target.Hero == Hero.MainHero) return false;

            bool sourceEvaluates = evaluator == proposal.Source;
            Kingdom other = sourceEvaluates ? proposal.Target : proposal.Source;
            Clan rulingHouse = evaluator.RulingClan;
            int relation = evaluator.Leader.GetRelation(other.Leader);
            bool commonThreat = evaluator.FactionsAtWarWith?.Any(f => f is Kingdom k && k != other
                && !k.IsEliminated && other.IsAtWarWith(k)) == true;
            bool trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>()
                ?.HasTradeAgreement(evaluator, other, out _) == true;
            var marriages = MarriageAllianceBehavior.Instance;
            bool marriage = MarriageAllianceHelper.HasMarriageAlliance(proposal.SourceHouse, proposal.TargetHouse)
                || marriages?.HasUnexcludedNativeMarriage(proposal.SourceHouse, proposal.TargetHouse) == true;
            var war = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            float targetScore = war?.GetRankedTargets(rulingHouse).FirstOrDefault(t => t.TargetKingdom == other)?.Score ?? 0f;
            if (float.IsNaN(targetScore) || float.IsInfinity(targetScore)) return false;
            warTargetScore = targetScore;
            score = (float)PeacetimeHostagePactRules.Score(relation, commonThreat, trade, marriage, targetScore,
                sourceEvaluates ? source.Tier : target.Tier, sourceEvaluates ? target.Tier : source.Tier,
                proposal.DurationDays, proposal.BaselineDays, sourceEvaluates ? 0 : proposal.Compensation);
            return true;
        }
    }
}
