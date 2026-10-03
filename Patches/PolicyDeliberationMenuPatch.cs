using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To stop the kingdom policy screen from force-opening an immediate voting popup after the
    /// player proposes or disavows a policy. BellumCivile routes those player actions into a
    /// week-long deliberation window instead, so the menu must queue the delayed vote and refresh
    /// the tab instead of calling the vanilla _forceDecide callback.
    /// </summary>
    [HarmonyPatch]
    public class PolicyDeliberationMenuPatch
    {
        private static readonly FieldInfo CurrentDecisionField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:_currentItemsUnresolvedDecision");

        private static readonly FieldInfo CurrentPolicyField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:_currentSelectedPolicyObject");

        private static readonly MethodInfo CanProposeGetter = AccessTools.PropertyGetter(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:CanProposeOrDisavowPolicy");

        private static readonly MethodInfo RefreshPolicyListMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:RefreshPolicyList");

        private static readonly MethodInfo SelectPolicyMethod = AccessTools.Method(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:SelectPolicy");

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:ExecuteProposeOrDisavow");

        public static bool Prefix(object __instance)
        {
            if (__instance == null || CurrentDecisionField == null || CurrentPolicyField == null)
                return true;

            if (CurrentDecisionField.GetValue(__instance) != null)
                return true;

            if (!(CanProposeGetter?.Invoke(__instance, null) is bool canPropose) || !canPropose)
                return true;

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null)
                return true;

            PolicyObject policy = CurrentPolicyField.GetValue(__instance) as PolicyObject;
            if (policy == null)
                return true;

            PolicyDeliberationBehavior deliberation = Campaign.Current?.GetCampaignBehavior<PolicyDeliberationBehavior>();
            if (deliberation == null)
                return true;

            bool abolish = kingdom.ActivePolicies.Contains(policy);
            bool playerIsRuler = kingdom.RulingClan == Clan.PlayerClan
                && CourtAgendaBehavior.Current?.IsNominationOpen(kingdom) != true;
            if (!playerIsRuler && !deliberation.CanPlayerUsePolicyMandate(kingdom, policy, abolish))
            {
                BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_CourtAgenda_InvalidPolicyMandate}You do not hold a valid court mandate to propose this policy motion."), BellumNotificationColors.Politics);
                return false;
            }

            System.Action queueAndRefresh = () =>
            {
                if (!deliberation.QueuePlayerProposedVote(kingdom, policy, abolish, agendaDeviationConfirmed: true))
                    BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_NominationNotFiled}The proposal could not be placed before the court. If your mandate remains valid, you may choose another proposal before its deadline."), BellumNotificationColors.Warning);
                RefreshPolicyListMethod?.Invoke(__instance, null);
                SelectPolicyMethod?.Invoke(__instance, new object[] { policy });
            };

            if (!playerIsRuler
                && deliberation.TryShowPlayerPolicyMandateDeviationConfirmation(kingdom, policy, abolish, queueAndRefresh))
                return false;

            queueAndRefresh();
            return false;
        }
    }
}
