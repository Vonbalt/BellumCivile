using System.Linq;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        // The whole-estate coordinator must publish the complete beneficiary manifest first.
        internal bool TryDeliverPartitionTreasury(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "estate treasury requires a registered, finalized batch and a saved payment manifest";
            if (pending?.GoldPayments == null || _pendingPartitions?.Contains(pending) != true
                || pending.CrownBatch?.CourtFinalizationReturned != true
                || !pending.CrownBatch.CourtFinalizationStarted
                || !pending.CrownBatch.HierarchyVerified) return false;
            if (!TryVerifyCrownBatchWorld(pending, out reason)) return false;
            if (pending.GoldPayments.Any(p => p == null || !p.GoldCredited
                && (p.GoldRecipient?.IsAlive != true || p.GoldRecipient.Clan?.Leader != p.GoldRecipient
                    || !p.GoldDebited && p.EndowmentDonor?.IsAlive != true)))
            { reason = "unpaid estate treasury participant changed; beneficiary reconciliation is required"; return false; }
            return PartitionEstateTreasury.TryDeliver(pending.GoldPayments, CrownAccessionBehavior.DeliverAbdicationGold, out reason);
        }
    }
}
