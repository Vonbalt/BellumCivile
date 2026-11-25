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
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

internal static class WarReconciliationTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Behaviors = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Behaviors.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Behaviors.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Behaviors.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Behaviors.RemoveAll(b => b is T);
        public void ClearBehaviors() => Behaviors.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Behaviors.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Store : IDataStore
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsLoading)
            {
                if (!_data.TryGetValue(key, out var saved)) return false;
                value = (T)saved;
                return true;
            }
            _data[key] = value is Dictionary<string, bool> momentum ? (object)new Dictionary<string, bool>(momentum)
                : value is List<WarScoreRecord> wars ? new List<WarScoreRecord>(wars) : (object)value;
            return true;
        }
    }

    private sealed class CustomStanceModel : DefaultDiplomacyModel
    {
        internal Kingdom First, Second;
        public override DiplomacyStance? GetShallowDiplomaticStance(IFaction first, IFaction second)
            => (first == First && second == Second) || (first == Second && second == First)
                ? DiplomacyStance.War : base.GetShallowDiplomaticStance(first, second);
    }

    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly List<(Kingdom Realm, Kingdom Target, bool Defender, float Load)> Openings = new List<(Kingdom, Kingdom, bool, float)>();
    private static int _day, _warChecks, _randomRolls;
    private static string _storyRealmId;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(new List<Clan>()); return false; }
    private static bool Time(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Roll(ref float __result) { __result = _randomRolls++ % 2 == 0 ? .1f : .9f; return false; }
    private static bool Enabled(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static void CountWarCheck() => _warChecks++;
    private static bool Story(Kingdom first, Kingdom second, ref bool __result)
    { __result = first.StringId == _storyRealmId || second.StringId == _storyRealmId; return false; }
    private static bool Opening(Kingdom kingdom, Kingdom target, bool isDefender, float existingConflictLoad)
    { Openings.Add((kingdom, target, isDefender, existingConflictLoad)); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.war_reconciliation");
        var army = typeof(WarScoreBehavior);
        var will = typeof(WarPeaceRevampBehavior);
        var mod = army.Assembly;
        var pairs = mod.GetType("BellumCivile.WarPeace.ActiveKingdomWarPairs", true);
        var capture = AccessTools.Method(pairs, "Capture");
        var setCampaign = AccessTools.PropertySetter(typeof(Campaign), "Current");
        var previousCampaign = Campaign.Current;
        var gameTime = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object previousTime = gameTime.GetValue(null);
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(WarReconciliationTests), prefix));
        void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        var campaign = Blank<Campaign>();
        var models = Blank<GameModels>();
        void UseModel(object model) => AccessTools.PropertySetter(typeof(GameModels), "DiplomacyModel").Invoke(models, new[] { model });
        var manager = new Manager();
        var runtimeDiplomacy = new CivilWarDiplomacyModel(new DefaultDiplomacyModel());
        AccessTools.PropertySetter(typeof(Campaign), "FactionManager").Invoke(campaign, new object[] { new FactionManager() });
        var dispatcher = Activator.CreateInstance(typeof(CampaignEventDispatcher), BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { Array.Empty<CampaignEventReceiver>() }, null);
        AccessTools.PropertySetter(typeof(Campaign), "CampaignEventDispatcher").Invoke(campaign, new[] { dispatcher });
        campaign.AddCampaignBehaviorManager(manager);
        Set(campaign, "_gameModels", models);
        Kingdom Realm(string id)
        {
            var realm = Blank<Kingdom>();
            realm.Id = new MBGUID((uint)Realms.Count + 1);
            realm.StringId = id;
            var house = Blank<Clan>();
            house.StringId = id + "_house";
            Set(house, "_kingdom", realm);
            Set(realm, "_rulingClan", house);
            Set(realm, "_factionsAtWarWith", new MBList<IFaction>());
            Set(realm, "_settlementsCache", new MBList<Settlement>());
            Realms.Add(realm);
            return realm;
        }
        MBList<IFaction> Enemies(Kingdom realm) => (MBList<IFaction>)AccessTools.Field(typeof(Kingdom), "_factionsAtWarWith").GetValue(realm);
        string Key(Kingdom first, Kingdom second) => first.StringId + "|" + second.StringId;
        (Kingdom First, Kingdom Second)[] Capture(bool exhaustive) => ((IEnumerable<(Kingdom, Kingdom)>)capture.Invoke(null,
            new object[] { (Func<Kingdom, bool>)(k => k != null), exhaustive })).ToArray();
        void Reconcile(WarScoreBehavior score, bool exhaustive = false) => AccessTools.Method(army, "ReconcileExistingWars").Invoke(score, new object[] { exhaustive });
        void Initialize(WarPeaceRevampBehavior momentum, bool exhaustive = false) => AccessTools.Method(will, "InitializeExistingWarMomentum").Invoke(momentum, new object[] { exhaustive });
        Dictionary<string, bool> Markers(WarPeaceRevampBehavior momentum) => (Dictionary<string, bool>)AccessTools.Field(will,
            "_initializedWarMomentumByConflictId").GetValue(momentum);

        try
        {
            setCampaign.Invoke(null, new object[] { campaign });
            UseModel(runtimeDiplomacy);
            gameTime.SetValue(null, 24000L);
            _day = 25;
            _randomRolls = 0;
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(AllRealms));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Time));
            Patch(AccessTools.PropertyGetter(typeof(MBRandom), "RandomFloat"), nameof(Roll));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(CountWarCheck));
            Patch(AccessTools.PropertyGetter(mod.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp"), nameof(Enabled));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.StorylineWarProtectionHelper"), "IsPeaceBlocked"), nameof(Story));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileLogger"), "Log"), nameof(Skip));
            Patch(AccessTools.Method(will, "ApplyWarOpeningMomentum"), nameof(Opening));

            var quiet = Realm("z_quiet"); var c = Realm("c"); var a = Realm("a"); var b = Realm("b");
            var d = Realm("d"); var story = Realm("story"); var e = Realm("e");
            var feud = Realm("bc_feud_fixture"); var rebel = Realm("rebel_fixture"); var rival = Realm("rival_fixture");
            var savedOnly = Realm("saved_without_cache"); var synthetic = Realm("custom_stance");
            while (Realms.Count < 55) Realm("quiet_" + Realms.Count);
            _storyRealmId = story.StringId;
            // Native stance changes without WarDeclared or MakePeace notifications.
            FactionManager.DeclareWar(a, b);
            FactionManager.DeclareWar(a, c);
            FactionManager.DeclareWar(b, d);
            FactionManager.DeclareWar(a, story);
            Enemies(a).Remove(c); // Only c reports this war.
            Enemies(a).Add(b); // Duplicated entry on top of the reciprocal links.
            Enemies(a).Add(quiet); // Stale, no live war.
            Enemies(a).Add(a.RulingClan);

            _warChecks = 0;
            var legacy = Capture(true);
            int legacyChecks = _warChecks;
            _warChecks = 0;
            var indexed = Capture(false);
            check(legacy.SequenceEqual(indexed) && indexed.SequenceEqual(new[] { (a, c), (a, b), (a, story), (b, d) }),
                "Sparse recovery preserves encounter order and handles duplicates, stale entries and one-sided links");
            check(legacyChecks == 1485 && _warChecks == 5, "55-realm fixture reduces live stance checks from 1,485 to five per scan");

            var score = new WarScoreBehavior();
            var momentum = new WarPeaceRevampBehavior();
            var factions = new FactionManagerBehavior();
            manager.Behaviors.AddRange(new CampaignBehaviorBase[] { score, momentum, factions });
            Reconcile(score);
            var wars = score.GetActiveWars();
            check(wars.Select(w => w.WarKey).SequenceEqual(legacy.Select(pair => Key(pair.First, pair.Second)))
                && wars.Select(w => w.AttackerKingdomId).SequenceEqual(new[] { "a", "b", "a", "d" }),
                "Native declarations without campaign callbacks are found in legacy order, using one draw per recovered pair");
            Reconcile(score);
            check(_randomRolls == 4 && score.GetActiveWars().SequenceEqual(wars), "Repeated reconciliation neither duplicates trackers nor rolls again");

            Initialize(momentum);
            float Load(Kingdom realm, Kingdom enemy) => Openings.Single(row => row.Realm == realm && row.Target == enemy).Load;
            check(Openings.Where(row => !row.Defender).Select(row => row.Realm.StringId).SequenceEqual(wars.Select(w => w.AttackerKingdomId)),
                "Opening enthusiasm uses the ledger's recorded attackers and order");
            check(Load(a, c) == 0 && Load(a, b) == 1 && Load(b, d) == 1,
                "Opening enthusiasm counts earlier initialized regular fronts in encounter order");
            check(Load(a, story) == 0 && Load(story, a) == 0, "A storyline war opens without a multiple-front penalty");
            var store = new Store();
            score.SyncData(store); momentum.SyncData(store);
            store.IsLoading = true;
            manager.Behaviors.Remove(score); manager.Behaviors.Remove(momentum);
            score = new WarScoreBehavior(); momentum = new WarPeaceRevampBehavior();
            score.SyncData(store); momentum.SyncData(store);
            manager.Behaviors.AddRange(new CampaignBehaviorBase[] { score, momentum });
            Openings.Clear();
            Reconcile(score, exhaustive: true); Initialize(momentum, exhaustive: true);
            check(_randomRolls == 4 && Openings.Count == 0 && score.GetActiveWars().Count == 4 && Markers(momentum).Count == 4,
                "Session recovery after save/load keeps existing trackers and does not repeat enthusiasm");

            _day++;
            FactionManager.DeclareWar(e, a);
            Reconcile(score); Initialize(momentum);
            check(Openings.Count == 2 && Load(a, e) == 2, "A late missed declaration counts saved regular fronts but not the saved storyline front");
            var oldWar = score.GetActiveWar(a, c);
            FactionManager.SetNeutral(a, c);
            Enemies(a).Add(c);
            Reconcile(score);
            check(!oldWar.IsActive && score.GetActiveWar(a, c) == null, "Missed peace still closes the tracker even with a stale enemy entry");
            _day++;
            Openings.Clear();
            FactionManager.DeclareWar(c, a);
            Reconcile(score); Initialize(momentum);
            check(score.GetActiveWar(a, c) != oldWar && Openings.Count == 2 && Load(a, c) == 2,
                "A new war on the same pair gets a fresh tracker and enthusiasm, without counting the old instance");

            var activeFactions = (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(factions);
            foreach (var shell in new[] { rebel, rival })
            {
                var faction = new FactionObject("fixture rebellion", a, shell.RulingClan, FactionType.Independence);
                faction.SetRebelKingdom(shell);
                activeFactions.Add(faction);
            }
            FactionManager.DeclareWar(a, rebel);
            FactionManager.DeclareWar(rebel, rival);
            FactionManager.DeclareWar(rebel, d);
            FactionManager.DeclareWar(feud, d);
            Openings.Clear();
            Reconcile(score); Initialize(momentum);
            check(score.GetActiveWar(rebel, d)?.ConflictType == WarScoreConflictType.ForeignWar
                && score.GetActiveWar(a, rebel) == null && score.GetActiveWar(rebel, rival) == null && score.GetActiveWar(feud, d) == null,
                "Only the rebel's external war enters the foreign ledger; internal and feud shell pairs stay out");
            check(Openings.Count == 0, "Opening enthusiasm recovery still excludes temporary realms, even in external wars");

            FactionManager.DeclareWar(a, savedOnly);
            Enemies(a).Remove(savedOnly); Enemies(savedOnly).Remove(a);
            Reconcile(score, exhaustive: true); Initialize(momentum, exhaustive: true);
            check(score.GetActiveWar(a, savedOnly) != null && Openings.Any(row => row.Target == savedOnly),
                "Startup scan recovers a native stance even if neither enemy list is populated yet");

            UseModel(new CivilWarDiplomacyModel(new CustomStanceModel { First = a, Second = synthetic }));
            Reconcile(score);
            check(score.GetActiveWar(a, synthetic) != null && !Enemies(a).Contains(synthetic) && !Enemies(synthetic).Contains(a),
                "A custom diplomacy model still recovers wars that exist outside the native enemy lists");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            setCampaign.Invoke(null, new object[] { previousCampaign });
            gameTime.SetValue(null, previousTime);
            Realms.Clear(); Openings.Clear(); _storyRealmId = null;
        }
    }
}
