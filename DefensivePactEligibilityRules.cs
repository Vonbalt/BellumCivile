using System;

namespace BellumCivile
{
    // The caller provides a cached frontier distance and current diplomatic facts.
    internal static class DefensivePactEligibilityRules
    {
        internal const int EarlyNeighborThreshold = 5;
        internal const int NeighborThreshold = 25;
        internal const int SecondFrontierThreshold = 50;
        internal const int ThirdFrontierThreshold = 75;
        internal const int AnyDistanceThreshold = 95;
        internal const int EarlyNeighborMaxFiefs = 5;

        internal static bool CanUseEarlyNeighborBand(int fiefs, bool sharesCulture) =>
            fiefs > 0 && (fiefs <= EarlyNeighborMaxFiefs || sharesCulture);

        internal static int JoiningThreshold(int frontierDistance, int fiefs, bool sharesCulture)
        {
            if (frontierDistance == 1) return CanUseEarlyNeighborBand(fiefs, sharesCulture) ? EarlyNeighborThreshold : NeighborThreshold;
            if (frontierDistance == 2) return SecondFrontierThreshold;
            if (frontierDistance == 3) return ThirdFrontierThreshold;
            return AnyDistanceThreshold;
        }

        internal static bool IsJoiningThreshold(int threshold) => threshold == EarlyNeighborThreshold || threshold == NeighborThreshold
            || threshold == SecondFrontierThreshold || threshold == ThirdFrontierThreshold || threshold == AnyDistanceThreshold;

        internal static bool CanJoin(double threat, int joiningThreshold, int fiefs, double realmStrength,
            double targetStrength, int rulerRelation, bool atPeace, bool alreadyMember, bool alliedToTarget,
            bool pactWithTarget, bool clientOfTarget, bool cooldownActive)
        {
            return DefensivePactThreatRules.Finite(threat) && threat >= 0 && threat <= 100
                && IsJoiningThreshold(joiningThreshold)
                && threat >= joiningThreshold && fiefs >= 0 && fiefs < 25
                && DefensivePactThreatRules.Finite(realmStrength) && realmStrength > 0
                && DefensivePactThreatRules.Finite(targetStrength) && targetStrength > 0
                && realmStrength < targetStrength * .8d && rulerRelation < 0
                && atPeace && !alreadyMember && !alliedToTarget && !pactWithTarget
                && !clientOfTarget && !cooldownActive;
        }

        internal static bool MustLeave(double threat, int joiningThreshold, int rulerRelation,
            bool alliedToTarget, bool pactWithTarget, bool clientOfTarget)
        {
            if (!DefensivePactThreatRules.Finite(threat) || threat < 0 || threat > 100
                || !IsJoiningThreshold(joiningThreshold))
                throw new ArgumentOutOfRangeException();
            return threat < joiningThreshold / 2d || rulerRelation >= 60
                || alliedToTarget || pactWithTarget || clientOfTarget;
        }

        internal static bool MustLeaveWithoutVote(double threat, int joiningThreshold, int rulerRelation,
            bool alliedToTarget, bool pactWithTarget, bool clientOfTarget, bool playerRealm)
        {
            bool ordinaryExit = MustLeave(threat, joiningThreshold, rulerRelation, alliedToTarget, pactWithTarget, clientOfTarget);
            return ordinaryExit && (!playerRealm || alliedToTarget || pactWithTarget || clientOfTarget);
        }
    }
}
