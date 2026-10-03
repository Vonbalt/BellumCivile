using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static class SettlementCauseLabelTests
{
    private static int _calls, _delta;
    private static Hero _first, _second;
    private static bool _notification, _throw;
    private static string _source;
    private static float _duration;
    private static bool Native(Hero originalHero, Hero originalGainedRelationWith, int relationChange, bool showQuickNotification)
    {
        _calls++; _first = originalHero; _second = originalGainedRelationWith; _delta = relationChange; _notification = showQuickNotification;
        var descriptor = AccessTools.Method(typeof(RelationMemoryService), "ResolveCapturedDescriptor").Invoke(null, new object[] { relationChange });
        _source = (string)AccessTools.Property(descriptor.GetType(), "SourceId").GetValue(descriptor);
        _duration = (float)AccessTools.Property(descriptor.GetType(), "DurationDays").GetValue(descriptor);
        if (_throw) throw new InvalidOperationException("native action failed");
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(RelationMemoryService).Assembly;
        var harmony = new Harmony("bellum.test.settlement_cause_labels");
        var helper = assembly.GetType("BellumCivile.Patches.NativeRelationCallLabels");
        var wrapper = AccessTools.Method(helper, "Apply");
        var native = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
        var siegeWrapper = AccessTools.Method(assembly.GetType("BellumCivile.Patches.SiegeAftermathRelationMemoryPatch"), "Apply");
        try
        {
            foreach (string name in new[] { "SettlementRescueRelationMemoryPatch", "SiegeAftermathRelationMemoryPatch", "DaughterQuestRelationMemoryPatch" })
                check(harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches." + name)).Patch().Count == 1, "Native event patch attaches: " + name);
            var battle = PatchProcessor.GetCurrentInstructions(AccessTools.Method(typeof(CharacterRelationCampaignBehavior), "MapEventEnded"));
            check(battle.Count(i => i.Calls(native)) == 2 && battle.Count(i => i.Calls(wrapper)) == 4,
                "Battle perks remain native while exactly four rescue reward calls are labeled");
            var sources = battle.Where(i => i.opcode == OpCodes.Ldstr).Select(i => i.operand as string).ToList();
            check(sources.SequenceEqual(new[] { RelationMemorySources.ProtectedSettlement, RelationMemorySources.ProtectedVillagers,
                RelationMemorySources.ProtectedVillagers, RelationMemorySources.ProtectedCaravan }), "Rescue labels follow the audited native call order");
            var siegeMethod = AccessTools.Method(typeof(SiegeAftermathCampaignBehavior), "OnSiegeAftermathApplied");
            check(siegeMethod.GetParameters()[2].ParameterType == typeof(SiegeAftermathAction.SiegeAftermath), "Siege wrapper loads the actual aftermath argument");
            var siege = PatchProcessor.GetCurrentInstructions(siegeMethod);
            check(siege.Count(i => i.Calls(siegeWrapper)) == 1 && siege.Count(i => (i.operand as MethodInfo)?.Name == "ApplyPlayerRelation") == 1,
                "Former-owner loss labeled without altering army-lord approval changes");
            harmony.Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyInternal"), prefix: new HarmonyMethod(typeof(SettlementCauseLabelTests), nameof(Native)));
            var first = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var second = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            foreach (string source in new[] { RelationMemorySources.ProtectedSettlement, RelationMemorySources.ProtectedVillagers,
                RelationMemorySources.ProtectedCaravan, RelationMemorySources.InterferedInAffairs })
            foreach (int delta in new[] { -10, -1, 0, 1, 5, 30 })
            {
                bool negative = source == RelationMemorySources.InterferedInAffairs;
                _calls = 0;
                wrapper.Invoke(null, new object[] { first, second, delta, true, negative ? null : source, negative ? source : null });
                check(_calls == 1 && _first == first && _second == second && _delta == delta && _notification,
                    "Cause wrapper forwards native arguments unchanged, once");
                string expected = (negative ? delta < 0 : delta > 0) ? source
                    : delta >= 0 ? RelationMemorySources.RecentFavor : RelationMemorySources.RecentGrievance;
                check(_source == expected, "Cause label applies only to the audited sign");
                var fallback = AccessTools.Method(typeof(RelationMemoryService), "BuildFallbackDescriptor").Invoke(null, new object[] { delta });
                check(_duration == (float)AccessTools.Property(fallback.GetType(), "DurationDays").GetValue(fallback), "Cause labels preserve magnitude-based duration");
            }
            foreach (var aftermath in new[] { SiegeAftermathAction.SiegeAftermath.Pillage, SiegeAftermathAction.SiegeAftermath.Devastate, SiegeAftermathAction.SiegeAftermath.ShowMercy })
            {
                siegeWrapper.Invoke(null, new object[] { first, second, -10, false, aftermath });
                check(_source == (aftermath == SiegeAftermathAction.SiegeAftermath.Pillage ? RelationMemorySources.PlunderedSettlement
                    : aftermath == SiegeAftermathAction.SiegeAftermath.Devastate ? RelationMemorySources.DevastatedSettlement : RelationMemorySources.RecentGrievance),
                    "Pillage, devastation and unexpected mercy penalties are distinguished");
            }
            using (RelationMemoryService.Begin("existing_explicit", 1))
            {
                wrapper.Invoke(null, new object[] { first, second, 5, false, RelationMemorySources.ProtectedCaravan, null });
                check(_source == "existing_explicit", "Existing explicit memory source retains priority");
            }
            _throw = true;
            try { wrapper.Invoke(null, new object[] { first, second, 5, false, RelationMemorySources.ProtectedCaravan, null }); } catch (TargetInvocationException) { }
            _throw = false; Native(first, second, 1, false);
            check(_source == RelationMemorySources.RecentFavor, "Cause scope restores after native exception");
            var rewrite = AccessTools.Method(helper, "Rewrite");
            bool rejected = false;
            try
            {
                ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { new CodeInstruction[0], new string[1], new string[1] })).ToList();
            }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Changed native call count fails rather than silently mislabeling rewards");
        }
        finally { _throw = false; harmony.UnpatchAll(harmony.Id); }
    }
}
