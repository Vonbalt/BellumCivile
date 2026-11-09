using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(InitialChildGenerationCampaignBehavior), "OnNewGameCreatedPartialFollowUp")]
    internal static class InitialChildGenerationAgePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var random = AccessTools.Method(typeof(MBRandom), nameof(MBRandom.RandomInt), new[] { typeof(int), typeof(int) });
            var create = AccessTools.Method(typeof(HeroCreator), nameof(HeroCreator.CreateChild));
            var matches = Enumerable.Range(1, Math.Max(0, code.Count - 2)).Where(i =>
                code[i - 1].LoadsConstant(2) && code[i].LoadsConstant(18) && code[i + 1].Calls(random)).ToArray();
            if (matches.Length != 1 || code.Count(c => c.Calls(create)) != 1)
                throw new InvalidOperationException("Starting-child age hook: native age roll or child-creation anchor changed.");

            // Replace only the exclusive age ceiling, preserving labels and exception blocks.
            code[matches[0]].opcode = OpCodes.Call;
            code[matches[0]].operand = AccessTools.Method(typeof(InitialChildGenerationAgePatch), nameof(MaximumAge));
            return code;
        }

        private static int MaximumAge()
        {
            if (!BellumCivileOptions.EnableCustomAdulthoodAge) return 18;
            return Math.Max(3, Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? BellumCivileOptions.AdulthoodAge);
        }
    }
}
