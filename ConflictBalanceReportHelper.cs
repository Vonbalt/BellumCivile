using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class ConflictBalanceReportHelper
    {
        public static TextObject Build(
            TextObject firstSide,
            float firstPower,
            TextObject secondSide,
            float secondPower,
            bool playerUncommitted)
        {
            firstPower = firstPower > 0f ? firstPower : 0f;
            secondPower = secondPower > 0f ? secondPower : 0f;
            float totalPower = firstPower + secondPower;

            TextObject report;
            if (totalPower <= 0.01f)
            {
                report = new TextObject("{=BC_ConflictBalance_Uncertain}Reports remain too uncertain to judge the strength of either side.");
            }
            else
            {
                float firstShare = firstPower / totalPower;
                if (firstShare >= C.ConflictBalanceOverwhelmingShare)
                {
                    report = BuildDirectionalReport(
                        "{=BC_ConflictBalance_Overwhelming}Reports indicate {STRONGER_SIDE} greatly outmatch {WEAKER_SIDE}.",
                        firstSide,
                        secondSide);
                }
                else if (firstShare >= C.ConflictBalanceAdvantageShare)
                {
                    report = BuildDirectionalReport(
                        "{=BC_ConflictBalance_Advantage}Reports indicate {STRONGER_SIDE} hold the advantage over {WEAKER_SIDE}.",
                        firstSide,
                        secondSide);
                }
                else if (firstShare <= 1f - C.ConflictBalanceOverwhelmingShare)
                {
                    report = BuildDirectionalReport(
                        "{=BC_ConflictBalance_Overwhelming}Reports indicate {STRONGER_SIDE} greatly outmatch {WEAKER_SIDE}.",
                        secondSide,
                        firstSide);
                }
                else if (firstShare <= 1f - C.ConflictBalanceAdvantageShare)
                {
                    report = BuildDirectionalReport(
                        "{=BC_ConflictBalance_Advantage}Reports indicate {STRONGER_SIDE} hold the advantage over {WEAKER_SIDE}.",
                        secondSide,
                        firstSide);
                }
                else
                {
                    report = new TextObject("{=BC_ConflictBalance_Even}Reports indicate {FIRST_SIDE} and {SECOND_SIDE} are evenly matched.");
                    report.SetTextVariable("FIRST_SIDE", firstSide);
                    report.SetTextVariable("SECOND_SIDE", secondSide);
                }
            }

            if (!playerUncommitted)
                return report;

            TextObject withPlayerNote = new TextObject("{=BC_ConflictBalance_PlayerUncommitted}{BALANCE_REPORT}");
            withPlayerNote.SetTextVariable("BALANCE_REPORT", report);
            return withPlayerNote;
        }

        public static TextObject BuildFactionSideName(FactionObject faction)
        {
            TextObject side = new TextObject("{=BC_ConflictSide_Faction}the banners of the {FACTION_NAME}");
            side.SetTextVariable("FACTION_NAME", faction?.GetDisplayName() ?? new TextObject("?"));
            return side;
        }

        public static TextObject BuildLeaderSupportersSideName(TextObject leaderName)
        {
            TextObject side = new TextObject("{=BC_ConflictSide_LeaderSupporters}the supporters of {LEADER_NAME}");
            side.SetTextVariable("LEADER_NAME", leaderName ?? new TextObject("?"));
            return side;
        }

        public static TextObject CrownLoyalistsSideName =>
            new TextObject("{=BC_ConflictSide_CrownLoyalists}the forces loyal to the crown");

        private static TextObject BuildDirectionalReport(
            string template,
            TextObject strongerSide,
            TextObject weakerSide)
        {
            TextObject report = new TextObject(template);
            report.SetTextVariable("STRONGER_SIDE", strongerSide);
            report.SetTextVariable("WEAKER_SIDE", weakerSide);
            return report;
        }
    }
}
