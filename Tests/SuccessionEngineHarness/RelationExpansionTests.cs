using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class RelationExpansionTests
{
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly HashSet<Hero> Notables = new HashSet<Hero>();
    private static readonly HashSet<Hero> Children = new HashSet<Hero>();
    private static readonly Dictionary<string, Hero> Heroes = new Dictionary<string, Hero>();
    private static readonly Dictionary<string, int> Raw = new Dictionary<string, int>();
    private static Hero _player;
    private static float _day;
    private static int _kinshipIncrease;
    private static string Key(Hero a, Hero b) => RelationMemoryRecord.BuildPairKey(a.StringId, b.StringId);
    private static bool House(Hero __instance, ref Clan __result) { Houses.TryGetValue(__instance, out __result); return false; }
    private static bool Notable(Hero __instance, ref bool __result) { __result = Notables.Contains(__instance); return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Children.Contains(__instance); return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool Father(Hero __instance, ref Hero __result) { __result = __instance.StringId == "son" ? _player : null; return false; }
    private static bool PlayerClan(ref Clan __result) { __result = Houses[_player]; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool Natural(Hero firstHero, Hero secondHero, ref int __result)
    { __result = Notables.Contains(firstHero) || Notables.Contains(secondHero) ? 0 : 20 + _kinshipIncrease; return false; }
    private static bool KinshipIncrease(Hero first, Hero second, ref int __result)
    { __result = Notables.Contains(first) || Notables.Contains(second) ? 0 : _kinshipIncrease; return false; }
    private static bool GetRaw(Hero firstHero, Hero secondHero, ref int __result) { Raw.TryGetValue(Key(firstHero, secondHero), out __result); return false; }
    private static bool SetRaw(Hero firstHero, Hero secondHero, int value) { Raw[Key(firstHero, secondHero)] = value; return false; }
    private static bool Objects(DynamicRelationBehavior __instance)
    {
        var index = (Dictionary<string, Hero>)AccessTools.Field(typeof(DynamicRelationBehavior), "_heroesById").GetValue(__instance);
        index.Clear(); foreach (var hero in Heroes) index[hero.Key] = hero.Value;
        return false;
    }
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) { Data[key] = data; return true; }
            if (!Data.TryGetValue(key, out object value)) return false;
            data = (T)value; return true;
        }
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool, string> check)
    {
        var previous = DynamicRelationBehavior.Instance;
        var harmony = new Harmony("bellum.test.relation_expansion");
        var type = typeof(DynamicRelationBehavior);
        try
        {
            var house = Blank<Clan>(); house.StringId = "house";
            Hero Hero(string id, bool notable = false)
            {
                var h = Blank<Hero>(); h.StringId = id; Heroes[id] = h;
                if (notable) Notables.Add(h); else Houses[h] = house;
                return h;
            }
            _player = Hero("player"); var son = Hero("son"); var notable = Hero("notable", true);
            var otherNotable = Hero("other_notable", true);
            void Prop(Type t, string name, string prefix) => harmony.Patch(AccessTools.PropertyGetter(t, name), prefix: new HarmonyMethod(typeof(RelationExpansionTests), prefix));
            Prop(typeof(Hero), "Clan", nameof(House)); Prop(typeof(Hero), "MainHero", nameof(Player));
            Prop(typeof(Hero), "Father", nameof(Father));
            Prop(typeof(Clan), "PlayerClan", nameof(PlayerClan)); Prop(typeof(Hero), "IsNotable", nameof(Notable));
            Prop(typeof(Hero), "IsChild", nameof(Child)); Prop(typeof(Hero), "IsAlive", nameof(Yes));
            Prop(typeof(Hero), "IsDisabled", nameof(No)); Prop(typeof(Hero), "IsLord", nameof(Yes));
            Prop(typeof(Hero), "Name", nameof(Name));
            Prop(typeof(Clan), "IsMinorFaction", nameof(No)); Prop(typeof(Clan), "IsNoble", nameof(Yes));
            Prop(typeof(Clan), "IsEliminated", nameof(No));
            Prop(type.Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicRelationDrift", nameof(Yes));
            Prop(type, "CurrentDay", nameof(Day));
            foreach (var pair in new[] { new[] { "GetNaturalRelationRaw", nameof(Natural) }, new[] { "GetRawRelation", nameof(GetRaw) },
                new[] { "SetRawRelation", nameof(SetRaw) }, new[] { "RebuildObjectResolutionCaches", nameof(Objects) } })
                harmony.Patch(AccessTools.Method(type, pair[0]), prefix: new HarmonyMethod(typeof(RelationExpansionTests), pair[1]));
            bool Eligible(Hero a, Hero b) => (bool)AccessTools.Method(type, "ShouldAffectPair").Invoke(null, new object[] { a, b });
            check(Eligible(_player, son), "Same-clan adults enter personal memory system");
            check(Eligible(_player, notable) && Eligible(son, notable), "Player and noble notable pairs are eligible");
            check(Eligible(notable, otherNotable), "Actual notable-to-notable events can use personal memory storage");
            check(!Eligible(_player, _player), "Self-relation remains excluded");
            Children.Add(son); check(!Eligible(_player, son), "Children remain excluded"); Children.Clear();
            var baseline = type.Assembly.GetType("BellumCivile.DynamicRelationBaselineHelper");
            check((int)AccessTools.Method(baseline, "CalculatePoliticalConditionsRaw").Invoke(null, new object[] { _player, son }) == 0,
                "Same-clan pairs skip all inter-house political modifiers");
            check((int)AccessTools.Method(baseline, "CalculateFoundationRaw").Invoke(null, new object[] { notable, _player }) == 0,
                "Notables do not acquire a noble trait/culture starting score");

            foreach (Hero target in new[] { son, notable })
            foreach (int opening in new[] { -35, 0, 35, 60, 100 })
            {
                _day = 100; Raw.Clear(); Raw[Key(_player, target)] = opening;
                var behavior = new DynamicRelationBehavior(); Objects(behavior);
                int Read() => behavior.GetRelationForRead(_player, target, Raw[Key(_player, target)]);
                check(Read() == opening, "First access preserves exact opening relation including zero");
                if (target == notable && opening == 0)
                {
                    var materialized = (IDictionary)AccessTools.Field(type, "_runtimeMaterializedPairValues").GetValue(behavior);
                    check(materialized.Count == 0, "Viewing neutral notable creates no materialized pair");
                    var savedNeutral = new Store(); behavior.SyncData(savedNeutral);
                    check(((Dictionary<string, int>)savedNeutral.Data["BellumCivile_RelationOpeningAdjustments"]).Count == 0,
                        "Neutral notable inspection creates no saved opening record");
                }
                int change = opening == 100 ? -5 : 5;
                var state = behavior.PrepareRelationSet(_player, target);
                Raw[Key(_player, target)] = opening + change;
                using (RelationMemoryService.Begin("test_favor", 1))
                    behavior.CaptureRelationSet(_player, target, opening + change, state);
                check(Read() == opening + change, "New relation change is captured once on top of opening history");
                var save = new Store(); behavior.SyncData(save);
                var loaded = new DynamicRelationBehavior(); save.IsLoading = true; loaded.SyncData(save);
                check(loaded.GetRelationForRead(_player, target, Raw[Key(_player, target)]) == opening + change,
                    "Opening adjustment and event memory survive behavior save/load");
                _day = 10000;
                check(loaded.GetRelationForRead(_player, target, Raw[Key(_player, target)]) == (target == notable ? 0 : 20),
                    "Expired opening history and event return to current natural baseline");
                var expiredSave = new Store(); loaded.SyncData(expiredSave); expiredSave.IsLoading = true;
                var expiredReload = new DynamicRelationBehavior(); expiredReload.SyncData(expiredSave);
                check(expiredReload.GetRelationForRead(_player, target, Raw[Key(_player, target)]) == (target == notable ? 0 : 20),
                    "Save/load cannot recreate expired opening history");
            }
            _day = 100; Raw[Key(_player, notable)] = 60;
            var tooltipBehavior = new DynamicRelationBehavior(); Objects(tooltipBehavior);
            object[] tooltipArgs = { notable, 0, null };
            bool built = (bool)AccessTools.Method(type, "TryBuildEncyclopediaThresholdTooltip").Invoke(tooltipBehavior, tooltipArgs);
            check(built && (int)tooltipArgs[1] == 60 && ((IList)tooltipArgs[2]).Count > 1,
                "Notable encyclopedia tooltip displays preserved actual personal relation");
            check((bool)AccessTools.Method(type, "CanBuildPlayerRelationTooltip").Invoke(tooltipBehavior, new object[] { _player, son }),
                "Player household members can display the relation tooltip");
            var oldSave = new Store { IsLoading = true };
            oldSave.Data["BellumCivile_RelationOpeningAdjustments"] = null;
            var legacy = new DynamicRelationBehavior(); legacy.SyncData(oldSave);
            check(legacy.GetRelationForRead(_player, notable, 60) == 60, "Legacy save without opening history initializes safely");

            // Reconstruct the prior permanent-offset save format rather than testing only new saves.
            foreach (int value in new[] { -15, 15 })
            {
                _day = 100; Raw[Key(_player, notable)] = value;
                var seed = new DynamicRelationBehavior(); Objects(seed);
                seed.GetRelationForRead(_player, notable, value);
                var permanent = new Store(); seed.SyncData(permanent);
                permanent.Data["BellumCivile_RelationMemories"] = new List<RelationMemoryRecord>();
                permanent.Data["BellumCivile_RelationOpeningAdjustments"] = new Dictionary<string, int> { [Key(_player, notable)] = value };
                permanent.Data["BellumCivile_MemoryDurationMultiplier"] = 2f;
                permanent.IsLoading = true;
                var migrated = new DynamicRelationBehavior(); migrated.SyncData(permanent);
                check(migrated.GetRelationForRead(_player, notable, value) == value, "Permanent opening upgrade preserves positive and negative totals");
                var converted = new Store(); migrated.SyncData(converted);
                var records = (List<RelationMemoryRecord>)converted.Data["BellumCivile_RelationMemories"];
                check(records.Count == 1 && records[0].SourceId == RelationMemorySources.PriorPersonalHistory,
                    "Permanent opening becomes one named personal memory");
                float expectedDays = 10 * Math.Max(1, CampaignTime.DaysInYear);
                check(Math.Abs(records[0].ExpiryDay - 100 - expectedDays) < .1f,
                    "Pre-session migration respects saved duration multiplier");
                check(((Dictionary<string, int>)converted.Data["BellumCivile_RelationOpeningAdjustments"]).Count == 0,
                    "Permanent opening save key is emptied after conversion");
                converted.IsLoading = true;
                var once = new DynamicRelationBehavior(); once.SyncData(converted);
                check(once.GetRelationForRead(_player, notable, value) == value, "Converted opening is not duplicated on reload");
                _day = 10000;
                check(once.GetRelationForRead(_player, notable, value) == 0, "Converted positive and negative opening memories both expire");
            }

            harmony.Patch(AccessTools.Method(baseline, "CalculateKinshipBaselineIncrease"), prefix: new HarmonyMethod(typeof(RelationExpansionTests), nameof(KinshipIncrease)));
            foreach (int opening in new[] { -35, 0, 35, 100 })
            {
                _kinshipIncrease = 0; _day = 100; Raw[Key(_player, son)] = opening;
                var before = new DynamicRelationBehavior(); Objects(before);
                check(before.GetRelationForRead(_player, son, opening) == opening, "Pre-kinship save seeded");
                var state = before.PrepareRelationSet(_player, son);
                int change = opening == 100 ? -5 : 5;
                Raw[Key(_player, son)] = opening + change;
                using (RelationMemoryService.Begin("migration_favor", 1)) before.CaptureRelationSet(_player, son, opening + change, state);
                var saved = new Store(); before.SyncData(saved);
                saved.Data.Remove("BellumCivile_BloodKinshipBaselineMigrated");
                _kinshipIncrease = 20;
                var upgraded = new DynamicRelationBehavior(); saved.IsLoading = true; upgraded.SyncData(saved);
                check(upgraded.GetRelationForRead(_player, son, Raw[Key(_player, son)]) == opening + change,
                    "Old tracked household pair keeps its visible total after kinship upgrade");
                var again = new Store(); upgraded.SyncData(again);
                var reloaded = new DynamicRelationBehavior(); again.IsLoading = true; reloaded.SyncData(again);
                check(reloaded.GetRelationForRead(_player, son, Raw[Key(_player, son)]) == opening + change,
                    "Kinship migration runs once across save/load");
                _day = 10000;
                check(reloaded.GetRelationForRead(_player, son, Raw[Key(_player, son)]) == 40,
                    "Kinship migration offsets expire with personal history");
            }
            var otherHouse = Blank<Clan>(); otherHouse.StringId = "other_house"; Houses[son] = otherHouse;
            _day = 100; Raw[Key(_player, son)] = 0;
            var crossHouse = new DynamicRelationBehavior(); Objects(crossHouse);
            check(crossHouse.GetRelationForRead(_player, son, 0) == 0,
                "First inspection of cross-house blood relatives also preserves neutral opening relation");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(type, "Instance").Invoke(null, new object[] { previous });
            Houses.Clear(); Heroes.Clear(); Notables.Clear(); Children.Clear(); Raw.Clear();
            _kinshipIncrease = 0;
        }
    }
}
