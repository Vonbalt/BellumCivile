using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BellumCivile
{
    internal sealed class CrownPartitionAllegiancePlan
    {
        internal IReadOnlyList<string> MovingHouses { get; }
        internal IReadOnlyDictionary<string, string> PrincipalTitles { get; }

        private CrownPartitionAllegiancePlan(IEnumerable<string> movers, Dictionary<string, string> principals)
        {
            MovingHouses = movers.OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly();
            PrincipalTitles = new ReadOnlyDictionary<string, string>(principals);
        }

        // Freeze the decision before any clan movement changes de facto hierarchy.
        // Holdings supplied here must exclude temporary allocation custody.
        internal static bool TryCreate(string crownId, string retainedHouse, string recipientHouse,
            IReadOnlyDictionary<string, string[]> holdings, IEnumerable<FeudalTitleRecord> snapshot,
            out CrownPartitionAllegiancePlan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(crownId) || string.IsNullOrWhiteSpace(retainedHouse)
                || string.IsNullOrWhiteSpace(recipientHouse) || retainedHouse == recipientHouse
                || holdings == null || snapshot == null || !holdings.ContainsKey(retainedHouse)
                || !holdings.ContainsKey(recipientHouse))
                return Fail("missing distinct ruling houses, Crown or membership snapshot", out reason);
            var titles = new Dictionary<string, FeudalTitleRecord>(StringComparer.Ordinal);
            var baronies = new Dictionary<string, FeudalTitleRecord>(StringComparer.Ordinal);
            foreach (var title in snapshot)
            {
                if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || titles.ContainsKey(title.TitleId))
                    return Fail("invalid or duplicate title snapshot", out reason);
                titles.Add(title.TitleId, title);
                if (title.IsActive && title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                {
                    if (baronies.ContainsKey(title.CapitalSettlementId))
                        return Fail("multiple active baronies describe the same settlement", out reason);
                    baronies.Add(title.CapitalSettlementId, title);
                }
            }
            if (!titles.TryGetValue(crownId, out var crown) || !crown.IsActive || crown.TitleType < FeudalTitleType.Kingdom
                || crown.DeJureHolderClanId != crown.DeFactoHolderClanId
                || crown.DeJureHolderClanId != retainedHouse && crown.DeJureHolderClanId != recipientHouse)
                return Fail("partition Crown is not fully held by either succession house", out reason);

            var ancestors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var title in titles.Values.Where(t => t.IsActive))
            {
                var chain = new HashSet<string>(StringComparer.Ordinal) { title.TitleId };
                var current = title;
                while (!string.IsNullOrEmpty(current.ParentTitleId))
                {
                    if (!chain.Add(current.ParentTitleId) || !titles.TryGetValue(current.ParentTitleId, out var parent)
                        || !parent.IsActive || parent.TitleType <= current.TitleType)
                        return Fail("incomplete or invalid legal hierarchy", out reason);
                    current = parent;
                }
                ancestors.Add(title.TitleId, chain);
            }
            var principals = new Dictionary<string, string>(StringComparer.Ordinal);
            var movers = new HashSet<string>(StringComparer.Ordinal) { recipientHouse };
            var seenLand = new HashSet<string>(StringComparer.Ordinal);
            var landed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in holdings)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                    return Fail("missing house identity or holdings snapshot", out reason);
                foreach (string id in entry.Value)
                {
                    if (string.IsNullOrWhiteSpace(id) || !seenLand.Add(id) || !baronies.TryGetValue(id, out var barony)
                        || barony.DeFactoHolderClanId != entry.Key)
                        return Fail("ambiguous or stale physical ownership snapshot", out reason);
                    landed.UnionWith(ancestors[barony.TitleId]);
                }
            }
            // Authority over landed vassals counts even without personal demesne.
            var principalByHouse = landed.Select(id => titles[id])
                .Where(t => !string.IsNullOrWhiteSpace(t.DeFactoHolderClanId))
                .GroupBy(t => t.DeFactoHolderClanId).ToDictionary(g => g.Key,
                    g => g.OrderByDescending(t => t.TitleType)
                        .ThenByDescending(t => t.DeJureHolderClanId == g.Key)
                        .ThenBy(t => t.TitleId, StringComparer.Ordinal).First(), StringComparer.Ordinal);
            foreach (var entry in holdings)
            {
                principalByHouse.TryGetValue(entry.Key, out var principal);
                principals.Add(entry.Key, principal?.TitleId);
                if (entry.Key != retainedHouse && principal != null && ancestors[principal.TitleId].Contains(crownId))
                    movers.Add(entry.Key);
            }
            plan = new CrownPartitionAllegiancePlan(movers, principals);
            return true;
        }

        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
