namespace BellumCivile
{
    internal static class CourtCampaignRules
    {
        internal const string Kind = "court_support_campaign";
        internal const float SupportBonus = 15;
        internal const double SelectionWeight = 0.25;

        internal static CourtObjectiveState DeclarationResult(bool initiated, bool authorized, double now, double selected, double deadline)
        {
            if (!CourtAgendaRules.ObjectiveInWindow(now, selected, deadline))
                return now > deadline ? CourtObjectiveState.Expired : CourtObjectiveState.Cancelled;
            return initiated && authorized ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled;
        }
    }
}
