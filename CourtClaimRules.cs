using System;

namespace BellumCivile
{
    internal static class CourtClaimRules
    {
        internal const string Kind = "court_restore_claim";
        internal const float SelectionWeight = 0.25f;
        internal const float SupportBonus = 15f;
        internal const float PackageBonus = 80f;

        internal static double GraceEnd(double deadline, int days) => deadline + Math.Max(1, days);
        internal static bool CanGrace(double acquired, double selected, double deadline, bool pending, double now, int days) =>
            pending && acquired >= 0 && CourtAgendaRules.ObjectiveInWindow(acquired, selected, deadline)
            && now > deadline && now <= GraceEnd(deadline, days);
    }
}
