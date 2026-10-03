using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To restrict the in-game policy proposal button so that only the player when they lead an ideological
    /// faction in the realm's court can propose policy votes, mirroring the BellumCivile council system
    /// where faction leaders initiate votes during their faction meetings.
    /// </summary>
    [HarmonyPatch]
    public class KingdomPolicyButtonPatch
    {
        private static readonly FieldInfo CurrentPolicyField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:_currentSelectedPolicyObject");

        static MethodBase TargetMethod() =>
            AccessTools.Method("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies.KingdomPoliciesVM:GetCanProposeOrDisavowPolicyWithReason");

        public static bool Prefix(bool hasUnresolvedDecision, ref bool __result, ref TextObject disabledReason)
        {
            if (hasUnresolvedDecision || CourtAgendaBehavior.Current?.IsNominationOpen(Clan.PlayerClan?.Kingdom) != true)
                return true;
            // Nomination is free; preserve vanilla's non-financial restrictions.
            __result = CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out disabledReason);
            if (__result && Clan.PlayerClan.IsUnderMercenaryService)
            {
                __result = false;
                disabledReason = GameTexts.FindText("str_mercenaries_cannot_propose_policies");
            }
            return false;
        }

        public static void Postfix(object __instance, bool hasUnresolvedDecision, ref bool __result, ref TextObject disabledReason)
        {
            if (!__result || hasUnresolvedDecision) return;

            if (Clan.PlayerClan?.Kingdom == null) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            bool playerIsRuler = Clan.PlayerClan == Clan.PlayerClan.Kingdom.RulingClan;
            var deliberation = Campaign.Current.GetCampaignBehavior<PolicyDeliberationBehavior>();
            PolicyObject selectedPolicy = CurrentPolicyField?.GetValue(__instance) as PolicyObject;

            if (selectedPolicy != null && CourtAgendaBehavior.Current?.CanPropose(Clan.PlayerClan.Kingdom, selectedPolicy) == false)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_CourtPolicyReconsideration}This policy already has a pending motion or is awaiting the next permitted reconsideration date.");
                return;
            }

            if (selectedPolicy != null
                && deliberation?.HasPendingVoteForPolicy(Clan.PlayerClan.Kingdom, selectedPolicy) == true)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_UI_PolicyAlreadyDeliberating}This policy is already under deliberation before the court.");
                return;
            }

            if (deliberation?.HasPlayerProposedVote(Clan.PlayerClan.Kingdom) == true)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_UI_PlayerPolicyDeliberating}You already have a policy motion under deliberation. Await its vote before proposing another.");
                return;
            }

            bool hasCourtMandate = deliberation?.HasActivePlayerPolicyMandate(Clan.PlayerClan.Kingdom) == true;

            if (!playerIsRuler && !hasCourtMandate)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_UI_PolicyDisabled}Only the ruler or a leader in court with an active mandate from his faction's meeting may bring policy votes before the council.");
            }
        }
    }
}
