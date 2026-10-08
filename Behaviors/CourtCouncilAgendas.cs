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
        private Dictionary<string, CampaignTime> _councilSettledUntil = new Dictionary<string, CampaignTime>();
        private Dictionary<string, float> _councilContestedDay = new Dictionary<string, float>();
        private static bool IsCouncil(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtCouncilObjectiveSource.CouncilKind;
        private static string CouncilKey(Kingdom realm, PrivyCouncilOffice office) => realm.StringId + "|" + office;
        private static bool CouncilOffice(CourtAgendaRecord agenda, out PrivyCouncilOffice office) =>
            Enum.TryParse(agenda?.ObjectiveData?.TargetId, out office) && Enum.IsDefined(typeof(PrivyCouncilOffice), office);

        // Refund old paid motions in their original budget category; never create new emergencies.
        private static NpcInfluenceExpenseKind LegacyCouncilExpense(CourtAgendaRecord agenda) =>
            IsCouncil(agenda) && agenda.Faction == null && agenda.Sponsor != Clan.PlayerClan
            && agenda.Sponsor == agenda.Realm?.RulingClan && CouncilOffice(agenda, out var office)
            && Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?.GetOfficeRecord(agenda.Realm, office)?.IsCaptivityVacancy == true
                ? NpcInfluenceExpenseKind.CrownEmergency : NpcInfluenceExpenseKind.Discretionary;

        internal bool HasCouncilReservation(Kingdom realm, CourtAgendaRecord except = null) => _agendas.Any(a =>
            a != except && a.Realm == realm && IsCouncil(a) && (a.IsFiled || !IsDirectCrownBusiness(except) && a.IsUnopened)
            && a.State != CourtAgendaState.Crisis && a.State != CourtAgendaState.AwaitingNomination);

        internal bool IsCouncilOfficeSettled(Kingdom realm, PrivyCouncilOffice office)
        {
            if (realm == null) return false;
            string key = CouncilKey(realm, office);
            if (!_councilSettledUntil.TryGetValue(key, out var until) || !until.IsFuture) return false;
            var record = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?.GetOfficeRecord(realm, office);
            // Old versions stamped a settlement before the ballot. A still-vacant
            // office without a live proceeding must not inherit that cooldown.
            if (record != null && string.IsNullOrEmpty(record.HolderClanId)
                && !_agendas.Any(a => a.Realm == realm && IsCouncil(a) && a.IsFiled
                    && CouncilOffice(a, out var pendingOffice) && pendingOffice == office)
                && CouncilAppointmentDeliberationBehavior.Current?.HasPendingAppointment(realm, office) != true
                && !realm.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any(d => d.Office == office))
            {
                _councilSettledUntil.Remove(key);
                _councilContestedDay.Remove(key);
                return false;
            }
            // A vacancy created after the last contest is new business, not another attack on that incumbent.
            return record == null || !string.IsNullOrEmpty(record.HolderClanId)
                || !_councilContestedDay.TryGetValue(key, out float day) || record.VacancyStartedDay <= day;
        }

        internal void RecordCouncilOfficeSettled(Kingdom realm, PrivyCouncilOffice office)
        {
            string key = CouncilKey(realm, office);
            _councilSettledUntil[key] = NextEvaluation(realm);
            _councilContestedDay[key] = (float)CampaignTime.Now.ToDays;
        }

        private static TextObject CouncilObjectiveText(CourtAgendaRecord agenda)
        {
            var text = new TextObject("{=BC_CourtCouncilCandidateObjective}Appoint {CANDIDATE} as {OFFICE}");
            text.SetTextVariable("CANDIDATE", agenda.PreferredCouncilCandidate?.Leader?.Name
                ?? agenda.PreferredCouncilCandidate?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("OFFICE", CouncilOffice(agenda, out var office)
                ? PrivyCouncilBehavior.GetLocalizedOfficeName(office, agenda.Realm) : TextObject.GetEmpty());
            return text;
        }

        private bool CouncilTargetValid(CourtAgendaRecord agenda, out PrivyCouncilOffice office)
        {
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            return CouncilOffice(agenda, out office) && council != null
                && Eligible(agenda.PreferredCouncilCandidate, agenda.Realm)
                && council.IsOfficeUnlocked(agenda.Realm, office)
                && council.GetOfficeHolder(agenda.Realm, office) == agenda.OriginalHolder
                && council.GetAppointmentCandidatesForVote(agenda.Realm, office).Contains(agenda.PreferredCouncilCandidate);
        }

        internal bool ValidateCouncilProceeding(string motionId, Kingdom realm, PrivyCouncilOffice office, Clan sponsor)
            => CouncilProceedingReason(motionId, realm, office, sponsor) == null;

        internal Clan GetCouncilProceedingNominee(string motionId, Kingdom realm, PrivyCouncilOffice office, Clan sponsor)
        {
            if (string.IsNullOrEmpty(motionId)) return null;
            return _agendas.FirstOrDefault(a => IsCouncil(a) && a.IsFiled && a.CouncilMotionId == motionId
                && a.Realm == realm && a.Sponsor == sponsor && CouncilOffice(a, out var target) && target == office)
                ?.PreferredCouncilCandidate;
        }

        internal string CouncilProceedingReason(string motionId, Kingdom realm, PrivyCouncilOffice office, Clan sponsor)
        {
            var agenda = _agendas.FirstOrDefault(a => IsCouncil(a) && a.CouncilMotionId == motionId && a.Realm == realm && a.IsFiled);
            if (agenda == null) return "council_agenda_missing";
            if (!ValidRealm(realm)) return "kingdom_missing";
            if (!Eligible(sponsor, realm) || agenda.Sponsor != sponsor
                || !(agenda.Faction == null ? realm.RulingClan == sponsor : agenda.Faction.ParentKingdom == realm && agenda.Faction.IsIdeology))
                return "council_sponsor_changed";
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null) return "council_service_unavailable";
            if (!CouncilOffice(agenda, out var currentOffice) || currentOffice != office || !council.IsOfficeUnlocked(realm, office))
                return "council_office_unavailable";
            if (council.GetOfficeHolder(realm, office) != agenda.OriginalHolder) return "council_holder_changed";
            var candidates = council.GetAppointmentCandidatesForVote(realm, office);
            // NPC Crown vacancy business promises a filled office, not a particular house.
            if (agenda.Faction == null && sponsor != Clan.PlayerClan && agenda.OriginalHolder == null)
            {
                if (candidates.Count == 0) return "council_waiting_for_candidates";
                if (!candidates.Contains(agenda.PreferredCouncilCandidate))
                {
                    agenda.PreferredCouncilCandidate = candidates.OrderByDescending(c => council.CalculateAppointmentMerit(realm, c, office))
                        .ThenBy(c => c.StringId).First();
                    BellumCivileLogger.Log($"Crown vacancy nominee refreshed; realm={realm.StringId}; office={office}; nominee={agenda.PreferredCouncilCandidate.StringId}; motion={motionId}.");
                }
                return null;
            }
            return CouncilTargetValid(agenda, out _) ? null : "council_nominee_ineligible";
        }

        internal void WithdrawCouncilProceeding(string motionId, string reason)
        {
            var agenda = _agendas.FirstOrDefault(a => IsCouncil(a) && a.CouncilMotionId == motionId && a.IsFiled);
            if (agenda != null) Cancel(agenda, reason);
        }

        private void AdvanceCouncil(CourtAgendaRecord agenda)
        {
            if (!CouncilOffice(agenda, out var office)) { Cancel(agenda, "council_office_missing"); return; }
            var council = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            var deliberation = CouncilAppointmentDeliberationBehavior.Current;
            if (agenda.IsUnopened)
            {
                if (agenda.PreferredCouncilCandidate != null && council?.GetOfficeHolder(agenda.Realm, office) == agenda.PreferredCouncilCandidate)
                {
                    agenda.State = CourtAgendaState.FulfilledElsewhere;
                    agenda.ResultApplied = agenda.PaymentSettled = true;
                    agenda.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.FulfilledElsewhere, "preferred_house_already_appointed");
                    agenda.ObjectiveData.TryClaimResult();
                    return;
                }
                if (!agenda.SessionDate.IsPast) return;
                if (!CouncilTargetValid(agenda, out office) || !IsDirectCrownBusiness(agenda) && IsCouncilOfficeSettled(agenda.Realm, office))
                { Cancel(agenda, "council_target_changed_before_filing"); return; }
                if (HasCouncilReservation(agenda.Realm, agenda) || deliberation?.HasPendingAppointment(agenda.Realm) == true
                    || agenda.Realm.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any())
                { Cancel(agenda, "council_proceeding_conflict"); return; }
                var source = new CourtCouncilObjectiveSource();
                var evaluation = source.EvaluateCandidate(new CourtTermContext(agenda.Realm, _ => false, agenda),
                    new CourtObjectiveOwner(agenda.Faction, agenda.Sponsor), new CourtObjectiveCandidate(
                        agenda.ObjectiveData.TargetId, agenda.ObjectiveData.ActionId, agenda.PreferredCouncilCandidate.StringId));
                if (!evaluation.Eligible || agenda.Sponsor != Clan.PlayerClan && !evaluation.Viable)
                { FinishExecutive(agenda, CourtAgendaState.NotProposed, evaluation.Reason); return; }
                if (deliberation == null || !TryPay(agenda, CourtCouncilObjectiveSource.Cost(agenda.Sponsor)))
                { FinishExecutive(agenda, CourtAgendaState.NotProposed, "council_unaffordable_or_unavailable"); return; }
                agenda.State = CourtAgendaState.Deliberating;
                agenda.ObjectiveData.Activate();
                if (!deliberation.TryQueueAgendaAppointment(agenda.Realm, office, agenda.Sponsor, agenda.Faction,
                    agenda.VoteDate, agenda.CouncilMotionId, out _))
                { Cancel(agenda, "queue_rejected_after_payment"); return; }
                return;
            }
            if (agenda.Realm.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any(d => d.CourtAgendaId == agenda.CouncilMotionId))
            { agenda.State = CourtAgendaState.Voting; return; }
            if (deliberation?.HasAgendaAppointment(agenda.Realm, office, agenda.CouncilMotionId) != true)
                Cancel(agenda, "filed_motion_missing_from_queue_and_vote");
        }

        internal void ConcludeCouncilAppointment(PrivyCouncilAppointmentDecision decision)
        {
            if (string.IsNullOrEmpty(decision.CourtAgendaId)) return;
            var agenda = _agendas.FirstOrDefault(a => IsCouncil(a) && a.IsFiled && a.CouncilMotionId == decision.CourtAgendaId
                && a.Realm == decision.Kingdom && a.Sponsor == decision.ProposerClan);
            if (agenda == null) return;
            var council = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (decision.AppliedCandidate == null || council?.GetOfficeHolder(agenda.Realm, decision.Office) != decision.AppliedCandidate)
            { Cancel(agenda, "council_appointment_unverified"); return; }
            // Appointment reactions are already applied by the decision. Do not add the generic agenda mood shock again.
            agenda.State = decision.AppliedCandidate == agenda.PreferredCouncilCandidate ? CourtAgendaState.Passed : CourtAgendaState.Defeated;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.ObjectiveData.Finish(agenda.State == CourtAgendaState.Passed ? CourtObjectiveState.Succeeded : CourtObjectiveState.Failed,
                agenda.State == CourtAgendaState.Passed ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, "verified_council_appointment");
            agenda.ObjectiveData.TryClaimResult();
            BellumCivileLogger.Log($"Court council objective resolved; realm={agenda.Realm.StringId}; office={decision.Office}; preferred={agenda.PreferredCouncilCandidate?.StringId}; appointed={decision.AppliedCandidate.StringId}; result={agenda.State}; motion={agenda.CouncilMotionId}.");
        }

        internal bool TryNominateCouncilAppointment(Kingdom realm, PrivyCouncilOffice office, out TextObject reason, bool readOnly = false, Action refresh = null)
        {
            if (realm?.RulingClan == Clan.PlayerClan)
                return TryPlayerCouncilBusiness(realm, office, out reason, readOnly);
            return TryCouncilMandateNomination(realm, office, out reason, readOnly, refresh);
        }

        internal void MaintainCouncilVacancyPriority(Kingdom realm)
        {
            if (realm?.RulingClan == Clan.PlayerClan) return;
            if (!ValidRealm(realm) || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || HasCouncilReservation(realm) || CouncilAppointmentDeliberationBehavior.Current?.HasPendingAppointment(realm) == true) return;
            var agenda = GetAgenda(realm, null);
            if (agenda == null || IsRoyalPeace(agenda) || agenda.IsFiled || agenda.ResultApplied || !agenda.SessionDate.IsFuture
                || agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree
                || !(agenda.IsUnopened || agenda.State == CourtAgendaState.NotProposed)) return;
            if (!NpcInfluenceBudgetService.CanAfford(realm.RulingClan, CourtCouncilObjectiveSource.Cost(realm.RulingClan), NpcInfluenceExpenseKind.Discretionary)) return;
            var council = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null || !council.GetOfficeRecords(realm).Any(r => r.Office <= PrivyCouncilOffice.Spymaster
                && string.IsNullOrEmpty(r.HolderClanId) && council.GetVacancyDays(r) >= PrivyCouncilBehavior.EmergencyVacancyDays
                && !council.IsVacancyExcused(realm, r.Office))) return;
            var context = new CourtTermContext(realm, _ => false);
            var facts = context.Council;
            var source = new CourtCouncilObjectiveSource();
            var owner = new CourtObjectiveOwner(null, realm.RulingClan);
            foreach (var candidate in source.FindCandidates(context, owner))
            {
                var office = (PrivyCouncilOffice)Enum.Parse(typeof(PrivyCouncilOffice), candidate.TargetId);
                if (office > PrivyCouncilOffice.Spymaster || candidate.ActionId != "fill"
                    || facts.Council.GetVacancyDays(facts.Council.GetOfficeRecord(realm, office)) < PrivyCouncilBehavior.EmergencyVacancyDays) continue;
                var evaluation = source.EvaluateCandidate(context, owner, candidate);
                if (!evaluation.Selectable) continue;
                source.ApplySelection(agenda, new CourtObjectiveChoice(source.Kind, candidate, evaluation));
                agenda.State = realm.RulingClan == Clan.PlayerClan ? CourtAgendaState.AwaitingPlayerDecision : CourtAgendaState.Announced;
                NotifyAgenda(agenda);
                break;
            }
        }
    }
}
