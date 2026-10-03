using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class PartitionBatchCompletion
    {
        internal static bool TryComplete(PendingPartitionSuccessionRecord pending, bool currentStateVerified, out string reason)
        {
            reason = "verified obligations, rewards and announcements required before releasing partition guards";
            var batch = pending?.CrownBatch;
            if (!currentStateVerified || batch == null || batch.Completed || !batch.ObligationsVerified
                || batch.OriginalAgreements == null || !pending.ClaimsStarted || !pending.ClaimsReturned
                || pending.GoldPayments == null || pending.GoldPayments.Count == 0
                || pending.GoldPayments.Any(p => p == null || !p.GoldDebited || !p.GoldCredited)
                || !PartitionEstateSettlement.TryResolveRecipients(pending, out var branches, out reason)) return false;
            reason = "saved payments or announcements disagree with the complete estate";
            if (pending.GoldPayments.Count != branches.Count - 1
                || pending.GoldPayments.Any(p => p.EndowmentDonor != branches[0].Item2)
                || !new HashSet<TaleWorlds.CampaignSystem.Hero>(branches.Skip(1).Select(b => b.Item2))
                    .SetEquals(pending.GoldPayments.Select(p => p.GoldRecipient))) return false;
            if (!PartitionEstateTreasury.TryDeliver(pending.GoldPayments, _ => false, out reason)) return false;
            reason = "saved announcements disagree with the complete estate";
            var required = new HashSet<string>(branches.Skip(1).Select(b => "heir:" + b.Item2.StringId)) { "treasury" };
            if (batch.AnnouncementsStarted == null || batch.AnnouncementsReturned == null
                || batch.AnnouncementsStarted.Count != required.Count || batch.AnnouncementsReturned.Count != required.Count
                || !required.SetEquals(batch.AnnouncementsStarted) || !required.SetEquals(batch.AnnouncementsReturned)) return false;
            // No native callbacks between the final verification and this all-or-nothing
            // publication. Batch-owned hierarchy/court receipts stay on the batch.
            foreach (var promotion in pending.CrownPromotions)
            {
                promotion.ObligationsSettled = true;
                promotion.Announced = true;
                promotion.Completed = true;
            }
            foreach (var share in pending.EstateShares) share.Completed = true;
            batch.Completed = true;
            reason = null;
            return true;
        }
    }
}
