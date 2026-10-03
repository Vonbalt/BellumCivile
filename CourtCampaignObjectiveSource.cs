using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtCampaignObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtCampaignRules.Kind;
        internal static Kingdom Target(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        internal static bool ValidPair(Kingdom realm, Kingdom target) => WarPeaceRevampBehavior.IsRevampEnabled()
            && CourtAgendaBehavior.ValidRealm(realm) && CourtAgendaBehavior.ValidRealm(target) && realm != target
            && ClientKingdomBehavior.Instance?.IsClientKingdom(realm) != true;

        private static bool EligibleOwner(CourtTermContext context, CourtObjectiveOwner owner) =>
            WarPeaceRevampBehavior.IsRevampEnabled() && CourtAgendaBehavior.ValidRealm(context.Realm)
            && ClientKingdomBehavior.Instance?.IsClientKingdom(context.Realm) != true
            && owner.Faction?.Type == FactionType.Glory && owner.Faction.ParentKingdom == context.Realm
            && owner.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(owner.Faction) >= 2;

        private static IEnumerable<Kingdom> CredibleTargets(CourtTermContext context, CourtObjectiveOwner owner) =>
            owner.Faction.Members.Where(c => CourtAgendaBehavior.Eligible(c, context.Realm) && c != context.Realm.RulingClan)
                .Distinct().SelectMany(c => context.CampaignTargets(c))
                .Where(s => !s.IsActiveWar && !s.IsLiberationTarget).Select(s => s.TargetKingdom).Where(k => k != null).Distinct();

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (!EligibleOwner(context, owner)) yield break;
            foreach (var target in CredibleTargets(context, owner).Where(k => ValidPair(context.Realm, k) && context.CanSupportCampaign(k)))
                yield return new CourtObjectiveCandidate(target.StringId, "support_campaign");
        }

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var target = Target(candidate.TargetId);
            bool eligible = EligibleOwner(context, owner) && candidate.ActionId == "support_campaign"
                && ValidPair(context.Realm, target) && context.CanSupportCampaign(target) && CredibleTargets(context, owner).Contains(target);
            return new CourtObjectiveEvaluation(eligible, eligible, new CourtObjectiveWeight(CourtCampaignRules.SelectionWeight),
                "glory_campaign_initiative; support_bonus=15; no_automatic_declaration");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "support_campaign" };
            if (previous.HasTermSnapshot)
            {
                agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                agenda.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            }
            agenda.Campaign = new CourtCampaignRecord { Target = Target(choice.Candidate.TargetId) };
            agenda.PolicyId = null;
        }
    }
}
