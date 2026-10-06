using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Election;
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
    /// To insert a deliberation window between the king announcing an expulsion and the vote actually
    /// firing. During this window, available to the player as king or vassal, the player can talk
    /// to clan leaders to learn their stance on the accusation (displayed as one of 5 tiers from
    /// "Strongly Support Expulsion" to "Strongly Oppose") and, if desired, open a barter screen to
    /// bribe them into voting the player's preferred way. Multiple concurrent expulsions appear as
    /// separate slots in the dialogue sub-menu (one per accused lord). Court agendas supply
    /// the vote date in every realm; this behavior executes the queue and owns persuasion.
    /// </summary>
    public class ExpulsionDeliberationBehavior : CampaignBehaviorBase
    {
        public static ExpulsionDeliberationBehavior Current =>
            Campaign.Current?.GetCampaignBehavior<ExpulsionDeliberationBehavior>();

        private Dictionary<string, string>       _pendingExpelProposer = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _pendingExpelDate     = new Dictionary<string, CampaignTime>();
        private Dictionary<string, float>        _pendingExpelCreatedDay = new Dictionary<string, float>();
        private Dictionary<string, int>          _pendingExpelRetryCount = new Dictionary<string, int>();
        private Dictionary<string, int>          _expelStaleRepairCount = new Dictionary<string, int>();

        private Dictionary<string, int> _expelBribedOverrides = new Dictionary<string, int>();
        private Dictionary<string, bool> _expelPersuasionFailed = new Dictionary<string, bool>();
        private readonly Dictionary<string, PersuasionOptionArgs> _expelPersuasionOptions = new Dictionary<string, PersuasionOptionArgs>();

        private float  _queriedScore;
        private bool   _swayToExpel;
        private bool   _currentQueryAlreadyBribed;
        private Clan   _currentQueryTargetClan;
        private string _currentQueryPendingKey;
        private List<string> _conversationPendingKeys = new List<string>();
        private int _conversationPendingPage;

        private Clan  _treasonInductTarget;
        private int   _treasonTargetFactionType;

        // -------------------- Key helpers --------------------

        private static string PendingKey(Kingdom kingdom, Clan target) =>
            kingdom.StringId + "|" + target.StringId;

        private static string KingdomPrefix(Kingdom kingdom) =>
            kingdom.StringId + "|";

        public static string BribeKey(Kingdom kingdom, Clan target, Clan voter) =>
            kingdom.StringId + "|" + target.StringId + "|" + voter.StringId;

        // -------------------- CampaignBehaviorBase --------------------

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            VotePledgeService.Invalidate();
            dataStore.SyncData("BellumCivile_PendingExpelProposer", ref _pendingExpelProposer);
            dataStore.SyncData("BellumCivile_PendingExpelDate",     ref _pendingExpelDate);
            dataStore.SyncData("BellumCivile_PendingExpelCreatedDay", ref _pendingExpelCreatedDay);
            dataStore.SyncData("BellumCivile_PendingExpelRetryCount", ref _pendingExpelRetryCount);
            dataStore.SyncData("BellumCivile_ExpelStaleRepairCount", ref _expelStaleRepairCount);
            dataStore.SyncData("BellumCivile_ExpelBribedOverrides", ref _expelBribedOverrides);
            dataStore.SyncData("BellumCivile_ExpelPersuasionFailed", ref _expelPersuasionFailed);

            if (_pendingExpelProposer == null) _pendingExpelProposer = new Dictionary<string, string>();
            if (_pendingExpelDate     == null) _pendingExpelDate     = new Dictionary<string, CampaignTime>();
            if (_pendingExpelCreatedDay == null) _pendingExpelCreatedDay = new Dictionary<string, float>();
            if (_pendingExpelRetryCount == null) _pendingExpelRetryCount = new Dictionary<string, int>();
            if (_expelStaleRepairCount == null) _expelStaleRepairCount = new Dictionary<string, int>();
            if (_expelBribedOverrides == null) _expelBribedOverrides = new Dictionary<string, int>();
            if (_expelPersuasionFailed == null) _expelPersuasionFailed = new Dictionary<string, bool>();
        }

        // -------------------- Public API --------------------

        // What does this method do?
        // Queues an expulsion vote for a 5-day deliberation window. Returns false if this exact
        // target is already pending (same-target duplicate guard; silently skip). Multiple DIFFERENT
        // targets can be queued simultaneously.
        internal bool QueueExpulsionVote(Kingdom kingdom, Clan targetClan, Clan proposerClan, CampaignTime voteDate)
        {
            string key = PendingKey(kingdom, targetClan);
            if (_pendingExpelDate.ContainsKey(key))
            {
                BellumCivileLogger.Log($"Expulsion deliberation duplicate ignored for {key}.");
                return false;
            }

            _pendingExpelProposer[key] = proposerClan?.StringId ?? "";
            _pendingExpelDate[key]     = voteDate;
            InitializePendingLifecycle(key, resetStaleRepair: true);
            BellumCivileLogger.Log($"Queued expulsion deliberation {key}; proposer={proposerClan?.StringId ?? "null"}; fires={_pendingExpelDate[key].ToDays:0.00}.");

            Hero ruler = kingdom.RulingClan?.Leader;
            TextObject msg = new TextObject("{=BC_Expul_Announcement}By royal decree, {RULER_NAME} has summoned the lords to pass judgment on the loyalty of {CLAN_LEADER} of {CLAN_NAME}. The court will deliberate and call for a vote within {DAYS} days.");
            msg.SetTextVariable("RULER_NAME",   ruler?.Name ?? kingdom.Name);
            msg.SetTextVariable("CLAN_LEADER",  targetClan.Leader?.Name ?? targetClan.Name);
            msg.SetTextVariable("CLAN_NAME",    targetClan.Name);
            msg.SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(_pendingExpelDate[key].ToDays - CampaignTime.Now.ToDays)));
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Politics, primaryKingdom: kingdom, primaryClan: proposerClan, secondaryClan: targetClan);

            return true;
        }

        public bool HasPendingExpulsion(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            string prefix = KingdomPrefix(kingdom);
            return _pendingExpelDate.Keys.Any(key => key.StartsWith(prefix));
        }

        public bool HasPendingExpulsionForTarget(Kingdom kingdom, Clan targetClan)
        {
            if (kingdom == null || targetClan == null) return false;
            return _pendingExpelDate.ContainsKey(PendingKey(kingdom, targetClan));
        }

        public IEnumerable<Clan> GetPendingTargets(Kingdom kingdom)
        {
            if (kingdom == null) yield break;
            string prefix = KingdomPrefix(kingdom);
            foreach (var kv in _pendingExpelDate.OrderBy(kv => kv.Value))
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                string targetId = kv.Key.Substring(prefix.Length);
                Clan target = Clan.All.FirstOrDefault(c => c.StringId == targetId);
                if (target != null) yield return target;
            }
        }

        public int? GetBribedVote(Kingdom kingdom, Clan targetClan, Clan voterClan)
        {
            if (kingdom == null || targetClan == null || voterClan == null) return null;
            if (_expelBribedOverrides.TryGetValue(BribeKey(kingdom, targetClan, voterClan), out int score)) return score;
            return null;
        }

        public void SetBribedVote(Kingdom kingdom, Clan targetClan, Clan voterClan, int score)
        {
            if (kingdom == null || targetClan == null || voterClan == null) return;
            _expelBribedOverrides[BribeKey(kingdom, targetClan, voterClan)] = score;
            VotePledgeService.Invalidate();
        }

        internal IEnumerable<string> GetVotePledgeKeys(Clan voter) => VotePledgeService.KeysFor(voter, "expulsion",
            _expelBribedOverrides.Where(entry => entry.Value != 0).Select(entry => entry.Key));

        public void ClearBribedVotesForTarget(Kingdom kingdom, Clan targetClan)
        {
            VotePledgeService.Invalidate();
            if (kingdom == null || targetClan == null) return;
            string prefix = kingdom.StringId + "|" + targetClan.StringId + "|";
            var toRemove = _expelBribedOverrides.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in toRemove) _expelBribedOverrides.Remove(k);
            var persuasionToRemove = _expelPersuasionFailed.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in persuasionToRemove) _expelPersuasionFailed.Remove(k);
        }

        private void ClearBribedVotesForPendingKey(string pendingKey)
        {
            VotePledgeService.Invalidate();
            if (string.IsNullOrEmpty(pendingKey)) return;
            string prefix = pendingKey + "|";
            var toRemove = _expelBribedOverrides.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in toRemove) _expelBribedOverrides.Remove(k);
            var persuasionToRemove = _expelPersuasionFailed.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in persuasionToRemove) _expelPersuasionFailed.Remove(k);
        }

        private void RemovePendingKey(string pendingKey)
        {
            _pendingExpelProposer.Remove(pendingKey);
            _pendingExpelDate.Remove(pendingKey);
            _pendingExpelCreatedDay.Remove(pendingKey);
            _pendingExpelRetryCount.Remove(pendingKey);
        }

        internal void CancelAgendaVote(Kingdom realm, Clan target)
        {
            if (realm == null || target == null) return;
            string key = PendingKey(realm, target);
            ClearBribedVotesForPendingKey(key);
            RemovePendingKey(key);
        }

        private void InitializePendingLifecycle(string pendingKey, bool resetStaleRepair)
        {
            _pendingExpelCreatedDay[pendingKey] = DelayedVoteReliability.CurrentDay;
            _pendingExpelRetryCount[pendingKey] = 0;
            if (resetStaleRepair)
                _expelStaleRepairCount[pendingKey] = 0;
        }

        private bool QueueRecoveredExpulsionVote(Kingdom kingdom, Clan target, Clan proposer)
        {
            if (kingdom == null || target == null || proposer == null)
                return false;

            string key = PendingKey(kingdom, target);
            if (_pendingExpelDate.ContainsKey(key))
                return false;

            CampaignTime? voteDate = CourtAgendaBehavior.Current?.ExecutiveVoteDate(kingdom, target.StringId, proposer);
            if (!voteDate.HasValue) return false;
            _pendingExpelProposer[key] = proposer.StringId;
            _pendingExpelDate[key] = voteDate.Value;
            InitializePendingLifecycle(key, resetStaleRepair: false);
            return true;
        }


        private void OnDailyTick()
        {
            CleanupOrphanedExpulsionDecisions(Clan.PlayerClan?.Kingdom);

            foreach (string pendingKey in _pendingExpelDate.Keys.ToList())
            {
                int sep = pendingKey.IndexOf('|');
                if (sep < 0)
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                    continue;
                }

                string kingdomId = pendingKey.Substring(0, sep);
                string targetId  = pendingKey.Substring(sep + 1);

                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                Clan target = Clan.All.FirstOrDefault(c => c.StringId == targetId);
                bool hasProposer = _pendingExpelProposer.TryGetValue(pendingKey, out string proposerClanId);
                Clan proposer = ResolveProposerClan(proposerClanId, kingdom);

                CleanupOrphanedExpulsionDecisions(kingdom);

                List<string> invalidReasons = new List<string>();
                if (kingdom == null) invalidReasons.Add("kingdom_missing");
                else if (kingdom.IsEliminated) invalidReasons.Add("kingdom_eliminated");
                if (target == null) invalidReasons.Add("target_missing");
                else
                {
                    if (target.IsEliminated) invalidReasons.Add("target_eliminated");
                    if (target.Kingdom != kingdom) invalidReasons.Add("target_wrong_kingdom");
                    if (kingdom != null && target == kingdom.RulingClan) invalidReasons.Add("target_is_ruling_clan");
                    if (target != Clan.PlayerClan && target.IsMinorFaction) invalidReasons.Add("target_minor_faction");
                    if (target.IsUnderMercenaryService) invalidReasons.Add("target_mercenary");
                    if (target.Leader == null) invalidReasons.Add("target_leader_missing");
                    else if (target.Leader.IsDead) invalidReasons.Add("target_leader_dead");
                }
                if (!hasProposer) invalidReasons.Add("proposer_flag_missing");
                if (!IsValidProposer(proposer, kingdom)) invalidReasons.Add(GetInvalidProposerReason(proposer, kingdom));

                bool invalid = invalidReasons.Count > 0;

                if (invalid)
                {
                    BellumCivileLogger.Log($"Removing invalid delayed expulsion vote {pendingKey}; kingdom={kingdom?.StringId ?? "null"} proposer={proposer?.StringId ?? proposerClanId ?? "null"} target={target?.StringId ?? "null"} reasons={string.Join(",", invalidReasons)}.");
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                    continue;
                }

                DelayedVoteReliability.EnsureLifecycle(
                    pendingKey,
                    _pendingExpelDate[pendingKey],
                    _pendingExpelCreatedDay,
                    _pendingExpelRetryCount);

                if (!_pendingExpelDate[pendingKey].IsPast) continue;

                if (kingdom.UnresolvedDecisions.OfType<ExpelClanFromKingdomDecision>().Any())
                {
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} is waiting for an existing unresolved expulsion decision to clear.");
                    continue;
                }

                ExpelClanFromKingdomDecision decision = new ExpelClanFromKingdomDecision(proposer, target);
                if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingExpelCreatedDay, _pendingExpelRetryCount))
                {
                    FinalizeExpiredExpulsionVote(pendingKey, kingdom, target, decision, "pending_age_or_retry_limit");
                    continue;
                }

                bool isAllowed = false;
                try
                {
                    isAllowed = decision.IsAllowed();
                }
                catch (System.Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} threw while checking IsAllowed(); error={ex.GetType().Name}:{ex.Message}.");
                    RegisterExpulsionFailure(pendingKey, kingdom, target, decision, "is_allowed_exception");
                    continue;
                }

                if (!isAllowed)
                {
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} is not currently allowed by Bannerlord; blockers={DescribeCurrentDecisionBlockers(kingdom)} rulingClan={kingdom?.RulingClan?.StringId ?? "null"} proposer={proposer?.StringId ?? "null"} target={target?.StringId ?? "null"} targetFiefs={target?.Fiefs.Count ?? 0}.");
                    RegisterExpulsionFailure(pendingKey, kingdom, target, decision, "not_allowed");
                    continue;
                }

                BellumCivileLogger.Log($"Attempting to fire delayed expulsion vote {pendingKey}.");
                try
                {
                    IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
                }
                catch (System.Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} threw while entering unresolved decisions; error={ex.GetType().Name}:{ex.Message}.");
                    RegisterExpulsionFailure(pendingKey, kingdom, target, decision, "add_decision_exception");
                    continue;
                }

                if (WasExpulsionDecisionQueued(kingdom, target, proposer))
                {
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} successfully entered unresolved decisions.");
                    RemovePendingKey(pendingKey);
                }
                else if (WasExpulsionDecisionResolvedImmediately(kingdom))
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} resolved immediately by Bannerlord. Removing pending retry. {DescribeImmediateExpulsionResolution(kingdom, target, proposer)}");
                    RemovePendingKey(pendingKey);
                }
                else
                {
                    BellumCivileLogger.Log($"Failed to queue delayed expulsion vote {pendingKey}; keeping it pending for retry. blockers={DescribeCurrentDecisionBlockers(kingdom)} rulingClan={kingdom?.RulingClan?.StringId ?? "null"} proposer={proposer?.StringId ?? "null"} target={target?.StringId ?? "null"} targetFiefs={target?.Fiefs.Count ?? 0}.");
                    RegisterExpulsionFailure(pendingKey, kingdom, target, decision, "add_decision_failed");
                }
            }
        }

        private void RegisterExpulsionFailure(
            string pendingKey,
            Kingdom kingdom,
            Clan target,
            ExpelClanFromKingdomDecision decision,
            string reason)
        {
            int attempts = DelayedVoteReliability.RegisterFailure(pendingKey, _pendingExpelRetryCount);
            if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingExpelCreatedDay, _pendingExpelRetryCount))
                FinalizeExpiredExpulsionVote(pendingKey, kingdom, target, decision, $"{reason};attempts={attempts}");
        }

        private void FinalizeExpiredExpulsionVote(
            string pendingKey,
            Kingdom kingdom,
            Clan target,
            ExpelClanFromKingdomDecision decision,
            string reason)
        {
            BellumCivileLogger.Log($"Delayed expulsion vote {pendingKey} exceeded reliability limits; reason={reason}. Attempting one final direct decision.");
            try
            {
                IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Final recovery attempt for expulsion vote {pendingKey} threw; error={ex.GetType().Name}:{ex.Message}.");
            }

            bool recovered = WasExpulsionDecisionQueued(kingdom, target, null)
                || target == null
                || target.Kingdom != kingdom;
            if (!recovered) CourtAgendaBehavior.Current?.CancelExecutiveMotion(kingdom, target?.StringId, decision.ProposerClan, reason);
            BellumCivileLogger.Log(recovered
                ? $"Final recovery attempt restored expulsion vote {pendingKey}."
                : $"Final recovery attempt could not restore expulsion vote {pendingKey}; stale pending state was cleared.");
            ClearBribedVotesForPendingKey(pendingKey);
            RemovePendingKey(pendingKey);
        }

        private void CleanupOrphanedExpulsionDecisions(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return;

            if (!HasPotentiallyRepairableExpulsionDecisions(kingdom))
                return;

            List<KingdomDecision> decisionsToRemove = new List<KingdomDecision>();

            List<ExpelClanFromKingdomDecision> expulsionDecisions = kingdom.UnresolvedDecisions
                .OfType<ExpelClanFromKingdomDecision>()
                .ToList();

            List<ExpelClanFromKingdomDecision> staleExpulsionDecisions = expulsionDecisions
                .Where(DelayedVoteReliability.IsLiveDecisionStale)
                .ToList();

            decisionsToRemove.AddRange(expulsionDecisions
                .Where(decision => IsOrphanedExpulsionDecision(kingdom, decision)));
            decisionsToRemove.AddRange(staleExpulsionDecisions);

            foreach (var duplicateGroup in expulsionDecisions
                .Where(decision => decision?.ClanToExpel != null)
                .GroupBy(decision => decision.ClanToExpel.StringId)
                .Where(group => group.Count() > 1))
            {
                ExpelClanFromKingdomDecision newest = duplicateGroup
                    .OrderByDescending(decision => decision.TriggerTime.ToDays)
                    .First();

                decisionsToRemove.AddRange(duplicateGroup.Where(decision => decision != newest));
            }

            foreach (KingdomDecision decision in decisionsToRemove.Distinct().ToList())
            {
                kingdom.RemoveDecision(decision);
                BellumCivileLogger.Log($"Removed orphaned, duplicate, or stale expulsion decision from kingdom {kingdom.StringId}; age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} days.");
            }

            foreach (ExpelClanFromKingdomDecision staleDecision in staleExpulsionDecisions)
            {
                Clan target = staleDecision?.ClanToExpel;
                Clan proposer = ResolveProposerClan(staleDecision?.ProposerClan?.StringId, kingdom);
                if (target == null
                    || target.IsEliminated
                    || target.Kingdom != kingdom
                    || !IsValidProposer(proposer, kingdom))
                {
                    continue;
                }

                string key = PendingKey(kingdom, target);
                if (_pendingExpelDate.ContainsKey(key))
                    continue;

                int repairCount = _expelStaleRepairCount.TryGetValue(key, out int stored) ? stored : 0;
                if (repairCount >= 1)
                {
                    BellumCivileLogger.Log($"Stale live expulsion decision {key} already exhausted its one rebuild allowance; motion withdrawn.");
                    continue;
                }

                _expelStaleRepairCount[key] = repairCount + 1;
                if (QueueRecoveredExpulsionVote(kingdom, target, proposer))
                    BellumCivileLogger.Log($"Rebuilt stale live expulsion decision {key} as a fresh delayed vote.");
            }
        }

        private static bool HasPotentiallyRepairableExpulsionDecisions(Kingdom kingdom)
        {
            return kingdom != null
                && kingdom.UnresolvedDecisions.OfType<ExpelClanFromKingdomDecision>().Any();
        }

        private static bool IsOrphanedExpulsionDecision(Kingdom expectedKingdom, ExpelClanFromKingdomDecision decision)
        {
            if (expectedKingdom == null || decision == null)
                return true;

            Kingdom decisionKingdom = decision.Kingdom;
            if (decisionKingdom == null || decisionKingdom != expectedKingdom || decisionKingdom.IsEliminated)
                return true;

            Clan targetClan = decision.ClanToExpel;
            if (targetClan == null || targetClan.IsEliminated || targetClan.Kingdom != decisionKingdom)
                return true;

            Clan proposerClan = decision.ProposerClan;
            if (proposerClan != null && !IsValidProposer(proposerClan, decisionKingdom))
                return true;

            return false;
        }

        private static string DescribeCurrentDecisionBlockers(Kingdom kingdom)
        {
            if (kingdom == null)
                return "kingdom_missing";

            if (kingdom.UnresolvedDecisions == null || !kingdom.UnresolvedDecisions.Any())
                return "none";

            return string.Join(",",
                kingdom.UnresolvedDecisions
                    .Select(decision =>
                    {
                        string decisionName = decision?.GetType().Name ?? "null_decision";
                        string proposerId = decision?.ProposerClan?.StringId ?? "null";
                        return decisionName + ":" + proposerId;
                    })
                    .Distinct()
                    .ToList());
        }

        private static bool WasExpulsionDecisionQueued(Kingdom kingdom, Clan target, Clan proposer)
        {
            if (kingdom == null || target == null)
                return false;

            return kingdom.UnresolvedDecisions
                .OfType<ExpelClanFromKingdomDecision>()
                .Any(decision =>
                    decision?.ClanToExpel == target &&
                    (proposer == null || decision.ProposerClan == proposer));
        }

        private static bool WasExpulsionDecisionResolvedImmediately(Kingdom kingdom)
        {
            return kingdom != null && Clan.PlayerClan != null && kingdom != Clan.PlayerClan.Kingdom;
        }

        private static string DescribeImmediateExpulsionResolution(Kingdom kingdom, Clan target, Clan proposer)
        {
            bool isNpcKingdom = kingdom != null && Clan.PlayerClan != null && kingdom != Clan.PlayerClan.Kingdom;
            string reason = isNpcKingdom
                ? "reason=non_player_kingdom_elections_resolve_synchronously"
                : "reason=decision_missing_after_add";

            string targetState;
            if (target == null)
                targetState = "target_state=missing";
            else if (target.IsEliminated)
                targetState = "target_state=eliminated";
            else if (target.Kingdom == kingdom)
                targetState = "target_state=remained_in_kingdom";
            else
                targetState = $"target_state=left_kingdom new_kingdom={target.Kingdom?.StringId ?? "none"}";

            return $"{reason}; {targetState}; kingdom={kingdom?.StringId ?? "null"} proposer={proposer?.StringId ?? "null"} target={target?.StringId ?? "null"} target_fiefs={target?.Fiefs.Count ?? 0} blockers_after={DescribeCurrentDecisionBlockers(kingdom)}.";
        }

        public int RunReliabilityRepair(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return 0;

            int before = kingdom.UnresolvedDecisions.Count;
            CleanupOrphanedExpulsionDecisions(kingdom);
            int changes = Math.Max(0, before - kingdom.UnresolvedDecisions.Count);

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingExpelDate.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingExpelDate[key],
                    _pendingExpelCreatedDay,
                    _pendingExpelRetryCount);
            }

            return changes;
        }

        public IEnumerable<string> GetReliabilityDiagnostics(Kingdom kingdom)
        {
            if (kingdom == null)
                yield break;

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingExpelDate.Keys.Where(k => k.StartsWith(prefix)).OrderBy(k => k))
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingExpelDate[key],
                    _pendingExpelCreatedDay,
                    _pendingExpelRetryCount);
                bool dialogueReady = TryResolvePendingExpulsionForConversation(kingdom, key, out _);
                yield return $"expulsion pending={key} due={_pendingExpelDate[key].ToDays:0.0} age={DelayedVoteReliability.GetPendingAgeDays(key, _pendingExpelCreatedDay):0.0} retries={DelayedVoteReliability.GetFailureCount(key, _pendingExpelRetryCount)} dialogue_ready={dialogueReady}";
            }

            foreach (ExpelClanFromKingdomDecision decision in kingdom.UnresolvedDecisions.OfType<ExpelClanFromKingdomDecision>())
            {
                yield return $"expulsion live={decision.ClanToExpel?.StringId ?? "null"} age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} stale={DelayedVoteReliability.IsLiveDecisionStale(decision)}";
            }
        }

        private static Clan ResolveProposerClan(string proposerClanId, Kingdom kingdom)
        {
            if (!string.IsNullOrEmpty(proposerClanId) && Clan.PlayerClan != null && Clan.PlayerClan.StringId == proposerClanId)
                return Clan.PlayerClan;

            Clan proposer = !string.IsNullOrEmpty(proposerClanId)
                ? Clan.All.FirstOrDefault(c => c.StringId == proposerClanId)
                : null;

            if (proposer == null && kingdom?.RulingClan != null && Clan.PlayerClan != null && kingdom == Clan.PlayerClan.Kingdom)
                return kingdom.RulingClan;

            return proposer;
        }

        private static bool IsValidProposer(Clan proposer, Kingdom kingdom)
        {
            if (proposer == null || kingdom == null)
                return false;

            if (proposer.IsEliminated)
                return false;

            if (proposer.Kingdom != kingdom)
                return false;

            if (proposer == Clan.PlayerClan)
                return !proposer.IsUnderMercenaryService;

            return !proposer.IsMinorFaction && !proposer.IsUnderMercenaryService;
        }

        private static string GetInvalidProposerReason(Clan proposer, Kingdom kingdom)
        {
            if (proposer == null) return "proposer_missing";
            if (proposer.IsEliminated) return "proposer_eliminated";
            if (proposer.Kingdom != kingdom) return "proposer_wrong_kingdom";
            if (proposer == Clan.PlayerClan && proposer.IsUnderMercenaryService) return "player_proposer_mercenary";
            if (proposer.IsMinorFaction) return "proposer_minor_faction";
            if (proposer.IsUnderMercenaryService) return "proposer_mercenary";
            return "proposer_invalid";
        }


        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RunReliabilityRepair(Clan.PlayerClan?.Kingdom);

            starter.AddPlayerLine(
                "expulsion_deliberation_topic",
                "lord_talk_speak_diplomacy_2",
                "expulsion_deliberation_topic_response",
                "{=BC_Expul_Topic}About the judgments pending before the court...",
                ExpulsionMenuEntryCondition,
                BeginExpulsionPendingSelection,
                100, null, null);

            starter.AddDialogLine(
                "expulsion_deliberation_choose_accusation",
                "expulsion_deliberation_topic_response",
                "expulsion_deliberation_topic_choices",
                "{=BC_Expul_ChooseAccusation}Which judgment did you wish to discuss?",
                null,
                null,
                100, null);

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                starter.AddPlayerLine(
                    $"expulsion_query_slot_{slot}",
                    "expulsion_deliberation_topic_choices",
                    "expulsion_query_response",
                    SlotQueryText(slot),
                    () => ExpulsionMenuSlotCondition(slot),
                    () => ExpulsionMenuSlotAction(slot),
                    100 - slot, null, null);
            }

            starter.AddPlayerLine(
                "expulsion_deliberation_more",
                "expulsion_deliberation_topic_choices",
                "expulsion_deliberation_topic_response",
                "{=BC_Deliberation_MoreMatters}There are other matters I would like to discuss.",
                ExpulsionPendingSelectionHasMore,
                AdvanceExpulsionPendingSelectionPage,
                60, null, null);

            starter.AddPlayerLine(
                "expulsion_deliberation_back",
                "expulsion_deliberation_topic_choices",
                "lord_pretalk",
                "{=BC_Deliberation_Back}Never mind. Let us speak of something else.",
                null,
                ClearExpulsionPendingSelection,
                50, null, null);

            starter.AddDialogLine("expulsion_query_not_clan_leader", "expulsion_query_response", "hero_main_options",
                "{=BC_Expul_NotClanLeader}Such matters of judgments are not mine to decide. I speak only for myself, not for the {CLAN_NAME}. If it is politics you wish to discuss, you must seek out {CLAN_LEADER}, the head of our family.",
                VoteConversationIsNotClanLeader,
                null,
                200, null);

            starter.AddDialogLine("expulsion_query_already_bribed", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_AlreadyBribed}We have already reached an arrangement regarding this matter. My position is set.",
                () => _currentQueryAlreadyBribed, null, 120, null);

            starter.AddDialogLine("expulsion_query_strongly_support", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_StrongSupport}I say good riddance. The realm is better without such traitors.",
                () => _queriedScore > 100f, null, 110, null);

            starter.AddDialogLine("expulsion_query_agree", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_Agree}I lean toward supporting the king's judgment, though I keep an open mind.",
                () => _queriedScore > 10f, null, 100, null);

            starter.AddDialogLine("expulsion_query_neutral", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_Neutral}I have not yet resolved myself on this matter. It could go either way.",
                () => _queriedScore >= -10f, null, 90, null);

            starter.AddDialogLine("expulsion_query_disagree", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_Disagree}I have reservations. This accusation seems more political than just.",
                () => _queriedScore >= -100f, null, 80, null);

            starter.AddDialogLine("expulsion_query_strongly_oppose", "expulsion_query_response", "expulsion_query_player_choice",
                "{=BC_Expul_StrongOppose}I will not raise my hand against an innocent lord. The king oversteps.",
                null, null, 70, null);

            // -------------------- Player choices after hearing the stance --------------------
            starter.AddPlayerLine("expulsion_query_persuade_expel", "expulsion_query_player_choice", "expulsion_persuasion_response",
                "{=BC_Expul_PersuadeExpelStart}Perhaps I can convince you to support the expulsion.",
                () => _queriedScore < C.ExpulsionBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToExpel = true; StartExpulsionPersuasion(); },
                120, ExpulsionPersuasionClickableCondition, null);

            starter.AddPlayerLine("expulsion_query_persuade_defend", "expulsion_query_player_choice", "expulsion_persuasion_response",
                "{=BC_Expul_PersuadeDefendStart}Perhaps I can convince you to speak in their defense.",
                () => _queriedScore > -C.ExpulsionBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToExpel = false; StartExpulsionPersuasion(); },
                119, ExpulsionPersuasionClickableCondition, null);

            starter.AddPlayerLine("expulsion_query_sway_expel", "expulsion_query_player_choice", "expulsion_sway_response",
                "{=BC_Expul_SwayExpel}I would be... grateful if you were to vote in favour of the expulsion.",
                () => _queriedScore < C.ExpulsionBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToExpel = true; },
                100, ExpulsionBribeClickableCondition, null);

            starter.AddPlayerLine("expulsion_query_sway_defend", "expulsion_query_player_choice", "expulsion_sway_response",
                "{=BC_Expul_SwayDefend}I would be... grateful if you were to speak in their defence.",
                () => _queriedScore > -C.ExpulsionBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToExpel = false; },
                100, ExpulsionBribeClickableCondition, null);

            starter.AddPlayerLine("expulsion_query_end", "expulsion_query_player_choice", "lord_pretalk",
                "{=BC_Expul_EndQuery}I will keep that in mind. Thank you, my lord.",
                null, null, 90, null, null);

            starter.AddDialogLine("expulsion_sway_npc_response", "expulsion_sway_response", "expulsion_sway_barter",
                "{=BC_Expul_SwayResponse}An interesting proposition. I am prepared to hear what you are offering.",
                null, null, 100, null);

            starter.AddPlayerLine("expulsion_sway_barter_start", "expulsion_sway_barter", "lord_pretalk",
                "{=BC_Expul_DiscussTerms}Let us discuss terms.",
                null, LaunchExpulsionBribeBarter, 100, null, null);

            starter.AddDialogLine("expulsion_persuasion_npc_response", "expulsion_persuasion_response", "expulsion_persuasion_arguments",
                "{=BC_Expul_PersuasionResponse}This is no small judgment. What argument do you offer?",
                null, null, 100, null);

            starter.AddPlayerLine("expulsion_persuasion_justice_expel", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeJusticeExpel}No realm can survive if treason goes unanswered.",
                () => _swayToExpel, () => BlockExpulsionPersuasionOption("justice"), 120,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("justice", out hintText),
                () => BuildExpulsionPersuasionOption("justice"));

            starter.AddPlayerLine("expulsion_persuasion_justice_defend", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeJusticeDefend}Suspicion is not proof. A noble house should not be ruined on fear alone.",
                () => !_swayToExpel, () => BlockExpulsionPersuasionOption("justice"), 120,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("justice", out hintText),
                () => BuildExpulsionPersuasionOption("justice"));

            starter.AddPlayerLine("expulsion_persuasion_security_expel", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeSecurityExpel}Leave this threat unchecked, and the realm may bleed for it later.",
                () => _swayToExpel, () => BlockExpulsionPersuasionOption("security"), 110,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("security", out hintText),
                () => BuildExpulsionPersuasionOption("security"));

            starter.AddPlayerLine("expulsion_persuasion_security_defend", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeSecurityDefend}Strip one clan unjustly, and half the realm will wonder who is next.",
                () => !_swayToExpel, () => BlockExpulsionPersuasionOption("security"), 110,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("security", out hintText),
                () => BuildExpulsionPersuasionOption("security"));

            starter.AddPlayerLine("expulsion_persuasion_realm_expel", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeRealmExpel}The king has made his judgment. The realm must not fracture over this.",
                () => _swayToExpel, () => BlockExpulsionPersuasionOption("realm"), 100,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("realm", out hintText),
                () => BuildExpulsionPersuasionOption("realm"));

            starter.AddPlayerLine("expulsion_persuasion_realm_defend", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeRealmDefend}Punishment without restraint breeds more rebellion than it prevents.",
                () => !_swayToExpel, () => BlockExpulsionPersuasionOption("realm"), 100,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("realm", out hintText),
                () => BuildExpulsionPersuasionOption("realm"));

            starter.AddPlayerLine("expulsion_persuasion_resolve_expel", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeResolveExpel}If we fear to act now, every traitor will learn that the crown is weak.",
                () => _swayToExpel, () => BlockExpulsionPersuasionOption("resolve"), 90,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("resolve", out hintText),
                () => BuildExpulsionPersuasionOption("resolve"));

            starter.AddPlayerLine("expulsion_persuasion_resolve_defend", "expulsion_persuasion_arguments", "expulsion_persuasion_result",
                "{=BC_Expul_PersuadeResolveDefend}If the crown can do this to them today, it can do it to any of us tomorrow.",
                () => !_swayToExpel, () => BlockExpulsionPersuasionOption("resolve"), 90,
                (out TextObject hintText) => ExpulsionPersuasionOptionClickable("resolve", out hintText),
                () => BuildExpulsionPersuasionOption("resolve"));

            starter.AddDialogLine("expulsion_persuasion_success", "expulsion_persuasion_result", "lord_pretalk",
                "{=BC_Expul_PersuasionSuccess}Very well. I will cast my vote as you ask.",
                ExpulsionPersuasionSucceededCondition,
                ApplyExpulsionPersuasionSuccess,
                120, null);

            starter.AddDialogLine("expulsion_persuasion_failed", "expulsion_persuasion_result", "lord_pretalk",
                "{=BC_Expul_PersuasionFailed}No. I have heard enough, and my judgment remains unchanged.",
                ExpulsionPersuasionFailedCondition,
                ApplyExpulsionPersuasionFailure,
                110, null);

            starter.AddDialogLine("expulsion_persuasion_continue", "expulsion_persuasion_result", "expulsion_persuasion_arguments",
                "{=BC_Expul_PersuasionContinue}You have not convinced me yet. What else can you say?",
                ExpulsionPersuasionCanContinueCondition,
                null, 100, null);

            starter.AddPlayerLine(
                "treason_indict_start",
                "lord_talk_speak_diplomacy_2",
                "treason_indict_npc_response",
                "{=BC_Treason_InductOption}I have a matter of grave concern. Step forward, I must speak with you.",
                TreasonInductCondition,
                TreasonInductAction,
                100, null, null);

            starter.AddDialogLine("treason_indict_royalist", "treason_indict_npc_response", "treason_indict_player_choice",
                "{=BC_Treason_ResponseRoyalist}I have served the crown faithfully all my life. If my king turns against me... I will hear the charge in silence.",
                () => _treasonTargetFactionType == (int)FactionType.Royalists, null, 150, null);

            starter.AddDialogLine("treason_indict_militarist", "treason_indict_npc_response", "treason_indict_player_choice",
                "{=BC_Treason_ResponseMilitarist}A soldier who fractures the line must answer to his general. Speak your charge, my lord.",
                () => _treasonTargetFactionType == (int)FactionType.Glory, null, 140, null);

            starter.AddDialogLine("treason_indict_aristocrat", "treason_indict_npc_response", "treason_indict_player_choice",
                "{=BC_Treason_ResponseAristocrat}This is an outrage! I have not acted against the realm and you know it. But I will hear your decree, my king.",
                () => _treasonTargetFactionType == (int)FactionType.Nobility, null, 130, null);

            starter.AddDialogLine("treason_indict_populist", "treason_indict_npc_response", "treason_indict_player_choice",
                "{=BC_Treason_ResponsePopulist}The people will remember what transpires here. Whether I stood or fled, history will judge us both. Make your decree.",
                () => _treasonTargetFactionType == (int)FactionType.Liberty, null, 120, null);

            starter.AddDialogLine("treason_indict_none", "treason_indict_npc_response", "treason_indict_player_choice",
                "{=BC_Treason_ResponseNone}I... this is unexpected. I did not think it would come to this. What would you have of me, my lord?",
                null, null, 100, null);

            starter.AddPlayerLine("treason_indict_expel", "treason_indict_player_choice", "treason_indict_vote_response",
                "{=BC_Treason_ChoiceExpel}I charge you with treason. You will answer before the assembled lords of the realm.",
                null,
                QueuePlayerTreasonVote,
                110, TreasonIndictClickableCondition, null);

            starter.AddDialogLine("treason_indict_vote_queued", "treason_indict_vote_response", "treason_indict_end",
                "{=BC_Treason_VoteResponse}Then summon the court. I will answer the charge before my peers.",
                null, null, 100, null);


            starter.AddPlayerLine("treason_schedule_decree", "treason_indict_player_choice", "treason_scheduled_response",
                "{=BC_Treason_DirectDecree}For your high treason, I pronounce judgment by royal decree. You shall no longer hold your place among my vassals.",
                () => CourtAgendaBehavior.Current != null,
                () => Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.TryIssuePlayerTreasonDecree(Clan.PlayerClan.Kingdom, _treasonInductTarget),
                100, HighTreasonIndictClickableCondition, null);
            starter.AddDialogLine("treason_scheduled_response", "treason_scheduled_response", "treason_indict_end",
                "{=BC_Treason_DirectResponse}Then let the realm bear witness to your judgment, my lord.",
                null, null, 100, null);

            starter.AddPlayerLine("treason_indict_cancel", "treason_indict_player_choice", "lord_pretalk",
                "{=BC_Treason_ChoiceCancel}I have reconsidered. We will speak no more of this.",
                null, null, 90, null, null);


            starter.AddPlayerLine("treason_indict_end", "treason_indict_end", "close_window",
                "{=BC_Treason_EndConversation}It is done.",
                null, null, 100, null, null);
        }


        private static string SlotQueryText(int slot) =>
            slot == 0 ? "{=BC_Expul_DirectQuestion0}What do you make of the accusation against the {EX0C}, members of the {EX0F}?"
          : slot == 1 ? "{=BC_Expul_DirectQuestion1}What do you make of the accusation against the {EX1C}, members of the {EX1F}?"
          : slot == 2 ? "{=BC_Expul_DirectQuestion2}What do you make of the accusation against the {EX2C}, members of the {EX2F}?"
                      : "{=BC_Expul_DirectQuestion3}What do you make of the accusation against the {EX3C}, members of the {EX3F}?";

        private bool ExpulsionMenuEntryCondition()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return false;
            if (npc.Clan == Clan.PlayerClan) return false;
            if (npc.Clan.IsUnderMercenaryService) return false;
            if (npc.Clan.IsMinorFaction && npc.Clan != Clan.PlayerClan) return false;
            if (Clan.PlayerClan.Kingdom == null) return false;
            if (npc.Clan.Kingdom != Clan.PlayerClan.Kingdom) return false;
            RefreshExpulsionConversationPendingKeys();
            return _conversationPendingKeys.Count > 0;
        }

        private void BeginExpulsionPendingSelection()
        {
            _conversationPendingPage = 0;
            RefreshExpulsionConversationPendingKeys();
        }

        private void ClearExpulsionPendingSelection()
        {
            _conversationPendingPage = 0;
            _conversationPendingKeys.Clear();
        }

        private void RefreshExpulsionConversationPendingKeys()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null)
            {
                _conversationPendingKeys.Clear();
                _conversationPendingPage = 0;
                return;
            }

            string prefix = KingdomPrefix(kingdom);
            _conversationPendingKeys = _pendingExpelDate
                .Where(kv => kv.Key.StartsWith(prefix) && TryResolvePendingExpulsionForConversation(kingdom, kv.Key, out _))
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            int maxPage = Math.Max(0, (_conversationPendingKeys.Count - 1) / DeliberationDialogueHelper.PageSize);
            _conversationPendingPage = Math.Min(_conversationPendingPage, maxPage);
        }

        private static bool TryResolvePendingExpulsionForConversation(Kingdom kingdom, string pendingKey, out Clan target)
        {
            target = null;
            if (kingdom == null || string.IsNullOrEmpty(pendingKey)) return false;

            string prefix = KingdomPrefix(kingdom);
            if (!pendingKey.StartsWith(prefix, StringComparison.Ordinal)) return false;

            string targetId = pendingKey.Substring(prefix.Length);
            target = Clan.All.FirstOrDefault(c => c.StringId == targetId);
            return target != null
                && !target.IsEliminated
                && target.Kingdom == kingdom
                && !target.IsUnderMercenaryService
                && target.Leader != null
                && target.Leader.IsAlive;
        }

        private bool VoteConversationIsNotClanLeader()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || npc == npc.Clan.Leader)
                return false;

            MBTextManager.SetTextVariable("CLAN_NAME", npc.Clan.Name);
            MBTextManager.SetTextVariable("CLAN_LEADER", npc.Clan.Leader?.Name ?? new TextObject("{=BC_Appeasement_HeadOfClan}the head of our clan"));
            return true;
        }

        private bool ExpulsionMenuSlotCondition(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return false;

            string pendingKey = _conversationPendingKeys[index];
            if (!TryResolvePendingExpulsionForConversation(Clan.PlayerClan?.Kingdom, pendingKey, out Clan target)) return false;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var factionObj     = factionManager?.GetIdeologicalFaction(target);
            string factionName = factionObj?.GetDisplayName()?.ToString()
                ?? new TextObject("{=BC_PolicyDelib_UnknownFaction}the faction").ToString();

            MBTextManager.SetTextVariable($"EX{slot}C", target.Name);
            MBTextManager.SetTextVariable($"EX{slot}F", factionName);

            return true;
        }

        private void ExpulsionMenuSlotAction(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return;

            string pendingKey = _conversationPendingKeys[index];
            if (!TryResolvePendingExpulsionForConversation(Clan.PlayerClan?.Kingdom, pendingKey, out Clan targetClan)) return;

            _currentQueryTargetClan = targetClan;
            _currentQueryPendingKey = pendingKey;

            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            string proposerClanId = _pendingExpelProposer.TryGetValue(pendingKey, out string pid) ? pid : "";
            Clan   proposerClan   = Clan.All.FirstOrDefault(c => c.StringId == proposerClanId)
                                 ?? Clan.PlayerClan.Kingdom?.RulingClan;

            _queriedScore = ExpulsionVoteAIPatch.CalculateSupportScore(
                targetClan, proposerClan, npc.Clan, factionManager);

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            _currentQueryAlreadyBribed = kingdom != null
                && _expelBribedOverrides.ContainsKey(BribeKey(kingdom, targetClan, npc.Clan));
        }

        private bool ExpulsionPendingSelectionHasMore() =>
            ((_conversationPendingPage + 1) * DeliberationDialogueHelper.PageSize) < _conversationPendingKeys.Count;

        private void AdvanceExpulsionPendingSelectionPage()
        {
            _conversationPendingPage++;
            RefreshExpulsionConversationPendingKeys();
        }

        private bool ExpulsionPersuasionClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan == null || kingdom == null || _currentQueryTargetClan == null)
            {
                explanation = new TextObject("{=BC_Expul_PersuasionUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(npc.Clan, kingdom,
                VotePledgeService.ExpulsionKey(kingdom, _currentQueryTargetClan, npc.Clan), out explanation)) return false;

            string key = BribeKey(kingdom, _currentQueryTargetClan, npc.Clan);
            if (_expelPersuasionFailed.ContainsKey(key))
            {
                explanation = new TextObject("{=BC_Expul_PersuasionAlreadyFailed}They have already rejected your argument in this matter.");
                return false;
            }

            int relation = Hero.MainHero?.GetRelation(npc.Clan.Leader) ?? 0;
            if (relation < 30)
            {
                explanation = new TextObject("{=BC_Expul_PersuasionLowRelation}They do not trust you enough to be swayed by argument. Relation required: 30.");
                return false;
            }

            return true;
        }

        private bool ExpulsionBribeClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan == null || kingdom == null || _currentQueryTargetClan == null)
            {
                explanation = new TextObject("{=BC_Expul_BribeUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(npc.Clan, kingdom,
                VotePledgeService.ExpulsionKey(kingdom, _currentQueryTargetClan, npc.Clan), out explanation)) return false;

            if (_currentQueryAlreadyBribed)
            {
                explanation = new TextObject("{=BC_Expul_BribeAlreadyCommitted}They have already committed their vote in this matter.");
                return false;
            }

            float desiredDirectionScore = _swayToExpel ? _queriedScore : -_queriedScore;
            if (desiredDirectionScore >= C.ExpulsionBribeSameDirectionThreshold)
                return true;

            float openness = CalculateExpulsionBribeOpenness(npc.Clan, desiredDirectionScore);
            if (openness >= C.FiefBribeOpennessThreshold)
                return true;

            int honor = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Honor);
            int mercy = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Mercy);
            int generosity = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Generosity);
            int relationToAccused = _currentQueryTargetClan?.Leader != null
                ? npc.Clan.Leader.GetRelation(_currentQueryTargetClan.Leader)
                : 0;

            if (honor >= 1)
                explanation = new TextObject("{=BC_Expul_BribeHonorRefusal}They consider this judgment a matter of honor and will not bargain over it.");
            else if (mercy >= 1 || generosity >= 1)
                explanation = new TextObject("{=BC_Expul_BribePrincipledRefusal}They are not inclined to trade favors over the fate of another noble house.");
            else if (relationToAccused >= 30 && _swayToExpel)
                explanation = new TextObject("{=BC_Expul_BribeLoyalRefusal}They are too loyal to the accused clan to discuss condemning them.");
            else if (desiredDirectionScore <= -100f)
                explanation = new TextObject("{=BC_Expul_BribeCommittedRefusal}They are too committed to their current stance to discuss changing sides.");
            else
                explanation = new TextObject("{=BC_Expul_BribeRefusal}They are not willing to bargain over this vote.");

            return false;
        }

        private float CalculateExpulsionBribeOpenness(Clan voter, float desiredDirectionScore)
        {
            if (voter?.Leader == null)
                return 0f;

            Hero voterLeader = voter.Leader;
            float openness = C.FiefBribeOpennessBase;

            int honor = voterLeader.GetTraitLevel(DefaultTraits.Honor);
            int mercy = voterLeader.GetTraitLevel(DefaultTraits.Mercy);
            int generosity = voterLeader.GetTraitLevel(DefaultTraits.Generosity);
            int calculating = voterLeader.GetTraitLevel(DefaultTraits.Calculating);

            if (honor > 0) openness -= honor * C.FiefBribeHonorPenalty;
            else if (honor < 0) openness += -honor * C.FiefBribeDishonorBonus;

            if (mercy > 0) openness -= mercy * C.FiefBribeMercyPenalty;
            else if (mercy < 0) openness += -mercy * C.FiefBribeCrueltyBonus;

            if (generosity > 0) openness -= generosity * C.FiefBribeGenerosityPenalty;
            else if (generosity < 0) openness += -generosity * C.FiefBribeGreedBonus;

            if (calculating > 0) openness += calculating * C.FiefBribeCalculatingBonus;
            else if (calculating < 0) openness -= -calculating * C.FiefBribeHotheadPenalty;

            if (Hero.MainHero != null)
                openness += voterLeader.GetRelation(Hero.MainHero) * C.FiefBribePlayerRelationScale;

            if (_currentQueryTargetClan?.Leader != null)
            {
                int relationToAccused = voterLeader.GetRelation(_currentQueryTargetClan.Leader);
                openness += (_swayToExpel ? -relationToAccused : relationToAccused) * C.FiefBribeSelectedRelationScale;
            }

            openness += MathF.Clamp(desiredDirectionScore, -150f, 100f) * 0.25f;
            return openness;
        }

        private void StartExpulsionPersuasion()
        {
            _expelPersuasionOptions.Clear();
            ConversationManager.StartPersuasion(
                goalValue: C.FiefPersuasionGoal,
                successValue: C.FiefPersuasionSuccessValue,
                failValue: 0f,
                criticalSuccessValue: C.FiefPersuasionCriticalSuccessValue,
                criticalFailValue: C.FiefPersuasionCriticalFailValue,
                initialProgress: 0f,
                difficulty: PersuasionDifficulty.Medium);
        }

        private PersuasionOptionArgs BuildExpulsionPersuasionOption(string argumentType)
        {
            if (_expelPersuasionOptions.TryGetValue(argumentType, out PersuasionOptionArgs cachedOption))
                return cachedOption;

            Hero voter = Hero.OneToOneConversationHero?.Clan?.Leader;

            TraitObject trait = DefaultTraits.Honor;
            TraitEffect effect = TraitEffect.Positive;
            PersuasionArgumentStrength strength = PersuasionArgumentStrength.Normal;
            TextObject line = new TextObject("{=BC_Expul_PersuadeJusticeExpel}No realm can survive if treason goes unanswered.");
            Tuple<TraitObject, int>[] correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };

            if (argumentType == "justice")
            {
                trait = DefaultTraits.Honor;
                line = new TextObject(_swayToExpel
                    ? "{=BC_Expul_PersuadeJusticeExpel}No realm can survive if treason goes unanswered."
                    : "{=BC_Expul_PersuadeJusticeDefend}Suspicion is not proof. A noble house should not be ruined on fear alone.");
                correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Honor);
            }
            else if (argumentType == "security")
            {
                trait = DefaultTraits.Calculating;
                line = new TextObject(_swayToExpel
                    ? "{=BC_Expul_PersuadeSecurityExpel}Leave this threat unchecked, and the realm may bleed for it later."
                    : "{=BC_Expul_PersuadeSecurityDefend}Strip one clan unjustly, and half the realm will wonder who is next.");
                correlations = new[] { Tuple.Create(DefaultTraits.Calculating, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Calculating);
            }
            else if (argumentType == "realm")
            {
                trait = _swayToExpel ? DefaultTraits.Honor : DefaultTraits.Mercy;
                line = new TextObject(_swayToExpel
                    ? "{=BC_Expul_PersuadeRealmExpel}The king has made his judgment. The realm must not fracture over this."
                    : "{=BC_Expul_PersuadeRealmDefend}Punishment without restraint breeds more rebellion than it prevents.");
                correlations = _swayToExpel
                    ? new[] { Tuple.Create(DefaultTraits.Honor, 1) }
                    : new[] { Tuple.Create(DefaultTraits.Mercy, 1), Tuple.Create(DefaultTraits.Generosity, 1) };
                strength = _swayToExpel
                    ? StrengthFromTrait(voter, DefaultTraits.Honor)
                    : StrengthFromBestTrait(voter, DefaultTraits.Mercy, DefaultTraits.Generosity);
            }
            else if (argumentType == "resolve")
            {
                trait = DefaultTraits.Valor;
                line = new TextObject(_swayToExpel
                    ? "{=BC_Expul_PersuadeResolveExpel}If we fear to act now, every traitor will learn that the crown is weak."
                    : "{=BC_Expul_PersuadeResolveDefend}If the crown can do this to them today, it can do it to any of us tomorrow.");
                correlations = new[] { Tuple.Create(DefaultTraits.Valor, 1), Tuple.Create(DefaultTraits.Honor, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Valor);
            }

            strength = AdjustExpulsionPersuasionStrengthForContext(strength, voter);

            PersuasionOptionArgs option = new PersuasionOptionArgs(
                DefaultSkills.Charm,
                trait,
                effect,
                strength,
                false,
                line,
                correlations,
                false,
                false,
                false);

            _expelPersuasionOptions[argumentType] = option;
            return option;
        }

        private void BlockExpulsionPersuasionOption(string argumentType)
        {
            BuildExpulsionPersuasionOption(argumentType)?.BlockTheOption(true);
        }

        private bool ExpulsionPersuasionOptionClickable(string argumentType, out TextObject hintText)
        {
            PersuasionOptionArgs option = BuildExpulsionPersuasionOption(argumentType);
            if (option == null || !option.IsBlocked)
            {
                hintText = null;
                return true;
            }

            hintText = new TextObject("{=9ACJsI6S}Blocked");
            return false;
        }

        private PersuasionArgumentStrength StrengthFromTrait(Hero hero, TraitObject trait)
        {
            int level = hero?.GetTraitLevel(trait) ?? 0;
            if (level >= 1) return PersuasionArgumentStrength.Easy;
            if (level <= -1) return PersuasionArgumentStrength.Hard;
            return PersuasionArgumentStrength.Normal;
        }

        private PersuasionArgumentStrength StrengthFromBestTrait(Hero hero, TraitObject first, TraitObject second)
        {
            int level = System.Math.Max(hero?.GetTraitLevel(first) ?? 0, hero?.GetTraitLevel(second) ?? 0);
            if (level >= 1) return PersuasionArgumentStrength.Easy;
            if (level <= -1) return PersuasionArgumentStrength.Hard;
            return PersuasionArgumentStrength.Normal;
        }

        private PersuasionArgumentStrength AdjustExpulsionPersuasionStrengthForContext(PersuasionArgumentStrength baseStrength, Hero voter)
        {
            float desiredDirectionScore = _swayToExpel ? _queriedScore : -_queriedScore;
            PersuasionArgumentStrength strength = baseStrength;

            if (desiredDirectionScore <= -100f)
                strength = MakePersuasionHarder(MakePersuasionHarder(strength));
            else if (desiredDirectionScore <= -10f)
                strength = MakePersuasionHarder(strength);
            else if (desiredDirectionScore >= 10f)
                strength = MakePersuasionEasier(strength);

            if (voter != null && _currentQueryTargetClan?.Leader != null)
            {
                int relationToAccused = voter.GetRelation(_currentQueryTargetClan.Leader);
                if (_swayToExpel)
                {
                    if (relationToAccused >= 30) strength = MakePersuasionHarder(strength);
                    else if (relationToAccused <= -30) strength = MakePersuasionEasier(strength);
                }
                else
                {
                    if (relationToAccused >= 30) strength = MakePersuasionEasier(strength);
                    else if (relationToAccused <= -30) strength = MakePersuasionHarder(strength);
                }
            }

            return strength;
        }

        private static PersuasionArgumentStrength MakePersuasionHarder(PersuasionArgumentStrength strength)
        {
            switch (strength)
            {
                case PersuasionArgumentStrength.ExtremelyEasy: return PersuasionArgumentStrength.VeryEasy;
                case PersuasionArgumentStrength.VeryEasy: return PersuasionArgumentStrength.Easy;
                case PersuasionArgumentStrength.Easy: return PersuasionArgumentStrength.Normal;
                case PersuasionArgumentStrength.Normal: return PersuasionArgumentStrength.Hard;
                case PersuasionArgumentStrength.Hard: return PersuasionArgumentStrength.VeryHard;
                case PersuasionArgumentStrength.VeryHard: return PersuasionArgumentStrength.ExtremelyHard;
                default: return PersuasionArgumentStrength.ExtremelyHard;
            }
        }

        private static PersuasionArgumentStrength MakePersuasionEasier(PersuasionArgumentStrength strength)
        {
            switch (strength)
            {
                case PersuasionArgumentStrength.ExtremelyHard: return PersuasionArgumentStrength.VeryHard;
                case PersuasionArgumentStrength.VeryHard: return PersuasionArgumentStrength.Hard;
                case PersuasionArgumentStrength.Hard: return PersuasionArgumentStrength.Normal;
                case PersuasionArgumentStrength.Normal: return PersuasionArgumentStrength.Easy;
                case PersuasionArgumentStrength.Easy: return PersuasionArgumentStrength.VeryEasy;
                case PersuasionArgumentStrength.VeryEasy: return PersuasionArgumentStrength.ExtremelyEasy;
                default: return PersuasionArgumentStrength.ExtremelyEasy;
            }
        }

        private bool ExpulsionPersuasionSucceededCondition()
        {
            return ConversationManager.GetPersuasionProgressSatisfied();
        }

        private bool ExpulsionPersuasionFailedCondition()
        {
            return ConversationManager.GetPersuasionIsFailure()
                || ExpulsionPersuasionLastRollWasCriticalFailure()
                || ExpulsionPersuasionArgumentsExhausted();
        }

        private bool ExpulsionPersuasionCanContinueCondition()
        {
            return !ExpulsionPersuasionSucceededCondition()
                && !ConversationManager.GetPersuasionIsFailure()
                && !ExpulsionPersuasionArgumentsExhausted();
        }

        private bool ExpulsionPersuasionArgumentsExhausted()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            int chosenCount = ConversationManager.GetPersuasionChosenOptions()?.Count() ?? 0;
            return chosenCount >= 4 && !ConversationManager.GetPersuasionProgressSatisfied();
        }

        private bool ExpulsionPersuasionLastRollWasCriticalFailure()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            Tuple<PersuasionOptionArgs, PersuasionOptionResult> lastChoice =
                ConversationManager.GetPersuasionChosenOptions()?.LastOrDefault();

            return lastChoice != null && lastChoice.Item2 == PersuasionOptionResult.CriticalFailure;
        }

        private void ApplyExpulsionPersuasionSuccess()
        {
            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan != null && kingdom != null && _currentQueryTargetClan != null)
            {
                int forcedScore = _swayToExpel ? C.ExpulsionBribeForcedSupportScore : -C.ExpulsionBribeForcedSupportScore;
                VotePledgeService.TryCommit(npc.Clan, kingdom, VotePledgeService.ExpulsionKey(kingdom, _currentQueryTargetClan, npc.Clan),
                    () => SetBribedVote(kingdom, _currentQueryTargetClan, npc.Clan, forcedScore));
            }

            ConversationManager.EndPersuasion();
            _expelPersuasionOptions.Clear();
        }

        private void ApplyExpulsionPersuasionFailure()
        {
            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan != null && kingdom != null && _currentQueryTargetClan != null)
                _expelPersuasionFailed[BribeKey(kingdom, _currentQueryTargetClan, npc.Clan)] = true;

            ConversationManager.EndPersuasion();
            _expelPersuasionOptions.Clear();
        }

        private void LaunchExpulsionBribeBarter()
        {
            Hero    target  = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (target?.Clan == null || kingdom == null || _currentQueryTargetClan == null) return;

            var bribeItem = new ExpulsionVoteBribeBarterable(
                target.Clan, kingdom, _currentQueryTargetClan, _swayToExpel, _queriedScore, Hero.MainHero);

            BarterManager.Instance.StartBarterOffer(
                Hero.MainHero,
                target,
                PartyBase.MainParty,
                target.PartyBelongedTo?.Party,
                null,
                (Barterable barterable, BarterData args, object obj) =>
                {
                    args.AddBarterable<ExpulsionVoteBribeBarterable>(bribeItem);

                    foreach (Settlement s in Hero.MainHero.Clan.Settlements)
                        if (s.IsTown || s.IsCastle)
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(s, Hero.MainHero, target));

                    return true;
                },
                0,
                false,
                new Barterable[] { bribeItem });
        }


        private bool TreasonInductCondition()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || npc != npc.Clan.Leader) return false;
            if (npc.Clan == Clan.PlayerClan) return false;
            if (Clan.PlayerClan.Kingdom == null) return false;
            if (Clan.PlayerClan.Kingdom.RulingClan != Clan.PlayerClan) return false;
            if (npc.Clan.Kingdom != Clan.PlayerClan.Kingdom) return false;

            Kingdom kingdom = Clan.PlayerClan.Kingdom;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager != null)
            {
                foreach (FactionObject f in factionManager.GetFactionsInKingdom(kingdom))
                {
                    if (!f.IsIdeology && f.Leader == npc.Clan) return false;
                }
            }

            return true;
        }

        private void TreasonInductAction()
        {
            Hero npc = Hero.OneToOneConversationHero;
            _treasonInductTarget = npc?.Clan;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject faction = _treasonInductTarget != null
                ? factionManager?.GetIdeologicalFaction(_treasonInductTarget)
                : null;
            _treasonTargetFactionType = faction != null ? (int)faction.Type : -1;
        }

        private bool TreasonIndictClickableCondition(out TextObject explanation)
        {
            return TreasonIndictClickableCondition(false, out explanation);
        }

        private bool HighTreasonIndictClickableCondition(out TextObject explanation)
        {
            return TreasonIndictClickableCondition(true, out explanation);
        }

        private bool TreasonIndictClickableCondition(bool highTreason, out TextObject explanation)
        {
            Clan targetClan = _treasonInductTarget ?? Hero.OneToOneConversationHero?.Clan;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (CourtAgendaBehavior.Current == null)
            {
                explanation = new TextObject("{=BC_CourtAgenda_Unavailable}The court cannot receive a motion at present.");
                return false;
            }
            if (!highTreason)
                return Campaign.Current.GetCampaignBehavior<IdeologyBehavior>().CanRulerIndictClan(kingdom, targetClan, false, out explanation);
            explanation = new TextObject("{=BC_Treason_DecreeThreshold}A royal decree requires relations of -100 and a valid vassal of the realm.");
            var ideology = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            return ideology?.CanIssueTreasonDecree(kingdom, targetClan) == true
                && ideology.CanRulerIndictClan(kingdom, targetClan, true, out explanation);
        }

        private void QueuePlayerTreasonVote()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>()
                ?.TryStartTreasonVote(kingdom, _treasonInductTarget, Clan.PlayerClan, out _);
        }


    }
}
