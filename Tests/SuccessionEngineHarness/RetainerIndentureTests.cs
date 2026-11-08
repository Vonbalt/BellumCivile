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
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class RetainerIndentureTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving)
            {
                Data[key] = value is List<RetainerIndentureRecord> contracts
                    ? contracts.Select(c => new RetainerIndentureRecord { Holder = c.Holder, Employer = c.Employer,
                        EndDay = c.EndDay, RenewalNotified = c.RenewalNotified }).ToList() : (object)value;
            }
            else value = Data.TryGetValue(key, out var saved) ? (T)saved : default;
            return true;
        }
    }
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static Clan _player, _other;
    private static Hero _head, _minor, _landed;
    private static double _day;
    private static int _year = 84, _payments, _paid;
    private static InquiryData _warning;
    private static MultiSelectionInquiryData _terms;
    private static Hero _conversationHero;
    private sealed class Line { internal Delegate Condition, Consequence, Clickable; }
    private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
    private static readonly Type Membership = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.HouseholdMembership");
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string method, params object[] args) => AccessTools.Method(owner.GetType(), method).Invoke(owner, args);
    private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Head(ref Hero __result) { __result = _head; return false; }
    private static bool House(Hero __instance, ref Clan __result) { __result = Houses[__instance]; return false; }
    private static bool Members(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(Houses.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool EmptyFiefs(ref MBReadOnlyList<Town> __result) { __result = new MBReadOnlyList<Town>(); return false; }
    private static bool ConversationHero(ref Hero __result) { __result = _conversationHero; return false; }
    private static bool CaptureLine(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        Lines[(string)__args[0]] = new Line { Condition = __args[repeat ? 6 : 4] as Delegate,
            Consequence = __args[repeat ? 7 : 5] as Delegate, Clickable = __args[repeat ? 9 : 7] as Delegate };
        __result = null; return false;
    }
    private static bool Child(Hero __instance, ref bool __result) { __result = __instance == _minor; return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Year(ref int __result) { __result = _year; return false; }
    private static bool Pay(Hero __0, Hero __1, int __2)
    { _payments++; _paid += __2; __0.Gold -= __2; __1.Gold += __2; return false; }
    private static bool Skip() => false;
    private static bool Warning(InquiryData __0) { _warning = __0; return false; }
    private static bool Terms(MultiSelectionInquiryData __0) { _terms = __0; return false; }
    private static bool Land(Hero hero, ref PersonalLordshipRecord __result)
    { __result = hero == _landed ? new PersonalLordshipRecord { Holder = hero } : null; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.retainer-indenture");
        var oldCampaign = Campaign.Current; var oldRegistry = LordshipBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        void Patch(Type type, string name, string handler, bool getter = true) => harmony.Patch(
            getter ? AccessTools.PropertyGetter(type, name) : AccessTools.Method(type, name),
            prefix: new HarmonyMethod(typeof(RetainerIndentureTests), handler));
        Hero Person(Clan clan, string name)
        {
            var hero = Blank<Hero>(); hero.StringId = name; Houses.Add(hero, clan);
            Set(hero, "_heroState", Hero.CharacterStates.Active); Set(hero, "_children", new MBList<Hero>()); Set(hero, "_exSpouses", new MBList<Hero>());
            var character = Blank<CharacterObject>(); character.StringId = name;
            Set(character, "_heroObject", hero); Set(hero, "_characterObject", character);
            return hero;
        }
        try
        {
            GameTexts.Initialize(new GameTextManager());
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            ticks.SetValue(null, 24000L); _day = 10; _year = 84; _payments = _paid = 0;
            Patch(typeof(Clan), "PlayerClan", nameof(Player)); Patch(typeof(Hero), "MainHero", nameof(Head));
            Patch(typeof(Clan), "Fiefs", nameof(EmptyFiefs));
            Patch(typeof(Hero), "Clan", nameof(House)); Patch(typeof(Clan), "Heroes", nameof(Members)); Patch(typeof(Clan), "Companions", nameof(Members));
            Patch(typeof(Hero), "IsNotable", nameof(No)); Patch(typeof(Hero), "IsSpecial", nameof(No)); Patch(typeof(Hero), "IsTemplate", nameof(No));
            Patch(typeof(Hero), "IsChild", nameof(Child)); Patch(typeof(Hero), "Name", nameof(Name));
            Patch(typeof(CampaignTime), "Now", nameof(Now)); Patch(typeof(CampaignTime), "DaysInYear", nameof(Year));
            Patch(typeof(GiveGoldAction), "ApplyBetweenCharacters", nameof(Pay), false);
            Patch(typeof(InformationManager), "DisplayMessage", nameof(Skip), false);
            Patch(typeof(InformationManager), "ShowInquiry", nameof(Warning), false);
            Patch(typeof(MBInformationManager), "ShowMultiSelectionInquiry", nameof(Terms), false);
            Patch(typeof(BellumCivileLogger), "Log", nameof(Skip), false);
            harmony.Patch(AccessTools.Method(typeof(LordshipBehavior), "GetGrant", new[] { typeof(Hero) }),
                prefix: new HarmonyMethod(typeof(RetainerIndentureTests), nameof(Land)));
            _player = Blank<Clan>(); _player.StringId = "player"; _other = Blank<Clan>(); _other.StringId = "other";
            _head = Person(_player, "head"); Set(_player, "_leader", _head);
            var otherHead = Person(_other, "other_head"); Set(_other, "_leader", otherHead);
            var retainer = Person(_player, "retainer"); var foreign = Person(_other, "foreign");
            var kin = Person(_player, "kin"); kin.Father = _head;
            _minor = Person(_player, "minor");
            void Invalidate() => AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null });
            Invalidate();
            LordshipBehavior Fresh()
            {
                var registry = new LordshipBehavior(); Set(registry, "_ready", true); Set(registry, "_campaign", campaign);
                AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
                _head.Gold = 100000; retainer.Gold = 100; _landed = null;
                return registry;
            }
            bool Sign(LordshipBehavior registry, Hero hero, int years, double expected = -1)
                => (bool)Call(registry, "TrySignIndenture", hero, years, null, expected);
            RetainerIndentureRecord Contract(LordshipBehavior registry, Hero hero) => (RetainerIndentureRecord)Call(registry, "GetIndenture", hero);
            bool Support(LordshipBehavior registry, Hero hero) => (bool)Call(registry, "HasServiceSupport", hero);
            var behavior = Fresh();
            foreach (Hero invalid in new[] { _head, kin, _minor, foreign, null })
                check(!Sign(behavior, invalid, 1), "Indentures exclude family, minors, foreign members and missing recipients: " + invalid?.StringId);
            foreach (int invalid in new[] { -1, 0, 4, int.MaxValue })
                check(!Sign(behavior, retainer, invalid), "Only the three agreed contract terms are accepted: " + invalid);
            _head.Gold = 4999;
            check(!Sign(behavior, retainer, 1) && retainer.Gold == 100 && _payments == 0,
                "Insufficient funds cannot charge the player or create a contract");
            foreach (var term in new[] { (years: 1, fee: 5000), (years: 2, fee: 8000), (years: 3, fee: 13000) })
            {
                behavior = Fresh();
                check(Sign(behavior, retainer, term.years) && _head.Gold == 100000 - term.fee && retainer.Gold == 100 + term.fee,
                    "The full fee goes to the retainer exactly once: " + term.years);
                var contract = Contract(behavior, retainer);
                check(contract.Employer == _player && contract.Holder == retainer && contract.EndDay == 10 + term.years * 84
                    && Support(behavior, retainer), "The selected term grants service support for actual campaign years: " + term.years);
                check(behavior.GetGrant(retainer) == null && !(bool)AccessTools.Method(Membership, "IsFamily").Invoke(null, new object[] { retainer }),
                    "A paid contract neither creates a lordship nor turns its holder into family: " + term.years);
            }
            check(!Sign(behavior, retainer, 1), "An active contract cannot be stacked or renewed before its final seven days");
            double originalEnd = Contract(behavior, retainer).EndDay;
            _day = originalEnd - 7;
            Call(behavior, "MaintainIndentures");
            check((bool)Call(behavior, "ShowIndentureRenewalInquiry") && _warning != null && Contract(behavior, retainer).RenewalNotified,
                "The final seven days generate a single saved renewal reminder");
            int beforePayments = _payments;
            _warning.NegativeAction();
            check(!(bool)Call(behavior, "ShowIndentureRenewalInquiry") && _payments == beforePayments,
                "Letting a contract expire neither renews it nor charges money, and does not repeat the reminder");
            double expectedEnd = Contract(behavior, retainer).EndDay;
            check(Sign(behavior, retainer, 2, expectedEnd) && Contract(behavior, retainer).EndDay == expectedEnd + 168
                && !Contract(behavior, retainer).RenewalNotified, "Early renewal adds the new term to the existing expiry, preserving all remaining days");
            check(!Sign(behavior, retainer, 2, expectedEnd), "A stale or double-clicked renewal cannot charge a second fee");
            _day = Contract(behavior, retainer).EndDay - 7;
            Call(behavior, "MaintainIndentures"); Call(behavior, "ShowIndentureRenewalInquiry");
            _warning.AffirmativeAction();
            check(_terms.InquiryElements.Count == 3, "Remote renewal offers the same three durations and prices as dialogue");
            var selection = new List<InquiryElement> { _terms.InquiryElements[0] };
            double remoteEnd = Contract(behavior, retainer).EndDay;
            _terms.AffirmativeAction(selection);
            check(Contract(behavior, retainer).EndDay == remoteEnd + 84, "Remote renewal preserves the remaining days and commits the selected term");
            beforePayments = _payments; _terms.AffirmativeAction(selection);
            check(_payments == beforePayments, "Repeating a remote renewal callback cannot charge twice");

            _day = 10; behavior = Fresh(); _year = 168;
            check(Sign(behavior, retainer, 1) && Contract(behavior, retainer).EndDay == 178,
                "Contract duration follows a modified campaign calendar rather than hardcoded year lengths");
            _year = 84; _day = 177.5;
            check((int)AccessTools.Method(typeof(LordshipBehavior), "IndentureDaysLeft").Invoke(null, new object[] { Contract(behavior, retainer) }) == 1,
                "The tooltip rounds a partial final day upward instead of reporting zero days on an active indenture");
            int retainedGold = retainer.Gold; _day = 178;
            check(!Support(behavior, retainer), "Service rights stop exactly at expiry, before hourly cleanup runs");
            Call(behavior, "MaintainIndentures");
            check(Contract(behavior, retainer) == null && Houses[retainer] == _player && retainer.Gold == retainedGold,
                "Agreed expiry preserves the companion, clan membership and remaining personal money");

            _day = 10; behavior = Fresh(); check(Sign(behavior, retainer, 1), "A contract can be signed again after the prior term has ended");
            _landed = retainer; _day = 94; Call(behavior, "MaintainIndentures");
            check(Support(behavior, retainer) && Contract(behavior, retainer) == null,
                "An existing lordship continues to support appointments after the indenture expires");
            _day = 10; behavior = Fresh(); Sign(behavior, retainer, 1);
            Houses[retainer] = _other;
            check(!Support(behavior, retainer), "Changing clans immediately invalidates the former employer's indenture");
            Call(behavior, "Validate"); Houses[retainer] = _player; Invalidate();
            check(Contract(behavior, retainer) == null, "A removed contract does not revive when the retainer later rejoins the clan");
            behavior = Fresh(); Sign(behavior, retainer, 1); Set(retainer, "_heroState", Hero.CharacterStates.Dead);
            Call(behavior, "Validate");
            check(Contract(behavior, retainer) == null, "Death ends a personal indenture without inheritance");
            Set(retainer, "_heroState", Hero.CharacterStates.Active);

            behavior = Fresh(); Sign(behavior, retainer, 3);
            Contract(behavior, retainer).RenewalNotified = true;
            var store = new Store { IsSaving = true }; behavior.SyncData(store);
            var loaded = Fresh(); store.IsSaving = false; loaded.SyncData(store);
            check(Contract(loaded, retainer)?.EndDay == 262 && Contract(loaded, retainer).RenewalNotified,
                "Loading rebuilds indexed contracts and preserves expiry and reminder receipts");
            var oldSave = Fresh(); oldSave.SyncData(new Store());
            check(!Support(oldSave, retainer), "Older saves initialize with no paid contracts or free service rights");
            check(_paid > 0, "All successful signings use the same native character-to-character gold action");

            behavior = Fresh(); Sign(behavior, retainer, 1);
            var helper = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
            Patch(typeof(LordshipBehavior).Assembly.GetType("BellumCivile.MarriageParticipantPolicy"), "IsParticipant", nameof(Yes), false);
            Patch(helper, "HouseholdMarriageProspect", nameof(No), false);
            var health = AccessTools.Method(helper, "AssessHouseHealth").Invoke(null, new object[] { _player });
            check(((HashSet<Hero>)AccessTools.Field(health.GetType(), "EndowedMembers").GetValue(health)).Contains(retainer)
                && !((HashSet<Hero>)AccessTools.Field(health.GetType(), "ReleasingEndowments").GetValue(health)).Contains(retainer),
                "Household manpower recognizes paid service without pretending a marriage departure would free a village");
            var display = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper");
            check(!(bool)AccessTools.Method(display, "TryGetWandererDisplayStyle").Invoke(null, new object[] { retainer, null, null }),
                "An indenture alone does not grant the Sir/Dame honorific reserved for actual lordship holders");

            Patch(typeof(Hero), "OneToOneConversationHero", nameof(ConversationHero)); _conversationHero = retainer;
            foreach (var method in typeof(CampaignGameStarter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => new[] { "AddPlayerLine", "AddDialogLine", "AddRepeatablePlayerLine" }.Contains(m.Name)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(RetainerIndentureTests), nameof(CaptureLine)));
            behavior = Fresh(); Call(behavior, "AddDialogues", Blank<CampaignGameStarter>());
            check((bool)Lines["bc_indent_offer"].Condition.DynamicInvoke(), "A landless player can open the captain's indenture dialogue");
            Lines["bc_indent_offer"].Consequence.DynamicInvoke();
            check((bool)Lines["bc_indent_landless"].Condition.DynamicInvoke(), "The dialogue explains coin in place of land when no vacant estate exists");
            beforePayments = _payments;
            foreach (int term in new[] { 1, 2, 3 }) Lines["bc_indent_term_" + term].Consequence.DynamicInvoke();
            check(_payments == beforePayments && Contract(behavior, retainer) == null,
                "Opening negotiations and selecting durations cannot charge money or sign a contract");
            check(new[] { "bc_indent_explain_cancel", "bc_indent_terms_cancel", "bc_indent_confirm_cancel" }.All(id => Lines[id].Consequence == null),
                "Every dialogue cancel exits without payment, relations changes or a contract");
            Lines["bc_indent_confirm"].Consequence.DynamicInvoke();
            check(_payments == beforePayments + 1 && Contract(behavior, retainer).EndDay == 262
                && (bool)Lines["bc_indent_success"].Condition.DynamicInvoke(), "Only final confirmation transfers the selected fee and reaches the acknowledgement");
            behavior = Fresh(); Lines.Clear(); Call(behavior, "AddDialogues", Blank<CampaignGameStarter>());
            Lines["bc_indent_offer"].Consequence.DynamicInvoke();
            Lines["bc_indent_term_1"].Consequence.DynamicInvoke(); _head.Gold = 4999;
            var clickableArgs = new object[] { null };
            check(!(bool)Lines["bc_indent_confirm"].Clickable.DynamicInvoke(clickableArgs)
                && clickableArgs[0] is TextObject disabled && disabled.GetID() == "BC_Indenture_NotEnoughGold",
                "Unaffordable confirmation is disabled with an explicit gold requirement");
            beforePayments = _payments; Lines["bc_indent_confirm"].Consequence.DynamicInvoke();
            check(_payments == beforePayments && Contract(behavior, retainer) == null,
                "Final confirmation rechecks funds even if a stale UI tries to execute the disabled choice");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { oldRegistry });
            AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null }); Houses.Clear();
            Lines.Clear();
        }
    }
}
