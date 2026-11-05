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
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class LordshipAppointmentTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        public T GetBehavior<T>() => _disband is T value ? value : default;
        public IEnumerable<T> GetBehaviors<T>() => _disband is T value ? new[] { value } : Enumerable.Empty<T>();
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
    }
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Hero>> Members = new Dictionary<Clan, MBReadOnlyList<Hero>>();
    private static Clan _player, _npc;
    private static Hero _retainer, _candidate, _endowed, _playerHero;
    private static MobileParty _party, _main;
    private static DisbandPartyCampaignBehavior _disband;
    private static float _day;
    private static string _unsafe;
    private static bool _nativeDeny, _throwCommand;
    private static int _starts, _fugitives, _asserts, _grantAttempts;
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
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool DaysFromNow(float __0, ref CampaignTime __result) { __result = CampaignTime.Days(_day + __0); return false; }
    private static bool Battle(MobileParty __instance, ref MapEvent __result) { __result = __instance == _party && _unsafe == "battle" ? Blank<MapEvent>() : null; return false; }
    private static bool Siege(MobileParty __instance, ref SiegeEvent __result) { __result = __instance == _party && _unsafe == "siege" ? Blank<SiegeEvent>() : null; return false; }
    private static bool Army(MobileParty __instance, ref Army __result) { __result = __instance == _party && _unsafe == "army" ? Blank<Army>() : null; return false; }
    private static bool Sea(MobileParty __instance, ref bool __result) { __result = __instance == _party && _unsafe == "sea"; return false; }
    private static bool Assert() { _asserts++; return false; }
    private static bool Grant(ref bool __result) { _grantAttempts++; __result = false; return false; }
    private static bool GrantLookup(Hero hero, ref PersonalLordshipRecord __result)
    { if (hero != _endowed) return true; __result = new PersonalLordshipRecord { Holder = hero }; return false; }
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
                == "Your clan has no available family members or retainers holding a lordship grant to lead a new party.",
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

            void Reset()
            {
                Set(_retainer, "_partyBelongedTo", null); Set(_retainer, "_heroState", Hero.CharacterStates.Active);
                _unsafe = null; _starts = _fugitives = _asserts = 0; _disband = new DisbandPartyCampaignBehavior(); appointments.Clear();
                _party = Party(_retainer);
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
        }
    }
}
