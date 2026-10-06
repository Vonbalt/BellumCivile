using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class FiefDeliberationBehavior
    {
        // Pending metadata also belongs to an opened allocation until the land is actually awarded.
        private Dictionary<string, CampaignTime> _openedFiefVoteDates = new Dictionary<string, CampaignTime>();

        private void SyncAllocationRecovery(IDataStore store)
        {
            store.SyncData("BC_OpenedFiefVoteDates", ref _openedFiefVoteDates);
            _openedFiefVoteDates = _openedFiefVoteDates ?? new Dictionary<string, CampaignTime>();
        }

        internal bool HasOpenedAllocation(Kingdom realm, Settlement settlement) => realm != null && settlement != null
            && _openedFiefVoteDates.ContainsKey(PendingKey(realm, settlement));

        private void OnAllocationDecisionAdded(KingdomDecision decision, bool isPlayerInvolved)
        {
            if (decision is SettlementClaimantDecision claimant) RememberOpenedAllocation(claimant);
        }

        internal void RememberOpenedAllocation(SettlementClaimantDecision decision)
        {
            var realm = decision?.Kingdom;
            var settlement = decision?.Settlement;
            if (!CanDeliberateFiefs(realm) || realm != Clan.PlayerClan?.Kingdom
                || settlement?.Town?.IsOwnerUnassigned != true || settlement.MapFaction != realm) return;
            string key = PendingKey(realm, settlement);
            if (_openedFiefVoteDates.ContainsKey(key)) return;

            // Older saves can contain an already-open ballot without a Bellum receipt.
            var due = _pendingFiefDate.TryGetValue(key, out var original) ? original : CampaignTime.Now - CampaignTime.Hours(1);
            _openedFiefVoteDates[key] = due;
            if (!_pendingFiefProposer.ContainsKey(key)) _pendingFiefProposer[key] = decision.ProposerClan?.StringId ?? "";
            var capturer = ResolveClaimantDecisionCapturer(decision);
            if (!_pendingFiefCapturer.ContainsKey(key)) _pendingFiefCapturer[key] = capturer?.StringId ?? "";
            if (!_pendingFiefExclude.ContainsKey(key)) _pendingFiefExclude[key] = decision.ClanToExclude?.StringId ?? "";
            if (!_pendingFiefParticipants.ContainsKey(key))
                _pendingFiefParticipants[key] = ResolveRecentSiegeParticipantClanIds(settlement, capturer);
            DelayedVoteReliability.EnsureLifecycle(key, due, _pendingFiefCreatedDay, _pendingFiefRetryCount);
        }

        private void FinishFiefHandoff(string key)
        {
            if (_openedFiefVoteDates.ContainsKey(key)) _pendingFiefDate.Remove(key);
            else RemovePendingKey(key);
        }

        private bool ResumeOpenedAllocation(Kingdom realm, Settlement settlement)
        {
            string key = PendingKey(realm, settlement);
            if (!_openedFiefVoteDates.TryGetValue(key, out var due)) return false;
            if (!CanDeliberateFiefs(realm) || settlement?.Town?.IsOwnerUnassigned != true || settlement.MapFaction != realm)
            {
                ClearBribedVotesForPendingKey(key);
                RemovePendingKey(key);
                return true;
            }
            if (_pendingFiefDate.ContainsKey(key) || realm.UnresolvedDecisions.Any(d => IsFiefDecisionForSettlement(d, settlement)))
                return true;
            if (DelayedVoteReliability.IsPendingExpired(key, _pendingFiefCreatedDay, _pendingFiefRetryCount))
            {
                if (DelayedVoteReliability.GetFailureCount(key, _pendingFiefRetryCount) <= DelayedVoteReliability.MaxFailedAttempts)
                {
                    _pendingFiefRetryCount[key] = DelayedVoteReliability.MaxFailedAttempts + 1;
                    BellumCivileLogger.Log($"Fief allocation recovery exhausted for {key}; allowing native allocation without restarting deliberation.");
                }
                return false; // Allow native allocation, without another Bellum delay or announcement.
            }

            int attempts = DelayedVoteReliability.RegisterFailure(key, _pendingFiefRetryCount);
            _pendingFiefDate[key] = due;
            _pendingFiefCapturer.TryGetValue(key, out string capturerId);
            BellumCivileLogger.Log($"Resumed interrupted fief allocation {key}; original_due={due.ToDays:0.00}; retries={attempts}; capturer={capturerId ?? "null"}; commitments_preserved=true.");
            return true;
        }

        private void ReconcileOpenedAllocations()
        {
            foreach (string key in _openedFiefVoteDates.Keys.ToList())
            {
                int separator = key.IndexOf('|');
                var realm = separator < 0 ? null : Kingdom.All.FirstOrDefault(k => k.StringId == key.Substring(0, separator));
                var settlement = separator < 0 ? null : Settlement.Find(key.Substring(separator + 1));
                if (realm == null || settlement == null || realm != Clan.PlayerClan?.Kingdom
                    || !CanDeliberateFiefs(realm) || settlement.MapFaction != realm || settlement.Town?.IsOwnerUnassigned != true)
                {
                    ClearBribedVotesForPendingKey(key);
                    RemovePendingKey(key);
                }
                else ResumeOpenedAllocation(realm, settlement);
            }
        }

        internal void ClearOpenedAllocations(Settlement settlement)
        {
            if (settlement == null) return;
            string suffix = "|" + settlement.StringId;
            foreach (string key in _openedFiefVoteDates.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToList())
            {
                ClearBribedVotesForPendingKey(key);
                RemovePendingKey(key);
            }
        }
    }
}
