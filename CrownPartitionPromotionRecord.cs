using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CrownPartitionPromotionRecord
    {
        [SaveableField(1)] public Kingdom Parent;
        [SaveableField(2)] public Clan RetainedHouse;
        [SaveableField(3)] public Hero Heir;
        [SaveableField(4)] public string CrownId;
        [SaveableField(5)] public string FounderId;
        [SaveableField(6)] public Clan Founder;
        [SaveableField(7)] public string SuccessorId;
        [SaveableField(8)] public Kingdom Successor;
        [SaveableField(9)] public Settlement Residence;
        [SaveableField(10)] public CampaignTime CapturedAt;
        [SaveableField(11)] public List<CrownPartitionHouseRecord> Houses = new List<CrownPartitionHouseRecord>();
        [SaveableField(12)] public List<RealmUnionTitleRecord> Titles = new List<RealmUnionTitleRecord>();
        [SaveableField(13)] public bool FounderCreationStarted;
        [SaveableField(14)] public bool FounderVerified;
        [SaveableField(15)] public bool RealmCreationStarted;
        [SaveableField(16)] public bool RealmVerified;
        [SaveableField(17)] public bool EstateDeliveryStarted;
        [SaveableField(18)] public bool EstateVerified;
        [SaveableField(19)] public bool HierarchyVerified;
        [SaveableField(20)] public bool ObligationsSettled;
        [SaveableField(21)] public bool Announced;
        [SaveableField(22)] public bool Completed;
        [SaveableField(23)] public string PendingReason;
        [SaveableField(24)] public List<string> EstateFiefIds = new List<string>();
        [SaveableField(25)] public List<string> EstateTitleIds = new List<string>();
        [SaveableField(26)] public bool FounderInitializationCompleted;
        [SaveableField(27)] public bool RealmInitializationCompleted;
        [SaveableField(28)] public List<CrownEstateDeliveryRecord> EstateReceipts;
        [SaveableField(29)] public bool TransferDiplomacyPrepared;
        [SaveableField(30)] public RealmLawSelectionRecord InheritedLaws;
        [SaveableField(31)] public List<string> InheritedPolicies;
        [SaveableField(32)] public bool GovernmentStarted;
        [SaveableField(33)] public bool GovernmentReturned;
        [SaveableField(34)] public bool GovernmentVerified;
        [SaveableField(35)] public List<string> InheritedEnemyIds;
        [SaveableField(36)] public List<string> WarDeclarationsStarted = new List<string>();
        [SaveableField(37)] public List<string> WarDeclarationsReturned = new List<string>();
        [SaveableField(38)] public bool CrownRegistrationStarted;
        [SaveableField(39)] public bool CrownRegistrationReturned;
        [SaveableField(40)] public Dictionary<string, string> PoliticalParentTargets;
        [SaveableField(41)] public bool HierarchyStarted;
        [SaveableField(42)] public bool HierarchyReturned;
        [SaveableField(43)] public bool CourtFinalizationStarted;
        [SaveableField(44)] public bool CourtFinalizationReturned;

        internal bool HasVerifiedEstateReceipts()
        {
            if (!EstateDeliveryStarted || !EstateVerified || EstateReceipts == null || EstateFiefIds == null
                || EstateTitleIds == null || !EstateTitleIds.Contains(CrownId)
                || EstateFiefIds.Any(string.IsNullOrWhiteSpace) || EstateTitleIds.Any(string.IsNullOrWhiteSpace)
                || EstateFiefIds.Distinct().Count() != EstateFiefIds.Count || EstateTitleIds.Distinct().Count() != EstateTitleIds.Count
                || EstateReceipts.Count != EstateFiefIds.Count + EstateTitleIds.Count) return false;
            var fiefs = new HashSet<string>();
            var titles = new HashSet<string>();
            foreach (var receipt in EstateReceipts)
                if (receipt == null || !receipt.Started || !receipt.ActionReturned || !receipt.Verified
                    || !(receipt.IsTitle ? titles : fiefs).Add(receipt.AssetId)) return false;
            return fiefs.SetEquals(EstateFiefIds) && titles.SetEquals(EstateTitleIds);
        }

        internal bool TryRecoverFounder(Clan candidate)
        {
            if (!FounderCreationStarted || !FounderInitializationCompleted || candidate == null
                || string.IsNullOrWhiteSpace(FounderId) || candidate.StringId != FounderId
                || candidate.IsEliminated || candidate.IsUnderMercenaryService
                || Heir == null || Heir.IsDead || candidate.Leader != Heir || Heir.Clan != candidate
                || Parent == null || candidate == RetainedHouse
                || candidate.Kingdom != Parent && (Successor == null || candidate.Kingdom != Successor)
                || Founder != null && Founder != candidate) return false;
            Founder = candidate;
            FounderVerified = true;
            return true;
        }

        internal bool TryRecoverRealm(Kingdom candidate)
        {
            if (!FounderVerified || !RealmCreationStarted || !RealmInitializationCompleted || candidate == null
                || string.IsNullOrWhiteSpace(SuccessorId) || candidate.StringId != SuccessorId
                || candidate.IsEliminated || candidate == Parent || Founder == null
                || Founder.IsEliminated || Heir == null || Heir.IsDead || Founder.Leader != Heir || Heir.Clan != Founder
                || candidate.RulingClan != Founder || Successor != null && Successor != candidate) return false;
            Successor = candidate;
            RealmVerified = true;
            return true;
        }

        internal bool TryBeginFounderCreation()
        {
            if (Completed || FounderCreationStarted || FounderVerified || Founder != null
                || string.IsNullOrWhiteSpace(FounderId)) return false;
            FounderCreationStarted = true;
            return true;
        }

        internal bool TryBeginRealmCreation()
        {
            if (Completed || !FounderCreationStarted || !FounderVerified || Founder == null
                || RealmCreationStarted || RealmVerified || Successor != null
                || string.IsNullOrWhiteSpace(SuccessorId)) return false;
            RealmCreationStarted = true;
            return true;
        }

        // Callers must verify current campaign objects, property and obligations first.
        // Receipts alone are not evidence that a native callback completed successfully.
        internal bool TryComplete(bool currentStateVerified)
        {
            if (!currentStateVerified || Parent == null || RetainedHouse == null || Heir == null
                || string.IsNullOrWhiteSpace(CrownId) || string.IsNullOrWhiteSpace(FounderId)
                || string.IsNullOrWhiteSpace(SuccessorId)
                || !FounderCreationStarted || !FounderInitializationCompleted || !FounderVerified || Founder == null
                || !RealmCreationStarted || !RealmInitializationCompleted || !RealmVerified || Successor == null
                || Founder == RetainedHouse || Successor == Parent
                || Founder.StringId != FounderId || Successor.StringId != SuccessorId
                || !HasVerifiedEstateReceipts() || !TransferDiplomacyPrepared || !GovernmentStarted || !GovernmentReturned
                || !GovernmentVerified || !HierarchyVerified || !ObligationsSettled
                || !CrownRegistrationStarted || !CrownRegistrationReturned
                || !HierarchyStarted || !HierarchyReturned || PoliticalParentTargets == null
                || !CourtFinalizationStarted || !CourtFinalizationReturned
                || Houses == null || Houses.Count == 0 || Titles == null || Titles.Count == 0)
                return false;
            var ids = new HashSet<string>();
            bool includesFounder = false;
            foreach (var house in Houses)
            {
                var receipt = house?.Transfer;
                if (receipt?.Clan == null || string.IsNullOrWhiteSpace(receipt.Clan.StringId)
                    || receipt.Clan == RetainedHouse || !ids.Add(receipt.Clan.StringId)
                    || !house.MovementReturned || !house.RestorationReturned || !house.PostMoveBalancesCaptured
                    || receipt.EndMercenaryContract || !receipt.ActionStarted || !receipt.ActionCompleted
                    || !receipt.RestorationStarted || !receipt.RestorationCompleted) return false;
                includesFounder |= receipt.Clan == Founder;
            }
            ids.Clear();
            foreach (var title in Titles)
                if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || !ids.Add(title.TitleId)) return false;
            if (!ids.SetEquals(PoliticalParentTargets.Keys)) return false;
            if (!includesFounder || !ids.Contains(CrownId)) return false;
            Completed = true;
            return true;
        }
    }

    public sealed class CrownPartitionHouseRecord
    {
        [SaveableField(1)] public string PrincipalTitleId;
        [SaveableField(2)] public RealmUnionClanRecord Transfer;
        [SaveableField(3)] public bool MovementReturned;
        [SaveableField(4)] public bool RestorationReturned;
        [SaveableField(5)] public float PostMoveInfluence;
        [SaveableField(6)] public int PostMoveDebt;
        [SaveableField(7)] public bool PostMoveBalancesCaptured;
        [SaveableField(8)] public TaleWorlds.Core.Banner PostMoveBanner;
        [SaveableField(9)] public uint PostMoveColor;
        [SaveableField(10)] public uint PostMoveColor2;
    }
}
