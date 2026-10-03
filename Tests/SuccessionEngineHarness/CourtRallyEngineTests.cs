using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtRallyEngineTests
{
    private static double _day;
    private static bool _owner,_support,_live;
    private static Kingdom _realm;
    private static Clan _player;
    private static CourtAgendaBehavior _current;
    private static bool Current(ref CourtAgendaBehavior __result){__result=_current;return false;}
    private static bool Now(ref CampaignTime __result){__result=CampaignTime.Days((float)_day);return false;}
    private static bool Days(ref double __result){__result=_day;return false;}
    private static bool Owner(ref bool __result){__result=_owner;return false;}
    private static bool Support(ref bool __result){__result=_support;return false;}
    private static bool Live(ref bool __result){__result=_live;return false;}
    private static bool Valid(ref bool __result){__result=true;return false;}
    private static bool Player(ref Clan __result){__result=_player;return false;}
    private static bool Realm(ref Kingdom __result){__result=_realm;return false;}
    private static bool Clients(ref ClientKingdomBehavior __result){__result=null;return false;}
    private static bool Skip()=>false;
    internal static void Run(Action<bool,string> check)
    {
        T Blank<T>()=>(T)FormatterServices.GetUninitializedObject(typeof(T));
        var type=typeof(CourtAgendaBehavior);var h=new Harmony("bellum.test.court_rally");
        void Patch(MethodBase method,string prefix)=>h.Patch(method,prefix:new HarmonyMethod(typeof(CourtRallyEngineTests),prefix));
        List<CourtAgendaRecord> Rows(CourtAgendaBehavior b)=>(List<CourtAgendaRecord>)AccessTools.Field(type,"_agendas").GetValue(b);
        List<CourtAgendaRecord> Saved(CourtAgendaBehavior b)=>(List<CourtAgendaRecord>)AccessTools.Field(type,"_rallies").GetValue(b);
        CourtAgendaRecord Add(CourtAgendaBehavior b)
        {
            _day=30;_owner=_support=_live=true;_player=null;
            _current=b;
            _realm=Blank<Kingdom>();_realm.StringId="home";
            var enemy=Blank<Kingdom>();enemy.StringId="enemy";
            var war=new WarScoreRecord("rallywar","home","enemy",0,new WarScoreFiefSnapshotRecord[0]);
            var a=new CourtAgendaRecord {Realm=_realm,Faction=Blank<FactionObject>(),State=CourtAgendaState.PursuingObjective,
                ObjectiveData=new CourtObjectiveRecord {Kind="court_rally_victory"},Rally=new CourtRallyRecord {
                    War=war,Target=enemy,Attacker="home",Defender="enemy",WarKey=war.WarKey,WarStarted=war.StartedDay,Activated=true,Started=20,Expires=41}};
            AccessTools.Method(typeof(CourtObjectiveRecord),"FreezeTerm").Invoke(a.ObjectiveData,new object[]{10d,84d});
            AccessTools.Method(typeof(CourtObjectiveRecord),"Activate").Invoke(a.ObjectiveData,null);
            Rows(b).Add(a);Saved(b).Add(a);return a;
        }
        float Will(CourtAgendaBehavior b,Clan c,WarScoreRecord w,float baseline)=>(float)AccessTools.Method(type,"RallyEffective").Invoke(b,new object[]{c,w,baseline});
        void Tick(CourtAgendaBehavior b)=>AccessTools.Method(type,"MaintainRallies").Invoke(b,null);
        void Receipt(CourtAgendaBehavior b,WarScoreRecord w,bool verified,bool white,string victor,float net)=>AccessTools.Method(type,"RecordRallyOutcome").Invoke(b,new object[]{w,verified,white,victor,net,"fixture"});
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"Now"),nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"ToDays"),nameof(Days));
            Patch(AccessTools.PropertyGetter(typeof(Clan),"PlayerClan"),nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan),"Kingdom"),nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(ClientKingdomBehavior),"Instance"),nameof(Clients));
            Patch(AccessTools.PropertyGetter(type,"Current"),nameof(Current));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior),"IsRevampEnabled"),nameof(Valid));
            Patch(AccessTools.Method(type,"RallyOwner"),nameof(Owner));
            Patch(AccessTools.Method(type,"ReceivesPoliticalSupport"),nameof(Support));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.CourtPeaceObjectiveSource"),"EligibleWar"),nameof(Live));
            Patch(AccessTools.Method(type,"ReportRally"),nameof(Skip));
            var b=new CourtAgendaBehavior();var a=Add(b);var clan=Blank<Clan>();
            check(Will(b,clan,a.Rally.War,40)==55,"Production rally applies to indexed original war");
            check(Will(b,clan,a.Rally.War,95)==100,"Snapshot near cap gains only five");
            var other=new WarScoreRecord("other","home","enemy",1,new WarScoreFiefSnapshotRecord[0]);
            check(Will(b,clan,other,40)==40,"Restarted/other war cannot inherit rally");
            _support=false;check(Will(b,clan,a.Rally.War,40)==40,"Departed or unaligned participant loses bonus");_support=true;
            _player=clan;check(Will(b,clan,a.Rally.War,40)==40,"Player preference is not forced");_player=null;
            _day=41;check(Will(b,clan,a.Rally.War,40)==40,"Season expiry removes bonus without changing base");
            check(!a.ResultApplied,"Objective remains open after encouragement expires");
            check((bool)AccessTools.Method(type,"RallyUsed").Invoke(b,new object[]{a.Realm,a.Rally.War,null}),"Used receipt prevents a second rally within the same term");
            _day=84;
            check(!(bool)AccessTools.Method(type,"RallyUsed").Invoke(b,new object[]{a.Realm,a.Rally.War,null}),"Old receipt does not block selection exactly at the next term boundary");
            _day=60;Receipt(b,a.Rally.War,true,false,"home",0);Tick(b);Tick(b);
            check(a.ResultApplied && a.Faction.Mood==10,"Verified victory after boost expiry rewards once");
            b=new CourtAgendaBehavior();a=Add(b);Rows(b).Clear();AccessTools.Field(type,"_rallyIndex").SetValue(b,null);
            check(Will(b,clan,a.Rally.War,40)==55,"Saved rally rebuilds index independently of visible agenda");
            Receipt(b,a.Rally.War,true,true,null,30);Tick(b);
            check(a.Faction.Mood==-10,"White peace fails regardless of occupations");
            b=new CourtAgendaBehavior();a=Add(b);_day=85;Tick(b);Receipt(b,a.Rally.War,true,false,"home",0);Tick(b);
            check(a.Faction.Mood==-5,"Unfinished term cannot later receive victory reward");
            b=new CourtAgendaBehavior();a=Add(b);_day=83;Receipt(b,a.Rally.War,true,false,null,20);_day=85;Tick(b);
            check(a.Faction.Mood==10,"Saved timely receipt survives delayed maintenance");
            b=new CourtAgendaBehavior();a=Add(b);AccessTools.Method(type,"BeginRallySettlement").Invoke(b,new object[]{a.Rally.War});_live=false;Tick(b);
            check(!a.ResultApplied,"Intermediate peace callback waits for treaty delivery");
            Receipt(b,a.Rally.War,true,false,"home",0);Tick(b);check(a.Faction.Mood==10,"Receipt wins over closed-war cancellation");
            b=new CourtAgendaBehavior();a=Add(b);AccessTools.Method(type,"BeginRallySettlement").Invoke(b,new object[]{a.Rally.War});_live=false;_day=32;Tick(b);
            check(a.ResultApplied && a.Faction.Mood==0,"Interrupted delivery expires neutrally, not as victory");
            b=new CourtAgendaBehavior();a=Add(b);_owner=false;Tick(b);check(a.Faction.Mood==0 && a.ResultApplied,"Invalid faction cancels without punishment");
            b=new CourtAgendaBehavior();a=Add(b);a.Rally.Defender="replaced";
            check(Will(b,clan,a.Rally.War,40)==40,"Retargeted original record cannot retain effect");
            Receipt(b,a.Rally.War,true,false,"home",0);Tick(b);check(a.Faction.Mood==0,"Retargeted war does not earn victory");
            b=new CourtAgendaBehavior();a=Add(b);Receipt(b,a.Rally.War,false,false,null,100);Tick(b);
            check(a.Faction.Mood==0 && a.ResultApplied,"Unverified terms never award apparent gains");
            var settlement=AccessTools.Method(type.Assembly.GetType("BellumCivile.CourtRallySettlement"),"Record");
            foreach(int paid in new[]{0,25,50,100})
            {
                b=new CourtAgendaBehavior();a=Add(b);
                var proposal=new TreatyProposalRecord(a.Rally.War.WarKey,"home","enemy","home",20,50,false);
                var money=new TreatyTermRecord(TreatyTermType.Reparations,20,goldAmount:100,fromKingdomId:"enemy",toKingdomId:"home");
                proposal.AddTerm(money);
                var gold=new Dictionary<TreatyTermRecord,int>{{money,paid}};
                settlement.Invoke(null,new object[]{a.Rally.War,proposal,gold,new HashSet<TreatyTermRecord>()});
                check(!a.Rally.OutcomeRecorded,"Unapplied proposal cannot create a result");
                proposal.SetState(TreatyProposalState.Applied,"fixture");
                settlement.Invoke(null,new object[]{a.Rally.War,proposal,gold,new HashSet<TreatyTermRecord>()});Tick(b);
                check(a.Faction.Mood==(paid>=50?10:-10),"Actual paid fraction, not promised reparations, decides material victory");
            }
            foreach(bool delivered in new[]{false,true})
            {
                b=new CourtAgendaBehavior();a=Add(b);
                var proposal=new TreatyProposalRecord(a.Rally.War.WarKey,"home","enemy","home",20,100,false);
                var term=new TreatyTermRecord(TreatyTermType.MakeClientKingdom,80,fromKingdomId:"enemy",toKingdomId:"home");
                proposal.AddTerm(term);proposal.SetState(TreatyProposalState.Applied,"fixture");
                var done=new HashSet<TreatyTermRecord>();if(delivered)done.Add(term);
                settlement.Invoke(null,new object[]{a.Rally.War,proposal,new Dictionary<TreatyTermRecord,int>(),done});Tick(b);
                check(a.Faction.Mood==(delivered?10:0),"Structural result requires successful native delivery receipt");
            }
        }
        finally{h.UnpatchAll(h.Id);}
    }
}
