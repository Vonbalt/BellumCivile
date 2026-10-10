using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class StartingClientageTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
                Data[key] = data is IList ? Activator.CreateInstance(data.GetType(), data) : (object)data;
            else
                data = Data.TryGetValue(key, out object saved) ? (T)saved : default;
            return true;
        }
    }

    private static readonly Type Behavior = typeof(ClientKingdomBehavior);
    private static readonly Type Config = Behavior.Assembly.GetType("BellumCivile.FeudalTitleConfig", true);
    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly HashSet<Kingdom> Temporary = new HashSet<Kingdom>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static readonly HashSet<Tuple<Kingdom, Kingdom>> Wars = new HashSet<Tuple<Kingdom, Kingdom>>();
    private static readonly List<string> Actions = new List<string>();
    private static ClientKingdomBehavior _behavior;
    private static float _day;
    private static bool _throwWar, _blockWar, _throwAgreement;
    private static int _submissionNotices;
    private static string _file;
    private static Action _checkAlignment;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object target, string method, params object[] args)
        => AccessTools.Method(target.GetType(), method).Invoke(target, args);
    private static object Field(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    private static Tuple<Kingdom, Kingdom> Pair(Kingdom a, Kingdom b) => string.CompareOrdinal(a.StringId, b.StringId) < 0
        ? Tuple.Create(a, b) : Tuple.Create(b, a);
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool Crown(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Home(Clan __instance, ref Kingdom __result)
    { __result = Rulers.FirstOrDefault(pair => pair.Value == __instance).Key; return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !Dead.Contains(__instance); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool IsTemporary(Kingdom __0, ref bool __result) { __result = Temporary.Contains(__0); return false; }
    private static bool Today(ref float __result) { __result = _day; return false; }
    private static bool AtWar(Kingdom __instance, IFaction __0, ref bool __result)
    { __result = __0 is Kingdom other && Wars.Contains(Pair(__instance, other)); return false; }
    private static bool Declare(IFaction __0, IFaction __1)
    {
        if (_throwWar) throw new InvalidOperationException("injected war failure");
        if (_blockWar) return false;
        var first = (Kingdom)__0; var second = (Kingdom)__1;
        Wars.Add(Pair(first, second));
        Actions.Add("war:" + first.StringId + ":" + second.StringId);
        // Exercise the actual Harmony startup guard against the native listener.
        Call(Blank<AllianceCampaignBehavior>(), "OnWarDeclared", first, second, DeclareWarAction.DeclareWarDetail.Default);
        return false;
    }
    private static bool Peace(IFaction __0, IFaction __1)
    {
        Wars.Remove(Pair((Kingdom)__0, (Kingdom)__1));
        Actions.Add("peace:" + __0.StringId + ":" + __1.StringId);
        return false;
    }
    private static bool EndIndependent(Kingdom client, Kingdom suzerain)
    { Actions.Add("cleanup:" + client.StringId); return false; }
    private static bool Agreements(Kingdom client, Kingdom suzerain)
    {
        _checkAlignment?.Invoke();
        if (_throwAgreement) throw new InvalidOperationException("injected agreement failure");
        if (!_behavior.IsClientOf(client, suzerain)) throw new Exception("Agreements preceded client record publication");
        Actions.Add("agreements:" + client.StringId);
        return false;
    }
    private static bool Expire(Kingdom client, ref int __result)
    { Actions.Add("pacts:" + client.StringId); __result = 0; return false; }
    private static bool Notice() { _submissionNotices++; return false; }
    private static bool OnlyClientRecord(Type type) => type == typeof(ClientKingdomRecord);
    private static bool OnlyClientList(Type type) => type == typeof(List<ClientKingdomRecord>);

    private static object NewConfig() => Activator.CreateInstance(Config, true);
    private static void Load(object config, string body, bool stylesOnly = false)
    {
        var xml = new XmlDocument(); xml.LoadXml("<BellumFeudalTitles>" + body + "</BellumFeudalTitles>");
        xml.Save(_file);
        Call(config, "LoadFile", _file, false, stylesOnly);
    }
    private static string Entry(string client, string suzerain, string extra = "")
        => $"<StartingClientage client='{client}' suzerain='{suzerain}' submission='Voluntary' {extra} />";
    private static IList Entries(object config) => (IList)Field(config, "_startingClientages");
    private static void UseConfig(string xml)
    {
        var config = NewConfig(); Load(config, xml);
        AccessTools.Field(Config, "_instance").SetValue(null, config);
    }
    private static ClientKingdomBehavior NewBehavior(bool newGame)
    {
        _behavior = new ClientKingdomBehavior();
        AccessTools.Field(Behavior, "<Instance>k__BackingField").SetValue(null, _behavior);
        if (newGame) Call(_behavior, "OnNewGameCreated", new object[] { null });
        return _behavior;
    }
    private static void Apply() => Call(_behavior, "OnAfterSessionLaunched", new object[] { null });
    private static bool Pending => (bool)Field(_behavior, "_startingClientagePending");
    private static List<ClientKingdomRecord> Records => (List<ClientKingdomRecord>)Field(_behavior, "_clients");

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.starting_clientage");
        object oldConfig = AccessTools.Field(Config, "_instance").GetValue(null);
        object oldBehavior = AccessTools.Field(Behavior, "<Instance>k__BackingField").GetValue(null);
        _file = Path.Combine(Path.GetTempPath(), "BellumStartingClientage_" + Guid.NewGuid().ToString("N") + ".xml");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(StartingClientageTests), prefix));
        try
        {
            var config = NewConfig();
            Load(config, Entry("client", "root") + "<StartingClientage client='forced' suzerain='root' submission='Forced' liberationCooldownDays='100' />");
            check(Entries(config).Count == 2 && (int)Field(Entries(config)[0], "LiberationCooldownDays") == 0
                && (bool)Field(Entries(config)[0], "Voluntary") && !(bool)Field(Entries(config)[1], "Voluntary")
                && (int)Field(Entries(config)[1], "LiberationCooldownDays") == 100, "XML loads voluntary/forced clientages and defaults cooldown to zero");
            check((string)Field(Entries(config)[0], "Source") == _file, "Declarations retain diagnostic source path");
            Load(config, Entry("forced", "new_root"));
            check(Entries(config).Count == 2 && (int)Field(Entries(config)[1], "LiberationCooldownDays") == 0
                && (string)Field(Entries(config)[1], "SuzerainId") == "new_root", "Later patches replace whole client entry, including default cooldown");
            Load(config, "<StartingClientage client='forced' remove='true' />");
            check(Entries(config).Count == 1, "Explicit removal cancels earlier declaration");
            Load(config, Entry("client", "ignored") + Entry("preset_client", "root"), true);
            check(Entries(config).Count == 1 && (string)Field(Entries(config)[0], "SuzerainId") == "root", "Terminology presets cannot add or overwrite clientage");
            foreach (string invalid in new[] {
                "<StartingClientage client='client' suzerain='root' submission='0' />",
                "<StartingClientage client='client' suzerain='root' submission='Forced' liberationCooldownDays='-1' />",
                "<StartingClientage client='client' suzerain='root' submission='Forced' liberationCooldownDays='1.5' />",
                "<StartingClientage client='client' suzerain='root' submission='Forced' liberationCooldownDays='2147483648' />",
                "<StartingClientage client='client' suzerain='root' />",
                "<StartingClientage client='client' submission='Forced' />",
                "<StartingClientage client='client' remove='maybe' />" })
            {
                config = NewConfig(); Load(config, Entry("client", "root")); Load(config, invalid);
                check(Entries(config).Count == 0, "Malformed override disables earlier entry: " + invalid);
            }
            config = NewConfig(); Load(config, "<StartingClientage suzerain='root' submission='Forced' />");
            check(Entries(config).Count == 0, "Client ID is required");
            DirectoryInfo repo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "BellumCivile.csproj"))) repo = repo.Parent;
            config = NewConfig();
            foreach (string filename in new[] { "bellum_feudal_titles.xml", "bellum_feudal_titles_patch.xml" })
                Call(config, "LoadFile", Path.Combine(repo.FullName, "ModuleData", filename), false, false);
            check(Entries(config).Count == 0, "Shipped XML examples do not change vanilla starting diplomacy");

            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Home));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.Method(Behavior.Assembly.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(IsTemporary));
            Patch(AccessTools.PropertyGetter(Behavior, "CurrentDay"), nameof(Today));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(AtWar));
            Patch(AccessTools.Method(typeof(DeclareWarAction), "ApplyByDefault"), nameof(Declare));
            Patch(AccessTools.Method(typeof(MakePeaceAction), "Apply"), nameof(Peace));
            Patch(AccessTools.Method(Behavior, "EndThirdPartyAgreements"), nameof(EndIndependent));
            Patch(AccessTools.Method(Behavior, "EnsureProtectedAgreements"), nameof(Agreements));
            Patch(AccessTools.Method(Behavior, "ShowClientageNotification"), nameof(Notice));
            var integration = Behavior.Assembly.GetType("BellumCivile.ModIntegrationHelper", true);
            Patch(AccessTools.Method(integration, "TryExpireDiplomacyNonAggressionPacts"), nameof(Expire));
            Patch(AccessTools.PropertyGetter(integration, "IsDiplomacyLoaded"), nameof(No));
            var alliancePatch = Behavior.Assembly.GetType("BellumCivile.Patches.StartingClientageAllianceWarPatch", true);
            harmony.CreateClassProcessor(alliancePatch).Patch();
            foreach (string id in new[] { "root_a", "root_b", "a1", "a2", "b1", "neutral", "temp" })
            {
                var realm = Blank<Kingdom>(); realm.StringId = id;
                var clan = Blank<Clan>(); clan.StringId = id + "_clan";
                Rulers[realm] = clan; Leaders[clan] = Blank<Hero>(); Realms.Add(realm);
            }
            Kingdom K(string id) => Realms.Single(realm => realm.StringId == id);
            Temporary.Add(K("temp"));
            _day = 10;

            UseConfig(Entry("a1", "root_a")); NewBehavior(false);
            var legacyStore = new Store { IsLoading = true }; _behavior.SyncData(legacyStore);
            Apply(); Call(_behavior, "OnSessionLaunched", new object[] { null }); Call(_behavior, "OnDailyTick");
            check(Records.Count == 0 && Actions.Count == 0 && !Pending, "Old saves with no startup fields are untouched, including daily maintenance");
            UseConfig(""); NewBehavior(true); Apply();
            check(!Pending && Records.Count == 0 && Actions.Count == 0, "New game without declarations is a no-op");

            foreach (string invalid in new[] {
                Entry("a1", "a1"), Entry("a1", "root_a") + Entry("root_a", "root_b"),
                Entry("a1", "a2") + Entry("a2", "a1"), Entry("a1", "missing"),
                Entry("missing", "root_a"), Entry("a1", "temp"), Entry("temp", "root_a"), Entry("a1", "ROOT_A") })
            {
                UseConfig(invalid); NewBehavior(true); Apply();
                check(!Pending && Records.Count == 0 && Actions.Count == 0, "Invalid graph/realm rejected before any diplomacy changes: " + invalid);
            }
            Dead.Add(Leaders[Rulers[K("root_a")]]); UseConfig(Entry("a1", "root_a")); NewBehavior(true); Apply();
            check(!Pending && Records.Count == 0 && Actions.Count == 0, "Dead suzerain rejected before mutation"); Dead.Clear();
            NewBehavior(true); Records.Add(new ClientKingdomRecord("a1", "root_b", 1, true, 1)); Apply();
            check(Records.Count == 1 && Records[0].SuzerainKingdomId == "root_b" && Actions.Count == 0, "Existing clientage is never overwritten");
            UseConfig(Entry("root_b", "root_a")); NewBehavior(true); Records.Add(new ClientKingdomRecord("b1", "root_b", 1, true, 1)); Apply();
            check(Records.Count == 1 && Actions.Count == 0, "Existing suzerain cannot become a nested client");

            string valid = Entry("a1", "root_a") + "<StartingClientage client='a2' suzerain='root_a' submission='Forced' liberationCooldownDays='100' />" + Entry("b1", "root_b");
            void InitialWars()
            {
                Wars.Clear(); Wars.Add(Pair(K("root_a"), K("root_b")));
                Wars.Add(Pair(K("a1"), K("root_a"))); Wars.Add(Pair(K("a1"), K("a2")));
                Wars.Add(Pair(K("a2"), K("neutral")));
            }
            void Aligned()
            {
                var left = new[] { K("root_a"), K("a1"), K("a2") };
                var right = new[] { K("root_b"), K("b1") };
                foreach (var a in left) foreach (var b in right)
                    if (!Wars.Contains(Pair(a, b))) throw new Exception("Cross-bloc hostility missing");
                if (Wars.Count != 6) throw new Exception("Independent or internal client wars remain");
            }
            _checkAlignment = Aligned;
            UseConfig(valid); InitialWars(); NewBehavior(true);
            Call(_behavior, "OnSessionLaunched", new object[] { null });
            check(Actions.Count == 0 && Pending, "New-game seeding waits for after-session initialization");
            Apply(); Aligned();
            check(!Pending && Records.Count == 3 && !Records.Single(r => r.ClientKingdomId == "a2").WasVoluntary
                && Records.Single(r => r.ClientKingdomId == "a2").LiberationCooldownUntilDay == 110
                && Records.Single(r => r.ClientKingdomId == "a1").LiberationCooldownUntilDay == 10,
                "Startup publishes voluntary/forced records with requested remaining cooldowns");
            check(Actions.Count(a => a.StartsWith("agreements:")) == 3 && _submissionNotices == 0,
                "Protected agreements follow complete bloc alignment without submission announcements");
            check(!(bool)Field(_behavior, "<IsApplyingStartingClientage>k__BackingField") && !_behavior.IsSynchronizingDiplomacy
                && (bool)AccessTools.Method(alliancePatch, "Prefix").Invoke(null, null), "Startup-only alliance suppression ends when initialization completes");
            var expectedWars = Wars.ToList(); int actionCount = Actions.Count; Apply();
            check(Actions.Count == actionCount, "Repeated startup callbacks do not replay diplomacy");
            var save = new Store(); _behavior.SyncData(save);
            Records.Clear(); _behavior.SyncData(save); save.IsLoading = true;
            UseConfig(Entry("a1", "root_b")); NewBehavior(false); _behavior.SyncData(save); Apply();
            check(Records.Count == 0 && Actions.Count == actionCount, "Save/reload after independence does not recreate configured clients, even with edited XML");

            UseConfig(Entry("b1", "root_b") + Entry("a2", "root_a", "liberationCooldownDays='100'") + Entry("a1", "root_a"));
            Realms.Reverse(); InitialWars(); Actions.Clear(); NewBehavior(true); Apply();
            check(!Pending && Wars.SetEquals(expectedWars), "Bloc diplomacy is independent of declaration and kingdom iteration order");
            Realms.Reverse();

            UseConfig(valid); InitialWars(); Actions.Clear(); NewBehavior(true); _throwWar = true; Apply();
            check(Pending && Records.Count == 0 && !_behavior.IsSynchronizingDiplomacy
                && !(bool)Field(_behavior, "<IsApplyingStartingClientage>k__BackingField"), "Interrupted alignment publishes no partial client records and releases guards");
            save = new Store(); _behavior.SyncData(save); save.IsLoading = true;
            UseConfig(Entry("a1", "neutral")); NewBehavior(false); _behavior.SyncData(save);
            Call(_behavior, "OnSessionLaunched", new object[] { null });
            _throwWar = false; _day = 12; Apply(); Aligned();
            check(!Pending && Records.Count == 3 && Records.Single(r => r.ClientKingdomId == "a1").SuzerainKingdomId == "root_a"
                && Records.All(r => r.StartedDay == 10) && Records.Single(r => r.ClientKingdomId == "a2").LiberationCooldownUntilDay == 110,
                "Interrupted save resumes frozen declarations and dates, not changed XML or reset cooldowns");

            _day = 20; UseConfig(valid); InitialWars(); Actions.Clear(); NewBehavior(true); _blockWar = true; Apply();
            check(Pending && Records.Count == 0 && !Actions.Any(a => a.StartsWith("agreements:")), "Silently blocked war action prevents publication and premature alliances");
            _blockWar = false; Apply(); check(!Pending && Records.Count == 3, "Blocked alignment can resume without duplicates");

            InitialWars(); Actions.Clear(); NewBehavior(true); _throwAgreement = true; Apply();
            check(!Pending && Records.Count == 3 && !_behavior.IsSynchronizingDiplomacy, "Agreement failure consumes startup plan after valid records are published");
            Records.RemoveAll(r => r.ClientKingdomId == "a1");
            save = new Store(); _behavior.SyncData(save); save.IsLoading = true;
            _throwAgreement = false; NewBehavior(false); _behavior.SyncData(save); _checkAlignment = null;
            Apply(); Call(_behavior, "OnSessionLaunched", new object[] { null });
            check(Records.Count == 2 && Records.All(r => r.ClientKingdomId != "a1"), "Agreement maintenance cannot recreate a client liberated after partial agreement setup");

            InitialWars(); Actions.Clear(); NewBehavior(true); _throwWar = true; Apply();
            check(Pending && Records.Count == 0, "Second interrupted startup retains pending plan");
            Wars.Remove(Pair(K("root_a"), K("root_b")));
            _throwWar = false; Apply();
            check(!Pending && Records.Count == 3 && Wars.Count == 0, "Retry follows suzerains' current peace instead of restarting a historical war");

            InitialWars(); Actions.Clear(); NewBehavior(true); _throwWar = true; Apply();
            Records.Add(new ClientKingdomRecord("a1", "neutral", 25, true, 25));
            int before = Actions.Count; _throwWar = false; Apply();
            check(!Pending && Records.Count == 1 && Records[0].SuzerainKingdomId == "neutral" && Actions.Count == before,
                "Retry cancels safely when another system establishes conflicting clientage");

            var context = new DefinitionContext();
            var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
            var basic = new SaveableBasicTypeDefiner(); initialize.Invoke(basic, new object[] { context });
            AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
            var definer = new BellumCivileSaveDefiner(); initialize.Invoke(definer, new object[] { context });
            Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"), nameof(OnlyClientRecord));
            Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"), nameof(OnlyClientList));
            Call(definer, "DefineClassTypes"); Call(definer, "DefineContainerDefinitions");
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { typeof(ClientKingdomRecord) }) != null
                && AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                .Invoke(context, new object[] { typeof(List<ClientKingdomRecord>) }) != null && !context.GotError,
                "Pending startup snapshot reuses registered native save class and container without new type IDs");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.Field(Config, "_instance").SetValue(null, oldConfig);
            AccessTools.Field(Behavior, "<Instance>k__BackingField").SetValue(null, oldBehavior);
            if (File.Exists(_file)) File.Delete(_file);
            Realms.Clear(); Rulers.Clear(); Leaders.Clear(); Wars.Clear(); Actions.Clear(); Temporary.Clear(); Dead.Clear();
            _throwWar = _blockWar = _throwAgreement = false; _checkAlignment = null;
        }
    }
}
