using System;

namespace BellumCivile
{
    internal static class CourtPeaceRules
    {
        internal const string Kind = "court_seek_peace";
        internal const float AcceptanceBonus = 15f;
        internal const double SelectionWeight = 0.25;

        internal static bool InWindow(double now, double start, double deadline) =>
            CourtAgendaRules.ObjectiveInWindow(now, start, deadline);

        internal static double Session(double selected, int nominationDays, double now, double original) =>
            CourtAgendaRules.EarlyObjectiveSession(selected, nominationDays, now, original);
    }
}
