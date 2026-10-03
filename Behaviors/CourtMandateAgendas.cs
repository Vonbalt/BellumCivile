using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, CampaignTime> _mandateSettledUntil = new Dictionary<string, CampaignTime>();
        private void ClearMandatePledges(Clan clan, Hero speaker)
        {
            foreach (var a in _agendas.Where(IsMandate))
                a.Mandate?.Pledges?.RemoveAll(p => clan != null && p.Clan == clan || speaker != null && p.Speaker == speaker);
        }
        internal static bool IsMandate(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtMandateRules.Kind;
        internal static bool MandateRealmReady(Kingdom realm) => ValidRealm(realm) && !realm.Leader.IsDead
            && Campaign.Current?.GetCampaignBehavior<SuccessionLawBehavior>()?.ResolvePermanentRealm(realm) == realm
            && ElectiveSuccessionBehavior.UsesElection(realm) && CrownAccessionBehavior.Instance?.IsPending(realm) != true
            && ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) == null
            && ElectiveSuccessionBehavior.Instance?.Get(realm)?.ReformElectionPending != true
            && !realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any();
        internal bool HasMandateReservation(Kingdom realm, CourtAgendaRecord except = null) =>
            (_mandateSettledUntil.TryGetValue(realm.StringId, out var until) && until.IsFuture && except?.IsFiled != true)
            || _agendas.Any(a => a != except && a.Realm == realm && IsMandate(a)
                && (a.IsFiled || a.IsUnopened && a.State != CourtAgendaState.Crisis))
            || realm.UnresolvedDecisions.OfType<MandateReformDecision>().Any(d => d.MotionId != except?.Mandate?.Id);
        internal CourtAgendaRecord MandateAgenda(string id) => string.IsNullOrEmpty(id) ? null
            : _agendas.FirstOrDefault(a => IsMandate(a) && a.Mandate?.Id == id);
        internal CourtAgendaRecord PendingMandate(Kingdom realm) => _agendas.FirstOrDefault(a => a.Realm == realm && IsMandate(a)
            && a.State == CourtAgendaState.Deliberating && a.VoteDate.IsFuture && MandateRealmReady(realm) && MandateLawUnchanged(a));
        internal static TextObject MandateLabel(string law) => new TextObject("{=BC_MandateAgenda}Reform elective mandates: {LAW}")
            .SetTextVariable("LAW", RealmLawRegistry.Instance.Find(law)?.Name ?? TextObject.GetEmpty());
        private static bool MandateLawUnchanged(CourtAgendaRecord a)
        {
            var old = RealmLawRegistry.Instance.Find(a?.Mandate?.OldLaw);
            var next = RealmLawRegistry.Instance.Find(a?.Mandate?.NewLaw);
            return old?.MandateYears != null && next?.MandateYears != null
                && CourtMandateRules.Next(old.MandateYears.Value, a.Mandate.Direction) == next.MandateYears
                && CourtMandateObjectiveSource.Laws?.GetActiveLawId(a.Realm, RealmLawRegistry.TermGroup) == old.Id;
        }
        internal bool ValidateMandateDecision(MandateReformDecision d)
        {
            var a = MandateAgenda(d?.MotionId);
            return a?.IsFiled == true && !a.ResultApplied && a.Mandate.Decision == d && a.Realm == d.Kingdom
                && a.Sponsor == d.ProposerClan && Eligible(a.Sponsor, a.Realm) && a.Faction?.ParentKingdom == a.Realm
                && a.Faction.IsIdeology && a.Mandate.OldLaw == d.OldLaw && a.Mandate.NewLaw == d.NewLaw
                && a.Mandate.Direction == d.Direction && d.Direction == CourtMandateObjectiveSource.Direction(a.Faction)
                && d.SponsorFaction == a.Faction.Type
                && CampaignTime.Now.ToDays <= a.VoteDate.ToDays + 14 && MandateRealmReady(a.Realm) && MandateLawUnchanged(a);
        }
        internal CourtMandatePledge MandatePledge(string id, Clan clan)
        {
            var a = MandateAgenda(id);
            if (a?.IsFiled != true || !MandateRealmReady(a.Realm) || !MandateLawUnchanged(a) || !Eligible(clan, a.Realm)) return null;
            return a.Mandate.Pledges?.FirstOrDefault(p => p.Clan == clan && p.Speaker == clan.Leader && !p.Speaker.IsDead);
        }
        internal bool CanLobbyMandate(string id, Hero speaker)
        {
            var a = MandateAgenda(id);
            return a != null && PendingMandate(a.Realm) == a && Eligible(Clan.PlayerClan, a.Realm)
                && speaker != null && !speaker.IsDead && !speaker.IsPrisoner && speaker == speaker.Clan?.Leader
                && speaker != Hero.MainHero && Eligible(speaker.Clan, a.Realm)
                && !DeliberationDialogueHelper.IsConversationInArmy(speaker);
        }
        internal bool CommitMandate(string id, Hero speaker, bool reform, bool bribed, bool failed = false)
        {
            if (!CanLobbyMandate(id, speaker)) return false;
            var a = MandateAgenda(id);
            var pledge = MandatePledge(id, speaker.Clan);
            if (pledge?.Committed == true || !bribed && pledge?.FailedPersuasion == true) return false;
            if (pledge == null)
            {
                pledge = new CourtMandatePledge { Clan = speaker.Clan, Speaker = speaker };
                a.Mandate.Pledges.Add(pledge);
            }
            pledge.FailedPersuasion |= failed;
            if (!failed) { pledge.Reform = reform; pledge.Committed = true; pledge.Bribed = bribed; }
            return true;
        }
        private void AdvanceMandate(CourtAgendaRecord a)
        {
            if (a.Mandate == null) { Cancel(a, "mandate_record_missing"); return; }
            string current = CourtMandateObjectiveSource.Laws?.GetActiveLawId(a.Realm, RealmLawRegistry.TermGroup);
            if (current == a.Mandate.NewLaw)
            {
                ClearMandateQueue(a);
                a.State = CourtAgendaState.FulfilledElsewhere;
                a.ResultApplied = a.PaymentSettled = true;
                a.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.FulfilledElsewhere, "mandate_fulfilled_elsewhere");
                a.ObjectiveData.TryClaimResult();
                return;
            }
            if (!MandateLawUnchanged(a) || !ElectiveSuccessionBehavior.UsesElection(a.Realm))
            { Cancel(a, "mandate_law_changed"); return; }
            if (CampaignTime.Now.ToDays > a.VoteDate.ToDays + 14) { Cancel(a, "mandate_proceeding_expired"); return; }
            if (!MandateRealmReady(a.Realm))
            {
                if (a.State == CourtAgendaState.Voting) Cancel(a, "mandate_succession_pending");
                return;
            }
            if (a.IsUnopened)
            {
                if (!a.SessionDate.IsPast) return;
                if (HasMandateReservation(a.Realm, a)) { Cancel(a, "mandate_proceeding_conflict"); return; }
                var d = new MandateReformDecision(a.Sponsor, a.Mandate.OldLaw, a.Mandate.NewLaw, a.Mandate.Direction, a.Faction.Type)
                    { MotionId = a.Mandate.Id };
                int cost = d.GetProposalInfluenceCost();
                if (a.Sponsor != Clan.PlayerClan && !CourtPolicyForecast.Calculate(d, cost, d.Outcome(true), d.Outcome(false)).Viable
                    || !TryPay(a, cost))
                { FinishExecutive(a, CourtAgendaState.NotProposed, "mandate_unaffordable_or_unviable"); return; }
                a.State = CourtAgendaState.Deliberating;
                a.Mandate.Decision = d;
                a.ObjectiveData.Activate();
                _mandateSettledUntil[a.Realm.StringId] = CampaignTime.Days((float)a.ObjectiveData.DeadlineDay);
                BellumCivileNotifications.Show(new TextObject("{=BC_MandateDeliberation}The {FACTION} has placed {LAW} before the court of {REALM}. The houses will deliberate until {DATE}; the present ruler's mandate will not be altered.")
                    .SetTextVariable("FACTION", a.Faction.GetDisplayName()).SetTextVariable("LAW", RealmLawRegistry.Instance.Find(a.Mandate.NewLaw).Name)
                    .SetTextVariable("REALM", a.Realm.Name).SetTextVariable("DATE", a.VoteDate.ToString()), BellumNotificationColors.Politics, primaryKingdom: a.Realm);
            }
            if (a.State == CourtAgendaState.Deliberating && a.VoteDate.IsPast)
            {
                var d = a.Mandate.Decision;
                if (d == null || !ValidateMandateDecision(d)) { Cancel(a, "mandate_ballot_invalid"); return; }
                if (++a.ActionAttempts > 7) { Cancel(a, "add_decision_failed"); return; }
                try
                {
                    if (!a.Realm.UnresolvedDecisions.Contains(d)) IdeologyBehavior.AddDecisionAsModAction(a.Realm, d);
                    if (!a.ResultApplied && a.Realm.UnresolvedDecisions.Contains(d)) a.State = CourtAgendaState.Voting;
                }
                catch (Exception ex) { BellumCivileLogger.Log($"Mandate ballot queue failed; motion={a.Mandate.Id}; error={ex}."); }
            }
            else if (a.State == CourtAgendaState.Voting && !a.Realm.UnresolvedDecisions.Contains(a.Mandate.Decision))
                Cancel(a, "filed_motion_missing_from_queue_and_vote");
        }
        internal void CancelMandateDecision(MandateReformDecision d, string reason)
        { var a = MandateAgenda(d?.MotionId); if (a?.Mandate.Decision == d && a != null) Cancel(a, reason); }
        private static void ClearMandateQueue(CourtAgendaRecord a)
        {
            a.Mandate?.Pledges?.Clear();
            foreach (var d in a.Realm.UnresolvedDecisions.OfType<MandateReformDecision>().Where(d => d.MotionId == a.Mandate?.Id).ToList())
                a.Realm.RemoveDecision(d);
        }
        internal void ConcludeMandate(MandateReformDecision d, bool enacted)
        {
            var a = MandateAgenda(d.MotionId);
            if (a?.IsFiled != true || a.Mandate.Decision != d || a.ResultApplied) return;
            a.Mandate.Pledges.Clear();
            FinishExecutive(a, enacted ? CourtAgendaState.Passed : CourtAgendaState.Defeated, "mandate_vote_resolved");
            BellumCivileNotifications.Show(new TextObject(enacted
                ? "{=BC_MandateApproval}The {FACTION} welcomes the court's adoption of {LAW}. Its members applaud this settlement of the realm's future succession. Approval +10."
                : "{=BC_MandateReprisal}The court has rejected the {FACTION}'s proposal for {LAW}. Its members resent the defeat of their reform. Approval -10.")
                .SetTextVariable("FACTION", a.Faction.GetDisplayName()).SetTextVariable("LAW", RealmLawRegistry.Instance.Find(a.Mandate.NewLaw).Name),
                BellumNotificationColors.Politics, primaryKingdom: a.Realm);
        }
    }
}
