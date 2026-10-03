using TaleWorlds.Library;

namespace BellumCivile
{
    internal static class BellumCivileDebug
    {
        private static bool? _showInGameMessagesOverride;

        public static bool ShowInGameMessages => _showInGameMessagesOverride ?? BellumCivileOptions.ShowDebugMessagesInGame;

        public static void SetInGameMessages(bool enabled)
        {
            _showInGameMessagesOverride = enabled;
        }

        public static void Trace(string category, string message, bool requestInGameDisplay = false)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string categoryText = string.IsNullOrWhiteSpace(category) ? "general" : category;
            string formattedMessage = $"[BellumCivile {categoryText} debug] {message}";
            BellumCivileLogger.Log(formattedMessage);

            if (ShowInGameMessages && requestInGameDisplay)
                BellumCivileNotifications.ShowPersonal(formattedMessage, BellumNotificationColors.Debug);
        }

        public static void TraceIfEnabled(string category, string message, bool requestInGameDisplay = false)
        {
            if (!ShowInGameMessages)
                return;

            Trace(category, message, requestInGameDisplay);
        }

        public static void TraceYearlyReport(string category, string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return;

            BellumCivileLogger.LogYearlyReport(category, report);

            if (ShowInGameMessages)
                Trace(category, report, requestInGameDisplay: true);
        }
    }
}
