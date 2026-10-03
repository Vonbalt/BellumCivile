namespace BellumCivile
{
    internal static class CourtSubjugationRules
    {
        internal const string Kind = "court_seek_clientage";
        internal const float SelectionWeight = 0.25f;
        internal const float SupportBonus = 15f;
        internal const float ConsiderationBonus = 40f;
        internal const float PackageBonus = 80f;

        internal static bool CanSelect(int fiefs, int cost, float strength, float targetStrength) =>
            fiefs > 0 && cost > 0 && cost < 150 && strength > 0 && targetStrength >= 0 && targetStrength < strength;

        internal static bool Fulfilled(string client, string suzerain, string target, string realm,
            double started, double selected, double deadline) =>
            !string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(realm) && client == target && suzerain == realm
            && CourtAgendaRules.ObjectiveInWindow(started, selected, deadline);
    }
}
