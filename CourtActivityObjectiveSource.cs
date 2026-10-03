using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal sealed class CourtActivityObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtActivityRules.Kind;
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (owner.Faction == null || !context.ActivityAdmitted(owner.Faction)) yield break;
            foreach (var definition in CourtActivityCatalog.All.Where(d => d.Faction == owner.Faction.Type
                && CourtActivityRules.MatchesMood(owner.Faction.Mood, d.Positive)))
                yield return new CourtObjectiveCandidate(definition.Id, definition.Positive ? "positive" : "negative");
        }
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var definition = CourtActivityCatalog.Find(candidate.TargetId);
            bool eligible = owner.Faction != null && definition != null && definition.Faction == owner.Faction.Type
                && candidate.ActionId == (definition.Positive ? "positive" : "negative")
                && CourtActivityRules.MatchesMood(owner.Faction.Mood, definition.Positive);
            bool viable = eligible && CourtSessionEvents.FindTargets(definition, owner.Faction, context.Realm).Count > 0;
            return new CourtObjectiveEvaluation(eligible, viable, new CourtObjectiveWeight(CourtActivityRules.Weight),
                "one_term_admission; saved_recipients; no_ballot");
        }
        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = choice.Candidate.ActionId };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.Activity = CourtSessionEvents.Prepare(CourtActivityCatalog.Find(choice.Candidate.TargetId), agenda.Faction, agenda.Realm);
            agenda.PolicyId = null;
        }
    }
}
