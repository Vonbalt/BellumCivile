using System;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

internal static partial class LordshipTests
{
    private static void CheckRetainerLabels(Action<bool, string> check, LordshipBehavior registry, Clan clan,
        Hero leader, Hero sibling, Hero widow, Hero retainer, Func<Clan, string, Hero> person)
    {
        var textField = AccessTools.Field(typeof(GameTexts), "_gameTextManager");
        object oldTexts = textField.GetValue(null);
        var texts = new GameTextManager();
        foreach (string id in new[] { "str_charactertype_lord", "str_charactertype_lady", "str_charactertype_wanderer",
            "str_wanderer", "str_charactertype_headman", "str_charactertype_minorfaction", "str_charactertype_unknown" })
            texts.AddGameText(id).AddVariation(id);
        GameTexts.Initialize(texts);
        var patch = Type("Patches.HouseholdOccupationDisplayPatch");
        var occupationName = AccessTools.Method(System.Type.GetType(
            "TaleWorlds.CampaignSystem.ViewModelCollection.CampaignUIHelper, TaleWorlds.CampaignSystem.ViewModelCollection", true),
            "GetHeroOccupationName");
        string OccupationName(Hero hero) => (string)occupationName.Invoke(null, new object[] { hero });
        var fixture = new Harmony("bellum.tests.lordships");
        var notableGetter = AccessTools.PropertyGetter(typeof(Hero), "IsNotable");
        fixture.Unpatch(notableGetter, AccessTools.Method(typeof(LordshipTests), nameof(NotSpecial)));
        try
        {
            void OccupationOf(Hero hero, Occupation occupation) => AccessTools.PropertySetter(typeof(Hero), "Occupation")
                .Invoke(hero, new object[] { occupation });
            foreach (Hero hero in new[] { leader, sibling, widow, retainer }) OccupationOf(hero, Occupation.Lord);
            string nativeType = HeroHelper.GetCharacterTypeName(retainer).ToString();
            string nativeOccupation = OccupationName(retainer);
            Call(patch, "Initialize", null); Call(patch, "Initialize", null);
            foreach (bool female in new[] { false, true })
            {
                var hero = person(clan, "display_retainer_" + female); hero.IsFemale = female;
                foreach (Occupation occupation in new[] { Occupation.Lord, Occupation.Wanderer })
                {
                    OccupationOf(hero, occupation);
                    check(HeroHelper.GetCharacterTypeName(hero).ToString() == "Retainer"
                        && OccupationName(hero) == "Retainer",
                        "Tooltip type and marriage/encyclopedia occupation identify an unrelated retainer: " + female + "/" + occupation);
                    check(hero.Occupation == occupation && hero.IsLord == (occupation == Occupation.Lord)
                        && hero.IsWanderer == (occupation == Occupation.Wanderer),
                        "Retainer display preserves native occupation and lord/wanderer flags: " + female + "/" + occupation);
                }
            }
            check(registry.GetGrant(retainer) == null && OccupationName(retainer) == "Retainer",
                "An unlanded retainer uses the same label without requiring a Sir/Dame endowment");
            foreach (Hero hero in new[] { leader, sibling, widow })
                foreach (bool female in new[] { false, true })
                {
                    hero.IsFemale = female;
                    string expected = female ? "str_charactertype_lady" : "str_charactertype_lord";
                    check(HeroHelper.GetCharacterTypeName(hero).ToString() == expected
                        && OccupationName(hero) == expected,
                        "Clan heads, blood family and married-in widows retain native labels: " + hero.StringId + "/" + female);
                }
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, false);
            check(HeroHelper.GetCharacterTypeName(retainer).ToString() == nativeType
                && OccupationName(retainer) == nativeOccupation,
                "Inactive campaign state leaves both native display helpers unchanged");
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            var oldClan = Clans[retainer]; Clans[retainer] = null;
            check(HeroHelper.GetCharacterTypeName(retainer).ToString() == nativeType
                && OccupationName(retainer) == nativeOccupation,
                "Clanless heroes retain native occupation labels");
            Clans[retainer] = oldClan;
            var notable = person(clan, "display_notable"); OccupationOf(notable, Occupation.Headman);
            check(HeroHelper.GetCharacterTypeName(notable).ToString() == "str_charactertype_headman"
                && OccupationName(notable) == "str_charactertype_headman",
                "Native notable occupation text remains intact");
            var targets = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method).Owners
                .Contains("com.bellumcivile.household.occupationdisplay")).ToList();
            check(targets.Count == 2 && targets.All(m => m.Name == "GetCharacterTypeName" || m.Name == "GetHeroOccupationName"),
                "Retainer presentation hooks patch only the two text helpers, not marriage or occupation rules");
        }
        finally
        {
            leader.IsFemale = sibling.IsFemale = widow.IsFemale = false;
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            fixture.Patch(notableGetter, prefix: new HarmonyMethod(typeof(LordshipTests), nameof(NotSpecial)));
            textField.SetValue(null, oldTexts);
        }
    }
}
