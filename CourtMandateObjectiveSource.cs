using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtMandateObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtMandateRules.Kind;
        internal static RealmLawBehavior Laws => Campaign.Current?.GetCampaignBehavior<RealmLawBehavior>();
        internal static int Cost(Clan sponsor) => Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
            .GetFactionMotionInfluenceCost(sponsor, 100) ?? 100;
        internal static int Direction(FactionObject faction) => faction?.Type == FactionType.Nobility ? 1
            : faction?.Type == FactionType.Liberty ? -1 : 0;
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (Direction(owner.Faction) == 0 || owner.Faction.ParentKingdom != context.Realm || owner.Faction.Mood <= -60
                || CourtAgendaBehavior.MemberCount(owner.Faction) < 2 || !CourtAgendaBehavior.MandateRealmReady(context.Realm)
                || CourtAgendaBehavior.Current?.HasMandateReservation(context.Realm, context.PlayerAgenda) == true) yield break;
            var old = RealmLawRegistry.Instance.Find(Laws?.GetActiveLawId(context.Realm, RealmLawRegistry.TermGroup));
            int? next = old?.MandateYears is int years ? CourtMandateRules.Next(years, Direction(owner.Faction)) : null;
            if (next.HasValue) yield return new CourtObjectiveCandidate(RealmLawRegistry.Instance.ForTerm(next.Value).Id, old.Id);
        }
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate candidate)
        {
            bool eligible = FindCandidates(c, o).Any(x => x.TargetId == candidate.TargetId && x.ActionId == candidate.ActionId);
            bool viable = false;
            if (eligible)
            {
                var decision = new MandateReformDecision(o.Sponsor, candidate.ActionId, candidate.TargetId, Direction(o.Faction), o.Faction.Type);
                viable = CourtPolicyForecast.Calculate(decision, Cost(o.Sponsor), decision.Outcome(true), decision.Outcome(false), c.Voters).Viable;
            }
            return new CourtObjectiveEvaluation(eligible, viable, new CourtObjectiveWeight(.5), "adjacent_mandate_reform; next_mandate_only");
        }
        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var old = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = choice.Candidate.ActionId };
            if (old.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(old.SelectedDay, old.DeadlineDay);
            agenda.Mandate = new CourtMandateRecord { Id = Guid.NewGuid().ToString("N"), OldLaw = choice.Candidate.ActionId,
                NewLaw = choice.Candidate.TargetId, Direction = Direction(agenda.Faction) };
            agenda.PolicyId = null;
        }
    }
}
