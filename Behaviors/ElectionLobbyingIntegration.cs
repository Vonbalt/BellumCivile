using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectionLobbyingBehavior
    {
        private static string AttemptKeyFor(Kingdom realm, Hero speaker) => realm.StringId + "|" + speaker.StringId;

        internal bool CanBeginAttempt(Kingdom realm, Hero speaker, Hero candidate, out TextObject reason)
        {
            reason = new TextObject("{=BC_EL_Unavailable}This vote is already pledged, the ballot is closed, or the circumstances have changed.");
            if (!ValidPlayerAppeal(realm, speaker) || Elections?.CanCommitPromise(realm, speaker, candidate, out _) != true)
                return false;
            if (Hero.MainHero.GetRelation(speaker) < 30)
            {
                reason = new TextObject("{=BC_EL_TrustGate}They do not trust you enough to hear your appeal. Relation required: 30.");
                return false;
            }
            if (_attempts.TryGetValue(AttemptKeyFor(realm, speaker), out var expiry) && expiry > CampaignTime.Now)
            {
                reason = new TextObject("{=BC_EL_AttemptSpent}They have heard your arguments for this court year.");
                return false;
            }
            reason = TextObject.GetEmpty();
            return true;
        }

        private static bool ValidPlayerAppeal(Kingdom realm, Hero speaker) => realm != null
            && Hero.MainHero?.IsAlive == true && Clan.PlayerClan?.Kingdom == realm && !Clan.PlayerClan.IsUnderMercenaryService
            && speaker?.IsAlive == true && !speaker.IsPrisoner && !speaker.IsDisabled && speaker.Clan != Clan.PlayerClan;

        internal bool TryBeginAttempt(Kingdom realm, Hero speaker, Hero candidate,
            out ElectionPersuasionAttempt attempt, out TextObject reason)
        {
            attempt = null;
            if (!CanBeginAttempt(realm, speaker, candidate, out reason)) return false;
            if (!Elections.CanCommitPromise(realm, speaker, candidate, out var until)) return false;
            foreach (var key in _attempts.Where(p => p.Value <= CampaignTime.Now).Select(p => p.Key).ToList()) _attempts.Remove(key);
            _attempts[AttemptKeyFor(realm, speaker)] = until;
            attempt = new ElectionPersuasionAttempt(this, Elections.Get(realm), realm, speaker, candidate, until);
            return true;
        }

        internal bool CompleteAttempt(ElectionPersuasionAttempt attempt, bool success)
        {
            if (attempt == null || attempt.Owner != this || attempt.Completed) return false;
            attempt.Completed = true;
            if (!success || !ValidPlayerAppeal(attempt.Realm, attempt.Speaker)
                || attempt.Expires <= CampaignTime.Now
                || !_attempts.TryGetValue(AttemptKeyFor(attempt.Realm, attempt.Speaker), out var expiry) || expiry != attempt.Expires
                || Elections?.CanCommitPromise(attempt.Realm, attempt.Speaker, attempt.Candidate, out var until) != true
                || until != attempt.Expires || Elections.Get(attempt.Realm) != attempt.Ballot
                || attempt.Ballot.MandateNumber != attempt.Mandate) return false;
            return Elections.TryCommitPromise(attempt.Realm, attempt.Speaker, attempt.Candidate, false);
        }
    }
}
