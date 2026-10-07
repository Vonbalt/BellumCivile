using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DiplomaticBartersBehavior), "ConsiderClanJoin")]
    internal static class ExileAutomaticRecruitmentPatch
    {
        private static bool Prefix(Clan clan)
        {
            // One scheduler owns asylum attempts, including refusals and their retry date.
            // Direct scripted transfers (inheritance, settlement, unions) remain untouched.
            return Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>()?
                .TakeOwnershipOfAutomaticRecruitment(clan) != true;
        }
    }

    [HarmonyPatch(typeof(JoinKingdomAsClanBarterable))]
    internal static class ExileRecruitmentPatch
    {
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom");

        internal static bool IsBlocked(JoinKingdomAsClanBarterable barterable, out Clan clan, out Kingdom realm)
        {
            clan = barterable?.OriginalOwner?.Clan;
            realm = barterable == null ? null : TargetField.GetValue(barterable) as Kingdom;
            return !RefugeSelectionHelper.CanRecruitLandlessClan(clan, realm);
        }

        [HarmonyPrefix, HarmonyPatch("GetUnitValueForFaction")]
        private static bool ValuePrefix(JoinKingdomAsClanBarterable __instance, ref int __result)
        {
            if (!IsBlocked(__instance, out _, out _)) return true;
            __result = int.MinValue / 4;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch("Apply")]
        private static bool ApplyPrefix(JoinKingdomAsClanBarterable __instance)
            => !IsBlocked(__instance, out _, out _);

        [HarmonyPostfix, HarmonyPatch("Apply")]
        private static void ApplyPostfix(JoinKingdomAsClanBarterable __instance)
        {
            Clan clan = __instance?.OriginalOwner?.Clan;
            Kingdom realm = __instance == null ? null : TargetField.GetValue(__instance) as Kingdom;
            Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>()?.RecordPlayerInvitation(clan, realm);
        }

        internal static void ShowRefusal(Clan clan, Kingdom realm)
        {
            var text = new TextObject("{=BC_Asylum_RecruitmentRefused}{RULER} will not accept the {CLAN} into {REALM} under the present circumstances.");
            text.SetTextVariable("RULER", realm?.Leader?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("CLAN", clan?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("REALM", realm?.Name ?? TextObject.GetEmpty());
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Warning);
        }
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyAndFinalizePlayerBarter")]
    internal static class ExileRecruitmentBarterPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            foreach (var recruitment in barterData.GetBarterables().OfType<JoinKingdomAsClanBarterable>())
            {
                if (!recruitment.IsOffered || recruitment.CurrentAmount <= 0
                    || !ExileRecruitmentPatch.IsBlocked(recruitment, out Clan clan, out Kingdom realm)) continue;
                __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
                ExileRecruitmentPatch.ShowRefusal(clan, realm);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "conversation_lord_from_ruling_clan_on_condition")]
    internal static class ExileRecruitmentDialoguePatch
    {
        internal static bool IsBlocked() => !RefugeSelectionHelper.CanRecruitLandlessClan(
            Hero.OneToOneConversationHero?.Clan, Clan.PlayerClan?.Kingdom);

        private static bool Prefix(ref bool __result)
        {
            if (!IsBlocked()) return true;
            MBTextManager.SetTextVariable("LIEGE_IS_RELATIVE", new TextObject(
                "{=BC_Asylum_RecruitmentDialogueRefused}Your ruler will not receive my house. Until that changes, there is no oath I can offer you."));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ExileRecruitmentConsequencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_leave_faction_barter_consequence");
            yield return AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_lord_defect_to_clan_without_barter_on_consequence");
        }

        private static bool Prefix()
        {
            if (!ExileRecruitmentDialoguePatch.IsBlocked()) return true;
            ConversationManager.EndPersuasion();
            ExileRecruitmentPatch.ShowRefusal(Hero.OneToOneConversationHero?.Clan, Clan.PlayerClan?.Kingdom);
            return false;
        }
    }

    [HarmonyPatch(typeof(LordDefectionCampaignBehavior), "defection_barter_successful_on_condition")]
    internal static class ExileRecruitmentSuccessPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ExileRecruitmentDialoguePatch.IsBlocked()) return true;
            __result = false;
            return false;
        }
    }
}
