using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal enum PersonalBloodBond { None, ParentChild, Siblings, Extended, Spouse }

    internal static class PersonalKinshipHelper
    {
        internal static HashSet<Hero> GetImmediateFamily(Hero hero)
        {
            var family = new HashSet<Hero>();
            if (hero == null) return family;
            family.Add(hero.Father);
            family.Add(hero.Mother);
            family.Add(hero.Spouse);
            if (hero.Children != null) foreach (Hero child in hero.Children) family.Add(child);
            if (hero.Father?.Children != null) foreach (Hero sibling in hero.Father.Children) family.Add(sibling);
            if (hero.Mother?.Children != null) foreach (Hero sibling in hero.Mother.Children) family.Add(sibling);
            family.Remove(null);
            family.Remove(hero);
            return family;
        }

        internal static PersonalBloodBond GetBond(Hero first, Hero second)
        {
            var blood = GetBloodBond(first, second);
            if (blood == PersonalBloodBond.ParentChild || blood == PersonalBloodBond.Siblings) return blood;
            return IsSpouse(first, second) ? PersonalBloodBond.Spouse : blood;
        }

        internal static bool IsSpouse(Hero first, Hero second) => first != null && second != null
            && first != second && !first.IsNotable && !second.IsNotable
            && (first.Spouse == second || second.Spouse == first);

        internal static PersonalBloodBond GetBloodBond(Hero first, Hero second)
        {
            if (first == null || second == null || first == second || first.IsNotable || second.IsNotable)
                return PersonalBloodBond.None;
            if (IsParent(first, second) || IsParent(second, first)) return PersonalBloodBond.ParentChild;
            if (AreSiblings(first, second)) return PersonalBloodBond.Siblings;
            // Bounded to grandparents, aunts/uncles and first cousins; no family-tree scans.
            if (IsParent(first, second.Father) || IsParent(first, second.Mother)
                || IsParent(second, first.Father) || IsParent(second, first.Mother)
                || AreSiblings(first, second.Father) || AreSiblings(first, second.Mother)
                || AreSiblings(second, first.Father) || AreSiblings(second, first.Mother)
                || AreSiblings(first.Father, second.Father) || AreSiblings(first.Father, second.Mother)
                || AreSiblings(first.Mother, second.Father) || AreSiblings(first.Mother, second.Mother))
                return PersonalBloodBond.Extended;
            return PersonalBloodBond.None;
        }

        internal static int GetBonus(Hero first, Hero second)
            => GetBondBonus(GetBond(first, second));

        internal static int GetBloodBonus(Hero first, Hero second)
            => GetBondBonus(GetBloodBond(first, second));

        private static int GetBondBonus(PersonalBloodBond bond)
        {
            switch (bond)
            {
                case PersonalBloodBond.ParentChild: return BellumCivileConstants.DynamicRelationParentChildBonus;
                case PersonalBloodBond.Siblings: return BellumCivileConstants.DynamicRelationSiblingBonus;
                case PersonalBloodBond.Extended: return BellumCivileConstants.DynamicRelationExtendedFamilyBonus;
                case PersonalBloodBond.Spouse: return BellumCivileConstants.DynamicRelationSpouseBonus;
                default: return 0;
            }
        }

        private static bool IsParent(Hero parent, Hero child) => parent != null && child != null
            && (child.Father == parent || child.Mother == parent);

        private static bool AreSiblings(Hero first, Hero second) => first != null && second != null && first != second
            && ((first.Father != null && first.Father == second.Father)
                || (first.Mother != null && first.Mother == second.Mother));
    }
}
