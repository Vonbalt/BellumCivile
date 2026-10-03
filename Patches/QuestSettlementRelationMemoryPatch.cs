using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Issues;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(IssuesCampaignBehavior), "OnIssueUpdated")]
    internal static class QuestResultRelationMemoryPatch
    {
        private static void Prefix(IssueBase.IssueUpdateDetails details, out IDisposable __state)
        {
            bool hasResult = details == IssueBase.IssueUpdateDetails.IssueFinishedWithSuccess
                || details == IssueBase.IssueUpdateDetails.IssueFail
                || details == IssueBase.IssueUpdateDetails.IssueTimedOut
                || details == IssueBase.IssueUpdateDetails.IssueFinishedWithBetrayal
                || details == IssueBase.IssueUpdateDetails.SentTroopsFinishedQuest
                || details == IssueBase.IssueUpdateDetails.SentTroopsFailedQuest;
            __state = RelationMemoryService.BeginNativeLabels(hasResult ? RelationMemorySources.FulfilledRequest : null,
                !hasResult ? null :
                details == IssueBase.IssueUpdateDetails.IssueFinishedWithBetrayal
                    ? RelationMemorySources.BetrayedTrust : RelationMemorySources.FailedRequest);
        }
        private static void Postfix(IDisposable __state) => __state?.Dispose();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    [HarmonyPatch]
    internal static class GrainCommunityRelationMemoryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(HeadmanNeedsGrainIssueBehavior.HeadmanNeedsGrainIssueQuest), "Success");
            yield return AccessTools.Method(typeof(HeadmanNeedsGrainIssueBehavior.HeadmanNeedsGrainIssueQuest), "TimeoutFail");
            yield return AccessTools.Method(typeof(HeadmanNeedsGrainIssueBehavior.HeadmanNeedsGrainIssue), "AlternativeSolutionEndWithSuccessConsequence");
            yield return AccessTools.Method(typeof(HeadmanNeedsGrainIssueBehavior.HeadmanNeedsGrainIssue), "AlternativeSolutionEndWithFailureConsequence");
        }
        private static void Prefix(out IDisposable __state) => __state = RelationMemoryService.BeginNativeLabels(
            RelationMemorySources.HelpedCommunity, RelationMemorySources.FailedCommunity);
        private static void Postfix(IDisposable __state) => __state?.Dispose();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    [HarmonyPatch(typeof(CharacterRelationCampaignBehavior), "OnRaidCompleted")]
    internal static class RaidRelationMemoryPatch
    {
        private static void Prefix(out IDisposable __state) => __state = RelationMemoryService.BeginNativeLabels(
            null, RelationMemorySources.RaidedLands);
        private static void Postfix(IDisposable __state) => __state?.Dispose();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    [HarmonyPatch(typeof(CharacterRelationCampaignBehavior), "DailyTick")]
    internal static class SettlementDailyRelationMemoryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
            var wrapper = AccessTools.Method(typeof(SettlementDailyRelationMemoryPatch), nameof(ApplySettlementRelation));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                // Only these three calls concern security/loyalty. Leave the daily charm perk untouched.
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = wrapper; matches++; }
                yield return instruction;
            }
            if (matches != 3) throw new InvalidOperationException("Expected three daily settlement relation calls.");
        }

        private static void ApplySettlementRelation(Hero first, Hero second, int change, bool showQuickNotification)
        {
            string positive = second?.HomeSettlement?.IsVillage == true
                ? RelationMemorySources.LocalGoodwill : RelationMemorySources.KeptPeace;
            using (RelationMemoryService.BeginNativeLabels(positive, RelationMemorySources.AllowedLawlessness))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second, change, showQuickNotification);
        }
    }
}
