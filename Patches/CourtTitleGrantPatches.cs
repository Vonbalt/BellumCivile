using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(FeudalTitlePlayerActionService), nameof(FeudalTitlePlayerActionService.TryExecuteGrant))]
    internal static class CourtTitleGrantReceiptPatch
    {
        private static void Postfix(Clan grantorClan, FeudalTitleRecord title, Clan recipientClan, bool __result)
        {
            if (__result) CourtAgendaBehavior.Current?.ObserveHierarchyTitleGrant(grantorClan, title, recipientClan);
        }
    }
}
