using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

internal static class MarriageHealthCacheTests
{
    private static Clan _clan;
    private static Hero _mother, _father, _spare;
    private static int _reads, _claimReads, _politicsReads;
    private static float _age;
    private static bool _alive;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Alive(ref bool __result) { __result = _alive; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Age(ref float __result) { __result = _age; return false; }
    private static bool Female(Hero __instance, ref bool __result) { __result = __instance == _mother; return false; }
    private static bool Clan(ref Clan __result) { __result = _clan; return false; }
    private static bool Leader(ref Hero __result) { __result = _father; return false; }
    private static bool Claim(List<string> reasons, ref float __result)
    { _claimReads++; reasons.Add("incoming claim +40"); __result = 40; return false; }
    private static bool Politics(Clan house, ref float __result)
    { _politicsReads++; __result = house == _clan ? 35 : 70; return false; }
    private static bool Spouse(Hero __instance, ref Hero __result)
    { __result = __instance == _mother ? _father : __instance == _father ? _mother : null; return false; }
    private static bool Prospect(Hero hero, ref bool __result) { __result = hero == _spare; return false; }
    private static bool Heroes(ref MBReadOnlyList<Hero> __result)
    { _reads++; __result = new MBReadOnlyList<Hero>(new List<Hero> { _mother, _father, _spare }); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var helper = typeof(StrategicMarriageBehavior).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var contextType = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
        var healthField = AccessTools.Field(contextType, "_health");
        object Context()
        {
            var context = FormatterServices.GetUninitializedObject(contextType);
            healthField.SetValue(context, Activator.CreateInstance(healthField.FieldType));
            var claimsField = AccessTools.Field(contextType, "_claimValues");
            claimsField.SetValue(context, Activator.CreateInstance(claimsField.FieldType));
            var politicsField = AccessTools.Field(contextType, "Politics");
            politicsField.SetValue(context, Activator.CreateInstance(politicsField.FieldType));
            return context;
        }
        var healthMethod = AccessTools.Method(contextType, "Health");
        var harmony = new Harmony("bellum.test.marriage_health_cache");
        void Patch(MethodBase target, string method) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(MarriageHealthCacheTests), method));
        _clan = Blank<Clan>(); _mother = Blank<Hero>(); _father = Blank<Hero>(); _spare = Blank<Hero>();
        _reads = 0; _claimReads = 0; _politicsReads = 0; _age = 30; _alive = true;
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsLord"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsActive"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsFemale"), nameof(Female));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(Clan));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Spouse"), nameof(Spouse));
            Patch(AccessTools.Method(helper, "HouseholdMarriageProspect"), nameof(Prospect));
            Patch(AccessTools.Method(helper, "CalculateDirectionalTitleClaimMarriageValue"), nameof(Claim));
            Patch(AccessTools.Method(contextType, "CalculatePoliticalValue"), nameof(Politics));
            var context = Context();
            var health = healthMethod.Invoke(context, new object[] { _clan });
            float Risk(object value) => (float)AccessTools.Field(value.GetType(), "Risk").GetValue(value);
            check(Risk(health) == 0, "Captive/inactive but living reproductive family is not erased from health");
            for (int i = 0; i < 10000; i++)
                check(ReferenceEquals(health, healthMethod.Invoke(context, new object[] { _clan })), "Search reuses the same health snapshot");
            check(_reads == 1, "Ten thousand evaluations scan the household just once");
            var claimMethod = AccessTools.Method(contextType, "IncomingClaimValue");
            var reasons = new List<string>();
            for (int i = 0; i < 1000; i++)
            {
                reasons.Clear();
                check((float)claimMethod.Invoke(context, new object[] { _clan, _spare, reasons, true }) == 40
                    && reasons.Count == 1, "Cached incoming claim preserves score and reason without duplicate lines");
            }
            check(_claimReads == 1 && _reads == 1, "One thousand incoming-carrier evaluations perform one claim scan");
            claimMethod.Invoke(context, new object[] { _clan, _mother, reasons, true });
            check(_claimReads == 2, "Different claim carriers have independent cache entries");
            claimMethod.Invoke(context, new object[] { _clan, _spare, reasons, false });
            check(_claimReads == 3, "Non-reproductive matches do not reuse a potential-descendant claim forecast");
            var other = Blank<Clan>();
            var politicalMethod = AccessTools.Method(contextType, "PoliticalValue");
            for (int i = 0; i < 1000; i++)
                check((float)politicalMethod.Invoke(context, new object[] { _clan, other }) == 35,
                    "House-pair political result is reusable across possible couples");
            check(_politicsReads == 1, "One thousand same-direction pairs require one political evaluation");
            check((float)politicalMethod.Invoke(context, new object[] { other, _clan }) == 70 && _politicsReads == 2,
                "Reverse direction has its own cached interests, not the other house's urgency");
            _age = 44;
            var later = healthMethod.Invoke(Context(), new object[] { _clan });
            check(Risk(later) > .6f && _reads == 2, "Next search sees aging without a global cache invalidation scheme");
            _alive = false;
            var dead = healthMethod.Invoke(Context(), new object[] { _clan });
            check((int)AccessTools.Field(dead.GetType(), "Adults").GetValue(dead) == 0 && _reads == 3,
                "Next search sees deaths and excludes dead family members");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
