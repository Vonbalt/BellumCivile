using System;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DeclareWarDecision), nameof(DeclareWarDecision.ApplyChosenOutcome))]
    internal static class DeclareWarDecisionMotiveCapturePatch
    {
        [HarmonyPrefix]
        private static void Prefix(DeclareWarDecision __instance, DecisionOutcome chosenOutcome)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || !(chosenOutcome is DeclareWarDecision.DeclareWarDecisionOutcome outcome)
                || !outcome.ShouldWarBeDeclared
                || !(__instance.FactionToDeclareWarOn is Kingdom target))
            {
                return;
            }

            ForeignPolicyBehavior foreignPolicy = Campaign.Current?
                .GetCampaignBehavior<ForeignPolicyBehavior>();
            foreignPolicy?.CapturePendingWarDeclarationMotive(
                __instance.Kingdom,
                target,
                outcome.SponsorClan ?? __instance.ProposerClan);
        }
    }

    [HarmonyPatch(typeof(DeclareWarLogEntry), nameof(DeclareWarLogEntry.GetNotificationText))]
    internal static class DeclareWarLogEntryMotivePatch
    {
        [HarmonyPostfix]
        private static void Postfix(DeclareWarLogEntry __instance, ref TextObject __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || !(__instance.Faction1 is Kingdom attacker)
                || !(__instance.Faction2 is Kingdom defender))
            {
                return;
            }

            ActiveForeignWarRecord context = Campaign.Current?
                .GetCampaignBehavior<ForeignPolicyBehavior>()?
                .GetWarDeclarationContext(attacker, defender);
            if (context == null || !context.HasPublicDeclarationMotive)
                return;

            TextObject justification = WarDeclarationReasonTextHelper.BuildPublicJustification(
                context.PublicDeclarationMotiveType,
                context.PublicDeclarationMotiveSubject);
            if (justification.IsEmpty())
                return;

            TextObject combined = new TextObject(
                "{=BC_WarDeclaration_WithJustification}{DECLARATION} {JUSTIFICATION}");
            combined.SetTextVariable("DECLARATION", __result ?? TextObject.GetEmpty());
            combined.SetTextVariable("JUSTIFICATION", justification);
            __result = combined;
        }
    }

    [HarmonyPatch(
        typeof(DeclareWarDecision.DeclareWarDecisionOutcome),
        nameof(DeclareWarDecision.DeclareWarDecisionOutcome.GetDecisionDescription))]
    internal static class DeclareWarDecisionDescriptionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            DeclareWarDecision.DeclareWarDecisionOutcome __instance,
            ref TextObject __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || !__instance.ShouldWarBeDeclared)
                return true;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            Clan proposer = __instance.SponsorClan;
            Kingdom target = __instance.FactionToDeclareWarOn as Kingdom;
            WarTargetScore assessment = behavior?
                .GetRankedTargets(proposer, forceRefresh: true)
                .FirstOrDefault(score => score.TargetKingdom == target);

            __result = WarDeclarationReasonTextHelper.Build(assessment, target);
            float hostagePenalty = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.WarDeterrence(proposer, target, council: true) ?? 0;
            if (hostagePenalty > 0)
                __result = new TextObject("{=BC_WarHostageReason}{REASON} A hostage-backed pledge of peace weighs against this war (-{PENALTY} support for this house).")
                    .SetTextVariable("REASON", __result).SetTextVariable("PENALTY", hostagePenalty.ToString("0"));
            float campaignBonus = CourtAgendaBehavior.Current?.CampaignInitiativeBonus(__instance.Kingdom, target, proposer) ?? 0;
            if (campaignBonus != 0)
                __result = new TextObject("{=BC_WarCampaignReason}{REASON} Court campaign initiative: +{BONUS} war support for this house.")
                    .SetTextVariable("REASON", __result).SetTextVariable("BONUS", campaignBonus);
            float clientageBonus = CourtAgendaBehavior.Current?.SubjugationWarBonus(__instance.Kingdom, target, proposer) ?? 0;
            if (clientageBonus != 0)
                __result = new TextObject("{=BC_WarClientageReason}{REASON} Court clientage initiative: +{BONUS} war support for this house.")
                    .SetTextVariable("REASON", __result).SetTextVariable("BONUS", clientageBonus);
            float claimBonus = CourtAgendaBehavior.Current?.ClaimWarBonus(__instance.Kingdom, target, proposer) ?? 0;
            if (claimBonus != 0)
                __result = new TextObject("{=BC_WarClaimReason}{REASON} Court claim initiative: +{BONUS} war support for this house.")
                    .SetTextVariable("REASON", __result).SetTextVariable("BONUS", claimBonus);
            return false;
        }
    }

    [HarmonyPatch(typeof(DeclareWarDecision), "DetermineSupport")]
    internal static class DeclareWarVoteAIPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            DeclareWarDecision __instance,
            Clan clan,
            DecisionOutcome possibleOutcome,
            ref float __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (behavior == null)
                return true;

            bool supportWar = possibleOutcome is DeclareWarDecision.DeclareWarDecisionOutcome outcome
                && outcome.ShouldWarBeDeclared;
            if (!behavior.TryEvaluateWarSupport(__instance, clan, supportWar, out __result))
                return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), "DetermineSupport")]
    internal static class MakePeaceVoteAIPatch
    {
        private static readonly FieldInfo IsProposedByOpponentField = AccessTools.Field(
            typeof(MakePeaceKingdomDecision),
            "_isProposedByOpponent");
        private static bool _missingOriginFieldLogged;

        [HarmonyPrefix]
        private static bool Prefix(
            MakePeaceKingdomDecision __instance,
            Clan clan,
            DecisionOutcome possibleOutcome,
            ref float __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            if (IsOpponentProposal(__instance))
                return true;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (behavior == null)
                return true;

            bool supportPeace = possibleOutcome is MakePeaceKingdomDecision.MakePeaceDecisionOutcome outcome
                && outcome.ShouldPeaceBeDeclared;
            if (!behavior.TryEvaluatePeaceSupport(__instance, clan, supportPeace, out __result))
                return true;

            return false;
        }

        private static bool IsOpponentProposal(MakePeaceKingdomDecision decision)
        {
            if (IsProposedByOpponentField == null)
            {
                if (!_missingOriginFieldLogged)
                {
                    _missingOriginFieldLogged = true;
                    BellumCivileLogger.Log(
                        "Could not locate MakePeaceKingdomDecision._isProposedByOpponent; preserving vanilla peace vote support logic.");
                }
                return true;
            }

            try
            {
                return (bool)IsProposedByOpponentField.GetValue(decision);
            }
            catch (Exception ex)
            {
                // If a future game update changes this field, preserving vanilla behavior is safer
                // than accidentally rewriting an incoming diplomatic offer as an internal motion.
                BellumCivileLogger.Log(
                    $"Could not inspect peace proposal origin; using vanilla support logic. error={ex.GetType().Name}:{ex.Message}.");
                return true;
            }
        }
    }
}
