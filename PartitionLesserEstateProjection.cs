using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class PartitionLesserEstateProjection
    {
        internal static bool TryApply(PendingPartitionSuccessionRecord pending,
            Dictionary<string, RealmUnionTitleRecord> titles, Dictionary<string, string> realms,
            Dictionary<string, List<string>> holdings, out string reason)
        {
            reason = "invalid lesser estate projection";
            var batch = pending?.CrownBatch;
            if (batch == null || titles == null || realms == null || holdings == null) return false;
            if (pending.EstateShares == null) { reason = null; return true; }
            var assignedTitles = new HashSet<string>(StringComparer.Ordinal);
            var assignedFiefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var share in pending.EstateShares)
            {
                if (share == null || share.Titles == null || share.Fiefs == null
                    || share.Titles.Any(id => !titles.ContainsKey(id) || !assignedTitles.Add(id))
                    || share.Fiefs.Any(id => string.IsNullOrWhiteSpace(id) || !assignedFiefs.Add(id))) return false;
                if (share.Primary || batch.Recipients.ContainsKey(share.RootTitleId ?? "")) continue;
                if (share.PartitionFounderStarted != share.PartitionFounderReturned)
                { reason = "lesser founder initialization is interrupted"; return false; }
                if (!share.PartitionFounderStarted)
                {
                    if (share.Recipient != null || share.PartitionReceipts != null || share.LandedSettled) return false;
                    continue;
                }
                string recipient = share.Recipient?.StringId;
                if (!batch.HierarchyVerified || string.IsNullOrWhiteSpace(recipient)
                    || share.CadetPlan?.Cadet != share.Recipient || share.CadetPlan.CadetId != recipient
                    || !share.CadetPlan.CadetInitialized || realms.ContainsKey(recipient)
                    || share.Heir == null || share.CadetPlan.Heir != share.Heir) return false;
                realms.Add(recipient, batch.SourceRealmId);
                holdings.Add(recipient, new List<string>());
                var ordered = share.Fiefs.Select(id => Tuple.Create(false, id))
                    .Concat(share.Titles.OrderBy(id => titles[id].Rank).ThenBy(id => id, StringComparer.Ordinal)
                        .Select(id => Tuple.Create(true, id))).ToList();
                if (share.PartitionReceipts == null)
                {
                    if (share.LandedSettled) return false;
                    continue;
                }
                if (share.PartitionReceipts.Any(r => r == null)
                    || !ordered.SequenceEqual(share.PartitionReceipts.Select(r => Tuple.Create(r.IsTitle, r.AssetId)))) return false;
                bool waiting = false;
                foreach (var receipt in share.PartitionReceipts)
                {
                    if (!receipt.Started)
                    {
                        if (receipt.ActionReturned || receipt.Verified) return false;
                        waiting = true;
                        continue;
                    }
                    if (waiting || !receipt.ActionReturned || !receipt.Verified) return false;
                    if (receipt.IsTitle)
                    {
                        var title = titles[receipt.AssetId];
                        if (title.Rank >= FeudalTitleType.Kingdom || title.LegalHolderId != batch.RetainedHouseId) return false;
                        title.LegalHolderId = recipient;
                        if (title.ActualHolderId == batch.RetainedHouseId) title.ActualHolderId = recipient;
                    }
                    else
                    {
                        var matches = titles.Values.Where(t => t.Rank == FeudalTitleType.Barony && t.CapitalId == receipt.AssetId).ToList();
                        if (matches.Count != 1 || matches[0].ActualHolderId != batch.RetainedHouseId
                            || !holdings[batch.RetainedHouseId].Remove(receipt.AssetId)) return false;
                        holdings[recipient].Add(receipt.AssetId);
                        matches[0].ActualHolderId = recipient;
                    }
                }
                if (share.LandedSettled && waiting) return false;
            }
            reason = null;
            return true;
        }
    }
}
