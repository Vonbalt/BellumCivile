using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class RealmNameTests
{
    private static readonly Assembly Assembly = typeof(FeudalTitleRecord).Assembly;
    private static Type Type(string name) => Assembly.GetType("BellumCivile." + name, true);
    private static object Call(string type, string method, params object[] args) => AccessTools.Method(Type(type), method).Invoke(null, args);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    // Reflection avoids the harness JIT inlining native getters before Harmony is installed.
    private static TextObject Full => (TextObject)AccessTools.Property(typeof(Kingdom), "Name").GetValue(_realm);
    private static TextObject Short => (TextObject)AccessTools.Property(typeof(Kingdom), "InformalName").GetValue(_realm);
    private static TextObject Encyclopedia => (TextObject)AccessTools.Property(typeof(Kingdom), "EncyclopediaTitle").GetValue(_realm);
    private static void Rename(TextObject full, TextObject shortName) => AccessTools.Method(typeof(Kingdom), "ChangeKingdomName")
        .Invoke(_realm, new object[] { full, shortName });
    private static Clan _house;
    private static Kingdom _realm;
    private static Hero _leader;
    private static FeudalTitleRecord _title;
    private static int _resolutions;
    private static bool RulingHouse(ref Clan __result) { __result = _house; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Banner(ref TaleWorlds.Core.Banner __result) { __result = null; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm }); return false; }
    private static bool Sovereign(ref FeudalTitleRecord __result)
    { _resolutions++; __result = _title; return false; }
    private static bool Formal(Kingdom kingdom, ref TextObject __result)
    { __result = new TextObject("Native formal " + kingdom.StringId); return false; }
    private static bool SettingsDefinitions(object settings, ref object __result)
    {
        var discoverer = settings.GetType().BaseType.Assembly.GetType("MCM.Implementation.AttributeSettingsPropertyDiscoverer");
        __result = AccessTools.Method(discoverer, "GetPropertiesInternal").Invoke(null, new[] { settings });
        return false;
    }
    private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>
        {
            ["BC_Test_RealmRoot"] = "Valandie", ["BC_Test_NativeRealm"] = "Valandie",
            ["BC_Test_RealmRank"] = "Royaume", ["BC_Test_RealmFormat"] = "{TITLE_NAME} ({TITLE_NOUN})",
            ["BC_Test_Territory"] = "Morcombie"
        };

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.realm_names");
        var settingsField = AccessTools.Field(Type("BellumCivileOptions"), "_settings");
        var configField = AccessTools.Field(Type("FeudalTitleConfig"), "_instance");
        var previousSettings = settingsField.GetValue(null);
        var previousConfig = configField.GetValue(null);
        var previousTitles = FeudalTitleBehavior.Instance;
        var languageField = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageId");
        var languageIndex = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageIndex");
        object previousLanguage = languageField.GetValue(null), previousIndex = languageIndex.GetValue(null);
        var languageTexts = (IDictionary)AccessTools.Field(typeof(LocalizedTextManager), "_gameTextDictionary").GetValue(null);
        var previousTexts = Translations.Keys.ToDictionary(key => key, key => languageTexts[key]);
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(RealmNameTests), prefix));
        void Mode(object settings, int mode)
        {
            var dropdown = AccessTools.Property(settings.GetType(), "RealmNameDisplay").GetValue(settings);
            AccessTools.Property(dropdown.GetType(), "SelectedIndex").SetValue(dropdown, mode);
        }
        int Selected(object settings)
        {
            var dropdown = AccessTools.Property(settings.GetType(), "RealmNameDisplay").GetValue(settings);
            return (int)AccessTools.Property(dropdown.GetType(), "SelectedIndex").GetValue(dropdown);
        }
        void Revise() => AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "DisplayRevision")
            .Invoke(FeudalTitleBehavior.Instance, new object[] { FeudalTitleBehavior.Instance.DisplayRevision + 1 });
        void AssertNames(string full, string shortName, string context)
        {
            check(Full.ToString() == full, context + ": full name (" + Full + ")");
            check(Short.ToString() == shortName, context + ": short name");
            check(Encyclopedia.ToString() == full, context + ": encyclopedia title");
            check(CampaignSceneNotificationHelper.GetFormalNameForKingdom(_realm).ToString() == full, context + ": formal name");
        }
        void AddRoot(string id, string native, string root)
        {
            var entryType = Type("RealmNameConfig").GetNestedType("Entry", BindingFlags.NonPublic);
            var dictionary = (IDictionary)AccessTools.Field(Type("RealmNameConfig"), "_entries").GetValue(null);
            if (dictionary == null)
            {
                dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), entryType));
                AccessTools.Field(Type("RealmNameConfig"), "_entries").SetValue(null, dictionary);
            }
            var entry = Activator.CreateInstance(entryType, true);
            AccessTools.Field(entryType, "Root").SetValue(entry, root);
            AccessTools.Field(entryType, "NativeName").SetValue(entry, native);
            dictionary[id] = entry;
        }
        void SaveCheck(Kingdom realm, string name, string shortName, string encyclopedia)
        {
            var objects = new List<object>();
            AccessTools.Method(typeof(Kingdom), "AutoGeneratedInstanceCollectObjects").Invoke(realm, new object[] { objects });
            string[] fields = { "Name", "InformalName", "EncyclopediaTitle" };
            string[] values = { name, shortName, encyclopedia };
            for (int i = 0; i < fields.Length; i++)
            {
                var value = (TextObject)AccessTools.Method(typeof(Kingdom), "AutoGeneratedGetMemberValue" + fields[i])
                    .Invoke(null, new object[] { realm });
                check(value.ToString() == values[i], "Save writer retains native " + fields[i]);
                check(objects.Any(o => ReferenceEquals(o, value)), "Save collector and writer share " + fields[i] + " object identity");
            }
        }
        try
        {
            Call("DynamicKingdomTitleNameHelper", "BeginCampaign");
            object settings = Activator.CreateInstance(Type("BellumCivileSettings"), true);
            settingsField.SetValue(null, settings);
            Assembly mcm = settings.GetType().BaseType.Assembly;
            Patch(AccessTools.Method(mcm.GetType("MCM.Abstractions.BaseSettingsExtensions"), "GetAllSettingPropertyDefinitions"), nameof(SettingsDefinitions));
            h.CreateClassProcessor(Type("Patches.RealmNameSettingsMigrationPatch")).Patch();
            object format = Activator.CreateInstance(mcm.GetType("MCM.Implementation.JsonSettingsFormat"), new object[] { null });
            object Load(string json)
            {
                object loaded = Activator.CreateInstance(Type("BellumCivileSettings"), true);
                AccessTools.Method(format.GetType(), "LoadFromJson").Invoke(format, new[] { loaded, json });
                return loaded;
            }
            check(Selected(settings) == 2, "New installations use Sovereign Title as the default");
            check(Selected(Load("{}")) == 2, "Settings without a saved naming preference use Sovereign Title");
            foreach (bool legacy in new[] { false, true })
            {
                object old = Load("{\"UseSovereignTitlesAsRealmNames\":" + legacy.ToString().ToLowerInvariant() + "}");
                check(Selected(old) == (legacy ? 2 : 1), "Legacy boolean migrates: " + legacy);
            }
            for (int index = 0; index < 3; index++)
            {
                Mode(settings, index);
                string json = (string)AccessTools.Method(format.GetType(), "SaveJson").Invoke(format, new[] { settings });
                check(json.Contains("RealmNameDisplay") && !json.Contains("UseSovereignTitlesAsRealmNames"),
                    "MCM serializes only the visible selector: " + index);
                check(Selected(Load(json)) == index, "MCM selector survives a settings roundtrip: " + index);
            }
            Mode(settings, 0);
            Call("Patches.RealmNameSettingsMigrationPatch", "Prefix", settings, "{\"RealmNameDisplay\":0,\"UseSovereignTitlesAsRealmNames\":true}");
            check(Selected(settings) == 0, "Explicit new selector wins over stale legacy preference");
            check(Selected(Load("{\"UseSovereignTitlesAsRealmNames\":true,\"RealmNameDisplay\":0}")) == 0,
                "MCM loads a modern Native selection without reviving the legacy toggle");
            Mode(settings, 99);
            check((RealmNameDisplayMode)AccessTools.Property(Type("BellumCivileOptions"), "RealmNameDisplay").GetValue(null)
                == RealmNameDisplayMode.SovereignTitle, "Invalid selector safely uses the default");
            Mode(settings, 1);

            object config = Activator.CreateInstance(Type("FeudalTitleConfig"), true);
            var xml = new XmlDocument();
            xml.LoadXml("<TitleStyle kingdom='vlandia'><Rank tier='Kingdom' titleName='{=BC_Test_RealmRank}Kingdom' titleFormat='{=BC_Test_RealmFormat}{TITLE_NOUN} of {TITLE_NAME}' /></TitleStyle>");
            var style = AccessTools.Method(Type("FeudalTitleConfig"), "ReadTitleStyle").Invoke(null, new object[] { xml.DocumentElement });
            ((IList)AccessTools.Field(config.GetType(), "_styles").GetValue(config)).Add(style);
            configField.SetValue(null, config);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { new FeudalTitleBehavior() });
            _realm = Blank<Kingdom>(); _realm.StringId = "vlandia";
            _house = Blank<Clan>(); _house.StringId = "test_house";
            _leader = Blank<Hero>();
            _title = new FeudalTitleRecord("crown", "{=BC_Test_Territory}Morcomb", FeudalTitleType.Kingdom,
                _house.StringId, _house.StringId, "", "", _realm.StringId, 0, 0);
            _realm.ChangeKingdomName(new TextObject("{=BC_Test_NativeRealm}Vlandia"), new TextObject("Vlandian short"));
            AccessTools.PropertySetter(typeof(Kingdom), "EncyclopediaTitle").Invoke(_realm, new object[] { new TextObject("Native encyclopedia") });
            AddRoot("vlandia", "{=BC_Test_NativeRealm}Vlandia", "{=BC_Test_RealmRoot}Vlandia");
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(RulingHouse));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(RulingHouse));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Banner"), nameof(Banner));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(Sovereign));
            Patch(AccessTools.Method(typeof(CampaignSceneNotificationHelper), "GetFormalNameForKingdom"), nameof(Formal));
            h.CreateClassProcessor(Type("Patches.DynamicKingdomTitleNamePatch")).Patch();
            h.CreateClassProcessor(Type("Patches.DynamicKingdomTitleNameSaveBoundaryPatch")).Patch();
            _resolutions = 0;
            AssertNames("Kingdom of Vlandia", "Vlandia", "Identity mode");
            for (int i = 0; i < 1000; i++) { var name = Full; var shortName = Short; }
            check(_resolutions == 1, "Two thousand full/short reads reuse one sovereign lookup");
            check(Full.Value == "{=BC_Test_RealmFormat}{TITLE_NOUN} of {TITLE_NAME}"
                && ((TextObject)Full.Attributes["TITLE_NAME"]).Value == "{=BC_Test_RealmRoot}Vlandia",
                "Display names retain localized format and root tokens");
            SaveCheck(_realm, "Vlandia", "Vlandian short", "Native encyclopedia");
            AssertNames("Kingdom of Vlandia", "Vlandia", "Save suppression has ended");
            Mode(settings, 2);
            AssertNames("Kingdom of Morcomb", "Morcomb", "Sovereign mode");
            _title.SetName("New Morcomb"); Revise();
            AssertNames("Kingdom of New Morcomb", "New Morcomb", "Title rename invalidates cache");
            var kingdomTitle = _title;
            _title = new FeudalTitleRecord("county", "Morcomb", FeudalTitleType.County,
                _house.StringId, _house.StringId, "", "", _realm.StringId, 0, 0); Revise();
            AssertNames("County of Morcomb", "Morcomb", "Independent county rank");
            _title = new FeudalTitleRecord("empire", "Calradia", FeudalTitleType.Empire,
                _house.StringId, _house.StringId, "", "", _realm.StringId, 0, 0); Revise();
            AssertNames("Empire of Calradia", "Calradia", "Sovereign promotion");
            _title = kingdomTitle; Revise();
            int before = _resolutions;
            _leader = Blank<Hero>(); Full.ToString();
            check(_resolutions == before + 1, "New ruler within the same house invalidates style context");
            var projected = Full; var projectedShort = Short;
            Mode(settings, 0); Full.ToString();
            Rename(projected, projectedShort);
            check(Full.ToString() == "Vlandia" && Short.ToString() == "Vlandian short",
                "Projected readback after cache invalidation cannot overwrite native full/short names");
            check(Encyclopedia.ToString() == "Native encyclopedia"
                && CampaignSceneNotificationHelper.GetFormalNameForKingdom(_realm).ToString() == "Native formal vlandia",
                "Native mode leaves original encyclopedia and formal sources intact");
            Mode(settings, 1);
            AssertNames("Kingdom of Vlandia", "Vlandia", "Readback does not become an identity rename");

            _title.SetName("{=BC_Test_Territory}Morcomb"); Revise();
            var savedDisplay = Full;
            foreach (var translation in Translations) languageTexts[translation.Key] = translation.Value;
            languageField.SetValue(null, "BellumTestLanguage"); languageIndex.SetValue(null, 42);
            AssertNames("Valandie (Royaume)", "Valandie", "Translated identity, rank and word order");
            check(savedDisplay.ToString() == "Valandie (Royaume)", "Previously returned TextObject also follows language changes");
            Mode(settings, 2);
            AssertNames("Morcombie (Royaume)", "Morcombie", "Translated sovereign territory");
            languageField.SetValue(null, previousLanguage); languageIndex.SetValue(null, previousIndex);

            Call("Patches.ArtemRealmNameCompatibility", "TryApply", h);
            foreach (bool alternate in new[] { false, true })
            {
                var view = new ArtemsBetterUIVisuals.BetterUIVisualsKingdomLabelsView { Realm = _realm, Alternate = alternate };
                view.RebuildLabels();
                var label = view.Labels.Single();
                check(label.KingdomName == "Kingdom of Morcomb", "Artem route renders exactly one rank: alternate=" + alternate);
                Mode(settings, 1); view.OnMapScreenUpdate(1.1f);
                check(ReferenceEquals(label, view.Labels.Single()) && label.KingdomName == "Kingdom of Vlandia",
                    "Artem setting refresh updates the existing label without a border/movie rebuild");
                Mode(settings, 0); view.OnMapScreenUpdate(1.1f);
                check(label.KingdomName == (alternate ? "Kingdom of Vlandia" : "Native formal vlandia"),
                    "Artem retains its own naming choice in Native mode");
                Mode(settings, 2);
            }
            var refreshing = new ArtemsBetterUIVisuals.BetterUIVisualsKingdomLabelsView { Realm = _realm, Alternate = true };
            refreshing.RebuildLabels();
            _title.SetName("Other Land"); Revise(); refreshing.OnMapScreenUpdate(1.1f);
            check(refreshing.Labels.Single().KingdomName == "Kingdom of Other Land", "Artem refreshes changed sovereign names");
            refreshing.ThrowOnRebuild = true;
            try { refreshing.RebuildLabels(); } catch (InvalidOperationException) { }
            check(AccessTools.Field(Type("Patches.ArtemRealmNameCompatibility"), "_capture").GetValue(null) == null,
                "Failed third-party rebuild restores the thread capture scope");

            Mode(settings, 1);
            Rename(new TextObject("{=!}My Realm"), new TextObject("My Realm"));
            AssertNames("Kingdom of My Realm", "My Realm", "Explicit realm rename updates identity mode");
            Mode(settings, 2);
            AssertNames("Kingdom of Other Land", "Other Land", "Realm rename does not rename the legal title");
            SaveCheck(_realm, "My Realm", "My Realm", "Native encyclopedia");
            _realm.StringId = "foreign_conversion"; Mode(settings, 1);
            check(Full.ToString() == "My Realm", "Unconfigured conversion identity stays native");
            _realm.StringId = "vlandia_rebels_1"; Mode(settings, 2);
            check(Full.ToString() == "My Realm" && Short.ToString() == "My Realm", "Temporary rebellion retains native names");
            _realm.StringId = "bc_feud_1";
            check(Full.ToString() == "My Realm", "Temporary feud retains its native name");
            _realm.StringId = "vlandia";
            Mode(settings, 1);
            Rename(new TextObject("{=BC_Test_RealmRoot}Vlandia"), new TextObject("Vlandian short"));
            languageField.SetValue(null, "BellumTestLanguage"); languageIndex.SetValue(null, 42);
            AssertNames("Valandie (Royaume)", "Valandie", "Remembered realm rename retains its localization token");
            languageField.SetValue(null, previousLanguage); languageIndex.SetValue(null, previousIndex);
            Rename(new TextObject("{=!}My Realm"), new TextObject("My Realm"));
            Mode(settings, 2);
            Call("DynamicKingdomTitleNameHelper", "CaptureNativeNamesAfterLoad");
            SaveCheck(_realm, "My Realm", "My Realm", "Native encyclopedia");

            Call("DynamicKingdomTitleNameHelper", "BeginCampaign");
            SaveCheck(_realm, "My Realm", "My Realm", "Native encyclopedia");
            check(true, "Saving before any display read preserves collector/value identity for all three fields");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            Call("DynamicKingdomTitleNameHelper", "BeginCampaign");
            settingsField.SetValue(null, previousSettings); configField.SetValue(null, previousConfig);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { previousTitles });
            languageField.SetValue(null, previousLanguage); languageIndex.SetValue(null, previousIndex);
            _realm = null; _house = null; _leader = null; _title = null;
            foreach (var previous in previousTexts)
            {
                if (previous.Value == null) languageTexts.Remove(previous.Key);
                else languageTexts[previous.Key] = previous.Value;
            }
        }
    }
}

// Only the audited public naming call sites are reproduced; no game rendering or
// copied third-party implementation is needed to exercise the adapter's real IL.
namespace ArtemsBetterUIVisuals
{
    public sealed class BetterUIVisualsKingdomLabelVM
    {
        public string KingdomName { get; set; }
        public BetterUIVisualsKingdomLabelVM(string name) { KingdomName = name; }
    }
    public sealed class BetterUIVisualsKingdomLabelsView
    {
        public Kingdom Realm;
        public bool Alternate, ThrowOnRebuild;
        public readonly List<BetterUIVisualsKingdomLabelVM> Labels = new List<BetterUIVisualsKingdomLabelVM>();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RebuildLabels()
        {
            Labels.Clear();
            string name = Alternate
                ? new TextObject("{=ABUV16}Kingdom of {KINGDOM_NAME}").SetTextVariable("KINGDOM_NAME", Realm.Name).ToString()
                : CampaignSceneNotificationHelper.GetFormalNameForKingdom(Realm).ToString();
            if (ThrowOnRebuild) throw new InvalidOperationException("Test rebuild interrupted");
            Labels.Add(new BetterUIVisualsKingdomLabelVM(name));
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnMapScreenUpdate(float dt) { }
    }
}
