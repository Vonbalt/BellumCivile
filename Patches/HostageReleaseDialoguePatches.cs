using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_player_let_prisoner_go_on_condition")]
    internal static class HostageReleaseDialogueConditionPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (HostageCustodyGuard.IsProtected(Hero.OneToOneConversationHero)) __result = false;
        }
    }

    [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_player_let_prisoner_go_on_consequence")]
    internal static class HostageReleaseDialogueConsequencePatch
    {
        // Guard the reward as well as the release if a stale dialogue is submitted.
        private static bool Prefix() => !HostageCustodyGuard.IsProtected(Hero.OneToOneConversationHero);
    }
}
