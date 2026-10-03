using System.Collections.Generic;

namespace BellumCivile
{
    public enum FeudalTitleFormationMode
    {
        Consolidation = 0,
        PersonalReorganization = 1,
        SovereignReorganization = 2
    }

    public sealed class FeudalTitleFormationMove
    {
        public string ChildTitleId { get; set; }
        public string HolderClanId { get; set; }
        public string OldDeJureParentTitleId { get; set; }
        public string OldDeFactoParentTitleId { get; set; }
        public bool ReparentDeJure { get; set; }
        public bool ReparentDeFacto { get; set; }
        public bool IsVassalTitle { get; set; }
    }

    public sealed class FeudalTitleFormationCandidate
    {
        public FeudalTitleType TargetType { get; set; }
        public FeudalTitleFormationMode Mode { get; set; }
        public string Name { get; set; }
        public string CapitalSettlementId { get; set; }
        public string SeedTitleId { get; set; }
        public List<string> ChildTitleIds { get; } = new List<string>();
        public List<FeudalTitleFormationMove> Moves { get; } = new List<FeudalTitleFormationMove>();
        public List<string> AffectedParentTitleIds { get; } = new List<string>();
        public List<string> VassalClanIds { get; } = new List<string>();
        public int DeJureHeldChildren { get; set; }
        public int RequiredDeJureChildren { get; set; }
        public int GoldCost { get; set; }
        public float InfluenceReward { get; set; }
        public float ProsperityScore { get; set; }
        public bool CreatesCoequalSovereignTitle { get; set; }
        public bool HasIndependentSuccessionRisk { get; set; }
    }
}
