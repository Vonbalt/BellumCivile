using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.CommentBehaviors;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CommentOnKingdomDestroyedBehavior), "OnKingdomDestroyed")]
    internal static class SuppressTemporaryKingdomDestroyedLogPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom destroyedKingdom)
        {
            return !BellumKingdomVisibilityHelper.ShouldSuppressVanillaDestroyedNotification(destroyedKingdom);
        }
    }

    [HarmonyPatch(typeof(CampaignInformationManager), "NewMapNoticeAdded")]
    internal static class SuppressTemporaryKingdomDestroyedMapNoticePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InformationData informationData)
        {
            if (informationData is KingdomDestroyedMapNotification notification
                && BellumKingdomVisibilityHelper.ShouldSuppressVanillaDestroyedNotification(notification.DestroyedKingdom))
                return false;

            return true;
        }
    }
}
