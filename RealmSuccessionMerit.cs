using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile
{
    // Realm-only profiles. Household ordering and estate packages deliberately do not use these scores.
    internal static class RealmSuccessionMerit
    {
        internal const double GenderPreferenceBonus = 5;
        internal static double Normalize(double value) => Math.Max(0, Math.Min(1, value));
        internal static double Skill(double value) => Normalize(value / 300);
        internal static double Experience(double age, double majority) =>
            age <= majority ? 0 : Normalize((age - majority) / Math.Max(1, 65 - majority));

        internal static double Score(HouseSuccessionLaw law, double kinship, double leadership,
            double charm, double steward, double tactics, double trade, double melee,
            double ranged, double athletics, double riding, double experience,
            bool leadsParty, bool leadsArmy, bool governor)
        {
            double kin = Normalize(kinship), l = Skill(leadership), c = Skill(charm),
                s = Skill(steward), t = Skill(tactics), exp = Normalize(experience);
            double martial = .6 * Skill(melee) + .3 * Skill(ranged) + .1 * Skill(athletics);
            switch (law)
            {
                case HouseSuccessionLaw.Kinship: // Kinship
                    return 40 * kin + 25 * l + 20 * c + 10 * s + 5 * t;
                case HouseSuccessionLaw.Tanistry:
                    return 45 * kin + 20 * l + 15 * t + 10 * martial + 10 * c;
                case HouseSuccessionLaw.MilitaryAcclamation:
                    return 35 * l + 30 * t + 20 * martial + 5 * Skill(riding)
                        + (leadsParty ? 3 + (leadsArmy ? 7 : 0) : 0);
                case HouseSuccessionLaw.ElectiveSeniority:
                    return 60 * exp + 15 * l + 10 * s + 10 * c + 5 * kin;
                case HouseSuccessionLaw.ShuraCouncil:
                    return 30 * s + 25 * c + 20 * l + 10 * Skill(trade) + 10 * exp + (governor ? 5 : 0);
                default:
                    throw new ArgumentOutOfRangeException(nameof(law), "This realm law uses precedence, not merit.");
            }
        }

        internal static double Rank(Hero candidate, SuccessionLawSet laws, double kinship)
        {
            var party = candidate.PartyBelongedTo;
            bool leadsParty = party != null && party.LeaderHero == candidate;
            double score = Score(laws.SuccessionLaw, kinship,
                candidate.GetSkillValue(DefaultSkills.Leadership), candidate.GetSkillValue(DefaultSkills.Charm),
                candidate.GetSkillValue(DefaultSkills.Steward), candidate.GetSkillValue(DefaultSkills.Tactics),
                candidate.GetSkillValue(DefaultSkills.Trade),
                Math.Max(candidate.GetSkillValue(DefaultSkills.OneHanded), Math.Max(candidate.GetSkillValue(DefaultSkills.TwoHanded), candidate.GetSkillValue(DefaultSkills.Polearm))),
                Math.Max(candidate.GetSkillValue(DefaultSkills.Bow), Math.Max(candidate.GetSkillValue(DefaultSkills.Crossbow), candidate.GetSkillValue(DefaultSkills.Throwing))),
                candidate.GetSkillValue(DefaultSkills.Athletics), candidate.GetSkillValue(DefaultSkills.Riding),
                Experience(candidate.Age, SuccessionLawHelper.GetAgeOfMajority()), leadsParty,
                leadsParty && party.Army != null && party.Army.LeaderParty == party, candidate.GovernorOf != null);
            return score + GenderPreferenceBonus * SuccessionLawHelper.GetGenderPreferenceRank(candidate, laws.GenderLaw);
        }
    }

    // Refresh-scoped cache: at most four parent links per person, no permanent campaign cache.
    internal sealed class RealmSuccessionKinship
    {
        private readonly Dictionary<Hero, Dictionary<Hero, int>> _depths = new Dictionary<Hero, Dictionary<Hero, int>>();

        internal double Get(Hero candidate, Hero sovereign)
        {
            if (candidate == null || sovereign == null) return 0;
            if (candidate == sovereign) return 1;
            var candidateAncestors = Ancestors(candidate);
            var sovereignAncestors = Ancestors(sovereign);
            double best = 0;
            foreach (var ancestor in candidateAncestors)
            {
                if (!sovereignAncestors.TryGetValue(ancestor.Key, out int otherDepth)) continue;
                int depth = ancestor.Value;
                double value;
                if (depth == 1 && otherDepth == 0) value = 1;
                else if (depth == 0 && otherDepth == 1 || depth == 2 && otherDepth == 0) value = .85;
                else if (depth == 1 && otherDepth == 1) value = .80;
                else if (depth == 1 && otherDepth == 2 || depth == 2 && otherDepth == 1) value = .60;
                else if (depth == 2 && otherDepth == 2) value = .45;
                else value = Math.Max(.20, .45 - .0625 * Math.Max(1, depth + otherDepth - 4));
                best = Math.Max(best, value);
            }
            return best;
        }

        private Dictionary<Hero, int> Ancestors(Hero hero)
        {
            if (_depths.TryGetValue(hero, out var result)) return result;
            result = new Dictionary<Hero, int> { [hero] = 0 };
            var pending = new Queue<Hero>();
            pending.Enqueue(hero);
            while (pending.Count > 0)
            {
                Hero current = pending.Dequeue();
                int depth = result[current];
                if (depth == 4) continue;
                foreach (Hero parent in new[] { current.Father, current.Mother })
                {
                    if (parent == null || result.ContainsKey(parent)) continue;
                    result.Add(parent, depth + 1);
                    pending.Enqueue(parent);
                }
            }
            _depths.Add(hero, result);
            return result;
        }
    }
}
