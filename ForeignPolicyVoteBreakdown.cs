using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class ForeignPolicyVoteBreakdown
    {
        public ForeignPolicyActionType ActionType { get; set; }
        public Kingdom SourceKingdom { get; set; }
        public Kingdom TargetKingdom { get; set; }
        public Clan VoterClan { get; set; }
        public Clan ProposerClan { get; set; }
        public float StrategicScore { get; set; }
        public float LegalScore { get; set; }
        public float CourtScore { get; set; }
        public float RelationshipScore { get; set; }
        public float TraitScore { get; set; }
        public float ReadinessScore { get; set; }
        public float ObjectiveScore { get; set; }
        public float TributeScore { get; set; }
        public float TotalScore { get; set; }
        public List<string> Reasons { get; } = new List<string>();

        public string Format()
        {
            return $"{VoterClan?.Name}: total={TotalScore:+0.0;-0.0;0.0}; strategic={StrategicScore:+0.0;-0.0;0.0}; legal={LegalScore:+0.0;-0.0;0.0}; court={CourtScore:+0.0;-0.0;0.0}; relations={RelationshipScore:+0.0;-0.0;0.0}; traits={TraitScore:+0.0;-0.0;0.0}; readiness={ReadinessScore:+0.0;-0.0;0.0}; objectives={ObjectiveScore:+0.0;-0.0;0.0}; tribute={TributeScore:+0.0;-0.0;0.0}; reasons={string.Join(", ", Reasons)}";
        }
    }
}
