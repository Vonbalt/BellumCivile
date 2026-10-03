using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

internal static class MarriageClaimForecastTests
{
    private static Clan _birth;
    private static Kingdom _realm;
    private static bool _eligible;
    private static bool Birth(ref Clan __result) { __result = _birth; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ambition(ref float __result) { __result = 1; return false; }
    private static bool Eligible(ref bool __result) { __result = _eligible; return false; }
    private static bool Children(ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero>()); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var helper = typeof(StrategicMarriageBehavior).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var cacheType = helper.GetNestedType("MarriageTitleClaimCache", BindingFlags.NonPublic);
        var score = AccessTools.Method(helper, "CalculateDirectionalTitleClaimMarriageValue");
        var house = Blank<Clan>(); house.StringId = "receiving";
        _birth = Blank<Clan>(); _birth.StringId = "birth";
        _realm = Blank<Kingdom>(); _realm.StringId = "home";
        var hero = Blank<Hero>(); hero.StringId = "carrier";
        var title = new FeudalTitleRecord("land", "Land", FeudalTitleType.Barony,
            "birth", "birth", "county", "", "home", 0, 0);
        var claims = new List<FeudalClaimRecord>();
        var held = new List<FeudalTitleRecord> { title };
        var ownHeld = new List<FeudalTitleRecord>();
        var existing = new Dictionary<string, FeudalClaimStrength>();
        var reasons = new List<string>();
        float Score(bool fertile = true)
        {
            var cache = AccessTools.Method(cacheType, "Build").Invoke(null, new object[] { null, 0 });
            ((Dictionary<string, FeudalTitleRecord>)AccessTools.Property(cacheType, "TitlesById").GetValue(cache))[title.TitleId] = title;
            ((Dictionary<string, List<FeudalClaimRecord>>)AccessTools.Property(cacheType, "ClaimsByClan").GetValue(cache))["birth"] = claims;
            ((Dictionary<string, List<FeudalTitleRecord>>)AccessTools.Field(cacheType, "_heldTitlesByClan").GetValue(cache))["birth"] = held;
            ((Dictionary<string, List<FeudalTitleRecord>>)AccessTools.Field(cacheType, "_heldTitlesByClan").GetValue(cache))["receiving"] = ownHeld;
            ((Dictionary<string, Dictionary<string, FeudalClaimStrength>>)AccessTools.Field(cacheType, "_existingStrengths").GetValue(cache))["receiving"] = existing;
            reasons.Clear();
            return (float)score.Invoke(null, new object[] { house, hero, _birth, hero, cache, reasons, 1f, fertile });
        }
        var harmony = new Harmony("bellum.test.marriage_claim_forecasts");
        void Patch(MethodBase target, string method) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(MarriageClaimForecastTests), method));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(Birth));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Children"), nameof(Children));
            Patch(AccessTools.Method(helper, "MarriagePoliticalRealm"), nameof(Realm));
            Patch(AccessTools.Method(helper, "CalculateTitleAmbitionMultiplier"), nameof(Ambition));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "IsCloseBloodClaimantOfClan"), nameof(Eligible));
            _eligible = true;
            float birthright = Score();
            check(birthright == 53 && reasons[0].Contains("birthright forecast"),
                "Eligible incoming relative forecasts actual strong marriage birthright before a claim exists");
            check(Score(false) == birthright, "Immediate marriage birthrights do not require future children");
            existing[title.TitleId] = FeudalClaimStrength.Strong;
            check(Score() == 0, "Existing equally strong claim gives no invented additional asset");
            existing[title.TitleId] = FeudalClaimStrength.Weak;
            check(Score() == 15, "Weak-to-strong upgrade scores only marginal improvement");
            existing.Clear();
            title.SetAssociatedKingdom("remote");
            check(Score() == 11.25f, "Remote birthright has quarter practical value without realm bonus");
            ownHeld.Add(new FeudalTitleRecord("neighbor", "Neighbor", FeudalTitleType.Barony,
                "receiving", "receiving", "county", "", "home", 0, 0));
            check(Score() == 65, "Indexed sibling-title relevance restores full local value even across realm boundaries");
            ownHeld[0].SetParentTitle("");
            title.SetParentTitle("");
            check(Score() == 11.25f, "Two missing parents do not invent local adjacency");
            ownHeld.Clear();
            title.SetParentTitle("county");
            title.SetAssociatedKingdom("home");
            title.SetDeFactoHolder("receiving");
            check(Score() == 0, "Already possessed land is excluded just like the actual wedding hook");
            title.SetDeFactoHolder("birth");
            title.SetDeJureHolder("third");
            check(Score() == 0, "De-facto-only birth-house possession conveys no birthright");
            title.SetDeJureHolder("birth");
            _eligible = false;
            check(Score() == 0, "Unrelated noble does not invent a birth-house claim");
            claims.Add(new FeudalClaimRecord("strong", "birth", "land", FeudalClaimStrength.Strong,
                "test", "carrier", "birth", 0, -1, carrierHeroId: "carrier"));
            check(Score() == 9.5f && reasons[0].Contains("conditional inherited"),
                "Pre-existing strong claim forecasts weak inheritance at quarter confidence, not immediate transfer");
            check(Score(false) == 0, "No prospective descendants or existing receiving-house children means no inheritance bonus");
            claims.Clear();
            claims.Add(new FeudalClaimRecord("weak", "birth", "land", FeudalClaimStrength.Weak,
                "test", "carrier", "birth", 0, -1, carrierHeroId: "carrier"));
            check(Score() == 0, "Weak claims terminate with their carrier and have no descendant forecast");
            check(claims.Count == 1 && claims[0].IsActive && title.DeJureHolderClanId == "birth",
                "Forecast does not create claims, deactivate claims or transfer titles");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
