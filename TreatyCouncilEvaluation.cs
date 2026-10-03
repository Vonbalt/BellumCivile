using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    public sealed class TreatyCouncilEvaluation
    {
        public float Support { get; }
        public int OverrideCost { get; }
        public int RejectionOverrideCost { get; }
        public int YayInfluence { get; }
        public int NayInfluence { get; }
        public int Participation { get; }
        public int Quorum { get; }
        public int VotingCapacity { get; }
        public bool IsSoleRulerDecision { get; }
        public bool SoleRulerAccepts { get; }
        public bool IsBudgetBlocked { get; }
        public bool HasQuorum => IsSoleRulerDecision || Participation >= Quorum;
        public bool IsRatified => !IsBudgetBlocked && (IsSoleRulerDecision
            ? SoleRulerAccepts
            : HasQuorum && YayInfluence > NayInfluence);
        public IReadOnlyList<TreatyCouncilMemberEvaluation> Members { get; }

        public TreatyCouncilEvaluation(
            int yayInfluence,
            int nayInfluence,
            int quorum,
            int overrideCost,
            int rejectionOverrideCost,
            IEnumerable<TreatyCouncilMemberEvaluation> members,
            bool isSoleRulerDecision = false,
            bool soleRulerAccepts = false,
            float? soleRulerSupport = null,
            bool isBudgetBlocked = false,
            int votingCapacity = 0,
            float? unopposedRulerSupport = null)
        {
            YayInfluence = System.Math.Max(0, yayInfluence);
            NayInfluence = System.Math.Max(0, nayInfluence);
            Participation = YayInfluence + NayInfluence;
            IsSoleRulerDecision = isSoleRulerDecision;
            IsBudgetBlocked = isBudgetBlocked;
            SoleRulerAccepts = !isBudgetBlocked && isSoleRulerDecision && soleRulerAccepts;
            Quorum = isSoleRulerDecision ? 0 : System.Math.Max(0, quorum);
            VotingCapacity = System.Math.Max(0, votingCapacity);
            Support = isSoleRulerDecision && soleRulerSupport.HasValue
                ? soleRulerSupport.Value
                : Participation == 0 && unopposedRulerSupport.HasValue
                    ? unopposedRulerSupport.Value
                : YayInfluence - NayInfluence;
            OverrideCost = isSoleRulerDecision || isBudgetBlocked ? 0 : System.Math.Max(0, overrideCost);
            RejectionOverrideCost = isSoleRulerDecision || isBudgetBlocked ? 0 : System.Math.Max(0, rejectionOverrideCost);
            Members = members?.ToList() ?? new List<TreatyCouncilMemberEvaluation>();
        }
    }
}
