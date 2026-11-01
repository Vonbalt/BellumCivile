using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(MarriageAction), "HandleClanChangeAfterMarriageForHero")]
    internal static class CompanionMarriageTransferPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            var setter = AccessTools.PropertySetter(typeof(Hero), nameof(Hero.Clan));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CompanionMarriageTransferPatch), nameof(SetWeddingClan));
                    matched++;
                }
                yield return instruction;
            }
            if (matched != 1) throw new InvalidOperationException("Companion wedding clan-transfer contract changed.");
        }

        private static void SetWeddingClan(Hero hero, Clan destination)
        {
            if (!hero.IsWanderer || hero.CompanionOf == null) { hero.Clan = destination; return; }
            Clan old = hero.Clan;
            if (old == destination) return;
            // Keep native companion rosters in sync without also inserting a wanderer into the lord roster.
            hero.CompanionOf = destination;
            CampaignEventDispatcher.Instance.OnHeroChangedClan(hero, old);
        }
    }
}
