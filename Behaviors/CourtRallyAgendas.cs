using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtAgendaRecord> _rallies = new List<CourtAgendaRecord>();
        private Dictionary<WarScoreRecord,List<CourtAgendaRecord>> _rallyIndex;
        private static bool IsRally(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtRallyRules.Kind;
        private static bool RallyOpen(CourtAgendaRecord a) => IsRally(a) && a.Rally != null && !a.ResultApplied
            && a.ObjectiveData.HasTermSnapshot && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective);
        private static bool RallyOwner(CourtAgendaRecord a) => ValidRealm(a.Realm) && a.Faction?.Type == FactionType.Glory
            && a.Faction.ParentKingdom == a.Realm && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(a.Realm).Contains(a.Faction) == true;
        private static bool RallyIdentity(CourtAgendaRecord a) => a.Rally.War != null && a.Rally.War.ConflictType == WarScoreConflictType.ForeignWar
            && a.Rally.War.WarKey == a.Rally.WarKey && a.Rally.War.StartedDay == a.Rally.WarStarted
            && a.Rally.War.AttackerKingdomId == a.Rally.Attacker && a.Rally.War.DefenderKingdomId == a.Rally.Defender;
        private IEnumerable<CourtAgendaRecord> RallyRows() => _rallies.Concat(_agendas.Where(IsRally)).Distinct();
        internal bool RallyUsed(Kingdom realm, WarScoreRecord war, CourtAgendaRecord excluded) => RallyRows().Any(a => a != excluded
            && a.Realm == realm && a.Rally?.War == war && a.Rally.Activated && CampaignTime.Now.ToDays < a.ObjectiveData.DeadlineDay);
        private void IndexRallies()
        {
            _rallyIndex = _rallies.Where(a => a.Rally?.War != null).GroupBy(a => a.Rally.War).ToDictionary(g => g.Key,g => g.ToList());
        }
        internal static float EffectiveWarWill(Clan clan, WarScoreRecord war, float baseline) => Current?.RallyEffective(clan,war,baseline) ?? baseline;
        private float RallyEffective(Clan clan, WarScoreRecord war, float baseline)
        {
            if (clan == null || clan == Clan.PlayerClan || war?.IsActive != true || !WarPeaceRevampBehavior.IsRevampEnabled()) return baseline;
            if (_rallyIndex == null) IndexRallies();
            if (!_rallyIndex.TryGetValue(war,out var rows)) return baseline;
            double now=CampaignTime.Now.ToDays;
            bool active=rows.Any(a => RallyOpen(a) && a.Realm == clan.Kingdom && RallyOwner(a) && RallyIdentity(a)
                && CourtPeaceObjectiveSource.EligibleWar(a.Realm,a.Rally.Target,war) && ClientKingdomBehavior.Instance?.IsClientKingdom(a.Rally.Target) != true
                && a.Rally.Activated && now >= a.Rally.Started && now < a.Rally.Expires && ReceivesPoliticalSupport(a,clan,a.Rally.Members));
            return CourtRallyRules.Effective(baseline,active);
        }
        internal WarWillMotiveBreakdown RallyBreakdown(Clan clan, WarScoreRecord war, WarWillMotiveBreakdown baseline)
        {
            float effective=RallyEffective(clan,war,baseline.WarWill);
            if(effective==baseline.WarWill) return baseline;
            var row=_rallyIndex[war].First(a=>a.Realm==clan.Kingdom && a.Rally.Activated && !a.ResultApplied
                && CampaignTime.Now.ToDays>=a.Rally.Started && CampaignTime.Now.ToDays<a.Rally.Expires && RallyIdentity(a));
            string label=new TextObject("{=BC_RallyMotive}Renewed resolve against {TARGET}").SetTextVariable("TARGET",row.Rally.Target.Name).ToString();
            string detail=new TextObject("{=BC_RallyMotiveDetail}Base enthusiasm: {BASE}. Renewed resolve: +{BONUS} until {DATE}. Effective enthusiasm: {EFFECTIVE}.")
                .SetTextVariable("BASE",baseline.WarWill.ToString("0.0")).SetTextVariable("BONUS",(effective-baseline.WarWill).ToString("0.0"))
                .SetTextVariable("DATE",CampaignTime.Days((float)row.Rally.Expires).ToString()).SetTextVariable("EFFECTIVE",effective.ToString("0.0")).ToString();
            var lines=baseline.Motives.ToList(); lines.Add(new WarWillMotiveLine(WarWillReasonType.Unknown,effective-baseline.WarWill,label,detail));
            var stance=effective>=BellumCivileOptions.WarWillDeclareThreshold?WarWillVoteStance.Yay:effective<=BellumCivileOptions.WarWillPeaceThreshold?WarWillVoteStance.Nay:WarWillVoteStance.Neutral;
            return new WarWillMotiveBreakdown(effective,stance,lines);
        }
        private void AdvanceRally(CourtAgendaRecord a)
        {
            if(a.ResultApplied || a.IsOngoingObjective || !a.SessionDate.IsPast) return;
            MaintainRallies();
            if(a.ResultApplied) return;
            if(a.Rally == null || !RallyOwner(a) || !RallyIdentity(a) || !CourtRallyObjectiveSource.WarEligible(a.Realm,a.Rally.Target,a.Rally.War)
                || a.Faction.Mood <= 0 || MemberCount(a.Faction)<2 || RallyUsed(a.Realm,a.Rally.War,a))
            { FinishRally(a,0,"invalid_at_session"); return; }
            a.Rally.Members=a.Faction.Members.Where(c => Eligible(c,a.Realm) && c!=a.Realm.RulingClan).Distinct().ToList();
            a.Rally.Started=CampaignTime.Now.ToDays;
            a.Rally.Expires=CourtRallyRules.End(a.Rally.Started,a.ObjectiveData.DeadlineDay,CampaignTime.DaysInYear);
            a.Rally.Activated=true;
            a.ObjectiveData.Activate(); a.State=CourtAgendaState.PursuingObjective;
            if(!_rallies.Contains(a)) _rallies.Add(a);
            _rallyIndex=null;
            ReportRally(a,new TextObject("{=BC_RallyBegun}The {FACTION} of {REALM} calls upon its houses to stand firm against {TARGET}. Its lords pledge renewed resolve until {BOOST_DATE}, and look to the Crown for a victorious peace before {DATE}."));
        }
        internal void BeginRallySettlement(WarScoreRecord war)
        {
            foreach(var a in RallyRows().Where(a => RallyOpen(a) && a.Rally.War == war && RallyIdentity(a)))
            { a.Rally.SettlementPending=true; a.Rally.PendingDay=CampaignTime.Now.ToDays; }
        }
        internal void RecordRallyOutcome(WarScoreRecord war, bool verified, bool white, string victor, float attackerNet, string reason)
        {
            foreach(var a in RallyRows().Where(a => RallyOpen(a) && a.Rally.War == war && RallyIdentity(a)).ToList())
            {
                if(a.Rally.OutcomeRecorded) continue;
                a.Rally.OutcomeDay=CampaignTime.Now.ToDays;
                float net=a.Realm.StringId==a.Rally.Attacker?attackerNet:-attackerNet;
                a.Rally.Defeat=verified && !white && (string.IsNullOrEmpty(victor)?net<=-10:victor!=a.Realm.StringId);
                a.Rally.Outcome= CourtRallyRules.Classify(verified,white,string.IsNullOrEmpty(victor)?0:victor==a.Realm.StringId?1:-1,
                    a.Realm.StringId==a.Rally.Attacker?attackerNet:-attackerNet);
                a.Rally.OutcomeReason=reason; a.Rally.OutcomeRecorded=true; a.Rally.SettlementPending=false;
                BellumCivileLogger.Log($"Court rally outcome receipt; war={war.WarKey}; realm={a.Realm.StringId}; verified={verified}; white={white}; victor={victor}; net={net}; day={a.Rally.OutcomeDay}; source={reason}.");
            }
        }
        private void MaintainRallies()
        {
            foreach(var a in RallyRows().Where(RallyOpen).ToList())
            {
                var r=a.Rally; double now=CampaignTime.Now.ToDays;
                if(!RallyOwner(a) || !RallyIdentity(a)) {FinishRally(a,0,"identity_changed");continue;}
                if(r.OutcomeRecorded)
                { FinishRally(a,r.OutcomeDay<=a.ObjectiveData.DeadlineDay?r.Outcome:-2,r.OutcomeReason); continue; }
                // Peace callbacks precede final treaty delivery; never infer success from tracker closure.
                if(r.SettlementPending && now <= r.PendingDay+1) continue;
                if(!CourtPeaceObjectiveSource.EligibleWar(a.Realm,r.Target,r.War) || ClientKingdomBehavior.Instance?.IsClientKingdom(r.Target)==true)
                {FinishRally(a,0,"unverified_closure");continue;}
                if(now>a.ObjectiveData.DeadlineDay){FinishRally(a,-2,"term_expired");continue;}
                r.Members.RemoveAll(c => !Eligible(c,a.Realm) || !a.Faction.Members.Contains(c));
            }
            if(_rallies.RemoveAll(a => a.ResultApplied && CampaignTime.Now.ToDays>a.ObjectiveData.DeadlineDay+1)>0) _rallyIndex=null;
        }
        private void FinishRally(CourtAgendaRecord a,int outcome,string reason)
        {
            if(a.ResultApplied) return;
            var state=outcome==1?CourtObjectiveState.Succeeded:outcome==-2?CourtObjectiveState.Expired:outcome==-1?CourtObjectiveState.Failed:CourtObjectiveState.Cancelled;
            var credit=outcome==1?(a.Rally.Activated?CourtObjectiveCredit.Sponsor:CourtObjectiveCredit.FulfilledElsewhere):CourtObjectiveCredit.None;
            if(!a.ObjectiveData.Finish(state,credit,reason) || !a.ObjectiveData.TryClaimResult()) return;
            a.ResultApplied=a.PaymentSettled=true;
            a.State=outcome==1?CourtAgendaState.Completed:outcome==0?CourtAgendaState.Cancelled:CourtAgendaState.NotProposed;
            float actual=0;
            if(RallyOwner(a))
            {
                float before=a.Faction.Mood;
                a.Faction.Mood=Math.Max(-100,Math.Min(100,before+CourtRallyRules.Shock(outcome)));
                actual=a.Faction.Mood-before;
                RecordResultHistory(a, CourtRallyRules.Shock(outcome));
            }
            ReportRally(a,new TextObject(outcome==1?"{=BC_RallyVictory}Peace with {TARGET} has vindicated the campaign urged by the {FACTION}. Its lords acclaim the Crown for bringing honor and advantage to {REALM}."
                :outcome==-2?"{=BC_RallyUnfinished}The court term ends with the war against {TARGET} still unfinished. The {FACTION} voices its disappointment at a campaign yet to bear the promised fruit."
                :outcome==-1 && a.Rally.Defeat?"{=BC_RallyDefeat}Defeat in the war with {TARGET} has bitterly disappointed the {FACTION}. Its lords reproach the Crown for failing the campaign they pledged to sustain."
                :outcome==-1?"{=BC_RallyDisappointed}Peace with {TARGET} has brought no victory to celebrate. The {FACTION} reproaches the Crown for ending the campaign without the advantage its lords had sought."
                :"{=BC_RallyCancelled}Events have overtaken the campaign against {TARGET}. The {FACTION} sets aside its appeal without reproach."),state,actual);
            BellumCivileLogger.Log($"Court rally concluded; war={a.Rally?.War?.WarKey}; realm={a.Realm?.StringId}; outcome={outcome}; reason={reason}; mood={actual}.");
        }
        private static TextObject RallyLabel(Kingdom target) => new TextObject("{=BC_RallyLabel}Rally against {TARGET}").SetTextVariable("TARGET",target?.Name??TextObject.GetEmpty());
        private static TextObject RallyText(CourtAgendaRecord a,TextObject text) => text.SetTextVariable("FACTION",a.Faction?.GetDisplayName()??TextObject.GetEmpty())
            .SetTextVariable("REALM",a.Realm?.Name??TextObject.GetEmpty()).SetTextVariable("TARGET",a.Rally?.Target?.Name??TextObject.GetEmpty())
            .SetTextVariable("DATE",CampaignTime.Days((float)a.ObjectiveData.DeadlineDay).ToString())
            .SetTextVariable("BOOST_DATE",CampaignTime.Days((float)(a.Rally?.Activated==true?a.Rally.Expires:CourtRallyRules.End(a.SessionDate.ToDays,a.ObjectiveData.DeadlineDay,CampaignTime.DaysInYear))).ToString());
        private static void ReportRally(CourtAgendaRecord a,TextObject text,CourtObjectiveState? state=null,float mood=0)
        {
            text=RallyText(a,text);
            if(state.HasValue) text=CourtObjectiveReports.WithMood(text,a.Faction?.GetDisplayName()??TextObject.GetEmpty(),mood,state.Value);
            BellumCivileNotifications.Show(text,CourtObjectiveReports.Color(state),primaryKingdom:a.Realm);
        }
        private static TextObject RallyStatus(CourtAgendaRecord a) => RallyText(a,new TextObject("{=BC_RallyStatus}Seek victory against {TARGET} by {DATE}. Participating houses and an NPC Crown favoring Glory gain up to +15 enthusiasm for this war until {BOOST_DATE}. Victory brings +10 approval; defeat or inconclusive peace -10; an unfinished campaign -5. Your decisions and forced settlements remain unchanged."));
        internal TextObject RallyHint(Kingdom realm,FactionObject faction) {var a=GetDisplayedAgenda(realm,faction);return IsRally(a)?RallyStatus(a):null;}
    }
}
