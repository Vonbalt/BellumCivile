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
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class FeudalPoliticalPerformanceTests
{
    private static readonly Dictionary<string, Clan> Clans = new Dictionary<string, Clan>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly HashSet<string> Neighbors = new HashSet<string>();
    private static readonly HashSet<string> Claimed = new HashSet<string>();
    private static readonly HashSet<Clan> Ineligible = new HashSet<Clan>();
    private static readonly List<string> Evaluated = new List<string>();
    private static float _day;
    private static int _trackChecks, _distanceChecks, _desireChecks;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Realm(Clan __instance, ref Kingdom __result) { __result = Realms[__instance]; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    private static bool ClanById(string clanId, ref Clan __result) { Clans.TryGetValue(clanId, out __result); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Clans.Values.ToList()); return false; }
    private static bool HasClaim(FeudalTitleRecord targetTitle, ref bool __result)
    { __result = Claimed.Contains(targetTitle.TitleId); return false; }
    private static bool Geometry(FeudalTitleRecord firstTitle, FeudalTitleRecord secondTitle, ref bool __result)
    {
        _distanceChecks++;
        __result = firstTitle.TitleId != secondTitle.TitleId && (Neighbors.Contains(firstTitle.TitleId + ":" + secondTitle.TitleId)
            || Neighbors.Contains(secondTitle.TitleId + ":" + firstTitle.TitleId));
        return false;
    }
    private static void CountTrack() => _trackChecks++;
    private static bool Desire(ref float __result) { _desireChecks++; __result = 3; return false; }
    private static bool Zero(ref float __result) { __result = 0; return false; }
    private static bool One(ref float __result) { __result = 1; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Year(ref int __result) { __result = 84; return false; }
    private static bool Eligible(Clan clan, ref bool __result)
    { __result = clan != null && !Ineligible.Contains(clan); return false; }
    private static bool Evaluate(Clan clan, ref string report, ref bool __result)
    { Evaluated.Add(clan.StringId); report = "fixture"; __result = false; return false; }

    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (!IsLoading)
            {
                Data[key] = value is List<string> list ? (object)new List<string>(list)
                    : value is Dictionary<string, float> dictionary ? new Dictionary<string, float>(dictionary) : (object)value;
                return true;
            }
            if (!Data.TryGetValue(key, out var saved)) return false;
            value = (T)saved;
            return true;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.feudal_politics");
        var titles = new FeudalTitleBehavior();
        var priorTitles = FeudalTitleBehavior.Instance;
        var instance = AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance");
        AccessTools.Field(typeof(FeudalTitleBehavior), "_titleDisplayRefreshPending").SetValue(titles, false);
        void Patch(MethodBase method, string prefix) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FeudalPoliticalPerformanceTests), prefix));
        Clan House(string id, Kingdom realm, int gold = 1000000)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            var hero = Blank<Hero>(); hero.Gold = gold;
            Clans.Add(id, clan); Leaders.Add(clan, hero); Realms.Add(clan, realm);
            return clan;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            var fabrication = typeof(FeudalClaimFabricationBehavior);
            Patch(AccessTools.Method(fabrication, "ResolveClan"), nameof(ClanById));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "HasActiveClaim"), nameof(HasClaim));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "AreTitlesAdjacentForTitleLogic"), nameof(Geometry));
            Patch(AccessTools.Method(fabrication, "TryGetFabricationTrack"), nameof(CountTrack));
            Patch(AccessTools.Method(fabrication.Assembly.GetType("BellumCivile.ClanFiefDesireHelper"),
                "CalculateRebelliousFiefDesirePressure"), nameof(Desire));
            foreach (string scorer in new[] { "CalculateFabricationRelationScore", "CalculateFabricationBudgetComfortScore",
                "CalculateFabricationTacticalPersonalityScore" })
                Patch(AccessTools.Method(fabrication, scorer), nameof(Zero));
            Patch(AccessTools.Method(fabrication, "CalculateFabricationPersonalityMultiplier"), nameof(One));

            var home = Blank<Kingdom>(); home.StringId = "home";
            var abroad = Blank<Kingdom>(); abroad.StringId = "abroad";
            var house = House("house", home);
            var vassal = House("vassal", home);
            var foreign = House("foreign", abroad);
            var landless = House("landless", home);
            var baron = House("baron", home);
            var all = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
            var rebuild = AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes");
            void Reindex() => rebuild.Invoke(titles, new object[] { true, false });
            FeudalTitleRecord Add(string id, FeudalTitleType rank, Clan legal, Clan actual, string parent = "",
                Kingdom realm = null, bool active = true, string name = null)
            {
                var title = new FeudalTitleRecord(id, name ?? id, rank, legal.StringId, actual.StringId,
                    parent, "", (realm ?? home).StringId, 0, 0, active);
                all.Add(id, title);
                return title;
            }
            var crown = Add("crown", FeudalTitleType.Kingdom, vassal, vassal);
            var lord = Add("lord", FeudalTitleType.Duchy, house, house, crown.TitleId);
            Add("controlled", FeudalTitleType.County, foreign, house);
            Add("vassal_county", FeudalTitleType.County, vassal, vassal, lord.TitleId);
            Add("reclaimed_county", FeudalTitleType.County, foreign, foreign, lord.TitleId, abroad);
            Add("tie_b", FeudalTitleType.Duchy, foreign, foreign, realm: abroad, name: "Equal");
            Add("tie_a", FeudalTitleType.Duchy, foreign, foreign, realm: abroad, name: "Equal");
            Add("claimed", FeudalTitleType.Duchy, foreign, foreign, realm: abroad);
            Add("closed", FeudalTitleType.Duchy, foreign, foreign, realm: abroad, active: false);
            Add("landless_barony", FeudalTitleType.Barony, foreign, foreign, realm: home);
            Add("baron_hold", FeudalTitleType.Barony, baron, baron);
            for (int i = 0; i < 3200; i++)
                Add("other_" + i, FeudalTitleType.Barony, foreign, foreign, realm: abroad);
            foreach (string id in new[] { "tie_a", "tie_b", "claimed", "closed" }) Neighbors.Add(lord.TitleId + ":" + id);
            Neighbors.Add("baron_hold:other_7");
            Neighbors.Add("baron_hold:other_999");
            Claimed.Add("claimed");
            Reindex();

            var held = AccessTools.Method(fabrication, "GetHeldTitles");
            var score = AccessTools.Method(fabrication, "TryBuildAiFabricationCandidate");
            var indexed = AccessTools.Method(fabrication, "BuildAiFabricationCandidates");
            object[] Legacy(Clan clan)
            {
                object holdings = held.Invoke(null, new object[] { titles, clan });
                var result = new List<object>();
                foreach (var title in titles.GetAllTitles().Where(t => t.IsActive && t.DeJureHolderClanId != clan.StringId
                    && !titles.HasActiveClaim(clan, t)))
                {
                    object[] args = { titles, clan, title, holdings, null, true, false, null };
                    if ((bool)score.Invoke(null, args)) result.Add(args[4]);
                }
                return result.ToArray();
            }
            object[] Indexed(Clan clan) => ((IEnumerable)indexed.Invoke(null, new object[] { titles, clan })).Cast<object>().ToArray();
            string Key(object entry)
            {
                var type = entry.GetType();
                object Get(string name) => AccessTools.Property(type, name).GetValue(entry);
                return ((FeudalTitleRecord)Get("Title")).TitleId + ":" + Get("Track") + ":" + Get("Score");
            }
            void Compare(Clan clan, string label, bool count = false)
            {
                _trackChecks = _distanceChecks = _desireChecks = 0;
                var old = Legacy(clan).Select(Key).ToArray();
                int oldTracks = _trackChecks;
                int oldDistances = _distanceChecks;
                _trackChecks = _distanceChecks = _desireChecks = 0;
                var next = Indexed(clan).Select(Key).ToArray();
                check(old.SequenceEqual(next), label + ": indexed search preserves targets, scores and order");
                if (!count) return;
                check(next.Length == 6 && next[4].StartsWith("tie_b:Horizontal") && next[5].StartsWith("tie_a:Horizontal"),
                    "All five fabrication tracks and cross-border exact ties remain available");
                check(_trackChecks < oldTracks && _desireChecks == 1,
                    "Indexed search cuts full track checks and computes clan desire once");
                Console.WriteLine($"Large-map fixture ({all.Count} titles): full track checks {oldTracks} -> {_trackChecks}; "
                    + $"adjacency checks {oldDistances} -> {_distanceChecks}.");
            }
            Compare(house, "Landed clan with adjoining foreign holdings", count: true);
            Compare(baron, "Baron retains same-rank cross-border options");
            check(_trackChecks == 0 && _distanceChecks > 3200 && Indexed(baron).Select(Key).Count() == 2,
                "A baron-heavy map still requires exact same-rank adjacency checks, but skips full track validation for those neighbors");
            Compare(landless, "Landless clan retains legally associated realm baronies");
            Leaders[house].Gold = 0;
            Compare(house, "Unaffordable clan");
            Leaders[house].Gold = 1000000;
            Add("new_neighbor", FeudalTitleType.Duchy, foreign, foreign, realm: abroad);
            Neighbors.Add(lord.TitleId + ":new_neighbor");
            crown.SetActive(false);
            Reindex();
            Compare(house, "Registry change invalidates the temporary search index");
            check(Indexed(house).Select(Key).Any(key => key.StartsWith("new_neighbor:"))
                && Indexed(house).Select(Key).All(key => !key.StartsWith("crown:")),
                "Updated search finds newly formed titles and excludes inactive titles");

            instance.Invoke(null, new object[] { titles });
            for (int i = 1; i <= 7; i++) House("scheduled_" + i, home);
            TestScheduler(check, harmony);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            instance.Invoke(null, new object[] { priorTitles });
            Clans.Clear(); Realms.Clear(); Leaders.Clear(); Neighbors.Clear(); Claimed.Clear();
            Ineligible.Clear(); Evaluated.Clear();
        }
    }

    private static void TestScheduler(Action<bool, string> check, Harmony harmony)
    {
        var type = typeof(FeudalPoliticalOptionsBehavior);
        void Patch(MethodBase method, string prefix) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FeudalPoliticalPerformanceTests), prefix));
        Patch(AccessTools.PropertyGetter(type, "CurrentDay"), nameof(Day));
        Patch(AccessTools.Method(type, "GetCampaignDaysInYear"), nameof(Year));
        Patch(AccessTools.PropertyGetter(typeof(MBRandom), "RandomFloat"), nameof(Zero));
        Patch(AccessTools.Method(type, "IsValidPoliticalActionClan"), nameof(Eligible));
        Patch(AccessTools.Method(type, "TryRunPoliticalOptionsForClan"), nameof(Evaluate));
        void Call(FeudalPoliticalOptionsBehavior behavior, string name, params object[] args) =>
            AccessTools.Method(type, name).Invoke(behavior, args);
        float Due(FeudalPoliticalOptionsBehavior behavior, string id) => ((Dictionary<string, float>)
            AccessTools.Field(type, "_nextClanEvaluationDays").GetValue(behavior))[id];

        _day = 100;
        var store = new Store { IsLoading = true };
        store.Data["BellumCivile_FeudalPoliticalOptionNextClanEvaluationDays"] = Enumerable.Range(1, 7)
            .ToDictionary(i => "scheduled_" + i, i => i == 3 ? 98f : 99f);
        Ineligible.UnionWith(Clans.Values.Where(clan => !clan.StringId.StartsWith("scheduled_")));
        var scheduler = new FeudalPoliticalOptionsBehavior();
        scheduler.SyncData(store);
        Call(scheduler, "OnSessionLaunched", new object[] { null });
        Call(scheduler, "OnDailyTick");
        check(Evaluated.Count == 0, "Daily tick selects due clans without evaluating them in a batch");
        _day += 1f / 24;
        Call(scheduler, "OnHourlyTick");
        check(Evaluated.SequenceEqual(new[] { "scheduled_3" }) && Due(scheduler, "scheduled_3") == 163,
            "First hourly tick runs the oldest due clan and preserves next evaluation cadence");

        var snapshot = new Store();
        scheduler.SyncData(snapshot); snapshot.IsLoading = true;
        scheduler = new FeudalPoliticalOptionsBehavior(); scheduler.SyncData(snapshot);
        Call(scheduler, "OnSessionLaunched", new object[] { null });
        Call(scheduler, "OnDailyTick");
        Ineligible.Add(Clans["scheduled_2"]);
        for (int i = 0; i < 7; i++) { _day += 1f / 24; Call(scheduler, "OnHourlyTick"); }
        check(Evaluated.SequenceEqual(new[] { "scheduled_3", "scheduled_1", "scheduled_4", "scheduled_5" }),
            "Reload preserves batch order and quota, and a newly ineligible house is skipped");
        Call(scheduler, "OnDailyTick");
        Call(scheduler, "OnHourlyTick");
        check(Evaluated.Count == 4, "A repeated daily callback cannot refill the quota on the same day");
        _day = 101;
        Call(scheduler, "OnDailyTick");
        Call(scheduler, "OnHourlyTick");
        Call(scheduler, "OnHourlyTick");
        check(Evaluated.Skip(4).SequenceEqual(new[] { "scheduled_6", "scheduled_7" }),
            "Overdue houses outside the previous cap are processed the following day");
    }
}
