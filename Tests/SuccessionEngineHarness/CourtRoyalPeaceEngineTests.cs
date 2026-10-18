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

internal static class CourtRoyalPeaceEngineTests
{
    private static double _day;
    private static bool _valid;
    private static int _executions;
    private static Clan _crown;
    private static Clan _player;
    private static Hero _leader;
    private static Kingdom _realm;
    private static RealmPeaceEnforcementBehavior _executor;
    private static bool _wartime, _enabled, _ready, _throwExecution;
    private static int _cost, _charged;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays < _day; return false; }
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Wartime(ref bool __result) { __result = _wartime; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom>()); return false; }
    private static bool Executor(ref RealmPeaceEnforcementBehavior __result) { __result = _executor; return false; }
    private static bool Accession(ref CrownAccessionBehavior __result) { __result = null; return false; }
    private static bool Elective(ref ElectiveSuccessionBehavior __result) { __result = null; return false; }
    private static bool Preview(ref RoyalPeacePreview __result)
    { __result = new RoyalPeacePreview(_realm, _enabled, _cost, null, "", new List<Clan>(), 1, 1, _wartime, _ready); return false; }
    private static bool Execute(ref bool __result, ref string report)
    {
        _executions++;
        if (_throwExecution) throw new InvalidOperationException("injected settlement failure");
        _charged += _cost;
        report = "fixture enforcement"; __result = true; return false;
    }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.court_royal_peace");
        var type = typeof(CourtAgendaBehavior);
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = dayTicks.GetValue(null);
        dayTicks.SetValue(null, 1000L);
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtRoyalPeaceEngineTests), name));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(Future));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsPast"), nameof(Past));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(True));
            Patch(AccessTools.PropertyGetter(typeof(CrownAccessionBehavior), "Instance"), nameof(Accession));
            Patch(AccessTools.PropertyGetter(typeof(ElectiveSuccessionBehavior), "Instance"), nameof(Elective));
            Patch(AccessTools.Method(type, "PermanentRoyalRealm"), nameof(Valid));
            Patch(AccessTools.PropertyGetter(type, "RoyalExecutor"), nameof(Executor));
            Patch(AccessTools.Method(type, "RoyalPeaceNotice"), nameof(Skip));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "GetRealmPreview"), nameof(Preview));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "TryEnforceRealmPeace"), nameof(Execute));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "KingdomHasExternalWar"), nameof(Wartime));
            _day = 10; _valid = _enabled = _ready = _wartime = true; _executions = _charged = 0;
            _throwExecution = false; _cost = 150;
            _realm = Blank<Kingdom>(); _realm.StringId = "parent";
            _crown = Blank<Clan>(); _crown.StringId = "crown";
            _player = _crown; _leader = Blank<Hero>();
            _executor = new RealmPeaceEnforcementBehavior();
            var behavior = new CourtAgendaBehavior();
            var agendas = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(behavior);
            var cases = (List<CourtRoyalPeaceCase>)AccessTools.Field(type, "_royalPeaceCases").GetValue(behavior);
            CourtRoyalPeaceCase NewCase(string id) => new CourtRoyalPeaceCase { Id = id, Realm = _realm,
                Ruler = _leader, Assessed = true, RealmWide = true, Wartime = _wartime, QuotedCost = _cost,
                Created = _day, Expires = _day + 100 };
            var entry = NewCase("case");
            cases.Add(entry);
            var agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _crown, State = CourtAgendaState.Announced,
                SessionDate = CampaignTime.Days(20), ObjectiveData = new CourtObjectiveRecord { Kind = "policy" } };
            agendas.Add(agenda);
            bool Assign() => (bool)AccessTools.Method(type, "TryAssignRoyalPeace").Invoke(behavior, new object[] { entry, agenda });
            void Advance() => AccessTools.Method(type, "AdvanceRoyalPeace").Invoke(behavior, new object[] { agenda });
            agenda.State = CourtAgendaState.Deliberating;
            check(!Assign(), "Royal crisis preserves a filed ballot");
            agenda.State = CourtAgendaState.PursuingObjective;
            check(!Assign(), "Royal crisis preserves an ongoing objective");
            agenda.State = CourtAgendaState.Announced; agenda.ObjectiveData.Kind = "treason_decree";
            check(!Assign(), "Royal crisis preserves an announced treason decree");
            agenda.ObjectiveData.Kind = "policy"; agenda.PaidInfluence = 100;
            check(!Assign(), "Royal crisis preserves paid court business");
            agenda.PaidInfluence = 0;
            agenda.PaymentSettled = true;
            check(!Assign(), "Royal decree preserves settled court payments");
            agenda.PaymentSettled = false; agenda.SubstitutionInfluencePaid = 1;
            check(!Assign(), "Royal decree preserves a paid substitution");
            agenda.SubstitutionInfluencePaid = 0; agenda.CrisisInterventionPending = true;
            check(!Assign(), "Royal decree preserves pending crisis intervention");
            agenda.CrisisInterventionPending = false;
            check(Assign() && entry.Agenda == agenda && entry.Announced, "Royal crisis assigns and records its warning");
            check(agenda.ObjectiveData.TargetId == _realm.StringId && _charged == 0,
                "Announcement targets the whole realm without taking payment");
            double due = agenda.SessionDate.ToDays;
            int deliberation = (int)AccessTools.PropertyGetter(type.Assembly.GetType("BellumCivile.BellumCivileOptions"), "PoliticalDeliberationDays").Invoke(null, null);
            check(due >= 11 && due >= 10 + Math.Max(1, deliberation), "Royal crisis grants a full frozen notice period");
            check(!Assign(), "An announced crisis cannot be assigned twice");
            Advance();
            check(_executions == 0, "Royal crisis cannot execute before its date");
            _day = due;
            Advance();
            check(_executions == 0 && !entry.Started, "Royal decree waits until its full notice has elapsed");
            _day = due + 1;
            Advance();
            check(_executions == 1 && _charged == 150 && entry.Started && entry.Closed && entry.Result == "enforced", "Due decree executes once and seals its saved receipt");
            check(agenda.State == CourtAgendaState.Decreed && agenda.ResultApplied, "Successful enforcement concludes the Crown agenda");
            Advance();
            check(_executions == 1, "Completed decree cannot replay");
            entry = NewCase("cancel");
            cases.Add(entry);
            agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _crown, State = CourtAgendaState.NotProposed };
            agendas.Add(agenda);
            check(Assign(), "An idle Crown agenda can receive an emergency decree");
            _valid = false;
            Advance();
            check(entry.Closed && agenda.State == CourtAgendaState.Cancelled && _executions == 1,
                "Changed war or ruler cancels before any payment or enforcement");

            void Reset(string id, bool wartime = true)
            {
                cases.Clear(); agendas.Clear(); _day = 10; _executions = _charged = 0;
                _valid = _enabled = _ready = true; _throwExecution = false; _wartime = wartime;
                _cost = wartime ? 150 : 450; _player = _crown;
                entry = NewCase(id); cases.Add(entry);
                agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _crown, State = CourtAgendaState.NotProposed };
                agendas.Add(agenda);
            }
            void Due() { _day = agenda.SessionDate.ToDays + 1; Advance(); }
            void UnpaidCancellation(string label) => check(entry.Closed && !entry.Started
                && agenda.State == CourtAgendaState.Cancelled && _charged == 0 && _executions == 0, label);

            Reset("price_changed_before_assignment"); _cost++;
            check(!Assign() && entry.Agenda == null && _charged == 0, "Assignment rejects a stale quote without charging");
            Reset("price_increased"); check(Assign(), "Price-change fixture announces"); _cost++;
            Due(); UnpaidCancellation("Increased realm price cancels without calling the executor");
            Reset("price_decreased"); check(Assign(), "Cheaper-quote fixture announces"); _cost = 100;
            Due(); check(_charged == 100 && entry.Result == "enforced", "A decreased quote executes at the current lower price");
            Reset("wartime_ended"); check(Assign(), "Wartime fixture announces"); _wartime = false; _cost = 450;
            Due(); UnpaidCancellation("Ending foreign war cancels the wartime quote without payment");
            Reset("war_started", false); check(Assign(), "Peacetime fixture announces"); _wartime = true; _cost = 150;
            Due(); UnpaidCancellation("Starting foreign war cancels even a cheaper wartime replacement quote");
            Reset("unaffordable"); check(Assign(), "Affordability fixture announces"); _enabled = false;
            Due(); UnpaidCancellation("Loss of affordability cancels without taking payment");
            Reset("ruler_changed"); check(Assign(), "Ruler fixture announces"); _leader = Blank<Hero>();
            Due(); UnpaidCancellation("A new ruler cannot execute the previous ruler's decree");

            Reset("unsafe"); check(Assign(), "Unsafe settlement fixture announces"); _ready = false;
            Due();
            check(!entry.Closed && !entry.Started && _executions == 0 && _charged == 0,
                "Unsafe settlement defers without sealing an attempt or charging");
            _ready = true; Advance();
            check(entry.Result == "enforced" && _executions == 1, "Deferred decree executes once settlement becomes safe");
            Reset("unsafe_timeout"); check(Assign(), "Unsafe timeout fixture announces"); _ready = false;
            int reviewDays = (int)AccessTools.Field(type.Assembly.GetType("BellumCivile.CourtRoyalPeaceRules"), "ReviewDays").GetRawConstantValue();
            _day = agenda.SessionDate.ToDays + reviewDays; Advance();
            check(!entry.Closed && _charged == 0, "The final safe-settlement review day remains available");
            _day++; Advance(); UnpaidCancellation("Unsafe settlement eventually cancels without payment");

            Reset("exception"); check(Assign(), "Exception fixture announces"); _throwExecution = true;
            Due(); Advance();
            check(entry.Started && entry.Closed && entry.Result == "execution_exception" && _executions == 1,
                "A throwing executor seals its receipt and cannot replay");
            Reset("loaded_started"); check(Assign(), "Interrupted receipt fixture announces"); entry.Started = true;
            AccessTools.Method(type, "MaintainRoyalPeaceCases").Invoke(behavior, null);
            check(entry.Closed && entry.Result == "interrupted_execution" && _executions == 0,
                "Loading an already started decree cancels instead of retrying");
            Reset("legacy"); entry.RealmWide = false; entry.FeudId = "old_war_only_case";
            entry.Announced = true; entry.Agenda = agenda;
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = "royal_peace_decree" };
            agenda.State = CourtAgendaState.Announced;
            AccessTools.Method(type, "MaintainRoyalPeaceCases").Invoke(behavior, null);
            check(entry.Result == "legacy_decree", "Old single-war saved cases are explicitly cancelled, never widened");
            UnpaidCancellation("Legacy case cancellation takes no payment");

            foreach (bool player in new[] { true, false })
            {
                Reset(player ? "player" : "npc", false); cases.Clear();
                _player = player ? _crown : Blank<Clan>();
                check(behavior.TryScheduleRoyalPeace(_realm, _crown, out _), "Both player and NPC Crowns can schedule a peacetime decree");
                var scheduled = cases.Single();
                check(scheduled.RealmWide && !scheduled.Wartime && scheduled.QuotedCost == 450
                    && scheduled.PlayerInitiated == player && scheduled.Announced && _executions == 0 && _charged == 0,
                    "Scheduling saves scope, mode, quote and initiator without execution");
                check(!behavior.TryScheduleRoyalPeace(_realm, _crown, out _) && cases.Count == 1,
                    "A second announcement for the realm is rejected");
                agenda = scheduled.Agenda; Due();
                check(scheduled.Result == "enforced" && _executions == 1 && _charged == 450,
                    "Player and NPC decrees use the shared executor after notice");
            }
            var fields = typeof(CourtRoyalPeaceCase).GetFields(BindingFlags.Public | BindingFlags.Instance);
            check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), "Royal crisis persists all case and execution fields");
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)).ToList();
            check(ids.Distinct().Count() == ids.Count, "Royal crisis save field IDs remain unique");
            var expected = new[] { "Id", "Realm", "Attacker", "Ruler", "Created", "Expires", "FeudId", "Assessed",
                "Closed", "Agenda", "Announced", "Started", "Result", "TitleId", "Claimant", "Holder", "WarStarted",
                "RealmWide", "Wartime", "QuotedCost", "PlayerInitiated" };
            check(expected.Select((name, i) => fields.Single(f => f.Name == name).GetCustomAttributesData()
                .Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)
                .Select(Convert.ToInt32).SequenceEqual(Enumerable.Range(1, 21)),
                "Legacy save IDs stay intact and realm quote fields occupy IDs 18 through 21");
        }
        finally { h.UnpatchAll(h.Id); dayTicks.SetValue(null, oldTicks); }
    }
}
