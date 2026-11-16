using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;

namespace BellumCivile.Patches
{
    // Install after campaign UI initialization, as with the other hero-tooltip patches.
    internal static class HeroTooltipPersonalRelationPatch
    {
        private static bool _installed;

        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.hero_tooltip_relation");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(TooltipRefresherCollection), "RefreshHeroTooltip"),
                    transpiler: new HarmonyMethod(typeof(HeroTooltipPersonalRelationPatch), nameof(Transpiler)));
                _installed = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(harmony.Id);
                BellumCivileLogger.Log("Hero tooltip personal relation patch unavailable: " + ex);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var native = AccessTools.Method(typeof(Hero), nameof(Hero.GetRelationWithPlayer));
            var matches = code.Where(instruction => instruction.Calls(native)).ToList();
            if (matches.Count != 1)
            {
                BellumCivileLogger.Log($"Hero tooltip personal relation patch skipped: expected one native relation call, found {matches.Count}.");
                return code;
            }
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(HeroTooltipPersonalRelationPatch), nameof(GetDisplayedRelation));
            return code;
        }

        private static float GetDisplayedRelation(Hero hero)
        {
            Hero player = Hero.MainHero;
            if (hero == null || player == null) return 0f;
            if (DynamicRelationBehavior.Instance?.ManagesPair(player, hero) != true)
                return hero.GetRelationWithPlayer();
            return CharacterRelationManager.GetHeroRelation(player, hero);
        }
    }
}
