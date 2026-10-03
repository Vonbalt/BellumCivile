using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsTrade(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtTradeRules.Kind;
        private static TextObject TradeLabel(Kingdom target) => new TextObject("{=BC_CourtTradeLabel}Seek a trade agreement with {TARGET}")
            .SetTextVariable("TARGET", target?.Name ?? TextObject.GetEmpty());
        private static bool TradeOwner(CourtAgendaRecord a) => ValidRealm(a.Realm) && a.Faction?.Type == FactionType.Liberty
            && a.Faction.ParentKingdom == a.Realm
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(a.Realm).Contains(a.Faction) == true;
        private static bool TradeOpen(CourtAgendaRecord a) => IsTrade(a) && !a.ResultApplied && a.Trade != null
            && a.ObjectiveData.HasTermSnapshot && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective);
        private static bool TradeExists(CourtAgendaRecord a) => Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>()?
            .HasTradeAgreement(a.Realm, a.Trade.Target, out _) == true;

        internal float TradeBonus(Kingdom realm, Kingdom target, Clan voter)
        {
            if (voter == null || voter == Clan.PlayerClan || !CourtTradeObjectiveSource.Pair(realm, target)) return 0;
            return _agendas.Any(a => TradeOpen(a) && a.IsOngoingObjective && a.Realm == realm && a.Trade.Target == target
                && a.Trade.Activated && TradeOwner(a)
                && CourtTradeRules.InWindow(CampaignTime.Now.ToDays, a.Trade.ActivatedDay, a.ObjectiveData.DeadlineDay)
                && ReceivesPoliticalSupport(a, voter, a.Trade.Members)) ? CourtTradeRules.SupportBonus : 0;
        }

        internal Kingdom PreferredTradeTarget(Clan clan) => clan == null || clan == Clan.PlayerClan ? null
            : _agendas.Where(a => TradeOpen(a) && !a.Trade.ProposalAttempted && a.Realm == clan.Kingdom && TradeBonus(a.Realm, a.Trade.Target, clan) != 0)
                .OrderBy(a => a.ObjectiveData.DeadlineDay).Select(a => a.Trade.Target).FirstOrDefault();

        private void OnCourtTradeSigned(Kingdom first, Kingdom second)
        {
            foreach (var a in _agendas.Where(a => TradeOpen(a) && (a.Realm == first && a.Trade.Target == second
                || a.Realm == second && a.Trade.Target == first)).ToList())
            {
                if (!TradeOwner(a) || !CourtTradeObjectiveSource.Pair(a.Realm, a.Trade.Target) || !TradeExists(a)) continue;
                double now = CampaignTime.Now.ToDays;
                if (!CourtTradeRules.InWindow(now, a.ObjectiveData.SelectedDay, a.ObjectiveData.DeadlineDay)) continue;
                a.Trade.SignedDay = now;
                FinishTrade(a, CourtObjectiveState.Succeeded, "trade_signed");
            }
        }

        private void MaintainTradeObjectives()
        {
            foreach (var a in _agendas.Where(TradeOpen).ToList())
            {
                if (!TradeOwner(a) || !CourtTradeObjectiveSource.Pair(a.Realm, a.Trade.Target))
                { FinishTrade(a, CourtObjectiveState.Cancelled, "realm_or_relationship_changed"); continue; }
                double now = CampaignTime.Now.ToDays;
                // Never backdate an unseen signature using the agreement's expiry or current duration model.
                bool exists = TradeExists(a);
                if (exists && CourtTradeRules.InWindow(a.Trade.SignedDay, a.ObjectiveData.SelectedDay, a.ObjectiveData.DeadlineDay))
                { FinishTrade(a, CourtObjectiveState.Succeeded, "saved_signature_recovered"); continue; }
                if (exists && CourtTradeRules.InWindow(now, a.ObjectiveData.SelectedDay, a.ObjectiveData.DeadlineDay))
                { a.Trade.SignedDay = now; FinishTrade(a, CourtObjectiveState.Succeeded, "active_trade_observed_in_term"); continue; }
                if (now > a.ObjectiveData.DeadlineDay)
                { FinishTrade(a, CourtObjectiveState.Expired, "term_expired"); continue; }
                if (now >= a.Trade.NextReviewDay)
                {
                    a.Trade.NextReviewDay = now + 1;
                    string invalidation = TradeInvalidation(a);
                    if (invalidation != null)
                    { FinishTrade(a, CourtObjectiveState.Cancelled, invalidation); continue; }
                }
                a.Trade.Members.RemoveAll(c => !Eligible(c, a.Realm) || !a.Faction.Members.Contains(c));
                PursueCourtTrade(a, now);
            }
        }

        private void OnCourtTradeWar(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            foreach (var a in _agendas.Where(a => TradeOpen(a) && (a.Realm == first && a.Trade.Target == second
                || a.Realm == second && a.Trade.Target == first)).ToList())
            {
                bool deliberate = detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision
                    || detail == DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility;
                var result = CourtTradeRules.WarResult(a.Realm == first, deliberate, CampaignTime.Now.ToDays, a.ObjectiveData.DeadlineDay);
                FinishTrade(a, result, result == CourtObjectiveState.Failed ? "own_offensive" : "war_overtook_trade");
            }
        }

        private void AdvanceTrade(CourtAgendaRecord a)
        {
            if (a.ResultApplied || a.IsOngoingObjective || !a.SessionDate.IsPast) return;
            MaintainTradeObjectives();
            if (a.ResultApplied) return;
            if (a.Trade == null || !TradeOwner(a) || !CourtTradeObjectiveSource.Pair(a.Realm, a.Trade.Target))
            { FinishTrade(a, CourtObjectiveState.Cancelled, "unavailable_at_session"); return; }
            a.Trade.Members = a.Faction.Members.Where(c => Eligible(c, a.Realm) && c != a.Realm.RulingClan).Distinct().ToList();
            a.Trade.Activated = true;
            a.Trade.ActivatedDay = CampaignTime.Now.ToDays;
            a.ObjectiveData.Activate();
            a.State = CourtAgendaState.PursuingObjective;
            ReportTrade(a, new TextObject("{=BC_CourtTradeBegun}The {FACTION} of {REALM} calls for closer trade with {TARGET}. Its lords pledge their support to an accord that would bring merchants of both realms to one another's markets. They ask that it be secured before the court term ends on {DATE}."));
        }

        private static TextObject TradeText(CourtAgendaRecord a, TextObject text) => text
            .SetTextVariable("FACTION", a.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
            .SetTextVariable("REALM", a.Realm?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("TARGET", a.Trade?.Target?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("DATE", CampaignTime.Days((float)a.ObjectiveData.DeadlineDay).ToString());
        private static void ReportTrade(CourtAgendaRecord a, TextObject text, CourtObjectiveState? result = null, float mood = 0)
        {
            text = TradeText(a, text);
            if (result.HasValue) text = CourtObjectiveReports.WithMood(text, a.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), mood, result.Value);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: a.Realm);
        }
        private void FinishTrade(CourtAgendaRecord a, CourtObjectiveState result, string reason)
        {
            if (a.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded;
            bool failed = result == CourtObjectiveState.Failed || result == CourtObjectiveState.Expired;
            var credit = success ? (a.Trade?.Activated == true ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.FulfilledElsewhere) : CourtObjectiveCredit.None;
            if (!a.ObjectiveData.Finish(result, credit, reason) || !a.ObjectiveData.TryClaimResult()) return;
            a.ResultApplied = a.PaymentSettled = true;
            a.State = success ? CourtAgendaState.Completed : failed ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            if (failed && a.Realm != null && a.Trade?.Target != null)
                _tradeRepeatUntil[a.Realm.StringId + "|" + a.Trade.Target.StringId] = CampaignTime.Now.ToDays + 2 * Math.Max(1, a.TermDays);
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failed ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actual = 0;
            if (TradeOwner(a) && shock != 0)
            {
                float before = a.Faction.Mood;
                a.Faction.Mood = Math.Max(-100, Math.Min(100, before + shock));
                actual = a.Faction.Mood - before;
                RecordResultHistory(a, shock);
            }
            ReportTrade(a, new TextObject(success
                ? "{=BC_CourtTradeSucceeded}An agreement has opened the way for closer trade between {REALM} and {TARGET}. The {FACTION} welcomes the accord as a promise fulfilled."
                : result == CourtObjectiveState.Failed ? "{=BC_CourtTradeOffensive}War with {TARGET} has swept aside the promised trade accord. The {FACTION} condemns the abandonment of its appeal."
                : failed ? "{=BC_CourtTradeExpired}The court term has ended without the hoped-for trade accord with {TARGET}. The {FACTION} reproaches the Crown for leaving its appeal unanswered."
                : "{=BC_CourtTradeCancelled}Changed circumstances have put the proposed trade accord with {TARGET} beyond reach. The {FACTION} sets aside its appeal without reproach."), result, actual);
            BellumCivileLogger.Log($"Court trade objective concluded; realm={a.Realm?.StringId}; target={a.Trade?.Target?.StringId}; result={result}; reason={reason}; actual_mood={actual}.");
        }
        private static TextObject TradeStatus(CourtAgendaRecord a) => TradeText(a, new TextObject(a.ResultApplied
            ? "{=BC_CourtTradeClosed}This trade initiative has ended."
            : "{=BC_CourtTradeStatus}Seek an agreement with {TARGET} by {DATE}. Participating houses and an NPC Crown favoring Liberty receive +15 trade support. Normal proposal costs, foreign acceptance and your vote remain unchanged. Signing brings +10 approval in addition to standing trade benefits; expiry brings -10."));
        internal TextObject TradeHint(Kingdom realm, FactionObject faction)
        {
            var a = GetDisplayedAgenda(realm, faction);
            return IsTrade(a) && !a.CrisisInterventionPending && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective || a.ResultApplied) ? TradeStatus(a) : null;
        }
    }
}
