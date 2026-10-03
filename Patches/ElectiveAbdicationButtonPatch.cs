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
    public static class ElectiveAbdicationButtonPatch
    {
        public static bool Prefix(KingdomManagementVM __instance)
        {
            var realm = Clan.PlayerClan?.Kingdom;
            if (!ElectiveSuccessionBehavior.UsesElection(realm) || realm.RulingClan != Clan.PlayerClan) return true;
            if (!__instance.IsKingdomActionEnabled) return false;
            if (ElectiveSuccessionBehavior.LegalHead(realm.RulingClan) != Hero.MainHero
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || CrownAccessionBehavior.Instance?.IsPending(realm) != false) return false;
            InformationManager.ShowInquiry(new InquiryData(GameTexts.FindText("str_abdicate_leadership").ToString(),
                new TextObject("{=BC_Election_Abdicate}Relinquish the Crown? The nobles will settle the succession through their standing commitments. You remain head of your house, but cannot stand in this election.").ToString(),
                true, true, GameTexts.FindText("str_yes").ToString(), GameTexts.FindText("str_no").ToString(),
                () => { if (CrownAccessionBehavior.Instance.BeginElectiveAbdication(realm))
                    AccessTools.Method(typeof(KingdomManagementVM), "ExecuteClose").Invoke(__instance, null); }, null), true);
            return false;
        }
    }
}
