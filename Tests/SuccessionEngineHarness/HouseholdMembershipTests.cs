using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

internal static class HouseholdMembershipTests
{
    private static Hero _player;
    private static Clan _house;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool MainHero(ref Hero __result) { __result = _player; return false; }
    private static bool PlayerClan(ref Clan __result) { __result = _house; return false; }

    internal static void Run(Action<bool, string> check, string aocDll = null)
    {
        var harmony = new Harmony("bellum.tests.household_membership");
        var assembly = typeof(BellumMarriageModel).Assembly;
        var membership = assembly.GetType("BellumCivile.HouseholdMembership", true);
        var eligibility = assembly.GetType("BellumCivile.LordshipEligibility", true);
        var policy = assembly.GetType("BellumCivile.MarriageParticipantPolicy", true);
        var display = assembly.GetType("BellumCivile.Patches.HouseholdOccupationDisplayPatch", true);
        var oldRegistry = LordshipBehavior.Instance;
        var oldCampaign = Campaign.Current;
        var spouseField = AccessTools.Field(typeof(Hero), "_spouse");
        void Invalidate() => AccessTools.Method(membership, "Invalidate").Invoke(null, new object[] { null });
        bool Flag(string name, Hero hero) => (bool)AccessTools.Method(membership, name).Invoke(null, new object[] { hero });
        bool Permitted(Hero hero, bool governor) => (bool)AccessTools.Method(eligibility, "Permitted")
            .Invoke(null, new object[] { hero, governor, null });
        bool Blood(Hero hero, Hero relative) => (bool)AccessTools.Method(membership, "IsBloodRelative")
            .Invoke(null, new object[] { hero, relative });
        void State(Hero hero, Hero.CharacterStates state) => AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, state);
        Clan House(string id)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            foreach (string name in new[] { "_heroesCache", "_companionsCache", "_aliveLordsCache", "_deadLordsCache" })
                AccessTools.Field(typeof(Clan), name).SetValue(clan, new MBList<Hero>());
            return clan;
        }
        Hero Person(Clan clan, string id)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, clan);
            State(hero, Hero.CharacterStates.Active);
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_exSpouses").SetValue(hero, new MBList<Hero>());
            AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero, new object[] { Occupation.Lord });
            ((MBList<Hero>)AccessTools.Field(typeof(Clan), "_heroesCache").GetValue(clan)).Add(hero);
            return hero;
        }
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), prefix: new HarmonyMethod(typeof(HouseholdMembershipTests), nameof(MainHero)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(HouseholdMembershipTests), nameof(PlayerClan)));
            var campaign = Blank<Campaign>(); AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            _house = House("household_player");
            _player = Person(_house, "household_head"); AccessTools.Field(typeof(Clan), "_leader").SetValue(_house, _player);
            var son = Person(_house, "son"); son.Father = _player;
            var companion = Person(_house, "companion"); var widow = Person(_house, "companion_widow");
            var primary = Person(_house, "primary_wife"); widow.IsFemale = primary.IsFemale = true;
            var otherRetainer = Person(_house, "other_retainer");
            var registry = new LordshipBehavior();
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            AccessTools.Field(typeof(LordshipBehavior), "_campaign").SetValue(registry, campaign);
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
            companion.Spouse = widow;
            Invalidate();
            check(Flag("IsFamily", _player) && Flag("IsFamily", son) && Blood(son, _player),
                "The clan head and blood descendants retain native family membership");
            check(!Flag("IsFamily", widow) && Flag("IsRetainer", widow),
                "An unrelated retainer's spouse does not become part of the ruling family");
            State(companion, Hero.CharacterStates.Dead); widow.Spouse = null;
            check(!Flag("IsFamily", widow), "An unrelated retainer's widow does not acquire family standing through bereavement");
            _player.Spouse = primary;
            check(Flag("IsFamily", primary) && !Flag("IsRetainer", primary),
                "A native marriage updates family standing without cache invalidation or roster changes");
            check(Permitted(primary, false) && Permitted(primary, true),
                "The primary spouse is exempt from both party-command and governor endowment requirements");
            check(!Permitted(widow, false) && !Permitted(widow, true),
                "Before remarriage the unendowed retainer remains blocked from both appointments");
            var cache = (IDictionary)AccessTools.Field(membership, "Cache").GetValue(null);
            object entry = cache[_house];
            spouseField.SetValue(widow, _player);
            check(_player.Spouse == primary && widow.Spouse == _player,
                "The AoC-style additional-wife link leaves the player's primary spouse intact");
            check(Flag("IsFamily", widow) && !Flag("IsRetainer", widow),
                "A reverse-only marriage link immediately recognizes the additional wife as married-in family");
            check(Permitted(widow, false) && Permitted(widow, true),
                "An additional wife needs no lordship to qualify for clan command or governorship");
            check(!(bool)AccessTools.Method(display, "IsRetainer").Invoke(null, new object[] { widow }),
                "Occupation display no longer substitutes Retainer for the additional wife");
            check((float)AccessTools.Method(policy, "RetainerCost").Invoke(null, new object[] { widow, _player }) == 0f,
                "Marriage scoring recognizes additional spouses as family rather than retainers");
            check(!Blood(widow, _player) && !Blood(widow, son),
                "Married-in family membership does not manufacture bloodline or dynastic claim eligibility");
            check(ReferenceEquals(entry, cache[_house]),
                "Changing spouse links reuses the ancestry cache instead of rebuilding family trees");
            if (!string.IsNullOrWhiteSpace(aocDll)) CheckActualAoc(check, aocDll, primary, widow, membership);

            spouseField.SetValue(widow, null); widow.ExSpouses.Add(_player);
            check(!Flag("IsFamily", widow) && Flag("IsRetainer", widow),
                "Ending an additional marriage removes married-in status immediately despite a living ex-spouse record");
            check(!Permitted(widow, false) && !Permitted(widow, true),
                "Divorced unrelated retainers regain the normal endowment requirements");
            _player.Spouse = null;
            check(!Flag("IsFamily", primary), "Native divorce also removes stale primary-wife membership without invalidation");
            spouseField.SetValue(_player, primary);
            check(Flag("IsFamily", primary) && primary.Spouse == null,
                "Forward-only spouse links remain recognized as before");
            spouseField.SetValue(primary, otherRetainer); spouseField.SetValue(otherRetainer, primary);
            check(!Flag("IsFamily", otherRetainer), "Spouses of married-in family do not recursively acquire ruling-family standing");
            spouseField.SetValue(primary, null); spouseField.SetValue(otherRetainer, null);
            spouseField.SetValue(_player, null); primary.ExSpouses.Add(_player);
            State(_player, Hero.CharacterStates.Dead);
            check(Flag("IsFamily", primary), "A reverse-only widowed link retains family standing when the blood relative dies");
            primary.ExSpouses.Clear(); _player.ExSpouses.Add(primary);
            check(Flag("IsFamily", primary), "A forward-only widowed link remains compatible with native family history");
            State(_player, Hero.CharacterStates.Active);
            check(!Flag("IsFamily", primary), "Former spouses of a living relative are not treated as widows");
            spouseField.SetValue(widow, son);
            check(Flag("IsFamily", widow), "Reverse spouse recognition also applies to other blood relatives, not only the player");
            spouseField.SetValue(widow, null);
            check(!Flag("IsFamily", widow), "Ending another blood relative's marriage updates immediately");
            var abroad = House("household_foreign");
            var foreignHead = Person(abroad, "foreign_head"); AccessTools.Field(typeof(Clan), "_leader").SetValue(abroad, foreignHead);
            var foreignSpouse = Person(abroad, "foreign_spouse"); spouseField.SetValue(foreignSpouse, _player);
            check(!Flag("IsFamily", foreignSpouse) && Flag("IsRetainer", foreignSpouse),
                "A marriage to another house's head does not grant family status inside an unrelated household");
            check(Flag("IsRetainer", otherRetainer) && !Blood(otherRetainer, _player),
                "Unrelated retainers and their bloodline rules remain unchanged");
            check(!Flag("IsFamily", null) && !Flag("IsRetainer", null), "Missing heroes remain safely unclassified");
        }
        finally
        {
            Invalidate(); harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { oldRegistry });
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            _player = null; _house = null;
        }
    }

    private static void CheckActualAoc(Action<bool, string> check, string dll, Hero primary, Hero additional, Type membership)
    {
        Type aoc = Assembly.LoadFrom(dll).GetType("FriendsWithBenefits.FriendsWithBenefitsBehavior", true);
        object behavior = FormatterServices.GetUninitializedObject(aoc);
        AccessTools.Field(aoc, "_stableMarriageStateOwnerHeroId").SetValue(behavior, _player.StringId);
        AccessTools.Field(aoc, "_stableMarriageSpouses").SetValue(behavior, new List<string> { primary.StringId, additional.StringId });
        AccessTools.Field(aoc, "_stableMarriageDivorcedSpouses").SetValue(behavior, new List<string>());
        check((bool)AccessTools.Method(aoc, "IsRecognizedPlayerSpouse").Invoke(behavior, new object[] { additional })
            && (bool)AccessTools.Method(membership, "IsFamily").Invoke(null, new object[] { additional }),
            "The actual supplied AoC DLL and Bellum now agree on the additional wife's family status");
        check(!typeof(BellumMarriageModel).Assembly.GetReferencedAssemblies().Any(a => a.Name == "FriendsWithBenefits"),
            "Compatibility requires no hard reference to Affairs of Calradia");
    }
}
