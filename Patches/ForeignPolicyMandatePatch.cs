using System.Collections.Generic;
using System.Reflection;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class ForeignPolicyDiplomacyButtonPatch
    {
        private const string DiplomacyVmType =
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy.KingdomDiplomacyVM";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase war = AccessTools.Method(DiplomacyVmType + ":GetIsProposingWarEnabledWithReason");
            MethodBase peace = AccessTools.Method(DiplomacyVmType + ":GetIsProposingPeaceEnabledWithReason");
            if (war != null)
                yield return war;
            if (peace != null)
                yield return peace;
        }

        [HarmonyPostfix]
        private static void Postfix(MethodBase __originalMethod, object __0, ref bool __result, ref TextObject disabledReason)
        {
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (playerKingdom == null)
                return;

            Kingdom target = ResolveTargetKingdom(__0);
            bool warAction = __originalMethod?.Name?.Contains("War") == true;
            if (clients?.IsClientKingdom(playerKingdom) == true)
            {
                if (warAction && target != null && clients.IsClientOf(playerKingdom, target))
                {
                    if (clients.TryCanProposeLiberation(Clan.PlayerClan, out _, out string liberationReason))
                    {
                        __result = true;
                        disabledReason = TextObject.GetEmpty();
                    }
                    else
                    {
                        __result = false;
                        disabledReason = new TextObject("{=BC_UI_ClientLiberationUnavailable}Your realm is not ready to fight its suzerain for liberation: {REASON}")
                            .SetTextVariable("REASON", liberationReason);
                    }
                    return;
                }

                __result = false;
                disabledReason = warAction
                    ? new TextObject("{=BC_UI_ClientWarDisabled}A client kingdom cannot declare an independent war. Only a war of liberation against its suzerain is lawful.")
                    : new TextObject("{=BC_UI_ClientPeaceDisabled}A client kingdom cannot negotiate peace independently. Its suzerain controls foreign diplomacy.");
                return;
            }

            if (!warAction || !WarPeaceRevampBehavior.IsRevampEnabled() || target == null)
                return;

            WarFrontReadinessAssessment readiness = WarFrontReadinessService.Assess(playerKingdom, target);
            if (readiness.IsAdditionalFront && !readiness.CanDeclare)
            {
                __result = false;
                disabledReason = GetSecondFrontDisabledReason(readiness);
            }
        }

        private static Kingdom ResolveTargetKingdom(object item)
        {
            if (item == null)
                return null;
            object value = AccessTools.Property(item.GetType(), "Faction2")?.GetValue(item, null)
                ?? AccessTools.Field(item.GetType(), "Faction2")?.GetValue(item)
                ?? AccessTools.Field(item.GetType(), "_faction2")?.GetValue(item);
            return value as Kingdom;
        }

        internal static TextObject GetForeignPolicyMandateDisabledReason(ForeignPolicyActionType actionType)
        {
            return actionType == ForeignPolicyActionType.War
                ? new TextObject("{=BC_UI_WarProposalDisabled}Only the ruler or a court faction leader with an active war mandate may bring a declaration of war before the council.")
                : new TextObject("{=BC_UI_PeaceProposalDisabled}Only the ruler or a court faction leader with an active peace mandate may bring a peace proposal before the council.");
        }

        internal static TextObject GetSecondFrontDisabledReason(WarFrontReadinessAssessment readiness)
        {
            if (readiness?.IsThirdOrLaterFront == true)
            {
                return new TextObject("{=BC_UI_ThirdFrontWarDisabled}The realm is already committed to {WAR_COUNT} foreign wars and cannot open another front.")
                    .SetTextVariable("WAR_COUNT", readiness.ExistingForeignWarCount);
            }

            TextObject reason = new TextObject("{=BC_UI_SecondFrontWarDisabled}The realm cannot sustain another offensive. Its strength against the combined enemy fronts is {RATIO}; at least {REQUIRED} is required.");
            reason.SetTextVariable("RATIO", readiness?.PowerRatio.ToString("0.00") ?? "0.00");
            reason.SetTextVariable("REQUIRED", BellumCivileConstants.WarPeaceRevampSecondFrontMinimumPowerRatio.ToString("0.00"));
            return reason;
        }
    }

    [HarmonyPatch]
    internal static class ForeignPolicyDiplomacyActionPatch
    {
        private const string DiplomacyVmType =
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy.KingdomDiplomacyVM";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase war = AccessTools.Method(DiplomacyVmType + ":OnDeclareWar");
            MethodBase peace = AccessTools.Method(DiplomacyVmType + ":OnDeclarePeace");
            if (war != null)
                yield return war;
            if (peace != null)
                yield return peace;
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod)
        {
            return true;
        }
    }

    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    internal static class BlockVanillaForeignPolicyDecisionPatch
    {
        public static bool Prefix(KingdomDecision kingdomDecision)
        {
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            Kingdom kingdom = kingdomDecision?.Kingdom;
            if (clients?.IsClientKingdom(kingdom) != true)
                return true;

            if (kingdomDecision is DeclareWarDecision warDecision)
            {
                Kingdom target = warDecision.FactionToDeclareWarOn as Kingdom;
                return clients.IsClientOf(kingdom, target)
                    && clients.TryCanProposeLiberation(kingdomDecision.ProposerClan, out _, out _);
            }
            if (kingdomDecision is MakePeaceKingdomDecision)
                return false;
            return true;
        }

        private static bool TryGetPlayerForeignPolicyDecision(
            KingdomDecision decision,
            out Kingdom kingdom,
            out ForeignPolicyActionType actionType)
        {
            kingdom = null;
            actionType = ForeignPolicyActionType.War;

            if (decision is DeclareWarDecision warDecision)
            {
                kingdom = warDecision.Kingdom;
                actionType = ForeignPolicyActionType.War;
            }
            else if (decision is MakePeaceKingdomDecision peaceDecision)
            {
                kingdom = peaceDecision.Kingdom;
                actionType = ForeignPolicyActionType.Peace;
            }

            return kingdom != null
                && kingdom == Clan.PlayerClan?.Kingdom
                && decision.ProposerClan == Clan.PlayerClan;
        }
    }

    [HarmonyPatch(typeof(DeclareWarDecision), nameof(DeclareWarDecision.IsAllowed))]
    internal static class ClientLiberationDecisionAvailabilityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(DeclareWarDecision __instance, ref bool __result)
        {
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            Kingdom client = __instance?.Kingdom;
            Kingdom target = __instance?.FactionToDeclareWarOn as Kingdom;
            if (clients?.IsClientOf(client, target) == true)
            {
                __result = clients.TryCanProposeLiberation(__instance.ProposerClan, out _, out _);
                return;
            }

            if (!__result || !WarPeaceRevampBehavior.IsRevampEnabled() || client == null || target == null)
                return;

            WarFrontReadinessAssessment readiness = WarFrontReadinessService.Assess(client, target);
            if (readiness.IsAdditionalFront && !readiness.CanDeclare)
                __result = false;
        }
    }
}
