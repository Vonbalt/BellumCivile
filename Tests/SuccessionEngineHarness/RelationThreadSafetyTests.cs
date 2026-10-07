using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RelationThreadSafetyTests
{
    private static readonly Type Behavior = typeof(DynamicRelationBehavior);
    private static readonly ConcurrentDictionary<string, int> Raw = new ConcurrentDictionary<string, int>();
    private static readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();
    private static int _owner, _nativeWrites, _backgroundNativeWrites;
    private static float _day;
    private static volatile bool _enabled;
    [ThreadStatic] private static bool _nestRead, _nestWrite, _throwRead, _throwWrite;
    private static bool _nestedReadPreserved, _nestedWritePreserved;
    private static Hero _first, _second;
    private static string Key(Hero first, Hero second) => RelationMemoryRecord.BuildPairKey(first.StringId, second.StringId);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object instance, string method, params object[] args) => AccessTools.Method(Behavior, method).Invoke(instance, args);
    private static IDictionary Data(object instance, string field) => (IDictionary)AccessTools.Field(Behavior, field).GetValue(instance);
    private static bool Suppressed
    {
        get => (bool)AccessTools.Property(Behavior, "SuppressRelationPatch").GetValue(null);
        set => AccessTools.PropertySetter(Behavior, "SuppressRelationPatch").Invoke(null, new object[] { value });
    }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool ObjectHash(object __instance, ref int __result)
    { __result = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance); return false; }
    private static bool Log(string message) { Logs.Enqueue(message); return false; }
    private static void OwnerOnly(MethodBase __originalMethod)
    {
        if (Thread.CurrentThread.ManagedThreadId != _owner)
            throw new InvalidOperationException("Background access to mutable relation state: " + __originalMethod.Name);
    }
    private static bool NativeRead(Hero hero1, Hero hero2, ref int __result)
    {
        if (_nestRead)
        {
            _nestRead = false;
            Call(null, "GetRawRelation", hero1, hero2);
            _nestedReadPreserved = Suppressed;
        }
        if (_throwRead) throw new InvalidOperationException("injected native read failure");
        Raw.TryGetValue(Key(hero1, hero2), out __result);
        return false;
    }
    private static bool NativeWrite(Hero hero1, Hero hero2, int value)
    {
        if (_nestWrite)
        {
            _nestWrite = false;
            Call(null, "SetRawRelation", hero1, hero2, value);
            _nestedWritePreserved = Suppressed;
        }
        if (_throwWrite) throw new InvalidOperationException("injected native write failure");
        Interlocked.Increment(ref _nativeWrites);
        if (Thread.CurrentThread.ManagedThreadId != _owner) Interlocked.Increment(ref _backgroundNativeWrites);
        Raw[Key(hero1, hero2)] = value;
        return false;
    }
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsSaving => true;
        public bool IsLoading => false;
        public bool SyncData<T>(string key, ref T data) { Values[key] = data; return true; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var previous = DynamicRelationBehavior.Instance;
        var h = new Harmony("bellum.tests.relation_threads");
        var mod = Behavior.Assembly;
        _owner = Thread.CurrentThread.ManagedThreadId; _day = 100; _enabled = true;
        _nativeWrites = _backgroundNativeWrites = 0;
        var behavior = new DynamicRelationBehavior();
        void Patch(MethodBase method, string name, int priority = Priority.Normal) =>
            h.Patch(method, prefix: new HarmonyMethod(typeof(RelationThreadSafetyTests), name) { priority = priority });
        Hero Hero(string id)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            ((Dictionary<string, Hero>)Data(behavior, "_heroesById"))[id] = hero;
            return hero;
        }
        bool Worker(Func<bool> test) => Task.Run(test).GetAwaiter().GetResult();
        var memories = (List<RelationMemoryRecord>)AccessTools.Field(Behavior, "_relationMemories").GetValue(behavior);
        try
        {
            Patch(AccessTools.PropertyGetter(mod.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicRelationDrift"), nameof(Enabled));
            Patch(AccessTools.PropertyGetter(Behavior, "CurrentDay"), nameof(Day));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsNotable"), nameof(Yes));
            Patch(AccessTools.Method(Behavior, "ShouldAffectPair"), nameof(Yes));
            Patch(AccessTools.Method(Behavior, "GetNaturalRelationRaw"), nameof(Zero));
            Patch(AccessTools.DeclaredMethod(AccessTools.Method(typeof(Hero), "GetHashCode").DeclaringType, "GetHashCode"), nameof(ObjectHash));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log", new[] { typeof(string) }), nameof(Log));
            foreach (string name in new[] { "RefreshMemoryDurationMultiplier", "CacheVisibleRelation", "TryGetCachedVisibleRelation",
                "AddMemoryRecord", "InvalidateMemoryPair", "RebuildMemoryIndexes", "PruneExpiredCaches", "PruneExpiredMemories" })
                Patch(AccessTools.Method(Behavior, name), nameof(OwnerOnly), Priority.First);
            Patch(AccessTools.Method(typeof(CharacterRelationManager), "GetHeroRelation"), nameof(NativeRead), Priority.Last);
            Patch(AccessTools.Method(typeof(CharacterRelationManager), "SetHeroRelation"), nameof(NativeWrite), Priority.Last);
            h.CreateClassProcessor(typeof(DynamicRelationPatch)).Patch();
            AccessTools.Field(Behavior, "_bloodKinshipBaselineMigrated").SetValue(behavior, true);
            AccessTools.Field(Behavior, "_spouseBaselineMigrated").SetValue(behavior, true);
            AccessTools.Field(Behavior, "_memorySchemaVersion").SetValue(behavior, 2);
            _first = Hero("thread_first"); _second = Hero("thread_second");
            var unknown = Hero("thread_unknown");
            Raw[Key(_first, _second)] = 30; Raw[Key(_first, unknown)] = -7;
            check(CharacterRelationManager.GetHeroRelation(_first, _second) == 30,
                "Campaign read still materializes the exact opening relation");
            int memoryCount = memories.Count;
            int publishedCount = Data(behavior, "_runtimeMaterializedPairValues").Count;
            int visibleCount = Data(behavior, "_visibleRelationCache").Count;
            check(Worker(() => behavior.GetRelationForRead(_first, _second, -99) == 30
                && behavior.GetRelationForRead(_first, unknown, -7) == -7),
                "Background reads use the published scalar or preserve the supplied native fallback");
            check(Worker(() => CharacterRelationManager.GetHeroRelation(_first, _second) == 30
                && CharacterRelationManager.GetHeroRelation(_first, unknown) == -7),
                "Actual Harmony relation-read path is safe for cached and previously unseen pairs");
            check(memories.Count == memoryCount && Data(behavior, "_runtimeMaterializedPairValues").Count == publishedCount
                && Data(behavior, "_visibleRelationCache").Count == visibleCount,
                "Background reads do not manufacture memories, initialize pairs, or fill mutable caches");

            int revision = (int)AccessTools.Field(Behavior, "_politicalRevision").GetValue(behavior);
            Worker(() => { behavior.InvalidateBaselineCache(); return true; });
            check((int)AccessTools.Field(Behavior, "_politicalRevision").GetValue(behavior) == revision,
                "Background invalidation does not clear campaign-owned collections");
            CharacterRelationManager.GetHeroRelation(_first, _second);
            check((int)AccessTools.Field(Behavior, "_politicalRevision").GetValue(behavior) == revision + 1,
                "The next campaign read consumes pending political invalidation");

            Suppressed = true;
            check(Worker(() => !Suppressed && behavior.GetRelationForRead(_first, _second, -99) == 30),
                "Main-thread recursion suppression does not leak to a background reader");
            Suppressed = false;
            check(Worker(() => { Suppressed = true; try { return behavior.GetRelationForRead(_first, _second, -99) == -99; }
                finally { Suppressed = false; } }), "Raw reads bypass projection only on their own thread");
            foreach (bool outer in new[] { false, true })
            {
                Suppressed = outer; _nestRead = true; _nestedReadPreserved = false;
                Call(null, "GetRawRelation", _first, _second);
                check(_nestedReadPreserved && Suppressed == outer, "Nested raw reads restore the previous suppression scope: " + outer);
                _nestWrite = true; _nestedWritePreserved = false;
                Call(null, "SetRawRelation", _first, _second, 30);
                check(_nestedWritePreserved && Suppressed == outer, "Nested raw writes restore the previous suppression scope: " + outer);
                foreach (bool write in new[] { false, true })
                {
                    _throwRead = !write; _throwWrite = write;
                    bool threw = false;
                    try { if (write) Call(null, "SetRawRelation", _first, _second, 30); else Call(null, "GetRawRelation", _first, _second); }
                    catch (TargetInvocationException) { threw = true; }
                    finally { _throwRead = _throwWrite = false; }
                    check(threw && Suppressed == outer, "Native failure restores the previous raw-relation scope: " + write + "/" + outer);
                }
            }
            Suppressed = false;

            int writesBefore = _nativeWrites;
            Worker(() =>
            {
                using (RelationMemoryService.Begin("thread_favor", 2, contextText: "Deferred event"))
                    CharacterRelationManager.SetHeroRelation(_first, _second, 35);
                return true;
            });
            check(_nativeWrites == writesBefore && Raw[Key(_first, _second)] == 30,
                "Background setter is deferred before native campaign mutation");
            check(CharacterRelationManager.GetHeroRelation(_first, _second) == 30,
                "A relation postfix does not flush pending writes against an already-read native value");
            using (RelationMemoryService.Begin("unrelated_campaign_context", 1))
            {
                Call(behavior, "ProcessPendingRelationWork");
                var context = AccessTools.Property(typeof(RelationMemoryService), "CurrentDescriptor").GetValue(null);
                check((string)AccessTools.Property(context.GetType(), "SourceId").GetValue(context) == "unrelated_campaign_context",
                    "Queued writer restores the campaign caller's memory context");
            }
            check(Raw[Key(_first, _second)] == 35 && CharacterRelationManager.GetHeroRelation(_first, _second) == 35
                && memories.Any(m => m.SourceId == "thread_favor" && m.Value == 5 && m.ContextText == "Deferred event"
                    && m.ExpiryDay - m.StartDay == 2 * Math.Max(1, CampaignTime.DaysInYear)),
                "Deferred setter retains its exact delta and original memory source");
            Call(behavior, "ProcessPendingRelationWork");
            check(memories.Single(m => m.SourceId == "thread_favor").Value == 5, "Deferred memory is applied once");

            var favored = Hero("native_favor"); var offended = Hero("native_grievance");
            Worker(() =>
            {
                using ((IDisposable)AccessTools.Method(typeof(RelationMemoryService), "BeginNativeLabels")
                    .Invoke(null, new object[] { "queued_native_favor", "queued_native_grievance" }))
                {
                    CharacterRelationManager.SetHeroRelation(_first, favored, 9);
                    CharacterRelationManager.SetHeroRelation(_first, offended, -9);
                }
                return true;
            });
            using (RelationMemoryService.Begin("unrelated_campaign_context", 1))
                Call(behavior, "ProcessPendingRelationWork");
            check(memories.Any(m => m.SourceId == "queued_native_favor" && m.Value == 9)
                && memories.Any(m => m.SourceId == "queued_native_grievance" && m.Value == -9),
                "Deferred native favor and grievance labels override unrelated campaign context");
            check(AccessTools.Field(typeof(RelationMemoryService), "_nativeLabels").GetValue(null) == null,
                "Deferred native labels do not leak into later campaign events");

            Worker(() =>
            {
                using (RelationMemoryService.Begin("failed_deferred_event", 1))
                    CharacterRelationManager.SetHeroRelation(_first, _second, 36);
                using (RelationMemoryService.Begin("after_deferred_failure", 1))
                    CharacterRelationManager.SetHeroRelation(_first, _second, 37);
                return true;
            });
            _throwWrite = true;
            bool deferredThrew = false;
            try { Call(behavior, "ProcessPendingRelationWork"); }
            catch (TargetInvocationException) { deferredThrew = true; }
            finally { _throwWrite = false; }
            check(deferredThrew && !(bool)AccessTools.Field(Behavior, "_processingPendingRelationWork").GetValue(behavior)
                && AccessTools.Property(typeof(RelationMemoryService), "CurrentDescriptor").GetValue(null) == null
                && !memories.Any(m => m.SourceId == "failed_deferred_event"),
                "Failed deferred native write restores drain and memory context without inventing a memory");
            Call(behavior, "ProcessPendingRelationWork");
            check(memories.Any(m => m.SourceId == "after_deferred_failure" && m.Value == 2),
                "A deferred failure does not discard unrelated pending changes");

            _enabled = false;
            check(Worker(() => behavior.GetRelationForRead(_first, _second, 11) == 11),
                "Disabled relationship memory does not substitute published values");
            _enabled = true;

            var targets = Enumerable.Range(0, 96).Select(i => Hero("parallel_" + i)).ToArray();
            foreach (var target in targets) { Raw[Key(_first, target)] = 20; CharacterRelationManager.GetHeroRelation(_first, target); }
            var started = new CountdownEvent(8); var start = new ManualResetEventSlim(false);
            var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            {
                started.Signal(); start.Wait();
                for (int i = 0; i < 10000; i++)
                {
                    int value = CharacterRelationManager.GetHeroRelation(_first, targets[(i + worker) % targets.Length]);
                    if (value < -100 || value > 100) throw new Exception("Invalid published relation");
                    if (i % 64 == 0) behavior.InvalidateBaselineCache();
                }
            })).ToArray();
            if (!started.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Relation workers did not start");
            start.Set();
            for (int i = 0; i < 3000; i++)
            {
                var target = targets[i % targets.Length];
                CharacterRelationManager.SetHeroRelation(_first, target, 20 + i % 2);
                CharacterRelationManager.GetHeroRelation(_first, target);
                if (i % 64 == 0) { _day += 1; Call(behavior, "OnDailyTick"); }
            }
            if (!Task.WaitAll(workers, TimeSpan.FromSeconds(30))) throw new Exception("Relation concurrency test timed out");
            start.Dispose(); started.Dispose();
            check(_backgroundNativeWrites == 0, "80,000 parallel reads alongside memory updates and pruning never write native state off-thread");
            check(Logs.Count(s => s.StartsWith("Guarded off-thread relation read;")) == 1
                && Logs.Count(s => s.StartsWith("Guarded off-thread relation write;")) == 1,
                "Off-thread caller diagnostics are limited to one read and one write per behavior");
            var queue = (ICollection)AccessTools.Field(Behavior, "_visiblePruneQueue").GetValue(behavior);
            check(queue.Count <= 8192 && queue.Count == (int)AccessTools.Property(
                AccessTools.Field(Behavior, "_queuedVisibleKeys").FieldType, "Count")
                .GetValue(AccessTools.Field(Behavior, "_queuedVisibleKeys").GetValue(behavior)),
                "Concurrent readers leave visible cache pruning membership consistent and bounded");

            Worker(() => { using (RelationMemoryService.Begin("before_save", 1))
                    CharacterRelationManager.SetHeroRelation(_first, _second, 41); return true; });
            var store = new Store(); behavior.SyncData(store);
            check(Raw[Key(_first, _second)] == 41 && memories.Any(m => m.SourceId == "before_save"),
                "Pending relation writes are materialized before campaign serialization");
            check(store.Values["BellumCivile_RelationMaterializedValues"] is Dictionary<string, int>
                && store.Values["BellumCivile_RelationMemories"] is List<RelationMemoryRecord>,
                "Concurrent publication does not alter the existing saved dictionary and memory formats");
        }
        finally
        {
            Suppressed = false; _nestRead = _nestWrite = _throwRead = _throwWrite = false;
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(Behavior, "Instance").Invoke(null, new object[] { previous });
            Raw.Clear(); while (Logs.TryDequeue(out _)) { }
        }
    }
}
