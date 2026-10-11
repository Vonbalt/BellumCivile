using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryFinishCrownBatch(PendingPartitionSuccessionRecord pending, out string reason)
        {
            try { return FinishCrownBatchSteps(pending, out reason); }
            catch (Exception ex)
            {
                reason = "Crown batch finalization interrupted: " + ex.Message;
                BellumCivileLogger.Log($"Crown batch {pending?.KingdomId}: {ex}");
                return false;
            }
        }

        private bool FinishCrownBatchSteps(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "registered Crown batch required for completion";
            if (pending?.CrownBatch == null || _pendingPartitions?.Contains(pending) != true) return false;
            var batch = pending.CrownBatch;
            if (batch.Completed)
            {
                PublishCrownPartition(pending);
                _pendingPartitions.Remove(pending);
                reason = null;
                return true;
            }
            if (!TryPrepareCrownBatch(pending, out reason) || !TryVerifyCrownBatchObligations(pending, out reason)) return false;
            if (!PartitionEstateSettlement.TryResolveRecipients(pending, out var branches, out reason)) return false;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var summary = Campaign.Current.GetCampaignBehavior<SuccessionYearlySummaryBehavior>();
            var parent = branches[0].Item1;
            var realm = ResolveKingdom(pending.KingdomId);
            foreach (var share in pending.EstateShares.Where(s => !s.Primary))
            {
                var recipient = branches.Single(b => b.Item2 == share.Heir).Item1;
                var promotion = pending.CrownPromotions.SingleOrDefault(p => p.CrownId == share.RootTitleId);
                var package = new FeudalInheritancePackage { RootTitle = titles.GetTitle(share.RootTitleId),
                    PrimaryFief = ResolveTown(share.PrimaryFiefId) };
                if (!batch.TryAnnounce("heir:" + share.Heir.StringId, () =>
                {
                    summary?.RecordPartitionCadetBranch(recipient, parent, realm, package.PrimaryFief);
                    if (promotion != null)
                    {
                        summary?.RecordSovereignPartition(promotion.Successor, package.RootTitle);
                        ShowSovereignPartitionMessage(parent, recipient, share.Heir, package, realm, promotion.Successor, titles);
                    }
                    else ShowPartitionMessage(parent, recipient, share.Heir, package, realm, titles);
                }, out reason)) return false;
            }
            if (!batch.TryAnnounce("treasury", () => summary?.RecordPartitionGoldTransfer(
                pending.GoldPayments.Sum(p => p.DeliveredGold)), out reason)) return false;
            if (!TryVerifyCrownBatchWorld(pending, out reason) || !TryVerifyCrownBatchObligations(pending, out reason)
                || !PartitionBatchCompletion.TryComplete(pending, true, out reason)) return false;
            PublishCrownPartition(pending);
            _pendingPartitions.Remove(pending);
            return true;
        }
    }
}
