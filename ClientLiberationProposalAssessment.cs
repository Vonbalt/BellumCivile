using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    // Later values identify candidates that have cleared more proposal gates.
    internal enum ClientLiberationProposalBlocker
    {
        PersonalDesire,
        Willingness,
        Influence,
        StrategicTarget,
        Permission,
        None
    }

    internal sealed class ClientLiberationProposalAssessment
    {
        internal ClientClanLibertyAssessment Candidate;
        internal float WarWill;
        internal float Resolve;
        internal float Willingness;
        internal NpcInfluenceBudgetAssessment Budget;
        internal ClientLiberationProposalBlocker Blocker;
        internal TextObject PermissionReason;

        internal static ClientLiberationProposalAssessment FindBest(ClientLibertyAssessment realm)
        {
            var war = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (realm?.ClientKingdom == null || war == null || !WarPeaceRevampBehavior.IsRevampEnabled())
                return null;

            var candidates = realm.Clans.Where(entry => entry?.Clan != null
                && entry.Clan.Kingdom == realm.ClientKingdom && !entry.Clan.IsEliminated
                && entry.Clan.Leader != null && !entry.Clan.IsUnderMercenaryService)
                .Select(entry => AssessCandidate(entry, war))
                .OrderByDescending(entry => entry.Blocker)
                .ThenByDescending(entry => entry.Willingness)
                .ThenByDescending(entry => entry.Candidate.LibertyDesire)
                .ThenByDescending(entry => entry.Candidate.Power)
                .ThenBy(entry => entry.Candidate.Clan.StringId, System.StringComparer.Ordinal)
                .ToList();

            // Only check strategy/permissions once the realm and a prospective proposer are ready.
            if (!realm.CanAttemptLiberation
                || realm.ClientKingdom.UnresolvedDecisions.Any(decision => decision is DeclareWarDecision))
                return candidates.FirstOrDefault();

            var scoring = new WarTargetScoringService();
            foreach (var candidate in candidates.Where(entry => entry.Blocker == ClientLiberationProposalBlocker.None))
            {
                WarTargetScore target = scoring.ScoreLiberationTarget(candidate.Candidate.Clan, realm);
                if (target == null || target.Score < BellumCivileOptions.WarTargetMinimumScore)
                    candidate.Blocker = ClientLiberationProposalBlocker.StrategicTarget;
                else if (!CanMakeDecision(candidate.Candidate.Clan, realm.SuzerainKingdom, out candidate.PermissionReason))
                    candidate.Blocker = ClientLiberationProposalBlocker.Permission;
                else
                    return candidate;
            }
            return candidates.OrderByDescending(entry => entry.Blocker).FirstOrDefault();
        }

        private static ClientLiberationProposalAssessment AssessCandidate(
            ClientClanLibertyAssessment entry, WarPeaceRevampBehavior war)
        {
            float will = war.PeekWarWill(entry.Clan);
            float willingness = ClientLiberationRules.EffectiveWarWill(will, entry.LibertyDesire);
            int cost = Campaign.Current?.Models?.DiplomacyModel?.GetInfluenceCostOfProposingWar(entry.Clan) ?? 200;
            var budget = NpcInfluenceBudgetService.Assess(entry.Clan, cost, NpcInfluenceExpenseKind.Discretionary);
            return new ClientLiberationProposalAssessment
            {
                Candidate = entry,
                WarWill = will,
                Resolve = ClientLiberationRules.ResolveBonus(entry.LibertyDesire),
                Willingness = willingness,
                Budget = budget,
                Blocker = entry.LibertyDesire < C.ClientClanLiberationDesireThreshold
                    ? ClientLiberationProposalBlocker.PersonalDesire
                    : willingness < BellumCivileOptions.WarWillDeclareThreshold
                        ? ClientLiberationProposalBlocker.Willingness
                        : !budget.CanAfford ? ClientLiberationProposalBlocker.Influence
                        : ClientLiberationProposalBlocker.None
            };
        }

        private static bool CanMakeDecision(Clan clan, Kingdom target, out TextObject reason)
            => new DeclareWarDecision(clan, target).CanMakeDecision(out reason, includeReason: true);
    }
}
