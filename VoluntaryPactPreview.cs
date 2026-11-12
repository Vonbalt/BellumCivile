using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class VoluntaryPactPreview
    {
        internal static VoluntaryPactProposalRecord Create(Kingdom source, Kingdom target, Clan sponsor,
            Hero hostage, int days, int payment)
        {
            if (source?.RulingClan == null || target?.RulingClan == null || sponsor == null || hostage == null)
                return null;
            return new VoluntaryPactProposalRecord
            {
                Id = "preview", Source = source, Target = target, SourceHouse = source.RulingClan,
                TargetHouse = target.RulingClan, Sponsor = sponsor, SourceHostage = hostage,
                DurationDays = days, BaselineDays = BellumCivileOptions.HostagePactMinimumDurationDays,
                Compensation = payment, Payer = source.Leader, Payee = target.Leader,
                CreatedDay = CampaignTime.Now.ToDays, Phase = VoluntaryPactProposalPhase.SourceVote
            };
        }

        internal static bool TryEvaluate(VoluntaryPactProposalRecord proposal, bool targetVote,
            out KingdomElection.ElectionOutcomeSupport support, out int outlook)
        {
            support = KingdomElection.ElectionOutcomeSupport.LowSupport;
            outlook = 0;
            try
            {
                Kingdom realm = targetVote ? proposal?.Target : proposal?.Source;
                Clan sponsor = targetVote ? proposal?.TargetHouse : proposal?.Sponsor;
                if (realm?.Leader == null || sponsor == null || sponsor.Kingdom != realm
                    || !PeacetimeHostagePactScoring.TryScore(proposal, realm, out float score, out float warScore))
                    return false;
                var decision = new VoluntaryPactDecision(sponsor, proposal.Id, targetVote, score, warScore);
                decision.SetPreviewProposal(proposal);
                var election = new KingdomElection(decision);
                election.SetupResultWithoutPlayerSupport();
                support = election.GetDecisionOutcomeSupportForSponsor(sponsor);
                var yes = election.PossibleOutcomes.OfType<VoluntaryPactDecision.PactOutcome>().FirstOrDefault(o => o.Approve);
                var no = election.PossibleOutcomes.OfType<VoluntaryPactDecision.PactOutcome>().FirstOrDefault(o => !o.Approve);
                if (yes == null || no == null) return false;
                outlook = CompareVotes(yes.TotalSupportPoints, no.TotalSupportPoints);
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Voluntary pact forecast unavailable; targetVote={targetVote}; error={ex}");
                return false;
            }
        }

        internal static int CompareVotes(float yes, float no) => yes > no ? 1 : yes < no ? -1 : 0;

        internal static TextObject SupportText(KingdomElection.ElectionOutcomeSupport support)
            => GameTexts.FindText("str_decision_outcome_support_status", support.ToString());
    }
}
