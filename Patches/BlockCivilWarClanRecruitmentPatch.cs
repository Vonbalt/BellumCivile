using System;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// Keeps recruitment from removing houses committed to Bellum's internal conflicts.
    /// Scripted settlement transfers remain owned by the civil-war and feud resolvers.
    /// </summary>
    [HarmonyPatch(typeof(JoinKingdomAsClanBarterable))]
    public static class BlockCivilWarClanRecruitmentPatch
    {
        private static readonly System.Reflection.FieldInfo TargetKingdomField =
            AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom");

        internal static bool ShouldBlock(JoinKingdomAsClanBarterable barterable, out Clan targetClan, out Kingdom targetKingdom)
        {
            targetClan = barterable?.OriginalOwner?.Clan;
            targetKingdom = TargetKingdomField?.GetValue(barterable) as Kingdom;

            return ShouldBlockRecruitment(targetClan, targetKingdom, out _);
        }

        internal static bool ShouldBlockRecruitment(Clan targetClan, Kingdom targetKingdom, out bool feud)
        {
            feud = false;
            if (targetClan == null || targetKingdom == null || targetClan.Kingdom == targetKingdom)
                return false;

            var feuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            feud = feuds?.ShouldBlockRecruitment(targetClan, targetKingdom) == true;
            // Even recruitment back to the parent would bypass the feud's settlement.
            if (feud) return true;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.ShouldBlockExternalCivilWarRecruitment(targetClan, targetKingdom) ?? false;
        }

        internal static void ShowRecruitmentBlocked(Clan clan, Kingdom targetKingdom)
        {
            ShouldBlockRecruitment(clan, targetKingdom, out bool feud);
            var text = feud
                ? new TextObject("{=BC_Feud_RecruitBlock}You cannot recruit {CLAN_NAME} while their house is committed to an active private war. Their feud must be settled first.")
                : new TextObject("{=BC_CivilWar_RecruitBlock}You cannot recruit {CLAN_NAME} while they are actively fighting in a civil war. Their loyalty must be decided on the battlefield first.");
            text.SetTextVariable("CLAN_NAME", clan.Name);
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Danger);
        }

        private static bool ShouldBlockVanillaDefection(JoinKingdomAsClanBarterable barterable, out Clan targetClan, out Kingdom oldKingdom, out Kingdom targetKingdom)
        {
            targetClan = barterable?.OriginalOwner?.Clan;
            oldKingdom = targetClan?.Kingdom;
            targetKingdom = TargetKingdomField?.GetValue(barterable) as Kingdom;

            if (barterable == null || targetClan == null || oldKingdom == null || targetKingdom == null)
                return false;

            if (!barterable.IsDefecting)
                return false;

            if (targetClan == Clan.PlayerClan || targetClan.IsMinorFaction || targetClan.IsUnderMercenaryService)
                return false;

            if (IsPlayerInitiatedDefection(barterable, targetKingdom))
                return false;

            return oldKingdom != targetKingdom;
        }

        private static bool IsPlayerInitiatedDefection(JoinKingdomAsClanBarterable barterable, Kingdom targetKingdom)
        {
            if (barterable?.OriginalOwner == null || targetKingdom == null)
                return false;

            if (targetKingdom.Leader == Hero.MainHero)
                return true;

            if (targetKingdom != Clan.PlayerClan?.Kingdom)
                return false;

            Hero conversationHero = Campaign.Current?.ConversationManager?.OneToOneConversationHero;
            return Campaign.Current?.ConversationManager?.IsConversationInProgress == true
                && conversationHero != null
                && conversationHero == barterable.OriginalOwner;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetUnitValueForFaction")]
        public static bool GetUnitValueForFactionPrefix(JoinKingdomAsClanBarterable __instance, IFaction factionForEvaluation, ref int __result)
        {
            if (ShouldBlockVanillaDefection(__instance, out _, out _, out _))
            {
                __result = int.MinValue / 4;
                return false;
            }

            if (!ShouldBlock(__instance, out _, out _))
                return true;

            __result = int.MinValue / 4;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("Apply")]
        public static bool ApplyPrefix(JoinKingdomAsClanBarterable __instance)
        {
            if (ShouldBlockVanillaDefection(__instance, out Clan defectingClan, out Kingdom oldKingdom, out Kingdom targetDefectionKingdom))
            {
                BellumCivileLogger.Log($"Blocked vanilla landed clan defection; clan={defectingClan.StringId} old_kingdom={oldKingdom.StringId} target_kingdom={targetDefectionKingdom.StringId} fiefs={defectingClan.Fiefs.Count}.");

                if (Clan.PlayerClan?.Kingdom == targetDefectionKingdom)
                {
                    TextObject text = new TextObject("{=BC_VanillaDefection_Blocked}Bellum Civile blocks silent landed defections. {CLAN_NAME} must leave their realm through rebellion, exile, or formal politics.");
                    text.SetTextVariable("CLAN_NAME", defectingClan.Name);
                    BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Danger);
                }

                return false;
            }

            if (!ShouldBlock(__instance, out Clan targetClan, out Kingdom targetKingdom))
                return true;

            if (Clan.PlayerClan?.Kingdom == targetKingdom)
            {
                ShowRecruitmentBlocked(targetClan, targetKingdom);
            }

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("Apply")]
        public static void ApplyPostfix(JoinKingdomAsClanBarterable __instance)
        {
            Clan recruitedClan = __instance?.OriginalOwner?.Clan;
            Kingdom targetKingdom = TargetKingdomField?.GetValue(__instance) as Kingdom;
            if (recruitedClan == null
                || targetKingdom == null
                || recruitedClan.Kingdom != targetKingdom)
            {
                return;
            }

            Hero[] prisonersToRelease = recruitedClan.Heroes
                .Where(hero => hero != null
                    && hero.IsAlive
                    && hero.IsPrisoner
                    && RetainedTreatyPrisonerBehavior.GetCaptorKingdom(hero) == targetKingdom)
                .ToArray();

            int releasedCount = 0;
            foreach (Hero prisoner in prisonersToRelease)
            {
                try
                {
                    EndCaptivityAction.ApplyByReleasedByChoice(prisoner);
                    releasedCount++;
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Failed to release recruited clan prisoner; clan={recruitedClan.StringId}; hero={prisoner.StringId}; target_kingdom={targetKingdom.StringId}; error={ex.GetType().Name}: {ex.Message}");
                }
            }

            if (releasedCount > 0)
            {
                BellumCivileLogger.Log($"Released recruited clan prisoners held by their new realm; clan={recruitedClan.StringId}; target_kingdom={targetKingdom.StringId}; released={releasedCount}.");
            }
        }
    }
}
