using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static partial class CrownPartitionBatchReconciliation
    {
        internal static bool TryProjectCrownRegistration(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions, CrownPartitionPromotionRecord journal,
            out Dictionary<string, RealmUnionTitleRecord> expected, out string reason)
        {
            expected = null;
            reason = "registration requires an unstarted batch member with a verified estate and shell";
            if (journal == null || promotions == null || !promotions.Contains(journal)
                || journal.CrownRegistrationStarted || journal.CrownRegistrationReturned
                || !journal.HasVerifiedEstateReceipts() || !journal.RealmCreationStarted
                || !journal.RealmInitializationCompleted || !journal.RealmVerified
                || batch == null || batch.HierarchyStarted) return false;
            if (!TryProjectTitles(batch, promotions, out var projected, out reason)) return false;
            // Predict just this action; never forge a returned receipt to validate it.
            projected[journal.CrownId].ActualParentId = string.Empty;
            projected[journal.CrownId].OriginRealmId = journal.SuccessorId;
            expected = projected;
            reason = null;
            return true;
        }

        internal static bool TryVerifyTitles(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions, IEnumerable<FeudalTitleRecord> currentTitles,
            out string reason)
        {
            if (!TryProjectTitles(batch, promotions, out var expected, out reason)) return false;
            return TryVerifyProjectedTitles(expected, currentTitles, out reason);
        }

        internal static bool TryVerifyProjectedTitles(IReadOnlyDictionary<string, RealmUnionTitleRecord> expected,
            IEnumerable<FeudalTitleRecord> currentTitles, out string reason)
        {
            reason = null;
            if (expected == null) return Fail("expected title snapshot unavailable", out reason);
            if (currentTitles == null) return Fail("current title registry unavailable", out reason);
            var current = new Dictionary<string, FeudalTitleRecord>(StringComparer.Ordinal);
            foreach (var title in currentTitles)
            {
                if (title == null || string.IsNullOrWhiteSpace(title.TitleId)) return Fail("invalid current title identity", out reason);
                if (!title.IsActive) continue;
                if (current.ContainsKey(title.TitleId)) return Fail("duplicate current title identity", out reason);
                current.Add(title.TitleId, title);
            }
            if (current.Count != expected.Count) return Fail("active title manifest changed during batch preparation", out reason);
            foreach (var entry in expected)
            {
                var before = entry.Value;
                if (!current.TryGetValue(entry.Key, out var title) || title.TitleType != before.Rank
                    || title.DeJureHolderClanId != before.LegalHolderId || title.DeFactoHolderClanId != before.ActualHolderId
                    || title.ParentTitleId != before.LegalParentId || title.DeFactoParentTitleId != before.ActualParentId
                    || title.AssociatedKingdomId != before.OriginRealmId || title.CapitalSettlementId != before.CapitalId)
                    return Fail("title state differs from authorized batch receipts: " + entry.Key, out reason);
            }
            reason = null;
            return true;
        }

        // Derive expected state from the original batch and completed receipts only.
        // A shared post-transfer hierarchy phase is deliberately not inferred here.
        internal static bool TryProjectTitles(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions,
            out Dictionary<string, RealmUnionTitleRecord> expected, out string reason)
        {
            expected = null;
            reason = "missing batch or complete Crown manifest";
            if (batch == null || promotions == null) return false;
            if (!batch.TryRestorePlan(out var plan, out reason)) return false;
            if (promotions.Count != batch.Recipients.Count || promotions.Any(p => p == null || string.IsNullOrWhiteSpace(p.CrownId))
                || promotions.Select(p => p.CrownId).Distinct().Count() != promotions.Count
                || !new HashSet<string>(batch.Recipients.Keys).SetEquals(promotions.Select(p => p.CrownId)))
                return Fail("Crown manifest is incomplete or duplicated", out reason);
            var baseline = plan.CopyTitleSnapshot().ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            var projected = plan.CopyTitleSnapshot().ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            var shells = new HashSet<string>(StringComparer.Ordinal);
            bool Under(string title, string crown)
            {
                while (!string.IsNullOrEmpty(title))
                {
                    if (title == crown) return true;
                    title = baseline[title].LegalParentId;
                }
                return false;
            }
            foreach (var p in promotions.OrderBy(p => p.CrownId, StringComparer.Ordinal))
            {
                if (p.FounderId != batch.Recipients[p.CrownId] || string.IsNullOrWhiteSpace(p.SuccessorId)
                    || p.SuccessorId == batch.SourceRealmId || !shells.Add(p.SuccessorId)
                    || p.Parent?.StringId != batch.SourceRealmId || p.RetainedHouse?.StringId != batch.RetainedHouseId)
                    return Fail("promotion identities disagree with the saved batch", out reason);
                if (p.HierarchyStarted || p.HierarchyReturned || p.HierarchyVerified || p.CourtFinalizationStarted
                    || p.CourtFinalizationReturned || p.Completed)
                    return Fail("shared hierarchy/finalization receipts require a later reconciliation phase", out reason);
                var titleIds = baseline.Values.Where(t => t.LegalHolderId == batch.RetainedHouseId && Under(t.TitleId, p.CrownId))
                    .OrderBy(t => t.Rank).ThenBy(t => t.TitleId, StringComparer.Ordinal).Select(t => t.TitleId).ToList();
                var controlled = baseline.Values.Where(t => t.Rank == FeudalTitleType.Barony
                    && t.ActualHolderId == batch.RetainedHouseId && Under(t.TitleId, p.CrownId)).ToList();
                if (controlled.Any(t => string.IsNullOrWhiteSpace(t.CapitalId))
                    || controlled.Select(t => t.CapitalId).Distinct().Count() != controlled.Count)
                    return Fail("controlled estate has no unambiguous physical barony mapping", out reason);
                var fiefs = controlled.ToDictionary(t => t.CapitalId, t => t.TitleId, StringComparer.Ordinal);
                if (p.EstateTitleIds == null || p.EstateFiefIds == null
                    || p.EstateTitleIds.Count != titleIds.Count || !new HashSet<string>(titleIds).SetEquals(p.EstateTitleIds)
                    || p.EstateFiefIds.Count != fiefs.Count || !new HashSet<string>(fiefs.Keys).SetEquals(p.EstateFiefIds)
                    || fiefs.Keys.Any(id => !batch.Holdings[batch.RetainedHouseId].Contains(id)))
                    return Fail("personal estate manifest disagrees with the original Crown package", out reason);
                if (p.EstateReceipts == null)
                {
                    if (p.EstateDeliveryStarted || p.EstateVerified || p.CrownRegistrationStarted || p.CrownRegistrationReturned)
                        return Fail("started estate is missing its asset receipts", out reason);
                    continue;
                }
                var order = p.EstateFiefIds.Select(id => Tuple.Create(false, id))
                    .Concat(titleIds.Select(id => Tuple.Create(true, id))).ToList();
                if (p.EstateReceipts.Any(r => r == null) || !p.EstateReceipts.Select(r => Tuple.Create(r.IsTitle, r.AssetId)).SequenceEqual(order))
                    return Fail("asset receipt order or identity disagrees with the Crown manifest", out reason);
                bool pending = false;
                foreach (var receipt in p.EstateReceipts)
                {
                    if (!receipt.Started)
                    {
                        if (receipt.ActionReturned || receipt.Verified) return Fail("unstarted asset claims completion", out reason);
                        pending = true;
                        continue;
                    }
                    if (pending || !receipt.ActionReturned || !receipt.Verified || !p.EstateDeliveryStarted
                        || !p.FounderCreationStarted || !p.FounderInitializationCompleted || !p.FounderVerified)
                        return Fail("asset delivery is interrupted, unverified or out of order", out reason);
                    string id = receipt.IsTitle ? receipt.AssetId : fiefs[receipt.AssetId];
                    var target = projected[id];
                    if (receipt.IsTitle) target.LegalHolderId = p.FounderId;
                    if (!receipt.IsTitle || baseline[id].ActualHolderId == batch.RetainedHouseId)
                        target.ActualHolderId = p.FounderId;
                }
                if (p.EstateVerified && !p.HasVerifiedEstateReceipts()) return Fail("estate verification contradicts its receipts", out reason);
                if (p.CrownRegistrationStarted != p.CrownRegistrationReturned)
                    return Fail("Crown registration is interrupted or inconsistent", out reason);
                if (p.CrownRegistrationReturned)
                {
                    if (!p.HasVerifiedEstateReceipts() || !p.RealmCreationStarted || !p.RealmInitializationCompleted || !p.RealmVerified)
                        return Fail("Crown registration precedes verified estate or shell creation", out reason);
                    projected[p.CrownId].ActualParentId = string.Empty;
                    projected[p.CrownId].OriginRealmId = p.SuccessorId;
                }
            }
            if (!TryApplySharedHierarchy(batch, promotions, projected, out reason)) return false;
            expected = projected;
            reason = null;
            return true;
        }

        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
