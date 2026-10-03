namespace BellumCivile
{
    // Policy motion state remains authoritative during migration, including player substitution
    // and legacy saves. Other objective kinds use CourtObjectiveLifecycle directly.
    internal static class CourtPolicyObjectiveBridge
    {
        internal static CourtObjectiveRecord Refresh(CourtAgendaRecord agenda)
        {
            var objective = agenda.ObjectiveData;
            if (objective != null && objective.Kind != "policy") return objective;
            if (objective == null)
            {
                objective = new CourtObjectiveRecord { Kind = "policy" };
                agenda.ObjectiveData = objective;
            }
            objective.TargetId = agenda.PolicyId;
            objective.ActionId = agenda.Abolish ? "repeal" : "enact";
            objective.Credit = CourtObjectiveCredit.None;
            objective.ResultReason = agenda.CancellationReason ?? agenda.State.ToString();
            // Existing policy result code owns rewards; a bridge must never offer them twice.
            objective.ResultClaimed = true;
            switch (agenda.State)
            {
                case CourtAgendaState.Announced:
                case CourtAgendaState.AwaitingPlayerDecision:
                case CourtAgendaState.AwaitingNomination:
                    objective.State = CourtObjectiveState.Selected;
                    break;
                case CourtAgendaState.Deliberating:
                case CourtAgendaState.Voting:
                    objective.State = CourtObjectiveState.Active;
                    break;
                case CourtAgendaState.Passed:
                    objective.State = CourtObjectiveState.Succeeded;
                    objective.Credit = CourtObjectiveCredit.Sponsor;
                    break;
                case CourtAgendaState.FulfilledElsewhere:
                    objective.State = CourtObjectiveState.Succeeded;
                    objective.Credit = CourtObjectiveCredit.FulfilledElsewhere;
                    break;
                case CourtAgendaState.Defeated:
                    objective.State = CourtObjectiveState.Failed;
                    break;
                case CourtAgendaState.NominationExpired:
                    objective.State = CourtObjectiveState.Expired;
                    break;
                default:
                    objective.State = CourtObjectiveState.Cancelled;
                    break;
            }
            return objective;
        }
    }
}
