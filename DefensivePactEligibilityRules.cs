using System;

namespace BellumCivile
{
    // The caller provides a cached frontier distance and current diplomatic facts.
    internal static class DefensivePactEligibilityRules
    {
        internal static int JoiningThreshold(int frontierDistance)
        {
            if (frontierDistance <= 0) return 95;
            if (frontierDistance == 1) return 25;
            if (frontierDistance == 2) return 50;
            if (frontierDistance == 3) return 75;
            return 95;
        }

        internal static bool CanJoin(double threat, int joiningThreshold, int fiefs, double realmStrength,
            double targetStrength, int rulerRelation, bool atPeace, bool alreadyMember, bool alliedToTarget,
            bool pactWithTarget, bool clientOfTarget, bool cooldownActive)
        {
            return DefensivePactThreatRules.Finite(threat) && threat >= 0 && threat <= 100
                && (joiningThreshold == 25 || joiningThreshold == 50 || joiningThreshold == 75 || joiningThreshold == 95)
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
                || (joiningThreshold != 25 && joiningThreshold != 50 && joiningThreshold != 75 && joiningThreshold != 95))
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
