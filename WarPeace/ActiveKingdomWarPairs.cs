using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BellumCivile.WarPeace
{
    internal static class ActiveKingdomWarPairs
    {
        internal static IReadOnlyList<(Kingdom First, Kingdom Second)> Capture(Func<Kingdom, bool> eligible, bool exhaustive)
        {
            List<Kingdom> realms = Kingdom.All.Where(eligible).ToList();
            // A custom diplomacy model can generate wars that the native enemy lists do not yet reflect.
            if (exhaustive || !UsesNativeWarStances(Campaign.Current?.Models?.DiplomacyModel))
                return ScanPairs(realms);

            var positions = new Dictionary<Kingdom, int>();
            for (int i = 0; i < realms.Count; i++)
                positions[realms[i]] = i;

            var candidates = new HashSet<(int First, int Second)>();
            for (int i = 0; i < realms.Count; i++)
            {
                Kingdom realm = realms[i];
                var enemies = realm.FactionsAtWarWith;
                if (enemies == null) return ScanPairs(realms);

                foreach (IFaction enemy in enemies)
                {
                    if (!(enemy is Kingdom other) || !positions.TryGetValue(other, out int j)) continue;
                    int order = string.CompareOrdinal(realm.StringId, other.StringId);
                    // Use either side's list, but keep the old lexical orientation of each pair.
                    if (order < 0) candidates.Add((i, j));
                    else if (order > 0) candidates.Add((j, i));
                }
            }

            // Keep Kingdom.All encounter order: recovery consumes random draws and opening morale is order-sensitive.
            var result = new List<(Kingdom First, Kingdom Second)>(candidates.Count);
            foreach (var (first, second) in candidates.OrderBy(pair => pair.First).ThenBy(pair => pair.Second))
                if (realms[first].IsAtWarWith(realms[second])) result.Add((realms[first], realms[second]));
            return result;
        }

        private static bool UsesNativeWarStances(DiplomacyModel model)
        {
            Type type = model?.GetType();
            return type == typeof(DefaultDiplomacyModel)
                || (type == typeof(CivilWarDiplomacyModel) && ((CivilWarDiplomacyModel)model).UsesNativeWarStances);
        }

        private static IReadOnlyList<(Kingdom First, Kingdom Second)> ScanPairs(List<Kingdom> realms)
        {
            var result = new List<(Kingdom First, Kingdom Second)>();
            foreach (Kingdom first in realms)
                foreach (Kingdom second in realms)
                    if (string.CompareOrdinal(first.StringId, second.StringId) < 0 && first.IsAtWarWith(second))
                        result.Add((first, second));
            return result;
        }
    }
}
