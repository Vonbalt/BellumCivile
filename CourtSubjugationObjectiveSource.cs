using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtSubjugationObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtSubjugationRules.Kind;
        internal static bool ValidPair(Kingdom realm, Kingdom target) => CourtCampaignObjectiveSource.ValidPair(realm, target)
            && ClientKingdomBehavior.Instance?.CanEstablishClientKingdom(target, realm, out _) == true;

        private static bool EligibleOwner(CourtTermContext context, CourtObjectiveOwner owner) =>
            CourtAgendaBehavior.ValidRealm(context.Realm) && WarPeaceRevampBehavior.IsRevampEnabled()
            && owner.Faction?.Type == FactionType.Glory && owner.Faction.ParentKingdom == context.Realm
            && owner.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(owner.Faction) >= 2;

        private static IEnumerable<Kingdom> Targets(CourtTermContext context, CourtObjectiveOwner owner)
        {
            var peaceful = owner.Faction.Members.Where(c => CourtAgendaBehavior.Eligible(c, context.Realm)
                    && c != context.Realm.RulingClan).Distinct().SelectMany(context.CampaignTargets)
                .Where(s => !s.IsActiveWar && !s.IsLiberationTarget).Select(s => s.TargetKingdom);
            var wars = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            var enemies = wars?.GetActiveWars().Select(w => new { War = w, Target = wars.GetOpposingKingdom(w, context.Realm) })
                .Where(p => CourtPeaceObjectiveSource.EligibleWar(context.Realm, p.Target, p.War)).Select(p => p.Target)
                ?? Enumerable.Empty<Kingdom>();
            return peaceful.Concat(enemies).Where(t => t != null).Distinct()
                .Where(t => context.CanSeekClientage(t) && (context.Realm.IsAtWarWith(t) || context.CanSupportCampaign(t)));
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner) =>
            EligibleOwner(context, owner) ? Targets(context, owner).Select(t => new CourtObjectiveCandidate(t.StringId, "seek_clientage"))
                : Enumerable.Empty<CourtObjectiveCandidate>();

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            bool eligible = EligibleOwner(context, owner) && candidate.ActionId == "seek_clientage"
                && Targets(context, owner).Any(t => t.StringId == candidate.TargetId);
            return new CourtObjectiveEvaluation(eligible, eligible, new CourtObjectiveWeight(CourtSubjugationRules.SelectionWeight),
                "glory_clientage; cost_below_150; weaker_target; no_automatic_war_or_treaty");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "seek_clientage" };
            if (previous.HasTermSnapshot)
            {
                agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                agenda.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            }
            agenda.Subjugation = new CourtSubjugationRecord { Target = CourtCampaignObjectiveSource.Target(choice.Candidate.TargetId) };
            agenda.PolicyId = null;
        }
    }
}
