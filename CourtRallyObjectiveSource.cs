using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtRallyObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtRallyRules.Kind;
        internal static bool WarEligible(Kingdom realm, Kingdom target, WarScoreRecord war) => CourtPeaceObjectiveSource.EligibleWar(realm, target, war)
            && ClientKingdomBehavior.Instance?.IsClientKingdom(target) != true && !war.ParleyForced && !war.ResolutionPending
            && !war.TerminalResolutionQueued && !WarScoreBehavior.IsStorylineProtectedForeignWar(war);
        private static bool Owner(CourtTermContext c, CourtObjectiveOwner o) => o.Faction?.Type == FactionType.Glory
            && o.Faction.ParentKingdom == c.Realm && o.Faction.Mood > 0 && CourtAgendaBehavior.MemberCount(o.Faction) >= 2;
        internal static IReadOnlyList<CourtObjectiveCandidate> Discover(CourtTermContext c, FactionObject faction)
        {
            var result = new List<CourtObjectiveCandidate>();
            var scores = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            var will = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (scores == null || will == null || faction == null) return result;
            var members = faction.Members.Where(x => CourtAgendaBehavior.Eligible(x, c.Realm) && x != c.Realm.RulingClan).Distinct().ToList();
            double power = members.Sum(x => System.Math.Max(1, x.CurrentTotalStrength));
            if (power <= 0 || members.Sum(x => will.PeekWarWill(x) * System.Math.Max(1, x.CurrentTotalStrength)) / power > 60) return result;
            foreach (var war in scores.GetActiveWars())
            {
                var target = scores.GetOpposingKingdom(war, c.Realm);
                if (WarEligible(c.Realm, target, war) && CourtAgendaBehavior.Current?.RallyUsed(c.Realm, war, c.PlayerAgenda) != true)
                    result.Add(new CourtObjectiveCandidate(target.StringId, "rally"));
            }
            return result;
        }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext c, CourtObjectiveOwner o) => Owner(c,o)
            ? c.RallyCandidates(o.Faction) : Enumerable.Empty<CourtObjectiveCandidate>();
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate candidate)
        {
            bool valid = Owner(c,o) && c.RallyCandidates(o.Faction).Any(x => x.TargetId == candidate.TargetId && x.ActionId == candidate.ActionId);
            return new CourtObjectiveEvaluation(valid,valid,new CourtObjectiveWeight(valid ? .25 : 0),"named_war_victory; scoped_resolve");
        }
        public void ApplySelection(CourtAgendaRecord a, CourtObjectiveChoice choice)
        {
            var old = a.GetObjective();
            var target = CourtPeaceObjectiveSource.Target(choice.Candidate.TargetId);
            var war = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>().GetActiveWar(a.Realm,target);
            a.ObjectiveData = new CourtObjectiveRecord { Kind=Kind, TargetId=target.StringId, ActionId="rally" };
            if(old.HasTermSnapshot)
            {
                a.ObjectiveData.FreezeTerm(old.SelectedDay,old.DeadlineDay);
                a.SessionDate=CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(old.SelectedDay,CourtAgendaRules.NominationDays(a.TermDays),CampaignTime.Now.ToDays,a.SessionDate.ToDays));
            }
            a.Rally=new CourtRallyRecord {Target=target,War=war,Attacker=war.AttackerKingdomId,Defender=war.DefenderKingdomId,WarKey=war.WarKey,WarStarted=war.StartedDay};
            a.PolicyId=null;
        }
    }
}
