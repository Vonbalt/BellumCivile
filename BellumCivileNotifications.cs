using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class BellumCivileNotifications
    {
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

            Show(text.ToString(), color, primaryKingdom, secondaryKingdom, primaryClan, secondaryClan, isPersonal, isMajorEvent);
        }

        public static void ShowPersonal(TextObject text, Color color)
        {
            if (text == null)
                return;

            ShowPersonal(text.ToString(), color);
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
                return;

            InformationManager.DisplayMessage(new InformationMessage(text, color));
        }
    }
}
