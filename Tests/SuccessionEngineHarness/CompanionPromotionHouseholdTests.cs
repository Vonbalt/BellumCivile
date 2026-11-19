using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

internal static class CompanionPromotionHouseholdTests
{
    private sealed class Store : IDataStore
    {
        internal object Pending;
        public bool IsSaving { get; set; } = true;
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Pending = data;
            else data = (T)Pending;
            return true;
        }
    }

    private static Clan _source, _destination;
    private static Hero _player;
    private static readonly List<(Hero Hero, Clan Former)> Changed = new List<(Hero, Clan)>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool PlayerClan(ref Clan __result) { __result = _source; return false; }
    private static bool MainHero(ref Hero __result) { __result = _player; return false; }
    private static bool FindClan(Func<Clan, bool> __0, ref Clan __result)
    { __result = __0(_destination) ? _destination : null; return false; }
    private static bool RecordChange(Hero __0, Clan __1) { Changed.Add((__0, __1)); return false; }
    private static bool Skip() => false;
    private static bool NoMapEvent(ref MapEvent __result) { __result = null; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var harmony = new Harmony("bellum.tests.companion_promotion_household");
        var behaviorType = typeof(CompanionSubinfeudationBehavior);
        var onCreated = AccessTools.Method(behaviorType, "OnClanCreated");
        var retry = AccessTools.Method(behaviorType, "RetryHouseholdMoves");
        var membership = behaviorType.Assembly.GetType("BellumCivile.HouseholdMembership", true);
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(CompanionPromotionHouseholdTests), prefix));
        void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
        Clan House(string id, Kingdom realm)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            Set(clan, "_kingdom", realm);
            foreach (string cache in new[] { "_heroesCache", "_companionsCache", "_aliveLordsCache", "_deadLordsCache" })
                Set(clan, cache, new MBList<Hero>());
            return clan;
        }
        Hero Person(Clan clan, string id, Hero.CharacterStates state = Hero.CharacterStates.Active, bool companion = false)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            Set(hero, "_heroState", state);
            Set(hero, "_children", new MBList<Hero>());
            Set(hero, "_exSpouses", new MBList<Hero>());
            AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero, new object[] { companion ? Occupation.Wanderer : Occupation.Lord });
            if (companion) hero.CompanionOf = clan;
            else hero.Clan = clan;
            return hero;
        }

        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(PlayerClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.Method(typeof(Clan), "FindFirst"), nameof(FindClan));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroChangedClan"), nameof(RecordChange));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(MobileParty), "MapEvent"), nameof(NoMapEvent));
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Set(campaign, "<CampaignEventDispatcher>k__BackingField", Blank<CampaignEventDispatcher>());
            var realm = Blank<Kingdom>();
            foreach (string cache in new[] { "_heroesCache", "_aliveLordsCache", "_deadLordsCache" })
                Set(realm, cache, new MBList<Hero>());
            _source = House("player", realm); _destination = House("new_house", realm);
            var foreign = House("foreign", null);
            _player = Person(_source, "player_head"); Set(_source, "_leader", _player);
            var founder = Person(_destination, "founder"); Set(_destination, "_leader", founder);
            var spouse = Person(_source, "spouse", companion: true); founder.Spouse = spouse; spouse.Spouse = founder;
            var child = Person(_source, "child", Hero.CharacterStates.NotSpawned); child.Father = founder;
            var prisoner = Person(_source, "prisoner", Hero.CharacterStates.Prisoner); prisoner.Father = founder;
            var parent = Person(_source, "parent"); founder.Father = parent;
            var sibling = Person(_source, "sibling"); sibling.Father = parent;
            var siblingSpouse = Person(_source, "sibling_spouse"); siblingSpouse.Spouse = sibling;
            var dead = Person(_source, "dead", Hero.CharacterStates.Dead); dead.Father = founder;
            var abroad = Person(foreign, "abroad"); abroad.Father = founder;
            var formerSpouse = Person(_source, "former_spouse"); formerSpouse.ExSpouses.Add(founder);
            var dynasticChild = Person(_source, "dynastic_child"); dynasticChild.Father = founder; dynasticChild.Mother = _player;
            var unrelated = Person(_source, "retainer", companion: true);
            var busy = Person(_source, "busy_spouse"); busy.Spouse = prisoner;
            var seaParty = Blank<MobileParty>(); Set(seaParty, "_isCurrentlyAtSea", true);
            Set(busy, "_partyBelongedTo", seaParty);

            var behavior = new CompanionSubinfeudationBehavior();
            Changed.Clear();
            onCreated.Invoke(behavior, new object[] { _destination, false });
            check(Changed.Count == 0 && child.Clan == _source, "Ordinary clan creation does not move anyone");
            onCreated.Invoke(behavior, new object[] { _destination, true });
            var movers = new[] { spouse, child, prisoner, parent, sibling, siblingSpouse };
            check(movers.All(h => h.Clan == _destination && !_source.Heroes.Contains(h))
                && Changed.Count == movers.Length && Changed.All(change => change.Former == _source),
                "A companion grant moves current spouses and blood relatives and raises one native clan-change event per hero");
            check(spouse.CompanionOf == _destination && spouse.IsWanderer && _destination.Companions.Contains(spouse)
                && child.IsNotSpawned && prisoner.IsPrisoner,
                "The new house keeps native companion rosters, child state and captivity intact");
            check(new[] { dead, formerSpouse, dynasticChild, unrelated, _player }.All(h => h.Clan == _source)
                && abroad.Clan == foreign, "Deceased and former relatives, player kin, outside clans and unrelated retainers stay put");
            check(busy.Clan == _source, "A relative at sea keeps their party and waits for a safe transfer");

            var save = new Store(); behavior.SyncData(save);
            var loaded = new CompanionSubinfeudationBehavior(); save.IsSaving = false; loaded.SyncData(save);
            Set(busy, "_partyBelongedTo", null);
            retry.Invoke(loaded, null);
            retry.Invoke(loaded, null);
            check(busy.Clan == _destination && Changed.Count(c => c.Hero == busy) == 1,
                "A saved pending relative follows once safe, even after their spouse has already moved");
        }
        finally
        {
            AccessTools.Method(membership, "Invalidate").Invoke(null, new object[] { null });
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            Changed.Clear(); _source = _destination = null; _player = null;
        }
    }
}
