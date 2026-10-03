using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private List<CivilWarRivalDefeatRecord> _rivalDefeats = new List<CivilWarRivalDefeatRecord>();
        private readonly HashSet<CivilWarRivalDefeatRecord> _resumingRivalDefeats = new HashSet<CivilWarRivalDefeatRecord>();
        internal bool OwnsRivalDefeat(CivilWarRivalDefeatRecord record) => record != null && !record.Completed && _rivalDefeats.Contains(record);
        internal bool IsRivalDefeatPending(Kingdom realm) => _rivalDefeats.Any(r => r.Protects(realm));
        internal bool IsRivalDefeatScorePending(WarScoreRecord war) => war != null && _rivalDefeats.Any(r =>
            r.Protects(r.Crown) && (war.AttackerKingdomId == r.Crown.StringId || war.DefenderKingdomId == r.Crown.StringId
                || war.AttackerKingdomId == r.Winner.Realm.StringId || war.DefenderKingdomId == r.Winner.Realm.StringId
                || war.AttackerKingdomId == r.Loser.Realm.StringId || war.DefenderKingdomId == r.Loser.Realm.StringId));

        private bool TryResumeRivalChallengeOutcome(SuccessionChallengeRecord challenge)
        {
            var record = _rivalDefeats.FirstOrDefault(r => r.OwnsChallenge(challenge));
            if (record == null) return false;
            ResumeRivalDefeat(record);
            return true;
        }

        private void ResumePendingRivalDefeats()
        {
            foreach (var record in _rivalDefeats.Where(r => !r.Completed).ToList()) ResumeRivalDefeat(record);
        }

        private void ResumeRivalDefeat(CivilWarRivalDefeatRecord record)
        {
            if (record.Completed || !_resumingRivalDefeats.Add(record)) return;
            try
            {
                var winner = record.Winner;
                var loser = record.Loser;
                var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(loser.Faction, loser.Realm,
                    "rival:" + loser.Realm.StringId, winner.Realm);
                ConflictOutcomeBehavior.Capture(resultNotice, "VICTOR", record.VictorHouse?.Leader?.Name);
                if (winner.Realm?.IsEliminated != false || record.VictorHouse?.IsEliminated != false
                    || winner.Faction.Leader != record.VictorHouse || record.VictorHouse.Kingdom != winner.Realm
                    || winner.Realm.RulingClan != record.VictorHouse
                    || !winner.Realm.IsAtWarWith(record.Crown)) return;
                if (record.Stage == 0)
                {
                    if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(loser.Faction,
                        SuccessionChallengeOutcome.Defeat, winner.Realm, record) == false) return;
                    record.Stage = 1;
                }
                if (record.Stage == 1)
                {
                    ReleaseMercenariesFromKingdom(loser.Realm);
                    foreach (var clan in record.LosingClans.Where(c => !c.IsEliminated && !c.IsUnderMercenaryService))
                    {
                        if (clan.Kingdom == loser.Realm)
                            winner.Faction.MoveClanToKingdomPreservingCivilWarInfluence(clan, winner.Realm,
                                preserveCustomBanner: true, showNotification: false);
                    }
                    if (loser.Realm.Clans.Any(c => !c.IsEliminated)) return;
                    record.Stage = 2;
                }
                if (record.Stage == 2)
                {
                    CompleteCivilWarTracker(loser.Faction, loser.Realm, "defeated by a rival claimant");
                    Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(loser.Faction);
                    foreach (var clan in record.LosingClans.Where(c => !c.IsEliminated && c.Kingdom == winner.Realm && !c.IsUnderMercenaryService))
                        winner.Faction.AddMember(clan);
                    ApplyCivilWarPeaceIfNeeded(record.Crown, loser.Realm);
                    ApplyCivilWarPeaceIfNeeded(winner.Realm, loser.Realm);
                    if (!DrainAndDestroyRebelKingdom(loser.Realm, winner.Realm)) return;
                    record.Stage = 3;
                }
                if (record.Stage == 3)
                {
                    if (!record.TribunalQueued)
                    {
                        if (record.Challenge != null)
                        {
                            TryQueueChallengeTribunal(loser.Faction, winner.Realm);
                            if (!record.Challenge.TribunalQueued) return;
                        }
                        else QueuePlayerTribunal(winner.Realm, winner.Realm, record.LosingClans,
                            loser.Realm, record.VictorHouse, GetRebelExileCause(loser.Faction.Type), record.FiefSnapshot);
                        record.TribunalQueued = true;
                    }
                    ApplyCivilWarVictoryInfluenceRewards(record.WinningClans, record.VictorHouse,
                        "rival claimant defeated", record.RewardedClans);
                    if (record.Challenge != null)
                    {
                        record.Challenge.OutcomeRewardsApplied = true;
                        SuccessionChallengeBehavior.Instance.CompleteWarOutcome(loser.Faction, winner.Realm);
                        if (!record.Challenge.ResolutionReturned) return;
                    }
                    if (!record.Announced)
                    {
                        CaptureOutcomeTribunal(resultNotice, winner.Realm);
                        ConflictOutcomeBehavior.Current?.Publish(resultNotice, "rival");
                        record.Announced = true;
                    }
                    record.Completed = true;
                    record.Failure = null;
                }
            }
            catch (Exception ex)
            {
                if (record.Failure != ex.Message) BellumCivileLogger.Log($"Rival claimant settlement deferred; war={record.Score.WarKey}; stage={record.Stage}; error={ex}");
                record.Failure = ex.Message;
            }
            finally { _resumingRivalDefeats.Remove(record); }
        }
    }
}
