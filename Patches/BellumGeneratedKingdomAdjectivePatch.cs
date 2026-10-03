using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Vanilla resolves kingdom adjectives by kingdom StringId. Bellum kingdoms are
    /// created at runtime, so they have no static str_adjective_for_faction variation.
    /// Their inherited culture provides the equivalent localized adjective.
    /// </summary>
    [HarmonyPatch(typeof(FactionHelper), nameof(FactionHelper.GetAdjectiveForFaction))]
    internal static class BellumGeneratedKingdomAdjectivePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(IFaction faction, ref TextObject __result)
        {
            if (!(faction is Kingdom kingdom)
                || kingdom.Culture == null
                || !BellumKingdomVisibilityHelper.ShouldUseCultureAdjective(kingdom))
            {
                return true;
            }

            __result = FactionHelper.GetAdjectiveForFactionCulture(kingdom.Culture);
            return false;
        }
    }
}
