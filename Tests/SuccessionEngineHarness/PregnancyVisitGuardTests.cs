using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class PregnancyVisitGuardTests
{
    private static readonly List<string> Logs = new List<string>();
    private static Settlement _home;
    private static float _roll;
    private static int _rolls, _locations, _flushes;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Log(string __0) { Logs.Add(__0); return false; }
    private static bool Flush() { _flushes++; return false; }
    private static bool NoDebug(ref bool __result) { __result = false; return false; }
    private static bool NoFaction(ref IFaction __result) { __result = null; return false; }
    private static bool NoParty(ref MobileParty __result) { __result = null; return false; }
    private static bool Location(ref Settlement __result) { _locations++; __result = _home; return false; }
    private static bool Roll(ref float __result) { _rolls++; __result = _roll; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.pregnancy_visit_guard");
        var options = typeof(ConsistentSpouseVisitPatch).Assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var oldCampaign = Campaign.Current;
        var setCampaign = AccessTools.PropertySetter(typeof(Campaign), "Current");
        var clan = AccessTools.Field(typeof(Hero), "_clan");
        var state = AccessTools.Field(typeof(Hero), "_heroState");
        Hero Person(string id, Clan house = null)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            state.SetValue(hero, Hero.CharacterStates.Active); clan.SetValue(hero, house);
            return hero;
        }
        var household = Blank<Clan>(); household.StringId = "pregnancy_house";
        var otherHousehold = Blank<Clan>(); otherHousehold.StringId = "pregnancy_spouse_house";
        var mother = Person("clanless_mother"); var spouse = Person("mother_spouse", otherHousehold);
        mother.Spouse = spouse;
        var native = Blank<PregnancyCampaignBehavior>();
        var nearby = AccessTools.Method(typeof(PregnancyCampaignBehavior), "CheckAreNearby");
        var refresh = AccessTools.Method(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit");
        bool Nearby(Hero woman, Hero partner) => (bool)nearby.Invoke(native, new object[] { woman, partner });
        bool originalFailed = false;
        try { Blank<DefaultPregnancyModel>().GetDailyChanceOfPregnancyForHero(mother); }
        catch (NullReferenceException) { originalFailed = true; }
        check(originalFailed, "The unpatched native pregnancy calculation reproduces the reported clanless-hero crash");
        try
        {
            void Patch(Type type, string property, string prefix) => harmony.Patch(AccessTools.PropertyGetter(type, property),
                prefix: new HarmonyMethod(typeof(PregnancyVisitGuardTests), prefix));
            harmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), prefix: new HarmonyMethod(typeof(PregnancyVisitGuardTests), nameof(Log)));
            harmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Flush"), prefix: new HarmonyMethod(typeof(PregnancyVisitGuardTests), nameof(Flush)));
            Patch(options, "ShowDebugMessagesInGame", nameof(NoDebug));
            Patch(typeof(Hero), "MapFaction", nameof(NoFaction));
            Patch(typeof(Hero), "CurrentSettlement", nameof(Location));
            Patch(typeof(Hero), "PartyBelongedTo", nameof(NoParty));
            Patch(typeof(MBRandom), "RandomFloat", nameof(Roll));
            harmony.CreateClassProcessor(typeof(ConsistentSpouseVisitPatch)).Patch();
            setCampaign.Invoke(null, new object[] { Blank<Campaign>() });
            _home = Blank<Settlement>(); _roll = 0.1f; _rolls = _locations = _flushes = 0; Logs.Clear();
            check(!(bool)AccessTools.PropertyGetter(options, "ShowDebugMessagesInGame").Invoke(null, null),
                "The guard is tested with debug messages explicitly disabled");
            check(!Nearby(mother, spouse), "A living clanless woman cannot enter the native pregnancy calculation");
            refresh.Invoke(native, new object[] { mother });
            check(_rolls == 0 && _locations == 0, "The real native visit caller short-circuits before location checks, random rolls or pregnancy calculation");
            check(Logs.Count == 1 && _flushes == 1, "A clanless visit is logged and flushed even with debug messages disabled");
            check(Logs[0].Contains("[SPOUSE-VISIT]") && Logs[0].Contains("skipped=missing_pregnancy_clan")
                && Logs[0].Contains("hero=clanless_mother") && Logs[0].Contains("hero_clan=null")
                && Logs[0].Contains("spouse=mother_spouse") && Logs[0].Contains("spouse_clan=pregnancy_spouse_house")
                && Logs[0].Contains("hero_companion_of=null") && Logs[0].Contains("spouse_companion_of=null")
                && Logs[0].Contains("hero_occupation=") && Logs[0].Contains("spouse_occupation="),
                "Diagnostics identify both spouses, effective clans, companion membership and occupations without resolving names");
            for (int attempt = 0; attempt < 20; attempt++) refresh.Invoke(native, new object[] { mother });
            check(Logs.Count == 1 && _flushes == 1, "Daily retries produce only one diagnostic for an affected hero in a session");
            mother.IsPregnant = true; refresh.Invoke(native, new object[] { mother });
            check(mother.IsPregnant && mother.Spouse == spouse && spouse.Spouse == mother && mother.Clan == null && spouse.Clan == otherHousehold,
                "Skipping a visit does not clear pregnancy, marriage links, or assign a replacement clan");
            mother.IsPregnant = false;
            var secondMother = Person("second_clanless_mother"); var secondSpouse = Person("second_spouse", otherHousehold);
            secondMother.Spouse = secondSpouse;
            refresh.Invoke(native, new object[] { secondMother });
            check(Logs.Count == 2, "A different clanless hero receives an independent diagnostic");
            setCampaign.Invoke(null, new object[] { Blank<Campaign>() });
            refresh.Invoke(native, new object[] { mother });
            check(Logs.Count == 3 && _flushes == 3, "Loading another campaign session permits fresh diagnostics for the same hero");

            clan.SetValue(mother, household);
            check(Nearby(mother, spouse) && _rolls == 0, "Restoring the required clan immediately restores together visits without a random roll");
            _home = null; _roll = 0.1f;
            check(Nearby(mother, spouse), "Valid separate-clan spouses retain the existing distant visit chance");
            _roll = 0.2f;
            check(!Nearby(mother, spouse) && _rolls == 2, "The existing strict twenty-percent distant visit threshold is unchanged");
            _home = Blank<Settlement>();
            state.SetValue(mother, Hero.CharacterStates.Prisoner);
            check(!Nearby(mother, spouse), "A valid household does not bypass the mother's captivity restriction");
            state.SetValue(mother, Hero.CharacterStates.Active); state.SetValue(spouse, Hero.CharacterStates.Prisoner);
            check(!Nearby(mother, spouse), "A valid household does not bypass the spouse's captivity restriction");
            state.SetValue(spouse, Hero.CharacterStates.Active); clan.SetValue(spouse, null);
            check(Nearby(mother, spouse), "The guard does not require an unrelated clan reference on the other spouse");
            var companionField = (FieldInfo)PatchProcessor.GetOriginalInstructions(AccessTools.PropertyGetter(typeof(Hero), "CompanionOf"))
                .First(i => i.opcode == OpCodes.Ldfld).operand;
            clan.SetValue(mother, null); companionField.SetValue(mother, household);
            check(mother.Clan == household && Nearby(mother, spouse), "A companion's effective clan remains valid even without a noble-clan backing field");
            check(Logs.Count == 3, "A previous warning does not blacklist recovered heroes or produce logs for valid visits");
            check(!Nearby(null, spouse) && !Nearby(mother, null), "Missing participants remain safely ineligible");
            companionField.SetValue(mother, null); state.SetValue(mother, Hero.CharacterStates.Dead);
            check(!Nearby(mother, spouse) && Logs.Count == 3, "Dead participants remain excluded without extra clanless diagnostics");
            check(Harmony.GetPatchInfo(AccessTools.Method(typeof(DefaultPregnancyModel), "GetDailyChanceOfPregnancyForHero")) == null
                && Harmony.GetPatchInfo(AccessTools.Method(typeof(PregnancyCampaignBehavior), "DailyTickHero")) == null,
                "The safeguard does not patch native pregnancy probabilities or the daily existing-pregnancy handler");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); setCampaign.Invoke(null, new object[] { oldCampaign });
            Logs.Clear(); _home = null;
        }
    }
}
