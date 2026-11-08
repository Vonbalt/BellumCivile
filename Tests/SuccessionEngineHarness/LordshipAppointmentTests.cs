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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class LordshipAppointmentTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) Values[key] = value;
            else if (Values.TryGetValue(key, out var saved)) value = (T)saved;
            else value = default;
            return true;
        }
    }
    private sealed class Manager : ICampaignBehaviorManager
    {
        public T GetBehavior<T>() => _disband is T value ? value : _teleport is T travel ? travel : default;
        public IEnumerable<T> GetBehaviors<T>() => _disband is T value ? new[] { value } : _teleport is T travel ? new[] { travel } : Enumerable.Empty<T>();
        public void AddBehavior(CampaignBehaviorBase b) { }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private sealed class Receiver : CampaignEventReceiver
    {
        public override void CanHeroLeadParty(Hero hero, ref bool result) { if (_nativeDeny) result = false; }
        public override void OnPartyDisbandStarted(MobileParty party)
        { _starts++; Call(typeof(DisbandPartyCampaignBehavior), "OnPartyDisbandStarted", _disband, party); }
        public override void OnCharacterBecameFugitive(Hero hero, bool showNotification) { _fugitives++; }
        public override void OnHeroTeleportationRequested(Hero hero, Settlement settlement, MobileParty party, TeleportHeroAction.TeleportationDetail detail)
        {
            Call(typeof(TeleportationCampaignBehavior), "OnHeroTeleportationRequested", _teleport, hero, settlement, party, detail);
            Call(typeof(DisbandPartyCampaignBehavior), "OnHeroTeleportationRequested", _disband, hero, settlement, party, detail);
        }
    }
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Hero>> Members = new Dictionary<Clan, MBReadOnlyList<Hero>>();
    private static Clan _player, _npc;
    private static Hero _retainer, _candidate, _endowed, _playerHero;
    private static MobileParty _party, _main;
    private static DisbandPartyCampaignBehavior _disband;
    private static TeleportationCampaignBehavior _teleport;
    private static MultiSelectionInquiryData _replacementInquiry;
    private static float _day;
    private static string _unsafe;
    private static bool _nativeDeny, _throwCommand;
    private static int _starts, _fugitives, _asserts, _grantAttempts;
    private static MBReadOnlyList<MobileParty> _allParties = new MBReadOnlyList<MobileParty>();
    private static int _partyScans, _destroyCalls;
    private static bool _destroyedWithoutClan;
    private static int _fundingCalls, _fundingAmount;
    private static readonly List<(string Label, string Value)> TooltipRows = new List<(string, string)>();
    private static Settlement _tooltipVillage;
    private static bool TooltipRow(object[] __args) { TooltipRows.Add(((string)__args[0], (string)__args[1])); return false; }
    private static bool LordshipNoun(ref TextObject __result) { __result = new TextObject("Feodum"); return false; }
    private static bool VillageName(ref TextObject __result) { __result = new TextObject("{=!}Calioc"); return false; }
    private static bool AllParties(ref MBReadOnlyList<MobileParty> __result) { _partyScans++; __result = _allParties; return false; }
    private static bool Elapsed(ref float __result) { __result = 1; return false; }
    private static bool HoursFromNow(float __0, ref CampaignTime __result) { __result = CampaignTime.Days(_day + __0 / 24); return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays < _day; return false; }
    private static bool Delay(ref ExplainedNumber __result) { __result = new ExplainedNumber(1); return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Skill(ref int __result) { __result = 100; return false; }
    private static bool SkillReference(ref SkillObject __result) { __result = null; return false; }
    private static bool PlayerAvailability(Hero hero, ref bool __result, ref TextObject explanation)
    { __result = true; explanation = TextObject.GetEmpty(); return false; }
    private static bool CaptureInquiry(MultiSelectionInquiryData __0) { _replacementInquiry = __0; return false; }
    private static bool Fund(Hero __0, Hero __1, int __2)
    { _fundingCalls++; _fundingAmount += __2; __0.Gold -= __2; __1.Gold += __2; return false; }
    private static bool NoSettlement(ref Settlement __result) { __result = null; return false; }
    private static bool Destroy(MobileParty destroyedParty)
    {
        _destroyCalls++;
        _destroyedWithoutClan |= destroyedParty.ActualClan == null;
        // Exercise native clan-cache finalization, substituting only the engine removal that follows it.
        Call(typeof(WarPartyComponent), "OnFinalize", destroyedParty.LordPartyComponent);
        destroyedParty.IsActive = false;
        Set(destroyedParty, "_actualClan", null);
        return false;
    }
    private static readonly Type Eligibility = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.LordshipEligibility", true);
    private static readonly Type PatchType = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.Patches.LordshipAppointmentPatches", true);
    private static readonly Type Household = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.HouseholdMembership", true);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(Type type, string method, object target, params object[] args) => AccessTools.Method(type, method).Invoke(target, args);
    private static void Set(object target, string name, object value)
    {
        var field = AccessTools.Field(target.GetType(), name) ?? AccessTools.Field(target.GetType(), "<" + name + ">k__BackingField");
        if (field != null) field.SetValue(target, value);
        else AccessTools.PropertySetter(target.GetType(), name).Invoke(target, new[] { value });
    }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainParty(ref MobileParty __result) { __result = _main; return false; }
    private static bool MainHero(ref Hero __result) { __result = _playerHero; return false; }
    private static bool CanLead(Hero hero) => (bool)Call(typeof(Hero), "CanLeadParty", hero);
    private static bool ClanOf(Hero __instance, ref Clan __result) { __result = Houses[__instance]; return false; }
    private static bool ClanMembers(Clan __instance, ref MBReadOnlyList<Hero> __result) { __result = Members[__instance]; return false; }
    private static bool EmptyFiefs(ref MBReadOnlyList<Town> __result) { __result = new MBReadOnlyList<Town>(); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool HeroName(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool ClanName(Clan __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool PartyName(MobileParty __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool DaysFromNow(float __0, ref CampaignTime __result) { __result = CampaignTime.Days(_day + __0); return false; }
    private static bool Battle(MobileParty __instance, ref MapEvent __result) { __result = __instance == _party && _unsafe == "battle" ? Blank<MapEvent>() : null; return false; }
    private static bool Siege(MobileParty __instance, ref SiegeEvent __result) { __result = __instance == _party && _unsafe == "siege" ? Blank<SiegeEvent>() : null; return false; }
    private static bool Army(MobileParty __instance, ref Army __result) { __result = __instance == _party && _unsafe == "army" ? Blank<Army>() : null; return false; }
    private static bool Sea(MobileParty __instance, ref bool __result) { __result = __instance == _party && _unsafe == "sea"; return false; }
    private static bool Assert() { _asserts++; return false; }
    private static bool Grant(ref bool __result) { _grantAttempts++; __result = false; return false; }
    private static bool GrantLookup(Hero hero, ref PersonalLordshipRecord __result)
    { if (hero != _endowed) return true; __result = new PersonalLordshipRecord { Holder = hero, Village = _tooltipVillage }; return false; }
    private static bool CommandProbe(ref bool __result, ref TextObject disabledReason)
    {
        if (_throwCommand) throw new InvalidOperationException("native command probe failed");
        __result = CanLead(_candidate);
        disabledReason = __result ? TextObject.GetEmpty()
            : new TextObject("{=6OZycZbc}Clan doesn't have any available heroes to lead the new party.");
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.lordship-appointments");
        var oldCampaign = Campaign.Current; var oldRegistry = LordshipBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        var hours = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerHour"); var oldHours = hours.GetValue(null);
        void Patch(Type type, string name, string handler, bool getter = true) => h.Patch(
            getter ? AccessTools.PropertyGetter(type, name) : AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), handler));
        Hero Person(Clan clan, string id)
        {
            var hero = Blank<Hero>(); hero.StringId = id; Houses[hero] = clan; Members[clan].Add(hero);
            Set(hero, "_heroState", Hero.CharacterStates.Active); Set(hero, "_children", new MBList<Hero>()); Set(hero, "_exSpouses", new MBList<Hero>());
            var character = Blank<CharacterObject>(); character.StringId = id; Set(character, "_heroObject", hero); Set(hero, "_characterObject", character);
            return hero;
        }
        MobileParty Party(Hero hero, bool military = true)
        {
            var party = Blank<MobileParty>(); party.StringId = hero.StringId + "_party"; party.IsActive = true;
            var component = Blank<LordPartyComponent>(); Set(component, "Owner", hero); Set(component, "_leader", hero); Set(component, "MobileParty", party);
            Set(party, "_partyComponent", component); Set(party, "_actualClan", hero.Clan); Set(party, "IsLordParty", military); Set(party, "IsCaravan", !military);
            var partyBase = Blank<PartyBase>(); Set(partyBase, "MobileParty", party); Set(party, "Party", partyBase);
            Set(partyBase, "MemberRoster", new TroopRoster(partyBase)); Set(partyBase, "PrisonRoster", new TroopRoster(partyBase));
            Set(partyBase, "ItemRoster", new ItemRoster());
            Set(party, "Ai", FormatterServices.GetUninitializedObject(AccessTools.Property(typeof(MobileParty), "Ai").PropertyType));
            party.MemberRoster.AddToCounts(hero.CharacterObject, 1);
            var soldier = Blank<CharacterObject>(); soldier.StringId = "soldier"; party.MemberRoster.AddToCounts(soldier, 12);
            return party;
        }
        try
        {
            GameTexts.Initialize(new GameTextManager());
            var campaign = Blank<Campaign>(); AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            _disband = new DisbandPartyCampaignBehavior(); campaign.AddCampaignBehaviorManager(new Manager());
            var dispatcher = (CampaignEventDispatcher)Activator.CreateInstance(typeof(CampaignEventDispatcher),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new CampaignEventReceiver[] { new Receiver() } }, null);
            Set(campaign, "CampaignEventDispatcher", dispatcher); Set(campaign, "CampaignObjectManager", Blank<CampaignObjectManager>());
            ticks.SetValue(null, 24000L); hours.SetValue(null, 1000L); _day = 10;
            Patch(typeof(CampaignTime), "Now", nameof(Now));
            Patch(typeof(CampaignTime), "DaysFromNow", nameof(DaysFromNow), false);
            Patch(typeof(Clan), "PlayerClan", nameof(Player)); Patch(typeof(MobileParty), "MainParty", nameof(MainParty));
            Patch(typeof(MobileParty), "All", nameof(AllParties));
            Patch(typeof(Hero), "MainHero", nameof(MainHero));
            Patch(typeof(Hero), "Clan", nameof(ClanOf)); Patch(typeof(Clan), "Heroes", nameof(ClanMembers)); Patch(typeof(Clan), "Companions", nameof(ClanMembers));
            Patch(typeof(Clan), "Fiefs", nameof(EmptyFiefs)); Patch(typeof(Hero), "IsChild", nameof(No)); Patch(typeof(Hero), "IsNotable", nameof(No));
            Patch(typeof(Hero), "IsWounded", nameof(No)); Patch(typeof(Hero), "IsSpecial", nameof(No)); Patch(typeof(Hero), "IsTemplate", nameof(No));
            Patch(typeof(Hero), "Name", nameof(HeroName)); Patch(typeof(Clan), "Name", nameof(ClanName));
            Patch(typeof(CampaignObjectManager), "HeroStateChanged", nameof(Skip), false);
            Patch(typeof(MobileParty), "SetMoveModeHold", nameof(Skip), false); Patch(typeof(MobileParty), "RemoveAllPartyRolesOfHero", nameof(Skip), false);
            Patch(typeof(MobileParty), "MapEvent", nameof(Battle)); Patch(typeof(MobileParty), "SiegeEvent", nameof(Siege));
            Patch(typeof(MobileParty), "Army", nameof(Army)); Patch(typeof(MobileParty), "IsCurrentlyAtSea", nameof(Sea));
            Patch(typeof(TaleWorlds.Library.Debug), "FailedAssert", nameof(Assert), false);
            h.Patch(AccessTools.Method(typeof(Hero), "CanLeadParty"), postfix: new HarmonyMethod(PatchType, "CanLead"));
            h.Patch(AccessTools.Method(typeof(MobileParty), "RemovePartyLeader"), postfix: new HarmonyMethod(PatchType, "AppointmentsChanged"));
            Patch(typeof(LordshipBehavior), "EnsureAppointmentGrant", nameof(Grant), false);
            h.Patch(AccessTools.Method(typeof(LordshipBehavior), "GetGrant", new[] { typeof(Hero) }),
                prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), nameof(GrantLookup)));
            Patch(typeof(InformationManager), "DisplayMessage", nameof(Skip), false);
            _main = Blank<MobileParty>();
            _player = Blank<Clan>(); _player.StringId = "player"; Members[_player] = new MBReadOnlyList<Hero>();
            _npc = Blank<Clan>(); _npc.StringId = "npc"; Members[_npc] = new MBReadOnlyList<Hero>();
            var leader = Person(_player, "leader"); _playerHero = leader; Set(_player, "_leader", leader);
            var npcLeader = Person(_npc, "npc_leader"); Set(_npc, "_leader", npcLeader);
            _retainer = Person(_player, "retainer"); var family = Person(_player, "family"); family.Father = leader;
            var npcRetainer = Person(_npc, "npc_retainer");
            Call(Household, "Invalidate", null, new object[] { null });
            var registry = new LordshipBehavior(); Set(registry, "_campaign", campaign); Set(registry, "_ready", true);
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
            var appointments = (List<LordshipAppointmentRecord>)AccessTools.Field(typeof(LordshipBehavior), "_appointments").GetValue(registry);

            check(CanLead(_retainer) && CanLead(npcRetainer), "Unendowed retainers retain native eligibility outside military/governor selection");
            AccessTools.Field(Eligibility, "CheckingClanCommand").SetValue(null, true);
            check(!CanLead(_retainer) && CanLead(family), "Military creation checks require retainers' grants without restricting family");
            _endowed = _retainer;
            check(CanLead(_retainer), "Receiving an endowment restores military eligibility without changing native checks");
            _endowed = null;
            AccessTools.Field(Eligibility, "ListingCandidates").SetValue(null, true);
            check(CanLead(_retainer), "Candidate enumeration preserves unendowed choices so their disabled reason can be shown");
            AccessTools.Field(Eligibility, "ListingCandidates").SetValue(null, false);
            AccessTools.Field(Eligibility, "CheckingClanCommand").SetValue(null, false);
            AccessTools.Field(Eligibility, "SelectingClan").SetValue(null, _npc);
            check(!CanLead(npcRetainer), "NPC military selection cannot use an unendowed retainer without an available grant");
            AccessTools.Field(Eligibility, "SelectingClan").SetValue(null, null);
            _nativeDeny = true;
            check(!CanLead(_retainer), "Commercial exemption never overrides another native/mod eligibility denial"); _nativeDeny = false;
            _main = Party(leader); _main.MemberRoster.AddToCounts(_retainer.CharacterObject, 1);
            var caravans = new CaravanConversationsCampaignBehavior();
            var suitable = (List<CharacterObject>)Call(typeof(CaravanConversationsCampaignBehavior), "FindSuitableCompanionsToLeadCaravan", caravans);
            check(suitable.Contains(_retainer.CharacterObject) && !suitable.Contains(leader.CharacterObject),
                "Vanilla's shared caravan/convoy selector includes the unendowed companion and excludes the player");
            _nativeDeny = true;
            suitable = (List<CharacterObject>)Call(typeof(CaravanConversationsCampaignBehavior), "FindSuitableCompanionsToLeadCaravan", caravans);
            check(!suitable.Contains(_retainer.CharacterObject), "The real commercial selector still respects native eligibility denials");
            _nativeDeny = false; _main.MemberRoster.RemoveTroop(_retainer.CharacterObject);

            var vm = Blank<ClanPartiesVM>(); _candidate = _retainer;
            var command = AccessTools.Method(typeof(ClanPartiesVM), "GetCanCreateNewParty");
            h.Patch(command, prefix: new HarmonyMethod(PatchType, "EnterCommandCheck"), postfix: new HarmonyMethod(PatchType, "CreatePartyReason"),
                finalizer: new HarmonyMethod(PatchType, "ExitCommandCheck"));
            h.Patch(command, prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), nameof(CommandProbe)));
            var commandArgs = new object[] { null };
            check(!(bool)command.Invoke(vm, commandArgs) && CanLead(_retainer),
                "The real clan create-button hook applies military eligibility only inside its own check");
            var leaderReason = (TextObject)commandArgs[0];
            check(leaderReason.GetID() == "BC_Lordship_NoPartyLeader" && leaderReason.ToString()
                == "Your clan has no available family members or retainers with a lordship grant or active indenture to lead a new party.",
                "The disabled create-party button explicitly explains family and endowed-retainer eligibility");
            _candidate = family;
            check((bool)command.Invoke(vm, commandArgs) && ((TextObject)commandArgs[0]).IsEmpty(),
                "An eligible family member keeps the create-party button enabled without a disabled hint");
            _candidate = _retainer;
            foreach (var id in new[] { "VSUqbvbE", "party_slots", "sailing", "campaign_action", "foreign_mod_reason" })
            {
                var reason = new TextObject("{=" + id + "}Original disabled reason");
                var hintArgs = new object[] { false, reason };
                Call(PatchType, "CreatePartyReason", null, hintArgs);
                check(ReferenceEquals(hintArgs[1], reason), "The hint preserves other native/mod restrictions: " + id);
            }
            var originalReason = new TextObject("{=6OZycZbc}Translated no-leader message");
            var reasonArgs = new object[] { false, originalReason };
            Call(PatchType, "CreatePartyReason", null, reasonArgs);
            check(((TextObject)reasonArgs[1]).GetID() == "BC_Lordship_NoPartyLeader",
                "No-leader recognition uses the native localization key, not English text");
            Set(registry, "_ready", false); reasonArgs[1] = originalReason;
            Call(PatchType, "CreatePartyReason", null, reasonArgs);
            check(ReferenceEquals(reasonArgs[1], originalReason), "Uninitialized lordship rules leave the native hint untouched");
            Set(registry, "_ready", true); reasonArgs[0] = true;
            Call(PatchType, "CreatePartyReason", null, reasonArgs);
            check(ReferenceEquals(reasonArgs[1], originalReason), "Successful checks never rewrite a disabled reason");
            _throwCommand = true;
            try { command.Invoke(vm, new object[] { null }); } catch (TargetInvocationException ex) { check(ex.InnerException is InvalidOperationException, "Native UI exceptions propagate normally"); }
            _throwCommand = false;
            check(CanLead(_retainer) && !(bool)AccessTools.Field(Eligibility, "CheckingClanCommand").GetValue(null),
                "An exceptional military check cannot leak its restriction into later caravan or convoy checks");

            _party = Party(_retainer);
            var itemType = typeof(ClanPartyItemVM);
            if (itemType.IsAbstract) itemType = itemType.Assembly.GetTypes().First(t => !t.IsAbstract
                && typeof(ClanPartyItemVM).IsAssignableFrom(t) && t.Name == "ClanPartyItemWithPartyVM");
            var item = (ClanPartyItemVM)FormatterServices.GetUninitializedObject(itemType);
            Set(item, "Party", _party.Party); Set(vm, "_currentSelectedParty", item);
            check(!(bool)Call(PatchType, "PlayerLeaderChange", null, vm, _retainer), "Changing a military party's leader still enforces the grant");
            check((bool)Call(PatchType, "PlayerLeaderChange", null, vm, null), "Disbanding a party is never blocked by the grant requirement");
            var card = new ClanCardSelectionItemInfo(_retainer, new TextObject("Retainer"), null, CardSelectionItemSpriteType.None,
                null, null, Enumerable.Empty<ClanCardSelectionItemPropertyInfo>(), false, TextObject.GetEmpty(), null);
            IEnumerable<ClanCardSelectionItemInfo> cards = new[] { card };
            var args = new object[] { vm, cards }; Call(PatchType, "ChangeLeaderCandidates", null, args);
            check(((IEnumerable<ClanCardSelectionItemInfo>)args[1]).Single().IsDisabled, "Military leader cards show the endowment requirement");
            Set(_party, "IsLordParty", false); Set(_party, "IsCaravan", true);
            check((bool)Call(PatchType, "PlayerLeaderChange", null, vm, _retainer), "Caravan and convoy leader changes keep native permission");
            args[1] = cards; Call(PatchType, "ChangeLeaderCandidates", null, args);
            check(ReferenceEquals(args[1], cards) && !cards.Single().IsDisabled, "Commercial leader cards are not disabled or rewritten by lordships");
            check(!(bool)Call(PatchType, "BeforeGovernor", null, null, _retainer)
                && (bool)Call(PatchType, "BeforeGovernor", null, null, family),
                "Governorship still requires a retainer endowment and leaves family eligibility intact");
            _endowed = _retainer;
            check((bool)Call(PatchType, "BeforeGovernor", null, null, _retainer), "Endowed retainers can still accept governorship");
            _endowed = null;
            var npcParty = Party(npcRetainer, false); _grantAttempts = 0;
            Call(typeof(LordshipBehavior), "CheckAppointment", registry, npcRetainer);
            check(_grantAttempts == 0 && appointments.Count == 0, "An NPC merchant leader neither consumes a lordship nor queues retirement");

            // The original disbandment regressions deliberately have no available replacement.
            Set(family, "_heroState", Hero.CharacterStates.Disabled);
            Set(npcLeader, "_heroState", Hero.CharacterStates.Disabled);
            Patch(typeof(Hero), "IsNoncombatant", nameof(No));

            void Reset()
            {
                Set(_retainer, "_partyBelongedTo", null); Set(_retainer, "_heroState", Hero.CharacterStates.Active);
                _unsafe = null; _starts = _fugitives = _asserts = 0; _disband = new DisbandPartyCampaignBehavior(); appointments.Clear();
                _party = Party(_retainer);
                _allParties = new MBReadOnlyList<MobileParty>(new List<MobileParty> { _party });
                AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairCandidates").SetValue(registry, null);
            }
            Reset();
            // Reproduce the old path with native actions, before invoking Bellum's focused repair.
            DisbandPartyAction.StartDisband(_party);
            check(_retainer.IsActive && _retainer.PartyBelongedTo == _party && _party.LeaderHero == null
                && _party.MemberRoster.Contains(_retainer.CharacterObject) && _disband.IsPartyWaitingForDisband(_party),
                "Native StartDisband alone reproduces the reported active hero stranded in a leaderless party");
            Call(typeof(LordshipBehavior), "RepairRetiredCommanders", registry);
            check(_retainer.IsFugitive && _retainer.PartyBelongedTo == null && !_party.MemberRoster.Contains(_retainer.CharacterObject)
                && _party.MemberRoster.TotalManCount == 12 && Members[_player].Contains(_retainer),
                "The load repair releases only the stranded hero while preserving clan membership and the party's soldiers");
            Call(typeof(LordshipBehavior), "RepairRetiredCommanders", registry);
            check(_fugitives == 1 && _starts == 1, "Repeating the repair cannot release or disband the same hero twice");

            foreach (var block in new[] { "battle", "siege", "army", "sea" })
            {
                Reset(); _day = 14;
                var record = new LordshipAppointmentRecord { Hero = _retainer, Party = _party, OwningClan = _player,
                    PlayerTransition = true, Deadline = 15 };
                appointments.Add(record); Call(typeof(LordshipBehavior), "Maintain", registry);
                check(_retainer.IsActive && _party.LeaderHero == _retainer && _starts == 0, "The five-day grace period preserves the existing command");
                _day = 15; _unsafe = block; Call(typeof(LordshipBehavior), "Maintain", registry);
                check(_retainer.IsActive && appointments.Contains(record) && _starts == 0, "Expired retirement waits for a safe context: " + block);
                _unsafe = null; Call(typeof(LordshipBehavior), "Maintain", registry);
                check(_retainer.IsFugitive && _retainer.PartyBelongedTo == null && _party.LeaderHero == null
                    && _party.MemberRoster.TotalManCount == 12 && _starts == 1 && _asserts == 0 && appointments.Count == 0,
                    "Expiry uses native hero release before troop disband, without destroying or stranding the commander");
            }
            Reset(); _endowed = _retainer;
            appointments.Add(new LordshipAppointmentRecord { Hero = _retainer, Party = _party, OwningClan = _player,
                PlayerTransition = true, Deadline = 0 });
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_retainer.IsActive && _party.LeaderHero == _retainer && _starts == 0 && appointments.Count == 0,
                "A grant received before enforcement preserves command after the old deadline expires");
            _endowed = null;
            Reset(); var commercialRecord = new LordshipAppointmentRecord { Hero = _retainer, Party = _party,
                OwningClan = _player, Retirement = true };
            appointments.Add(commercialRecord); Set(_party, "IsLordParty", false); Set(_party, "IsCaravan", true);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == _retainer && _retainer.IsActive && _starts == 0 && appointments.Count == 0,
                "A stale military appointment cannot retire a party that has become a caravan or convoy");

            foreach (string excluded in new[] { "normal", "passenger", "family", "commercial", "foreign", "endowed", "missing_roster" })
            {
                Reset();
                if (excluded != "normal") DisbandPartyAction.StartDisband(_party);
                if (excluded == "passenger") Set(_party.LordPartyComponent, "Owner", leader);
                if (excluded == "family") { _retainer.Father = leader; Call(Household, "Invalidate", null, new object[] { null }); }
                if (excluded == "commercial") { Set(_party, "IsLordParty", false); Set(_party, "IsCaravan", true); }
                if (excluded == "foreign") Set(_party, "_actualClan", _npc);
                if (excluded == "endowed") _endowed = _retainer;
                if (excluded == "missing_roster") { _party.MemberRoster.RemoveTroop(_retainer.CharacterObject); Set(_retainer, "_partyBelongedTo", _party); }
                int released = _fugitives; Call(typeof(LordshipBehavior), "RepairRetiredCommanders", registry);
                check(_fugitives == released, "The load repair leaves unrelated hero/party states alone: " + excluded);
                _retainer.Father = null; Call(Household, "Invalidate", null, new object[] { null });
                _endowed = null;
            }
            Reset(); DisbandPartyAction.StartDisband(_party); _unsafe = "battle";
            Set(registry, "_retirementRepairPending", true); Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_retainer.IsActive && (bool)AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairPending").GetValue(registry),
                "The one-off repair remains pending if the affected party is temporarily unsafe");
            _unsafe = null; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_retainer.IsFugitive && !(bool)AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairPending").GetValue(registry),
                "The deferred load repair completes safely and stops scanning afterward");

            void ResetNpc()
            {
                Set(npcRetainer, "_partyBelongedTo", null); Set(npcRetainer, "_heroState", Hero.CharacterStates.Active);
                npcRetainer.Father = null; Call(Household, "Invalidate", null, new object[] { null });
                _unsafe = null; _endowed = null; _starts = _fugitives = _asserts = _partyScans = 0;
                _disband = new DisbandPartyCampaignBehavior(); appointments.Clear();
                _party = Party(npcRetainer);
                Set(_npc, "_warPartyComponentsCache", new MBList<WarPartyComponent> { _party.LordPartyComponent });
                _allParties = new MBReadOnlyList<MobileParty>(new List<MobileParty> { _party });
                AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairCandidates").SetValue(registry, null);
            }
            ResetNpc();
            appointments.Add(new LordshipAppointmentRecord { Hero = npcRetainer, Party = _party,
                OwningClan = _npc, Retirement = true, Deadline = 0 });
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == null && npcRetainer.IsFugitive && npcRetainer.PartyBelongedTo == null
                && _party.MemberRoster.TotalManCount == 12 && Members[_npc].Contains(npcRetainer)
                && _disband.IsPartyWaitingForDisband(_party) && _starts == 1 && _asserts == 0,
                "NPC retirement releases its commander before native troop disbandment, retaining the hero and soldiers");
            h.Patch(AccessTools.Method(typeof(DestroyPartyAction), "ApplyInternal"),
                prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), nameof(Destroy)));
            h.Patch(AccessTools.Method(typeof(MobilePartyHelper), "GetCurrentSettlementOfMobilePartyForAICalculation"),
                prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), nameof(NoSettlement)));
            Patch(typeof(CampaignTime), "ElapsedDaysUntilNow", nameof(Elapsed));
            Patch(typeof(CharacterObject), "IsPlayerCharacter", nameof(No));
            _party.IsDisbanding = true; _destroyCalls = 0; _destroyedWithoutClan = false;
            Call(typeof(DisbandPartyCampaignBehavior), "CheckDisbandedPartyDaily", _disband, _party, null);
            check(_destroyCalls == 1 && !_destroyedWithoutClan && _npc.WarPartyComponents.Count == 0,
                "Native destinationless daily cleanup destroys the retired NPC party once and finalizes its clan cache safely");

            ResetNpc();
            foreach (var troop in _party.MemberRoster.GetTroopRoster().Where(t => !t.Character.IsHero).ToList())
                _party.MemberRoster.RemoveTroop(troop.Character, troop.Number);
            _destroyCalls = 0; _destroyedWithoutClan = false;
            appointments.Add(new LordshipAppointmentRecord { Hero = npcRetainer, Party = _party, OwningClan = _npc, Retirement = true });
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(npcRetainer.IsFugitive && Members[_npc].Contains(npcRetainer) && _destroyCalls == 1
                && !_destroyedWithoutClan && !_party.IsActive && appointments.Count == 0,
                "Retiring a commander-only NPC party safely destroys the empty party once");

            ResetNpc(); DisbandPartyAction.StartDisband(_party); _grantAttempts = 0;
            Call(typeof(LordshipBehavior), "CheckAppointment", registry, npcRetainer);
            check(_grantAttempts == 0 && appointments.Count == 0,
                "An already disbanding NPC command is not mistaken for a fresh appointment needing an automatic grant");
            Call(typeof(DisbandPartyCampaignBehavior), "OnPartyDisbandCanceled", _disband, _party);
            _party.IsDisbanding = true;
            Call(typeof(LordshipBehavior), "RepairRetiredCommanders", registry);
            check(_party.LeaderHero == null && npcRetainer.IsFugitive && _party.IsDisbanding && _starts == 1,
                "The load repair also handles the active disband phase after native waiting has expired");

            ResetNpc(); DisbandPartyAction.StartDisband(_party);
            check(_party.LeaderHero == npcRetainer && npcRetainer.PartyBelongedTo == _party,
                "Native NPC disbandment reproduces the old attached-commander save state");
            _unsafe = "battle";
            Set(registry, "_retirementRepairPending", true); Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == npcRetainer && _partyScans == 1
                && (bool)AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairPending").GetValue(registry),
                "The one-off NPC repair records an unsafe candidate without removing its commander");
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_partyScans == 1, "Deferred repair rechecks its candidate list without rescanning every campaign party");
            _unsafe = null; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == null && npcRetainer.IsFugitive && npcRetainer.PartyBelongedTo == null
                && _party.MemberRoster.TotalManCount == 12 && _partyScans == 1 && _starts == 1
                && !(bool)AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairPending").GetValue(registry),
                "Affected NPC save repair releases the commander once without restarting disbandment");
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_fugitives == 1 && _partyScans == 1, "Completed NPC repair stops running on later hourly maintenance");

            foreach (string excluded in new[] { "normal", "family", "commercial", "foreign", "endowed", "legacy", "other_owner", "missing_roster", "inactive" })
            {
                ResetNpc();
                if (excluded != "normal") DisbandPartyAction.StartDisband(_party);
                if (excluded == "family") { npcRetainer.Father = npcLeader; Call(Household, "Invalidate", null, new object[] { null }); }
                if (excluded == "commercial") { Set(_party, "IsLordParty", false); Set(_party, "IsCaravan", true); }
                if (excluded == "foreign") Set(_party, "_actualClan", _player);
                if (excluded == "endowed") _endowed = npcRetainer;
                if (excluded == "legacy") appointments.Add(new LordshipAppointmentRecord { Hero = npcRetainer, Party = _party, OwningClan = _npc });
                if (excluded == "other_owner") Set(_party.LordPartyComponent, "Owner", npcLeader);
                if (excluded == "missing_roster") { _party.MemberRoster.RemoveTroop(npcRetainer.CharacterObject); Set(npcRetainer, "_partyBelongedTo", _party); }
                if (excluded == "inactive") _party.IsActive = false;
                Call(typeof(LordshipBehavior), "RepairRetiredCommanders", registry);
                check(npcRetainer.IsActive && _fugitives == 0, "NPC save repair leaves unrelated or protected commands alone: " + excluded);
            }
            ResetNpc(); DisbandPartyAction.StartDisband(_party); _unsafe = "battle";
            Set(registry, "_retirementRepairPending", true); Call(typeof(LordshipBehavior), "Maintain", registry);
            _endowed = npcRetainer; _unsafe = null; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(npcRetainer.IsActive && _fugitives == 0 && _partyScans == 1
                && !(bool)AccessTools.Field(typeof(LordshipBehavior), "_retirementRepairPending").GetValue(registry),
                "A deferred NPC repair revalidates an endowment received before it became safe to release the commander");

            // Keep native transfer actions, saved travel records, arrival dispatch, ownership and rosters intact.
            var models = Blank<GameModels>(); Set(campaign, "_gameModels", models);
            Set(models, "DelayedTeleportationModel", new DefaultDelayedTeleportationModel());
            Set(models, "ClanFinanceModel", Blank<DefaultClanFinanceModel>());
            Patch(typeof(DefaultDelayedTeleportationModel), "GetTeleportationDelayAsHours", nameof(Delay), false);
            Patch(typeof(DefaultDelayedTeleportationModel), "CanPerformImmediateTeleport", nameof(Yes), false);
            Patch(typeof(CampaignTime), "HoursFromNow", nameof(HoursFromNow), false);
            Patch(typeof(CampaignTime), "IsPast", nameof(Past));
            Patch(typeof(Hero), "GetSkillValue", nameof(Skill), false);
            foreach (string skill in new[] { "Tactics", "Leadership", "Scouting", "Steward" })
                Patch(typeof(DefaultSkills), skill, nameof(SkillReference));
            Patch(typeof(PartyBase), "SetVisualAsDirty", nameof(Skip), false);
            Patch(typeof(MobileParty), "Name", nameof(PartyName));
            Patch(typeof(Settlement), "IsUnderRaid", nameof(No));
            Patch(typeof(Settlement), "IsUnderSiege", nameof(No));
            Patch(typeof(MBInformationManager), "ShowMultiSelectionInquiry", nameof(CaptureInquiry), false);
            Patch(typeof(FactionHelper), "IsMainClanMemberAvailableForPartyLeaderChange", nameof(PlayerAvailability), false);
            Patch(typeof(GiveGoldAction), "ApplyBetweenCharacters", nameof(Fund), false);
            h.Patch(AccessTools.Method(typeof(TeleportHeroAction), "ApplyImmediateTeleportToPartyAsPartyLeader"),
                prefix: new HarmonyMethod(PatchType, "ReplacementArrival"));
            h.Patch(AccessTools.Method(typeof(DisbandPartyCampaignBehavior), "OnPartyDisbandStarted"),
                transpiler: new HarmonyMethod(PatchType, "RetirementDisbandCode"));
            var transfers = (List<LordshipCommandTransferRecord>)AccessTools.Field(typeof(LordshipBehavior), "_commandTransfers").GetValue(registry);
            var replacementHero = Person(_npc, "npc_replacement"); replacementHero.Father = npcLeader;
            Call(Household, "Invalidate", null, new object[] { null });
            void ReplacementReset(bool player = false)
            {
                if (player) Reset(); else ResetNpc();
                transfers.Clear(); _teleport = new TeleportationCampaignBehavior(); _replacementInquiry = null;
                _day = 30; _grantAttempts = 0; _endowed = null;
                Set(registry, "_retirementRepairPending", false); Set(registry, "_replacementInquiryOpen", false);
                Set(registry, "_replacementPromptPending", false);
                Set(replacementHero, "_heroState", Hero.CharacterStates.Active); Set(replacementHero, "_partyBelongedTo", null);
                Set(family, "_heroState", player ? Hero.CharacterStates.Active : Hero.CharacterStates.Disabled);
                Set(family, "_partyBelongedTo", null);
                var commander = player ? _retainer : npcRetainer;
                appointments.Add(new LordshipAppointmentRecord { Hero = commander, Party = _party, OwningClan = commander.Clan, Retirement = true });
            }
            void Arrive()
            {
                _day += 1; Call(typeof(TeleportationCampaignBehavior), "HourlyTick", _teleport);
                Call(typeof(LordshipBehavior), "Maintain", registry);
            }
            ReplacementReset();
            var troops = _party.MemberRoster; var prisoners = _party.PrisonRoster; var items = _party.ItemRoster;
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(npcRetainer.IsFugitive && _party.LeaderHero == null && replacementHero.IsTraveling
                && transfers.Count == 1 && appointments.Count == 0 && _starts == 0,
                "NPC retirement sends an eligible family replacement through native travel instead of disbanding");
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(transfers.Count == 1 && _starts == 0, "A travelling replacement is reserved without duplicate transfers or disbandment");
            Arrive();
            check(_party.LeaderHero == replacementHero && _party.Owner == replacementHero && replacementHero.PartyBelongedTo == _party
                && _party.ActualClan == _npc && _party.MemberRoster.TotalManCount == 13 && npcRetainer.PartyBelongedTo == null
                && ReferenceEquals(troops, _party.MemberRoster) && ReferenceEquals(prisoners, _party.PrisonRoster)
                && ReferenceEquals(items, _party.ItemRoster) && transfers.Count == 0 && _starts == 0,
                "Native arrival preserves the party, troops, prisoners and inventory while changing its leader and owner");

            ReplacementReset(); replacementHero.Father = null; Call(Household, "Invalidate", null, new object[] { null });
            _endowed = replacementHero; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(replacementHero.IsTraveling && _grantAttempts == 0, "An already endowed NPC retainer can replace the outgoing commander without a new grant");
            _endowed = null; Arrive();
            check(_party.LeaderHero == null && replacementHero.IsFugitive && _starts == 1 && transfers.Count == 0,
                "A retainer who loses their grant in transit is rejected on arrival and falls back to safe disbandment");

            replacementHero.Father = npcLeader; Call(Household, "Invalidate", null, new object[] { null });
            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            Houses[replacementHero] = _player; Arrive();
            check(_party.ActualClan == _npc && _party.LeaderHero == null && replacementHero.IsFugitive && _starts == 1,
                "A replacement who changes clans while travelling cannot take ownership of the former clan's party");
            Houses[replacementHero] = _npc; Call(Household, "Invalidate", null, new object[] { null });

            ReplacementReset(); Set(replacementHero, "_heroState", Hero.CharacterStates.Disabled);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_starts == 1 && transfers.Count == 0, "No available replacement retains the existing safe disband fallback");

            ReplacementReset(player: true);
            Set(family, "Gold", 10000);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_retainer.IsActive && _party.LeaderHero == _retainer && _starts == 0 && transfers.Count == 0,
                "The player's existing commander remains in place until the player chooses a replacement");
            check((bool)Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry)
                && _replacementInquiry.InquiryElements.Count == 1 && _replacementInquiry.InquiryElements[0].Identifier == family,
                "The player is offered only eligible family members or endowed retainers");
            _replacementInquiry.AffirmativeAction(new List<InquiryElement> { _replacementInquiry.InquiryElements[0] });
            check(family.IsTraveling && _retainer.IsFugitive && _starts == 0, "Selecting a player-clan replacement starts native travel without disbanding");
            Arrive();
            check(_party.LeaderHero == family && transfers.Count == 0 && _starts == 0,
                "The selected player-clan commander takes over the existing party");

            ReplacementReset(player: true); family.Gold = 0; _playerHero.Gold = 100000;
            _fundingCalls = _fundingAmount = 0;
            Call(typeof(LordshipBehavior), "Maintain", registry);
            Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry);
            _replacementInquiry.AffirmativeAction(new List<InquiryElement> { _replacementInquiry.InquiryElements[0] });
            Call(typeof(LordshipBehavior), "Maintain", registry); Arrive();
            int expectedFunding = models.ClanFinanceModel.PartyGoldLowerThreshold;
            check(_fundingCalls == 1 && _fundingAmount == expectedFunding && family.Gold == expectedFunding
                && _playerHero.Gold == 100000 - expectedFunding,
                "Native-style player funding charges the commander shortfall exactly once, not again during travel or arrival");

            ReplacementReset(player: true); Call(typeof(LordshipBehavior), "Maintain", registry);
            Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry);
            _replacementInquiry.NegativeAction(new List<InquiryElement>());
            check(_retainer.IsFugitive && _starts == 1 && appointments.Count == 0,
                "The player can explicitly choose safe disbandment instead of appointing a replacement");

            ReplacementReset(player: true); Call(typeof(LordshipBehavior), "Maintain", registry);
            Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry);
            Set(family, "_heroState", Hero.CharacterStates.Disabled);
            _replacementInquiry.AffirmativeAction(new List<InquiryElement> { _replacementInquiry.InquiryElements[0] });
            check(_party.LeaderHero == _retainer && _retainer.IsActive && _starts == 0,
                "An unavailable selection is revalidated before the outgoing commander is removed");

            ReplacementReset(player: true); Call(typeof(LordshipBehavior), "Maintain", registry);
            Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry);
            _endowed = _retainer;
            _replacementInquiry.AffirmativeAction(new List<InquiryElement> { _replacementInquiry.InquiryElements[0] });
            check(_party.LeaderHero == _retainer && !family.IsTraveling && _starts == 0,
                "Restoring the outgoing retainer's grant while the choice is open invalidates the retirement safely");

            ReplacementReset();
            _party.MemberRoster.AddToCounts(replacementHero.CharacterObject, 1);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == replacementHero && !replacementHero.IsTraveling && _starts == 0,
                "A qualified replacement already in the party takes command immediately without a travel gap");

            ReplacementReset();
            var settlement = Blank<Settlement>(); Set(replacementHero, "_stayingInSettlement", settlement);
            replacementHero.Father = null; Call(Household, "Invalidate", null, new object[] { null });
            for (int i = 0; i < 10; i++) _party.MemberRoster.AddToCounts(Blank<CharacterObject>(), 1);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_starts == 1 && !replacementHero.IsTraveling && _disband.IsPartyWaitingForDisband(_party),
                "Bellum's fallback cannot invoke vanilla's ten-roster-entry rescue to appoint an unendowed retainer");
            check((bool)Call(PatchType, "NativeDisbandCommander", null, replacementHero, _party),
                "The scoped disband guard is restored after Bellum's action and leaves unrelated native decisions alone");
            Set(replacementHero, "_stayingInSettlement", null);
            replacementHero.Father = npcLeader; Call(Household, "Invalidate", null, new object[] { null });

            foreach (string restriction in new[] { "governor", "other_party", "prisoner", "travelling", "native_denial", "unendowed" })
            {
                ReplacementReset();
                if (restriction == "governor") Set(replacementHero, "GovernorOf", Blank<Town>());
                if (restriction == "other_party") Party(replacementHero);
                if (restriction == "prisoner") Set(replacementHero, "_heroState", Hero.CharacterStates.Prisoner);
                if (restriction == "travelling") Set(replacementHero, "_heroState", Hero.CharacterStates.Traveling);
                if (restriction == "native_denial") _nativeDeny = true;
                if (restriction == "unendowed") { replacementHero.Father = null; Call(Household, "Invalidate", null, new object[] { null }); }
                Call(typeof(LordshipBehavior), "Maintain", registry);
                check(_starts == 1 && transfers.Count == 0, "Replacement selection respects unavailable commanders: " + restriction);
                _nativeDeny = false; Set(replacementHero, "GovernorOf", null);
                replacementHero.Father = npcLeader; Call(Household, "Invalidate", null, new object[] { null });
            }

            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            var alternate = Person(_npc, "alternate"); alternate.Father = npcLeader;
            Call(Household, "Invalidate", null, new object[] { null });
            Set(replacementHero, "_heroState", Hero.CharacterStates.Dead);
            Call(typeof(TeleportationCampaignBehavior), "OnHeroKilled", _teleport, replacementHero, null,
                KillCharacterAction.KillCharacterActionDetail.DiedInBattle, false);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(alternate.IsTraveling && _party.LeaderHero == null && !_disband.IsPartyWaitingForDisband(_party)
                && !_party.IsDisbanding, "A canceled journey selects another available NPC commander and clears native disband waiting");
            Arrive();
            check(_party.LeaderHero == alternate && transfers.Count == 0,
                "The replacement for a deceased incoming commander safely completes the handover");
            Set(alternate, "_heroState", Hero.CharacterStates.Disabled); Set(alternate, "_partyBelongedTo", null);

            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            Set(alternate, "_heroState", Hero.CharacterStates.Active);
            TeleportHeroAction.ApplyDelayedTeleportToPartyAsPartyLeader(alternate, _party);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(transfers.Count == 0 && alternate.IsTraveling && replacementHero.IsFugitive,
                "A later native replacement order takes precedence without being overwritten by Bellum's retry");
            Arrive();
            check(_party.LeaderHero == alternate, "A superseding native appointment reaches its intended party");
            Set(alternate, "_heroState", Hero.CharacterStates.Disabled); Set(alternate, "_partyBelongedTo", null);

            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            Set(alternate, "_heroState", Hero.CharacterStates.Active);
            _party.MemberRoster.AddToCounts(alternate.CharacterObject, 1); _party.ChangePartyLeader(alternate);
            Arrive();
            check(_party.LeaderHero == alternate && replacementHero.IsFugitive && _starts == 0,
                "An incoming commander cannot overwrite a different leader appointed before arrival");
            Set(alternate, "_heroState", Hero.CharacterStates.Disabled); Set(alternate, "_partyBelongedTo", null);

            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            _party.IsActive = false;
            Call(typeof(TeleportationCampaignBehavior), "OnMobilePartyDestroyed", _teleport, _party, null);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(replacementHero.IsFugitive && transfers.Count == 0 && _starts == 0,
                "Destruction of the destination cancels its saved journey without touching another party");

            ReplacementReset(); Call(typeof(LordshipBehavior), "Maintain", registry);
            var store = new Store { IsSaving = true }; registry.SyncData(store); _teleport.SyncData(store);
            var loaded = new LordshipBehavior(); store.IsSaving = false; loaded.SyncData(store);
            _teleport = new TeleportationCampaignBehavior(); _teleport.SyncData(store);
            Set(loaded, "_ready", true); Set(loaded, "_campaign", campaign);
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { loaded });
            Call(typeof(LordshipBehavior), "Maintain", loaded);
            check(replacementHero.IsTraveling && _starts == 0 && _party.LeaderHero == null,
                "Saved handovers resume the native journey without creating a second order or disbanding the party");
            _day += 1; Call(typeof(TeleportationCampaignBehavior), "HourlyTick", _teleport);
            Call(typeof(LordshipBehavior), "Maintain", loaded);
            check(_party.LeaderHero == replacementHero && _starts == 0,
                "A replacement reaches the same party after the Bellum and native transfer records reload");
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
            var oldSave = new LordshipBehavior(); oldSave.SyncData(new Store());
            check(AccessTools.Field(typeof(LordshipBehavior), "_commandTransfers").GetValue(oldSave) is List<LordshipCommandTransferRecord> oldTransfers
                && oldTransfers.Count == 0, "Saves from before handovers initialize an empty transfer list");

            var indentures = (List<RetainerIndentureRecord>)AccessTools.Field(typeof(LordshipBehavior), "_indentures").GetValue(registry);
            RetainerIndentureRecord Contract(Hero holder, double end)
            {
                var contract = new RetainerIndentureRecord { Holder = holder, Employer = _player, EndDay = end };
                indentures.Add(contract); Call(typeof(LordshipBehavior), "ReindexIndentures", registry); return contract;
            }
            void IndentureReset()
            {
                Reset(); _day = 50; _endowed = null; transfers.Clear(); indentures.Clear();
                Call(typeof(LordshipBehavior), "ReindexIndentures", registry);
                _teleport = new TeleportationCampaignBehavior();
                Set(registry, "_retirementRepairPending", false);
                Set(registry, "_replacementInquiryOpen", false);
                Set(family, "_heroState", Hero.CharacterStates.Disabled);
            }
            var finance = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.Patches.LordshipSurplusPatch");
            bool NativeSurplus() => (bool)Call(finance, "Prefix", null, _party, _player, 123);
            IndentureReset(); var activeContract = Contract(_retainer, 134);
            AccessTools.Field(Eligibility, "CheckingClanCommand").SetValue(null, true);
            check(CanLead(_retainer), "An indentured, landless companion passes the actual military eligibility hook");
            AccessTools.Field(Eligibility, "CheckingClanCommand").SetValue(null, false);
            check((bool)Call(PatchType, "BeforeGovernor", null, null, _retainer)
                && (bool)Call(PatchType, "PlayerCommand", null, _retainer),
                "An active indenture satisfies both command and governor guards without a land grant");
            check(!NativeSurplus(), "An indentured captain keeps party surplus under the existing native-finance hook");
            appointments.Add(new LordshipAppointmentRecord { Hero = _retainer, Party = _party, OwningClan = _player, Retirement = true });
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == _retainer && appointments.Count == 0 && _starts == 0,
                "A valid indenture cancels a previously queued lordship retirement without disbanding the party");

            Patch(typeof(CampaignUIHelper), "IsHeroInformationHidden", nameof(No), false);
            Patch(typeof(LordshipBehavior).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper"), "GetLordshipNounForClan", nameof(LordshipNoun), false);
            Patch(typeof(LordshipBehavior).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper"), "GetLordshipNoun", nameof(LordshipNoun), false);
            foreach (var method in typeof(PropertyBasedTooltipVM).GetMethods().Where(m => m.Name == "AddProperty"))
                h.Patch(method, prefix: new HarmonyMethod(typeof(LordshipAppointmentTests), nameof(TooltipRow)));
            TooltipRows.Clear();
            Call(PatchType, "HeroTooltip", null, Blank<PropertyBasedTooltipVM>(), new object[] { _retainer });
            check(TooltipRows.Any(r => r.Label == "Lordship" && r.Value == "None")
                && TooltipRows.Last().Label == "Indenture" && TooltipRows.Last().Value == "84 days remaining",
                "An unlanded retainer shows the descriptive Lordship label, not Feodum, above the remaining indenture");
            Patch(typeof(Settlement), "Name", nameof(VillageName));
            _tooltipVillage = Blank<Settlement>(); _endowed = _retainer;
            TooltipRows.Clear();
            Call(PatchType, "HeroTooltip", null, Blank<PropertyBasedTooltipVM>(), new object[] { _retainer });
            check(TooltipRows.Any(r => r.Label == "Lordship" && r.Value == "Calioc")
                && !TooltipRows.Any(r => r.Label == "Feodum"),
                "A landed retainer keeps the descriptive Lordship label and the actual village name");
            _tooltipVillage = null; _endowed = null;

            activeContract.EndDay = _day; _unsafe = "battle";
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == _retainer && appointments.Any(a => a.Retirement) && NativeSurplus(),
                "Contract expiry ends the surplus exemption immediately but defers retirement during battle");
            _unsafe = null; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_retainer.IsFugitive && _starts == 1 && _party.MemberRoster.TotalManCount == 12,
                "An expired, unsupported captain follows native safe disbandment when no replacement exists");

            IndentureReset(); Contract(_retainer, _day); _endowed = _retainer;
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(_party.LeaderHero == _retainer && _starts == 0 && !NativeSurplus(),
                "A real lordship preserves command and surplus protection after an indenture expires");
            IndentureReset(); Contract(_retainer, 134);
            Call(typeof(LordshipBehavior), "Remove", registry, new PersonalLordshipRecord { Holder = _retainer, GrantingClan = _player });
            check(appointments.Count == 0 && _party.LeaderHero == _retainer,
                "Revoking a land grant does not retire a captain whose paid indenture is still valid");

            var paidCaptain = Person(_player, "paid_replacement"); paidCaptain.Gold = 10000;
            IndentureReset(); Contract(paidCaptain, 134);
            appointments.Add(new LordshipAppointmentRecord { Hero = _retainer, Party = _party, OwningClan = _player, Retirement = true });
            Call(typeof(LordshipBehavior), "Maintain", registry); Call(typeof(LordshipBehavior), "ShowCommandReplacementInquiry", registry);
            check(_replacementInquiry.InquiryElements.Any(i => i.Identifier == paidCaptain),
                "Paid captains appear in the replacement picker even though they own no village");
            _replacementInquiry.AffirmativeAction(new List<InquiryElement> { _replacementInquiry.InquiryElements.First(i => i.Identifier == paidCaptain) });
            Arrive();
            check(_party.LeaderHero == paidCaptain && _starts == 0,
                "The native arrival guard accepts an indentured replacement without inventing a lordship");
            Set(paidCaptain, "_partyBelongedTo", null); Set(paidCaptain, "_heroState", Hero.CharacterStates.Disabled);

            IndentureReset(); Set(_retainer, "_partyBelongedTo", null);
            var town = Blank<Town>(); var townSettlement = Blank<Settlement>();
            var settlementParty = Blank<PartyBase>(); Set(settlementParty, "Settlement", townSettlement);
            Set(town, "_owner", settlementParty); town.Governor = _retainer;
            Set(town, "_ownerClan", _player);
            var governorContract = Contract(_retainer, 134);
            Call(typeof(LordshipBehavior), "Maintain", registry);
            check(town.Governor == _retainer && _retainer.GovernorOf == town, "A paid retainer keeps an existing governorship for the agreed term");
            governorContract.EndDay = _day; Call(typeof(LordshipBehavior), "Maintain", registry);
            check(town.Governor == null && _retainer.GovernorOf == null,
                "An unsupported governor is removed through the native governor action when the indenture expires");
        }
        finally
        {
            h.UnpatchAll(h.Id); AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { oldRegistry });
            AccessTools.Field(Eligibility, "SelectingClan").SetValue(null, null);
            AccessTools.Field(Eligibility, "ListingCandidates").SetValue(null, false);
            AccessTools.Field(Eligibility, "CheckingClanCommand").SetValue(null, false);
            ticks.SetValue(null, oldTicks); hours.SetValue(null, oldHours); Call(Household, "Invalidate", null, new object[] { null });
            Houses.Clear(); Members.Clear();
            _tooltipVillage = null;
        }
    }
}
