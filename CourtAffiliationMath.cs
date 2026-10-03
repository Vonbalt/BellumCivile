using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal static class CourtAffiliationMath
    {
        internal static Dictionary<FactionType, float> Personality(int honor, int generosity, int mercy, int valor, int calculating)
        {
            return new Dictionary<FactionType, float>
            {
                [FactionType.Nobility] = 8 * honor - 4 * generosity + 2 * mercy + 4 * calculating,
                [FactionType.Glory] = 2 * honor + 2 * generosity - 4 * mercy + 8 * valor - 4 * calculating,
                [FactionType.Liberty] = 4 * honor + 8 * generosity + 4 * mercy + 2 * calculating
            };
        }
        internal static int Rank(FeudalTitleType type)
        {
            switch (type)
            {
                case FeudalTitleType.Barony: return 1;
                case FeudalTitleType.County: return 2;
                case FeudalTitleType.Duchy: return 3;
                case FeudalTitleType.Kingdom:
                case FeudalTitleType.Empire: return 4;
                default: return 0;
            }
        }
        internal static float Competence(int skill) => skill <= 60 ? 0f
            : skill < 100 ? (skill - 60) / 40f : Math.Min(4f, 1f + (skill - 100) / 50f);
        internal static Dictionary<FactionType, float> Training(Hero hero)
        {
            float Skill(SkillObject skill) => Competence(hero?.GetSkillValue(skill) ?? 0);
            float weapon = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm,
                DefaultSkills.Bow, DefaultSkills.Crossbow, DefaultSkills.Throwing }.Max(Skill);
            var values = new Dictionary<FactionType, float>
            {
                [FactionType.Nobility] = (Skill(DefaultSkills.Steward) + Skill(DefaultSkills.Leadership)) / 2f,
                [FactionType.Glory] = (Skill(DefaultSkills.Tactics) + weapon) / 2f,
                [FactionType.Liberty] = (Skill(DefaultSkills.Charm) + Skill(DefaultSkills.Trade)) / 2f
            };
            float minimum = values.Values.Min();
            foreach (var type in CourtFactionRoster.Types) values[type] -= minimum;
            return values;
        }
    }
}
