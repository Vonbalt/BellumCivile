using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum CourtObjectiveState { Selected, Active, Succeeded, Failed, Expired, Cancelled }
    public enum CourtObjectiveCredit { None, Sponsor, FulfilledElsewhere }

    public sealed class CourtObjectiveRecord
    {
        [SaveableField(1)] public string Kind;
        [SaveableField(2)] public string TargetId;
        [SaveableField(3)] public string ActionId;
        [SaveableField(4)] public double SelectedDay;
        [SaveableField(5)] public double DeadlineDay;
        [SaveableField(6)] public bool HasTermSnapshot;
        [SaveableField(7)] public CourtObjectiveState State;
        [SaveableField(8)] public CourtObjectiveCredit Credit;
        [SaveableField(9)] public string ResultReason;
        [SaveableField(10)] public bool ResultClaimed;

        public bool IsTerminal => State != CourtObjectiveState.Selected && State != CourtObjectiveState.Active;

        internal void FreezeTerm(double selectedDay, double deadlineDay)
        {
            if (HasTermSnapshot) return;
            if (double.IsNaN(selectedDay) || double.IsInfinity(selectedDay)
                || double.IsNaN(deadlineDay) || double.IsInfinity(deadlineDay) || deadlineDay < selectedDay)
                throw new ArgumentOutOfRangeException(nameof(deadlineDay));
            SelectedDay = selectedDay;
            DeadlineDay = deadlineDay;
            HasTermSnapshot = true;
        }

        internal void Activate()
        {
            if (State == CourtObjectiveState.Selected) State = CourtObjectiveState.Active;
        }

        internal bool Finish(CourtObjectiveState state, CourtObjectiveCredit credit, string reason)
        {
            if (!Enum.IsDefined(typeof(CourtObjectiveState), state)
                || state == CourtObjectiveState.Selected || state == CourtObjectiveState.Active)
                throw new ArgumentException("A terminal outcome is required.", nameof(state));
            if (IsTerminal) return false;
            State = state;
            Credit = credit;
            ResultReason = reason;
            return true;
        }

        internal bool TryClaimResult()
        {
            if (!IsTerminal || ResultClaimed) return false;
            ResultClaimed = true;
            return true;
        }
    }

    internal sealed class CourtObjectiveOutcome
    {
        internal CourtObjectiveState State { get; }
        internal CourtObjectiveCredit Credit { get; }
        internal string Reason { get; }
        internal CourtObjectiveOutcome(CourtObjectiveState state, CourtObjectiveCredit credit, string reason)
        { State = state; Credit = credit; Reason = reason; }
    }

    internal interface ICourtObjectiveProgress<TEvent>
    {
        CourtObjectiveOutcome EvaluateProgress(CourtObjectiveRecord objective, TEvent relevantEvent);
        CourtObjectiveOutcome EvaluateDeadline(CourtObjectiveRecord objective);
    }

    internal static class CourtObjectiveLifecycle
    {
        internal static bool OnEvent<TEvent>(CourtObjectiveRecord objective, ICourtObjectiveProgress<TEvent> handler,
            TEvent relevantEvent, double eventDay)
        {
            if (double.IsNaN(eventDay) || double.IsInfinity(eventDay) || objective.IsTerminal || (objective.HasTermSnapshot
                && (eventDay < objective.SelectedDay || eventDay > objective.DeadlineDay))) return false;
            objective.Activate();
            return Apply(objective, handler.EvaluateProgress(objective, relevantEvent));
        }

        internal static bool OnDeadline<TEvent>(CourtObjectiveRecord objective, ICourtObjectiveProgress<TEvent> handler,
            double now, bool hasFiledMotion)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || objective.IsTerminal
                || !objective.HasTermSnapshot || now <= objective.DeadlineDay || hasFiledMotion) return false;
            return Apply(objective, handler.EvaluateDeadline(objective)
                ?? new CourtObjectiveOutcome(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "term_expired"));
        }

        // A linked, already-filed proceeding may resolve after its original term.
        // Its handler decides whether passage fulfills the objective or merely advances it.
        internal static bool OnMotionResult<TEvent>(CourtObjectiveRecord objective, ICourtObjectiveProgress<TEvent> handler, TEvent result)
        {
            if (objective.State != CourtObjectiveState.Active) return false;
            return Apply(objective, handler.EvaluateProgress(objective, result));
        }

        private static bool Apply(CourtObjectiveRecord objective, CourtObjectiveOutcome outcome) => outcome != null
            && objective.Finish(outcome.State, outcome.Credit, outcome.Reason);
    }
}
