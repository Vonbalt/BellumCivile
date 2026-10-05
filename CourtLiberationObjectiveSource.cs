using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile
{
    internal sealed class CourtLiberationAssessment
    {
        internal ClientKingdomRecord Clientage;
        internal Kingdom Suzerain;
        internal ClientLibertyAssessment Baseline;
        internal ClientLibertyAssessment Boosted;
        internal bool Eligible;
        internal bool Viable;
        internal double Weight;

        internal CourtLiberationAssessment(Kingdom realm)
        {
            var clients = ClientKingdomBehavior.Instance;
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !CourtAgendaBehavior.ValidRealm(realm) || clients == null) return;
            Clientage = clients.GetClientRecord(realm);
            Suzerain = clients.GetSuzerain(realm);
            if (Clientage == null || !CourtAgendaBehavior.ValidRealm(Suzerain) || realm.IsAtWarWith(Suzerain)) return;
            Baseline = clients.BuildLibertyAssessmentWithBonus(realm, 0);
            Boosted = clients.BuildLibertyAssessmentWithBonus(realm, CourtLiberationRules.DesireBonus);
            float crown = Baseline?.Clans.FirstOrDefault(c => c.Clan == realm.RulingClan)?.LibertyDesire ?? 0;
            float boostedCrown = Boosted?.Clans.FirstOrDefault(c => c.Clan == realm.RulingClan)?.LibertyDesire ?? 0;
            Eligible = Boosted?.CanAttemptLiberation == true && boostedCrown >= BellumCivileConstants.ClientClanLiberationDesireThreshold;
            float will = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?.GetWarWill(realm.RulingClan) ?? 0;
            Viable = Eligible && ClientLiberationRules.EffectiveWarWill(will, boostedCrown) >= BellumCivileOptions.WarWillDeclareThreshold;
            Weight = Eligible ? CourtLiberationRules.Weight(Boosted.LiberationReadiness, crown,
                realm.RulingClan.Leader.GetTraitLevel(DefaultTraits.Valor), realm.RulingClan.Leader.GetTraitLevel(DefaultTraits.Calculating)) : 0;
        }
    }

    internal sealed class CourtLiberationObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtLiberationRules.Kind;
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (owner.Faction != null || owner.Sponsor != context.Realm.RulingClan) yield break;
            if (CourtAgendaBehavior.Current?.CrownActionCoolingDown(context.Realm, Kind) == true) yield break;
            var facts = context.Liberation;
            if (facts.Eligible && CourtAgendaBehavior.Current?.HasLiberationObjective(context.Realm, context.PlayerAgenda) != true)
                yield return new CourtObjectiveCandidate(facts.Suzerain.StringId, "prepare");
        }
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            bool crown = owner.Faction == null && owner.Sponsor == context.Realm.RulingClan;
            if (!crown) return new CourtObjectiveEvaluation(false, false, new CourtObjectiveWeight(0), "crown_only");
            var facts = context.Liberation;
            bool eligible = facts.Eligible && candidate.ActionId == "prepare" && candidate.TargetId == facts.Suzerain?.StringId
                && CourtAgendaBehavior.Current?.CrownActionCoolingDown(context.Realm, Kind) != true
                && CourtAgendaBehavior.Current?.HasLiberationObjective(context.Realm, context.PlayerAgenda) != true;
            return new CourtObjectiveEvaluation(eligible, eligible && facts.Viable
                && NpcInfluenceBudgetService.CanAfford(owner.Sponsor, CourtAgendaBehavior.CrownInitiativeCost, NpcInfluenceExpenseKind.Discretionary), new CourtObjectiveWeight(facts.Weight),
                "liberation_preparations; desire_bonus=20; native_war_vote_retained");
        }
        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "prepare" };
            if (previous.HasTermSnapshot)
            {
                agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                agenda.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            }
            agenda.Liberation = new CourtLiberationRecord { Suzerain = CourtCampaignObjectiveSource.Target(choice.Candidate.TargetId),
                Ruler = agenda.Realm.RulingClan.Leader, Clientage = ClientKingdomBehavior.Instance?.GetClientRecord(agenda.Realm) };
            agenda.PolicyId = null;
        }
    }
}
