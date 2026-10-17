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
using TaleWorlds.Library;

internal static class RegencySettingsTests
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

    private static readonly Dictionary<string, Hero> Heroes = new Dictionary<string, Hero>();
    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static readonly List<Clan> Clans = new List<Clan>();
    private static List<Hero> _line;
    private static Clan _player;
    private static Hero _main;
    private static bool _realmExtinct;
    private static int _destroyed;
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Majority(ref int __result) { __result = 18; return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainHero(ref Hero __result) { __result = _main; return false; }
    private static bool Extinct(ref bool __result) { __result = _realmExtinct; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Clans); return false; }
    private static bool ResolveHero(string __0, ref Hero __result)
    { __result = __0 != null && Heroes.TryGetValue(__0, out var hero) ? hero : null; return false; }
    private static bool ResolveClan(string __0, ref Clan __result)
    { __result = Clans.FirstOrDefault(c => c.StringId == __0); return false; }
    private static bool Line(ref List<Hero> __result) { __result = _line.ToList(); return false; }
    private static bool Install(Clan clan, Hero newLeader, ref bool __result)
    { AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, newLeader); __result = true; return false; }
    private static bool Destroy() { _destroyed++; return false; }
    private static bool Members(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(Heroes.Values.Where(h => h.Clan == __instance).ToList()); return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(RegencyBehavior).Assembly;
        var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings", true);
        var options = assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var policy = assembly.GetType("BellumCivile.RegencyPolicy", true);
        var cache = AccessTools.Field(options, "_settings");
        object oldSettings = cache.GetValue(null);
        var oldCampaign = Campaign.Current;
        var oldRegencies = RegencyBehavior.Instance;
        var harmony = new Harmony("bellum.test.regency_settings");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(RegencySettingsTests), prefix));
        object Call(object instance, string method, params object[] args) =>
            AccessTools.Method(instance.GetType(), method).Invoke(instance, args);
        bool Eligible(Hero heir) => (bool)AccessTools.Method(policy, "CanReceiveCrown").Invoke(null, new object[] { heir });
        Clan House(string id)
        { var c = Blank<Clan>(); c.StringId = id; Clans.Add(c); return c; }
        Hero Person(string id, Clan clan, float age)
        {
            var h = Blank<Hero>(); h.StringId = id;
            AccessTools.Field(typeof(Hero), "_clan").SetValue(h, clan);
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(h, Hero.CharacterStates.Active);
            Heroes.Add(id, h); Ages.Add(h, age); return h;
        }
        try
        {
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.FeudalTitleStylePresetCatalog"), "CreateDropdown"), nameof(Skip));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.SuccessionLawHelper"), "GetAgeOfMajority"), nameof(Majority));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Extinct));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Members));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ResolveHero"), nameof(ResolveHero));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ResolveClan"), nameof(ResolveClan));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "TryInstallClanLeader"), nameof(Install));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ShowRegencyEnded"), nameof(Skip));
            Patch(AccessTools.Method(policy, "TryRestoreAdultLeader"), nameof(No));
            Patch(AccessTools.Method(typeof(DestroyClanAction), "ApplyByClanLeaderDeath"), nameof(Destroy));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine"), nameof(Line));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "OrderEstateHeirs"), nameof(Line));

            object settings = Activator.CreateInstance(settingsType, true);
            cache.SetValue(null, settings);
            void Toggle(bool value) => AccessTools.Property(settingsType, "AllowNewRegencies").SetValue(settings, value);
            check((bool)AccessTools.Property(options, "AllowNewRegencies").GetValue(null), "New and existing settings default to allowing regencies");
            var metadata = AccessTools.Property(settingsType, "AllowNewRegencies").GetCustomAttributes(false);
            var setting = metadata.Single(a => a.GetType().Name == "SettingPropertyBoolAttribute");
            check(!(bool)AccessTools.Property(setting.GetType(), "RequireRestart").GetValue(setting), "Regency toggle does not require a restart");
            string hint = (string)AccessTools.Property(setting.GetType(), "HintText").GetValue(setting);
            check(hint.Contains("Bellum: On") && hint.Contains("Vanilla:") && hint.Contains("player's campaign may end"),
                "Tooltip states both defaults, continuation behavior and player risk");

            var regencies = new RegencyBehavior();
            var crowns = new CrownAccessionBehavior();
            var estates = new PartitionSuccessionBehavior();
            var manager = new Manager(); manager.AddBehavior(regencies); manager.AddBehavior(crowns); manager.AddBehavior(estates);
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            AccessTools.PropertySetter(typeof(RegencyBehavior), "Instance").Invoke(null, new object[] { regencies });
            var house = House("regency_house");
            var ward = Person("ward", house, 12);
            var regent = Person("regent", house, 40);
            var adult = Person("adult", house, 23);
            var predecessor = Person("predecessor", house, 60);
            AccessTools.Field(typeof(Clan), "_leader").SetValue(house, regent);
            var realm = Blank<Kingdom>(); realm.StringId = "realm";
            var records = (List<RegencyRecord>)AccessTools.Field(typeof(RegencyBehavior), "_regencies").GetValue(regencies);
            RegencyRecord Record()
            {
                var r = new RegencyRecord(house.StringId, ward.StringId, regent.StringId, predecessor.StringId, default(CampaignTime), false);
                records.Clear(); records.Add(r); return r;
            }
            check(Eligible(ward), "Enabled policy preserves minor crown candidacy");
            Toggle(false);
            check(!Eligible(ward) && Eligible(adult) && !Eligible(null), "Disabled policy skips unsupported minor heirs but preserves adults");
            check(!regencies.EnsureCrownHeirRegency(house, ward, predecessor) && records.Count == 0,
                "Disabled setting cannot open a Crown regency");
            check(Call(regencies, "CreateGeneratedRegent", house) == null, "Disabled setting cannot generate a caretaker");
            var cadetPlan = new CrownAccessionRecord { Heir = ward, PreviousHouse = house, Predecessor = predecessor };
            check(!(bool)Call(estates, "PrepareAbdicationCadet", cadetPlan) && cadetPlan.Cadet == null,
                "Disabled setting stops a new minor cadet before registering its clan or moving anyone");

            var pendingType = typeof(RegencyBehavior).GetNestedType("PendingPlayerSuccession", BindingFlags.NonPublic);
            var prepared = Activator.CreateInstance(pendingType, true);
            AccessTools.Field(pendingType, "DyingHeroId").SetValue(prepared, predecessor.StringId);
            AccessTools.Field(pendingType, "WardHeroId").SetValue(prepared, ward.StringId);
            AccessTools.Field(pendingType, "CandidateHeroIds").SetValue(prepared, new List<string> { adult.StringId });
            var pendingField = AccessTools.Field(typeof(RegencyBehavior), "_pendingPlayerSuccession");
            pendingField.SetValue(regencies, prepared); _player = house; _main = predecessor;
            check(!(bool)Call(regencies, "TryInjectPlayerSuccessionCandidates", house, null)
                && !(bool)Call(regencies, "TryCommitPreparedPlayerSuccession", adult) && records.Count == 0,
                "Disabling also blocks a previously prepared player regent from injection or commitment");
            AccessTools.Field(pendingType, "Mode").SetValue(prepared,
                Enum.Parse(AccessTools.Field(pendingType, "Mode").FieldType, "WardSucceeds"));
            AccessTools.Field(pendingType, "CandidateHeroIds").SetValue(prepared, new List<string> { ward.StringId });
            Ages[ward] = 18;
            check((bool)Call(regencies, "TryInjectPlayerSuccessionCandidates", house, null)
                && (bool)Call(regencies, "IsPreparedPlayerSuccessorValid", ward),
                "Disabled setting still permits the adult player ward's prepared handover");
            Ages[ward] = 12; pendingField.SetValue(regencies, null); _player = null; _main = null;
            var record = Record();
            check(Eligible(ward) && regencies.EnsureCrownHeirRegency(house, ward, predecessor) && records.Single() == record,
                "Existing live regency survives toggling off without a replacement");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, Hero.CharacterStates.Prisoner);
            check(Eligible(ward), "Captivity does not cancel an existing regency when disabled");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, Hero.CharacterStates.Active);
            var deaths = (HashSet<string>)AccessTools.Field(typeof(RegencyBehavior), "_regentDeathsInProgress").GetValue(regencies);
            deaths.Add(regent.StringId);
            check(!Eligible(ward) && !regencies.EnsureCrownHeirRegency(house, ward, predecessor), "A dying caretaker cannot authorize a minor Crown succession");
            deaths.Clear();
            Call(regencies, "ReplaceRegent", house, record, regent, true);
            check(records.Count == 0 && house.Leader == regent, "Before-death hook removes the regency and leaves native succession to finish");
            record = Record();
            Ages[ward] = 18;
            Call(regencies, "ReplaceRegent", house, record, regent, true);
            check(records.Count == 0 && house.Leader == ward, "Adult ward succeeds normally even when replacement regencies are disabled");
            Ages[ward] = 12;
            AccessTools.Field(typeof(Clan), "_leader").SetValue(house, regent);
            record = Record();
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, Hero.CharacterStates.Dead);
            _destroyed = 0;
            Call(regencies, "ReplaceRegent", house, record, regent, true);
            check(records.Count == 0 && _destroyed == 1, "Maintenance releases a dead-regent clan to native extinction when no adult can succeed");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, Hero.CharacterStates.Active);

            _line = new List<Hero> { adult };
            CrownAccessionRecord Crown() => new CrownAccessionRecord {
                Realm = realm, PreviousHouse = house, Predecessor = predecessor, Heir = ward,
                HeirHouse = house, RequiresRegency = true, IncomingHousePrepared = true,
                EndowmentFiefs = new List<string> { "planned_castle" }, EndowmentGold = 300 };
            var crown = Crown();
            check((bool)Call(crowns, "ReconcileRegencyAvailability", crown) && crown.Heir == adult
                && !crown.Emergency && !crown.RequiresRegency && !crown.IncomingHousePrepared,
                "Unstarted Crown inheritance chooses the eligible adult and rebuilds its household plan");
            check(crown.EndowmentFiefs.Count == 0 && crown.EndowmentGold == 0,
                "An unexecuted minor's endowment is not charged to the replacement heir");
            _line.Clear(); crown = Crown();
            check((bool)Call(crowns, "ReconcileRegencyAvailability", crown) && crown.Heir == null && crown.Emergency,
                "Crown without an eligible heir advances to emergency election instead of awaiting a new regent");
            crown = Crown(); crown.DeliveredFiefs.Add("delivered_castle"); crown.GoldDebited = crown.GoldCredited = true;
            crown.OutcomeApplied = crown.TitleTransferred = true;
            check((bool)Call(crowns, "ReconcileRegencyAvailability", crown) && crown.Emergency
                && crown.DeliveredFiefs.SequenceEqual(new[] { "delivered_castle" }) && crown.GoldDebited
                && !crown.OutcomeApplied && !crown.TitleTransferred,
                "Partially settled Crown preserves property receipts and reopens only political succession");
            Record(); crown = Crown();
            check((bool)Call(crowns, "ReconcileRegencyAvailability", crown) && crown.Heir == ward && !crown.Emergency,
                "Existing supported minor Crown succession remains intact");
            records.Clear();
            var estate = new CrossClanEstateRecord { Source = house, Realm = realm, Deceased = predecessor };
            var payment = new CrownAccessionRecord { GoldDebited = true, DeliveredGold = 100 };
            var share = new CrossClanEstateShare { Heir = ward, DeliveredTitles = new List<string> { "delivered_title" }, GoldPayment = payment };
            estate.Shares.Add(share); _line.Add(adult);
            share.Recipient = house;
            check((bool)Call(estates, "ReconcileEstateRegencyAvailability", estate) && share.Heir == ward && share.Recipient == house,
                "A minor's pending delivery into an existing adult-led house requires no new regency and remains intact");
            share.Recipient = null;
            check((bool)Call(estates, "ReconcileEstateRegencyAvailability", estate) && share.Heir == adult
                && share.DeliveredTitles.Single() == "delivered_title" && share.GoldPayment == payment && payment.GoldDebited,
                "Pending estate reroutes only the undelivered remainder and retains its gold journal");
            _line.Clear(); share.Heir = ward; share.IsEscheat = false;
            check((bool)Call(estates, "ReconcileEstateRegencyAvailability", estate) && share.IsEscheat && share.DomesticOnly
                && share.Heir == null && !share.ReceivesGold && share.GoldPayment == payment,
                "No viable estate heir uses Crown reversion, including older journals");
            share.Fiefs.Add("undelivered_castle"); share.Titles.Add("undelivered_title");
            payment.GoldCredited = true; _realmExtinct = true;
            check((bool)Call(estates, "ReconcileEstateRegencyAvailability", estate) && estate.Completed && share.Completed
                && share.SupersededFiefs.Contains("undelivered_castle") && share.SupersededTitles.Contains("undelivered_title")
                && share.DeliveredTitles.Single() == "delivered_title" && share.GoldPayment == payment,
                "Extinct realm releases impossible Crown reversions without fabricating property deliveries or replaying gold");
            _realmExtinct = false;
            Toggle(true);
            check(Eligible(ward), "Re-enabling the setting restores future minor heir eligibility");
        }
        finally
        {
            cache.SetValue(null, oldSettings);
            AccessTools.PropertySetter(typeof(RegencyBehavior), "Instance").Invoke(null, new object[] { oldRegencies });
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            harmony.UnpatchAll(harmony.Id);
            Heroes.Clear(); Ages.Clear(); Clans.Clear(); _line = null; _player = null; _main = null; _realmExtinct = false;
        }
    }
}
