using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class BannerCompatibilityTests
{
    private static readonly Assembly Bellum = typeof(FeudalTitleRecord).Assembly;
    private static Type Type(string name) => Bellum.GetType("BellumCivile." + name, true);
    private static object Call(string type, string method, params object[] args) => AccessTools.Method(Type(type), method).Invoke(null, args);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    private const string Artwork = "11.1.2.1500.1500.764.764.1.0.0.505.2.1.300.400.700.600.1.1.45.506.1.2.200.250.800.900.0.0.90";
    private const string Simple = "11.1.1.1500.1500.764.764.0.0.0.505.2.2.300.400.764.764.0.0.0";
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }
    private static bool Background(ref int __result) { __result = 11; return false; }
    private static bool Icon(ref int __result) { __result = 505; return false; }
    private static bool SovereignName(ref TextObject __result) { __result = new TextObject("Kingdom of Vlandia"); return false; }
    private static bool ChangeRealm(Clan __instance, Kingdom value)
    {
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(__instance, value);
        // Simulate a destructive setter to test the independent creation snapshot as well as the global guard.
        if ((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", __instance))
        {
            __instance.ClanOriginalBanner.ChangePrimaryColor(BannerManager.GetColor(3));
            __instance.ClanOriginalBanner.ChangeIconColors(BannerManager.GetColor(4));
        }
        else AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom").Invoke(__instance, null);
        return false;
    }
    private static Clan House(Banner banner, Kingdom realm = null)
    {
        var clan = Blank<Clan>();
        clan.Banner = banner;
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(clan, realm);
        AccessTools.Field(typeof(Clan), "_warPartyComponentsCache").SetValue(clan, new MBList<WarPartyComponent>());
        AccessTools.Property(typeof(Clan), "Name").SetValue(clan, new TextObject("Test House"));
        return clan;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.banner_compatibility");
        BannerManager.Initialize();
        var colors = (Dictionary<int, BannerColor>)AccessTools.Field(typeof(BannerManager), "_colorPalette").GetValue(BannerManager.Instance);
        for (int i = 0; i <= 250; i++) colors[i] = new BannerColor(0xff000000u | (uint)(i * 65793), true, true);
        var realm = Blank<Kingdom>();
        AccessTools.Property(typeof(Kingdom), "Name").SetValue(realm, new TextObject("Vlandia"));
        AccessTools.Property(typeof(Kingdom), "PrimaryBannerColor").SetValue(realm, BannerManager.GetColor(1));
        AccessTools.Property(typeof(Kingdom), "SecondaryBannerColor").SetValue(realm, BannerManager.GetColor(2));
        AccessTools.Property(typeof(Kingdom), "Color").SetValue(realm, BannerManager.GetColor(1));
        AccessTools.Property(typeof(Kingdom), "Color2").SetValue(realm, BannerManager.GetColor(2));
        realm.Banner = new Banner(Simple);
        var parent = House(new Banner(Artwork), realm);
        var nativeSync = AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(NoPlayer)));
            string original = parent.ClanOriginalBanner.Serialize();
            object visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "cadet_test");
            Banner inherited = Property<Banner>(visuals, "Banner");
            check(Property<bool>(visuals, "CopiedParentArtwork"), "Two-color layered banners are recognized even in kingdom colors");
            check(inherited.Serialize() == original, "Cadet clone preserves every layer, color, stroke and transform");
            check(!ReferenceEquals(inherited, parent.ClanOriginalBanner), "Cadet banner does not alias its parent's banner");
            check(Property<uint>(visuals, "PrimaryColor") == inherited.GetPrimaryColor()
                && Property<uint>(visuals, "SecondaryColor") == inherited.GetFirstIconColor(), "Copied banner supplies its own display colors");
            inherited.GetBannerDataAtIndex(1).ColorId = 4;
            check(parent.ClanOriginalBanner.Serialize() == original, "Editing a child's layer cannot change its parent");
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(new Banner(Artwork))), "Kingdomless layered artwork is recognized");
            check(!(bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(new Banner(Simple), realm)), "Uniform vanilla artwork keeps the generated path");
            var mixed = new Banner(Simple);
            mixed.GetBannerDataAtIndex(1).ColorId2 = 1;
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", House(mixed, realm)), "Single-icon custom artwork with mixed layer colors is recognized");

            harmony.Patch(AccessTools.PropertySetter(typeof(Clan), "Kingdom"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(ChangeRealm)));
            visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "cadet_test");
            var cadet = House(Property<Banner>(visuals, "Banner"));
            Call("KingdomVisualHelper", "AssignCadetKingdom", cadet, realm, visuals);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Creation survives in-place recoloring by the kingdom setter");
            check(cadet.Color == parent.ClanOriginalBanner.GetPrimaryColor(), "Creation retains banner-derived clan color metadata");

            harmony.CreateClassProcessor(Type("Patches.CustomClanBannerSyncPatch")).Patch();
            nativeSync.Invoke(cadet, null);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Native synchronization preserves layered cadet artwork");
            cadet.Banner = new Banner(cadet.ClanOriginalBanner.Serialize());
            nativeSync.Invoke(cadet, null);
            check(cadet.ClanOriginalBanner.Serialize() == original, "Serialized banner round trip followed by native load synchronization is lossless");
            harmony.Patch(AccessTools.Method(Type("KingdomVisualHelper"), "PickBackgroundMeshId"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(Background)));
            harmony.Patch(AccessTools.Method(Type("KingdomVisualHelper"), "PickIconMeshId"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(Icon)));
            object generated = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", House(new Banner(Simple), realm), "generated_test");
            check(!Property<bool>(generated, "CopiedParentArtwork") && Property<Banner>(generated, "Banner").BannerDataList.Count == 2,
                "Vanilla cadet generation still creates a fresh background and sigil");
            var generatedClan = House(Property<Banner>(generated, "Banner"));
            Call("KingdomVisualHelper", "AssignCadetKingdom", generatedClan, realm, generated);
            check(generatedClan.ClanOriginalBanner.GetPrimaryColor() == realm.PrimaryBannerColor
                && generatedClan.ClanOriginalBanner.GetFirstIconColor() == realm.SecondaryBannerColor,
                "Generated cadets retain native kingdom-color synchronization");
            realm.RulingClan = parent;
            string kingdomBanner = realm.Banner.Serialize();
            visuals = Call("KingdomVisualHelper", "ResolveCadetBranchVisuals", parent, "royal_cadet");
            check(Property<Banner>(visuals, "Banner").Serialize() == original, "Royal cadets inherit the house banner, not the kingdom banner proxy");
            nativeSync.Invoke(parent, null);
            check(parent.ClanOriginalBanner.Serialize() == original && realm.Banner.Serialize() == kingdomBanner,
                "Ruling house and kingdom artwork remain separate through native synchronization");

            var map = new Hashtable { ["Vlandia"] = new object() };
            var display = new TextObject("Kingdom of Vlandia");
            var native = new TextObject("Vlandia");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", map, display, native), native), "POC can fall back from a sovereign title to its configured native name");
            map[display.ToString()] = new object();
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", map, display, native), display), "Explicit displayed-name POC configuration takes precedence");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", new Hashtable(), display, native), display), "Unknown realms retain POC's original fallback behavior");
            var translated = new TextObject("Royaume de Valandie");
            var translatedNative = new TextObject("Valandie");
            check(ReferenceEquals(Call("Patches.PocBannerCompatibility", "SelectName", new Hashtable { ["Valandie"] = new object() }, translated, translatedNative), translatedNative), "Lookup aliases work with localized names without English rank stripping");

            string modulePath = Environment.GetEnvironmentVariable("BellumTestPocModule");
            if (!string.IsNullOrWhiteSpace(modulePath)) RunPoc(check, harmony, realm, modulePath);
            else Console.WriteLine("SKIP: optional installed POC integration (set BellumTestPocModule to its DLL).");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void RunPoc(Action<bool, string> check, Harmony harmony, Kingdom realm, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("POC test module not found", path);
        Assembly poc = Assembly.LoadFrom(path);
        Type module = poc.GetType("PocColor.PocColorMod", true);
        PropertyInfo field = AccessTools.Property(module, "config");
        object old = field.GetValue(null);
        object config = Activator.CreateInstance(field.PropertyType);
        object New(string name) => Activator.CreateInstance(poc.GetType("PocColor.Config." + name, true));
        void Set(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
        IDictionary Map(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name);
            var m = (IDictionary)Activator.CreateInstance(p.PropertyType);
            p.SetValue(obj, m);
            return m;
        }
        try
        {
            field.SetValue(null, config);
            IDictionary kingdoms = Map(config, "kingdoms"), clans = Map(config, "clans");
            object rule = New("PocColorModConfigKingdom");
            Set(rule, "FollowKingdomColors", "false");
            Set(rule, "combatShields", new[] { Simple, Artwork });
            kingdoms["Vlandia"] = rule;
            Call("Patches.PocBannerCompatibility", "TryApply", harmony);
            check(AccessTools.Field(Type("Patches.PocBannerCompatibility"), "_readConfig").GetValue(null) != null, "Installed POC API and name-lookup transpilers are supported");
            foreach (string name in new[] { "PocColorModAgentEquipItemsFromSpawnEquipment", "PocColorModAgentEquipNewEntity" })
            {
                Type patch = poc.GetType("PocColor.PocColorModSetColors+" + name, true);
                MethodInfo method = AccessTools.Method(patch, name.EndsWith("NewEntity") ? "Postfix" : "Prefix");
                check(Harmony.GetPatchInfo(method)?.Transpilers.Any(p => p.PatchMethod.DeclaringType == Type("Patches.PocEquipmentOwnerCompatibility")) == true,
                    "Installed POC equipment lookup accepts the actual-owner adapter: " + name);
            }
            var owner = House(new Banner(Artwork), realm);
            AccessTools.Property(typeof(Clan), "Name").SetValue(owner, new TextObject("Cadet House"));
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", owner, null) == "Cadet House",
                "An identical banner's cached parent cannot override its troop party's actual cadet owner");
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", null, owner) == "Cadet House",
                "Equipment without a party can use a hero's real house");
            check((string)Call("Patches.PocEquipmentOwnerCompatibility", "SelectClanName", "Parent House", null, null) == "Parent House",
                "Equipment without campaign ownership retains POC's cache fallback");
            Call("DynamicKingdomTitleNameHelper", "RecordNativeInitialization", realm,
                new TextObject("Vlandia"), new TextObject("Vlandia"), new TextObject("Vlandia"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), prefix: new HarmonyMethod(typeof(BannerCompatibilityTests), nameof(SovereignName)));
            var lookup = (TextObject)Call("Patches.PocBannerCompatibility", "ReadKingdomName", realm);
            check(lookup.ToString() == "Vlandia", "Installed POC adapter resolves a renamed realm to its native config key");
            string[] ShieldPool(string name)
            {
                var result = (ValueTuple<int, string[], string[], string[], string[]>)AccessTools.Method(config.GetType(), "GetBattleConfig")
                    .Invoke(config, new object[] { name, "", "Test troop", false, false, false, false, false, false, false, false, 1, "Vlandia" });
                return result.Item5;
            }
            check(ShieldPool("Kingdom of Vlandia") == null && ShieldPool(lookup.ToString()).Length == 2,
                "Actual POC battle resolver recovers the random-shield pool lost through realm renaming");
            foreach (string[] pool in new[] { new[] { Simple, Artwork }, new[] { Simple }, new[] { Artwork } })
            {
                Set(rule, "combatShields", pool);
                check(ShieldPool(lookup.ToString()).SequenceEqual(pool), "POC retains the configured random, clan or kingdom shield pool");
            }
            Set(rule, "combatShields", new[] { Simple, Artwork });
            object namedRule = New("PocColorModConfigKingdom");
            Set(namedRule, "combatShields", new[] { Artwork });
            kingdoms["Kingdom of Vlandia"] = namedRule;
            string explicitKey = ((TextObject)Call("Patches.PocBannerCompatibility", "ReadKingdomName", realm)).ToString();
            check(explicitKey == "Kingdom of Vlandia" && ShieldPool(explicitKey).SequenceEqual(new[] { Artwork }),
                "Actual POC battle resolver honors explicitly configured sovereign names ahead of native aliases");
            kingdoms.Remove("Kingdom of Vlandia");
            object houseRule = New("PocColorModConfigClan");
            Set(houseRule, "clanBanner", Simple);
            clans["Test House"] = houseRule;
            var house = House(new Banner(Simple), realm);
            check((bool)Call("KingdomVisualHelper", "ShouldPreserveClanBanner", house), "POC's explicit simple banner is recognized in kingdom colors");
            var policyArgs = new object[] { house, false, false };
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[1] && (bool)policyArgs[2], "POC remains authoritative for explicitly configured artwork");
            clans.Clear();
            policyArgs = new object[] { house, false, false };
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check(!(bool)policyArgs[1] && !(bool)policyArgs[2], "Random combat shields do not count as a clan-banner recoloring instruction");
            Set(rule, "FollowKingdomColors", "true");
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[2], "Explicit kingdom-color following is honored");
            Set(rule, "FollowKingdomColors", "false");
            Set(rule, "FollowKingdomBackgroundOnly", "true");
            Call("Patches.PocBannerCompatibility", "TryGetPolicy", policyArgs);
            check((bool)policyArgs[2], "Explicit background-only following is honored");
            check(((string[])rule.GetType().GetProperty("combatShields").GetValue(rule)).Length == 2,
                "Compatibility leaves POC's combat shield pool unchanged");

            Set(rule, "FollowKingdomBackgroundOnly", "false");
            Type pocSync = poc.GetType("PocColor.PocColorModSetColors+PocColorModOverrideBanner", true);
            harmony.CreateClassProcessor(pocSync).Patch();
            var copy = House(new Banner(Artwork), realm);
            MethodInfo sync = AccessTools.Method(typeof(Clan), "UpdateBannerColorsAccordingToKingdom");
            string copyCode = copy.ClanOriginalBanner.Serialize();
            sync.Invoke(copy, null);
            check(copy.ClanOriginalBanner.Serialize() == copyCode, "POC and Bellum native-sync patches preserve copied cadet artwork together");

            string bcpPath = Environment.GetEnvironmentVariable("BellumTestBannerPersistenceModule");
            if (!string.IsNullOrWhiteSpace(bcpPath))
            {
                Assembly bcp = Assembly.LoadFrom(bcpPath);
                object options = Activator.CreateInstance(bcp.GetType("BannerColorPersistence.Config", true));
                PropertyInfo bcpConfig = AccessTools.Property(bcp.GetType("BannerColorPersistence.PersistColorSubModule", true), "Config");
                object previous = bcpConfig.GetValue(null);
                try
                {
                    Set(options, "BannerColorPersistenceEnabled", true);
                    Set(options, "ShouldPreventNPCBannerColorChanges", true);
                    bcpConfig.SetValue(null, options);
                    harmony.CreateClassProcessor(bcp.GetType("BannerColorPersistence.PreventBannerColorUpdates", true)).Patch();
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() == copyCode, "POC, Banner Color Persistence and Bellum synchronize without losing artwork");
                    Set(rule, "FollowKingdomColors", "true");
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() != copyCode, "Explicit POC recoloring remains authoritative with all three patches installed");
                    Set(rule, "FollowKingdomColors", "false");
                    harmony.Unpatch(sync, AccessTools.Method(pocSync, "Prefix"));
                    harmony.Unpatch(sync, AccessTools.Method(pocSync, "Postfix"));
                    harmony.CreateClassProcessor(pocSync).Patch();
                    copy.Banner = new Banner(copyCode);
                    sync.Invoke(copy, null);
                    check(copy.ClanOriginalBanner.Serialize() == copyCode, "Artwork also survives when persistence patches are installed before POC's sync patches");
                }
                finally { bcpConfig.SetValue(null, previous); }
            }
            else Console.WriteLine("SKIP: optional Banner Color Persistence integration (set BellumTestBannerPersistenceModule).");
        }
        finally { field.SetValue(null, old); }
    }
}
