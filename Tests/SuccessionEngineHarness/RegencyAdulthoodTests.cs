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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class RegencyAdulthoodTests
{
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
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsLoading) { if (_data.TryGetValue(key, out var data)) value = (T)data; }
            else _data[key] = value;
            return true;
        }
    }
    private sealed class Creation : DefaultHeroCreationModel
    {
        public override List<(SkillObject, int)> GetInheritedSkillsForHero(Hero hero)
        { _skills++; return new List<(SkillObject, int)> { (Blank<SkillObject>(), 112) }; }
    }
    private sealed class Selection : DefaultEquipmentSelectionModel
    {
        public override Equipment GetEquipmentForHeroComeOfAge(Hero hero, Equipment.EquipmentType type)
        {
            _selections++;
            if (_throwSelection) throw new InvalidOperationException("Injected equipment selection failure");
            if (_missingSelection) return null;
            return type == Equipment.EquipmentType.Battle ? _adultBattle : _adultCivilian;
        }
    }
    private static readonly Dictionary<Hero, Equipment> Battle = new Dictionary<Hero, Equipment>(), Civilian = new Dictionary<Hero, Equipment>();
    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static readonly List<Hero> Heroes = new List<Hero>();
    private static readonly List<MBEquipmentRoster> Rosters = new List<MBEquipmentRoster>();
    private static List<Hero> _continuity;
    private static Equipment _child, _adultBattle, _adultCivilian;
    private static CultureObject _culture;
    private static Clan _player;
    private static int _majority, _skills, _developers, _levels, _selections, _activations, _teleports, _replacementNotices;
    private static bool _throwSelection, _missingSelection;
    private static readonly List<string> Logs = new List<string>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object o, string f, object value) => AccessTools.Field(o.GetType(), f).SetValue(o, value);
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Majority(ref int __result) { __result = _majority; return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool Character(ref CharacterObject __result) { __result = Blank<CharacterObject>(); return false; }
    private static bool BattleGear(Hero __instance, ref Equipment __result) { __result = Battle[__instance]; return false; }
    private static bool CivilianGear(Hero __instance, ref Equipment __result) { __result = Civilian[__instance]; return false; }
    private static bool AllRosters(ref MBReadOnlyList<MBEquipmentRoster> __result) { __result = new MBReadOnlyList<MBEquipmentRoster>(Rosters); return false; }
    private static bool AllHeroes(ref MBReadOnlyList<Hero> __result) { __result = new MBReadOnlyList<Hero>(Heroes); return false; }
    private static bool Develop() { _developers++; return false; }
    private static bool Level() { _levels++; return false; }
    private static bool Activate(Hero __instance, Hero.CharacterStates newState)
    { _activations++; Set(__instance, "_heroState", newState); return false; }
    private static bool Teleport() { _teleports++; return false; }
    private static bool Log(string message) { Logs.Add(message); return false; }
    private static bool Continuity(ref List<Hero> __result) { __result = _continuity; return false; }
    private static bool ReplacementNotice() { _replacementNotices++; return false; }
    private static bool ResolveHero(string __0, ref Hero __result)
    { __result = Heroes.FirstOrDefault(h => h.StringId == __0); return false; }
    private static bool Install(Clan clan, Hero newLeader, ref bool __result)
    { Set(clan, "_leader", newLeader); __result = true; return false; }
    private static Equipment Gear(Equipment.EquipmentType type, int slot)
    {
        var gear = new Equipment(type);
        ((EquipmentElement[])AccessTools.Field(typeof(Equipment), "_itemSlots").GetValue(gear))[slot] = new EquipmentElement(Blank<ItemObject>());
        return gear;
    }
    private static Equipment Copy(Equipment source, Equipment.EquipmentType type)
    { var e = new Equipment(type); e.FillFrom(source, false); return e; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.regency_adulthood");
        var previous = Campaign.Current;
        var oldRegency = RegencyBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var mod = typeof(RegencyBehavior).Assembly;
        var recordField = AccessTools.Field(typeof(RegencyBehavior), "_regencies");
        var nativeYoung = AccessTools.Field(typeof(AgingCampaignBehavior), "_heroesYoungerThanHeroComesOfAge");
        var equip = mod.GetType("BellumCivile.RegencyAdulthoodEquipment", true);
        var manager = new Manager();
        CampaignEventDispatcher dispatcher = null;
        RegencyBehavior regencies = null;
        AgingCampaignBehavior aging = null;
        Clan house = null;
        Hero ward = null, regent = null;
        RegencyRecord record = null;
        void Patch(MethodBase target, string name) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(RegencyAdulthoodTests), name));
        object Call(object target, string method, params object[] args) => AccessTools.Method(target.GetType(), method).Invoke(target, args);
        bool Ready() => (bool)Call(regencies, "EnsureWardAdulthood", house, record, ward);
        bool Prepared() => (bool)Call(record, "IsAdulthoodPrepared", ward);
        void Tick() => Call(regencies, "ProcessAdultWardHandovers");
        void Repair(Hero hero) => AccessTools.Method(equip, "TryRecoverLegacy").Invoke(null, new object[] { hero });
        Dictionary<Hero, int> Young() => (Dictionary<Hero, int>)nativeYoung.GetValue(aging);
        Hero Person(string id, Clan clan, float age, Hero.CharacterStates state)
        {
            var h = Blank<Hero>(); h.StringId = id; Set(h, "_clan", clan); Set(h, "_heroState", state);
            h.Culture = _culture;
            Set(h, "_heroDeveloper", Blank<HeroDeveloper>()); Set(h, "_children", new MBList<Hero>());
            Battle[h] = Copy(_child, Equipment.EquipmentType.Battle); Civilian[h] = Copy(_child, Equipment.EquipmentType.Civilian);
            Ages[h] = age; Heroes.Add(h); return h;
        }
        void Reset(int adulthood = 16)
        {
            Battle.Clear(); Civilian.Clear(); Ages.Clear(); Heroes.Clear(); Logs.Clear(); Rosters.Clear();
            _majority = adulthood; _skills = _developers = _levels = _selections = _activations = _teleports = _replacementNotices = 0;
            _throwSelection = _missingSelection = false; _player = Blank<Clan>();
            _child = Gear(Equipment.EquipmentType.Civilian, (int)EquipmentIndex.Body);
            _adultBattle = Gear(Equipment.EquipmentType.Battle, (int)EquipmentIndex.Weapon0);
            _adultBattle[(int)EquipmentIndex.Horse] = new EquipmentElement(Blank<ItemObject>());
            _adultCivilian = Gear(Equipment.EquipmentType.Civilian, (int)EquipmentIndex.Body);
            _culture = Blank<CultureObject>();
            var roster = Blank<MBEquipmentRoster>();
            AccessTools.PropertySetter(typeof(MBEquipmentRoster), "EquipmentCulture").Invoke(roster, new object[] { _culture });
            AccessTools.PropertySetter(typeof(MBEquipmentRoster), "EquipmentCategories").Invoke(roster,
                new object[] { EquipmentCategories.IsLordTemplate | EquipmentCategories.IsChildEquipmentTemplate });
            Set(roster, "_equipments", new MBList<Equipment> { _child }); Rosters.Add(roster);
            var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
            AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
            AccessTools.PropertySetter(typeof(GameModels), "HeroCreationModel").Invoke(models, new object[] { new Creation() });
            AccessTools.PropertySetter(typeof(GameModels), "EquipmentSelectionModel").Invoke(models, new object[] { new Selection() });
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            var events = new CampaignEvents();
            AccessTools.PropertySetter(typeof(Campaign), "CampaignEvents").Invoke(campaign, new object[] { events });
            dispatcher = (CampaignEventDispatcher)Activator.CreateInstance(typeof(CampaignEventDispatcher),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new CampaignEventReceiver[] { events } }, null);
            AccessTools.PropertySetter(typeof(Campaign), "CampaignEventDispatcher").Invoke(campaign, new object[] { dispatcher });
            manager.Items.Clear(); regencies = new RegencyBehavior(); aging = new AgingCampaignBehavior();
            manager.Items.Add(regencies); manager.Items.Add(aging); campaign.AddCampaignBehaviorManager(manager);
            // Match native registration: spawn before aging, Bellum after native.
            new HeroSpawnCampaignBehavior().RegisterEvents(); aging.RegisterEvents(); regencies.RegisterEvents();
            house = Blank<Clan>(); house.StringId = "ward_house";
            ward = Person("ward", house, adulthood, Hero.CharacterStates.NotSpawned);
            regent = Person("regent", house, 40, Hero.CharacterStates.Active); Set(house, "_leader", regent);
            record = new RegencyRecord(house.StringId, ward.StringId, regent.StringId, "predecessor", default(CampaignTime), false);
            ((IList)recordField.GetValue(regencies)).Add(record); Young()[ward] = adulthood - 1;
        }
        try
        {
            ticks.SetValue(null, 24000L);
            Patch(AccessTools.Method(mod.GetType("BellumCivile.SuccessionLawHelper"), "GetAgeOfMajority"), nameof(Majority));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "CharacterObject"), nameof(Character));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsLord"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "BattleEquipment"), nameof(BattleGear));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "CivilianEquipment"), nameof(CivilianGear));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "AllAliveHeroes"), nameof(AllHeroes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsBanditFaction"), nameof(No));
            Patch(AccessTools.Method(typeof(Equipment), "IsItemFitsToSlot"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(MBEquipmentRosterExtensions), "All"), nameof(AllRosters));
            Patch(AccessTools.Method(typeof(Hero), "SetSkillValue"), nameof(Skip));
            Patch(AccessTools.Method(typeof(HeroDeveloper), "InitializeHeroDeveloper"), nameof(Develop));
            Patch(AccessTools.Method(typeof(HeroDeveloper), "SetInitialLevel"), nameof(Level));
            Patch(AccessTools.Method(typeof(Hero), "ChangeState"), nameof(Activate));
            Patch(AccessTools.Method(typeof(TeleportHeroAction), "ApplyImmediateTeleportToSettlement"), nameof(Teleport));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ResolveHero"), nameof(ResolveHero));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "TryInstallClanLeader"), nameof(Install));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ShowRegencyEnded"), nameof(Skip));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "BuildContinuityLine"), nameof(Continuity));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ShowWardSucceededAfterDeath"), nameof(ReplacementNotice));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            check(harmony.CreateClassProcessor(mod.GetType("BellumCivile.Patches.RegencyAdulthoodEventPatch", true)).Patch().Count == 1,
                "The adulthood receipt wraps the installed native birthday dispatcher");
            harmony.CreateClassProcessor(mod.GetType("BellumCivile.Patches.HostageAdulthoodRelocationGuardPatch", true)).Patch();

            foreach (int adulthood in new[] { 16, 18, 21 })
            {
                Reset(adulthood); Young().Remove(ward); dispatcher.OnHeroComesOfAge(ward);
                check(house.Leader == regent && Prepared() && _skills == 1 && _developers == 1 && _selections == 2,
                    "Reverse-order birthday listeners finish native skills and equipment before handover at age " + adulthood);
                check(Battle[ward][EquipmentIndex.Horse].Item != null && Battle[ward][EquipmentIndex.Weapon0].Item != null,
                    "Native birthday gives the heir adult weapons and a mount at age " + adulthood);
                Tick(); check(house.Leader == ward && ((IList)recordField.GetValue(regencies)).Count == 0 && _skills == 1 && _selections == 2,
                    "Post-event handover preserves the completed native loadout without rerolling at age " + adulthood);
                Tick(); check(_skills == 1, "Repeated ticks cannot initialize adult skills again");
            }

            Reset(); bool blockedInsideEvent = false;
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(new object(), hero => blockedInsideEvent = !Ready());
            Young().Remove(ward); dispatcher.OnHeroComesOfAge(ward);
            check(blockedInsideEvent && _skills == 1 && _selections == 2,
                "A reentrant handover inside the birthday dispatcher waits instead of activating early or dispatching twice");

            Reset(); check(Ready() && Young().Count == 0 && Prepared() && _skills == 1 && _selections == 2,
                "Early daily/load/death handover consumes the real pending birthday and completes the entire native event");
            check(Ready() && _skills == 1 && _selections == 2, "Repeated readiness checks do not repeat native initialization");
            Tick(); check(house.Leader == ward && _skills == 1, "Catch-up birthday queues the same safe post-event handover");
            Reset(); Ready();
            var store = new Store(); regencies.SyncData(store); store.IsLoading = true;
            var loaded = new RegencyBehavior(); loaded.SyncData(store);
            manager.Items.Remove(regencies); manager.Items.Add(loaded); regencies = loaded;
            record = (RegencyRecord)((IList)recordField.GetValue(regencies))[0];
            check(Ready() && _skills == 1 && _selections == 2,
                "A saved completion receipt prevents replay after loading a new regency behavior");
            Reset(); Set(ward, "_heroState", Hero.CharacterStates.Disabled);
            check(Ready() && _skills == 1 && _activations == 0, "Disabled wards initialize through vanilla before any explicit activation");
            Call(regencies, "EndRegency", house, record, ward, false);
            check(house.Leader == ward && _activations == 1 && _skills == 1, "Disabled ward handover activates only after native development");

            Reset(); Set(ward, "_heroState", Hero.CharacterStates.Prisoner);
            ward.CaptivityStartTime = CampaignTime.Days(10);
            var hostages = new HostagePactBehavior(); manager.Items.Add(hostages);
            Set(hostages, "_pacts", new List<HostagePactRecord> { new HostagePactRecord { Phase = HostagePactPhase.Active,
                FirstHostage = new TreatyHostageRecord { Hero = ward, CustodyEstablished = true, CaptivityStartDay = 10 } } });
            check(Ready() && _skills == 1 && _activations == 0 && _teleports == 0,
                "Captive wards receive native adult development without bypassing the actual hostage relocation guard");
            Tick(); check(house.Leader == ward && ward.IsPrisoner && _activations == 0 && _teleports == 0,
                "A hostage can inherit clan leadership without being released or removed from the dungeon");
            check(Ready() && _skills == 1, "Captive readiness also cannot deliver adult skills twice");

            Reset(); _player = house;
            check(Ready() && _skills == 0 && _developers == 0 && _levels == 1 && _selections == 2,
                "Player ward catch-up preserves native player education and level handling instead of NPC inherited skills");

            Reset(); _throwSelection = true;
            check(!Ready() && !Young().ContainsKey(ward) && !Prepared() && _skills == 1 && house.Leader == regent,
                "Native initialization failure retains the regency rather than activating an unequipped heir");
            _throwSelection = false;
            check(!Ready() && _skills == 1 && !Prepared(), "Partial native failure is not blindly retried with another skill roll");
            store = new Store(); regencies.SyncData(store); store.IsLoading = true;
            loaded = new RegencyBehavior(); loaded.SyncData(store);
            manager.Items.Remove(regencies); manager.Items.Add(loaded); regencies = loaded;
            record = (RegencyRecord)((IList)recordField.GetValue(regencies))[0];
            check(!Ready() && _skills == 1, "Saved interrupted initialization remains deferred rather than rerolling after reload");
            var replacement = Person("replacement", house, 16, Hero.CharacterStates.NotSpawned);
            record.ReplaceWard(replacement); Young()[replacement] = 15; ward = replacement;
            check(Ready() && _skills == 2, "A different ward cannot inherit the previous ward's failure or completion receipt");

            Reset(); var dyingWard = ward; Set(dyingWard, "_heroState", Hero.CharacterStates.Dead);
            replacement = Person("adult_replacement", house, 16, Hero.CharacterStates.NotSpawned);
            _continuity = new List<Hero> { replacement }; Young()[replacement] = 15;
            Call(regencies, "ReplaceDyingWard", house, record, dyingWard);
            check(house.Leader == replacement && record.WardHeroId == replacement.StringId && _skills == 1 && _replacementNotices == 1,
                "A dying ward's adult successor becomes the recorded ward before native initialization and leadership delivery");
            Reset(); dyingWard = ward; Set(dyingWard, "_heroState", Hero.CharacterStates.Dead);
            replacement = Person("blocked_replacement", house, 16, Hero.CharacterStates.NotSpawned);
            _continuity = new List<Hero> { replacement }; Young()[replacement] = 15; _throwSelection = true;
            Call(regencies, "ReplaceDyingWard", house, record, dyingWard);
            check(house.Leader == regent && record.WardHeroId == replacement.StringId && _replacementNotices == 0,
                "Failed adulthood preparation retains the replacement ward without falsely announcing successful succession");

            Reset(); Young().Remove(ward); _throwSelection = true;
            bool propagated = false;
            try { dispatcher.OnHeroComesOfAge(ward); } catch (InvalidOperationException) { propagated = true; }
            check(propagated && !Prepared() && house.Leader == regent,
                "The normal native birthday event still propagates its original failure without a false completion receipt");

            Reset(); Young().Remove(ward); Set(ward, "_heroState", Hero.CharacterStates.Active);
            check(Ready() && _skills == 0 && _selections == 2 && Battle[ward][EquipmentIndex.Horse].Item != null,
                "Known old adult ward receives missing equipment only, never rerolled adult skills");
            Reset(); Young().Clear(); Set(ward, "_heroState", Hero.CharacterStates.Active); ward.Father = regent;
            Repair(ward); check(_selections == 2 && _skills == 0 && Battle[ward][EquipmentIndex.Horse].Item != null,
                "A former NPC ward still wearing an exact childhood loadout receives equipment-only recovery");
            Repair(ward); check(_selections == 2, "Recovered legacy adult equipment is not replaced a second time");
            Reset(); Set(ward, "_heroState", Hero.CharacterStates.Active); ward.Father = regent;
            Civilian[ward] = Copy(_adultCivilian, Equipment.EquipmentType.Civilian); var customCivilian = Civilian[ward][EquipmentIndex.Body].Item;
            Repair(ward); check(_selections == 1 && Civilian[ward][EquipmentIndex.Body].Item == customCivilian,
                "Legacy repair preserves an unrelated adult civilian outfit");
            Reset(); Set(ward, "_heroState", Hero.CharacterStates.Active); ward.Father = regent;
            Battle[ward] = Gear(Equipment.EquipmentType.Battle, (int)EquipmentIndex.Body);
            Repair(ward); check(_selections == 0, "An unarmed adult with a non-childhood outfit is not automatically re-equipped");
            Battle[ward] = new Equipment(Equipment.EquipmentType.Battle);
            Repair(ward); check(_selections == 0, "Unknown empty adult loadouts are not proof of a past regency and are left alone");
            Battle[ward] = Copy(_child, Equipment.EquipmentType.Battle); _player = house;
            Repair(ward); check(_selections == 0, "Legacy sweep never changes ordinary player-clan inventory");
            _player = Blank<Clan>(); Ages[ward] = 15;
            Repair(ward); check(_selections == 0, "Underage nobles keep their childhood equipment");
            Ages[ward] = 16; _missingSelection = true;
            Repair(ward); check(Battle[ward][EquipmentIndex.Body].Item == _child[EquipmentIndex.Body].Item,
                "Missing adult templates leave the existing outfit intact");

            var fields = typeof(RegencyRecord).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value)).ToArray();
            check(ids.Length == 8 && ids.Distinct().Count() == 8 && ids.Min() == 1 && ids.Max() == 8,
                "Regency adulthood receipts append unique save IDs without changing the original six fields");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
            AccessTools.PropertySetter(typeof(RegencyBehavior), "Instance").Invoke(null, new object[] { oldRegency });
            ticks.SetValue(null, oldTicks);
        }
    }
}
