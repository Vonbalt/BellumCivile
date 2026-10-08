using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static void DynasticBlocked(CourtAgendaRecord a, string reason, bool alliance = false)
        {
            if ((alliance ? a.Dynastic.AllianceBlocker : a.Dynastic.MarriageBlocker) == reason) return;
            if (alliance) a.Dynastic.AllianceBlocker = reason;
            else a.Dynastic.MarriageBlocker = reason;
            BellumCivileLogger.Log($"Court dynastic pursuit waiting; realm={a.Realm.StringId}; target={a.Dynastic.Target.StringId}; alliance={alliance}; reason={reason}.");
        }

        internal bool PursueDynasticMarriage(Clan clan, StrategicMarriageBehavior strategy)
        {
            if (clan == null || clan == Clan.PlayerClan || clan.IsEliminated) return false;
            var a = NpcDynasticAgenda(clan);
            if (a == null) return false;
            var p = a.Dynastic;
            double now = CampaignTime.Now.ToDays;
            if (p.MarriageTechnicalAttempts >= 3) return false;
            if (now < p.NextMarriageAttemptDay) return true;
            p.NextMarriageAttemptDay = now + 3;
            if (!DynasticIdentity(p)) return false; // Maintenance cancels a changed couple or house.
            if (!BellumMarriageStrategyHelper.CourtMarriageParticipant(p.First)
                || !BellumMarriageStrategyHelper.CourtMarriageParticipant(p.Second))
            { DynasticBlocked(a, "couple_temporarily_unavailable"); return true; }
            bool player = p.First.Clan == Clan.PlayerClan || p.Second.Clan == Clan.PlayerClan;
            if (player && !strategy.CanSendCourtMarriageOffer)
            { DynasticBlocked(a, "player_offer_cooldown_or_pending"); return true; }
            int attempts = p.MarriageTechnicalAttempts;
            BellumMarriageMatch match = null;
            try
            {
                match = BellumMarriageStrategyHelper.EvaluateCourtMarriage(p.First, p.Second, false, p.Destination);
                if (match == null)
                { DynasticBlocked(a, "native_couple_unavailable"); return true; }
                if (!MarriageOutcome.BothAccept(match.SuitorAcceptance, match.CandidateAcceptance,
                    p.First.Clan == Clan.PlayerClan, p.Second.Clan == Clan.PlayerClan, BellumCivileConstants.MarriageStrategyMinimumScore))
                {
                    p.NpcAttempted = true;
                    p.MarriageOutcome = "house_refused";
                    p.Response = CourtDynasticResponse.ForeignRefused;
                    BellumCivileLogger.Log($"Court royal marriage considered; realm={a.Realm.StringId}; accepted=false; aligned={GetFavoredBloc(a.Realm) == FactionType.Nobility}.");
                    return true;
                }
                if (!BellumMarriageStrategyHelper.CourtMarriageExecutionReady(match))
                { DynasticBlocked(a, "native_couple_unavailable"); return true; }
                if (match.Outcome.Destination != p.Destination)
                { FinishDynastic(a, CourtObjectiveState.Cancelled, "household_terms_changed"); return true; }
                p.MarriageTechnicalAttempts++;
                if (player)
                {
                    p.Response = CourtDynasticResponse.OfferIssued;
                    if (!strategy.TrySendCourtMarriageOffer(match))
                    {
                        p.Response = CourtDynasticResponse.AwaitingApproach;
                        DynasticBlocked(a, "offer_not_registered");
                        return true;
                    }
                    p.NpcAttempted = true;
                    p.MarriageOutcome = "player_offer_issued";
                    strategy.RecordCourtMarriageOpportunity(clan, match, false);
                }
                else
                {
                    // Seal before native callbacks; a partial failure must not repeat a completed wedding.
                    p.NpcAttempted = true;
                    using (new NpcMarriageClanContext(match.Suitor, match.Candidate, p.Destination))
                        MarriageAction.Apply(match.Suitor, match.Candidate);
                    if (!DynasticMarried(p))
                    {
                        p.NpcAttempted = !DynasticIdentity(p);
                        DynasticBlocked(a, "wedding_not_completed");
                        return true;
                    }
                    p.MarriageOutcome = "married";
                    OnCourtMarriageCompleted(p.First, p.Second);
                    strategy.RecordCourtMarriageOpportunity(clan, match, true);
                }
                p.MarriageBlocker = null;
                BellumCivileLogger.Log($"Court royal marriage considered; realm={a.Realm.StringId}; outcome={p.MarriageOutcome}; aligned={GetFavoredBloc(a.Realm) == FactionType.Nobility}.");
            }
            catch (Exception ex)
            {
                bool married = DynasticMarried(p);
                bool offered = player && (StrategicMarriageBehavior.HasMarriageOfferFor(p.First)
                    || StrategicMarriageBehavior.HasMarriageOfferFor(p.Second));
                p.NpcAttempted = married || offered || !DynasticIdentity(p);
                if (!p.NpcAttempted) p.Response = CourtDynasticResponse.AwaitingApproach;
                p.MarriageOutcome = married ? "married_after_callback_error" : offered ? "offer_after_callback_error" : "technical_failure";
                if ((married || offered) && match != null) strategy.RecordCourtMarriageOpportunity(clan, match, false);
                // Evaluation itself can throw before reaching the action-attempt receipt.
                p.MarriageTechnicalAttempts = attempts + 1;
                BellumCivileLogger.Log($"Court royal marriage exception; realm={a.Realm.StringId}; outcome={p.MarriageOutcome}; error={ex}");
            }
            return true;
        }

        private Clan DynasticAllianceSponsor(CourtAgendaRecord a)
        {
            var model = Campaign.Current.Models.AllianceModel;
            return a.Dynastic.Members.Concat(new[] { a.Realm.RulingClan }).Distinct()
                .Where(c => Eligible(c, a.Realm) && c != Clan.PlayerClan && c.CurrentTotalStrength > 0
                    && DynasticAllianceBonus(a.Realm, a.Dynastic.Target, c) > 0)
                .OrderBy(c => c.StringId).FirstOrDefault(c =>
                    NpcInfluenceBudgetService.CanAfford(c, model.GetInfluenceCostOfProposingStartingAlliance(c), NpcInfluenceExpenseKind.Discretionary)
                    && model.GetSupportScoreOfStartingAllianceForClan(a.Realm, a.Dynastic.Target, c, out _) > 0);
        }

        private void PursueDynasticAlliance(CourtAgendaRecord a, double now)
        {
            var p = a.Dynastic;
            if (a.State != CourtAgendaState.Completed || p.AllianceAttempted || p.AllianceTechnicalAttempts >= 3
                || now < p.NextAllianceAttemptDay || !DynasticWindow(a) || !DynasticOwnerValid(a) || !DynasticMarried(p)) return;
            p.NextAllianceAttemptDay = now + 3;
            if (a.Realm.AlliedKingdoms.Contains(p.Target))
            { p.AllianceAttempted = true; p.AllianceOutcome = "already_allied"; return; }
            if (a.Realm.UnresolvedDecisions.OfType<StartAllianceDecision>().Any(d => d.KingdomToStartAllianceWith == p.Target)
                || p.Target.UnresolvedDecisions.OfType<StartAllianceDecision>().Any(d => d.KingdomToStartAllianceWith == a.Realm))
            { p.AllianceAttempted = true; p.AllianceOutcome = "existing_ballot"; return; }
            if (a.Realm.UnresolvedDecisions.OfType<StartAllianceDecision>().Any()
                || p.Target.UnresolvedDecisions.OfType<StartAllianceDecision>().Any())
            { DynasticBlocked(a, "other_alliance_ballot_pending", true); return; }
            var sponsor = DynasticAllianceSponsor(a);
            if (sponsor == null) { DynasticBlocked(a, "no_funded_willing_participant", true); return; }
            StartAllianceDecision decision = null;
            int paid = 0;
            int attempts = p.AllianceTechnicalAttempts;
            try
            {
                decision = new StartAllianceDecision(sponsor, p.Target);
                if (decision.TriggerTime.ToDays > a.ObjectiveData.DeadlineDay)
                { DynasticBlocked(a, "insufficient_time_for_ballot", true); return; }
                if (!decision.IsAllowed() || !decision.CanMakeDecision(out _))
                { DynasticBlocked(a, "native_or_foreign_acceptance_unavailable", true); return; }
                int cost = decision.GetInfluenceCost(sponsor);
                if (!NpcInfluenceBudgetService.TrySpend(sponsor, cost, NpcInfluenceExpenseKind.Discretionary, "court_dynastic_alliance"))
                { DynasticBlocked(a, "proposal_unaffordable", true); return; }
                paid = cost;
                p.AllianceOutcome = null;
                p.AllianceTechnicalAttempts++;
                IdeologyBehavior.AddDecisionAsModAction(a.Realm, decision, ignoreInfluenceCost: true);
                if (a.Realm.UnresolvedDecisions.Contains(decision) || p.AllianceOutcome != null)
                {
                    p.AllianceAttempted = true;
                    paid = 0;
                    p.AllianceBlocker = null;
                    if (p.AllianceOutcome == null) p.AllianceOutcome = "ballot_pending";
                    BellumCivileLogger.Log($"Court dynastic alliance submitted; realm={a.Realm.StringId}; target={p.Target.StringId}; sponsor={sponsor.StringId}; cost={cost}; vote_day={decision.TriggerTime.ToDays:0.0}.");
                }
                else DynasticBlocked(a, "decision_not_registered", true);
            }
            catch (Exception ex)
            {
                p.AllianceTechnicalAttempts = attempts + 1;
                if (decision != null && (a.Realm.UnresolvedDecisions.Contains(decision) || p.AllianceOutcome != null))
                { p.AllianceAttempted = true; paid = 0; }
                BellumCivileLogger.Log($"Court dynastic alliance exception; realm={a.Realm.StringId}; error={ex}");
            }
            finally
            {
                if (paid > 0) NpcInfluenceBudgetService.Refund(sponsor, paid, NpcInfluenceExpenseKind.Discretionary, "court_dynastic_registration_failed");
            }
        }

        private void OnCourtDynasticDecisionAdded(KingdomDecision decision, bool playerInvolved)
        {
            if (!(decision is StartAllianceDecision alliance)) return;
            foreach (var a in _agendas.Where(a => DynasticAllianceDecisionMatches(a, alliance)))
            { a.Dynastic.AllianceAttempted = true; a.Dynastic.AllianceOutcome = "ballot_pending"; }
        }

        private static bool DynasticAllianceDecisionMatches(CourtAgendaRecord a, StartAllianceDecision d) => IsDynastic(a)
            && a.State == CourtAgendaState.Completed && a.Dynastic != null && DynasticWindow(a)
            && (a.Realm == d.Kingdom && a.Dynastic.Target == d.KingdomToStartAllianceWith
                || a.Realm == d.KingdomToStartAllianceWith && a.Dynastic.Target == d.Kingdom);

        private void OnCourtDynasticDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool playerInvolved)
        {
            if (!(decision is StartAllianceDecision alliance)) return;
            foreach (var a in _agendas.Where(a => DynasticAllianceDecisionMatches(a, alliance)))
            {
                a.Dynastic.AllianceAttempted = true;
                a.Dynastic.AllianceOutcome = outcome is StartAllianceDecision.StartAllianceDecisionOutcome result && result.ShouldAllianceBeStarted
                    ? "ballot_approved" : "ballot_rejected";
                BellumCivileLogger.Log($"Court dynastic alliance concluded; realm={a.Realm.StringId}; target={a.Dynastic.Target.StringId}; outcome={a.Dynastic.AllianceOutcome}.");
            }
        }
    }
}
