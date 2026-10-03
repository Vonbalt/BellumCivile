using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtRoyalPeaceEngineTests
{
    private static double _day;
    private static bool _valid;
    private static int _executions;
    private static Clan _crown;
    private static Kingdom _realm;
    private static ClaimFeudRecord _feud;
    private static RealmPeaceEnforcementBehavior _executor;
    private static ClaimFeudWarBehavior _wars;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays < _day; return false; }
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool Strength(ref float __result) { __result = 1000; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Feud(ref ClaimFeudRecord __result) { __result = _feud; return false; }
    private static bool Executor(ref RealmPeaceEnforcementBehavior __result) { __result = _executor; return false; }
    private static bool Wars(ref ClaimFeudWarBehavior __result) { __result = _wars; return false; }
    private static bool Accession(ref CrownAccessionBehavior __result) { __result = null; return false; }
    private static bool Elective(ref ElectiveSuccessionBehavior __result) { __result = null; return false; }
    private static bool Preview(ref RoyalPeacePreview __result)
    { __result = new RoyalPeacePreview(_realm, true, 500, null, "", new List<Clan>()); return false; }
    private static bool Execute(ref bool __result, ref string report)
    { _executions++; report = "fixture enforcement"; __result = true; return false; }
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
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "CurrentTotalStrength"), nameof(Strength));
            Patch(AccessTools.PropertyGetter(typeof(CrownAccessionBehavior), "Instance"), nameof(Accession));
            Patch(AccessTools.PropertyGetter(typeof(ElectiveSuccessionBehavior), "Instance"), nameof(Elective));
            Patch(AccessTools.Method(type, "RoyalCaseValid"), nameof(Valid));
            Patch(AccessTools.Method(type, "RoyalFeud"), nameof(Feud));
            Patch(AccessTools.PropertyGetter(type, "RoyalExecutor"), nameof(Executor));
            Patch(AccessTools.PropertyGetter(type, "RoyalWars"), nameof(Wars));
            Patch(AccessTools.Method(type, "RoyalPeaceNotice"), nameof(Skip));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "GetClaimFeudPreview"), nameof(Preview));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "TryEnforceClaimFeudPeace"), nameof(Execute));
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "HasActiveWarForFeud"), nameof(True));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior).Assembly.GetType("BellumCivile.CourtRoyalPeaceRules"), "Substantial"), nameof(True));
            _day = 10; _valid = true; _executions = 0;
            _realm = Blank<Kingdom>(); _realm.StringId = "parent";
            _crown = Blank<Clan>(); _crown.StringId = "crown";
            _executor = new RealmPeaceEnforcementBehavior(); _wars = new ClaimFeudWarBehavior();
            _feud = new ClaimFeudRecord("feud", "parent", "a", "b", "title", FeudalClaimStrength.Strong, 100, 1, "claim", "test");
            var behavior = new CourtAgendaBehavior();
            var agendas = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(behavior);
            var cases = (List<CourtRoyalPeaceCase>)AccessTools.Field(type, "_royalPeaceCases").GetValue(behavior);
            var entry = new CourtRoyalPeaceCase { Id = "case", Realm = _realm, Attacker = _realm, FeudId = "feud", Assessed = true };
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
            check(Assign() && entry.Agenda == agenda && entry.Announced, "Royal crisis assigns and records its warning");
            double due = agenda.SessionDate.ToDays;
            int deliberation = (int)AccessTools.PropertyGetter(type.Assembly.GetType("BellumCivile.BellumCivileOptions"), "PoliticalDeliberationDays").Invoke(null, null);
            check(due >= 11 && due >= 10 + Math.Max(1, deliberation), "Royal crisis grants a full frozen notice period");
            check(!Assign(), "An announced crisis cannot be assigned twice");
            Advance();
            check(_executions == 0, "Royal crisis cannot execute before its date");
            _day = due + 1;
            Advance();
            check(_executions == 1 && entry.Started && entry.Closed && entry.Result == "enforced", "Due decree executes once and seals its saved receipt");
            check(agenda.State == CourtAgendaState.Decreed && agenda.ResultApplied, "Successful enforcement concludes the Crown agenda");
            Advance();
            check(_executions == 1, "Completed decree cannot replay");
            entry = new CourtRoyalPeaceCase { Id = "cancel", Realm = _realm, FeudId = "feud", Assessed = true };
            cases.Add(entry);
            agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _crown, State = CourtAgendaState.NotProposed };
            agendas.Add(agenda);
            check(Assign(), "An idle Crown agenda can receive an emergency decree");
            _valid = false;
            Advance();
            check(entry.Closed && agenda.State == CourtAgendaState.Cancelled && _executions == 1,
                "Changed war or ruler cancels before any payment or enforcement");
            var fields = typeof(CourtRoyalPeaceCase).GetFields(BindingFlags.Public | BindingFlags.Instance);
            check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), "Royal crisis persists all case and execution fields");
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)).ToList();
            check(ids.Distinct().Count() == ids.Count, "Royal crisis save field IDs remain unique");
        }
        finally { h.UnpatchAll(h.Id); dayTicks.SetValue(null, oldTicks); }
    }
}
