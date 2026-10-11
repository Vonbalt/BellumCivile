using System.Linq;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private void PublishCrownPartition(PendingPartitionSuccessionRecord pending)
        {
            if (pending?.CrownBatch?.Completed != true || pending.IntegrationPublished
                || BellumIntegrationBehavior.Current == null) return;
            BellumIntegrationBehavior.Current.RecordPartition(ResolveHero(pending.DeadLeaderId),
                ResolveClan(pending.ParentClanId), ResolveKingdom(pending.KingdomId),
                pending.EstateShares.Select(s => new PartitionRecipientRecord(s.Heir,
                    pending.CrownPromotions?.FirstOrDefault(p => p.CrownId == s.RootTitleId)?.Founder ?? s.Recipient, s.Primary)));
            pending.IntegrationPublished = true;
        }

        private void PublishCrossClanPartition(CrossClanEstateRecord record)
        {
            if (!record.Completed || record.IntegrationPublished || BellumIntegrationBehavior.Current == null) return;
            BellumIntegrationBehavior.Current.RecordPartition(record.Deceased, record.Source, record.Realm,
                record.Shares.Select(s => new PartitionRecipientRecord(s.Heir, s.Recipient, s.Primary)));
            record.IntegrationPublished = true;
        }
    }
}
