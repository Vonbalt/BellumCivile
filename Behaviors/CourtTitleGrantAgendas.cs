using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtTitleGrantRecord> _titleDeliveries = new List<CourtTitleGrantRecord>();
        private bool _processingTitleGrants;
        private static bool IsTitleGrant(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtTitleGrantRules.Grant || a?.ObjectiveData?.Kind == CourtTitleGrantRules.Petition;
        private static bool TitleOpen(CourtAgendaRecord a) => IsTitleGrant(a) && !a.ResultApplied && a.TitleGrant != null
            && a.ObjectiveData.HasTermSnapshot && (a.IsUnopened || a.IsOngoingObjective);
        private static bool TitleAnnounced(CourtAgendaRecord a) => TitleOpen(a) && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective);
        internal bool TitleFactionFavored(Kingdom realm, FactionType type) => GetFavoredBloc(realm) == type;
        internal bool ConflictingTitleMotion(Kingdom realm, string title, Clan recipient, CourtAgendaRecord excluded) => _agendas.Any(a => a != excluded
            && TitleOpen(a) && a.Realm == realm && a.TitleGrant.TitleId == title && a.TitleGrant.Recipient != recipient);
        private static bool GrantIdentity(CourtTitleGrantRecord p) => ValidRealm(p.Realm) && p.Grantor?.Leader == p.Ruler && p.Ruler?.IsAlive == true
            && p.Realm.RulingClan == p.Grantor && p.Recipient?.Leader == p.Beneficiary && p.Beneficiary?.IsAlive == true && Eligible(p.Recipient, p.Realm);
        private static bool GrantOwner(CourtAgendaRecord a) => a.Faction == null || a.Faction.Type == FactionType.Nobility
            && a.Faction.ParentKingdom == a.Realm && a.Faction.Members.Contains(a.TitleGrant.Recipient)
            && Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(a.Realm).Contains(a.Faction) == true;
        private static bool GrantDelivered(CourtTitleGrantRecord p)
        {
            var title = CourtTitleGrantObjectiveSource.Title(p.TitleId);
            return title?.IsActive == true && CourtTitleGrantRules.Delivered(p, title.DeJureHolderClanId, title.DeFactoHolderClanId);
        }
        private static bool GrantUnchanged(CourtTitleGrantRecord p)
        {
            var title = CourtTitleGrantObjectiveSource.Title(p.TitleId);
            return title?.DeJureHolderClanId == p.OldLegal && title.DeFactoHolderClanId == p.OldPractical
                && CourtTitleGrantObjectiveSource.Eligible(p.Realm, title, p.Recipient);
        }
        private static TextObject TitleGrantLabel(string titleId, Clan recipient, bool petition) => new TextObject(petition
            ? "{=BC_TitleGrantPetitionLabel}Petition for {TITLE} for {RECIPIENT}" : "{=BC_TitleGrantLabel}Bestow {TITLE} on {RECIPIENT}")
            .SetTextVariable("TITLE", CourtTitleGrantObjectiveSource.Title(titleId)?.Name ?? titleId)
            .SetTextVariable("RECIPIENT", recipient?.Name ?? TextObject.GetEmpty());

        private void AdvanceTitleGrant(CourtAgendaRecord a)
        {
            if (a.ResultApplied || a.IsOngoingObjective || !a.SessionDate.IsPast) return;
            var p = a.TitleGrant;
            if (p == null || !GrantIdentity(p) || !GrantOwner(a) || !GrantUnchanged(p))
            { FinishTitleAgenda(a, CourtObjectiveState.Cancelled, "invalid_at_session"); return; }
            a.State = CourtAgendaState.PursuingObjective; a.ObjectiveData.Activate();
            p.Deadline = a.ObjectiveData.DeadlineDay;
            bool petition = a.Faction != null;
            if (petition) ReportTitle(p, "{=BC_TitleGrantPetitionBegun}The nobility of {REALM} petitions the Crown to bestow {TITLE} upon {RECIPIENT}, citing the house's lawful claim. They ask that their appeal be honored before {DATE}.");
            if (petition && p.Grantor == Clan.PlayerClan) p.Response = CourtTitleResponse.AwaitingPlayer;
            else
            {
                p.Acceptance = CourtTitleGrantObjectiveSource.Acceptance(p.Realm, CourtTitleGrantObjectiveSource.Title(p.TitleId), p.Recipient);
                if (p.Grantor == Clan.PlayerClan || p.Acceptance >= 60) AcceptTitleGrant(a);
                else
                {
                    p.Response = CourtTitleResponse.Refused;
                    if (petition) ReportTitle(p, "{=BC_TitleGrantRefused}{RULER} has declined the petition to grant {TITLE} to {RECIPIENT}. The nobility's appeal remains unfulfilled.");
                    else FinishTitleAgenda(a, CourtObjectiveState.Cancelled, "crown_reconsidered");
                }
                BellumCivileLogger.Log($"Court title willingness; title={p.TitleId}; score={p.Acceptance}; response={p.Response}.");
            }
        }
        private void AcceptTitleGrant(CourtAgendaRecord a)
        {
            var p = a.TitleGrant;
            var existing = _titleDeliveries.FirstOrDefault(x => x.Realm == p.Realm && x.TitleId == p.TitleId && x.Recipient == p.Recipient
                && x.Response == CourtTitleResponse.Accepted);
            if (existing != null) { a.TitleGrant = existing; return; }
            p.Response = CourtTitleResponse.Accepted;
            if (!_titleDeliveries.Contains(p)) _titleDeliveries.Add(p);
        }
        private void MaintainTitleGrants()
        {
            if (_processingTitleGrants) return;
            _processingTitleGrants = true;
            try
            {
                foreach (var p in _titleDeliveries.Where(x => x.Response == CourtTitleResponse.Accepted).ToList()) ExecuteTitleDelivery(p);
                foreach (var a in _agendas.Where(TitleAnnounced).ToList())
                {
                    var p = a.TitleGrant;
                    if (p.Response == CourtTitleResponse.Accepted && p.PaymentAttempted) continue;
                    if (!GrantIdentity(p) || !GrantOwner(a)) { FinishTitleAgenda(a, CourtObjectiveState.Cancelled, "identity_changed"); continue; }
                    if (GrantDelivered(p))
                    { FinishTitleAgenda(a, CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay ? CourtObjectiveState.Succeeded : CourtObjectiveState.Expired, "grant_observed_elsewhere"); continue; }
                    if (!GrantUnchanged(p)) { FinishTitleAgenda(a, CourtObjectiveState.Cancelled, "rights_or_claim_changed"); continue; }
                    if (CampaignTime.Now.ToDays > a.ObjectiveData.DeadlineDay)
                        FinishTitleAgenda(a, a.Faction != null ? CourtObjectiveState.Expired : CourtObjectiveState.Cancelled, "term_expired");
                }
                _titleDeliveries.RemoveAll(p => p.Response != CourtTitleResponse.Accepted && CampaignTime.Now.ToDays > p.Deadline);
            }
            finally { _processingTitleGrants = false; }
        }
        private void ExecuteTitleDelivery(CourtTitleGrantRecord p)
        {
            double now = CampaignTime.Now.ToDays;
            if (now < p.RetryDay) return;
            if (!GrantIdentity(p) || !p.PaymentAttempted && !_agendas.Any(a => TitleAnnounced(a) && a.TitleGrant == p && GrantOwner(a)))
            { FailTitleDelivery(p, "grant_authority_changed"); return; }
            if (!p.PaymentAttempted && GrantDelivered(p)) { CompleteTitleAgendas(p, "external_grant"); return; }
            if (p.PaymentAttempted && (p.Attempts >= 3 || now > p.RecoveryUntil) || !p.PaymentAttempted && now > p.Deadline)
            { FailTitleDelivery(p, "delivery_window_ended"); return; }
            try
            {
                if (!p.PaymentAttempted)
                {
                    if (!GrantUnchanged(p)) { FailTitleDelivery(p, "grant_changed"); return; }
                    if (!NpcInfluenceBudgetService.CanAfford(p.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary)) return;
                    p.PaymentAttempted = true; p.RecoveryUntil = now + 1;
                    p.Paid = NpcInfluenceBudgetService.TrySpend(p.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary, "court_title_grant");
                }
                if (!p.Paid) { FailTitleDelivery(p, "payment_not_confirmed"); return; }
                p.Attempts++; p.RetryDay = now + .25;
                if (!GrantIdentity(p)) { FailTitleDelivery(p, "identity_changed_after_payment"); return; }
                p.TransferAttempted = true;
                if (!CourtTitleGrantObjectiveSource.Titles.CompleteCourtTitleGrant(p, out string reason))
                { FailTitleDelivery(p, reason); return; }
                p.TransferComplete = true;
                if (!p.RelationApplied)
                {
                    p.RelationApplied = true;
                    RelationMemoryService.ApplyChange(p.Ruler, p.Beneficiary, p.RelationGain, true, RelationMemorySources.CourtTitleGrant, 10,
                        RelationMemoryScope.Personal, CourtTitleGrantObjectiveSource.Title(p.TitleId).Name);
                }
                if (!p.MoodApplied)
                {
                    p.MoodApplied = true;
                    p.RewardFaction = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(p.Recipient);
                    if (p.RewardFaction?.ParentKingdom == p.Realm && p.RewardFaction.IsIdeology)
                    {
                        float before = p.RewardFaction.Mood;
                        p.RewardFaction.Mood = Math.Max(-100, Math.Min(100, p.RewardFaction.Mood + p.RelationGain * .5f));
                        p.ApprovalGranted = p.RewardFaction.Mood - before;
                    }
                }
                CompleteTitleAgendas(p, "court_grant_delivered");
                if (!p.Reported)
                {
                    p.Reported = true;
                    ReportTitleSuccess(p);
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Court title delivery interrupted; title=" + p.TitleId + "; " + ex);
                if (!p.Paid || p.Attempts >= 3) FailTitleDelivery(p, "delivery_interrupted");
            }
        }
        private void FailTitleDelivery(CourtTitleGrantRecord p, string reason)
        {
            p.Response = CourtTitleResponse.Failed;
            var title = CourtTitleGrantObjectiveSource.Title(p.TitleId);
            if (p.Paid && !p.Refunded && title?.DeJureHolderClanId == p.OldLegal && title.DeFactoHolderClanId == p.OldPractical)
            {
                p.Refunded = true;
                NpcInfluenceBudgetService.Refund(p.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary, "court_title_grant_failed");
            }
            foreach (var a in _agendas.Where(a => TitleOpen(a) && a.TitleGrant == p).ToList())
                FinishTitleAgenda(a, !p.PaymentAttempted && reason == "delivery_window_ended" && a.Faction != null
                    ? CourtObjectiveState.Expired : CourtObjectiveState.Cancelled, reason);
            if (p.PaymentAttempted && !p.Reported)
            {
                p.Reported = true;
                ReportTitle(p, "{=BC_TitleGrantInterrupted}The grant of {TITLE} to {RECIPIENT} could not be fully carried out. Any rights already transferred remain in place; no further transfer will be attempted under this authorization.");
            }
        }
        private void CompleteTitleAgendas(CourtTitleGrantRecord p, string reason)
        {
            p.Response = CourtTitleResponse.Delivered;
            foreach (var a in _agendas.Where(a => TitleAnnounced(a) && a.Realm == p.Realm && a.TitleGrant.TitleId == p.TitleId
                && a.TitleGrant.Recipient == p.Recipient && GrantOwner(a) && GrantIdentity(a.TitleGrant)).ToList())
                if (CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay || a.TitleGrant == p && p.TransferComplete)
                    FinishTitleAgenda(a, CourtObjectiveState.Succeeded, reason);
        }
        internal void ObserveHierarchyTitleGrant(Clan grantor, FeudalTitleRecord title, Clan recipient)
        {
            foreach (var a in _agendas.Where(a => TitleAnnounced(a) && a.TitleGrant.Grantor == grantor && a.TitleGrant.TitleId == title.TitleId
                && a.TitleGrant.Recipient == recipient && GrantIdentity(a.TitleGrant) && GrantOwner(a) && GrantDelivered(a.TitleGrant)).ToList())
                if (CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay)
                {
                    if (!a.TitleGrant.PaymentAttempted) a.TitleGrant.Response = CourtTitleResponse.Delivered;
                    FinishTitleAgenda(a, CourtObjectiveState.Succeeded, "hierarchy_grant_already_rewarded");
                }
        }
        private void FinishTitleAgenda(CourtAgendaRecord a, CourtObjectiveState state, string reason)
        {
            if (a.ResultApplied) return;
            bool success = state == CourtObjectiveState.Succeeded;
            if (!a.ObjectiveData.Finish(state, success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason) || !a.ObjectiveData.TryClaimResult()) return;
            a.ResultApplied = a.PaymentSettled = true;
            a.State = success ? CourtAgendaState.Completed : state == CourtObjectiveState.Expired ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            if (state == CourtObjectiveState.Expired && a.Faction != null && GrantOwner(a))
            {
                float before = a.Faction.Mood;
                a.Faction.Mood = Math.Max(-100, before + BellumCivileConstants.CourtAgendaFailureShock);
                RecordResultHistory(a, BellumCivileConstants.CourtAgendaFailureShock);
                var text = TitleText(a.TitleGrant, new TextObject("{=BC_TitleGrantExpired}The term has ended without the grant of {TITLE} to {RECIPIENT}. The nobility reproaches the Crown for leaving its petition unanswered."));
                BellumCivileNotifications.Show(CourtObjectiveReports.WithMood(text, a.Faction.GetDisplayName(), a.Faction.Mood - before, state), CourtObjectiveReports.Color(state), primaryKingdom: a.Realm);
            }
            BellumCivileLogger.Log($"Court title agenda concluded; title={a.TitleGrant?.TitleId}; state={state}; reason={reason}.");
        }
        private static TextObject TitleText(CourtTitleGrantRecord p, TextObject text) => text
            .SetTextVariable("TITLE", CourtTitleGrantObjectiveSource.Title(p.TitleId)?.Name ?? p.TitleId)
            .SetTextVariable("RECIPIENT", p.Recipient?.Name ?? TextObject.GetEmpty()).SetTextVariable("RULER", p.Ruler?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("REALM", p.Realm?.Name ?? TextObject.GetEmpty()).SetTextVariable("DATE", CampaignTime.Days((float)p.Deadline).ToString());
        private static void ReportTitle(CourtTitleGrantRecord p, string text) => BellumCivileNotifications.Show(TitleText(p, new TextObject(text)), BellumNotificationColors.Politics, primaryKingdom: p.Realm);
        private static void ReportTitleSuccess(CourtTitleGrantRecord p)
        {
            var text = TitleText(p, new TextObject("{=BC_TitleGrantDelivered}{RULER} has bestowed {TITLE} upon {RECIPIENT}. The house remains a vassal of {REALM}; its claim has been honored by royal grant."));
            if (p.RewardFaction != null)
                text = CourtObjectiveReports.WithMood(text, p.RewardFaction.GetDisplayName(), p.ApprovalGranted, CourtObjectiveState.Succeeded);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(CourtObjectiveState.Succeeded), primaryKingdom: p.Realm);
        }
        private static TextObject TitleGrantStatus(CourtAgendaRecord a) => TitleText(a.TitleGrant, new TextObject(a.ResultApplied
            ? "{=BC_TitleGrantClosed}This title motion has concluded."
            : "{=BC_TitleGrantStatus}Named grant: {TITLE} to {RECIPIENT}. Deadline: {DATE}. Royal authorization and 100 influence are required. The recipient remains a vassal; subordinate settlements are not granted. An unfulfilled Nobility petition brings -10 approval at term end."));
        internal TextObject TitleGrantHint(Kingdom realm, FactionObject faction)
        { var a = GetDisplayedAgenda(realm, faction); return IsTitleGrant(a) && a.TitleGrant != null ? TitleGrantStatus(a) : null; }
    }
}
