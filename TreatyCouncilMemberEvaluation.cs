using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class TreatyCouncilMemberEvaluation
    {
        public Clan Clan { get; }
        public float Enthusiasm { get; }
        public float Influence { get; }
        public float Utility { get; }
        public TreatyCouncilVoteStance Stance { get; }
        public int InfluenceCommitment { get; }
        public IReadOnlyList<TreatyCouncilReason> Reasons { get; }
        public TreatyCouncilReason PrimaryReason => Reasons.OrderByDescending(reason => System.Math.Abs(reason.Amount)).FirstOrDefault();

        public TreatyCouncilMemberEvaluation(Clan clan, float enthusiasm, float influence, float utility, TreatyCouncilVoteStance stance, int influenceCommitment, IEnumerable<TreatyCouncilReason> reasons)
        {
            Clan = clan;
            Enthusiasm = enthusiasm;
            Influence = influence;
            Utility = utility;
            Stance = stance;
            InfluenceCommitment = influenceCommitment;
            Reasons = reasons?.ToList() ?? new List<TreatyCouncilReason>();
        }
    }
}
