using BellumCivile.Behaviors;
using HarmonyLib;
using System.Reflection;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Vanilla deducts influence before the lord answers the army request.
    /// Refused Bellum summons must therefore stop that consequence before the
    /// higher-priority refusal dialogue is evaluated.
    /// </summary>
    [HarmonyPatch]
    internal static class ArmySummonsConversationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(LordConversationsCampaignBehavior),
                "player_ask_to_join_players_army_on_consequence");
        }

        private static bool Prefix()
        {
            return ArmySummonsConversationBehavior.ShouldVanillaDeductInfluence();
        }
    }
}
