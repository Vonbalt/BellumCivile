using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal sealed class FeudalFabricationTitleIndex
    {
        private readonly Dictionary<FeudalTitleRecord, int> _order = new Dictionary<FeudalTitleRecord, int>();
        private readonly ILookup<FeudalTitleType, FeudalTitleRecord> _byRank;
        private readonly ILookup<string, FeudalTitleRecord> _baroniesByRealm;

        internal FeudalFabricationTitleIndex(IEnumerable<FeudalTitleRecord> titles)
        {
            var active = titles.Where(title => title != null && title.IsActive).ToList();
            for (int i = 0; i < active.Count; i++) _order[active[i]] = i;
            _byRank = active.ToLookup(title => title.TitleType);
            _baroniesByRealm = _byRank[FeudalTitleType.Barony].ToLookup(title => title.AssociatedKingdomId,
                StringComparer.OrdinalIgnoreCase);
        }

        internal IEnumerable<FeudalTitleRecord> Rank(FeudalTitleType rank) => _byRank[rank];
        internal IEnumerable<FeudalTitleRecord> Baronies(string realmId) => _baroniesByRealm[realmId];

        // The final score sort is stable. Preserve title registry order for exact score/rank/name ties.
        internal IEnumerable<FeudalTitleRecord> InRegistryOrder(IEnumerable<FeudalTitleRecord> candidates) =>
            candidates.OrderBy(title => _order[title]);
    }
}
