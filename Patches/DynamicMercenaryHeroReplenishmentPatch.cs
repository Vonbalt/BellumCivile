using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class DynamicMercenaryHeroReplenishmentPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(HeroSpawnCampaignBehavior),
                "SpawnMinorFactionHeroes",
                new[] { typeof(Clan), typeof(bool) });
        }

        private static bool Prefix(Clan clan)
        {
            return !DynamicMercenaryBandService.IsDynamicBand(clan);
        }
    }
}
