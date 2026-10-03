using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class CrossClanEstateRecord
    {
        [SaveableField(1)] public Clan Source;
        [SaveableField(2)] public Hero Deceased;
        [SaveableField(3)] public Kingdom Realm;
        [SaveableField(4)] public CampaignTime ReadyDate;
        [SaveableField(5)] public List<CrossClanEstateShare> Shares = new List<CrossClanEstateShare>();
        [SaveableField(6)] public bool Completed;
        [SaveableField(7)] public string Failure;
        [SaveableField(8)] public Hero GoldDonor;
        [SaveableField(9)] public int GoldPerShare;
        [SaveableField(10)] public bool GoldPrepared;
        [SaveableField(11)] public string PreferredPrimaryTitleId;
        [SaveableField(12)] public string RealmCrownTitleId;
    }

    public class CrossClanEstateShare
    {
        [SaveableField(1)] public Hero Heir;
        [SaveableField(2)] public bool Primary;
        [SaveableField(3)] public List<string> Fiefs = new List<string>();
        [SaveableField(4)] public List<string> Titles = new List<string>();
        [SaveableField(5)] public List<string> DeliveredFiefs = new List<string>();
        [SaveableField(6)] public List<string> DeliveredTitles = new List<string>();
        [SaveableField(7)] public Clan Recipient;
        [SaveableField(8)] public CrownAccessionRecord CadetPlan;
        [SaveableField(9)] public bool Completed;
        [SaveableField(10)] public CrownAccessionRecord GoldPayment;
        [SaveableField(11)] public bool LandedSettled;
        [SaveableField(12)] public string RootTitleId;
        [SaveableField(13)] public string PrimaryFiefId;
        [SaveableField(14)] public string Status;
        [SaveableField(15)] public List<string> SupersededFiefs = new List<string>();
        [SaveableField(16)] public List<string> SupersededTitles = new List<string>();
        [SaveableField(17)] public bool BeneficiaryReconciled;
        [SaveableField(18)] public List<string> SovereignTransfers = new List<string>();
        [SaveableField(19)] public List<string> DeliveredSovereignTransfers = new List<string>();
        [SaveableField(20)] public List<CrownEstateDeliveryRecord> PartitionReceipts;
        [SaveableField(21)] public bool PartitionFounderStarted;
        [SaveableField(22)] public bool PartitionFounderReturned;
    }
}
