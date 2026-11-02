using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class AlliancePeaceRestriction
    {
        internal const string CancellationReason = "an active call-to-arms agreement prevents a separate peace";

        internal static bool IsPeaceBlocked(Kingdom first, Kingdom second)
            => TryGetObligation(first, second, out _, out _, out _);

        internal static bool TryGetPeaceBlock(Kingdom first, Kingdom second, out TextObject reason)
        {
            reason = TextObject.GetEmpty();
            if (!TryGetObligation(first, second, out Kingdom called, out Kingdom caller, out Kingdom enemy))
                return false;

            reason = new TextObject("{=BC_AllianceWar_PeaceBlocked}{CALLED_REALM} is bound by its call-to-arms agreement with {CALLING_REALM} and cannot negotiate a separate peace with {ENEMY_REALM} while that agreement remains active.");
            reason.SetTextVariable("CALLED_REALM", called.Name);
            reason.SetTextVariable("CALLING_REALM", caller?.Name ?? TextObject.GetEmpty());
            reason.SetTextVariable("ENEMY_REALM", enemy.Name);
            return true;
        }

        private static bool TryGetObligation(Kingdom first, Kingdom second,
            out Kingdom called, out Kingdom caller, out Kingdom enemy)
        {
            called = caller = enemy = null;
            if (first == null || second == null || first == second)
                return false;

            // Keep native agreement lifecycle authoritative, without importing vanilla peace desirability.
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (alliances == null)
                return false;
            if (alliances.IsAtWarByCallToWarAgreement(first, second, out caller))
            {
                called = first;
                enemy = second;
                return true;
            }
            if (alliances.IsAtWarByCallToWarAgreement(second, first, out caller))
            {
                called = second;
                enemy = first;
                return true;
            }
            return false;
        }
    }
}
