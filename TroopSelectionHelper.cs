using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal static class TroopSelectionHelper
    {
        public static List<CharacterObject> GetCultureEliteTroops(CultureObject culture, bool requireCavalry = false, int minTier = 5, int maxTier = 6)
        {
            if (culture == null)
                return new List<CharacterObject>();

            HashSet<CharacterObject> visited = new HashSet<CharacterObject>();
            List<CharacterObject> result = new List<CharacterObject>();

            AddTroopTree(culture.BasicTroop, culture, requireCavalry, minTier, maxTier, visited, result);
            AddTroopTree(culture.EliteBasicTroop, culture, requireCavalry, minTier, maxTier, visited, result);

            return result;
        }

        public static CharacterObject GetRandomCultureEliteTroop(CultureObject culture, bool requireCavalry = false, int minTier = 5, int maxTier = 6)
        {
            List<CharacterObject> troops = GetCultureEliteTroops(culture, requireCavalry, minTier, maxTier);
            return troops.Count > 0
                ? troops[MBRandom.RandomInt(troops.Count)]
                : null;
        }

        private static void AddTroopTree(
            CharacterObject root,
            CultureObject culture,
            bool requireCavalry,
            int minTier,
            int maxTier,
            HashSet<CharacterObject> visited,
            List<CharacterObject> result)
        {
            if (root == null || !visited.Add(root))
                return;

            if (IsValidRosterTroop(root, culture, requireCavalry, minTier, maxTier))
                result.Add(root);

            CharacterObject[] upgrades = root.UpgradeTargets;
            if (upgrades == null)
                return;

            foreach (CharacterObject upgrade in upgrades.Where(u => u != null))
                AddTroopTree(upgrade, culture, requireCavalry, minTier, maxTier, visited, result);
        }

        private static bool IsValidRosterTroop(CharacterObject troop, CultureObject culture, bool requireCavalry, int minTier, int maxTier)
        {
            return troop != null
                && !troop.IsHero
                && troop.Culture == culture
                && troop.Tier >= minTier
                && troop.Tier <= maxTier
                && (!requireCavalry || troop.IsMounted);
        }
    }
}
