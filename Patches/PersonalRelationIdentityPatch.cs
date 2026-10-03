using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
    internal static class PersonalRelationIdentityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(DiplomacyModel), nameof(DiplomacyModel.GetHeroesForEffectiveRelation));
            var replacement = AccessTools.Method(typeof(PersonalRelationIdentityPatch), nameof(ResolvePair));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    // Retain native gain modifiers, clamping, the write and event dispatch.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1)
                throw new InvalidOperationException("Personal relation identity expected one effective-pair lookup in ChangeRelationAction.ApplyInternal.");
        }

        private static void ResolvePair(DiplomacyModel model, Hero first, Hero second, out Hero effectiveFirst, out Hero effectiveSecond)
        {
            if (RelationMemoryService.UsesOriginalPersonalPair(first, second))
            {
                effectiveFirst = first;
                effectiveSecond = second;
                return;
            }
            model.GetHeroesForEffectiveRelation(first, second, out effectiveFirst, out effectiveSecond);
        }
    }
}
