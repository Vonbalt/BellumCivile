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
    internal static class DefensivePactVoteRules
    {
        internal const int InfluenceCost = 100;
        internal static float Support(double threat, int threshold, int relation)
        {
            if (!DefensivePactThreatRules.Finite(threat)) return 0f;
            return (float)Math.Max(0d, Math.Min(100d, 50d + Math.Max(0d, threat - threshold) * .5d
                - Math.Max(-100, Math.Min(100, relation)) * .25d));
        }

        internal static float LeaveSupport(double threat, int threshold, int relation)
        {
            if (!DefensivePactThreatRules.Finite(threat) || (threshold != 25 && threshold != 50 && threshold != 75 && threshold != 95)) return 0f;
            return (float)Math.Max(0d, Math.Min(100d, 50d + (threshold - threat) * .5d
                + Math.Max(-100, Math.Min(100, relation)) * .25d));
        }
    }

    public sealed class DefensivePactJoinDecision : KingdomDecision
    {
        public sealed class JoinOutcome : DecisionOutcome
        {
            [SaveableField(100)] internal bool Approve;
            [SaveableField(101)] internal bool IsDeparture;
            public JoinOutcome(bool approve, bool isDeparture = false) { Approve = approve; IsDeparture = isDeparture; }
            public override TextObject GetDecisionTitle() => Approve
                ? new TextObject("{=BC_DefensivePact_VoteApprove}Approve") : new TextObject("{=BC_DefensivePact_VoteReject}Reject");
            public override TextObject GetDecisionDescription() => IsDeparture
                ? Approve ? new TextObject("{=BC_DefensivePact_ApproveDeparture}We should leave the defensive pact.")
                    : new TextObject("{=BC_DefensivePact_RejectDeparture}We should remain in the defensive pact.")
                : Approve ? new TextObject("{=BC_DefensivePact_ApproveDescription}We should enter the defensive pact.")
                    : new TextObject("{=BC_DefensivePact_RejectDescription}We should not enter the defensive pact.");
            public override string GetDecisionLink() => null;
            public override ImageIdentifier GetDecisionImageIdentifier() => null;
        }

        [SaveableField(100)] internal Kingdom Target;
        [SaveableField(101)] internal bool FilingStarted;
        [SaveableField(102)] internal bool Filed;
        [SaveableField(103)] internal bool Resolved;
        [SaveableField(104)] internal bool JoinStarted;
        [SaveableField(105)] internal bool Joined;
        [SaveableField(106)] internal float InfluenceBefore;
        [SaveableField(107)] internal bool RefundStarted;
        [SaveableField(108)] internal Kingdom Member;
        [SaveableField(109)] internal double InitialThreat;
        [SaveableField(110)] internal int InitialThreshold;
        [SaveableField(111)] internal bool IsDeparture;
        [SaveableField(112)] internal bool Departed;

        public DefensivePactJoinDecision(Clan proposer, Kingdom target, double initialThreat, int initialThreshold, bool isDeparture = false) : base(proposer)
        { Target = target; Member = proposer?.Kingdom; InitialThreat = initialThreat; InitialThreshold = initialThreshold; IsDeparture = isDeparture; }
        private static DefensivePactBehavior Owner => Campaign.Current?.GetCampaignBehavior<DefensivePactBehavior>();
        private DefensivePactCampaignAssessment Assessment => Owner?.Assess(Kingdom, Target);
        private bool BasicIdentityValid => Owner != null && ProposerClan != null && !ProposerClan.IsEliminated
            && !ProposerClan.IsUnderMercenaryService && Member != null && ProposerClan.Kingdom == Member
            && Kingdom == Member && Target != null && Member != Target && !Member.IsEliminated && !Target.IsEliminated
            && BellumCivileOptions.EnableWarPeaceLogicRevamp && Member.RulingClan?.Leader?.IsAlive == true
            && Target.RulingClan?.Leader?.IsAlive == true && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(Member)
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(Target);
        public override bool IsAllowed()
        {
            if (Resolved || JoinStarted || !BasicIdentityValid) return false;
            if (IsDeparture) return Owner.IsMemberAgainst(Member.StringId, Target.StringId);
            var assessment = Assessment;
            return assessment == null || assessment.IsEligible;
        }
        public override int GetProposalInfluenceCost() => DefensivePactVoteRules.InfluenceCost;
        public override TextObject GetGeneralTitle() => IsDeparture
            ? new TextObject("{=BC_DefensivePact_LeaveVoteTitle}Leave a defensive pact")
            : new TextObject("{=BC_DefensivePact_VoteTitle}Join a defensive pact");
        public override TextObject GetSupportTitle() => GetGeneralTitle();
        public override TextObject GetChooseTitle() => GetGeneralTitle();
        public override TextObject GetSupportDescription() => Describe();
        public override TextObject GetChooseDescription() => Describe();
        public override TextObject GetSecondaryEffects() => Describe();
        protected override bool CanProposerClanChangeOpinion() => true;
        protected override bool ShouldBeCancelledInternal() => !IsAllowed();
        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates() => new DecisionOutcome[] { new JoinOutcome(true, IsDeparture), new JoinOutcome(false, IsDeparture) };
        public override Clan DetermineChooser() => Kingdom?.RulingClan;

        public override float DetermineSupport(Clan clan, DecisionOutcome outcome)
        {
            if (!(outcome is JoinOutcome choice) || clan?.Leader == null || Target?.Leader == null) return 0f;
            if (IsDeparture)
            {
                double threat = InitialThreat;
                int threshold = InitialThreshold;
                var owner = Owner;
                if (owner != null && owner.TryGetDepartureContext(Member, Target, out double currentThreat, out int currentThreshold))
                { threat = currentThreat; threshold = currentThreshold; }
                float departureSupport = DefensivePactVoteRules.LeaveSupport(threat, threshold, clan.Leader.GetRelation(Target.Leader));
                return choice.Approve ? departureSupport : 100f - departureSupport;
            }
            var assessment = Assessment;
            if (assessment != null && !assessment.IsEligible) return 0f;
            float support = DefensivePactVoteRules.Support(assessment?.Threat ?? InitialThreat,
                assessment?.JoiningThreshold ?? InitialThreshold,
                clan.Leader.GetRelation(Target.Leader));
            return choice.Approve ? support : 100f - support;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> outcomes)
        {
            var approve = outcomes?.OfType<JoinOutcome>().FirstOrDefault(o => o.Approve);
            if (approve != null && ProposerClan != null) approve.SetSponsor(ProposerClan);
        }

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            if (Resolved || JoinStarted || !(chosenOutcome is JoinOutcome choice) || !IsAllowed()) return;
            if (!choice.Approve) { Resolved = true; return; }
            JoinStarted = true;
            RecoverJoin(Owner);
        }

        internal void RecoverJoin(DefensivePactBehavior owner)
        {
            if (!JoinStarted || Resolved) return;
            if (owner == null) return;
            if (IsDeparture)
            {
                if (!BasicIdentityValid) { Resolved = true; return; }
                Departed = !owner.IsMemberAgainst(Member?.StringId, Target?.StringId) || owner.TryLeave(Member.StringId);
                Resolved = true;
                return;
            }
            if (owner.IsMemberAgainst(Member?.StringId, Target?.StringId)) { Joined = true; Resolved = true; return; }
            if (!BasicIdentityValid) { Resolved = true; return; }
            var assessment = owner.Assess(Member, Target);
            if (assessment == null) return;
            Joined = assessment.IsEligible && owner.TryJoin(Member.StringId, Target.StringId, CampaignTime.Now.ToDays,
                assessment.Threat, assessment.JoiningThreshold, assessment.Fiefs, assessment.MemberStrength,
                assessment.TargetStrength, assessment.RulerRelation, assessment.AtPeace, assessment.Allied,
                assessment.HasPledge, assessment.IsTargetClient);
            Resolved = true;
        }

        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> outcomes, DecisionOutcome chosen) { }
        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> outcomes)
            => outcomes?.OfType<JoinOutcome>().FirstOrDefault(o => o.Approve);
        public override TextObject GetChosenOutcomeText(DecisionOutcome outcome, SupportStatus status, bool isShortVersion = false)
            => IsDeparture ? Departed
                    ? new TextObject("{=BC_DefensivePact_VoteLeft}The realm has left the defensive pact.")
                    : new TextObject("{=BC_DefensivePact_VoteStayed}The realm remains in the defensive pact.")
                : JoinStarted && !Resolved ? new TextObject("{=BC_DefensivePact_VoteAwaitingVerification}The council approved the pact; current circumstances are being verified.")
                : !Resolved ? new TextObject("{=BC_DefensivePact_VoteInvalid}The proposed pact could no longer be decided.")
                : Joined ? new TextObject("{=BC_DefensivePact_VoteJoined}The realm has joined the defensive pact.")
                : new TextObject("{=BC_DefensivePact_VoteNotJoined}The realm did not join the defensive pact.");

        private TextObject Describe() => new TextObject(IsDeparture
                ? "{=BC_DefensivePact_LeaveVoteDescription}The realm would leave the defensive pact against {TARGET} and would no longer be bound to answer its calls to arms."
                : "{=BC_DefensivePact_VoteDescription}The realm would join a defensive pact against {TARGET}. If {TARGET} attacks a signatory, the others will take up arms in separate wars.")
            .SetTextVariable("TARGET", Target?.Name ?? TextObject.GetEmpty());
    }
}
