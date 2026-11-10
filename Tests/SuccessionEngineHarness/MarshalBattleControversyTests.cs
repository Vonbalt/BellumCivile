using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

internal static class MarshalBattleControversyTests
{
    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Store : IDataStore
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        public bool IsSaving { get; set; } = true;
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving)
            {
                object copy = value;
                if (copy is Dictionary<string, float> floats) copy = new Dictionary<string, float>(floats);
                if (copy is Dictionary<string, int> integers) copy = new Dictionary<string, int>(integers);
                _values[key] = copy;
            }
            else if (_values.TryGetValue(key, out var copy)) value = (T)copy;
            return true;
        }
    }

    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Hero, MobileParty> HeroParties = new Dictionary<Hero, MobileParty>();
    private static readonly Dictionary<MobileParty, MapEvent> HeroBattles = new Dictionary<MobileParty, MapEvent>();
    private static readonly Dictionary<PartyBase, MapEvent> PartyBattles = new Dictionary<PartyBase, MapEvent>();
    private static readonly Dictionary<PartyBase, Kingdom> PartyRealms = new Dictionary<PartyBase, Kingdom>();
    private static readonly Dictionary<MBObjectBase, MBGUID> Ids = new Dictionary<MBObjectBase, MBGUID>(new ReferenceComparer<MBObjectBase>());
    private static readonly Dictionary<Kingdom, PrivyCouncilOfficeRecord> Seats = new Dictionary<Kingdom, PrivyCouncilOfficeRecord>();
    private static readonly List<string> Logs = new List<string>();
    private static ControversyBehavior _behavior;
    private static MapEventManager _mapManager;
    private static MapEvent _playerBattle;
    private static float _competence;
    private static PartyBase _deferredParty;
    private static Hero _deferredVictim;
    private static bool _throwCapture;
    private static uint _nextId;
    private static readonly Type ContextType = typeof(ControversyBehavior).Assembly.GetType("BellumCivile.Patches.MarshalBattleCasualtyContext");
    private static MapEvent Current => (MapEvent)AccessTools.Property(ContextType, "Current").GetValue(null);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool House(Hero __instance, ref Clan __result) { Houses.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Party(Hero __instance, ref MobileParty __result) { HeroParties.TryGetValue(__instance, out __result); return false; }
    private static bool HeroBattle(MobileParty __instance, ref MapEvent __result) { HeroBattles.TryGetValue(__instance, out __result); return false; }
    private static bool PartyBattle(PartyBase __instance, ref MapEvent __result) { PartyBattles.TryGetValue(__instance, out __result); return false; }
    private static bool Faction(PartyBase __instance, ref IFaction __result)
    { __result = PartyRealms.TryGetValue(__instance, out var realm) ? realm : null; return false; }
    private static bool ObjectId(MBObjectBase __instance, ref MBGUID __result)
    { if (!Ids.TryGetValue(__instance, out __result)) return true; return false; }
    private static bool Seat(Kingdom kingdom, PrivyCouncilOffice office, ref PrivyCouncilOfficeRecord __result)
    { __result = office == PrivyCouncilOffice.Marshal && Seats.TryGetValue(kingdom, out var seat) ? seat : null; return false; }
    private static bool Competence(ref float __result) { __result = _competence; return false; }
    private static bool MapManager(ref MapEventManager __result) { __result = _mapManager; return false; }
    private static bool PlayerBattle(ref MapEvent __result) { __result = _playerBattle; return false; }
    private static bool Log(string message) { Logs.Add(message); return false; }

    // Substitute engine-dependent action bodies, retaining the real context prefixes/finalizers and Bellum callbacks.
    private static bool CaptureBody(PartyBase capturerParty, Hero prisonerCharacter)
    {
        HeroParties[prisonerCharacter] = null;
        if (_throwCapture) throw new InvalidOperationException("capture probe");
        Call("OnHeroPrisonerTaken", capturerParty, prisonerCharacter);
        return false;
    }
    private static bool DeathBody(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail actionDetail)
    {
        HeroParties[victim] = null;
        Call("OnHeroKilled", victim, killer, actionDetail, true);
        return false;
    }
    private static bool DeferredBody()
    {
        PartyBattles[_deferredParty] = null;
        HeroBattles[HeroParties[_deferredVictim]] = null;
        KillCharacterAction.ApplyByBattle(_deferredVictim, null);
        return false;
    }
    private static void Call(string method, params object[] args) =>
        AccessTools.Method(typeof(ControversyBehavior), method).Invoke(_behavior, args);
    private static Dictionary<string, float> Budget =>
        (Dictionary<string, float>)AccessTools.Field(typeof(ControversyBehavior), "_battleCasualtyIncreases").GetValue(_behavior);
    private static MBList<MapEvent> LiveBattles =>
        (MBList<MapEvent>)AccessTools.Field(typeof(MapEventManager), "_mapEvents").GetValue(_mapManager);
    private static MapEvent Battle()
    {
        var battle = Blank<MapEvent>();
        Ids[battle] = new MBGUID(++_nextId);
        LiveBattles.Add(battle);
        return battle;
    }
    private static Hero Noble(Kingdom realm, MapEvent battle)
    {
        var hero = Blank<Hero>();
        var clan = Blank<Clan>(); clan.StringId = "house_" + ++_nextId;
        Houses[hero] = clan; Realms[clan] = realm;
        var party = Blank<MobileParty>(); HeroParties[hero] = party; HeroBattles[party] = battle;
        return hero;
    }
    private static void Capture(Kingdom realm, MapEvent battle, PartyBase captor = null) =>
        TakePrisonerAction.Apply(captor, Noble(realm, battle));
    private static void Death(Kingdom realm, MapEvent battle) =>
        KillCharacterAction.ApplyByBattle(Noble(realm, battle), null);

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var harmony = new Harmony("bellum.test.marshal_battle_cap");
        void Patch(MethodBase method, string name, int priority = Priority.Normal) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(MarshalBattleControversyTests), name) { priority = priority });
        var council = new PrivyCouncilBehavior();
        var manager = new Manager(); manager.AddBehavior(council);
        var campaign = Blank<Campaign>();
        var realm = Blank<Kingdom>(); realm.StringId = "marshal_cap";
        var ally = Blank<Kingdom>(); ally.StringId = "marshal_ally";
        var type = typeof(PrivyCouncilBehavior);
        void Reset(float competence = 50)
        {
            _competence = competence; _throwCapture = false; _playerBattle = null;
            _behavior = new ControversyBehavior(); Logs.Clear(); LiveBattles.Clear();
            foreach (var kingdom in new[] { realm, ally })
            {
                var seat = new PrivyCouncilOfficeRecord(kingdom.StringId, PrivyCouncilOffice.Marshal, 0);
                seat.Appoint("marshal_holder", 0); Seats[kingdom] = seat;
            }
        }
        bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
        try
        {
            Houses.Clear(); Realms.Clear(); HeroParties.Clear(); HeroBattles.Clear(); PartyBattles.Clear(); PartyRealms.Clear(); Ids.Clear(); Seats.Clear();
            _nextId = 0; ticks.SetValue(null, 1000L);
            _mapManager = Blank<MapEventManager>();
            AccessTools.Field(typeof(MapEventManager), "_mapEvents").SetValue(_mapManager, new MBList<MapEvent>());
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "MapEventManager"), nameof(MapManager));
            Patch(AccessTools.PropertyGetter(typeof(PlayerEncounter), "Battle"), nameof(PlayerBattle));
            Patch(AccessTools.PropertyGetter(typeof(MBObjectBase), "Id"), nameof(ObjectId));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "PartyBelongedTo"), nameof(Party));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(MobileParty), "MapEvent"), nameof(HeroBattle));
            Patch(AccessTools.PropertyGetter(typeof(PartyBase), "MapEvent"), nameof(PartyBattle));
            Patch(AccessTools.PropertyGetter(typeof(PartyBase), "MapFaction"), nameof(Faction));
            Patch(AccessTools.Method(type, "EnsureCouncilForKingdom"), nameof(Skip));
            Patch(AccessTools.Method(type, "GetOfficeRecord"), nameof(Seat));
            Patch(AccessTools.Method(type, "IsOfficeUnlocked"), nameof(Yes));
            Patch(AccessTools.Method(type, "IsAssignmentActive"), nameof(No));
            Patch(AccessTools.Method(type, "GetEffectiveCoreOfficeCompetence"), nameof(Competence));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            foreach (string name in new[] { "MarshalPrisonerBattleContextPatch", "MarshalDeathBattleContextPatch", "MarshalDeferredDeathBattleContextPatch" })
            {
                var patch = type.Assembly.GetType("BellumCivile.Patches." + name);
                var methods = harmony.CreateClassProcessor(patch).Patch();
                check(methods.Count == 1, "Native battle context target is supported: " + name);
            }
            Patch(AccessTools.Method(typeof(TakePrisonerAction), "ApplyInternal"), nameof(CaptureBody), Priority.Last);
            Patch(AccessTools.Method(typeof(KillCharacterAction), "ApplyInternal"), nameof(DeathBody), Priority.Last);
            Patch(AccessTools.Method(typeof(MapEventSide), "HandleMapEventEndForPartyInternal"), nameof(DeferredBody), Priority.Last);

            Reset(); var battle = Battle();
            for (int i = 0; i < 3; i++) Capture(realm, battle);
            Death(realm, battle);
            check(Near(Seats[realm].Controversy, 14), "Three captures and one battle death retain their ordinary fourteen-point cost");
            Death(realm, battle);
            check(Near(Seats[realm].Controversy, 15), "Captures and deaths share one fifteen-point allowance after native party links are cleared");
            for (int i = 0; i < 10; i++) { Capture(realm, battle); Death(realm, battle); }
            check(Near(Seats[realm].Controversy, 15) && Logs.Count == 1,
                "Further losses cannot exceed the cap and produce only one cap diagnostic");
            check(Current == null, "Native action contexts are restored after successful callbacks");

            Reset(); battle = Battle();
            var captorA = Blank<PartyBase>(); var captorB = Blank<PartyBase>();
            PartyBattles[captorA] = battle; PartyBattles[captorB] = battle;
            for (int i = 0; i < 10; i++) Capture(realm, null, i % 2 == 0 ? captorA : captorB);
            check(Near(Seats[realm].Controversy, 15),
                "Captures with no remaining victim party share one allowance across the army's captor parties");

            Reset(); battle = Battle();
            for (int i = 0; i < 3; i++) Capture(realm, battle);
            Death(realm, battle);
            Seats[realm].Appoint("replacement_marshal", 100);
            Death(realm, battle);
            check(Near(Seats[realm].Controversy, 1),
                "A replacement marshal starts clean without resetting the realm's existing battle allowance");

            Reset(); battle = Battle();
            for (int i = 0; i < 5; i++) Capture(realm, battle);

            var second = Battle();
            Capture(realm, second); Capture(realm, battle); Death(realm, second);
            check(Near(Seats[realm].Controversy, 23), "Interleaved battles retain separate allowances on the same campaign day");
            for (int i = 0; i < 8; i++) Capture(ally, battle);
            check(Near(Seats[ally].Controversy, 15) && Near(Seats[realm].Controversy, 23),
                "Each realm in the same battle has its own cap");

            foreach (float competence in new[] { 0f, 30f, 50f, 70f, 90f })
            {
                Reset(competence); battle = Battle();
                for (int i = 0; i < 8; i++) { Capture(realm, battle); Death(realm, battle); }
                check(Near(Seats[realm].Controversy, 15), "Final increases stay capped after competence scaling: " + competence);
            }
            Reset(90); battle = Battle(); Capture(realm, battle); Death(realm, battle);
            check(Near(Seats[realm].Controversy, 6), "High competence still reduces ordinary losses below the ceiling");

            Reset(); battle = Battle();
            for (int i = 0; i < 5; i++) Capture(realm, battle);
            var captor = Blank<PartyBase>(); PartyRealms[captor] = realm; PartyBattles[captor] = battle;
            Capture(ally, battle, captor);
            check(Near(Seats[realm].Controversy, 12), "Enemy captures retain their existing controversy reduction");
            Death(realm, battle);
            check(Near(Seats[realm].Controversy, 12), "A reduction does not refill the battle's spent loss allowance");
            for (int i = 0; i < 7; i++) KillCharacterAction.ApplyByExecution(Noble(realm, battle), null);
            check(Near(Seats[realm].Controversy, 47), "Later deliberate executions remain outside the battle casualty cap");
            Capture(realm, null); Death(realm, null);
            check(Near(Seats[realm].Controversy, 55), "Events without a verified battle context preserve existing handling");
            council.AddControversy(realm, PrivyCouncilOffice.Marshal, 2, "own village raided");
            council.AddControversy(realm, PrivyCouncilOffice.Marshal, 1, "own caravan destroyed");
            council.AddControversy(realm, PrivyCouncilOffice.Marshal, 10, "military setback");
            council.AddControversy(realm, PrivyCouncilOffice.Marshal, 4, "war on multiple fronts");
            check(Near(Seats[realm].Controversy, 72), "Raids, caravans, siege losses and ongoing pressures do not consume the allowance");
            Seats[realm].SetControversy(90, "test", 100);
            for (int i = 0; i < 8; i++) Capture(ally, battle, captor);
            check(Near(Seats[realm].Controversy, 66), "Successful-action reductions remain uncapped");

            Reset(); battle = Battle();
            Capture(realm, battle); Capture(realm, battle);
            var store = new Store(); _behavior.SyncData(store);
            _behavior = new ControversyBehavior(); store.IsSaving = false; _behavior.SyncData(store);
            var loadedBattle = Blank<MapEvent>(); Ids[loadedBattle] = Ids[battle];
            LiveBattles.Clear(); LiveBattles.Add(loadedBattle); battle = loadedBattle;
            for (int i = 0; i < 10; i++) Death(realm, battle);
            check(Near(Seats[realm].Controversy, 15), "Save/load retains the allowance across a new native battle instance with its saved ID");
            Call("OnDailyTick");
            check(Budget.Count == 1, "Daily cleanup retains active battle budgets across day boundaries");
            LiveBattles.Clear(); _playerBattle = battle; Call("OnDailyTick");
            check(Budget.Count == 1, "A pending player encounter preserves its budget after manager removal");
            _playerBattle = null; Call("OnDailyTick");
            check(Budget.Count == 0, "Finished battle budgets are pruned without retaining native battle objects");
            var oldSave = new Store { IsSaving = false }; _behavior = new ControversyBehavior(); _behavior.SyncData(oldSave);
            check(Budget.Count == 0, "Older saves without battle budgets initialize safely");

            Reset(); battle = Battle(); Capture(realm, battle); Capture(realm, battle);
            _deferredParty = Blank<PartyBase>(); PartyBattles[_deferredParty] = battle;
            var side = Blank<MapEventSide>(); AccessTools.Field(typeof(MapEventSide), "_mapEvent").SetValue(side, battle);
            for (int i = 0; i < 5; i++)
            {
                _deferredVictim = Noble(realm, battle);
                AccessTools.Method(typeof(MapEventSide), "HandleMapEventEndForPartyInternal").Invoke(side, new object[] { _deferredParty });
            }
            check(Near(Seats[realm].Controversy, 15) && Current == null,
                "Deferred deaths after map-event detachment share the earlier capture allowance");
            _throwCapture = true;
            try { Capture(realm, Battle()); } catch (InvalidOperationException) { }
            check(Current == null, "Exceptions propagate while restoring the native action context");
            _throwCapture = false;

            Reset(); battle = Battle(); manager.Items.Clear();
            for (int i = 0; i < 10; i++) { Capture(realm, battle); Death(realm, battle); }
            var legacy = (Dictionary<string, int>)AccessTools.Field(typeof(ControversyBehavior), "_trackedControversyById").GetValue(_behavior);
            check(legacy[realm.StringId] == 15, "Legacy controversy routing also respects the shared battle cap");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.Method(ContextType, "Restore").Invoke(null, new object[] { null });
            ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
