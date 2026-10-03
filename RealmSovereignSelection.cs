using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmSovereignSelection
    {
        public static FeudalTitleRecord Select(IEnumerable<FeudalTitleRecord> titles, string rulerClanId,
            bool requireLegalOwnership, string preferredTitleId)
        {
            if (string.IsNullOrWhiteSpace(rulerClanId)) return null;
            return titles.Where(title => title != null && title.IsActive
                    && title.DeFactoHolderClanId == rulerClanId
                    && (!requireLegalOwnership || title.DeJureHolderClanId == rulerClanId))
                .OrderByDescending(title => title.TitleType)
                .ThenByDescending(title => title.TitleId == preferredTitleId)
                .ThenBy(title => title.TitleId, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
