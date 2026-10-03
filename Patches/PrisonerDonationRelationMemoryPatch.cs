using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CharacterRelationCampaignBehavior), "OnPrisonerDonatedToSettlement")]
    internal static class PrisonerDonationRelationMemoryPatch
    {
        private static void Prefix(out IDisposable __state)
        {
            __state = RelationMemoryService.Begin(RelationMemorySources.DeliveredNoblePrisoners,
                BellumCivileConstants.PrisonerDonationMemoryYears, RelationMemoryScope.House);
        }

        private static void Postfix(IDisposable __state) => __state?.Dispose();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
    internal static class PrisonerDonationFinalGainPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var rounding = AccessTools.Method(typeof(MBRandom), nameof(MBRandom.RoundRandomized), new[] { typeof(float) });
            var limiter = AccessTools.Method(typeof(PrisonerDonationFinalGainPatch), nameof(LimitGain));
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(rounding))
                {
                    // Leave vanilla bonuses, rounding, notifications and relation events intact.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, limiter);
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException("Prisoner donation cap expected one rounded relation gain in ChangeRelationAction.ApplyInternal.");
        }

        private static int LimitGain(int gain, Hero firstHero, Hero secondHero)
        {
            if (gain <= 0 || RelationMemoryService.CurrentDescriptor?.SourceId != RelationMemorySources.DeliveredNoblePrisoners
                || DynamicRelationBehavior.Instance == null || !BellumCivileOptions.EnableDynamicRelationDrift)
                return gain;

            Campaign.Current.Models.DiplomacyModel.GetHeroesForEffectiveRelation(firstHero, secondHero,
                out Hero effectiveFirst, out Hero effectiveSecond);
            return DynamicRelationBehavior.Instance.LimitPrisonerDonationGain(effectiveFirst, effectiveSecond, gain);
        }
    }
}
