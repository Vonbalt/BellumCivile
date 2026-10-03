using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsSubjugationObjective(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtSubjugationRules.Kind;
        private static TextObject SubjugationLabel(Kingdom target) => new TextObject("{=BC_CourtSeekClientage}Seek the submission of {REALM}")
            .SetTextVariable("REALM", target?.Name ?? TextObject.GetEmpty());

        private CourtAgendaRecord ActiveSubjugation(Kingdom realm, Kingdom target) => _agendas.FirstOrDefault(a =>
            IsSubjugationObjective(a) && a.Realm == realm && a.IsOngoingObjective && !a.ResultApplied
            && a.ObjectiveData.HasTermSnapshot && CampaignOwnerValid(a) && a.Subjugation != null
            && a.Subjugation.ActiveBonus(target, CampaignTime.Now.ToDays, a.ObjectiveData.DeadlineDay) != 0
            && CourtSubjugationObjectiveSource.ValidPair(realm, target));

        internal float SubjugationWarBonus(Kingdom realm, Kingdom target, Clan voter)
        {
            var agenda = ActiveSubjugation(realm, target);
            return agenda != null && ReceivesPoliticalSupport(agenda, voter, agenda.Subjugation.Members)
                && WarPeaceRevampBehavior.CanSupportCourtCampaign(realm, target) ? CourtSubjugationRules.SupportBonus : 0;
        }

        private static CourtTreatyDraftPreference SubjugationPreference(CourtAgendaRecord agenda) =>
            new CourtTreatyDraftPreference(TreatyTermType.MakeClientKingdom, agenda.Subjugation.Target.StringId,
                agenda.Realm.StringId, CourtSubjugationRules.ConsiderationBonus, CourtSubjugationRules.PackageBonus,
                "Crown supports the court's clientage objective");

        internal float SubjugationTreatyBonus(Kingdom realm, WarScoreRecord war, Clan voter, IEnumerable<TreatyTermRecord> terms)
        {
            if (realm == null || war == null || terms == null) return 0;
            var target = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetOpposingKingdom(war, realm);
            if (!CourtPeaceObjectiveSource.EligibleWar(realm, target, war)) return 0;
            var agenda = ActiveSubjugation(realm, target);
            return agenda != null && ReceivesPoliticalSupport(agenda, voter, agenda.Subjugation.Members)
                && SubjugationPreference(agenda).Score(terms) != 0 ? CourtSubjugationRules.SupportBonus : 0;
        }

        internal CourtTreatyDraftPreference CrownTreatyPreference(Kingdom drafter, Kingdom target, WarScoreRecord war)
        {
            if (drafter?.RulingClan == null || drafter.RulingClan == Clan.PlayerClan
                || !CourtPeaceObjectiveSource.EligibleWar(drafter, target, war)) return null;
            var agenda = ActiveSubjugation(drafter, target);
            return agenda != null && ReceivesPoliticalSupport(agenda, drafter.RulingClan, agenda.Subjugation.Members)
                ? SubjugationPreference(agenda) : CrownClaimPreference(drafter, target);
        }

        private bool RecoverSubjugationResult(CourtAgendaRecord agenda)
        {
            var client = ClientKingdomBehavior.Instance?.GetClientRecord(agenda.Subjugation?.Target);
            if (client == null || !CourtSubjugationRules.Fulfilled(client.ClientKingdomId, client.SuzerainKingdomId,
                agenda.Subjugation?.Target?.StringId, agenda.Realm?.StringId, client.StartedDay,
                agenda.ObjectiveData.SelectedDay, agenda.ObjectiveData.DeadlineDay)) return false;
            FinishSubjugationObjective(agenda, CourtObjectiveState.Succeeded, "clientage_established");
            return true;
        }

        internal void OnCourtClientageEstablished(Kingdom client, Kingdom suzerain)
        {
            foreach (var agenda in _agendas.Where(a => IsSubjugationObjective(a) && !a.ResultApplied
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)
                && a.Realm == suzerain && a.Subjugation?.Target == client).ToList())
                if (CampaignOwnerValid(agenda) && agenda.ObjectiveData.HasTermSnapshot) RecoverSubjugationResult(agenda);
        }

        private void MaintainSubjugationObjectives()
        {
            foreach (var agenda in _agendas.Where(a => IsSubjugationObjective(a) && !a.ResultApplied
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)).ToList())
            {
                if (!CampaignOwnerValid(agenda) || agenda.Subjugation == null || !agenda.ObjectiveData.HasTermSnapshot)
                { FinishSubjugationObjective(agenda, CourtObjectiveState.Cancelled, "owner_or_plan_invalid"); continue; }
                // Establishment changes client eligibility; recognize its dated receipt before invalidation/expiry.
                if (RecoverSubjugationResult(agenda)) continue;
                if (!CourtSubjugationObjectiveSource.ValidPair(agenda.Realm, agenda.Subjugation.Target))
                { FinishSubjugationObjective(agenda, CourtObjectiveState.Cancelled, "clientage_no_longer_possible"); continue; }
                if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
                { FinishSubjugationObjective(agenda, CourtObjectiveState.Expired, "term_expired"); continue; }
                agenda.Subjugation.Members?.RemoveAll(c => !Eligible(c, agenda.Realm) || !agenda.Faction.Members.Contains(c));
            }
        }

        private void AdvanceSubjugationObjective(CourtAgendaRecord agenda)
        {
            if (agenda.ResultApplied || agenda.IsOngoingObjective || !agenda.SessionDate.IsPast) return;
            if (!CampaignOwnerValid(agenda) || agenda.Subjugation == null || !agenda.ObjectiveData.HasTermSnapshot)
            { FinishSubjugationObjective(agenda, CourtObjectiveState.Cancelled, "unavailable_at_session"); return; }
            if (RecoverSubjugationResult(agenda)) return;
            if (!CourtSubjugationObjectiveSource.ValidPair(agenda.Realm, agenda.Subjugation.Target))
            { FinishSubjugationObjective(agenda, CourtObjectiveState.Cancelled, "unavailable_at_session"); return; }
            if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
            { FinishSubjugationObjective(agenda, CourtObjectiveState.Expired, "term_expired"); return; }
            var plan = agenda.Subjugation;
            plan.Members = agenda.Faction.Members.Where(c => Eligible(c, agenda.Realm) && c != agenda.Realm.RulingClan).Distinct().ToList();
            plan.ActivatedDay = CampaignTime.Now.ToDays;
            plan.Activated = true;
            agenda.ObjectiveData.Activate();
            agenda.State = CourtAgendaState.PursuingObjective;
            ReportSubjugation(agenda, new TextObject("{=BC_CourtClientageBegun}The {FACTION} of {REALM} calls for {TARGET} to acknowledge the Crown's overlordship. Its houses pledge their voices to this ambition, expecting submission before the term ends on {DATE}. War and the terms of peace remain matters for the realm to decide."));
            BellumCivileLogger.Log($"Court clientage initiative active; realm={agenda.Realm.StringId}; target={plan.Target.StringId}; members={plan.Members.Count}; deadline={agenda.ObjectiveData.DeadlineDay}.");
        }

        private static void ReportSubjugation(CourtAgendaRecord agenda, TextObject text, CourtObjectiveState? result = null, float actualMoodChange = 0)
        {
            text.SetTextVariable("FACTION", agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
                .SetTextVariable("REALM", agenda.Realm?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("TARGET", agenda.Subjugation?.Target?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString());
            if (result.HasValue)
                text = CourtObjectiveReports.WithMood(text, agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), actualMoodChange, result.Value);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: agenda.Realm);
        }

        private void FinishSubjugationObjective(CourtAgendaRecord agenda, CourtObjectiveState result, string reason)
        {
            if (agenda.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded, failure = result == CourtObjectiveState.Expired;
            var credit = success ? (agenda.Subjugation?.Activated == true ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.FulfilledElsewhere)
                : CourtObjectiveCredit.None;
            if (!agenda.ObjectiveData.Finish(result, credit, reason) || !agenda.ObjectiveData.TryClaimResult()) return;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.State = success ? CourtAgendaState.Completed : failure ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failure ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actualMoodChange = 0;
            if (CampaignOwnerValid(agenda) && shock != 0)
            {
                float before = agenda.Faction.Mood;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, agenda.Faction.Mood + shock));
                actualMoodChange = agenda.Faction.Mood - before;
                RecordResultHistory(agenda, shock);
            }
            ReportSubjugation(agenda, new TextObject(success
                ? "{=BC_CourtClientageSucceeded}{TARGET} has acknowledged the overlordship of {REALM}. The lords of the {FACTION} praise the Crown for fulfilling their ambition, their confidence in its leadership renewed."
                : failure ? "{=BC_CourtClientageFailed}The court term draws to a close, yet {TARGET} has not submitted to {REALM}. The lords of the {FACTION} voice their displeasure at court, reproaching the Crown for leaving their ambition unfulfilled."
                : "{=BC_CourtClientageCancelled}Changed circumstances have overtaken the {FACTION}'s ambition to bring {TARGET} under the Crown's protection. The undertaking is set aside without blame."), result, actualMoodChange);
            BellumCivileLogger.Log($"Court clientage initiative concluded; realm={agenda.Realm?.StringId}; target={agenda.ObjectiveData.TargetId}; result={result}; credit={credit}; mood={shock}; actual_mood={actualMoodChange}; reason={reason}.");
        }

        internal TextObject SubjugationHint(Kingdom realm, FactionObject faction)
        {
            var agenda = GetDisplayedAgenda(realm, faction);
            if (!IsSubjugationObjective(agenda) || agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis
                || agenda.State == CourtAgendaState.AwaitingNomination || agenda.State == CourtAgendaState.NominationExpired) return null;
            return SubjugationStatus(agenda);
        }

        private static TextObject SubjugationStatus(CourtAgendaRecord agenda) => new TextObject(agenda.IsOngoingObjective
            ? "{=BC_CourtClientageStatus}Until {DATE}, participating houses and a Crown favoring this faction receive +15 support for a lawful war and for treaties granting the named clientage. An aligned NPC ruler also prefers that demand when drafting peace. Actual clientage brings +10 mood; an unfulfilled term brings -10. Your choices, treaty costs and truces remain unchanged."
            : agenda.State == CourtAgendaState.Announced ? "{=BC_CourtClientageScheduled}The faction will take up its ambition on {SESSION}, seeking submission by {DATE}."
            : agenda.State == CourtAgendaState.Completed ? "{=BC_CourtClientageComplete}The named realm entered the Crown's clientage within the term."
            : agenda.ObjectiveData.State == CourtObjectiveState.Expired ? "{=BC_CourtClientageExpired}The term ended without the intended submission."
            : "{=BC_CourtClientageClosed}This undertaking is no longer being pursued.")
            .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString())
            .SetTextVariable("SESSION", agenda.SessionDate.ToString());
    }
}
