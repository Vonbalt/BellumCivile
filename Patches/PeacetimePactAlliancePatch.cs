using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DefaultAllianceModel), nameof(DefaultAllianceModel.GetScoreOfStartingAlliance))]
    internal static class PeacetimePactAlliancePatch
    {
        private static readonly TextObject Reason = new TextObject("{=BC_VoluntaryPact_AllianceSupport}Voluntary hostage pact");

        private static void Postfix(Kingdom querierKingdom, Kingdom queriedKingdom, ref ExplainedNumber __result)
        {
            // A zero native result can indicate an early eligibility exit; do not turn it into support.
            if (__result.ResultNumber == 0f) return;
            ApplyBonus(ref __result, Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?
                .HasActiveVoluntaryPact(querierKingdom, queriedKingdom) == true);
        }

        internal static void ApplyBonus(ref ExplainedNumber result, bool active)
        {
            if (active && result.ResultNumber != 0f) result.Add(8f, Reason);
        }
    }
}
