using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal interface IInfluenceVotePledgeBarterable
    {
        bool TrySecure(Hero offerer, Hero other);
    }

    internal static class VotePledgeService
    {
        private static Campaign _campaign;
        private static readonly Dictionary<Clan, HashSet<string>> Reservations = new Dictionary<Clan, HashSet<string>>();
        private static readonly Dictionary<Clan, Kingdom> ReservationRealms = new Dictionary<Clan, Kingdom>();

        internal static void Invalidate()
        {
            Reservations.Clear();
            ReservationRealms.Clear();
        }

        internal static IEnumerable<string> KeysFor(Clan voter, string kind, IEnumerable<string> keys)
        {
            if (voter?.Kingdom == null) return Enumerable.Empty<string>();
            string prefix = voter.Kingdom.StringId + "|", suffix = "|" + voter.StringId;
            return keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)
                && key.EndsWith(suffix, StringComparison.Ordinal)).Select(key => kind + "|" + key);
        }

        internal static string FiefKey(Kingdom realm, Settlement fief, Clan voter) =>
            "fief|" + realm?.StringId + "|" + fief?.StringId + "|" + voter?.StringId;
        internal static string PolicyKey(Kingdom realm, PolicyObject policy, Clan voter) =>
            "policy|" + realm?.StringId + "|" + policy?.StringId + "|" + voter?.StringId;
        internal static string ExpulsionKey(Kingdom realm, Clan target, Clan voter) =>
            "expulsion|" + realm?.StringId + "|" + target?.StringId + "|" + voter?.StringId;
        internal static string CouncilKey(Kingdom realm, PrivyCouncilOffice office, Clan voter) =>
            "council|" + realm?.StringId + "|" + (int)office + "|" + voter?.StringId;

        // These four ballot types share the native 20-influence minimum tier.
        // Final voting/payment still consults the actual decision for its cost.
        internal static int MinimumCost(Clan voter) => Math.Max(0, (int)(20f *
            (1f + (voter?.Leader?.GetPerkValue(DefaultPerks.Charm.FlexibleEthics) == true
                ? DefaultPerks.Charm.FlexibleEthics.PrimaryBonus : 0f))));

        private static HashSet<string> GetReservations(Clan voter)
        {
            if (_campaign != Campaign.Current)
            {
                _campaign = Campaign.Current;
                Invalidate();
            }
            if (ReservationRealms.TryGetValue(voter, out var realm) && realm == voter.Kingdom
                && Reservations.TryGetValue(voter, out var cached)) return cached;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in new[]
            {
                FiefDeliberationBehavior.Current?.GetVotePledgeKeys(voter),
                PolicyDeliberationBehavior.Current?.GetVotePledgeKeys(voter),
                ExpulsionDeliberationBehavior.Current?.GetVotePledgeKeys(voter),
                CouncilAppointmentDeliberationBehavior.Current?.GetVotePledgeKeys(voter)
            })
                if (source != null) keys.UnionWith(source);
            Reservations[voter] = keys;
            ReservationRealms[voter] = voter.Kingdom;
            return keys;
        }

        internal static float ReservedInfluence(Clan voter, string exceptKey = null)
        {
            if (voter == null || voter == Clan.PlayerClan) return 0f;
            var keys = GetReservations(voter);
            int count = keys.Count - (exceptKey != null && keys.Contains(exceptKey) ? 1 : 0);
            return count > 0 ? count * MinimumCost(voter) : 0f;
        }

        internal static bool CanPromise(Clan voter, Kingdom realm, string key, out TextObject reason)
        {
            reason = TextObject.GetEmpty();
            if (!IsEligible(voter, realm))
            {
                reason = new TextObject("{=BC_VotePledge_Unavailable}This lord can no longer vote in this realm.");
                return false;
            }
            int cost = MinimumCost(voter);
            if (voter.Influence + 0.001f < cost + ReservedInfluence(voter, key))
            {
                reason = new TextObject("{=BC_VotePledge_NoInfluence}They lack the influence to promise another vote. At least {COST} influence must remain available after their existing promises.");
                reason.SetTextVariable("COST", cost);
                return false;
            }
            return true;
        }

        internal static bool TryCommit(Clan voter, Kingdom realm, string key, Action commit)
        {
            if (!CanPromise(voter, realm, key, out TextObject reason))
            {
                BellumCivileNotifications.ShowPersonal(reason, BellumNotificationColors.Politics);
                return false;
            }
            commit();
            return true;
        }

        internal static bool IsEligible(Clan voter, Kingdom realm) => realm != null && !realm.IsEliminated
            && voter?.Kingdom == realm && !voter.IsEliminated && voter.Leader != null && !voter.Leader.IsDead
            && !voter.IsUnderMercenaryService && (!voter.IsMinorFaction || voter == Clan.PlayerClan);

        internal static bool IsBarterParticipant(Hero offerer, Hero other, Clan voter, Kingdom realm) =>
            offerer != null && offerer == Hero.MainHero && other != null && other == voter?.Leader
            && Hero.OneToOneConversationHero == other && offerer.Clan?.Kingdom == realm
            && !offerer.Clan.IsUnderMercenaryService && IsEligible(voter, realm);

        internal static bool TryGetPledge(KingdomDecision decision, Clan voter, IEnumerable<DecisionOutcome> outcomes,
            out string key, out DecisionOutcome pledged)
        {
            key = null;
            pledged = null;
            Kingdom realm = decision?.Kingdom;
            if (!IsEligible(voter, realm) || outcomes == null) return false;
            if (decision is SettlementClaimantDecision fief)
            {
                string candidate = FiefDeliberationBehavior.Current?.GetBribedCandidateVote(realm, fief.Settlement, voter);
                int? faction = FiefDeliberationBehavior.Current?.GetBribedVote(realm, fief.Settlement, voter);
                if (string.IsNullOrEmpty(candidate) && !faction.HasValue) return false;
                key = FiefKey(realm, fief.Settlement, voter);
                pledged = outcomes.FirstOrDefault(outcome =>
                {
                    Clan clan = FiefVoteAIPatch.GetCandidateClan(outcome);
                    return IsEligible(clan, realm) && clan != fief.ClanToExclude
                        && (!string.IsNullOrEmpty(candidate) ? clan.StringId == candidate
                            : Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?
                                .GetIdeologicalFaction(clan)?.Type == (FactionType)faction.Value);
                });
            }
            else if (decision is KingdomPolicyDecision policy)
            {
                int? score = PolicyDeliberationBehavior.Current?.GetBribedVote(realm, policy.Policy, voter);
                if (!score.HasValue || score.Value == 0) return false;
                key = PolicyKey(realm, policy.Policy, voter);
                pledged = outcomes.OfType<KingdomPolicyDecision.PolicyDecisionOutcome>()
                    .FirstOrDefault(outcome => outcome.ShouldDecisionBeEnforced == (score.Value > 0));
            }
            else if (decision is ExpelClanFromKingdomDecision expulsion)
            {
                int? score = ExpulsionDeliberationBehavior.Current?.GetBribedVote(realm, expulsion.ClanToExpel, voter);
                if (!score.HasValue || score.Value == 0) return false;
                key = ExpulsionKey(realm, expulsion.ClanToExpel, voter);
                if (expulsion.ClanToExpel?.Kingdom == realm)
                    pledged = outcomes.OfType<ExpelClanFromKingdomDecision.ExpelClanDecisionOutcome>()
                        .FirstOrDefault(outcome => outcome.ShouldBeExpelled == (score.Value > 0));
            }
            else if (decision is PrivyCouncilAppointmentDecision council)
            {
                string candidate = CouncilAppointmentDeliberationBehavior.Current?
                    .GetRecordedCandidatePledge(realm, council.Office, voter);
                if (string.IsNullOrEmpty(candidate)) return false;
                key = CouncilKey(realm, council.Office, voter);
                if (CouncilAppointmentDeliberationBehavior.Current.GetCommittedCandidateVote(realm, council.Office, voter) != null)
                    pledged = outcomes.OfType<PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome>()
                        .FirstOrDefault(outcome => outcome.CandidateClan?.StringId == candidate);
            }
            return key != null;
        }

        internal static void ApplySupport(KingdomDecision decision, Clan voter, string key, DecisionOutcome pledged,
            ref Supporter.SupportWeights weight, ref DecisionOutcome outcome)
        {
            if (pledged == null)
            {
                weight = Supporter.SupportWeights.StayNeutral;
                outcome = null;
                return;
            }
            int minimum = decision.GetInfluenceCostOfSupport(voter, Supporter.SupportWeights.SlightlyFavor);
            float available = Math.Max(0f, voter.Influence - ReservedInfluence(voter, key));
            if (available + 0.001f < minimum)
            {
                weight = Supporter.SupportWeights.StayNeutral;
                outcome = null;
                return;
            }
            // A promise guarantees a minimum vote, not maximum influence expenditure.
            if (outcome != pledged || weight < Supporter.SupportWeights.SlightlyFavor)
                weight = Supporter.SupportWeights.SlightlyFavor;
            while (weight > Supporter.SupportWeights.SlightlyFavor
                && !NpcInfluenceBudgetService.CanAfford(voter, decision.GetInfluenceCostOfSupport(voter, weight),
                    NpcInfluenceExpenseKind.CouncilCommitment)) weight--;
            outcome = pledged;
        }

        internal static void ReportUnfulfilled(Clan voter, KingdomDecision decision, bool candidateUnavailable)
        {
            TextObject notice = new TextObject(candidateUnavailable
                ? "{=BC_VotePledge_InvalidCandidate}{LORD} could not fulfill a voting promise: the promised choice was no longer available on the ballot for {MATTER}."
                : "{=BC_VotePledge_BalanceLost}{LORD} could not fulfill a voting promise on {MATTER}: their remaining influence could no longer cover the vote and other promises.");
            notice.SetTextVariable("LORD", voter.Leader?.Name ?? voter.Name);
            notice.SetTextVariable("MATTER", decision.GetGeneralTitle());
            BellumCivileNotifications.Show(notice, BellumNotificationColors.Politics, primaryKingdom: decision.Kingdom,
                primaryClan: voter, isPersonal: decision.Kingdom == Clan.PlayerClan?.Kingdom);
        }
    }
}
