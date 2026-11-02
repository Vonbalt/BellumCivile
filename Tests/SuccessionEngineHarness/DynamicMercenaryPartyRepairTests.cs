using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class DynamicMercenaryPartyRepairTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) { Values[key] = value; return true; }
            if (!Values.TryGetValue(key, out var stored)) return false;
            value = (T)stored; return true;
        }
    }
    private static Clan _clan, _other;
    private static Settlement _home;
    private static string _block;
    private static int _moves, _visuals, _searches, _scans, _clanReads, _logs;
    private static CampaignVec2 _destination, _locatedAt;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Player(ref Clan __result) { __result = _block == "player-clan" ? _clan : _other; return false; }
    private static bool MainParty(ref MobileParty __result) { __result = null; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { _clanReads++; __result = new MBReadOnlyList<Clan>(new[] { _clan }); return false; }
    private static bool AllSettlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(new[] { _home }); return false; }
    private static bool AllParties(ref MBReadOnlyList<MobileParty> __result) { _scans++; __result = new MBReadOnlyList<MobileParty>(); return false; }
    private static bool Current(ref Settlement __result) { __result = _block == "settlement" ? _home : null; return false; }
    private static bool Battle(ref MapEvent __result) { __result = _block == "battle" ? Blank<MapEvent>() : null; return false; }
    private static bool Side(ref MapEventSide __result) { __result = _block == "event-side" ? Blank<MapEventSide>() : null; return false; }
    private static bool Camp(ref BesiegerCamp __result) { __result = _block == "camp" ? Blank<BesiegerCamp>() : null; return false; }
    private static bool Siege(ref SiegeEvent __result) { __result = _block == "siege" ? Blank<SiegeEvent>() : null; return false; }
    private static bool Army(ref Army __result) { __result = _block == "army" ? Blank<Army>() : null; return false; }
    private static bool Attached(ref MobileParty __result) { __result = _block == "attached" ? Blank<MobileParty>() : null; return false; }
    private static bool Quest(ref bool __result) { __result = _block == "quest"; return false; }
    private static bool Sea(ref bool __result) { __result = _block == "sea"; return false; }
    private static bool Transition(ref bool __result) { __result = _block == "transition"; return false; }
    private static bool Disband(ref bool __result) { __result = _block == "disbanding"; return false; }
    private static bool Disabled(ref bool __result) { __result = _block == "ai-disabled"; return false; }
    private static bool Scripted(ref bool __result) { __result = _block == "scripted-ai"; return false; }
    private static bool UnderSiege(ref bool __result) { __result = _block == "town-siege"; return false; }
    private static bool Town(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Search(ref CampaignVec2 __result) { _searches++; __result = _destination; return false; }
    private static void Located(MobileParty __instance) { _moves++; _locatedAt = __instance.Position; }
    private static bool Visual() { _visuals++; return false; }
    private static bool Log() { _logs++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.dynamic_mercenary_repair");
        var previous = Campaign.Current;
        var scene = new PartyNavigationTestScene(); var campaign = scene.CreateCampaign();
        var service = typeof(FactionObject).Assembly.GetType("BellumCivile.DynamicMercenaryBandService", true);
        var behaviorType = typeof(DynamicMercenaryBandBehavior);
        var locatorType = AccessTools.Property(typeof(Campaign), "MobilePartyLocator").PropertyType;
        void Set(object instance, string name, object value) => AccessTools.PropertySetter(instance.GetType(), name).Invoke(instance, new[] { value });
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(DynamicMercenaryPartyRepairTests), name));
        void Getter(Type type, string property, string name) => Patch(AccessTools.PropertyGetter(type, property), name);
        _clan = Blank<Clan>(); _clan.StringId = "bc_dynamic_merc_repair_company";
        _other = Blank<Clan>(); _other.StringId = "other_house";
        var hero = Blank<Hero>(); hero.StringId = "captain";
        AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, _clan);
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
        Set(_clan, "IsMinorFaction", true);
        _home = Blank<Settlement>(); _home.StringId = "repair_home";
        Set(_home, "GatePosition", new CampaignVec2(new Vec2(100, 100), true));
        var party = Blank<MobileParty>(); party.IsActive = true;
        var component = Blank<LordPartyComponent>(); Set(component, "Owner", hero);
        AccessTools.Field(typeof(LordPartyComponent), "_leader").SetValue(component, hero);
        AccessTools.PropertySetter(typeof(PartyComponent), "MobileParty").Invoke(component, new object[] { party });
        AccessTools.Field(typeof(MobileParty), "_partyComponent").SetValue(party, component);
        AccessTools.Field(typeof(MobileParty), "_actualClan").SetValue(party, _clan);
        Set(party, "IsLordParty", true);
        var ai = Blank<MobilePartyAi>(); AccessTools.Field(typeof(MobilePartyAi), "_mobileParty").SetValue(ai, party); Set(party, "Ai", ai);
        var partyBase = Blank<PartyBase>(); Set(partyBase, "MobileParty", party); Set(party, "Party", partyBase);
        var troop = Blank<CharacterObject>(); troop.StringId = "repair_soldier";
        var members = TroopRoster.CreateDummyTroopRoster(); members.AddToCounts(troop, 10);
        var prisoners = TroopRoster.CreateDummyTroopRoster(); prisoners.AddToCounts(troop, 2);
        var items = new ItemRoster();
        Set(partyBase, "MemberRoster", members); Set(partyBase, "PrisonRoster", prisoners); Set(partyBase, "ItemRoster", items);
        AccessTools.Field(typeof(Hero), "_partyBelongedTo").SetValue(hero, party);
        AccessTools.Field(typeof(Clan), "_warPartyComponentsCache").SetValue(_clan, new MBList<WarPartyComponent> { component });
        var behavior = new DynamicMercenaryBandBehavior();
        AccessTools.Field(behaviorType, "_bands").SetValue(behavior, new List<DynamicMercenaryBandRecord> {
            new DynamicMercenaryBandRecord(_clan.StringId, hero.StringId, _other.StringId, "culture", _home.StringId, default) });
        string PartyId(string receipt = "30_party_1") => _clan.StringId + "_" + hero.StringId + "_" + receipt;
        bool HasId() => (bool)AccessTools.Method(service, "HasCreatedPartyId").Invoke(null, new object[] { party, _clan });
        bool Repair() => (bool)AccessTools.Method(behaviorType, "TryRepairPartyPosition").Invoke(null, new object[] { party, _clan, _home });
        void Queue(DynamicMercenaryBandBehavior value) => AccessTools.Method(behaviorType, "QueuePartyPositionRepair").Invoke(value, null);
        void Tick(DynamicMercenaryBandBehavior value) => AccessTools.Method(behaviorType, "OnTick").Invoke(value, new object[] { 0f });
        void Reset()
        {
            _block = null; _moves = _visuals = _searches = _logs = 0;
            scene.InvalidPositions.Clear(); scene.DisconnectedPositions.Clear(); scene.PathsAvailable = true; scene.ThrowOnPath = false; scene.PathDetour = 0;
            var old = new CampaignVec2(new Vec2(101, 100), true);
            AccessTools.Field(typeof(MobileParty), "_position").SetValue(party, old);
            AccessTools.Field(typeof(MobileParty), "_locatorNodeIndex").SetValue(party, -1);
            AccessTools.Field(typeof(Campaign), "_mobilePartyLocator").SetValue(campaign,
                Activator.CreateInstance(locatorType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { 5f, 32, 32 }, null));
            scene.InvalidPositions.Add(old.ToVec2()); _destination = new CampaignVec2(new Vec2(108, 100), true);
            party.StringId = PartyId(); ai.RethinkAtNextHourlyTick = false;
        }
        try
        {
            Getter(typeof(Clan), "PlayerClan", nameof(Player)); Getter(typeof(MobileParty), "MainParty", nameof(MainParty));
            Getter(typeof(Clan), "All", nameof(AllClans)); Getter(typeof(Settlement), "All", nameof(AllSettlements));
            Getter(typeof(MobileParty), "All", nameof(AllParties)); Getter(typeof(Settlement), "IsTown", nameof(Town));
            Getter(typeof(Settlement), "IsUnderSiege", nameof(UnderSiege)); Getter(typeof(MobileParty), "CurrentSettlement", nameof(Current));
            Getter(typeof(MobileParty), "MapEvent", nameof(Battle)); Getter(typeof(MobileParty), "MapEventSide", nameof(Side));
            Getter(typeof(MobileParty), "BesiegerCamp", nameof(Camp)); Getter(typeof(MobileParty), "SiegeEvent", nameof(Siege));
            Getter(typeof(MobileParty), "Army", nameof(Army)); Getter(typeof(MobileParty), "AttachedTo", nameof(Attached));
            Getter(typeof(MobileParty), "IsCurrentlyUsedByAQuest", nameof(Quest)); Getter(typeof(MobileParty), "IsCurrentlyAtSea", nameof(Sea));
            Getter(typeof(MobileParty), "IsTransitionInProgress", nameof(Transition)); Getter(typeof(MobileParty), "IsDisbanding", nameof(Disband));
            Getter(typeof(MobilePartyAi), "IsDisabled", nameof(Disabled)); Getter(typeof(MobilePartyAi), "DoNotMakeNewDecisions", nameof(Scripted));
            Getter(typeof(FactionObject).Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicMercenaryBands", nameof(No));
            Patch(AccessTools.Method(typeof(NavigationHelper), "FindReachablePointAroundPosition", new[] { typeof(CampaignVec2),
                typeof(MobileParty.NavigationType), typeof(float), typeof(float), typeof(bool) }), nameof(Search));
            h.Patch(AccessTools.PropertySetter(typeof(MobileParty), "Position"), postfix:
                new HarmonyMethod(typeof(DynamicMercenaryPartyRepairTests), nameof(Located)));
            Patch(AccessTools.Method(typeof(PartyBase), "SetVisualAsDirty"), nameof(Visual));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            Reset();
            check(HasId(), "Bellum company, owner and creation-day receipt identify the original party");
            foreach (string receipt in new[] { "0_party_1", "30_party_9473" })
            { party.StringId = PartyId(receipt); check(HasId(), "Native unique party index remains recognizable: " + receipt); }
            foreach (string receipt in new[] { "day_party_1", "30_party_0", "30_party_1junk", "30_party_1_2", "30_party_", "30_party_99999999999" })
            { party.StringId = PartyId(receipt); check(!HasId(), "Uncertain party IDs are excluded: " + receipt); }
            party.StringId = hero.StringId + "_party_1";
            check(!HasId() && !Repair(), "Native respawns within a Bellum company are not mistaken for Bellum-created parties");
            Reset(); party.SetMoveGoToPoint(new CampaignVec2(new Vec2(180, 100), true), MobileParty.NavigationType.Default);
            check(Repair() && party.Position == _destination && _moves == 1 && _locatedAt == _destination,
                "Confirmed invalid legacy spawn moves through the actual native position setter and locator update");
            var locator = AccessTools.Field(typeof(Campaign), "_mobilePartyLocator").GetValue(campaign);
            int nodeIndex = (int)AccessTools.Field(typeof(MobileParty), "_locatorNodeIndex").GetValue(party);
            check(nodeIndex >= 0 && ReferenceEquals(((Array)AccessTools.Field(locatorType, "_nodes").GetValue(locator)).GetValue(nodeIndex), party),
                "The native map locator contains the relocated party in its new grid node");
            check(party.DefaultBehavior == AiBehavior.Hold && party.TargetPosition == _destination && ai.RethinkAtNextHourlyTick,
                "Native movement reset clears the stale destination and requests an AI rethink");
            check(ReferenceEquals(party.MemberRoster, members) && members.TotalManCount == 10
                && ReferenceEquals(party.PrisonRoster, prisoners) && prisoners.TotalManCount == 2 && ReferenceEquals(party.ItemRoster, items),
                "Relocation retains the same party, troops, prisoners and inventory");
            check(_visuals > 0 && _logs == 1 && party.Owner == hero && party.LeaderHero == hero && party.ActualClan == _clan,
                "Relocation refreshes visuals and logs the repair without changing leadership or allegiance");
            Reset(); scene.InvalidPositions.Clear(); scene.DisconnectedPositions.Add(party.Position.ToVec2());
            check(Repair(), "A valid but disconnected legacy spawn can also be recovered");
            foreach (string block in new[] { "settlement", "battle", "event-side", "camp", "siege", "army", "attached", "quest",
                "sea", "transition", "disbanding", "ai-disabled", "scripted-ai", "town-siege", "player-clan" })
            {
                Reset(); _block = block;
                check(!Repair() && _moves == 0 && _searches == 0, "Protected or controlled party is untouched: " + block);
            }
            Reset(); party.IsActive = false; check(!Repair(), "Inactive parties are never moved"); party.IsActive = true;
            Reset(); Set(partyBase, "MemberRoster", TroopRoster.CreateDummyTroopRoster());
            check(!Repair(), "Empty parties are left to native cleanup instead of being revived"); Set(partyBase, "MemberRoster", members);
            Reset(); AccessTools.Field(typeof(LordPartyComponent), "_leader").SetValue(component, null);
            check(!Repair(), "Leaderless parties are not moved"); AccessTools.Field(typeof(LordPartyComponent), "_leader").SetValue(component, hero);
            Reset(); scene.InvalidPositions.Clear(); scene.PathDetour = 500;
            check(!Repair() && _searches == 0 && _moves == 0, "A long valid route near town is not treated as a stuck spawn");
            Reset(); var far = new CampaignVec2(new Vec2(104, 100), true); AccessTools.Field(typeof(MobileParty), "_position").SetValue(party, far);
            scene.InvalidPositions.Add(far.ToVec2()); check(!Repair(), "Broken positions outside the old three-unit spawn area remain outside this repair");
            Reset(); _destination = _home.GatePosition;
            check(!Repair() && _moves == 0, "No verified replacement means no relocation");

            Reset(); _scans = _clanReads = 0;
            var launch = PatchProcessor.GetOriginalInstructions(AccessTools.Method(behaviorType, "OnSessionLaunched")).ToList();
            check(launch.FindIndex(i => i.operand is MethodInfo m && m.Name == "QueuePartyPositionRepair")
                > launch.FindIndex(i => i.operand is MethodInfo m && m.Name == "ReconcileBands"),
                "Load queues the repair after company reconciliation, without relocating during session launch");
            Queue(behavior); check(_moves == 0 && _clanReads == 0, "Queuing an old save does not inspect or mutate parties early");
            Tick(behavior);
            check(_moves == 1 && _scans == 0, "First normal tick repairs only recorded company war parties, even with new company creation disabled");
            int reads = _clanReads; Tick(behavior);
            check(_clanReads == reads && _moves == 1, "Subsequent ticks neither rescan nor repeat the repair");
            var store = new Store(); behavior.SyncData(store);
            check((int)store.Values["BellumCivile_DynamicMercenaryPartyPositionRepair"] == 1, "Repair version is persisted with the campaign behavior");
            store.IsLoading = true; var loaded = new DynamicMercenaryBandBehavior(); loaded.SyncData(store); Queue(loaded); Tick(loaded);
            check(_clanReads == reads && _moves == 1, "Saving and loading a repaired campaign does not repeat its scan");
            var fresh = new DynamicMercenaryBandBehavior();
            AccessTools.Method(behaviorType, "InitializePartyPositionRepair").Invoke(fresh, new object[] { null }); Queue(fresh); Tick(fresh);
            check(_clanReads == reads, "New campaigns start migrated and do not run the legacy repair");
            Reset(); scene.ThrowOnPath = true; var legacy = new DynamicMercenaryBandBehavior();
            var legacyStore = new Store { IsLoading = true }; legacy.SyncData(legacyStore);
            AccessTools.Field(behaviorType, "_bands").SetValue(legacy, AccessTools.Field(behaviorType, "_bands").GetValue(behavior));
            Queue(legacy); Tick(legacy);
            check(_moves == 0 && _logs >= 2, "A navigation exception is diagnosed without moving the party or interrupting the campaign");
            legacyStore.IsLoading = false; legacy.SyncData(legacyStore);
            check((int)legacyStore.Values["BellumCivile_DynamicMercenaryPartyPositionRepair"] == 1,
                "Skipped uncertain cases still complete the one-off migration rather than retrying every load");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
