using System;

namespace BellumCivile
{
    internal static class CourtActivityRules
    {
        internal const string Kind = "faction_activity";
        internal const double AdmissionChance = .30;
        internal const double Weight = .25;
        internal static bool MatchesMood(float mood, bool positive) => positive ? mood >= 21 : mood <= -21;
        internal static bool Admit(float mood, double roll) => !double.IsNaN(roll) && roll >= 0 && roll < AdmissionChance
            && (MatchesMood(mood, true) || MatchesMood(mood, false));
    }
}
