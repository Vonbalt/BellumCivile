using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CourtObjectiveReports
    {
        internal static TextObject WithMood(TextObject narrative, TextObject faction, float actualChange, CourtObjectiveState result)
        {
            string effect = result == CourtObjectiveState.Cancelled
                ? new TextObject("{=BC_CourtObjectiveMoodUnchanged}Faction mood unchanged.").ToString()
                : CourtSessionEventReports.ForTarget(faction, CourtSessionEventReports.Change(0, actualChange,
                    new TextObject("{=BC_CourtObjectiveMoodUnit}mood")));
            return new TextObject("{=BC_CourtSessionReport}{REPORT}\n{EFFECT}")
                .SetTextVariable("REPORT", narrative).SetTextVariable("EFFECT", effect);
        }

        internal static Color Color(CourtObjectiveState? result) => result == CourtObjectiveState.Succeeded
            ? BellumNotificationColors.Success : result == CourtObjectiveState.Expired
                ? BellumNotificationColors.Danger : BellumNotificationColors.Politics;
    }
}
