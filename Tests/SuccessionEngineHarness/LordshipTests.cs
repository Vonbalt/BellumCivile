using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static partial class LordshipTests
{
    private static readonly Dictionary<Hero, Clan> Clans = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Hero>> Members = new Dictionary<Clan, MBReadOnlyList<Hero>>();
    private static readonly Dictionary<SettlementComponent, Settlement> Settlements = new Dictionary<SettlementComponent, Settlement>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static readonly Dictionary<Village, Settlement> Bounds = new Dictionary<Village, Settlement>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Town>> Fiefs = new Dictionary<Clan, MBReadOnlyList<Town>>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Village>> Villages = new Dictionary<Clan, MBReadOnlyList<Village>>();
    private static readonly Dictionary<Settlement, MBReadOnlyList<Village>> BoundVillages = new Dictionary<Settlement, MBReadOnlyList<Village>>();
    private static readonly Dictionary<Town, Hero> Governors = new Dictionary<Town, Hero>();
    private static readonly Dictionary<Hero, Town> Posts = new Dictionary<Hero, Town>();
    private static Clan _player;
    private static int _rewards, _paid;
    private static float _day;
    private static bool _contested;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static Type Type(string name) => typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile." + name, true);
    private static object Call(Type type, string name, object instance, params object[] args) => AccessTools.Method(type, name).Invoke(instance, args);

    private static bool HeroClan(Hero __instance, ref Clan __result) { Clans.TryGetValue(__instance, out __result); return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result) { __result = Members[__instance]; return false; }
    private static bool Companions(ref MBReadOnlyList<Hero> __result) { __result = new MBReadOnlyList<Hero>(); return false; }
    private static bool ClanFiefs(Clan __instance, ref MBReadOnlyList<Town> __result) { __result = Fiefs[__instance]; return false; }
    private static bool ClanVillages(Clan __instance, ref MBReadOnlyList<Village> __result) { __result = Villages[__instance]; return false; }
    private static bool Component(SettlementComponent __instance, ref Settlement __result) { Settlements.TryGetValue(__instance, out __result); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool TownOwner(Town __instance, ref Clan __result) { __result = Owners[Settlements[__instance]]; return false; }
    private static bool Bound(Village __instance, ref Settlement __result) { Bounds.TryGetValue(__instance, out __result); return false; }
    private static bool BoundList(Settlement __instance, ref MBReadOnlyList<Village> __result) { __result = BoundVillages[__instance]; return false; }
    private static bool Governor(Town __instance, ref Hero __result) { Governors.TryGetValue(__instance, out __result); return false; }
    private static bool Post(Hero __instance, ref Town __result) { Posts.TryGetValue(__instance, out __result); return false; }
    private static bool NotSpecial(ref bool __result) { __result = false; return false; }
    private static bool Clock(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool SettlementName(Settlement __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool ComponentName(SettlementComponent __instance, ref TextObject __result) { __result = new TextObject("{=!}" + Settlements[__instance].StringId); return false; }
    private static bool Tax(ref int __result) { __result = 101; return false; }
    private static bool NativeIncome(Clan clan, bool applyWithdrawals, bool includeDescriptions, ref ExplainedNumber __result)
    {
        __result = new ExplainedNumber(0f, includeDescriptions);
        var native = Blank<DefaultClanFinanceModel>();
        foreach (var village in Villages[clan]) __result.Add(native.CalculateVillageIncome(clan, village, applyWithdrawals));
        return false;
    }
    private static bool Contested(ref bool __result, ref FeudalTitleRecord title) { __result = _contested; title = null; return false; }
    private static bool Reward() { _rewards++; return false; }
    private static bool Gold(Hero __1, int __2) { _paid += __2; return false; }

    internal static void Run(Action<bool, string> check, string game)
    {
        var harmony = new Harmony("bellum.tests.lordships");
        var oldInstance = LordshipBehavior.Instance;
        var tickField = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object ticks = tickField.GetValue(null);
        void Patch(Type type, string name, string handler, bool getter = true) => harmony.Patch(
            getter ? AccessTools.PropertyGetter(type, name) : AccessTools.Method(type, name),
            prefix: new HarmonyMethod(typeof(LordshipTests), handler));
        var household = Type("HouseholdMembership");
        void Invalidate() => Call(household, "Invalidate", null, new object[] { null });
        bool Family(Hero hero) => (bool)Call(household, "IsFamily", null, hero);
        Clan House(string id)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            Members[clan] = new MBReadOnlyList<Hero>(); Fiefs[clan] = new MBReadOnlyList<Town>(); Villages[clan] = new MBReadOnlyList<Village>();
            return clan;
        }
        Hero Person(Clan clan, string id)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_exSpouses").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
            Clans[hero] = clan; Members[clan].Add(hero); return hero;
        }
        Settlement VillageOf(Clan clan, string id, out Town town)
        {
            var parent = Blank<Settlement>(); parent.StringId = id + "_castle"; Owners[parent] = clan;
            town = Blank<Town>(); Settlements[town] = parent;
            AccessTools.Field(typeof(Settlement), "Town").SetValue(parent, town);
            Fiefs[clan].Add(town);
            var settlement = Blank<Settlement>(); settlement.StringId = id;
            var village = Blank<Village>(); Settlements[village] = settlement; Bounds[village] = parent;
            AccessTools.Field(typeof(Settlement), "Village").SetValue(settlement, village);
            BoundVillages[parent] = new MBReadOnlyList<Village>(new[] { village }); Villages[clan].Add(village);
            return settlement;
        }
        try
        {
            tickField.SetValue(null, 24000L); _day = 10;
            Patch(typeof(Hero), "Clan", nameof(HeroClan)); Patch(typeof(Clan), "PlayerClan", nameof(Player));
            Patch(typeof(Hero), "MainHero", nameof(MainHero));
            Patch(typeof(Clan), "Heroes", nameof(Heroes)); Patch(typeof(Clan), "Companions", nameof(Companions));
            Patch(typeof(Clan), "Fiefs", nameof(ClanFiefs)); Patch(typeof(Clan), "Villages", nameof(ClanVillages));
            Patch(typeof(SettlementComponent), "Settlement", nameof(Component)); Patch(typeof(Settlement), "OwnerClan", nameof(Owner));
            Patch(typeof(Town), "OwnerClan", nameof(TownOwner));
            Patch(typeof(Village), "Bound", nameof(Bound)); Patch(typeof(Settlement), "BoundVillages", nameof(BoundList));
            Patch(typeof(Hero), "GovernorOf", nameof(Post)); Patch(typeof(Town), "Governor", nameof(Governor));
            Patch(typeof(Hero), "IsChild", nameof(NotSpecial)); Patch(typeof(Hero), "IsNotable", nameof(NotSpecial));
            Patch(typeof(Hero), "IsSpecial", nameof(NotSpecial)); Patch(typeof(Hero), "IsTemplate", nameof(NotSpecial));
            Patch(typeof(Hero), "Name", nameof(Name)); Patch(typeof(Settlement), "Name", nameof(SettlementName));
            Patch(typeof(SettlementComponent), "Name", nameof(ComponentName));
            Patch(typeof(CampaignTime), "Now", nameof(Clock));
            Patch(typeof(RelationMemoryService), "ApplyChange", nameof(Reward), false);
            Patch(typeof(GiveGoldAction), "ApplyBetweenCharacters", nameof(Gold), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateVillageIncome", nameof(Tax), false);
            Patch(Type("FeudalTitlePenaltyHelper"), "IsBoundToContestedLegalTitle", nameof(Contested), false);
            var clan = House("house"); _player = House("player");
            var leader = Person(clan, "leader"); AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, leader);
            var child = Person(clan, "child"); child.Father = leader;
            var sibling = Person(clan, "sibling"); var ancestor = Person(clan, "ancestor"); leader.Father = ancestor; sibling.Father = ancestor;
            var stranger = Person(clan, "retainer"); var spouse = Person(clan, "spouse");
            AccessTools.Field(typeof(Hero), "_spouse").SetValue(child, spouse);
            Invalidate();
            check(Family(leader) && Family(child) && Family(sibling), "Known blood kin and the legal head are family");
            check(Family(spouse), "A spouse married into the family remains family");
            check(!Family(stranger), "An unrelated member does not acquire peerage from clan membership");
            check((bool)Call(household, "IsRetainer", null, stranger), "Unrelated members use the shared retainer classification");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(child, Hero.CharacterStates.Dead);
            AccessTools.Field(typeof(Hero), "_spouse").SetValue(child, null);
            child.ExSpouses.Add(spouse); Invalidate();
            check(Family(spouse), "A surviving widow or widower stays in the household family");
            ancestor.Father = child; Invalidate();
            check(Family(sibling), "Malformed ancestry cycles terminate without recursive scans");

            var registry = new LordshipBehavior();
            AccessTools.Property(typeof(LordshipBehavior), "Instance").SetValue(null, registry);
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            var village = VillageOf(clan, "first", out var town);
            var second = VillageOf(clan, "second", out _);
            var oldTitles = FeudalTitleBehavior.Instance;
            try
            {
                var titles = new FeudalTitleBehavior();
                AccessTools.Property(typeof(FeudalTitleBehavior), "Instance").SetValue(null, titles);
                var custody = (Dictionary<string, string>)AccessTools.Field(typeof(FeudalTitleBehavior), "_allocationCustodianByTitle").GetValue(titles);
                custody[FeudalTitleBehavior.BuildBaronyTitleId(village.Village.Bound)] = clan.StringId;
                check(!registry.CanGrant(clan, village, stranger, out _), "Temporary fief custodians cannot endow the village before allocation");
                check(registry.FindVacancy(clan) == second, "Vacancy selection skips custodial fiefs without hiding other available grants");
            }
            finally { AccessTools.Property(typeof(FeudalTitleBehavior), "Instance").SetValue(null, oldTitles); }
            _rewards = 0;
            check(registry.TryGrant(clan, village, stranger, out _) && _rewards == 1, "A free grant records one personal reward");
            check(!registry.TryGrant(clan, second, stranger, out _) && _rewards == 1, "One hero cannot hold two lordships or collect duplicate grant rewards");
            check(!registry.TryGrant(clan, village, sibling, out _) && _rewards == 1, "An occupied village cannot be granted twice");
            check(!registry.TryGrant(_player, second, sibling, out _), "A sovereign or outsider cannot grant another clan's village");
            check(registry.GetGrant(stranger)?.Village == village, "Hero and village indices resolve the same grant");
            check(registry.GetGrant(village).Parent == village.Village.Bound, "The saved grant remains bound to its actual parent barony");
            var incomeType = Type("LordshipIncome");
            for (int gross = 0; gross < 501; gross++)
            {
                int net = (int)Call(incomeType, "Net", null, gross, true);
                check(net == gross + (int)Math.Round(-0.5f * gross), "Contested income preserves existing deduction rounding: " + gross);
                check((int)Call(incomeType, "Net", null, gross, false) == gross, "Rightful ownership does not lose village income: " + gross);
            }
            object Pass(bool apply) => Activator.CreateInstance(incomeType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { clan, Blank<DefaultClanFinanceModel>(), apply }, null);
            foreach (bool contested in new[] { false, true })
            {
                _contested = contested; _paid = 0;
                object pass = Pass(false); ((IDisposable)pass).Dispose();
                int net = contested ? 51 : 101;
                var args = new object[] { new ExplainedNumber(net) };
                Call(incomeType, "Redirect", pass, args);
                check(((ExplainedNumber)args[0]).ResultNumber == 0 && _paid == 0, "Forecasts redirect only the net share without paying gold: " + contested);
                pass = Pass(true); ((IDisposable)pass).Dispose(); args[0] = new ExplainedNumber(net);
                Call(incomeType, "Redirect", pass, args);
                check(((ExplainedNumber)args[0]).ResultNumber == 0 && _paid == net, "Committed income goes to the holder exactly once, after the ownership penalty: " + contested);
                Call(incomeType, "Redirect", pass, args);
                check(((ExplainedNumber)args[0]).ResultNumber == 0 && _paid == net, "A repeated redirect callback cannot debit or pay the allocation twice: " + contested);
            }
            harmony.Unpatch(AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateVillageIncome"),
                AccessTools.Method(typeof(LordshipTests), nameof(Tax)));
            Patch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange", nameof(NativeIncome), false);
            var nativeModel = Blank<DefaultClanFinanceModel>();
            var model = new BellumFeudalClanFinanceModel(nativeModel);
            foreach (bool contested in new[] { false, true })
            {
                _contested = contested; _paid = 0;
                foreach (var v in Villages[clan]) v.TradeTaxAccumulated = 1001;
                int before = Villages[clan].Sum(v => v.TradeTaxAccumulated);
                int raw = nativeModel.CalculateVillageIncome(clan, village.Village, false);
                int adjusted = (int)Call(incomeType, "Net", null, raw, contested);
                var forecast = model.CalculateClanGoldChange(clan, false, false);
                check(_paid == 0 && Villages[clan].Sum(v => v.TradeTaxAccumulated) == before,
                    "Full Bellum forecast does not withdraw native village taxes or pay personal gold: " + contested);
                var committed = model.CalculateClanGoldChange(clan, false, true);
                check(committed.ResultNumber == adjusted && _paid == adjusted,
                    "Actual native village withdrawals conserve the holder and ungranted-village clan shares: " + contested);
                check(Villages[clan].Sum(v => v.TradeTaxAccumulated) == before - raw * Villages[clan].Count,
                    "The endowment does not withdraw the tax pool a second time: " + contested);
                check(forecast.ResultNumber == committed.ResultNumber,
                    "Displayed and settled clan income use the same pre-withdrawal ownership penalty: " + contested);
            }
            Governors[town] = stranger; Posts[stranger] = town;
            var legacy = new LordshipAppointmentRecord { Hero = stranger, Town = town, OwningClan = clan, PlayerTransition = true, Deadline = 15 };
            var appointments = (List<LordshipAppointmentRecord>)AccessTools.Field(typeof(LordshipBehavior), "_appointments").GetValue(registry);
            appointments.Add(legacy);
            check(legacy.IsCurrent, "A grandfathered appointment identifies its exact clan and role");
            check((bool)Call(typeof(LordshipBehavior), "HasLegacyAppointment", registry, stranger, null, town), "The old appointment remains protected before its saved deadline");
            _day = 15;
            check(!(bool)Call(typeof(LordshipBehavior), "HasLegacyAppointment", registry, stranger, null, town), "Five-day protection expires at the stored deadline");
            _day = 14; var elsewhere = Blank<Town>();
            check(!(bool)Call(typeof(LordshipBehavior), "HasLegacyAppointment", registry, stranger, null, elsewhere), "A grandfathered governorship cannot authorize a different appointment");
            Clans[stranger] = _player;
            check(!legacy.IsCurrent && registry.GetGrant(stranger) == null, "Clan departure invalidates both personal tenure and old role exemptions");
            Clans[stranger] = clan;
            Owners[village.Village.Bound] = _player;
            check(registry.GetGrant(village) == null, "Loss of the parent barony immediately invalidates the personal grant");
            Owners[village.Village.Bound] = clan;
            Call(typeof(LordshipBehavior), "Validate", registry);
            check(registry.TryRevoke(clan, village, out _) && _rewards == 2, "Voluntary revocation uses the shared action and one personal penalty");
            check(registry.GetGrant(stranger) == null, "Revocation vacates both indices without creating a clan title or claim");
            var malformed = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
            malformed.Add(new PersonalLordshipRecord()); malformed.Add(null);
            Call(typeof(LordshipBehavior), "Validate", registry);
            check(malformed.Count == 0, "Incomplete saved grants are removed without null dictionary accesses");
            var eligibility = Type("LordshipEligibility");
            int priorRewards = _rewards;
            AccessTools.Field(eligibility, "SelectingClan").SetValue(null, clan);
            check((bool)Call(eligibility, "Permitted", null, stranger, false, null)
                && registry.GetGrant(stranger) == null && _rewards == priorRewards,
                "NPC candidate inspection is provisional and awards neither land nor relations");
            AccessTools.Field(eligibility, "SelectingClan").SetValue(null, null);
            check(!(bool)Call(eligibility, "Permitted", null, stranger, false, null),
                "An unlanded retainer cannot use provisional NPC eligibility outside selection");
            check(clan.Gold == 0 && (bool)Call(typeof(LordshipBehavior), "EnsureAppointmentGrant", registry, stranger)
                && registry.GetGrant(stranger) != null, "A needed appointment grants available land even when the NPC clan is broke");
            priorRewards = _rewards;
            Call(typeof(LordshipBehavior), "EnsureAppointmentGrant", registry, stranger);
            check(_rewards == priorRewards, "Repeated appointment callbacks cannot grant or reward the same retainer twice");
            Call(typeof(LordshipBehavior), "OnClanDay", registry, clan);
            check(registry.GetGrant(stranger) != null && registry.GetGrant(second) == null,
                "Poverty blocks discretionary gifts without revoking an existing endowment");
            CheckRoundTrip(check, registry, legacy);
            CheckPrefab(check);
            LordshipHierarchyTests.Run(check, village);
            Patch(typeof(Hero), "CanLeadParty", nameof(NotSpecial), false);
            check(!(bool)Call(Type("Patches.LordshipAppointmentPatches"), "FallbackCommander", null, leader),
                "The native commander fallback respects another mod's CanLeadParty prohibition");
            harmony.Unpatch(AccessTools.Method(typeof(Hero), "CanLeadParty"), AccessTools.Method(typeof(LordshipTests), nameof(NotSpecial)));
            CheckNativeHooks(check);
            CheckSaveFields(check);
            priorRewards = _rewards;
            Owners[village.Village.Bound] = _player;
            Call(typeof(LordshipBehavior), "Validate", registry);
            check(malformed.Count == 0 && _rewards == priorRewards,
                "Confirmed barony loss removes the grant permanently without a voluntary-revocation penalty");
            Owners[village.Village.Bound] = clan;
            check(registry.GetGrant(stranger) == null,
                "Reconquering a barony does not resurrect its former personal endowments");
            CheckRetainerLabels(check, registry, clan, leader, sibling, spouse, stranger, Person);
            CheckVassalGrants(check, harmony, registry, clan, leader, House, Person, (c, id) => VillageOf(c, id, out _));
        }
        finally
        {
            AccessTools.Property(typeof(LordshipBehavior), "Instance").SetValue(null, oldInstance);
            Invalidate(); harmony.UnpatchAll(harmony.Id); tickField.SetValue(null, ticks);
        }
    }

    private static void CheckPrefab(Action<bool, string> check)
    {
        var type = Type("UI.EncyclopediaClanHouseholdPrefabPatch");
        var patch = Activator.CreateInstance(type, true);
        var doc = new XmlDocument();
        doc.LoadXml("<Children><EncyclopediaDivider Id='MembersDivider' Parameter.Title='@MembersText' Parameter.ItemList='..\\MembersGrid'/><NavigationScopeTargeter ScopeParent='..\\MembersGrid'/><NavigatableGridWidget Id='MembersGrid' DataSource='{Members}' ColumnCount='7'><ItemTemplate><EncyclopediaSubPageElement/></ItemTemplate></NavigatableGridWidget></Children>");
        var original = doc.SelectSingleNode("//*[@Id='MembersGrid']");
        Call(type, "Apply", patch, doc); Call(type, "Apply", patch, doc);
        check(doc.SelectNodes("//*[@Id='BellumClanHousehold']").Count == 1, "Clan prefab augmentation is idempotent");
        check(doc.SelectSingleNode("//*[@Id='MembersGrid']") == original && original.ParentNode == doc.DocumentElement,
            "Native member anchors, binding and parent survive the replacement presentation");
        check(doc.SelectNodes("//NavigatableGridWidget[@ColumnCount='7']").Count == 3, "Family and retainer sections preserve native seven-column sizing");
        original.ParentNode.RemoveChild(original); Call(type, "Apply", patch, doc);
        check(doc.SelectSingleNode("//*[@Id='BellumClanHousehold']") == null, "A foreign replacement receives a safe native fallback instead of missing-anchor errors");
    }

    private static void CheckNativeHooks(Action<bool, string> check)
    {
        var type = Type("Patches.LordshipAppointmentPatches");
        var harmony = new Harmony("bellum.tests.lordship.native_contracts");
        try
        {
            foreach (var spec in new[] { new[] { "HeroSpawnCampaignBehavior", "GetBestAvailableCommander", "CommanderCode" },
                new[] { "ClanVariablesCampaignBehavior", "UpdateGovernorsOfClan", "GovernorCode" } })
            {
                var native = typeof(Clan).Assembly.GetTypes().Single(t => t.Name == spec[0]);
                harmony.Patch(AccessTools.Method(native, spec[1]), transpiler: new HarmonyMethod(type, spec[2]));
                check(true, "Native selection method accepts the checked lordship integration: " + spec[1]);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void CheckSaveFields(Action<bool, string> check)
    {
        foreach (var type in new[] { typeof(PersonalLordshipRecord), typeof(LordshipAppointmentRecord), typeof(LordshipIncomeCredit) })
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments.Single().Value).ToList();
            check(ids.Count == ids.Distinct().Count(), "Persisted lordship fields have unique IDs: " + type.Name);
        }
        check((int)FeudalTitleType.Barony == 0 && Enum.GetValues(typeof(FeudalTitleType)).Length == 5,
            "Personal lordships do not renumber or extend the sovereign title hierarchy");
        var context = new DefinitionContext();
        var basic = new SaveableBasicTypeDefiner();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var definer = new BellumCivileSaveDefiner(); initialize.Invoke(definer, new object[] { context });
        var harmony = new Harmony("bellum.tests.lordships.save_definitions");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"),
                prefix: new HarmonyMethod(typeof(LordshipTests), nameof(OnlyRecords)));
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(LordshipTests), nameof(OnlyLists)));
            Call(typeof(BellumCivileSaveDefiner), "DefineClassTypes", definer);
            Call(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions", definer);
            foreach (var type in new[] { typeof(PersonalLordshipRecord), typeof(LordshipAppointmentRecord), typeof(LordshipIncomeCredit) })
                check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                    .Invoke(context, new object[] { type }) != null && AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                    .Invoke(context, new object[] { typeof(List<>).MakeGenericType(type) }) != null,
                    "Native save registry resolves the new record and list: " + type.Name);
            check(!context.GotError, "Lordship save definitions introduce no native registry errors");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static bool OnlyRecords(Type type) => type == typeof(PersonalLordshipRecord) || type == typeof(LordshipAppointmentRecord) || type == typeof(LordshipIncomeCredit);
    private static bool OnlyLists(Type type) => type == typeof(List<PersonalLordshipRecord>) || type == typeof(List<LordshipAppointmentRecord>) || type == typeof(List<LordshipIncomeCredit>);

    private sealed class MemoryStore : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) Data[key] = value;
            else if (Data.TryGetValue(key, out var stored)) value = (T)stored;
            return true;
        }
    }

    private static void CheckRoundTrip(Action<bool, string> check, LordshipBehavior registry, LordshipAppointmentRecord legacy)
    {
        var appointments = (List<LordshipAppointmentRecord>)AccessTools.Field(typeof(LordshipBehavior), "_appointments").GetValue(registry);
        appointments.Add(legacy);
        AccessTools.Field(typeof(LordshipBehavior), "_schema").SetValue(registry, 1);
        AccessTools.Field(typeof(LordshipBehavior), "_warned").SetValue(registry, true);
        var store = new MemoryStore { IsSaving = true };
        registry.SyncData(store);
        store.IsSaving = false;
        var loaded = new LordshipBehavior(); loaded.SyncData(store);
        var restored = (List<LordshipAppointmentRecord>)AccessTools.Field(typeof(LordshipBehavior), "_appointments").GetValue(loaded);
        check(restored.Contains(legacy) && legacy.Deadline == 15,
            "Data-store round trip retains the original migration deadline rather than granting another five days");
        check((int)AccessTools.Field(typeof(LordshipBehavior), "_schema").GetValue(loaded) == 1
            && (bool)AccessTools.Field(typeof(LordshipBehavior), "_warned").GetValue(loaded),
            "Completed first-load migration and warning state survive the data-store round trip");
    }
}
