using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtPeaceObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtPeaceRules.Kind;
        internal static Kingdom Target(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        internal static bool Valid(Kingdom realm) => realm?.RulingClan?.Leader != null && !realm.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);
        internal static bool EligibleWar(Kingdom realm, Kingdom target, WarScoreRecord war) =>
            WarPeaceRevampBehavior.IsRevampEnabled() && Valid(realm) && Valid(target)
            && realm != target && realm.IsAtWarWith(target) && war?.IsActive == true
            && war.ConflictType == WarScoreConflictType.ForeignWar
            && ((war.AttackerKingdomId == realm.StringId && war.DefenderKingdomId == target.StringId)
                || (war.DefenderKingdomId == realm.StringId && war.AttackerKingdomId == target.StringId))
            && ClientKingdomBehavior.Instance?.IsClientKingdom(realm) != true;

        private static bool EligibleOwner(CourtTermContext context, CourtObjectiveOwner owner) =>
            owner.Faction?.Type == FactionType.Liberty && owner.Faction.ParentKingdom == context.Realm
            && owner.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(owner.Faction) >= 2;

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (!EligibleOwner(context, owner)) yield break;
            var scores = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (scores == null) yield break;
            foreach (var war in scores.GetActiveWars())
            {
                var target = scores.GetOpposingKingdom(war, context.Realm);
                if (EligibleWar(context.Realm, target, war))
                    yield return new CourtObjectiveCandidate(target.StringId, "seek_peace");
            }
        }

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var target = Target(candidate.TargetId);
            var war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(context.Realm, target);
            bool eligible = EligibleOwner(context, owner) && candidate.ActionId == "seek_peace" && EligibleWar(context.Realm, target, war);
            return new CourtObjectiveEvaluation(eligible, eligible, new CourtObjectiveWeight(CourtPeaceRules.SelectionWeight),
                "liberty_peace_initiative; acceptance_bonus=15; no_automatic_parley");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            var target = Target(choice.Candidate.TargetId);
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "seek_peace" };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.Peace = new CourtPeaceRecord { Target = target,
                War = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(agenda.Realm, target) };
            if (previous.HasTermSnapshot)
                agenda.SessionDate = CampaignTime.Days((float)CourtPeaceRules.Session(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            agenda.PolicyId = null;
        }
    }
}
