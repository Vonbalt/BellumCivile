using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using BellumCivile.Behaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomManagementVM), "SetSelectedCategory")]
    internal static class KingdomPolicyStanceRefreshPatch
    {
        private static void Postfix(KingdomManagementVM __instance)
            => Refresh(__instance);

        internal static void Refresh(KingdomManagementVM __instance)
        {
            var policies = __instance.Policy;
            if (policies?.Show != true) return;
            // Reuse card VMs so returning from Factions updates stances without losing selection.
            foreach (var card in policies.ActivePolicies) card.RefreshValues();
            foreach (var card in policies.OtherPolicies) card.RefreshValues();
        }
    }

    [HarmonyPatch(typeof(KingdomManagementVM), "OnFrameTick")]
    internal static class KingdomPolicyMoodRefreshPatch
    {
        private sealed class State
        {
            internal FactionObject Faction;
            internal bool Crown;
            internal CourtPolicyStance Band;
        }
        private static readonly ConditionalWeakTable<KingdomManagementVM, State> States =
            new ConditionalWeakTable<KingdomManagementVM, State>();

        private static void Postfix(KingdomManagementVM __instance)
        {
            if (__instance.Policy?.Show != true) return;
            var clan = Clan.PlayerClan;
            var faction = clan == null ? null : Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(clan);
            bool crown = CourtMembershipEligibility.IsRuler(clan);
            var band = CourtPolicyStanceRules.Resolve(CourtPolicyStance.Neutral, true, faction?.Mood ?? 0);
            var state = States.GetValue(__instance, _ => new State());
            if (state.Faction == faction && state.Crown == crown && state.Band == band) return;
            state.Faction = faction;
            state.Crown = crown;
            state.Band = band;
            KingdomPolicyStanceRefreshPatch.Refresh(__instance);
        }
    }
}
