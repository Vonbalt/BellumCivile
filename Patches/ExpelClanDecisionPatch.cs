using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using System.Reflection;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To hook into the final outcome of the vanilla Expulsion Decision. If the King initiates a legal expulsion against a vassal, it applies heavy relationship grudges to the victim and their close friends.
    /// </summary>
    [HarmonyPatch(typeof(ExpelClanFromKingdomDecision), "ApplyChosenOutcome")]
    public class ExpelClanDecisionPatch
    {
        private static FieldInfo _expelFieldInfo;
        private static PropertyInfo _expelPropInfo;
        private static bool _expelReflectionInitialized;

        public static bool Prefix(ExpelClanFromKingdomDecision __instance, DecisionOutcome chosenOutcome, ref bool __state)
        {
            __state = false;

            if (!ShouldExpel(chosenOutcome)) return true;

            Clan targetClan = __instance.ClanToExpel;
            Kingdom kingdom = __instance.Kingdom;
            Clan sponsorClan = __instance.ProposerClan ?? kingdom?.RulingClan;

            var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null) return true;

            if (targetClan == Clan.PlayerClan && ideologyBehavior.TryResolvePlayerLegalExpulsionJudgment(kingdom, sponsorClan, targetClan))
            {
                ideologyBehavior.RecordExpulsionVoteOutcome(kingdom, targetClan, true);

                Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>()
                    ?.ClearBribedVotesForTarget(kingdom, targetClan);

                __state = true;
                return false;
            }

            UltimatumResolution resolution = ideologyBehavior.TryResolveLegalExpulsionRebellionDetailed(
                kingdom,
                sponsorClan,
                targetClan,
                deferredResolution => CompleteDeferredLegalExpulsion(
                    deferredResolution,
                    kingdom,
                    sponsorClan,
                    targetClan));
            if (resolution == UltimatumResolution.Failed)
            {
                if (targetClan?.Kingdom == kingdom)
                    return true;

                // A partial custom transition already removed the clan from the decision's
                // kingdom. Running vanilla now would expel it from its new realm instead.
                ApplyCustomResolutionBookkeeping(ideologyBehavior, kingdom, targetClan);
                BellumCivileLogger.Log(
                    $"Skipped vanilla expulsion after partial custom transition; kingdom={kingdom?.StringId ?? "none"}; target={targetClan?.StringId ?? "none"}; currentKingdom={targetClan?.Kingdom?.StringId ?? "none"}.");
                __state = true;
                return false;
            }

            ApplyCustomResolutionBookkeeping(ideologyBehavior, kingdom, targetClan);

            __state = true;
            return false;
        }

        public static void Postfix(ExpelClanFromKingdomDecision __instance, DecisionOutcome chosenOutcome, bool __state)
        {
            CourtAgendaBehavior.Current?.ConcludeExecutiveVote(__instance, ShouldExpel(chosenOutcome));
            if (__state) return;

            // Clear bribed-vote overrides for this specific target now that the vote has resolved.
            Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>()
                ?.ClearBribedVotesForTarget(__instance.Kingdom, __instance.ClanToExpel);

            Clan targetClan = __instance.ClanToExpel;
            Clan sponsorClan = __instance.ProposerClan;
            Kingdom decisionKingdom = __instance.Kingdom ?? sponsorClan?.Kingdom;
            bool expelled = ShouldExpel(chosenOutcome);

            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()
                ?.RecordExpulsionVoteOutcome(decisionKingdom, targetClan, expelled);

            if (expelled)
                ApplyExpulsionAftermath(targetClan, sponsorClan, decisionKingdom);
        }

        private static void CompleteDeferredLegalExpulsion(
            UltimatumResolution resolution,
            Kingdom kingdom,
            Clan sponsorClan,
            Clan targetClan)
        {
            BellumCivileLogger.Log(
                $"Deferred legal expulsion resolved; kingdom={kingdom?.StringId ?? "none"}; target={targetClan?.StringId ?? "none"}; result={resolution}.");

            if (resolution != UltimatumResolution.Failed || targetClan == null || targetClan.Kingdom != kingdom)
                return;

            ChangeKingdomAction.ApplyByLeaveKingdom(targetClan, showNotification: false);
            ApplyExpulsionAftermath(targetClan, sponsorClan, kingdom);
            BellumCivileLogger.Log(
                $"Applied deferred vanilla-equivalent expulsion fallback; kingdom={kingdom?.StringId ?? "none"}; target={targetClan.StringId}; currentKingdom={targetClan.Kingdom?.StringId ?? "none"}.");
        }

        private static void ApplyCustomResolutionBookkeeping(
            IdeologyBehavior ideologyBehavior,
            Kingdom kingdom,
            Clan targetClan)
        {
            ideologyBehavior?.RecordExpulsionVoteOutcome(kingdom, targetClan, true);
            Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>()
                ?.ClearBribedVotesForTarget(kingdom, targetClan);
        }

        private static void ApplyExpulsionAftermath(Clan targetClan, Clan sponsorClan, Kingdom decisionKingdom)
        {
            if (targetClan == null || sponsorClan == null || targetClan.Leader == null || sponsorClan.Leader == null)
                return;

            if (decisionKingdom?.RulingClan == sponsorClan || sponsorClan.Kingdom?.RulingClan == sponsorClan)
            {
                Hero king = sponsorClan.Leader;
                Hero targetLeader = targetClan.Leader;

                if (!targetLeader.IsDead && !king.IsDead)
                    RelationMemoryService.ApplyChange(targetLeader, king, C.ExpelResolveVictimGrudge, true,
                        RelationMemorySources.ExiledMyHouse, 15f, RelationMemoryScope.House);

                if (targetClan == Clan.PlayerClan || sponsorClan == Clan.PlayerClan)
                {
                    TextObject text = new TextObject("{=BC_Expel_Grudge}{CLAN_NAME} resents the crown for questioning their loyalty!");
                    text.SetTextVariable("CLAN_NAME", targetClan.Name);
                    BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Danger);
                }

                Kingdom relationKingdom = decisionKingdom ?? sponsorClan.Kingdom;
                ExpulsionRelationHelper.ApplyFriendMemories(relationKingdom, sponsorClan, king, targetClan);
            }
        }

        internal static bool ShouldExpel(DecisionOutcome chosenOutcome)
        {
            if (chosenOutcome == null) return false;

            if (!_expelReflectionInitialized)
            {
                var type = chosenOutcome.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                System.Type searchType = type;
                while (searchType != null && _expelFieldInfo == null)
                {
                    _expelFieldInfo = searchType.GetField("ShouldBeExpelled", flags)
                                   ?? searchType.GetField("Expel", flags)
                                   ?? searchType.GetField("IsExpelled", flags)
                                   ?? searchType.GetField("bShouldBeExpelled", flags);
                    searchType = searchType.BaseType;
                }

                if (_expelFieldInfo == null)
                {
                    searchType = type;
                    while (searchType != null && _expelPropInfo == null)
                    {
                        _expelPropInfo = searchType.GetProperty("ShouldBeExpelled", flags)
                                      ?? searchType.GetProperty("Expel", flags)
                                      ?? searchType.GetProperty("IsExpelled", flags);
                        searchType = searchType.BaseType;
                    }
                }

                _expelReflectionInitialized = true;
            }

            if (_expelFieldInfo != null) return (bool)_expelFieldInfo.GetValue(chosenOutcome);
            if (_expelPropInfo != null) return (bool)_expelPropInfo.GetValue(chosenOutcome);
            return false;
        }
    }
}
