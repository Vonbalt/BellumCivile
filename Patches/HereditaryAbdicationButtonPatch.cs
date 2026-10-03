using System.Collections.Generic;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomManagementVM), "ExecuteKingdomAction")]
    public static class HereditaryAbdicationButtonPatch
    {
        public static bool Prefix(KingdomManagementVM __instance)
        {
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            if (!CrownAccessionBehavior.UsesHereditaryPlayerAbdication(realm)) return true;
            if (!__instance.IsKingdomActionEnabled) return false;
            var behavior = CrownAccessionBehavior.Instance;
            if (behavior == null) return false;
            if (!behavior.TryPreviewAbdication(realm, out var preview, out var reason))
            {
                BellumCivileNotifications.ShowPersonal(reason, BellumNotificationColors.Warning);
                return false;
            }
            InformationManager.ShowInquiry(new InquiryData(
                GameTexts.FindText("str_abdicate_leadership").ToString(), behavior.DescribeAbdication(preview).ToString(),
                true, true, GameTexts.FindText("str_yes").ToString(), GameTexts.FindText("str_no").ToString(),
                () =>
                {
                    if (!behavior.ConfirmAbdication(preview, out var result))
                    {
                        BellumCivileNotifications.ShowPersonal(result, BellumNotificationColors.Warning);
                        __instance.OnRefresh();
                        return;
                    }
                    if (!preview.Completed && !preview.Emergency)
                        BellumCivileNotifications.ShowPersonal(result, BellumNotificationColors.Warning);
                    // Do not force an unrelated filed motion or native abdication vote.
                    AccessTools.Method(typeof(KingdomManagementVM), "ExecuteClose").Invoke(__instance, null);
                }, null), true);
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomManagementVM), "GetIsKingdomActionEnabledWithReason")]
    public static class HereditaryAbdicationAvailabilityPatch
    {
        public static void Postfix(bool isPlayerTheRuler, ref List<TextObject> disabledReasons, ref bool __result)
        {
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            if (!__result || !isPlayerTheRuler || !CrownAccessionBehavior.UsesHereditaryPlayerAbdication(realm)) return;
            var behavior = CrownAccessionBehavior.Instance;
            if (behavior != null && !behavior.TryPreviewAbdication(realm, out _, out var reason))
            {
                __result = false;
                disabledReasons.Add(reason);
            }
        }
    }
}
