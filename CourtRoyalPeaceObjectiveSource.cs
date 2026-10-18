using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;

namespace BellumCivile
{
    internal sealed class CourtRoyalPeaceObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtRoyalPeaceRules.Kind;
        private static double Weight(CourtTermContext context, CourtObjectiveOwner owner) => owner.Faction == null
            && owner.Sponsor == context.Realm.RulingClan
            ? CourtAgendaBehavior.Current?.PeacetimeRoyalPeaceWeight(context.Realm) ?? 0 : 0;
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
            => Weight(context, owner) > 0 ? new[] { new CourtObjectiveCandidate(context.Realm.StringId, "realm_peace") }
                : Enumerable.Empty<CourtObjectiveCandidate>();
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            double weight = candidate.TargetId == context.Realm.StringId ? Weight(context, owner) : 0;
            return new CourtObjectiveEvaluation(weight > 0, weight > 0, new CourtObjectiveWeight(weight), "realm_peace; peacetime_interference");
        }
        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
            => CourtAgendaBehavior.Current?.SelectPeacetimeRoyalPeace(agenda);
    }
}
