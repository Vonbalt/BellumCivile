using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveContestBehavior
    {
        private static bool PendingStartup(ElectiveContestRecord record) => record != null
            && record.WarDispatchStarted && !record.WarDispatchCompleted && !record.Closed && !record.StartupAborted;

        private static bool StartupRealm(ElectiveContestRecord record, Kingdom realm) => realm != null
            && (record.Realm == realm || record.Candidates.Any(c => c.WarShell == realm
                || c.WarFaction?.GetTrackedRebelKingdomIncludingEliminated() == realm));

        internal bool IsDispatchRealmPending(Kingdom realm) => _contests.Any(c => PendingStartup(c) && StartupRealm(c, realm));

        internal bool IsDispatchScorePending(WarScoreRecord score) => score != null && _contests.Any(c => PendingStartup(c)
            && (c.Realm.StringId == score.AttackerKingdomId || c.Realm.StringId == score.DefenderKingdomId
                || c.Candidates.Any(h => (h.WarShell ?? h.WarFaction?.GetTrackedRebelKingdomIncludingEliminated()) is Kingdom shell
                    && (shell.StringId == score.AttackerKingdomId || shell.StringId == score.DefenderKingdomId))));

        internal bool OwnsRivalStartup(ElectiveContestRecord record, FactionObject first, FactionObject second) =>
            record != null && _contests.Contains(record) && PendingStartup(record) && first != null && second != null && first != second
            && record.SurrenderTo == null && record.Candidates.Any(c => c.WarFaction == first && c.WarStarted)
            && record.Candidates.Any(c => c.WarFaction == second && c.WarStarted);

        private bool StartRivalry(ElectiveContestRecord record, ElectiveContestCandidate first, ElectiveContestCandidate second)
        {
            var journal = CivilWarConflictBehavior.Instance;
            var a = first.WarShell;
            var b = second.WarShell;
            if (journal == null || a?.IsEliminated != false || b?.IsEliminated != false || a == b
                || !a.IsAtWarWith(record.Realm) || !b.IsAtWarWith(record.Realm)
                || !OwnsRivalStartup(record, first.WarFaction, second.WarFaction))
            {
                record.DispatchFailure = "Waiting for both claimant realms and their Crown wars";
                return false;
            }
            var conflict = journal.GetConflict(first.WarFaction);
            if (conflict == null || journal.GetConflict(second.WarFaction) != conflict) return false;
            // Store pair identity before declaring hostility. No foreign-war action or extra recruitment.
            record.Rivalry = record.Rivalry ?? conflict.ObserveRivalry(first.WarFaction, second.WarFaction);
            if (record.Rivalry == null || record.Rivalry.Closed) return false;
            if (!a.IsAtWarWith(b)) FactionManager.DeclareWar(a, b);
            var pair = journal.RegisterRivalry(first.WarFaction, second.WarFaction, record);
            if (pair != record.Rivalry || pair.Score?.IsActive != true || !a.IsAtWarWith(b))
            {
                record.DispatchFailure = "Rival hostility exists; awaiting its registered war tracker";
                return false;
            }
            return true;
        }

        private bool AbortStartup(ElectiveContestRecord record, string reason)
        {
            record.StartupAborted = true;
            record.DispatchFailure = reason;
            return FinishAbortedStartup(record);
        }

        private bool FinishAbortedStartup(ElectiveContestRecord record)
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (manager == null || resolution == null || record.Realm?.IsEliminated != false
                || record.Realm.Leader?.IsAlive != true || CrownAccessionBehavior.Instance?.IsPending(record.Realm) == true) return false;
            foreach (var candidate in record.Candidates.Where(c => c.WarFaction != null))
            {
                var faction = candidate.WarFaction;
                var shell = candidate.WarShell ?? faction.GetTrackedRebelKingdomIncludingEliminated();
                // Native startup may have completed immediately before an exception interrupted its caller.
                if (!faction.IsChallengeStartupPending && shell != null) candidate.WarStarted = true;
                if (candidate.WarStarted && faction.Leader?.IsEliminated == false && faction.Leader.Leader?.IsAlive == true
                    && shell?.IsEliminated == false && faction.Leader.Kingdom == shell) continue;
                if (shell?.IsEliminated == false)
                {
                    resolution.ResolveWhitePeace(faction, shell);
                    if (!shell.IsEliminated || shell.Clans.Any(c => !c.IsEliminated)) return false;
                }
                else
                {
                    CivilWarConflictBehavior.Instance?.Complete(faction);
                    manager.RemoveFaction(faction);
                }
            }
            if (record.Rivalry?.Closed == false && record.Rivalry.Score?.IsActive != true)
            {
                var live = record.Candidates.Where(c => c.WarStarted && c.WarShell?.IsEliminated == false).ToList();
                if (live.Count == 2 && live[0].WarShell.IsAtWarWith(live[1].WarShell)
                    && CivilWarConflictBehavior.Instance?.RegisterRivalry(live[0].WarFaction, live[1].WarFaction) != record.Rivalry)
                    return false;
            }
            record.Closed = true;
            record.ClosedReason = "Election-war startup interrupted; established wars retained, unfinished rebellions withdrawn";
            return true;
        }
    }
}
