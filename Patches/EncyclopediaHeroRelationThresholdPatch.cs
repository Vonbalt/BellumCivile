using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), "Refresh")]
    public static class EncyclopediaHeroRelationThresholdPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EncyclopediaHeroPageVM __instance)
        {
            Hero viewedHero = __instance?.Obj as Hero;
            DynamicRelationBehavior behavior = DynamicRelationBehavior.Instance;
            if (behavior == null || viewedHero == null || __instance.Stats == null || __instance.IsInformationHidden)
                return;

            if (!behavior.TryBuildEncyclopediaThresholdTooltip(viewedHero, out int currentRelation, out var tooltip))
                return;

            string definition = GameTexts.FindText("str_enc_sf_relation").ToString();
            int relationIndex = -1;
            for (int i = 0; i < __instance.Stats.Count; i++)
            {
                if (string.Equals(__instance.Stats[i]?.Definition, definition, StringComparison.Ordinal))
                {
                    relationIndex = i;
                    break;
                }
            }

            if (relationIndex < 0)
                return;

            __instance.Stats.RemoveAt(relationIndex);
            __instance.Stats.Insert(relationIndex, new StringPairItemVM(
                definition,
                currentRelation.ToString(),
                new BasicTooltipViewModel(() => tooltip)));
        }
    }
}
