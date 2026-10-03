using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Incidents;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CampaignEventDispatcher), nameof(CampaignEventDispatcher.OnIncidentResolved))]
    internal static class CouncilIncidentResolvedPatch
    {
        private static bool Prefix(Incident incident)
        {
            return incident == null
                || string.IsNullOrEmpty(incident.StringId)
                || !incident.StringId.StartsWith(
                    CouncilIncidentBehavior.RuntimeIncidentIdPrefix,
                    System.StringComparison.Ordinal);
        }
    }
}
