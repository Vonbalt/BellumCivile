using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static class PersonalKinshipTests
{
    private static readonly Dictionary<Hero, Hero> Fathers = new Dictionary<Hero, Hero>();
    private static readonly Dictionary<Hero, Hero> Mothers = new Dictionary<Hero, Hero>();
    private static readonly Dictionary<Hero, Hero> Spouses = new Dictionary<Hero, Hero>();
    private static Hero _notable;
    private static int _oldFoundation, _calls, _delta;
    private static bool _throw;
    private static string _source;
    private static Hero _first, _second;
    private static bool _notification;
    private static readonly Type Helper = typeof(RelationMemoryService).Assembly.GetType("BellumCivile.PersonalKinshipHelper");
    private static int Bonus(Hero a, Hero b) => (int)AccessTools.Method(Helper, "GetBonus").Invoke(null, new object[] { a, b });
    private static bool Father(Hero __instance, ref Hero __result) { Fathers.TryGetValue(__instance, out __result); return false; }
    private static bool Mother(Hero __instance, ref Hero __result) { Mothers.TryGetValue(__instance, out __result); return false; }
    private static bool Spouse(Hero __instance, ref Hero __result) { Spouses.TryGetValue(__instance, out __result); return false; }
    private static bool Notable(Hero __instance, ref bool __result) { __result = __instance == _notable; return false; }
    private static bool Foundation(Hero firstHero, Hero secondHero, ref int __result)
    { __result = _oldFoundation + Bonus(firstHero, secondHero); return false; }
    private static bool Political(ref int __result) { __result = 0; return false; }
    private static bool Native(Hero originalHero, Hero originalGainedRelationWith, int relationChange, bool showQuickNotification)
    {
        _calls++; _first = originalHero; _second = originalGainedRelationWith; _delta = relationChange; _notification = showQuickNotification;
        var d = AccessTools.Method(typeof(RelationMemoryService), "ResolveCapturedDescriptor").Invoke(null, new object[] { relationChange });
        _source = (string)AccessTools.Property(d.GetType(), "SourceId").GetValue(d);
        if (_throw) throw new InvalidOperationException("simulated native failure");
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.kinship_marriage");
        try
        {
            foreach (var p in new[] { "Father", "Mother", "Spouse", "IsNotable" })
                harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), p), prefix: new HarmonyMethod(typeof(PersonalKinshipTests), p == "IsNotable" ? nameof(Notable) : p));
            Hero New() => (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var grandpa = New(); var parent = New(); var aunt = New(); var son = New(); var sister = New();
            var half = New(); var cousin = New(); var unrelated = New();
            Fathers[parent] = grandpa; Fathers[aunt] = grandpa;
            Fathers[son] = parent; Fathers[sister] = parent; Mothers[son] = New(); Mothers[sister] = Mothers[son];
            Mothers[half] = Mothers[son]; Fathers[cousin] = aunt;
            foreach (var row in new[] {
                Tuple.Create(parent, son, 20), Tuple.Create(son, sister, 15), Tuple.Create(son, half, 15),
                Tuple.Create(grandpa, son, 5), Tuple.Create(aunt, son, 5), Tuple.Create(son, cousin, 5),
                Tuple.Create(son, unrelated, 0), Tuple.Create(unrelated, New(), 0), Tuple.Create(son, son, 0), Tuple.Create((Hero)null, son, 0) })
            {
                check(Bonus(row.Item1, row.Item2) == row.Item3, "Closest blood bond uses approved weight");
                check(Bonus(row.Item2, row.Item1) == row.Item3, "Blood bond is symmetric");
            }
            _notable = son; check(Bonus(parent, son) == 0, "Notables do not gain noble family foundation"); _notable = null;
            foreach (var partner in new[] { unrelated, cousin, sister, parent })
            {
                Spouses[son] = partner;
                int expected = partner == parent ? 20 : 15;
                check(Bonus(son, partner) == expected && Bonus(partner, son) == expected,
                    "Spouse +15 is symmetric and only strongest family bond applies");
            }
            Spouses.Clear();
            check(Bonus(son, unrelated) == 0 && Bonus(son, cousin) == 5, "Ending marriage removes only the spouse bond");
            var baseline = Helper.Assembly.GetType("BellumCivile.DynamicRelationBaselineHelper");
            harmony.Patch(AccessTools.Method(baseline, "CalculateFoundationScore"), prefix: new HarmonyMethod(typeof(PersonalKinshipTests), nameof(Foundation)));
            harmony.Patch(AccessTools.Method(baseline, "CalculatePoliticalConditionScore"), prefix: new HarmonyMethod(typeof(PersonalKinshipTests), nameof(Political)));
            var constants = Helper.Assembly.GetType("BellumCivile.BellumCivileConstants");
            int min = (int)AccessTools.Field(constants, "DynamicRelationBaselineMin").GetRawConstantValue();
            int max = (int)AccessTools.Field(constants, "DynamicRelationBaselineMax").GetRawConstantValue();
            int Clamp(int x) => Math.Max(min, Math.Min(max, x));
            foreach (int old in new[] { min - 30, min - 5, 0, max - 5, max + 30 })
            {
                _oldFoundation = old;
                int increase = (int)AccessTools.Method(baseline, "CalculateKinshipBaselineIncrease").Invoke(null, new object[] { parent, son });
                check(increase == Clamp(old + 20) - Clamp(old), "Kinship migration accounts for baseline caps");
                foreach (var partner in new[] { unrelated, cousin, sister, parent })
                {
                    int blood = Bonus(son, partner);
                    Spouses[son] = partner;
                    int spouseIncrease = (int)AccessTools.Method(baseline, "CalculateSpouseBaselineIncrease").Invoke(null, new object[] { son, partner });
                    check(spouseIncrease == Clamp(old + Bonus(son, partner)) - Clamp(old + blood),
                        "Spouse migration compensates only incremental baseline after caps and overlapping kinship");
                    Spouses.Clear();
                }
            }

            var patch = Helper.Assembly.GetType("BellumCivile.Patches.MarriageRelationMemoryPatch");
            check(harmony.CreateClassProcessor(patch).Patch().Count == 2, "Both native wedding gain paths patch successfully");
            var wrapper = AccessTools.Method(patch, "ApplyMarriageRelation");
            var marriage = AccessTools.Method(typeof(MarriageAction), "ApplyInternal");
            check(PatchProcessor.GetCurrentInstructions(marriage).Count(i => i.Calls(wrapper)) == 1, "Marriage model gain wrapped exactly once");
            check(PatchProcessor.GetCurrentInstructions(marriage).Count(i => (i.operand as MethodInfo)?.Name == "OnBeforeHeroesMarried") == 1,
                "Other marriage subscribers remain untouched");
            harmony.Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyInternal"), prefix: new HarmonyMethod(typeof(PersonalKinshipTests), nameof(Native)));
            foreach (int delta in new[] { 20, 30, 0, -5 })
            {
                _calls = 0;
                wrapper.Invoke(null, new object[] { son, unrelated, delta, false });
                check(_calls == 1 && _first == son && _second == unrelated && _delta == delta && !_notification,
                    "Wedding labels forward original native arguments once");
                check(_source == (delta > 0 ? RelationMemorySources.CelebratedMarriage
                    : delta == 0 ? RelationMemorySources.RecentFavor : RelationMemorySources.RecentGrievance),
                    "Only wedding gains receive celebration label");
            }
            _throw = true;
            try { wrapper.Invoke(null, new object[] { son, unrelated, 20, false }); } catch (TargetInvocationException) { }
            _throw = false;
            Native(son, unrelated, 1, false);
            check(_source == RelationMemorySources.RecentFavor, "Wedding label restores after native exception");
        }
        finally { harmony.UnpatchAll(harmony.Id); Fathers.Clear(); Mothers.Clear(); Spouses.Clear(); _notable = null; _throw = false; }
    }
}
