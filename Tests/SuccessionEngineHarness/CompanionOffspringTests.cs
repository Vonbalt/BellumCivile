using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class CompanionOffspringTests
{
    private static Hero _player, _newborn, _removed;
    private static Clan _playerHouse;
    private static MBReadOnlyList<Hero> _visible;
    private static CampaignEventDispatcher _dispatcher;
    private static Clan _birthHouse;
    private static Occupation _birthOccupation;
    private static Hero _birthMother, _birthFather;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool MainHero(ref Hero __result) { __result = _player; return false; }
    private static bool PlayerHouse(ref Clan __result) { __result = _playerHouse; return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = _dispatcher; return false; }
    private static bool Skip() => false;
    private static bool BirthDates(ref ValueTuple<CampaignTime, CampaignTime> __result)
    { __result = default; return false; }
    private static bool CreateBaby(CharacterObject character, ref Hero __result)
    {
        AccessTools.Field(typeof(CharacterObject), "_originCharacter").SetValue(_newborn.CharacterObject,
            character.OriginalCharacter ?? character);
        __result = _newborn;
        return false;
    }
    private static bool BirthArgs(object __instance, Hero hero, bool isOffspring)
    {
        AccessTools.Field(__instance.GetType(), "<Hero>k__BackingField").SetValue(__instance, hero);
        AccessTools.Field(__instance.GetType(), "<IsOffspring>k__BackingField").SetValue(__instance, isOffspring);
        return false;
    }
    private static bool InitializeBaby(Hero hero, object initializationArgs)
    {
        object Read(string property) => AccessTools.Property(initializationArgs.GetType(), property).GetValue(initializationArgs);
        _birthHouse = (Clan)Read("Clan");
        _birthOccupation = (Occupation)Read("Occupation");
        _birthMother = (Hero)Read("Mother"); _birthFather = (Hero)Read("Father");
        hero.Mother = _birthMother; hero.Father = _birthFather; hero.IsFemale = (bool)Read("IsFemale");
        hero.Clan = _birthHouse;
        AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero, new object[] { _birthOccupation });
        return false;
    }
    private static bool AllAlive(ref MBReadOnlyList<Hero> __result) { __result = _visible; return false; }
    private static bool Remove(Hero __0) { _removed = __0; return false; }
    private static bool NoSettlement(ref Settlement __result) { __result = null; return false; }
    private static bool Roll(ref float __result) { __result = 0f; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.companion_offspring");
        var oldCampaign = Campaign.Current;
        var current = AccessTools.PropertySetter(typeof(Campaign), "Current");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(CompanionOffspringTests), prefix));
        Hero Person(Clan house, Occupation occupation = Occupation.Lord, bool companion = false)
        {
            var hero = Blank<Hero>();
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
            AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero, new object[] { occupation });
            var character = Blank<CharacterObject>();
            AccessTools.Field(typeof(CharacterObject), "_heroObject").SetValue(character, hero);
            AccessTools.Field(typeof(Hero), "_characterObject").SetValue(hero, character);
            if (companion) hero.CompanionOf = house;
            else AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, house);
            return hero;
        }
        Clan House()
        {
            var clan = Blank<Clan>();
            foreach (string name in new[] { "_heroesCache", "_companionsCache", "_aliveLordsCache", "_deadLordsCache" })
                AccessTools.Field(typeof(Clan), name).SetValue(clan, new MBList<Hero>());
            AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, Person(clan));
            return clan;
        }
        Hero Birth(Hero mother, Hero father, bool female)
        {
            _newborn = Person(null);
            return HeroCreator.DeliverOffSpring(mother, father, female);
        }
        bool CleanupSelects(Hero child)
        {
            var behavior = Blank<CompanionsCampaignBehavior>();
            AccessTools.Field(typeof(CompanionsCampaignBehavior), "_aliveCompanionTemplates")
                .SetValue(behavior, new HashSet<CharacterObject>());
            AccessTools.Method(typeof(CompanionsCampaignBehavior), "OnHeroCreated")
                .Invoke(behavior, new object[] { child, true });
            _visible = new MBList<Hero> { child }; _removed = null;
            AccessTools.Method(typeof(CompanionsCampaignBehavior), "TryKillCompanion").Invoke(behavior, null);
            return _removed == child;
        }
        try
        {
            var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
            AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
            current.Invoke(null, new object[] { campaign });
            AccessTools.PropertySetter(typeof(GameModels), "HeroCreationModel")
                .Invoke(models, new object[] { Blank<DefaultHeroCreationModel>() });
            _dispatcher = Blank<CampaignEventDispatcher>(); _player = null; _playerHouse = null;
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(PlayerHouse));
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroChangedClan"), nameof(Skip));
            Patch(AccessTools.Method(typeof(DefaultHeroCreationModel), "GetBirthAndDeathDay"), nameof(BirthDates));
            Patch(AccessTools.Method(typeof(HeroCreator), "CreateHero"), nameof(CreateBaby));
            Patch(AccessTools.Constructor(typeof(HeroCreator).GetNestedType("HeroInitializationArgs", BindingFlags.NonPublic),
                new[] { typeof(Hero), typeof(bool) }), nameof(BirthArgs));
            Patch(AccessTools.Method(typeof(HeroCreator), "InitializeHeroFromSettings"), nameof(InitializeBaby));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "AllAliveHeroes"), nameof(AllAlive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "CurrentSettlement"), nameof(NoSettlement));
            Patch(AccessTools.PropertyGetter(typeof(MBRandom), "RandomFloat"), nameof(Roll));
            Patch(AccessTools.Method(typeof(KillCharacterAction), "ApplyByRemove"), nameof(Remove));
            var house = House(); var other = House();
            var mother = Person(house, Occupation.Wanderer, true); mother.IsFemale = true;
            var father = Person(house); mother.Spouse = father;
            Hero baseline = Birth(mother, father, true);
            check(baseline.Clan == house && baseline.IsWanderer && baseline.CompanionOf == null,
                "Unmodified native birth gives a companion's daughter a clan but inherits Wanderer occupation");
            check(CleanupSelects(baseline), "Native wanderer cleanup can remove that uncorrected clan-born child");
            check(harmony.CreateClassProcessor(typeof(SeparateSpouseBirthClanPatch)).Patch().Count == 1,
                "The installed birth method accepts both household and offspring occupation hooks");
            foreach (bool female in new[] { true, false })
            {
                Hero child = Birth(mother, father, female);
                check(child.Clan == house && child.IsLord && !child.IsWanderer && child.CompanionOf == null,
                    "Companion/noble offspring are ordinary clan-born members; female=" + female);
                check(_birthOccupation == Occupation.Lord && _birthHouse == house
                    && _birthMother == mother && _birthFather == father && child.IsFemale == female,
                    "Corrected occupation and unchanged parentage reach initialization before publication; female=" + female);
                check(!CleanupSelects(child), "Corrected offspring never enter native wanderer cleanup; female=" + female);
            }
            var companionFather = Person(house, Occupation.Wanderer, true);
            var nobleMother = Person(house); nobleMother.IsFemale = true;
            foreach (Hero woman in new[] { mother, nobleMother })
                foreach (bool female in new[] { true, false })
                    check(Birth(woman, companionFather, female).IsLord,
                        "A companion father does not pass Wanderer occupation to clan-born children; mother_wanderer="
                        + woman.IsWanderer + "; female=" + female);
            check(mother.IsWanderer && mother.CompanionOf == house && companionFather.IsWanderer
                && companionFather.CompanionOf == house && father.IsLord && mother.Spouse == father,
                "Birth correction leaves adult occupations, companion membership and marriage unchanged");
            check(baseline.IsWanderer, "The birth-time correction does not silently rewrite pre-existing characters");
            var foreign = Person(other, Occupation.Wanderer, true);
            check(Birth(mother, foreign, false).Clan == house && _birthOccupation == Occupation.Lord,
                "Separate-clan offspring use the same maternal household for occupation and clan selection");
            foreach (bool playerMother in new[] { true, false })
            {
                _player = playerMother ? mother : foreign; _playerHouse = _player.Clan;
                foreach (bool female in new[] { true, false })
                    check(Birth(mother, foreign, female).Clan == house && _birthOccupation == Occupation.Lord,
                        "Player-parent births preserve maternal household selection; player_mother=" + playerMother + "; female=" + female);
            }
            _player = null; _playerHouse = null;
            foreach (Occupation occupation in new[] { Occupation.Lord, Occupation.Special, Occupation.Headman })
            {
                var parent = Person(house, occupation);
                check(Birth(parent, father, true).Occupation == occupation,
                    "Non-Wanderer inherited occupations remain native: " + occupation);
            }
            var clanlessMother = Person(null, Occupation.Wanderer);
            var clanlessFather = Person(null, Occupation.Wanderer);
            check(Birth(clanlessMother, clanlessFather, true).Clan == null && _birthOccupation == Occupation.Wanderer,
                "Clanless births retain native occupation without inventing a household");
            foreach (string property in new[] { "IsMinorFaction", "IsBanditFaction", "IsEliminated" })
            {
                var excluded = House();
                if (property == "IsEliminated") AccessTools.Field(typeof(Clan), "_isEliminated").SetValue(excluded, true);
                else AccessTools.PropertySetter(typeof(Clan), property).Invoke(excluded, new object[] { true });
                var first = Person(excluded, Occupation.Wanderer); var second = Person(excluded, Occupation.Wanderer);
                check(Birth(first, second, true).Occupation == Occupation.Wanderer,
                    "Unsupported households retain native offspring occupation: " + property);
            }
            var noHead = House(); AccessTools.Field(typeof(Clan), "_leader").SetValue(noHead, null);
            check(Birth(Person(noHead, Occupation.Wanderer), Person(noHead), true).Occupation == Occupation.Wanderer,
                "A household without a living head is not implicitly normalized");
            var native = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(HeroCreator), "DeliverOffSpring")).ToList();
            var getter = AccessTools.PropertyGetter(typeof(Hero), "Occupation");
            var resolver = AccessTools.Method(typeof(SeparateSpouseBirthClanPatch), "ResolveBirthOccupation");
            var modified = SeparateSpouseBirthClanPatch.Transpiler(native).ToList();
            check(native.Count(c => c.Calls(getter)) == 2 && modified.Count(c => c.Calls(resolver)) == 2
                && !modified.Any(c => c.Calls(getter)), "Both same-sex native occupation branches use the scoped birth resolver");
            bool rejected = false;
            try { SeparateSpouseBirthClanPatch.Transpiler(native.Where(c => !c.Calls(getter))).ToList(); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Changed native occupation anchors fail explicitly instead of partially patching birth");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); current.Invoke(null, new object[] { oldCampaign });
            _player = _newborn = _removed = null; _playerHouse = _birthHouse = null; _visible = null;
        }
    }
}
