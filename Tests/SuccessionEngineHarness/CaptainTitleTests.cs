using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

internal static class CaptainTitleTests
{
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, CultureObject> Cultures = new Dictionary<Clan, CultureObject>();
    private static Clan _player, _company;
    private static Hero _family, _minor, _landed, _female;
    private static double _day;
    private static bool _hired;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool House(Hero __instance, ref Clan __result) { Houses.TryGetValue(__instance, out __result); return false; }
    private static bool Culture(Clan __instance, ref CultureObject __result) { Cultures.TryGetValue(__instance, out __result); return false; }
    private static bool Retainer(Hero hero, ref bool __result) { __result = hero != null && hero != _family && Houses[hero] == _player; return false; }
    private static bool MinorClan(Clan __instance, ref bool __result) { __result = __instance == _company || __instance == _player; return false; }
    private static bool Hired(ref bool __result) { __result = _hired; return false; }
    private static bool Female(Hero __instance, ref bool __result) { __result = __instance == _female; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = __instance == _minor; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Land(Hero hero, ref PersonalLordshipRecord __result)
    { __result = hero == _landed ? new PersonalLordshipRecord { Holder = hero } : null; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }

    private static (string Male, string Female) Expected(string preset, string culture)
    {
        if (preset == "anglicized") return ("Captain", "Captain");
        if (culture.StartsWith("Empire")) return ("Lochagos", "Lochagos");
        switch (culture)
        {
            case "Vlandia": return preset == "immersive" ? ("Capitaneus", "Capitanea") : ("Capitaine", "Capitaine");
            case "Battania": return preset == "immersive" ? ("Captaen", "Captaen") : ("Capten", "Capten");
            case "Khuzait": return ("Y\u00FCzba\u015F\u0131", "Y\u00FCzba\u015F\u0131");
            case "Aserai": return ("Qaid", "Qaida");
            case "Sturgia": return ("Sotnik", "Sotnitsa");
            case "Nord": return ("H\u01EBfu\u00F0sma\u00F0r", "H\u01EBfu\u00F0skona");
            case "Rhodok": return ("Houbetman", "Houbetvrouwe");
            default: throw new InvalidOperationException("Unexpected captain culture: " + culture);
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.captain-titles");
        var assembly = typeof(FeudalTitleBehavior).Assembly;
        var display = assembly.GetType("BellumCivile.FeudalTitleDisplayHelper", true);
        var names = assembly.GetType("BellumCivile.Patches.FeudalTitleHeroNamePatch", true);
        var configType = assembly.GetType("BellumCivile.FeudalTitleConfig", true);
        var configField = AccessTools.Field(configType, "_instance");
        var oldConfig = configField.GetValue(null);
        var oldRegistry = LordshipBehavior.Instance;
        var oldTitles = FeudalTitleBehavior.Instance;
        var oldCampaign = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        void Patch(Type type, string property, string handler) => h.Patch(AccessTools.PropertyGetter(type, property),
            prefix: new HarmonyMethod(typeof(CaptainTitleTests), handler));
        void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
        void Instance(Type type, object value) => AccessTools.PropertySetter(type, "Instance").Invoke(null, new[] { value });
        void Clear() => AccessTools.Method(names, "InvalidateCache").Invoke(null, null);
        string Title(Hero hero)
        {
            object[] args = { hero, null };
            return (bool)AccessTools.Method(display, "TryGetDisplayStyle", new[] { typeof(Hero), typeof(string).MakeByRefType() })
                .Invoke(null, args) ? (string)args[1] : null;
        }
        string Name(Hero hero)
        {
            object[] args = { hero, new TextObject("{=!}Test") };
            AccessTools.Method(names, "Postfix").Invoke(null, args);
            return ((TextObject)args[1]).ToString();
        }
        try
        {
            GameTexts.Initialize(new GameTextManager());
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { Blank<Campaign>() });
            ticks.SetValue(null, 24000L); _day = 10;
            _player = Blank<Clan>(); _player.StringId = "player";
            _company = Blank<Clan>(); _company.StringId = "mercenaries";
            Hero Person(Clan clan, string id)
            {
                var hero = Blank<Hero>(); hero.StringId = id; Houses[hero] = clan;
                Set(hero, "_heroState", Hero.CharacterStates.Active);
                return hero;
            }
            var leader = Person(_player, "clan_leader"); Set(_player, "_leader", leader); _family = leader;
            var retainer = Person(_player, "retainer"); _female = Person(_player, "female_retainer");
            var captain = Person(_company, "mercenary_leader"); Set(_company, "_leader", captain);
            var member = Person(_company, "mercenary_member"); _minor = Person(_player, "minor");
            Patch(typeof(Clan), "PlayerClan", nameof(Player)); Patch(typeof(Clan), "Culture", nameof(Culture));
            Patch(typeof(Hero), "Clan", nameof(House)); Patch(typeof(Hero), "IsFemale", nameof(Female));
            Patch(typeof(Hero), "IsChild", nameof(Child)); Patch(typeof(Hero), "IsAlive", nameof(Yes));
            Patch(typeof(Hero), "IsDisabled", nameof(No)); Patch(typeof(Hero), "IsNotable", nameof(No));
            Patch(typeof(Clan), "IsEliminated", nameof(No)); Patch(typeof(Clan), "IsBanditFaction", nameof(No));
            Patch(typeof(Clan), "IsMinorFaction", nameof(MinorClan)); Patch(typeof(Clan), "IsUnderMercenaryService", nameof(Hired));
            Patch(typeof(CampaignTime), "Now", nameof(Now));
            h.Patch(AccessTools.Method(assembly.GetType("BellumCivile.HouseholdMembership"), "IsRetainer"),
                prefix: new HarmonyMethod(typeof(CaptainTitleTests), nameof(Retainer)));
            h.Patch(AccessTools.Method(typeof(LordshipBehavior), "GetGrant", new[] { typeof(Hero) }),
                prefix: new HarmonyMethod(typeof(CaptainTitleTests), nameof(Land)));
            var registry = new LordshipBehavior(); Set(registry, "_ready", true); Set(registry, "_campaign", Campaign.Current);
            Instance(typeof(LordshipBehavior), registry);
            Instance(typeof(FeudalTitleBehavior), new FeudalTitleBehavior());
            var contracts = (IDictionary)AccessTools.Field(typeof(LordshipBehavior), "_indenturesByHero").GetValue(registry);
            foreach (Hero hero in new[] { retainer, _female, _minor, _family })
                contracts[hero] = new RetainerIndentureRecord { Holder = hero, Employer = _player, EndDay = 94 };
            var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "BellumCivile.csproj"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Missing shipped captain presets.");
            foreach (string preset in new[] { "anglicized", "immersive", "bannerkings" })
            {
                var config = Activator.CreateInstance(configType, true);
                AccessTools.Method(configType, "LoadFile").Invoke(config, new object[] {
                    Path.Combine(root.FullName, "ModuleData", "bellum_title_styles_" + preset + ".xml"), false, true });
                configField.SetValue(null, config);
                foreach (string culture in new[] { "Empire", "Empire_n", "Empire_s", "Empire_w", "Vlandia", "Battania", "Khuzait", "Aserai", "Sturgia", "Nord", "Rhodok" })
                {
                    var nation = Blank<CultureObject>(); nation.StringId = culture.ToLowerInvariant();
                    AccessTools.PropertySetter(typeof(BasicCultureObject), "Name").Invoke(nation, new object[] { new TextObject("{=!}" + culture) });
                    Cultures[_player] = Cultures[_company] = nation; Clear();
                    var expected = Expected(preset, culture);
                    check(Title(retainer) == expected.Male && Title(_female) == expected.Female,
                        preset + "/" + culture + ": active indentures use both cultural captain forms without a lordship");
                    check(Title(captain) == expected.Male && Title(member) == null,
                        preset + "/" + culture + ": an unhired minor-faction leader, but not ordinary members, shares the captain style");
                    _hired = true;
                    check(Title(leader) == expected.Male,
                        preset + "/" + culture + ": the player clan leader receives the same style while serving as a mercenary");
                    _hired = false;
                    check(Title(leader) != expected.Male,
                        preset + "/" + culture + ": a player clan's persistent minor-faction flag does not imply mercenary service");
                    check(Name(retainer) == (preset == "bannerkings" ? "Test, " + expected.Male : expected.Male + " Test"),
                        preset + "/" + culture + ": actual names follow the preset's prefix or suffix format");
                    _day = 94;
                    check(Title(retainer) == null && Name(retainer) == "Test",
                        preset + "/" + culture + ": expiry immediately drops Captain from an already cached name");
                    _day = 10; _landed = retainer;
                    check(Title(retainer) != null && Title(retainer) != expected.Male,
                        preset + "/" + culture + ": a lordship takes precedence over a simultaneous paid contract");
                    _landed = null;
                    if (culture == "Vlandia")
                    {
                        var realm = Blank<Kingdom>(); realm.StringId = "empire_w";
                        Set(realm, "_rulingClan", _company); Set(_player, "_kingdom", realm);
                        check(Title(retainer) == expected.Male,
                            preset + ": a foreign sovereign style does not overwrite a captain's own clan culture");
                        Set(_player, "_kingdom", null);
                        realm.StringId = "vlandia";
                        var titles = FeudalTitleBehavior.Instance;
                        var records = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
                        void Holdings(params FeudalTitleRecord[] holdings)
                        {
                            records.Clear(); foreach (var title in holdings) records[title.TitleId] = title;
                            AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
                        }
                        string baron = preset == "anglicized" ? "Baron" : preset == "immersive" ? "Baro" : "Bers";
                        string king = preset == "anglicized" ? "King" : preset == "immersive" ? "Rex" : "Reis";
                        _hired = true;
                        check(Name(leader).Contains(expected.Male), preset + ": an active player mercenary title is cached normally");
                        _hired = false; Set(_player, "_kingdom", realm);
                        check(!string.IsNullOrEmpty(Title(leader)) && Title(leader) != expected.Male
                            && !Name(leader).Contains(expected.Male),
                            preset + ": a landless player vassal uses the noble fallback, not the cached mercenary title");
                        var barony = new FeudalTitleRecord("captain_barony", "Sargot", FeudalTitleType.Barony,
                            _player.StringId, _player.StringId, "", "", "", 0, 0);
                        Holdings(barony);
                        check(Title(leader) == baron && Name(leader).Contains(baron) && !Name(leader).Contains(expected.Male),
                            preset + ": becoming a landed vassal replaces the cached captain title with the held noble rank");
                        var crown = new FeudalTitleRecord("captain_crown", "Vlandia", FeudalTitleType.Kingdom,
                            _company.StringId, _player.StringId, "", "", "", 0, 0);
                        Set(realm, "_rulingClan", _player); Holdings(barony, crown);
                        check(Title(leader) == king && Name(leader).Contains(king) && !Name(leader).Contains(expected.Male),
                            preset + ": a player ruler with de facto Crown ownership uses the royal rank despite the minor-faction flag");
                        Set(realm, "_rulingClan", _company); Holdings(barony);
                        check(Title(leader) == baron, preset + ": abdication returns to the remaining noble rank rather than Captain");
                        Set(_player, "_kingdom", null);
                        check(Title(leader) == baron, preset + ": independent landed players also retain their noble rank");
                        Holdings();
                    }
                }
            }
            var empty = Activator.CreateInstance(configType, true); configField.SetValue(null, empty); Clear();
            check(Title(retainer) == "Captain" && Title(_female) == "Captain" && Title(captain) == null,
                "Unconfigured styles give indentured retainers a Captain fallback without changing custom mercenary-leader behavior");
            check(Title(_minor) == null && Title(_family) != "Captain" && Title(null) == null,
                "Minors, genuine family and missing heroes never acquire an indentured captain title");
            Houses[retainer] = _company; Clear();
            check(Title(retainer) == null, "Leaving the employing clan removes the contract honorific");
            Houses[retainer] = _player; contracts.Remove(retainer); Clear();
            check(Title(retainer) == null && Name(retainer) == "Test", "An unpaid, unlanded retainer receives no knight or captain title");
        }
        finally
        {
            Clear(); h.UnpatchAll(h.Id);
            configField.SetValue(null, oldConfig); Instance(typeof(LordshipBehavior), oldRegistry); Instance(typeof(FeudalTitleBehavior), oldTitles);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign }); ticks.SetValue(null, oldTicks);
            Houses.Clear(); Cultures.Clear(); _player = _company = null; _family = _minor = _landed = _female = null;
            _hired = false;
        }
    }
}
