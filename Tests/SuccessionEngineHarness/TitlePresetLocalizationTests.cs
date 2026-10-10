using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

internal static class TitlePresetLocalizationTests
{
    private static readonly Type Config = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalTitleConfig", true);
    private static List<ModuleInfo> _activeModules;
    private static bool ActiveModules(ref List<ModuleInfo> __result) { __result = _activeModules; return false; }
    private static bool Day(ref float __result) { __result = 100; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.title_preset_localization");
        var configField = AccessTools.Field(Config, "_instance");
        object previousConfig = configField.GetValue(null);
        var realmConfig = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.RealmNameConfig", true);
        var realmEntries = AccessTools.Field(realmConfig, "_entries");
        object previousRealmEntries = realmEntries.GetValue(null);
        var language = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageId");
        var languageIndex = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageIndex");
        object previousLanguage = language.GetValue(null), previousIndex = languageIndex.GetValue(null);
        var translations = (IDictionary)AccessTools.Field(typeof(LocalizedTextManager), "_gameTextDictionary").GetValue(null);
        var previousTexts = new Dictionary<string, object>();
        string temp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BellumTitlePresetTests_" + Guid.NewGuid().ToString("N")));
        int languageRevision = 1700;
        void Language(string name)
        {
            language.SetValue(null, name);
            languageIndex.SetValue(null, ++languageRevision);
        }
        void Translate(string key, string value)
        {
            if (!previousTexts.ContainsKey(key)) previousTexts[key] = translations[key];
            translations[key] = value;
        }
        object NewConfig() => Activator.CreateInstance(Config, true);
        void Load(object config, string path) => AccessTools.Method(Config, "LoadFile")
            .Invoke(config, new object[] { path, false, true });
        IDictionary Names(object config) => (IDictionary)AccessTools.Field(Config, "_titleNames").GetValue(config);
        string Field(object value, string field) => (string)AccessTools.Field(value.GetType(), field).GetValue(value);
        try
        {
            DirectoryInfo repository = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (repository != null && !File.Exists(Path.Combine(repository.FullName, "BellumCivile.csproj")))
                repository = repository.Parent;
            if (repository == null) throw new InvalidOperationException("Could not locate shipped preset XML.");
            string data = Path.Combine(repository.FullName, "ModuleData");
            var catalog = new XmlDocument(); catalog.Load(Path.Combine(data, "Languages", "EN", "strings.xml"));
            var english = catalog.SelectNodes("/base/strings/string").Cast<XmlElement>()
                .ToDictionary(node => node.GetAttribute("id"), node => node.GetAttribute("text"), StringComparer.Ordinal);

            foreach (string preset in new[] { "anglicized", "immersive", "bannerkings" })
            {
                string path = Path.Combine(data, "bellum_title_styles_" + preset + ".xml");
                var document = new XmlDocument(); document.Load(path);
                object config = NewConfig(); Load(config, path);
                Language("English");
                string root = (string)Names(config)["bc_title_kingdom_vlandia"];
                var rootText = new TextObject(root);
                string fallback = root.Substring(root.IndexOf('}') + 1);
                check(rootText.GetID().Length > 0 && rootText.ToString() == fallback,
                    preset + ": real preset preserves English Crown name with a localization key");
                check(rootText.GetID() != "FjwRsf1C" || preset == "anglicized",
                    preset + ": distinct terminology does not reuse the native Vlandia key");
                var label = new TextObject(document.DocumentElement.GetAttribute("presetName"));
                check(label.GetID().StartsWith("BC_"), preset + ": preset label is keyed");
                var declaredRoots = document.SelectNodes("/BellumFeudalTitles/TitleName").Cast<XmlElement>()
                    .GroupBy(node => node.GetAttribute("id"), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last().GetAttribute("name"), StringComparer.OrdinalIgnoreCase);
                check(Names(config).Count == declaredRoots.Count
                    && declaredRoots.All(entry => (string)Names(config)[entry.Key] == entry.Value),
                    preset + ": all shipped title roots load without identity changes");

                var styles = (IList)AccessTools.Field(Config, "_styles").GetValue(config);
                object vlandian = styles.Cast<object>().First(style => Field(style, "CultureRef") == "Vlandia");
                var ranks = (IList)AccessTools.Field(vlandian.GetType(), "Ranks").GetValue(vlandian);
                object crown = ranks.Cast<object>().First(rank =>
                    (FeudalTitleType)AccessTools.Field(rank.GetType(), "Tier").GetValue(rank) == FeudalTitleType.Kingdom);
                string noun = Field(crown, "TitleName"), male = Field(crown, "MaleRank"), female = Field(crown, "FemaleRank");
                foreach (string raw in new[] { noun, male, female })
                {
                    var text = new TextObject(raw);
                    check(text.GetID().StartsWith("BC_") && text.ToString() == english[text.GetID()],
                        preset + ": shipped rank keeps its original fallback: " + text);
                    Translate(text.GetID(), "Translated " + text);
                }
                Translate(rootText.GetID(), "Translated " + preset);
                Translate(label.GetID(), "Preset " + preset);
                Language("BellumPresetTestLanguage");
                check(rootText.ToString() == "Translated " + preset, preset + ": loaded Crown root translates");
                check(label.ToString() == "Preset " + preset, preset + ": loaded preset label translates");
                check(new[] { noun, male, female }.All(raw => new TextObject(raw).ToString().StartsWith("Translated ")),
                    preset + ": loaded landed noun and both holder ranks translate");

                XmlAttribute formatAttribute = document.SelectSingleNode("/BellumFeudalTitles/TitleStyle/Rank/@titleFormat") as XmlAttribute;
                var format = new TextObject(formatAttribute.Value);
                Translate(format.GetID(), "{TITLE_NAME} / {TITLE_NOUN}");
                format.SetTextVariable("TITLE_NAME", rootText);
                format.SetTextVariable("TITLE_NOUN", new TextObject(noun));
                check(format.ToString() == rootText + " / " + new TextObject(noun),
                    preset + ": translated format can reorder nested localized roots and ranks");
                Language("English");
                check(rootText.ToString() == fallback, preset + ": the same TextObject follows a language switch back");
                check((string)Names(config)["bc_title_kingdom_vlandia"] == root,
                    preset + ": language changes do not rewrite configuration or title IDs");
            }

            harmony.Patch(AccessTools.Method(typeof(ModuleHelper), "GetActiveModules"),
                prefix: new HarmonyMethod(typeof(TitlePresetLocalizationTests), nameof(ActiveModules)));
            ModuleInfo Module(string path)
            {
                var module = new ModuleInfo();
                AccessTools.PropertySetter(typeof(ModuleInfo), "FolderPath").Invoke(module, new object[] { path });
                return module;
            }
            ModuleInfo Patch(string folder, string key)
            {
                string module = Path.GetFullPath(Path.Combine(temp, folder));
                Directory.CreateDirectory(Path.Combine(module, "ModuleData"));
                var document = new XmlDocument();
                document.LoadXml("<BellumFeudalTitles><TitleName id='bc_title_kingdom_vlandia' name='{=" + key + "}Patch name' />"
                    + "<TitleStyle culture='Vlandia'><Rank tier='Kingdom' maleRank='{=" + key + "_Rank}Patch rank' /></TitleStyle></BellumFeudalTitles>");
                document.Save(Path.Combine(module, "ModuleData", "bellum_feudal_titles_patch.xml"));
                return Module(module);
            }
            ModuleInfo early = Patch("Modules/Z_Early", "BC_Test_Early");
            ModuleInfo own = Patch("Modules/Bellum", "BC_Test_Own");
            ModuleInfo late = Patch("External/A_Late", "BC_Test_Late");
            Patch("Modules/Disabled", "BC_Test_Disabled");
            _activeModules = new List<ModuleInfo> { early, own, null, Module(""), Module(Path.Combine(temp, "Missing")), late, own };
            string[] Paths() => ((IEnumerable<string>)AccessTools.Method(Config, "FindPatchFiles").Invoke(null, null)).ToArray();
            string[] paths = Paths();
            check(paths.Length == 3, "Only active, existing patches load; missing, empty and duplicate paths are skipped");
            check(paths.SequenceEqual(new[] { early, own, late }.Select(module =>
                    Path.Combine(module.FolderPath, "ModuleData", "bellum_feudal_titles_patch.xml"))),
                "Patches follow active load order, including Bellum and external module locations");
            object merged = NewConfig();
            Load(merged, Path.Combine(data, "bellum_title_styles_anglicized.xml"));
            foreach (string path in paths) Load(merged, path);
            check((string)Names(merged)["bc_title_kingdom_vlandia"] == "{=BC_Test_Late}Patch name",
                "Last active patch overrides the selected preset and earlier patches");
            object mergedVlandian = ((IList)AccessTools.Field(Config, "_styles").GetValue(merged))
                .Cast<object>().First(style => Field(style, "CultureRef") == "Vlandia");
            object mergedCrown = ((IList)AccessTools.Field(mergedVlandian.GetType(), "Ranks").GetValue(mergedVlandian))
                .Cast<object>().First(rank => (FeudalTitleType)AccessTools.Field(rank.GetType(), "Tier").GetValue(rank) == FeudalTitleType.Kingdom);
            check(Field(mergedCrown, "MaleRank") == "{=BC_Test_Late_Rank}Patch rank",
                "Rank overrides follow the same active patch order as territorial roots");
            check(Field(mergedCrown, "FemaleRank").StartsWith("{=BC_TitleStyle_Anglicized_"),
                "Partial style patch preserves the other localized preset fields");
            _activeModules = new List<ModuleInfo> { late, own, early };
            object reversed = NewConfig();
            foreach (string path in Paths()) Load(reversed, path);
            check((string)Names(reversed)["bc_title_kingdom_vlandia"] == "{=BC_Test_Early}Patch name",
                "Changing active module order changes precedence, independently of directory names");
            _activeModules.Clear();
            check(Paths().Length == 0, "Installed patches are not discovered when no module is active");

            var realmDocument = new XmlDocument();
            realmDocument.LoadXml("<RealmNames><Realm id='vlandia' nativeName='{=BC_Test_NativeGuard}Vlandia' "
                + "root='{=BC_Test_CustomRoot}Custom realm' /></RealmNames>");
            string realmPath = Path.Combine(own.FolderPath, "ModuleData", "bellum_realm_names_patch.xml");
            realmDocument.Save(realmPath);
            _activeModules.Add(own);
            realmEntries.SetValue(null, null);
            TextObject ResolveRoot(string native) => (TextObject)AccessTools.Method(realmConfig, "ResolveRootText")
                .Invoke(null, new object[] { "vlandia", native });
            Translate("FjwRsf1C", "Translated native Vlandia");
            Translate("BC_Test_NativeGuard", "Translated native Vlandia");
            Translate("BC_Test_CustomRoot", "Translated custom realm");
            Language("BellumPresetTestLanguage");
            TextObject custom = ResolveRoot(new TextObject("{=FjwRsf1C}Vlandia").ToString());
            check(custom != null && custom.ToString() == "Translated custom realm",
                "Realm root and guard can use different custom keys; matching uses translated native text");
            check(ResolveRoot("Player rename") == null, "A custom realm-name translation does not override an unrelated player rename");
            realmDocument.DocumentElement.FirstChild.Attributes["root"].Value = "{=BC_Test_UnregisteredRoot}Fallback realm";
            realmDocument.DocumentElement.FirstChild.Attributes["nativeName"].Value = "";
            realmDocument.Save(realmPath);
            realmEntries.SetValue(null, null);
            check(ResolveRoot("Different identity").ToString() == "Fallback realm",
                "Explicitly unguarded roots use their fallback when a custom translation is missing");
            Language("English");

            // Exercise the saved-record repair without campaign time or native object registries.
            harmony.Patch(AccessTools.PropertyGetter(typeof(FeudalTitleBehavior), "CurrentDay"),
                prefix: new HarmonyMethod(typeof(TitlePresetLocalizationTests), nameof(Day)));
            var behavior = new FeudalTitleBehavior();
            object realConfig = NewConfig(); Load(realConfig, Path.Combine(data, "bellum_title_styles_anglicized.xml"));
            configField.SetValue(null, realConfig);
            var titles = (IDictionary)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(behavior);
            foreach (DictionaryEntry entry in Names(realConfig))
                titles[entry.Key] = new FeudalTitleRecord((string)entry.Key, "Old literal root", FeudalTitleType.Kingdom,
                    "legal_holder", "holder", "", "capital", "realm", 0, 0);
            var vlandia = (FeudalTitleRecord)titles["bc_title_kingdom_vlandia"];
            var renamed = (FeudalTitleRecord)titles["bc_title_kingdom_sturgia"];
            renamed.SetName("Player's chosen name");
            ((IDictionary)AccessTools.Field(typeof(FeudalTitleBehavior), "_playerTitleNameOverrides").GetValue(behavior))
                [renamed.TitleId] = renamed.Name;
            AccessTools.Method(typeof(FeudalTitleBehavior), "ApplyConfiguredTitleNames").Invoke(behavior, new object[] { "localization test" });
            check(vlandia.Name == "{=FjwRsf1C}Vlandia", "Existing unkeyed Crown root picks up the preset's native translation token");
            check(vlandia.DeFactoHolderClanId == "holder" && vlandia.DeJureHolderClanId == "legal_holder"
                && vlandia.CapitalSettlementId == "capital" && vlandia.AssociatedKingdomId == "realm",
                "Localizing an existing title leaves holders, capital and realm intact");
            check(renamed.Name == "Player's chosen name", "Saved player title renames survive preset localization");
            check((int)AccessTools.Method(typeof(FeudalTitleBehavior), "ApplyConfiguredTitleNames")
                .Invoke(behavior, new object[] { "repeat localization test" }) == 0, "Repeated preset name repair is idempotent");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _activeModules = null;
            configField.SetValue(null, previousConfig);
            realmEntries.SetValue(null, previousRealmEntries);
            language.SetValue(null, previousLanguage); languageIndex.SetValue(null, previousIndex);
            foreach (var entry in previousTexts)
            {
                if (entry.Value == null) translations.Remove(entry.Key);
                else translations[entry.Key] = entry.Value;
            }
            if (Directory.Exists(temp) && Path.GetDirectoryName(temp).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(temp, true);
        }
    }
}
