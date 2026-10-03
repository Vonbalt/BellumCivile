using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Temporary feud kingdoms are campaign shells used only to host a private war. They must not
    /// acquire normal kingdom politics while that war is active or while an old save is being repaired.
    /// </summary>
    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    internal static class TemporaryFeudKingdomDecisionPatch
    {
        private static bool Prefix(Kingdom __instance, KingdomDecision kingdomDecision)
        {
            if (!BellumKingdomVisibilityHelper.IsTemporaryFeudKingdom(__instance))
                return true;

            BellumCivileLogger.Log(
                $"Blocked kingdom decision in temporary feud realm; kingdom={__instance.StringId}; decision={kingdomDecision?.GetType().Name ?? "unknown"}.");
            return false;
        }
    }
}
