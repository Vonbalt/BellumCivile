using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class CourtTitleGrantEngineTests
{
    private static double _day;
    private static bool _identity, _unchanged, _delivered, _afford, _throw, _partial;
    private static int _spends, _transfers, _refunds;
    private static FeudalTitleRecord _title;
    private static FeudalTitleBehavior _titles;
    private static string _formattedTitle;
    private static bool TitleName(FeudalTitleRecord title, ref string __result)
    {
        if (title != _title) throw new InvalidOperationException("Unexpected title formatted for court grant");
        __result = _formattedTitle; return false;
    }
    private static bool ClanName(ref TextObject __result) { __result = new TextObject("fen Penraic"); return false; }
    private static bool DateText(ref string __result) { __result = "Spring 1"; return false; }
    private static bool Now(ref CampaignTime __result) { __result=CampaignTime.Days((float)_day); return false; }
    private static bool Days(ref double __result) { __result=_day; return false; }
    private static bool Identity(ref bool __result) { __result=_identity; return false; }
    private static bool Unchanged(ref bool __result) { __result=_unchanged; return false; }
    private static bool Delivered(ref bool __result) { __result=_delivered; return false; }
    private static bool Afford(ref bool __result) { __result=_afford; return false; }
    private static bool Spend(ref bool __result) { _spends++; __result=true; return false; }
    private static bool Refund() { _refunds++; return false; }
    private static bool Title(ref FeudalTitleRecord __result) { __result=_title; return false; }
    private static bool Titles(ref FeudalTitleBehavior __result) { __result=_titles; return false; }
    private static bool Valid(ref bool __result) { __result=true; return false; }
    private static bool Skip() => false;
    private static bool Transfer(ref bool __result)
    {
        _transfers++;
        if (_partial) _title.SetDeJureHolder("recipient");
        if (_throw) throw new InvalidOperationException("Injected title delivery interruption");
        _title.SetDeJureHolder("recipient"); _title.SetDeFactoHolder("recipient");
        _delivered=true; __result=true; return false;
    }
    internal static void Run(Action<bool,string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var type=typeof(CourtAgendaBehavior);
        var source=type.Assembly.GetType("BellumCivile.CourtTitleGrantObjectiveSource");
        var budget=type.Assembly.GetType("BellumCivile.NpcInfluenceBudgetService");
        var h=new Harmony("bellum.test.court_title_grant");
        void Patch(MethodBase method,string name) => h.Patch(method,prefix:new HarmonyMethod(typeof(CourtTitleGrantEngineTests),name));
        List<CourtAgendaRecord> Agendas(CourtAgendaBehavior b) => (List<CourtAgendaRecord>)AccessTools.Field(type,"_agendas").GetValue(b);
        List<CourtTitleGrantRecord> Journal(CourtAgendaBehavior b) => (List<CourtTitleGrantRecord>)AccessTools.Field(type,"_titleDeliveries").GetValue(b);
        CourtAgendaRecord Add(CourtAgendaBehavior b)
        {
            _day=40; _identity=_unchanged=_afford=true; _delivered=_throw=_partial=false;
            _spends=_transfers=_refunds=0;
            _title=new FeudalTitleRecord("county","County",FeudalTitleType.County,"crown","crown","","","",0,0);
            _titles=Blank<FeudalTitleBehavior>();
            var recipient=Blank<Clan>(); recipient.StringId="recipient";
            var a=new CourtAgendaRecord {Realm=Blank<Kingdom>(),State=CourtAgendaState.PursuingObjective,
                ObjectiveData=new CourtObjectiveRecord {Kind="court_bestow_title"}};
            AccessTools.Method(typeof(CourtObjectiveRecord),"FreezeTerm").Invoke(a.ObjectiveData,new object[]{10d,84d});
            AccessTools.Method(typeof(CourtObjectiveRecord),"Activate").Invoke(a.ObjectiveData,null);
            a.TitleGrant=new CourtTitleGrantRecord {Realm=a.Realm,Recipient=recipient,TitleId="county",Deadline=84,OldLegal="crown",OldPractical="crown",
                Legal=true,Practical=true,Response=CourtTitleResponse.Accepted,RelationApplied=true,MoodApplied=true};
            Agendas(b).Add(a); Journal(b).Add(a.TitleGrant); return a;
        }
        void Tick(CourtAgendaBehavior b) => AccessTools.Method(type,"MaintainTitleGrants").Invoke(b,null);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"Now"),nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime),"ToDays"),nameof(Days));
            Patch(AccessTools.Method(type,"GrantIdentity"),nameof(Identity));
            Patch(AccessTools.Method(type,"GrantOwner"),nameof(Valid));
            Patch(AccessTools.Method(type,"GrantUnchanged"),nameof(Unchanged));
            Patch(AccessTools.Method(type,"GrantDelivered"),nameof(Delivered));
            Patch(AccessTools.Method(type,"ReportTitle"),nameof(Skip));
            Patch(AccessTools.Method(type,"ReportTitleSuccess"),nameof(Skip));
            Patch(AccessTools.Method(source,"Title"),nameof(Title));
            Patch(AccessTools.PropertyGetter(source,"Titles"),nameof(Titles));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior),"CompleteCourtTitleGrant"),nameof(Transfer));
            Patch(AccessTools.Method(budget,"CanAfford"),nameof(Afford));
            Patch(AccessTools.Method(budget,"TrySpend"),nameof(Spend));
            Patch(AccessTools.Method(budget,"Refund"),nameof(Refund));
            var b=new CourtAgendaBehavior(); var a=Add(b); Tick(b); Tick(b);
            check(a.ResultApplied && a.State==CourtAgendaState.Completed && _spends==1 && _transfers==1,"Title delivery charges and transfers once across repeated ticks");
            b=new CourtAgendaBehavior(); a=Add(b); _afford=false; Tick(b);
            check(!a.ResultApplied && _spends==0 && !a.TitleGrant.PaymentAttempted,"Insufficient influence waits without sealing a payment");
            _afford=true; Tick(b); check(a.ResultApplied && _spends==1,"Previously authorized affordable grant resumes without new willingness roll");
            b=new CourtAgendaBehavior(); a=Add(b); _identity=false; Tick(b);
            check(a.State==CourtAgendaState.Cancelled && _spends==0,"Changed ruler cancels before payment");
            b=new CourtAgendaBehavior(); a=Add(b); _unchanged=false; Tick(b);
            check(a.State==CourtAgendaState.Cancelled && _spends==0,"Changed rights or claim cancel before payment");
            b=new CourtAgendaBehavior(); a=Add(b); _throw=true; Tick(b);
            check(a.TitleGrant.Paid && !a.ResultApplied && _spends==1 && _transfers==1,"Interrupted paid transfer remains recoverable");
            Tick(b); check(_transfers==1,"Retry is not repeated in same tick");
            _day=40.25; _throw=false; Tick(b);
            check(a.ResultApplied && _spends==1 && _transfers==2,"Recovery resumes without paying twice");
            b=new CourtAgendaBehavior(); a=Add(b); _throw=true; Tick(b); _day=40.25; Tick(b); _day=40.5; Tick(b); Tick(b);
            check(a.TitleGrant.Response==CourtTitleResponse.Failed && _transfers==3 && _refunds==1,"Three failed unchanged transfers refund once and stop");
            b=new CourtAgendaBehavior(); a=Add(b); _throw=_partial=true; Tick(b); _day=40.25; Tick(b); _day=40.5; Tick(b);
            check(_refunds==0 && _title.DeJureHolderClanId=="recipient","Partly delivered rights are not silently reverted or refunded");
            b=new CourtAgendaBehavior(); a=Add(b); _throw=true; Tick(b); Agendas(b).Clear(); _throw=false; _day=40.25; Tick(b);
            check(a.TitleGrant.TransferComplete && _spends==1,"Paid journal recovers even if its agenda has been removed");
            b=new CourtAgendaBehavior(); a=Add(b); _delivered=true; Tick(b);
            check(a.ResultApplied && _spends==0 && _transfers==0,"External grant fulfills without second payment or transfer");
            b=new CourtAgendaBehavior(); a=Add(b);
            var petition=new CourtAgendaRecord {Realm=a.Realm,Faction=Blank<FactionObject>(),State=CourtAgendaState.PursuingObjective,
                TitleGrant=a.TitleGrant,ObjectiveData=new CourtObjectiveRecord {Kind="court_petition_title"}};
            AccessTools.Method(typeof(CourtObjectiveRecord),"FreezeTerm").Invoke(petition.ObjectiveData,new object[]{10d,84d});
            AccessTools.Method(typeof(CourtObjectiveRecord),"Activate").Invoke(petition.ObjectiveData,null);
            Agendas(b).Add(petition); Tick(b);
            check(a.ResultApplied && petition.ResultApplied && _spends==1 && petition.Faction.Mood==0,"One grant satisfies both motions without a second petition approval reward");
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper"), "FormatTitleName",
                new[] { typeof(FeudalTitleRecord) }), nameof(TitleName));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(ClanName));
            Patch(AccessTools.Method(typeof(CampaignTime), "ToString", Type.EmptyTypes), nameof(DateText));
            _title = new FeudalTitleRecord("uchalion", "Uchalion", FeudalTitleType.Duchy, "crown", "crown", "", "", "", 0, 0);
            _formattedTitle = "Petty Kingdom of Uchalion";
            a.TitleGrant.TitleId = _title.TitleId;
            string Label(bool isPetition) => ((TextObject)AccessTools.Method(type, "TitleGrantLabel")
                .Invoke(null, new object[] { _title?.TitleId ?? "missing_title", a.TitleGrant.Recipient, isPetition })).ToString();
            check(Label(false) == "Bestow Petty Kingdom of Uchalion on fen Penraic",
                "Crown grant uses the full hierarchy title name rather than its bare territorial root");
            check(Label(true) == "Petition for Petty Kingdom of Uchalion for fen Penraic",
                "Nobility title petitions use the same full title name");
            check(((TextObject)AccessTools.Method(type, "ExecutiveObjectiveText").Invoke(null, new object[] { a }))
                .ToString().Contains(_formattedTitle), "Completed title agendas resolve their full names when displayed");
            var text = (TextObject)AccessTools.Method(type, "TitleText").Invoke(null, new object[] {
                new CourtTitleGrantRecord { TitleId = _title.TitleId, Recipient = a.TitleGrant.Recipient }, new TextObject("{TITLE}") });
            check(text.ToString() == _formattedTitle, "Title-grant reports and inquiry details use the hierarchy name");
            _formattedTitle = "Royaume d'Uchalion";
            check(Label(false) == "Bestow Royaume d'Uchalion on fen Penraic",
                "Grant labels preserve the formatter's localized style instead of hardcoding an English rank");
            _title = null;
            check(Label(false) == "Bestow missing_title on fen Penraic", "Missing title retains the existing ID fallback");
            check(h.CreateClassProcessor(type.Assembly.GetType("BellumCivile.Patches.CourtTitleGrantReceiptPatch")).Patch()?.Count>0,"Ordinary hierarchy grant observer binds to installed method");
        }
        finally {h.UnpatchAll(h.Id);}
    }
}
