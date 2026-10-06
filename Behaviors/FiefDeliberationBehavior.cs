using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
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
    /// To insert a 5-day deliberation window between a fief capture (or player gift-to-kingdom) and
    /// the SettlementClaimantDecision actually firing. BlockVanillaAnnexPatch already stops ConsiderAnnex
    /// from creating the vote immediately; this behavior subscribes to the same openToClaim=true signal
    /// and queues the vote for 5 days later. During that window the player can talk to clan leaders to
    /// learn which faction they naturally favour for the fief, then bribe them (via FiefVoteBribeBarterable)
    /// to vote for a specific ideological faction's candidate instead. Multiple concurrent captures are
    /// each queued separately; only one vote fires at a time (the guard in OnDailyTick waits if a
    /// SettlementClaimantDecision or SettlementClaimantPreliminaryDecision is already unresolved).
    /// AI kingdoms are unaffected; their fief redistribution still flows through the Fief Ambition system.
    /// </summary>
    public class FiefDeliberationBehavior : CampaignBehaviorBase
    {
        internal sealed class FiefNominationRankingEntry
        {
            public string CandidateId { get; set; }
            public int Count { get; set; }
            public float Score { get; set; }
        }

        public static FiefDeliberationBehavior Current =>
            Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();

        private Dictionary<string, string>       _pendingFiefProposer  = new Dictionary<string, string>();
        private Dictionary<string, string>       _pendingFiefCapturer  = new Dictionary<string, string>();
        private Dictionary<string, string>       _pendingFiefParticipants = new Dictionary<string, string>();
        private Dictionary<string, string>       _pendingFiefExclude   = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _pendingFiefDate      = new Dictionary<string, CampaignTime>();
        private Dictionary<string, float>        _pendingFiefCreatedDay = new Dictionary<string, float>();
        private Dictionary<string, int>          _pendingFiefRetryCount = new Dictionary<string, int>();
        private Dictionary<string, int>          _fiefStaleRepairCount = new Dictionary<string, int>();
        private Dictionary<string, string>       _pendingFirstRightCapturer = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _pendingFirstRightDate = new Dictionary<string, CampaignTime>();

        private Dictionary<string, int> _fiefBribedVotes = new Dictionary<string, int>();
        private Dictionary<string, string> _fiefBribedCandidateVotes = new Dictionary<string, string>();
        private Dictionary<string, string> _fiefNomineeByVoter = new Dictionary<string, string>();
        private Dictionary<string, string> _fiefNominationReasons = new Dictionary<string, string>();
        private Dictionary<string, float>  _fiefNominationScores = new Dictionary<string, float>();
        private Dictionary<string, bool>   _fiefCommittedNominationKeys = new Dictionary<string, bool>();
        private Dictionary<string, bool>   _fiefSelfEncouragementUsed = new Dictionary<string, bool>();
        private Dictionary<string, bool>   _fiefPersuasionFailed = new Dictionary<string, bool>();
        private readonly Dictionary<string, PersuasionOptionArgs> _fiefPersuasionOptions = new Dictionary<string, PersuasionOptionArgs>();

        private int        _queriedFaction        = -1;
        private bool       _currentQueryAlreadyBribed;
        private Settlement _currentQuerySettlement;
        private string     _selectedBribeCandidateClanId = "";
        private Clan       _currentQueryNominee;
        private Clan       _currentQueryVoterClan;
        private List<Clan> _currentQueryCandidates = new List<Clan>();
        private int        _candidateSelectionPage;
        private List<string> _conversationPendingKeys = new List<string>();
        private int _conversationPendingPage;
        private readonly Dictionary<string, List<string>> _recentSiegeParticipantClanIdsBySettlement = new Dictionary<string, List<string>>();


        private static string PendingKey(Kingdom kingdom, Settlement settlement) =>
            kingdom.StringId + "|" + settlement.StringId;

        private static string KingdomPrefix(Kingdom kingdom) =>
            kingdom.StringId + "|";

        public static string BribeKey(Kingdom kingdom, Settlement settlement, Clan voter) =>
            kingdom.StringId + "|" + settlement.StringId + "|" + voter.StringId;


        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            VotePledgeService.Invalidate();
            dataStore.SyncData("BellumCivile_PendingFiefProposer", ref _pendingFiefProposer);
            dataStore.SyncData("BellumCivile_PendingFiefCapturer", ref _pendingFiefCapturer);
            dataStore.SyncData("BellumCivile_PendingFiefParticipants", ref _pendingFiefParticipants);
            dataStore.SyncData("BellumCivile_PendingFiefExclude",  ref _pendingFiefExclude);
            dataStore.SyncData("BellumCivile_PendingFiefDate",     ref _pendingFiefDate);
            dataStore.SyncData("BellumCivile_PendingFiefCreatedDay", ref _pendingFiefCreatedDay);
            dataStore.SyncData("BellumCivile_PendingFiefRetryCount", ref _pendingFiefRetryCount);
            dataStore.SyncData("BellumCivile_FiefStaleRepairCount", ref _fiefStaleRepairCount);
            dataStore.SyncData("BellumCivile_PendingFirstRightCapturer", ref _pendingFirstRightCapturer);
            dataStore.SyncData("BellumCivile_PendingFirstRightDate", ref _pendingFirstRightDate);
            dataStore.SyncData("BellumCivile_FiefBribedVotes",     ref _fiefBribedVotes);
            dataStore.SyncData("BellumCivile_FiefBribedCandidateVotes", ref _fiefBribedCandidateVotes);
            dataStore.SyncData("BellumCivile_FiefNomineeByVoter", ref _fiefNomineeByVoter);
            dataStore.SyncData("BellumCivile_FiefNominationReasons", ref _fiefNominationReasons);
            dataStore.SyncData("BellumCivile_FiefNominationScores", ref _fiefNominationScores);
            dataStore.SyncData("BellumCivile_FiefCommittedNominationKeys", ref _fiefCommittedNominationKeys);
            dataStore.SyncData("BellumCivile_FiefSelfEncouragementUsed", ref _fiefSelfEncouragementUsed);
            dataStore.SyncData("BellumCivile_FiefPersuasionFailed", ref _fiefPersuasionFailed);

            if (_pendingFiefProposer == null) _pendingFiefProposer = new Dictionary<string, string>();
            if (_pendingFiefCapturer == null) _pendingFiefCapturer = new Dictionary<string, string>();
            if (_pendingFiefParticipants == null) _pendingFiefParticipants = new Dictionary<string, string>();
            if (_pendingFiefExclude  == null) _pendingFiefExclude  = new Dictionary<string, string>();
            if (_pendingFiefDate     == null) _pendingFiefDate     = new Dictionary<string, CampaignTime>();
            if (_pendingFiefCreatedDay == null) _pendingFiefCreatedDay = new Dictionary<string, float>();
            if (_pendingFiefRetryCount == null) _pendingFiefRetryCount = new Dictionary<string, int>();
            if (_fiefStaleRepairCount == null) _fiefStaleRepairCount = new Dictionary<string, int>();
            if (_pendingFirstRightCapturer == null) _pendingFirstRightCapturer = new Dictionary<string, string>();
            if (_pendingFirstRightDate == null) _pendingFirstRightDate = new Dictionary<string, CampaignTime>();
            if (_fiefBribedVotes     == null) _fiefBribedVotes     = new Dictionary<string, int>();
            if (_fiefBribedCandidateVotes == null) _fiefBribedCandidateVotes = new Dictionary<string, string>();
            if (_fiefNomineeByVoter == null) _fiefNomineeByVoter = new Dictionary<string, string>();
            if (_fiefNominationReasons == null) _fiefNominationReasons = new Dictionary<string, string>();
            if (_fiefNominationScores == null) _fiefNominationScores = new Dictionary<string, float>();
            if (_fiefCommittedNominationKeys == null) _fiefCommittedNominationKeys = new Dictionary<string, bool>();
            if (_fiefSelfEncouragementUsed == null) _fiefSelfEncouragementUsed = new Dictionary<string, bool>();
            if (_fiefPersuasionFailed == null) _fiefPersuasionFailed = new Dictionary<string, bool>();
        }


        public int? GetBribedVote(Kingdom kingdom, Settlement settlement, Clan voterClan)
        {
            if (kingdom == null || settlement == null || voterClan == null) return null;
            if (_fiefBribedVotes.TryGetValue(BribeKey(kingdom, settlement, voterClan), out int faction))
                return faction;
            return null;
        }

        internal IEnumerable<string> GetVotePledgeKeys(Clan voter) => VotePledgeService.KeysFor(voter, "fief",
            _fiefBribedCandidateVotes.Keys.Concat(_fiefBribedVotes.Keys));

        public string GetBribedCandidateVote(Kingdom kingdom, Settlement settlement, Clan voterClan)
        {
            if (kingdom == null || settlement == null || voterClan == null) return null;
            return _fiefBribedCandidateVotes.TryGetValue(BribeKey(kingdom, settlement, voterClan), out string candidateClanId)
                ? candidateClanId
                : null;
        }

        public void SetBribedVote(Kingdom kingdom, Settlement settlement, Clan voterClan, int factionType)
        {
            if (kingdom == null || settlement == null || voterClan == null) return;
            _fiefBribedVotes[BribeKey(kingdom, settlement, voterClan)] = factionType;
            VotePledgeService.Invalidate();
        }

        public void SetBribedCandidateVote(Kingdom kingdom, Settlement settlement, Clan voterClan, Clan candidateClan)
        {
            if (kingdom == null || settlement == null || voterClan == null || candidateClan == null) return;

            string key = BribeKey(kingdom, settlement, voterClan);
            _fiefBribedCandidateVotes[key] = candidateClan.StringId;
            _fiefNomineeByVoter[key] = candidateClan.StringId;
            _fiefNominationReasons[key] = "bribed";
            _fiefNominationScores[key] = float.MaxValue / 4f;
            _fiefCommittedNominationKeys[key] = true;
            VotePledgeService.Invalidate();
        }

        public void ClearBribedVotesForSettlement(Kingdom kingdom, Settlement settlement)
        {
            if (kingdom == null || settlement == null) return;
            ClearBribedVotesForPendingKey(PendingKey(kingdom, settlement));
        }

        private void ClearBribedVotesForPendingKey(string pendingKey)
        {
            VotePledgeService.Invalidate();
            if (string.IsNullOrEmpty(pendingKey)) return;
            string prefix = pendingKey + "|";
            var toRemove = _fiefBribedVotes.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in toRemove) _fiefBribedVotes.Remove(k);
            var candidateToRemove = _fiefBribedCandidateVotes.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in candidateToRemove) _fiefBribedCandidateVotes.Remove(k);
            var committedToRemove = _fiefCommittedNominationKeys.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in committedToRemove) _fiefCommittedNominationKeys.Remove(k);
            ClearNominationsForPendingKey(pendingKey);
            _fiefSelfEncouragementUsed.Remove(pendingKey);
            var failedToRemove = _fiefPersuasionFailed.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string k in failedToRemove) _fiefPersuasionFailed.Remove(k);
        }

        public Clan GetNominatedCandidate(Kingdom kingdom, Settlement settlement, Clan voterClan)
        {
            if (kingdom == null || settlement == null || voterClan == null) return null;
            string key = BribeKey(kingdom, settlement, voterClan);
            return _fiefNomineeByVoter.TryGetValue(key, out string candidateId)
                ? ResolveClan(candidateId)
                : null;
        }

        public string GetNominationReasonText(Kingdom kingdom, Settlement settlement, Clan voterClan)
        {
            if (kingdom == null || settlement == null || voterClan == null) return "";
            string key = BribeKey(kingdom, settlement, voterClan);
            Clan candidate = GetNominatedCandidate(kingdom, settlement, voterClan);
            string rawReasons = _fiefNominationReasons.TryGetValue(key, out string reasons)
                ? reasons
                : "judgment";

            return FiefNominationHelper.BuildReasonText(
                rawReasons.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                candidate,
                voterClan);
        }

        public List<string> GetRankedNominatedCandidateIds(Kingdom kingdom, Settlement settlement, IEnumerable<Clan> validCandidates, int maxCount)
        {
            return GetRankedNominations(kingdom, settlement, validCandidates, maxCount)
                .Select(entry => entry.CandidateId)
                .ToList();
        }

        internal List<FiefNominationRankingEntry> GetRankedNominations(Kingdom kingdom, Settlement settlement, IEnumerable<Clan> validCandidates, int maxCount)
        {
            if (kingdom == null || settlement == null || validCandidates == null || maxCount <= 0)
                return new List<FiefNominationRankingEntry>();

            string prefix = PendingKey(kingdom, settlement) + "|";
            HashSet<string> validIds = new HashSet<string>(validCandidates
                .Where(c => c != null)
                .Select(c => c.StringId));

            return _fiefNomineeByVoter
                .Where(kv => kv.Key.StartsWith(prefix) && validIds.Contains(kv.Value))
                .GroupBy(kv => kv.Value)
                .Select(group => new FiefNominationRankingEntry
                {
                    CandidateId = group.Key,
                    Count = group.Count(),
                    Score = group.Sum(kv => _fiefNominationScores.TryGetValue(kv.Key, out float score) ? score : 0f)
                })
                .OrderByDescending(group => group.Count)
                .ThenByDescending(group => group.Score)
                .Take(maxCount)
                .ToList();
        }

        public List<Clan> GetCurrentNominationRanking(Kingdom kingdom, Settlement settlement, int maxCount)
        {
            if (kingdom == null || settlement == null)
                return new List<Clan>();

            Clan excluded = null;
            string pendingKey = PendingKey(kingdom, settlement);
            if (_pendingFiefExclude.TryGetValue(pendingKey, out string excludedId) && !string.IsNullOrEmpty(excludedId))
                excluded = ResolveClan(excludedId);

            List<Clan> validCandidates = FiefNominationHelper.GetEligibleCandidates(kingdom, settlement, excluded).ToList();
            List<string> rankedIds = GetRankedNominatedCandidateIds(kingdom, settlement, validCandidates, maxCount);
            List<Clan> ranked = rankedIds
                .Select(ResolveClan)
                .Where(c => c != null)
                .ToList();

            if (ranked.Count >= maxCount)
                return ranked;

            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            Clan capturerClan = ResolveStoredCapturerClan(pendingKey);

            List<Clan> fallback = validCandidates
                .Where(c => !ranked.Contains(c))
                .Select(c => new
                {
                    Clan = c,
                    Score = FiefNominationHelper.ScoreCandidate(Clan.PlayerClan, c, kingdom, settlement, capturerClan, factionManager)?.Score ?? 0f
                })
                .OrderByDescending(c => c.Score)
                .Take(maxCount - ranked.Count)
                .Select(c => c.Clan)
                .ToList();

            ranked.AddRange(fallback);
            return ranked;
        }

        private void BuildPreliminaryNominations(Kingdom kingdom, Settlement settlement, Clan capturerClan, Clan clanToExclude)
        {
            if (kingdom == null || settlement == null)
                return;

            string pendingKey = PendingKey(kingdom, settlement);
            ClearNominationsForPendingKey(pendingKey);

            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            foreach (Clan voter in Clan.All.Where(c => FiefNominationHelper.IsValidVoter(c, kingdom)))
            {
                FiefNominationResult result = FiefNominationHelper.ChooseNominee(
                    voter,
                    kingdom,
                    settlement,
                    capturerClan,
                    clanToExclude,
                    factionManager);

                if (result?.Candidate == null)
                    continue;

                string key = BribeKey(kingdom, settlement, voter);
                _fiefNomineeByVoter[key] = result.Candidate.StringId;
                _fiefNominationReasons[key] = string.Join("|", result.Reasons);
                _fiefNominationScores[key] = result.Score;
            }
        }

        internal void RefreshPreliminaryNominationsForVote(
            Kingdom kingdom,
            Settlement settlement,
            Clan capturerClan,
            IEnumerable<Clan> candidatePool)
        {
            if (kingdom == null || settlement == null || candidatePool == null)
                return;

            List<Clan> candidates = candidatePool
                .Where(candidate => candidate != null)
                .Distinct()
                .ToList();
            if (candidates.Count == 0)
                return;

            string pendingKey = PendingKey(kingdom, settlement);
            string prefix = pendingKey + "|";
            HashSet<string> candidateIds = new HashSet<string>(candidates.Select(candidate => candidate.StringId));
            List<string> committedKeys = _fiefNomineeByVoter.Keys
                .Where(key => key.StartsWith(prefix)
                    && (_fiefCommittedNominationKeys.ContainsKey(key) || _fiefBribedCandidateVotes.ContainsKey(key)))
                .ToList();
            Dictionary<string, string> committedNominees = committedKeys
                .Where(key => candidateIds.Contains(_fiefNomineeByVoter[key]))
                .ToDictionary(key => key, key => _fiefNomineeByVoter[key]);
            Dictionary<string, string> committedReasons = committedNominees.Keys
                .ToDictionary(
                    key => key,
                    key => _fiefNominationReasons.TryGetValue(key, out string reason) ? reason : "judgment");
            Dictionary<string, float> committedScores = committedNominees.Keys
                .ToDictionary(
                    key => key,
                    key => _fiefNominationScores.TryGetValue(key, out float score) ? score : 0f);

            ClearNominationsForPendingKey(pendingKey);

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            foreach (Clan voter in Clan.All.Where(clan => FiefNominationHelper.IsValidVoter(clan, kingdom)))
            {
                FiefNominationResult best = null;
                foreach (Clan candidate in candidates)
                {
                    FiefNominationResult result = FiefNominationHelper.ScoreCandidate(
                        voter,
                        candidate,
                        kingdom,
                        settlement,
                        capturerClan,
                        factionManager);
                    if (result != null && (best == null || result.Score > best.Score))
                        best = result;
                }

                if (best?.Candidate == null)
                    continue;

                string key = BribeKey(kingdom, settlement, voter);
                _fiefNomineeByVoter[key] = best.Candidate.StringId;
                _fiefNominationReasons[key] = string.Join("|", best.Reasons);
                _fiefNominationScores[key] = best.Score;
            }

            foreach (KeyValuePair<string, string> committed in committedNominees)
            {
                string voterId = committed.Key.Substring(prefix.Length);
                Clan voter = ResolveClan(voterId);
                if (!FiefNominationHelper.IsValidVoter(voter, kingdom))
                    continue;

                _fiefNomineeByVoter[committed.Key] = committed.Value;
                _fiefNominationReasons[committed.Key] = committedReasons[committed.Key];
                _fiefNominationScores[committed.Key] = committedScores[committed.Key];
            }
        }

        private void ClearNominationsForPendingKey(string pendingKey)
        {
            if (string.IsNullOrEmpty(pendingKey)) return;

            string prefix = pendingKey + "|";
            var nominationKeys = _fiefNomineeByVoter.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string key in nominationKeys)
                _fiefNomineeByVoter.Remove(key);

            var reasonKeys = _fiefNominationReasons.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string key in reasonKeys)
                _fiefNominationReasons.Remove(key);

            var scoreKeys = _fiefNominationScores.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (string key in scoreKeys)
                _fiefNominationScores.Remove(key);
        }

        private static Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrEmpty(clanId))
                return null;

            if (Clan.PlayerClan != null && Clan.PlayerClan.StringId == clanId)
                return Clan.PlayerClan;

            return Clan.All.FirstOrDefault(c => c.StringId == clanId);
        }

        private Clan ResolveStoredCapturerClan(string pendingKey)
        {
            if (string.IsNullOrEmpty(pendingKey))
                return null;

            if (!_pendingFiefCapturer.TryGetValue(pendingKey, out string capturerHeroId) || string.IsNullOrEmpty(capturerHeroId))
                return null;

            Hero capturer = Hero.FindFirst(h => h.StringId == capturerHeroId);
            return capturer?.Clan;
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.IsSiegeAssault || mapEvent.Winner == null)
                return;

            Settlement settlement = ResolveMapEventSettlement(mapEvent);
            if (settlement == null)
                return;

            List<string> participantClanIds = GetNobleClanIdsFromSide(mapEvent.Winner);
            if (participantClanIds.Count == 0)
                return;

            _recentSiegeParticipantClanIdsBySettlement[settlement.StringId] = participantClanIds;
        }

        private static Settlement ResolveMapEventSettlement(MapEvent mapEvent)
        {
            if (mapEvent == null)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            System.Type type = mapEvent.GetType();

            foreach (string name in new[] { "MapEventSettlement", "Settlement", "_mapEventSettlement", "_settlement" })
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && typeof(Settlement).IsAssignableFrom(property.PropertyType))
                    return property.GetValue(mapEvent) as Settlement;

                FieldInfo field = type.GetField(name, flags);
                if (field != null && typeof(Settlement).IsAssignableFrom(field.FieldType))
                    return field.GetValue(mapEvent) as Settlement;
            }

            PropertyInfo settlementProperty = type.GetProperties(flags)
                .FirstOrDefault(property => typeof(Settlement).IsAssignableFrom(property.PropertyType));
            if (settlementProperty != null)
                return settlementProperty.GetValue(mapEvent) as Settlement;

            FieldInfo settlementField = type.GetFields(flags)
                .FirstOrDefault(field => typeof(Settlement).IsAssignableFrom(field.FieldType));
            return settlementField?.GetValue(mapEvent) as Settlement;
        }

        private static List<string> GetNobleClanIdsFromSide(MapEventSide side)
        {
            List<string> clanIds = new List<string>();
            if (side?.Parties == null)
                return clanIds;

            foreach (MapEventParty mapEventParty in side.Parties)
            {
                Clan clan = mapEventParty?.Party?.MobileParty?.LeaderHero?.Clan;
                if (clan == null
                    || string.IsNullOrWhiteSpace(clan.StringId)
                    || clan.IsEliminated
                    || clan.IsBanditFaction
                    || (clan.IsMinorFaction && clan != Clan.PlayerClan))
                    continue;

                if (!clanIds.Contains(clan.StringId))
                    clanIds.Add(clan.StringId);
            }

            return clanIds;
        }

        private string ResolveRecentSiegeParticipantClanIds(Settlement settlement, Hero capturerHero)
        {
            List<string> clanIds = settlement != null
                && _recentSiegeParticipantClanIdsBySettlement.TryGetValue(settlement.StringId, out List<string> stored)
                ? stored.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
                : new List<string>();

            string capturerClanId = capturerHero?.Clan?.StringId;
            if (!string.IsNullOrWhiteSpace(capturerClanId) && !clanIds.Contains(capturerClanId))
                clanIds.Add(capturerClanId);

            return string.Join(",", clanIds);
        }

        private bool SelectedCandidateParticipatedInSiege(out bool wasCapturer)
        {
            wasCapturer = false;

            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (kingdom == null || settlement == null || candidate == null)
                return false;

            string pendingKey = PendingKey(kingdom, settlement);
            Clan capturerClan = ResolveStoredCapturerClan(pendingKey);
            wasCapturer = capturerClan != null && capturerClan == candidate;
            if (wasCapturer)
                return true;

            if (!_pendingFiefParticipants.TryGetValue(pendingKey, out string participantIds)
                || string.IsNullOrWhiteSpace(participantIds))
                return false;

            return participantIds
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(id => string.Equals(id.Trim(), candidate.StringId, StringComparison.Ordinal));
        }

        public bool QueueRelinquishedSettlementVote(Settlement settlement)
        {
            if (settlement?.OwnerClan?.Kingdom == null) return false;

            return QueueFiefVote(
                settlement.OwnerClan.Kingdom,
                settlement,
                settlement.OwnerClan,
                null,
                settlement.OwnerClan);
        }

        internal bool QueueAgendaSettlementVote(CourtAgendaRecord agenda, Settlement settlement, bool allocation)
        {
            return QueueFiefVote(agenda.Realm, settlement, agenda.Sponsor, null, agenda.OriginalHolder,
                allocation ? "{=BC_Fief_Delib_RevocationAnnouncement}The lords of the realm have agreed that {SETTLEMENT_NAME} must be taken from {CLAN_NAME}. The court will now deliberate on who should receive it and call for a vote within {DAYS} days."
                : "{=BC_CrownGrantDeliberation}The crown has placed {SETTLEMENT_NAME} before the lords of the realm. The court will deliberate on who should receive it and call for a vote within {DAYS} days.",
                announcementClan: agenda.OriginalHolder, scheduledDate: allocation ? agenda.AllocationVoteDate : agenda.VoteDate);
        }

        public bool QueueRevocationSettlementVote(Settlement settlement, Clan proposerClan)
        {
            if (settlement?.OwnerClan?.Kingdom == null) return false;

            return QueueFiefVote(
                settlement.OwnerClan.Kingdom,
                settlement,
                proposerClan ?? settlement.OwnerClan.Kingdom.RulingClan,
                null,
                settlement.OwnerClan,
                "{=BC_Fief_Delib_RevocationAnnouncement}The lords of the realm have agreed that {SETTLEMENT_NAME} must be taken from {CLAN_NAME}. The court will now deliberate on who should receive it and call for a vote within {DAYS} days.");
        }

        public bool QueueTreatySettlementVote(Settlement settlement, Clan proposerClan)
        {
            Kingdom kingdom = settlement?.OwnerClan?.Kingdom;
            if (kingdom == null)
                return false;

            return QueueFiefVote(
                kingdom,
                settlement,
                proposerClan ?? kingdom.RulingClan,
                null,
                null);
        }

        public bool QueueConfiscatedSettlementVote(Settlement settlement, Kingdom kingdom, Clan proposerClan, Clan dispossessedClan)
        {
            if (settlement == null || kingdom == null || kingdom.IsEliminated)
                return false;

            string key = PendingKey(kingdom, settlement);
            if (_pendingFiefDate.ContainsKey(key))
                return true;

            return QueueFiefVote(
                kingdom,
                settlement,
                proposerClan ?? kingdom.RulingClan,
                null,
                dispossessedClan,
                "{=BC_Fief_Delib_ConfiscationAnnouncement}Following the confiscation of {SETTLEMENT_NAME} from {CLAN_NAME}, the court will deliberate on its new holder and call for a vote within {DAYS} days.",
                announcementClan: dispossessedClan);
        }

        public bool QueueSovereignSeparationSettlementVote(
            Settlement settlement,
            Kingdom kingdom,
            Clan proposerClan,
            Clan departingClan)
        {
            if (settlement == null || kingdom == null || kingdom.IsEliminated)
                return false;

            string key = PendingKey(kingdom, settlement);
            if (_pendingFiefDate.ContainsKey(key))
                return true;

            return QueueFiefVote(
                kingdom,
                settlement,
                proposerClan ?? kingdom.RulingClan,
                null,
                departingClan,
                "{=BC_Fief_Delib_SovereignSeparationAnnouncement}Following the peaceful separation of the {CLAN_NAME}, {SETTLEMENT_NAME} has returned to the Crown. The court will deliberate on its new holder and call for a vote within {DAYS} days.",
                announcementClan: departingClan);
        }

        public bool QueueBlockedClaimantDecision(SettlementClaimantDecision decision)
        {
            Kingdom kingdom = decision?.Kingdom;
            Settlement settlement = decision?.Settlement;
            if (kingdom == null || settlement == null || kingdom.IsEliminated)
                return false;

            string key = PendingKey(kingdom, settlement);
            if (_pendingFiefDate.ContainsKey(key))
                return true;

            return QueueFiefVote(
                kingdom,
                settlement,
                decision.ProposerClan ?? kingdom.RulingClan,
                ResolveClaimantDecisionCapturer(decision),
                decision.ClanToExclude);
        }

        public bool HasPendingFiefVote(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            string prefix = KingdomPrefix(kingdom);
            return _pendingFiefDate.Keys.Any(key => key.StartsWith(prefix));
        }

        public bool HasPendingFiefVoteForSettlement(Kingdom kingdom, Settlement settlement)
        {
            if (kingdom == null || settlement == null) return false;
            return _pendingFiefDate.ContainsKey(PendingKey(kingdom, settlement));
        }

        internal IEnumerable<(Settlement settlement, CampaignTime date)> PendingCrownAllocations(Kingdom realm)
        {
            if (realm?.RulingClan == null) yield break;
            string prefix = KingdomPrefix(realm);
            foreach (var pending in _pendingFiefDate.Where(p => p.Key.StartsWith(prefix)).OrderBy(p => p.Value))
            {
                if (!_pendingFiefProposer.TryGetValue(pending.Key, out var proposer) || proposer != realm.RulingClan.StringId) continue;
                var settlement = Settlement.Find(pending.Key.Substring(prefix.Length));
                if (settlement != null) yield return (settlement, pending.Value);
            }
        }

        internal static bool IsAwaitingAllocation(Settlement settlement)
        {
            if (settlement?.Town == null) return false;
            if (settlement.Town.IsOwnerUnassigned) return true;
            Kingdom realm = settlement.OwnerClan?.Kingdom;
            if (realm == null) return false;
            var behavior = Current;
            string key = PendingKey(realm, settlement);
            return behavior?._pendingFiefDate.ContainsKey(key) == true
                || behavior?._pendingFirstRightDate.ContainsKey(key) == true
                || realm.UnresolvedDecisions.OfType<SettlementClaimantDecision>()
                    .Any(decision => decision.Settlement == settlement);
        }

        private bool QueueFiefVote(
            Kingdom kingdom,
            Settlement settlement,
            Clan proposerClan,
            Hero capturerHero,
            Clan clanToExclude,
            string announcementText = null,
            bool isReliabilityRecovery = false,
            Clan announcementClan = null, CampaignTime? scheduledDate = null)
        {
            if (!CanDeliberateFiefs(kingdom) || settlement == null)
                return false;

            if (settlement.Town == null)
            {
                BellumCivileLogger.Log($"Ignored fief deliberation request for non-fortification {settlement.StringId}.");
                return false;
            }

            string key = PendingKey(kingdom, settlement);
            if (_pendingFiefDate.ContainsKey(key))
            {
                BellumCivileLogger.Log($"Fief deliberation duplicate ignored for {key}.");
                return false;
            }

            _pendingFiefProposer[key] = proposerClan?.StringId ?? "";
            _pendingFiefCapturer[key] = capturerHero?.StringId ?? "";
            _pendingFiefParticipants[key] = ResolveRecentSiegeParticipantClanIds(settlement, capturerHero);
            _pendingFiefExclude[key]  = clanToExclude?.StringId ?? "";
            _pendingFiefDate[key]     = scheduledDate
                ?? CourtAgendaBehavior.Current?.ExecutiveVoteDate(kingdom, settlement.StringId, proposerClan)
                ?? CampaignTime.Now + CampaignTime.Days(O.PoliticalDeliberationDays);
            _pendingFiefCreatedDay[key] = DelayedVoteReliability.CurrentDay;
            _pendingFiefRetryCount[key] = 0;
            if (!isReliabilityRecovery)
                _fiefStaleRepairCount[key] = 0;
            BuildPreliminaryNominations(kingdom, settlement, capturerHero?.Clan ?? proposerClan, clanToExclude);
            BellumCivileLogger.Log($"Queued fief deliberation {key}; proposer={proposerClan?.StringId ?? "null"} capturer={capturerHero?.StringId ?? "null"} participants={_pendingFiefParticipants[key]} excluded={clanToExclude?.StringId ?? "null"} fires={_pendingFiefDate[key].ToDays:0.00}.");

            TextObject msg = new TextObject(announcementText ?? "{=BC_Fief_Delib_Announcement}The fate of {SETTLEMENT_NAME} is to be decided by the lords of the realm. The court will deliberate and call for a vote within {DAYS} days.");
            msg.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
            msg.SetTextVariable("CLAN_NAME", announcementClan?.Name?.ToString() ?? settlement.OwnerClan?.Name?.ToString() ?? "its former lord");
            msg.SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(_pendingFiefDate[key].ToDays - CampaignTime.Now.ToDays)));
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Land, primaryKingdom: kingdom, primaryClan: proposerClan, secondaryClan: settlement.OwnerClan);

            return true;
        }

        private void CancelPendingFiefVote(Kingdom kingdom, Settlement settlement)
        {
            if (kingdom == null || settlement == null) return;

            string key = PendingKey(kingdom, settlement);
            if (!_pendingFiefDate.ContainsKey(key)) return;

            ClearBribedVotesForSettlement(kingdom, settlement);
            RemovePendingKey(key);
        }

        internal void CancelAgendaVote(Kingdom realm, Settlement settlement) => CancelPendingFiefVote(realm, settlement);

        private void QueuePendingFirstRightChoice(Kingdom kingdom, Settlement settlement, Hero capturerHero)
        {
            if (kingdom == null || settlement == null) return;

            string key = PendingKey(kingdom, settlement);
            if (_pendingFirstRightDate.ContainsKey(key)) return;

            _pendingFirstRightCapturer[key] = capturerHero?.StringId ?? string.Empty;
            _pendingFirstRightDate[key] = CampaignTime.Now;
        }

        private void RemovePendingFirstRightChoice(string key)
        {
            _pendingFirstRightCapturer.Remove(key);
            _pendingFirstRightDate.Remove(key);
        }

        private static bool ShouldAwaitDiplomacyFirstRightChoice(
            Settlement settlement,
            Kingdom kingdom,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail,
            bool openToClaim)
        {
            if (!openToClaim || detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                return false;

            if (settlement?.Town == null || kingdom == null || Clan.PlayerClan == null)
                return false;

            if (!ModIntegrationHelper.IsDiplomacyLoaded)
                return false;

            bool? firstRightEnabled = ModIntegrationHelper.TryIsDiplomacyFiefFirstRightEnabled();
            if (firstRightEnabled == false)
                return false;

            if (Clan.PlayerClan.Kingdom != kingdom || Clan.PlayerClan.IsUnderMercenaryService)
                return false;

            if (capturerHero == null || capturerHero.Clan != Clan.PlayerClan)
                return false;

            return settlement.Town.IsOwnerUnassigned;
        }

        private static void ShowRightOfConquestAnnouncement(Settlement settlement)
        {
            if (settlement == null || Clan.PlayerClan == null) return;

            TextObject msg = new TextObject("{=BC_Fief_Delib_RightOfConquest}Invoking the ancient custom of First Refusal, {PLAYER_NAME} of the {CLAN_NAME} has taken direct ownership of {SETTLEMENT_NAME} after successfully capturing the fief.");
            msg.SetTextVariable("PLAYER_NAME", Hero.MainHero?.Name ?? Clan.PlayerClan.Leader?.Name ?? Clan.PlayerClan.Name);
            msg.SetTextVariable("CLAN_NAME", Clan.PlayerClan.Name);
            msg.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
            BellumCivileNotifications.Show(msg, BellumNotificationColors.Politics, primaryKingdom: Clan.PlayerClan?.Kingdom, primaryClan: Clan.PlayerClan, isPersonal: true);
        }


        // What does this method do?
        // Listens to OnSettlementOwnerChangedEvent. When openToClaim=true (the flag vanilla uses
        // to signal ConsiderAnnex that a vote should happen) after a siege, and the new kingdom is
        // the player's kingdom, queues a 7-day deliberation instead of the vanilla immediate vote.
        // BlockVanillaAnnexPatch already prevents ConsiderAnnex from firing, so this is the sole
        // path that creates a vote for war captures in the player's kingdom.
        private void OnSettlementOwnerChanged(
            Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null) return;

            Kingdom newKingdom = newOwner?.Clan?.Kingdom;
            // A completed clan-to-clan grant supersedes delayed allocation, but a
            // new ruler in the same house does not. Do not remove a resolving native decision.
            if (!openToClaim && newOwner?.Clan != oldOwner?.Clan)
            {
                string suffix = "|" + settlement.StringId;
                foreach (string pendingKey in _pendingFiefDate.Keys
                    .Where(value => value.EndsWith(suffix, StringComparison.Ordinal)).ToList())
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    RemovePendingKey(pendingKey);
                    BellumCivileLogger.Log($"Cancelled superseded fief deliberation {pendingKey}; owner={newOwner?.Clan?.StringId}; detail={detail}.");
                }
                foreach (string pendingKey in _pendingFirstRightDate.Keys
                    .Where(value => value.EndsWith(suffix, StringComparison.Ordinal)).ToList())
                    RemovePendingFirstRightChoice(pendingKey);
                if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision)
                    ClearFiefVoteStateForSettlement(settlement, "completed ownership transfer");
            }
            if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(newKingdom))
            {
                ClearFiefVoteStateForSettlement(settlement, "temporary realm ownership change");
                return;
            }

            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                && BellumTreatyTransferContext.IsTreatyTransfer)
            {
                Kingdom receivingKingdom = newOwner?.Clan?.Kingdom;
                if (openToClaim && receivingKingdom != null)
                {
                    Clan treatyProposerClan = newOwner?.Clan ?? receivingKingdom.RulingClan;
                    QueueFiefVote(receivingKingdom, settlement, treatyProposerClan, null, null);
                }
                return;
            }

            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null) return;
            string key = PendingKey(playerKingdom, settlement);

            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
            {
                if (openToClaim)
                    CancelPendingFiefVote(playerKingdom, settlement);
                return;
            }

            if (_pendingFirstRightDate.ContainsKey(key)
                && ModIntegrationHelper.ShouldBypassFiefDeliberation(settlement, playerKingdom, capturerHero))
            {
                ShowRightOfConquestAnnouncement(settlement);
                CancelPendingFiefVote(playerKingdom, settlement);
                RemovePendingFirstRightChoice(key);
                return;
            }

            if (ShouldAwaitDiplomacyFirstRightChoice(settlement, playerKingdom, capturerHero, detail, openToClaim))
            {
                QueuePendingFirstRightChoice(playerKingdom, settlement, capturerHero);
                CancelPendingFiefVote(playerKingdom, settlement);
                return;
            }

            if (!openToClaim) return;

            if (newKingdom == null || newKingdom != playerKingdom) return;

            Clan proposerClan = capturerHero?.Clan ?? newOwner?.Clan ?? playerKingdom.RulingClan;
            QueueFiefVote(playerKingdom, settlement, proposerClan, capturerHero, null);
        }
        
        private void OnHourlyTick()
        {
            foreach (string pendingKey in _pendingFirstRightDate.Keys.ToList())
            {
                int sep = pendingKey.IndexOf('|');
                if (sep < 0) { RemovePendingFirstRightChoice(pendingKey); continue; }

                string kingdomId = pendingKey.Substring(0, sep);
                string settlementId = pendingKey.Substring(sep + 1);

                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                Settlement settlement = Settlement.All.FirstOrDefault(s => s.StringId == settlementId);

                if (!CanDeliberateFiefs(kingdom) || settlement == null || settlement.MapFaction != kingdom)
                {
                    RemovePendingFirstRightChoice(pendingKey);
                    continue;
                }

                string capturerHeroId = _pendingFirstRightCapturer.TryGetValue(pendingKey, out string cid) ? cid : string.Empty;
                Hero capturer = !string.IsNullOrEmpty(capturerHeroId)
                    ? Hero.FindFirst(h => h.StringId == capturerHeroId)
                    : null;

                if (ModIntegrationHelper.ShouldBypassFiefDeliberation(settlement, kingdom, capturer))
                {
                    ShowRightOfConquestAnnouncement(settlement);
                    CancelPendingFiefVote(kingdom, settlement);
                    RemovePendingFirstRightChoice(pendingKey);
                    continue;
                }

                if ((_pendingFirstRightDate[pendingKey] + CampaignTime.Hours(1)).IsFuture)
                    continue;

                Clan proposerClan = capturer?.Clan ?? Clan.PlayerClan ?? kingdom.RulingClan;

                QueueFiefVote(kingdom, settlement, proposerClan, capturer, null);
                RemovePendingFirstRightChoice(pendingKey);
            }
        }


        private void OnDailyTick()
        {
            CleanupOrphanedFiefDecisionsForAllKingdoms();
            RecoverMissingUnassignedFiefVotes(Clan.PlayerClan?.Kingdom);

            var allKeys = _pendingFiefDate.Keys.ToList();
            foreach (string pendingKey in allKeys)
            {
                int sep = pendingKey.IndexOf('|');
                if (sep < 0) { RemovePendingKey(pendingKey); continue; }

                string kingdomId    = pendingKey.Substring(0, sep);
                string settlementId = pendingKey.Substring(sep + 1);

                Kingdom    kingdom    = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                Settlement settlement = Settlement.All.FirstOrDefault(s => s.StringId == settlementId);

                bool invalid = !CanDeliberateFiefs(kingdom)
                            || settlement == null
                            || settlement.MapFaction != kingdom;

                if (invalid)
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    BellumCivileLogger.Log($"Removing invalid delayed fief vote {pendingKey}; kingdom={kingdom?.StringId ?? "null"} settlement={settlement?.StringId ?? "null"}.");
                    RemovePendingKey(pendingKey);
                    continue;
                }

                DelayedVoteReliability.EnsureLifecycle(
                    pendingKey,
                    _pendingFiefDate[pendingKey],
                    _pendingFiefCreatedDay,
                    _pendingFiefRetryCount);

                bool siegeCapturePending = _pendingFiefCapturer.TryGetValue(pendingKey, out string storedCapturerHeroId)
                                        && !string.IsNullOrEmpty(storedCapturerHeroId);

                Hero storedCapturer = siegeCapturePending
                    ? Hero.FindFirst(h => h.StringId == storedCapturerHeroId)
                    : null;

                if (siegeCapturePending && ModIntegrationHelper.ShouldBypassFiefDeliberation(settlement, kingdom, storedCapturer))
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    BellumCivileLogger.Log($"Removing delayed fief vote {pendingKey} because diplomacy first refusal bypassed claimant deliberation.");
                    RemovePendingKey(pendingKey);
                    continue;
                }

                if (!_pendingFiefDate[pendingKey].IsPast) continue;

                bool voteActive = kingdom.UnresolvedDecisions.OfType<SettlementClaimantDecision>().Any()
                               || kingdom.UnresolvedDecisions.OfType<SettlementClaimantPreliminaryDecision>().Any();
                if (voteActive)
                {
                    BellumCivileLogger.Log($"Delayed fief vote {pendingKey} is waiting for existing unresolved claimant decisions to clear.");
                    continue;
                }

                bool hasProposer = _pendingFiefProposer.TryGetValue(pendingKey, out string proposerClanId);
                string capturerHeroId = siegeCapturePending ? storedCapturerHeroId
                    : (_pendingFiefCapturer.TryGetValue(pendingKey, out string cid) ? cid : "");
                string excludedClanId = _pendingFiefExclude.TryGetValue(pendingKey, out string xid) ? xid : "";

                Clan proposerClan = ResolveProposerClan(proposerClanId, kingdom);
                Hero capturer = capturerHeroId.Length > 0
                    ? Hero.FindFirst(h => h.StringId == capturerHeroId)
                    : null;
                Clan clanToExclude = excludedClanId.Length > 0
                    ? Clan.All.FirstOrDefault(c => c.StringId == excludedClanId)
                    : null;

                List<string> invalidReasons = new List<string>();
                if (!hasProposer) invalidReasons.Add("proposer_flag_missing");
                if (!IsValidProposer(proposerClan, kingdom)) invalidReasons.Add(GetInvalidProposerReason(proposerClan, kingdom));

                bool proposerInvalid = invalidReasons.Count > 0;
                if (proposerInvalid)
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    BellumCivileLogger.Log($"Removing invalid delayed fief vote {pendingKey}; kingdom={kingdom?.StringId ?? "null"} proposer={proposerClan?.StringId ?? proposerClanId ?? "null"} settlement={settlement?.StringId ?? "null"} reasons={string.Join(",", invalidReasons)}.");
                    RemovePendingKey(pendingKey);
                    continue;
                }

                SettlementClaimantDecision decision = new SettlementClaimantDecision(proposerClan, settlement, capturer, clanToExclude);
                if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingFiefCreatedDay, _pendingFiefRetryCount))
                {
                    FinalizeExpiredFiefVote(pendingKey, kingdom, settlement, decision, "pending_age_or_retry_limit");
                    continue;
                }

                bool isAllowed = false;
                try
                {
                    isAllowed = decision.IsAllowed();
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed fief vote {pendingKey} threw while checking IsAllowed(); error={ex.GetType().Name}:{ex.Message}. Pending vote kept for retry.");
                    if (RegisterFiefFailure(pendingKey, kingdom, settlement, decision, "is_allowed_exception"))
                        continue;
                    continue;
                }

                if (!isAllowed)
                {
                    BellumCivileLogger.Log($"Delayed fief vote {pendingKey} is not currently allowed by Bannerlord; proposer={proposerClan?.StringId ?? "null"} settlement={settlement?.StringId ?? "null"} owner={settlement?.OwnerClan?.StringId ?? "null"} capturer={capturer?.StringId ?? "null"} excluded={clanToExclude?.StringId ?? "null"}.");
                    if (RegisterFiefFailure(pendingKey, kingdom, settlement, decision, "not_allowed"))
                        continue;
                    continue;
                }

                BellumCivileLogger.Log($"Attempting to fire delayed fief vote {pendingKey}; proposer={proposerClan.StringId} excluded={clanToExclude?.StringId ?? "null"} capturer={capturer?.StringId ?? "null"}.");

                try
                {
                    IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Delayed fief vote {pendingKey} threw while entering unresolved decisions; error={ex.GetType().Name}:{ex.Message}. Pending vote kept for retry.");
                    if (RegisterFiefFailure(pendingKey, kingdom, settlement, decision, "add_decision_exception"))
                        continue;
                    continue;
                }

                if (WasFiefDecisionQueued(kingdom, settlement, null))
                {
                    RemovePendingKey(pendingKey);
                }
                else if (WasFiefDecisionResolvedImmediately(kingdom, settlement))
                {
                    ClearBribedVotesForPendingKey(pendingKey);
                    BellumCivileLogger.Log($"Delayed fief vote {pendingKey} resolved immediately by Bannerlord; owner={settlement.OwnerClan?.StringId ?? "null"}. Removing pending retry.");
                    RemovePendingKey(pendingKey);
                }
                else
                {
                    BellumCivileLogger.Log($"Failed to queue delayed fief vote {pendingKey}; keeping it pending for retry. owner={settlement.OwnerClan?.StringId ?? "null"} unassigned={settlement.Town?.IsOwnerUnassigned.ToString() ?? "null"}.");
                    RegisterFiefFailure(pendingKey, kingdom, settlement, decision, "add_decision_failed");
                }
            }
        }

        private bool RegisterFiefFailure(
            string pendingKey,
            Kingdom kingdom,
            Settlement settlement,
            SettlementClaimantDecision decision,
            string reason)
        {
            int attempts = DelayedVoteReliability.RegisterFailure(pendingKey, _pendingFiefRetryCount);
            if (!DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingFiefCreatedDay, _pendingFiefRetryCount))
                return false;

            FinalizeExpiredFiefVote(pendingKey, kingdom, settlement, decision, $"{reason};attempts={attempts}");
            return true;
        }

        private void FinalizeExpiredFiefVote(
            string pendingKey,
            Kingdom kingdom,
            Settlement settlement,
            SettlementClaimantDecision decision,
            string reason)
        {
            BellumCivileLogger.Log(
                $"Delayed fief vote {pendingKey} exceeded its reliability limits; reason={reason}. Attempting one final direct claimant decision before withdrawing the stale queue.");

            try
            {
                IdeologyBehavior.AddDecisionAsModAction(kingdom, decision);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Final recovery attempt for delayed fief vote {pendingKey} threw; error={ex.GetType().Name}:{ex.Message}.");
            }

            bool recovered = WasFiefDecisionQueued(kingdom, settlement, null)
                || WasFiefDecisionResolvedImmediately(kingdom, settlement);
            if (!recovered) CourtAgendaBehavior.Current?.CancelExecutiveMotion(kingdom, settlement?.StringId, decision.ProposerClan, reason);
            BellumCivileLogger.Log(
                recovered
                    ? $"Final recovery attempt restored fief vote {pendingKey}."
                    : $"Final recovery attempt could not restore fief vote {pendingKey}; stale pending state was cleared to release the kingdom decision queue.");

            ClearBribedVotesForPendingKey(pendingKey);
            RemovePendingKey(pendingKey);
        }

        private void CleanupOrphanedFiefDecisionsForAllKingdoms()
        {
            foreach (Kingdom kingdom in Kingdom.All)
            {
                CleanupOrphanedFiefDecisions(kingdom);
            }
        }

        public void CleanupFiefDecisionsForSettlement(Kingdom kingdom, Settlement settlement, KingdomDecision decisionToKeep = null)
        {
            if (kingdom == null || kingdom.IsEliminated || settlement == null)
                return;

            List<KingdomDecision> decisionsToRemove = kingdom.UnresolvedDecisions
                .Where(decision => decision != null
                                && decision != decisionToKeep
                                && IsFiefDecisionForSettlement(decision, settlement))
                .ToList();

            foreach (KingdomDecision decision in decisionsToRemove.Distinct().ToList())
            {
                kingdom.RemoveDecision(decision);
                BellumCivileLogger.Log($"Removed stale fief decision '{decision.GetType().Name}' for {settlement.StringId} in {kingdom.StringId} after claimant resolution.");
            }
        }

        public int ClearFiefVoteStateForSettlement(Settlement settlement, string reason)
        {
            if (settlement == null || string.IsNullOrWhiteSpace(settlement.StringId))
                return 0;

            int cleared = 0;
            string pendingSuffix = "|" + settlement.StringId;
            foreach (string pendingKey in _pendingFiefDate.Keys
                .Where(key => key.EndsWith(pendingSuffix, StringComparison.Ordinal))
                .ToList())
            {
                ClearBribedVotesForPendingKey(pendingKey);
                RemovePendingKey(pendingKey);
                cleared++;
            }

            foreach (string firstRightKey in _pendingFirstRightDate.Keys
                .Where(key => key.EndsWith(pendingSuffix, StringComparison.Ordinal))
                .ToList())
            {
                RemovePendingFirstRightChoice(firstRightKey);
                cleared++;
            }

            foreach (Kingdom kingdom in Kingdom.All.Where(candidate => candidate != null && !candidate.IsEliminated))
            {
                List<KingdomDecision> decisions = kingdom.UnresolvedDecisions
                    .Where(decision => decision != null && IsFiefDecisionForSettlement(decision, settlement))
                    .ToList();
                foreach (KingdomDecision decision in decisions)
                {
                    kingdom.RemoveDecision(decision);
                    cleared++;
                }

                ClearBribedVotesForSettlement(kingdom, settlement);
            }

            _recentSiegeParticipantClanIdsBySettlement.Remove(settlement.StringId);
            if (_currentQuerySettlement == settlement)
            {
                _currentQuerySettlement = null;
                _currentQueryNominee = null;
                _currentQueryVoterClan = null;
                _currentQueryCandidates.Clear();
                ClearFiefPendingSelection();
            }
            else
            {
                _conversationPendingKeys.RemoveAll(key => key.EndsWith(pendingSuffix, StringComparison.Ordinal));
                int maxPage = Math.Max(0, (_conversationPendingKeys.Count - 1) / DeliberationDialogueHelper.PageSize);
                _conversationPendingPage = Math.Min(_conversationPendingPage, maxPage);
            }

            if (cleared > 0)
            {
                BellumCivileLogger.Log(
                    $"Cleared fief vote state for direct settlement award; settlement={settlement.StringId}; entries={cleared}; reason={reason ?? "unknown"}.");
            }

            return cleared;
        }

        private void CleanupOrphanedFiefDecisions(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return;

            if (!CanDeliberateFiefs(kingdom))
            {
                foreach (var decision in kingdom.UnresolvedDecisions.Where(d => d is SettlementClaimantDecision
                    || d is SettlementClaimantPreliminaryDecision).ToList())
                    kingdom.RemoveDecision(decision);
                return;
            }

            if (!HasPotentiallyRepairableFiefDecisions(kingdom))
                return;

            List<KingdomDecision> decisionsToRemove = new List<KingdomDecision>();

            List<SettlementClaimantDecision> claimantDecisions = kingdom.UnresolvedDecisions
                .OfType<SettlementClaimantDecision>()
                .ToList();

            List<SettlementClaimantPreliminaryDecision> preliminaryDecisions = kingdom.UnresolvedDecisions
                .OfType<SettlementClaimantPreliminaryDecision>()
                .ToList();

            List<SettlementClaimantDecision> staleClaimantDecisions = claimantDecisions
                .Where(DelayedVoteReliability.IsLiveDecisionStale)
                .ToList();

            List<SettlementClaimantPreliminaryDecision> stalePreliminaryDecisions = preliminaryDecisions
                .Where(DelayedVoteReliability.IsLiveDecisionStale)
                .ToList();

            decisionsToRemove.AddRange(claimantDecisions
                .Where(decision => IsOrphanedFiefDecision(kingdom, decision, decision?.Settlement)));

            decisionsToRemove.AddRange(preliminaryDecisions
                .Where(decision => IsOrphanedFiefDecision(kingdom, decision, decision?.Settlement)));
            decisionsToRemove.AddRange(staleClaimantDecisions);
            decisionsToRemove.AddRange(stalePreliminaryDecisions);

            foreach (var duplicateGroup in claimantDecisions
                .Where(decision => decision?.Settlement != null)
                .GroupBy(decision => decision.Settlement.StringId)
                .Where(group => group.Count() > 1))
            {
                SettlementClaimantDecision newest = duplicateGroup
                    .OrderByDescending(decision => decision.TriggerTime.ToDays)
                    .First();

                decisionsToRemove.AddRange(duplicateGroup.Where(decision => decision != newest));
            }

            foreach (var duplicateGroup in preliminaryDecisions
                .Where(decision => decision?.Settlement != null)
                .GroupBy(decision => decision.Settlement.StringId)
                .Where(group => group.Count() > 1))
            {
                SettlementClaimantPreliminaryDecision newest = duplicateGroup
                    .OrderByDescending(decision => decision.TriggerTime.ToDays)
                    .First();

                decisionsToRemove.AddRange(duplicateGroup.Where(decision => decision != newest));
            }

            HashSet<string> settlementsWithClaimantVotes = new HashSet<string>(
                claimantDecisions
                    .Where(decision => decision?.Settlement != null)
                    .Select(decision => decision.Settlement.StringId));

            decisionsToRemove.AddRange(preliminaryDecisions
                .Where(decision => decision?.Settlement != null
                                && settlementsWithClaimantVotes.Contains(decision.Settlement.StringId)));

            foreach (KingdomDecision decision in decisionsToRemove.Distinct().ToList())
            {
                kingdom.RemoveDecision(decision);
                BellumCivileLogger.Log($"Removed orphaned, duplicate, or stale fief decision '{decision.GetType().Name}' from kingdom {kingdom.StringId}; age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} days.");
            }

            foreach (SettlementClaimantDecision staleDecision in staleClaimantDecisions)
            {
                Settlement settlement = staleDecision?.Settlement;
                if (settlement?.Town == null
                    || settlement.MapFaction != kingdom
                    || !settlement.Town.IsOwnerUnassigned
                    || HasPendingFiefVoteForSettlement(kingdom, settlement))
                {
                    continue;
                }

                string key = PendingKey(kingdom, settlement);
                int repairCount = _fiefStaleRepairCount.TryGetValue(key, out int stored) ? stored : 0;
                if (repairCount >= 1)
                {
                    BellumCivileLogger.Log($"Stale live fief decision {key} already exhausted its one rebuild allowance; motion withdrawn.");
                    continue;
                }

                _fiefStaleRepairCount[key] = repairCount + 1;
                QueueFiefVote(
                    kingdom,
                    settlement,
                    staleDecision.ProposerClan ?? kingdom.RulingClan,
                    ResolveClaimantDecisionCapturer(staleDecision),
                    staleDecision.ClanToExclude,
                    isReliabilityRecovery: true);
                BellumCivileLogger.Log($"Rebuilt stale live fief decision {key} as a fresh delayed vote.");
            }

            foreach (SettlementClaimantPreliminaryDecision staleDecision in stalePreliminaryDecisions)
            {
                Settlement settlement = staleDecision?.Settlement;
                Clan proposer = ResolveProposerClan(staleDecision?.ProposerClan?.StringId, kingdom);
                if (settlement?.Town == null
                    || settlement.MapFaction != kingdom
                    || !IsValidProposer(proposer, kingdom))
                {
                    continue;
                }

                string key = PendingKey(kingdom, settlement);
                int repairCount = _fiefStaleRepairCount.TryGetValue(key, out int stored) ? stored : 0;
                if (repairCount >= 1)
                {
                    BellumCivileLogger.Log($"Stale preliminary fief decision {key} already exhausted its one rebuild allowance; motion withdrawn.");
                    continue;
                }

                _fiefStaleRepairCount[key] = repairCount + 1;
                try
                {
                    IdeologyBehavior.AddDecisionAsModAction(
                        kingdom,
                        new SettlementClaimantPreliminaryDecision(proposer, settlement));
                    BellumCivileLogger.Log($"Rebuilt stale preliminary fief decision {key} as a fresh live motion.");
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Could not rebuild stale preliminary fief decision {key}; error={ex.GetType().Name}:{ex.Message}.");
                }
            }
        }

        private static bool HasPotentiallyRepairableFiefDecisions(Kingdom kingdom)
        {
            return kingdom != null
                && (kingdom.UnresolvedDecisions.OfType<SettlementClaimantDecision>().Any()
                    || kingdom.UnresolvedDecisions.OfType<SettlementClaimantPreliminaryDecision>().Any());
        }

        private static bool IsFiefDecisionForSettlement(KingdomDecision decision, Settlement settlement)
        {
            if (decision == null || settlement == null)
                return false;

            if (decision is SettlementClaimantDecision claimantDecision)
                return claimantDecision.Settlement == settlement;

            if (decision is SettlementClaimantPreliminaryDecision preliminaryDecision)
                return preliminaryDecision.Settlement == settlement;

            return false;
        }

        private static bool IsOrphanedFiefDecision(Kingdom expectedKingdom, KingdomDecision decision, Settlement settlement)
        {
            if (expectedKingdom == null || decision == null || settlement == null)
                return true;

            Kingdom decisionKingdom = decision.Kingdom;
            if (decisionKingdom == null || decisionKingdom != expectedKingdom || decisionKingdom.IsEliminated)
                return true;

            if (settlement.Town == null)
                return true;

            if (settlement.MapFaction != decisionKingdom)
                return true;

            if (decision.ProposerClan != null && (decision.ProposerClan.IsEliminated || decision.ProposerClan.Kingdom != decisionKingdom))
                return true;

            return false;
        }

        private static bool WasFiefDecisionQueued(Kingdom kingdom, Settlement settlement, Clan proposer)
        {
            if (kingdom == null || settlement == null)
                return false;

            return kingdom.UnresolvedDecisions
                .OfType<SettlementClaimantDecision>()
                .Any(decision =>
                    decision?.Settlement == settlement &&
                    (proposer == null || decision.ProposerClan == proposer));
        }

        private static bool WasFiefDecisionResolvedImmediately(Kingdom kingdom, Settlement settlement)
        {
            if (kingdom == null || settlement?.Town == null)
                return false;

            Clan owner = settlement.OwnerClan;
            return settlement.MapFaction == kingdom
                && owner != null
                && owner.Kingdom == kingdom
                && !settlement.Town.IsOwnerUnassigned;
        }

        private static Hero ResolveClaimantDecisionCapturer(SettlementClaimantDecision decision)
        {
            if (decision == null)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type decisionType = typeof(SettlementClaimantDecision);
            FieldInfo field = decisionType.GetField("_capturerHero", flags)
                ?? decisionType.GetField("CapturerHero", flags)
                ?? decisionType.GetField("_capturer", flags)
                ?? decisionType.GetField("Capturer", flags);
            if (field?.GetValue(decision) is Hero fieldHero)
                return fieldHero;

            PropertyInfo property = decisionType.GetProperty("CapturerHero", flags)
                ?? decisionType.GetProperty("Capturer", flags);
            return property?.GetValue(decision) as Hero;
        }

        public int RunReliabilityRepair(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return 0;

            int changes = 0;
            int decisionsBefore = kingdom.UnresolvedDecisions.Count;
            CleanupOrphanedFiefDecisions(kingdom);
            changes += Math.Max(0, decisionsBefore - kingdom.UnresolvedDecisions.Count);

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingFiefDate.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingFiefDate[key],
                    _pendingFiefCreatedDay,
                    _pendingFiefRetryCount);
            }

            if (kingdom == Clan.PlayerClan?.Kingdom)
                changes += RecoverMissingUnassignedFiefVotes(kingdom);

            return changes;
        }

        private static bool CanDeliberateFiefs(Kingdom kingdom) => kingdom != null && !kingdom.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);

        private int RecoverMissingUnassignedFiefVotes(Kingdom kingdom)
        {
            if (!CanDeliberateFiefs(kingdom) || kingdom != Clan.PlayerClan?.Kingdom)
                return 0;

            int recovered = 0;
            foreach (Settlement settlement in Settlement.All.Where(s =>
                s?.Town != null
                && s.MapFaction == kingdom
                && s.Town.IsOwnerUnassigned))
            {
                bool hasLiveDecision = kingdom.UnresolvedDecisions.Any(decision =>
                    IsFiefDecisionForSettlement(decision, settlement));
                if (hasLiveDecision || HasPendingFiefVoteForSettlement(kingdom, settlement))
                    continue;

                if (!QueueFiefVote(kingdom, settlement, kingdom.RulingClan, null, null))
                    continue;

                recovered++;
                BellumCivileLogger.Log($"Recovered unassigned fief {settlement.StringId} in {kingdom.StringId} by queuing a missing claimant vote.");
            }

            return recovered;
        }

        public IEnumerable<string> GetReliabilityDiagnostics(Kingdom kingdom)
        {
            if (kingdom == null)
                yield break;

            string prefix = KingdomPrefix(kingdom);
            foreach (string key in _pendingFiefDate.Keys.Where(k => k.StartsWith(prefix)).OrderBy(k => k))
            {
                DelayedVoteReliability.EnsureLifecycle(
                    key,
                    _pendingFiefDate[key],
                    _pendingFiefCreatedDay,
                    _pendingFiefRetryCount);
                bool dialogueReady = TryResolvePendingFiefForConversation(kingdom, key, out _);
                yield return $"fief pending={key} due={_pendingFiefDate[key].ToDays:0.0} age={DelayedVoteReliability.GetPendingAgeDays(key, _pendingFiefCreatedDay):0.0} retries={DelayedVoteReliability.GetFailureCount(key, _pendingFiefRetryCount)} dialogue_ready={dialogueReady}";
            }

            foreach (KingdomDecision decision in kingdom.UnresolvedDecisions.Where(d =>
                d is SettlementClaimantDecision || d is SettlementClaimantPreliminaryDecision))
            {
                Settlement settlement = decision is SettlementClaimantDecision claimant
                    ? claimant.Settlement
                    : ((SettlementClaimantPreliminaryDecision)decision).Settlement;
                yield return $"fief live={decision.GetType().Name} settlement={settlement?.StringId ?? "null"} age={DelayedVoteReliability.GetDecisionAgeDays(decision):0.0} stale={DelayedVoteReliability.IsLiveDecisionStale(decision)}";
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

        private void RemovePendingKey(string key)
        {
            _pendingFiefProposer.Remove(key);
            _pendingFiefCapturer.Remove(key);
            _pendingFiefParticipants.Remove(key);
            _pendingFiefExclude.Remove(key);
            _pendingFiefDate.Remove(key);
            _pendingFiefCreatedDay.Remove(key);
            _pendingFiefRetryCount.Remove(key);
            _fiefStaleRepairCount.Remove(key);
        }


        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RunReliabilityRepair(Clan.PlayerClan?.Kingdom);

            starter.AddPlayerLine(
                "fief_deliberation_topic",
                "lord_talk_speak_diplomacy_2",
                "fief_deliberation_topic_response",
                "{=BC_Fief_Delib_Topic}About the ongoing land grants...",
                FiefMenuEntryCondition,
                BeginFiefPendingSelection,
                100, null, null);

            starter.AddDialogLine(
                "fief_deliberation_choose_grant",
                "fief_deliberation_topic_response",
                "fief_deliberation_topic_choices",
                "{=BC_Fief_Delib_ChooseGrant}Which land grant did you wish to discuss?",
                null,
                null,
                100, null);

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                starter.AddPlayerLine(
                    $"fief_query_slot_{slot}",
                    "fief_deliberation_topic_choices",
                    "fief_query_stance_response",
                    FiefSlotQuestionText(slot),
                    () => FiefMenuSlotCondition(slot),
                    () => FiefMenuSlotAction(slot),
                    100 - slot, null, null);
            }

            starter.AddPlayerLine(
                "fief_deliberation_more",
                "fief_deliberation_topic_choices",
                "fief_deliberation_topic_response",
                "{=BC_Deliberation_MoreMatters}There are other matters I would like to discuss.",
                FiefPendingSelectionHasMore,
                AdvanceFiefPendingSelectionPage,
                60, null, null);

            starter.AddPlayerLine(
                "fief_deliberation_back",
                "fief_deliberation_topic_choices",
                "lord_pretalk",
                "{=BC_Deliberation_Back}Never mind. Let us speak of something else.",
                null,
                ClearFiefPendingSelection,
                50, null, null);

            starter.AddDialogLine("fief_query_not_clan_leader", "fief_query_stance_response", "hero_main_options",
                "{=BC_Fief_Delib_NotClanLeader}Such matters of land grants are not mine to decide. I speak only for myself, not for the {CLAN_NAME}. If it is politics you wish to discuss, you must seek out {CLAN_LEADER}, the head of our family.",
                VoteConversationIsNotClanLeader,
                null,
                200, null);

            starter.AddDialogLine("fief_query_already_bribed", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_AlreadyBribed}You have already secured my support in this matter. My vote is committed.",
                () => _currentQueryAlreadyBribed, null, 120, null);

            starter.AddDialogLine("fief_query_stance_nominee", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceNominee}I believe {CANDIDATE_NAME} should receive {SETTLEMENT_NAME}. {REASON_TEXT}",
                () => _currentQueryNominee != null && _currentQueryNominee != _currentQueryVoterClan, null, 115, null);

            starter.AddDialogLine("fief_query_stance_nominee_self", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceNomineeSelf}I believe my own house should receive {SETTLEMENT_NAME}. {REASON_TEXT}",
                () => _currentQueryNominee != null && _currentQueryNominee == _currentQueryVoterClan, null, 116, null);

            starter.AddDialogLine("fief_query_stance_royalist", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceRoyalist}A lord loyal to the crown should receive this fief. Strong bonds to the king are what make a realm stable.",
                () => _queriedFaction == (int)FactionType.Royalists, null, 110, null);

            starter.AddDialogLine("fief_query_stance_militarist", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceMilitarist}Those walls should belong to a capable warrior. A lord who holds them by strength of arms, not politics.",
                () => _queriedFaction == (int)FactionType.Glory, null, 100, null);

            starter.AddDialogLine("fief_query_stance_populist", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StancePopulist}A landless lord deserves this opportunity. The Populists would see the wealth of this realm more evenly shared.",
                () => _queriedFaction == (int)FactionType.Liberty, null, 90, null);

            starter.AddDialogLine("fief_query_stance_aristocrat", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceAristocrat}Only a lord of proper standing and lineage should administer those lands. Breeding and influence matter here.",
                () => _queriedFaction == (int)FactionType.Nobility, null, 80, null);

            starter.AddDialogLine("fief_query_stance_none", "fief_query_stance_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_StanceNone}I am still weighing the candidates, if I am honest. There are several worthy options to consider.",
                null, null, 70, null);

            starter.AddPlayerLine("fief_query_push_player", "fief_query_player_choice", "fief_candidate_selected_response",
                "{=BC_Fief_Delib_PushPlayer}I would like to nominate myself for such an honor.",
                FiefPlayerCandidatePushCondition,
                FiefPlayerCandidatePushAction,
                130, null, null);

            starter.AddPlayerLine("fief_query_push_npc_self", "fief_query_player_choice", "lord_pretalk",
                "{=BC_Fief_Delib_PushNpcSelf}I believe you should receive this fief.",
                FiefNpcSelfPushCondition,
                ApplyNpcSelfNominationEncouragement,
                125, null, null);

            starter.AddPlayerLine("fief_query_push_other", "fief_query_player_choice", "fief_candidate_list_response",
                "{=BC_Fief_Delib_PushOther}I would prefer another lord to receive this fief.",
                FiefOtherCandidatePushCondition,
                BeginFiefCandidateSelection,
                124, null, null);

            starter.AddDialogLine("fief_candidate_list_npc", "fief_candidate_list_response", "fief_candidate_list_choice",
                "{=BC_Fief_Delib_CandidateListPrompt}Whose claim do you want me to consider?",
                null, null, 100, null);

            for (int i = 0; i < 5; i++)
            {
                int slot = i;
                starter.AddPlayerLine(
                    $"fief_query_push_candidate_{slot}",
                    "fief_candidate_list_choice",
                    "fief_candidate_selected_response",
                    FiefCandidatePushText(slot),
                    () => FiefCandidatePushCondition(slot),
                    () => FiefCandidatePushAction(slot),
                    120 - slot, null, null);
            }

            starter.AddPlayerLine("fief_candidate_list_more", "fief_candidate_list_choice", "fief_candidate_list_response",
                "{=BC_Fief_Delib_CandidateListMore}I have another candidate in mind.",
                FiefCandidateListMoreCondition,
                AdvanceFiefCandidateListPage,
                80, null, null);

            starter.AddPlayerLine("fief_candidate_list_back", "fief_candidate_list_choice", "fief_query_player_choice",
                "{=BC_Fief_Delib_CandidateListBack}Never mind. Let us discuss another approach.",
                null, ClearFiefCandidateSelection, 70, null, null);

            starter.AddDialogLine("fief_candidate_selected_npc_self", "fief_candidate_selected_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_CandidateSelectedResponseSelf}Very well. I am listening, why should I back your claim?",
                FiefSelectedCandidateIsPlayerCondition,
                null,
                110, null);

            starter.AddDialogLine("fief_candidate_selected_npc", "fief_candidate_selected_response", "fief_query_player_choice",
                "{=BC_Fief_Delib_CandidateSelectedResponse}Very well. I am listening, why should I back the claim of {SELECTED_CANDIDATE_NAME}?",
                FiefSelectedCandidateIsOtherCondition,
                null,
                100, null);

            starter.AddPlayerLine("fief_query_try_persuasion", "fief_query_player_choice", "fief_persuasion_response",
                "{=BC_Fief_Delib_TryPersuasion}Perhaps I can convince you to change your mind.",
                FiefSelectedCandidateCondition,
                StartFiefVotePersuasion,
                123, FiefPersuasionClickableCondition, null);

            starter.AddPlayerLine("fief_query_try_bribe", "fief_query_player_choice", "fief_sway_response",
                "{=BC_Fief_Delib_TryBribe}I would be... grateful if you were to support my choice.",
                FiefSelectedCandidateCondition,
                null,
                122, FiefBribeClickableCondition, null);

            starter.AddPlayerLine("fief_query_push_royalist", "fief_query_player_choice", "fief_sway_response",
                "{=BC_Fief_Delib_PushRoyalist}I would be most grateful if you supported a Traditionalist candidate for this fief.",
                () => false,
                null,
                100, null, null);

            starter.AddPlayerLine("fief_query_push_militarist", "fief_query_player_choice", "fief_sway_response",
                "{=BC_Fief_Delib_PushMilitarist}A Militarist lord deserves this fief. Perhaps your vote could be persuaded in that direction?",
                () => false,
                null,
                100, null, null);

            starter.AddPlayerLine("fief_query_push_populist", "fief_query_player_choice", "fief_sway_response",
                "{=BC_Fief_Delib_PushPopulist}The Populists have waited long enough. I wonder if your support might be... flexible.",
                () => false,
                null,
                100, null, null);

            starter.AddPlayerLine("fief_query_push_aristocrat", "fief_query_player_choice", "fief_sway_response",
                "{=BC_Fief_Delib_PushAristocrat}An Aristocrat lord would be the right choice here. Might your vote be open to some... persuasion?",
                () => false,
                null,
                100, null, null);

            starter.AddPlayerLine("fief_query_end", "fief_query_player_choice", "lord_pretalk",
                "{=BC_Fief_Delib_EndQuery}I will leave the vote to play out. Thank you, my {ADDRESS}.",
                SetFiefDialogueAddress,
                ClearFiefCandidateSelection, 90, null, null);

            starter.AddPlayerLine("fief_query_end_fallback", "fief_query_player_choice", "lord_pretalk",
                "{=BC_Fief_Delib_EndQueryFallback}I will leave the vote to play out.",
                NoFiefDialogueAddress, ClearFiefCandidateSelection, 90, null, null);

            starter.AddDialogLine("fief_sway_npc_response", "fief_sway_response", "fief_sway_barter",
                "{=BC_Fief_Delib_SwayResponse}An interesting proposition. I am prepared to hear what you are offering.",
                null, null, 100, null);

            starter.AddPlayerLine("fief_sway_barter_start", "fief_sway_barter", "lord_pretalk",
                "{=BC_Fief_Delib_DiscussTerms}Let us discuss terms.",
                null, LaunchFiefBribeBarter, 100, null, null);

            starter.AddDialogLine("fief_persuasion_npc_response", "fief_persuasion_response", "fief_persuasion_arguments",
                "{=BC_Fief_Delib_PersuasionResponse}You ask much. Why should I turn my support that way?",
                null, null, 100, null);

            starter.AddPlayerLine("fief_persuasion_capturer", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeCapturer}They took the walls by force. Such deeds deserve reward.",
                FiefSelectedCandidateParticipatedInSiegeOtherCondition, () => BlockFiefPersuasionOption("capturer"), 120,
                (out TextObject hintText) => FiefPersuasionOptionClickable("capturer", out hintText),
                () => BuildFiefPersuasionOption("capturer"));

            starter.AddPlayerLine("fief_persuasion_capturer_self", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeCapturerSelf}I helped take those walls by force. Such deeds deserve reward.",
                FiefSelectedCandidateParticipatedInSiegeSelfCondition, () => BlockFiefPersuasionOption("capturer"), 121,
                (out TextObject hintText) => FiefPersuasionOptionClickable("capturer", out hintText),
                () => BuildFiefPersuasionOption("capturer"));

            starter.AddPlayerLine("fief_persuasion_need", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeNeed}Their house lacks the lands expected of its station.",
                FiefSelectedCandidateIsOtherCondition, () => BlockFiefPersuasionOption("need"), 110,
                (out TextObject hintText) => FiefPersuasionOptionClickable("need", out hintText),
                () => BuildFiefPersuasionOption("need"));

            starter.AddPlayerLine("fief_persuasion_need_self", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeNeedSelf}My house lacks the lands expected of its station.",
                FiefSelectedCandidateIsPlayerCondition, () => BlockFiefPersuasionOption("need"), 111,
                (out TextObject hintText) => FiefPersuasionOptionClickable("need", out hintText),
                () => BuildFiefPersuasionOption("need"));

            starter.AddPlayerLine("fief_persuasion_legal_claim", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeLegalClaim}Their house has a lawful claim to these lands.",
                FiefSelectedCandidateHasLegalClaimOtherCondition, () => BlockFiefPersuasionOption("legal_claim"), 108,
                (out TextObject hintText) => FiefPersuasionOptionClickable("legal_claim", out hintText),
                () => BuildFiefPersuasionOption("legal_claim"));

            starter.AddPlayerLine("fief_persuasion_legal_claim_self", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeLegalClaimSelf}My house has a lawful claim to these lands.",
                FiefSelectedCandidateHasLegalClaimSelfCondition, () => BlockFiefPersuasionOption("legal_claim"), 109,
                (out TextObject hintText) => FiefPersuasionOptionClickable("legal_claim", out hintText),
                () => BuildFiefPersuasionOption("legal_claim"));

            starter.AddPlayerLine("fief_persuasion_stability", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeStability}I believe it would be in the best interest of the realm.",
                FiefSelectedCandidateIsOtherCondition, () => BlockFiefPersuasionOption("stability"), 100,
                (out TextObject hintText) => FiefPersuasionOptionClickable("stability", out hintText),
                () => BuildFiefPersuasionOption("stability"));

            starter.AddPlayerLine("fief_persuasion_stability_self", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeStabilitySelf}I believe it would be in the best interest of the realm.",
                FiefSelectedCandidateIsPlayerCondition, () => BlockFiefPersuasionOption("stability"), 101,
                (out TextObject hintText) => FiefPersuasionOptionClickable("stability", out hintText),
                () => BuildFiefPersuasionOption("stability"));

            starter.AddPlayerLine("fief_persuasion_faction_shared", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeFaction}Our faction should stand together in this vote.",
                () => FiefSelectedCandidateIsOtherCondition() && PlayerSharesFactionWithCurrentVoter(),
                () => BlockFiefPersuasionOption("faction"), 90,
                (out TextObject hintText) => FiefPersuasionOptionClickable("faction", out hintText),
                () => BuildFiefPersuasionOption("faction"));

            starter.AddPlayerLine("fief_persuasion_faction_other", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeFactionOther}Your faction should stand together in this vote.",
                () => FiefSelectedCandidateIsOtherCondition() && !PlayerSharesFactionWithCurrentVoter(),
                () => BlockFiefPersuasionOption("faction"), 89,
                (out TextObject hintText) => FiefPersuasionOptionClickable("faction", out hintText),
                () => BuildFiefPersuasionOption("faction"));

            starter.AddPlayerLine("fief_persuasion_faction_self_shared", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeFactionSelf}Our faction should stand behind my claim.",
                () => FiefSelectedCandidateIsPlayerCondition() && PlayerSharesFactionWithCurrentVoter(),
                () => BlockFiefPersuasionOption("faction"), 91,
                (out TextObject hintText) => FiefPersuasionOptionClickable("faction", out hintText),
                () => BuildFiefPersuasionOption("faction"));

            starter.AddPlayerLine("fief_persuasion_faction_self_other", "fief_persuasion_arguments", "fief_persuasion_result",
                "{=BC_Fief_Delib_PersuadeFactionSelfOther}Your faction should stand behind my claim.",
                () => FiefSelectedCandidateIsPlayerCondition() && !PlayerSharesFactionWithCurrentVoter(),
                () => BlockFiefPersuasionOption("faction"), 88,
                (out TextObject hintText) => FiefPersuasionOptionClickable("faction", out hintText),
                () => BuildFiefPersuasionOption("faction"));

            starter.AddPlayerLine("fief_persuasion_withdraw", "fief_persuasion_arguments", "lord_pretalk",
                "{=BC_Fief_Delib_PersuasionWithdraw}I have said enough. Let us leave the matter there.",
                null, ApplyFiefPersuasionFailure, 10, null, null);

            starter.AddDialogLine("fief_persuasion_success", "fief_persuasion_result", "lord_pretalk",
                "{=BC_Fief_Delib_PersuasionSuccess}Very well. I will support your candidate when the vote is called.",
                FiefPersuasionSucceededCondition,
                ApplyFiefPersuasionSuccess,
                120, null);

            starter.AddDialogLine("fief_persuasion_failed", "fief_persuasion_result", "lord_pretalk",
                "{=BC_Fief_Delib_PersuasionFailed}No. I have heard your argument, and I remain unconvinced.",
                FiefPersuasionFailedCondition,
                ApplyFiefPersuasionFailure,
                110, null);

            starter.AddDialogLine("fief_persuasion_continue", "fief_persuasion_result", "fief_persuasion_arguments",
                "{=BC_Fief_Delib_PersuasionContinue}You have not convinced me yet. Say what else you must.",
                FiefPersuasionCanContinueCondition, null, 100, null);
        }


        private static string FiefSlotQuestionText(int slot) =>
            slot == 0 ? "{=BC_Fief_Delib_DirectQuestion0}Whom do you think should receive the fief of {FI0S}?"
          : slot == 1 ? "{=BC_Fief_Delib_DirectQuestion1}Whom do you think should receive the fief of {FI1S}?"
          : slot == 2 ? "{=BC_Fief_Delib_DirectQuestion2}Whom do you think should receive the fief of {FI2S}?"
                      : "{=BC_Fief_Delib_DirectQuestion3}Whom do you think should receive the fief of {FI3S}?";

        private bool SetFiefDialogueAddress()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc == null)
                return false;

            MBTextManager.SetTextVariable(
                "ADDRESS",
                npc.IsFemale
                    ? new TextObject("{=BC_Address_Lady}lady")
                    : new TextObject("{=BC_Address_Lord}lord"));
            return true;
        }

        private bool NoFiefDialogueAddress()
        {
            return Hero.OneToOneConversationHero == null;
        }

        private static string FiefCandidatePushText(int slot) =>
            slot == 0 ? "{=BC_Fief_Delib_PushCandidate0}I would prefer {FC0N} to receive this fief."
          : slot == 1 ? "{=BC_Fief_Delib_PushCandidate1}I would prefer {FC1N} to receive this fief."
          : slot == 2 ? "{=BC_Fief_Delib_PushCandidate2}I would prefer {FC2N} to receive this fief."
          : slot == 3 ? "{=BC_Fief_Delib_PushCandidate3}I would prefer {FC3N} to receive this fief."
                      : "{=BC_Fief_Delib_PushCandidate4}I would prefer {FC4N} to receive this fief.";

        private bool FiefMenuEntryCondition()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return false;
            if (npc.Clan == Clan.PlayerClan) return false;
            if (Clan.PlayerClan.Kingdom == null) return false;
            if (npc.Clan.Kingdom != Clan.PlayerClan.Kingdom) return false;
            RefreshFiefConversationPendingKeys();
            return _conversationPendingKeys.Count > 0;
        }

        private void BeginFiefPendingSelection()
        {
            _conversationPendingPage = 0;
            RefreshFiefConversationPendingKeys();
        }

        private void ClearFiefPendingSelection()
        {
            _conversationPendingPage = 0;
            _conversationPendingKeys.Clear();
        }

        private void RefreshFiefConversationPendingKeys()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null)
            {
                _conversationPendingKeys.Clear();
                _conversationPendingPage = 0;
                return;
            }

            string prefix = KingdomPrefix(kingdom);
            _conversationPendingKeys = _pendingFiefDate
                .Where(kv => kv.Key.StartsWith(prefix) && TryResolvePendingFiefForConversation(kingdom, kv.Key, out _))
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            int maxPage = Math.Max(0, (_conversationPendingKeys.Count - 1) / DeliberationDialogueHelper.PageSize);
            _conversationPendingPage = Math.Min(_conversationPendingPage, maxPage);
        }

        private static bool TryResolvePendingFiefForConversation(Kingdom kingdom, string pendingKey, out Settlement settlement)
        {
            settlement = null;
            if (kingdom == null || string.IsNullOrEmpty(pendingKey)) return false;

            string prefix = KingdomPrefix(kingdom);
            if (!pendingKey.StartsWith(prefix, StringComparison.Ordinal)) return false;

            string settlementId = pendingKey.Substring(prefix.Length);
            settlement = Settlement.All.FirstOrDefault(s => s.StringId == settlementId);
            return settlement != null && settlement.IsFortification && settlement.MapFaction == kingdom;
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

        private bool FiefMenuSlotCondition(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return false;
            if (!TryResolvePendingFiefForConversation(Clan.PlayerClan?.Kingdom, _conversationPendingKeys[index], out Settlement settlement)) return false;

            MBTextManager.SetTextVariable($"FI{slot}S", settlement.Name);
            return true;
        }

        private void FiefMenuSlotAction(int slot)
        {
            int index = (_conversationPendingPage * DeliberationDialogueHelper.PageSize) + slot;
            if (index < 0 || index >= _conversationPendingKeys.Count) return;
            if (!TryResolvePendingFiefForConversation(Clan.PlayerClan?.Kingdom, _conversationPendingKeys[index], out Settlement settlement)) return;

            _currentQuerySettlement   = settlement;
            _selectedBribeCandidateClanId = "";

            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null) return;
            _currentQueryVoterClan = npc.Clan;

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();

            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(npc.Clan);
            _queriedFaction = voterFaction != null ? (int)voterFaction.Type : -1;

            _currentQueryAlreadyBribed = kingdom != null
                && (_fiefBribedVotes.ContainsKey(BribeKey(kingdom, settlement, npc.Clan))
                    || _fiefBribedCandidateVotes.ContainsKey(BribeKey(kingdom, settlement, npc.Clan)));

            _currentQueryNominee = GetNominatedCandidate(kingdom, settlement, npc.Clan);
            _currentQueryCandidates = GetCurrentNominationRanking(kingdom, settlement, 5);

            string selfCandidateBlock = GetPlayerCandidateBlockReason();
            if (!string.IsNullOrEmpty(selfCandidateBlock))
            {
                BellumCivileDebug.Trace(
                    "fief vote",
                    $"player self-nomination option blocked for {settlement.StringId}: {selfCandidateBlock}; playerClan={Clan.PlayerClan?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"} excluded={GetCurrentExcludedClan()?.StringId ?? "null"} alreadyBribed={_currentQueryAlreadyBribed}.",
                    requestInGameDisplay: true);
            }

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", settlement.Name?.ToString() ?? settlement.StringId);
            MBTextManager.SetTextVariable("CANDIDATE_NAME", _currentQueryNominee?.Leader?.Name?.ToString()
                ?? _currentQueryNominee?.Name?.ToString()
                ?? "");
            MBTextManager.SetTextVariable("REASON_TEXT", GetNominationReasonText(kingdom, settlement, npc.Clan));
        }

        private bool FiefPendingSelectionHasMore() =>
            ((_conversationPendingPage + 1) * DeliberationDialogueHelper.PageSize) < _conversationPendingKeys.Count;

        private void AdvanceFiefPendingSelectionPage()
        {
            _conversationPendingPage++;
            RefreshFiefConversationPendingKeys();
        }

        private bool FiefCandidatePushCondition(int slot)
        {
            if (_currentQueryAlreadyBribed || _currentQuerySettlement == null)
                return false;

            Clan candidate = GetCandidateFromSelectionPage(slot);
            if (candidate?.Leader == null)
                return false;

            MBTextManager.SetTextVariable($"FC{slot}N", candidate.Leader.Name?.ToString() ?? candidate.Name?.ToString() ?? candidate.StringId);
            return true;
        }

        private void FiefCandidatePushAction(int slot)
        {
            Clan candidate = GetCandidateFromSelectionPage(slot);
            _selectedBribeCandidateClanId = candidate?.StringId ?? "";
        }

        private void BeginFiefCandidateSelection()
        {
            _candidateSelectionPage = 0;
            _selectedBribeCandidateClanId = "";
        }

        private void ClearFiefCandidateSelection()
        {
            _candidateSelectionPage = 0;
            _selectedBribeCandidateClanId = "";
        }

        private Clan GetCandidateFromSelectionPage(int slot)
        {
            if (slot < 0)
                return null;

            List<Clan> candidates = GetSelectableFiefCandidates();
            int index = _candidateSelectionPage * 5 + slot;
            return index >= 0 && index < candidates.Count ? candidates[index] : null;
        }

        private bool FiefCandidateListMoreCondition()
        {
            List<Clan> candidates = GetSelectableFiefCandidates();
            return (_candidateSelectionPage + 1) * 5 < candidates.Count;
        }

        private void AdvanceFiefCandidateListPage()
        {
            _candidateSelectionPage++;
        }

        private bool FiefPlayerCandidatePushCondition()
        {
            if (HasSelectedFiefCandidate())
                return false;

            return string.IsNullOrEmpty(GetPlayerCandidateBlockReason());
        }

        private void FiefPlayerCandidatePushAction()
        {
            _selectedBribeCandidateClanId = Clan.PlayerClan?.StringId ?? "";
        }

        private bool FiefSelectedCandidateCondition()
        {
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (candidate?.Leader == null || _currentQuerySettlement == null)
                return false;

            MBTextManager.SetTextVariable("SELECTED_CANDIDATE_NAME", candidate.Leader.Name);
            return true;
        }

        private bool HasSelectedFiefCandidate()
        {
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            return candidate?.Leader != null && _currentQuerySettlement != null;
        }

        private bool FiefSelectedCandidateIsPlayerCondition()
        {
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (candidate?.Leader == null || _currentQuerySettlement == null || candidate != Clan.PlayerClan)
                return false;

            MBTextManager.SetTextVariable("SELECTED_CANDIDATE_NAME", candidate.Leader.Name);
            return true;
        }

        private bool FiefSelectedCandidateIsOtherCondition()
        {
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (candidate?.Leader == null || _currentQuerySettlement == null || candidate == Clan.PlayerClan)
                return false;

            MBTextManager.SetTextVariable("SELECTED_CANDIDATE_NAME", candidate.Leader.Name);
            return true;
        }

        private bool FiefSelectedCandidateHasLegalClaimSelfCondition()
        {
            return FiefSelectedCandidateIsPlayerCondition()
                && SelectedCandidateHasLegalClaim();
        }

        private bool FiefSelectedCandidateHasLegalClaimOtherCondition()
        {
            return FiefSelectedCandidateIsOtherCondition()
                && SelectedCandidateHasLegalClaim();
        }

        private bool FiefSelectedCandidateParticipatedInSiegeSelfCondition()
        {
            return FiefSelectedCandidateIsPlayerCondition()
                && SelectedCandidateParticipatedInSiege(out _);
        }

        private bool FiefSelectedCandidateParticipatedInSiegeOtherCondition()
        {
            return FiefSelectedCandidateIsOtherCondition()
                && SelectedCandidateParticipatedInSiege(out _);
        }

        private bool SelectedCandidateHasLegalClaim()
        {
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (candidate?.Leader == null || _currentQuerySettlement == null)
                return false;

            return FiefNominationHelper.CalculateLegalClaimScore(voter, candidate, _currentQuerySettlement).HasClaim;
        }

        private bool PlayerSharesFactionWithCurrentVoter()
        {
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            if (voter == null || Clan.PlayerClan == null)
                return false;

            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject playerFaction = factionManager?.GetIdeologicalFaction(Clan.PlayerClan);
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voter);

            return playerFaction != null && voterFaction != null && playerFaction.Type == voterFaction.Type;
        }

        private bool FiefPersuasionClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            if (voter?.Leader == null || kingdom == null || settlement == null)
            {
                explanation = new TextObject("{=BC_Fief_Delib_PersuasionUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(voter, kingdom, VotePledgeService.FiefKey(kingdom, settlement, voter), out explanation))
                return false;

            string key = BribeKey(kingdom, settlement, voter);
            if (_fiefPersuasionFailed.ContainsKey(key))
            {
                explanation = new TextObject("{=BC_Fief_Delib_PersuasionAlreadyFailed}They have already rejected your argument in this matter.");
                return false;
            }

            int relation = Hero.MainHero?.GetRelation(voter.Leader) ?? 0;
            if (relation < 30)
            {
                explanation = new TextObject("{=BC_Fief_Delib_PersuasionLowRelation}They do not trust you enough to be swayed by argument. Relation required: 30.");
                return false;
            }

            return true;
        }

        private bool FiefBribeClickableCondition(out TextObject explanation)
        {
            explanation = new TextObject("");

            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            if (voter?.Leader == null || candidate?.Leader == null || kingdom == null || settlement == null)
            {
                explanation = new TextObject("{=BC_Fief_Delib_BribeUnavailable}This matter is no longer available.");
                return false;
            }

            if (!VotePledgeService.CanPromise(voter, kingdom, VotePledgeService.FiefKey(kingdom, settlement, voter), out explanation))
                return false;

            if (_currentQueryAlreadyBribed)
            {
                explanation = new TextObject("{=BC_Fief_Delib_BribeAlreadyCommitted}They have already committed their vote in this matter.");
                return false;
            }

            if (_currentQueryNominee == candidate)
                return true;

            float openness = CalculateFiefBribeOpenness(voter, candidate, kingdom, settlement);
            if (openness >= C.FiefBribeOpennessThreshold)
                return true;

            int honor = voter.Leader.GetTraitLevel(DefaultTraits.Honor);
            int mercy = voter.Leader.GetTraitLevel(DefaultTraits.Mercy);
            int generosity = voter.Leader.GetTraitLevel(DefaultTraits.Generosity);

            if (honor >= 1)
                explanation = new TextObject("{=BC_Fief_Delib_BribeHonorRefusal}They consider this vote a matter of honor and will not bargain over it.");
            else if (mercy >= 1 || generosity >= 1)
                explanation = new TextObject("{=BC_Fief_Delib_BribePrincipledRefusal}They are not inclined to trade favors over the fate of another clan's lands.");
            else if (_currentQueryNominee?.Leader != null && voter.Leader.GetRelation(_currentQueryNominee.Leader) >= 30)
                explanation = new TextObject("{=BC_Fief_Delib_BribeLoyalRefusal}They are too committed to their chosen candidate to discuss changing sides.");
            else
                explanation = new TextObject("{=BC_Fief_Delib_BribeRefusal}They are not willing to bargain over this vote.");

            return false;
        }

        private float CalculateFiefBribeOpenness(Clan voter, Clan candidate, Kingdom kingdom, Settlement settlement)
        {
            if (voter?.Leader == null || candidate?.Leader == null || kingdom == null || settlement == null)
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

            openness += voterLeader.GetRelation(candidate.Leader) * C.FiefBribeSelectedRelationScale;

            if (_currentQueryNominee?.Leader != null && _currentQueryNominee != candidate)
                openness -= Math.Max(0, voterLeader.GetRelation(_currentQueryNominee.Leader) - 20) * C.FiefBribeNomineeRelationScale;

            string pendingKey = PendingKey(kingdom, settlement);
            Clan capturer = ResolveStoredCapturerClan(pendingKey);
            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FiefNominationResult selectedScore = FiefNominationHelper.ScoreCandidate(
                voter, candidate, kingdom, settlement, capturer, factionManager);
            FiefNominationResult nomineeScore = _currentQueryNominee != null
                ? FiefNominationHelper.ScoreCandidate(voter, _currentQueryNominee, kingdom, settlement, capturer, factionManager)
                : null;

            if (selectedScore != null && nomineeScore != null)
            {
                float gap = selectedScore.Score - nomineeScore.Score;
                if (gap >= -20f)
                    openness += C.FiefBribeLegitimateClaimBonus;
                else if (gap <= -80f)
                    openness -= C.FiefBribeWeakClaimPenalty;
            }
            else if (selectedScore != null && selectedScore.Score > 0f)
            {
                openness += C.FiefBribeLegitimateClaimBonus;
            }

            return openness;
        }

        private void StartFiefVotePersuasion()
        {
            _fiefPersuasionOptions.Clear();
            ConversationManager.StartPersuasion(
                goalValue: C.FiefPersuasionGoal,
                successValue: C.FiefPersuasionSuccessValue,
                failValue: 0f,
                criticalSuccessValue: C.FiefPersuasionCriticalSuccessValue,
                criticalFailValue: C.FiefPersuasionCriticalFailValue,
                initialProgress: 0f,
                difficulty: PersuasionDifficulty.Medium);
        }

        private PersuasionOptionArgs BuildFiefPersuasionOption(string argumentType)
        {
            if (_fiefPersuasionOptions.TryGetValue(argumentType, out PersuasionOptionArgs cachedOption))
                return cachedOption;

            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;

            TraitObject trait = DefaultTraits.Honor;
            TraitEffect effect = TraitEffect.Positive;
            PersuasionArgumentStrength strength = PersuasionArgumentStrength.Normal;
            TextObject line = new TextObject("{=BC_Fief_Delib_PersuadeGeneric}This is the wisest course for the realm.");
            Tuple<TraitObject, int>[] correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };

            if (argumentType == "capturer")
            {
                trait = DefaultTraits.Valor;
                line = candidate == Clan.PlayerClan
                    ? new TextObject("{=BC_Fief_Delib_PersuadeCapturerSelf}I helped take those walls by force. Such deeds deserve reward.")
                    : new TextObject("{=BC_Fief_Delib_PersuadeCapturer}They took the walls by force. Such deeds deserve reward.");
                correlations = new[] { Tuple.Create(DefaultTraits.Valor, 1), Tuple.Create(DefaultTraits.Honor, 1) };
                bool participated = SelectedCandidateParticipatedInSiege(out bool wasCapturer);
                strength = wasCapturer
                    ? PersuasionArgumentStrength.Easy
                    : participated
                        ? PersuasionArgumentStrength.Normal
                        : PersuasionArgumentStrength.VeryHard;
            }
            else if (argumentType == "need")
            {
                trait = DefaultTraits.Generosity;
                line = candidate == Clan.PlayerClan
                    ? new TextObject("{=BC_Fief_Delib_PersuadeNeedSelf}My house lacks the lands expected of its station.")
                    : new TextObject("{=BC_Fief_Delib_PersuadeNeed}Their house lacks the lands expected of its station.");
                correlations = new[] { Tuple.Create(DefaultTraits.Generosity, 1), Tuple.Create(DefaultTraits.Mercy, 1) };

                int fiefDelta = candidate != null ? FactionObject.CalculateDesiredFiefs(candidate) - candidate.Fiefs.Count : 0;
                strength = fiefDelta > 0
                    ? PersuasionArgumentStrength.Easy
                    : PersuasionArgumentStrength.Hard;
            }
            else if (argumentType == "legal_claim")
            {
                trait = DefaultTraits.Honor;
                line = candidate == Clan.PlayerClan
                    ? new TextObject("{=BC_Fief_Delib_PersuadeLegalClaimSelf}My house has a lawful claim to these lands.")
                    : new TextObject("{=BC_Fief_Delib_PersuadeLegalClaim}Their house has a lawful claim to these lands.");
                correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1), Tuple.Create(DefaultTraits.Calculating, 1) };

                FiefLegalClaimScore legalClaim = FiefNominationHelper.CalculateLegalClaimScore(voter, candidate, settlement);
                if (legalClaim.IsDeJureHolder)
                    strength = PersuasionArgumentStrength.Easy;
                else if (legalClaim.Strength == FeudalClaimStrength.Strong)
                    strength = legalClaim.IsParentTitleClaim
                        ? PersuasionArgumentStrength.Normal
                        : PersuasionArgumentStrength.Easy;
                else if (legalClaim.Strength == FeudalClaimStrength.Weak)
                    strength = legalClaim.IsParentTitleClaim
                        ? PersuasionArgumentStrength.Hard
                        : PersuasionArgumentStrength.Normal;
                else
                    strength = PersuasionArgumentStrength.VeryHard;
            }
            else if (argumentType == "stability")
            {
                trait = DefaultTraits.Calculating;
                line = candidate == Clan.PlayerClan
                    ? new TextObject("{=BC_Fief_Delib_PersuadeStabilitySelf}Granting this fief to my clan would strengthen the realm and quiet discontent.")
                    : new TextObject("{=BC_Fief_Delib_PersuadeStability}This grant would strengthen the realm and quiet discontent.");
                correlations = new[] { Tuple.Create(DefaultTraits.Calculating, 1), Tuple.Create(DefaultTraits.Honor, 1) };
                strength = PersuasionArgumentStrength.Normal;
            }
            else if (argumentType == "faction")
            {
                trait = DefaultTraits.Honor;
                bool sharedFaction = PlayerSharesFactionWithCurrentVoter();
                if (candidate == Clan.PlayerClan)
                {
                    line = sharedFaction
                        ? new TextObject("{=BC_Fief_Delib_PersuadeFactionSelf}Our faction should stand behind my claim.")
                        : new TextObject("{=BC_Fief_Delib_PersuadeFactionSelfOther}Your faction should stand behind my claim.");
                }
                else
                {
                    line = sharedFaction
                        ? new TextObject("{=BC_Fief_Delib_PersuadeFaction}Our faction should stand together in this vote.")
                        : new TextObject("{=BC_Fief_Delib_PersuadeFactionOther}Your faction should stand together in this vote.");
                }
                correlations = new[] { Tuple.Create(DefaultTraits.Honor, 1) };
                var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voter);
                FactionObject candidateFaction = factionManager?.GetIdeologicalFaction(candidate);
                if (voterFaction != null && candidateFaction != null && voterFaction.Type == candidateFaction.Type)
                    strength = PersuasionArgumentStrength.Easy;
                else
                    strength = PersuasionArgumentStrength.Hard;
            }

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

            _fiefPersuasionOptions[argumentType] = option;
            return option;
        }

        private void BlockFiefPersuasionOption(string argumentType)
        {
            BuildFiefPersuasionOption(argumentType)?.BlockTheOption(true);
        }

        private bool FiefPersuasionOptionClickable(string argumentType, out TextObject hintText)
        {
            PersuasionOptionArgs option = BuildFiefPersuasionOption(argumentType);
            if (option == null || !option.IsBlocked)
            {
                hintText = null;
                return true;
            }

            hintText = new TextObject("{=9ACJsI6S}Blocked");
            return false;
        }

        private bool FiefPersuasionSucceededCondition()
        {
            return ConversationManager.GetPersuasionProgressSatisfied();
        }

        private bool FiefPersuasionFailedCondition()
        {
            return ConversationManager.GetPersuasionIsFailure()
                || FiefPersuasionLastRollWasCriticalFailure()
                || FiefPersuasionArgumentsExhausted();
        }

        private bool FiefPersuasionCanContinueCondition()
        {
            return !FiefPersuasionSucceededCondition()
                && !ConversationManager.GetPersuasionIsFailure()
                && !FiefPersuasionArgumentsExhausted();
        }

        private bool FiefPersuasionArgumentsExhausted()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            int chosenCount = ConversationManager.GetPersuasionChosenOptions()?.Count() ?? 0;
            int availableCount = GetAvailableFiefPersuasionArgumentCount();
            return (availableCount == 0 || chosenCount >= availableCount)
                && !ConversationManager.GetPersuasionProgressSatisfied();
        }

        private int GetAvailableFiefPersuasionArgumentCount()
        {
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);
            if (candidate?.Leader == null || _currentQuerySettlement == null)
                return 0;

            int count = 3; // Need, stability, and faction arguments are always available.
            if (SelectedCandidateParticipatedInSiege(out _))
                count++;
            if (SelectedCandidateHasLegalClaim())
                count++;

            return count;
        }

        private bool FiefPersuasionLastRollWasCriticalFailure()
        {
            if (!ConversationManager.GetPersuasionIsActive())
                return false;

            Tuple<PersuasionOptionArgs, PersuasionOptionResult> lastChoice =
                ConversationManager.GetPersuasionChosenOptions()?.LastOrDefault();

            return lastChoice != null && lastChoice.Item2 == PersuasionOptionResult.CriticalFailure;
        }

        private void ApplyFiefPersuasionSuccess()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Clan candidate = ResolveClan(_selectedBribeCandidateClanId);

            if (kingdom != null && settlement != null && voter != null && candidate != null)
                VotePledgeService.TryCommit(voter, kingdom, VotePledgeService.FiefKey(kingdom, settlement, voter),
                    () => SetBribedCandidateVote(kingdom, settlement, voter, candidate));

            ConversationManager.EndPersuasion();
            _fiefPersuasionOptions.Clear();
        }

        private void ApplyFiefPersuasionFailure()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;

            if (kingdom != null && settlement != null && voter != null)
                _fiefPersuasionFailed[BribeKey(kingdom, settlement, voter)] = true;

            ConversationManager.EndPersuasion();
            _fiefPersuasionOptions.Clear();
        }

        private bool FiefNpcSelfPushCondition()
        {
            if (HasSelectedFiefCandidate())
                return false;

            if (_currentQueryAlreadyBribed || _currentQuerySettlement == null)
                return false;

            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null || voter == null)
                return false;

            string pendingKey = PendingKey(kingdom, _currentQuerySettlement);
            if (_fiefSelfEncouragementUsed.ContainsKey(pendingKey))
                return false;

            return FiefNominationHelper.IsValidCandidate(voter, kingdom, GetCurrentExcludedClan());
        }

        private void ApplyNpcSelfNominationEncouragement()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;

            if (kingdom == null || settlement == null || voter?.Leader == null || Hero.MainHero == null)
                return;

            string pendingKey = PendingKey(kingdom, settlement);
            if (_fiefSelfEncouragementUsed.ContainsKey(pendingKey))
                return;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, voter.Leader, 1, false);
            _fiefSelfEncouragementUsed[pendingKey] = true;

            string key = BribeKey(kingdom, settlement, voter);
            float currentScore = _fiefNominationScores.TryGetValue(key, out float storedScore) ? storedScore : 0f;
            FiefNominationResult selfScore = FiefNominationHelper.ScoreCandidate(
                voter,
                voter,
                kingdom,
                settlement,
                ResolveStoredCapturerClan(pendingKey),
                Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>());

            if (selfScore != null && selfScore.Score + 20f >= currentScore)
            {
                _fiefNomineeByVoter[key] = voter.StringId;
                _fiefNominationReasons[key] = "self";
                _fiefNominationScores[key] = selfScore.Score + 20f;
                _fiefCommittedNominationKeys[key] = true;
            }

            TextObject text = new TextObject("{=BC_Fief_Delib_NpcSelfEncouraged}{LORD_NAME} receives your support with visible satisfaction.");
            text.SetTextVariable("LORD_NAME", voter.Leader.Name);
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Success);
        }

        private bool FiefOtherCandidatePushCondition()
        {
            if (HasSelectedFiefCandidate())
                return false;

            if (_currentQueryAlreadyBribed || _currentQuerySettlement == null || Clan.PlayerClan?.Kingdom == null)
                return false;

            return GetSelectableFiefCandidates().Count > 0;
        }

        private void OpenFiefCandidateSelectionInquiry()
        {
            List<Clan> candidates = GetSelectableFiefCandidates();
            if (candidates.Count == 0)
                return;

            List<InquiryElement> elements = candidates
                .Select(clan => new InquiryElement(
                    clan.StringId,
                    clan.Leader?.Name?.ToString() ?? clan.Name?.ToString() ?? clan.StringId,
                    null,
                    true,
                    BuildCandidateSelectionHint(clan)))
                .ToList();

            TextObject title = new TextObject("{=BC_Fief_Delib_SelectCandidateTitle}Choose a Candidate");
            TextObject description = new TextObject("{=BC_Fief_Delib_SelectCandidateDesc}Select the lord you want to persuade {LORD_NAME} to support for {SETTLEMENT_NAME}.");
            description.SetTextVariable("LORD_NAME", (_currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan)?.Leader?.Name ?? new TextObject(""));
            description.SetTextVariable("SETTLEMENT_NAME", _currentQuerySettlement?.Name ?? new TextObject(""));

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                title.ToString(),
                description.ToString(),
                elements,
                true,
                1,
                1,
                new TextObject("{=BC_Fief_Delib_SelectCandidateAccept}Select").ToString(),
                new TextObject("{=BC_Fief_Delib_SelectCandidateCancel}Cancel").ToString(),
                OnFiefCandidateSelected,
                null);

            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private void OnFiefCandidateSelected(List<InquiryElement> selectedElements)
        {
            if (selectedElements == null || selectedElements.Count == 0)
                return;

            string candidateId = selectedElements[0].Identifier as string;
            Clan candidate = ResolveClan(candidateId);
            if (candidate == null)
                return;

            _selectedBribeCandidateClanId = candidate.StringId;

            TextObject text = new TextObject("{=BC_Fief_Delib_CandidateSelected}You have chosen to press the claim of {CANDIDATE_NAME}.");
            text.SetTextVariable("CANDIDATE_NAME", candidate.Leader?.Name ?? candidate.Name ?? new TextObject(""));
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Neutral);
        }

        private List<Clan> GetSelectableFiefCandidates()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;

            if (kingdom == null || settlement == null || voter == null)
                return new List<Clan>();

            Clan excluded = GetCurrentExcludedClan();
            Clan capturerClan = ResolveStoredCapturerClan(PendingKey(kingdom, settlement));
            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();

            return FiefNominationHelper.GetEligibleCandidates(kingdom, settlement, excluded)
                .Where(c => c != Clan.PlayerClan && c != voter)
                .Select(c => new
                {
                    Clan = c,
                    Score = FiefNominationHelper.ScoreCandidate(voter, c, kingdom, settlement, capturerClan, factionManager)?.Score ?? 0f
                })
                .OrderByDescending(c => c.Score)
                .Select(c => c.Clan)
                .ToList();
        }

        private string BuildCandidateSelectionHint(Clan candidate)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            Clan voter = _currentQueryVoterClan ?? Hero.OneToOneConversationHero?.Clan;

            if (kingdom == null || settlement == null || voter == null || candidate == null)
                return "";

            FiefNominationResult result = FiefNominationHelper.ScoreCandidate(
                voter,
                candidate,
                kingdom,
                settlement,
                ResolveStoredCapturerClan(PendingKey(kingdom, settlement)),
                Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>());

            return FiefNominationHelper.BuildReasonText(result?.Reasons, candidate, voter);
        }

        private Clan GetCurrentExcludedClan()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement settlement = _currentQuerySettlement;
            if (kingdom == null || settlement == null)
                return null;

            string pendingKey = PendingKey(kingdom, settlement);
            return _pendingFiefExclude.TryGetValue(pendingKey, out string excludedId)
                ? ResolveClan(excludedId)
                : null;
        }

        private string GetPlayerCandidateBlockReason()
        {
            if (_currentQueryAlreadyBribed) return "lord_already_committed";
            if (_currentQuerySettlement == null) return "no_selected_pending_fief";
            if (Clan.PlayerClan == null) return "missing_player_clan";
            if (Clan.PlayerClan.Leader == null) return "player_clan_missing_leader";

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null) return "player_not_in_kingdom";

            Clan excluded = GetCurrentExcludedClan();
            if (Clan.PlayerClan == excluded) return "player_clan_excluded_from_this_vote";
            if (Clan.PlayerClan.Kingdom != kingdom) return "player_wrong_kingdom";
            if (Clan.PlayerClan.IsEliminated) return "player_clan_eliminated";
            if (Clan.PlayerClan.IsUnderMercenaryService) return "player_clan_mercenary";

            return "";
        }

        private bool FactionExistsInKingdom(FactionType factionType)
        {
            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null) return false;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;
            return factionManager.GetFactionsInKingdom(kingdom)
                .Any(f => f.IsIdeology && f.Type == factionType && f.Members.Count > 0);
        }

        private void LaunchFiefBribeBarter()
        {
            Hero       npc        = Hero.OneToOneConversationHero;
            Kingdom    kingdom    = Clan.PlayerClan.Kingdom;
            Settlement settlement = _currentQuerySettlement;

            if (npc?.Clan == null || kingdom == null || settlement == null
                || string.IsNullOrEmpty(_selectedBribeCandidateClanId)) return;

            Clan selectedCandidate = ResolveClan(_selectedBribeCandidateClanId);
            if (selectedCandidate?.Leader == null || selectedCandidate.Kingdom != kingdom)
                return;

            float resistanceScore;
            if (_currentQueryNominee == selectedCandidate)
            {
                resistanceScore = 0f;
            }
            else if (_currentQueryNominee == null)
            {
                resistanceScore = 50f;
            }
            else
            {
                int relationToNatural = _currentQueryNominee.Leader != null
                    ? npc.Clan.Leader.GetRelation(_currentQueryNominee.Leader)
                    : 0;
                int relationToSelected = npc.Clan.Leader.GetRelation(selectedCandidate.Leader);

                resistanceScore = 75f;
                if (relationToNatural - relationToSelected > 30)
                    resistanceScore += 50f;
            }

            var bribeItem = new FiefVoteBribeBarterable(
                npc.Clan, kingdom, settlement, selectedCandidate, resistanceScore, Hero.MainHero);

            BarterManager.Instance.StartBarterOffer(
                Hero.MainHero,
                npc,
                PartyBase.MainParty,
                npc.PartyBelongedTo?.Party,
                null,
                (Barterable barterable, BarterData args, object obj) =>
                {
                    args.AddBarterable<FiefVoteBribeBarterable>(bribeItem);

                    foreach (Settlement s in Hero.MainHero.Clan.Settlements)
                        if (s.IsTown || s.IsCastle)
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(s, Hero.MainHero, npc));

                    return true;
                },
                0,
                false,
                new Barterable[] { bribeItem });
        }

    }
}
