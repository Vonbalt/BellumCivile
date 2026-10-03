using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public enum FeudalDeJureDriftDisplayState
    {
        None = 0,
        Ready = 1,
        Advancing = 2,
        Paused = 3,
        Reversing = 4,
        Blocked = 5
    }

    public enum FeudalDeJureDriftBlockReason
    {
        None = 0,
        InactiveTitle = 1,
        HighestTier = 2,
        MissingLegalParent = 3,
        UnresolvedRights = 4,
        SplitRights = 5,
        NoControllingRealm = 6,
        TemporaryRealm = 7,
        AlreadyIntegrated = 8,
        PartialPackageControl = 9,
        IncludedInHigherPackage = 10,
        NoReceivingTitle = 11,
        ControlShifted = 12,
        AwaitingEvaluation = 13,
        CompletionPending = 14,
        RequirementsNotMet = 15
    }

    /// <summary>
    /// A live, non-saveable projection of the de jure drift rules for UI and diagnostics.
    /// </summary>
    public sealed class FeudalDeJureDriftAssessment
    {
        public bool ShouldDisplay { get; internal set; }
        public FeudalDeJureDriftDisplayState State { get; internal set; }
        public FeudalDeJureDriftBlockReason BlockReason { get; internal set; }
        public Kingdom TargetKingdom { get; internal set; }
        public Kingdom CurrentControlKingdom { get; internal set; }
        public Hero Integrator { get; internal set; }
        public FeudalTitleRecord PackageRoot { get; internal set; }
        public float Progress { get; internal set; }
        public int ControlledTitles { get; internal set; }
        public int RequiredTitles { get; internal set; }
        public string TechnicalReason { get; internal set; }

        public bool IsIncludedInPackage => PackageRoot != null;
    }
}
