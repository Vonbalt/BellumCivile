using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        // Prepares one recorded Crown package. The whole-estate coordinator still
        // owns completion, announcements, remaining shares and journal retirement.
        internal bool TryPrepareCrownPromotion(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "Crown promotion is not registered or is already complete";
            if (!IsRegisteredCrownPromotion(journal) || journal.Completed) return false;
            if (_pendingPartitions.Any(p => p?.CrownPromotions?.Contains(journal) == true && p.CrownBatch != null))
            {
                reason = "saved Crown batch requires shared expected-state reconciliation before preparation";
                journal.PendingReason = reason;
                return false;
            }
            if (_pendingPartitions.Any(p => p?.CrownPromotions?.Contains(journal) == true && p.CrownPromotions.Count != 1))
            {
                reason = "multiple Crown packages require whole-estate batch coordination before preparation";
                journal.PendingReason = reason;
                return false;
            }
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) { reason = "title service unavailable"; journal.PendingReason = reason; return false; }
            try
            {
                bool prepared = PrepareCrownPromotionSteps(journal, titles, out reason);
                journal.PendingReason = prepared
                    ? "Crown package prepared; whole-estate completion and obligation verification remain pending"
                    : reason;
                return prepared;
            }
            catch (Exception ex)
            {
                reason = "Crown preparation interrupted: " + ex.Message;
                journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
                return false;
            }
        }

        private bool PrepareCrownPromotionSteps(CrownPartitionPromotionRecord journal, FeudalTitleBehavior titles, out string reason)
        {
            reason = null;
            if (!TryPrepareCrownPromotionFounder(journal, titles, out reason)) return false;
            if (!journal.EstateVerified && !TryDeliverCrownPromotionEstate(journal, titles, out reason)) return false;
            if (!titles.TryPrepareCrownPromotionRealm(journal, out reason)) return false;
            if (!TryPrepareCrownPromotionGovernment(journal, out reason)) return false;
            if (!journal.HierarchyStarted && !titles.TryRegisterCrownPromotion(journal, out reason)) return false;
            // Hierarchy finalization first verifies/moves every house, then freezes
            // the new political parent map. Its retry path only verifies that map.
            if (!titles.TryFinalizeCrownPromotionHierarchy(journal, out reason)) return false;
            return TryFinalizeCrownPromotionCourt(journal, out reason);
        }
    }
}
