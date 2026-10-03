using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class MarriageRelationMemoryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MarriageAction), "ApplyInternal");
            yield return AccessTools.Method(typeof(CharacterRelationCampaignBehavior), "OnHeroesMarried");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
            var wrapper = AccessTools.Method(typeof(MarriageRelationMemoryPatch), nameof(ApplyMarriageRelation));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                // Scope only the native gain, not other mods' marriage/clan-change event subscribers.
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = wrapper; matches++; }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Expected one native marriage relation call per method.");
        }

        private static void ApplyMarriageRelation(Hero first, Hero second, int change, bool showQuickNotification)
        {
            using (RelationMemoryService.BeginNativeLabels(RelationMemorySources.CelebratedMarriage, null))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second, change, showQuickNotification);
        }
    }
}
