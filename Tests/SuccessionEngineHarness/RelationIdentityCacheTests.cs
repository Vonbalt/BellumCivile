using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;

internal static class RelationIdentityCacheTests
{
    private static readonly Type PatchType = typeof(RelationMemoryService).Assembly.GetType("BellumCivile.Patches.PersonalRelationIdentityPatch");
    private static Hero _leader, _otherLeader;
    private static bool _tno, _throw, _nest, _restored;
    private static readonly List<Hero[]> Pairs = new List<Hero[]>();
    private static bool Enabled(ref bool __result) { __result = true; return false; }
    private static float _day;
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Effective(Hero hero1, Hero hero2, ref Hero effectiveHero1, ref Hero effectiveHero2)
    {
        effectiveHero1 = _tno ? hero1 : _leader;
        effectiveHero2 = _tno ? hero2 : _otherLeader;
        return false;
    }
    private static Hero[] Resolve(Hero a, Hero b)
    {
        var model = (DefaultDiplomacyModel)FormatterServices.GetUninitializedObject(typeof(DefaultDiplomacyModel));
        object[] args = { model, a, b, null, null };
        AccessTools.Method(PatchType, "ResolvePair").Invoke(null, args);
        return new[] { (Hero)args[3], (Hero)args[4] };
    }
    private static bool Action(Hero originalHero, Hero originalGainedRelationWith)
    {
        Hero hero = originalHero;
        Hero gainedRelationWith = originalGainedRelationWith;
        Pairs.Add(Resolve(hero, gainedRelationWith));
        if (_nest)
        {
            _nest = false;
            RelationMemoryService.ApplyChange(_leader, _otherLeader, 1, false, "nested", 1, RelationMemoryScope.House);
            _restored = Resolve(hero, gainedRelationWith)[0] == hero;
        }
        if (_throw) throw new InvalidOperationException("test action failure");
        return false;
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.relation_identity_cache");
        var previous = DynamicRelationBehavior.Instance;
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(RelationMemoryService).Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicRelationDrift"), prefix: new HarmonyMethod(typeof(RelationIdentityCacheTests), nameof(Enabled)));
            harmony.Patch(AccessTools.Method(typeof(DefaultDiplomacyModel), "GetHeroesForEffectiveRelation"), prefix: new HarmonyMethod(typeof(RelationIdentityCacheTests), nameof(Effective)));
            harmony.Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyInternal"), prefix: new HarmonyMethod(typeof(RelationIdentityCacheTests), nameof(Action)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(DynamicRelationBehavior), "CurrentDay"), prefix: new HarmonyMethod(typeof(RelationIdentityCacheTests), nameof(Day)));
            var native = AccessTools.Method(typeof(ChangeRelationAction), "ApplyInternal");
            harmony.Patch(native, transpiler: new HarmonyMethod(PatchType, "Transpiler"));
            var donationPatch = typeof(RelationMemoryService).Assembly.GetType("BellumCivile.Patches.PrisonerDonationFinalGainPatch");
            harmony.Patch(native, transpiler: new HarmonyMethod(donationPatch, "Transpiler"));
            check(PatchProcessor.GetCurrentInstructions(native).Count(i => i.Calls(AccessTools.Method(PatchType, "ResolvePair"))) == 1,
                "Native relation action rewrites exactly the identity lookup");
            check(PatchProcessor.GetCurrentInstructions(native).Count(i => i.Calls(AccessTools.Method(donationPatch, "LimitGain"))) == 1,
                "Personal identity patch composes with existing prisoner-donation cap");
            foreach (string method in new[] { "GetRelationIncreaseFactor", "SetPersonalRelation", "OnHeroRelationChanged" })
                check(PatchProcessor.GetCurrentInstructions(native).Count(i => (i.operand as MethodInfo)?.Name == method) == 1,
                    "Identity rewrite preserves exactly one native " + method + " call");

            var a = Blank<Hero>(); var b = Blank<Hero>(); _leader = Blank<Hero>(); _otherLeader = Blank<Hero>();
            foreach (bool tno in new[] { false, true })
            {
                _tno = tno; Pairs.Clear();
                RelationMemoryService.ApplyChange(a, b, 5, false, "personal", 1);
                check(Pairs.Single()[0] == a && Pairs.Single()[1] == b, "Bellum personal changes retain original heroes with or without TNO routing");
                Pairs.Clear();
                RelationMemoryService.ApplyChange(a, b, 5, false, "house", 1, RelationMemoryScope.House);
                check(Pairs.Single()[0] == (tno ? a : _leader), "House changes defer to active diplomacy identity routing");
                check(Resolve(a, b)[0] == (tno ? a : _leader), "Personal identity context does not leak into later native actions");
            }
            _tno = false; _nest = true;
            RelationMemoryService.ApplyChange(a, b, 5, false, "outer", 1);
            check(_restored, "Nested house action restores outer personal identity context");
            _throw = true;
            try { RelationMemoryService.ApplyChange(a, b, 5, false, "failing", 1); } catch (InvalidOperationException) { }
            _throw = false;
            check(Resolve(a, b)[0] == _leader, "Exception restores identity context");

            var behavior = new DynamicRelationBehavior();
            var type = typeof(DynamicRelationBehavior);
            var heroes = (Dictionary<string, Hero>)AccessTools.Field(type, "_heroesById").GetValue(behavior);
            var clans = (Dictionary<string, Clan>)AccessTools.Field(type, "_clansById").GetValue(behavior);
            Hero Hero(string id) { var h = Blank<Hero>(); h.StringId = id; heroes[id] = h; return h; }
            var first = Hero("first"); var second = Hero("second"); var third = Hero("third");
            foreach (string id in new[] { "houseA", "houseB", "houseC" }) { var c = Blank<Clan>(); c.StringId = id; clans[id] = c; }
            var cache = (IDictionary)AccessTools.Field(type, "_visibleRelationCache").GetValue(behavior);
            var keyType = type.Assembly.GetType("BellumCivile.DynamicRelationPairKey");
            var entry = Activator.CreateInstance(type.GetNestedType("VisibleRelationCacheEntry", BindingFlags.NonPublic));
            object Key(Hero x, Hero y) => Activator.CreateInstance(keyType, x, y);
            var affected = Key(first, second); var unrelated = Key(first, third);
            void Seed() { cache[affected] = entry; cache[unrelated] = entry; }
            var add = AccessTools.Method(type, "AddMemoryRecord");
            void Add(RelationMemoryScope scope, string x, string y, string source, float expiry) => add.Invoke(behavior,
                new object[] { new RelationMemoryRecord(scope, x, y, source, "", 5, _day, expiry) });
            _day = 100; Seed();
            Add(RelationMemoryScope.Personal, "first", "second", "test", 102);
            check(!cache.Contains(affected) && cache.Contains(unrelated), "New personal memory invalidates only its own visible pair");
            Seed(); Add(RelationMemoryScope.Personal, "first", "second", "test", 102);
            check(!cache.Contains(affected) && cache.Contains(unrelated), "Merged personal memory also invalidates only its pair");
            Seed(); Add(RelationMemoryScope.House, "houseA", "houseB", "house", 102);
            var revisions = (IDictionary)AccessTools.Field(type, "_houseMemoryRevisions").GetValue(behavior);
            check(revisions.Count == 1 && cache.Count == 2, "House memory increments only its house-pair revision without scanning visible pairs");
            check((int)AccessTools.Field(type, "_memoryRevision").GetValue(behavior) == 0, "Ordinary personal and house changes do not advance global memory revision");
            _day = 102;
            AccessTools.Method(type, "PruneExpiredMemories").Invoke(behavior, new object[] { false });
            check(!cache.Contains(affected) && cache.Contains(unrelated), "Expiry does not invalidate unrelated personal pairs");
            check((int)AccessTools.Field(type, "_memoryRevision").GetValue(behavior) == 0, "Expiry avoids global invalidation");
            AccessTools.Method(type, "RebuildMemoryIndexes").Invoke(behavior, null);
            check((int)AccessTools.Field(type, "_memoryRevision").GetValue(behavior) == 1, "Full index rebuild still invalidates all old revisions");
        }
        finally
        {
            _throw = false; harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(DynamicRelationBehavior), "Instance").Invoke(null, new object[] { previous });
        }
    }
}
