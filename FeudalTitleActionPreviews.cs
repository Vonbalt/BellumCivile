using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public enum FeudalSovereignElevationChoice
    {
        NotApplicable = 0,
        PeacefulSeparation = 1,
        RetainHoldingsAndRebel = 2
    }

    public sealed class FeudalSovereignElevationPreview
    {
        public bool IsRequired { get; set; }
        public Kingdom ParentKingdom { get; set; }
        public FeudalTitleRecord ParentSovereignTitle { get; set; }
        public int ExternalSettlementCount { get; set; }
        public int ExternalUpperTitleCount { get; set; }
        public bool HasExternalHoldings => ExternalSettlementCount > 0 || ExternalUpperTitleCount > 0;
    }

    public sealed class FeudalFabricationPreview
    {
        public bool CanStart { get; set; }
        public string Reason { get; set; }
        public FeudalClaimFabricationTrack Track { get; set; }
        public int GoldCost { get; set; }
        public float InfluenceCost { get; set; }
        public float DailyProgress { get; set; }
        public float EstimatedDays { get; set; }
        public FeudalClaimFabricationRecord ActiveRecord { get; set; }
    }

    public sealed class FeudalUsurpationPreview
    {
        public bool IsLawfulAssumption { get; set; }
        public bool CanUsurp { get; set; }
        public string Reason { get; set; }
        public float ControlShare { get; set; }
        public int ControlledTitles { get; set; }
        public int RequiredTitles { get; set; }
        public int TotalTitles { get; set; }
        public int GoldCost { get; set; }
        public float InfluenceCost { get; set; }
        public bool HasClaim { get; set; }
        public FeudalSovereignElevationPreview SovereignElevation { get; set; }
    }

    public enum FeudalTitleFormationBlockReason
    {
        None = 0,
        Unavailable = 1,
        HighestTier = 2,
        MissingLeader = 3,
        SeatNotFullyHeld = 4,
        SeatDisputed = 5,
        NoNeighboringTitles = 6,
        NeighboringTitlesIneligible = 7,
        InsufficientContiguousTitles = 8,
        InvalidCluster = 9,
        ConflictingParents = 10,
        UnauthorizedHolder = 11,
        IneligibleVassal = 12,
        NoReorganizationAuthority = 13,
        ParentDisputed = 14,
        WouldEmptyParent = 15,
        SplitVassalEstate = 16,
        ExistingHigherTitle = 17,
        InsufficientGold = 18,
        ParentNotControlled = 19
    }

    public sealed class FeudalTitleFormationObstacle
    {
        public FeudalTitleRecord Title { get; set; }
        public FeudalTitleFormationBlockReason Reason { get; set; }
    }

    /// <summary>
    /// A live, non-saveable assessment of title formation from one selected seat.
    /// </summary>
    public sealed class FeudalFormationPreview
    {
        public bool IsVisible { get; set; }
        public bool CanForm { get; set; }
        public FeudalTitleFormationBlockReason BlockReason { get; set; }
        public string Reason { get; set; }
        public FeudalTitleType TargetType { get; set; }
        public int RequiredTitles { get; set; }
        public int ContiguousEligibleTitles { get; set; }
        public int NeighboringTitles { get; set; }
        public int EligibleNeighboringTitles { get; set; }
        public int GoldCost { get; set; }
        public int CurrentGold { get; set; }
        public float InfluenceReward { get; set; }
        public System.Collections.Generic.List<FeudalTitleFormationCandidate> Candidates { get; }
            = new System.Collections.Generic.List<FeudalTitleFormationCandidate>();
        public System.Collections.Generic.List<FeudalTitleRecord> SelectableTitles { get; }
            = new System.Collections.Generic.List<FeudalTitleRecord>();
        public System.Collections.Generic.List<FeudalTitleFormationObstacle> Obstacles { get; }
            = new System.Collections.Generic.List<FeudalTitleFormationObstacle>();
    }

    public sealed class FeudalDissolutionPreview
    {
        public bool IsVisible { get; set; }
        public bool CanDissolve { get; set; }
        public string Reason { get; set; }
        public float InfluenceCost { get; set; }
        public int DeJureChildren { get; set; }
        public int DeFactoChildren { get; set; }
    }

    public sealed class FeudalRenamePreview
    {
        public bool CanRename { get; set; }
        public string Reason { get; set; }
    }

    public sealed class FeudalGrantPreview
    {
        public bool CanOpen { get; set; }
        public string Reason { get; set; }
        public float InfluenceCost { get; set; }
        public System.Collections.Generic.List<FeudalGrantRecipientPreview> Recipients { get; }
            = new System.Collections.Generic.List<FeudalGrantRecipientPreview>();
    }

    public sealed class FeudalRevocationPreview
    {
        public bool CanRevoke { get; set; }
        public string Reason { get; set; }
        public float InfluenceCost { get; set; }
        public Clan HolderClan { get; set; }
        public FeudalClaimStrength ClaimStrength { get; set; }
        public bool HolderLikelyDefies { get; set; }
        public float RevokerPower { get; set; }
        public float HolderPower { get; set; }
    }

    public sealed class FeudalGrantRecipientPreview
    {
        public Clan Clan { get; set; }
        public bool CanReceive { get; set; }
        public string Reason { get; set; }
        public int RelationGain { get; set; }
        public bool CreatesIndependentRealm { get; set; }
        public bool TransfersDeJure { get; set; }
        public bool TransfersDeFacto { get; set; }
        public bool HadClaim { get; set; }
    }

    public sealed class FeudalGrantResult
    {
        public FeudalTitleRecord Title { get; set; }
        public Clan RecipientClan { get; set; }
        public Kingdom NewIndependentKingdom { get; set; }
        public bool CreatedIndependentRealm { get; set; }
        public bool TransferredDeJure { get; set; }
        public bool TransferredDeFacto { get; set; }
        public int RelationGain { get; set; }
    }
}
