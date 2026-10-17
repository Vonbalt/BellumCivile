using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.SiegeCompleted))]
    public static class SiegeCaptureContextPatch
    {
        private sealed class Capture
        {
            internal Settlement Settlement;
            internal MobileParty Party;
            internal Capture Previous;
        }

        [ThreadStatic] private static Capture _current;

        [HarmonyPrefix]
        private static void Prefix(Settlement settlement, MobileParty capturerParty, out Capture __state)
        {
            __state = new Capture { Settlement = settlement, Party = capturerParty, Previous = _current };
            _current = __state;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Capture __state)
        {
            if (__state != null) _current = __state.Previous;
            return __exception;
        }

        internal static PartyBase GetCapturingParty(Settlement settlement) =>
            settlement != null && _current?.Settlement == settlement ? _current.Party?.Party : null;
    }

    [HarmonyPatch(typeof(ChangeOwnerOfSettlementAction), "ApplyInternal")]
    public static class SiegeGarrisonOwnershipPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var heroParty = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.PartyBelongedTo));
            var party = AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.Party));
            var town = AccessTools.Field(typeof(Settlement), nameof(Settlement.Town));
            var garrison = AccessTools.PropertyGetter(typeof(Fief), nameof(Fief.GarrisonParty));
            var destroy = AccessTools.Method(typeof(DestroyPartyAction), nameof(DestroyPartyAction.Apply));
            var matches = new List<int>();
            for (int i = 1; i + 5 < codes.Count; i++)
            {
                if (codes[i - 1].opcode == OpCodes.Ldarg_2 && codes[i].Calls(heroParty)
                    && codes[i + 1].Calls(party) && codes[i + 2].opcode == OpCodes.Ldarg_0
                    && codes[i + 3].LoadsField(town) && codes[i + 4].Calls(garrison)
                    && codes[i + 5].Calls(destroy))
                    matches.Add(i);
            }
            if (matches.Count != 1 || codes.Count(c => c.Calls(destroy)) != 1)
                throw new InvalidOperationException("Siege ownership guard: native garrison cleanup anchors changed.");

            // Replace only the unsafe party lookup, retaining native transfer order and hero attribution.
            int index = matches[0];
            codes[index].opcode = OpCodes.Ldarg_0;
            codes[index].operand = null;
            codes[index + 1].opcode = OpCodes.Call;
            codes[index + 1].operand = AccessTools.Method(typeof(SiegeGarrisonOwnershipPatch), nameof(ResolveDestroyer));
            return codes;
        }

        public static PartyBase ResolveDestroyer(Hero capturerHero, Settlement settlement)
        {
            // Native destruction also supports an unknown destroyer (for example, party disbanding).
            return capturerHero?.PartyBelongedTo?.Party ?? SiegeCaptureContextPatch.GetCapturingParty(settlement);
        }
    }
}
