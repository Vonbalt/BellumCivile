using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CourtTitleGrantEngineTests
{
    private static double _day;
    private static bool _identity, _unchanged, _delivered, _afford, _throw, _partial;
    private static int _spends, _transfers, _refunds;
    private static FeudalTitleRecord _title;
    private static FeudalTitleBehavior _titles;
    private static string _formattedTitle;
    private static Clan _player;
    private static InquiryData _offer;
    private static readonly List<int> Relations = new List<int>();
    private static string _memory;
    private static float _memoryYears;
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Capture(InquiryData __0) { _offer = __0; return false; }
    private static bool RulerName(ref TextObject __result) { __result = new TextObject("King Derthert"); return false; }
    private static bool RealmName(ref TextObject __result) { __result = new TextObject("Vlandia"); return false; }
    private static bool Relation(int relationChange, string sourceId, float durationYears)
    { Relations.Add(relationChange); _memory = sourceId; _memoryYears = durationYears; return false; }
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
            _player = null; _offer = null; Relations.Clear(); _memory = null; _memoryYears = 0;
            _title=new FeudalTitleRecord("county","County",FeudalTitleType.County,"crown","crown","","","",0,0);
            _titles=Blank<FeudalTitleBehavior>();
            var recipient=Blank<Clan>(); recipient.StringId="recipient";
            var a=new CourtAgendaRecord {Realm=Blank<Kingdom>(),State=CourtAgendaState.PursuingObjective,
                ObjectiveData=new CourtObjectiveRecord {Kind="court_bestow_title"}};
            AccessTools.Method(typeof(CourtObjectiveRecord),"FreezeTerm").Invoke(a.ObjectiveData,new object[]{10d,84d});
            AccessTools.Method(typeof(CourtObjectiveRecord),"Activate").Invoke(a.ObjectiveData,null);
            a.TitleGrant=new CourtTitleGrantRecord {Realm=a.Realm,Recipient=recipient,TitleId="county",Deadline=84,OldLegal="crown",OldPractical="crown",
                Legal=true,Practical=true,Response=CourtTitleResponse.Accepted,RelationApplied=true,MoodApplied=true,
                Ruler=Blank<Hero>(),Beneficiary=Blank<Hero>(),Grantor=Blank<Clan>()};
            Agendas(b).Add(a); Journal(b).Add(a.TitleGrant); return a;
        }
        void Tick(CourtAgendaBehavior b) => AccessTools.Method(type,"MaintainTitleGrants").Invoke(b,null);
        bool Offer(CourtAgendaBehavior b) => (bool)AccessTools.Method(type, "ProcessTitleGrantOfferInquiry").Invoke(b, null);
        CourtAgendaRecord PlayerOffer(CourtAgendaBehavior b)
        {
            var result = Add(b); _player = result.TitleGrant.Recipient; _formattedTitle = "County of Neurvallia";
            result.TitleGrant.RelationApplied = false; result.TitleGrant.RelationGain = 25;
            return result;
        }
        CourtTitleGrantRecord Copy(CourtTitleGrantRecord original)
        {
            var copy = new CourtTitleGrantRecord();
            foreach (var field in typeof(CourtTitleGrantRecord).GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(copy, field.GetValue(original));
            return copy;
        }
        CourtAgendaRecord Matching(CourtAgendaBehavior b, CourtAgendaRecord original, bool petitionMotion)
        {
            var result = new CourtAgendaRecord { Realm = original.Realm, State = CourtAgendaState.PursuingObjective,
                Faction = petitionMotion ? Blank<FactionObject>() : null, TitleGrant = Copy(original.TitleGrant),
                ObjectiveData = new CourtObjectiveRecord { Kind = petitionMotion ? "court_petition_title" : "court_bestow_title" } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(result.ObjectiveData, new object[] { 10d, 84d });
            AccessTools.Method(typeof(CourtObjectiveRecord), "Activate").Invoke(result.ObjectiveData, null);
            Agendas(b).Add(result); return result;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(RulerName));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(RealmName));
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

            Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry"), nameof(Capture));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.BellumCivileNotifications"), "ShowPersonal", new[] { typeof(TextObject), typeof(Color) }), nameof(Skip));
            Patch(AccessTools.Method(typeof(RelationMemoryService), "ApplyChange"), nameof(Relation));
            b = new CourtAgendaBehavior(); a = PlayerOffer(b); Tick(b); Tick(b);
            check(_spends == 0 && _transfers == 0 && !a.ResultApplied, "Player title grant waits for consent before cost, transfer or result");
            check(((TextObject)AccessTools.Method(type, "TitleGrantStatus").Invoke(null, new object[] { a })).ToString().Contains("awaits your answer"),
                "Court status distinguishes pending recipient consent from a completed grant");
            check(Offer(b) && _offer.AffirmativeText == "Accept the honor" && _offer.NegativeText == "Refuse", "Recipient receives the two-button royal honor popup");
            check(_offer.Text.Contains("King Derthert") && _offer.Text.Contains(_formattedTitle) && _offer.Text.Contains("Vlandia")
                && _offer.Text.Contains("both lawful title and practical authority") && _offer.Text.Contains("Relations: -10")
                && _offer.Text.Contains("does not include its subordinate towns or castles"), "Offer names the ruler, formatted higher title, rights, realm and refusal consequence");
            var accepted = _offer;
            accepted.AffirmativeAction(); Tick(b); accepted.AffirmativeAction(); accepted.NegativeAction();
            check(a.TitleGrant.RecipientAccepted && a.ResultApplied && _spends == 1 && _transfers == 1,
                "Acceptance immediately uses normal delivery once; repeated callbacks cannot reverse it");
            check(Relations.SequenceEqual(new[] { 25 }) && _memory == RelationMemorySources.CourtTitleGrant && _memoryYears == 10,
                "Accepted player grant retains its original goodwill and memory duration");
            check(!Offer(b), "Delivered honor is not offered again");

            foreach (bool legal in new[] { true, false })
            {
                b = new CourtAgendaBehavior(); a = PlayerOffer(b); a.TitleGrant.Legal = legal; a.TitleGrant.Practical = !legal;
                Offer(b);
                check(_offer.Text.Contains(legal ? "lawful title only; practical authority remains unchanged"
                    : "practical authority only; lawful ownership remains unchanged"), "Offer accurately describes a single set of rights: legal=" + legal);
            }
            b = new CourtAgendaBehavior(); a = PlayerOffer(b); var matching = Matching(b, a, true);
            var unrelated = Matching(b, a, true); unrelated.TitleGrant.Ruler = Blank<Hero>();
            Journal(b).Add(matching.TitleGrant);
            Offer(b); var refused = _offer; refused.NegativeAction(); refused.NegativeAction(); refused.AffirmativeAction(); Tick(b);
            check(a.TitleGrant.RecipientRefused && a.State == CourtAgendaState.Cancelled && _spends == 0 && _transfers == 0 && _refunds == 0,
                "Refusal withdraws the grant without spending influence, transferring or refunding");
            check(matching.ResultApplied && matching.State == CourtAgendaState.Cancelled && matching.Faction.Mood == 0,
                "Matching nobility petition closes without blaming the Crown or changing faction approval");
            check(matching.TitleGrant.Response == CourtTitleResponse.Refused && !unrelated.ResultApplied,
                "Refusal seals duplicate authorizations without cancelling a different ruler's offer");
            check(Relations.SequenceEqual(new[] { -10 }) && _memory == RelationMemorySources.RefusedCourtTitleGrant && _memoryYears == 2,
                "Refusal applies one named personal grievance for two years");
            check(RelationMemorySources.GetDisplayText(_memory, null) == "Refused my title grant", "Refusal has its own relation-memory caption");
            Agendas(b).Remove(unrelated);
            var declinedCopy = Copy(a.TitleGrant);
            b = new CourtAgendaBehavior(); Journal(b).Add(declinedCopy); Tick(b);
            check(declinedCopy.RecipientRefused && declinedCopy.RefusalRelationApplied && !Offer(b) && Relations.Count == 1,
                "Restored refusal receipt cannot replay its popup, grant or penalty");

            b = new CourtAgendaBehavior(); a = PlayerOffer(b);
            var pendingCopy = Copy(a.TitleGrant); a.TitleGrant = pendingCopy;
            Journal(b).Clear(); Journal(b).Add(pendingCopy);
            Tick(b); check(Offer(b) && _spends == 0, "Restored unspent authorization still requires the player's answer");
            _afford = false; _offer.AffirmativeAction();
            check(pendingCopy.RecipientAccepted && !pendingCopy.PaymentAttempted, "Acceptance remains recorded if funding becomes unavailable before confirmation");
            var consentCopy = Copy(pendingCopy); a.TitleGrant = consentCopy; Journal(b).Clear(); Journal(b).Add(consentCopy);
            _afford = true; Tick(b);
            check(consentCopy.TransferComplete && _spends == 1 && !Offer(b), "Restored consent resumes delivery without asking or paying twice");
            b = new CourtAgendaBehavior(); a = PlayerOffer(b); _afford = false; Tick(b);
            check(!Offer(b) && _spends == 0, "Unaffordable royal authorization is not presented as a ready gift");
            _afford = true; check(Offer(b), "The pending gift is offered when the ruler can fund it");

            foreach (bool accept in new[] { true, false })
            {
                foreach (string invalidation in new[] { "ruler", "rights", "deadline", "removed", "external_grant" })
                {
                    b = new CourtAgendaBehavior(); a = PlayerOffer(b); Offer(b); var stale = _offer;
                    if (invalidation == "ruler") _identity = false;
                    if (invalidation == "rights") _unchanged = false;
                    if (invalidation == "deadline") _day = 85;
                    if (invalidation == "removed") Agendas(b).Clear();
                    if (invalidation == "external_grant") _delivered = true;
                    if (accept) stale.AffirmativeAction(); else stale.NegativeAction();
                    check(_spends == 0 && _transfers == 0 && Relations.Count == 0 && !a.TitleGrant.RecipientAccepted && !a.TitleGrant.RecipientRefused,
                        "Stale response has no transfer, charge or penalty: " + invalidation + "; accept=" + accept);
                }
            }
            b = new CourtAgendaBehavior(); a = PlayerOffer(b); a.TitleGrant.PaymentAttempted = a.TitleGrant.Paid = true; a.TitleGrant.RecoveryUntil = 41;
            Tick(b);
            check(a.TitleGrant.TransferComplete && !a.TitleGrant.RecipientAccepted && _spends == 0 && _transfers == 1 && !Offer(b),
                "Legacy already-paid grant retains recovery without a retroactive refusal gate");
            b = new CourtAgendaBehavior(); a = PlayerOffer(b); a.TitleGrant.PaymentAttempted = a.TitleGrant.Paid = a.TitleGrant.TransferAttempted = true;
            a.TitleGrant.RecoveryUntil = 41; _title.SetDeJureHolder("recipient"); Tick(b);
            check(a.TitleGrant.TransferComplete && _spends == 0 && _transfers == 1 && !Offer(b), "Partially delivered older grant completes without another consent or charge");

            var savedFields = typeof(CourtTitleGrantRecord).GetFields(BindingFlags.Instance | BindingFlags.Public);
            var saveIds = savedFields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(x => x.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)).ToArray();
            check(saveIds.Length == 30 && saveIds.OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 30)),
                "Recipient response and refusal receipts append save IDs without renumbering existing fields");
        }
        finally {h.UnpatchAll(h.Id);}
    }
}
