using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PartitionCompletionSnapshot
    {
        public string Id { get; }
        public Hero Deceased { get; }
        public Clan OriginHouse { get; }
        public Kingdom OriginRealm { get; }
        public double CompletedDay { get; }
        public IReadOnlyList<PartitionRecipientSnapshot> Recipients { get; }

        internal PartitionCompletionSnapshot(PartitionCompletionRecord record)
        {
            Id = record.Id; Deceased = record.Deceased; OriginHouse = record.OriginHouse;
            OriginRealm = record.OriginRealm; CompletedDay = record.CompletedDay;
            Recipients = record.Recipients.Select(r => new PartitionRecipientSnapshot(r)).ToList().AsReadOnly();
        }
    }

    public sealed class PartitionRecipientSnapshot
    {
        public Hero Heir { get; }
        public Clan House { get; }
        public Kingdom Realm { get; }
        public bool Primary { get; }
        internal PartitionRecipientSnapshot(PartitionRecipientRecord record)
        { Heir = record.Heir; House = record.House; Realm = record.Realm; Primary = record.Primary; }
    }

    internal sealed class PartitionCompletionRecord
    {
        [SaveableField(1)] internal string Id;
        [SaveableField(2)] internal Hero Deceased;
        [SaveableField(3)] internal Clan OriginHouse;
        [SaveableField(4)] internal Kingdom OriginRealm;
        [SaveableField(5)] internal double CompletedDay;
        [SaveableField(6)] internal List<PartitionRecipientRecord> Recipients;
        [SaveableField(7)] internal bool Dispatched;
    }

    internal sealed class PartitionRecipientRecord
    {
        [SaveableField(1)] internal Hero Heir;
        [SaveableField(2)] internal Clan House;
        [SaveableField(3)] internal Kingdom Realm;
        [SaveableField(4)] internal bool Primary;

        internal PartitionRecipientRecord(Hero heir, Clan house, bool primary)
        { Heir = heir; House = house; Realm = house?.Kingdom; Primary = primary; }
    }
}
