using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using BellumCivile;
using HarmonyLib;

internal static class PrefabFinalizationCompatibilityTests
{
    private static readonly Assembly Core = typeof(SubModule).Assembly;
    private static readonly Type Patch = Core.GetType("BellumCivile.UI.EncyclopediaFamilyPrefabFinalizationPatch", true);
    private static bool MissingTarget(ref MethodBase __result) { __result = null; return false; }

    internal static void Run(Action<bool, string> check, string game)
    {
        MethodInfo Resolve(Type type) => (MethodInfo)AccessTools.Method(Patch, "FindTarget").Invoke(null, new object[] { type });
        var native = new XmlDocument();
        native.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
        var clan = new XmlDocument();
        clan.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaClanPage.xml"));
        check(Resolve(typeof(ModernApi))?.Name == "ApplyPatches", "Current UIExtenderEx selects the shared ApplyPatches finalization point");
        check(Resolve(typeof(LegacyApi))?.Name == "ProcessMovie", "Older UIExtenderEx selects its ProcessMovie finalization point");
        check(Resolve(typeof(BothApis))?.Name == "ApplyPatches", "Only the shared hook is selected when both entry points exist");
        foreach (var type in new[] { null, typeof(UnsupportedApi), typeof(InstanceApi), typeof(GenericApi) })
            check(Resolve(type) == null, "Missing and unfamiliar prefab APIs are not guessed: " + type?.Name);
        var harmony = new Harmony("bellum.tests.prefab_finalization_versions");
        try
        {
            foreach (var type in new[] { typeof(ModernApi), typeof(LegacyApi) })
            {
                var target = Resolve(type);
                harmony.Patch(target, postfix: new HarmonyMethod(Patch, "Postfix"));
                foreach (string path in type == typeof(ModernApi) ? new[] { "EncyclopediaHeroPage" }
                    : new[] { @"C:\Modules\SandBox\EncyclopediaHeroPage.xml", "/modules/SandBox/EncyclopediaHeroPage.xml" })
                {
                    var document = (XmlDocument)native.CloneNode(true);
                    target.Invoke(null, new object[] { path, document });
                    check(document.SelectSingleNode("//*[@Id='FamilyGrid' and @IsHidden='true' and @MarginTop='31']") != null,
                        "Finalization follows all registry edits and supports native path separators: " + type.Name + "/" + path);
                }
                var clanPage = (XmlDocument)clan.CloneNode(true);
                target.Invoke(null, new object[] { type == typeof(ModernApi) ? "EncyclopediaClanPage" : "/modules/EncyclopediaClanPage.xml", clanPage });
                check(clanPage.SelectSingleNode("//*[@Id='BellumClanHousehold']") != null,
                    "Clan household finalization works with " + type.Name);
                var unrelated = (XmlDocument)native.CloneNode(true);
                target.Invoke(null, new object[] { "OtherPage", unrelated });
                check(unrelated.SelectSingleNode("//*[@Id='FamilyGrid' and not(@IsHidden)]") != null,
                    "Unrelated movies keep their original visibility: " + type.Name);
            }

            var heroOwned = (XmlDocument)native.CloneNode(true);
            var clanOwned = (XmlDocument)clan.CloneNode(true);
            Finalize("EncyclopediaHeroPage", heroOwned);
            Finalize("EncyclopediaClanPage", clanOwned);
            harmony.Patch(AccessTools.Method(Patch, "TargetMethod"), prefix: new HarmonyMethod(typeof(PrefabFinalizationCompatibilityTests), nameof(MissingTarget)));
            harmony.CreateClassProcessor(Patch).Patch();
            check(!(bool)AccessTools.Property(Patch, "Supported").GetValue(null), "An unsupported UIExtenderEx hook is skipped without a Harmony startup exception");
            Finalize("EncyclopediaHeroPage", heroOwned);
            Finalize("EncyclopediaClanPage", clanOwned);
            check(heroOwned.SelectSingleNode("//*[@Id='FamilyGrid' and not(@IsHidden)]") != null
                && !(bool)AccessTools.Property(Core.GetType("BellumCivile.UI.EncyclopediaFamilyLayoutCompatibility", true), "Enabled").GetValue(null),
                "Unsupported hooks restore native hero family visibility and hide Bellum's replacement");
            check(clanOwned.SelectSingleNode("//*[@Id='MembersGrid' and not(@IsHidden)]") != null
                && clanOwned.SelectSingleNode("//*[@Id='BellumClanHousehold']") == null,
                "Unsupported hooks restore the native clan member list without duplicate sections");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.Method(Patch, "Prepare").Invoke(null, null);
            Finalize("EncyclopediaHeroPage", (XmlDocument)native.CloneNode(true));
            Finalize("EncyclopediaClanPage", (XmlDocument)clan.CloneNode(true));
        }
    }

    private static void Finalize(string movie, XmlDocument document)
        => AccessTools.Method(Patch, "FinalizePrefab").Invoke(null, new object[] { movie, document });

    private static void RegistryEdit(XmlDocument document)
    {
        (document.SelectSingleNode("//*[@Id='FamilyGrid']") as XmlElement)?.SetAttribute("MarginTop", "31");
    }
    private static class ModernApi
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyPatches(string movieName, XmlDocument document) => RegistryEdit(document);
    }
    private static class LegacyApi
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ProcessMovie(string path, XmlDocument document) => RegistryEdit(document);
    }
    private static class BothApis
    {
        private static void ApplyPatches(string name, XmlDocument document) { }
        private static void ProcessMovie(string path, XmlDocument document) { }
    }
    private static class UnsupportedApi
    {
        private static int ApplyPatches(string name, XmlDocument document) => 0;
        private static void ProcessMovie(string path, object document) { }
    }
    private sealed class InstanceApi
    {
        private void ApplyPatches(string name, XmlDocument document) { }
    }
    private static class GenericApi
    {
        private static void ApplyPatches<T>(string name, XmlDocument document) { }
    }
}
