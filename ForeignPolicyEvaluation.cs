using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class ForeignPolicyClaimStake
    {
        public Clan ClaimantClan { get; set; }
        public FeudalTitleRecord Title { get; set; }
        public FeudalClaimStrength Strength { get; set; }
        public bool IsImpliedDeJureClaim { get; set; }
        public float Pressure { get; set; }
    }

    public sealed class ForeignPolicyEvaluation
    {
        public ForeignPolicyActionType ActionType { get; set; }
        public Kingdom SourceKingdom { get; set; }
        public Kingdom TargetKingdom { get; set; }
        public ForeignPolicyMotive DominantMotive { get; set; }
        public bool IsNeighboringRealm { get; set; }
        public bool IsCandidate { get; set; }
        public bool PassesEffectiveThreshold { get; set; }
        public float BaseDiplomacyScore { get; set; }
        public float ClaimScoreBonus { get; set; }
        public float PersonalClaimPressure { get; set; }
        public float EffectiveDiplomacyScore => BaseDiplomacyScore + ClaimScoreBonus;
        public float DecisionThreshold { get; set; }
        public float ClaimPressure { get; set; }
        public float BaseUrgency { get; set; }
        public float FactionAdjustment { get; set; }
        public float ProvisionalUrgency => Math.Max(0f, Math.Min(100f, BaseUrgency + FactionAdjustment));
        public FactionType? CourtFactionType { get; set; }
        public ForeignPolicyObjectiveStatus ObjectiveStatus { get; set; }
        public ForeignPolicyTributeAssessment TributeAssessment { get; set; }
        public List<ForeignPolicyClaimStake> ClaimStakes { get; } = new List<ForeignPolicyClaimStake>();
        public List<string> Reasons { get; } = new List<string>();
    }
}
