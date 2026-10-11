using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public static partial class BellumIntegration
    {
        public static bool CanBeginElectionPersuasion(Kingdom realm, Hero speaker, Hero candidate, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            reason = UnavailableVote();
            return ElectionLobbyingBehavior.Current?.CanBeginAttempt(realm, speaker, candidate, out reason) == true;
        }

        public static bool TryBeginElectionPersuasion(Kingdom realm, Hero speaker, Hero candidate,
            out ElectionPersuasionAttempt attempt, out TextObject reason)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            attempt = null; reason = UnavailableVote();
            return ElectionLobbyingBehavior.Current?.TryBeginAttempt(realm, speaker, candidate, out attempt, out reason) == true;
        }

        public static bool CompleteElectionPersuasion(ElectionPersuasionAttempt attempt, bool success)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return ElectionLobbyingBehavior.Current?.CompleteAttempt(attempt, success) == true;
        }
    }
}
