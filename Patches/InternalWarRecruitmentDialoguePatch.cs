using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "conversation_lord_from_ruling_clan_on_condition")]
    internal static class InternalWarRecruitmentDialoguePatch
    {
        internal static bool ConversationBlocked(out bool feud) => BlockCivilWarClanRecruitmentPatch.ShouldBlockRecruitment(
            Hero.OneToOneConversationHero?.Clan, Clan.PlayerClan?.Kingdom, out feud);

        private static bool Prefix(ref bool __result)
        {
            if (!ConversationBlocked(out bool feud)) return true;
            // This is the common refusal node, before native persuasion tasks are created.
            MBTextManager.SetTextVariable("LIEGE_IS_RELATIVE", feud
                ? new TextObject("{=BC_Feud_RecruitRefusal}My house has pledged its swords in this feud. Until it is settled, I will hear no offers to change my allegiance.")
                : new TextObject("{=BC_CivilWar_RecruitRefusal}My house has taken its stand in this struggle for the realm. I will not abandon our cause for another liege while it remains unresolved."));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class InternalWarRecruitmentConsequencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_leave_faction_barter_consequence");
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_lord_defect_to_clan_without_barter_on_consequence");
        }

        private static bool Prefix()
        {
            if (!InternalWarRecruitmentDialoguePatch.ConversationBlocked(out _)) return true;
            ConversationManager.EndPersuasion();
            BlockCivilWarClanRecruitmentPatch.ShowRecruitmentBlocked(Hero.OneToOneConversationHero.Clan, Clan.PlayerClan.Kingdom);
            return false;
        }
    }

    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "defection_barter_successful_on_condition")]
    internal static class InternalWarRecruitmentSuccessPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!InternalWarRecruitmentDialoguePatch.ConversationBlocked(out _)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyAndFinalizePlayerBarter")]
    internal static class InternalWarRecruitmentBarterPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            foreach (var recruitment in barterData.GetBarterables().OfType<JoinKingdomAsClanBarterable>())
            {
                if (!recruitment.IsOffered || recruitment.CurrentAmount <= 0
                    || !BlockCivilWarClanRecruitmentPatch.ShouldBlock(recruitment, out Clan clan, out Kingdom target)) continue;
                // Reject the whole exchange before any gold, goods, or clan transfers apply.
                __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
                BlockCivilWarClanRecruitmentPatch.ShowRecruitmentBlocked(clan, target);
                return false;
            }
            return true;
        }
    }
}
