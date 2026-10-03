using System;

namespace BellumCivile
{
    internal static class CourtExecutiveRules
    {
        internal const string Treason = "treason";
        internal const string Decree = "treason_decree";
        internal const string Grant = "grant_fief";
        internal const string Revoke = "revoke_fief";
        internal static bool GrantEligible(int fiefs) => fiefs > 5;
        internal static bool LibertyRevocationEligible(int fiefs) => fiefs >= 5;
        internal static bool CanOverwrite(bool filed, bool unopened, double sessionDay, double now) =>
            !filed && unopened && sessionDay > now;
        internal static double GrantWeight(bool strongClaim, bool weakClaim, int generosity, int honor, int calculating) =>
            Math.Max(0.1, 1 + (strongClaim ? .75 : weakClaim ? .35 : 0)
                + generosity * .25 + honor * .1 - calculating * .1);
    }
}
