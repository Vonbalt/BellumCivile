using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BellumCivile
{
    // A common pre-mutation baseline for all separating Crowns. This plans
    // allegiance only; estate delivery and native transfers require saved receipts.
    internal sealed class CrownPartitionBatchPlan
    {
        internal string RetainedHouseId { get; }
        internal string PrimaryCrownId { get; }
        internal IReadOnlyDictionary<string, string> RecipientByCrown { get; }
        internal IReadOnlyDictionary<string, string> DestinationCrownByHouse { get; }
        internal IReadOnlyDictionary<string, string> PrincipalTitleByHouse { get; }
        internal IReadOnlyDictionary<string, IReadOnlyList<string>> HoldingsByHouse { get; }
        private readonly List<RealmUnionTitleRecord> _titles;

        private CrownPartitionBatchPlan(string retainedHouse, string primaryCrown, Dictionary<string, string> recipients,
            Dictionary<string, string> destinations, Dictionary<string, string> principals,
            Dictionary<string, string[]> holdings, IEnumerable<FeudalTitleRecord> titles)
        {
            RetainedHouseId = retainedHouse;
            PrimaryCrownId = primaryCrown;
            RecipientByCrown = new ReadOnlyDictionary<string, string>(recipients);
            DestinationCrownByHouse = new ReadOnlyDictionary<string, string>(destinations);
            PrincipalTitleByHouse = new ReadOnlyDictionary<string, string>(principals);
            HoldingsByHouse = new ReadOnlyDictionary<string, IReadOnlyList<string>>(holdings.ToDictionary(
                p => p.Key, p => (IReadOnlyList<string>)p.Value.OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly(), StringComparer.Ordinal));
            _titles = titles.OrderBy(t => t.TitleId, StringComparer.Ordinal).Select(RealmUnionTitleRecord.Capture).ToList();
        }

        internal List<RealmUnionTitleRecord> CopyTitleSnapshot() => _titles.Select(t => new RealmUnionTitleRecord
        {
            TitleId = t.TitleId, LegalHolderId = t.LegalHolderId, ActualHolderId = t.ActualHolderId,
            LegalParentId = t.LegalParentId, ActualParentId = t.ActualParentId,
            OriginRealmId = t.OriginRealmId, CapitalId = t.CapitalId, Rank = t.Rank
        }).ToList();

        internal static bool TryCreate(string retainedHouse, string primaryCrown,
            IReadOnlyDictionary<string, string> recipientByCrown,
            IReadOnlyDictionary<string, string[]> sourceHoldings,
            IEnumerable<FeudalTitleRecord> titleSnapshot,
            out CrownPartitionBatchPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (recipientByCrown == null || recipientByCrown.Count == 0 || sourceHoldings == null
                || titleSnapshot == null || string.IsNullOrWhiteSpace(retainedHouse)
                || !sourceHoldings.ContainsKey(retainedHouse))
                return Fail("missing Crown assignments or source membership snapshot", out reason);
            var titles = titleSnapshot.ToList();
            if (!FeudalCrownPartitionPlan.TryCreate(retainedHouse, primaryCrown, titles, out var packages, out reason)) return false;
            var available = new HashSet<string>(packages.Where(p => p.CrownId != primaryCrown).Select(p => p.CrownId), StringComparer.Ordinal);
            var recipients = new Dictionary<string, string>(StringComparer.Ordinal);
            var founders = new HashSet<string>(StringComparer.Ordinal);
            var holdings = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var source in sourceHoldings)
            {
                if (string.IsNullOrWhiteSpace(source.Key) || source.Value == null)
                    return Fail("invalid source house or holdings", out reason);
                holdings.Add(source.Key, source.Value.ToArray());
            }
            foreach (var assignment in recipientByCrown.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(assignment.Key) || !available.Contains(assignment.Key)
                    || string.IsNullOrWhiteSpace(assignment.Value) || !founders.Add(assignment.Value)
                    || holdings.ContainsKey(assignment.Value))
                    return Fail("Crown or reserved founder is ineligible, duplicated or already belongs to the source", out reason);
                recipients.Add(assignment.Key, assignment.Value);
                holdings.Add(assignment.Value, new string[0]);
            }
            var destinations = holdings.Keys.OrderBy(id => id, StringComparer.Ordinal)
                .ToDictionary(id => id, id => primaryCrown, StringComparer.Ordinal);
            var principals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var assignment in recipients)
            {
                if (!CrownPartitionAllegiancePlan.TryCreate(assignment.Key, retainedHouse, assignment.Value,
                    holdings, titles, out var allegiance, out reason)) return false;
                foreach (var principal in allegiance.PrincipalTitles)
                {
                    if (principals.TryGetValue(principal.Key, out string previous) && previous != principal.Value)
                        return Fail("Crown packages disagree about a house's principal title", out reason);
                    principals[principal.Key] = principal.Value;
                }
                foreach (string house in allegiance.MovingHouses)
                {
                    if (house == retainedHouse || destinations[house] != primaryCrown)
                        return Fail("a house was assigned to more than one separating Crown", out reason);
                    destinations[house] = assignment.Key;
                }
            }
            plan = new CrownPartitionBatchPlan(retainedHouse, primaryCrown, recipients, destinations, principals, holdings, titles.Where(t => t.IsActive));
            return true;
        }

        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
