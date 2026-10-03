using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static partial class CrownPartitionBatchReconciliation
    {
        private static bool TryApplySharedHierarchy(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions,
            Dictionary<string, RealmUnionTitleRecord> projected, out string reason)
        {
            reason = null;
            if (!batch.HierarchyStarted && !batch.HierarchyReturned && !batch.HierarchyVerified && batch.PoliticalParentTargets == null)
                return true;
            if (!batch.HierarchyStarted || !batch.HierarchyReturned)
                return Fail("shared hierarchy application was interrupted or has inconsistent receipts", out reason);
            if (!ValidateSharedHierarchyTargets(batch, promotions, projected, batch.PoliticalParentTargets, out reason)) return false;
            foreach (var target in batch.PoliticalParentTargets) projected[target.Key].ActualParentId = target.Value;
            return true;
        }

        internal static bool ValidateSharedHierarchyTargets(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions,
            IReadOnlyDictionary<string, RealmUnionTitleRecord> projected, IReadOnlyDictionary<string, string> targets,
            out string reason)
        {
            reason = "shared hierarchy requires all house transfers and a complete political parent map";
            if (batch == null || promotions == null || projected == null || targets == null
                || targets.Count != projected.Count || !new HashSet<string>(projected.Keys).SetEquals(targets.Keys)) return false;
            var expectedMovers = new HashSet<string>(batch.Destinations.Where(p => p.Value != batch.PrimaryCrownId).Select(p => p.Key));
            var moved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in promotions)
            {
                if (!p.HasVerifiedEstateReceipts() || !p.CrownRegistrationReturned || !p.TransferDiplomacyPrepared
                    || p.Houses == null || p.Houses.Count == 0) return false;
                foreach (var house in p.Houses)
                {
                    var receipt = house?.Transfer;
                    string id = receipt?.Clan?.StringId;
                    if (string.IsNullOrWhiteSpace(id) || !moved.Add(id) || !batch.Destinations.TryGetValue(id, out string destination)
                        || destination != p.CrownId || receipt.EndMercenaryContract || !receipt.ActionStarted || !receipt.ActionCompleted
                        || !receipt.RestorationStarted || !receipt.RestorationCompleted || !house.MovementReturned
                        || !house.RestorationReturned || !house.PostMoveBalancesCaptured) return false;
                }
            }
            if (!moved.SetEquals(expectedMovers)) return false;
            var roots = new HashSet<string>(batch.Recipients.Keys, StringComparer.Ordinal) { batch.PrimaryCrownId };
            foreach (var pair in targets)
            {
                var title = projected[pair.Key];
                string parent = pair.Value;
                if (parent == null || roots.Contains(pair.Key) && parent.Length != 0)
                    return Fail("sovereign Crown cannot retain a political superior", out reason);
                if (parent.Length != 0 && (!projected.TryGetValue(parent, out var superior) || superior.Rank <= title.Rank))
                    return Fail("political parent is absent or not higher ranked", out reason);
                if (!batch.Destinations.TryGetValue(title.ActualHolderId ?? "", out string destination))
                {
                    if (parent != title.ActualParentId)
                        return Fail("shared hierarchy cannot rewrite a foreign house's political ancestry", out reason);
                    continue;
                }
                if (parent.Length != 0 && (!batch.Destinations.TryGetValue(projected[parent].ActualHolderId ?? "", out string parentDestination)
                    || destination != parentDestination))
                    return Fail("political parent belongs to a different successor realm", out reason);
                if (!roots.Contains(pair.Key) && title.Rank < projected[destination].Rank && parent.Length == 0)
                    return Fail("landed subordinate title cannot become an unassigned political root", out reason);
            }
            reason = null;
            return true;
        }
    }
}
