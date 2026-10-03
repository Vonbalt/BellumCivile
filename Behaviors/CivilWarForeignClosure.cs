using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private static void PreservePromotedRebelForeignWars(Kingdom rebel, Kingdom successor, Kingdom parent)
        {
            if (rebel == null || rebel.IsEliminated || successor == null || successor.IsEliminated || successor == parent) return;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var internalRealms = Kingdom.All.Where(k => k == parent
                || manager?.GetFactionByRebelKingdom(k)?.ParentKingdom == parent).ToList();
            var enemies = GetExternalEnemiesToInherit(rebel, internalRealms);
            InheritExternalWars(successor, enemies);
            foreach (var enemy in enemies)
                BellumCivileLogger.Log($"Promoted rebel foreign war retained; shell={rebel.StringId}; successor={successor.StringId}; enemy={enemy.StringId}; at_war={successor.IsAtWarWith(enemy)}.");
        }

        private void CloseReunifiedRebelForeignWars(Kingdom rebel, Kingdom destination)
        {
            if (rebel == null || destination == null || rebel == destination || rebel.IsEliminated) return;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var scores = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
            foreach (var enemy in Kingdom.All.Where(k => k != rebel && k != destination && !k.IsEliminated
                && rebel.IsAtWarWith(k)).ToList())
            {
                // Internal rival wars have their own settlement. Never extend peace to the destination realm.
                if (manager?.GetFactionByRebelKingdom(enemy)?.ParentKingdom == destination) continue;
                var war = scores?.GetActiveWar(rebel, enemy);
                if (war != null && war.ConflictType != WarScoreConflictType.ForeignWar) continue;
                if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(enemy) && war == null) continue;
                scores?.CompleteReunifiedForeignWar(rebel, enemy);
                ApplyCivilWarPeaceIfNeeded(rebel, enemy);
                var text = new TextObject("{=BC_RebelForeignWar_Reunited}With {REALM} reunited, {ENEMY}'s war against {HOST} has ended.");
                text.SetTextVariable("REALM", destination.Name);
                text.SetTextVariable("ENEMY", enemy.Name);
                text.SetTextVariable("HOST", rebel.Name);
                BellumCivileNotifications.Show(text, BellumNotificationColors.Success,
                    primaryKingdom: destination, secondaryKingdom: enemy, isMajorEvent: true);
                BellumCivileLogger.Log($"Rebel foreign hostility closed; shell={rebel.StringId}; destination={destination.StringId}; enemy={enemy.StringId}; destination_at_war={destination.IsAtWarWith(enemy)}.");
            }
        }
    }
}
