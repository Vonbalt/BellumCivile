using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation.Tags;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    internal static class VassalLordshipPatches
    {
        private static bool _installed;
        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.lordship.vassal");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(LordConversationsCampaignBehavior), "conversation_player_is_accepted_as_a_vassal_on_consequence"),
                    prefix: new HarmonyMethod(typeof(VassalLordshipPatches), nameof(BeforeOath)),
                    postfix: new HarmonyMethod(typeof(VassalLordshipPatches), nameof(AfterOath)));
                harmony.Patch(AccessTools.Method(typeof(UnderCommandTag), nameof(UnderCommandTag.IsApplicableTo)),
                    postfix: new HarmonyMethod(typeof(VassalLordshipPatches), nameof(RecognizeLord)));
                harmony.Patch(AccessTools.Method(typeof(PlayerTownVisitCampaignBehavior), "SetIntroductionText"),
                    transpiler: new HarmonyMethod(typeof(VassalLordshipPatches), nameof(VillageIntroduction)));
                var villagers = AccessTools.TypeByName("SandBox.CampaignBehaviors.CommonVillagersCampaignBehavior");
                harmony.Patch(AccessTools.Method(villagers, "conversation_liege_delivered_line_on_street_on_condition"),
                    postfix: new HarmonyMethod(typeof(VassalLordshipPatches), nameof(RecognizeVillageLord)));
                _installed = true;
            }
            catch { harmony.UnpatchAll(harmony.Id); throw; }
        }

        private static void BeforeOath(out LordshipBehavior.VassalOath __state)
            => __state = LordshipBehavior.Instance?.CaptureVassalOath();
        private static void AfterOath(LordshipBehavior.VassalOath __state) => LordshipBehavior.Instance?.CompleteVassalOath(__state);

        private static void RecognizeLord(CharacterObject character, ref bool __result)
        {
            if (!__result && character?.IsHero == true && character.HeroObject.Spouse != Hero.MainHero
                && LordshipBehavior.Instance?.RecognizesPlayerLord(character.HeroObject) == true) __result = true;
        }

        private static void RecognizeVillageLord(object __instance, ref bool __result)
        {
            if (!__result && LordshipBehavior.Instance?.IsPlayerLordship(Settlement.CurrentSettlement) == true)
                __result = (bool)AccessTools.Method(__instance.GetType(), "conversation_already_delivered_line_on_street_on_condition").Invoke(__instance, null);
        }

        internal static string VillageIntroductionFormat(string original, Settlement settlement)
            => settlement?.IsVillage == true && LordshipBehavior.Instance?.IsPlayerLordship(settlement) == true
                ? "{=M5iR1e5h}You have arrived at your fief of {SETTLEMENT_LINK}. {PROSPERITY_INFO}" : original;

        private static IEnumerable<CodeInstruction> VillageIntroduction(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text && text.StartsWith("{=RVDojUOM}", StringComparison.Ordinal))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VassalLordshipPatches), nameof(VillageIntroductionFormat)));
                    matched++;
                }
            }
            if (matched != 1) throw new InvalidOperationException("Lordship village introduction contract changed.");
        }
    }
}
