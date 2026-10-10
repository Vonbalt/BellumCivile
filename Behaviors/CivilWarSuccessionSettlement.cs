using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private readonly HashSet<SuccessionChallengeRecord> _settlingChallenges = new HashSet<SuccessionChallengeRecord>();

        private bool TryQueueChallengeTribunal(FactionObject faction, Kingdom realm)
        {
            var challenges = SuccessionChallengeBehavior.Instance;
            var record = challenges?.GetWarRecord(faction);
            if (record == null) return false;
            if (record.TribunalQueued || record.WarOutcome == SuccessionChallengeOutcome.WhitePeace) return true;
            if (record.WarOutcome == SuccessionChallengeOutcome.Victory && !challenges.LegalizeChallengeVictory(faction, realm)) return true;
            if (record.WarOutcome == SuccessionChallengeOutcome.Victory && record.HasWarCrownReceipt
                && realm.RulingClan != record.OutcomeVictor
                && Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.GetDeFactoSovereignClan(realm) != record.OutcomeVictor)
            {
                // A recovered historical victory must not put a later reigning house on trial.
                record.TribunalQueued = true;
                BellumCivileLogger.Log($"Skipped superseded challenge tribunal; crisis={record.Id}; realm={realm.StringId}; original_victor={record.OutcomeVictor?.StringId}.");
                return true;
            }
            // The saved queue also dispatches AI verdicts. Queue insertion and its
            // receipt have no intervening campaign callbacks.
            QueuePlayerTribunal(realm, realm, record.OutcomeLosers, record.WarShell, record.OutcomeVictor,
                record.WarOutcome == SuccessionChallengeOutcome.Defeat ? GetRebelExileCause(faction.Type) : GetLoyalistExileCause(faction.Type),
                faction.ExportCivilWarStartFiefSnapshot());
            record.TribunalQueued = true;
            return true;
        }

        private static void ApplyChallengeAwareVictoryRewards(FactionObject faction, IEnumerable<Clan> winners, Clan leader, string reason,
            bool loyalistVictory = false)
        {
            var record = SuccessionChallengeBehavior.Instance?.GetWarRecord(faction);
            if (record?.OutcomeRewardsApplied == true) return;
            var history = loyalistVictory ? CivilWarConflictBehavior.Instance?.GetRewardHistory(faction) : null;
            if (history != null)
                winners = winners.Where(clan =>
                {
                    if (history.CanReceiveLoyalistReward(clan)) return true;
                    BellumCivileLogger.Log($"Skipped loyalist victory reward for previously defeated rebel; conflict={history.Id}; clan={clan?.StringId}; reason={reason}.");
                    return false;
                });
            ApplyCivilWarVictoryInfluenceRewards(winners, leader, reason, record?.RewardedClans);
            if (record != null) record.OutcomeRewardsApplied = true;
        }

        internal void ResumeChallengeOutcome(SuccessionChallengeRecord record)
        {
            if (record != null && TryResumeRivalChallengeOutcome(record)) return;
            // One recovery owner chooses the successor and moves the households.
            if (record != null && TryResumeChallengeCollapse(record)) return;
            if (record == null || record.ResolutionReturned || !_settlingChallenges.Add(record)) return;
            try
            {
                var challenges = SuccessionChallengeBehavior.Instance;
                var faction = record.WarFaction;
                var realm = record.OutcomeRealm;
                var resultNotice = faction == null ? null : ConflictOutcomeBehavior.Current?.BeginCivil(faction, record.WarShell);
                if (record.RestoredRealmRequested && !record.RestoredRealmReady)
                {
                    realm = CreateRestoredSuccessorKingdom(record.Realm, record.WarShell, record.OutcomeVictor,
                        runImmediateRepair: false, challenge: record);
                    if (realm == null) return;
                }
                if (faction == null || realm?.IsEliminated != false || challenges == null) return;
                if (!challenges.PrepareWarOutcome(faction, record.WarOutcome, realm)) return;
                if (record.WarOutcome == SuccessionChallengeOutcome.Defeat)
                    CivilWarConflictBehavior.Instance?.GetRewardHistory(faction)?.RecordCrownDefeat(record.OutcomeLosers);
                Clan victor = record.OutcomeVictor;
                if (record.RestoredRealmRequested && record.Realm != realm)
                {
                    if (record.Realm.IsEliminated) TransferLiveClansFromEliminatedKingdom(record.Realm, realm, victor);
                    else DrainAndDestroyRebelKingdom(record.Realm, realm);
                }
                if (record.WarOutcome == SuccessionChallengeOutcome.Victory && !record.HasWarCrownReceipt)
                {
                    if (victor?.IsEliminated != false || (victor.Kingdom != realm && victor.Kingdom != record.WarShell)
                        || victor.Leader != record.Challenger || record.Challenger?.IsAlive != true) return;
                    if (realm.RulingClan != victor && realm.Leader != record.Sovereign) return;
                    ReleaseMercenariesFromKingdom(record.WarShell);
                    foreach (Clan clan in record.WarShell?.Clans.ToList() ?? new List<Clan>())
                        TransferCivilWarClan(clan, record.WarShell, realm, faction);
                    if (victor.Kingdom != realm) return;
                    if (realm.RulingClan != victor)
                    {
                        ClearSuccessionStateForKingdom(realm, "recover dynastic challenge victory");
                        Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.MarkAsUsurper(realm);
                        realm.RulingClan = victor;
                    }
                    if (Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.TrySetKingdomTitleRuler(realm, victor, true,
                        "recover dynastic challenge victory") != true) return;
                    record.WarCrownTransferred = true;
                }
                if (record.WarOutcome == SuccessionChallengeOutcome.Victory
                    && !challenges.LegalizeChallengeVictory(faction, realm)) return;
                TryQueueChallengeTribunal(faction, realm);
                ApplyCivilWarPeaceIfNeeded(realm, record.WarShell);
                CompleteCivilWarTracker(faction, record.WarShell, "recover dynastic challenge outcome");
                Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);
                if (record.WarShell?.IsEliminated == false)
                {
                    ReleaseMercenariesFromKingdom(record.WarShell);
                    DrainAndDestroyRebelKingdom(record.WarShell, realm);
                }
                faction.RestoreCivilWarInfluenceSnapshots(faction.Members.Concat(record.OutcomeLosers));
                if (record.WarOutcome != SuccessionChallengeOutcome.WhitePeace)
                {
                    var winners = record.WarOutcome == SuccessionChallengeOutcome.Victory ? faction.Members
                        : realm.Clans.Where(c => !record.OutcomeLosers.Contains(c));
                    ApplyChallengeAwareVictoryRewards(faction, winners, victor, "recover dynastic challenge outcome",
                        loyalistVictory: record.WarOutcome == SuccessionChallengeOutcome.Defeat);
                }
                challenges.CompleteWarOutcome(faction, realm);
                CaptureOutcomeTribunal(resultNotice, realm);
                if (record.ResolutionReturned && record.Phase == SuccessionChallengePhase.Settled)
                    ConflictOutcomeBehavior.Current?.Publish(resultNotice,
                        record.RestoredRealmRequested ? "collapse" : record.WarOutcome == SuccessionChallengeOutcome.Victory ? "claimant"
                        : record.WarOutcome == SuccessionChallengeOutcome.Defeat ? "loyalist"
                        : resultNotice != null && resultNotice.Names.TryGetValue("RESULT_KIND", out var peaceKind) ? peaceKind : "peace",
                        ConflictOutcomeBehavior.ContinuingWars(realm));
            }
            catch (Exception ex)
            {
                record.Failure = ex.Message;
                BellumCivileLogger.Log($"Succession outcome pending; crisis={record.Id}; error={ex}");
            }
            finally { _settlingChallenges.Remove(record); }
        }
    }
}
