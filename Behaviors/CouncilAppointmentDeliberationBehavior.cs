using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class CouncilAppointmentDeliberationBehavior : CampaignBehaviorBase
    {

        public static CouncilAppointmentDeliberationBehavior Current =>
            Campaign.Current?.GetCampaignBehavior<CouncilAppointmentDeliberationBehavior>();

        private Dictionary<string, string> _pendingProposerClan = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingCourtAgendaIds = new Dictionary<string, string>();
        private Dictionary<string, int> _pendingFactionType = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _pendingVoteDate = new Dictionary<string, CampaignTime>();
        private Dictionary<string, float> _pendingCreatedDay = new Dictionary<string, float>();
        private Dictionary<string, int> _pendingRetryCount = new Dictionary<string, int>();
        private readonly HashSet<string> _candidateWaitLogged = new HashSet<string>();
        private Dictionary<string, bool> _activeDecisionKeys = new Dictionary<string, bool>();
        private List<string> _pendingOfficeSettlements = new List<string>();
        private Dictionary<string, string> _nomineeByVoter = new Dictionary<string, string>();
        private Dictionary<string, string> _nominationReasons = new Dictionary<string, string>();
        private Dictionary<string, float> _nominationScores = new Dictionary<string, float>();
        private Dictionary<string, bool> _committedVotes = new Dictionary<string, bool>();
        private Dictionary<string, bool> _persuasionFailures = new Dictionary<string, bool>();

        private Kingdom _conversationKingdom;
        private PrivyCouncilOffice _conversationOffice;
        private Clan _conversationVoter;
        private Clan _conversationNominee;
        private Clan _selectedCandidate;
        private List<Clan> _candidatePage = new List<Clan>();
        private int _candidatePageIndex;
        private bool _persuasionSucceeded;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnKingdomDecisionConcluded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            VotePledgeService.Invalidate();
            dataStore.SyncData("BellumCivile_CouncilDelib_Proposer", ref _pendingProposerClan);
            dataStore.SyncData("BC_CouncilDelib_AgendaIds", ref _pendingCourtAgendaIds);
            _pendingCourtAgendaIds = _pendingCourtAgendaIds ?? new Dictionary<string, string>();
            dataStore.SyncData("BellumCivile_CouncilDelib_Faction", ref _pendingFactionType);
            dataStore.SyncData("BellumCivile_CouncilDelib_Date", ref _pendingVoteDate);
            dataStore.SyncData("BellumCivile_CouncilDelib_Created", ref _pendingCreatedDay);
            dataStore.SyncData("BellumCivile_CouncilDelib_Retries", ref _pendingRetryCount);
            dataStore.SyncData("BellumCivile_CouncilDelib_Active", ref _activeDecisionKeys);
            dataStore.SyncData("BC_CouncilDelib_PendingSettlements", ref _pendingOfficeSettlements);
            dataStore.SyncData("BellumCivile_CouncilDelib_Nominees", ref _nomineeByVoter);
            dataStore.SyncData("BellumCivile_CouncilDelib_Reasons", ref _nominationReasons);
            dataStore.SyncData("BellumCivile_CouncilDelib_Scores", ref _nominationScores);
            dataStore.SyncData("BellumCivile_CouncilDelib_Committed", ref _committedVotes);
            dataStore.SyncData("BellumCivile_CouncilDelib_PersuasionFailed", ref _persuasionFailures);

            _pendingProposerClan = _pendingProposerClan ?? new Dictionary<string, string>();
            _pendingFactionType = _pendingFactionType ?? new Dictionary<string, int>();
            _pendingVoteDate = _pendingVoteDate ?? new Dictionary<string, CampaignTime>();
            _pendingCreatedDay = _pendingCreatedDay ?? new Dictionary<string, float>();
            _pendingRetryCount = _pendingRetryCount ?? new Dictionary<string, int>();
            _activeDecisionKeys = _activeDecisionKeys ?? new Dictionary<string, bool>();
            _pendingOfficeSettlements = _pendingOfficeSettlements ?? new List<string>();
            _nomineeByVoter = _nomineeByVoter ?? new Dictionary<string, string>();
            _nominationReasons = _nominationReasons ?? new Dictionary<string, string>();
            _nominationScores = _nominationScores ?? new Dictionary<string, float>();
            _committedVotes = _committedVotes ?? new Dictionary<string, bool>();
            _persuasionFailures = _persuasionFailures ?? new Dictionary<string, bool>();
        }

        public bool HasPendingAppointment(Kingdom kingdom)
        {
            if (kingdom == null)
                return false;
            string prefix = kingdom.StringId + "|";
            return _pendingVoteDate.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal))
                || _activeDecisionKeys.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        public bool HasPendingAppointment(Kingdom kingdom, PrivyCouncilOffice office)
        {
            string key = BuildPendingKey(kingdom, office);
            return !string.IsNullOrEmpty(key)
                && (_pendingVoteDate.ContainsKey(key) || _activeDecisionKeys.ContainsKey(key));
        }

        public bool IsOfficeMotionOnCooldown(Kingdom kingdom, PrivyCouncilOffice office)
            => CourtAgendaBehavior.Current?.IsCouncilOfficeSettled(kingdom, office) == true;

        public bool TryProposePlayerAppointment(
            Kingdom kingdom, PrivyCouncilOffice office, out TextObject failureReason, Action refresh = null)
        {
            var calendar = CourtAgendaBehavior.Current;
            if (calendar != null && kingdom?.RulingClan == Clan.PlayerClan)
                return calendar.TryPlayerCouncilBusiness(kingdom, office, out failureReason, false, refresh);
            if (calendar != null)
                return calendar.TryNominateCouncilAppointment(kingdom, office, out failureReason, refresh: refresh);
            failureReason = new TextObject("{=BC_CourtAgenda_Unavailable}The court cannot receive a motion at present.");
            return false;
        }

        public bool TryStartFactionAppointment(
            Kingdom kingdom, PrivyCouncilOffice office, Clan proposer, FactionObject faction, out TextObject failureReason)
        {
            failureReason = new TextObject("{=BC_CourtCouncilTermOnly}An appointment must be placed upon your court agenda before its session. Another council proceeding or a settled office cannot be reopened this term.");
            return false;
        }

        // Retained for old callers; new ballots must carry an authorized agenda and date.
        public bool TryStartImmediateAppointment(
            Kingdom kingdom, PrivyCouncilOffice office, Clan proposer, FactionObject faction, string source)
            => false;

        internal bool TryQueueAgendaAppointment(Kingdom kingdom, PrivyCouncilOffice office,
            Clan proposer, FactionObject faction, CampaignTime voteDate, string agendaId, out TextObject failureReason)
        {
            if (string.IsNullOrEmpty(agendaId) || CourtAgendaBehavior.Current?.ValidateCouncilProceeding(agendaId, kingdom, office, proposer) != true)
            {
                failureReason = new TextObject("{=BC_CouncilDelib_InvalidRealm}This appointment is no longer a valid matter for the realm.");
                return false;
            }
            return TryQueueOrStartAppointment(kingdom, office, proposer, faction,
                "court_agenda", out failureReason, voteDate, agendaId);
        }

        internal bool HasAgendaAppointment(Kingdom realm, PrivyCouncilOffice office, string agendaId) =>
            _pendingCourtAgendaIds.TryGetValue(BuildPendingKey(realm, office), out var saved) && saved == agendaId;

        internal void CancelAgendaAppointment(Kingdom realm, PrivyCouncilOffice office, string agendaId)
        {
            if (string.IsNullOrEmpty(agendaId)) return;
            if (HasAgendaAppointment(realm, office, agendaId)) ClearAppointmentState(BuildPendingKey(realm, office));
            foreach (var decision in realm.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>()
                .Where(d => d.CourtAgendaId == agendaId).ToList())
            {
                realm.RemoveDecision(decision);
                _activeDecisionKeys.Remove(BuildPendingKey(realm, office));
            }
        }

        public string GetCommittedCandidateVote(Kingdom kingdom, PrivyCouncilOffice office, Clan voter)
        {
            string key = BuildVoterKey(kingdom, office, voter);
            if (string.IsNullOrEmpty(key)
                || !_committedVotes.ContainsKey(key)
                || !_nomineeByVoter.TryGetValue(key, out string candidateId))
            {
                return null;
            }

            Clan candidate = ResolveClan(candidateId);
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            return CouncilAppointmentNominationHelper.ScoreCandidate(
                voter, candidate, kingdom, office, council) != null
                    ? candidateId
                    : null;
        }

        internal string GetRecordedCandidatePledge(Kingdom kingdom, PrivyCouncilOffice office, Clan voter)
        {
            string key = BuildVoterKey(kingdom, office, voter);
            return !string.IsNullOrEmpty(key) && _committedVotes.ContainsKey(key)
                && _nomineeByVoter.TryGetValue(key, out string candidate) ? candidate : null;
        }

        internal IEnumerable<string> GetVotePledgeKeys(Clan voter) => VotePledgeService.KeysFor(voter, "council", _committedVotes.Keys);

        public void SetCommittedCandidateVote(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan voter,
            Clan candidate,
            string reason)
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            string key = BuildVoterKey(kingdom, office, voter);
            CouncilAppointmentNominationResult result = CouncilAppointmentNominationHelper.ScoreCandidate(
                voter, candidate, kingdom, office, council);
            if (string.IsNullOrEmpty(key) || result == null)
                return;

            _nomineeByVoter[key] = candidate.StringId;
            _nominationReasons[key] = reason ?? string.Join("|", result.Reasons);
            _nominationScores[key] = result.Score;
            _committedVotes[key] = true;
            VotePledgeService.Invalidate();
            if (_conversationVoter == voter)
            {
                _conversationNominee = candidate;
                _selectedCandidate = candidate;
            }
        }

        private bool TryQueueOrStartAppointment(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan proposer,
            FactionObject faction,
            string source,
            out TextObject failureReason,
            CampaignTime scheduledVoteDate, string agendaId)
        {
            if (!ValidateNewAppointment(kingdom, office, proposer, out failureReason))
                return false;

            string pendingKey = BuildPendingKey(kingdom, office);
            if (!string.IsNullOrEmpty(agendaId)) _pendingCourtAgendaIds[pendingKey] = agendaId;
            _pendingProposerClan[pendingKey] = proposer.StringId;
            _pendingFactionType[pendingKey] = faction != null ? (int)faction.Type : -1;
            _pendingVoteDate[pendingKey] = scheduledVoteDate;
            // An agenda's promised deliberation window is not a stalled-ballot retry period.
            _pendingCreatedDay[pendingKey] = (float)scheduledVoteDate.ToDays;
            _pendingRetryCount[pendingKey] = 0;
            BuildPreliminaryNominations(kingdom, office, preserveCommitted: false);

            TextObject notice = new TextObject("{=BC_CouncilDelib_Queued}The nobility of {KINGDOM_NAME} has begun considering candidates for the office of {OFFICE}. The court will deliberate and call for a vote within {DAYS} days.");
            notice.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            notice.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName(office, kingdom));
            notice.SetTextVariable("DAYS", (int)Math.Max(0, Math.Ceiling((_pendingVoteDate[pendingKey] - CampaignTime.Now).ToDays)));
            BellumCivileNotifications.Show(notice, BellumNotificationColors.Politics,
                primaryKingdom: kingdom, isPersonal: kingdom == Clan.PlayerClan?.Kingdom);
            BellumCivileLogger.Log($"Queued council appointment deliberation; kingdom={kingdom.StringId}; office={office}; proposer={proposer.StringId}; source={source}; due={_pendingVoteDate[pendingKey].ToDays:0.0}.");
            failureReason = TextObject.GetEmpty();
            return true;
        }

        private bool ValidateNewAppointment(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan proposer,
            out TextObject failureReason)
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (kingdom == null || kingdom.IsEliminated || proposer == null || proposer.Kingdom != kingdom)
            {
                failureReason = new TextObject("{=BC_CouncilDelib_InvalidRealm}This appointment is no longer a valid matter for the realm.");
                return false;
            }

            if (council == null || !council.IsOfficeUnlocked(kingdom, office))
            {
                failureReason = new TextObject("{=BC_Council_ProposalLockedOffice}This council office is not available to the realm.");
                return false;
            }

            if (HasPendingAppointment(kingdom)
                || kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any())
            {
                failureReason = new TextObject("{=BC_Council_ProposalPending}Another council appointment is already before the realm.");
                return false;
            }

            if (!council.HasAppointmentContest(kingdom, office))
            {
                failureReason = new TextObject("{=BC_Council_ProposalNoCandidates}No eligible noble is available to contest this council office.");
                return false;
            }

            if (!(proposer == Clan.PlayerClan && kingdom.RulingClan == proposer)
                && council.GetOfficeHolder(kingdom, office) != null && IsOfficeMotionOnCooldown(kingdom, office))
            {
                failureReason = new TextObject("{=BC_Council_ProposalOfficeCooldown}The realm recently settled this office and will not reopen it yet.");
                return false;
            }

            failureReason = TextObject.GetEmpty();
            return true;
        }

        private void OnDailyTick()
        {
            ReconcileOfficeSettlements();
            foreach (string pendingKey in _pendingVoteDate.Keys.ToList())
            {
                if (!_pendingVoteDate[pendingKey].IsPast)
                    continue;

                // Missing infrastructure is not a political cancellation of a paid motion.
                if (_pendingCourtAgendaIds.ContainsKey(pendingKey) && CourtAgendaBehavior.Current == null)
                    continue;

                if (!TryResolvePendingKey(pendingKey, out Kingdom kingdom, out PrivyCouncilOffice office))
                {
                    WithdrawPendingAppointment(pendingKey, "kingdom_missing");
                    continue;
                }

                PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
                if (council == null) continue;
                Clan proposer = ResolveClan(_pendingProposerClan.TryGetValue(pendingKey, out string proposerId)
                    ? proposerId : null);
                if (_pendingCourtAgendaIds.TryGetValue(pendingKey, out var agendaId))
                {
                    string reason = CourtAgendaBehavior.Current.CouncilProceedingReason(agendaId, kingdom, office, proposer);
                    if (reason == "council_waiting_for_candidates" || reason == "council_service_unavailable")
                    {
                        if (_candidateWaitLogged.Add(pendingKey))
                            BellumCivileLogger.Log($"Council appointment waiting; key={pendingKey}; reason={reason}; motion={agendaId}.");
                        if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingCreatedDay, _pendingRetryCount))
                            WithdrawPendingAppointment(pendingKey, "council_candidate_wait_expired");
                        continue;
                    }
                    _candidateWaitLogged.Remove(pendingKey);
                    if (reason != null)
                    {
                        WithdrawPendingAppointment(pendingKey, reason);
                        continue;
                    }
                }
                if (!council.IsOfficeUnlocked(kingdom, office) || !council.HasAppointmentContest(kingdom, office))
                {
                    BellumCivileLogger.Log($"Council appointment deliberation withdrawn after its contest became invalid; key={pendingKey}.");
                    WithdrawPendingAppointment(pendingKey, "council_contest_invalid");
                    continue;
                }

                if (kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any())
                    continue;

                if (proposer == null || proposer.Kingdom != kingdom)
                    proposer = kingdom.RulingClan;

                FactionObject faction = ResolveFaction(kingdom, pendingKey);
                if (DelayedVoteReliability.IsPendingExpired(pendingKey, _pendingCreatedDay, _pendingRetryCount))
                {
                    BellumCivileLogger.Log($"Expired council appointment deliberation removed; key={pendingKey}.");
                    WithdrawPendingAppointment(pendingKey, "council_ballot_retry_exhausted");
                    continue;
                }

                if (!FireAppointmentDecision(kingdom, office, proposer, faction, "deliberation", buildStoredNominations: true))
                {
                    DelayedVoteReliability.RegisterFailure(pendingKey, _pendingRetryCount);
                    continue;
                }

                RemovePendingLifecycle(pendingKey);
            }

            CleanupStaleActiveDecisions();
        }

        private void WithdrawPendingAppointment(string key, string reason)
        {
            if (_pendingCourtAgendaIds.TryGetValue(key, out var motionId))
                CourtAgendaBehavior.Current?.WithdrawCouncilProceeding(motionId, reason);
            ClearAppointmentState(key);
            BellumCivileLogger.Log($"Council appointment withdrawn; key={key}; reason={reason}.");
        }

        private bool FireAppointmentDecision(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan proposer,
            FactionObject faction,
            string source,
            bool buildStoredNominations)
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null || kingdom == null || proposer == null)
                return false;

            string pendingKey = BuildPendingKey(kingdom, office);
            _pendingCourtAgendaIds.TryGetValue(pendingKey, out var agendaId);
            Clan formalNominee = null;
            if (!string.IsNullOrEmpty(agendaId))
            {
                if (CourtAgendaBehavior.Current?.ValidateCouncilProceeding(agendaId, kingdom, office, proposer) != true)
                    return false;
                formalNominee = CourtAgendaBehavior.Current.GetCouncilProceedingNominee(agendaId, kingdom, office, proposer);
                if (formalNominee == null) return false;
            }

            if (buildStoredNominations)
                BuildPreliminaryNominations(kingdom, office, preserveCommitted: true);

            var ranking = buildStoredNominations
                ? BuildStoredRanking(kingdom, office)
                : CouncilAppointmentNominationHelper.BuildRanking(kingdom, office, council)
                    .ToList();
            List<Clan> shortlist = CouncilAppointmentNominationHelper.BuildShortlist(
                ranking.Select(entry => entry.Candidate), council.GetAppointmentCandidatesForVote(kingdom, office), formalNominee);

            if (shortlist.Count == 0)
                return false;

            PrivyCouncilAppointmentDecision decision = new PrivyCouncilAppointmentDecision(proposer, office, shortlist);
            decision.CourtAgendaId = agendaId;
            if (!decision.IsAllowed())
                return false;

            _activeDecisionKeys[pendingKey] = true;
            try
            {
                IdeologyBehavior.AddDecisionAsModAction(kingdom, decision, ignoreInfluenceCost: true);
            }
            catch (Exception ex)
            {
                _activeDecisionKeys.Remove(pendingKey);
                BellumCivileLogger.Log($"Council appointment decision failed; key={pendingKey}; source={source}; error={ex.GetType().Name}:{ex.Message}.");
                return false;
            }

            bool unresolved = kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>()
                .Any(active => active.Office == office);
            if (!unresolved && _activeDecisionKeys.ContainsKey(pendingKey))
            {
                if (decision.AppliedCandidate != null) CompleteAppointmentLifecycle(pendingKey);
                else ClearAppointmentState(pendingKey);
            }

            BellumCivileLogger.Log($"Council appointment ballot opened; kingdom={kingdom.StringId}; office={office}; proposer={proposer.StringId}; faction={faction?.Type.ToString() ?? "none"}; source={source}; nominee={formalNominee?.StringId ?? "none"}; candidates={string.Join(",", shortlist.Select(clan => clan.StringId))}.");
            return true;
        }

        private void BuildPreliminaryNominations(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            bool preserveCommitted)
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null)
                return;

            string prefix = BuildPendingKey(kingdom, office) + "|";
            Dictionary<string, string> committedNominees = preserveCommitted
                ? _nomineeByVoter.Where(pair => pair.Key.StartsWith(prefix) && _committedVotes.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, string>();
            Dictionary<string, string> committedReasons = preserveCommitted
                ? _nominationReasons.Where(pair => committedNominees.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, string>();
            Dictionary<string, float> committedScores = preserveCommitted
                ? _nominationScores.Where(pair => committedNominees.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, float>();

            ClearNominationState(prefix, keepCommitted: preserveCommitted);
            foreach (Clan voter in kingdom.Clans.Where(clan => CouncilAppointmentNominationHelper.IsValidVoter(clan, kingdom)))
            {
                string voterKey = BuildVoterKey(kingdom, office, voter);
                if (committedNominees.TryGetValue(voterKey, out string committedId))
                {
                    Clan committed = ResolveClan(committedId);
                    if (CouncilAppointmentNominationHelper.ScoreCandidate(voter, committed, kingdom, office, council) != null)
                    {
                        _nomineeByVoter[voterKey] = committedId;
                        _nominationReasons[voterKey] = committedReasons.TryGetValue(voterKey, out string storedReason) ? storedReason : "committed";
                        _nominationScores[voterKey] = committedScores.TryGetValue(voterKey, out float storedScore) ? storedScore : 0f;
                        _committedVotes[voterKey] = true;
                        continue;
                    }
                }

                CouncilAppointmentNominationResult result = CouncilAppointmentNominationHelper.ChooseNominee(
                    voter, kingdom, office, council);
                if (result?.Candidate == null)
                    continue;

                _nomineeByVoter[voterKey] = result.Candidate.StringId;
                _nominationReasons[voterKey] = string.Join("|", result.Reasons);
                _nominationScores[voterKey] = result.Score;
            }
        }

        private List<CouncilAppointmentNominationRanking> BuildStoredRanking(
            Kingdom kingdom,
            PrivyCouncilOffice office)
        {
            string prefix = BuildPendingKey(kingdom, office) + "|";
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            HashSet<Clan> validCandidates = new HashSet<Clan>(
                council?.GetAppointmentCandidatesForVote(kingdom, office) ?? new List<Clan>());
            return _nomineeByVoter
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .GroupBy(pair => pair.Value)
                .Select(group => new CouncilAppointmentNominationRanking
                {
                    Candidate = ResolveClan(group.Key),
                    Count = group.Count(),
                    Score = group.Sum(pair => _nominationScores.TryGetValue(pair.Key, out float score) ? score : 0f)
                })
                .Where(entry => entry.Candidate != null
                    && entry.Candidate != kingdom.RulingClan
                    && validCandidates.Contains(entry.Candidate))
                .OrderByDescending(entry => entry.Count)
                .ThenByDescending(entry => entry.Score)
                .ThenByDescending(entry => FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(entry.Candidate).HasValue
                    ? (int)FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(entry.Candidate).Value : -1)
                .ThenBy(entry => entry.Candidate.StringId)
                .ToList();
        }

        private void OnKingdomDecisionConcluded(
            KingdomDecision decision,
            DecisionOutcome chosenOutcome,
            bool isPlayerInvolved)
        {
            PrivyCouncilAppointmentDecision appointment = decision as PrivyCouncilAppointmentDecision;
            if (appointment?.Kingdom == null)
                return;

            string key = BuildPendingKey(appointment.Kingdom, appointment.Office);
            CourtAgendaBehavior.Current?.ConcludeCouncilAppointment(appointment);
            if (appointment.AppliedCandidate != null) CompleteAppointmentLifecycle(key);
            else ClearAppointmentState(key);
        }

        private void CompleteAppointmentLifecycle(string pendingKey)
        {
            if (string.IsNullOrEmpty(pendingKey)) return;
            if (!_pendingOfficeSettlements.Contains(pendingKey))
                _pendingOfficeSettlements.Add(pendingKey);
            ClearAppointmentState(pendingKey);
            ReconcileOfficeSettlements();
        }

        private void ReconcileOfficeSettlements()
        {
            var calendar = CourtAgendaBehavior.Current;
            if (calendar == null) return;
            foreach (string key in _pendingOfficeSettlements.ToList())
            {
                if (TryResolvePendingKey(key, out var realm, out var office))
                    calendar.RecordCouncilOfficeSettled(realm, office);
                else
                    BellumCivileLogger.Log($"Discarded council settlement receipt for an invalid realm/office; key={key}.");
                _pendingOfficeSettlements.Remove(key);
            }
        }

        private void CleanupStaleActiveDecisions()
        {
            foreach (string key in _activeDecisionKeys.Keys.ToList())
            {
                if (!TryResolvePendingKey(key, out Kingdom kingdom, out PrivyCouncilOffice office)
                    || !kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>()
                        .Any(decision => decision.Office == office))
                {
                    ClearAppointmentState(key);
                }
            }
        }

        private void RemovePendingLifecycle(string pendingKey)
        {
            _pendingCourtAgendaIds.Remove(pendingKey);
            _pendingProposerClan.Remove(pendingKey);
            _pendingFactionType.Remove(pendingKey);
            _pendingVoteDate.Remove(pendingKey);
            _pendingCreatedDay.Remove(pendingKey);
            _pendingRetryCount.Remove(pendingKey);
            _candidateWaitLogged.Remove(pendingKey);
        }

        private void ClearAppointmentState(string pendingKey)
        {
            RemovePendingLifecycle(pendingKey);
            _activeDecisionKeys.Remove(pendingKey);
            ClearNominationState(pendingKey + "|", keepCommitted: false);
        }

        private void ClearNominationState(string prefix, bool keepCommitted)
        {
            VotePledgeService.Invalidate();
            foreach (string key in _nomineeByVoter.Keys.Where(key => key.StartsWith(prefix)).ToList())
            {
                if (keepCommitted && _committedVotes.ContainsKey(key))
                    continue;
                _nomineeByVoter.Remove(key);
                _nominationReasons.Remove(key);
                _nominationScores.Remove(key);
                _committedVotes.Remove(key);
                _persuasionFailures.Remove(key);
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine(
                "council_appointment_deliberation_topic",
                "lord_talk_speak_diplomacy_2",
                "council_appointment_deliberation_response",
                "{=BC_CouncilDelib_Topic}About the appointment before the {COUNCIL_NAME}...",
                PrepareConversation,
                null,
                100,
                null,
                null);

            starter.AddDialogLine(
                "council_appointment_not_leader",
                "council_appointment_deliberation_response",
                "hero_main_options",
                "{=BC_CouncilDelib_NotLeader}Such matters about the council are not mine to decide. I speak only for myself, not for the {CLAN_NAME}. If it is politics you wish to discuss, you must seek out {CLAN_LEADER}, the head of our family.",
                ConversationHeroIsNotClanLeader,
                ClearConversation,
                190,
                null);

            starter.AddDialogLine(
                "council_appointment_committed",
                "council_appointment_deliberation_response",
                "council_appointment_choices",
                "{=BC_CouncilDelib_Committed}My word is already given. I will support {CANDIDATE_NAME} when the appointment is called.",
                ConversationVoteIsCommitted,
                null,
                180,
                null);

            starter.AddDialogLine(
                "council_appointment_stance_self",
                "council_appointment_deliberation_response",
                "council_appointment_choices",
                "{=BC_CouncilDelib_StanceSelf}I intend to put forward my own house for the office of {OFFICE}. {REASON_TEXT}",
                () => ConversationCanDiscuss() && _conversationNominee == _conversationVoter,
                null,
                170,
                null);

            starter.AddDialogLine(
                "council_appointment_stance_other",
                "council_appointment_deliberation_response",
                "council_appointment_choices",
                "{=BC_CouncilDelib_StanceOther}I intend to support {CANDIDATE_NAME} for the office of {OFFICE}. {REASON_TEXT}",
                () => ConversationCanDiscuss() && _conversationNominee != null,
                null,
                160,
                null);

            starter.AddPlayerLine(
                "council_appointment_support_player",
                "council_appointment_choices",
                "council_appointment_candidate_response",
                "{=BC_CouncilDelib_SupportPlayer}I would ask you to support my house instead.",
                () => CanSelectCandidate(Clan.PlayerClan),
                () => SelectCandidate(Clan.PlayerClan),
                140,
                null,
                null);

            starter.AddPlayerLine(
                "council_appointment_support_self",
                "council_appointment_choices",
                "council_appointment_candidate_response",
                "{=BC_CouncilDelib_SupportYou}I believe your own house should seek this office.",
                () => CanSelectCandidate(_conversationVoter),
                () => SelectCandidate(_conversationVoter),
                135,
                null,
                null);

            starter.AddPlayerLine(
                "council_appointment_support_other",
                "council_appointment_choices",
                "council_appointment_candidate_list",
                "{=BC_CouncilDelib_SupportOther}I would prefer you to support another candidate.",
                BeginCandidateSelection,
                null,
                130,
                null,
                null);

            starter.AddDialogLine(
                "council_appointment_candidate_prompt",
                "council_appointment_candidate_list",
                "council_appointment_candidate_options",
                "{=BC_CouncilDelib_CandidatePrompt}Whose service do you wish me to consider?",
                null,
                null,
                100,
                null);

            for (int i = 0; i < 5; i++)
            {
                int slot = i;
                starter.AddPlayerLine(
                    "council_appointment_candidate_" + slot,
                    "council_appointment_candidate_options",
                    "council_appointment_candidate_response",
                    "{CANDIDATE_" + slot + "}",
                    () => CandidateSlotCondition(slot),
                    () => CandidateSlotAction(slot),
                    120 - slot,
                    null,
                    null);
            }

            starter.AddPlayerLine(
                "council_appointment_candidate_more",
                "council_appointment_candidate_options",
                "council_appointment_candidate_list",
                "{=BC_CouncilDelib_MoreCandidates}I have another candidate in mind.",
                CandidateListHasMore,
                () => _candidatePageIndex++,
                70,
                null,
                null);

            starter.AddPlayerLine(
                "council_appointment_candidate_back",
                "council_appointment_candidate_options",
                "council_appointment_choices",
                "{=BC_Deliberation_Back}Never mind. Let us speak of something else.",
                null,
                ClearCandidateSelection,
                60,
                null,
                null);

            starter.AddDialogLine(
                "council_appointment_candidate_acknowledge",
                "council_appointment_candidate_response",
                "council_appointment_sway_choices",
                "{=BC_CouncilDelib_CandidateAcknowledge}You would have me support {SELECTED_CANDIDATE}. What reason have you to change my mind?",
                () => _selectedCandidate != null,
                null,
                100,
                null);

            starter.AddPlayerLine(
                "council_appointment_persuade",
                "council_appointment_sway_choices",
                "council_appointment_persuasion_result",
                "{=BC_CouncilDelib_Persuade}Their ability and standing make them the soundest choice for this office.",
                () => _selectedCandidate != null,
                AttemptPersuasion,
                120,
                PersuasionClickable,
                null);

            starter.AddPlayerLine(
                "council_appointment_bribe",
                "council_appointment_sway_choices",
                "council_appointment_barter_response",
                "{=BC_CouncilDelib_Bribe}Perhaps a private arrangement would help settle your doubts.",
                () => _selectedCandidate != null,
                null,
                110,
                BribeClickable,
                null);

            starter.AddPlayerLine(
                "council_appointment_sway_back",
                "council_appointment_sway_choices",
                "lord_pretalk",
                "{=BC_CouncilDelib_SwayBack}Then I will leave you to your judgment.",
                null,
                ClearConversation,
                80,
                null,
                null);

            starter.AddDialogLine(
                "council_appointment_persuasion_success",
                "council_appointment_persuasion_result",
                "lord_pretalk",
                "{=BC_CouncilDelib_PersuasionSuccess}Very well. You have made your case, and my support will go to {SELECTED_CANDIDATE}.",
                () => _persuasionSucceeded,
                ApplyPersuasionSuccess,
                120,
                null);

            starter.AddDialogLine(
                "council_appointment_persuasion_failure",
                "council_appointment_persuasion_result",
                "lord_pretalk",
                "{=BC_CouncilDelib_PersuasionFailure}No. I have heard your argument, but my judgment remains unchanged.",
                () => !_persuasionSucceeded,
                ApplyPersuasionFailure,
                110,
                null);

            starter.AddDialogLine(
                "council_appointment_barter_accept",
                "council_appointment_barter_response",
                "council_appointment_barter_start",
                "{=BC_CouncilDelib_BribeResponse}I am prepared to hear what arrangement you have in mind.",
                null,
                null,
                100,
                null);

            starter.AddPlayerLine(
                "council_appointment_barter_start_line",
                "council_appointment_barter_start",
                "lord_pretalk",
                "{=BC_Fief_Delib_DiscussTerms}Let us discuss terms.",
                null,
                LaunchBribeBarter,
                100,
                null,
                null);

            starter.AddPlayerLine(
                "council_appointment_end",
                "council_appointment_choices",
                "lord_pretalk",
                "{=BC_CouncilDelib_End}I will leave the appointment to the judgment of the realm.",
                null,
                ClearConversation,
                50,
                null,
                null);
        }

        private bool PrepareConversation()
        {
            Hero npc = Hero.OneToOneConversationHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (npc?.Clan == null || kingdom == null || npc.Clan.Kingdom != kingdom)
                return false;

            if (!CouncilAppointmentNominationHelper.IsValidVoter(npc.Clan, kingdom))
                return false;

            string prefix = kingdom.StringId + "|";
            string pendingKey = _pendingVoteDate.Keys.FirstOrDefault(key => key.StartsWith(prefix));
            if (pendingKey == null || !TryResolvePendingKey(pendingKey, out kingdom, out PrivyCouncilOffice office))
                return false;

            _conversationKingdom = kingdom;
            _conversationOffice = office;
            _conversationVoter = npc.Clan;
            string voterKey = BuildVoterKey(kingdom, office, npc.Clan);
            _conversationNominee = ResolveClan(_nomineeByVoter.TryGetValue(
                voterKey, out string nomineeId) ? nomineeId : null);
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (CouncilAppointmentNominationHelper.ScoreCandidate(
                    _conversationVoter, _conversationNominee, kingdom, office, council) == null)
            {
                _committedVotes.Remove(voterKey);
                VotePledgeService.Invalidate();
                CouncilAppointmentNominationResult replacement = CouncilAppointmentNominationHelper.ChooseNominee(
                    _conversationVoter, kingdom, office, council);
                _conversationNominee = replacement?.Candidate;
                if (_conversationNominee != null)
                {
                    _nomineeByVoter[voterKey] = _conversationNominee.StringId;
                    _nominationReasons[voterKey] = string.Join("|", replacement.Reasons);
                    _nominationScores[voterKey] = replacement.Score;
                }
            }
            _selectedCandidate = null;
            _candidatePage.Clear();
            _candidatePageIndex = 0;
            SetConversationVariables();
            return true;
        }

        private bool ConversationHeroIsNotClanLeader()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || npc == npc.Clan.Leader)
                return false;
            MBTextManager.SetTextVariable("CLAN_LEADER", npc.Clan.Leader?.Name ?? npc.Clan.Name);
            MBTextManager.SetTextVariable("CLAN_NAME", npc.Clan.Name);
            return true;
        }

        private bool ConversationCanDiscuss()
        {
            return !ConversationHeroIsNotClanLeader();
        }

        private bool ConversationVoteIsCommitted()
        {
            if (!ConversationCanDiscuss())
                return false;
            bool committed = !string.IsNullOrEmpty(GetCommittedCandidateVote(
                _conversationKingdom, _conversationOffice, _conversationVoter));
            SetConversationVariables();
            return committed;
        }

        private void SetConversationVariables()
        {
            TextObject reason = GetNominationReason(_conversationKingdom, _conversationOffice, _conversationVoter);
            MBTextManager.SetTextVariable("COUNCIL_NAME", CourtInstitutionDisplayHelper.GetPrivyCouncilName(_conversationKingdom));
            MBTextManager.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName(_conversationOffice, _conversationKingdom));
            MBTextManager.SetTextVariable("CANDIDATE_NAME", _conversationNominee?.Leader?.Name ?? _conversationNominee?.Name ?? TextObject.GetEmpty());
            MBTextManager.SetTextVariable("SELECTED_CANDIDATE", _selectedCandidate?.Leader?.Name ?? _selectedCandidate?.Name ?? TextObject.GetEmpty());
            MBTextManager.SetTextVariable("REASON_TEXT", reason);
        }

        private TextObject GetNominationReason(Kingdom kingdom, PrivyCouncilOffice office, Clan voter)
        {
            string voterKey = BuildVoterKey(kingdom, office, voter);
            string raw = _nominationReasons.TryGetValue(voterKey, out string reason) ? reason : "judgment";
            return CouncilAppointmentNominationHelper.BuildReasonText(raw.Split('|'));
        }

        private bool CanSelectCandidate(Clan candidate)
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            return candidate != null
                && candidate != _conversationKingdom?.RulingClan
                && CouncilAppointmentNominationHelper.ScoreCandidate(
                    _conversationVoter, candidate, _conversationKingdom, _conversationOffice, council) != null;
        }

        private void SelectCandidate(Clan candidate)
        {
            if (!CanSelectCandidate(candidate))
                return;

            _selectedCandidate = candidate;
            SetConversationVariables();
        }

        private bool BeginCandidateSelection()
        {
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null || _conversationVoter == null)
                return false;

            _candidatePage = council.GetAppointmentCandidatesForVote(_conversationKingdom, _conversationOffice)
                .Where(candidate => candidate != _conversationKingdom?.RulingClan
                    && candidate != Clan.PlayerClan
                    && candidate != _conversationVoter
                    && candidate != _conversationNominee)
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = CouncilAppointmentNominationHelper.ScoreCandidate(
                        _conversationVoter, candidate, _conversationKingdom, _conversationOffice, council)?.Score ?? -100f
                })
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Candidate.StringId)
                .Select(entry => entry.Candidate)
                .ToList();
            _candidatePageIndex = 0;
            return _candidatePage.Count > 0;
        }

        private bool CandidateSlotCondition(int slot)
        {
            int index = _candidatePageIndex * 5 + slot;
            if (index < 0 || index >= _candidatePage.Count)
                return false;
            MBTextManager.SetTextVariable("CANDIDATE_" + slot, _candidatePage[index].Leader?.Name ?? _candidatePage[index].Name);
            return true;
        }

        private void CandidateSlotAction(int slot)
        {
            int index = _candidatePageIndex * 5 + slot;
            if (index >= 0 && index < _candidatePage.Count)
                SelectCandidate(_candidatePage[index]);
        }

        private bool CandidateListHasMore()
        {
            return (_candidatePageIndex + 1) * 5 < _candidatePage.Count;
        }

        private bool PersuasionClickable(out TextObject explanation)
        {
            explanation = TextObject.GetEmpty();
            if (!VotePledgeService.CanPromise(_conversationVoter, _conversationKingdom,
                VotePledgeService.CouncilKey(_conversationKingdom, _conversationOffice, _conversationVoter), out explanation)) return false;
            string voterKey = BuildVoterKey(_conversationKingdom, _conversationOffice, _conversationVoter);
            if (_committedVotes.ContainsKey(voterKey))
            {
                explanation = new TextObject("{=BC_CouncilDelib_AlreadyCommittedHint}This lord has already committed their support.");
                return false;
            }
            if (_persuasionFailures.ContainsKey(voterKey))
            {
                explanation = new TextObject("{=BC_CouncilDelib_PersuasionFailedHint}They have already rejected your argument over this appointment.");
                return false;
            }
            if ((Hero.MainHero?.GetRelation(_conversationVoter?.Leader) ?? -100) < 30)
            {
                explanation = new TextObject("{=BC_CouncilDelib_PersuasionRelationHint}They do not trust you enough to be swayed. Relation required: 30.");
                return false;
            }
            return _selectedCandidate != null;
        }

        private void AttemptPersuasion()
        {
            Hero target = _conversationVoter?.Leader;
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (target == null || _selectedCandidate == null || council == null)
            {
                _persuasionSucceeded = false;
                return;
            }

            float chance = 20f
                + (Hero.MainHero?.GetSkillValue(DefaultSkills.Charm) ?? 0) * 0.18f
                + Hero.MainHero.GetRelation(target) * 0.25f
                - target.GetTraitLevel(DefaultTraits.Honor) * 8f
                + target.GetTraitLevel(DefaultTraits.Calculating) * 5f;
            CouncilAppointmentNominationResult selected = CouncilAppointmentNominationHelper.ScoreCandidate(
                _conversationVoter, _selectedCandidate, _conversationKingdom, _conversationOffice, council);
            CouncilAppointmentNominationResult natural = CouncilAppointmentNominationHelper.ScoreCandidate(
                _conversationVoter, _conversationNominee, _conversationKingdom, _conversationOffice, council);
            chance += ((selected?.Score ?? -100f) - (natural?.Score ?? 0f)) * 0.15f;
            chance = Math.Max(10f, Math.Min(85f, chance));
            _persuasionSucceeded = MBRandom.RandomFloat * 100f <= chance;
        }

        private void ApplyPersuasionSuccess()
        {
            VotePledgeService.TryCommit(_conversationVoter, _conversationKingdom,
                VotePledgeService.CouncilKey(_conversationKingdom, _conversationOffice, _conversationVoter), () => SetCommittedCandidateVote(
                _conversationKingdom,
                _conversationOffice,
                _conversationVoter,
                _selectedCandidate,
                "persuaded"));
            ClearConversation();
        }

        private void ApplyPersuasionFailure()
        {
            string key = BuildVoterKey(_conversationKingdom, _conversationOffice, _conversationVoter);
            if (!string.IsNullOrEmpty(key))
                _persuasionFailures[key] = true;
            ClearConversation();
        }

        private bool BribeClickable(out TextObject explanation)
        {
            explanation = TextObject.GetEmpty();
            if (!VotePledgeService.CanPromise(_conversationVoter, _conversationKingdom,
                VotePledgeService.CouncilKey(_conversationKingdom, _conversationOffice, _conversationVoter), out explanation)) return false;
            string voterKey = BuildVoterKey(_conversationKingdom, _conversationOffice, _conversationVoter);
            if (_committedVotes.ContainsKey(voterKey))
            {
                explanation = new TextObject("{=BC_CouncilDelib_AlreadyCommittedHint}This lord has already committed their support.");
                return false;
            }

            Hero voter = _conversationVoter?.Leader;
            if (voter == null || _selectedCandidate == null)
                return false;
            if (voter.GetTraitLevel(DefaultTraits.Honor) >= 1 && voter.GetRelation(Hero.MainHero) < 30)
            {
                explanation = new TextObject("{=BC_CouncilDelib_BribeHonorHint}They consider a council appointment a matter of honor and will not bargain over it.");
                return false;
            }
            return true;
        }

        private void LaunchBribeBarter()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || _selectedCandidate == null)
                return;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            float selected = CouncilAppointmentNominationHelper.ScoreCandidate(
                npc.Clan, _selectedCandidate, _conversationKingdom, _conversationOffice, council)?.Score ?? -100f;
            float natural = CouncilAppointmentNominationHelper.ScoreCandidate(
                npc.Clan, _conversationNominee, _conversationKingdom, _conversationOffice, council)?.Score ?? 0f;
            CouncilAppointmentVoteBribeBarterable bribe = new CouncilAppointmentVoteBribeBarterable(
                npc.Clan,
                _conversationKingdom,
                _conversationOffice,
                _selectedCandidate,
                natural - selected,
                Hero.MainHero);

            BarterManager.Instance.StartBarterOffer(
                Hero.MainHero,
                npc,
                PartyBase.MainParty,
                npc.PartyBelongedTo?.Party,
                null,
                (Barterable barterable, BarterData args, object state) =>
                {
                    args.AddBarterable<CouncilAppointmentVoteBribeBarterable>(bribe);
                    foreach (Settlement settlement in Hero.MainHero.Clan.Settlements)
                        if (settlement.IsTown || settlement.IsCastle)
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(settlement, Hero.MainHero, npc));
                    return true;
                },
                0,
                false,
                new Barterable[] { bribe });
            ClearConversation();
        }

        private void ClearCandidateSelection()
        {
            _selectedCandidate = null;
            _candidatePage.Clear();
            _candidatePageIndex = 0;
        }

        private void ClearConversation()
        {
            _conversationKingdom = null;
            _conversationVoter = null;
            _conversationNominee = null;
            _selectedCandidate = null;
            _candidatePage.Clear();
            _candidatePageIndex = 0;
        }

        private FactionObject ResolveFaction(Kingdom kingdom, string pendingKey)
        {
            if (!_pendingFactionType.TryGetValue(pendingKey, out int raw) || raw < 0)
                return null;
            return Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?
                .GetFactionsInKingdom(kingdom)
                .FirstOrDefault(faction => faction.IsIdeology && (int)faction.Type == raw);
        }

        private static string BuildPendingKey(Kingdom kingdom, PrivyCouncilOffice office)
        {
            return kingdom == null ? string.Empty : kingdom.StringId + "|" + (int)office;
        }

        private static string BuildVoterKey(Kingdom kingdom, PrivyCouncilOffice office, Clan voter)
        {
            return kingdom == null || voter == null
                ? string.Empty
                : BuildPendingKey(kingdom, office) + "|" + voter.StringId;
        }

        private static bool TryResolvePendingKey(
            string pendingKey,
            out Kingdom kingdom,
            out PrivyCouncilOffice office)
        {
            kingdom = null;
            office = PrivyCouncilOffice.Marshal;
            if (string.IsNullOrEmpty(pendingKey))
                return false;
            string[] parts = pendingKey.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int rawOffice))
                return false;
            kingdom = Kingdom.All.FirstOrDefault(candidate => candidate.StringId == parts[0]);
            if (kingdom == null || kingdom.IsEliminated || !Enum.IsDefined(typeof(PrivyCouncilOffice), rawOffice))
                return false;
            office = (PrivyCouncilOffice)rawOffice;
            return true;
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrEmpty(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan.StringId == clanId);
        }
    }
}
