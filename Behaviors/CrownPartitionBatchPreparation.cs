using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        // Opt-in preparation only. Estate completion and journal retirement remain separate.
        internal bool TryPrepareCrownBatch(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "registered Crown batch and title service required";
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || pending?.EstateShares == null || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
            var batch = pending.CrownBatch;
            if (batch.Completed) { reason = "batch is already complete"; return false; }
            if (!TryCaptureCrownBatchObligations(pending, out reason)) return false;
            if (batch.CourtFinalizationStarted != batch.CourtFinalizationReturned
                || batch.CourtFinalizationReturned && !batch.HierarchyVerified)
            { reason = "shared court receipts are interrupted or inconsistent; preparation will not be replayed"; return false; }
            var promotions = pending.CrownPromotions.OrderBy(p => p.CrownId, StringComparer.Ordinal).ToList();
            try
            {
                if (!batch.HierarchyStarted)
                {
                    foreach (var journal in promotions)
                        if (!TryPrepareCrownPromotionFounder(journal, titles, out reason)
                            || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
                    foreach (var journal in promotions)
                        if ((!journal.HasVerifiedEstateReceipts() && !TryDeliverCrownPromotionEstate(journal, titles, out reason))
                            || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
                    foreach (var journal in promotions)
                        if (!titles.TryPrepareCrownPromotionRealm(journal, out reason)
                            || !TryPrepareCrownPromotionGovernment(journal, out reason)
                            || !titles.TryRegisterCrownPromotion(journal, out reason)
                            || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
                    foreach (var journal in promotions)
                        if (!TryMoveCrownPromotionHouses(journal, out reason)) return false;
                }
                if (!titles.TryFinalizeCrownBatchHierarchy(pending, out reason)) return false;
                foreach (var share in pending.EstateShares ?? Enumerable.Empty<CrossClanEstateShare>())
                    if (!share.Primary && !batch.Recipients.ContainsKey(share.RootTitleId ?? "") && !share.LandedSettled
                        && !TryDeliverLesserPartitionShare(pending, share, out reason)) return false;
                if (!TryFinalizeCrownBatchCourt(pending, out reason)) return false;
                if (!TrySettleCrownBatchRewards(pending, out reason)) return false;
                foreach (var journal in promotions)
                    journal.PendingReason = "Crown batch and estate rewards settled; obligation verification and final completion remain pending";
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Crown batch preparation interrupted: " + ex.Message;
                foreach (var journal in promotions) journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown batch {batch.SourceRealmId}: {ex}");
                return false;
            }
        }
    }
}
