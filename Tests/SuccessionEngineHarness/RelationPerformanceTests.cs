using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RelationPerformanceTests
{
    private static float _day;
    private static Hero _notable;
    private static bool Notable(Hero __instance, ref bool __result) { __result = __instance == _notable; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    // Uninitialized harness heroes have no game object IDs; use identity hashes to avoid artificial collisions.
    private static bool ObjectHash(object __instance, ref int __result)
    { __result = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance); return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(DynamicRelationBehavior);
        var previous = DynamicRelationBehavior.Instance;
        var harmony = new Harmony("bellum.test.relation_performance");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(type, "CurrentDay"), prefix: new HarmonyMethod(typeof(RelationPerformanceTests), nameof(Day)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsNotable"), prefix: new HarmonyMethod(typeof(RelationPerformanceTests), nameof(Notable)));
            harmony.Patch(AccessTools.DeclaredMethod(AccessTools.Method(typeof(Hero), "GetHashCode").DeclaringType, "GetHashCode"),
                prefix: new HarmonyMethod(typeof(RelationPerformanceTests), nameof(ObjectHash)));
            var prune = AccessTools.Method(type, "PruneExpiredMemories");
            foreach (int count in new[] { 600, 6000, 36000 })
            {
                _day = 100;
                var behavior = new DynamicRelationBehavior();
                var heroes = (Dictionary<string, Hero>)AccessTools.Field(type, "_heroesById").GetValue(behavior);
                var anchor = Blank<Hero>(); anchor.StringId = "anchor"; heroes[anchor.StringId] = anchor;
                var records = (List<RelationMemoryRecord>)AccessTools.Field(type, "_relationMemories").GetValue(behavior);
                for (int p = 0; p < count / 12; p++)
                {
                    var h = Blank<Hero>(); h.StringId = "person_" + p; heroes[h.StringId] = h;
                    for (int m = 0; m < 12; m++) records.Add(new RelationMemoryRecord(RelationMemoryScope.Personal,
                        anchor.StringId, h.StringId, "event_" + m, "", 5, 100, m % 2 == 0 ? 101 : 200));
                }
                AccessTools.Method(type, "RebuildMemoryIndexes").Invoke(behavior, null);
                var expected = records.Where(m => m.ExpiryDay == 200).ToList();
                behavior.BuildPerformanceDiagnostics(true);
                prune.Invoke(behavior, new object[] { false });
                check(records.Count == count, "Expiry deadline skips premature scans");
                _day = 101;
                var timer = Stopwatch.StartNew();
                prune.Invoke(behavior, new object[] { false });
                timer.Stop();
                double elapsed = timer.Elapsed.TotalMilliseconds;
                check(records.SequenceEqual(expected), "Compaction preserves surviving records and order");
                var index = (IDictionary)AccessTools.Field(type, "_personalMemoriesByPair").GetValue(behavior);
                check(index.Values.Cast<IList>().Sum(bucket => bucket.Count) == count / 2, "Expiry updates pair indexes consistently");
                string stats = behavior.BuildPerformanceDiagnostics(false);
                check(stats.Contains("prune_runs=1") && stats.Contains("prune_scanned=" + count) && stats.Contains("prune_removed=" + count / 2),
                    "Live diagnostics report scanned and removed counts");
                prune.Invoke(behavior, new object[] { false });
                check(behavior.BuildPerformanceDiagnostics(false).Contains("prune_runs=1"), "No second scan before next expiry");
                behavior.BuildPerformanceDiagnostics(true);
                check(behavior.BuildPerformanceDiagnostics(false).Contains("prune_runs=0") && records.Count == count / 2,
                    "Diagnostic reset changes counters only");
                Console.WriteLine($"BENCH relation expiry: records={count}; removed={count / 2}; actual_prune_ms={elapsed:F3}; harness, not live campaign");
                _day = 200; prune.Invoke(behavior, new object[] { false });
                check(records.Count == 0 && index.Count == 0, "Final expiry clears empty pair buckets");
            }

            _day = 100;
            var cached = new DynamicRelationBehavior();
            var keyType = type.Assembly.GetType("BellumCivile.DynamicRelationPairKey");
            var store = AccessTools.Method(type, "CacheVisibleRelation");
            var cache = (IDictionary)AccessTools.Field(type, "_visibleRelationCache").GetValue(cached);
            var opening = (IDictionary)AccessTools.Field(type, "_runtimeOpeningAdjustments").GetValue(cached);
            var materialized = (IDictionary)AccessTools.Field(type, "_runtimeMaterializedPairValues").GetValue(cached);
            var a = Blank<Hero>(); a.StringId = "a";
            object firstKey = null;
            for (int i = 0; i < 10000; i++)
            {
                var b = Blank<Hero>(); b.StringId = "b_" + i;
                object key = Activator.CreateInstance(keyType, a, b);
                if (i == 0) { firstKey = key; opening[key] = 25; materialized[key] = 50; }
                store.Invoke(cached, new[] { key, (object)50, 101f });
            }
            check(cache.Count == 8192 && !cache.Contains(firstKey), "Visible cache evicts oldest entries at its size bound");
            check((int)opening[firstKey] == 25 && (int)materialized[firstKey] == 50, "Cache eviction preserves durable opening and materialized values");
            _day = 101;
            var pruneCaches = AccessTools.Method(type, "PruneExpiredCaches");
            pruneCaches.Invoke(cached, null);
            check(cache.Count == 8192 - 128, "Daily visible pruning respects budget and exact expiry boundary");
            cache.Clear();
            var queue = (ICollection)AccessTools.Field(type, "_visiblePruneQueue").GetValue(cached);
            int queued = queue.Count;
            pruneCaches.Invoke(cached, null);
            check(queue.Count == queued - 128, "Stale queue keys drain even if cache is empty");
            AccessTools.Method(type, "ClearVisibleRelationCache").Invoke(cached, null);
            check(queue.Count == 0 && opening.Count == 1 && materialized.Count == 1, "Full cache clear cleans queues without deleting relationship history");
            _notable = a;
            check((int)AccessTools.Method(type, "GetNaturalRelationRaw").Invoke(cached, new[] { firstKey, a, Blank<Hero>() }) == 0
                && ((IDictionary)AccessTools.Field(type, "_foundationCache").GetValue(cached)).Count == 0
                && ((IDictionary)AccessTools.Field(type, "_politicalCache").GetValue(cached)).Count == 0,
                "Notable baselines bypass irrelevant noble caches");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _notable = null;
            AccessTools.PropertySetter(type, "Instance").Invoke(null, new object[] { previous });
        }
    }
}
