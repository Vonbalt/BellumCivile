using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DefaultEncyclopediaConceptPage), "InitializeFilterItems")]
    internal static class EncyclopediaConceptFilterPatch
    {
        private static readonly Predicate<object> Matches = item =>
            item is Concept concept && concept.FilterGroup == "BellumCivile";

        private static void Postfix(ref IEnumerable<EncyclopediaFilterGroup> __result)
        {
            var groups = __result?.ToList();
            // Keep this in Types: a separate filter group would intersect with native types.
            var types = groups?.FirstOrDefault(group => group?.Name?.GetID() == "tBx7XXps");
            if (types?.Filters == null) return;
            if (!types.Filters.Any(filter => filter?.Name?.GetID() == "BC_Concept_Filter"))
                types.Filters.Add(new EncyclopediaFilterItem(
                    new TextObject("{=BC_Concept_Filter}Bellum Civile"), Matches));
            __result = groups;
        }
    }
}
