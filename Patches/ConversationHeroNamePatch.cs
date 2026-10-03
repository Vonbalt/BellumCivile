using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Vanilla wraps conversation hero names with its generic noble/ruler title after
    /// Bellum has already applied the culturally appropriate feudal title to Hero.Name.
    /// Keep the conversation nameplate on the same single source of truth as the rest
    /// of Bellum's title UI so combinations such as "Baron King Derthert" cannot occur.
    /// </summary>
    [HarmonyPatch(typeof(MissionConversationVM), "Refresh")]
    internal static class ConversationHeroNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(MissionConversationVM __instance)
        {
            Hero conversationHero = Campaign.Current?.ConversationManager?.OneToOneConversationHero;
            if (__instance == null || conversationHero == null)
                return;

            __instance.CurrentCharacterNameLbl = conversationHero.Name?.ToString() ?? string.Empty;
        }
    }
}
