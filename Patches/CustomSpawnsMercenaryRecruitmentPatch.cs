using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Prevents Custom Spawns clans marked ForceNoKingdom from being pulled into kingdom mercenary service.
    /// Vanilla mercenary clans are also minor factions, so this intentionally keys off Custom Spawns data instead.
    /// </summary>
    public static class CustomSpawnsMercenaryRecruitmentGuard
    {
        public static bool ShouldBlock(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && kingdom != null
                && ModIntegrationHelper.IsCustomSpawnsForceNoKingdomClan(clan);
        }

        public static bool TryGetClanAndKingdom(object[] args, out Clan clan, out Kingdom kingdom)
        {
            clan = null;
            kingdom = null;

            if (args == null)
                return false;

            foreach (object arg in args)
            {
                if (clan == null && arg is Clan foundClan)
                    clan = foundClan;
                else if (kingdom == null && arg is Kingdom foundKingdom)
                    kingdom = foundKingdom;

                if (clan != null && kingdom != null)
                    return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ChangeKingdomAction))]
    public static class CustomSpawnsChangeKingdomMercenaryPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(ChangeKingdomAction))
                .Where(method => method.Name == "ApplyByJoinFactionAsMercenary");
        }

        public static bool Prefix(object[] __args)
        {
            if (!CustomSpawnsMercenaryRecruitmentGuard.TryGetClanAndKingdom(__args, out Clan clan, out Kingdom kingdom)
                || !CustomSpawnsMercenaryRecruitmentGuard.ShouldBlock(clan, kingdom))
                return true;

            BellumCivileLogger.Log($"Blocked Custom Spawns ForceNoKingdom clan {clan.StringId} from joining {kingdom.StringId} as a mercenary.");
            return false;
        }
    }

    [HarmonyPatch(typeof(StartMercenaryServiceAction))]
    public static class CustomSpawnsStartMercenaryServicePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(StartMercenaryServiceAction))
                .Where(method => method.Name == "ApplyByDefault");
        }

        public static bool Prefix(object[] __args)
        {
            if (!CustomSpawnsMercenaryRecruitmentGuard.TryGetClanAndKingdom(__args, out Clan clan, out Kingdom kingdom)
                || !CustomSpawnsMercenaryRecruitmentGuard.ShouldBlock(clan, kingdom))
                return true;

            BellumCivileLogger.Log($"Blocked Custom Spawns ForceNoKingdom clan {clan.StringId} from starting mercenary service for {kingdom.StringId}.");
            return false;
        }
    }
}
