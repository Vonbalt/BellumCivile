using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, double> _tradeRepeatUntil = new Dictionary<string, double>();
        private static readonly System.Reflection.MethodInfo ConsiderCourtTrade =
            AccessTools.Method(typeof(KingdomDecisionProposalBehavior), "ConsiderTradeAgreement");

        internal float TradeRepeatWeight(Kingdom realm, Kingdom target) => realm != null && target != null
            && _tradeRepeatUntil.TryGetValue(realm.StringId + "|" + target.StringId, out var until)
            && until > CampaignTime.Now.ToDays ? .5f : 1f;

        private void TradeBlocked(CourtAgendaRecord a, string reason)
        {
            if (a.Trade.LastBlocker == reason) return;
            a.Trade.LastBlocker = reason;
            BellumCivileLogger.Log($"Court trade pursuit waiting; realm={a.Realm.StringId}; target={a.Trade.Target.StringId}; day={CampaignTime.Now.ToDays:0.0}; deadline={a.ObjectiveData.DeadlineDay:0.0}; reason={reason}.");
        }

        private string TradeInvalidation(CourtAgendaRecord a)
        {
            var realm = a.Realm;
            var target = a.Trade.Target;
            var model = Campaign.Current?.Models?.TradeAgreementModel;
            var agreements = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (model == null || agreements == null) return null;
            if (realm.Towns.Count == 0 || target.Towns.Count == 0) return "trade_towns_lost";
            int Count(Kingdom k) => Kingdom.All.Count(other => other != k && !other.IsEliminated
                && agreements.HasTradeAgreement(k, other, out _));
            if (Count(target) >= model.GetMaximumTradeAgreementCount(target)) return "foreign_trade_slots_filled";
            // Choosing other partners at home remains a political failure, not a neutral cancellation.
            if (Count(realm) >= model.GetMaximumTradeAgreementCount(realm)) return null;
            return !model.CanMakeTradeAgreement(realm, target, false, out _)
                || !model.CanMakeTradeAgreement(target, realm, false, out _) ? "trade_connection_lost" : null;
        }

        private void PursueCourtTrade(CourtAgendaRecord a, double now)
        {
            if (!a.IsOngoingObjective || !a.Trade.Activated || a.Trade.ProposalAttempted
                || a.Trade.TechnicalAttempts >= 3 || now < a.Trade.NextAttemptDay) return;
            a.Trade.NextAttemptDay = now + 3;
            if (a.Realm.UnresolvedDecisions.OfType<TradeAgreementDecision>().Any(d => d.TargetKingdom == a.Trade.Target))
            { a.Trade.ProposalAttempted = true; a.Trade.ProposalOutcome = "existing_ballot"; return; }
            if (a.Realm.UnresolvedDecisions.OfType<TradeAgreementDecision>().Any())
            { TradeBlocked(a, "other_trade_ballot_pending"); return; }
            if (!CourtTradeObjectiveSource.Legal(a.Realm, a.Trade.Target, true))
            { TradeBlocked(a, "native_or_foreign_acceptance_unavailable"); return; }
            var sponsor = CourtTradeObjectiveSource.ProspectiveSponsor(a.Realm, a.Trade.Target, a.Faction, active: true);
            if (sponsor == null) { TradeBlocked(a, "no_funded_willing_participant"); return; }
            var native = Campaign.Current.GetCampaignBehavior<KingdomDecisionProposalBehavior>();
            if (native == null || ConsiderCourtTrade == null) { TradeBlocked(a, "proposal_service_unavailable"); return; }
            int paid = 0;
            TradeAgreementDecision decision = null;
            try
            {
                if (!(bool)ConsiderCourtTrade.Invoke(native, new object[] { sponsor, a.Realm, a.Trade.Target }))
                { TradeBlocked(a, "native_council_forecast_opposed"); return; }
                decision = new TradeAgreementDecision(sponsor, a.Trade.Target);
                if (decision.TriggerTime.ToDays > a.ObjectiveData.DeadlineDay)
                { TradeBlocked(a, "insufficient_time_for_ballot"); return; }
                if (!decision.IsAllowed() || !decision.CanMakeDecision(out _))
                { TradeBlocked(a, "decision_not_allowed"); return; }
                int cost = decision.GetInfluenceCost(sponsor);
                if (!NpcInfluenceBudgetService.TrySpend(sponsor, cost, NpcInfluenceExpenseKind.Discretionary, "court_trade_proposal"))
                { TradeBlocked(a, "proposal_unaffordable"); return; }
                paid = cost;
                a.Trade.ProposalOutcome = null;
                a.Trade.TechnicalAttempts++;
                IdeologyBehavior.AddDecisionAsModAction(a.Realm, decision, ignoreInfluenceCost: true);
                if (a.Realm.UnresolvedDecisions.Contains(decision) || a.Trade.ProposalOutcome != null || a.ResultApplied)
                {
                    a.Trade.ProposalAttempted = true;
                    paid = 0;
                    BellumCivileLogger.Log($"Court trade proposal submitted; realm={a.Realm.StringId}; target={a.Trade.Target.StringId}; sponsor={sponsor.StringId}; day={now:0.0}; vote_day={decision.TriggerTime.ToDays:0.0}; cost={cost}.");
                }
                else TradeBlocked(a, "decision_not_registered");
            }
            catch (Exception ex)
            {
                if (paid == 0) a.Trade.TechnicalAttempts++;
                // An event subscriber may throw after the native decision was already registered.
                if (decision != null && (a.Realm.UnresolvedDecisions.Contains(decision) || a.Trade.ProposalOutcome != null || a.ResultApplied))
                { a.Trade.ProposalAttempted = true; paid = 0; }
                BellumCivileLogger.Log($"Court trade proposal exception; realm={a.Realm.StringId}; target={a.Trade.Target.StringId}; error={ex}");
            }
            finally
            {
                if (paid > 0) NpcInfluenceBudgetService.Refund(sponsor, paid, NpcInfluenceExpenseKind.Discretionary, "court_trade_registration_failed");
            }
        }

        private void OnCourtTradeDecisionAdded(KingdomDecision decision, bool playerInvolved)
        {
            if (!(decision is TradeAgreementDecision trade)) return;
            foreach (var a in _agendas.Where(a => TradeOpen(a) && a.Realm == trade.Kingdom && a.Trade.Target == trade.TargetKingdom))
            { a.Trade.ProposalAttempted = true; a.Trade.ProposalOutcome = "ballot_pending"; }
        }

        private void OnCourtTradeDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool playerInvolved)
        {
            if (!(decision is TradeAgreementDecision trade)) return;
            foreach (var a in _agendas.Where(a => TradeOpen(a) && a.Realm == trade.Kingdom && a.Trade.Target == trade.TargetKingdom))
            {
                a.Trade.ProposalAttempted = true;
                a.Trade.ProposalOutcome = outcome is TradeAgreementDecision.TradeAgreementDecisionOutcome result && result.ShouldTradeAgreementStart
                    ? "ballot_approved" : "ballot_rejected";
                BellumCivileLogger.Log($"Court trade ballot concluded; realm={a.Realm.StringId}; target={a.Trade.Target.StringId}; outcome={a.Trade.ProposalOutcome}.");
            }
        }
    }
}
