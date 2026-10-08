using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

internal static class MarriageProspectTests
{
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static int _day, _evaluations, _completed;
    private static object _acceptedMatch;
    private static bool _ready, _engaged, _eligible;
    private static bool Day(ref int __result) { __result = _day; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool House(Hero __instance, ref Clan __result) { __result = Houses[__instance]; return false; }
    private static bool Ready(ref bool __result) { __result = _ready; return false; }
    private static bool Eligible(ref bool __result) { __result = _eligible; return false; }
    private static bool Engaged(ref bool __result) { __result = _engaged; return false; }
    private static bool Evaluate(ref object __result) { _evaluations++; __result = _acceptedMatch; return false; }
    private static bool Complete() { _completed++; return false; }
    private static bool Year(ref int __result) { __result = 24; return false; }
    private static bool Need(ref float __result) { __result = 0; return false; }
    private static bool Skip() => false;
    private static bool NoCourt(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var first = Blank<Hero>(); first.StringId = "prospect_first";
        var second = Blank<Hero>(); second.StringId = "prospect_second";
        var a = Blank<Clan>(); a.StringId = "prospect_a";
        var b = Blank<Clan>(); b.StringId = "prospect_b";
        Houses.Clear(); Houses[first] = a; Houses[second] = b;
        var behavior = new StrategicMarriageBehavior();
        var records = new List<PendingMarriageProspect>();
        AccessTools.Field(typeof(StrategicMarriageBehavior), "_marriageProspects").SetValue(behavior, records);
        var process = AccessTools.Method(typeof(StrategicMarriageBehavior), "ProcessMarriageProspect");
        var reserved = AccessTools.Method(typeof(StrategicMarriageBehavior), "HasPendingProspect");
        var helper = typeof(StrategicMarriageBehavior).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var h = new Harmony("bellum.test.marriage_prospects");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(MarriageProspectTests), prefix));
        PendingMarriageProspect Add()
        {
            var p = new PendingMarriageProspect { Sponsor = a, First = first, Second = second,
                FirstHouse = a, SecondHouse = b, Destination = a, ExpiresDay = _day + 30 };
            records.Add(p);
            AccessTools.Field(typeof(StrategicMarriageBehavior), "_prospectByHero").SetValue(behavior, null);
            return p;
        }
        bool Process() => (bool)process.Invoke(behavior, new object[] { a });
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(StrategicMarriageBehavior), "CurrentDay"), nameof(Day));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(NoPlayer));
            Patch(AccessTools.PropertyGetter(typeof(BellumMarriageModel).Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableBellumStrategicMarriageLogic"), nameof(Yes));
            Patch(AccessTools.Method(typeof(Clan), "IsAtWarWith"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(NoCourt));
            Patch(AccessTools.Method(typeof(StrategicMarriageBehavior), "HasMarriageOfferFor"), nameof(Engaged));
            Patch(AccessTools.Method(helper, "MarriageProspectEligible"), nameof(Eligible));
            Patch(AccessTools.Method(helper, "MarriageParticipantReady"), nameof(Ready));
            Patch(AccessTools.Method(helper, "ReevaluateProspect"), nameof(Evaluate));
            Patch(AccessTools.Method(helper, "CalculateDynasticNeed"), nameof(Need));
            Patch(AccessTools.Method(typeof(StrategicMarriageBehavior), "CompleteStrategicMatch"), nameof(Complete));
            Patch(AccessTools.Method(typeof(StrategicMarriageBehavior), "GetCampaignDaysInYear"), nameof(Year));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            _day = 100; _ready = _engaged = false; _eligible = true; _evaluations = _completed = 0; _acceptedMatch = null;
            var p = Add();
            check((bool)reserved.Invoke(behavior, new object[] { first }) && (bool)reserved.Invoke(behavior, new object[] { second }),
                "Pending prospect reserves both participants");
            check(Process() && records.Count == 1 && _evaluations == 0 && p.NextAttemptDay == 103,
                "Unavailable match waits three days without a scoring search");
            _ready = true; _day = 102;
            check(Process() && _evaluations == 0, "Availability recovery respects the lightweight retry interval");
            _day = 103;
            check(Process() && _evaluations == 1 && records.Count == 0,
                "Ready match rechecks consent and cancels a refused pair");
            _ready = false; p = Add(); _day = p.ExpiresDay + 1;
            check(!Process() && records.Count == 0, "Expired prospect releases the annual scheduler instead of extending itself");
            p = Add(); Houses[first] = b;
            Process();
            check(records.Count == 0, "Household change cancels a prospect");
            Houses[first] = a; p = Add(); _engaged = true;
            check(Process() && records.Count == 0, "A competing engagement cancels without attempting a wedding");
            _engaged = false; p = Add(); _eligible = false;
            check(Process() && records.Count == 0, "Permanent eligibility loss cancels the pending match");
            _eligible = _ready = true;
            var assembly = typeof(BellumMarriageModel).Assembly;
            var outcomeType = assembly.GetType("BellumCivile.Behaviors.MarriageOutcome");
            var outcome = FormatterServices.GetUninitializedObject(outcomeType);
            AccessTools.Field(outcomeType, "<Destination>k__BackingField").SetValue(outcome, a);
            var matchType = assembly.GetType("BellumCivile.Behaviors.BellumMarriageMatch");
            _acceptedMatch = Activator.CreateInstance(matchType, true);
            AccessTools.PropertySetter(matchType, "Outcome").Invoke(_acceptedMatch, new[] { outcome });
            AccessTools.PropertySetter(matchType, "Suitor").Invoke(_acceptedMatch, new object[] { first });
            Add();
            check(Process() && records.Count == 0 && _completed == 1,
                "Accepted recovered pair proceeds to the guarded completion path exactly once");
            check(!Process() && _completed == 1, "Subsequent tick cannot repeat a resumed wedding");
            Add();
            AccessTools.Field(outcomeType, "<Destination>k__BackingField").SetValue(outcome, b);
            check(Process() && records.Count == 0 && _completed == 1,
                "Reevaluated household change cancels saved terms instead of silently reversing a delayed wedding");
            _acceptedMatch = null;
            var fields = typeof(PendingMarriageProspect).GetFields();
            var ids = new HashSet<int>();
            foreach (var field in fields)
            {
                var saved = field.GetCustomAttribute<SaveableFieldAttribute>();
                check(saved != null, "Prospect field is persisted: " + field.Name);
                if (saved != null) check(ids.Add(saved.LocalSaveId), "Prospect save field ID is unique: " + field.Name);
            }
            var scope = typeof(BellumMarriageModel).Assembly.GetType("BellumCivile.MarriageProspectEvaluation");
            bool Active() => (bool)AccessTools.PropertyGetter(scope, "Active").Invoke(null, null);
            check(!Active(), "Discovery relaxation is inactive outside scoring");
            using ((IDisposable)Activator.CreateInstance(scope, true))
            {
                check(Active(), "Discovery scope relaxes temporary state only while active");
                using ((IDisposable)Activator.CreateInstance(scope, true)) { }
                check(Active(), "Nested discovery retains the outer scope");
            }
            check(!Active(), "Discovery scope restores ordinary marriage checks");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
