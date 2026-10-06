using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public enum PlayerPolicyMandateAlignment
    {
        Invalid,
        Aligned,
        OutsideAgenda,
        OpposesOwnAgenda,
        AdvancesRivalAgenda
    }

    /// <summary>
    /// Why did I do this file?
    /// To insert a deliberation window between a faction announcing a policy motion and the vote
    /// actually firing. During this window the player can talk to clan leaders to learn their stance
    /// and bribe them via the barter screen. Multiple policy votes can be in deliberation simultaneously
    /// (one per proposing faction); each appears as a separate choice in the dialogue sub-menu.
    /// All realms, including solo rulers, use scheduled deliberation and voting.
    /// </summary>
    public class PolicyDeliberationBehavior : CampaignBehaviorBase
    {
        public static PolicyDeliberationBehavior Current =>
            Campaign.Current?.GetCampaignBehavior<PolicyDeliberationBehavior>();

        private Dictionary<string, int>          _pendingPolicyAbolish     = new Dictionary<string, int>();
        private Dictionary<string, string>       _pendingFactionLeaderClan = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _pendingVoteDate          = new Dictionary<string, CampaignTime>();
        private Dictionary<string, float>        _pendingPolicyCreatedDay  = new Dictionary<string, float>();
        private Dictionary<string, int>          _pendingPolicyRetryCount  = new Dictionary<string, int>();
        private Dictionary<string, int>          _policyStaleRepairCount   = new Dictionary<string, int>();

        private Dictionary<string, int> _bribedVoteOverrides = new Dictionary<string, int>();
        private Dictionary<string, bool> _policyPersuasionFailed = new Dictionary<string, bool>();
        private readonly Dictionary<string, PersuasionOptionArgs> _policyPersuasionOptions = new Dictionary<string, PersuasionOptionArgs>();

        private float        _queriedScore;
        private bool         _swayToSupport;
        private bool         _currentQueryAlreadyBribed;
        private PolicyObject _currentQueryPolicy;
        private string       _currentQueryPendingKey;
        private List<string> _conversationPendingKeys = new List<string>();
        private int _conversationPendingPage;


        private static string PendingKey(Kingdom kingdom, PolicyObject policy) =>
            kingdom.StringId + "|" + policy.StringId;

        private static string KingdomPrefix(Kingdom kingdom) =>
            kingdom.StringId + "|";

        public static string BribeKey(Kingdom kingdom, PolicyObject policy, Clan clan) =>
            kingdom.StringId + "|" + policy.StringId + "|" + clan.StringId;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            VotePledgeService.Invalidate();
            dataStore.SyncData("BellumCivile_PendingPolicyAbolish",     ref _pendingPolicyAbolish);
            dataStore.SyncData("BellumCivile_PendingFactionLeaderClan", ref _pendingFactionLeaderClan);
            dataStore.SyncData("BellumCivile_PendingVoteDate",          ref _pendingVoteDate);
            dataStore.SyncData("BellumCivile_PendingPolicyCreatedDay",  ref _pendingPolicyCreatedDay);
            dataStore.SyncData("BellumCivile_PendingPolicyRetryCount",  ref _pendingPolicyRetryCount);
            dataStore.SyncData("BellumCivile_PolicyStaleRepairCount",   ref _policyStaleRepairCount);
            dataStore.SyncData("BellumCivile_BribedVoteOverrides",      ref _bribedVoteOverrides);
            dataStore.SyncData("BellumCivile_PolicyPersuasionFailed",   ref _policyPersuasionFailed);

            if (_pendingPolicyAbolish     == null) _pendingPolicyAbolish     = new Dictionary<string, int>();
            if (_pendingFactionLeaderClan == null) _pendingFactionLeaderClan = new Dictionary<string, string>();
            if (_pendingVoteDate          == null) _pendingVoteDate          = new Dictionary<string, CampaignTime>();
            if (_pendingPolicyCreatedDay  == null) _pendingPolicyCreatedDay  = new Dictionary<string, float>();
            if (_pendingPolicyRetryCount  == null) _pendingPolicyRetryCount  = new Dictionary<string, int>();
            if (_policyStaleRepairCount   == null) _policyStaleRepairCount   = new Dictionary<string, int>();
            if (_bribedVoteOverrides      == null) _bribedVoteOverrides      = new Dictionary<string, int>();
            if (_policyPersuasionFailed   == null) _policyPersuasionFailed   = new Dictionary<string, bool>();
        }


        public bool QueuePolicyVote(Kingdom kingdom, PolicyObject policy, bool abolish, FactionObject proposingFaction,
            Clan sponsor = null, CampaignTime? scheduledVoteDate = null)
        {
            sponsor = sponsor ?? proposingFaction?.Leader ?? kingdom?.RulingClan;
            if (kingdom == null || policy == null || sponsor == null) return false;
            string key = PendingKey(kingdom, policy);
            if (_pendingVoteDate.ContainsKey(key)) return false;

            _pendingPolicyAbolish[key]     = abolish ? 1 : 0;
            _pendingFactionLeaderClan[key] = sponsor.StringId;
            _pendingVoteDate[key]          = scheduledVoteDate ?? CampaignTime.Now + CampaignTime.Days(O.PoliticalDeliberationDays);
            InitializePendingLifecycle(key, resetStaleRepair: true);

            TextObject msg = new TextObject("{=BC_PolicyDelib_Announcement}The {FACTION_NAME} of {KINGDOM_NAME} formally moved to {ACTION} the {POLICY_NAME} policy. The court will deliberate on this proposal and call for a vote within {DAYS} days.");
            msg.SetTextVariable("FACTION_NAME", proposingFaction?.GetDisplayName() ?? new TextObject("{=BC_CourtCrown}Crown"));
            msg.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            msg.SetTextVariable("ACTION", new TextObject(abolish ? "{=BC_Ideology_Repeal}repeal" : "{=BC_Ideology_Enact}enact"));
            msg.SetTextVariable("POLICY_NAME", policy.Name);
            msg.SetTextVariable("DAYS", (int)Math.Ceiling(Math.Max(0, _pendingVoteDate[key].ToDays - CampaignTime.Now.ToDays)));
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Politics, primaryKingdom: kingdom, primaryClan: proposingFaction?.Leader);

            return true;
        }

        public IEnumerable<PolicyObject> GetPendingVotePolicies(Kingdom kingdom)
        {
            if (kingdom == null) yield break;
            string prefix = KingdomPrefix(kingdom);
            foreach (var kv in _pendingVoteDate.OrderBy(kv => kv.Value))
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                string policyId = kv.Key.Substring(prefix.Length);
                PolicyObject policy = PolicyObject.All.FirstOrDefault(p => p.StringId == policyId);
                if (policy != null) yield return policy;
            }
        }

        public bool HasPendingVote(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            string prefix = KingdomPrefix(kingdom);
            return _pendingVoteDate.Keys.Any(key => key.StartsWith(prefix));
        }

        public bool HasPendingVoteForPolicy(Kingdom kingdom, PolicyObject policy)
        {
            if (kingdom == null || policy == null) return false;
            return _pendingVoteDate.ContainsKey(PendingKey(kingdom, policy));
        }

        public CampaignTime? GetPendingVoteDate(Kingdom kingdom, PolicyObject policy)
        {
            if (kingdom == null || policy == null) return null;
            return _pendingVoteDate.TryGetValue(PendingKey(kingdom, policy), out var date) ? (CampaignTime?)date : null;
        }

        public void CancelQueuedPolicyVote(Kingdom kingdom, PolicyObject policy, string sponsorId = null)
        {
            if (kingdom == null || policy == null)
                return;

            string pendingKey = PendingKey(kingdom, policy);
            if (sponsorId != null && _pendingFactionLeaderClan.TryGetValue(pendingKey, out var owner) && owner != sponsorId) return;
            ClearBribedVotesForPendingKey(pendingKey);
            RemovePendingKey(pendingKey);
            BellumCivileLogger.Log($"Cancelled queued policy deliberation {pendingKey}.");
        }

        public bool HasActivePlayerPolicyMandate(Kingdom kingdom)
            => kingdom != null && CourtAgendaBehavior.Current?.IsNominationOpen(kingdom) == true;

        public bool CanPlayerUsePolicyMandate(Kingdom kingdom, PolicyObject policy, bool abolish)
        {
            return GetPlayerPolicyMandateAlignment(kingdom, policy, abolish) != PlayerPolicyMandateAlignment.Invalid;
        }

        public PlayerPolicyMandateAlignment GetPlayerPolicyMandateAlignment(Kingdom kingdom, PolicyObject policy, bool abolish)
        {
            if (kingdom == null || policy == null || !HasActivePlayerPolicyMandate(kingdom))
                return PlayerPolicyMandateAlignment.Invalid;

            var termAgenda = CourtAgendaBehavior.Current?.GetPlayerTermAgenda(kingdom);
            if (termAgenda?.Faction == null || !CourtAgendaBehavior.Current.IsNominationOpen(kingdom))
                return PlayerPolicyMandateAlignment.Invalid;

            FactionType factionType = termAgenda.Faction.Type;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject playerFaction = factionManager?.GetIdeologicalFaction(Clan.PlayerClan);
            if (playerFaction == null
                || playerFaction.ParentKingdom != kingdom
                || playerFaction.Leader != Clan.PlayerClan
                || playerFaction.Type != factionType)
            {
                return PlayerPolicyMandateAlignment.Invalid;
            }

            CourtPolicyStance stance = IdeologyPolicyRoster.GetEffectiveStance(playerFaction, policy);
            if (stance == CourtPolicyStance.Neutral) return PlayerPolicyMandateAlignment.OutsideAgenda;
            return (stance == CourtPolicyStance.Support) != abolish
                ? PlayerPolicyMandateAlignment.Aligned : PlayerPolicyMandateAlignment.OpposesOwnAgenda;
        }

        public bool TryShowPlayerPolicyMandateDeviationConfirmation(
            Kingdom kingdom,
            PolicyObject policy,
            bool abolish,
            Action onConfirmed)
        {
            PlayerPolicyMandateAlignment alignment = GetPlayerPolicyMandateAlignment(kingdom, policy, abolish);
            if (alignment == PlayerPolicyMandateAlignment.Invalid
                || alignment == PlayerPolicyMandateAlignment.Aligned)
            {
                return false;
            }

            FactionObject playerFaction = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetIdeologicalFaction(Clan.PlayerClan);
            if (playerFaction == null || !GetPolicyMandateRelationTargets(playerFaction).Any())
                return false;

            TextObject title = new TextObject("{=BC_PolicyMandate_DeviationTitle}Depart from the faction agenda?");
            TextObject description;
            if (alignment == PlayerPolicyMandateAlignment.AdvancesRivalAgenda)
            {
                description = new TextObject("{=BC_PolicyMandate_RivalConfirmation}This motion advances the agenda of your faction's rivals. Members of the {FACTION_NAME} will regard using their mandate this way as a betrayal. Do you still wish to proceed?");
            }
            else if (alignment == PlayerPolicyMandateAlignment.OpposesOwnAgenda)
            {
                description = new TextObject("{=BC_PolicyMandate_OwnAgendaConfirmation}This motion directly attacks a policy championed by the {FACTION_NAME}. Its members will regard using their mandate this way as a betrayal. Do you still wish to proceed?");
            }
            else
            {
                description = new TextObject("{=BC_PolicyMandate_OutsideConfirmation}This motion lies outside the agenda entrusted to you by the {FACTION_NAME}. Pursuing it will displease your political allies. Do you still wish to proceed?");
            }

            description.SetTextVariable("FACTION_NAME", playerFaction.GetDisplayName());
            InformationManager.ShowInquiry(new InquiryData(
                title.ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_PolicyMandate_Proceed}Proceed").ToString(),
                new TextObject("{=BC_PolicyMandate_Reconsider}Reconsider").ToString(),
                onConfirmed,
                null), true);
            return true;
        }


        public int? GetBribedVote(Kingdom kingdom, PolicyObject policy, Clan clan)
        {
            if (kingdom == null || policy == null || clan == null) return null;
            if (_bribedVoteOverrides.TryGetValue(BribeKey(kingdom, policy, clan), out int score)) return score;
            return null;
        }

        public void SetBribedVote(Kingdom kingdom, PolicyObject policy, Clan clan, int score)
        {
            if (kingdom == null || policy == null || clan == null) return;
            _bribedVoteOverrides[BribeKey(kingdom, policy, clan)] = score;
            VotePledgeService.Invalidate();
        }

        internal IEnumerable<string> GetVotePledgeKeys(Clan voter) => VotePledgeService.KeysFor(voter, "policy",
            _bribedVoteOverrides.Where(entry => entry.Value != 0).Select(entry => entry.Key));

        // What does this method do?
        // Called by BlockVanillaPolicyPatch when the player proposes a vote through the Kingdom UI.
        // Routes through the same deliberation window as faction-initiated proposals, but uses a
        // player-specific announcement that names the proposer. Returns false only if this exact
        // policy is already pending (shouldn't happen in practice since the button is greyed out).
        public bool QueuePlayerProposedVote(Kingdom kingdom, PolicyObject policy, bool abolish, bool agendaDeviationConfirmed = false)
        {
            if (kingdom == null || policy == null)
                return false;

            bool nomination = CourtAgendaBehavior.Current?.IsNominationOpen(kingdom) == true;
            bool playerIsRuler = kingdom.RulingClan == Clan.PlayerClan && !nomination;
            PlayerPolicyMandateAlignment mandateAlignment = playerIsRuler
                ? PlayerPolicyMandateAlignment.Aligned
                : GetPlayerPolicyMandateAlignment(kingdom, policy, abolish);
            if (!playerIsRuler && mandateAlignment == PlayerPolicyMandateAlignment.Invalid)
            {
                BellumCivileLogger.Log($"Blocked player policy proposal without valid court mandate; kingdom={kingdom.StringId} policy={policy.StringId} abolish={abolish}.");
                return false;
            }
            if (!playerIsRuler
                && mandateAlignment != PlayerPolicyMandateAlignment.Aligned
                && !agendaDeviationConfirmed)
            {
                BellumCivileLogger.Log($"Blocked unconfirmed player policy deviation; kingdom={kingdom.StringId} policy={policy.StringId} abolish={abolish} alignment={mandateAlignment}.");
                return false;
            }

            if (nomination)
            {
                if (!CourtAgendaBehavior.Current.TryNominatePolicy(kingdom, policy, abolish)) return false;
                ApplyPlayerPolicyMandateDeviation(mandateAlignment);
                return true;
            }

            if (HasPlayerProposedVote(kingdom))
            {
                BellumCivileLogger.Log($"Blocked additional player policy proposal while another player motion is pending; kingdom={kingdom.StringId} policy={policy.StringId} abolish={abolish}.");
                return false;
            }

            string key = PendingKey(kingdom, policy);
            if (_pendingVoteDate.ContainsKey(key))
            {
                BellumCivileLogger.Log($"Policy deliberation duplicate ignored for {key}.");
                return false;
            }

            if (CourtAgendaBehavior.Current?.TryRegisterManualPayment(kingdom, policy, abolish) != true)
                return false;

            _pendingPolicyAbolish[key]     = abolish ? 1 : 0;
            _pendingFactionLeaderClan[key] = Clan.PlayerClan.StringId;
            _pendingVoteDate[key]          = CourtAgendaBehavior.Current.GetScheduledVoteDate(kingdom, policy, Clan.PlayerClan)
                ?? CampaignTime.Now + CampaignTime.Days(O.PoliticalDeliberationDays);
            InitializePendingLifecycle(key, resetStaleRepair: true);
            BellumCivileLogger.Log($"Queued player policy deliberation {key}; abolish={abolish}; fires={_pendingVoteDate[key].ToDays:0.00}.");

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject playerFaction = factionManager?.GetIdeologicalFaction(Clan.PlayerClan);
            TextObject fallbackFaction = new TextObject("{=BC_PolicyDelib_RulingCouncil}ruling council");
            string factionDisplay = playerFaction?.GetDisplayName()?.ToString() ?? fallbackFaction.ToString();

            TextObject msg = new TextObject("{=BC_PolicyDelib_PlayerAnnouncement}{PROPOSER_NAME} of the {FACTION_NAME} of {KINGDOM_NAME} formally moved to {ACTION} the {POLICY_NAME} policy. The court will deliberate and call for a vote within {DAYS} days.");
            msg.SetTextVariable("PROPOSER_NAME", Hero.MainHero.Name);
            msg.SetTextVariable("FACTION_NAME", factionDisplay);
            msg.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            msg.SetTextVariable("ACTION", new TextObject(abolish ? "{=BC_Ideology_Repeal}repeal" : "{=BC_Ideology_Enact}enact"));
            msg.SetTextVariable("POLICY_NAME", policy.Name);
            msg.SetTextVariable("DAYS", Math.Max(0, O.PoliticalDeliberationDays));
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Politics, primaryKingdom: kingdom, primaryClan: Clan.PlayerClan, isPersonal: true);

            if (!playerIsRuler)
            {
                ApplyPlayerPolicyMandateDeviation(mandateAlignment);
            }

            return true;
        }

        private void ApplyPlayerPolicyMandateDeviation(PlayerPolicyMandateAlignment alignment)
        {
            int relationChange;
            float memoryYears;
            string memorySource;
            switch (alignment)
            {
                case PlayerPolicyMandateAlignment.OutsideAgenda:
                    relationChange = C.CourtPolicyMandateOutsideAgendaRelationPenalty;
                    memoryYears = C.CourtPolicyMandateOutsideAgendaMemoryYears;
                    memorySource = RelationMemorySources.PolicyMandateOutsideAgenda;
                    break;
                case PlayerPolicyMandateAlignment.OpposesOwnAgenda:
                case PlayerPolicyMandateAlignment.AdvancesRivalAgenda:
                    relationChange = C.CourtPolicyMandateBetrayalRelationPenalty;
                    memoryYears = C.CourtPolicyMandateBetrayalMemoryYears;
                    memorySource = RelationMemorySources.PolicyMandateBetrayal;
                    break;
                default:
                    return;
            }

            FactionObject playerFaction = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetIdeologicalFaction(Clan.PlayerClan);
            if (playerFaction == null || Hero.MainHero == null)
                return;

            foreach (Hero memberLeader in GetPolicyMandateRelationTargets(playerFaction))
            {
                RelationMemoryService.ApplyChange(
                    memberLeader,
                    Hero.MainHero,
                    relationChange,
                    true,
                    memorySource,
                    memoryYears,
                    RelationMemoryScope.Personal,
                    playerFaction.GetDisplayName()?.ToString());
            }

            BellumCivileLogger.Log($"Applied player policy mandate deviation relations; faction={playerFaction.Type}; alignment={alignment}; change={relationChange}; memory={memorySource}; years={memoryYears:0.#}.");
        }

        private static IEnumerable<Hero> GetPolicyMandateRelationTargets(FactionObject faction)
        {
            return faction?.Members?
                .Where(member => member != null
                    && member != Clan.PlayerClan
                    && !member.IsEliminated
                    && member.Leader != null
                    && !member.Leader.IsDead)
                .Select(member => member.Leader)
                .Distinct()
                ?? Enumerable.Empty<Hero>();
        }

        private static IEnumerable<FactionType> GetIdeologicalFactionTypes()
        {
            yield return FactionType.Glory;
            yield return FactionType.Nobility;
            yield return FactionType.Liberty;
        }

        public bool HasPlayerProposedVote(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            string prefix = KingdomPrefix(kingdom);
            return _pendingFactionLeaderClan.Any(kv =>
                kv.Key.StartsWith(prefix)
                && kv.Value == Clan.PlayerClan.StringId
                && _pendingVoteDate.ContainsKey(kv.Key));
        }

        public void ClearBribedVotesForPolicy(Kingdom kingdom, PolicyObject policy)
        {
            VotePledgeService.Invalidate();
            if (kingdom == null || policy == null) return;
            string prefix = kingdom.StringId + "|" + policy.StringId + "|";
            var toRemove = _bribedVoteOverrides.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in toRemove) _bribedVoteOverrides.Remove(k);
            var persuasionToRemove = _policyPersuasionFailed.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in persuasionToRemove) _policyPersuasionFailed.Remove(k);
        }

        private void ClearBribedVotesForPendingKey(string pendingKey)
        {
            VotePledgeService.Invalidate();
            if (string.IsNullOrEmpty(pendingKey)) return;
            string prefix = pendingKey + "|";
            var toRemove = _bribedVoteOverrides.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in toRemove) _bribedVoteOverrides.Remove(k);
            var persuasionToRemove = _policyPersuasionFailed.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in persuasionToRemove) _policyPersuasionFailed.Remove(k);
        }

        private void RemovePendingKey(string pendingKey)
        {
            _pendingPolicyAbolish.Remove(pendingKey);
            _pendingFactionLeaderClan.Remove(pendingKey);
            _pendingVoteDate.Remove(pendingKey);
            _pendingPolicyCreatedDay.Remove(pendingKey);
            _pendingPolicyRetryCount.Remove(pendingKey);
        }

        private void InitializePendingLifecycle(string pendingKey, bool resetStaleRepair)
        {
            _pendingPolicyCreatedDay[pendingKey] = DelayedVoteReliability.CurrentDay;
            _pendingPolicyRetryCount[pendingKey] = 0;
            if (resetStaleRepair)
                _policyStaleRepairCount[pendingKey] = 0;
        }

        private bool QueueRecoveredPolicyVote(Kingdom kingdom, PolicyObject policy, bool abolish, Clan proposer, CampaignTime originalVoteDate)
        {
            if (kingdom == null || policy == null || proposer == null)
                return false;

            string key = PendingKey(kingdom, policy);
            if (_pendingVoteDate.ContainsKey(key))
                return false;

            _pendingPolicyAbolish[key] = abolish ? 1 : 0;
            _pendingFactionLeaderClan[key] = proposer.StringId;
            _pendingVoteDate[key] = CourtAgendaBehavior.Current?.GetScheduledVoteDate(kingdom, policy, proposer) ?? originalVoteDate;
            InitializePendingLifecycle(key, resetStaleRepair: false);
            return true;
        }


        private void OnDailyTick()
        {
            foreach (Kingdom realm in Kingdom.All)
                if (!realm.IsEliminated && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm))
                    CleanupOrphanedPolicyDecisions(realm);

            foreach (string pendingKey in _pendingVoteDate.Keys.ToList())
            {
                if (!_pendingVoteDate.ContainsKey(pendingKey)) continue;
                int sep = pendingKey.IndexOf('|');
                if (sep < 0)
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                    continue;
                }

                string kingdomId = pendingKey.Substring(0, sep);
                string policyId  = pendingKey.Substring(sep + 1);

                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                PolicyObject policy = PolicyObject.All.FirstOrDefault(p => p.StringId == policyId);
                bool hasAbolish = _pendingPolicyAbolish.TryGetValue(pendingKey, out int abolishInt);
                bool hasLeader = _pendingFactionLeaderClan.TryGetValue(pendingKey, out string leaderClanId);
                Clan proposer = ResolveProposerClan(leaderClanId, kingdom);

                List<string> invalidReasons = new List<string>();
                if (kingdom == null) invalidReasons.Add("kingdom_missing");
                else if (kingdom.IsEliminated) invalidReasons.Add("kingdom_eliminated");
                else if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)) invalidReasons.Add("temporary_bellum_realm");
                if (policy == null) invalidReasons.Add("policy_missing");
                if (!hasAbolish) invalidReasons.Add("abolish_flag_missing");
                if (!hasLeader) invalidReasons.Add("leader_flag_missing");
                if (!IsValidProposer(proposer, kingdom)) invalidReasons.Add(GetInvalidProposerReason(proposer, kingdom));

                bool invalid = invalidReasons.Count > 0;

                if (!invalid)
                {
                    bool abolish = abolishInt == 1;
                    bool alreadyResolved = abolish
                        ? !kingdom.ActivePolicies.Contains(policy)
                        : kingdom.ActivePolicies.Contains(policy);
                    if (alreadyResolved)
                    {
                        invalidReasons.Add("policy_already_in_target_state");
                        invalid = true;
                    }
                }

                if (invalid)
                {
                    BellumCivileLogger.Log($"Removing invalid delayed policy vote {pendingKey}; kingdom={kingdom?.StringId ?? "null"} proposer={proposer?.StringId ?? leaderClanId ?? "null"} policy={policy?.StringId ?? "null"} reasons={string.Join(",", invalidReasons)}.");
                    CourtAgendaBehavior.Current?.CancelFiledMotion(kingdom, policyId, leaderClanId,
                        PolicyRefundRules.IsTechnical(invalidReasons) ? invalidReasons[0] : "political_queue_invalidation");
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                    continue;
                }

                if (CourtAgendaBehavior.Current?.TryWithdrawChangedPolicyMotion(kingdom, policy, proposer) == true)
                    continue;

                DelayedVoteReliability.EnsureLifecycle(
                    pendingKey,
                    _pendingVoteDate[pendingKey],
                    _pendingPolicyCreatedDay,
                    _pendingPolicyRetryCount);

                if (!_pendingVoteDate[pendingKey].IsPast) continue;

                if (kingdom.UnresolvedDecisions.OfType<KingdomPolicyDecision>().Any())
                {
                    if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingPolicyCreatedDay, _pendingPolicyRetryCount))
                    {
                        BellumCivileLogger.Log($"Expired policy deliberation {pendingKey} while waiting for another unresolved vote.");
                        CourtAgendaBehavior.Current?.CancelFiledMotion(kingdom, policyId, leaderClanId, "blocked_vote_timeout");
                        ClearBribedVotesForPendingKey(pendingKey);
                        RemovePendingKey(pendingKey);
                    }
                    continue;
                }

                KingdomPolicyDecision decision = new KingdomPolicyDecision(proposer, policy, abolishInt == 1);
                if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingPolicyCreatedDay, _pendingPolicyRetryCount))
                {
                    FinalizeExpiredPolicyVote(pendingKey, kingdom, policy, decision, "pending_age_or_retry_limit");
                    continue;
                }

                bool isAllowed;
                try
                {
                    isAllowed = decision.IsAllowed();
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed policy vote {pendingKey} threw while checking IsAllowed(); error={ex.GetType().Name}:{ex.Message}.");
                    RegisterPolicyFailure(pendingKey, kingdom, policy, decision, "is_allowed_exception");
                    continue;
                }

                if (!isAllowed)
                {
                    BellumCivileLogger.Log($"Delayed policy vote {pendingKey} is not currently allowed by Bannerlord.");
                    RegisterPolicyFailure(pendingKey, kingdom, policy, decision, "not_allowed");
                    continue;
                }

                BellumCivileLogger.Log($"Attempting to fire delayed policy vote {pendingKey}.");
                try
                {
                    IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed policy vote {pendingKey} threw while entering unresolved decisions; error={ex.GetType().Name}:{ex.Message}.");
                    RegisterPolicyFailure(pendingKey, kingdom, policy, decision, "add_decision_exception");
                    continue;
                }

                if (CourtAgendaBehavior.Current?.WasMotionCancelled(kingdom, policy, proposer) == true)
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                }
                else if (WasPolicyDecisionQueued(kingdom, policy, proposer)
                    || CourtAgendaBehavior.Current?.HasConcluded(kingdom, policy, proposer) == true)
                {
                    BellumCivileLogger.Log($"Delayed policy vote {pendingKey} successfully entered unresolved decisions.");
                    RemovePendingKey(pendingKey);
                }
                else
                {
                    BellumCivileLogger.Log($"Failed to queue delayed policy vote {pendingKey}; keeping it pending for retry.");
                    RegisterPolicyFailure(pendingKey, kingdom, policy, decision, "add_decision_failed");
                }
            }
        }

        private void RegisterPolicyFailure(
            string pendingKey,
            Kingdom kingdom,
            PolicyObject policy,
            KingdomPolicyDecision decision,
            string reason)
        {
            if (CourtAgendaBehavior.Current?.WasMotionCancelled(kingdom, policy, decision?.ProposerClan) == true)
            {
                ClearBribedVotesForPendingKey(pendingKey);
                RemovePendingKey(pendingKey);
                return;
            }
            DelayedVoteReliability.RegisterFailure(pendingKey, _pendingPolicyRetryCount);
            if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingPolicyCreatedDay, _pendingPolicyRetryCount))
                FinalizeExpiredPolicyVote(pendingKey, kingdom, policy, decision, reason);
        }

        private void FinalizeExpiredPolicyVote(
            string pendingKey,
            Kingdom kingdom,
            PolicyObject policy,
            KingdomPolicyDecision decision,
            string reason)
        {
            BellumCivileLogger.Log($"Delayed policy vote {pendingKey} exceeded reliability limits; reason={reason}. Attempting one final direct decision.");
            try
            {
                IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Final recovery attempt for policy vote {pendingKey} threw; error={ex.GetType().Name}:{ex.Message}.");
                reason = "add_decision_exception";
            }

            bool targetStateReached = decision != null && (ResolvePolicyAbolish(decision)
                ? !kingdom.ActivePolicies.Contains(policy)
                : kingdom.ActivePolicies.Contains(policy));
            bool recovered = WasPolicyDecisionQueued(kingdom, policy, null) || targetStateReached;
            BellumCivileLogger.Log(recovered
                ? $"Final recovery attempt restored policy vote {pendingKey}."
                : $"Final recovery attempt could not restore policy vote {pendingKey}; stale pending state was cleared.");
            if (!WasPolicyDecisionQueued(kingdom, policy, null))
            {
                ClearBribedVotesForPendingKey(pendingKey);
                CourtAgendaBehavior.Current?.CancelFiledMotion(kingdom, policy.StringId, decision?.ProposerClan?.StringId,
                    targetStateReached ? "policy_already_in_target_state" : reason);
            }
            RemovePendingKey(pendingKey);
        }

        private void CleanupOrphanedPolicyDecisions(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return;

            if (!HasPotentiallyRepairablePolicyDecisions(kingdom))
                return;

            List<KingdomDecision> decisionsToRemove = new List<KingdomDecision>();

            List<KingdomPolicyDecision> policyDecisions = kingdom.UnresolvedDecisions
                .OfType<KingdomPolicyDecision>()
                .ToList();

            List<KingdomPolicyDecision> stalePolicyDecisions = policyDecisions
                .Where(DelayedVoteReliability.IsLiveDecisionStale)
                .ToList();

            decisionsToRemove.AddRange(policyDecisions
                .Where(decision => IsOrphanedPolicyDecision(kingdom, decision)));
            decisionsToRemove.AddRange(stalePolicyDecisions);

            foreach (var duplicateGroup in policyDecisions
                .Where(decision => decision?.Policy != null)
                .GroupBy(decision => decision.Policy.StringId)
                .Where(group => group.Count() > 1))
            {
                KingdomPolicyDecision newest = duplicateGroup
                    .OrderByDescending(decision => decision.TriggerTime.ToDays)
                    .First();

                decisionsToRemove.AddRange(duplicateGroup.Where(decision => decision != newest));
            }

            foreach (KingdomDecision decision in decisionsToRemove.Distinct().ToList())
            {
                kingdom.RemoveDecision(decision);
                if (decision is KingdomPolicyDecision removed && IsOrphanedPolicyDecision(kingdom, removed))
                    CourtAgendaBehavior.Current?.CancelFiledMotion(kingdom, removed.Policy?.StringId, removed.ProposerClan?.StringId,
                        removed.Policy == null ? "policy_missing" : "orphaned_vote_political_invalidation");
                BellumCivileLogger.Log($"Removed orphaned, duplicate, or stale policy decision from kingdom {kingdom.StringId}; age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} days.");
            }

            foreach (KingdomPolicyDecision staleDecision in stalePolicyDecisions)
            {
                PolicyObject policy = staleDecision?.Policy;
                Clan proposer = ResolveProposerClan(staleDecision?.ProposerClan?.StringId, kingdom);
                if (policy == null || !IsValidProposer(proposer, kingdom))
                    continue;

                bool abolish = ResolvePolicyAbolish(staleDecision);
                bool alreadyResolved = abolish
                    ? !kingdom.ActivePolicies.Contains(policy)
                    : kingdom.ActivePolicies.Contains(policy);
                string key = PendingKey(kingdom, policy);
                if (alreadyResolved || _pendingVoteDate.ContainsKey(key))
                    continue;

                int repairCount = _policyStaleRepairCount.TryGetValue(key, out int stored) ? stored : 0;
                if (repairCount >= 1)
                {
                    BellumCivileLogger.Log($"Stale live policy decision {key} already exhausted its one rebuild allowance; motion withdrawn.");
                    CourtAgendaBehavior.Current?.CancelFiledMotion(kingdom, policy.StringId, proposer.StringId, "stale_vote_timeout");
                    continue;
                }

                _policyStaleRepairCount[key] = repairCount + 1;
                if (QueueRecoveredPolicyVote(kingdom, policy, abolish, proposer, staleDecision.TriggerTime))
                    BellumCivileLogger.Log($"Rebuilt stale live policy decision {key} as a fresh delayed vote.");
            }
        }

        private static bool HasPotentiallyRepairablePolicyDecisions(Kingdom kingdom)
        {
            return kingdom != null
                && kingdom.UnresolvedDecisions.OfType<KingdomPolicyDecision>().Any();
        }

        private static bool IsOrphanedPolicyDecision(Kingdom expectedKingdom, KingdomPolicyDecision decision)
        {
            if (expectedKingdom == null || decision == null)
                return true;

            Kingdom decisionKingdom = decision.Kingdom;
            if (decisionKingdom == null || decisionKingdom != expectedKingdom || decisionKingdom.IsEliminated)
                return true;

            if (decision.Policy == null)
                return true;

            if (decision.ProposerClan != null &&
                (decision.ProposerClan.IsEliminated
                 || (decision.ProposerClan.IsMinorFaction && decision.ProposerClan != Clan.PlayerClan)
                 || decision.ProposerClan.IsUnderMercenaryService
                 || decision.ProposerClan.Kingdom != decisionKingdom))
            {
                return true;
            }

            return false;
        }

        private static bool WasPolicyDecisionQueued(Kingdom kingdom, PolicyObject policy, Clan proposer)
        {
            if (kingdom == null || policy == null)
                return false;

            return kingdom.UnresolvedDecisions
                .OfType<KingdomPolicyDecision>()
                .Any(decision =>
                    decision?.Policy == policy &&
                    (proposer == null || decision.ProposerClan == proposer));
        }

        internal static bool ResolvePolicyAbolish(KingdomPolicyDecision decision)
        {
            if (decision == null)
                return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo field = typeof(KingdomPolicyDecision).GetField("_isInvertedDecision", flags)
                ?? typeof(KingdomPolicyDecision).GetField("IsInvertedDecision", flags);
            if (field?.GetValue(decision) is bool fieldValue)
                return fieldValue;

            PropertyInfo property = typeof(KingdomPolicyDecision).GetProperty("IsInvertedDecision", flags)
                ?? typeof(KingdomPolicyDecision).GetProperty("IsInverted", flags);
            if (property?.GetValue(decision) is bool propertyValue)
                return propertyValue;

            return decision.Kingdom?.ActivePolicies.Contains(decision.Policy) == true;
        }

        public int RunReliabilityRepair(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return 0;

            int before = kingdom.UnresolvedDecisions.Count;
            CleanupOrphanedPolicyDecisions(kingdom);
            int changes = Math.Max(0, before - kingdom.UnresolvedDecisions.Count);

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingVoteDate.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingVoteDate[key],
                    _pendingPolicyCreatedDay,
                    _pendingPolicyRetryCount);
            }

            return changes;
        }

        public IEnumerable<string> GetReliabilityDiagnostics(Kingdom kingdom)
        {
            if (kingdom == null)
                yield break;

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingVoteDate.Keys.Where(k => k.StartsWith(prefix)).OrderBy(k => k))
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingVoteDate[key],
                    _pendingPolicyCreatedDay,
                    _pendingPolicyRetryCount);
                bool dialogueReady = TryResolvePendingPolicyForConversation(kingdom, key, out _, out _);
                yield return $"policy pending={key} due={_pendingVoteDate[key].ToDays:0.0} age={DelayedVoteReliability.GetPendingAgeDays(key, _pendingPolicyCreatedDay):0.0} retries={DelayedVoteReliability.GetFailureCount(key, _pendingPolicyRetryCount)} dialogue_ready={dialogueReady}";
            }

            foreach (KingdomPolicyDecision decision in kingdom.UnresolvedDecisions.OfType<KingdomPolicyDecision>())
            {
                yield return $"policy live={decision.Policy?.StringId ?? "null"} abolish={ResolvePolicyAbolish(decision)} age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} stale={DelayedVoteReliability.IsLiveDecisionStale(decision)}";
            }
        }

        private static Clan ResolveProposerClan(string leaderClanId, Kingdom kingdom)
        {
            if (!string.IsNullOrEmpty(leaderClanId) && Clan.PlayerClan != null && Clan.PlayerClan.StringId == leaderClanId)
                return Clan.PlayerClan;

            Clan proposer = !string.IsNullOrEmpty(leaderClanId)
                ? Clan.All.FirstOrDefault(c => c.StringId == leaderClanId)
                : null;

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
                "policy_deliberation_topic",
                "lord_talk_speak_diplomacy_2",
                "policy_deliberation_topic_response",
                "{=BC_PolicyDelib_Topic}About the ongoing motions in court...",
                PolicyMenuEntryCondition,
                BeginPolicyPendingSelection,
                100, null, null);

            starter.AddDialogLine(
                "policy_deliberation_choose_motion",
                "policy_deliberation_topic_response",
                "policy_deliberation_topic_choices",
                "{=BC_PolicyDelib_ChooseMotion}Which motion did you wish to discuss?",
                null,
                null,
                100, null);

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                starter.AddPlayerLine(
                    $"policy_query_slot_{slot}",
                    "policy_deliberation_topic_choices",
                    "policy_query_response",
                    SlotQueryText(slot),
                    () => PolicyMenuSlotCondition(slot),
                    () => PolicyMenuSlotAction(slot),
                    100 - slot, null, null);
            }

            starter.AddPlayerLine(
                "policy_deliberation_more",
                "policy_deliberation_topic_choices",
                "policy_deliberation_topic_response",
                "{=BC_Deliberation_MoreMatters}There are other matters I would like to discuss.",
                PolicyPendingSelectionHasMore,
                AdvancePolicyPendingSelectionPage,
                60, null, null);

            starter.AddPlayerLine(
                "policy_deliberation_back",
                "policy_deliberation_topic_choices",
                "lord_pretalk",
                "{=BC_Deliberation_Back}Never mind. Let us speak of something else.",
                null,
                ClearPolicyPendingSelection,
                50, null, null);

            starter.AddDialogLine("policy_query_not_clan_leader", "policy_query_response", "hero_main_options",
                "{=BC_PolicyDelib_NotClanLeader}Such matters of court are not mine to decide. I speak only for myself, not for the {CLAN_NAME}. If it is politics you wish to discuss, you must seek out {CLAN_LEADER}, the head of our family.",
                VoteConversationIsNotClanLeader,
                null,
                200, null);

            starter.AddDialogLine("policy_query_already_bribed", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_AlreadyBribed}We have already reached an arrangement on this matter. My vote on this question is decided.",
                () => _currentQueryAlreadyBribed, null, 120, null);

            starter.AddDialogLine("policy_query_strongly_support", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_StrongSupport}I firmly believe this is the right course for the realm. Unless the world turns upside down, I shall vote in its favor.",
                () => _queriedScore > 100f, null, 110, null);

            starter.AddDialogLine("policy_query_agree", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_Agree}I lean in favor of this proposal. Unless circumstances change markedly, you may count on my support.",
                () => _queriedScore > 10f, null, 100, null);

            starter.AddDialogLine("policy_query_neutral", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_Neutral}I have not yet reached a firm conclusion on the matter. My vote could go either way depending on what transpires.",
                () => _queriedScore >= -10f, null, 90, null);

            starter.AddDialogLine("policy_query_disagree", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_Disagree}I have reservations about this proposal. I am inclined to vote against it, though I remain open to persuasion.",
                () => _queriedScore >= -100f, null, 80, null);

            starter.AddDialogLine("policy_query_strongly_oppose", "policy_query_response", "policy_query_player_choice",
                "{=BC_PolicyDelib_StrongOppose}I will vote against this motion. It runs contrary to everything my family stands for.",
                null, null, 70, null);

            starter.AddPlayerLine("policy_query_persuade_support", "policy_query_player_choice", "policy_persuasion_response",
                "{=BC_PolicyDelib_PersuadeSupportStart}Perhaps I can convince you to support this motion.",
                () => _queriedScore < C.PolicyBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToSupport = true; StartPolicyVotePersuasion(); },
                120, PolicyPersuasionClickableCondition, null);

            starter.AddPlayerLine("policy_query_persuade_oppose", "policy_query_player_choice", "policy_persuasion_response",
                "{=BC_PolicyDelib_PersuadeOpposeStart}Perhaps I can convince you to oppose this motion.",
                () => _queriedScore > -C.PolicyBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToSupport = false; StartPolicyVotePersuasion(); },
                119, PolicyPersuasionClickableCondition, null);

            starter.AddPlayerLine("policy_query_sway_support", "policy_query_player_choice", "policy_sway_response",
                "{=BC_PolicyDelib_SwaySupport}I would be... grateful if you were to vote in favor of this motion.",
                () => _queriedScore < C.PolicyBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToSupport = true; },
                100, PolicyBribeClickableCondition, null);

            starter.AddPlayerLine("policy_query_sway_oppose", "policy_query_player_choice", "policy_sway_response",
                "{=BC_PolicyDelib_SwayOppose}I would be... grateful if you were to vote against this motion.",
                () => _queriedScore > -C.PolicyBribeSameDirectionThreshold && !_currentQueryAlreadyBribed,
                () => { _swayToSupport = false; },
                100, PolicyBribeClickableCondition, null);

            starter.AddPlayerLine("policy_query_end", "policy_query_player_choice", "lord_pretalk",
                "{=BC_PolicyDelib_EndQuery}I will keep that in mind. Thank you, my lord.",
                null, null, 90, null, null);

            starter.AddDialogLine("policy_sway_npc_response", "policy_sway_response", "policy_sway_barter",
                "{=BC_PolicyDelib_SwayResponse}An interesting proposition. I am prepared to hear what you are offering.",
                null, null, 100, null);

            starter.AddPlayerLine("policy_sway_barter_start", "policy_sway_barter", "lord_pretalk",
                "{=BC_PolicyDelib_DiscussTerms}Let us discuss terms.",
                null, LaunchPolicyBribeBarter, 100, null, null);

            starter.AddDialogLine("policy_persuasion_npc_response", "policy_persuasion_response", "policy_persuasion_arguments",
                "{=BC_PolicyDelib_PersuasionResponse}You may try. What argument do you offer?",
                null, null, 100, null);

            starter.AddPlayerLine("policy_persuasion_honor", "policy_persuasion_arguments", "policy_persuasion_result",
                "{=BC_PolicyDelib_PersuadeHonor}A just realm must stand by its principles, even when it is inconvenient.",
                null, () => BlockPolicyPersuasionOption("honor"), 120,
                (out TextObject hintText) => PolicyPersuasionOptionClickable("honor", out hintText),
                () => BuildPolicyPersuasionOption("honor"));

            starter.AddPlayerLine("policy_persuasion_realm", "policy_persuasion_arguments", "policy_persuasion_result",
                "{=BC_PolicyDelib_PersuadeRealm}Set aside factional pride. This is what the realm needs.",
                null, () => BlockPolicyPersuasionOption("realm"), 110,
                (out TextObject hintText) => PolicyPersuasionOptionClickable("realm", out hintText),
                () => BuildPolicyPersuasionOption("realm"));

            starter.AddPlayerLine("policy_persuasion_strategy", "policy_persuasion_arguments", "policy_persuasion_result",
                "{=BC_PolicyDelib_PersuadeStrategy}Look at the balance of power. This motion strengthens our position.",
                null, () => BlockPolicyPersuasionOption("strategy"), 100,
                (out TextObject hintText) => PolicyPersuasionOptionClickable("strategy", out hintText),
                () => BuildPolicyPersuasionOption("strategy"));

            starter.AddPlayerLine("policy_persuasion_courage", "policy_persuasion_arguments", "policy_persuasion_result",
                "{=BC_PolicyDelib_PersuadeCourage}If you believe this is right, then have the courage to stand for it.",
                null, () => BlockPolicyPersuasionOption("courage"), 90,
                (out TextObject hintText) => PolicyPersuasionOptionClickable("courage", out hintText),
                () => BuildPolicyPersuasionOption("courage"));

            starter.AddDialogLine("policy_persuasion_success", "policy_persuasion_result", "lord_pretalk",
                "{=BC_PolicyDelib_PersuasionSuccess}Very well. I will cast my vote as you ask.",
                PolicyPersuasionSucceededCondition,
                ApplyPolicyPersuasionSuccess,
                120, null);

            starter.AddDialogLine("policy_persuasion_failed", "policy_persuasion_result", "lord_pretalk",
                "{=BC_PolicyDelib_PersuasionFailed}No. I have heard enough, and my judgment remains unchanged.",
                PolicyPersuasionFailedCondition,
                ApplyPolicyPersuasionFailure,
                110, null);

            starter.AddDialogLine("policy_persuasion_continue", "policy_persuasion_result", "policy_persuasion_arguments",
                "{=BC_PolicyDelib_PersuasionContinue}You have not convinced me yet. What else can you say?",
                PolicyPersuasionCanContinueCondition,
                null, 100, null);
        }

        // -------------------- Dialogue helpers --------------------

        private static string SlotQueryText(int slot) =>
            slot == 0 ? "{=BC_PolicyDelib_DirectQuestion0}What do you think about the motion to {PD0A} {PD0P}, put forth by the {PD0F}?"
          : slot == 1 ? "{=BC_PolicyDelib_DirectQuestion1}What do you think about the motion to {PD1A} {PD1P}, put forth by the {PD1F}?"
          : slot == 2 ? "{=BC_PolicyDelib_DirectQuestion2}What do you think about the motion to {PD2A} {PD2P}, put forth by the {PD2F}?"
                      : "{=BC_PolicyDelib_DirectQuestion3}What do you think about the motion to {PD3A} {PD3P}, put forth by the {PD3F}?";

        private bool PolicyMenuEntryCondition()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return false;
            if (npc.Clan == Clan.PlayerClan) return false;
            if (Clan.PlayerClan.Kingdom == null) return false;
            if (npc.Clan.Kingdom != Clan.PlayerClan.Kingdom) return false;
            RefreshPolicyConversationPendingKeys();
            return _conversationPendingKeys.Count > 0;
        }

        private void BeginPolicyPendingSelection()
        {
            _conversationPendingPage = 0;
            RefreshPolicyConversationPendingKeys();
        }

        private void ClearPolicyPendingSelection()
        {
            _conversationPendingPage = 0;
            _conversationPendingKeys.Clear();
        }

        private void RefreshPolicyConversationPendingKeys()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null)
            {
                _conversationPendingKeys.Clear();
                _conversationPendingPage = 0;
                return;
            }

            string prefix = KingdomPrefix(kingdom);
            _conversationPendingKeys = _pendingVoteDate
                .Where(kv => kv.Key.StartsWith(prefix) && TryResolvePendingPolicyForConversation(kingdom, kv.Key, out _, out _))
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            int maxPage = Math.Max(0, (_conversationPendingKeys.Count - 1) / DeliberationDialogueHelper.PageSize);
            _conversationPendingPage = Math.Min(_conversationPendingPage, maxPage);
        }

        private bool TryResolvePendingPolicyForConversation(
            Kingdom kingdom,
            string pendingKey,
            out PolicyObject policy,
            out bool abolish)
        {
            policy = null;
            abolish = false;
            if (kingdom == null || string.IsNullOrEmpty(pendingKey)) return false;

            string prefix = KingdomPrefix(kingdom);
            if (!pendingKey.StartsWith(prefix, StringComparison.Ordinal)) return false;

            string policyId = pendingKey.Substring(prefix.Length);
            policy = PolicyObject.All.FirstOrDefault(p => p.StringId == policyId);
            if (policy == null || !_pendingPolicyAbolish.TryGetValue(pendingKey, out int abolishValue)) return false;

            abolish = abolishValue == 1;
            bool isActive = kingdom.ActivePolicies.Contains(policy);
            return abolish ? isActive : !isActive;
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

        private bool PolicyMenuSlotCondition(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return false;

            string pendingKey = _conversationPendingKeys[index];
            if (!TryResolvePendingPolicyForConversation(Clan.PlayerClan?.Kingdom, pendingKey, out PolicyObject policy, out bool abolish)) return false;

            var    factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            string leaderClanId   = _pendingFactionLeaderClan.TryGetValue(pendingKey, out string id) ? id : "";
            Clan   leaderClan     = Clan.All.FirstOrDefault(c => c.StringId == leaderClanId);
            var    factionObj     = factionManager?.GetIdeologicalFaction(leaderClan);
            string factionName    = factionObj?.GetDisplayName()?.ToString()
                ?? new TextObject("{=BC_PolicyDelib_UnknownFaction}the faction").ToString();

            MBTextManager.SetTextVariable($"PD{slot}F", factionName);
            MBTextManager.SetTextVariable($"PD{slot}A",
                new TextObject(abolish ? "{=BC_Ideology_Repeal}repeal" : "{=BC_Ideology_Enact}enact"));
            MBTextManager.SetTextVariable($"PD{slot}P", policy.Name);

            return true;
        }

        private void PolicyMenuSlotAction(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return;

            string pendingKey = _conversationPendingKeys[index];
            if (!TryResolvePendingPolicyForConversation(Clan.PlayerClan?.Kingdom, pendingKey, out PolicyObject policy, out bool abolish)) return;

            _currentQueryPolicy     = policy;
            _currentQueryPendingKey = pendingKey;

            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            string leaderClanId = _pendingFactionLeaderClan.TryGetValue(pendingKey, out string id) ? id : "";
            Clan   leaderClan   = Clan.All.FirstOrDefault(c => c.StringId == leaderClanId);
            Clan   proposerClan = leaderClan ?? Clan.PlayerClan.Kingdom?.RulingClan;
            if (proposerClan == null) return;

            KingdomPolicyDecision tempDecision = null;
            try { tempDecision = new KingdomPolicyDecision(proposerClan, policy, abolish); }
            catch { return; }

            _queriedScore = PolicyVoteAIPatch.CalculateSupportScore(
                tempDecision, npc.Clan, !abolish, factionManager);

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            _currentQueryAlreadyBribed = kingdom != null
                && _bribedVoteOverrides.ContainsKey(BribeKey(kingdom, policy, npc.Clan));
        }

        private bool PolicyPendingSelectionHasMore() =>
            ((_conversationPendingPage + 1) * DeliberationDialogueHelper.PageSize) < _conversationPendingKeys.Count;

        private void AdvancePolicyPendingSelectionPage()
        {
            _conversationPendingPage++;
            RefreshPolicyConversationPendingKeys();
        }

        private bool PolicyPersuasionClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan == null || kingdom == null || _currentQueryPolicy == null)
            {
                explanation = new TextObject("{=BC_PolicyDelib_PersuasionUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(npc.Clan, kingdom, VotePledgeService.PolicyKey(kingdom, _currentQueryPolicy, npc.Clan), out explanation))
                return false;

            string key = BribeKey(kingdom, _currentQueryPolicy, npc.Clan);
            if (_policyPersuasionFailed.ContainsKey(key))
            {
                explanation = new TextObject("{=BC_PolicyDelib_PersuasionAlreadyFailed}They have already rejected your argument in this matter.");
                return false;
            }

            int relation = Hero.MainHero?.GetRelation(npc.Clan.Leader) ?? 0;
            if (relation < 30)
            {
                explanation = new TextObject("{=BC_PolicyDelib_PersuasionLowRelation}They do not trust you enough to be swayed by argument. Relation required: 30.");
                return false;
            }

            return true;
        }

        private bool PolicyBribeClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan == null || kingdom == null || _currentQueryPolicy == null)
            {
                explanation = new TextObject("{=BC_PolicyDelib_BribeUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(npc.Clan, kingdom, VotePledgeService.PolicyKey(kingdom, _currentQueryPolicy, npc.Clan), out explanation))
                return false;

            if (_currentQueryAlreadyBribed)
            {
                explanation = new TextObject("{=BC_PolicyDelib_BribeAlreadyCommitted}They have already committed their vote in this matter.");
                return false;
            }

            float desiredDirectionScore = _swayToSupport ? _queriedScore : -_queriedScore;
            if (desiredDirectionScore >= C.PolicyBribeSameDirectionThreshold)
                return true;

            float openness = CalculatePolicyBribeOpenness(npc.Clan, desiredDirectionScore);
            if (openness >= C.FiefBribeOpennessThreshold)
                return true;

            int honor = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Honor);
            int mercy = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Mercy);
            int generosity = npc.Clan.Leader.GetTraitLevel(DefaultTraits.Generosity);

            if (honor >= 1)
                explanation = new TextObject("{=BC_PolicyDelib_BribeHonorRefusal}They consider this vote a matter of honor and will not bargain over it.");
            else if (mercy >= 1 || generosity >= 1)
                explanation = new TextObject("{=BC_PolicyDelib_BribePrincipledRefusal}They are not inclined to trade favors over the laws of the realm.");
            else if (desiredDirectionScore <= -100f)
                explanation = new TextObject("{=BC_PolicyDelib_BribeCommittedRefusal}They are too committed to their current stance to discuss changing sides.");
            else
                explanation = new TextObject("{=BC_PolicyDelib_BribeRefusal}They are not willing to bargain over this vote.");

            return false;
        }

        private float CalculatePolicyBribeOpenness(Clan voter, float desiredDirectionScore)
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

            openness += MathF.Clamp(desiredDirectionScore, -150f, 100f) * 0.25f;

            return openness;
        }

        private void StartPolicyVotePersuasion()
        {
            _policyPersuasionOptions.Clear();
            ConversationManager.StartPersuasion(
                goalValue: C.FiefPersuasionGoal,
                successValue: C.FiefPersuasionSuccessValue,
                failValue: 0f,
                criticalSuccessValue: C.FiefPersuasionCriticalSuccessValue,
                criticalFailValue: C.FiefPersuasionCriticalFailValue,
                initialProgress: 0f,
                difficulty: PersuasionDifficulty.Medium);
        }

        private PersuasionOptionArgs BuildPolicyPersuasionOption(string argumentType)
        {
            if (_policyPersuasionOptions.TryGetValue(argumentType, out PersuasionOptionArgs cachedOption))
                return cachedOption;

            Hero npc = Hero.OneToOneConversationHero;
            Hero voter = npc?.Clan?.Leader;

            TraitObject trait = DefaultTraits.Honor;
            TraitEffect effect = TraitEffect.Positive;
            PersuasionArgumentStrength strength = PersuasionArgumentStrength.Normal;
            TextObject line = new TextObject("{=BC_PolicyDelib_PersuadeRealm}Set aside factional pride. This is what the realm needs.");
            Tuple<TraitObject, int>[] correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };

            if (argumentType == "honor")
            {
                trait = DefaultTraits.Honor;
                line = new TextObject("{=BC_PolicyDelib_PersuadeHonor}A just realm must stand by its principles, even when it is inconvenient.");
                correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Honor);
            }
            else if (argumentType == "realm")
            {
                trait = DefaultTraits.Mercy;
                line = new TextObject("{=BC_PolicyDelib_PersuadeRealm}Set aside factional pride. This is what the realm needs.");
                correlations = new[] { Tuple.Create(DefaultTraits.Mercy, 1), Tuple.Create(DefaultTraits.Generosity, 1) };
                strength = StrengthFromBestTrait(voter, DefaultTraits.Mercy, DefaultTraits.Generosity);
            }
            else if (argumentType == "strategy")
            {
                trait = DefaultTraits.Calculating;
                line = new TextObject("{=BC_PolicyDelib_PersuadeStrategy}Look at the balance of power. This motion strengthens our position.");
                correlations = new[] { Tuple.Create(DefaultTraits.Calculating, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Calculating);
            }
            else if (argumentType == "courage")
            {
                trait = DefaultTraits.Valor;
                line = new TextObject("{=BC_PolicyDelib_PersuadeCourage}If you believe this is right, then have the courage to stand for it.");
                correlations = new[] { Tuple.Create(DefaultTraits.Valor, 1), Tuple.Create(DefaultTraits.Honor, 1) };
                strength = StrengthFromTrait(voter, DefaultTraits.Valor);
            }

            strength = AdjustPolicyPersuasionStrengthForResistance(strength);

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

            _policyPersuasionOptions[argumentType] = option;
            return option;
        }

        private void BlockPolicyPersuasionOption(string argumentType)
        {
            BuildPolicyPersuasionOption(argumentType)?.BlockTheOption(true);
        }

        private bool PolicyPersuasionOptionClickable(string argumentType, out TextObject hintText)
        {
            PersuasionOptionArgs option = BuildPolicyPersuasionOption(argumentType);
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

        private PersuasionArgumentStrength AdjustPolicyPersuasionStrengthForResistance(PersuasionArgumentStrength baseStrength)
        {
            float desiredDirectionScore = _swayToSupport ? _queriedScore : -_queriedScore;
            if (desiredDirectionScore <= -100f)
                return MakePersuasionHarder(MakePersuasionHarder(baseStrength));
            if (desiredDirectionScore <= -10f)
                return MakePersuasionHarder(baseStrength);
            if (desiredDirectionScore >= 10f)
                return MakePersuasionEasier(baseStrength);

            return baseStrength;
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

        private bool PolicyPersuasionSucceededCondition()
        {
            return ConversationManager.GetPersuasionProgressSatisfied();
        }

        private bool PolicyPersuasionFailedCondition()
        {
            return ConversationManager.GetPersuasionIsFailure()
                || PolicyPersuasionLastRollWasCriticalFailure()
                || PolicyPersuasionArgumentsExhausted();
        }

        private bool PolicyPersuasionCanContinueCondition()
        {
            return !PolicyPersuasionSucceededCondition()
                && !ConversationManager.GetPersuasionIsFailure()
                && !PolicyPersuasionArgumentsExhausted();
        }

        private bool PolicyPersuasionArgumentsExhausted()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            int chosenCount = ConversationManager.GetPersuasionChosenOptions()?.Count() ?? 0;
            return chosenCount >= 4 && !ConversationManager.GetPersuasionProgressSatisfied();
        }

        private bool PolicyPersuasionLastRollWasCriticalFailure()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            Tuple<PersuasionOptionArgs, PersuasionOptionResult> lastChoice =
                ConversationManager.GetPersuasionChosenOptions()?.LastOrDefault();

            return lastChoice != null && lastChoice.Item2 == PersuasionOptionResult.CriticalFailure;
        }

        private void ApplyPolicyPersuasionSuccess()
        {
            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan != null && kingdom != null && _currentQueryPolicy != null)
            {
                int forcedScore = _swayToSupport ? C.PolicyBribeForcedSupportScore : -C.PolicyBribeForcedSupportScore;
                VotePledgeService.TryCommit(npc.Clan, kingdom, VotePledgeService.PolicyKey(kingdom, _currentQueryPolicy, npc.Clan),
                    () => SetBribedVote(kingdom, _currentQueryPolicy, npc.Clan, forcedScore));
            }

            ConversationManager.EndPersuasion();
            _policyPersuasionOptions.Clear();
        }

        private void ApplyPolicyPersuasionFailure()
        {
            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan != null && kingdom != null && _currentQueryPolicy != null)
                _policyPersuasionFailed[BribeKey(kingdom, _currentQueryPolicy, npc.Clan)] = true;

            ConversationManager.EndPersuasion();
            _policyPersuasionOptions.Clear();
        }

        private void LaunchPolicyBribeBarter()
        {
            Hero    target  = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (target?.Clan == null || kingdom == null || _currentQueryPolicy == null) return;

            var bribeItem = new PolicyVoteBribeBarterable(
                target.Clan, kingdom, _currentQueryPolicy, _swayToSupport, _queriedScore, Hero.MainHero);

            BarterManager.Instance.StartBarterOffer(
                Hero.MainHero,
                target,
                PartyBase.MainParty,
                target.PartyBelongedTo?.Party,
                null,
                (Barterable barterable, BarterData args, object obj) =>
                {
                    args.AddBarterable<PolicyVoteBribeBarterable>(bribeItem);

                    foreach (Settlement s in Hero.MainHero.Clan.Settlements)
                        if (s.IsTown || s.IsCastle)
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(s, Hero.MainHero, target));

                    return true;
                },
                0,
                false,
                new Barterable[] { bribeItem });
        }
    }
}
