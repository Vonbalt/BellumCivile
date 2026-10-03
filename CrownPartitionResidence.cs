using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal static class CrownPartitionResidence
    {
        // A residence is a location, not an inherited asset. Only current realm
        // holdings inside the Crown's legal territory are eligible.
        internal static Town Select(Kingdom parentRealm, string preferredSettlementId,
            IEnumerable<string> crownSettlementIds, IEnumerable<Town> candidates)
        {
            if (parentRealm == null || parentRealm.IsEliminated || crownSettlementIds == null || candidates == null)
                return null;
            var territory = new HashSet<string>(crownSettlementIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
            return candidates.Where(t => t?.Settlement != null && territory.Contains(t.Settlement.StringId)
                    && t.OwnerClan != null && !t.OwnerClan.IsEliminated && t.OwnerClan.Kingdom == parentRealm
                    && !t.OwnerClan.IsUnderMercenaryService)
                .OrderBy(t => t.Settlement.StringId == preferredSettlementId ? 0 : 1)
                .ThenByDescending(t => t.Prosperity)
                .ThenBy(t => t.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
