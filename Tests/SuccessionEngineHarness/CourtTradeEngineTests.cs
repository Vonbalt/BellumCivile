using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static class CourtTradeEngineTests
{
    private static double _day;
    private static bool _pair, _exists, _support;
    private static string _invalidation;
    private static bool Invalidation(ref string __result) { __result = _invalidation; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Days(ref double __result) { __result = _day; return false; }
    private static bool Valid(ref bool __result) { __result = true; return false; }
    private static bool Pair(ref bool __result) { __result = _pair; return false; }
    private static bool Exists(ref bool __result) { __result = _exists; return false; }
    private static bool Support(ref bool __result) { __result = _support; return false; }
    private static bool Skip() => false;
    private static bool Player(ref Clan __result) { __result = null; return false; }

    internal static void Run(Action<bool,string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var type = typeof(CourtAgendaBehavior);
        var source = type.Assembly.GetType("BellumCivile.CourtTradeObjectiveSource");
        var realm = Blank<Kingdom>(); realm.StringId = "trade_home";
        var target = Blank<Kingdom>(); target.StringId = "trade_foreign";
        var voter = Blank<Clan>();
        var h = new Harmony("bellum.test.court_trade");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtTradeEngineTests), name));
        CourtAgendaRecord Add(CourtAgendaBehavior b)
        {
            _day=40; _pair=true; _exists=false; _support=true; _invalidation=null;
            var a = new CourtAgendaRecord { Realm=realm, Faction=Blank<FactionObject>(), State=CourtAgendaState.PursuingObjective, TermDays=24,
                Trade=new CourtTradeRecord { Target=target, Activated=true, ActivatedDay=20 },
                ObjectiveData=new CourtObjectiveRecord { Kind="court_trade_agreement" } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] {10d,84d});
            ((List<CourtAgendaRecord>)AccessTools.Field(type,"_agendas").GetValue(b)).Add(a);
            return a;
        }
        void Tick(CourtAgendaBehavior b) => AccessTools.Method(type,"MaintainTradeObjectives").Invoke(b,null);
        void Signed(CourtAgendaBehavior b, Kingdom other = null) => AccessTools.Method(type,"OnCourtTradeSigned").Invoke(b,new object[] {other ?? target,realm});
        float Bonus(CourtAgendaBehavior b) => (float)AccessTools.Method(type,"TradeBonus").Invoke(b,new object[] {realm,target,voter});
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"Now"),nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Clan),"PlayerClan"),nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"ToDays"),nameof(Days));
            Patch(AccessTools.Method(type,"TradeOwner"),nameof(Valid));
            Patch(AccessTools.Method(source,"Pair"),nameof(Pair));
            Patch(AccessTools.Method(type,"TradeExists"),nameof(Exists));
            Patch(AccessTools.Method(type,"ReceivesPoliticalSupport"),nameof(Support));
            Patch(AccessTools.Method(type,"ReportTrade"),nameof(Skip));
            Patch(AccessTools.Method(type,"TradeInvalidation"),nameof(Invalidation));
            var b=new CourtAgendaBehavior(); var a=Add(b);
            check(Bonus(b)==15,"Trade bonus active for a participating house");
            _support=false; check(Bonus(b)==0,"Shared eligibility denies trade bonus"); _support=true;
            Signed(b); check(!a.ResultApplied,"Trade receipt without actual agreement cannot fulfill objective");
            _exists=true; Signed(b,Blank<Kingdom>()); check(!a.ResultApplied,"Unrelated trade signature ignored");
            Signed(b); check(a.State==CourtAgendaState.Completed && a.Faction.Mood==10,"Reversed pair signature fulfills trade with approval");
            Tick(b); Signed(b); check(a.Faction.Mood==10 && Bonus(b)==0,"Trade completion seals reward and ends support");
            b=new CourtAgendaBehavior(); a=Add(b); _day=85; _exists=true; Tick(b);
            check(a.ObjectiveData.State==CourtObjectiveState.Expired && a.Faction.Mood==-10,"Late agreement observation cannot invent timely completion");
            check((float)AccessTools.Method(type,"TradeRepeatWeight").Invoke(b,new object[] {realm,target})==.5f,
                "Failed trade target receives a modest selection penalty");
            _day=134;
            check((float)AccessTools.Method(type,"TradeRepeatWeight").Invoke(b,new object[] {realm,target})==1f,
                "Trade repetition penalty expires after two term lengths");
            b=new CourtAgendaBehavior(); a=Add(b); _invalidation="foreign_trade_slots_filled"; Tick(b);
            check(a.ObjectiveData.State==CourtObjectiveState.Cancelled && a.Faction.Mood==0,
                "Externally invalidated trade target cancels without expiry punishment");
            b=new CourtAgendaBehavior(); a=Add(b); a.Trade.SignedDay=80; _day=85; _exists=true; Tick(b);
            check(a.State==CourtAgendaState.Completed && a.Faction.Mood==10,"Saved timely signature survives interrupted completion across deadline");
            b=new CourtAgendaBehavior(); a=Add(b); _pair=false; _exists=true; Tick(b);
            check(a.ObjectiveData.State==CourtObjectiveState.Cancelled && a.Faction.Mood==0,"Clientage or invalid realm pair cannot award trade success");
            b=new CourtAgendaBehavior(); a=Add(b); _exists=true; Tick(b);
            check(a.State==CourtAgendaState.Completed,"Maintenance recovers agreement observed within term");
            b=new CourtAgendaBehavior(); a=Add(b); a.State=CourtAgendaState.Announced; a.Trade.Activated=false; _exists=true; Signed(b);
            check(a.ObjectiveData.Credit==CourtObjectiveCredit.FulfilledElsewhere,"Pre-session signature retains fulfilled-elsewhere credit");
            b=new CourtAgendaBehavior(); a=Add(b);
            AccessTools.Method(type,"OnCourtTradeWar").Invoke(b,new object[] {realm,target,DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision});
            check(a.ObjectiveData.State==CourtObjectiveState.Failed && a.Faction.Mood==-10,"Deliberate own offensive reproaches Crown once");
            Tick(b); check(a.Faction.Mood==-10,"War result cannot also receive expiry penalty");
            b=new CourtAgendaBehavior(); a=Add(b);
            AccessTools.Method(type,"OnCourtTradeWar").Invoke(b,new object[] {target,realm,DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision});
            check(a.ObjectiveData.State==CourtObjectiveState.Cancelled && a.Faction.Mood==0,"Enemy attack cancels without blame");
            b=new CourtAgendaBehavior(); a=Add(b); a.Faction.Mood=98; _exists=true; Signed(b);
            check(a.Faction.Mood==100,"Trade fulfillment respects mood cap");
            foreach (string name in new[] {"CourtTradeSupportPatch","CourtTradeProposalPatch"})
                check(h.CreateClassProcessor(type.Assembly.GetType("BellumCivile.Patches."+name)).Patch()?.Count>0,"Installed native trade patch binds: "+name);
            check(AccessTools.Property(typeof(CampaignEvents),"OnTradeAgreementSignedEvent")!=null,"Installed native signing event exists");
            var copy=new CourtTradeRecord();
            foreach (var field in typeof(CourtTradeRecord).GetFields()) field.SetValue(copy,field.GetValue(a.Trade));
            check(copy.Target==target && copy.SignedDay==40 && copy.Activated,"Field roundtrip preserves target and signature (not native save/load)");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
