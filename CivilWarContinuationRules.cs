namespace BellumCivile
{
    internal enum CivilWarDefeatedClanDestination { CurrentCrown, VictoriousClaimant }

    internal static class CivilWarContinuationRules
    {
        internal static bool DemandSatisfied(FactionType demand, bool targetedMonarchReplaced, bool claimantInstalled)
        {
            switch (demand)
            {
                case FactionType.Abdication: return targetedMonarchReplaced;
                case FactionType.InstallRuler: return claimantInstalled;
                default: return false;
            }
        }

        internal static bool MutuallyExclusive(FactionType first, FactionType second,
            bool sameCrown, bool sameClaimant) => sameCrown && !sameClaimant
            && first == FactionType.InstallRuler && second == FactionType.InstallRuler;

        // Claimant victories absorb the defeated coalition, whether Crown or rival.
        internal static CivilWarDefeatedClanDestination DefeatedClanDestination(bool victoriousSideIsClaimant) =>
            victoriousSideIsClaimant ? CivilWarDefeatedClanDestination.VictoriousClaimant
                : CivilWarDefeatedClanDestination.CurrentCrown;
    }
}
