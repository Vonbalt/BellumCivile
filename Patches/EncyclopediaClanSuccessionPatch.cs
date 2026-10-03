using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EncyclopediaClanPageVM), "Refresh")]
    public static class EncyclopediaClanSuccessionPatch
    {
        private const string HeirDefinitionText = "{=BC_ClanHeir_Label}Heir:";

        [HarmonyPostfix]
        public static void Postfix(EncyclopediaClanPageVM __instance)
        {
            Clan clan = __instance?.Obj as Clan;
            if (clan == null || __instance.ClanInfo == null)
                return;

            Hero firstHeir = SuccessionLawHelper.GetVisibleSuccessionLine(clan).FirstOrDefault();
            string heirName = firstHeir?.Name?.ToString()
                ?? new TextObject("{=BC_SuccessionLine_NoValidHeir}No valid heir").ToString();
            AddLawRow(
                __instance,
                new TextObject(HeirDefinitionText).ToString(),
                heirName,
                new BasicTooltipViewModel(() => SuccessionLawHelper.BuildSuccessionLineTooltipProperties(clan)));
        }

        private static void AddLawRow(
            EncyclopediaClanPageVM page,
            string definition,
            string value,
            BasicTooltipViewModel hint)
        {
            if (page.ClanInfo.Any(item => item != null && item.Definition == definition))
                return;

            page.ClanInfo.Add(new StringPairItemVM(definition, value, hint));
        }
    }
}
