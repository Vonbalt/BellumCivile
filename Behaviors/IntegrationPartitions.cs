using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class BellumIntegrationBehavior
    {
        private const int RecentPartitionLimit = 100;
        private List<PartitionCompletionRecord> _partitions = new List<PartitionCompletionRecord>();
        private bool _partitionDispatchPending;
        internal event Action<PartitionCompletionSnapshot> PartitionCompleted;

        private void SyncPartitions(IDataStore store)
        {
            store.SyncData("BC_Integration_PartitionCompletions", ref _partitions);
            _partitions = (_partitions ?? new List<PartitionCompletionRecord>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.Id) && p.Recipients != null).ToList();
            _partitionDispatchPending = _partitions.Any(p => !p.Dispatched);
        }

        internal void RecordPartition(Hero deceased, Clan source, Kingdom realm, IEnumerable<PartitionRecipientRecord> recipients)
        {
            if (deceased == null || source == null) return;
            string id = "partition|" + deceased.StringId + "|" + source.StringId;
            if (_partitions.Any(p => p.Id == id)) return;
            var branches = recipients.Where(r => r?.House != null && r.Heir != null).ToList();
            if (branches.Select(r => r.House).Distinct().Count() < 2) return;
            _partitions.Add(new PartitionCompletionRecord { Id = id, Deceased = deceased, OriginHouse = source,
                OriginRealm = realm, CompletedDay = CampaignTime.Now.ToDays, Recipients = branches });
            _partitionDispatchPending = true;
        }

        internal IReadOnlyList<PartitionCompletionSnapshot> GetRecentPartitions() =>
            _partitions.Select(p => new PartitionCompletionSnapshot(p)).ToList().AsReadOnly();

        private void DispatchPartitions(float dt)
        {
            if (!_partitionDispatchPending) return;
            _partitionDispatchPending = false;
            // Delivery is outside inheritance execution. A subscriber cannot roll back the estate.
            foreach (var record in _partitions.Where(p => !p.Dispatched).ToList())
            {
                record.Dispatched = true;
                var snapshot = new PartitionCompletionSnapshot(record);
                foreach (Action<PartitionCompletionSnapshot> subscriber in
                    PartitionCompleted?.GetInvocationList() ?? Array.Empty<Delegate>())
                {
                    try { subscriber(snapshot); }
                    catch (Exception ex) { BellumCivileLogger.Log("Integration partition subscriber failed: " + ex); }
                }
            }
            while (_partitions.Count > RecentPartitionLimit && _partitions[0].Dispatched) _partitions.RemoveAt(0);
        }
    }
}
