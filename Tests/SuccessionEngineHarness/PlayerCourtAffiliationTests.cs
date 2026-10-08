using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PlayerCourtAffiliationTests
{
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Crowns = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static Clan _player, _ruler, _peer;
    private static Kingdom _home, _temporary;
    private static FactionManagerBehavior _manager;
    private static int _penalties, _registrations;
    private static readonly Type ManagerType = typeof(FactionManagerBehavior);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static List<FactionObject> Factions => (List<FactionObject>)AccessTools.Field(ManagerType, "_activeFactions").GetValue(_manager);
    private static object Call(string method, params object[] args) => AccessTools.Method(ManagerType, method).Invoke(_manager, args);
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool PlayerHero(ref Hero __result) { __result = Leaders[_player]; return false; }
    private static bool ClanRealm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Crowns.TryGetValue(__instance, out __result); return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(x => x.Value == __instance).Select(x => x.Key).ToList()); return false; }
    private static bool Alive(ref bool __result) { __result = true; return false; }
    private static bool NotNoble(ref bool __result) { __result = false; return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Pick(ref int __result) { __result = 0; return false; }
    private static bool Interval(ref int __result) { __result = 2; return false; }
    private static bool Skip() => false;
    private static bool Register(FactionObject newFaction)
    { Factions.Add(newFaction); _manager.InvalidateFactionLookupCache(false); _registrations++; return false; }
    private static bool FactionName(FactionType type, ref TextObject __result) { __result = new TextObject(type.ToString()); return false; }
    private static bool Penalty(Hero firstHero, Hero secondHero, int relationChange, string sourceId)
    {
        if (firstHero != Leaders[_player] || secondHero != Leaders[_peer] || relationChange != -1 || sourceId != "court_neutrality")
            throw new Exception("Incorrect court neutrality recipient or memory");
        _penalties++; return false;
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

    private static Clan Clan(string id, Kingdom realm)
    {
        var clan = Blank<Clan>(); clan.StringId = id;
        var hero = Blank<Hero>(); hero.StringId = id + "_leader";
        Realms[clan] = realm; Leaders[clan] = hero; return clan;
    }
    private static Kingdom Realm(string id) { var realm = Blank<Kingdom>(); realm.StringId = id; return realm; }
    private static FactionObject Setup(string shell, FactionType? type = FactionType.Nobility)
    {
        Realms.Clear(); Crowns.Clear(); Leaders.Clear(); _penalties = _registrations = 0;
        _manager = new FactionManagerBehavior();
        _home = Realm("home"); _temporary = Realm(shell);
        _player = Clan("player", _home); _ruler = Clan("ruler", _home); _peer = Clan("peer", _home);
        Crowns[_home] = _ruler; Crowns[_temporary] = _ruler;
        if (!type.HasValue) return null;
        var faction = new FactionObject("test court", _home, _peer, type.Value);
        faction.AddMember(_player); Factions.Add(faction);
        return faction;
    }
    private static void Move(Clan clan, Kingdom destination)
    {
        var old = Realms[clan]; Realms[clan] = destination;
        Call("OnClanChangedKingdom", clan, old, destination, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        _manager.InvalidateFactionLookupCache(false);
    }
    private static void Restore() => Call("RestorePlayerCourtAffiliation");
    private static void Neutrality() => AccessTools.Method(typeof(IdeologyBehavior), "ApplyPlayerNeutralityPenalty")
        .Invoke(new IdeologyBehavior(), new object[] { _manager });
    private static bool Pending => AccessTools.Field(ManagerType, "_playerCourtReturnClan").GetValue(_manager) != null;

    internal static void Run(Action<bool, string> check)
    {
        var oldInstance = FactionManagerBehavior.Instance;
        var h = new Harmony("bellum.tests.player_court_return");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(PlayerCourtAffiliationTests), name));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(PlayerHero));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(ClanRealm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int) }), nameof(Pick));
            Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int), typeof(int) }), nameof(Interval));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "Msg"), nameof(Skip));
            Patch(AccessTools.Method(typeof(RelationMemoryService), "ApplyChangeWithDefaultDuration"), nameof(Penalty));
            Patch(AccessTools.Method(typeof(FactionObject), "GetIdeologyDisplayName"), nameof(FactionName));
            Patch(AccessTools.Method(ManagerType, "RegisterNewFaction"), nameof(Register));
            Patch(AccessTools.Method(ManagerType, "QueueSettlementRefresh"), nameof(Skip));
            Patch(AccessTools.Method(ManagerType, "RepairReferencedKingdomRulerState"), nameof(Skip));
            Patch(AccessTools.Method(ManagerType.Assembly.GetType("BellumCivile.NobleClanEligibilityHelper"), "IsLiveNobleClan"), nameof(NotNoble));

            foreach (var shell in new[] { "bc_feud_test_claimant", "home_rebels_test" })
            foreach (var type in new[] { FactionType.Glory, FactionType.Nobility, FactionType.Liberty })
            {
                var faction = Setup(shell, type);
                Move(_player, _temporary);
                check(Pending, "Player court preference captured before temporary transfer: " + shell + "/" + type);
                check(faction.Members.Contains(_player) == shell.Contains("_rebels_"),
                    "Existing civil-war retention and feud detachment are unchanged");
                Realms[_peer] = _temporary; Realms[_ruler] = _temporary;
                Neutrality();
                check(_penalties == 0, "No neutrality penalty in a temporary conflict realm");
                // Exercise restoration even when a civil-war cleanup has already removed membership.
                faction.RemoveMember(_player);
                var save = new Store(); _manager.SyncData(save); save.IsLoading = true;
                _manager = new FactionManagerBehavior(); _manager.SyncData(save);
                check(Pending, "Player affiliation receipt survives campaign behavior save/load");
                Move(_player, _home); Restore(); Restore();
                check(_manager.GetIdeologicalFaction(_player) == faction && faction.Leader == _peer
                    && faction.Members.Count(c => c == _player) == 1 && !Pending && _registrations == 0,
                    "Return rejoins the same court once without replacing its leader");
                Neutrality(); check(_penalties == 0, "Restored affiliation prevents a postwar neutrality penalty");
            }

            var oldCourt = Setup("home_rebels_test"); Move(_player, _temporary); Move(_player, _home); Restore();
            check(_manager.GetIdeologicalFaction(_player) == oldCourt && !Pending, "Intact civil-war membership is preserved without duplicate restoration");

            oldCourt = Setup("bc_feud_test_holder"); Move(_player, _temporary); Factions.Clear();
            Move(_player, _home); Restore(); Restore();
            check(_manager.GetIdeologicalFaction(_player)?.Type == FactionType.Nobility && _registrations == 1,
                "A dissolved original court faction is recreated once with the saved ideology");

            Setup("bc_feud_test_holder", null); Move(_player, _temporary); Move(_player, _home); Restore();
            check(!Pending && _manager.GetIdeologicalFaction(_player) == null, "Previously unaffiliated player is never assigned an invented preference");
            Neutrality(); check(_penalties == 1, "Voluntary neutrality in an ordinary court retains its named -1 penalty");

            Setup("home_rebels_test"); Move(_player, _temporary); Call("ForgetPlayerCourtAffiliation");
            Factions[0].RemoveMember(_player); Move(_player, _home); Restore();
            check(_manager.GetIdeologicalFaction(_player) == null, "Deliberate court departure cancels postwar restoration");

            Setup("bc_feud_test_holder"); Move(_player, _temporary); Move(_player, _home);
            var chosen = new FactionObject("new choice", _home, _player, FactionType.Liberty); Factions.Add(chosen);
            Restore(); check(_manager.GetIdeologicalFaction(_player) == chosen && !Pending, "A newer player faction choice is never overridden");

            Setup("home_rebels_test"); Move(_player, _temporary); Factions[0].RemoveMember(_player);
            Move(_player, _home); Crowns[_home] = _player; Restore();
            check(!Pending && _manager.GetIdeologicalFaction(_player) == null, "Accession to the Crown cancels restoration to a vassal faction");
            Neutrality(); check(_penalties == 0, "Rulers remain exempt from neutrality penalties");

            foreach (bool exile in new[] { false, true })
            {
                Setup("bc_feud_test_holder"); Move(_player, _temporary);
                var foreign = Realm("foreign"); Crowns[foreign] = _ruler;
                Move(_player, exile ? null : foreign); Restore();
                Move(_player, _home); Restore();
                check(!Pending && _manager.GetIdeologicalFaction(_player) == null,
                    "Exile or unrelated foreign defection cannot leave a stale affiliation receipt: " + exile);
            }

            foreach (string id in new[] { "home_restored", "home_restored_2", "home_indep_peer" })
            {
                Setup("home_rebels_test"); Move(_player, _temporary);
                var successor = Realm(id); Crowns[successor] = _peer; Realms[_peer] = successor;
                Move(_player, successor); Restore();
                check(_manager.GetIdeologicalFaction(_player)?.ParentKingdom == successor
                    && _manager.GetIdeologicalFaction(_player)?.Type == FactionType.Nobility,
                    "A non-ruling player retains their ideology in a permanent civil-war successor: " + id);
            }

            Setup("bc_feud_test_holder"); Move(_player, _temporary); _player.StartMercenaryService(); Restore();
            Neutrality(); check(!Pending && _penalties == 0, "Mercenary service cancels restoration and incurs no neutrality penalty");
            Setup("bc_feud_test_holder"); Move(_peer, _temporary);
            check(!Pending, "NPC transfers do not create player affiliation receipts");

            oldCourt = Setup("home_rebels_test"); Realms[_player] = _temporary;
            var legacy = new Store { IsLoading = true }; _manager.SyncData(legacy); Restore();
            check(Pending, "Older civil-war save can recover an affiliation still present in its roster");
            Move(_player, _home); Restore(); check(_manager.GetIdeologicalFaction(_player) == oldCourt, "Recovered legacy affiliation remains intact on return");
            Setup("bc_feud_test_holder", null); Realms[_player] = _temporary; Restore();
            check(!Pending, "Older feud save with no remaining affiliation is not guessed");

            var forget = AccessTools.Method(ManagerType, "ForgetPlayerCourtAffiliation");
            check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(FactionsWindowVM), "ExecuteJoin"))
                .Any(i => i.Calls(forget)), "Explicit player court departure clears the saved preference through the UI");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(ManagerType, "Instance").Invoke(null, new object[] { oldInstance });
            Realms.Clear(); Crowns.Clear(); Leaders.Clear();
            _manager = null; _player = _ruler = _peer = null; _home = _temporary = null;
        }
    }
}
