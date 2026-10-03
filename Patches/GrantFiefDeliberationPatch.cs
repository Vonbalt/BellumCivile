using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To stop the kingdom-management "give settlement back to the realm" confirmation handler from
    /// force-opening a decision immediately after relinquishing the fief. BellumCivile intentionally
    /// blocks that immediate SettlementClaimantDecision so the new FiefDeliberationBehavior can queue
    /// the vote for a week later; vanilla's UI lambda then indexes into an empty UnresolvedDecisions
    /// list and crashes. This patch replaces just that one lambda for the player's kingdom.
    /// </summary>
    [HarmonyPatch]
    public class GrantFiefDeliberationPatch
    {
        private static readonly FieldInfo SettlementField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM+<>c__DisplayClass21_0:settlement");

        private static readonly FieldInfo ViewModelField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM+<>c__DisplayClass21_0:<>4__this");

        private static readonly MethodInfo RefreshSettlementMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM:OnSettlementGranted");

        private static readonly MethodInfo GetKingdomMethod = AccessTools.PropertyGetter(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM:Kingdom");

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM+<>c__DisplayClass21_0:<OnGrantFief>b__0");

        public static bool Prefix(object __instance)
        {
            if (__instance == null || SettlementField == null || ViewModelField == null)
                return true;

            FiefDeliberationBehavior deliberation = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            if (deliberation == null)
                return true;

            Settlement settlement = SettlementField.GetValue(__instance) as Settlement;
            object kingdomManagementVm = ViewModelField.GetValue(__instance);
            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;

            if (settlement == null || kingdomManagementVm == null || playerKingdom == null)
                return true;

            Kingdom vmKingdom = GetKingdomMethod?.Invoke(kingdomManagementVm, null) as Kingdom;
            if (vmKingdom == null || vmKingdom != playerKingdom)
                return true;

            if (deliberation.HasPendingFiefVoteForSettlement(playerKingdom, settlement))
            {
                RefreshSettlementMethod?.Invoke(kingdomManagementVm, null);
                return false;
            }

            if (!deliberation.QueueRelinquishedSettlementVote(settlement))
            {
                BlockVanillaFiefVotePatch.AllowNextImmediatePlayerClaimantVote();
                return true;
            }

            // Keep the settlement list in sync now that we intentionally skip ForceDecideDecision.
            RefreshSettlementMethod?.Invoke(kingdomManagementVm, null);
            return false;
        }
    }
}
