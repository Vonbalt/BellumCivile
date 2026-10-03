using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

internal static class CouncilCompetencePresentationTests
{
    private static readonly Dictionary<string, SkillObject> Skills = new Dictionary<string, SkillObject>();
    private static readonly Dictionary<string, CharacterAttribute> Attributes = new Dictionary<string, CharacterAttribute>();
    private static readonly Dictionary<string, int> Values = new Dictionary<string, int>();
    private static int _attribute;
    private static bool Skill(MethodBase __originalMethod, ref SkillObject __result)
    { __result = Skills[__originalMethod.Name.Substring(4)]; return false; }
    private static bool Attribute(MethodBase __originalMethod, ref CharacterAttribute __result)
    { __result = Attributes[__originalMethod.Name.Substring(4)]; return false; }
    private static bool SkillName(PropertyObject __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool SkillValue(SkillObject skill, ref int __result)
    { Values.TryGetValue(skill.StringId, out __result); return false; }
    private static bool AttributeValue(ref int __result) { __result = _attribute; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.council_competence_presentation");
        try
        {
            foreach (string name in new[] { "Tactics", "Leadership", "Charm", "Steward", "Trade", "Roguery", "Scouting" })
            {
                var skill = (SkillObject)FormatterServices.GetUninitializedObject(typeof(SkillObject)); skill.StringId = name; Skills[name] = skill;
                harmony.Patch(AccessTools.PropertyGetter(typeof(DefaultSkills), name), prefix: new HarmonyMethod(typeof(CouncilCompetencePresentationTests), nameof(Skill)));
            }
            foreach (string name in new[] { "Endurance", "Social", "Intelligence", "Cunning" })
            {
                var attribute = (CharacterAttribute)FormatterServices.GetUninitializedObject(typeof(CharacterAttribute)); attribute.StringId = name; Attributes[name] = attribute;
                harmony.Patch(AccessTools.PropertyGetter(typeof(DefaultCharacterAttributes), name), prefix: new HarmonyMethod(typeof(CouncilCompetencePresentationTests), nameof(Attribute)));
            }
            harmony.Patch(AccessTools.PropertyGetter(typeof(PropertyObject), "Name"), prefix: new HarmonyMethod(typeof(CouncilCompetencePresentationTests), nameof(SkillName)));
            harmony.Patch(AccessTools.Method(typeof(Hero), "GetSkillValue"), prefix: new HarmonyMethod(typeof(CouncilCompetencePresentationTests), nameof(SkillValue)));
            harmony.Patch(AccessTools.Method(typeof(Hero), "GetAttributeValue"), prefix: new HarmonyMethod(typeof(CouncilCompetencePresentationTests), nameof(AttributeValue)));
            var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var presentation = typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.CouncilCompetencePresentation");
            foreach (var row in new[] {
                new[] { "Marshal", "Tactics", "Leadership", "Endurance" },
                new[] { "Chancellor", "Charm", "Steward", "Social" },
                new[] { "Seneschal", "Steward", "Trade", "Intelligence" },
                new[] { "Spymaster", "Roguery", "Scouting", "Cunning" },
                new[] { "FirstAdvisor", "Charm", "Leadership", "Social" },
                new[] { "SecondAdvisor", "Charm", "Leadership", "Social" } })
            {
                var office = (PrivyCouncilOffice)Enum.Parse(typeof(PrivyCouncilOffice), row[0]);
                string requirements = AccessTools.Method(presentation, "Requirements").Invoke(null, new object[] { office }).ToString();
                check(requirements.Contains(row[1]) && requirements.Contains(row[2]) && requirements.Contains(row[3])
                    && requirements.Contains("40%") && requirements.Contains("20%"), "Tooltip lists actual weighted factors for " + office);
                foreach (int first in new[] { -20, 0, 150, 300, 500 })
                {
                    Values.Clear(); Values[row[1]] = first; Values[row[2]] = 150; _attribute = 5;
                    float expected = Math.Max(0, Math.Min(300, first)) / 300f * 40 + 20 + 10;
                    check(Math.Abs(PrivyCouncilBehavior.CalculateCompetence(hero, office) - expected) < .001,
                        "Shared factor extraction preserves competence arithmetic and caps for " + office);
                    string candidate = AccessTools.Method(presentation, "Candidate").Invoke(null, new object[] { hero, office }).ToString();
                    check(candidate.Contains("(" + expected.ToString("0") + ")"), "Ballot displays candidate personal score for the correct office");
                }
            }
            foreach (var row in new[] { Tuple.Create(0f, "Inapt"), Tuple.Create(20f, "Mediocre"), Tuple.Create(40f, "Average"), Tuple.Create(60f, "Skillful"), Tuple.Create(80f, "Masterful") })
                check((string)AccessTools.Method(presentation, "Tier").Invoke(null, new object[] { row.Item1 }) == row.Item2, "Shared tier labels match existing thresholds");
            check(AccessTools.Method(presentation, "Candidate").Invoke(null, new object[] { null, PrivyCouncilOffice.Marshal }).ToString().Contains("unavailable"),
                "Missing candidate is not misrepresented as incompetent");
        }
        finally { harmony.UnpatchAll(harmony.Id); Skills.Clear(); Attributes.Clear(); Values.Clear(); }
    }
}
