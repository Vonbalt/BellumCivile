using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal sealed class CourtExecutiveObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind { get; }
        internal CourtExecutiveObjectiveSource(string kind) { Kind = kind; }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (Kind == CourtExecutiveRules.Treason)
            {
                if (owner.Faction != null || !context.ManualSelection && !BellumCivileOptions.EnableAutomaticTreasonIndictments) yield break;
                foreach (var clan in context.Voters.Where(c => c != owner.Sponsor))
                    yield return new CourtObjectiveCandidate(clan.StringId, "indict");
            }
            else
            {
                if (Kind == CourtExecutiveRules.Grant && owner.Faction != null) yield break;
                if (Kind == CourtExecutiveRules.Revoke && owner.Faction?.Type != FactionType.Nobility
                    && owner.Faction?.Type != FactionType.Liberty) yield break;
                foreach (var settlement in context.Settlements)
                    yield return new CourtObjectiveCandidate(settlement.StringId, Kind);
            }
        }

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var realm = context.Realm;
            KingdomDecision decision;
            Func<DecisionOutcome, bool> supports;
            double weight = 1;
            if (Kind == CourtExecutiveRules.Treason)
            {
                var target = context.FindClan(candidate.TargetId);
                var ideology = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
                if (target?.Leader == null || !CourtAgendaBehavior.IsDirectCrownBusiness(context.PlayerAgenda) && target.Leader.GetRelation(realm.RulingClan.Leader) <= -100
                    || ideology?.CanRulerIndictClan(realm, target, false, out _, readOnly: true) != true) return Reject("treason_ineligible");
                decision = new ExpelClanFromKingdomDecision(owner.Sponsor, target);
                supports = ExpelClanDecisionPatch.ShouldExpel;
                weight += Math.Max(0, -target.Leader.GetRelation(realm.RulingClan.Leader) - 60) / 40.0;
            }
            else
            {
                var settlement = context.FindSettlement(candidate.TargetId);
                var holder = settlement?.OwnerClan;
                if (settlement?.Town == null || !CourtAgendaBehavior.Eligible(holder, realm)
                    || Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.HasPendingFiefVoteForSettlement(realm, settlement) == true
                    || realm.UnresolvedDecisions.Any(d => d is SettlementClaimantDecision claim && claim.Settlement == settlement
                        || d is SettlementClaimantPreliminaryDecision revoke && revoke.Settlement == settlement)) return Reject("land_unavailable");
                var claims = context.Claims(settlement);
                if (Kind == CourtExecutiveRules.Grant)
                {
                    if (holder != realm.RulingClan || !CourtExecutiveRules.GrantEligible(holder.Fiefs.Count)
                        || !context.Voters.Any(c => c != holder)) return Reject("grant_holdings");
                    var leader = holder.Leader;
                    weight = CourtExecutiveRules.GrantWeight(claims.Any(c => c.Strength == FeudalClaimStrength.Strong),
                        claims.Any(c => c.Strength == FeudalClaimStrength.Weak), leader.GetTraitLevel(DefaultTraits.Generosity),
                        leader.GetTraitLevel(DefaultTraits.Honor), leader.GetTraitLevel(DefaultTraits.Calculating));
                    decision = new SettlementClaimantDecision(owner.Sponsor, settlement, null, holder);
                    return new CourtObjectiveEvaluation(true,
                        NpcInfluenceBudgetService.CanAfford(owner.Sponsor, decision.GetProposalInfluenceCost(), NpcInfluenceExpenseKind.Discretionary),
                        new CourtObjectiveWeight(weight), "recipient_election; no predetermined beneficiary");
                }
                if (holder == owner.Sponsor) return Reject("self_revocation");
                if (owner.Faction?.Type == FactionType.Liberty ? !CourtExecutiveRules.LibertyRevocationEligible(holder.Fiefs.Count) : claims.Count == 0)
                    return Reject("no_faction_motive");
                decision = new SettlementClaimantPreliminaryDecision(owner.Sponsor, settlement);
                supports = RevocationVoteAIPatch.ResolveShouldSettlementOwnerChange;
            }
            if (context.ManualSelection) return new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(weight), "player_eligible");
            try
            {
                var outcomes = decision.DetermineInitialCandidates().ToList();
                var yes = outcomes.FirstOrDefault(supports);
                var no = outcomes.FirstOrDefault(o => !supports(o));
                if (yes == null || no == null) return Reject("missing_outcomes");
                var forecast = CourtPolicyForecast.Calculate(decision, decision.GetProposalInfluenceCost(), yes, no, context.Voters);
                return new CourtObjectiveEvaluation(true, forecast.Viable, new CourtObjectiveWeight(weight),
                    $"support={forecast.For}/{forecast.For + forecast.Against}; affordable={forecast.Affordable}");
            }
            catch (Exception ex)
            {
                return Reject("forecast_exception:" + ex.GetType().Name);
            }
        }

        internal static IEnumerable<FeudalClaimRecord> Claims(Kingdom realm, Settlement settlement)
        {
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || !titles.TryGetBarony(settlement, out var title)
                || title.DeFactoHolderClanId != settlement.OwnerClan?.StringId) return Enumerable.Empty<FeudalClaimRecord>();
            var eligible = new HashSet<string>(realm.Clans.Where(c => c != settlement.OwnerClan && CourtAgendaBehavior.Eligible(c, realm)).Select(c => c.StringId));
            return titles.GetActiveClaimsByTitle(title).Where(c => eligible.Contains(c.ClaimantClanId)
                && (c.Strength == FeudalClaimStrength.Weak || c.Strength == FeudalClaimStrength.Strong));
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId,
                ActionId = choice.Candidate.ActionId };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.PolicyId = null;
            agenda.OriginalHolder = Kind == CourtExecutiveRules.Treason ? null
                : Settlement.All.FirstOrDefault(s => s.StringId == choice.Candidate.TargetId)?.OwnerClan;
            agenda.AllocationVoteDate = agenda.VoteDate + (agenda.VoteDate - agenda.SessionDate);
        }

        private static CourtObjectiveEvaluation Reject(string reason) => new CourtObjectiveEvaluation(false, false, new CourtObjectiveWeight(0), reason);
    }
}
