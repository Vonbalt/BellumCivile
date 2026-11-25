using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal sealed class FeudalTitleCandidateIndex
    {
        private readonly List<FeudalTitleRecord> _active;
        private readonly ILookup<FeudalTitleType, FeudalTitleRecord> _byRank;
        private Dictionary<FeudalTitleRecord, int> _order;
        private ILookup<string, FeudalTitleRecord> _baroniesByRealm;

        internal FeudalTitleCandidateIndex(IEnumerable<FeudalTitleRecord> titles)
        {
            _active = titles.Where(title => title != null && title.IsActive).ToList();
            _byRank = _active.ToLookup(title => title.TitleType);
        }

        internal IEnumerable<FeudalTitleRecord> Rank(FeudalTitleType rank) => _byRank[rank];
        internal IEnumerable<FeudalTitleRecord> Baronies(string realmId)
        {
            if (_baroniesByRealm == null)
                _baroniesByRealm = _byRank[FeudalTitleType.Barony].ToLookup(title => title.AssociatedKingdomId,
                    StringComparer.OrdinalIgnoreCase);
            return _baroniesByRealm[realmId];
        }

        // The final score sort is stable. Preserve title registry order for exact score/rank/name ties.
        internal IEnumerable<FeudalTitleRecord> InRegistryOrder(IEnumerable<FeudalTitleRecord> candidates)
        {
            if (_order == null)
            {
                _order = new Dictionary<FeudalTitleRecord, int>(_active.Count);
                for (int i = 0; i < _active.Count; i++) _order[_active[i]] = i;
            }
            return candidates.OrderBy(title => _order[title]);
        }
    }
}
