using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtProtectionCandidate
    {
        internal Kingdom Threat;
        internal CourtProtectionAssessment Assessment;
    }

    internal sealed class CourtProtectionObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtProtectionRules.Kind;
        private static bool Owner(CourtTermContext c, CourtObjectiveOwner o) => o.Faction == null && o.Sponsor == c.Realm.RulingClan
            && CourtAgendaBehavior.Current?.CrownActionCoolingDown(c.Realm, CourtProtectionRules.Kind) != true
            && CourtAgendaBehavior.Current?.HasProtectionOffer(c.Realm) != true;

        internal static IReadOnlyList<CourtProtectionCandidate> Discover(CourtTermContext context)
        {
            var results = new List<CourtProtectionCandidate>();
            if (!CourtAgendaBehavior.ValidRealm(context.Realm) || CourtAgendaBehavior.Current?.HasProtectionOffer(context.Realm) == true) return results;
            var service = new CourtProtectionAssessmentService();
            foreach (var threat in context.Realm.FactionsAtWarWith.OfType<Kingdom>().Where(CourtAgendaBehavior.ValidRealm))
            {
                if (threat.CurrentTotalStrength < 2 * context.Realm.CurrentTotalStrength) continue;
                if (WarPeaceRevampBehavior.IsRevampEnabled() && Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(context.Realm, threat) == null) continue;
                var candidates = Kingdom.All.Where(CourtAgendaBehavior.ValidRealm).Where(k => k != context.Realm && k != threat)
                    .Select(k => service.Assess(context.Realm, threat, k)).Where(a => a.Eligible)
                    .OrderByDescending(a => a.Score.Total).ThenBy(a => a.Protector.StringId, StringComparer.Ordinal);
                foreach (var assessment in context.ManualSelection ? candidates.AsEnumerable() : candidates.Take(1))
                    results.Add(new CourtProtectionCandidate { Threat = threat, Assessment = assessment });
            }
            return results;
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext c, CourtObjectiveOwner owner) => !Owner(c, owner)
            ? Enumerable.Empty<CourtObjectiveCandidate>() : c.ProtectionCandidates.Select(p => new CourtObjectiveCandidate(p.Assessment.Protector.StringId, p.Threat.StringId));
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate candidate)
        {
            var p = Owner(c, o) ? c.ProtectionCandidates.FirstOrDefault(x => x.Threat.StringId == candidate.ActionId && x.Assessment.Protector.StringId == candidate.TargetId) : null;
            return new CourtObjectiveEvaluation(p != null, p != null
                && NpcInfluenceBudgetService.CanAfford(o.Sponsor, CourtAgendaBehavior.CrownInitiativeCost, NpcInfluenceExpenseKind.Discretionary),
                new CourtObjectiveWeight(p?.Assessment.MotionWeight ?? 0), "voluntary_submission_for_named_intervention");
        }
        public void ApplySelection(CourtAgendaRecord a, CourtObjectiveChoice choice)
        {
            var old = a.GetObjective();
            var protector = CourtCampaignObjectiveSource.Target(choice.Candidate.TargetId);
            var threat = CourtCampaignObjectiveSource.Target(choice.Candidate.ActionId);
            a.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = choice.Candidate.ActionId };
            if (old.HasTermSnapshot)
            {
                a.ObjectiveData.FreezeTerm(old.SelectedDay, old.DeadlineDay);
                a.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(old.SelectedDay,
                    CourtAgendaRules.NominationDays(a.TermDays), CampaignTime.Now.ToDays, a.SessionDate.ToDays));
            }
            a.Protection = new CourtProtectionRecord { Client = a.Realm, Protector = protector, Threat = threat,
                ClientHouse = a.Realm.RulingClan, ClientRuler = a.Realm.RulingClan.Leader,
                ProtectorHouse = protector?.RulingClan, ProtectorRuler = protector?.RulingClan?.Leader,
                ThreatWar = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(a.Realm, threat),
                ThreatWarStart = a.Realm.GetStanceWith(threat).WarStartDate.ToDays,
                TermStart = old.SelectedDay, TermEnd = old.DeadlineDay };
            a.PolicyId = null;
        }
    }
}
