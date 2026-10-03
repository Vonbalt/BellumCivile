using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtAppeasementObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtAppeasementRules.Kind;
        internal static FactionObject Target(Kingdom realm, string id) => Campaign.Current?
            .GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(realm)
            .FirstOrDefault(f => f.IsIdeology && ((int)f.Type).ToString() == id);

        internal static bool Eligible(FactionObject target, Kingdom realm)
        {
            if (realm?.RulingClan?.Leader == null || realm.IsEliminated || target == null
                || CourtAgendaBehavior.Current?.CrownActionCoolingDown(realm, CourtAppeasementRules.Kind) == true
                || target.ParentKingdom != realm || !target.IsIdeology || target.Mood > -40
                || CourtAgendaBehavior.MemberCount(target) == 0 || target.CrownAccommodation?.Active == true
                || target.CrownAccommodation?.TermEnd.IsFuture == true) return false;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return manager != null && manager.GetFactionsInKingdom(realm).Contains(target)
                && !manager.GetFactionsInKingdom(realm).Any(f => f.IsGrandCoalition
                    && (f.IsCoalitionFrom(target.Type) || f.Members.Any(c => target.Members.Contains(c)))
                    && (f.IsUltimatumPending || f.IsCivilWarActive() || f.HasTrackedRebelKingdom));
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (owner.Faction != null || owner.Sponsor != context.Realm.RulingClan) yield break;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) yield break;
            foreach (var faction in manager.GetFactionsInKingdom(context.Realm).Where(f => Eligible(f, context.Realm)))
                yield return new CourtObjectiveCandidate(((int)faction.Type).ToString(), "reconcile");
        }

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var target = Target(context.Realm, candidate.TargetId);
            bool eligible = owner.Faction == null && owner.Sponsor == context.Realm.RulingClan
                && candidate.ActionId == "reconcile" && Eligible(target, context.Realm);
            bool affordable = eligible && NpcInfluenceBudgetService.CanAfford(owner.Sponsor,
                CourtAppeasementRules.Cost(CourtAgendaBehavior.MemberCount(target)), NpcInfluenceExpenseKind.Discretionary);
            return new CourtObjectiveEvaluation(eligible, affordable, new CourtObjectiveWeight(1), "crown_accommodation; no_ballot");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var target = Target(agenda.Realm, choice.Candidate.TargetId);
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "reconcile" };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.Appeasement = new CourtAppeasementRecord { Target = target, Realm = agenda.Realm,
                RulingClan = agenda.Realm.RulingClan, Ruler = agenda.Realm.RulingClan.Leader,
                QuotedCost = CourtAppeasementRules.Cost(CourtAgendaBehavior.MemberCount(target)),
                DurationDays = agenda.HasScheduleSnapshot ? agenda.TermDays : BellumCivileOptions.CourtTermDays,
                TermEnd = CampaignTime.Days((float)previous.DeadlineDay) };
            agenda.PolicyId = null;
        }
    }
}
