using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal sealed class FeudalCrownPackage
    {
        internal string CrownId { get; }
        internal IReadOnlyList<string> TitleIds { get; }
        internal IReadOnlyList<string> PersonalTitleIds { get; }

        internal FeudalCrownPackage(string crown, IEnumerable<FeudalTitleRecord> titles, string house)
        {
            CrownId = crown;
            var ordered = titles.OrderBy(t => t.TitleId, StringComparer.Ordinal).ToList();
            TitleIds = ordered.Select(t => t.TitleId).ToList().AsReadOnly();
            PersonalTitleIds = ordered.Where(t => t.DeJureHolderClanId == house)
                .Select(t => t.TitleId).ToList().AsReadOnly();
        }
    }

    // Pure Crown topology selection. This does not allocate heirs, transfer vassals,
    // authorize independence, or treat a vassal's settlement as the ruler's property.
    internal static class FeudalCrownPartitionPlan
    {
        internal static bool TryCreate(string house, string primaryId,
            IEnumerable<FeudalTitleRecord> titleSnapshot,
            out IReadOnlyList<FeudalCrownPackage> packages, out string reason)
        {
            packages = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(house) || string.IsNullOrWhiteSpace(primaryId) || titleSnapshot == null)
                return Fail("missing Crown partition identity or title snapshot", out reason);
            var titles = new Dictionary<string, FeudalTitleRecord>(StringComparer.Ordinal);
            foreach (var title in titleSnapshot)
            {
                if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || titles.ContainsKey(title.TitleId))
                    return Fail("missing or duplicate title identity", out reason);
                titles.Add(title.TitleId, title);
            }
            if (!titles.TryGetValue(primaryId, out var primary) || !FullyHeld(primary, house)
                || primary.TitleType < FeudalTitleType.Kingdom)
                return Fail("primary Crown is not active and fully held", out reason);

            // Validate topology once and memoize ancestor paths for package/binding checks.
            var ancestors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var title in titles.Values.Where(t => t.IsActive))
            {
                var path = new HashSet<string>(StringComparer.Ordinal);
                var current = title;
                while (!string.IsNullOrEmpty(current.ParentTitleId))
                {
                    if (!path.Add(current.ParentTitleId) || current.ParentTitleId == title.TitleId
                        || !titles.TryGetValue(current.ParentTitleId, out var parent) || !parent.IsActive
                        || parent.TitleType <= current.TitleType)
                        return Fail("incomplete, cyclic or rank-invalid legal title hierarchy", out reason);
                    if (ancestors.TryGetValue(parent.TitleId, out var known))
                    {
                        path.UnionWith(known);
                        break;
                    }
                    current = parent;
                }
                ancestors.Add(title.TitleId, path);
            }
            var roots = titles.Values.Where(t => FullyHeld(t, house) && t.TitleType == primary.TitleType)
                .Where(t => t.TitleId == primaryId || !ancestors[t.TitleId].Intersect(ancestors[primaryId])
                    .Any(id => FullyHeld(titles[id], house) && titles[id].TitleType > primary.TitleType))
                .OrderBy(t => t.TitleId == primaryId ? 0 : 1)
                .ThenBy(t => t.TitleId, StringComparer.Ordinal).ToList();
            var result = new List<FeudalCrownPackage>();
            foreach (var root in roots)
                result.Add(new FeudalCrownPackage(root.TitleId,
                    titles.Values.Where(t => t.IsActive && (t.TitleId == root.TitleId || ancestors[t.TitleId].Contains(root.TitleId))), house));
            packages = result.AsReadOnly();
            return true;
        }

        private static bool FullyHeld(FeudalTitleRecord title, string house) => title.IsActive
            && title.DeJureHolderClanId == house && title.DeFactoHolderClanId == house;

        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
