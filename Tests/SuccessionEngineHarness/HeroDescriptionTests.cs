using System;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem;

internal static class HeroDescriptionTests
{
    private static Clan _clan;
    private static bool House(ref Clan __result) { __result = _clan; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var patch = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.Patches.HeroEncyclopediaDescriptionPatch");
        var classify = AccessTools.Method(patch, "Classify");
        var build = AccessTools.Method(patch, "BuildText");
        object State(bool realm, bool rebel = false, bool feud = false, bool temporary = false, bool loyal = false)
            => classify.Invoke(null, new object[] { realm, rebel, feud, temporary, loyal });
        TextObject Description(object state, bool head, bool ruler, bool female = false, string rank = "High King", string realm = "New Kingdom")
        {
            var result = new TextObject("");
            var lord = new TextObject("Person").SetTextVariable("NAME", "High King Pryndor").SetTextVariable("GENDER", female ? 1 : 0);
            result.SetTextVariable("LORD", lord).SetTextVariable("TITLE", rank)
                .SetTextVariable("CLAN", "House Test").SetTextVariable("REALM", realm)
                .SetTextVariable("SIDE", "Pryndor's Coalition").SetTextVariable("REPUTATION", "honorable");
            return (TextObject)build.Invoke(null, new object[] { head, ruler, state, result.Attributes });
        }
        check(State(true).ToString() == "Realm", "Shared culture is not an input to civil-war classification");
        check(State(false).ToString() == "None", "Kingdomless nobles have no implied realm");
        check(State(true, true, false, true).ToString() == "Rebellion", "Tracked active rebel realm gets rebellion description");
        check(State(true, false, true, true).ToString() == "Feud", "Tracked feud receives private-war wording");
        check(State(true, false, false, true).ToString() == "Temporary", "Unresolved shell never promotes its leader to sovereign");
        check(State(true, false, false, false, true).ToString() == "Loyalist", "Actual active conflict identifies Crown side");
        var normal = Description(State(true), true, true);
        string rendered = normal.ToString();
        check(rendered.Contains("High King of New Kingdom"), "Ruler uses configured rank and actual kingdom name");
        check(!rendered.Contains("civil war"), "Independent same-culture realm has no invented civil war");
        check(rendered.Contains("He has the reputation of being honorable"), "Local nested variables preserve gender and reputation");
        foreach (bool leader in new[] { false, true })
        {
            var rebel = Description(State(true, true), true, leader, rank: "Duke").ToString();
            check(rebel.Contains("is Duke and head") && !rebel.Contains("Duke of New Kingdom"), "Rebel rank is separate from realm leadership");
            check(rebel.Contains("Pryndor's Coalition") && rebel.Contains("civil war within New Kingdom"), "Rebel side and parent realm are explicit");
            check(rebel.Contains(leader ? "He leads" : "house has joined"), "Side leadership differs from supporting clan leadership");
            var feud = Description(State(true, false, true), true, leader).ToString();
            check(feud.Contains("private feud within New Kingdom") && !feud.Contains("civil war"), "Feuds are not labeled civil wars");
        }
        check(Description(State(true), false, false, true).ToString().Contains("She has the reputation"), "Female clan members retain correct pronouns");
        check(!Description(State(true), false, false).ToString().Contains("is High King"), "Members are not promoted to their clan leader's rank");
        check(!Description(State(false), true, false).ToString().Contains("New Kingdom"), "Kingdomless description does not invent allegiance");
        var first = Description(State(true), true, true);
        var second = Description(State(true), true, true, realm: "Other Kingdom");
        check(second.ToString().Contains("Other Kingdom") && first.ToString().Contains("New Kingdom"), "Descriptions keep independent local realm variables");
        var postfix = AccessTools.Method(patch, "Postfix");
        var original = new TextObject("Original biography");
        var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        hero.EncyclopediaText = new TextObject("Authored history");
        object[] args = { hero, original }; postfix.Invoke(null, args);
        check(ReferenceEquals(args[1], original), "Authored biographies are untouched");
        args = new object[] { null, original }; postfix.Invoke(null, args);
        check(ReferenceEquals(args[1], original), "Missing heroes preserve native output");
        hero.EncyclopediaText = null;
        _clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var harmony = new Harmony("bellum.test.hero_biography_guards");
        try
        {
            void Patch(Type t, string property, string method) => harmony.Patch(AccessTools.PropertyGetter(t, property),
                prefix: new HarmonyMethod(typeof(HeroDescriptionTests), method));
            Patch(typeof(Hero), "Clan", nameof(House));
            Patch(typeof(Hero), "IsAlive", nameof(Yes));
            Patch(typeof(Hero), "IsDisabled", nameof(No));
            Patch(typeof(Hero), "IsLord", nameof(Yes));
            Patch(typeof(Clan), "IsMinorFaction", nameof(Yes));
            args = new object[] { hero, original }; postfix.Invoke(null, args);
            check(ReferenceEquals(args[1], original), "Minor and mercenary origin descriptions remain native");
            harmony.Unpatch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), HarmonyPatchType.All, harmony.Id);
            Patch(typeof(Clan), "IsMinorFaction", nameof(No));
            Patch(typeof(Clan), "IsRebelClan", nameof(Yes));
            args = new object[] { hero, original }; postfix.Invoke(null, args);
            check(ReferenceEquals(args[1], original), "Native rebel-clan origin description remains unchanged");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
