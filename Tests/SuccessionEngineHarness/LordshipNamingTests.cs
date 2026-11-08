using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

internal static class LordshipNamingTests
{
    private static readonly Dictionary<Clan, CultureObject> Cultures = new Dictionary<Clan, CultureObject>();
    private static readonly Dictionary<string, TextObject> DialogueVariables = new Dictionary<string, TextObject>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Culture(Clan __instance, ref CultureObject __result) { Cultures.TryGetValue(__instance, out __result); return false; }
    private static bool VillageName(ref TextObject __result) { __result = new TextObject("{=BC_Test_LordshipVillage}Calioc"); return false; }
    private static bool DialogueText(string __0, TextObject __1) { DialogueVariables[__0] = __1; return false; }
    private static bool NoPlayerClan(ref Clan __result) { __result = null; return false; }

    private static (string Noun, string Name, string Male, string Female) Expected(string preset, string culture)
    {
        if (preset == "anglicized") return ("Lordship", "Lordship of Calioc", "Sir", "Dame");
        if (culture.StartsWith("Empire", StringComparison.OrdinalIgnoreCase))
            return ("Pronoia", preset == "immersive" ? "Pronoia t\u014Dn Calioc" : "Pronoia Calioc", "Pronoiarios", "Pronoiarissa");
        switch (culture)
        {
            case "Vlandia": return preset == "immersive" ? ("Feodum", "Feodum Calioc", "Miles", "Militissa")
                : ("Fief", "Calioc Fief", "Chevaler", "Chevaleresse");
            case "Battania": return preset == "immersive" ? ("Baile", "Baile na Calioc", "Ridire", "Banridire")
                : ("Maenor", "Maenor y Calioc", "Marchog", "Marchoges");
            case "Khuzait": return ("Nutug", "Calioc Nutug", "N\u00F6k\u00F6r", "N\u00F6k\u00F6r");
            case "Aserai": return ("Iqta", "Iqta Calioc", "Faris", "Farisa");
            case "Sturgia": return ("Pomestie", "Pomestie Calioc", "Druzhinnik", "Druzhinnitsa");
            case "Nord": return ("L\u00E9n", "L\u00E9n Calioc", "Hir\u00F0ma\u00F0r", "Hir\u00F0kona");
            case "Rhodok": return ("Lehen", "Lehen Calioc", "Riter", "Riterin");
            default: throw new InvalidOperationException("Unknown lordship culture: " + culture);
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.lordship-naming");
        var mod = typeof(FeudalTitleRecord).Assembly;
        var configType = mod.GetType("BellumCivile.FeudalTitleConfig", true);
        var configField = AccessTools.Field(configType, "_instance");
        var oldConfig = configField.GetValue(null);
        var language = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageId");
        var languageIndex = AccessTools.Field(typeof(MBTextManager), "_activeTextLanguageIndex");
        var oldLanguage = language.GetValue(null); var oldIndex = languageIndex.GetValue(null);
        var translations = (IDictionary)AccessTools.Field(typeof(LocalizedTextManager), "_gameTextDictionary").GetValue(null);
        var oldTexts = new Dictionary<string, object>();
        var display = mod.GetType("BellumCivile.FeudalTitleDisplayHelper", true);
        void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        void Language(string name) { language.SetValue(null, name); languageIndex.SetValue(null, (int)languageIndex.GetValue(null) + 1); }
        void Translate(string id, string text) { if (!oldTexts.ContainsKey(id)) oldTexts[id] = translations[id]; translations[id] = text; }
        object Config() => Activator.CreateInstance(configType, true);
        object Style(string xml)
        {
            var doc = new XmlDocument(); doc.LoadXml(xml);
            return AccessTools.Method(configType, "ReadTitleStyle").Invoke(null, new object[] { doc.DocumentElement });
        }
        IList Styles(object config) => (IList)AccessTools.Field(configType, "_styles").GetValue(config);
        object Add(object config, string xml) { var style = Style(xml); Styles(config).Add(style); return style; }
        CultureObject Nation(string id)
        {
            var culture = Blank<CultureObject>(); culture.StringId = id;
            AccessTools.PropertySetter(typeof(BasicCultureObject), "Name").Invoke(culture, new object[] { new TextObject("{=!}" + id) });
            return culture;
        }
        var owner = Blank<Clan>(); owner.StringId = "barony_owner";
        var village = Blank<Settlement>(); village.StringId = "village_A1";
        var parent = Blank<Settlement>(); parent.StringId = "castle_A1";
        var town = Blank<Town>(); Set(town, "_ownerClan", owner); Set(parent, "Town", town);
        var component = Blank<Village>(); Set(component, "_bound", parent); Set(village, "Village", component);
        string Noun() => ((TextObject)AccessTools.Method(display, "GetLordshipNoun").Invoke(null, new object[] { village })).ToString();
        void Expect(string name, string noun, string context) => check(LordshipBehavior.Name(village).ToString() == name && Noun() == noun, context);
        var parentNode = Blank<HierarchyTitleNodeVM>();
        Set(parentNode, "<Name>k__BackingField", "Test barony");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "Culture"), prefix: new HarmonyMethod(typeof(LordshipNamingTests), nameof(Culture)));
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(LordshipNamingTests), nameof(NoPlayerClan)));
            h.Patch(AccessTools.PropertyGetter(typeof(Settlement), "Name"), prefix: new HarmonyMethod(typeof(LordshipNamingTests), nameof(VillageName)));
            h.Patch(AccessTools.Method(typeof(MBTextManager), "SetTextVariable", new[] { typeof(string), typeof(TextObject), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(LordshipNamingTests), nameof(DialogueText)));
            Language("English");
            var vlandia = Nation("vlandia"); var aserai = Nation("aserai");
            Cultures[owner] = vlandia; village.Culture = aserai;
            var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "BellumCivile.csproj"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Missing shipped lordship presets.");
            var catalog = new XmlDocument(); catalog.Load(Path.Combine(root.FullName, "ModuleData", "Languages", "EN", "strings.xml"));
            var english = catalog.SelectNodes("/base/strings/string").Cast<XmlElement>()
                .ToDictionary(node => node.GetAttribute("id"), node => node.GetAttribute("text"), StringComparer.Ordinal);
            TextObject Estate(string key, Settlement estate = null) => (TextObject)AccessTools.Method(typeof(LordshipBehavior), "EstateText")
                .Invoke(null, new object[] { key, english[key], estate, owner });
            foreach (string preset in new[] { "anglicized", "immersive", "bannerkings" })
            {
                var config = Config();
                AccessTools.Method(configType, "LoadFile").Invoke(config, new object[] {
                    Path.Combine(root.FullName, "ModuleData", "bellum_title_styles_" + preset + ".xml"), false, true });
                configField.SetValue(null, config);
                foreach (string culture in new[] { "Empire", "Empire_n", "Empire_s", "Empire_w", "Vlandia", "Battania", "Khuzait", "Aserai", "Sturgia", "Nord", "Rhodok" })
                {
                    var expected = Expected(preset, culture);
                    Cultures[owner] = Nation(culture.ToLowerInvariant());
                    Expect(expected.Name, expected.Noun, preset + "/" + culture + ": name, connectors, word order and cultural noun follow the barony owner");
                    var node = new HierarchyLordshipNodeVM(village, parentNode, null, null);
                    check(node.Name == expected.Name && node.Tier == "Lordship",
                        preset + "/" + culture + ": cultural estate names retain a plain Lordship rank");
                    var tooltip = AccessTools.Field(typeof(HierarchyTitleNodeVM), "_tooltip").GetValue(node);
                    var rows = ((IEnumerable)((Delegate)AccessTools.Field(tooltip.GetType(), "_tooltipProperties").GetValue(tooltip)).DynamicInvoke()).Cast<object>();
                    string Label(object row, string property) => (string)AccessTools.Property(row.GetType(), property).GetValue(row);
                    check(rows.Any(row => Label(row, "DefinitionLabel") == "Tier" && Label(row, "ValueLabel") == "Lordship"),
                        preset + "/" + culture + ": tooltip explains the underlying rank instead of repeating the cultural noun");
                    foreach (string key in new[] { "BC_Lordship_VassalAward", "BC_Lordship_VassalReturn", "BC_Lordship_VassalReturnConfirm",
                        "BC_Lordship_VassalReturnDone", "BC_Lordship_DialogueGrant", "BC_Lordship_DialogueRevokeOffer", "BC_Lordship_DialogueSurrender",
                        "BC_Lordship_RecipientDescription", "BC_Lordship_RevokeConfirm", "BC_Lordship_RevenueHint", "BC_Lordship_RevokeHint",
                        "BC_Lordship_Occupied", "BC_Lordship_AlreadyHolds" })
                    {
                        string text = Estate(key, village).SetTextVariable("HERO", new TextObject("{=!}Recipient")).ToString();
                        check(text.Contains(expected.Name) && !text.Contains("{"),
                            preset + "/" + culture + ": cultural grant reference resolves fully in " + key);
                    }
                    check(Estate("BC_Lordship_GrantTitle").ToString() == "Grant " + expected.Noun
                        && Estate("BC_Lordship_RevokeTitle", village).ToString() == "Revoke " + expected.Noun,
                        preset + "/" + culture + ": grant/revoke headings use the cultural noun, including before a village is selected");
                    AccessTools.Method(typeof(LordshipBehavior), "SetConversationEstate").Invoke(null, new object[] { village });
                    check(DialogueVariables["LORDSHIP"].ToString() == expected.Name && DialogueVariables["VILLAGE"].ToString() == "Calioc",
                        preset + "/" + culture + ": oath, return and household dialogues receive both the cultural name and legacy village variable");
                    foreach (var tier in new[] { FeudalTitleType.Kingdom, FeudalTitleType.Empire })
                    {
                        var resolved = AccessTools.Method(display, "ResolveRankStyle").Invoke(null, new object[] { tier, owner, null, "" });
                        check(resolved != null && new TextObject((string)AccessTools.Field(resolved.GetType(), "WandererRank").GetValue(resolved)).ToString() == expected.Male
                            && new TextObject((string)AccessTools.Field(resolved.GetType(), "FemaleWandererRank").GetValue(resolved)).ToString() == expected.Female,
                            preset + "/" + culture + "/" + tier + ": both retainer honorifics resolve through existing rank selection");
                    }
                }
                foreach (var style in Styles(config).Cast<object>())
                foreach (var configured in ((IEnumerable)AccessTools.Field(style.GetType(), "Ranks").GetValue(style)).Cast<object>())
                {
                    string Field(string name) => (string)AccessTools.Field(configured.GetType(), name).GetValue(configured);
                    var tier = (FeudalTitleType)AccessTools.Field(configured.GetType(), "Tier").GetValue(configured);
                    if (tier == FeudalTitleType.Barony)
                        check(!string.IsNullOrEmpty(Field("LordshipName")) && !string.IsNullOrEmpty(Field("LordshipFormat")),
                            preset + ": every barony entry exposes both lordship attributes");
                    if (tier < FeudalTitleType.Kingdom) continue;
                    string culture = (string)AccessTools.Field(style.GetType(), "CultureRef").GetValue(style);
                    var expected = Expected(preset, string.IsNullOrEmpty(culture) ? "Empire" : culture);
                    check(new TextObject(Field("WandererRank")).ToString() == expected.Male
                        && new TextObject(Field("FemaleWandererRank")).ToString() == expected.Female,
                        preset + ": every sovereign entry, including kingdom overrides, declares the correct retainer titles");
                    check(!((IEnumerable)AccessTools.Field(style.GetType(), "Ranks").GetValue(style)).Cast<object>()
                        .SelectMany(entry => new[] { "MaleRank", "FemaleRank", "NobleRank", "FemaleNobleRank", "HeirRank", "FemaleHeirRank", "ChildRank", "FemaleChildRank" }
                            .Select(field => (string)AccessTools.Field(entry.GetType(), field).GetValue(entry)))
                        .Where(value => !string.IsNullOrEmpty(value)).Select(value => new TextObject(value).ToString())
                        .Any(value => value == expected.Male || value == expected.Female),
                        preset + ": retainer titles remain distinct from higher ranks and family honorifics");
                }
                foreach (string realmId in new[] { "empire", "empire_s", "empire_w" })
                {
                    var nativeRealm = Blank<Kingdom>(); nativeRealm.StringId = realmId;
                    Cultures[owner] = Nation("empire"); Set(owner, "_kingdom", nativeRealm); Set(nativeRealm, "_rulingClan", owner);
                    var resolved = AccessTools.Method(display, "ResolveRankStyle").Invoke(null,
                        new object[] { FeudalTitleType.Kingdom, owner, null, "" });
                    var expected = Expected(preset, "Empire");
                    check(new TextObject((string)AccessTools.Field(resolved.GetType(), "WandererRank").GetValue(resolved)).ToString() == expected.Male
                        && new TextObject((string)AccessTools.Field(resolved.GetType(), "FemaleWandererRank").GetValue(resolved)).ToString() == expected.Female,
                        preset + "/" + realmId + ": native imperial sovereign overrides retain cultural honorifics");
                    Expect(expected.Name, expected.Noun, preset + "/" + realmId + ": lordships keep barony grammar rather than sovereign-only connectors");
                    Set(owner, "_kingdom", null);
                }
            }
            var custom = Config(); configField.SetValue(null, custom); Cultures[owner] = vlandia;
            Expect("Lordship of Calioc", "Lordship", "Configurations without new fields preserve legacy naming");
            check(Estate("BC_Lordship_VassalAward", village).ToString().Contains("Lordship of Calioc")
                && Estate("BC_Lordship_GrantTitle").ToString() == "Grant Lordship",
                "Unconfigured cultures retain complete English grant references and menu labels");
            Translate("BC_Lordship_Name", "Ancien domaine de {VILLAGE}"); Language("BellumLordshipTest");
            Expect("Ancien domaine de Calioc", "Lordship", "Unconfigured legacy names retain their existing translation key"); Language("English");
            var baseStyle = Add(custom, "<TitleStyle culture='Vlandia'><Rank tier='Barony' titleName='Barony' lordshipName='Feodum' lordshipFormat='{TITLE_NOUN} {VILLAGE}' /></TitleStyle>");
            Expect("Feodum Calioc", "Feodum", "A style does not require an occupied grant to name a lordship");
            var patch = Style("<TitleStyle culture='Vlandia'><Rank tier='Barony' lordshipName='Fief' /></TitleStyle>");
            AccessTools.Method(configType, "MergeStyle").Invoke(null, new[] { baseStyle, patch });
            Expect("Fief Calioc", "Fief", "A noun-only patch preserves the earlier naming format");
            patch = Style("<TitleStyle culture='Vlandia'><Rank tier='Barony' lordshipFormat='{TITLE_NAME} / {TITLE_NOUN}' /></TitleStyle>");
            AccessTools.Method(configType, "MergeStyle").Invoke(null, new[] { baseStyle, patch });
            Expect("Calioc / Fief", "Fief", "A format-only patch preserves its noun and supports the standard TITLE_NAME token");
            var rank = ((IList)AccessTools.Field(baseStyle.GetType(), "Ranks").GetValue(baseStyle))[0];
            check((string)AccessTools.Field(rank.GetType(), "TitleName").GetValue(rank) == "Barony",
                "Lordship patch fields do not overwrite the parent barony's rank name");
            var realm = Blank<Kingdom>(); realm.StringId = "test_realm"; Set(owner, "_kingdom", realm);
            var rulingHouse = Blank<Clan>(); rulingHouse.StringId = "ruling_house"; Cultures[rulingHouse] = aserai; Set(realm, "_rulingClan", rulingHouse);
            var rulerStyle = Add(custom, "<TitleStyle rulerCulture='Aserai'><Rank tier='Barony' lordshipName='Iqta' lordshipFormat='{TITLE_NOUN} {VILLAGE}' /></TitleStyle>");
            Expect("Iqta Calioc", "Iqta", "Ruler-culture terminology takes the same precedence as other barony ranks");
            var realmStyle = Add(custom, "<TitleStyle kingdom='test_realm'><Rank tier='Barony' lordshipName='Royal Fief' lordshipFormat='{TITLE_NOUN}: {VILLAGE}' /></TitleStyle>");
            Expect("Royal Fief: Calioc", "Royal Fief", "Kingdom-specific terminology overrides ruler and holder cultures");
            Styles(custom).Remove(realmStyle); Styles(custom).Remove(rulerStyle); Set(owner, "_kingdom", null);
            Set(rank, "LordshipFormat", null); Expect("Fief of Calioc", "Fief", "A configured noun alone receives the default noun-aware format");
            Set(rank, "LordshipName", null); Set(rank, "LordshipFormat", "{TITLE_NOUN} / {VILLAGE}");
            Expect("Lordship / Calioc", "Lordship", "A format alone uses the default localized noun");
            Set(rank, "LordshipName", "{=BC_Test_LordshipNoun}Feodum");
            Set(rank, "LordshipFormat", "{=BC_Test_LordshipFormat}{TITLE_NOUN} {VILLAGE}");
            var localized = LordshipBehavior.Name(village);
            var localizedAward = Estate("BC_Lordship_VassalAward", village);
            Translate("BC_Test_LordshipNoun", "Domaine"); Translate("BC_Test_LordshipFormat", "{VILLAGE} - {TITLE_NOUN}"); Translate("BC_Test_LordshipVillage", "Calioc traduit");
            Language("BellumLordshipTest");
            check(localized.ToString() == "Calioc traduit - Domaine" && Noun() == "Domaine",
                "Existing TextObjects preserve nested translation keys and support language-specific word order");
            check(localizedAward.ToString().Contains("Calioc traduit - Domaine"),
                "Grant prose preserves nested noun, format and village translations across language changes");
            Language("English"); check(localized.ToString() == "Feodum Calioc", "The same name follows a language switch back without rebuilding the grant");
            check(parent.OwnerClan == owner && village.StringId == "village_A1" && Enum.GetNames(typeof(FeudalTitleType)).All(n => n != "Lordship"),
                "Terminology leaves native ownership, village IDs and landed title tiers unchanged");
        }
        finally
        {
            h.UnpatchAll(h.Id); configField.SetValue(null, oldConfig);
            language.SetValue(null, oldLanguage); languageIndex.SetValue(null, oldIndex);
            foreach (var pair in oldTexts) { if (pair.Value == null) translations.Remove(pair.Key); else translations[pair.Key] = pair.Value; }
            Cultures.Clear();
            DialogueVariables.Clear();
        }
    }
}
