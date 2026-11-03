using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class AsylumVisitTests
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
        internal object Saved;
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (key != "BellumCivile_AsylumVisits") return false;
            if (IsLoading) value = (T)Saved;
            else
            {
                var copy = new Dictionary<string, AsylumVisitRecord>();
                foreach (var pair in (Dictionary<string, AsylumVisitRecord>)(object)value)
                {
                    var record = new AsylumVisitRecord();
                    foreach (var field in typeof(AsylumVisitRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
                        field.SetValue(record, field.GetValue(pair.Value));
                    copy[pair.Key] = record;
                }
                Saved = copy;
            }
            return true;
        }
    }
    private static readonly Type Behavior = typeof(ExiledClanRecoveryBehavior);
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static readonly Dictionary<Hero, Settlement> Locations = new Dictionary<Hero, Settlement>();
    private static readonly HashSet<Settlement> UnsafeHosts = new HashSet<Settlement>();
    private static readonly List<Settlement> Holdings = new List<Settlement>();
    private static Clan _player, _exile, _vassal;
    private static Hero _ruler, _guest, _speaker;
    private static Kingdom _realm, _foreign;
    private static Clan _foreignCrown;
    private static bool _landed, _blockDeparture;
    private static int _departures, _randomDraws;
    private static ExiledClanRecoveryBehavior _behavior;
    private static Manager _manager;
    private static MobileParty _party;
    private static Settlement _partyHome;
    private static string _block;
    private static float _day;
    private static bool _worldSafe, _noticeSafe, _hostile;
    private static int _teleports, _enters, _leaves, _joins, _relations, _relationCalls, _denials, _notices;
    private static InquiryData _inquiry;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object value, string name, params object[] args)
        => AccessTools.Method(value is Type type ? type : value.GetType(), name).Invoke(value is Type ? null : value, args);
    private static void Set(object value, string name, object data) => AccessTools.PropertySetter(value.GetType(), name).Invoke(value, new[] { data });
    private static Dictionary<string, AsylumVisitRecord> Visits(ExiledClanRecoveryBehavior b = null)
        => (Dictionary<string, AsylumVisitRecord>)AccessTools.Field(Behavior, "_asylumVisits").GetValue(b ?? _behavior);
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Ignore() => false;
    private static bool World(ref bool __result) { __result = _worldSafe; return false; }
    private static bool Notice(ref bool __result) { __result = _noticeSafe; return false; }
    private static bool War(ref bool __result) { __result = _hostile; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Remaining(CampaignTime __instance, ref float __result) { __result = (float)__instance.ToDays - _day; return false; }
    private static bool Elapsed(CampaignTime __instance, ref float __result) { __result = _day - (float)__instance.ToDays; return false; }
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool PlayerHero(ref Hero __result) { __result = _ruler; return false; }
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result) { __result = Leaders.FirstOrDefault(p => p.Value == __instance).Key; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Crown(Kingdom __instance, ref Clan __result) { __result = __instance == _foreign ? _foreignCrown : _player; return false; }
    private static bool RealmLeader(Kingdom __instance, ref Hero __result)
    { __result = __instance == _foreign ? Leaders[_foreignCrown] : Leaders[_player]; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Leaders.Keys); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new[] { _realm, _foreign }.Where(k => k != null)); return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result) { __result = new MBReadOnlyList<Hero>(new[] { Leaders[__instance] }); return false; }
    private static bool Settlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(Holdings); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool ClanName(Clan __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool HomeName(Settlement __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !(__instance == _guest && _block == "dead"); return false; }
    private static bool Prisoner(Hero __instance, ref bool __result) { __result = __instance == _guest && _block == "prisoner"; return false; }
    private static bool Child(ref bool __result) { __result = _block == "child"; return false; }
    private static bool Age(ref float __result) { __result = 30; return false; }
    private static bool Traveling(ref bool __result) { __result = _block == "traveling"; return false; }
    private static bool Host(Settlement settlement, Kingdom realm, ref bool __result)
    { __result = settlement != null && Owners[settlement]?.Kingdom == realm && !UnsafeHosts.Contains(settlement); return false; }
    private static bool PlayerRefuge(Kingdom kingdom, ref bool __result)
    { __result = kingdom == _realm && Holdings.Count > 0 && Leaders[_player] == _ruler; return false; }
    private static bool Location(Hero __instance, ref Settlement __result)
    { if (__instance == _guest && _party != null) __result = _partyHome; else Locations.TryGetValue(__instance, out __result); return false; }
    private static bool PartyOf(Hero __instance, ref MobileParty __result) { __result = __instance == _guest ? _party : null; return false; }
    private static bool PartyHome(ref Settlement __result) { __result = _partyHome; return false; }
    private static bool PartyLeader(ref Hero __result) { __result = _block == "other-commander" ? _ruler : _guest; return false; }
    private static bool PartyClan(ref Clan __result) { __result = _exile; return false; }
    private static bool NoMainParty(ref MobileParty __result) { __result = null; return false; }
    private static bool Quest(ref bool __result) { __result = _block == "quest"; return false; }
    private static bool Sea(ref bool __result) { __result = _block == "sea"; return false; }
    private static bool Transition(ref bool __result) { __result = _block == "transition"; return false; }
    private static bool Disband(ref bool __result) { __result = _block == "disband"; return false; }
    private static bool Landless(ref bool __result) { __result = !_landed; return false; }
    private static bool Mercenary(ref bool __result) { __result = _block == "mercenary"; return false; }
    private static bool Minor(ref bool __result) { __result = _block == "minor"; return false; }
    private static bool Components(Clan __instance, ref MBReadOnlyList<WarPartyComponent> __result)
    {
        __result = new MBReadOnlyList<WarPartyComponent>();
        if (__instance == _exile && _party != null)
        {
            var component = Blank<LordPartyComponent>();
            AccessTools.PropertySetter(typeof(PartyComponent), "MobileParty").Invoke(component, new object[] { _party });
            __result.Add(component);
        }
        return false;
    }
    private static bool Random(ref int __result) { _randomDraws++; __result = 0; return false; }
    private static bool Departure(Clan clan)
    {
        _departures++;
        if (_blockDeparture) return false;
        var old = Realms[clan]; Realms[clan] = null;
        Call(_behavior, "OnClanChangedKingdom", clan, old, null, ChangeKingdomAction.ChangeKingdomActionDetail.LeaveKingdom, false);
        return false;
    }
    private static bool Army(ref Army __result) { __result = _block == "army" ? Blank<Army>() : null; return false; }
    private static bool Battle(ref TaleWorlds.CampaignSystem.MapEvents.MapEvent __result)
    { __result = _block == "battle" ? Blank<TaleWorlds.CampaignSystem.MapEvents.MapEvent>() : null; return false; }
    private static bool Siege(ref TaleWorlds.CampaignSystem.Siege.SiegeEvent __result)
    { __result = _block == "siege" ? Blank<TaleWorlds.CampaignSystem.Siege.SiegeEvent>() : null; return false; }
    private static bool Enter(MobileParty __0, Settlement __1) { _enters++; _partyHome = __1; return false; }
    private static bool LeaveParty() { _leaves++; _partyHome = null; return false; }
    private static bool LeaveHero(Hero __0) { _leaves++; Locations.Remove(__0); return false; }
    private static bool Teleport(Hero __0, Settlement __1)
    { if (_party != null) throw new Exception("Commander must not be removed from their party"); _teleports++; Locations[__0] = __1; return false; }
    private static bool Join(Clan clan, Kingdom newKingdom)
    {
        _joins++; Realms[clan] = newKingdom;
        Call(_behavior, "OnClanChangedKingdom", clan, null, newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        return false;
    }
    private static bool Relation(int __2) { _relationCalls++; _relations += __2; return false; }
    private static bool Deny(string clanId)
    { _denials++; Call(_behavior, "RemovePendingAsylum", clanId); return false; }
    private static bool Inquiry(InquiryData __0) { _inquiry = __0; _notices++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.asylum_visits");
        var oldCampaign = Campaign.Current; var oldRegistry = LordshipBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        var hourTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerHour"); var oldHourTicks = hourTicks.GetValue(null);
        void Patch(Type type, string name, string handler, bool getter = true) => h.Patch(
            getter ? AccessTools.PropertyGetter(type, name) : AccessTools.Method(type, name),
            prefix: new HarmonyMethod(typeof(AsylumVisitTests), handler));
        Clan House(string id)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            var hero = Blank<Hero>(); hero.StringId = id + "_head";
            Leaders[clan] = hero; Realms[clan] = null; return clan;
        }
        Settlement Estate(string id, Clan owner, float prosperity)
        {
            var home = Blank<Settlement>(); home.StringId = id; Owners[home] = owner;
            var town = Blank<Town>(); AccessTools.Field(typeof(Settlement), "Town").SetValue(home, town);
            Set(town, "Prosperity", prosperity); Holdings.Add(home); return home;
        }
        void Pending()
        {
            ((List<string>)AccessTools.Field(Behavior, "_pendingAsylumClanIds").GetValue(_behavior)).Add(_exile.StringId);
            ((Dictionary<string, string>)AccessTools.Field(Behavior, "_pendingAsylumTargetKingdomIds").GetValue(_behavior))[_exile.StringId] = _realm.StringId;
        }
        void Reset(bool party = false)
        {
            _behavior = new ExiledClanRecoveryBehavior(); _day = 100; _block = null; _hostile = false;
            _manager.Items.Clear(); _manager.Items.Add(_behavior);
            _worldSafe = _noticeSafe = true; Realms[_exile] = null; Leaders[_player] = _ruler;
            _speaker = _guest; Locations.Clear(); UnsafeHosts.Clear(); _partyHome = null;
            _teleports = _enters = _leaves = _joins = _relations = _relationCalls = _denials = _notices = 0;
            _party = null; _inquiry = null;
            if (party)
            {
                _party = Blank<MobileParty>(); _party.IsActive = true;
                Set(_party, "IsLordParty", true); Set(_party, "Ai", Blank<MobilePartyAi>());
                var partyBase = Blank<PartyBase>(); Set(partyBase, "MobileParty", _party); Set(_party, "Party", partyBase);
                AccessTools.Field(typeof(MobileParty), "_position").SetValue(_party, new CampaignVec2(new Vec2(10, 10), true));
            }
        }
        bool Create() => (bool)Call(_behavior, "TryCreateAsylumVisit", _exile, _realm);
        void Process() => Call(_behavior, "ProcessAsylumVisits");
        bool Awaiting(Hero hero) => (bool)Call(_behavior, "IsAwaitingAsylum", hero);
        void Oath()
        {
            Locations[_ruler] = Visits()[_exile.StringId].Settlement;
            Call(_behavior, "BeginAsylumConversation"); Call(_behavior, "CompleteAsylumOath");
        }
        try
        {
            ticks.SetValue(null, 24000L); hourTicks.SetValue(null, 1000L);
            _player = House("asylum_player"); _exile = House("asylum_exile"); _vassal = House("asylum_vassal");
            _ruler = Leaders[_player]; _guest = Leaders[_exile];
            _realm = Blank<Kingdom>(); _realm.StringId = "asylum_realm"; Realms[_player] = Realms[_vassal] = _realm;
            var campaign = Blank<Campaign>(); SetCampaign(campaign);
            _manager = new Manager(); campaign.AddCampaignBehaviorManager(_manager);
            var registry = new LordshipBehavior(); AccessTools.Property(typeof(LordshipBehavior), "Instance").SetValue(null, registry);
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            AccessTools.Field(typeof(LordshipBehavior), "_campaign").SetValue(registry, campaign);
            Patch(typeof(CampaignTime), "Now", nameof(Now)); Patch(typeof(CampaignTime), "IsFuture", nameof(Future));
            Patch(typeof(CampaignTime), "RemainingDaysFromNow", nameof(Remaining));
            Patch(typeof(CampaignTime), "ElapsedDaysUntilNow", nameof(Elapsed));
            Patch(typeof(Clan), "PlayerClan", nameof(Player)); Patch(typeof(Hero), "MainHero", nameof(PlayerHero));
            Patch(typeof(Hero), "OneToOneConversationHero", nameof(Speaker)); Patch(typeof(Clan), "Leader", nameof(Leader));
            Patch(typeof(Clan), "Kingdom", nameof(Realm)); Patch(typeof(Hero), "Clan", nameof(HeroClan));
            Patch(typeof(Clan), "All", nameof(AllClans)); Patch(typeof(Kingdom), "All", nameof(AllRealms));
            Patch(typeof(Kingdom), "RulingClan", nameof(Crown)); Patch(typeof(Kingdom), "Leader", nameof(RealmLeader));
            Patch(typeof(Kingdom), "Settlements", nameof(Settlements)); Patch(typeof(Settlement), "OwnerClan", nameof(Owner));
            Patch(typeof(Clan), "Heroes", nameof(Heroes)); Patch(typeof(Clan), "IsNoble", nameof(Yes));
            Patch(typeof(Clan), "IsEliminated", nameof(No)); Patch(typeof(Clan), "IsMinorFaction", nameof(Minor));
            Patch(typeof(Clan), "IsUnderMercenaryService", nameof(Mercenary)); Patch(typeof(Clan), "IsBanditFaction", nameof(No));
            Patch(typeof(Clan), "WarPartyComponents", nameof(Components));
            Patch(typeof(Hero), "IsAlive", nameof(Alive)); Patch(typeof(Hero), "IsDisabled", nameof(No));
            Patch(typeof(Hero), "IsChild", nameof(Child)); Patch(typeof(Hero), "IsPrisoner", nameof(Prisoner));
            Patch(typeof(Hero), "Age", nameof(Age));
            Patch(typeof(Hero), "IsTraveling", nameof(Traveling)); Patch(typeof(Hero), "HasMet", nameof(Yes));
            Patch(typeof(Hero), "CurrentSettlement", nameof(Location)); Patch(typeof(Hero), "PartyBelongedTo", nameof(PartyOf));
            Patch(typeof(Hero), "Name", nameof(Name)); Patch(typeof(Clan), "Name", nameof(ClanName)); Patch(typeof(Settlement), "Name", nameof(HomeName));
            Patch(typeof(Clan), "IsAtWarWith", nameof(War), false);
            Patch(typeof(Kingdom), "IsAtWarWith", nameof(War), false);
            Patch(Behavior, "IsLandlessForExile", nameof(Landless), false);
            Patch(Behavior, "IsPlayerRuledValidRefuge", nameof(PlayerRefuge), false);
            Patch(Behavior, "IsAsylumHost", nameof(Host), false);
            Patch(Behavior, "CanMutateAsylumWorld", nameof(World), false); Patch(Behavior, "CanPresentAsylumNotice", nameof(Notice), false);
            Patch(Behavior, "DenyPlayerAsylum", nameof(Deny), false);
            Patch(typeof(MobileParty), "MainParty", nameof(NoMainParty)); Patch(typeof(MobileParty), "CurrentSettlement", nameof(PartyHome));
            Patch(typeof(MobileParty), "LeaderHero", nameof(PartyLeader)); Patch(typeof(MobileParty), "ActualClan", nameof(PartyClan));
            Patch(typeof(MobileParty), "IsCurrentlyUsedByAQuest", nameof(Quest)); Patch(typeof(MobileParty), "IsCurrentlyAtSea", nameof(Sea));
            Patch(typeof(MobileParty), "IsTransitionInProgress", nameof(Transition)); Patch(typeof(MobileParty), "IsDisbanding", nameof(Disband));
            Patch(typeof(MobileParty), "Army", nameof(Army)); Patch(typeof(MobileParty), "MapEvent", nameof(Battle));
            Patch(typeof(MobileParty), "SiegeEvent", nameof(Siege));
            Patch(typeof(MobileParty), "SetMoveModeHold", nameof(Ignore), false);
            Patch(typeof(EnterSettlementAction), "ApplyForParty", nameof(Enter), false);
            Patch(typeof(LeaveSettlementAction), "ApplyForParty", nameof(LeaveParty), false);
            Patch(typeof(LeaveSettlementAction), "ApplyForCharacterOnly", nameof(LeaveHero), false);
            Patch(typeof(TeleportHeroAction), "ApplyImmediateTeleportToSettlement", nameof(Teleport), false);
            Patch(typeof(ChangeKingdomAction), "ApplyByJoinToKingdom", nameof(Join), false);
            Patch(typeof(RelationMemoryService), "ApplyChange", nameof(Relation), false);
            foreach (var method in Behavior.Assembly.GetType("BellumCivile.BellumCivileNotifications", true)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == "ShowPersonal"))
                h.Patch(method, prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(Ignore)));
            Patch(typeof(InformationManager), "ShowInquiry", nameof(Inquiry), false);
            Patch(typeof(BellumCivileLogger), "Log", nameof(Ignore), false);

            var own = Estate("own_keep", _player, 100);
            var richer = Estate("richer_keep", _player, 200);
            var vassal = Estate("vassal_keep", _vassal, 1000);
            Reset();
            check(Call(Behavior, "FindAsylumSettlement", _realm) == richer, "Best personal keep wins even over a richer vassal holding");
            UnsafeHosts.Add(richer); check(Call(Behavior, "FindAsylumSettlement", _realm) == own, "Unsafe personal keeps are skipped");
            UnsafeHosts.Add(own); check(Call(Behavior, "FindAsylumSettlement", _realm) == null, "Vassal keeps are not used while the player still owns fortifications");
            Owners[own] = Owners[richer] = _vassal; UnsafeHosts.Clear();
            check(Call(Behavior, "FindAsylumSettlement", _realm) == vassal, "A landless ruler uses the best vassal keep");
            Owners[own] = Owners[richer] = _player;
            Reset(); check(Create(), "A free independent exile can arrive for an audience");
            var visit = Visits()[_exile.StringId];
            check(_teleports == 1 && _enters == 0 && _guest.CurrentSettlement == richer && visit.Deadline.ToDays == 107,
                "Partyless visitors use native teleport and receive seven days from arrival");
            check(Realms[_exile] == null && _joins == 0 && Awaiting(_guest) && !Awaiting(_ruler), "Arrival does not grant membership and only the actual guest is held");
            foreach (string name in new[] { "AsylumVisitorCommandPatch", "AsylumVisitorRelocationPatch" })
            {
                var patch = Behavior.Assembly.GetType("BellumCivile.Patches." + name, true);
                var args = new object[] { _guest, null };
                check(!(bool)Call(patch, "Prefix", args), "A waiting visitor is protected by " + name);
                args[0] = _ruler;
                check((bool)Call(patch, "Prefix", args), "Unrelated heroes bypass " + name);
            }
            Pending(); Process(); check(_teleports == 1, "Processing an existing visit cannot duplicate placement or reset its window");
            var store = new Store(); Call(_behavior, "SyncAsylumVisits", store); store.IsLoading = true;
            var loaded = new ExiledClanRecoveryBehavior(); Call(loaded, "SyncAsylumVisits", store);
            check(Visits(loaded)[_exile.StringId] != visit && Visits(loaded)[_exile.StringId].Deadline.ToDays == 107,
                "Save/load preserves the original visit deadline instead of restarting it");
            check(!(bool)Call(_behavior, "CanDiscussAsylum"), "The guest cannot be admitted remotely");
            Locations[_ruler] = richer; check((bool)Call(_behavior, "CanDiscussAsylum"), "The ruler can hear the guest at the designated keep");
            Call(_behavior, "BeginAsylumConversation");
            check(_joins == 0 && _relations == 0 && visit.Deadline.ToDays == 107, "Opening or postponing an audience has no admission side effects");
            Oath(); check(_joins == 1 && _relations == 10 && visit.State == AsylumVisitRecord.Accepted,
                "The completed oath admits the intact clan and awards exactly ten relations");
            Process(); Process(); check(_joins == 1 && _relationCalls == 1 && Visits().Count == 0 && _leaves == 0,
                "Native membership callbacks and later ticks cannot duplicate admission rewards or force accepted guests out");

            Reset(true); check(Create(), "An intact lord party can arrive"); visit = Visits()[_exile.StringId];
            check(_teleports == 0 && _enters == 1 && visit.Party == _party && _party.LeaderHero == _guest && _party.Ai.IsDisabled,
                "Party arrival preserves the commander and uses a bounded native AI hold");
            _day = 106.99f; Process(); check(Visits().Count == 1, "The visit remains open immediately before seven days");
            _day = 107; Process(); check(Visits().Count == 0 && !_party.Ai.IsDisabled && _leaves == 1 && _relations == 0,
                "The seventh-day boundary expires the visit, releases its party and charges no relations");
            Reset(true); check(Create(), "A fresh party can begin a second visit"); visit = Visits()[_exile.StringId];
            AccessTools.Method(typeof(MobilePartyAi), "DisableForHours").Invoke(_party.Ai, new object[] { 300 });
            check(_party.Ai.IsDisabled && ((CampaignTime)AccessTools.Property(typeof(MobilePartyAi), "_enableAgainAtHour").GetValue(_party.Ai)).ToDays != visit.PartyHoldUntil.ToDays,
                "A later external hold has its own deadline");
            visit.State = AsylumVisitRecord.Cancelled; Process();
            check(_party.Ai.IsDisabled, "Cleanup preserves a newer AI hold installed by another system");
            Reset(); Create(); Pending(); Locations[_ruler] = richer;
            Call(_behavior, "RefuseAsylumAudience");
            check(_relations == -10 && _leaves == 0, "Refusal charges its relation penalty but does not move an actor during dialogue");
            _worldSafe = false; Process(); check(Visits().Count == 1 && _leaves == 0, "Scene-time cleanup is deferred");
            _worldSafe = true; Process(); Process();
            check(_relationCalls == 1 && _denials == 1 && _leaves == 1, "Refusal is applied once before returning to existing exile recovery");
            foreach (string reason in new[] { "dead", "prisoner", "child", "traveling", "other-commander", "quest", "sea", "transition", "disband", "army", "battle", "siege" })
            {
                Reset(true); _block = reason;
                check(!Create() && _enters == 0 && _teleports == 0, "Unsafe guest is never relocated: " + reason);
            }
            Reset(true); _party.Ai.DisableForHours(10);
            check(!Create(), "An already disabled party is not taken over by asylum placement");
            foreach (string reason in new[] { "dead", "prisoner", "host-lost", "ruler-changed", "joined-elsewhere" })
            {
                Reset(); Create(); visit = Visits()[_exile.StringId];
                if (reason == "host-lost") UnsafeHosts.Add(richer);
                else if (reason == "ruler-changed") Leaders[_player] = Leaders[_vassal];
                else if (reason == "joined-elsewhere") Realms[_exile] = Blank<Kingdom>();
                else _block = reason;
                Process(); check(Visits().Count == 0 && _joins == 0 && _relations == 0, "Invalid petition is cancelled without admission: " + reason);
                if (reason == "prisoner") check(_leaves == 0, "Cancellation never frees or relocates a captive");
            }
            Reset(); Pending(); _noticeSafe = false; _day = 1000; Process();
            check(Visits().Count == 0 && _teleports == 0, "Legacy requests wait for a visible notice before starting their clock");
            _noticeSafe = true; Process();
            check(Visits()[_exile.StringId].Deadline.ToDays == 1007 && _teleports == 1, "Legacy requests receive a fresh seven-day audience on actual arrival");
            Call(_behavior, "ShowPlayerAsylumInquiry", _exile, _realm);
            check(_notices == 1 && _inquiry.IsAffirmativeOptionShown && !_inquiry.IsNegativeOptionShown
                && _inquiry.Text.Contains(richer.StringId) && _inquiry.Text.Contains("7 days"),
                "Asylum notice identifies the keep and wait, with acknowledgement instead of remote acceptance");
            _inquiry.AffirmativeAction(); Call(_behavior, "ShowPlayerAsylumInquiry", _exile, _realm);
            check(_notices == 1 && _joins == 0, "Acknowledgement neither admits the clan nor repeats the notice");
            CheckEstate(check, registry, richer);
            _block = null; Realms[_exile] = null;
            Call(_behavior, "BeginAsylumConversation");
            var gift = registry.GetGrant(_guest); var selectedVillage = gift.Village;
            // Vacate the earlier direct-grant fixture, then exercise the full oath path.
            Call(registry, "Remove", gift); _relations = _relationCalls = 0;
            Locations[_ruler] = richer;
            Call(_behavior, "BeginAsylumConversation");
            AccessTools.Field(Behavior, "_asylumOfferedVillage").SetValue(_behavior, selectedVillage);
            Call(_behavior, "CompleteAsylumOath"); visit = Visits()[_exile.StringId];
            check(_joins == 1 && _relations == 20 && visit.GrantSettled && visit.GrantAwarded
                && registry.GetGrant(_guest)?.Village == selectedVillage, "Completing homage with the chosen village grants it and awards twenty relations total");
            var decision = new Store(); Call(_behavior, "SyncAsylumVisits", decision); decision.IsLoading = true;
            var recovered = new ExiledClanRecoveryBehavior(); Call(recovered, "SyncAsylumVisits", decision);
            _behavior = recovered; Process(); Process();
            check(_joins == 1 && _relationCalls == 2 && Visits().Count == 0, "A saved completed decision cannot repeat its gift or acceptance reward");
            check(registry.GetGrant(_guest).IsValid, "Accepting asylum leaves the personal endowment valid");
            Reset(); Create(); Pending(); Locations[_ruler] = richer; Call(_behavior, "BeginAsylumConversation");
            AccessTools.Field(Behavior, "_asylumOfferedVillage").SetValue(_behavior, selectedVillage);
            Call(_behavior, "CompleteAsylumOath");
            check(_joins == 0 && _relations == 0 && Visits()[_exile.StringId].State == AsylumVisitRecord.Waiting,
                "An estate that became occupied cannot silently complete a stale promise");
            CheckCommand(check, h, party => Reset(party), richer);
        }
        finally
        {
            h.UnpatchAll(h.Id); SetCampaign(oldCampaign); ticks.SetValue(null, oldTicks); hourTicks.SetValue(null, oldHourTicks);
            AccessTools.Property(typeof(LordshipBehavior), "Instance").SetValue(null, oldRegistry);
            Leaders.Clear(); Realms.Clear(); Owners.Clear(); Locations.Clear(); UnsafeHosts.Clear(); Holdings.Clear();
            _foreign = null; _foreignCrown = null; _landed = _blockDeparture = false;
        }
    }

    private static void SetCampaign(Campaign campaign) => AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });

    private static void CheckCommand(Action<bool, string> check, Harmony h, Action<bool> reset, Settlement keep)
    {
        h.Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyByLeaveKingdom"),
            prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(Departure)));
        h.Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(Random)));
        _foreign = Blank<Kingdom>(); _foreign.StringId = "asylum_source"; _foreignCrown = _vassal;
        Realms[_vassal] = _foreign;
        string Command(params string[] args) => CheatCommands.TestAsylum(args.ToList());
        void Prepare(bool party = false)
        {
            reset(party); Realms[_exile] = _foreign; _foreignCrown = _vassal;
            _landed = _blockDeparture = false; _departures = _randomDraws = 0;
        }
        Prepare();
        check(Command("help").StartsWith("Usage:") && Command("too", "many").StartsWith("Usage:") && _departures == 0,
            "Test command documents its save changes and rejects extra arguments without mutation");
        var campaign = Campaign.Current; SetCampaign(null);
        check(Command().Contains("Load a campaign"), "Test command is safe at the main menu"); SetCampaign(campaign);
        _manager.Items.Clear(); check(Command().Contains("unavailable"), "Missing exile behavior cannot mutate the campaign"); _manager.Items.Add(_behavior);
        Leaders[_player] = Leaders[_vassal];
        check(Command().Contains("must rule") && _departures == 0, "Only the actual player ruler may run the test"); Leaders[_player] = _ruler;
        _noticeSafe = false; check(Command().Contains("campaign map") && _departures == 0, "Unsafe screens prevent the test before departure"); _noticeSafe = true;
        foreach (var home in Holdings) UnsafeHosts.Add(home);
        check(Command().Contains("No safe keep") && _departures == 0, "No host keep means no test exile"); UnsafeHosts.Clear();
        check(Command("missing_id").Contains("No clan has ID") && _departures == 0, "Unknown IDs never silently fall back to random selection");
        _landed = true; check(Command(_exile.StringId).Contains("owns a town") && _departures == 0, "Landed clans are never stripped of settlements by the test"); _landed = false;
        _foreignCrown = _exile; check(Command(_exile.StringId).Contains("Ruling clans") && _departures == 0, "Foreign rulers are excluded"); _foreignCrown = _vassal;
        _hostile = true; check(Command(_exile.StringId).Contains("at war") && _departures == 0, "The test never overrides diplomatic hostility"); _hostile = false;
        _foreign.StringId = "bc_feud_test";
        check(Command(_exile.StringId).Contains("temporary") && _departures == 0, "Temporary conflict realms cannot supply a test exile"); _foreign.StringId = "asylum_source";
        foreach (string block in new[] { "dead", "prisoner", "traveling", "minor", "mercenary" })
        {
            _block = block;
            check(Command(_exile.StringId).StartsWith("Error:") && _departures == 0, "Unsafe test candidate is rejected before mutation: " + block);
        }
        _block = null;
        string report = Command(_exile.StringId.ToUpperInvariant());
        var visit = Visits()[_exile.StringId];
        check(report.StartsWith("Success!") && _departures == 1 && _joins == 0 && Realms[_exile] == null,
            "An exact clan ID triggers native departure without recruiting the clan");
        check(_teleports == 1 && _notices == 1 && visit.Settlement == keep && visit.Deadline.ToDays == 107,
            "The command creates the real saved audience and normal notification");
        check(report.Contains(_exile.StringId) && report.Contains(_foreign.StringId) && report.Contains(keep.StringId)
            && report.Contains(visit.Deadline.ToDays.ToString("0.00")) && report.Contains("test save"), "Console output identifies the clan, origin, keep, deadline and save warning");
        var causes = (Dictionary<string, int>)AccessTools.Field(Behavior, "_trackedExileCauseIds").GetValue(_behavior);
        check(causes[_exile.StringId] == (int)ExileCause.Expulsion
            && !(bool)Call(_behavior, "CanReturnToRealm", _exile, _foreign), "The normal leave callback preserves forced-expulsion history and the return ban");
        check(_relations == 0 && _randomDraws == 0, "Preparing an explicit test grants neither relations nor a random reroll");
        _day = 102;
        check(Command(_exile.StringId).Contains("already has") && _departures == 1 && visit.Deadline.ToDays == 107,
            "Repeated tests cannot overwrite a visit or extend its original deadline");
        foreach (string block in new[] { "army", "battle", "siege", "quest", "sea", "transition", "disband", "other-commander" })
        {
            Prepare(true); _block = block;
            check(Command(_exile.StringId).StartsWith("Error:") && _departures == 0 && _enters == 0,
                "Unsafe party prevents test departure and relocation: " + block);
        }
        Prepare(true); report = Command(_exile.StringId);
        check(report.StartsWith("Success!") && _enters == 1 && _teleports == 0 && _party.Ai.IsDisabled,
            "The test preserves an existing party and uses the normal keep-entry hold");
        Prepare(); report = Command();
        check(report.StartsWith("Success!") && _randomDraws == 1 && Visits().Count == 1 && _departures == 1,
            "Random mode selects only an eligible foreign clan and uses the same lifecycle");
        Prepare(); Realms[_exile] = _realm;
        check(Command().Contains("No eligible") && _departures == 0, "No eligible foreign clan produces a clear no-op");
        check(Command(_exile.StringId).Contains("another realm"), "Existing player vassals are excluded");
        Prepare(); _blockDeparture = true; report = Command(_exile.StringId);
        check(report.StartsWith("Error:") && !report.Contains("Success!") && Visits().Count == 0 && _joins == 0,
            "A blocked native departure cannot be reported as a successful asylum test");
        _blockDeparture = false;
    }

    private static void CheckEstate(Action<bool, string> check, LordshipBehavior registry, Settlement parent)
    {
        var village = Blank<Settlement>(); village.StringId = "asylum_village";
        var component = Blank<Village>(); AccessTools.Field(typeof(Settlement), "Village").SetValue(village, component);
        AccessTools.Field(typeof(Village), "_bound").SetValue(component, parent);
        check((bool)Call(registry, "CanOfferAsylumGrant", _player, village, _guest), "An independent landless petitioner may be promised a vacant village");
        check(!(bool)Call(registry, "CanGrantVassal", _player, village, _guest), "The promised estate cannot be granted before actual admission");
        Realms[_exile] = _realm;
        check((bool)Call(registry, "CanGrantVassal", _player, village, _guest), "An admitted landless vassal is eligible without becoming a household retainer");
        var args = new object[] { _player, village, _guest, _ruler, null };
        check((bool)Call(registry, "TryGrantVassal", args), "A player ruler can endow the admitted clan leader");
        var grant = registry.GetGrant(_guest);
        check(grant?.IsValid == true && grant.IsVassalGrant && grant.BeneficiaryClan == _exile && parent.OwnerClan == _player,
            "Endowment preserves both clan identity and native parent-barony ownership");
        check(_relations == 10, "The estate contributes only its existing ten-point gift reward");
        Realms[_exile] = Blank<Kingdom>(); check(!grant.IsValid, "The gift ends if the houses part realms");
        Realms[_exile] = _realm; _block = "dead"; check(!grant.IsValid, "The gift is personal and ends on the holder's death");
    }
}
