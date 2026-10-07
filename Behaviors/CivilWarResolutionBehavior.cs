using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To intercept the end of a civil war (when a ruler is captured) and properly execute the post-war cleanup. This includes applying the specific demands of a successful rebellion or running tribunals/executions for a crushed one.
    /// </summary>
    public partial class CivilWarResolutionBehavior : CampaignBehaviorBase
    {
        private const int CivilWarPacifiedDays = 30;
        private static int _peaceHandlingSuppressionDepth = 0;
        private enum TribunalVerdict { Pardon = 0, Punish = 1, Execute = 2 }

        private sealed class IndependencePartitionGroup
        {
            public Clan Leader;
            public FeudalTitleRecord SourceTitle;
            public readonly List<Clan> Clans = new List<Clan>();
        }

        private List<string> _pendingCaptureResolutionOrder = new List<string>();
        private Dictionary<string, string> _captureResolutionParentKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _captureResolutionRebelKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _captureResolutionLeaderClanIds = new Dictionary<string, string>();
        private Dictionary<string, bool> _captureResolutionRebelVictory = new Dictionary<string, bool>();
        private Dictionary<string, string> _pendingSuccessionCandidates = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingSuccessionExcluded = new Dictionary<string, string>();
        private Dictionary<string, bool> _pendingSuccessionCanReplacePlayerRuler = new Dictionary<string, bool>();
        private Dictionary<string, CampaignTime> _authorizedPlayerSuccessionUntil = new Dictionary<string, CampaignTime>();
        private Dictionary<string, string> _authorizedPlayerSuccessionCandidateIds = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _playerDeathSuccessionElectionUntil = new Dictionary<string, CampaignTime>();
        private int _nextTribunalGroupId = 1;
        private List<string> _pendingTribunalOrder = new List<string>();
        private Dictionary<string, string> _tribunalRewardKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _tribunalDestinationKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _tribunalExcludedRefugeIds = new Dictionary<string, string>();
        private Dictionary<string, string> _tribunalVictorClanIds = new Dictionary<string, string>();
        private Dictionary<string, string> _tribunalLosingClanIds = new Dictionary<string, string>();
        private Dictionary<string, int> _tribunalExileCauseIds = new Dictionary<string, int>();
        private Dictionary<string, string> _tribunalCivilWarStartFiefSnapshots = new Dictionary<string, string>();
        private Dictionary<string, bool> _tribunalAnyExecuted = new Dictionary<string, bool>();
        private Dictionary<string, bool> _tribunalAnyForgiven = new Dictionary<string, bool>();
        private Dictionary<string, bool> _tribunalAnyExiled = new Dictionary<string, bool>();
        private Dictionary<string, bool> _tribunalOldKingExecuted = new Dictionary<string, bool>();
        private List<string> _pendingSuccessionTribunalGroups = new List<string>();
        private Dictionary<string, string> _pendingSuccessionTribunalAbdications = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingSuccessionTribunalRewardKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingSuccessionTribunalDestinationKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingSuccessionTribunalExcludedRefugeIds = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingSuccessionTribunalLosingClanIds = new Dictionary<string, string>();
        private Dictionary<string, int> _pendingSuccessionTribunalExileCauseIds = new Dictionary<string, int>();
        private Dictionary<string, string> _pendingSuccessionTribunalCivilWarStartFiefSnapshots = new Dictionary<string, string>();
        private int _nextDeferredExecutionId = 1;
        private List<string> _pendingPostWarExecutionOrder = new List<string>();
        private Dictionary<string, string> _postWarExecutionRewardKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _postWarExecutionVictorHeroIds = new Dictionary<string, string>();
        private Dictionary<string, string> _postWarExecutionCondemnedHeroIds = new Dictionary<string, string>();
        private Dictionary<string, string> _postWarExecutionLosingClanIds = new Dictionary<string, string>();
        private Dictionary<string, string> _postWarExecutionDestinationKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _postWarExecutionExcludedRefugeIds = new Dictionary<string, string>();
        private Dictionary<string, int> _postWarExecutionExileCauseIds = new Dictionary<string, int>();
        private Dictionary<string, bool> _postWarExecutionOldKing = new Dictionary<string, bool>();
        private string _activeTribunalItemKey = string.Empty;
        private List<string> _pendingRebelCleanupOrder = new List<string>();
        private Dictionary<string, string> _pendingRebelCleanupFallbackIds = new Dictionary<string, string>();

        public static bool IsPeaceHandlingSuppressed => _peaceHandlingSuppressionDepth > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnPlayerCharacterChangedEvent.AddNonSerializedListener(this, OnPlayerCharacterChanged);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => RecoverCoalitionBallots());
        }
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_TribunalReactions", ref _tribunalReactions);
            _tribunalReactions = _tribunalReactions ?? new List<CourtTribunalReactionRecord>();
            dataStore.SyncData("BC_CoalitionDeaths", ref _coalitionDeaths);
            dataStore.SyncData("BC_CoalitionSuccessionNotices", ref _coalitionSuccessionNotices);
            dataStore.SyncData("BC_CoalitionNominees", ref _coalitionNominees);
            _coalitionNominees = _coalitionNominees ?? new Dictionary<string, string>();
            _coalitionDeaths = _coalitionDeaths ?? new Dictionary<string, string>();
            _coalitionSuccessionNotices = _coalitionSuccessionNotices ?? new List<string>();
            dataStore.SyncData("BC_CivilWarCollapses", ref _collapses);
            dataStore.SyncData("BC_CivilWarRivalDefeats", ref _rivalDefeats);
            _rivalDefeats = _rivalDefeats ?? new List<CivilWarRivalDefeatRecord>();
            _collapses = _collapses ?? new List<CivilWarCollapseRecord>();
            dataStore.SyncData("BC_PendingSuccessionTribunalAbdications", ref _pendingSuccessionTribunalAbdications);
            dataStore.SyncData("BellumCivile_PendingCaptureResolutionOrder", ref _pendingCaptureResolutionOrder);
            dataStore.SyncData("BellumCivile_CaptureResolutionParentKingdomIds", ref _captureResolutionParentKingdomIds);
            dataStore.SyncData("BellumCivile_CaptureResolutionRebelKingdomIds", ref _captureResolutionRebelKingdomIds);
            dataStore.SyncData("BellumCivile_CaptureResolutionLeaderClanIds", ref _captureResolutionLeaderClanIds);
            dataStore.SyncData("BellumCivile_CaptureResolutionRebelVictory", ref _captureResolutionRebelVictory);
            dataStore.SyncData("BellumCivile_PendingSuccessionCandidates", ref _pendingSuccessionCandidates);
            dataStore.SyncData("BellumCivile_PendingSuccessionExcluded", ref _pendingSuccessionExcluded);
            dataStore.SyncData("BellumCivile_PendingSuccessionCanReplacePlayerRuler", ref _pendingSuccessionCanReplacePlayerRuler);
            dataStore.SyncData("BellumCivile_AuthorizedPlayerSuccessionUntil", ref _authorizedPlayerSuccessionUntil);
            dataStore.SyncData("BellumCivile_AuthorizedPlayerSuccessionCandidateIds", ref _authorizedPlayerSuccessionCandidateIds);
            dataStore.SyncData("BellumCivile_PlayerDeathSuccessionElectionUntil", ref _playerDeathSuccessionElectionUntil);
            dataStore.SyncData("BellumCivile_NextTribunalGroupId", ref _nextTribunalGroupId);
            dataStore.SyncData("BellumCivile_PendingTribunalOrder", ref _pendingTribunalOrder);
            dataStore.SyncData("BellumCivile_TribunalRewardKingdomIds", ref _tribunalRewardKingdomIds);
            dataStore.SyncData("BellumCivile_TribunalDestinationKingdomIds", ref _tribunalDestinationKingdomIds);
            dataStore.SyncData("BellumCivile_TribunalExcludedRefugeIds", ref _tribunalExcludedRefugeIds);
            dataStore.SyncData("BellumCivile_TribunalVictorClanIds", ref _tribunalVictorClanIds);
            dataStore.SyncData("BellumCivile_TribunalLosingClanIds", ref _tribunalLosingClanIds);
            dataStore.SyncData("BellumCivile_TribunalExileCauseIds", ref _tribunalExileCauseIds);
            dataStore.SyncData("BellumCivile_TribunalCivilWarStartFiefSnapshots", ref _tribunalCivilWarStartFiefSnapshots);
            dataStore.SyncData("BellumCivile_TribunalAnyExecuted", ref _tribunalAnyExecuted);
            dataStore.SyncData("BellumCivile_TribunalAnyForgiven", ref _tribunalAnyForgiven);
            dataStore.SyncData("BellumCivile_TribunalAnyExiled", ref _tribunalAnyExiled);
            dataStore.SyncData("BellumCivile_TribunalOldKingExecuted", ref _tribunalOldKingExecuted);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalGroups", ref _pendingSuccessionTribunalGroups);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalRewardKingdomIds", ref _pendingSuccessionTribunalRewardKingdomIds);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalDestinationKingdomIds", ref _pendingSuccessionTribunalDestinationKingdomIds);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalExcludedRefugeIds", ref _pendingSuccessionTribunalExcludedRefugeIds);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalLosingClanIds", ref _pendingSuccessionTribunalLosingClanIds);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalExileCauseIds", ref _pendingSuccessionTribunalExileCauseIds);
            dataStore.SyncData("BellumCivile_PendingSuccessionTribunalCivilWarStartFiefSnapshots", ref _pendingSuccessionTribunalCivilWarStartFiefSnapshots);
            dataStore.SyncData("BellumCivile_NextDeferredExecutionId", ref _nextDeferredExecutionId);
            dataStore.SyncData("BellumCivile_PendingPostWarExecutionOrder", ref _pendingPostWarExecutionOrder);
            dataStore.SyncData("BellumCivile_PostWarExecutionRewardKingdomIds", ref _postWarExecutionRewardKingdomIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionVictorHeroIds", ref _postWarExecutionVictorHeroIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionCondemnedHeroIds", ref _postWarExecutionCondemnedHeroIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionLosingClanIds", ref _postWarExecutionLosingClanIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionDestinationKingdomIds", ref _postWarExecutionDestinationKingdomIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionExcludedRefugeIds", ref _postWarExecutionExcludedRefugeIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionExileCauseIds", ref _postWarExecutionExileCauseIds);
            dataStore.SyncData("BellumCivile_PostWarExecutionOldKing", ref _postWarExecutionOldKing);
            dataStore.SyncData("BellumCivile_PendingRebelCleanupOrder", ref _pendingRebelCleanupOrder);
            dataStore.SyncData("BellumCivile_PendingRebelCleanupFallbackIds", ref _pendingRebelCleanupFallbackIds);
            EnsureCollectionsInitialized();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_pendingSuccessionTribunalAbdications == null) _pendingSuccessionTribunalAbdications = new Dictionary<string, string>();
            if (_pendingCaptureResolutionOrder == null) _pendingCaptureResolutionOrder = new List<string>();
            if (_captureResolutionParentKingdomIds == null) _captureResolutionParentKingdomIds = new Dictionary<string, string>();
            if (_captureResolutionRebelKingdomIds == null) _captureResolutionRebelKingdomIds = new Dictionary<string, string>();
            if (_captureResolutionLeaderClanIds == null) _captureResolutionLeaderClanIds = new Dictionary<string, string>();
            if (_captureResolutionRebelVictory == null) _captureResolutionRebelVictory = new Dictionary<string, bool>();
            if (_pendingSuccessionCandidates == null) _pendingSuccessionCandidates = new Dictionary<string, string>();
            if (_pendingSuccessionExcluded == null) _pendingSuccessionExcluded = new Dictionary<string, string>();
            if (_pendingSuccessionCanReplacePlayerRuler == null) _pendingSuccessionCanReplacePlayerRuler = new Dictionary<string, bool>();
            if (_authorizedPlayerSuccessionUntil == null) _authorizedPlayerSuccessionUntil = new Dictionary<string, CampaignTime>();
            if (_authorizedPlayerSuccessionCandidateIds == null) _authorizedPlayerSuccessionCandidateIds = new Dictionary<string, string>();
            if (_playerDeathSuccessionElectionUntil == null) _playerDeathSuccessionElectionUntil = new Dictionary<string, CampaignTime>();
            if (_pendingTribunalOrder == null) _pendingTribunalOrder = new List<string>();
            if (_tribunalRewardKingdomIds == null) _tribunalRewardKingdomIds = new Dictionary<string, string>();
            if (_tribunalDestinationKingdomIds == null) _tribunalDestinationKingdomIds = new Dictionary<string, string>();
            if (_tribunalExcludedRefugeIds == null) _tribunalExcludedRefugeIds = new Dictionary<string, string>();
            if (_tribunalVictorClanIds == null) _tribunalVictorClanIds = new Dictionary<string, string>();
            if (_tribunalLosingClanIds == null) _tribunalLosingClanIds = new Dictionary<string, string>();
            if (_tribunalExileCauseIds == null) _tribunalExileCauseIds = new Dictionary<string, int>();
            if (_tribunalCivilWarStartFiefSnapshots == null) _tribunalCivilWarStartFiefSnapshots = new Dictionary<string, string>();
            if (_tribunalAnyExecuted == null) _tribunalAnyExecuted = new Dictionary<string, bool>();
            if (_tribunalAnyForgiven == null) _tribunalAnyForgiven = new Dictionary<string, bool>();
            if (_tribunalAnyExiled == null) _tribunalAnyExiled = new Dictionary<string, bool>();
            if (_tribunalOldKingExecuted == null) _tribunalOldKingExecuted = new Dictionary<string, bool>();
            if (_pendingSuccessionTribunalGroups == null) _pendingSuccessionTribunalGroups = new List<string>();
            if (_pendingSuccessionTribunalRewardKingdomIds == null) _pendingSuccessionTribunalRewardKingdomIds = new Dictionary<string, string>();
            if (_pendingSuccessionTribunalDestinationKingdomIds == null) _pendingSuccessionTribunalDestinationKingdomIds = new Dictionary<string, string>();
            if (_pendingSuccessionTribunalExcludedRefugeIds == null) _pendingSuccessionTribunalExcludedRefugeIds = new Dictionary<string, string>();
            if (_pendingSuccessionTribunalLosingClanIds == null) _pendingSuccessionTribunalLosingClanIds = new Dictionary<string, string>();
            if (_pendingSuccessionTribunalExileCauseIds == null) _pendingSuccessionTribunalExileCauseIds = new Dictionary<string, int>();
            if (_pendingSuccessionTribunalCivilWarStartFiefSnapshots == null) _pendingSuccessionTribunalCivilWarStartFiefSnapshots = new Dictionary<string, string>();
            if (_nextDeferredExecutionId < 1) _nextDeferredExecutionId = 1;
            if (_pendingPostWarExecutionOrder == null) _pendingPostWarExecutionOrder = new List<string>();
            if (_postWarExecutionRewardKingdomIds == null) _postWarExecutionRewardKingdomIds = new Dictionary<string, string>();
            if (_postWarExecutionVictorHeroIds == null) _postWarExecutionVictorHeroIds = new Dictionary<string, string>();
            if (_postWarExecutionCondemnedHeroIds == null) _postWarExecutionCondemnedHeroIds = new Dictionary<string, string>();
            if (_postWarExecutionLosingClanIds == null) _postWarExecutionLosingClanIds = new Dictionary<string, string>();
            if (_postWarExecutionDestinationKingdomIds == null) _postWarExecutionDestinationKingdomIds = new Dictionary<string, string>();
            if (_postWarExecutionExcludedRefugeIds == null) _postWarExecutionExcludedRefugeIds = new Dictionary<string, string>();
            if (_postWarExecutionExileCauseIds == null) _postWarExecutionExileCauseIds = new Dictionary<string, int>();
            if (_postWarExecutionOldKing == null) _postWarExecutionOldKing = new Dictionary<string, bool>();
            if (_pendingRebelCleanupOrder == null) _pendingRebelCleanupOrder = new List<string>();
            if (_pendingRebelCleanupFallbackIds == null) _pendingRebelCleanupFallbackIds = new Dictionary<string, string>();
        }

        public bool QueueDeferredSuccessionVote(Kingdom kingdom, Clan candidateClan, Clan excludedClan, bool canReplacePlayerRuler = false)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || kingdom.IsEliminated || candidateClan == null || candidateClan.IsEliminated) return false;
            if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)) return false;
            if (candidateClan.Kingdom != kingdom || candidateClan == excludedClan) return false;
            if (!NobleClanEligibilityHelper.IsValidRulingClan(candidateClan, kingdom)) return false;

            _pendingSuccessionCandidates[kingdom.StringId] = candidateClan.StringId;
            _pendingSuccessionExcluded[kingdom.StringId] = excludedClan?.StringId ?? string.Empty;
            _pendingSuccessionCanReplacePlayerRuler[kingdom.StringId] = canReplacePlayerRuler;
            BellumCivileLogger.Log($"Queued deferred succession election; kingdom={kingdom.StringId}; candidate={candidateClan.StringId}; excluded={excludedClan?.StringId ?? "none"}; can_replace_player={canReplacePlayerRuler}.");
            return true;
        }

        public bool TryBeginAcceptedAbdicationVote(Kingdom kingdom, Clan candidateClan, Clan formerRulingClan)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || formerRulingClan == null || kingdom.RulingClan != formerRulingClan)
                return false;

            if (ElectiveSuccessionBehavior.UsesElection(kingdom))
                return ElectiveSuccessionBehavior.Instance?.BeginDeposition(kingdom, candidateClan,
                    ElectiveSuccessionBehavior.LegalHead(formerRulingClan)) == true;

            ClearSuccessionStateForKingdom(kingdom, "accepted abdication ultimatum");
            if (!QueueDeferredSuccessionVote(
                kingdom,
                candidateClan,
                formerRulingClan,
                canReplacePlayerRuler: true))
            {
                return false;
            }

            ProcessPendingSuccessionVote(kingdom.StringId);

            return kingdom.RulingClan != formerRulingClan
                || _pendingSuccessionCandidates.ContainsKey(kingdom.StringId)
                || kingdom.UnresolvedDecisions
                    .OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>()
                    .Any(decision => decision.ProposerClan == candidateClan);
        }

        public bool IsPlayerRulerReplacementAuthorized(Kingdom kingdom)
        {
            return IsPlayerRulerReplacementAuthorized(kingdom, null);
        }

        public bool IsPlayerRulerReplacementAuthorized(Kingdom kingdom, Clan candidateClan)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return false;

            string kingdomId = kingdom.StringId;
            bool pendingAuthorized = _pendingSuccessionCanReplacePlayerRuler.TryGetValue(kingdomId, out bool canReplacePlayer)
                                  && canReplacePlayer
                                  && IsAuthorizedPlayerSuccessionCandidate(kingdomId, candidateClan, _pendingSuccessionCandidates);
            bool activeAuthorized = _authorizedPlayerSuccessionUntil.TryGetValue(kingdomId, out CampaignTime authorizedUntil)
                                 && authorizedUntil.IsFuture
                                 && IsAuthorizedPlayerSuccessionCandidate(kingdomId, candidateClan, _authorizedPlayerSuccessionCandidateIds);
            return pendingAuthorized || activeAuthorized;
        }

        public bool IsPlayerRulerReplacementElectionAuthorized(
            Kingdom kingdom,
            TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision decision)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || decision == null || decision.Kingdom != kingdom)
                return false;

            string kingdomId = kingdom.StringId;
            if (!_authorizedPlayerSuccessionUntil.TryGetValue(kingdomId, out CampaignTime authorizedUntil)
                || !authorizedUntil.IsFuture
                || !_authorizedPlayerSuccessionCandidateIds.TryGetValue(kingdomId, out string authorizedProposerId)
                || string.IsNullOrEmpty(authorizedProposerId))
            {
                return false;
            }

            return string.Equals(decision.ProposerClan?.StringId, authorizedProposerId, System.StringComparison.Ordinal);
        }

        public void CompletePlayerRulerReplacementElection(
            Kingdom kingdom,
            TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision decision)
        {
            if (!IsPlayerRulerReplacementElectionAuthorized(kingdom, decision))
                return;

            string kingdomId = kingdom.StringId;
            _authorizedPlayerSuccessionUntil.Remove(kingdomId);
            _authorizedPlayerSuccessionCandidateIds.Remove(kingdomId);
            BellumCivileLogger.Log($"Consumed authorized player-ruler replacement election; kingdom={kingdomId}; proposer={decision.ProposerClan?.StringId ?? "null"}.");
        }

        private static bool IsAuthorizedPlayerSuccessionCandidate(string kingdomId, Clan candidateClan, Dictionary<string, string> candidateIds)
        {
            if (string.IsNullOrEmpty(kingdomId) || candidateIds == null)
                return false;

            if (!candidateIds.TryGetValue(kingdomId, out string authorizedCandidateId)
                || string.IsNullOrEmpty(authorizedCandidateId))
            {
                return false;
            }

            return candidateClan == null || authorizedCandidateId == candidateClan.StringId;
        }

        public bool IsPlayerDeathSuccessionElectionAuthorized(Kingdom kingdom, TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision decision)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || decision == null || decision.Kingdom != kingdom)
                return false;

            if (!_playerDeathSuccessionElectionUntil.TryGetValue(kingdom.StringId, out CampaignTime authorizedUntil)
                || authorizedUntil.IsPast)
            {
                return false;
            }

            return decision.ProposerClan == Clan.PlayerClan
                && Clan.PlayerClan?.Kingdom == kingdom
                && kingdom.RulingClan == Clan.PlayerClan;
        }

        private void OnPlayerCharacterChanged(Hero oldPlayer, Hero newPlayer, MobileParty newMainParty, bool isMainPartyChanged)
        {
            EnsureCollectionsInitialized();
            if (oldPlayer == null || newPlayer == null || oldPlayer == newPlayer)
                return;

            if (Clan.PlayerClan == null || oldPlayer.Clan != Clan.PlayerClan || newPlayer.Clan != Clan.PlayerClan)
                return;

            if (oldPlayer.IsAlive && !oldPlayer.IsDead)
                return;

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan != Clan.PlayerClan)
                return;

            _playerDeathSuccessionElectionUntil[kingdom.StringId] = CampaignTime.Now + CampaignTime.Days(7f);
            BellumCivileLogger.Log($"Opened player-death succession election window for {kingdom.StringId}; old_player={oldPlayer.StringId}; new_player={newPlayer.StringId}.");
        }

        public void ClearUnauthorizedPlayerSuccessionForKingdom(Kingdom kingdom, string reason = null)
        {
            ClearSuccessionStateForKingdom(kingdom, reason);
        }

        public void ClearSuccessionStateForKingdom(Kingdom kingdom, string reason = null)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return;

            string kingdomId = kingdom.StringId;
            bool removedDecision = false;
            foreach (var decision in kingdom.UnresolvedDecisions
                .OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>()
                .ToList())
            {
                kingdom.RemoveDecision(decision);
                removedDecision = true;
            }

            bool removedPending = _pendingSuccessionCandidates.ContainsKey(kingdomId);
            if (removedPending)
                RemovePendingSuccessionVote(kingdomId);

            bool removedAuthorization = _authorizedPlayerSuccessionUntil.Remove(kingdomId);
            _authorizedPlayerSuccessionCandidateIds.Remove(kingdomId);
            _playerDeathSuccessionElectionUntil.Remove(kingdomId);

            if (removedDecision || removedPending || removedAuthorization)
            {
                BellumCivileLogger.Log($"Cleared succession state for {kingdomId}; reason={reason ?? "unknown"}; removed_decision={removedDecision}; removed_pending={removedPending}; removed_authorization={removedAuthorization}.");
            }
        }

        private static void RunWithoutPeaceHandling(System.Action action)
        {
            _peaceHandlingSuppressionDepth++;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _peaceHandlingSuppressionDepth--;
            }
        }

        private static void ActivateCompatibilityRepairWindow()
        {
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.ActivateCompatibilityRepairWindow();
        }

        private static void ClearTemporaryKingdomRepair(Kingdom kingdom)
        {
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.ClearTemporaryKingdomRepair(kingdom);
        }

        private static void ClearTemporaryKingdomRepair(string kingdomId)
        {
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.ClearTemporaryKingdomRepair(kingdomId);
        }

        private static void RunWithRulerRepairSuppressed(Kingdom kingdom, System.Action action)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager != null)
                factionManager.RunWithRulerRepairSuppressed(kingdom, action);
            else
                action?.Invoke();
        }

        private static T RunWithRulerRepairSuppressed<T>(Kingdom kingdom, System.Func<T> action)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager != null
                ? factionManager.RunWithRulerRepairSuppressed(kingdom, action)
                : action != null ? action() : default(T);
        }

        private static void RepairCompatibilityStateNow(string reason)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            factionManager.ActivateCompatibilityRepairWindow();
            factionManager.RunCompatibilityRepairPass(reason);
        }

        private static Kingdom ResolveTrackedRebelKingdom(FactionObject faction, Kingdom preferredKingdom = null, bool allowEliminated = false)
        {
            if (faction == null || faction.IsIdeology) return null;

            if (preferredKingdom != null
                && preferredKingdom != faction.ParentKingdom
                && (allowEliminated || !preferredKingdom.IsEliminated)
                && (faction.IsTrackedRebelKingdom(preferredKingdom)
                    || (!faction.HasTrackedRebelKingdom && faction.Leader?.Kingdom == preferredKingdom)))
            {
                return preferredKingdom;
            }

            Kingdom trackedKingdom = allowEliminated
                ? faction.GetTrackedRebelKingdomIncludingEliminated()
                : faction.GetRebelKingdom();
            if (trackedKingdom != null
                && trackedKingdom != faction.ParentKingdom
                && (allowEliminated || !trackedKingdom.IsEliminated))
            {
                return trackedKingdom;
            }

            return null;
        }

        private static Kingdom ResolveKingdomById(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return null;
            return Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
        }

        private static Clan ResolveClanById(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return null;
            return Clan.All.FirstOrDefault(c => c.StringId == clanId);
        }

        private static Hero ResolveHeroById(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
        }

        private static int CountStrongholds(Kingdom kingdom)
        {
            return kingdom?.Fiefs.Count(f => f != null && (f.IsTown || f.IsCastle)) ?? 0;
        }

        private static void TransferClansToKingdom(Kingdom sourceKingdom, Kingdom targetKingdom, Clan preferredRuler = null)
        {
            if (sourceKingdom == null || targetKingdom == null) return;

            RunWithRulerRepairSuppressed(sourceKingdom, () =>
            {
                List<Clan> clansToTransfer = sourceKingdom.Clans.ToList();
                if (preferredRuler != null && clansToTransfer.Remove(preferredRuler))
                {
                    KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(preferredRuler, targetKingdom, showNotification: false);
                    EnsureValidRulingClan(targetKingdom, preferredRuler, forcePreferred: true);
                }

                foreach (Clan clan in clansToTransfer)
                    KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, targetKingdom, showNotification: false);

                EnsureValidRulingClan(targetKingdom, preferredRuler, forcePreferred: preferredRuler != null);
            });
        }

        private static string BuildCaptureResolutionKey(string parentKingdomId, string rebelKingdomId)
        {
            return parentKingdomId + "|" + rebelKingdomId;
        }

        private static bool DelimitedStringContains(string delimitedIds, string targetId)
        {
            if (string.IsNullOrEmpty(delimitedIds) || string.IsNullOrEmpty(targetId))
                return false;

            return delimitedIds
                .Split(',')
                .Any(id => id == targetId);
        }

        private void QueueDeferredCaptureResolution(FactionObject faction, Kingdom rebelKingdom, bool rebelVictory)
        {
            EnsureCollectionsInitialized();
            if (faction == null || faction.ParentKingdom == null || rebelKingdom == null) return;

            string key = BuildCaptureResolutionKey(faction.ParentKingdom.StringId, rebelKingdom.StringId);
            _captureResolutionParentKingdomIds[key] = faction.ParentKingdom.StringId;
            _captureResolutionRebelKingdomIds[key] = rebelKingdom.StringId;
            _captureResolutionLeaderClanIds[key] = faction.Leader?.StringId ?? string.Empty;
            _captureResolutionRebelVictory[key] = rebelVictory;

            if (!_pendingCaptureResolutionOrder.Contains(key))
                _pendingCaptureResolutionOrder.Add(key);
        }

        private void RemovePendingCaptureResolution(string key)
        {
            _pendingCaptureResolutionOrder.Remove(key);
            _captureResolutionParentKingdomIds.Remove(key);
            _captureResolutionRebelKingdomIds.Remove(key);
            _captureResolutionLeaderClanIds.Remove(key);
            _captureResolutionRebelVictory.Remove(key);
        }

        private void RemovePendingSuccessionVote(string kingdomId)
        {
            _pendingSuccessionCandidates.Remove(kingdomId);
            _pendingSuccessionExcluded.Remove(kingdomId);
            _pendingSuccessionCanReplacePlayerRuler.Remove(kingdomId);
        }

        private void QueueDeferredSuccessionTribunal(Kingdom rewardKingdom, Kingdom destinationKingdom, IEnumerable<Clan> losingClans, Kingdom excludedRefuge, ExileCause exileCause = ExileCause.Unknown, string civilWarStartFiefSnapshot = null, string abdicationCauseId = null)
        {
            EnsureCollectionsInitialized();
            if (rewardKingdom == null || rewardKingdom.IsEliminated || destinationKingdom == null || destinationKingdom.IsEliminated)
                return;

            string losingClanIds = string.Join(",",
                FilterPendingSuccessionTribunalLosingClans(losingClans)
                    .Select(c => c.StringId)
                    .Distinct()
                    .ToList());

            if (string.IsNullOrEmpty(losingClanIds))
                return;

            string groupId = "succession_" + (_nextTribunalGroupId++);
            BeginTribunalReactions(groupId, rewardKingdom, losingClanIds.Split(',').Select(ResolveClanById));
            if (abdicationCauseId != null) _pendingSuccessionTribunalAbdications[groupId] = abdicationCauseId;
            _pendingSuccessionTribunalRewardKingdomIds[groupId] = rewardKingdom.StringId;
            _pendingSuccessionTribunalDestinationKingdomIds[groupId] = destinationKingdom.StringId;
            _pendingSuccessionTribunalExcludedRefugeIds[groupId] = excludedRefuge?.StringId ?? string.Empty;
            _pendingSuccessionTribunalLosingClanIds[groupId] = losingClanIds;
            _pendingSuccessionTribunalExileCauseIds[groupId] = (int)exileCause;
            _pendingSuccessionTribunalCivilWarStartFiefSnapshots[groupId] = civilWarStartFiefSnapshot ?? string.Empty;

            if (!_pendingSuccessionTribunalGroups.Contains(groupId))
                _pendingSuccessionTribunalGroups.Add(groupId);
        }

        private void RemovePendingSuccessionTribunal(string groupId, bool handedOff = false)
        {
            if (string.IsNullOrEmpty(groupId)) return;
            if (!handedOff) CloseTribunalReactions(groupId, apply: false);

            _pendingSuccessionTribunalGroups.Remove(groupId);
            _pendingSuccessionTribunalAbdications.Remove(groupId);
            _pendingSuccessionTribunalRewardKingdomIds.Remove(groupId);
            _pendingSuccessionTribunalDestinationKingdomIds.Remove(groupId);
            _pendingSuccessionTribunalExcludedRefugeIds.Remove(groupId);
            _pendingSuccessionTribunalLosingClanIds.Remove(groupId);
            _pendingSuccessionTribunalExileCauseIds.Remove(groupId);
            _pendingSuccessionTribunalCivilWarStartFiefSnapshots.Remove(groupId);
        }

        internal bool HasPendingPostWarJudgments(Kingdom realm)
        {
            EnsureCollectionsInitialized();
            if (realm == null) return false;
            string id = realm.StringId;
            return _pendingSuccessionTribunalRewardKingdomIds.ContainsValue(id)
                || _tribunalRewardKingdomIds.ContainsValue(id)
                || _postWarExecutionRewardKingdomIds.ContainsValue(id);
        }

        private void ProcessPendingSuccessionTribunals()
        {
            EnsureCollectionsInitialized();
            if (_pendingSuccessionTribunalGroups.Count == 0) return;

            foreach (string groupId in _pendingSuccessionTribunalGroups.ToList())
            {
                Kingdom rewardKingdom = ResolveKingdomById(GetDictionaryValue(_pendingSuccessionTribunalRewardKingdomIds, groupId));
                Kingdom destinationKingdom = ResolveKingdomById(GetDictionaryValue(_pendingSuccessionTribunalDestinationKingdomIds, groupId));
                Kingdom excludedRefuge = ResolveKingdomById(GetDictionaryValue(_pendingSuccessionTribunalExcludedRefugeIds, groupId));
                string losingClanIds = GetDictionaryValue(_pendingSuccessionTribunalLosingClanIds, groupId);
                ExileCause exileCause = GetStoredExileCause(_pendingSuccessionTribunalExileCauseIds, groupId);
                string civilWarStartFiefSnapshot = GetDictionaryValue(_pendingSuccessionTribunalCivilWarStartFiefSnapshots, groupId);

                if (rewardKingdom == null || rewardKingdom.IsEliminated || destinationKingdom == null || destinationKingdom.IsEliminated)
                {
                    RemovePendingSuccessionTribunal(groupId);
                    continue;
                }

                if (rewardKingdom.UnresolvedDecisions.OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>().Any())
                    continue;
                if (CrownAccessionBehavior.Instance?.IsPending(rewardKingdom) == true)
                    continue;
                var deposition = ElectiveSuccessionBehavior.Instance?.PendingDeposition(rewardKingdom);
                if (deposition != null && (!deposition.InterimPrepared || rewardKingdom.RulingClan != deposition.InterimHouse))
                    continue;
                if (_pendingSuccessionTribunalAbdications.TryGetValue(groupId, out string causeId)
                    && CrownAccessionBehavior.Instance?.HasCompletedForcedAccession(rewardKingdom, causeId) != true)
                    continue;

                EnsureValidRulingClan(rewardKingdom);
                Clan crownedRuler = rewardKingdom.RulingClan;
                if (crownedRuler == null || crownedRuler.IsEliminated || crownedRuler.Kingdom != rewardKingdom)
                    continue;

                List<Clan> losingClans = losingClanIds
                    .Split(',')
                    .Select(ResolveClanById)
                    .Where(c => IsValidTribunalLosingClan(c, rewardKingdom, crownedRuler))
                    .Distinct()
                    .ToList();

                if (losingClans.Count == 0)
                {
                    RemovePendingSuccessionTribunal(groupId);
                    continue;
                }

                if (CanPlayerJudgeTribunal(rewardKingdom, crownedRuler))
                {
                    QueuePlayerTribunal(rewardKingdom, destinationKingdom, losingClans, excludedRefuge, crownedRuler, exileCause, civilWarStartFiefSnapshot, groupId);
                }
                else
                {
                    ApplyPostWarConsequences(rewardKingdom, losingClans, destinationKingdom, out bool anyExec, out bool anyForgive, out bool anyExile, out bool oldKingExec, excludedRefuge, exileCause, civilWarStartFiefSnapshot, groupId);
                }

                RemovePendingSuccessionTribunal(groupId, handedOff: true);
            }
        }

        private void OnDailyTick()
        {
            PurgeSuccessfulIndependenceJudgments();
            PurgeUnauthorizedPlayerSuccessionState();
            ProcessPendingSuccessionVotes();
            ProcessPendingSuccessionTribunals();
            ProcessPendingTribunals();
            ProcessPendingRebelCleanups();
        }

        private void OnHourlyTick()
        {
            ProcessCoalitionSuccessions();
            ResumePendingCollapses();
            ResumePendingRivalDefeats();
            PurgeSuccessfulIndependenceJudgments();
            ProcessPendingPostWarExecution();
            FlushTribunalReactions();
        }

        private void PurgeSuccessfulIndependenceJudgments()
        {
            EnsureCollectionsInitialized();
            foreach (string groupId in _tribunalExileCauseIds
                .Where(entry => (ExileCause)entry.Value == ExileCause.LoyalistIndependence)
                .Select(entry => entry.Key).ToList())
            {
                _pendingTribunalOrder.RemoveAll(key => key.StartsWith(groupId + "|", System.StringComparison.Ordinal));
                if (_activeTribunalItemKey.StartsWith(groupId + "|", System.StringComparison.Ordinal))
                    _activeTribunalItemKey = string.Empty;
                FinalizeTribunalGroup(groupId, applyShocks: false);
                BellumCivileLogger.Log($"Cancelled obsolete successful-independence tribunal; group={groupId}.");
            }
            foreach (string groupId in _pendingSuccessionTribunalExileCauseIds
                .Where(entry => (ExileCause)entry.Value == ExileCause.LoyalistIndependence)
                .Select(entry => entry.Key).ToList())
                RemovePendingSuccessionTribunal(groupId);
            foreach (string executionId in _postWarExecutionExileCauseIds
                .Where(entry => (ExileCause)entry.Value == ExileCause.LoyalistIndependence)
                .Select(entry => entry.Key).ToList())
            {
                RemovePendingPostWarExecution(executionId);
                BellumCivileLogger.Log($"Cancelled obsolete successful-independence execution; id={executionId}.");
            }
        }

        private void PurgeUnauthorizedPlayerSuccessionState()
        {
            EnsureCollectionsInitialized();

            foreach (string kingdomId in _authorizedPlayerSuccessionUntil.Keys.ToList())
            {
                if (_authorizedPlayerSuccessionUntil[kingdomId].IsPast)
                {
                    _authorizedPlayerSuccessionUntil.Remove(kingdomId);
                    _authorizedPlayerSuccessionCandidateIds.Remove(kingdomId);
                }
            }

            foreach (string kingdomId in _playerDeathSuccessionElectionUntil.Keys.ToList())
            {
                if (_playerDeathSuccessionElectionUntil[kingdomId].IsPast)
                    _playerDeathSuccessionElectionUntil.Remove(kingdomId);
            }

            foreach (Kingdom kingdom in Kingdom.All
                .Where(k => k != null
                         && !k.IsEliminated
                         && k.RulingClan == Clan.PlayerClan)
                .ToList())
            {
                string kingdomId = kingdom.StringId;
                bool pendingAuthorized = _pendingSuccessionCanReplacePlayerRuler.TryGetValue(kingdomId, out bool canReplacePlayer)
                                      && canReplacePlayer
                                      && _pendingSuccessionCandidates.ContainsKey(kingdomId);
                bool activeAuthorized = _authorizedPlayerSuccessionUntil.TryGetValue(kingdomId, out CampaignTime authorizedUntil)
                                     && authorizedUntil.IsFuture
                                     && _authorizedPlayerSuccessionCandidateIds.ContainsKey(kingdomId);

                if (pendingAuthorized || activeAuthorized)
                    continue;

                bool removedDecision = false;
                foreach (var decision in kingdom.UnresolvedDecisions
                    .OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>()
                    .ToList())
                {
                    if (IsCoalitionSuccession(kingdom) || IsPlayerDeathSuccessionElectionAuthorized(kingdom, decision))
                        continue;

                    kingdom.RemoveDecision(decision);
                    removedDecision = true;
                }

                if (removedDecision)
                    BellumCivileLogger.Log($"Removed unauthorized king-selection decision from player-ruled kingdom {kingdomId}.");

                if (_pendingSuccessionCandidates.ContainsKey(kingdomId))
                {
                    RemovePendingSuccessionVote(kingdomId);
                    BellumCivileLogger.Log($"Removed legacy pending succession that would have replaced the player ruler in {kingdomId}.");
                }
            }
        }

        public bool IsKingdomIdReferencedByBellumCivileState(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return false;
            if (IsKingdomIdReferencedByResolutionState(kingdomId)) return true;

            Kingdom kingdom = ResolveKingdomById(kingdomId);
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (kingdom == null || factionManager == null) return false;

            return factionManager.GetFactionByRebelKingdom(kingdom) != null
                || factionManager.GetFactionsInKingdom(kingdom).Any(f => !f.IsIdeology);
        }

        public bool IsClanIdReferencedByBellumCivileState(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return false;
            if (IsClanIdReferencedByResolutionState(clanId)) return true;

            Clan clan = ResolveClanById(clanId);
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (clan == null || factionManager == null) return false;

            return factionManager.GetRebelFaction(clan) != null
                || factionManager.GetIdeologicalFaction(clan) != null;
        }

        public Clan GetExpectedRulerForReferencedKingdom(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId))
                return null;

            Kingdom kingdom = ResolveKingdomById(kingdomId);
            if (kingdom?.RulingClan == Clan.PlayerClan
                && (!_pendingSuccessionCanReplacePlayerRuler.TryGetValue(kingdomId, out bool canReplacePlayer) || !canReplacePlayer))
            {
                return kingdom.RulingClan;
            }

            if (_pendingSuccessionCandidates.TryGetValue(kingdomId, out string pendingCandidateId))
                return ResolveClanById(pendingCandidateId);

            return null;
        }

        private bool IsKingdomIdReferencedByResolutionState(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return false;

            return _captureResolutionParentKingdomIds.Values.Contains(kingdomId)
                || _captureResolutionRebelKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionCandidates.Keys.Contains(kingdomId)
                || _tribunalRewardKingdomIds.Values.Contains(kingdomId)
                || _tribunalDestinationKingdomIds.Values.Contains(kingdomId)
                || _tribunalExcludedRefugeIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalRewardKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalDestinationKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalExcludedRefugeIds.Values.Contains(kingdomId)
                || _pendingRebelCleanupOrder.Contains(kingdomId)
                || _pendingRebelCleanupFallbackIds.Values.Contains(kingdomId);
        }

        private bool IsKingdomIdReferencedByResolutionStateOutsidePendingCleanup(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return false;

            return _captureResolutionParentKingdomIds.Values.Contains(kingdomId)
                || _captureResolutionRebelKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionCandidates.Keys.Contains(kingdomId)
                || _tribunalRewardKingdomIds.Values.Contains(kingdomId)
                || _tribunalDestinationKingdomIds.Values.Contains(kingdomId)
                || _tribunalExcludedRefugeIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalRewardKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalDestinationKingdomIds.Values.Contains(kingdomId)
                || _pendingSuccessionTribunalExcludedRefugeIds.Values.Contains(kingdomId)
                || _pendingRebelCleanupFallbackIds.Values.Contains(kingdomId);
        }

        private bool IsClanIdReferencedByResolutionState(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return false;

            return _captureResolutionLeaderClanIds.Values.Contains(clanId)
                || _pendingSuccessionCandidates.Values.Contains(clanId)
                || _pendingSuccessionExcluded.Values.Contains(clanId)
                || _tribunalVictorClanIds.Values.Contains(clanId)
                || _tribunalLosingClanIds.Values.Any(ids => DelimitedStringContains(ids, clanId))
                || _pendingSuccessionTribunalLosingClanIds.Values.Any(ids => DelimitedStringContains(ids, clanId));
        }

        private static void ApplyCivilWarPeaceIfNeeded(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            if (firstKingdom == null || secondKingdom == null || firstKingdom == secondKingdom) return;
            if (firstKingdom.IsEliminated || secondKingdom.IsEliminated) return;
            if (!firstKingdom.IsAtWarWith(secondKingdom)) return;

            RunWithoutPeaceHandling(() =>
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => MakePeaceAction.Apply(firstKingdom, secondKingdom)));
        }

        private static void CompleteCivilWarTracker(FactionObject faction, Kingdom rebelKingdom, string reason)
        {
            CivilWarConflictBehavior.Instance?.Complete(faction);
            Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()
                ?.CompleteCivilWar(faction, rebelKingdom, reason);
        }

        private void QueuePendingRebelCleanup(Kingdom rebelKingdom, Kingdom fallbackKingdom)
        {
            EnsureCollectionsInitialized();
            if (rebelKingdom == null || rebelKingdom.IsEliminated) return;

            string rebelKingdomId = rebelKingdom.StringId;
            _pendingRebelCleanupFallbackIds[rebelKingdomId] = fallbackKingdom?.StringId ?? string.Empty;

            if (!_pendingRebelCleanupOrder.Contains(rebelKingdomId))
                _pendingRebelCleanupOrder.Add(rebelKingdomId);
        }

        private void RemovePendingRebelCleanup(string rebelKingdomId)
        {
            if (string.IsNullOrEmpty(rebelKingdomId)) return;

            _pendingRebelCleanupOrder.Remove(rebelKingdomId);
            _pendingRebelCleanupFallbackIds.Remove(rebelKingdomId);
        }

        private void ProcessPendingRebelCleanups()
        {
            EnsureCollectionsInitialized();
            if (_pendingRebelCleanupOrder.Count == 0) return;

            foreach (string rebelKingdomId in _pendingRebelCleanupOrder.ToList())
            {
                Kingdom rebelKingdom = ResolveKingdomById(rebelKingdomId);
                if (rebelKingdom == null || rebelKingdom.IsEliminated)
                {
                    ClearTemporaryKingdomRepair(rebelKingdomId);
                    RemovePendingRebelCleanup(rebelKingdomId);
                    continue;
                }

                if (IsKingdomIdReferencedByResolutionStateOutsidePendingCleanup(rebelKingdomId)) continue;

                Kingdom fallbackKingdom = null;
                if (_pendingRebelCleanupFallbackIds.TryGetValue(rebelKingdomId, out string fallbackKingdomId))
                    fallbackKingdom = ResolveKingdomById(fallbackKingdomId);

                if (DrainAndDestroyRebelKingdom(rebelKingdom, fallbackKingdom, allowDeferred: false))
                    RemovePendingRebelCleanup(rebelKingdomId);
            }
        }

        private void ProcessPendingCaptureResolutions()
        {
            EnsureCollectionsInitialized();
            if (_pendingCaptureResolutionOrder.Count == 0) return;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            foreach (string key in _pendingCaptureResolutionOrder.ToList())
            {
                if (!_captureResolutionParentKingdomIds.TryGetValue(key, out string parentKingdomId)
                    || !_captureResolutionRebelKingdomIds.TryGetValue(key, out string rebelKingdomId)
                    || !_captureResolutionRebelVictory.TryGetValue(key, out bool rebelVictory))
                {
                    RemovePendingCaptureResolution(key);
                    continue;
                }

                Kingdom parentKingdom = ResolveKingdomById(parentKingdomId);
                Kingdom rebelKingdom = ResolveKingdomById(rebelKingdomId);
                FactionObject faction = factionManager.GetFactionByRebelKingdom(rebelKingdom);

                if (faction == null && parentKingdom != null)
                {
                    faction = factionManager.GetFactionsInKingdom(parentKingdom)
                        .FirstOrDefault(f => !f.IsIdeology
                                          && (f.IsTrackedRebelKingdom(rebelKingdom)
                                              || (_captureResolutionLeaderClanIds.TryGetValue(key, out string leaderClanId)
                                                  && !string.IsNullOrEmpty(leaderClanId)
                                                  && f.Leader?.StringId == leaderClanId)));
                }

                if (faction == null)
                {
                    if (parentKingdom != null && parentKingdom.IsEliminated)
                    {
                        HandleDestroyedParentKingdom(parentKingdom);
                    }

                    RemovePendingCaptureResolution(key);
                    continue;
                }

                Kingdom resolvedRebelKingdom = ResolveTrackedRebelKingdom(faction, rebelKingdom) ?? rebelKingdom;
                if (resolvedRebelKingdom == null)
                {
                    if (faction.ParentKingdom != null && faction.ParentKingdom.IsEliminated)
                    {
                        HandleDestroyedParentKingdom(faction.ParentKingdom);
                    }
                    else
                    {
                        CleanupExternallyResolvedFaction(faction);
                    }

                    RemovePendingCaptureResolution(key);
                    continue;
                }

                if (rebelVictory && IsCoalitionSuccession(resolvedRebelKingdom)) continue;
                if (rebelVictory)
                    ResolveRebelVictory(faction, resolvedRebelKingdom);
                else
                    ResolveLiegeVictory(faction, resolvedRebelKingdom);

                RemovePendingCaptureResolution(key);
            }
        }

        private void ProcessPendingSuccessionVotes()
        {
            EnsureCollectionsInitialized();
            if (_pendingSuccessionCandidates.Count == 0) return;

            foreach (string kingdomId in _pendingSuccessionCandidates.Keys.ToList())
                ProcessPendingSuccessionVote(kingdomId);
        }

        private void ProcessPendingSuccessionVote(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId) || !_pendingSuccessionCandidates.ContainsKey(kingdomId))
                return;

            Kingdom kingdom = ResolveKingdomById(kingdomId);
            Clan candidateClan = ResolveClanById(_pendingSuccessionCandidates[kingdomId]);
            bool canReplacePlayerRuler = _pendingSuccessionCanReplacePlayerRuler.TryGetValue(kingdomId, out bool storedCanReplacePlayer)
                                       && storedCanReplacePlayer;

            if (kingdom == null || kingdom.IsEliminated)
            {
                RemovePendingSuccessionVote(kingdomId);
                return;
            }

            if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
            {
                RemovePendingSuccessionVote(kingdomId);
                BellumCivileLogger.Log($"Removed deferred succession election from temporary realm; kingdom={kingdomId}.");
                return;
            }

            Clan excludedClan = null;
            if (_pendingSuccessionExcluded.TryGetValue(kingdomId, out string excludedClanId))
                excludedClan = ResolveClanById(excludedClanId);

            if (excludedClan == null || excludedClan.IsEliminated || excludedClan.Kingdom != kingdom)
                excludedClan = kingdom.RulingClan;

            if (candidateClan == null
                || candidateClan.IsEliminated
                || candidateClan.Kingdom != kingdom
                || candidateClan == excludedClan
                || !NobleClanEligibilityHelper.IsValidRulingClan(candidateClan, kingdom))
            {
                candidateClan = kingdom.Clans
                    .Where(clan => clan != excludedClan && NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom))
                    .OrderByDescending(clan => clan.Renown)
                    .FirstOrDefault();

                if (candidateClan == null)
                {
                    RemovePendingSuccessionVote(kingdomId);
                    BellumCivileLogger.Log($"Discarded deferred succession election without an eligible replacement; kingdom={kingdomId}; excluded={excludedClan?.StringId ?? "none"}.");
                    return;
                }

                _pendingSuccessionCandidates[kingdomId] = candidateClan.StringId;
                BellumCivileLogger.Log($"Replaced invalid deferred succession candidate; kingdom={kingdomId}; candidate={candidateClan.StringId}; excluded={excludedClan?.StringId ?? "none"}.");
            }

            if (kingdom.RulingClan == Clan.PlayerClan && candidateClan != Clan.PlayerClan && !canReplacePlayerRuler)
            {
                foreach (var decision in kingdom.UnresolvedDecisions
                    .OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>()
                    .ToList())
                {
                    kingdom.RemoveDecision(decision);
                }

                RemovePendingSuccessionVote(kingdomId);
                BellumCivileLogger.Log($"Blocked unauthorized pending succession in player-ruled kingdom {kingdomId}; candidate={candidateClan.StringId}.");
                return;
            }

            if (kingdom.RulingClan == candidateClan)
            {
                RemovePendingSuccessionVote(kingdomId);
                return;
            }

            if (kingdom.UnresolvedDecisions.OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>().Any())
                return;

            if (ElectiveSuccessionBehavior.UsesElection(kingdom))
            {
                if (ElectiveSuccessionBehavior.Instance?.BeginDeposition(kingdom, candidateClan,
                    ElectiveSuccessionBehavior.LegalHead(excludedClan)) == true)
                    RemovePendingSuccessionVote(kingdomId);
                return;
            }

            if (excludedClan == null || excludedClan == candidateClan)
            {
                RemovePendingSuccessionVote(kingdomId);
                return;
            }

            if (kingdom.RulingClan == Clan.PlayerClan && candidateClan != Clan.PlayerClan && canReplacePlayerRuler)
            {
                _authorizedPlayerSuccessionUntil[kingdomId] = CampaignTime.Now + CampaignTime.Days(7);
                _authorizedPlayerSuccessionCandidateIds[kingdomId] = candidateClan.StringId;
            }

            IdeologyBehavior.AddDecisionAsModAction(
                kingdom,
                new TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision(candidateClan, excludedClan),
                true);
            BellumCivileLogger.Log($"Created deferred succession election; kingdom={kingdom.StringId}; candidate={candidateClan.StringId}; excluded={excludedClan.StringId}; election_can_replace_player={canReplacePlayerRuler}.");

            RemovePendingSuccessionVote(kingdomId);
        }

        private static string BuildTribunalItemKey(string groupId, string clanId) => groupId + "|" + clanId;

        private static bool TryParseTribunalItemKey(string itemKey, out string groupId, out string clanId)
        {
            groupId = null;
            clanId = null;
            if (string.IsNullOrEmpty(itemKey)) return false;

            int splitIndex = itemKey.IndexOf('|');
            if (splitIndex <= 0 || splitIndex >= itemKey.Length - 1) return false;

            groupId = itemKey.Substring(0, splitIndex);
            clanId = itemKey.Substring(splitIndex + 1);
            return !string.IsNullOrEmpty(groupId) && !string.IsNullOrEmpty(clanId);
        }

        private void QueuePlayerTribunal(Kingdom rewardKingdom, Kingdom destinationKingdom, IEnumerable<Clan> losingClans, Kingdom excludedRefuge, Clan victorClan, ExileCause exileCause = ExileCause.Unknown, string civilWarStartFiefSnapshot = null, string reactionGroupId = null)
        {
            EnsureCollectionsInitialized();
            if (rewardKingdom == null || rewardKingdom.IsEliminated || destinationKingdom == null || destinationKingdom.IsEliminated || victorClan == null)
                return;

            List<Clan> tribunalClans = FilterTribunalLosingClans(losingClans, rewardKingdom, victorClan);

            if (tribunalClans.Count == 0) return;

            string groupId = reactionGroupId ?? (_nextTribunalGroupId++).ToString();
            BeginTribunalReactions(groupId, rewardKingdom, tribunalClans);
            _tribunalRewardKingdomIds[groupId] = rewardKingdom.StringId;
            _tribunalDestinationKingdomIds[groupId] = destinationKingdom.StringId;
            _tribunalExcludedRefugeIds[groupId] = excludedRefuge?.StringId ?? string.Empty;
            _tribunalVictorClanIds[groupId] = victorClan.StringId;
            _tribunalLosingClanIds[groupId] = string.Join(",", tribunalClans.Select(c => c.StringId).Distinct());
            _tribunalExileCauseIds[groupId] = (int)exileCause;
            _tribunalCivilWarStartFiefSnapshots[groupId] = civilWarStartFiefSnapshot ?? string.Empty;
            _tribunalAnyExecuted[groupId] = false;
            _tribunalAnyForgiven[groupId] = false;
            _tribunalAnyExiled[groupId] = false;
            _tribunalOldKingExecuted[groupId] = false;

            foreach (Clan losingClan in tribunalClans)
            {
                string itemKey = BuildTribunalItemKey(groupId, losingClan.StringId);
                if (_pendingTribunalOrder.Contains(itemKey)) continue;
                _pendingTribunalOrder.Add(itemKey);
            }
        }

        private bool CanPlayerJudgeTribunal(Kingdom rewardKingdom, Clan victorClan)
        {
            return rewardKingdom != null
                && !rewardKingdom.IsEliminated
                && victorClan == Clan.PlayerClan
                && Clan.PlayerClan.Kingdom == rewardKingdom;
        }

        private void CaptureOutcomeTribunal(ConflictOutcomeNotice notice, Kingdom realm)
        {
            if (realm != null && (_tribunalRewardKingdomIds.ContainsValue(realm.StringId)
                || _pendingSuccessionTribunalRewardKingdomIds.ContainsValue(realm.StringId)))
                ConflictOutcomeBehavior.Capture(notice, "TRIBUNAL",
                    new TextObject("{=BC_Result_Tribunal}Judgment upon the defeated houses is still to come."));
        }

        private void ProcessPendingTribunals()
        {
            EnsureCollectionsInitialized();
            if (ConflictOutcomeBehavior.Current?.HasPending == true || InformationManager.IsAnyInquiryActive()) return;
            if (_collapses.Any(r => !r.Completed) || _rivalDefeats.Any(r => !r.Completed)) return;
            if (!string.IsNullOrEmpty(_activeTribunalItemKey)) return;

            while (_pendingTribunalOrder.Count > 0)
            {
                string itemKey = _pendingTribunalOrder[0];
                if (!TryParseTribunalItemKey(itemKey, out string groupId, out string clanId))
                {
                    RemoveTribunalItem(itemKey);
                    continue;
                }

                Clan losingClan = ResolveClanById(clanId);
                Kingdom rewardKingdom = ResolveKingdomById(GetDictionaryValue(_tribunalRewardKingdomIds, groupId));
                Kingdom destinationKingdom = ResolveKingdomById(GetDictionaryValue(_tribunalDestinationKingdomIds, groupId));
                Kingdom excludedRefuge = ResolveKingdomById(GetDictionaryValue(_tribunalExcludedRefugeIds, groupId));
                Clan victorClan = ResolveClanById(GetDictionaryValue(_tribunalVictorClanIds, groupId));
                if (SuccessionChallengeBehavior.Instance?.HasPendingWarOutcome(rewardKingdom) == true) return;

                if (rewardKingdom != null)
                    BeginTribunalReactions(groupId, rewardKingdom,
                        GetDictionaryValue(_tribunalLosingClanIds, groupId).Split(',').Select(ResolveClanById));

                if (losingClan == null || losingClan.IsEliminated || rewardKingdom == null || destinationKingdom == null || victorClan == null
                    || !IsValidTribunalLosingClan(losingClan, rewardKingdom, victorClan))
                {
                    RemoveTribunalItem(itemKey);
                    continue;
                }

                if (!CanPlayerJudgeTribunal(rewardKingdom, victorClan))
                {
                    AutoResolveTribunalItem(itemKey, losingClan, rewardKingdom, destinationKingdom, excludedRefuge, victorClan);
                    continue;
                }

                ShowTribunalInquiry(itemKey, losingClan);
                return;
            }
        }

        private static string GetDictionaryValue(Dictionary<string, string> dictionary, string key)
        {
            return dictionary != null && dictionary.TryGetValue(key, out string value) ? value : string.Empty;
        }

        private static ExileCause GetStoredExileCause(Dictionary<string, int> dictionary, string key)
        {
            return dictionary != null && dictionary.TryGetValue(key, out int value)
                ? (ExileCause)value
                : ExileCause.Unknown;
        }

        private static ExileCause GetRebelExileCause(FactionType factionType)
        {
            switch (factionType)
            {
                case FactionType.Independence: return ExileCause.RebelIndependence;
                case FactionType.Abdication: return ExileCause.RebelAbdication;
                case FactionType.InstallRuler: return ExileCause.RebelInstallRuler;
                default: return ExileCause.Unknown;
            }
        }

        private static ExileCause GetLoyalistExileCause(FactionType factionType)
        {
            switch (factionType)
            {
                case FactionType.Independence: return ExileCause.LoyalistIndependence;
                case FactionType.Abdication: return ExileCause.LoyalistAbdication;
                case FactionType.InstallRuler: return ExileCause.LoyalistInstallRuler;
                default: return ExileCause.Unknown;
            }
        }

        private HashSet<Clan> ResolveTribunalLosingClanSet(string groupId, Clan fallbackClan, Kingdom rewardKingdom = null, Clan victorClan = null)
        {
            HashSet<Clan> losingClans = new HashSet<Clan>(
                GetClanSetFromDelimitedIds(GetDictionaryValue(_tribunalLosingClanIds, groupId))
                    .Where(c => IsValidTribunalLosingClan(c, rewardKingdom, victorClan)));

            if (IsValidTribunalLosingClan(fallbackClan, rewardKingdom, victorClan))
                losingClans.Add(fallbackClan);

            return losingClans;
        }

        private static HashSet<Clan> GetClanSetFromDelimitedIds(string clanIds)
        {
            HashSet<Clan> clans = new HashSet<Clan>();
            if (string.IsNullOrEmpty(clanIds)) return clans;

            foreach (string clanId in clanIds.Split(','))
            {
                Clan clan = ResolveClanById(clanId);
                if (clan != null)
                    clans.Add(clan);
            }

            return clans;
        }

        private static HashSet<Clan> BuildClanSet(IEnumerable<Clan> clans)
        {
            return clans != null
                ? new HashSet<Clan>(clans.Where(c => c != null))
                : new HashSet<Clan>();
        }

        private static bool IsValidTribunalLosingClan(Clan clan, Kingdom rewardKingdom, Clan victorClan)
        {
            if (clan == null || clan.IsEliminated || clan.IsUnderMercenaryService || IsNonPlayerMinorClan(clan))
                return false;

            if (victorClan != null && clan == victorClan)
                return false;

            if (rewardKingdom?.RulingClan != null && clan == rewardKingdom.RulingClan)
                return false;

            return true;
        }

        private static bool IsNonPlayerMinorClan(Clan clan)
        {
            return NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan);
        }

        private static List<Clan> FilterTribunalLosingClans(IEnumerable<Clan> clans, Kingdom rewardKingdom, Clan victorClan)
        {
            return clans?
                .Where(c => IsValidTribunalLosingClan(c, rewardKingdom, victorClan))
                .Distinct()
                .ToList() ?? new List<Clan>();
        }

        private static List<Clan> FilterPendingSuccessionTribunalLosingClans(IEnumerable<Clan> clans)
        {
            return clans?
                .Where(c => c != null
                         && !c.IsEliminated
                         && !c.IsUnderMercenaryService
                         && !IsNonPlayerMinorClan(c))
                .Distinct()
                .ToList() ?? new List<Clan>();
        }

        private void ShowTribunalInquiry(string itemKey, Clan losingClan)
        {
            Hero targetLeader = losingClan?.Leader;
            if (targetLeader == null)
            {
                RemoveTribunalItem(itemKey);
                return;
            }

            List<InquiryElement> verdicts = new List<InquiryElement>
            {
                new InquiryElement((int)TribunalVerdict.Pardon, new TextObject("{=BC_Tribunal_Pardon}Pardon").ToString(), null, true, new TextObject("{=BC_Tribunal_Pardon_Desc}Spare the defeated lord, restore his standing, and demand submission.").ToString()),
                new InquiryElement((int)TribunalVerdict.Punish, new TextObject("{=BC_Tribunal_Punish}Punish").ToString(), null, true, new TextObject("{=BC_Tribunal_Punish_Desc}Confiscate a major fief and force submission; landless lords are cast out.").ToString()),
                new InquiryElement((int)TribunalVerdict.Execute, new TextObject("{=BC_Tribunal_Execute}Execute").ToString(), null, true, new TextObject("{=BC_Tribunal_Execute_Desc}Put the defeated lord to death and let his house bear the consequences.").ToString())
            };

            TextObject title = new TextObject("{=BC_Tribunal_Title}Post-War Judgment");
            TextObject desc = new TextObject("{=BC_Tribunal_Desc}As the victor in the recent civil war, how shall you deal with {LORD_NAME} of the {CLAN_NAME}?");
            desc.SetTextVariable("LORD_NAME", targetLeader.Name);
            desc.SetTextVariable("CLAN_NAME", losingClan.Name);

            if (TryParseTribunalItemKey(itemKey, out string groupId, out _))
            {
                int remainingJudgments = _pendingTribunalOrder.Count(k => k.StartsWith(groupId + "|"));
                if (remainingJudgments > 1)
                {
                    TextObject remainingText = new TextObject("{=BC_Tribunal_Remaining} {COUNT} defeated clans still await judgment in this tribunal, including this one.");
                    remainingText.SetTextVariable("COUNT", remainingJudgments);
                    desc = new TextObject(desc.ToString() + remainingText.ToString());
                }
            }

            _activeTribunalItemKey = itemKey;

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                title.ToString(),
                desc.ToString(),
                verdicts,
                true,
                1,
                1,
                new TextObject("{=BC_Tribunal_Confirm}Pass Judgment").ToString(),
                new TextObject("{=BC_Tribunal_Defer}Decide Later").ToString(),
                OnTribunalVerdictSelected,
                OnTribunalVerdictDeferred);

            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private void OnTribunalVerdictSelected(List<InquiryElement> selectedVerdicts)
        {
            string itemKey = _activeTribunalItemKey;
            _activeTribunalItemKey = string.Empty;

            if (string.IsNullOrEmpty(itemKey) || selectedVerdicts == null || selectedVerdicts.Count == 0)
                return;

            if (!TryParseTribunalItemKey(itemKey, out string groupId, out string clanId))
            {
                RemoveTribunalItem(itemKey);
                return;
            }

            Clan losingClan = ResolveClanById(clanId);
            Kingdom rewardKingdom = ResolveKingdomById(GetDictionaryValue(_tribunalRewardKingdomIds, groupId));
            Kingdom destinationKingdom = ResolveKingdomById(GetDictionaryValue(_tribunalDestinationKingdomIds, groupId));
            Kingdom excludedRefuge = ResolveKingdomById(GetDictionaryValue(_tribunalExcludedRefugeIds, groupId));
            Clan victorClan = ResolveClanById(GetDictionaryValue(_tribunalVictorClanIds, groupId));
            Hero victor = victorClan?.Leader;

            if (losingClan == null || rewardKingdom == null || destinationKingdom == null || victor == null
                || !IsValidTribunalLosingClan(losingClan, rewardKingdom, victorClan))
            {
                RemoveTribunalItem(itemKey);
                return;
            }

            TribunalVerdict verdict = (TribunalVerdict)(int)selectedVerdicts[0].Identifier;
            ApplyTribunalVerdict(verdict, groupId, losingClan, rewardKingdom, destinationKingdom, excludedRefuge, victor);
            RemoveTribunalItem(itemKey);
            ProcessPendingTribunals();
        }

        private void OnTribunalVerdictDeferred(List<InquiryElement> _)
        {
            _activeTribunalItemKey = string.Empty;
        }

        private void RemoveTribunalItem(string itemKey)
        {
            if (!TryParseTribunalItemKey(itemKey, out string groupId, out _))
                groupId = null;

            _pendingTribunalOrder.Remove(itemKey);
            _activeTribunalItemKey = _activeTribunalItemKey == itemKey ? string.Empty : _activeTribunalItemKey;

            if (!string.IsNullOrEmpty(groupId) && !_pendingTribunalOrder.Any(k => k.StartsWith(groupId + "|")))
                FinalizeTribunalGroup(groupId);
        }

        private void FinalizeTribunalGroup(string groupId, bool applyShocks = true)
        {
            CloseTribunalReactions(groupId, applyShocks);

            _tribunalRewardKingdomIds.Remove(groupId);
            _tribunalDestinationKingdomIds.Remove(groupId);
            _tribunalExcludedRefugeIds.Remove(groupId);
            _tribunalVictorClanIds.Remove(groupId);
            _tribunalLosingClanIds.Remove(groupId);
            _tribunalExileCauseIds.Remove(groupId);
            _tribunalCivilWarStartFiefSnapshots.Remove(groupId);
            _tribunalAnyExecuted.Remove(groupId);
            _tribunalAnyForgiven.Remove(groupId);
            _tribunalAnyExiled.Remove(groupId);
            _tribunalOldKingExecuted.Remove(groupId);
        }

        private void CleanupExternallyResolvedFaction(FactionObject faction)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            if (faction == null) return;
            if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(faction, SuccessionChallengeOutcome.WhitePeace, faction.ParentKingdom) == false) return;

            CompleteCivilWarTracker(faction, null, "civil-war faction lost its rebel realm state");
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);

            SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(faction, faction.ParentKingdom);

            if (faction.ParentKingdom != null && !faction.ParentKingdom.IsEliminated)
            {
                IEnumerable<Clan> clansBackInParent = faction.Members
                    .Where(c => c != null && c.Kingdom == faction.ParentKingdom);

                ApplyPacifiedCooldowns(clansBackInParent, CivilWarPacifiedDays);
                PacifyIdeologies(faction.ParentKingdom, faction.ParentKingdom.RulingClan);
            }

            BellumCivileLogger.Log($"Cleaned externally resolved civil war faction {faction.Name} after rebel kingdom state was lost.");
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim?.Clan?.Kingdom == null) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            FactionObject activeFaction = factionManager.GetFactionByRebelKingdom(victim.Clan.Kingdom);
            if (IsCoalitionSuccession(victim.Clan.Kingdom))
            {
                ProcessCoalitionSuccessions();
                return;
            }
            if (activeFaction != null)
                CivilWarTransitionDiagnostics.Log("rebel hero death", activeFaction, victim.Clan.Kingdom,
                    $"victim={victim.StringId}; detail={detail}; matches_current_faction_head={victim == activeFaction.Leader?.Leader}");
            if (activeFaction == null || victim != activeFaction.Leader?.Leader) return;
            if (CivilWarConflictBehavior.IsFactionTransferPending(activeFaction)) return;

            Kingdom rebelKingdom = ResolveTrackedRebelKingdom(activeFaction, victim.Clan.Kingdom);
            if (rebelKingdom == null) return;
            bool playerInvolved = Clan.PlayerClan.Kingdom == activeFaction.ParentKingdom
                                || Clan.PlayerClan.Kingdom == rebelKingdom;

            bool clanHasSurvivors = activeFaction.Leader.Heroes
                .Any(h => h != victim && h.IsAlive && !h.IsChild);
            if (clanHasSurvivors) return;

            Clan newRebelLeader = rebelKingdom.Clans
                .Where(c => c != activeFaction.Leader && !c.IsEliminated && c.Leader != null)
                .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                .FirstOrDefault();

            if (newRebelLeader != null)
            {
                activeFaction.Leader = newRebelLeader;
                rebelKingdom.RulingClan = newRebelLeader;
                TextObject msg = new TextObject("{=BC_CivilWar_NewRebelLeader}With {LEADER_NAME} slain, the rebellion rallies under {NEW_LEADER}. The war continues!");
                msg.SetTextVariable("LEADER_NAME", victim.Name);
                msg.SetTextVariable("NEW_LEADER", newRebelLeader.Leader.Name);
                BellumCivileNotifications.Show(msg, BellumNotificationColors.Rebellion, primaryKingdom: activeFaction.ParentKingdom, secondaryKingdom: rebelKingdom, primaryClan: activeFaction.Leader, secondaryClan: newRebelLeader);
            }
            else
            {
                TextObject msg = new TextObject("{=BC_CivilWar_RebelLeaderDied}The rebel leader has fallen on the field. With no one left to lead them, the rebellion collapses.");
                BellumCivileNotifications.Show(msg, BellumNotificationColors.Success, primaryKingdom: activeFaction.ParentKingdom, secondaryKingdom: rebelKingdom, primaryClan: activeFaction.Leader, isMajorEvent: true);
                ResolveLiegeVictory(activeFaction, rebelKingdom);
            }
        }

        public void ResolveLiegeVictory(FactionObject faction, Kingdom rebelKingdom)
        {
            ResolveLiegeVictoryInternal(faction, rebelKingdom, allowEliminatedRebelShell: false);
        }

        public bool TryResolveTerminalLiegeVictory(FactionObject faction, Kingdom rebelKingdom)
        {
            return ResolveLiegeVictoryInternal(faction, rebelKingdom, allowEliminatedRebelShell: true);
        }

        private bool ResolveLiegeVictoryInternal(FactionObject faction, Kingdom rebelKingdom, bool allowEliminatedRebelShell)
        {
            CivilWarTransitionDiagnostics.Log("loyalist victory requested", faction, rebelKingdom,
                $"allow_eliminated={allowEliminatedRebelShell}");
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return false;
            rebelKingdom = ResolveTrackedRebelKingdom(faction, rebelKingdom, allowEliminatedRebelShell);
            if (faction == null || rebelKingdom == null || faction.ParentKingdom == null)
            {
                CleanupExternallyResolvedFaction(faction);
                return false;
            }

            if (TrySettleSatisfiedCivilWarDemand(faction)) return true;

            if (faction.ParentKingdom.IsEliminated)
            {
                HandleDestroyedParentKingdom(faction.ParentKingdom);
                return true;
            }

            int parentStrongholds = CountStrongholds(faction.ParentKingdom);
            int rebelStrongholds = CountStrongholds(rebelKingdom);
            if (parentStrongholds == 0)
            {
                if (rebelStrongholds > 0)
                {
                    HandleCollapsedParentKingdom(faction.ParentKingdom);
                }
                else
                {
                    ResolveCollapsedCivilWarStalemate(faction, rebelKingdom);
                }
                return true;
            }

            List<Clan> losingClans = allowEliminatedRebelShell
                ? faction.Members
                    .Concat(rebelKingdom.Clans)
                    .Where(clan => clan != null && !clan.IsEliminated)
                    .Distinct()
                    .ToList()
                : rebelKingdom.Clans.ToList();
            HashSet<Clan> losingClanSet = new HashSet<Clan>(losingClans);
            List<Clan> loyalistWinners = faction.ParentKingdom.Clans
                .Where(clan => clan != null && !losingClanSet.Contains(clan))
                .ToList();

            var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebelKingdom);
            if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(faction, SuccessionChallengeOutcome.Defeat, faction.ParentKingdom) == false) return false;

            CivilWarConflictBehavior.Instance?.GetRewardHistory(faction)?.RecordCrownDefeat(losingClans);

            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(faction, RebellionSummaryOutcome.LoyalistVictory);

            CompleteCivilWarTracker(faction, rebelKingdom, "loyalist victory");

            ApplyCivilWarPeaceIfNeeded(faction.ParentKingdom, rebelKingdom);

            PacifyIdeologies(faction.ParentKingdom, faction.ParentKingdom.RulingClan);

            CaptureCurrentCivilWarFiefSnapshot(faction, rebelKingdom);
            string civilWarStartFiefSnapshot = faction.ExportCivilWarStartFiefSnapshot();
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);

            bool playerTribunal = faction.ParentKingdom.RulingClan == Clan.PlayerClan;
            if (TryQueueChallengeTribunal(faction, faction.ParentKingdom)) { }
            else if (playerTribunal)
            {
                QueuePlayerTribunal(faction.ParentKingdom, faction.ParentKingdom, losingClans, rebelKingdom, faction.ParentKingdom.RulingClan, GetRebelExileCause(faction.Type), civilWarStartFiefSnapshot);
            }
            else
            {
                ApplyPostWarConsequences(faction.ParentKingdom, losingClans, faction.ParentKingdom, out bool anyExec, out bool anyForgive, out bool anyExile, out bool oldKingExec, rebelKingdom, GetRebelExileCause(faction.Type), civilWarStartFiefSnapshot);
            }

            ReleaseMercenariesFromKingdom(rebelKingdom);
            if (allowEliminatedRebelShell)
            {
                foreach (Clan mercenary in losingClans
                    .Where(clan => clan.IsUnderMercenaryService && clan.Kingdom == faction.ParentKingdom)
                    .ToList())
                {
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(mercenary, showNotification: false);
                }
            }
            DrainAndDestroyRebelKingdom(rebelKingdom, faction.ParentKingdom, reunification: true);
            faction.RestoreCivilWarInfluenceSnapshots(losingClans.Concat(loyalistWinners));
            ApplyChallengeAwareVictoryRewards(faction, loyalistWinners, faction.ParentKingdom.RulingClan, "loyalist victory", loyalistVictory: true);
            SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(faction, faction.ParentKingdom);
            if (SuccessionChallengeBehavior.Instance?.GetWarRecord(faction) is SuccessionChallengeRecord pendingDefeat && !pendingDefeat.ResolutionReturned) return false;
            CaptureOutcomeTribunal(resultNotice, faction.ParentKingdom);
            ConflictOutcomeBehavior.Current?.Publish(resultNotice, "loyalist", ConflictOutcomeBehavior.ContinuingWars(faction.ParentKingdom));
            return true;
        }

        private static void CaptureCurrentCivilWarFiefSnapshot(FactionObject faction, Kingdom rebelKingdom)
        {
            if (faction == null)
                return;

            faction.CaptureCivilWarStartFiefCounts(faction.ParentKingdom?.Clans);
            faction.CaptureCivilWarStartFiefCounts(rebelKingdom?.Clans);
            faction.CaptureCivilWarStartInfluence(faction.ParentKingdom?.Clans);
            faction.CaptureCivilWarStartInfluence(rebelKingdom?.Clans);
        }

        private static void ApplyCivilWarVictoryInfluenceRewards(IEnumerable<Clan> victoriousClans, Clan victoriousLeader, string reason,
            List<Clan> savedRewards = null)
        {
            HashSet<Clan> rewarded = new HashSet<Clan>();
            foreach (Clan clan in victoriousClans ?? Enumerable.Empty<Clan>())
            {
                if (clan == null || rewarded.Contains(clan) || savedRewards?.Contains(clan) == true
                    || clan.IsEliminated || clan.IsUnderMercenaryService || clan.Kingdom == null)
                    continue;

                float reward = clan == victoriousLeader
                    ? C.CivilWarVictoryLeaderInfluenceReward
                    : C.CivilWarVictoryMemberInfluenceReward;
                if (reward <= 0f)
                    continue;

                if (savedRewards == null) ChangeClanInfluenceAction.Apply(clan, reward);
                else
                {
                    // Native Apply adds influence, then publishes this event. Save the
                    // challenge receipt between those steps so a callback cannot replay it.
                    clan.Influence += reward;
                    savedRewards.Add(clan);
                    CampaignEventDispatcher.Instance.OnClanInfluenceChanged(clan, reward);
                }
                rewarded.Add(clan);
                BellumCivileLogger.Log($"Civil war victory influence reward applied; clan={clan.StringId}; reward={reward:0}; reason={reason ?? "unknown"}.");
            }
        }

        public void ResolveRebelVictory(FactionObject faction, Kingdom rebelKingdom)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            rebelKingdom = ResolveTrackedRebelKingdom(faction, rebelKingdom);
            if (IsCoalitionSuccession(rebelKingdom)) return;
            if (faction == null || rebelKingdom == null || faction.ParentKingdom == null)
            {
                CleanupExternallyResolvedFaction(faction);
                return;
            }

            if (TrySettleSatisfiedCivilWarDemand(faction)) return;

            if (faction.Type == FactionType.Abdication && !faction.IsHereditaryAbdication
                && !faction.ParentKingdom.IsEliminated
                && (CrownAccessionBehavior.Instance?.IsPending(faction.ParentKingdom) == true
                    || faction.ParentKingdom.UnresolvedDecisions.Any(d => d is TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision)
                    || ElectiveSuccessionBehavior.Instance?.PendingDeposition(faction.ParentKingdom) != null)) return;

            if (faction.IsHereditaryAbdication)
            {
                var accession = CrownAccessionBehavior.Instance;
                if (accession == null || accession.IsPending(faction.ParentKingdom)) return;
                if (accession.IsAbdicationSatisfied(faction) && !faction.ParentKingdom.IsEliminated)
                {
                    ResolveWhitePeace(faction, rebelKingdom, demandSatisfied: true);
                    return;
                }
            }

            if (faction.ParentKingdom.IsEliminated)
            {
                HandleParentCollapse(faction.ParentKingdom, parentAlreadyDestroyed: true, decisiveCivilWarVictor: faction);
                return;
            }

            int parentStrongholds = CountStrongholds(faction.ParentKingdom);
            int rebelStrongholds = CountStrongholds(rebelKingdom);
            if (parentStrongholds == 0 && rebelStrongholds == 0)
            {
                ResolveCollapsedCivilWarStalemate(faction, rebelKingdom);
                return;
            }

            Clan victoriousLeader = faction.Leader;
            if (victoriousLeader == null) return;
            if (SuccessionChallengeBehavior.Instance?.CanWinChallenge(faction) == false)
            {
                ResolveWhitePeace(faction, rebelKingdom, cause: new TextObject("{=BC_Result_ClaimantUnavailable}The original claimant can no longer take the throne. The challenge has ended, and the rebel houses return to the realm."));
                return;
            }
            var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebelKingdom);
            if (CivilWarConflictBehavior.Instance?.PrepareRivalCrownVictory(faction) == false) return;
            if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(faction, SuccessionChallengeOutcome.Victory, faction.ParentKingdom) == false) return;

            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(faction, RebellionSummaryOutcome.RebelVictory);
            CompleteCivilWarTracker(faction, rebelKingdom, "rebel victory");
            ApplyCivilWarPeaceIfNeeded(faction.ParentKingdom, rebelKingdom);

            PacifyIdeologies(faction.ParentKingdom, victoriousLeader);

            List<Clan> rebelWinners = rebelKingdom.Clans.ToList();
            List<Clan> parentClansAtResolution = faction.ParentKingdom.Clans.ToList();
            CaptureCurrentCivilWarFiefSnapshot(faction, rebelKingdom);
            string civilWarStartFiefSnapshot = faction.ExportCivilWarStartFiefSnapshot();

            bool anyExec = false, anyForgive = false, anyExile = false, oldKingExec = false;
            bool usedPlayerTribunal = false;
            bool tribunalDeferredUntilSuccession = false;

            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);

            ReleaseMercenariesFromKingdom(rebelKingdom);

            switch (faction.Type)
            {
                case FactionType.Independence:
                    Clan formerRulingHouse = faction.ParentKingdom.RulingClan;
                    PromoteRebelKingdomToIndependentKingdom(faction, rebelKingdom, victoriousLeader);
                    ApplyIndependenceMemories(formerRulingHouse, rebelWinners);
                    break;

                case FactionType.Abdication:
                    var rebelClansSet = new HashSet<Clan>(rebelKingdom.Clans);
                    Clan deposedRuler = faction.ParentKingdom.RulingClan;
                    List<Clan> abdicationLoyalists = faction.ParentKingdom.Clans
                        .Where(c => !rebelClansSet.Contains(c))
                        .ToList();

                    foreach (Clan rebelClan in rebelKingdom.Clans.ToList())
                        faction.MoveClanToKingdomPreservingCivilWarInfluence(rebelClan, faction.ParentKingdom, preserveCustomBanner: true, showNotification: false);

                    if (faction.IsHereditaryAbdication)
                        CrownAccessionBehavior.Instance.BeginForcedAbdication(faction.ParentKingdom,
                            deposedRuler, faction.AbdicationMonarch, faction.AbdicationLaws, faction.AbdicationCauseId);
                    else if (ElectiveSuccessionBehavior.UsesElection(faction.ParentKingdom))
                    {
                        ElectiveSuccessionBehavior.Instance.BeginDeposition(faction.ParentKingdom, victoriousLeader,
                            faction.AbdicationMonarch ?? ElectiveSuccessionBehavior.LegalHead(deposedRuler));
                        EnsureAbdicationElectionPostcondition(faction.ParentKingdom, victoriousLeader, deposedRuler);
                    }
                    else
                    {
                        QueueDeferredSuccessionVote(faction.ParentKingdom, victoriousLeader, deposedRuler, canReplacePlayerRuler: true);
                        EnsureAbdicationElectionPostcondition(faction.ParentKingdom, victoriousLeader, deposedRuler);
                    }
                    QueueDeferredSuccessionTribunal(faction.ParentKingdom, faction.ParentKingdom, abdicationLoyalists,
                        rebelKingdom, GetLoyalistExileCause(faction.Type), civilWarStartFiefSnapshot,
                        faction.IsHereditaryAbdication ? faction.AbdicationCauseId : null);
                    tribunalDeferredUntilSuccession = true;

                    DrainAndDestroyRebelKingdom(rebelKingdom, faction.ParentKingdom, reunification: true);
                    break;

                case FactionType.InstallRuler:
                    Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.MarkAsUsurper(faction.ParentKingdom);

                    var coupRebelClansSet = new HashSet<Clan>(rebelKingdom.Clans);
                    List<Clan> coupLoyalists = faction.ParentKingdom.Clans.Where(c => !coupRebelClansSet.Contains(c)).ToList();

                    foreach (Clan rebelClan in rebelKingdom.Clans.ToList())
                        faction.MoveClanToKingdomPreservingCivilWarInfluence(rebelClan, faction.ParentKingdom, preserveCustomBanner: true, showNotification: false);

                    ClearSuccessionStateForKingdom(faction.ParentKingdom, "install-ruler victory");
                    faction.ParentKingdom.RulingClan = victoriousLeader;
                    Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                        ?.TrySetKingdomTitleRuler(faction.ParentKingdom, victoriousLeader, legalTransfer: true, reason: "install-ruler victory");

                    if (TryQueueChallengeTribunal(faction, faction.ParentKingdom))
                        tribunalDeferredUntilSuccession = true;
                    else if (victoriousLeader == Clan.PlayerClan)
                    {
                        usedPlayerTribunal = true;
                        QueuePlayerTribunal(faction.ParentKingdom, faction.ParentKingdom, coupLoyalists, rebelKingdom, victoriousLeader, GetLoyalistExileCause(faction.Type), civilWarStartFiefSnapshot);
                    }
                    else
                    {
                        ApplyPostWarConsequences(faction.ParentKingdom, coupLoyalists, faction.ParentKingdom, out anyExec, out anyForgive, out anyExile, out oldKingExec, rebelKingdom, GetLoyalistExileCause(faction.Type), civilWarStartFiefSnapshot);
                    }

                    DrainAndDestroyRebelKingdom(rebelKingdom, faction.ParentKingdom, reunification: true);
                    EnsureValidRulingClan(faction.ParentKingdom, victoriousLeader, forcePreferred: true);
                    EnsureInstallRulerPostcondition(faction.ParentKingdom, victoriousLeader, "install-ruler victory");
                    break;

                case FactionType.Royalists:
                case FactionType.Glory:
                case FactionType.Nobility:
                case FactionType.Liberty:
                    foreach (Clan rebelClan in rebelKingdom.Clans.ToList()) faction.MoveClanToKingdomPreservingCivilWarInfluence(rebelClan, faction.ParentKingdom, preserveCustomBanner: true, showNotification: false);
                    DrainAndDestroyRebelKingdom(rebelKingdom, faction.ParentKingdom, reunification: true);
                    break;
            }

            faction.RestoreCivilWarInfluenceSnapshots(rebelWinners.Concat(parentClansAtResolution));
            ApplyChallengeAwareVictoryRewards(faction, rebelWinners, victoriousLeader, "rebel victory");

            if (!usedPlayerTribunal && !tribunalDeferredUntilSuccession)
                FlushTribunalReactions();
            SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(faction, faction.ParentKingdom);
            ConflictOutcomeBehavior.Capture(resultNotice, "NEW_REALM", victoriousLeader.Kingdom?.Name);
            if (SuccessionChallengeBehavior.Instance?.GetWarRecord(faction) is SuccessionChallengeRecord pendingVictory && !pendingVictory.ResolutionReturned) return;
            CaptureOutcomeTribunal(resultNotice, faction.ParentKingdom);
            var successionText = CrownAccessionBehavior.Instance?.IsPending(faction.ParentKingdom) == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(faction.ParentKingdom) != null
                || faction.ParentKingdom.UnresolvedDecisions.Any(d => d is TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision)
                ? new TextObject("{=BC_Result_SuccessionPending}The succession remains to be settled.")
                : new TextObject("{=BC_Result_Successor}{SUCCESSOR} has assumed the crown.").SetTextVariable("SUCCESSOR", faction.ParentKingdom.Leader?.Name);
            if (ElectiveSuccessionBehavior.Instance?.PendingDeposition(faction.ParentKingdom) is ElectiveSuccessionRecord deposition)
                successionText = new TextObject("{=BC_Result_Caretaker}{CARETAKER} will govern in the interim and judge the defeated houses. Once the judgments are concluded, the nobles will deliberate and elect a sovereign.")
                    .SetTextVariable("CARETAKER", ElectiveSuccessionBehavior.LegalHead(deposition.InterimHouse)?.Name);
            ConflictOutcomeBehavior.Capture(resultNotice, "SUCCESSION", successionText);
            ConflictOutcomeBehavior.Current?.Publish(resultNotice,
                faction.Type == FactionType.InstallRuler ? "claimant" : faction.Type == FactionType.Abdication ? "abdication"
                : faction.Type == FactionType.Independence ? "independence" : "victory",
                ConflictOutcomeBehavior.ContinuingWars(faction.ParentKingdom));
        }

        public void ResolveUnscriptedPeace(FactionObject faction, Kingdom rebelKingdom)
        {
            CivilWarTransitionDiagnostics.Log("unscripted peace requested", faction, rebelKingdom);
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            rebelKingdom = ResolveTrackedRebelKingdom(faction, rebelKingdom, allowEliminated: true);
            if (faction == null || faction.ParentKingdom == null) return;

            if (rebelKingdom == null)
            {
                CleanupExternallyResolvedFaction(faction);
                return;
            }

            if (rebelKingdom.IsEliminated)
            {
                ResolveLiegeVictoryInternal(faction, rebelKingdom, allowEliminatedRebelShell: true);
                return;
            }

            if (faction.ParentKingdom.IsEliminated)
            {
                HandleDestroyedParentKingdom(faction.ParentKingdom);
                return;
            }

            ResolveWhitePeace(faction, rebelKingdom);
        }

        public void HandleDestroyedParentKingdom(Kingdom destroyedParent)
        {
            HandleParentCollapse(destroyedParent, parentAlreadyDestroyed: true);
        }

        public void HandleCollapsedParentKingdom(Kingdom collapsedParent)
        {
            HandleParentCollapse(collapsedParent, parentAlreadyDestroyed: false);
        }

        public void ResolveCollapsedCivilWarStalemate(FactionObject faction, Kingdom rebelKingdom)
        {
            rebelKingdom = ResolveTrackedRebelKingdom(faction, rebelKingdom);
            if (faction == null || faction.ParentKingdom == null || rebelKingdom == null) return;

            ResolveWhitePeace(faction, rebelKingdom, cause: new TextObject("{=BC_Result_Landless}Both sides have lost their last strongholds. Neither has been able to enforce its demands."));
        }

        private void HandleParentCollapse(
            Kingdom collapsedParent,
            bool parentAlreadyDestroyed,
            FactionObject decisiveCivilWarVictor = null)
        {
            if (collapsedParent == null) return;
            var pendingCollapse = _collapses.FirstOrDefault(c => c.Parent == collapsedParent && !c.Completed);
            if (pendingCollapse != null) { ResumeCollapse(pendingCollapse); return; }
            if (CivilWarConflictBehavior.IsRealmTransferPending(collapsedParent)) return;
            // The challenge journal owns recovery after a successor realm was chosen.
            // Re-entering the generic collapse path could create another successor shell.
            if (SuccessionChallengeBehavior.Instance?.HasPendingWarOutcome(collapsedParent) == true) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            Dictionary<FactionObject, Kingdom> activeRebellions = factionManager.GetFactionsInKingdom(collapsedParent)
                .Where(f => !f.IsIdeology
                         && ResolveTrackedRebelKingdom(f) != null)
                .Select(f => new { Faction = f, RebelKingdom = ResolveTrackedRebelKingdom(f) })
                .Where(x => x.RebelKingdom != null
                         && x.RebelKingdom != collapsedParent
                         && !x.RebelKingdom.IsEliminated)
                .ToDictionary(x => x.Faction, x => x.RebelKingdom);

            if (activeRebellions.Count == 0)
            {
                foreach (FactionObject orphanedFaction in factionManager.GetFactionsInKingdom(collapsedParent).Where(f => !f.IsIdeology).ToList())
                {
                    if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(orphanedFaction, SuccessionChallengeOutcome.WhitePeace, collapsedParent) == false) continue;
                    CompleteCivilWarTracker(orphanedFaction, null, "parent realm collapsed without a surviving rebel realm");
                    factionManager.RemoveFaction(orphanedFaction);
                    SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(orphanedFaction, collapsedParent);
                }

                if (!parentAlreadyDestroyed)
                {
                    CollapseLandlessParentWithoutSuccessor(collapsedParent);
                }
                else
                {
                    TryRepairEliminatedKingdomWithLiveState(collapsedParent, "destroyed parent without active rebellion");
                }
                return;
            }

            decisiveCivilWarVictor = ResolveDecisiveCivilWarVictor(
                collapsedParent,
                activeRebellions,
                decisiveCivilWarVictor);

            // A civil war which itself stripped the parent of its last stronghold must settle
            // its declared demand. The generic successor-state path is reserved for a parent
            // realm destroyed by an outside power while its civil war remains unresolved.
            if (!parentAlreadyDestroyed
                && decisiveCivilWarVictor != null
                && activeRebellions.TryGetValue(decisiveCivilWarVictor, out Kingdom decisiveRebelKingdom))
            {
                BellumCivileLogger.Log($"Resolving decisive civil-war occupation through faction demand; parent={collapsedParent.StringId}; faction={decisiveCivilWarVictor.Name}; type={decisiveCivilWarVictor.Type}; rebel={decisiveRebelKingdom.StringId}.");
                ResolveRebelVictory(decisiveCivilWarVictor, decisiveRebelKingdom);
                return;
            }

            if (!parentAlreadyDestroyed && decisiveCivilWarVictor == null
                && TryBeginContinuingCollapse(collapsedParent, activeRebellions)) return;

            foreach (var independenceFaction in activeRebellions.Where(kvp => kvp.Key.Type == FactionType.Independence).ToList())
            {
                ResolveRecognizedIndependence(independenceFaction.Key, independenceFaction.Value, true);
            }

            List<KeyValuePair<FactionObject, Kingdom>> successionFactions = activeRebellions
                .Where(kvp => kvp.Key.Type != FactionType.Independence)
                .OrderByDescending(kvp => kvp.Key == decisiveCivilWarVictor)
                .ThenByDescending(kvp => kvp.Key.CalculateFactionPower())
                .ToList();

            if (successionFactions.Count == 0)
            {
                if (!parentAlreadyDestroyed)
                {
                    CollapseLandlessParentWithoutSuccessor(collapsedParent);
                }
                else
                {
                    TryRepairEliminatedKingdomWithLiveState(collapsedParent, "destroyed parent without succession faction");
                }
                return;
            }

            FactionObject strongestFaction = successionFactions[0].Key;
            Kingdom successorKingdom = successionFactions[0].Value;
            Clan successorLeader = strongestFaction.Leader;

            if (successorKingdom == null || successorLeader == null) return;
            var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(strongestFaction, successorKingdom);
            if (resultNotice != null && activeRebellions.Values.Contains(Clan.PlayerClan?.Kingdom)) resultNotice.PlayerInvolved = true;
            bool settleDecisiveFactionDemand = decisiveCivilWarVictor == strongestFaction;
            Clan deposedRuler = collapsedParent.RulingClan;
            Hero abdicationHeir = strongestFaction.IsHereditaryAbdication
                ? Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.GetReigningHouseSuccessionCandidate(collapsedParent)
                : null;
            List<Clan> collapsedParentLoyalists = GetLiveNobleClans(collapsedParent)
                .Where(clan => clan != null && !successorKingdom.Clans.Contains(clan))
                .ToList();
            List<Clan> successorWinnerClans = successorKingdom.Clans.ToList();
            strongestFaction.CaptureCivilWarStartInfluence(successorWinnerClans);
            CaptureCurrentCivilWarFiefSnapshot(strongestFaction, successorKingdom);
            string civilWarStartFiefSnapshot = strongestFaction.ExportCivilWarStartFiefSnapshot();

            if (settleDecisiveFactionDemand)
            {
                Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()
                    ?.RecordOutcome(strongestFaction, RebellionSummaryOutcome.RebelVictory);
            }

            List<Kingdom> inheritedWars = !parentAlreadyDestroyed
                ? GetExternalEnemiesToInherit(collapsedParent, activeRebellions.Values)
                : new List<Kingdom>();

            var challengeRecord = SuccessionChallengeBehavior.Instance?.GetWarRecord(strongestFaction);
            if (challengeRecord != null)
            {
                if (!SuccessionChallengeBehavior.Instance.PrepareWarOutcome(strongestFaction, SuccessionChallengeOutcome.Victory, collapsedParent)) return;
                challengeRecord.RestoredRealmRequested = true;
            }
            Kingdom restoredSuccessorKingdom = CreateRestoredSuccessorKingdom(collapsedParent, successorKingdom, successorLeader,
                runImmediateRepair: false, challenge: challengeRecord);
            if (restoredSuccessorKingdom == null) return;
            if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(strongestFaction, SuccessionChallengeOutcome.Victory, restoredSuccessorKingdom) == false) return;
            CompleteCivilWarTracker(strongestFaction, successorKingdom, "parent realm collapsed into a civil-war successor");
            strongestFaction.RestoreCivilWarInfluenceSnapshots(successorWinnerClans);
            ApplyChallengeAwareVictoryRewards(strongestFaction, successorWinnerClans, successorLeader, "parent collapse succession");

            factionManager.RemoveFaction(strongestFaction);

            foreach (KeyValuePair<FactionObject, Kingdom> rivalFaction in successionFactions.Skip(1).ToList())
            {
                Kingdom rivalKingdom = rivalFaction.Value;
                if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(rivalFaction.Key, SuccessionChallengeOutcome.WhitePeace, restoredSuccessorKingdom) == false) continue;
                if (rivalKingdom != null && rivalKingdom != successorKingdom && !rivalKingdom.IsEliminated)
                {
                    ApplyPacifiedCooldowns(rivalKingdom.Clans, CivilWarPacifiedDays);
                    ReleaseMercenariesFromKingdom(rivalKingdom);
                    DrainAndDestroyRebelKingdom(rivalKingdom, restoredSuccessorKingdom);
                }

                CompleteCivilWarTracker(rivalFaction.Key, rivalKingdom, "rival civil-war realm absorbed by the successor");
                factionManager.RemoveFaction(rivalFaction.Key);
                SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(rivalFaction.Key, restoredSuccessorKingdom);
            }

            foreach (FactionObject leftover in factionManager.GetFactionsInKingdom(collapsedParent).Where(f => !f.IsIdeology).ToList())
            {
                if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(leftover, SuccessionChallengeOutcome.WhitePeace, restoredSuccessorKingdom) == false) continue;
                CompleteCivilWarTracker(leftover, leftover.GetTrackedRebelKingdomIncludingEliminated(), "civil-war faction cleared after parent collapse");
                factionManager.RemoveFaction(leftover);
                SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(leftover, restoredSuccessorKingdom);
            }

            if (!parentAlreadyDestroyed && !collapsedParent.IsEliminated)
            {
                ApplyPacifiedCooldowns(collapsedParent.Clans, CivilWarPacifiedDays);
                EnsureValidRulingClan(collapsedParent);
                DrainAndDestroyRebelKingdom(collapsedParent, restoredSuccessorKingdom);
            }
            else if (collapsedParent.IsEliminated)
            {
                ApplyPacifiedCooldowns(GetLiveNobleClans(collapsedParent), CivilWarPacifiedDays);
                TransferLiveClansFromEliminatedKingdom(collapsedParent, restoredSuccessorKingdom, successorLeader);
            }

            if (settleDecisiveFactionDemand)
            {
                ApplyDecisiveCollapsedCivilWarDemand(
                    strongestFaction,
                    restoredSuccessorKingdom,
                    successorKingdom,
                    successorLeader,
                    deposedRuler,
                    collapsedParentLoyalists,
                    civilWarStartFiefSnapshot,
                    abdicationHeir);
            }

            InheritExternalWars(restoredSuccessorKingdom, inheritedWars);
            EnsureValidRulingClan(restoredSuccessorKingdom, successorLeader);
            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(restoredSuccessorKingdom);
            RepairCompatibilityStateNow($"post-collapse succession for {collapsedParent.StringId} into {restoredSuccessorKingdom.StringId}");
            SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(strongestFaction, restoredSuccessorKingdom);

            if (challengeRecord == null || challengeRecord.ResolutionReturned)
            {
                CaptureOutcomeTribunal(resultNotice, restoredSuccessorKingdom);
                if (resultNotice != null) resultNotice.Names["LEADER"] = restoredSuccessorKingdom.Leader?.Name?.ToString() ?? "";
                ConflictOutcomeBehavior.Current?.Publish(resultNotice, "collapse",
                    ConflictOutcomeBehavior.RemainingWars(restoredSuccessorKingdom, inheritedWars));
            }
        }

        private static FactionObject ResolveDecisiveCivilWarVictor(
            Kingdom collapsedParent,
            IReadOnlyDictionary<FactionObject, Kingdom> activeRebellions,
            FactionObject requestedVictor)
        {
            if (collapsedParent == null || activeRebellions == null || activeRebellions.Count == 0)
                return null;

            if (requestedVictor != null && activeRebellions.ContainsKey(requestedVictor))
                return requestedVictor;

            WarScoreBehavior warScoreBehavior = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (warScoreBehavior == null)
                return null;

            return activeRebellions
                .Select(pair => new
                {
                    Faction = pair.Key,
                    RebelKingdom = pair.Value,
                    War = warScoreBehavior.GetActiveWar(pair.Value, collapsedParent)
                })
                .Where(entry => entry.War != null
                    && entry.War.ConflictType == WarScoreConflictType.CivilWar
                    && entry.War.AttackerKingdomId == entry.RebelKingdom.StringId
                    && entry.War.DefenderKingdomId == collapsedParent.StringId
                    && (entry.War.Score >= BellumCivileConstants.WarScoreForcePeaceThreshold
                        || DoesRebelRealmControlAllParentWarFiefs(entry.War, collapsedParent, entry.RebelKingdom)))
                .OrderByDescending(entry => entry.War.Score)
                .ThenByDescending(entry => entry.Faction.CalculateFactionPower())
                .Select(entry => entry.Faction)
                .FirstOrDefault();
        }

        private static bool DoesRebelRealmControlAllParentWarFiefs(
            WarScoreRecord war,
            Kingdom collapsedParent,
            Kingdom rebelKingdom)
        {
            if (war?.FiefSnapshots == null || collapsedParent == null || rebelKingdom == null)
                return false;

            List<WarScoreFiefSnapshotRecord> parentFiefs = war.FiefSnapshots
                .Where(snapshot => snapshot != null
                    && snapshot.OwnerKingdomId == collapsedParent.StringId
                    && (snapshot.IsTown || snapshot.IsCastle))
                .ToList();

            return parentFiefs.Count > 0
                && parentFiefs.All(snapshot => Settlement.All
                    .FirstOrDefault(settlement => settlement.StringId == snapshot.SettlementId)
                    ?.OwnerClan?.Kingdom == rebelKingdom);
        }

        private void ApplyDecisiveCollapsedCivilWarDemand(
            FactionObject faction,
            Kingdom restoredKingdom,
            Kingdom formerRebelKingdom,
            Clan victoriousLeader,
            Clan deposedRuler,
            List<Clan> loyalists,
            string civilWarStartFiefSnapshot,
            Hero abdicationHeir)
        {
            if (faction == null || restoredKingdom == null || victoriousLeader == null)
                return;

            loyalists = FilterTribunalLosingClans(loyalists, restoredKingdom, victoriousLeader);
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            switch (faction.Type)
            {
                case FactionType.InstallRuler:
                {
                    ClearSuccessionStateForKingdom(restoredKingdom, "decisive collapsed install-ruler victory");
                    Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>()?.MarkAsUsurper(restoredKingdom);
                    EnsureValidRulingClan(restoredKingdom, victoriousLeader, forcePreferred: true);
                    titleBehavior?.TrySetKingdomTitleRuler(
                        restoredKingdom,
                        victoriousLeader,
                        legalTransfer: true,
                        reason: "decisive collapsed install-ruler victory");

                    if (TryQueueChallengeTribunal(faction, restoredKingdom)) { }
                    else if (victoriousLeader == Clan.PlayerClan)
                    {
                        QueuePlayerTribunal(
                            restoredKingdom,
                            restoredKingdom,
                            loyalists,
                            formerRebelKingdom,
                            victoriousLeader,
                            GetLoyalistExileCause(faction.Type),
                            civilWarStartFiefSnapshot);
                    }
                    else
                    {
                        ApplyPostWarConsequences(
                            restoredKingdom,
                            loyalists,
                            restoredKingdom,
                            out bool anyExec,
                            out bool anyForgive,
                            out bool anyExile,
                            out bool oldKingExec,
                            formerRebelKingdom,
                            GetLoyalistExileCause(faction.Type),
                            civilWarStartFiefSnapshot);
                        FlushTribunalReactions();
                    }

                    EnsureInstallRulerPostcondition(restoredKingdom, victoriousLeader, "decisive collapsed install-ruler victory");
                    break;
                }

                case FactionType.Abdication:
                {
                    if (faction.IsHereditaryAbdication)
                    {
                        CrownAccessionBehavior.Instance.BeginForcedAbdication(restoredKingdom,
                            deposedRuler, faction.AbdicationMonarch, faction.AbdicationLaws, faction.AbdicationCauseId, abdicationHeir);
                        QueueDeferredSuccessionTribunal(restoredKingdom, restoredKingdom, loyalists,
                            formerRebelKingdom, GetLoyalistExileCause(faction.Type), civilWarStartFiefSnapshot, faction.AbdicationCauseId);
                        break;
                    }
                    if (ElectiveSuccessionBehavior.UsesElection(restoredKingdom))
                    {
                        ElectiveSuccessionBehavior.Instance.BeginDeposition(restoredKingdom, victoriousLeader,
                            faction.AbdicationMonarch ?? ElectiveSuccessionBehavior.LegalHead(deposedRuler));
                        QueueDeferredSuccessionTribunal(restoredKingdom, restoredKingdom, loyalists,
                            formerRebelKingdom, GetLoyalistExileCause(faction.Type), civilWarStartFiefSnapshot);
                        EnsureAbdicationElectionPostcondition(restoredKingdom, victoriousLeader, deposedRuler);
                        break;
                    }
                    ClearSuccessionStateForKingdom(restoredKingdom, "decisive collapsed abdication victory");
                    Clan interimRuler = NobleClanEligibilityHelper.IsValidRulingClan(deposedRuler, restoredKingdom)
                        && deposedRuler != victoriousLeader
                            ? deposedRuler
                            : loyalists.FirstOrDefault(clan => clan != victoriousLeader
                                && NobleClanEligibilityHelper.IsValidRulingClan(clan, restoredKingdom));

                    if (interimRuler == null)
                    {
                        EnsureValidRulingClan(restoredKingdom, victoriousLeader, forcePreferred: true);
                        titleBehavior?.TrySetKingdomTitleRuler(
                            restoredKingdom,
                            victoriousLeader,
                            legalTransfer: true,
                            reason: "abdication victory with no surviving opposing candidate");
                        BellumCivileLogger.Log($"Abdication victory could not queue an election because no valid opposing clan survived; claimant crowned by necessity. kingdom={restoredKingdom.StringId}; claimant={victoriousLeader.StringId}.");
                        break;
                    }

                    restoredKingdom.RulingClan = interimRuler;
                    titleBehavior?.TrySetKingdomTitleRuler(
                        restoredKingdom,
                        interimRuler,
                        legalTransfer: false,
                        reason: "interim ruler pending decisive collapsed abdication election");
                    QueueDeferredSuccessionVote(restoredKingdom, victoriousLeader, interimRuler, canReplacePlayerRuler: true);
                    QueueDeferredSuccessionTribunal(
                        restoredKingdom,
                        restoredKingdom,
                        loyalists,
                        formerRebelKingdom,
                        GetLoyalistExileCause(faction.Type),
                        civilWarStartFiefSnapshot);
                    EnsureAbdicationElectionPostcondition(restoredKingdom, victoriousLeader, interimRuler);
                    break;
                }
            }
        }

        private void EnsureInstallRulerPostcondition(Kingdom kingdom, Clan intendedRuler, string reason)
        {
            if (kingdom == null || intendedRuler == null)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan titleRuler = titleBehavior?.GetDeFactoSovereignClan(kingdom);
            if (kingdom.RulingClan != intendedRuler || titleRuler != intendedRuler)
            {
                Clan previousRuler = kingdom.RulingClan;
                kingdom.RulingClan = intendedRuler;
                titleBehavior?.TrySetKingdomTitleRuler(kingdom, intendedRuler, legalTransfer: true, reason: reason + " postcondition repair");
                BellumCivileLogger.Log($"Repaired failed Install Ruler postcondition; kingdom={kingdom.StringId}; previous={previousRuler?.StringId ?? "null"}; intended={intendedRuler.StringId}; previous_title_ruler={titleRuler?.StringId ?? "null"}; reason={reason}.");
            }

            BellumCivileLogger.Log($"Install Ruler settlement verified; kingdom={kingdom.StringId}; ruling_clan={kingdom.RulingClan?.StringId ?? "null"}; title_ruler={titleBehavior?.GetDeFactoSovereignClan(kingdom)?.StringId ?? "null"}; intended={intendedRuler.StringId}; reason={reason}.");
        }

        private void EnsureAbdicationElectionPostcondition(Kingdom kingdom, Clan candidate, Clan excludedClan)
        {
            EnsureCollectionsInitialized();
            var deposition = ElectiveSuccessionBehavior.Instance?.PendingDeposition(kingdom);
            if (deposition != null)
            {
                BellumCivileLogger.Log($"Abdication deliberation settlement {(deposition.InterimHouse == candidate ? "verified" : "FAILED")}; kingdom={kingdom.StringId}; caretaker={deposition.InterimHouse?.StringId}; deposed={deposition.DeposedRuler?.StringId}; election_day={deposition.DepositionElectionDate.ToDays}; prepared={deposition.InterimPrepared}.");
                return;
            }
            bool queued = kingdom != null
                && candidate != null
                && _pendingSuccessionCandidates.TryGetValue(kingdom.StringId, out string candidateId)
                && candidateId == candidate.StringId
                && _pendingSuccessionExcluded.TryGetValue(kingdom.StringId, out string excludedId)
                && excludedId == excludedClan?.StringId;

            BellumCivileLogger.Log($"Abdication election settlement {(queued ? "verified" : "FAILED")}; kingdom={kingdom?.StringId ?? "null"}; candidate={candidate?.StringId ?? "null"}; excluded={excludedClan?.StringId ?? "null"}; pending={queued}.");
        }

        public bool TryRepairEliminatedKingdomWithLiveState(Kingdom eliminatedKingdom, string reason = null)
        {
            if (CivilWarConflictBehavior.IsRealmTransferPending(eliminatedKingdom)) return false;
            if (eliminatedKingdom == null || !eliminatedKingdom.IsEliminated || IsBellumCivileTemporaryKingdom(eliminatedKingdom))
                return false;

            List<Clan> liveClans = GetLiveNobleClans(eliminatedKingdom).ToList();
            if (liveClans.Count == 0 || !HasLiveHoldings(eliminatedKingdom, liveClans))
                return false;

            Clan preferredRuler = SelectEmergencySuccessorRuler(eliminatedKingdom, liveClans);
            if (preferredRuler == null)
                return false;

            Kingdom existingRestored = FindExistingRestoredSuccessor(eliminatedKingdom);
            if (existingRestored != null)
            {
                TransferLiveClansFromEliminatedKingdom(eliminatedKingdom, existingRestored, preferredRuler);
                EnsureValidRulingClan(existingRestored, preferredRuler);
                Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.RegisterRestoredRealmMantle(eliminatedKingdom, existingRestored, preferredRuler, "emergency restored successor reused");
                Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(existingRestored);
                RepairCompatibilityStateNow($"repaired eliminated kingdom {eliminatedKingdom.StringId} into existing successor {existingRestored.StringId}: {reason ?? "unknown"}");
                BellumCivileLogger.Log($"Repaired eliminated kingdom {eliminatedKingdom.StringId} by moving live clans to existing successor {existingRestored.StringId}; reason={reason ?? "unknown"}.");
                return true;
            }

            Kingdom temporarySuccessor = FindLiveTemporarySuccessorForEliminatedParent(eliminatedKingdom);
            if (temporarySuccessor != null)
            {
                Clan successorLeader = SelectEmergencySuccessorRuler(temporarySuccessor, GetLiveNobleClans(temporarySuccessor))
                                      ?? preferredRuler;
                Kingdom restoredFromRebels = CreateRestoredSuccessorKingdom(eliminatedKingdom, temporarySuccessor, successorLeader, runImmediateRepair: false);
                if (restoredFromRebels != null)
                {
                    TransferLiveClansFromEliminatedKingdom(eliminatedKingdom, restoredFromRebels, preferredRuler);
                    EnsureValidRulingClan(restoredFromRebels, successorLeader);
                    Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.RegisterRestoredRealmMantle(eliminatedKingdom, restoredFromRebels, successorLeader, "emergency restored successor from temporary realm");
                    Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(restoredFromRebels);
                    RepairCompatibilityStateNow($"repaired eliminated kingdom {eliminatedKingdom.StringId} through temporary successor {temporarySuccessor.StringId}: {reason ?? "unknown"}");
                    BellumCivileLogger.Log($"Repaired eliminated kingdom {eliminatedKingdom.StringId} by restoring temporary successor {temporarySuccessor.StringId} into {restoredFromRebels.StringId}; reason={reason ?? "unknown"}.");
                    return true;
                }
            }

            Kingdom restoredReplacement = CreateRestoredReplacementKingdom(eliminatedKingdom, preferredRuler);
            if (restoredReplacement == null)
                return false;

            BellumCivileLogger.Log($"Repaired eliminated kingdom {eliminatedKingdom.StringId} by creating replacement successor {restoredReplacement.StringId}; reason={reason ?? "unknown"}.");
            return true;
        }

        // What does this complex formula do?
        // Calculates the probability of the victorious ruler executing, forgiving, or confiscating land from the defeated lords, scaling heavily on the ruler's Mercy and Generosity traits.
        private void ApplyPostWarConsequences(Kingdom winningKingdom, List<Clan> losingClans, Kingdom destinationKingdom, out bool anyExecuted, out bool anyForgiven, out bool anyExiled, out bool oldKingExecuted, Kingdom excludedRefuge = null, ExileCause exileCause = ExileCause.Unknown, string civilWarStartFiefSnapshot = null, string reactionGroupId = null)
        {
            anyExecuted = false;
            anyForgiven = false;
            anyExiled = false;
            oldKingExecuted = false;

            Hero winnerRuler = winningKingdom.RulingClan?.Leader;
            if (winnerRuler == null) return;
            Clan victorClan = winnerRuler.Clan ?? winningKingdom.RulingClan;

            int mercyLevel = winnerRuler.GetTraitLevel(DefaultTraits.Mercy);
            float executionChance = O.PostWarExecutionBase;
            if (mercyLevel <= -2) executionChance += C.PostWarExecutionMercyStep2;
            else if (mercyLevel == -1) executionChance += C.PostWarExecutionMercyStep1;
            else if (mercyLevel == 1) executionChance -= C.PostWarExecutionMercyStep1;
            else if (mercyLevel >= 2) executionChance -= C.PostWarExecutionMercyStep2;
            executionChance = MathF.Clamp(executionChance, 0f, 1f);

            int generosityLevel = winnerRuler.GetTraitLevel(DefaultTraits.Generosity);
            float confiscateChance = O.PostWarConfiscationBase;
            if (generosityLevel <= -2) confiscateChance += C.PostWarConfiscationGenerosityStep2;
            else if (generosityLevel == -1) confiscateChance += C.PostWarConfiscationGenerosityStep1;
            else if (generosityLevel == 1) confiscateChance -= C.PostWarConfiscationGenerosityStep1;
            else if (generosityLevel >= 2) confiscateChance -= C.PostWarConfiscationGenerosityStep2;
            confiscateChance = MathF.Clamp(confiscateChance, 0f, 1f);

            losingClans = FilterTribunalLosingClans(losingClans, winningKingdom, victorClan);
            if (losingClans.Count == 0) return;

            string groupId = reactionGroupId ?? "ai_" + (_nextTribunalGroupId++);
            BeginTribunalReactions(groupId, winningKingdom, losingClans);

            HashSet<Clan> defeatedClans = BuildClanSet(losingClans);
            Dictionary<Clan, int> civilWarStartFiefCounts = ResolveCivilWarStartFiefSnapshot(civilWarStartFiefSnapshot);
            List<Clan> rewardCandidates = BuildRewardCandidates(winningKingdom, winnerRuler, defeatedClans, civilWarStartFiefCounts);
            int nextRewardeeIndex = 0;

            foreach (Clan losingClan in losingClans.ToList())
            {
                if (losingClan == null || losingClan.IsUnderMercenaryService || IsNonPlayerMinorClan(losingClan)) continue;

                float individualExecutionChance = RoyalHeirTribunalRules.ExecutionChance(executionChance,
                    IsDefeatedRoyalHeir(winningKingdom, winnerRuler, losingClan.Leader, exileCause));
                bool willExecute = losingClan.Leader != null && MBRandom.RandomFloat < individualExecutionChance;
                if (willExecute)
                {
                    QueuePostWarExecution(winningKingdom, winnerRuler, losingClan, destinationKingdom, excludedRefuge, exileCause,
                        out bool executedOldKing, out bool exileExpected, out bool executionQueued, groupId);
                    if (executionQueued)
                    {
                        if (executedOldKing) oldKingExecuted = true;
                        else anyExecuted = true;
                        if (exileExpected) anyExiled = true;
                    }
                    continue;
                }

                bool willConfiscate = losingClan.Fiefs.Count > 0 && MBRandom.RandomFloat <= confiscateChance;
                if (willConfiscate)
                {
                    bool confiscated = ApplyPostWarPunishment(winningKingdom, winnerRuler, losingClan, rewardCandidates, ref nextRewardeeIndex);
                    bool exiled = ApplyPostWarExileOrSubmission(losingClan, destinationKingdom, excludedRefuge, exileCause);
                    if (exiled)
                        anyExiled = true;
                    RecordTribunalReaction(groupId, losingClan, confiscated || exiled ? -10 : 0);
                    continue;
                }

                ApplyPostWarPardon(losingClan, winnerRuler, destinationKingdom);
                RecordTribunalReaction(groupId, losingClan, 10, pardon: true);
                anyForgiven = true;
            }
            CloseTribunalReactions(groupId);
        }

        private void AutoResolveTribunalItem(string itemKey, Clan losingClan, Kingdom rewardKingdom, Kingdom destinationKingdom, Kingdom excludedRefuge, Clan victorClan)
        {
            Hero victor = victorClan?.Leader;
            if (victor == null)
            {
                RemoveTribunalItem(itemKey);
                return;
            }

            string groupId = itemKey.Substring(0, itemKey.IndexOf('|'));
            ExileCause exileCause = GetStoredExileCause(_tribunalExileCauseIds, groupId);
            Dictionary<Clan, int> civilWarStartFiefCounts = ResolveCivilWarStartFiefSnapshot(GetDictionaryValue(_tribunalCivilWarStartFiefSnapshots, groupId));
            int mercyLevel = victor.GetTraitLevel(DefaultTraits.Mercy);
            float executionChance = O.PostWarExecutionBase;
            if (mercyLevel <= -2) executionChance += C.PostWarExecutionMercyStep2;
            else if (mercyLevel == -1) executionChance += C.PostWarExecutionMercyStep1;
            else if (mercyLevel == 1) executionChance -= C.PostWarExecutionMercyStep1;
            else if (mercyLevel >= 2) executionChance -= C.PostWarExecutionMercyStep2;
            executionChance = MathF.Clamp(executionChance, 0f, 1f);

            int generosityLevel = victor.GetTraitLevel(DefaultTraits.Generosity);
            float confiscateChance = O.PostWarConfiscationBase;
            if (generosityLevel <= -2) confiscateChance += C.PostWarConfiscationGenerosityStep2;
            else if (generosityLevel == -1) confiscateChance += C.PostWarConfiscationGenerosityStep1;
            else if (generosityLevel == 1) confiscateChance -= C.PostWarConfiscationGenerosityStep1;
            else if (generosityLevel >= 2) confiscateChance -= C.PostWarConfiscationGenerosityStep2;
            confiscateChance = MathF.Clamp(confiscateChance, 0f, 1f);

            HashSet<Clan> defeatedClans = ResolveTribunalLosingClanSet(groupId, losingClan, rewardKingdom, victorClan);
            List<Clan> rewardCandidates = BuildRewardCandidates(rewardKingdom, victor, defeatedClans, civilWarStartFiefCounts);
            int nextRewardeeIndex = 0;

            float individualExecutionChance = RoyalHeirTribunalRules.ExecutionChance(executionChance,
                IsDefeatedRoyalHeir(rewardKingdom, victor, losingClan.Leader, exileCause));
            bool willExecute = losingClan.Leader != null && MBRandom.RandomFloat < individualExecutionChance;
            if (willExecute)
            {
                QueuePostWarExecution(rewardKingdom, victor, losingClan, destinationKingdom, excludedRefuge, exileCause,
                    out bool executedOldKing, out bool exileExpected, out bool executionQueued, groupId);
                if (executionQueued)
                {
                    MarkTribunalExecuted(groupId, executedOldKing);
                    if (exileExpected) MarkTribunalExiled(groupId);
                }

                RemoveTribunalItem(itemKey);
                return;
            }

            bool willConfiscate = losingClan.Fiefs.Count > 0 && MBRandom.RandomFloat <= confiscateChance;
            if (willConfiscate)
            {
                bool confiscated = ApplyPostWarPunishment(rewardKingdom, victor, losingClan, rewardCandidates, ref nextRewardeeIndex);
                bool exiled = ApplyPostWarExileOrSubmission(losingClan, destinationKingdom, excludedRefuge, exileCause);
                if (exiled)
                    MarkTribunalExiled(groupId);
                RecordTribunalReaction(groupId, losingClan, confiscated || exiled ? -10 : 0);

                RemoveTribunalItem(itemKey);
                return;
            }

            ApplyPostWarPardon(losingClan, victor, destinationKingdom);
            RecordTribunalReaction(groupId, losingClan, 10, pardon: true);
            MarkTribunalForgiven(groupId);

            RemoveTribunalItem(itemKey);
        }

        private static bool IsDefeatedRoyalHeir(Kingdom realm, Hero victor, Hero defeated, ExileCause cause)
        {
            if (realm == null || victor == null || defeated == null || victor == defeated
                || realm.RulingClan?.Leader != victor || !CrownAccessionBehavior.IsHereditaryRealm(realm)
                || (cause != ExileCause.RebelIndependence && cause != ExileCause.RebelAbdication
                    && cause != ExileCause.RebelInstallRuler)) return false;
            // Defeated houses may still belong to the temporary rebel realm at sentencing.
            return HereditaryRealmSuccession.OrderLine(new[] { defeated }, victor,
                SuccessionLawHelper.GetLawsForKingdom(realm), CrownAccessionBehavior.Instance?.GetAbdicatedMonarchs(realm)).Contains(defeated);
        }

        private void ApplyTribunalVerdict(TribunalVerdict verdict, string groupId, Clan losingClan, Kingdom rewardKingdom, Kingdom destinationKingdom, Kingdom excludedRefuge, Hero victor)
        {
            ExileCause exileCause = GetStoredExileCause(_tribunalExileCauseIds, groupId);
            Dictionary<Clan, int> civilWarStartFiefCounts = ResolveCivilWarStartFiefSnapshot(GetDictionaryValue(_tribunalCivilWarStartFiefSnapshots, groupId));
            switch (verdict)
            {
                case TribunalVerdict.Pardon:
                    ApplyPostWarPardon(losingClan, victor, destinationKingdom);
                    RecordTribunalReaction(groupId, losingClan, 10, pardon: true);
                    MarkTribunalForgiven(groupId);
                    break;

                case TribunalVerdict.Punish:
                {
                    List<Clan> rewardCandidates = BuildRewardCandidates(rewardKingdom, victor, ResolveTribunalLosingClanSet(groupId, losingClan, rewardKingdom, victor?.Clan), civilWarStartFiefCounts);
                    int nextRewardeeIndex = 0;
                    bool confiscated = ApplyPostWarPunishment(rewardKingdom, victor, losingClan, rewardCandidates, ref nextRewardeeIndex);
                    bool exiled = ApplyPostWarExileOrSubmission(losingClan, destinationKingdom, excludedRefuge, exileCause);
                    if (exiled)
                        MarkTribunalExiled(groupId);
                    RecordTribunalReaction(groupId, losingClan, confiscated || exiled ? -10 : 0);
                    break;
                }

                case TribunalVerdict.Execute:
                {
                    QueuePostWarExecution(rewardKingdom, victor, losingClan, destinationKingdom, excludedRefuge, exileCause,
                        out bool executedOldKing, out bool exileExpected, out bool executionQueued, groupId);
                    if (executionQueued)
                    {
                        MarkTribunalExecuted(groupId, executedOldKing);
                        if (exileExpected) MarkTribunalExiled(groupId);
                    }
                    break;
                }
            }
        }

        private static List<Clan> BuildRewardCandidates(Kingdom rewardKingdom, Hero winnerRuler, HashSet<Clan> excludedClans = null, Dictionary<Clan, int> civilWarStartFiefCounts = null)
        {
            if (rewardKingdom == null || winnerRuler == null) return new List<Clan>();

            int generosityLevel = winnerRuler.GetTraitLevel(DefaultTraits.Generosity);
            int calculatingLevel = winnerRuler.GetTraitLevel(DefaultTraits.Calculating);

            return rewardKingdom.Clans
                .Where(c => IsValidRewardCandidate(c, winnerRuler, rewardKingdom, excludedClans))
                .OrderBy(c => HasGainedFiefsSinceCivilWarStart(c, civilWarStartFiefCounts) ? 1 : 0)
                .ThenByDescending(c => ScoreRewardCandidate(c, winnerRuler, generosityLevel, calculatingLevel))
                .ToList();
        }

        private static Dictionary<Clan, int> ResolveCivilWarStartFiefSnapshot(string snapshot)
        {
            Dictionary<Clan, int> result = new Dictionary<Clan, int>();
            if (string.IsNullOrEmpty(snapshot))
                return result;

            foreach (string entry in snapshot.Split(','))
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                int separator = entry.IndexOf(':');
                if (separator <= 0 || separator >= entry.Length - 1)
                    continue;

                string clanId = entry.Substring(0, separator);
                string countText = entry.Substring(separator + 1);
                Clan clan = ResolveClanById(clanId);
                if (clan == null || !int.TryParse(countText, out int startFiefCount))
                    continue;

                result[clan] = startFiefCount;
            }

            return result;
        }

        private static bool HasGainedFiefsSinceCivilWarStart(Clan clan, Dictionary<Clan, int> civilWarStartFiefCounts)
        {
            return clan != null
                && civilWarStartFiefCounts != null
                && civilWarStartFiefCounts.TryGetValue(clan, out int startFiefCount)
                && clan.Fiefs.Count > startFiefCount;
        }

        private void ApplyPostWarPardon(Clan losingClan, Hero winnerRuler, Kingdom destinationKingdom)
        {
            TextObject pardonText = new TextObject("{=BC_Resolution_MercyPardon}In an act of mercy, {RULER_NAME} has pardoned the {CLAN_NAME} and restored their standing.");
            pardonText.SetTextVariable("RULER_NAME", winnerRuler.Name);
            pardonText.SetTextVariable("CLAN_NAME", losingClan.Name);
            BellumCivileNotifications.Show(pardonText, BellumNotificationColors.Success, primaryKingdom: destinationKingdom, primaryClan: winnerRuler?.Clan, secondaryClan: losingClan);

            if (losingClan.Leader != null)
                RelationMemoryService.ApplyChange(losingClan.Leader, winnerRuler, C.PostWarPardonBonus, true,
                    RelationMemorySources.PardonedMyHouse, 10f, RelationMemoryScope.House);

            if (losingClan.Kingdom != destinationKingdom)
                KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(losingClan, destinationKingdom, showNotification: false);
        }

        private bool ApplyPostWarPunishment(Kingdom rewardKingdom, Hero winnerRuler, Clan losingClan, List<Clan> rewardCandidates, ref int nextRewardeeIndex)
        {
            Town mostProsperousFief = losingClan.Fiefs
                .Where(f => f.IsTown || f.IsCastle)
                .OrderByDescending(f => f.Prosperity)
                .FirstOrDefault();

            if (mostProsperousFief == null) return false;

            bool confiscateToPlayer = winnerRuler == Hero.MainHero
                && Clan.PlayerClan != null
                && Clan.PlayerClan.Kingdom == rewardKingdom;

            Clan loyalistRewardee = confiscateToPlayer
                ? Clan.PlayerClan
                : (rewardCandidates.Count > 0
                    ? rewardCandidates[nextRewardeeIndex++ % rewardCandidates.Count]
                    : rewardKingdom.RulingClan);

            if (loyalistRewardee?.Leader != null)
                ChangeOwnerOfSettlementAction.ApplyByDefault(loyalistRewardee.Leader, mostProsperousFief.Settlement);
            if (loyalistRewardee == null || mostProsperousFief.Settlement.OwnerClan != loyalistRewardee)
                return false;

            TextObject confiscateText = confiscateToPlayer
                ? new TextObject("{=BC_Resolution_SpoilsOfWar_PlayerCustody}As spoils of war, {RULER_NAME} has stripped the {CLAN_NAME} of {FIEF_NAME}. The fief has been placed under {RECIPIENT_CLAN_NAME} authority pending redistribution.")
                : new TextObject("{=BC_Resolution_SpoilsOfWar}As spoils of war, {RULER_NAME} has stripped the {CLAN_NAME} of {FIEF_NAME} and granted it to the {RECIPIENT_CLAN_NAME}.");
            confiscateText.SetTextVariable("RULER_NAME", winnerRuler.Name);
            confiscateText.SetTextVariable("CLAN_NAME", losingClan.Name);
            confiscateText.SetTextVariable("FIEF_NAME", mostProsperousFief.Name);
            confiscateText.SetTextVariable("RECIPIENT_CLAN_NAME", loyalistRewardee?.Name ?? rewardKingdom.RulingClan?.Name ?? rewardKingdom.Name);
            BellumCivileNotifications.Show(confiscateText, BellumNotificationColors.Land, primaryKingdom: rewardKingdom, primaryClan: winnerRuler?.Clan, secondaryClan: losingClan);

            if (losingClan.Leader != null && !losingClan.Leader.IsDead)
                RelationMemoryService.ApplyChange(losingClan.Leader, winnerRuler, C.PostWarConfiscationPenalty, false,
                    RelationMemorySources.ConfiscatedMyHouse, 20f, RelationMemoryScope.House, mostProsperousFief.Name?.ToString());

            return true;
        }

        private void QueuePostWarExecution(
            Kingdom rewardKingdom,
            Hero winnerRuler,
            Clan losingClan,
            Kingdom destinationKingdom,
            Kingdom excludedRefuge,
            ExileCause exileCause,
            out bool oldKingExecuted,
            out bool exileExpected,
            out bool executionQueued,
            string reactionGroupId = null)
        {
            EnsureCollectionsInitialized();
            oldKingExecuted = false;
            exileExpected = false;
            executionQueued = false;

            if (rewardKingdom == null || rewardKingdom.IsEliminated
                || destinationKingdom == null || destinationKingdom.IsEliminated
                || winnerRuler == null || winnerRuler.IsDead
                || losingClan == null || losingClan.IsEliminated)
            {
                return;
            }

            if (losingClan == winnerRuler.Clan || losingClan == rewardKingdom.RulingClan)
            {
                BellumCivileLogger.Log($"Skipped invalid post-war execution of victorious/ruling clan {losingClan.StringId} in {rewardKingdom.StringId}.");
                return;
            }

            Hero condemnedLeader = losingClan.Leader;
            if (condemnedLeader == null || condemnedLeader.IsDead || condemnedLeader == winnerRuler)
                return;

            oldKingExecuted = losingClan == destinationKingdom.RulingClan;
            exileExpected = IsPostWarExileExpected(losingClan, destinationKingdom);

            MoveCondemnedClanOutOfTemporaryKingdom(losingClan, destinationKingdom);

            string duplicateKey = _pendingPostWarExecutionOrder.FirstOrDefault(key =>
                GetDictionaryValue(_postWarExecutionCondemnedHeroIds, key) == condemnedLeader.StringId);
            if (!string.IsNullOrEmpty(duplicateKey))
            {
                executionQueued = true;
                // The original tribunal owns this sentence; a second queue is not another verdict.
                RecordTribunalReaction(reactionGroupId, losingClan, 0);
                BellumCivileLogger.Log($"Skipped duplicate deferred post-war execution; hero={condemnedLeader.StringId}; existing={duplicateKey}.");
                return;
            }

            string executionId = "postwar_execution_" + (_nextDeferredExecutionId++);
            _pendingPostWarExecutionOrder.Add(executionId);
            _postWarExecutionRewardKingdomIds[executionId] = rewardKingdom.StringId;
            _postWarExecutionVictorHeroIds[executionId] = winnerRuler.StringId;
            _postWarExecutionCondemnedHeroIds[executionId] = condemnedLeader.StringId;
            _postWarExecutionLosingClanIds[executionId] = losingClan.StringId;
            _postWarExecutionDestinationKingdomIds[executionId] = destinationKingdom.StringId;
            _postWarExecutionExcludedRefugeIds[executionId] = excludedRefuge?.StringId ?? string.Empty;
            _postWarExecutionExileCauseIds[executionId] = (int)exileCause;
            _postWarExecutionOldKing[executionId] = oldKingExecuted;
            LinkTribunalExecution(reactionGroupId, losingClan, executionId);
            executionQueued = true;

            BellumCivileLogger.Log($"Queued deferred post-war execution; id={executionId}; hero={condemnedLeader.StringId}; clan={losingClan.StringId}; victor={winnerRuler.StringId}; destination={destinationKingdom.StringId}.");
        }

        private void ProcessPendingPostWarExecution()
        {
            EnsureCollectionsInitialized();
            if (_pendingPostWarExecutionOrder.Count == 0)
                return;

            string executionId = _pendingPostWarExecutionOrder[0];
            Kingdom rewardKingdom = ResolveKingdomById(GetDictionaryValue(_postWarExecutionRewardKingdomIds, executionId));
            Hero winnerRuler = ResolveHeroById(GetDictionaryValue(_postWarExecutionVictorHeroIds, executionId));
            Hero condemnedLeader = ResolveHeroById(GetDictionaryValue(_postWarExecutionCondemnedHeroIds, executionId));
            Clan losingClan = ResolveClanById(GetDictionaryValue(_postWarExecutionLosingClanIds, executionId));
            Kingdom destinationKingdom = ResolveKingdomById(GetDictionaryValue(_postWarExecutionDestinationKingdomIds, executionId));
            Kingdom excludedRefuge = ResolveKingdomById(GetDictionaryValue(_postWarExecutionExcludedRefugeIds, executionId));
            ExileCause exileCause = GetStoredExileCause(_postWarExecutionExileCauseIds, executionId);
            bool oldKingExecuted = _postWarExecutionOldKing.TryGetValue(executionId, out bool storedOldKing) && storedOldKing;

            if (rewardKingdom == null || rewardKingdom.IsEliminated
                || destinationKingdom == null || destinationKingdom.IsEliminated
                || winnerRuler == null || winnerRuler.IsDead
                || winnerRuler.Clan == null || winnerRuler.Clan.Kingdom != rewardKingdom
                || condemnedLeader == null || condemnedLeader.IsDead
                || losingClan == null || losingClan.IsEliminated
                || condemnedLeader.Clan != losingClan || losingClan.Leader != condemnedLeader
                || condemnedLeader == winnerRuler
                || losingClan == winnerRuler.Clan || losingClan == rewardKingdom.RulingClan)
            {
                BellumCivileLogger.Log($"Discarded invalid deferred post-war execution; id={executionId}; hero={condemnedLeader?.StringId ?? "null"}; clan={losingClan?.StringId ?? "null"}; victor={winnerRuler?.StringId ?? "null"}.");
                RemovePendingPostWarExecution(executionId);
                return;
            }

            if (losingClan.Kingdom != null
                && losingClan.Kingdom != destinationKingdom
                && IsBellumCivileTemporaryKingdom(losingClan.Kingdom))
            {
                MoveCondemnedClanOutOfTemporaryKingdom(losingClan, destinationKingdom);
                if (losingClan.Kingdom != destinationKingdom)
                {
                    BellumCivileLogger.Log($"Discarded deferred post-war execution because clan restoration could not be completed; id={executionId}; clan={losingClan.StringId}.");
                    RemovePendingPostWarExecution(executionId);
                    return;
                }

                // Give map and party listeners another hourly tick after a corrective transfer.
                return;
            }

            RemovePendingPostWarExecution(executionId, settleReaction: false);
            Hero previousExecution = _activeTribunalExecution;
            if (HasTribunalExecutionReaction(executionId)) _activeTribunalExecution = condemnedLeader;
            bool exiledAfterSentence = false;
            try
            {
                KillCharacterAction.ApplyByExecution(condemnedLeader, winnerRuler, false);
                if (condemnedLeader.IsDead)
                    ApplyPostWarExecutionEffects(rewardKingdom, winnerRuler, losingClan, condemnedLeader, exileCause, oldKingExecuted);
                else
                    BellumCivileLogger.Log($"Deferred post-war execution was blocked by another system; id={executionId}; hero={condemnedLeader.StringId}.");
                exiledAfterSentence = ApplyPostWarExileOrSubmission(losingClan, destinationKingdom, excludedRefuge, exileCause);
            }
            finally
            {
                _activeTribunalExecution = previousExecution;
                CompleteTribunalExecution(executionId, condemnedLeader.IsDead, exiledAfterSentence);
            }
        }

        private void RemovePendingPostWarExecution(string executionId, bool settleReaction = true)
        {
            if (string.IsNullOrEmpty(executionId))
                return;

            if (settleReaction) CompleteTribunalExecution(executionId, executed: false);

            _pendingPostWarExecutionOrder.Remove(executionId);
            _postWarExecutionRewardKingdomIds.Remove(executionId);
            _postWarExecutionVictorHeroIds.Remove(executionId);
            _postWarExecutionCondemnedHeroIds.Remove(executionId);
            _postWarExecutionLosingClanIds.Remove(executionId);
            _postWarExecutionDestinationKingdomIds.Remove(executionId);
            _postWarExecutionExcludedRefugeIds.Remove(executionId);
            _postWarExecutionExileCauseIds.Remove(executionId);
            _postWarExecutionOldKing.Remove(executionId);
        }

        private static bool IsPostWarExileExpected(Clan losingClan, Kingdom destinationKingdom)
        {
            return ExiledClanRecoveryBehavior.CanClanBeRelocated(losingClan)
                && !IsCurrentRulingClanOfDestination(losingClan, destinationKingdom)
                && ExiledClanRecoveryBehavior.IsLandlessForExile(losingClan);
        }

        private static void ApplyPostWarExecutionEffects(Kingdom rewardKingdom, Hero winnerRuler, Clan losingClan, Hero condemnedLeader, ExileCause exileCause, bool oldKingExecuted)
        {
            bool loyalistJudgment = IsLoyalistExecutionJudgment(exileCause);
            TextObject execReason = oldKingExecuted
                ? new TextObject("{=BC_Resolution_ExecReason_OldKing}as punishment for defiance")
                : loyalistJudgment
                    ? new TextObject("{=BC_Resolution_ExecReason_Treason}for the crime of high treason")
                    : new TextObject("{=BC_Resolution_ExecReason_Defiance}as punishment for defiance");
            TextObject execText = loyalistJudgment
                ? new TextObject("{=BC_Resolution_Execution}By royal decree, {RULER_NAME} has executed {LEADER_NAME} of the {CLAN_NAME} {EXECUTION_REASON}!")
                : new TextObject("{=BC_Resolution_Execution_Rebel}By decree, {RULER_NAME} has executed {LEADER_NAME} of the {CLAN_NAME} {EXECUTION_REASON}!");
            execText.SetTextVariable("RULER_NAME", winnerRuler.Name);
            execText.SetTextVariable("LEADER_NAME", condemnedLeader.Name);
            execText.SetTextVariable("CLAN_NAME", losingClan.Name);
            execText.SetTextVariable("EXECUTION_REASON", execReason);
            BellumCivileNotifications.Show(execText, BellumNotificationColors.Danger, primaryKingdom: rewardKingdom, primaryClan: winnerRuler?.Clan, secondaryClan: losingClan);
        }

        private static bool IsLoyalistExecutionJudgment(ExileCause exileCause)
        {
            return exileCause == ExileCause.RebelIndependence
                || exileCause == ExileCause.RebelAbdication
                || exileCause == ExileCause.RebelInstallRuler
                || exileCause == ExileCause.Treason;
        }

        private static void MoveCondemnedClanOutOfTemporaryKingdom(Clan losingClan, Kingdom destinationKingdom)
        {
            if (losingClan == null
                || destinationKingdom == null
                || destinationKingdom.IsEliminated
                || losingClan.Kingdom == null
                || losingClan.Kingdom == destinationKingdom)
            {
                return;
            }

            Kingdom currentKingdom = losingClan.Kingdom;
            bool inTemporaryRebelKingdom = IsBellumCivileTemporaryKingdom(currentKingdom);
            if (!inTemporaryRebelKingdom && currentKingdom.IsAtWarWith(destinationKingdom))
                return;

            RunWithRulerRepairSuppressed(currentKingdom, () =>
                KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(losingClan, destinationKingdom, showNotification: false));
            BellumCivileLogger.Log($"Moved condemned clan {losingClan.StringId} from {currentKingdom.StringId} to {destinationKingdom.StringId} before execution to avoid temporary-kingdom succession cleanup.");
        }

        private bool ApplyPostWarExileOrSubmission(Clan losingClan, Kingdom destinationKingdom, Kingdom excludedRefuge, ExileCause exileCause = ExileCause.Unknown)
        {
            if (!ExiledClanRecoveryBehavior.CanClanBeRelocated(losingClan))
                return false;

            if (IsCurrentRulingClanOfDestination(losingClan, destinationKingdom))
            {
                BellumCivileLogger.Log($"Skipped post-war exile/submission for ruling clan {losingClan.StringId} in {destinationKingdom.StringId}; exileCause={exileCause}. This prevents tribunal cleanup from moving the destination kingdom's ruler after execution or succession state changes.");
                return false;
            }

            if (ExiledClanRecoveryBehavior.IsLandlessForExile(losingClan))
            {
                Hero exileRuler = destinationKingdom?.RulingClan?.Leader;
                if (losingClan.Leader != null
                    && exileRuler != null
                    && losingClan.Leader != exileRuler)
                {
                    RelationMemoryService.ApplyChange(
                        losingClan.Leader,
                        exileRuler,
                        C.PostWarExilePenalty,
                        losingClan == Clan.PlayerClan || exileRuler == Hero.MainHero,
                        RelationMemorySources.ExiledMyHouse,
                        25f,
                        RelationMemoryScope.House);
                }

                (FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>())
                    ?.ConfiscateNonBaronyTitlesForExile(losingClan, destinationKingdom?.RulingClan, destinationKingdom, $"post-war exile {exileCause}");

                if (losingClan == Clan.PlayerClan)
                {
                    ChangeKingdomAction.ApplyByLeaveKingdom(losingClan, false);
                    TextObject expelText = new TextObject("{=BC_Resolution_PlayerExpelled}Stripped of your lands, you have been cast out of the realm. Seek your fortune elsewhere.");
                    BellumCivileNotifications.Show(expelText, BellumNotificationColors.Danger, primaryClan: losingClan, isPersonal: true);
                }
                else
                {
                    ExiledClanRecoveryBehavior exileRecovery = Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
                    ExileResolutionResult exileResult = exileRecovery != null
                        ? exileRecovery.ResolveClanExileWithResult(losingClan, destinationKingdom, excludedRefuge, exileCause)
                        : ExileResolutionResult.Failed;

                    if (exileResult == ExileResolutionResult.MovedToRefuge && losingClan.Kingdom != null)
                    {
                        TextObject exileText = new TextObject("{=BC_Resolution_Banished}Stripped of their lands, the {CLAN_NAME} have been banished and are rumored to have sought exile in {KINGDOM_NAME}.");
                        exileText.SetTextVariable("CLAN_NAME", losingClan.Name);
                        exileText.SetTextVariable("KINGDOM_NAME", losingClan.Kingdom.Name);
                        BellumCivileNotifications.Show(exileText, BellumNotificationColors.Danger, primaryKingdom: destinationKingdom, secondaryKingdom: losingClan.Kingdom, primaryClan: losingClan);
                    }
                    else if (exileResult == ExileResolutionResult.PendingPlayerAsylum)
                    {
                        TextObject pendingText = new TextObject("{=BC_Resolution_AsylumPending}Stripped of their lands, the {CLAN_NAME} have fled into exile and sent envoys seeking asylum.");
                        pendingText.SetTextVariable("CLAN_NAME", losingClan.Name);
                        BellumCivileNotifications.Show(pendingText, BellumNotificationColors.Danger, primaryKingdom: destinationKingdom, primaryClan: losingClan);
                    }
                    else
                    {
                        if (exileRecovery == null)
                            ChangeKingdomAction.ApplyByLeaveKingdom(losingClan, false);

                        TextObject strippedText = new TextObject("{=BC_Resolution_Landless}Stripped of their lands, the {CLAN_NAME} have been banished and are rumored to be wandering in exile.");
                        strippedText.SetTextVariable("CLAN_NAME", losingClan.Name);
                        BellumCivileNotifications.Show(strippedText, BellumNotificationColors.Danger, primaryKingdom: destinationKingdom, primaryClan: losingClan);
                    }
                }

                EnsurePostWarDestinationRulerValid(destinationKingdom, $"landless exile of {losingClan.StringId}");
                return true;
            }

            if (losingClan.Kingdom != destinationKingdom)
            {
                KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(losingClan, destinationKingdom, showNotification: false);
                EnsurePostWarDestinationRulerValid(destinationKingdom, $"submission of {losingClan.StringId}");
            }

            return false;
        }

        private static bool IsCurrentRulingClanOfDestination(Clan clan, Kingdom destinationKingdom)
        {
            return clan != null
                && destinationKingdom != null
                && !destinationKingdom.IsEliminated
                && destinationKingdom.RulingClan == clan;
        }

        private static void EnsurePostWarDestinationRulerValid(Kingdom destinationKingdom, string context)
        {
            if (destinationKingdom == null || destinationKingdom.IsEliminated)
                return;

            Clan rulingClan = destinationKingdom.RulingClan;
            bool rulerIsValid = rulingClan != null
                && !rulingClan.IsEliminated
                && !rulingClan.IsUnderMercenaryService
                && rulingClan.Leader != null
                && !rulingClan.Leader.IsDead
                && rulingClan.Kingdom == destinationKingdom;

            if (rulerIsValid)
                return;

            string before = rulingClan?.StringId ?? "null";
            EnsureValidRulingClan(destinationKingdom);
            string after = destinationKingdom.RulingClan?.StringId ?? "null";
            BellumCivileLogger.Log($"Repaired invalid ruling clan after post-war {context}; kingdom={destinationKingdom.StringId}; before={before}; after={after}.");
        }

        private void MarkTribunalForgiven(string groupId)
        {
            _tribunalAnyForgiven[groupId] = true;
        }

        private void MarkTribunalExecuted(string groupId, bool oldKingExecuted)
        {
            if (oldKingExecuted) _tribunalOldKingExecuted[groupId] = true;
            else _tribunalAnyExecuted[groupId] = true;
        }

        private void MarkTribunalExiled(string groupId)
        {
            _tribunalAnyExiled[groupId] = true;
        }

        private static void ReleaseMercenariesFromKingdom(Kingdom kingdom)
        {
            foreach (Clan clan in kingdom.Clans.Where(c => c.IsUnderMercenaryService).ToList())
                ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
        }

        private Kingdom PromoteRebelKingdomToIndependentKingdom(FactionObject faction, Kingdom rebelKingdom, Clan victoriousLeader)
        {
            List<Kingdom> permanentKingdoms = PromoteRebelKingdomToIndependentKingdoms(faction, rebelKingdom, victoriousLeader);
            return permanentKingdoms.FirstOrDefault(kingdom => kingdom?.RulingClan == victoriousLeader)
                ?? permanentKingdoms.FirstOrDefault();
        }

        private List<Kingdom> PromoteRebelKingdomToIndependentKingdoms(FactionObject faction, Kingdom rebelKingdom, Clan victoriousLeader)
        {
            List<IndependencePartitionGroup> groups = BuildIndependencePartitionGroups(faction, rebelKingdom, victoriousLeader);
            if (groups.Count == 0 && victoriousLeader != null)
            {
                groups.Add(new IndependencePartitionGroup
                {
                    Leader = victoriousLeader,
                    SourceTitle = GetHighestDeFactoTitle(victoriousLeader, faction?.ParentKingdom)
                });
                groups[0].Clans.AddRange(rebelKingdom?.Clans.ToList() ?? new List<Clan>());
            }

            List<System.Tuple<IndependencePartitionGroup, Kingdom>> promotedGroups = new List<System.Tuple<IndependencePartitionGroup, Kingdom>>();
            foreach (IndependencePartitionGroup group in groups)
            {
                Kingdom permanentKingdom = CreateIndependentKingdomForGroup(faction, rebelKingdom, group);
                if (permanentKingdom != null)
                    promotedGroups.Add(System.Tuple.Create(group, permanentKingdom));
            }

            foreach (System.Tuple<IndependencePartitionGroup, Kingdom> promotedGroup in promotedGroups)
                TransferIndependenceGroupToKingdom(faction, rebelKingdom, promotedGroup.Item2, promotedGroup.Item1);

            List<Kingdom> permanentKingdoms = promotedGroups.Select(group => group.Item2).ToList();
            foreach (Kingdom kingdom in permanentKingdoms)
            {
                Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(kingdom);
                EnsureValidRulingClan(kingdom, kingdom.RulingClan, forcePreferred: kingdom.RulingClan != null);
            }

            Kingdom fallbackKingdom = permanentKingdoms.FirstOrDefault(kingdom => kingdom?.RulingClan == victoriousLeader)
                                   ?? permanentKingdoms.FirstOrDefault();
            foreach (var kingdom in permanentKingdoms)
                PreservePromotedRebelForeignWars(rebelKingdom, kingdom, faction.ParentKingdom);
            DrainAndDestroyRebelKingdom(rebelKingdom, fallbackKingdom);
            RepairCompatibilityStateNow($"recognized independence transition into {string.Join(",", permanentKingdoms.Select(kingdom => kingdom?.StringId ?? "null"))}");

            if (permanentKingdoms.Count > 1)
            {
                BellumCivileLogger.Log(
                    $"Partitioned independence victory into {permanentKingdoms.Count} realms; faction={faction?.Name}; leader={victoriousLeader?.StringId ?? "null"}; groups={string.Join("; ", groups.Select(group => $"{group.Leader?.StringId ?? "null"}:{string.Join(",", group.Clans.Select(clan => clan?.StringId ?? "null"))}"))}.");
            }

            return permanentKingdoms;
        }

        private Kingdom CreateIndependentKingdomForGroup(FactionObject faction, Kingdom rebelKingdom, IndependencePartitionGroup group)
        {
            Clan victoriousLeader = group?.Leader;
            if (faction == null || rebelKingdom == null || !KingdomCreationSafetyHelper.IsValidFounder(victoriousLeader))
                return null;

            IndependentKingdomProfile independenceProfile = IndependentKingdomProfileHelper.Create(victoriousLeader, faction.ParentKingdom);

            string indepStringId = faction.ParentKingdom.StringId + "_indep_" + victoriousLeader.StringId;
            Kingdom permanentKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == indepStringId && !k.IsEliminated);
            if (permanentKingdom == null)
            {
                string indepId = indepStringId;
                int idSuffix = 0;
                while (Kingdom.All.Any(k => k.StringId == indepId))
                    indepId = indepStringId + "_" + (++idSuffix);
                permanentKingdom = KingdomCreationSafetyHelper.CreateKingdom(indepId, victoriousLeader);
            }

            if (!KingdomCreationSafetyHelper.PrepareRuler(permanentKingdom, victoriousLeader, "independence victory"))
                return null;

            Settlement capital = victoriousLeader.Settlements.FirstOrDefault()
                              ?? rebelKingdom.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
            var permanentVisuals = KingdomVisualHelper.ResolveIndependentSuccessorKingdomVisuals(faction.ParentKingdom, victoriousLeader, permanentKingdom.StringId);

            permanentKingdom.InitializeKingdom(
                independenceProfile.Name,
                independenceProfile.Name,
                independenceProfile.Culture ?? rebelKingdom.Culture,
                permanentVisuals.Banner,
                permanentVisuals.PrimaryColor,
                permanentVisuals.SecondaryColor,
                capital,
                independenceProfile.EncyclopediaText,
                independenceProfile.EncyclopediaTitle,
                independenceProfile.RulerTitle);
            KingdomVisualHelper.ApplyKingdomPalette(permanentKingdom, permanentVisuals);
            RegisterIndependentRealmSourceTitle(permanentKingdom, independenceProfile, "recognized independence victory");
            EnsureValidRulingClan(permanentKingdom, victoriousLeader);
            ClearTemporaryKingdomRepair(permanentKingdom);
            RebelPolicyHelper.CopyPolicies(rebelKingdom, permanentKingdom);
            return permanentKingdom;
        }

        private static void TransferIndependenceGroupToKingdom(FactionObject faction, Kingdom rebelKingdom, Kingdom permanentKingdom, IndependencePartitionGroup group)
        {
            if (faction == null || rebelKingdom == null || permanentKingdom == null || group?.Leader == null)
                return;

            RunWithRulerRepairSuppressed(rebelKingdom, () =>
            {
                List<Clan> clans = group.Clans
                    .Where(clan => clan != null && !clan.IsEliminated && clan.Kingdom == rebelKingdom)
                    .Distinct()
                    .ToList();

                if (clans.Remove(group.Leader))
                {
                    CourtPoliticalPositionBehavior.MoveToSuccessorRealm(group.Leader, faction.ParentKingdom, permanentKingdom,
                        () => faction.MoveClanToKingdomPreservingCivilWarInfluence(group.Leader, permanentKingdom, preserveCustomBanner: true, showNotification: false));
                    EnsureValidRulingClan(permanentKingdom, group.Leader, forcePreferred: true);
                }

                foreach (Clan clan in clans)
                    CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, faction.ParentKingdom, permanentKingdom,
                        () => faction.MoveClanToKingdomPreservingCivilWarInfluence(clan, permanentKingdom, preserveCustomBanner: true, showNotification: false));

                EnsureValidRulingClan(permanentKingdom, group.Leader, forcePreferred: true);
            });
        }

        private static List<IndependencePartitionGroup> BuildIndependencePartitionGroups(FactionObject faction, Kingdom rebelKingdom, Clan victoriousLeader)
        {
            List<Clan> rebelClans = rebelKingdom?.Clans
                .Where(clan => clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService)
                .Distinct()
                .ToList() ?? new List<Clan>();
            if (victoriousLeader == null || !rebelClans.Contains(victoriousLeader))
                return new List<IndependencePartitionGroup>();

            FeudalTitleRecord leaderTitle = GetHighestDeFactoTitle(victoriousLeader, faction?.ParentKingdom);
            int leaderRank = GetTitleRank(leaderTitle);
            Dictionary<Clan, FeudalTitleRecord> highestTitles = rebelClans.ToDictionary(
                clan => clan,
                clan => GetHighestDeFactoTitle(clan, faction?.ParentKingdom));

            HashSet<Clan> rootClans = new HashSet<Clan> { victoriousLeader };
            foreach (Clan clan in rebelClans)
            {
                if (clan == victoriousLeader)
                    continue;

                FeudalTitleRecord title = highestTitles[clan];
                if (title != null && GetTitleRank(title) >= leaderRank)
                    rootClans.Add(clan);
            }

            Dictionary<Clan, IndependencePartitionGroup> groupsByRoot = rootClans
                .OrderByDescending(clan => clan == victoriousLeader)
                .ThenByDescending(clan => GetTitleRank(highestTitles.ContainsKey(clan) ? highestTitles[clan] : null))
                .ThenBy(clan => clan.Name?.ToString() ?? clan.StringId)
                .ToDictionary(
                    clan => clan,
                    clan => new IndependencePartitionGroup
                    {
                        Leader = clan,
                        SourceTitle = highestTitles.ContainsKey(clan) ? highestTitles[clan] : null
                    });

            foreach (Clan root in rootClans)
                groupsByRoot[root].Clans.Add(root);

            foreach (Clan clan in rebelClans.Where(clan => !rootClans.Contains(clan)))
            {
                FeudalTitleRecord title = highestTitles[clan];
                Clan root = FindNearestRebelRootLiege(title, rootClans);
                if (root == null)
                    root = victoriousLeader;

                groupsByRoot[root].Clans.Add(clan);
            }

            return groupsByRoot.Values
                .Where(group => group.Leader != null && group.Clans.Count > 0)
                .OrderByDescending(group => group.Leader == victoriousLeader)
                .ThenByDescending(group => GetTitleRank(group.SourceTitle))
                .ThenBy(group => group.Leader.Name?.ToString() ?? group.Leader.StringId)
                .ToList();
        }

        private static Clan FindNearestRebelRootLiege(FeudalTitleRecord title, HashSet<Clan> rootClans)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || title == null || rootClans == null || rootClans.Count == 0)
                return null;

            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null && visited.Add(current.TitleId))
            {
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(current, FeudalHierarchyMode.DeFacto);
                if (parent == null)
                    return null;

                Clan holder = ResolveClanById(parent.DeFactoHolderClanId);
                if (holder != null && rootClans.Contains(holder))
                    return holder;

                current = parent;
            }

            return null;
        }

        private static FeudalTitleRecord GetHighestDeFactoTitle(Clan clan, Kingdom parentKingdom)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || clan == null)
                return null;

            string parentKingdomTitleId = FeudalTitleBehavior.BuildKingdomTitleId(parentKingdom);
            return titleBehavior.GetTitlesHeldByClan(clan, deJure: false)
                .Where(title => title != null
                    && title.IsActive
                    && !string.Equals(title.TitleId, parentKingdomTitleId, System.StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(title => title.TitleType)
                .ThenByDescending(title => string.Equals(title.DeJureHolderClanId, clan.StringId, System.StringComparison.Ordinal))
                .ThenBy(title => title.Name ?? string.Empty)
                .FirstOrDefault();
        }

        private static int GetTitleRank(FeudalTitleRecord title)
        {
            return title == null ? -1 : (int)title.TitleType;
        }

        private static void RegisterIndependentRealmSourceTitle(Kingdom independentKingdom, IndependentKingdomProfile profile, string reason)
        {
            if (independentKingdom == null || !profile.UsesExistingSourceTitle)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord sourceTitle = titleBehavior?.GetTitle(profile.SourceTitleId);
            if (sourceTitle != null)
                titleBehavior.RegisterIndependentRealmShell(independentKingdom, sourceTitle, reason);
        }

        internal void ResolveWhitePeace(FactionObject faction, Kingdom rebelKingdom, bool demandSatisfied = false, TextObject cause = null)
        {
            CivilWarTransitionDiagnostics.Log("white peace requested", faction, rebelKingdom,
                $"demand_satisfied={demandSatisfied}; cause={cause}");
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebelKingdom);
            if (resultNotice != null) resultNotice.Names["RESULT_KIND"] = demandSatisfied ? "satisfied" : cause != null ? "ended" : "peace";
            ConflictOutcomeBehavior.Capture(resultNotice, "DEMAND_RESULT", faction.Type == FactionType.Abdication
                ? new TextObject("{=BC_Result_DepositionFulfilled}the former ruler removed from the throne")
                : new TextObject("{=BC_Result_CrownFulfilled}{LEADER} holding the crown").SetTextVariable("LEADER", faction.ParentKingdom.Leader?.Name));
            ConflictOutcomeBehavior.Capture(resultNotice, "CAUSE", cause);
            if (SuccessionChallengeBehavior.Instance?.PrepareWarOutcome(faction, SuccessionChallengeOutcome.WhitePeace, faction.ParentKingdom) == false) return;
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(faction, RebellionSummaryOutcome.WhitePeace);
            CompleteCivilWarTracker(faction, rebelKingdom, "white peace");

            ApplyCivilWarPeaceIfNeeded(faction.ParentKingdom, rebelKingdom);

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            factionManager?.RemoveFaction(faction);

            ApplyPacifiedCooldowns(rebelKingdom.Clans, CivilWarPacifiedDays);
            ReleaseMercenariesFromKingdom(rebelKingdom);
            List<Clan> rebelClans = rebelKingdom.Clans.ToList();
            foreach (Clan rebelClan in rebelKingdom.Clans.ToList())
                faction.MoveClanToKingdomPreservingCivilWarInfluence(rebelClan, faction.ParentKingdom, preserveCustomBanner: true, showNotification: false);

            DrainAndDestroyRebelKingdom(rebelKingdom, faction.ParentKingdom, reunification: true);
            faction.RestoreCivilWarInfluenceSnapshots(rebelClans);
            PacifyIdeologies(faction.ParentKingdom, faction.ParentKingdom.RulingClan);
            ActivateCompatibilityRepairWindow();

            SuccessionChallengeBehavior.Instance?.CompleteWarOutcome(faction, faction.ParentKingdom);

            if (SuccessionChallengeBehavior.Instance?.GetWarRecord(faction) is SuccessionChallengeRecord pendingPeace && !pendingPeace.ResolutionReturned) return;
            ConflictOutcomeBehavior.Current?.Publish(resultNotice, demandSatisfied ? "satisfied" : cause != null ? "ended" : "peace",
                ConflictOutcomeBehavior.ContinuingWars(faction.ParentKingdom));
        }

        private static void ApplyIndependenceMemories(Clan formerRulingHouse, IEnumerable<Clan> departingClans)
        {
            if (formerRulingHouse == null || formerRulingHouse.IsEliminated
                || formerRulingHouse.Leader == null || !formerRulingHouse.Leader.IsAlive)
                return;

            // One house memory per departing noble house, not one for every pair of war participants.
            foreach (Clan clan in departingClans.Where(NobleClanEligibilityHelper.IsLiveNobleClan).Distinct())
            {
                if (clan == formerRulingHouse || clan.Leader == null || !clan.Leader.IsAlive)
                    continue;
                RelationMemoryService.ApplyChange(formerRulingHouse.Leader, clan.Leader,
                    -25, false, RelationMemorySources.WarOfIndependence, 15f, RelationMemoryScope.House);
            }
        }

        private void ResolveRecognizedIndependence(FactionObject faction, Kingdom rebelKingdom, bool parentDestroyed)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebelKingdom);
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(faction, RebellionSummaryOutcome.RecognizedIndependence);
            CompleteCivilWarTracker(faction, rebelKingdom, parentDestroyed
                ? "independence recognized after parent realm destruction"
                : "independence recognized");

            if (!parentDestroyed)
                ApplyCivilWarPeaceIfNeeded(faction.ParentKingdom, rebelKingdom);

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            factionManager?.RemoveFaction(faction);

            ReleaseMercenariesFromKingdom(rebelKingdom);
            List<Clan> independentClans = rebelKingdom.Clans.ToList();
            Clan formerRulingHouse = faction.ParentKingdom.RulingClan;
            Kingdom permanentKingdom = PromoteRebelKingdomToIndependentKingdom(faction, rebelKingdom, faction.Leader);
            ApplyIndependenceMemories(formerRulingHouse, independentClans);
            faction.RestoreCivilWarInfluenceSnapshots(independentClans);
            if (parentDestroyed)
                ApplyCivilWarVictoryInfluenceRewards(independentClans, faction.Leader, "recognized independence");
            ActivateCompatibilityRepairWindow();

            if (permanentKingdom == null || permanentKingdom.IsEliminated) return;
            ConflictOutcomeBehavior.Capture(resultNotice, "NEW_REALM", permanentKingdom.Name);
            ConflictOutcomeBehavior.Current?.Publish(resultNotice, "independence");
        }

        private Kingdom CreateRestoredSuccessorKingdom(Kingdom collapsedParent, Kingdom successorRebelKingdom, Clan successorLeader,
            bool runImmediateRepair = true, SuccessionChallengeRecord challenge = null)
        {
            if (collapsedParent == null || successorRebelKingdom == null || successorLeader == null) return null;

            string restoredStringIdBase = collapsedParent.StringId + "_restored"
                + (challenge == null ? "" : "_challenge_" + challenge.Id);
            string restoredStringId = restoredStringIdBase;
            int idSuffix = 0;
            while (challenge == null && Kingdom.All.Any(k => k.StringId == restoredStringId))
                restoredStringId = restoredStringIdBase + "_" + (++idSuffix);

            Kingdom restoredKingdom = challenge == null ? null : Kingdom.All.FirstOrDefault(k => k.StringId == restoredStringId);
            if (restoredKingdom?.IsEliminated == true) return null;
            restoredKingdom = restoredKingdom ?? KingdomCreationSafetyHelper.CreateKingdom(restoredStringId, successorLeader);
            if (restoredKingdom == null)
                return null;
            if (challenge != null) challenge.OutcomeRealm = restoredKingdom;

            Settlement capital = successorLeader.Settlements.FirstOrDefault()
                              ?? successorRebelKingdom.Settlements.FirstOrDefault()
                              ?? collapsedParent.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
            var restoredVisuals = KingdomVisualHelper.ResolveInheritedKingdomVisuals(collapsedParent, successorLeader, restoredKingdom.StringId);

            if (challenge == null || !challenge.RestoredRealmInitialized)
            {
                restoredKingdom.InitializeKingdom(
                    collapsedParent.Name,
                    collapsedParent.Name,
                    collapsedParent.Culture ?? successorRebelKingdom.Culture,
                    restoredVisuals.Banner,
                    restoredVisuals.PrimaryColor,
                    restoredVisuals.SecondaryColor,
                    capital,
                    new TextObject(""),
                    new TextObject(""),
                    new TextObject(""));
                if (challenge != null) challenge.RestoredRealmInitialized = true;
            }
            KingdomVisualHelper.ApplyKingdomPalette(restoredKingdom, restoredVisuals);
            EnsureValidRulingClan(restoredKingdom, successorLeader);
            ClearTemporaryKingdomRepair(restoredKingdom);

            if (collapsedParent.ActivePolicies.Any())
                RebelPolicyHelper.CopyPolicies(collapsedParent, restoredKingdom);
            else
                RebelPolicyHelper.CopyPolicies(successorRebelKingdom, restoredKingdom);

            ReleaseMercenariesFromKingdom(successorRebelKingdom);
            TransferClansToKingdom(successorRebelKingdom, restoredKingdom, successorLeader);
            PreservePromotedRebelForeignWars(successorRebelKingdom, restoredKingdom, collapsedParent);
            DrainAndDestroyRebelKingdom(successorRebelKingdom, restoredKingdom);
            EnsureValidRulingClan(restoredKingdom, successorLeader);
            Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.RegisterRestoredRealmMantle(collapsedParent, restoredKingdom, successorLeader, "parent collapse succession");
            ClearTemporaryKingdomRepair(restoredKingdom);
            if (runImmediateRepair)
                RepairCompatibilityStateNow($"created restored successor kingdom {restoredKingdom.StringId} from {collapsedParent.StringId}");
            if (challenge != null) challenge.RestoredRealmReady = true;
            return restoredKingdom;
        }

        private Kingdom CreateRestoredReplacementKingdom(Kingdom eliminatedParent, Clan preferredRuler)
        {
            if (eliminatedParent == null || preferredRuler == null)
                return null;

            string restoredStringIdBase = eliminatedParent.StringId + "_restored";
            string restoredStringId = restoredStringIdBase;
            int idSuffix = 0;
            while (Kingdom.All.Any(k => k.StringId == restoredStringId))
                restoredStringId = restoredStringIdBase + "_" + (++idSuffix);

            Kingdom restoredKingdom = KingdomCreationSafetyHelper.CreateKingdom(restoredStringId, preferredRuler);
            if (restoredKingdom == null)
                return null;
            Settlement capital = preferredRuler.Settlements.FirstOrDefault()
                              ?? eliminatedParent.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
            var restoredVisuals = KingdomVisualHelper.ResolveInheritedKingdomVisuals(eliminatedParent, preferredRuler, restoredKingdom.StringId);

            restoredKingdom.InitializeKingdom(
                eliminatedParent.Name,
                eliminatedParent.Name,
                eliminatedParent.Culture ?? preferredRuler.Culture,
                restoredVisuals.Banner,
                restoredVisuals.PrimaryColor,
                restoredVisuals.SecondaryColor,
                capital,
                new TextObject(""),
                new TextObject(""),
                new TextObject(""));
            KingdomVisualHelper.ApplyKingdomPalette(restoredKingdom, restoredVisuals);
            EnsureValidRulingClan(restoredKingdom, preferredRuler);
            ClearTemporaryKingdomRepair(restoredKingdom);
            RebelPolicyHelper.CopyPolicies(eliminatedParent, restoredKingdom);
            TransferLiveClansFromEliminatedKingdom(eliminatedParent, restoredKingdom, preferredRuler);
            EnsureValidRulingClan(restoredKingdom, preferredRuler);
            Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.RegisterRestoredRealmMantle(eliminatedParent, restoredKingdom, preferredRuler, "emergency restored replacement");
            ClearTemporaryKingdomRepair(restoredKingdom);
            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(restoredKingdom);
            RepairCompatibilityStateNow($"created emergency restored successor kingdom {restoredKingdom.StringId} from eliminated {eliminatedParent.StringId}");
            return restoredKingdom;
        }

        private void CollapseLandlessParentWithoutSuccessor(Kingdom collapsedParent)
        {
            if (collapsedParent == null || collapsedParent.IsEliminated) return;

            ReleaseMercenariesFromKingdom(collapsedParent);

            foreach (Clan clan in collapsedParent.Clans.Where(c => !c.IsUnderMercenaryService).ToList())
            {
                if (clan == Clan.PlayerClan)
                {
                    ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                    continue;
                }

                ExiledClanRecoveryBehavior exileRecovery = Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
                if (exileRecovery != null)
                    exileRecovery.ResolveClanExile(clan, collapsedParent, cause: ExileCause.KingdomDestroyed);
                else
                {
                    Kingdom refuge = RefugeSelectionHelper.FindBestRefuge(clan, collapsedParent);
                    if (refuge != null)
                        KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, refuge, showNotification: false);
                    else
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                }
            }

            ActivateCompatibilityRepairWindow();
            DestroyKingdomAction.Apply(collapsedParent);
            RepairCompatibilityStateNow($"collapsed landless parent kingdom {collapsedParent.StringId} without successor");
        }

        private static List<Kingdom> GetExternalEnemiesToInherit(Kingdom collapsedParent, IEnumerable<Kingdom> activeRebelKingdoms)
        {
            if (collapsedParent == null) return new List<Kingdom>();

            HashSet<Kingdom> internalCivilWarKingdoms = new HashSet<Kingdom>(activeRebelKingdoms.Where(k => k != null));
            return Kingdom.All
                .Where(k => k != null
                         && k != collapsedParent
                         && !k.IsEliminated
                         && !internalCivilWarKingdoms.Contains(k)
                         && collapsedParent.IsAtWarWith(k))
                .ToList();
        }

        private static void InheritExternalWars(Kingdom successorKingdom, IEnumerable<Kingdom> inheritedEnemies)
        {
            if (successorKingdom == null || successorKingdom.IsEliminated || inheritedEnemies == null) return;

            foreach (Kingdom enemy in inheritedEnemies.Where(k => k != null && !k.IsEliminated && k != successorKingdom))
            {
                if (successorKingdom.IsAtWarWith(enemy)) continue;
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(successorKingdom, enemy));
            }
        }

        private static void ApplyPacifiedCooldowns(IEnumerable<Clan> clans, int days)
        {
            FactionManagerBehavior manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;

            foreach (Clan clan in clans.Where(NobleClanEligibilityHelper.IsLiveNobleClan))
                manager.ApplyPacifiedCooldown(clan, days);
        }

        private static IEnumerable<Clan> GetLiveNobleClans(Kingdom kingdom)
        {
            if (kingdom == null)
                return Enumerable.Empty<Clan>();

            return Clan.All
                .Where(c => NobleClanEligibilityHelper.IsLiveNobleClan(c)
                         && c.Kingdom == kingdom);
        }

        private static bool HasLiveHoldings(Kingdom kingdom, IEnumerable<Clan> liveClans)
        {
            if (kingdom == null)
                return false;

            if (CountStrongholds(kingdom) > 0)
                return true;

            return liveClans != null && liveClans.Any(c => c.Settlements.Any(s => s != null && (s.IsTown || s.IsCastle)));
        }

        private static Clan SelectEmergencySuccessorRuler(Kingdom kingdom, IEnumerable<Clan> candidateClans)
        {
            if (kingdom == null)
                return null;

            bool IsCandidate(Clan clan) =>
                NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom);

            if (IsCandidate(kingdom.RulingClan))
                return kingdom.RulingClan;

            return (candidateClans ?? Enumerable.Empty<Clan>())
                .Where(IsCandidate)
                .Distinct()
                .OrderByDescending(c => (c.Settlements.Count(s => s != null && (s.IsTown || s.IsCastle)) * 1000f)
                                      + RebellionPowerHelper.CalculateClanPower(c))
                .FirstOrDefault();
        }

        private static Kingdom FindExistingRestoredSuccessor(Kingdom eliminatedParent)
        {
            if (eliminatedParent == null || string.IsNullOrEmpty(eliminatedParent.StringId))
                return null;

            string restoredPrefix = eliminatedParent.StringId + "_restored";
            string independentPrefix = eliminatedParent.StringId + "_indep_";
            return Kingdom.All
                .Where(k => k != null
                         && !k.IsEliminated
                         && k != eliminatedParent
                         && (k.StringId == restoredPrefix
                             || k.StringId.StartsWith(restoredPrefix + "_")
                             || k.StringId.StartsWith(independentPrefix)))
                .OrderByDescending(k => CountStrongholds(k))
                .ThenByDescending(k => k.Clans.Count(c => c != null && !c.IsEliminated))
                .FirstOrDefault();
        }

        private static Kingdom FindLiveTemporarySuccessorForEliminatedParent(Kingdom eliminatedParent)
        {
            if (eliminatedParent == null || string.IsNullOrEmpty(eliminatedParent.StringId))
                return null;

            string rebelPrefix = eliminatedParent.StringId + "_rebels_";
            return Kingdom.All
                .Where(k => k != null
                         && !k.IsEliminated
                         && k != eliminatedParent
                         && k.StringId.StartsWith(rebelPrefix)
                         && GetLiveNobleClans(k).Any())
                .OrderByDescending(k => CountStrongholds(k))
                .ThenByDescending(k => k.Clans.Count(c => c != null && !c.IsEliminated))
                .FirstOrDefault();
        }

        private static bool IsBellumCivileTemporaryKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !string.IsNullOrEmpty(kingdom.StringId)
                && kingdom.StringId.Contains("_rebels_");
        }

        private static bool TransferLiveClansFromEliminatedKingdom(Kingdom eliminatedKingdom, Kingdom targetKingdom, Clan preferredRuler = null)
        {
            if (eliminatedKingdom == null || targetKingdom == null || targetKingdom.IsEliminated)
                return false;

            return RunWithRulerRepairSuppressed(eliminatedKingdom, () =>
            {
                List<Clan> liveClans = GetLiveNobleClans(eliminatedKingdom).ToList();
                if (liveClans.Count == 0)
                    return false;

                if (preferredRuler != null && liveClans.Remove(preferredRuler))
                {
                    KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(preferredRuler, targetKingdom, showNotification: false);
                    EnsureValidRulingClan(targetKingdom, preferredRuler);
                }

                foreach (Clan clan in liveClans)
                    KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, targetKingdom, showNotification: false);

                EnsureValidRulingClan(targetKingdom, preferredRuler);
                BellumCivileLogger.Log($"Moved live clans from eliminated kingdom {eliminatedKingdom.StringId} into {targetKingdom.StringId}.");
                return true;
            });
        }

        private static Kingdom ResolveFallbackCleanupKingdom(Kingdom rebelKingdom, Kingdom preferredFallback)
        {
            if (preferredFallback != null && !preferredFallback.IsEliminated && preferredFallback != rebelKingdom)
                return preferredFallback;

            return rebelKingdom?.Clans
                .Where(c => c != null
                         && !c.IsEliminated
                         && !c.IsUnderMercenaryService
                         && c.Kingdom != null
                         && c.Kingdom != rebelKingdom
                         && !c.Kingdom.IsEliminated)
                .GroupBy(c => c.Kingdom)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        private static void EnsureValidRulingClan(Kingdom kingdom, Clan preferredClan = null, bool forcePreferred = false)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return;

            bool IsValidClan(Clan clan) =>
                NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom);

            if (forcePreferred && IsValidClan(preferredClan))
            {
                kingdom.RulingClan = preferredClan;
                return;
            }

            if (IsValidClan(kingdom.RulingClan))
                return;

            if (IsValidClan(preferredClan))
            {
                kingdom.RulingClan = preferredClan;
                return;
            }

            Clan replacementClan = kingdom.Clans
                .Where(c => IsValidClan(c))
                .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                .FirstOrDefault();

            if (replacementClan != null)
                kingdom.RulingClan = replacementClan;
        }

        private bool DrainAndDestroyRebelKingdom(Kingdom rebelKingdom, Kingdom fallbackKingdom, bool allowDeferred = true, bool reunification = false)
        {
            if (CivilWarConflictBehavior.IsRealmTransferPending(rebelKingdom)
                || CivilWarConflictBehavior.IsRealmTransferPending(fallbackKingdom)) return false;
            if (rebelKingdom == null) return true;
            if (reunification) CloseReunifiedRebelForeignWars(rebelKingdom, fallbackKingdom);

            return RunWithRulerRepairSuppressed(rebelKingdom, () =>
            {
                if (rebelKingdom.IsEliminated)
                {
                    ClearTemporaryKingdomRepair(rebelKingdom);
                    return true;
                }

                List<Clan> stragglers = rebelKingdom.Clans.ToList();
                bool hasNonMercenaryStragglers = stragglers.Any(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c.Kingdom == rebelKingdom);
                Kingdom resolvedFallback = ResolveFallbackCleanupKingdom(rebelKingdom, fallbackKingdom);

                if (hasNonMercenaryStragglers && resolvedFallback == null)
                {
                    EnsureValidRulingClan(rebelKingdom);
                    ClearTemporaryKingdomRepair(rebelKingdom);

                    if (allowDeferred)
                    {
                        QueuePendingRebelCleanup(rebelKingdom, fallbackKingdom);
                        ActivateCompatibilityRepairWindow();
                        BellumCivileLogger.Log($"Delaying rebel kingdom drain for {rebelKingdom.StringId} until a safe fallback kingdom is available.");
                    }

                    return false;
                }

                foreach (Clan straggler in stragglers)
                {
                    if (straggler == null || straggler.IsEliminated || straggler.Kingdom != rebelKingdom) continue;

                    if (straggler.IsUnderMercenaryService)
                        ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(straggler, showNotification: false);
                    else
                        KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(straggler, resolvedFallback, showNotification: false);
                }

                if (rebelKingdom.Clans.Any(c => c != null && !c.IsEliminated))
                {
                    EnsureValidRulingClan(rebelKingdom);
                    ClearTemporaryKingdomRepair(rebelKingdom);

                    if (allowDeferred)
                    {
                        QueuePendingRebelCleanup(rebelKingdom, resolvedFallback);
                        ActivateCompatibilityRepairWindow();
                        BellumCivileLogger.Log($"Rebel kingdom {rebelKingdom.StringId} still has lingering clans after cleanup and will be retried on a later tick.");
                    }

                    return false;
                }

                ClearTemporaryKingdomRepair(rebelKingdom);
                DestroyKingdomAction.Apply(rebelKingdom);
                return true;
            });
        }

        private void PacifyIdeologies(Kingdom kingdom, Clan victoriousClan)
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;

            FactionObject victoriousIdeology = victoriousClan != null ? manager.GetIdeologicalFaction(victoriousClan) : null;

            foreach (FactionObject ideology in manager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology))
            {
                if (ideology.Mood < 0f) ideology.Mood = TaleWorlds.Library.MathF.Min(0f, ideology.Mood + 50f);

                if (victoriousIdeology != null && ideology == victoriousIdeology)
                {
                    ideology.Mood = TaleWorlds.Library.MathF.Min(100f, ideology.Mood + 20f);
                }
            }
        }

        private static bool IsValidRewardCandidate(Clan clan, Hero victoriousRuler, Kingdom winningKingdom, HashSet<Clan> excludedClans)
        {
            if (clan == null) return false;
            if (excludedClans != null && excludedClans.Contains(clan)) return false;
            if (!NobleClanEligibilityHelper.IsLiveNobleClan(clan)) return false;
            if (clan.Leader == null || clan.Leader.IsDead) return false;
            if (clan.Leader.GetRelation(victoriousRuler) < 0) return false;
            if (clan == winningKingdom.RulingClan && clan.Fiefs.Count > 0) return false;
            return true;
        }

        private static float ScoreRewardCandidate(Clan clan, Hero victoriousRuler, int generosityLevel, int calculatingLevel)
        {
            float score = clan.Leader.GetRelation(victoriousRuler) * C.PostWarRewardRelWeight;

            if (clan.Fiefs.Count == 0)
                score += C.PostWarRewardLandlessBonus;

            float genScale = 1.0f;
            if (generosityLevel >= 2)       genScale += C.PostWarRewardTraitScaleStep2;
            else if (generosityLevel == 1)  genScale += C.PostWarRewardTraitScaleStep1;
            else if (generosityLevel == -1) genScale -= C.PostWarRewardTraitScaleStep1;
            else if (generosityLevel <= -2) genScale -= C.PostWarRewardTraitScaleStep2;

            int fiefsNeeded = FactionObject.CalculateDesiredFiefs(clan) - clan.Fiefs.Count;
            if (fiefsNeeded > 0)
                score += fiefsNeeded * C.PostWarRewardNeedBonusPerFief * genScale;

            float calcScale = 1.0f;
            if (calculatingLevel >= 2)       calcScale += C.PostWarRewardTraitScaleStep2;
            else if (calculatingLevel == 1)  calcScale += C.PostWarRewardTraitScaleStep1;
            else if (calculatingLevel == -1) calcScale -= C.PostWarRewardTraitScaleStep1;
            else if (calculatingLevel <= -2) calcScale -= C.PostWarRewardTraitScaleStep2;

            float strength = RebellionPowerHelper.GetClanMilitaryStrength(clan);
            score += strength / 1000f * C.PostWarRewardPowerPer1k * calcScale;

            score += clan.Tier * C.PostWarRewardTierWeight;

            return score;
        }
    }
}
