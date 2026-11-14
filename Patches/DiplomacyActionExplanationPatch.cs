using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDiplomacyProposalActionItemVM), nameof(KingdomDiplomacyProposalActionItemVM.RefreshValues))]
    internal static class DiplomacyActionExplanationPatch
    {
        private static void Postfix(KingdomDiplomacyProposalActionItemVM __instance)
        {
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || __instance == null || string.IsNullOrEmpty(__instance.Explanation)) return;
            var labels = new List<string>();
            foreach (KingdomElection.ElectionOutcomeSupport support in Enum.GetValues(typeof(KingdomElection.ElectionOutcomeSupport)))
            {
                string label = GameTexts.FindText("str_decision_outcome_support_status", support.ToString())?.ToString();
                if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
            }
            __instance.Explanation = Format(__instance.Explanation, labels);
        }

        internal static string Format(string explanation, IEnumerable<string> labels)
        {
            if (string.IsNullOrEmpty(explanation) || labels == null) return explanation;
            foreach (string label in labels)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                string suffix = "(" + label + ")";
                if (!explanation.EndsWith(suffix, StringComparison.Ordinal)) continue;
                int start = explanation.Length - suffix.Length;
                if (start == 0 || !char.IsWhiteSpace(explanation[start - 1])) return explanation;
                string heading = explanation.Substring(0, start).TrimEnd();
                return heading.Length == 0 ? explanation : heading + "\n" + suffix;
            }
            return explanation;
        }
    }
}
