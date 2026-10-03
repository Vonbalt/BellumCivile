using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsActivity(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtActivityRules.Kind;

        private static TextObject ActivityObjectiveText(CourtAgendaRecord agenda)
        {
            var definition = CourtActivityCatalog.Find(agenda.ObjectiveData.TargetId);
            var label = definition?.AgendaText ?? new TextObject("{=BC_CourtActivityUnknown}Faction undertaking");
            var targets = agenda.Activity?.Targets;
            if (targets?.Count == 1 && targets[0].Settlement != null)
                return new TextObject("{=BC_CourtActivityAt}{ACTIVITY}: {TARGET}").SetTextVariable("ACTIVITY", label)
                    .SetTextVariable("TARGET", targets[0].Settlement.Name);
            return label;
        }

        private static TextObject ActivityStatus(CourtAgendaRecord agenda)
        {
            if (agenda.State == CourtAgendaState.Announced)
                return new TextObject("{=BC_CourtActivityDate}Scheduled for {DATE}").SetTextVariable("DATE", agenda.SessionDate.ToString());
            if (agenda.State == CourtAgendaState.Completed) return new TextObject("{=BC_CourtActivityCompleted}Completed.");
            if (agenda.State == CourtAgendaState.Withdrawn) return new TextObject("{=BC_CourtActivityWithdrawn}Withdrawn; the undertaking can no longer proceed as promised.");
            return StatusLabel(agenda.State);
        }

        internal TextObject ActivityHint(Kingdom realm, FactionObject faction)
        {
            var agenda = GetDisplayedAgenda(realm, faction);
            if (!IsActivity(agenda) || agenda.State == CourtAgendaState.Crisis
                || agenda.State == CourtAgendaState.AwaitingNomination || agenda.State == CourtAgendaState.NominationExpired
                || agenda.Activity?.Targets == null) return null;
            return new TextObject(agenda.Activity.Positive
                ? "{=BC_CourtActivityPositiveHint}Goodwill toward the Crown prompted this undertaking.\nIntended recipients: {TARGETS}\nScheduled for {DATE}."
                : "{=BC_CourtActivityNegativeHint}Discontent with the Crown prompted this undertaking.\nIntended recipients: {TARGETS}\nScheduled for {DATE}.")
                .SetTextVariable("TARGETS", string.Join(", ", agenda.Activity.Targets.Select(t => t.Name)))
                .SetTextVariable("DATE", agenda.SessionDate.ToString());
        }

        private void AdvanceActivity(CourtAgendaRecord agenda)
        {
            if (!agenda.SessionDate.IsPast || agenda.ResultApplied) return;
            var plan = agenda.Activity;
            if (!CourtSessionEvents.ValidPlan(plan, agenda.Faction, agenda.Realm))
            { FinishActivity(agenda, false, "activity_mood_ruler_or_plan_changed"); return; }
            CourtSessionEvents.Execute(plan, agenda.Faction, agenda.Realm);
            bool changed = plan.Targets.Any(t => t.Completed && t.Changed);
            bool interrupted = plan.Targets.Any(t => t.Started && !t.Completed);
            FinishActivity(agenda, changed, interrupted ? "activity_partial_execution" : changed ? "activity_completed" : "activity_no_effective_targets");
            CourtSessionEvents.Report(plan, agenda.Faction, agenda.Realm);
        }

        private void FinishActivity(CourtAgendaRecord agenda, bool changed, string reason)
        {
            if (agenda.ResultApplied) return;
            agenda.State = changed ? CourtAgendaState.Completed : CourtAgendaState.Withdrawn;
            agenda.ResultApplied = agenda.PaymentSettled = agenda.EventApplied = true;
            agenda.ObjectiveData.Finish(changed ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled,
                changed ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason);
            agenda.ObjectiveData.TryClaimResult();
            BellumCivileLogger.Log($"Court activity concluded; realm={agenda.Realm.StringId}; faction={agenda.Faction.Type}; event={agenda.ObjectiveData.TargetId}; state={agenda.State}; reason={reason}.");
            if (!changed) NotifyAgenda(agenda);
        }
    }
}
