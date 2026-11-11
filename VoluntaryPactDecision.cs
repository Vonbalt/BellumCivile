using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class VoluntaryPactDecision : KingdomDecision
    {
        public sealed class PactOutcome : DecisionOutcome
        {
            [SaveableField(100)] internal bool Approve;
            public PactOutcome(bool approve) { Approve = approve; }
            public override TextObject GetDecisionTitle() => Approve
                ? new TextObject("{=BC_VoluntaryPact_VoteApprove}Approve")
                : new TextObject("{=BC_VoluntaryPact_VoteReject}Reject");
            public override TextObject GetDecisionDescription() => Approve
                ? new TextObject("{=BC_VoluntaryPact_ApproveDescription}Approve the proposed exchange of hostages and pledge of peace.")
                : new TextObject("{=BC_VoluntaryPact_RejectDescription}Reject the proposed pledge of peace.");
            public override string GetDecisionLink() => null;
            public override ImageIdentifier GetDecisionImageIdentifier() => null;
        }

        [SaveableField(100)] internal string ProposalId;
        [SaveableField(101)] internal bool IsTargetVote;
        [SaveableField(102)] internal float StrategicSupport;
        [SaveableField(103)] internal bool OutcomeRecorded;
        [SaveableField(104)] internal float RulerWarTargetScore;

        public VoluntaryPactDecision(Clan proposer, string proposalId, bool targetVote, float strategicSupport,
            float rulerWarTargetScore = 0f) : base(proposer)
        {
            ProposalId = proposalId;
            IsTargetVote = targetVote;
            StrategicSupport = strategicSupport;
            RulerWarTargetScore = rulerWarTargetScore;
        }

        private static VoluntaryPactProposalBehavior Owner => Campaign.Current?.GetCampaignBehavior<VoluntaryPactProposalBehavior>();
        private VoluntaryPactProposalRecord Proposal => Owner?.Find(ProposalId);
        public override bool IsAllowed()
        {
            var proposal = Proposal;
            return !OutcomeRecorded && proposal != null && Owner.IsBallotEligible(proposal)
                && !float.IsNaN(StrategicSupport) && !float.IsInfinity(StrategicSupport)
                && !float.IsNaN(RulerWarTargetScore) && !float.IsInfinity(RulerWarTargetScore)
                && StrategicSupport >= 0 && StrategicSupport <= 100
                && (IsTargetVote
                    ? proposal.Phase == VoluntaryPactProposalPhase.TargetVote && Kingdom == proposal.Target && ProposerClan == proposal.TargetHouse
                    : proposal.Phase == VoluntaryPactProposalPhase.SourceVote && Kingdom == proposal.Source && ProposerClan == proposal.Sponsor);
        }
        public override int GetProposalInfluenceCost() => IsTargetVote ? 0 : PeacetimeHostagePactRules.ProposalInfluenceCost;
        public override TextObject GetGeneralTitle() => new TextObject("{=BC_VoluntaryPact_VoteTitle}Hostage pledge of peace");
        public override TextObject GetSupportTitle() => GetGeneralTitle();
        public override TextObject GetChooseTitle() => GetGeneralTitle();
        public override TextObject GetSupportDescription() => BuildDescription();
        public override TextObject GetChooseDescription() => BuildDescription();
        public override TextObject GetSecondaryEffects() => BuildDescription();
        protected override bool CanProposerClanChangeOpinion() => true;
        protected override bool ShouldBeCancelledInternal() => !IsAllowed();
        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
            => new DecisionOutcome[] { new PactOutcome(true), new PactOutcome(false) };
        public override Clan DetermineChooser() => Kingdom?.RulingClan;

        public override float DetermineSupport(Clan clan, DecisionOutcome outcome)
        {
            if (!(outcome is PactOutcome choice) || clan?.Leader == null || Proposal == null) return 0f;
            var other = IsTargetVote ? Proposal.Source : Proposal.Target;
            var ruler = Kingdom?.Leader;
            var otherRuler = other?.Leader;
            int relationDifference = ruler != null && otherRuler != null
                ? clan.Leader.GetRelation(otherRuler) - ruler.GetRelation(otherRuler) : 0;
            float adjustment = Math.Max(-10f, Math.Min(10f, relationDifference * .1f));
            var war = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (clan != Kingdom?.RulingClan && war?.TryGetCachedTargetScore(clan, other, out float clanWarScore) == true)
                adjustment += (float)(PeacetimeHostagePactRules.WarTargetPenalty(RulerWarTargetScore)
                    - PeacetimeHostagePactRules.WarTargetPenalty(clanWarScore));
            float support = Math.Max(0f, Math.Min(100f, StrategicSupport + adjustment));
            return choice.Approve ? support : 100f - support;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> outcomes)
        {
            var approve = outcomes?.OfType<PactOutcome>().FirstOrDefault(o => o.Approve);
            if (approve != null && ProposerClan != null) approve.SetSponsor(ProposerClan);
        }

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            if (OutcomeRecorded || !(chosenOutcome is PactOutcome choice) || !IsAllowed()) return;
            if (Owner.TryResolveVote(ProposalId, Kingdom, choice.Approve)) OutcomeRecorded = true;
        }
        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> outcomes, DecisionOutcome chosen) { }
        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> outcomes)
            => outcomes?.OfType<PactOutcome>().FirstOrDefault(o => o.Approve);
        public override TextObject GetChosenOutcomeText(DecisionOutcome outcome, SupportStatus status, bool isShortVersion = false)
            => !OutcomeRecorded ? new TextObject("{=BC_VoluntaryPact_VoteInvalid}The proposed pledge could no longer be decided.")
                : outcome is PactOutcome choice && choice.Approve
                ? new TextObject("{=BC_VoluntaryPact_VoteApproved}The council has approved the proposed pledge.")
                : new TextObject("{=BC_VoluntaryPact_VoteRejected}The council has rejected the proposed pledge.");

        private TextObject BuildDescription()
        {
            var proposal = Proposal;
            var text = new TextObject("{=BC_VoluntaryPact_BallotDescription}{SOURCE} and {TARGET} would pledge peace for {DAYS} days. {SOURCE} would offer {SOURCE_HOSTAGE}; {TARGET} would offer {TARGET_HOSTAGE}. The proposing house would pay {GOLD} denars to the receiving house.");
            text.SetTextVariable("SOURCE", proposal?.Source?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("TARGET", proposal?.Target?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("SOURCE_HOSTAGE", proposal?.SourceHostage?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("TARGET_HOSTAGE", proposal?.TargetHostage?.Name
                ?? new TextObject("{=BC_VoluntaryPact_HostageToBeChosen}a relative chosen by its ruler"));
            text.SetTextVariable("DAYS", proposal?.DurationDays ?? 0);
            text.SetTextVariable("GOLD", proposal?.Compensation ?? 0);
            return text;
        }
    }
}
