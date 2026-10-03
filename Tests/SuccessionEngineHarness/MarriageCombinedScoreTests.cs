using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class MarriageCombinedScoreTests
{
    private static Hero _first, _second;
    private static Clan _a, _b;
    private static float _politicsA, _politicsB, _claim;
    private static int _tierA, _tierB, _claimCalls;
    private static bool House(Hero __instance, ref Clan __result) { __result = __instance == _first ? _a : _b; return false; }
    private static bool Tier(Clan __instance, ref int __result) { __result = __instance == _a ? _tierA : _tierB; return false; }
    private static bool Age(ref float __result) { __result = 30; return false; }
    private static bool ZeroInt(ref int __result) { __result = 0; return false; }
    private static bool ZeroFloat(ref float __result) { __result = 0; return false; }
    private static bool Blood(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Court(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool Politics(Clan house, ref float __result) { __result = house == _a ? _politicsA : _politicsB; return false; }
    private static bool Claim(ref float __result) { _claimCalls++; __result = _claim; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var assembly = typeof(StrategicMarriageBehavior).Assembly;
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var contextType = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
        var healthType = helper.GetNestedType("HouseHealth", BindingFlags.NonPublic);
        var outcomeType = assembly.GetType("BellumCivile.Behaviors.MarriageOutcome");
        var score = AccessTools.Method(helper, "EvaluateHouse");
        _first = Blank<Hero>(); _second = Blank<Hero>(); _a = Blank<Clan>(); _b = Blank<Clan>();
        _a.StringId = "a"; _b.StringId = "b";
        var home = Blank<Kingdom>(); var foreign = Blank<Kingdom>();
        var harmony = new Harmony("bellum.test.marriage_combined");
        void Patch(MethodBase target, string method) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(MarriageCombinedScoreTests), method));
        void Zero(MethodInfo target) => Patch(target, target.ReturnType == typeof(int) ? nameof(ZeroInt) : nameof(ZeroFloat));
        void Near(float actual, float expected, string label) => check(Math.Abs(actual - expected) < .001f, label);
        float[] Scores(float riskA, float riskB, bool domestic = true, bool fertile = true)
        {
            var context = FormatterServices.GetUninitializedObject(contextType);
            foreach (string fieldName in new[] { "Realms", "_health", "CrownHeirs" })
            {
                var field = AccessTools.Field(contextType, fieldName);
                field.SetValue(context, Activator.CreateInstance(field.FieldType));
            }
            var realms = (IDictionary)AccessTools.Field(contextType, "Realms").GetValue(context);
            realms[_a] = home; realms[_b] = domestic ? home : foreign;
            var health = (IDictionary)AccessTools.Field(contextType, "_health").GetValue(context);
            foreach (var clan in new[] { _a, _b })
            {
                var snapshot = FormatterServices.GetUninitializedObject(healthType);
                AccessTools.Field(healthType, "Risk").SetValue(snapshot, clan == _a ? riskA : riskB);
                health[clan] = snapshot;
            }
            var outcome = FormatterServices.GetUninitializedObject(outcomeType);
            AccessTools.Field(outcomeType, "<Destination>k__BackingField").SetValue(outcome, _a);
            AccessTools.Field(outcomeType, "<HasReproductiveOpportunity>k__BackingField").SetValue(outcome, fertile);
            var reasons = new List<string>();
            _claimCalls = 0;
            float a = (float)score.Invoke(null, new object[] { _first, _second, outcome, context, reasons });
            float b = (float)score.Invoke(null, new object[] { _second, _first, outcome, context, reasons });
            check(_claimCalls == 1, "Only the receiving house evaluates incoming claim value");
            return new[] { a, b };
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Tier"), nameof(Tier));
            Zero(AccessTools.Method(typeof(Clan), "GetRelationWithClan"));
            Zero(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(Court));
            Patch(AccessTools.Method(helper, "AreKingdomsAllied"), nameof(No));
            Patch(AccessTools.Method(contextType, "IsBloodMember"), nameof(Blood));
            Patch(AccessTools.Method(contextType, "HouseholdDepartureCost"), nameof(ZeroFloat));
            Patch(AccessTools.Method(contextType, "PoliticalValue"), nameof(Politics));
            Patch(AccessTools.Method(contextType, "IncomingClaimValue"), nameof(Claim));
            _tierA = _tierB = 3; _politicsA = _politicsB = 29.5f; _claim = 0;
            var healthy = Scores(0, 0);
            Near(healthy[0], 124.5f, "Healthy ordinary houses: receiver combines continuity and partner utility once");
            Near(healthy[1], 144.5f, "Healthy ordinary houses: donor combines bloodline, kinship and partner utility once");
            _politicsA = _politicsB = 0;
            var repeat = Scores(0, 0);
            Near(repeat[0], 95, "No new political-tie value leaves baseline receiver at acceptance floor");
            Near(repeat[1], 115, "Healthy donor values an ordinary bloodline marriage without requiring a special political objective");
            _tierA = 5; _tierB = 3;
            var survival = Scores(1, 0);
            Near(survival[0], 186, "Endangered receiver relaxes status preference without a second urgency threshold");
            Near(survival[1], 135, "Other house receives its own prestige and kinship, not the receiver's urgency");
            var infertile = Scores(1, 0, fertile: false);
            Near(infertile[0], 66, "Non-reproductive pairing has no survival bonus");
            _tierA = _tierB = 3; _claim = 53;
            var birthright = Scores(0, 0);
            Near(birthright[0], 148, "Immediate lawful birthright enters receiving score once");
            Near(birthright[1], 115, "Birth-house score does not receive the departing relative's claim benefit");
            _claim = 9.5f;
            Near(Scores(0, 0)[0], 104.5f, "Discounted inheritance is not promoted to full immediate claim value");
            _claim = 0; _politicsA = 80; _politicsB = 45;
            var protection = Scores(0, 0, domestic: false);
            Near(protection[0], 125, "Threatened foreign receiver values an attainable political tie");
            Near(protection[1], 110, "Secure foreign donor combines useful political ties and outgoing kinship");
            _politicsB = 70;
            Near(Scores(0, 0, domestic: false)[1], 135,
                "Secure donor can consent when the foreign political tie is useful");
            Near(Scores(0, 1, domestic: false)[1], 47.5f,
                "Endangered donor protects its line despite the same political opportunity");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
