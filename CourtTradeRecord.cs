using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtTradeRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public List<Clan> Members = new List<Clan>();
        [SaveableField(3)] public bool Activated;
        [SaveableField(4)] public double ActivatedDay;
        [SaveableField(5)] public double SignedDay = -1;
        [SaveableField(6)] public bool ProposalAttempted;
        [SaveableField(7)] public double NextAttemptDay;
        [SaveableField(8)] public string LastBlocker;
        [SaveableField(9)] public string ProposalOutcome;
        [SaveableField(10)] public int TechnicalAttempts;
        [SaveableField(11)] public double NextReviewDay;
    }

    internal static class CourtTradeRules
    {
        internal const string Kind = "court_trade_agreement";
        internal const float SupportBonus = 15;
        internal static float Support(float natural, bool eligible) => eligible
            ? Math.Max(0, Math.Min(100, natural + SupportBonus)) : natural;
        internal static bool InWindow(double day, double selected, double deadline) =>
            CourtAgendaRules.ObjectiveInWindow(day, selected, deadline);
        internal static CourtObjectiveState WarResult(bool initiated, bool deliberate, double now, double deadline) =>
            now > deadline ? CourtObjectiveState.Expired : initiated && deliberate ? CourtObjectiveState.Failed : CourtObjectiveState.Cancelled;
    }
}
