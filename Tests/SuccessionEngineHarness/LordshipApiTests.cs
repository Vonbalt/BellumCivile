using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class LordshipApiTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
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
            if (IsSaving) _data[key] = value;
            else if (_data.TryGetValue(key, out var stored)) value = (T)stored;
            return true;
        }
    }

    private static Clan _player;
    private static Hero _main;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainHero(ref Hero __result) { __result = _main; return false; }
    private static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var oldRegistry = LordshipBehavior.Instance;
        var harmony = new Harmony("bellum.tests.lordship-api");
        void Current(Campaign campaign) => AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
        void Registry(LordshipBehavior registry) => AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
        void Index(LordshipBehavior registry) => AccessTools.Method(typeof(LordshipBehavior), "Reindex").Invoke(registry, null);
        List<PersonalLordshipRecord> Grants(LordshipBehavior registry) => (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
        bool Rejected(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        Hero Person(Clan clan, string id)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            Field(hero, "_clan", clan); Field(hero, "_heroState", Hero.CharacterStates.Active);
            return hero;
        }
        Settlement Village(Clan owner, string id, out Settlement parent)
        {
            parent = Blank<Settlement>(); parent.StringId = id + "_castle";
            var town = Blank<Town>(); Field(town, "_ownerClan", owner); Field(parent, "Town", town);
            var village = Blank<Settlement>(); village.StringId = id;
            var component = Blank<Village>(); Field(component, "_bound", parent); Field(village, "Village", component);
            return village;
        }
        try
        {
            Current(null);
            check(Rejected(() => BellumIntegration.GetPlayerNominalFiefIds())
                && Rejected(() => BellumIntegration.IsPlayerNominalFief(null)), "Both queries retain the API's active-campaign requirement");
            var campaign = Blank<Campaign>(); Current(campaign);
            var manager = new Manager(); campaign.AddCampaignBehaviorManager(manager);
            manager.Items.Add(new BellumIntegrationBehavior());
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(LordshipApiTests), nameof(Player)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), prefix: new HarmonyMethod(typeof(LordshipApiTests), nameof(MainHero)));
            var realm = Blank<Kingdom>(); realm.StringId = "realm";
            var rebels = Blank<Kingdom>(); rebels.StringId = "rebels";
            var source = Blank<Clan>(); source.StringId = "granting_house"; Field(source, "_kingdom", realm);
            _player = Blank<Clan>(); _player.StringId = "player"; Field(_player, "_kingdom", realm);
            _main = Person(_player, "original_player"); Field(_player, "_leader", _main);
            var ruler = Person(source, "grantor"); Field(source, "_leader", ruler); Field(realm, "_rulingClan", source);
            var village = Village(source, "village_A1", out var parent);
            var ordinary = Village(_player, "native_owned_village", out var playerCastle);
            var registry = new LordshipBehavior(); Registry(registry); Field(registry, "_campaign", campaign);
            void Empty(string reason) => check(!BellumIntegration.IsPlayerNominalFief(village)
                && BellumIntegration.GetPlayerNominalFiefIds().Count == 0, reason);
            Registry(null); Empty("An absent lordship registry returns false/empty without creating one"); Registry(registry);
            var grant = new PersonalLordshipRecord { Village = village, Parent = parent, GrantingClan = source,
                Holder = _main, BeneficiaryClan = _player, Grantor = ruler, IsVassalGrant = true };
            Grants(registry).Add(grant); Index(registry);
            Empty("Saved grants remain unavailable until lordship initialization finishes");
            Field(registry, "_ready", true);
            var api = System.Type.GetType("BellumCivile.BellumIntegration, BellumCivile", true);
            var getIds = api.GetMethod("GetPlayerNominalFiefIds", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var isFief = api.GetMethod("IsPlayerNominalFief", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Settlement) }, null);
            check(getIds?.ReturnType == typeof(IReadOnlyList<string>) && isFief?.ReturnType == typeof(bool)
                && (int)api.GetField("ApiVersion").GetRawConstantValue() == 1, "Reflection exposes the requested signatures on the existing additive version-1 API");
            var ids = getIds.Invoke(null, null) as IReadOnlyList<string>;
            check(ids?.Count == 1 && ids[0] == village.StringId && (bool)isFief.Invoke(null, new object[] { village }),
                "The mod author's reflection pattern returns the player's current village StringId");
            check(!BellumIntegration.IsPlayerNominalFief(null) && !BellumIntegration.IsPlayerNominalFief(parent)
                && !BellumIntegration.IsPlayerNominalFief(ordinary) && !BellumIntegration.IsPlayerNominalFief(playerCastle),
                "Null, castles and villages attached to native player holdings are excluded");
            int revision = registry.Revision;
            if (ids is string[] array) array[0] = "consumer_changed_snapshot";
            check(BellumIntegration.GetPlayerNominalFiefIds().Single() == village.StringId && registry.Revision == revision
                && parent.OwnerClan == source && Grants(registry).Single() == grant,
                "Returned IDs are detached snapshots and querying does not change grants, revisions or native ownership");
            check(Task.Run(() => Rejected(() => BellumIntegration.GetPlayerNominalFiefIds())
                && Rejected(() => BellumIntegration.IsPlayerNominalFief(village))).Result,
                "Both queries reject background access before reading mutable campaign state");
            Field(registry, "_campaign", Blank<Campaign>()); Empty("A registry from another campaign cannot leak stale grants"); Field(registry, "_campaign", campaign);
            var initialHero = _main;
            _main = null; Empty("No active player character returns false/empty"); _main = initialHero;
            var otherHolder = Person(_player, "retainer");
            grant.Holder = otherHolder; Index(registry); Empty("A grant held by a player-clan retainer is not the player's personal fief");
            grant.Holder = initialHero; Index(registry);
            Field(_player, "_kingdom", null); Empty("Leaving the realm immediately excludes the grant without waiting for maintenance");
            check(Grants(registry).Contains(grant), "The read-only query does not delete a temporarily retained grant");
            Field(_player, "_kingdom", rebels); Empty("A one-sided crown rebellion is excluded while the two houses are in different realms");
            Field(source, "_kingdom", rebels);
            check(BellumIntegration.IsPlayerNominalFief(village), "Both houses arriving in the same rebellion preserve the personal grant");
            Field(_player, "_kingdom", realm); Field(source, "_kingdom", realm);
            var newRulerHouse = Blank<Clan>(); newRulerHouse.StringId = "new_ruling_house"; Field(newRulerHouse, "_kingdom", realm);
            Field(realm, "_rulingClan", newRulerHouse);
            check(BellumIntegration.IsPlayerNominalFief(village), "Changing the monarch alone does not remove the granting house's endowment");
            Field(parent.Town, "_ownerClan", newRulerHouse); Empty("The granting house losing the parent barony immediately excludes its grant");
            Field(parent.Town, "_ownerClan", source);
            Field(initialHero, "_heroState", Hero.CharacterStates.Dead); Empty("The recipient's death immediately excludes the nonhereditary grant");
            var heir = Person(_player, "player_heir"); _main = heir; Empty("A player successor does not inherit the predecessor's personal lordship");
            _main = initialHero; Field(initialHero, "_heroState", Hero.CharacterStates.Active);
            Field(initialHero, "_clan", newRulerHouse); Empty("The recipient leaving their beneficiary clan invalidates the grant"); Field(initialHero, "_clan", _player);
            var savedIds = BellumIntegration.GetPlayerNominalFiefIds();
            var store = new Store(); registry.SyncData(store);
            var loaded = new LordshipBehavior(); store.IsLoading = true; loaded.SyncData(store); Registry(loaded); Field(loaded, "_campaign", campaign);
            Empty("Deserialization alone does not publish uninitialized lookup indexes");
            Index(loaded); Field(loaded, "_ready", true);
            check(BellumIntegration.GetPlayerNominalFiefIds().SequenceEqual(savedIds), "Existing saved grants become queryable after index restoration without replaying grant events");
            // Simulate a new load reconstructing native settlement objects while retaining their StringIds.
            var reloadedVillage = Village(source, village.StringId, out var reloadedParent);
            Grants(loaded).Clear();
            var reloadedGrant = new PersonalLordshipRecord { Village = reloadedVillage, Parent = reloadedParent,
                GrantingClan = source, Holder = _main, BeneficiaryClan = _player, Grantor = ruler, IsVassalGrant = true };
            Grants(loaded).Add(reloadedGrant); Index(loaded);
            check(!BellumIntegration.IsPlayerNominalFief(village) && BellumIntegration.IsPlayerNominalFief(reloadedVillage)
                && BellumIntegration.GetPlayerNominalFiefIds().SequenceEqual(savedIds),
                "Rebuilt settlement references retain stable ID results without exposing stale native objects");
            Grants(loaded).Clear(); Index(loaded);
            check(!BellumIntegration.IsPlayerNominalFief(reloadedVillage) && BellumIntegration.GetPlayerNominalFiefIds().Count == 0
                && savedIds.Count == 1, "Revocation/return disappears from new queries while prior snapshots remain unchanged");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); Registry(oldRegistry); Current(oldCampaign);
            _player = null; _main = null;
        }
    }
}
