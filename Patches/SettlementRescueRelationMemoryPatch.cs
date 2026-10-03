using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    // Rewrite only audited native relation calls, leaving event subscribers and other rewards outside the label scope.
    internal static class NativeRelationCallLabels
    {
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, string[] positives, string[] negatives)
        {
            if (positives.Length != negatives.Length) throw new ArgumentException("Relation label maps must have equal lengths.");
            var native = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
            var wrapper = AccessTools.Method(typeof(NativeRelationCallLabels), nameof(Apply));
            int index = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    if (index >= positives.Length) throw new InvalidOperationException("Unexpected additional native relation call.");
                    string positive = positives[index];
                    string negative = negatives[index++];
                    if (positive != null || negative != null)
                    {
                        var label = positive == null ? new CodeInstruction(OpCodes.Ldnull) : new CodeInstruction(OpCodes.Ldstr, positive);
                        label.MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                        yield return label;
                        yield return negative == null ? new CodeInstruction(OpCodes.Ldnull) : new CodeInstruction(OpCodes.Ldstr, negative);
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = wrapper;
                    }
                }
                yield return instruction;
            }
            if (index != positives.Length) throw new InvalidOperationException("Expected native relation call was not found.");
        }

        private static void Apply(Hero first, Hero second, int change, bool showQuickNotification, string positive, string negative)
        {
            using (RelationMemoryService.BeginNativeLabels(positive, negative))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second, change, showQuickNotification);
        }
    }

    [HarmonyPatch(typeof(CharacterRelationCampaignBehavior), "MapEventEnded")]
    internal static class SettlementRescueRelationMemoryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => NativeRelationCallLabels.Rewrite(
            instructions,
            // The first two calls are Oratory/Warlord perk rewards, not settlement rescues.
            new[] { null, null, RelationMemorySources.ProtectedSettlement, RelationMemorySources.ProtectedVillagers,
                RelationMemorySources.ProtectedVillagers, RelationMemorySources.ProtectedCaravan }, new string[6]);
    }

    [HarmonyPatch(typeof(SiegeAftermathCampaignBehavior), "OnSiegeAftermathApplied")]
    internal static class SiegeAftermathRelationMemoryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
            var wrapper = AccessTools.Method(typeof(SiegeAftermathRelationMemoryPatch), nameof(Apply));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_3).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = wrapper;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Expected one relation penalty with the former settlement owner.");
        }

        private static void Apply(Hero first, Hero second, int change, bool showQuickNotification, SiegeAftermathAction.SiegeAftermath aftermathType)
        {
            string negative = aftermathType == SiegeAftermathAction.SiegeAftermath.Devastate ? RelationMemorySources.DevastatedSettlement
                : aftermathType == SiegeAftermathAction.SiegeAftermath.Pillage ? RelationMemorySources.PlunderedSettlement : null;
            using (RelationMemoryService.BeginNativeLabels(null, negative))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second, change, showQuickNotification);
        }
    }

    [HarmonyPatch(typeof(SandBox.Issues.NotableWantsDaughterFoundIssueBehavior.NotableWantsDaughterFoundIssueQuest), "ApplyDeliveryFailedDueToDuelLostConsequences")]
    internal static class DaughterQuestRelationMemoryPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => NativeRelationCallLabels.Rewrite(
            instructions, new string[1], new[] { RelationMemorySources.InterferedInAffairs });
    }
}
