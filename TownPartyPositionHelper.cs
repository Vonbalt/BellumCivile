using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal static class TownPartyPositionHelper
    {
        internal const float MinimumSpawnDistance = 6f;
        internal const float MaximumSpawnDistance = 12f;
        // Use the native distance model's long-range budget when testing disconnection.
        internal const float LongRangePathDistance = 100000000f;

        internal static bool IsValidLandPoint(CampaignVec2 point) => point.IsOnLand
            && NavigationHelper.IsPositionValidForNavigationType(point, MobileParty.NavigationType.Default);

        internal static bool CanReachLandPoint(CampaignVec2 from, CampaignVec2 to, float maximumDistance)
        {
            if (!IsValidLandPoint(from) || !IsValidLandPoint(to)) return false;
            var models = Campaign.Current.Models;
            return Campaign.Current.MapSceneWrapper.GetPathDistanceBetweenAIFaces(from.Face, to.Face,
                from.ToVec2(), to.ToVec2(), .3f, maximumDistance, out _,
                models.PartyNavigationModel.GetInvalidTerrainTypesForNavigationType(MobileParty.NavigationType.Default),
                models.MapDistanceModel.RegionSwitchCostFromLandToSea, models.MapDistanceModel.RegionSwitchCostFromSeaToLand);
        }

        internal static bool TryFindSpawn(Settlement home, out CampaignVec2 position)
        {
            position = CampaignVec2.Invalid;
            if (home == null || !IsValidLandPoint(home.GatePosition)) return false;
            var candidate = NavigationHelper.FindReachablePointAroundPosition(home.GatePosition,
                MobileParty.NavigationType.Default, MaximumSpawnDistance, MinimumSpawnDistance, false);
            float distance = candidate.DistanceSquared(home.GatePosition);
            // The native search returns its center on failure; never accept that fallback as a spawn.
            if (float.IsNaN(distance) || float.IsInfinity(distance)
                || distance < MinimumSpawnDistance * MinimumSpawnDistance
                || distance > MaximumSpawnDistance * MaximumSpawnDistance
                || !CanReachLandPoint(home.GatePosition, candidate, MaximumSpawnDistance * 2)) return false;
            position = candidate;
            return true;
        }
    }
}
