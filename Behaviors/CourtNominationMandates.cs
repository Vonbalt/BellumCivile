using System.Linq;
using System;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private const string NominationMotionKind = "player_nomination";

        private static TextObject NominationLabel(string kind) => new TextObject(kind == "policy"
            ? "{=BC_NominationPolicy}Propose a Policy" : "{=BC_NominationCouncil}Council Appointment");

        private void ShowNominationMandateConfirmation(CourtAgendaRecord agenda, string kind, bool crisis, int price, string category)
        {
            var identity = agenda.GetObjective();
            var state = agenda.State;
            var deadline = CampaignTime.Days((float)CourtAgendaRules.NominationCloses(
                CampaignTime.Now.ToDays, agenda.SessionDate.ToDays, agenda.NominationWindowDays));
            var body = new TextObject("{=BC_NominationOffer}Your peers will entrust this matter to your judgment. Bring one proposal before the court by {DATE}, or they will remember your unfulfilled promise.\n\n{ACTION}\n\nSubstitution costs {COST} influence. The usual proposal cost is paid only when you file; deliberation begins then.")
                .SetTextVariable("DATE", deadline.ToString()).SetTextVariable("COST", price)
                .SetTextVariable("ACTION", new TextObject(kind == "policy"
                    ? "{=BC_NominationPolicyAction}Choose a policy to enact or repeal in the kingdom's Policies tab."
                    : "{=BC_NominationCouncilAction}Choose an eligible office in the Privy Council to propose an appointment or replacement."));
            _activePlayerInquiry = agenda;
            InformationManager.ShowInquiry(new InquiryData(NominationLabel(kind).ToString(), body.ToString(), true, true,
                new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), new TextObject("{=BC_CourtPickerBack}Back").ToString(), () =>
                {
                    if (_activePlayerInquiry != agenda) return;
                    _activePlayerInquiry = null;
                    if (!CanChooseTermBusiness(agenda) || agenda.ObjectiveData != identity || agenda.State != state
                        || crisis != agenda.CrisisInterventionPending || price != ReplacementCost(agenda, crisis)
                        || !deadline.IsFuture || !AvailablePlayerMotions(agenda, kind).Any()) return;
                    if (!NpcInfluenceBudgetService.TrySpend(Clan.PlayerClan, price,
                        crisis ? NpcInfluenceExpenseKind.CrownEmergency : NpcInfluenceExpenseKind.Discretionary, "court_term_substitute")) return;
                    // A fresh record releases all previous objective-specific reservations and effects.
                    var mandate = new CourtAgendaRecord { Realm = agenda.Realm, Faction = agenda.Faction, Sponsor = Clan.PlayerClan,
                        State = CourtAgendaState.AwaitingNomination, NominationKind = kind, NominationDeadline = deadline,
                        NominationWindowDays = agenda.NominationWindowDays, SessionDate = agenda.SessionDate, VoteDate = agenda.VoteDate,
                        HasScheduleSnapshot = agenda.HasScheduleSnapshot, TermDays = agenda.TermDays, PlayerSelectionConfirmed = true,
                        SubstitutionInfluencePaid = price, OrdinaryCrisisRestrained = agenda.OrdinaryCrisisRestrained || crisis,
                        ObjectiveData = new CourtObjectiveRecord { Kind = "policy" } };
                    if (identity.HasTermSnapshot) mandate.ObjectiveData.FreezeTerm(identity.SelectedDay, identity.DeadlineDay);
                    _agendas[_agendas.IndexOf(agenda)] = mandate;
                    if (crisis) ApplyMemberMemory(mandate, BellumCivileConstants.CourtAgendaDeclineRelationPenalty,
                        RelationMemorySources.OverruledMyFaction, 7f);
                    NotifyAgenda(mandate);
                    BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_NominationGranted}The {FACTION} entrusts you with a mandate: {MOTION}. Your peers expect a proposal before {DATE}.")
                        .SetTextVariable("FACTION", AgendaOwnerName(mandate)).SetTextVariable("MOTION", NominationLabel(kind))
                        .SetTextVariable("DATE", deadline.ToString()), BellumNotificationColors.Politics);
                }, () => ReturnFromMotionPicker(agenda, identity, state, crisis, category)), true);
        }

        private bool TryCouncilMandateNomination(Kingdom realm, PrivyCouncilOffice office, out TextObject reason, bool readOnly, Action refresh)
        {
            reason = new TextObject("{=BC_CouncilMandateRequired}A council appointment mandate from your faction is required. It must still be within its nomination deadline.");
            if (!IsMandateOpen(realm, CourtCouncilObjectiveSource.CouncilKind)) return false;
            var mandate = GetPlayerTermAgenda(realm);
            var motions = AvailablePlayerMotions(mandate, CourtCouncilObjectiveSource.CouncilKind)
                .Where(m => m.Candidate.TargetId == office.ToString()).ToList();
            if (motions.Count == 0)
            { reason = new TextObject("{=BC_CrownCouncilUnavailable}No eligible replacement is available, or another council appointment is already under deliberation."); return false; }
            int cost = CourtCouncilObjectiveSource.Cost(Clan.PlayerClan);
            reason = new TextObject("{=BC_CouncilMandateFile}Use your council mandate to propose this appointment. Costs {COST} influence and begins a {DAYS}-day deliberation. Your mandate expires {DATE}.")
                .SetTextVariable("COST", cost).SetTextVariable("DAYS", BellumCivileOptions.PoliticalDeliberationDays)
                .SetTextVariable("DATE", mandate.NominationDeadline.ToString());
            if (Clan.PlayerClan.Influence < cost) return false;
            if (readOnly) return true;
            string hint = reason.ToString();
            var choices = motions.Select(m => new InquiryElement(m, m.Label.ToString(), null, true, hint)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(NominationLabel(CourtCouncilObjectiveSource.CouncilKind).ToString(),
                reason.ToString(), choices, true, 1, 1,
                new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), new TextObject("{=BC_CourtAgenda_SubCancel}Cancel").ToString(),
                selected =>
                {
                    if (selected?.Count == 1 && !FileMandateMotion(mandate, (PlayerMotion)selected[0].Identifier))
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_NominationNotFiled}The proposal could not be placed before the court. If your mandate remains valid, you may choose another proposal before its deadline."), BellumNotificationColors.Warning);
                    refresh?.Invoke();
                }, _ => refresh?.Invoke()), true);
            return true;
        }

        private bool FileMandateMotion(CourtAgendaRecord mandate, PlayerMotion motion)
        {
            if (mandate == null || GetPlayerTermAgenda(mandate.Realm) != mandate || !IsMandateOpen(mandate.Realm, motion.Kind)) return false;
            var draft = new CourtAgendaRecord { Realm = mandate.Realm, Faction = mandate.Faction, Sponsor = Clan.PlayerClan,
                State = CourtAgendaState.Announced, PlayerSelectionConfirmed = true, TermDays = mandate.TermDays,
                HasScheduleSnapshot = true, OrdinaryCrisisRestrained = mandate.OrdinaryCrisisRestrained,
                ObjectiveData = new CourtObjectiveRecord { Kind = "policy" } };
            if (motion.Kind == "policy")
            {
                var policy = PolicyObject.All.FirstOrDefault(p => p.StringId == motion.Candidate.TargetId);
                bool repeal = motion.Candidate.ActionId == "repeal";
                if (policy == null || !CanPropose(draft.Realm, policy) || draft.Realm.ActivePolicies.Contains(policy) != repeal
                    || PolicyDeliberationBehavior.Current == null || PolicyDeliberationBehavior.Current.HasPlayerProposedVote(draft.Realm)) return false;
                draft.PolicyId = policy.StringId; draft.Abolish = repeal;
                draft.CapturePolicyStance();
                int cost = FilingCost(new KingdomPolicyDecision(Clan.PlayerClan, policy, repeal), draft.Faction);
                if (!TryPay(draft, cost)) return false;
            }
            else
            {
                var current = AvailablePlayerMotions(mandate, motion.Kind).FirstOrDefault(m => SamePlayerMotion(m, motion));
                if (current == null || current.FilingCost != motion.FilingCost) return false;
                ((ICourtAgendaObjectiveSource)_objectiveSelector.FindSource(motion.Kind)).ApplySelection(draft,
                    new CourtObjectiveChoice(motion.Kind, current.Candidate, new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "player_mandate")));
            }
            var term = mandate.GetObjective();
            if (term.HasTermSnapshot) draft.GetObjective().FreezeTerm(term.SelectedDay, term.DeadlineDay);
            draft.SessionDate = CampaignTime.Now - CampaignTime.Days(.001f);
            draft.VoteDate = CampaignTime.Now + CampaignTime.Days(BellumCivileOptions.PoliticalDeliberationDays);
            _agendas.Add(draft);
            if (motion.Kind == "policy")
            {
                draft.State = CourtAgendaState.Deliberating;
                if (!PolicyDeliberationBehavior.Current.QueuePolicyVote(draft.Realm, Policy(draft), draft.Abolish, draft.Faction, draft.Sponsor, draft.VoteDate))
                    Cancel(draft, "queue_rejected_after_payment");
            }
            else AdvanceCouncil(draft);
            if (!draft.IsFiled)
            {
                _agendas.Remove(draft);
                return false;
            }
            draft.SubstitutionInfluencePaid = mandate.SubstitutionInfluencePaid;
            _agendas.Remove(mandate);
            NotifyAgenda(draft);
            return true;
        }
    }
}
