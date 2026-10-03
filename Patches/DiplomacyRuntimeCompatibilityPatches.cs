using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class DiplomacyRuntimeCompatibilityHelper
    {
        internal static bool TryResolveKingdomPair(object instance, out Kingdom first, out Kingdom second)
        {
            first = null;
            second = null;
            if (instance == null)
                return false;

            Type type = instance.GetType();
            first = AccessTools.Field(type, "_faction1")?.GetValue(instance) as Kingdom;
            second = AccessTools.Field(type, "_faction2")?.GetValue(instance) as Kingdom;
            return first != null && second != null;
        }

        internal static Kingdom GetOpponent(Kingdom first, Kingdom second, Kingdom playerKingdom)
        {
            if (playerKingdom == first)
                return second;
            if (playerKingdom == second)
                return first;
            return null;
        }

        internal static void Refresh(object instance)
        {
            try
            {
                AccessTools.Method(instance?.GetType(), "OnRefresh")?.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Could not refresh Diplomacy action after Bellum reroute: {ex.GetType().Name}:{ex.Message}");
            }
        }

        internal static void ShowUnavailable(TextObject reason)
        {
            if (reason != null)
                InformationManager.DisplayMessage(new InformationMessage(reason.ToString(), BellumNotificationColors.Warning));
        }
    }

    /// <summary>
    /// Diplomacy's peace action applies costs, fief repatriation, and realm elimination before it
    /// reaches vanilla MakePeaceAction. Stop it at the entry point whenever Bellum owns the active
    /// conflict, leaving Diplomacy civil wars from older saves on their own resolution path.
    /// </summary>
    [HarmonyPatch]
    internal static class DiplomacyKingdomPeaceActionCompatibilityPatch
    {
        private const string ActionTypeName = "Diplomacy.DiplomaticAction.WarPeace.KingdomPeaceAction";
        private static readonly HashSet<string> ReportedWarKeys = new HashSet<string>();
        private static Type _actionType;

        private static bool Prepare()
        {
            _actionType = AccessTools.TypeByName(ActionTypeName);
            return _actionType != null && AccessTools.Method(_actionType, "ApplyPeace") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_actionType, "ApplyPeace");
        }

        [HarmonyPrefix]
        private static bool Prefix(Kingdom kingdomMakingPeace, Kingdom otherKingdom)
        {
            if (!CivilWarResolutionBehavior.IsPeaceHandlingSuppressed && !ClaimFeudWarBehavior.IsPeaceHandlingSuppressed
                && InternalPeaceSettlementBehavior.TryIdentify(kingdomMakingPeace, otherKingdom, out _, out bool rival))
            {
                if (!WarPeaceRevampBehavior.IsRevampEnabled() && !rival)
                    InternalPeaceSettlementBehavior.Current?.QueueWhitePeace(kingdomMakingPeace, otherKingdom);
                return false;
            }
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || kingdomMakingPeace == null
                || otherKingdom == null)
            {
                return true;
            }

            // Existing Diplomacy civil wars must retain Diplomacy's own peace/consolidation path.
            // New Diplomacy rebellions are suppressed below, but this also protects mid-campaign
            // installs and saves which already contain a Diplomacy rebel kingdom.
            if (ModIntegrationHelper.IsDiplomacyRebelKingdom(kingdomMakingPeace)
                || ModIntegrationHelper.IsDiplomacyRebelKingdom(otherKingdom))
            {
                return true;
            }

            WarScoreRecord war = Campaign.Current?
                .GetCampaignBehavior<WarScoreBehavior>()?
                .GetActiveWar(kingdomMakingPeace, otherKingdom);
            if (war == null)
                return true;

            if (ReportedWarKeys.Add(war.WarKey ?? string.Empty))
            {
                BellumCivileLogger.Log(
                    $"Blocked Diplomacy peace resolution for Bellum-owned conflict; war={war.WarKey}; kind={war.ConflictType}.");
            }
            return false;
        }
    }

    /// <summary>
    /// Redirect Diplomacy's optional direct-peace button into the same cached treaty/parley path as
    /// the vanilla diplomacy tab. No Diplomacy cost or fief return is allowed to run first.
    /// </summary>
    [HarmonyPatch]
    internal static class DiplomacyDirectPeaceCompatibilityPatch
    {
        private const string MixinTypeName = "Diplomacy.ViewModelMixin.KingdomWarItemVMMixin";
        private static Type _mixinType;

        private static bool Prepare()
        {
            _mixinType = AccessTools.TypeByName(MixinTypeName);
            return _mixinType != null && AccessTools.Method(_mixinType, "ExecuteDirectAction") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_mixinType, "ExecuteDirectAction");
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            if (!DiplomacyRuntimeCompatibilityHelper.TryResolveKingdomPair(__instance, out Kingdom first, out Kingdom second))
            {
                return true;
            }

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = DiplomacyRuntimeCompatibilityHelper.GetOpponent(first, second, playerKingdom);
            if (playerKingdom == null || opponent == null)
                return true;

            if (InternalPeaceSettlementBehavior.TryIdentify(playerKingdom, opponent, out _, out _))
            {
                InternalPeaceSettlementBehavior.Current?.ShowTerms(playerKingdom, opponent);
                return false;
            }

            if (!WarPeaceRevampBehavior.IsRevampEnabled()) return true;

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(playerKingdom, opponent);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
            {
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(new TextObject(
                    "{=BC_DiplomacyCompat_ParleyUnavailable}Bellum could not find an active foreign conflict for this peace proposal."));
                return false;
            }

            int influenceCost = Math.Max(
                0,
                Campaign.Current?.Models?.DiplomacyModel?.GetInfluenceCostOfProposingPeace(Clan.PlayerClan) ?? 0);
            if (Clan.PlayerClan == null || Clan.PlayerClan.Influence < influenceCost)
            {
                TextObject reason = new TextObject(
                    "{=BC_DiplomacyCompat_InfluenceRequired}You need {INFLUENCE} influence to bring this proposal before the realm.");
                reason.SetTextVariable("INFLUENCE", influenceCost);
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(reason);
                return false;
            }

            bool preferWhitePeace = Math.Abs(war.Score) <= BellumCivileConstants.WarScoreWhitePeaceMaximumScore;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            Action refresh = () => DiplomacyRuntimeCompatibilityHelper.Refresh(__instance);
            if (treaties != null
                && treaties.TryOpenPlayerParley(
                    war,
                    forced: false,
                    preferWhitePeace,
                    "Diplomacy direct peace action",
                    refresh,
                    out string report))
            {
                if (influenceCost > 0)
                    ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influenceCost);
                BellumCivileLogger.Log(
                    $"Redirected Diplomacy direct peace action into Bellum parley; war={war.WarKey}; influence_cost={influenceCost}; report={report}.");
                return false;
            }

            DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(new TextObject(
                "{=BC_DiplomacyCompat_ParleyUnavailable}Bellum could not find an active foreign conflict for this peace proposal."));
            return false;
        }
    }

    /// <summary>
    /// Diplomacy's optional direct-war button bypasses the kingdom council. Convert it into a normal
    /// DeclareWarDecision so Bellum's War Will voting and influence commitments remain authoritative.
    /// </summary>
    [HarmonyPatch]
    internal static class DiplomacyDirectWarCompatibilityPatch
    {
        private const string MixinTypeName = "Diplomacy.ViewModelMixin.KingdomTruceItemVMMixin";
        private static Type _mixinType;

        private static bool Prepare()
        {
            _mixinType = AccessTools.TypeByName(MixinTypeName);
            return _mixinType != null && AccessTools.Method(_mixinType, "ExecuteDirectAction") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_mixinType, "ExecuteDirectAction");
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || !DiplomacyRuntimeCompatibilityHelper.TryResolveKingdomPair(__instance, out Kingdom first, out Kingdom second))
            {
                return true;
            }

            IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (alliances?.IsAllyWithKingdom(first, second) == true)
                return true;

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = DiplomacyRuntimeCompatibilityHelper.GetOpponent(first, second, playerKingdom);
            if (playerKingdom == null || opponent == null || Clan.PlayerClan == null)
                return true;

            WarFrontReadinessAssessment readiness = WarFrontReadinessService.Assess(playerKingdom, opponent);
            if (readiness.IsAdditionalFront && !readiness.CanDeclare)
            {
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(
                    ForeignPolicyDiplomacyButtonPatch.GetSecondFrontDisabledReason(readiness));
                return false;
            }

            DeclareWarDecision decision = new DeclareWarDecision(Clan.PlayerClan, opponent);
            if (!decision.CanMakeDecision(out TextObject reason))
            {
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(reason);
                return false;
            }

            int influenceCost = Math.Max(
                0,
                Campaign.Current?.Models?.DiplomacyModel?.GetInfluenceCostOfProposingWar(Clan.PlayerClan) ?? 0);
            if (Clan.PlayerClan.Influence < influenceCost)
            {
                TextObject costReason = new TextObject(
                    "{=BC_DiplomacyCompat_InfluenceRequired}You need {INFLUENCE} influence to bring this proposal before the realm.");
                costReason.SetTextVariable("INFLUENCE", influenceCost);
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(costReason);
                return false;
            }

            playerKingdom.AddDecision(decision, false);
            DiplomacyRuntimeCompatibilityHelper.Refresh(__instance);
            BellumCivileLogger.Log(
                $"Redirected Diplomacy direct war action into Bellum council vote; kingdom={playerKingdom.StringId}; target={opponent.StringId}; influence_cost={influenceCost}.");
            return false;
        }
    }

    /// <summary>
    /// Bellum owns internal factions and their temporary kingdoms. Block only Diplomacy's creation,
    /// joining, and escalation actions; existing Diplomacy records may still leave, expire, and
    /// resolve, which keeps older saves recoverable.
    /// </summary>
    [HarmonyPatch]
    internal static class DiplomacyCivilWarCreationCompatibilityPatch
    {
        private static readonly HashSet<string> ReportedActions = new HashSet<string>();

        private static bool Prepare()
        {
            return ModIntegrationHelper.IsDiplomacyLoaded;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            string[] typeNames =
            {
                "Diplomacy.CivilWar.Actions.CreateFactionAction",
                "Diplomacy.CivilWar.Actions.JoinFactionAction",
                "Diplomacy.CivilWar.Actions.StartRebellionAction"
            };

            foreach (string typeName in typeNames)
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                    continue;

                MethodBase method = AccessTools.Method(type, "Apply");
                if (method != null)
                    yield return method;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod)
        {
            if (Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>() == null)
                return true;

            string action = __originalMethod?.DeclaringType?.Name ?? __originalMethod?.Name ?? "unknown";
            if (ReportedActions.Add(action))
                BellumCivileLogger.Log($"Suppressed Diplomacy civil-war action '{action}'; Bellum controls internal factions.");
            return false;
        }
    }

    /// <summary>
    /// Client kingdoms surrender independent diplomacy. Diplomacy non-aggression pacts are not part
    /// of Bannerlord's alliance/trade behaviors, so guard their optional reflected action separately.
    /// </summary>
    [HarmonyPatch]
    internal static class DiplomacyClientNonAggressionPactCompatibilityPatch
    {
        private const string ActionTypeName = "Diplomacy.DiplomaticAction.NonAggressionPact.FormNonAggressionPactAction";
        private static Type _actionType;

        private static bool Prepare()
        {
            return ModIntegrationHelper.IsDiplomacyLoaded;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            _actionType = AccessTools.TypeByName(ActionTypeName);
            if (_actionType == null)
                yield break;

            // Both overrides are suppressed: AssessCosts prevents a blocked pact from charging the
            // proposer, while ApplyInternal prevents registration and notifications. Targeting the
            // concrete overrides avoids Harmony's shared-generic behavior on AbstractDiplomaticAction.
            MethodBase assessCosts = AccessTools.Method(_actionType, "AssessCosts");
            MethodBase applyInternal = AccessTools.Method(_actionType, "ApplyInternal");
            if (assessCosts != null)
                yield return assessCosts;
            if (applyInternal != null)
                yield return applyInternal;
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod, Kingdom proposingKingdom, Kingdom otherKingdom)
        {
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (clients == null
                || (!clients.IsClientKingdom(proposingKingdom) && !clients.IsClientKingdom(otherKingdom)))
            {
                return true;
            }

            if (__originalMethod?.Name == "ApplyInternal"
                && WarPeaceRevampBehavior.IsPlayerPoliticalParticipant(proposingKingdom, otherKingdom))
            {
                DiplomacyRuntimeCompatibilityHelper.ShowUnavailable(new TextObject(
                    "{=BC_DiplomacyCompat_ClientPactBlocked}A client kingdom cannot form an independent non-aggression pact."));
            }
            if (__originalMethod?.Name == "ApplyInternal")
            {
                BellumCivileLogger.Log(
                    $"Blocked Diplomacy non-aggression pact involving a client kingdom; first={proposingKingdom?.StringId}; second={otherKingdom?.StringId}.");
            }
            return false;
        }
    }
}
