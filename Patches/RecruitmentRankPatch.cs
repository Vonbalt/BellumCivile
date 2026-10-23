using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class RecruitmentRankGuard
    {
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom");

        internal static bool IsBlocked(Clan clan, Kingdom realm, out bool peer)
        {
            peer = false;
            Clan player = Clan.PlayerClan;
            if (clan == null || realm == null || player == null || Hero.MainHero == null
                || realm != player.Kingdom || realm.RulingClan != player || player.Leader != Hero.MainHero
                || clan == player || clan.Kingdom == realm || clan.IsEliminated || realm.IsEliminated
                || clan.IsMinorFaction || clan.IsUnderMercenaryService)
                return false;

            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            // Use possessed titles, as independence partitions do, not claims or a historical kingdom shell.
            FeudalTitleRecord sovereign = titles?.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto);
            FeudalTitleRecord highest = titles?.GetHighestDeFactoTitleHeldByClan(clan);
            if (sovereign == null || highest == null || highest.TitleType < sovereign.TitleType)
                return false;

            peer = highest.TitleType == sovereign.TitleType;
            return true;
        }

        internal static bool ConversationBlocked(out bool peer) => IsBlocked(
            Hero.OneToOneConversationHero?.Clan, Clan.PlayerClan?.Kingdom, out peer);

        internal static bool IsBlocked(JoinKingdomAsClanBarterable recruitment, out bool peer)
            => IsBlocked(recruitment?.OriginalOwner?.Clan,
                recruitment == null ? null : TargetField?.GetValue(recruitment) as Kingdom, out peer);

        internal static TextObject Refusal(bool peer) => peer
            ? new TextObject("{=BC_RecruitmentRank_PeerRefusal}We stand as peers in rank. I will not swear fealty to you.")
            : new TextObject("{=BC_RecruitmentRank_HigherRefusal}My title stands above yours. I will not swear fealty to a ruler of lesser rank.");

        internal static void ShowRefusal(bool peer) => BellumCivileNotifications.ShowPersonal(Refusal(peer), BellumNotificationColors.Warning);
    }

    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "conversation_lord_from_ruling_clan_on_condition")]
    internal static class RecruitmentRankDialoguePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ref bool __result)
        {
            if (!RecruitmentRankGuard.ConversationBlocked(out bool peer)) return true;
            MBTextManager.SetTextVariable("LIEGE_IS_RELATIVE", RecruitmentRankGuard.Refusal(peer));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RecruitmentRankConsequencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_leave_faction_barter_consequence");
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_lord_defect_to_clan_without_barter_on_consequence");
        }

        private static bool Prefix()
        {
            if (!RecruitmentRankGuard.ConversationBlocked(out bool peer)) return true;
            ConversationManager.EndPersuasion();
            RecruitmentRankGuard.ShowRefusal(peer);
            return false;
        }
    }

    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "defection_barter_successful_on_condition")]
    internal static class RecruitmentRankSuccessPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!RecruitmentRankGuard.ConversationBlocked(out _)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyAndFinalizePlayerBarter")]
    internal static class RecruitmentRankBarterPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            foreach (var recruitment in barterData.GetBarterables().OfType<JoinKingdomAsClanBarterable>())
            {
                if (!recruitment.IsOffered || recruitment.CurrentAmount <= 0
                    || !RecruitmentRankGuard.IsBlocked(recruitment, out bool peer)) continue;
                // Cancel before any money, goods or allegiance changes are applied.
                __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
                RecruitmentRankGuard.ShowRefusal(peer);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(JoinKingdomAsClanBarterable))]
    internal static class RecruitmentRankBarterablePatch
    {
        [HarmonyPrefix, HarmonyPatch("GetUnitValueForFaction")]
        private static bool ValuePrefix(JoinKingdomAsClanBarterable __instance, ref int __result)
        {
            if (!RecruitmentRankGuard.IsBlocked(__instance, out _)) return true;
            __result = int.MinValue / 4;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch("Apply")]
        private static bool ApplyPrefix(JoinKingdomAsClanBarterable __instance)
        {
            if (!RecruitmentRankGuard.IsBlocked(__instance, out bool peer)) return true;
            RecruitmentRankGuard.ShowRefusal(peer);
            return false;
        }
    }
}
