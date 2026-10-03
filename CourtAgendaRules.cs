using System;

namespace BellumCivile
{
    internal static class CourtAgendaRules
    {
        internal static bool ReceivesPoliticalSupport(bool player, bool ruler, bool aligned,
            bool currentMember, bool sessionMember) =>
            !player && (ruler ? aligned : currentMember && sessionMember);

        internal static bool ObjectiveInWindow(double now, double start, double deadline) =>
            !double.IsNaN(now) && !double.IsInfinity(now) && now >= start && now <= deadline;

        internal static double EarlyObjectiveSession(double selected, int nominationDays, double now, double original) =>
            Math.Min(original, Math.Max(now + 1, selected + nominationDays + 1));

        internal static bool Qualifies(int forPoints, int againstPoints, int supportingHouses,
            bool affordable, bool sponsorSupports, float sponsorPreference) =>
            affordable && sponsorSupports && sponsorPreference > 0 && forPoints > 0
            && (forPoints > againstPoints || (supportingHouses >= 2 && (long)forPoints * 100 >= 30L * (forPoints + againstPoints)));

        internal static double NextBoundary(double day, double interval) => (Math.Floor(day / interval) + 1) * interval;
        internal static int NominationDays(double termDays) => Math.Max(1, (int)Math.Floor(termDays / 8));
        internal static double NominationCloses(double now, double sessionDay, int windowDays) => Math.Min(now + windowDays, sessionDay - 1);

        internal static bool NominationAuthorized(CourtAgendaState state, string storedKind, string requestedKind,
            double now, double deadline) => state == CourtAgendaState.AwaitingNomination
            && (requestedKind == "policy" || requestedKind == "council_appointment")
            && (storedKind ?? "policy") == requestedKind && now < deadline;

        internal static int LandConcentration(int houses, int totalFiefs, int topFiefs)
        {
            if (houses < 3 || totalFiefs <= 0) return 0;
            int topHouses = (houses + 2) / 3;
            if ((long)topFiefs * houses * 5 <= (long)totalFiefs * topHouses * 6) return -1;
            if ((long)topFiefs * houses * 5 >= (long)totalFiefs * topHouses * 9) return 1;
            return 0;
        }
    }
}
