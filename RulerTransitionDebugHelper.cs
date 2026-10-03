using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class RulerTransitionDebugHelper
    {
        public static void Report(string source, Kingdom kingdom, Clan previousRuler, Clan newRuler, string details = null, bool requestInGameDisplay = false)
        {
            string kingdomName = kingdom?.Name?.ToString() ?? kingdom?.StringId ?? "unknown kingdom";
            string previousName = previousRuler?.Name?.ToString() ?? previousRuler?.StringId ?? "null";
            string newName = newRuler?.Name?.ToString() ?? newRuler?.StringId ?? "null";
            string detailText = string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details;
            string message = $"{source}: {kingdomName} ruler {previousName} -> {newName}.{detailText}";

            bool playerInvolved = previousRuler == Clan.PlayerClan
                               || newRuler == Clan.PlayerClan
                               || kingdom == Clan.PlayerClan?.Kingdom;

            BellumCivileDebug.Trace("ruler", message, requestInGameDisplay || playerInvolved);
        }

        public static string BuildValidityDetails(Kingdom kingdom, Clan clan)
        {
            if (kingdom == null)
                return "kingdom=null";
            if (clan == null)
                return "clan=null";

            return $"candidate={clan.StringId}; candidate_kingdom={clan.Kingdom?.StringId ?? "null"}; target_kingdom={kingdom.StringId}; eliminated={clan.IsEliminated}; mercenary={clan.IsUnderMercenaryService}; minor={clan.IsMinorFaction}; leader={clan.Leader?.StringId ?? "null"}; leader_dead={clan.Leader?.IsDead.ToString() ?? "null"}";
        }
    }
}
