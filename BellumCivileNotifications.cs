using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class BellumCivileNotifications
    {
        // Use before building notification-only names and text. Do not guard gameplay effects.
        public static bool ShouldPrepare(
            Kingdom primaryKingdom = null,
            Kingdom secondaryKingdom = null,
            Clan primaryClan = null,
            Clan secondaryClan = null,
            bool isPersonal = false,
            bool isMajorEvent = false,
            [CallerMemberName] string source = null)
        {
            bool allowed = ShouldShow(primaryKingdom, secondaryKingdom, primaryClan, secondaryClan, isPersonal, isMajorEvent);
            if (!allowed && NotificationPerformanceDiagnostics.Enabled)
                NotificationPerformanceDiagnostics.Record(false, isPersonal, isMajorEvent, "prepare:" + source, 0, 0);
            return allowed;
        }

        public static bool ShouldShow(
            Kingdom primaryKingdom = null,
            Kingdom secondaryKingdom = null,
            Clan primaryClan = null,
            Clan secondaryClan = null,
            bool isPersonal = false,
            bool isMajorEvent = false)
        {
            if (isPersonal)
                return true;

            BellumNotificationScope scope = BellumCivileOptions.BellumCivileNotifications;
            if (scope == BellumNotificationScope.Disabled)
                return false;
            if (scope == BellumNotificationScope.Global)
                return true;

            if (isMajorEvent)
                return true;

            Clan playerClan = Clan.PlayerClan;
            Kingdom playerKingdom = playerClan?.Kingdom;
            if (playerClan == null)
                return false;

            if (primaryClan == playerClan || secondaryClan == playerClan)
                return true;

            if (playerKingdom == null)
                return false;

            if (primaryClan?.Kingdom == playerKingdom || secondaryClan?.Kingdom == playerKingdom)
                return true;

            return primaryKingdom == playerKingdom || secondaryKingdom == playerKingdom;
        }

        public static void Show(
            TextObject text,
            Color color,
            Kingdom primaryKingdom = null,
            Kingdom secondaryKingdom = null,
            Clan primaryClan = null,
            Clan secondaryClan = null,
            bool isPersonal = false,
            bool isMajorEvent = false)
        {
            if (text == null)
                return;

            if (!ShouldShow(primaryKingdom, secondaryKingdom, primaryClan, secondaryClan, isPersonal, isMajorEvent))
            {
                if (NotificationPerformanceDiagnostics.Enabled)
                    NotificationPerformanceDiagnostics.Record(false, isPersonal, isMajorEvent, NotificationPerformanceDiagnostics.TextId(text), 0, 0);
                return;
            }

            bool measure = NotificationPerformanceDiagnostics.Enabled;
            long start = measure ? Stopwatch.GetTimestamp() : 0;
            string rendered = text.ToString();
            long formatted = measure ? Stopwatch.GetTimestamp() : 0;
            if (string.IsNullOrWhiteSpace(rendered))
                return;

            InformationManager.DisplayMessage(new InformationMessage(rendered, color));
            if (measure)
                NotificationPerformanceDiagnostics.Record(true, isPersonal, isMajorEvent, NotificationPerformanceDiagnostics.TextId(text),
                    formatted - start, Stopwatch.GetTimestamp() - formatted);
        }

        public static void ShowPersonal(TextObject text, Color color)
        {
            if (text == null)
                return;

            Show(text, color, isPersonal: true);
        }

        public static void ShowPersonal(string text, Color color)
        {
            Show(text, color, isPersonal: true);
        }

        public static void Show(
            string text,
            Color color,
            Kingdom primaryKingdom = null,
            Kingdom secondaryKingdom = null,
            Clan primaryClan = null,
            Clan secondaryClan = null,
            bool isPersonal = false,
            bool isMajorEvent = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!ShouldShow(primaryKingdom, secondaryKingdom, primaryClan, secondaryClan, isPersonal, isMajorEvent))
            {
                if (NotificationPerformanceDiagnostics.Enabled)
                    NotificationPerformanceDiagnostics.Record(false, isPersonal, isMajorEvent, "preformatted", 0, 0);
                return;
            }

            bool measure = NotificationPerformanceDiagnostics.Enabled;
            long start = measure ? Stopwatch.GetTimestamp() : 0;
            InformationManager.DisplayMessage(new InformationMessage(text, color));
            if (measure)
                NotificationPerformanceDiagnostics.Record(true, isPersonal, isMajorEvent, "preformatted", 0, Stopwatch.GetTimestamp() - start);
        }

        internal static void ShowCapturedOutcome(string text, Color color, bool allowed)
        {
            bool measure = NotificationPerformanceDiagnostics.Enabled;
            if (!allowed)
            {
                if (measure) NotificationPerformanceDiagnostics.Record(false, false, false, "captured-outcome", 0, 0);
                return;
            }

            long start = measure ? Stopwatch.GetTimestamp() : 0;
            InformationManager.DisplayMessage(new InformationMessage(text, color));
            if (measure) NotificationPerformanceDiagnostics.Record(true, false, false, "captured-outcome", 0, Stopwatch.GetTimestamp() - start);
        }
    }
}
