using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public static partial class BellumIntegration
    {
        internal static TextObject UnavailableVote() => new TextObject(
            "{=BC_Integration_VoteUnavailable}This vote or its requested choice is no longer available, or this house has already promised its vote.");

        private static bool CommitPledge(Clan voter, Kingdom realm, string key, bool valid,
            Action commit, out TextObject reason)
        {
            reason = UnavailableVote();
            if (!valid || voter == Clan.PlayerClan) return false;
            if (!VotePledgeService.CanPromise(voter, realm, key, out reason)) return false;
            commit();
            return true;
        }

        /// <summary>Support means enforce the proposed decision, including repeal when that is proposed.</summary>
        public static bool TryPledgePolicyVote(Kingdom realm, Clan voter, PolicyObject policy,
            bool support, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            var behavior = PolicyDeliberationBehavior.Current;
            return CommitPledge(voter, realm, VotePledgeService.PolicyKey(realm, policy, voter),
                behavior?.HasPendingVoteForPolicy(realm, policy) == true
                    && !behavior.GetBribedVote(realm, policy, voter).HasValue,
                () => behavior.SetBribedVote(realm, policy, voter,
                    support ? C.PolicyBribeForcedSupportScore : -C.PolicyBribeForcedSupportScore), out reason);
        }

        public static bool TryPledgeExpulsionVote(Kingdom realm, Clan voter, Clan target,
            bool expel, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            var behavior = ExpulsionDeliberationBehavior.Current;
            return CommitPledge(voter, realm, VotePledgeService.ExpulsionKey(realm, target, voter),
                target != null && target.Kingdom == realm && target != realm?.RulingClan
                    && behavior?.HasPendingExpulsionForTarget(realm, target) == true
                    && !behavior.GetBribedVote(realm, target, voter).HasValue,
                () => behavior.SetBribedVote(realm, target, voter,
                    expel ? C.ExpulsionBribeForcedSupportScore : -C.ExpulsionBribeForcedSupportScore), out reason);
        }

        public static bool TryPledgeFiefVote(Kingdom realm, Clan voter, Settlement fief,
            Clan candidate, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            var behavior = FiefDeliberationBehavior.Current;
            bool valid = behavior?.HasPendingFiefVoteForSettlement(realm, fief) == true
                && fief.MapFaction == realm && VotePledgeService.IsEligible(candidate, realm)
                && !behavior.GetBribedVote(realm, fief, voter).HasValue
                && string.IsNullOrEmpty(behavior.GetBribedCandidateVote(realm, fief, voter));
            if (valid)
                valid = !realm.UnresolvedDecisions.OfType<SettlementClaimantDecision>()
                    .Any(d => d.Settlement == fief && d.ClanToExclude == candidate);
            return CommitPledge(voter, realm, VotePledgeService.FiefKey(realm, fief, voter), valid,
                () => behavior.SetBribedCandidateVote(realm, fief, voter, candidate), out reason);
        }

        public static bool TryPledgeCouncilVote(Kingdom realm, Clan voter, PrivyCouncilOffice office,
            Clan candidate, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            var behavior = CouncilAppointmentDeliberationBehavior.Current;
            return CommitPledge(voter, realm, VotePledgeService.CouncilKey(realm, office, voter),
                Enum.IsDefined(typeof(PrivyCouncilOffice), office)
                    && behavior?.HasPendingAppointment(realm, office) == true
                    && behavior.GetRecordedCandidatePledge(realm, office, voter) == null
                    && CouncilAppointmentNominationHelper.ScoreCandidate(voter, candidate, realm, office,
                        Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>()) != null,
                () => behavior.SetCommittedCandidateVote(realm, office, voter, candidate, "persuaded"), out reason);
        }
    }
}
