using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EndCaptivityAction), nameof(EndCaptivityAction.ApplyByPeace))]
    internal static class RetainedTreatyPrisonerPeaceReleasePatch
    {
        private static bool Prefix(Hero character)
        {
            RetainedTreatyPrisonerBehavior behavior = Campaign.Current?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>();
            if (behavior?.IsRetained(character) != true)
                return true;

            behavior.RecordBlockedPeaceRelease(character);
            return false;
        }
    }

    [HarmonyPatch(typeof(PrisonerReleaseCampaignBehavior), "DailyHeroTick")]
    internal static class RetainedTreatyPrisonerEscapePatch
    {
        private static bool Prefix(Hero hero)
        {
            RetainedTreatyPrisonerBehavior behavior = Campaign.Current?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>();
            return behavior?.TryHandleRetainedEscape(hero) != true;
        }
    }
}
