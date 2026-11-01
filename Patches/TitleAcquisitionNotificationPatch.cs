using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map;

namespace BellumCivile.Patches
{
    internal static class TitleAcquisitionNotificationPatch
    {
        private static bool _installed;
        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.title.notifications");
            try
            {
                harmony.Patch(AccessTools.GetDeclaredConstructors(typeof(MapNotificationVM))[0],
                    postfix: new HarmonyMethod(typeof(TitleAcquisitionNotificationPatch), nameof(Register)));
                _installed = true;
            }
            catch { harmony.UnpatchAll(harmony.Id); throw; }
        }

        private static void Register(MapNotificationVM __instance) => __instance.RegisterMapNotificationType(
            typeof(TitleAcquiredMapNotification), typeof(UI.TitleAcquiredNotificationItemVM));
    }
}
