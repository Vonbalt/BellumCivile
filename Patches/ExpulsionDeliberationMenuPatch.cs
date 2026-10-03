using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To stop the kingdom clan screen from immediately opening the expulsion voting popup after the
    /// player presses the expel button. BellumCivile queues that judgment for a deliberation period,
    /// so the kingdom menu should enqueue the delayed vote and refresh its state instead of invoking
    /// the vanilla _forceDecide callback.
    /// </summary>
    [HarmonyPatch]
    public class ExpulsionDeliberationMenuPatch
    {
        private static readonly FieldInfo CurrentClanField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:_currentSelectedClan");

        private static readonly FieldInfo ClanItemClanField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanItemVM:Clan");

        private static readonly MethodInfo ExpelCostGetter = AccessTools.PropertyGetter(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:ExpelCost");

        private static readonly MethodInfo RefreshClanListMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:RefreshClanList");

        private static readonly MethodInfo SelectClanMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:SelectClan");

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:ExecuteExpelCurrentClan");

        public static bool Prefix(object __instance)
        {
            if (__instance == null || CurrentClanField == null || ClanItemClanField == null)
                return true;

            if (!(ExpelCostGetter?.Invoke(__instance, null) is int expelCost))
                return true;

            if (Hero.MainHero.Clan.Influence < expelCost)
                return false;

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null)
                return true;

            object currentClanVm = CurrentClanField.GetValue(__instance);
            if (currentClanVm == null)
                return true;

            Clan targetClan = ClanItemClanField.GetValue(currentClanVm) as Clan;
            if (targetClan == null)
                return true;

            IdeologyBehavior ideologyBehavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null)
                return true;

            if (!ideologyBehavior.TryStartTreasonVote(kingdom, targetClan, Clan.PlayerClan, out var explanation))
            {
                BellumCivileNotifications.ShowPersonal(explanation, BellumNotificationColors.Politics);
                RefreshClanListMethod?.Invoke(__instance, null);
                SelectClanMethod?.Invoke(__instance, new object[] { targetClan });
                return false;
            }

            RefreshClanListMethod?.Invoke(__instance, null);
            SelectClanMethod?.Invoke(__instance, new object[] { targetClan });
            return false;
        }
    }
}
