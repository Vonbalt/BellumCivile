using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        private const int IndependenceRulerRepairVersion = 1;
        private const string IndependenceRulerRepairTag = "[BellumCivile independence ruler migration]";
        private int _independenceRulerRepairVersion;
        private List<string> _pendingIndependenceRulerRepairs = new List<string>();
        private bool _independenceRulerRepairLoaded;
        private bool _independenceRulerRepairSessionReady;
        private bool _independenceRulerRepairRunning;
        private readonly HashSet<string> _independenceRulerRepairDeferrals = new HashSet<string>();

        private void SyncIndependenceRulerRepair(IDataStore store)
        {
            store.SyncData("BellumCivile_IndependenceRulerRepairVersion", ref _independenceRulerRepairVersion);
            store.SyncData("BellumCivile_PendingIndependenceRulerRepairs", ref _pendingIndependenceRulerRepairs);
            _pendingIndependenceRulerRepairs = _pendingIndependenceRulerRepairs ?? new List<string>();
            if (!store.IsLoading) return;
            _independenceRulerRepairLoaded = true;
            _independenceRulerRepairSessionReady = false;
            _independenceRulerRepairDeferrals.Clear();
        }

        private void SkipNewGameIndependenceRulerRepair(CampaignGameStarter starter)
        {
            _independenceRulerRepairVersion = IndependenceRulerRepairVersion;
            _pendingIndependenceRulerRepairs.Clear();
            _independenceRulerRepairLoaded = false;
        }

        private void StartIndependenceRulerRepair(CampaignGameStarter starter)
        {
            _independenceRulerRepairSessionReady = true;
            ProcessIndependenceRulerRepair();
        }

        private void ProcessIndependenceRulerRepair()
        {
            if (!_independenceRulerRepairLoaded || !_independenceRulerRepairSessionReady || !_sessionLaunched
                || _independenceRulerRepairVersion >= IndependenceRulerRepairVersion || _independenceRulerRepairRunning)
                return;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;

            _independenceRulerRepairRunning = true;
            try
            {
                // A saved nonempty queue is the original scan's remainder, not a request to rescan.
                if (_pendingIndependenceRulerRepairs.Count == 0)
                {
                    _pendingIndependenceRulerRepairs = _independentRealmSourceTitleByKingdomId.Keys
                        .Where(id => id != null && id.IndexOf("_indep_", StringComparison.Ordinal) >= 0)
                        .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
                    BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=started; version={IndependenceRulerRepairVersion}; candidates={_pendingIndependenceRulerRepairs.Count}.");
                }
                foreach (string id in _pendingIndependenceRulerRepairs.ToList())
                {
                    try
                    {
                        bool repaired = TryRepairIndependentRuler(id, manager, out bool deferred, out string reason);
                        if (deferred)
                        {
                            LogIndependenceRulerRepairDeferral(id, reason);
                            continue;
                        }
                        if (!repaired)
                            BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=skipped; realm={id}; reason={reason}.");
                        _pendingIndependenceRulerRepairs.RemoveAll(pending => pending == id);
                    }
                    catch (Exception ex)
                    {
                        LogIndependenceRulerRepairDeferral(id, "exception:" + ex.GetType().Name + ":" + ex.Message);
                    }
                }
                if (_pendingIndependenceRulerRepairs.Count != 0) return;
                _independenceRulerRepairVersion = IndependenceRulerRepairVersion;
                BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=completed; version={IndependenceRulerRepairVersion}.");
            }
            finally { _independenceRulerRepairRunning = false; }
        }

        private void LogIndependenceRulerRepairDeferral(string id, string reason)
        {
            if (_independenceRulerRepairDeferrals.Add(id + "|" + reason))
                BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=deferred; realm={id}; reason={reason}.");
        }

        private bool TryRepairIndependentRuler(string id, FactionManagerBehavior manager, out bool deferred, out string reason)
        {
            deferred = false;
            reason = "realm_missing_or_eliminated";
            Kingdom realm = Kingdom.All.FirstOrDefault(k => k?.StringId == id);
            if (realm == null || realm.IsEliminated) return false;
            Clan previous = realm.RulingClan;
            if (IndependenceRulerRepairTransitionPending(realm, manager)
                || IndependenceRulerRepairTransitionPending(previous?.Kingdom, manager))
            {
                deferred = true;
                reason = "succession_or_realm_transfer_pending";
                return false;
            }
            reason = "no_living_foreign_ruler";
            if (previous == null || previous.IsEliminated || previous.Leader == null || previous.Leader.IsDead
                || previous.Kingdom == null || previous.Kingdom == realm) return false;
            reason = "player_ruler_protected";
            if (previous == Clan.PlayerClan) return false;

            // Never consult the current crown cache: it may point into the foreign ruler's holdings.
            FeudalTitleRecord source = GetIndependentRealmSovereignTitle(realm);
            reason = "founding_title_missing_or_ownership_ambiguous";
            if (source == null || source.AssociatedKingdomId != id || string.IsNullOrEmpty(source.DeFactoHolderClanId)
                || source.DeFactoHolderClanId != source.DeJureHolderClanId) return false;
            List<Clan> holders = Clan.All.Where(c => c?.StringId == source.DeFactoHolderClanId).Take(2).ToList();
            if (holders.Count != 1) return false;
            Clan founder = holders[0];
            reason = "founder_not_a_live_resident_noble";
            if (!NobleClanEligibilityHelper.IsValidRulingClan(founder, realm) || !realm.Clans.Contains(founder)) return false;

            List<Kingdom> parents = Kingdom.All.Where(k => k != null && k != realm
                && MatchesOriginalIndependenceFounder(id, k, founder)).Take(2).ToList();
            reason = "original_founder_identity_not_proven";
            if (parents.Count != 1) return false;
            Kingdom parent = parents[0];
            if (IndependenceRulerRepairTransitionPending(parent, manager))
            {
                deferred = true;
                reason = "original_parent_transition_pending";
                return false;
            }
            reason = "foreign_ruler_no_longer_in_original_parent";
            if (parent.IsEliminated || previous.Kingdom != parent || !parent.Clans.Contains(previous)) return false;
            reason = "founder_already_referenced_as_another_ruler";
            if (Kingdom.All.Any(k => k != null && k != realm && !k.IsEliminated && k.RulingClan == founder)) return false;
            reason = "founding_title_shared_with_another_live_realm";
            if (_independentRealmSourceTitleByKingdomId.Any(pair => pair.Key != id && pair.Value == source.TitleId
                && Kingdom.All.Any(k => k?.StringId == pair.Key && !k.IsEliminated))) return false;

            _currentRealmTitleByKingdomId.TryGetValue(id, out string oldCrown);
            BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=repairing; realm={id}; previous={previous.StringId}; "
                + $"previous_realm={parent.StringId}; founder={founder.StringId}; source_title={source.TitleId}; old_crown={oldCrown ?? "none"}; ownership_and_membership_unchanged=true.");
            // Bind the verified title before the raw setter, so interrupted/reentrant callbacks
            // cannot repair the foreign ruler's legitimate title through the stale crown cache.
            _currentRealmTitleByKingdomId[id] = source.TitleId;
            RuntimeRevision++;
            DisplayRevision++;
            _titleDisplayRefreshPending = true;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            realm.RulingClan = founder;
            if (realm.RulingClan != founder || !NobleClanEligibilityHelper.IsValidRulingClan(founder, realm))
                throw new InvalidOperationException("The verified ruler assignment did not remain valid.");
            reason = "verified_founder_restored";
            BellumCivileLogger.Log($"{IndependenceRulerRepairTag} stage=repaired; realm={id}; previous={previous.StringId}; "
                + $"ruler={founder.StringId}; source_title={source.TitleId}; ownership_and_membership_unchanged=true.");
            return true;
        }

        private static bool MatchesOriginalIndependenceFounder(string id, Kingdom parent, Clan founder)
        {
            string prefix = parent.StringId + "_indep_" + founder.StringId;
            if (id == prefix) return true;
            if (!id.StartsWith(prefix + "_", StringComparison.Ordinal)) return false;
            string suffix = id.Substring(prefix.Length + 1);
            return suffix.Length > 0 && suffix.All(c => c >= '0' && c <= '9');
        }

        private static bool IndependenceRulerRepairTransitionPending(Kingdom realm, FactionManagerBehavior manager)
        {
            return realm != null && (manager.IsRulerRepairSuppressed(realm)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.IsCoalitionSuccession(realm) == true
                || realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any());
        }
    }
}
