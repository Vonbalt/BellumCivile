using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile
{
    internal sealed class CourtDynasticObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtDynasticRules.Kind;
        internal static Hero HeroById(string id) => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
        internal static bool RealmPair(Kingdom realm, Kingdom target) => CourtAgendaBehavior.ValidRealm(realm)
            && CourtAgendaBehavior.ValidRealm(target) && realm != target && !realm.IsAtWarWith(target)
            && ClientKingdomBehavior.Instance?.IsClientKingdom(realm) != true
            && ClientKingdomBehavior.Instance?.IsClientKingdom(target) != true;

        private static bool Owner(CourtTermContext c, CourtObjectiveOwner o) => o.Faction?.Type == FactionType.Nobility
            && o.Faction.ParentKingdom == c.Realm && o.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(o.Faction) >= 2;

        internal static IReadOnlyList<CourtObjectiveCandidate> Discover(CourtTermContext context)
        {
            if (!CourtAgendaBehavior.ValidRealm(context.Realm)) return new List<CourtObjectiveCandidate>();
            double deadline = context.PlayerAgenda?.ObjectiveData?.HasTermSnapshot == true
                ? context.PlayerAgenda.ObjectiveData.DeadlineDay : CampaignTime.Now.ToDays + BellumCivileOptions.CourtTermDays;
            double now = CampaignTime.Now.ToDays;
            double session = context.PlayerAgenda?.ObjectiveData?.HasTermSnapshot == true
                ? CourtAgendaRules.EarlyObjectiveSession(context.PlayerAgenda.ObjectiveData.SelectedDay,
                    CourtAgendaRules.NominationDays(context.PlayerAgenda.TermDays), now, context.PlayerAgenda.SessionDate.ToDays)
                : now + CourtAgendaRules.NominationDays(BellumCivileOptions.CourtTermDays);
            if (session + 2 > deadline)
            {
                BellumCivileDebug.TraceIfEnabled("court", $"Royal marriage discovery; realm={context.Realm.StringId}; blocker=insufficient_approach_window.");
                return new List<CourtObjectiveCandidate>();
            }
            if (Campaign.Current.GetCampaignBehavior<StrategicMarriageBehavior>()?
                .HasCourtMarriageOpportunity(context.Realm.RulingClan, deadline) != true)
            {
                BellumCivileDebug.TraceIfEnabled("court", $"Royal marriage discovery; realm={context.Realm.StringId}; blocker=marriage_service_unavailable.");
                return new List<CourtObjectiveCandidate>();
            }
            var targets = Kingdom.All.Where(t => RealmPair(context.Realm, t) && !context.Realm.AlliedKingdoms.Contains(t))
                .Where(t => { var d = new StartAllianceDecision(context.Realm.RulingClan, t); return d.IsAllowed() && d.CanMakeDecision(out _); }).ToList();
            var candidates = BellumMarriageStrategyHelper.FindCourtMarriages(context.Realm, targets, context.PlayerAgenda)
                .Select(m => new CourtObjectiveCandidate(m.Candidate.Clan.Kingdom.StringId, m.Suitor.StringId, m.Candidate.StringId)).ToList();
            BellumCivileDebug.TraceIfEnabled("court", $"Royal marriage discovery; realm={context.Realm.StringId}; viable_alliance_realms={targets.Count}; named_matches={candidates.Count}.");
            return candidates;
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner) =>
            Owner(context, owner) ? context.DynasticCandidates : new List<CourtObjectiveCandidate>();

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            bool valid = Owner(context, owner) && context.DynasticCandidates.Any(c => c.TargetId == candidate.TargetId
                && c.ActionId == candidate.ActionId && c.BeneficiaryId == candidate.BeneficiaryId);
            return new CourtObjectiveEvaluation(valid, valid, new CourtObjectiveWeight(CourtDynasticRules.SelectionWeight), "named_royal_marriage; alliance_support_not_requirement");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            var first = HeroById(choice.Candidate.ActionId);
            var second = HeroById(choice.Candidate.BeneficiaryId);
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = choice.Candidate.ActionId };
            if (previous.HasTermSnapshot)
            {
                agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                agenda.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            }
            agenda.Dynastic = new CourtDynasticRecord { Target = second?.Clan?.Kingdom, OurHouse = first?.Clan, TheirHouse = second?.Clan,
                First = first, Second = second, Destination = first == null || second == null ? null : Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second) };
            agenda.PolicyId = null;
        }
    }
}
