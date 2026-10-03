using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CivilWarConflictBehavior
    {
        internal static bool IsRivalryScore(WarScoreRecord war) => war?.ConflictType == WarScoreConflictType.CivilWar
            && (war.ContextId?.StartsWith(CivilWarPairRecord.RivalryPrefix, System.StringComparison.Ordinal) == true
                || Instance?._conflicts.Any(c => c.Pairs.Any(p => !p.Closed && p.IsRivalry && p.Score == war)) == true);

        // Explicit observation of an existing hostile pair, including an owned startup;
        // this does not declare war, recruit clans or revive a completed rivalry.
        internal CivilWarPairRecord RegisterRivalry(FactionObject first, FactionObject second, ElectiveContestRecord startup = null)
        {
            bool owned = ElectiveContestBehavior.Instance?.OwnsRivalStartup(startup, first, second) == true;
            if (first == null || second == null || !owned && (IsFactionTransferPending(first) || IsFactionTransferPending(second))) return null;
            var conflict = GetConflict(first);
            if (conflict == null || GetConflict(second) != conflict) return null;
            var a = first.GetTrackedRebelKingdomIncludingEliminated();
            var b = second.GetTrackedRebelKingdomIncludingEliminated();
            if (a?.IsEliminated != false || b?.IsEliminated != false || !a.IsAtWarWith(b)
                || !a.IsAtWarWith(conflict.CrownRealm) || !b.IsAtWarWith(conflict.CrownRealm)) return null;
            var pair = conflict.ObserveRivalry(first, second);
            if (pair == null) return null;
            if (pair.Score == null)
            {
                var attacker = conflict.Sides.First(s => s.Id == pair.AttackerSideId).Realm;
                var defender = conflict.Sides.First(s => s.Id == pair.DefenderSideId).Realm;
                pair.Score = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.RegisterCivilWarRivalry(pair.Id, attacker, defender);
            }
            return pair.Score != null ? pair : null;
        }

        internal CivilWarConflictRecord FindRivalry(WarScoreRecord war, out CivilWarSideRecord attacker, out CivilWarSideRecord defender)
        {
            attacker = defender = null;
            foreach (var conflict in _conflicts)
                if (conflict.TryGetRivalSides(war, out attacker, out defender)) return conflict;
            return null;
        }
    }

    public partial class CivilWarResolutionBehavior
    {
        internal bool TryResolveRivalVictory(WarScoreRecord war, bool attackerWon)
        {
            var pending = _rivalDefeats.FirstOrDefault(r => r.Score == war);
            if (pending != null) { ResumeRivalDefeat(pending); return pending.Completed; }
            var journal = CivilWarConflictBehavior.Instance;
            if (journal == null || war?.IsActive != true) return false;
            var conflict = journal.FindRivalry(war, out var attacker, out var defender);
            if (conflict == null || conflict.CrownRealm?.IsEliminated != false) return false;
            var winner = attackerWon ? attacker : defender;
            var loser = attackerWon ? defender : attacker;
            var crown = conflict.CrownRealm;
            if (winner.Realm?.IsEliminated != false || winner.Faction?.Leader?.Kingdom != winner.Realm
                || winner.Realm.RulingClan != winner.Faction.Leader
                || SuccessionChallengeBehavior.Instance?.CanWinChallenge(winner.Faction) == false
                || loser.Faction?.ParentKingdom != crown || winner.Faction.ParentKingdom != crown
                || CivilWarConflictBehavior.IsFactionTransferPending(winner.Faction)
                || CivilWarConflictBehavior.IsFactionTransferPending(loser.Faction)
                || crown.RulingClan?.Leader?.IsAlive != true || CrownAccessionBehavior.Instance?.IsPending(crown) == true
                || !winner.Realm.IsAtWarWith(crown)) return false;
            CaptureCurrentCivilWarFiefSnapshot(loser.Faction, loser.Realm);
            var record = new CivilWarRivalDefeatRecord
            {
                Score = war, Winner = winner, Loser = loser, Crown = crown, VictorHouse = winner.Faction.Leader,
                LosingClans = loser.Faction.Members.Concat(loser.Realm.Clans).Where(c => c != null && !c.IsEliminated).Distinct().ToList(),
                WinningClans = winner.Realm.Clans.ToList(), FiefSnapshot = loser.Faction.ExportCivilWarStartFiefSnapshot(),
                Challenge = SuccessionChallengeBehavior.Instance?.GetWarRecord(loser.Faction)
            };
            if (record.Challenge != null && record.Challenge.WarOutcome != SuccessionChallengeOutcome.None) return false;
            _rivalDefeats.Add(record);
            ResumeRivalDefeat(record);
            return record.Completed;
        }
    }
}
