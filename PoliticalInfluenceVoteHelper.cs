using System;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class PoliticalInfluenceVoteTally
    {
        public int YayInfluence { get; }
        public int NayInfluence { get; }
        public int Participation => YayInfluence + NayInfluence;
        public int Quorum { get; }
        public bool HasQuorum => Participation >= Quorum;
        public bool IsRatified => HasQuorum && YayInfluence > NayInfluence;
        public int RatificationOverrideCost { get; }
        public int RejectionOverrideCost { get; }

        public PoliticalInfluenceVoteTally(int yayInfluence, int nayInfluence, int quorum)
        {
            YayInfluence = Math.Max(0, yayInfluence);
            NayInfluence = Math.Max(0, nayInfluence);
            Quorum = Math.Max(0, quorum);

            RatificationOverrideCost = IsRatified
                ? 0
                : PoliticalInfluenceVoteHelper.RoundUpToVoteStep(Math.Max(
                    Math.Max(0, Quorum - Participation),
                    Math.Max(0, NayInfluence - YayInfluence + C.TreatyCouncilMinimumVoteStep)));
            RejectionOverrideCost = !IsRatified
                ? 0
                : PoliticalInfluenceVoteHelper.RoundUpToVoteStep(
                    YayInfluence - NayInfluence + C.TreatyCouncilMinimumVoteStep);
        }
    }

    internal static class PoliticalInfluenceVoteHelper
    {
        public static int GetCommitment(float utility, float availableInfluence, out TreatyCouncilVoteStance stance)
        {
            float position = Math.Max(0f, Math.Min(100f, 50f - utility));
            int desired;
            if (position <= 19f)
            {
                stance = TreatyCouncilVoteStance.Yay;
                desired = C.TreatyCouncilStrongCommitment;
            }
            else if (position <= 39f)
            {
                stance = TreatyCouncilVoteStance.Yay;
                desired = C.TreatyCouncilMildCommitment;
            }
            else if (position <= 59f)
            {
                stance = TreatyCouncilVoteStance.Abstain;
                return 0;
            }
            else if (position <= 79f)
            {
                stance = TreatyCouncilVoteStance.Nay;
                desired = C.TreatyCouncilMildCommitment;
            }
            else
            {
                stance = TreatyCouncilVoteStance.Nay;
                desired = C.TreatyCouncilStrongCommitment;
            }

            int commitment = GetAffordableCommitment(desired, availableInfluence);
            if (commitment <= 0)
                stance = TreatyCouncilVoteStance.Abstain;
            return commitment;
        }

        public static int GetCommitment(
            float utility,
            Clan clan,
            out TreatyCouncilVoteStance stance,
            float? balanceOverride = null)
        {
            float available = clan == null
                ? 0f
                : NpcInfluenceBudgetService.GetSpendableInfluence(
                    clan,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    balanceOverride);
            return GetCommitment(utility, available, out stance);
        }

        public static int GetAffordableCommitment(int desired, float availableInfluence)
        {
            int available = Math.Max(0, (int)Math.Floor(availableInfluence));
            if (desired >= C.TreatyCouncilStrongCommitment && available >= C.TreatyCouncilStrongCommitment)
                return C.TreatyCouncilStrongCommitment;
            if (desired >= C.TreatyCouncilMildCommitment && available >= C.TreatyCouncilMildCommitment)
                return C.TreatyCouncilMildCommitment;
            return available >= C.TreatyCouncilMinimumVoteStep ? C.TreatyCouncilMinimumVoteStep : 0;
        }

        public static int GetAffordableCommitment(int desired, Clan clan, float? balanceOverride = null)
        {
            float available = clan == null
                ? 0f
                : NpcInfluenceBudgetService.GetSpendableInfluence(
                    clan,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    balanceOverride);
            return GetAffordableCommitment(desired, available);
        }

        public static int GetVotingCapacity(Clan clan)
        {
            return clan == null ? 0 : GetAffordableCommitment(C.TreatyCouncilStrongCommitment, clan);
        }

        public static PoliticalInfluenceVoteTally Calculate(int yayInfluence, int nayInfluence, int totalVotingCapacity)
        {
            int quorum = RoundUpToVoteStep(Math.Max(
                C.TreatyCouncilMinimumQuorum,
                (int)Math.Ceiling(Math.Max(0, totalVotingCapacity) * C.TreatyCouncilQuorumShare)));
            return new PoliticalInfluenceVoteTally(yayInfluence, nayInfluence, quorum);
        }

        public static int RoundUpToVoteStep(int value)
        {
            int step = C.TreatyCouncilMinimumVoteStep;
            return value <= 0 ? 0 : ((value + step - 1) / step) * step;
        }
    }
}
