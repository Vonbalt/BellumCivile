using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PersonalBereavementTests
{
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Hero, Hero> Fathers = new Dictionary<Hero, Hero>();
    private static readonly Dictionary<Hero, Hero> Spouses = new Dictionary<Hero, Hero>();
    private static readonly Dictionary<Hero, MBList<Hero>> Children = new Dictionary<Hero, MBList<Hero>>();
    private static readonly Dictionary<string, int> Raw = new Dictionary<string, int>();
    private static Hero _player;
    private static float _day;
    private static bool _enabled = true;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool House(Hero __instance, ref Clan __result) { Houses.TryGetValue(__instance, out __result); return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Father(Hero __instance, ref Hero __result) { Fathers.TryGetValue(__instance, out __result); return false; }
    private static bool Spouse(Hero __instance, ref Hero __result) { Spouses.TryGetValue(__instance, out __result); return false; }
    private static bool ChildList(Hero __instance, ref MBList<Hero> __result) { Children.TryGetValue(__instance, out __result); return false; }
    private static bool Alive(ref MBReadOnlyList<Hero> __result) { __result = new MBList<Hero>(); return false; }
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool PlayerClan(ref Clan __result) { __result = Houses[_player]; return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("Same name"); return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Natural(ref int __result) { __result = 0; return false; }
    private static string Key(Hero a, Hero b) => RelationMemoryRecord.BuildPairKey(a.StringId, b.StringId);
    private static bool GetRaw(Hero firstHero, Hero secondHero, ref int __result) { Raw.TryGetValue(Key(firstHero, secondHero), out __result); return false; }
    private static bool SetRaw(Hero firstHero, Hero secondHero, int value) { Raw[Key(firstHero, secondHero)] = value; return false; }
    private static bool Objects(DynamicRelationBehavior __instance)
    {
        var heroes = (Dictionary<string, Hero>)AccessTools.Field(typeof(DynamicRelationBehavior), "_heroesById").GetValue(__instance);
        var clans = (Dictionary<string, Clan>)AccessTools.Field(typeof(DynamicRelationBehavior), "_clansById").GetValue(__instance);
        foreach (var pair in Houses) { heroes[pair.Key.StringId] = pair.Key; clans[pair.Value.StringId] = pair.Value; }
        return false;
    }
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) { Data[key] = value; return true; }
            if (!Data.TryGetValue(key, out var loaded)) return false;
            value = (T)loaded; return true;
        }
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(DynamicRelationBehavior);
        var previous = DynamicRelationBehavior.Instance;
        var harmony = new Harmony("bellum.test.personal_bereavement");
        try
        {
            void Prop(Type t, string name, string method) => harmony.Patch(AccessTools.PropertyGetter(t, name), prefix: new HarmonyMethod(typeof(PersonalBereavementTests), method));
            foreach (string name in new[] { "IsAlive", "IsLord" }) Prop(typeof(Hero), name, nameof(Yes));
            foreach (string name in new[] { "IsChild", "IsDisabled", "IsNotable" }) Prop(typeof(Hero), name, nameof(No));
            Prop(typeof(Hero), "Clan", nameof(House)); Prop(typeof(Hero), "Father", nameof(Father));
            Prop(typeof(Hero), "Spouse", nameof(Spouse)); Prop(typeof(Hero), "Children", nameof(ChildList));
            Prop(typeof(Hero), "AllAliveHeroes", nameof(Alive)); Prop(typeof(Hero), "MainHero", nameof(Player));
            Prop(typeof(Hero), "Name", nameof(Name)); Prop(typeof(Clan), "Leader", nameof(Leader));
            Prop(typeof(Clan), "PlayerClan", nameof(PlayerClan)); Prop(typeof(Clan), "IsNoble", nameof(Yes));
            Prop(typeof(Clan), "IsMinorFaction", nameof(No)); Prop(typeof(Clan), "IsEliminated", nameof(No));
            Prop(type.Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicRelationDrift", nameof(Enabled));
            Prop(type, "CurrentDay", nameof(Day));
            harmony.Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.DynamicRelationBaselineHelper"), "GetPrisonerCustodyModifier"),
                prefix: new HarmonyMethod(typeof(PersonalBereavementTests), nameof(Natural)));
            foreach (var pair in new[] { new[] { "GetNaturalRelationRaw", nameof(Natural) }, new[] { "GetRawRelation", nameof(GetRaw) },
                new[] { "SetRawRelation", nameof(SetRaw) }, new[] { "RebuildObjectResolutionCaches", nameof(Objects) } })
                harmony.Patch(AccessTools.Method(type, pair[0]), prefix: new HarmonyMethod(typeof(PersonalBereavementTests), pair[1]));
            Clan House(string id) { var c = Blank<Clan>(); c.StringId = id; return c; }
            Hero Hero(string id, Clan c) { var h = Blank<Hero>(); h.StringId = id; Houses[h] = c; Children[h] = new MBList<Hero>(); return h; }
            var crown = House("crown"); var victimHouse = House("victim_house");
            _player = Hero("killer", crown); Leaders[crown] = _player;
            var victim = Hero("victim", victimHouse); var son = Hero("son", victimHouse);
            var spouse = Hero("spouse", crown); var sibling = Hero("sibling", crown);
            var father = Hero("father", victimHouse); var unrelated = Hero("unrelated", victimHouse);
            Leaders[victimHouse] = son;
            Fathers[son] = victim; Fathers[victim] = father; Fathers[sibling] = father;
            Children[victim].Add(son); Children[father].Add(victim); Children[father].Add(sibling);
            Spouses[victim] = spouse;
            var before = AccessTools.Method(type, "OnBeforeHeroKilled");
            var complete = AccessTools.Method(type, "CompleteDeathMemories");
            var pendingField = AccessTools.Field(type, "_pendingDeathMemory");
            var preserve = AccessTools.Method(type, "PreserveDeathContext");
            var nativeScope = AccessTools.Method(type, "BeginNativeExecutionRelations");
            var active = AccessTools.Method(type, "GetActiveMemories");
            foreach (var death in new[] { KillCharacterAction.KillCharacterActionDetail.DiedInBattle,
                KillCharacterAction.KillCharacterActionDetail.Executed, KillCharacterAction.KillCharacterActionDetail.Murdered })
            {
                _day = 100; Raw.Clear();
                var behavior = new DynamicRelationBehavior(); Objects(behavior);
                int Read(Hero h) => behavior.GetRelationForRead(h, _player, Raw[Key(h, _player)]);
                foreach (var h in new[] { son, spouse, sibling, father, unrelated })
                { Raw[Key(h, _player)] = 50; Read(h); }
                before.Invoke(behavior, new object[] { victim, _player, death, false });
                object snapshot = pendingField.GetValue(behavior);
                var family = (HashSet<Hero>)AccessTools.Field(snapshot.GetType(), "Family").GetValue(snapshot);
                check(family.SetEquals(new[] { son, spouse, sibling, father }), "Snapshot includes immediate family across houses, without unrelated members");
                using ((IDisposable)preserve.Invoke(behavior, null))
                {
                    before.Invoke(behavior, new object[] { unrelated, null, KillCharacterAction.KillCharacterActionDetail.DiedOfOldAge, false });
                    check(pendingField.GetValue(behavior) == null, "Nested natural death does not inherit outer suppression");
                }
                check(ReferenceEquals(snapshot, pendingField.GetValue(behavior)), "Nested death restores outer snapshot");
                foreach (var h in new[] { son, spouse, sibling, father })
                {
                    var state = behavior.PrepareRelationSet(h, _player); Raw[Key(h, _player)] = 10;
                    using ((IDisposable)nativeScope.Invoke(behavior, null))
                        behavior.CaptureRelationSet(h, _player, 10, state);
                    check(Read(h) == 50, "Native close-family penalty is replaced until death dispatch completes");
                }
                complete.Invoke(behavior, new object[] { victim, _player, death });
                int penalty = death == KillCharacterAction.KillCharacterActionDetail.DiedInBattle ? 20 : 30;
                foreach (var h in new[] { son, spouse, sibling, father, unrelated })
                    check(Read(h) == 50 - penalty, "Family and house reactions each count once");
                var memories = ((IEnumerable<RelationMemoryRecord>)active.Invoke(behavior, new object[] { son, _player })).Where(m => m.EventId == "death:victim").ToList();
                check(memories.Count == 1 && memories[0].Scope == RelationMemoryScope.Personal, "Tooltip shows personal grief instead of duplicate house grief");
                string source = death == KillCharacterAction.KillCharacterActionDetail.DiedInBattle ? RelationMemorySources.KilledCloseFamily
                    : death == KillCharacterAction.KillCharacterActionDetail.Murdered ? RelationMemorySources.MurderedCloseFamily : RelationMemorySources.ExecutedCloseFamily;
                check(memories[0].SourceId == source, "Battle, murder and execution use truthful family labels");
                complete.Invoke(behavior, new object[] { victim, _player, death });
                check(Read(son) == 50 - penalty, "Repeated completion without snapshot adds nothing");
                var save = new Store(); behavior.SyncData(save); save.IsLoading = true;
                var loaded = new DynamicRelationBehavior(); loaded.SyncData(save);
                check(loaded.GetRelationForRead(son, _player, Raw[Key(son, _player)]) == 50 - penalty, "Personal/house exclusion survives behavior save/load");
                Houses[son] = crown; loaded.InvalidateBaselineCache();
                check(loaded.GetRelationForRead(son, _player, Raw[Key(son, _player)]) == 50 - penalty, "Personal grief follows a relative into another house");
                Houses[son] = victimHouse;
            }
            var disabled = new DynamicRelationBehavior(); _enabled = false;
            before.Invoke(disabled, new object[] { victim, _player, KillCharacterAction.KillCharacterActionDetail.Executed, false });
            check(pendingField.GetValue(disabled) == null, "Disabled relation system creates no death snapshot"); _enabled = true;

            _day = 100; Raw[Key(son, _player)] = 0;
            var expiry = new DynamicRelationBehavior(); Objects(expiry);
            expiry.GetRelationForRead(son, _player, 0);
            var add = AccessTools.Method(type, "AddMemoryRecord");
            add.Invoke(expiry, new object[] { new RelationMemoryRecord(RelationMemoryScope.Personal, son.StringId, _player.StringId,
                RelationMemorySources.KilledCloseFamily, "Same name", -20, 100, 101, eventId: "death:first") });
            foreach (string id in new[] { "first", "second" })
                add.Invoke(expiry, new object[] { new RelationMemoryRecord(RelationMemoryScope.House, victimHouse.StringId, crown.StringId,
                    RelationMemorySources.KilledKinsman, "Same name", -20, 100, id == "first" ? 102 : 103, eventId: "death:" + id) });
            int ReadExpiry() => expiry.GetRelationForRead(son, _player, Raw[Key(son, _player)]);
            check(ReadExpiry() == -40, "Different victims with identical names remain distinct; only matching death is excluded");
            _day = 101;
            check(ReadExpiry() == -40, "House grief resumes when personal override expires first");
            check(((IEnumerable<RelationMemoryRecord>)active.Invoke(expiry, new object[] { son, _player })).Count(m => m.Scope == RelationMemoryScope.House) == 2,
                "Tooltip returns to house entries after personal expiry");
            _day = 102; check(ReadExpiry() == -20, "Independent death expiry is retained");
            _day = 103; check(ReadExpiry() == 0, "All death memories expire without a residual duplicate");

            _day = 100; Raw[Key(spouse, _player)] = 50;
            var scoped = new DynamicRelationBehavior(); Objects(scoped);
            scoped.GetRelationForRead(spouse, _player, 50);
            before.Invoke(scoped, new object[] { victim, _player, KillCharacterAction.KillCharacterActionDetail.Executed, false });
            var externalState = scoped.PrepareRelationSet(spouse, _player); Raw[Key(spouse, _player)] = 48;
            scoped.CaptureRelationSet(spouse, _player, 48, externalState);
            check(scoped.GetRelationForRead(spouse, _player, 48) == 48, "Non-native reactions during death dispatch are not suppressed");
            using ((IDisposable)nativeScope.Invoke(scoped, null))
            using (RelationMemoryService.Begin("explicit_court_reaction", 1))
            {
                externalState = scoped.PrepareRelationSet(spouse, _player); Raw[Key(spouse, _player)] = 46;
                scoped.CaptureRelationSet(spouse, _player, 46, externalState);
            }
            check(scoped.GetRelationForRead(spouse, _player, 46) == 46, "Explicit Bellum sources retain their own consequences");
            var lifecycle = type.Assembly.GetType("BellumCivile.Patches.DeathRelationContextPatch");
            object originalSnapshot = pendingField.GetValue(scoped);
            object[] contextArgs = { null };
            AccessTools.Method(lifecycle, "Prefix").Invoke(null, contextArgs);
            pendingField.SetValue(scoped, null);
            var error = new InvalidOperationException("death aborted");
            check(ReferenceEquals(AccessTools.Method(lifecycle, "Finalizer").Invoke(null, new object[] { error, contextArgs[0] }), error)
                && ReferenceEquals(pendingField.GetValue(scoped), originalSnapshot), "Death action finalizer restores prior context and propagates exceptions");

            var record = new RelationMemoryRecord(RelationMemoryScope.House, "a", "b", "test", "same name", -20, 100, 200);
            check(record.EventId == "", "Old memory records without event IDs remain readable");
            check(AccessTools.Field(typeof(RelationMemoryRecord), "_eventId").GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"), "Death identity is a saved optional field");
            var fields = typeof(RelationMemoryRecord).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value).ToList();
            check(ids.Count == 10 && ids.Distinct().Count() == 10, "Relation memory save fields retain unique IDs");
            foreach (string patchName in new[] { "DeathRelationContextPatch", "DeathRelationCompletionPatch", "NativeExecutionRelationContextPatch" })
                check(harmony.CreateClassProcessor(type.Assembly.GetType("BellumCivile.Patches." + patchName)).Patch().Count == 1,
                    "Death lifecycle hook attaches to native assembly: " + patchName);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(type, "Instance").Invoke(null, new object[] { previous });
            Houses.Clear(); Leaders.Clear(); Fathers.Clear(); Spouses.Clear(); Children.Clear(); Raw.Clear(); _enabled = true;
        }
    }
}
