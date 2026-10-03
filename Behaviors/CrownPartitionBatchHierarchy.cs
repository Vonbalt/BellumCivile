using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        internal bool TryFinalizeCrownBatchHierarchy(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "shared hierarchy requires a registered Crown batch";
            var partitions = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (pending?.CrownBatch == null || partitions == null) return false;
            var batch = pending.CrownBatch;
            if (batch.HierarchyReturned)
            {
                batch.HierarchyVerified = partitions.TryVerifyCrownBatchWorld(pending, out reason);
                return batch.HierarchyVerified;
            }
            if (batch.HierarchyStarted || batch.HierarchyVerified || batch.PoliticalParentTargets != null)
            { reason = "shared hierarchy application was interrupted; it will not be replayed"; return false; }
            if (!partitions.TryVerifyCrownBatchWorld(pending, out reason)
                || !CrownPartitionBatchReconciliation.TryProjectTitles(batch, pending.CrownPromotions, out var expected, out reason)) return false;
            var realmIds = new HashSet<string>(pending.CrownPromotions.Select(p => p.SuccessorId), StringComparer.Ordinal) { batch.SourceRealmId };
            var targets = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in expected)
            {
                var title = GetTitle(entry.Key);
                Kingdom realm = ResolveClan(title.DeFactoHolderClanId)?.Kingdom;
                string parent = title.DeFactoParentTitleId;
                if (realm != null && realmIds.Contains(realm.StringId))
                    parent = ResolveBestDeFactoParent(title, realm)?.TitleId ?? string.Empty;
                if (entry.Key == batch.PrimaryCrownId || batch.Recipients.ContainsKey(entry.Key)) parent = string.Empty;
                targets.Add(entry.Key, parent);
            }
            if (!CrownPartitionBatchReconciliation.ValidateSharedHierarchyTargets(batch, pending.CrownPromotions, expected, targets, out reason)) return false;
            try
            {
                batch.PoliticalParentTargets = targets;
                batch.HierarchyStarted = true;
                foreach (var target in targets)
                {
                    expected[target.Key].ActualParentId = target.Value;
                    var title = GetTitle(target.Key);
                    if (title.DeFactoParentTitleId == target.Value) continue;
                    title.SetDeFactoParentTitle(target.Value);
                    title.MarkSynced(CurrentDay);
                    QueueServiceReview(title, "shared Crown partition hierarchy");
                }
                RebuildRuntimeIndexes();
                if (!CrownPartitionBatchReconciliation.TryVerifyProjectedTitles(expected, GetAllTitles(), out reason))
                    throw new InvalidOperationException(reason);
                batch.HierarchyReturned = true;
                batch.HierarchyVerified = true;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                batch.HierarchyVerified = false;
                reason = "shared hierarchy interrupted: " + ex.Message;
                BellumCivileLogger.Log($"Crown batch {batch.SourceRealmId}: {ex}");
                return false;
            }
        }
    }
}
