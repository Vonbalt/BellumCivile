using HarmonyLib;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "CanHeroMoveToAnotherSettlement")]
    internal static class AsylumVisitorRelocationPatch
    {
        private static bool Prefix(Hero hero, ref bool __result)
        {
            if (Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>()?.IsAwaitingAsylum(hero) != true) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "SpawnLordParty")]
    internal static class AsylumVisitorCommandPatch
    {
        private static bool Prefix(Hero __0, ref MobileParty __result)
        {
            // The native command-selection fallback can bypass Hero.CanLeadParty().
            if (Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>()?.IsAwaitingAsylum(__0) != true) return true;
            __result = null;
            return false;
        }
    }
}
