using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    // Treaty hostages are not ordinary blood-feud prisoners. Bellum's own treaty
    // judgments still call KillCharacterAction directly when the pact is resolved.
    [HarmonyPatch(typeof(ExecutionCampaignBehavior), "OnHeroTakenPrisoner")]
    internal static class HostageBloodFeudCapturePatch
    {
        private static bool Prefix(Hero prisoner) => !HostageCustodyGuard.IsProtected(prisoner);
    }

    [HarmonyPatch(typeof(ExecutionCampaignBehavior), "DailyTickHero")]
    internal static class HostageBloodFeudDailyPatch
    {
        private static bool Prefix(Hero hero) => !HostageCustodyGuard.IsProtected(hero);
    }

    [HarmonyPatch(typeof(ExecutionCampaignBehavior), "GetClanMemberCurrentExecutionData")]
    internal static class HostageBloodFeudSettlementPatch
    {
        private static bool Prefix(Hero hero, ref Clan currentExecutorClan, ref bool __result)
        {
            if (!HostageCustodyGuard.IsProtected(hero)) return true;
            currentExecutorClan = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExecutionCampaignBehavior), "QuarterHourlyTick")]
    internal static class HostageBloodFeudQueuePatch
    {
        private static void Prefix(List<Hero> ____heroesPendingMapEventEndToBeExecuted)
        {
            // A prisoner can become a treaty hostage after the execution was queued.
            ____heroesPendingMapEventEndToBeExecuted.RemoveAll(HostageCustodyGuard.IsProtected);
        }
    }
}
