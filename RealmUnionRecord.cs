using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class RealmUnionRecord
    {
        [SaveableField(1)] public Kingdom Source;
        [SaveableField(2)] public Kingdom Destination;
        [SaveableField(3)] public Hero Heir;
        [SaveableField(4)] public Clan PreviousHouse;
        [SaveableField(5)] public Clan SurvivingHouse;
        [SaveableField(6)] public string InheritedCrownId;
        [SaveableField(7)] public string PrimaryCrownId;
        [SaveableField(8)] public CampaignTime CapturedAt;
        [SaveableField(9)] public List<RealmUnionClanRecord> Clans = new List<RealmUnionClanRecord>();
        [SaveableField(10)] public List<RealmUnionTitleRecord> Titles = new List<RealmUnionTitleRecord>();
        [SaveableField(11)] public bool CrownTransferStarted;
        [SaveableField(12)] public bool CrownVerified;
        [SaveableField(13)] public bool ObligationsSettled;
        [SaveableField(14)] public bool RetirementStarted;
        [SaveableField(15)] public bool SourceRetired;
        [SaveableField(16)] public bool Announced;
        [SaveableField(17)] public bool Completed;
        [SaveableField(18)] public string PendingReason;
        [SaveableField(19)] public bool CrownTransferReturned;
        [SaveableField(20)] public Dictionary<string, string> SourceObligations;
        [SaveableField(21)] public Dictionary<string, string> DestinationObligations;
        [SaveableField(22)] public bool TradeTransferStarted;
        [SaveableField(23)] public bool TradeTransferReturned;
        [SaveableField(24)] public bool AllianceTransferStarted;
        [SaveableField(25)] public bool AllianceTransferReturned;
        [SaveableField(26)] public Dictionary<string, string> TributeDestinationPeaceDates;
        [SaveableField(27)] public List<string> TributeTransfersStarted = new List<string>();
        [SaveableField(28)] public List<string> TributeTransfersReturned = new List<string>();
        [SaveableField(29)] public List<ActiveTreatyTributeRecord> SourceLegacyTributes;
        [SaveableField(30)] public List<ActiveTreatyTributeRecord> DestinationLegacyTributes;
        [SaveableField(31)] public bool LegacyTributeTransferStarted;
        [SaveableField(32)] public bool LegacyTributeTransferReturned;
        [SaveableField(33)] public List<ClientKingdomRecord> SourceClientRecords;
        [SaveableField(34)] public List<ClientKingdomRecord> DestinationClientRecords;
        [SaveableField(35)] public bool ClientTransferStarted;
        [SaveableField(36)] public bool ClientTransferReturned;
        [SaveableField(37)] public bool RetirementReturned;
    }

    public sealed class RealmUnionClanRecord
    {
        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public bool EndMercenaryContract;
        [SaveableField(3)] public float Influence;
        [SaveableField(4)] public int Debt;
        [SaveableField(5)] public Banner Banner;
        [SaveableField(6)] public Banner OriginalBanner;
        [SaveableField(7)] public uint Color;
        [SaveableField(8)] public uint Color2;
        [SaveableField(9)] public List<string> Holdings = new List<string>();
        [SaveableField(10)] public bool ActionStarted;
        [SaveableField(11)] public bool ActionCompleted;
        [SaveableField(12)] public bool RestorationStarted;
        [SaveableField(13)] public bool RestorationCompleted;
        [SaveableField(14)] public bool ActionReturned;
        [SaveableField(15)] public bool RestorationReturned;
        [SaveableField(16)] public float PostMoveInfluence;
        [SaveableField(17)] public int PostMoveDebt;
        [SaveableField(18)] public Banner PostMoveBanner;
        [SaveableField(19)] public uint PostMoveColor;
        [SaveableField(20)] public uint PostMoveColor2;
        [SaveableField(21)] public bool PostMoveCaptured;

        internal bool TryBeginAction()
        {
            if (ActionStarted || ActionCompleted) return false;
            ActionStarted = true;
            return true;
        }

        internal bool CompleteAction(bool verified)
        {
            if (!ActionStarted || !verified) return false;
            ActionCompleted = true;
            return true;
        }

        internal bool TryBeginRestoration()
        {
            if (EndMercenaryContract || !ActionCompleted || RestorationStarted || RestorationCompleted) return false;
            RestorationStarted = true;
            return true;
        }

        internal bool CompleteRestoration(bool verified)
        {
            if (EndMercenaryContract || !ActionCompleted || !RestorationStarted || !verified) return false;
            RestorationCompleted = true;
            return true;
        }
    }

    public sealed class RealmUnionTitleRecord
    {
        [SaveableField(1)] public string TitleId;
        [SaveableField(2)] public string LegalHolderId;
        [SaveableField(3)] public string ActualHolderId;
        [SaveableField(4)] public string LegalParentId;
        [SaveableField(5)] public string ActualParentId;
        [SaveableField(6)] public string OriginRealmId;
        [SaveableField(7)] public string CapitalId;
        [SaveableField(8)] public FeudalTitleType Rank;

        internal static RealmUnionTitleRecord Capture(FeudalTitleRecord title) => new RealmUnionTitleRecord
        {
            TitleId = title.TitleId, LegalHolderId = title.DeJureHolderClanId,
            ActualHolderId = title.DeFactoHolderClanId, LegalParentId = title.ParentTitleId,
            ActualParentId = title.DeFactoParentTitleId, OriginRealmId = title.AssociatedKingdomId,
            CapitalId = title.CapitalSettlementId, Rank = title.TitleType
        };
    }
}
