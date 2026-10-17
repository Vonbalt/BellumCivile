using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class InternalInstabilityPresets
    {
        internal static IReadOnlyDictionary<string, int> GetValues(int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex > 2)
                return null;

            int Pick(int standard, int reduced, int increased) =>
                selectedIndex == 1 ? reduced : selectedIndex == 2 ? increased : standard;

            return new Dictionary<string, int>
            {
                [nameof(BellumCivileSettings.RebellionThreshold)] = Pick(100, 125, 75),
                [nameof(BellumCivileSettings.CourtCrisisMoodThreshold)] = Pick(-60, -80, -40),
                [nameof(BellumCivileSettings.CourtChallengeChancePercent)] = Pick(100, 50, 100),
                [nameof(BellumCivileSettings.HeirChallengeLoyaltyThreshold)] = Pick(25, 15, 35),
                [nameof(BellumCivileSettings.ElectionChallengeChancePercent)] = Pick(100, 50, 125),
                [nameof(BellumCivileSettings.ClaimFeudStartThreshold)] = Pick(65, 80, 55),
                [nameof(BellumCivileSettings.ClaimedRevocationChancePercent)] = Pick(100, 50, 150),
                [nameof(BellumCivileSettings.TreasonResistanceChancePercent)] = Pick(100, 50, 150),
                [nameof(BellumCivileSettings.ClientLiberationWillingnessThreshold)] = Pick(75, 85, 65)
            };
        }

        internal static void ShowUnavailable()
        {
            BellumCivileLogger.Log("Internal instability preset could not be applied through MCM's slider controls.");
            InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                "{=BC_MCM_InstabilityPresetUnavailable}Unable to apply the instability preset. Check the Bellum log or adjust the sliders manually.").ToString()));
        }
    }
}
