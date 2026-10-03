using System;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// A ruler receives opponent peace proposals through vanilla's courier inquiry rather than the
    /// kingdom-vote notification used for vassals. Accepting that inquiry normally applies peace
    /// immediately. Translate the acceptance into Bellum's cached parley and defer opening until
    /// the inquiry layer has closed.
    /// </summary>
    [HarmonyPatch(typeof(PeaceOfferCampaignBehavior), "AcceptPeaceOffer")]
    public static class RulerPeaceOfferParleyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref IFaction ____opponentFaction)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = ____opponentFaction as Kingdom;
            if (playerKingdom == null
                || opponent == null
                || playerKingdom.RulingClan != Clan.PlayerClan)
            {
                return true;
            }

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                .GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return true;

            if (StorylineWarProtectionHelper.TryGetPeaceBlock(playerKingdom, opponent, out TextObject storylineReason))
            {
                ____opponentFaction = null;
                InformationManager.DisplayMessage(new InformationMessage(storylineReason.ToString(), BellumNotificationColors.Warning));
                BellumCivileLogger.Log($"Suppressed a ruler courier peace offer for a Story Mode protected war; war={war.WarKey}.");
                return false;
            }

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            if (treaties?.TryQueueDeferredPlayerParleyOpen(
                    war,
                    forced: false,
                    preferWhitePeace,
                    "vanilla ruler courier peace offer accepted",
                    opponent,
                    out string report) == true)
            {
                // Vanilla keeps this field as a one-off offer lock. Clear it because Bellum now owns
                // the proposal; otherwise later courier offers can be suppressed indefinitely.
                ____opponentFaction = null;
                BellumCivileLogger.Log(
                    $"Redirected vanilla ruler courier offer into Bellum parley; war={war.WarKey}; report={report}.");
                return false;
            }

            BellumCivileLogger.Log(
                $"Could not redirect vanilla ruler courier offer into Bellum parley; war={war.WarKey}.");
            return true;
        }
    }

    /// <summary>
    /// Replaces the diplomacy-tab peace proposal with the Bellum parley whenever the war-and-peace
    /// revamp owns the conflict. Vanilla availability checks still decide whether the player may use
    /// the button; only the resulting action is rerouted.
    /// </summary>
    [HarmonyPatch]
    public static class KingdomDiplomacyPeaceParleyPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(KingdomDiplomacyVM).GetMethod(
                "OnDeclarePeace",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        private static bool Prefix(KingdomDiplomacyVM __instance, KingdomWarItemVM item)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || item == null)
                return true;

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (playerKingdom == null)
                return true;

            Kingdom opponent = item.Faction2 as Kingdom;
            if (InternalPeaceSettlementBehavior.TryIdentify(playerKingdom, opponent, out _, out _))
            {
                InternalPeaceSettlementBehavior.Current?.ShowTerms(playerKingdom, opponent);
                return false;
            }
            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return true;

            if (StorylineWarProtectionHelper.TryGetPeaceBlock(playerKingdom, opponent, out TextObject storylineReason))
            {
                InformationManager.DisplayMessage(new InformationMessage(storylineReason.ToString(), BellumNotificationColors.Warning));
                return false;
            }

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "treaty behavior unavailable";
            Action refreshDiplomacy = () => __instance?.RefreshValues();
            if (treaties != null && treaties.TryOpenPlayerParley(war, forced: false, preferWhitePeace, "player diplomacy action", refreshDiplomacy, out report))
            {
                // Returning false skips vanilla OnDeclarePeace, including its normal proposal charge.
                // Pay that same model-driven cost here only after the replacement parley opened.
                int influenceCost = Math.Max(
                    0,
                    Campaign.Current?.Models?.DiplomacyModel?.GetInfluenceCostOfProposingPeace(Clan.PlayerClan) ?? 0);
                if (influenceCost > 0)
                    ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influenceCost);

                BellumCivileLogger.Log($"Redirected diplomacy peace action into Bellum parley; war={war.WarKey}; influence_cost={influenceCost}; report={report}.");
                return false;
            }

            BellumCivileLogger.Log($"Could not redirect diplomacy peace action into Bellum parley; war={war.WarKey}; report={report}.");
            return true;
        }
    }

    /// <summary>
    /// A pending vanilla peace vote is still delivered through the normal kingdom-vote notification.
    /// Intercept its inspect action, retire the stale vote, and present the same Bellum parley instead.
    /// </summary>
    [HarmonyPatch]
    public static class KingdomPeaceVoteNotificationParleyPatch
    {
        private static readonly FieldInfo IsProposedByOpponentField = AccessTools.Field(
            typeof(MakePeaceKingdomDecision),
            "_isProposedByOpponent");

        private static MethodBase TargetMethod()
        {
            return typeof(KingdomVoteNotificationItemVM).GetMethod(
                "OnInspect",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        private static bool Prefix(KingdomDecision ____decision)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            MakePeaceKingdomDecision peaceDecision = ____decision as MakePeaceKingdomDecision;
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = peaceDecision?.FactionToMakePeaceWith as Kingdom;
            if (peaceDecision == null || playerKingdom == null || opponent == null)
                return true;

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return true;

            if (StorylineWarProtectionHelper.TryGetPeaceBlock(playerKingdom, opponent, out TextObject storylineReason))
            {
                peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
                InformationManager.DisplayMessage(new InformationMessage(storylineReason.ToString(), BellumNotificationColors.Warning));
                BellumCivileLogger.Log($"Retired a stale peace vote notification for a Story Mode protected war; war={war.WarKey}.");
                return false;
            }
            if (PeaceParleyDecisionRedirector.RetireDuringRetryWindow(peaceDecision, war)) return false;

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "treaty behavior unavailable";
            Kingdom drafter = IsOpponentProposal(peaceDecision) ? opponent : playerKingdom;
            if (treaties == null || !treaties.TryOpenPlayerParley(war, forced: false, preferWhitePeace, "vanilla peace vote notification", drafter, null, out report))
            {
                BellumCivileLogger.Log($"Could not redirect vanilla peace vote notification; war={war.WarKey}; report={report}.");
                return true;
            }

            // The offer has been translated into the Bellum treaty record. Leaving this decision alive would
            // let the vanilla vote later conclude and compete with the parley's final terms.
            peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
            BellumCivileLogger.Log($"Redirected vanilla peace vote notification into Bellum parley; war={war.WarKey}; report={report}.");
            return false;
        }

        internal static bool IsOpponentProposal(MakePeaceKingdomDecision decision)
        {
            try
            {
                return decision != null && IsProposedByOpponentField != null && (bool)IsProposedByOpponentField.GetValue(decision);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Starts Bellum's response clock as soon as vanilla creates the relevant notification. This matters
    /// when the player deliberately ignores it: vassals eventually abstain and the AI resolves, while a
    /// ruler is brought back to the parley after the configured response window.
    /// </summary>
    [HarmonyPatch]
    public static class KingdomPeaceVoteNotificationQueuedParleyPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(KingdomVoteNotificationItemVM)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(constructor => constructor.GetParameters().Length == 1);
        }

        [HarmonyPostfix]
        private static void Postfix(KingdomDecision ____decision)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            MakePeaceKingdomDecision peaceDecision = ____decision as MakePeaceKingdomDecision;
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = peaceDecision?.FactionToMakePeaceWith as Kingdom;
            if (peaceDecision == null || playerKingdom == null || opponent == null)
                return;

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return;

            if (StorylineWarProtectionHelper.IsPeaceBlocked(playerKingdom, opponent))
            {
                peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
                BellumCivileLogger.Log($"Prevented a peace vote notification from queuing during a Story Mode protected war; war={war.WarKey}.");
                return;
            }
            if (PeaceParleyDecisionRedirector.RetireDuringRetryWindow(peaceDecision, war)) return;

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            Kingdom drafter = KingdomPeaceVoteNotificationParleyPatch.IsOpponentProposal(peaceDecision) ? opponent : playerKingdom;
            if (treaties?.TryQueuePlayerParley(war, forced: false, preferWhitePeace, "vanilla peace vote notification created", drafter, out string report) == true)
            {
                // Bellum now owns resolution of this offer. Retire the parallel vanilla decision
                // immediately; the notification VM keeps enough context to open the Bellum parley.
                peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
                BellumCivileLogger.Log($"Queued Bellum parley from vanilla peace notification; war={war.WarKey}; report={report}.");
            }
        }
    }

    /// <summary>
    /// Kingdom.AddDecision dispatches its notification event before adding a player-facing decision
    /// to UnresolvedDecisions. The notification patch above can therefore queue Bellum's parley before
    /// its attempted removal can take effect. Retire the mirrored vanilla vote after AddDecision has
    /// completed so the kingdom screen cannot later force it open.
    /// </summary>
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    public static class KingdomPeaceDecisionAddedParleyCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Kingdom __instance, KingdomDecision kingdomDecision)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || !(kingdomDecision is MakePeaceKingdomDecision peaceDecision)
                || __instance == null
                || !__instance.UnresolvedDecisions.Contains(kingdomDecision))
            {
                return;
            }

            if (!PeaceParleyDecisionRedirector.TryQueueAndRetire(
                    peaceDecision,
                    deferOpen: false,
                    "vanilla peace decision added",
                    out string report))
            {
                return;
            }

            BellumCivileLogger.Log(
                $"Retired mirrored vanilla peace decision after AddDecision completed; kingdom={__instance.StringId}; report={report}.");
        }
    }

    /// <summary>
    /// Save games may already contain a mirrored peace decision created before the post-add cleanup
    /// existed. Redirect it before KingdomDecisionsVM can show its mandatory-resolution inquiry.
    /// </summary>
    [HarmonyPatch(typeof(KingdomDecisionsVM), nameof(KingdomDecisionsVM.HandleDecision))]
    public static class KingdomPeaceDecisionScreenFallbackPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(KingdomDecision curDecision)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || !(curDecision is MakePeaceKingdomDecision peaceDecision))
            {
                return true;
            }

            if (!PeaceParleyDecisionRedirector.TryQueueAndRetire(
                    peaceDecision,
                    deferOpen: true,
                    "stale vanilla peace decision reached kingdom screen",
                    out string report))
            {
                return true;
            }

            BellumCivileLogger.Log(
                $"Redirected stale vanilla peace decision before mandatory kingdom vote UI; report={report}.");
            return false;
        }
    }

    internal static class PeaceParleyDecisionRedirector
    {
        internal static bool RetireDuringRetryWindow(MakePeaceKingdomDecision decision, WarScoreRecord war)
        {
            if (decision.ProposerClan == Clan.PlayerClan || war.ParleyPending
                || Math.Abs(war.Score) >= BellumCivileConstants.WarScoreForcePeaceThreshold
                || war.CanReconsiderPeace((float)CampaignTime.Now.ToDays)) return false;
            decision.Kingdom?.RemoveDecision(decision);
            return true;
        }
        internal static bool TryQueueAndRetire(
            MakePeaceKingdomDecision peaceDecision,
            bool deferOpen,
            string reason,
            out string report)
        {
            report = "peace decision is not eligible for Bellum parley redirection";
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = peaceDecision?.FactionToMakePeaceWith as Kingdom;
            if (peaceDecision == null
                || playerKingdom == null
                || opponent == null
                || peaceDecision.Kingdom != playerKingdom)
            {
                return false;
            }

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                .GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return false;

            if (StorylineWarProtectionHelper.IsPeaceBlocked(playerKingdom, opponent))
            {
                peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
                report = "retired because the active campaign quest prevents peace";
                return true;
            }
            if (RetireDuringRetryWindow(peaceDecision, war))
            {
                report = "retired during peace reconsideration window";
                return true;
            }

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            Kingdom drafter = KingdomPeaceVoteNotificationParleyPatch.IsOpponentProposal(peaceDecision)
                ? opponent
                : playerKingdom;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            bool queued = deferOpen
                ? treaties?.TryQueueDeferredPlayerParleyOpen(
                    war,
                    forced: false,
                    preferWhitePeace,
                    reason,
                    drafter,
                    out report) == true
                : treaties?.TryQueuePlayerParley(
                    war,
                    forced: false,
                    preferWhitePeace,
                    reason,
                    drafter,
                    out report) == true;
            if (!queued)
                return false;

            peaceDecision.Kingdom?.RemoveDecision(peaceDecision);
            return true;
        }
    }
}
