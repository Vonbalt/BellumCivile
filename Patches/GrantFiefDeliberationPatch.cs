using System;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;

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
        private static FieldInfo SettlementField;
        private static FieldInfo ViewModelField;

        private static readonly MethodInfo RefreshSettlementMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM:OnSettlementGranted");

        private static readonly MethodInfo GetKingdomMethod = AccessTools.PropertyGetter(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.KingdomManagementVM:Kingdom");

        static MethodBase TargetMethod()
        {
            // Compiler-generated closure numbers change when native VM members are added.
            var callback = typeof(KingdomManagementVM).GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Single(m => m.Name == "<OnGrantFief>b__0" && m.GetParameters().Length == 0);
            SettlementField = AccessTools.Field(callback.DeclaringType, "settlement");
            ViewModelField = AccessTools.Field(callback.DeclaringType, "<>4__this");
            if (SettlementField?.FieldType != typeof(Settlement) || ViewModelField?.FieldType != typeof(KingdomManagementVM))
                throw new InvalidOperationException("Unsupported grant-fief confirmation closure.");
            return callback;
        }

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
