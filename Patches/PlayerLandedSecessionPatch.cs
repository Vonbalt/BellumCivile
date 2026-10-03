using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Converts the kingdom menu's "keep all holdings" departure into Bellum's independence
    /// civil war before vanilla can detach the player as a kingdomless clan.
    /// </summary>
    [HarmonyPatch(typeof(KingdomManagementVM), "OnConfirmLeaveKingdomWithOption")]
    internal static class PlayerLandedSecessionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(KingdomManagementVM __instance, List<InquiryElement> obj)
        {
            string selectedOption = obj?.FirstOrDefault()?.Identifier as string;
            if (selectedOption != "keep" || !WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            WarPeaceRevampBehavior warPeace = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            string failureReason = "war and peace behavior unavailable";
            if (warPeace == null || !warPeace.TryStartPlayerLandedSecession(out failureReason))
            {
                BellumCivileLogger.Log(
                    $"Could not intercept player landed secession; reason={failureReason}. Falling back to vanilla departure.");
                return true;
            }

            __instance.ExecuteClose();
            return false;
        }
    }
}
