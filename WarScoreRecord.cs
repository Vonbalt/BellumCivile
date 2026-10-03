using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public partial class WarScoreRecord
    {
        [SaveableField(1)] private string _warKey;
        [SaveableField(2)] private string _attackerKingdomId;
        [SaveableField(3)] private string _defenderKingdomId;
        [SaveableField(4)] private float _startedDay;
        [SaveableField(5)] private float _endedDay;
        [SaveableField(6)] private float _score;
        [SaveableField(7)] private bool _isActive;
        [SaveableField(8)] private List<WarScoreFiefSnapshotRecord> _fiefSnapshots;
        [SaveableField(9)] private List<WarScoreEventRecord> _events;
        [SaveableField(10)] private float _occupationScore;
        [SaveableField(11)] private float _battleScore;
        [SaveableField(12)] private float _raidScore;
        [SaveableField(13)] private float _prisonerScore;
        [SaveableField(14)] private float _tickingScore;
        [SaveableField(15)] private float _lastTickDay;
        [SaveableField(16)] private WarScoreConflictType _conflictType;
        [SaveableField(17)] private string _contextId;
        [SaveableField(18)] private bool _resolutionPending;
        [SaveableField(19)] private float _objectiveScore;
        [SaveableField(20)] private float _nextWhitePeaceCheckDay;
        [SaveableField(21)] private bool _whitePeaceOfferPending;
        [SaveableField(22)] private bool _parleyPending;
        [SaveableField(23)] private bool _parleyForced;
        [SaveableField(24)] private float _parleyOpenedDay;
        [SaveableField(25)] private bool _terminalResolutionQueued;
        [SaveableField(26)] private string _terminalResolutionReason;
        [SaveableField(27)] private float _landlessPressureScore;
        [SaveableField(28)] private string _originalWarKey;
        [SaveableField(29)] private Dictionary<string, float> _prisonerCustody;
        [SaveableField(30)] private List<string> _attackerRaidedVillages;
        [SaveableField(31)] private List<string> _defenderRaidedVillages;

        public string WarKey => _warKey;
        public string OriginalWarKey => _originalWarKey ?? _warKey;
        public string AttackerKingdomId => _attackerKingdomId;
        public string DefenderKingdomId => _defenderKingdomId;
        public float StartedDay => _startedDay;
        public float EndedDay => _endedDay;
        public float Score => _score;
        public bool IsActive => _isActive;
        public IReadOnlyList<WarScoreFiefSnapshotRecord> FiefSnapshots => _fiefSnapshots;
        public IReadOnlyList<WarScoreEventRecord> Events => _events;
        public float OccupationScore => _occupationScore;
        public float BattleScore => _battleScore;
        public float RaidScore => _raidScore;
        public float PrisonerScore => _prisonerScore;
        public float TickingScore => _tickingScore;
        public float LastTickDay => _lastTickDay;
        public WarScoreConflictType ConflictType => _conflictType;
        public string ContextId => _contextId;
        public bool ResolutionPending => _resolutionPending;
        public float ObjectiveScore => _objectiveScore;
        public float NextWhitePeaceCheckDay => _nextWhitePeaceCheckDay;
        public bool WhitePeaceOfferPending => _whitePeaceOfferPending;
        public bool ParleyPending => _parleyPending;
        public bool ParleyForced => _parleyForced;
        public float ParleyOpenedDay => _parleyOpenedDay;
        public bool TerminalResolutionQueued => _terminalResolutionQueued;
        public string TerminalResolutionReason => _terminalResolutionReason;
        public float LandlessPressureScore => _landlessPressureScore;

        public WarScoreRecord(
            string warKey,
            string attackerKingdomId,
            string defenderKingdomId,
            float startedDay,
            IEnumerable<WarScoreFiefSnapshotRecord> fiefSnapshots,
            WarScoreConflictType conflictType = WarScoreConflictType.ForeignWar,
            string contextId = "")
        {
            _warKey = warKey ?? string.Empty;
            _originalWarKey = _warKey;
            _attackerKingdomId = attackerKingdomId ?? string.Empty;
            _defenderKingdomId = defenderKingdomId ?? string.Empty;
            _startedDay = startedDay;
            _endedDay = -1f;
            _score = 0f;
            _isActive = true;
            _fiefSnapshots = new List<WarScoreFiefSnapshotRecord>(fiefSnapshots ?? new List<WarScoreFiefSnapshotRecord>());
            _events = new List<WarScoreEventRecord>();
            _occupationScore = 0f;
            _battleScore = 0f;
            _raidScore = 0f;
            _prisonerScore = 0f;
            _prisonerCustody = new Dictionary<string, float>();
            _attackerRaidedVillages = new List<string>();
            _defenderRaidedVillages = new List<string>();
            _tickingScore = 0f;
            _lastTickDay = startedDay;
            _conflictType = conflictType;
            _contextId = contextId ?? string.Empty;
            _resolutionPending = false;
            _objectiveScore = 0f;
            _nextWhitePeaceCheckDay = -1f;
            _whitePeaceOfferPending = false;
            _parleyPending = false;
            _parleyForced = false;
            _parleyOpenedDay = -1f;
            _terminalResolutionQueued = false;
            _terminalResolutionReason = string.Empty;
            _landlessPressureScore = 0f;
        }

        internal void AddMissingFiefSnapshots(IEnumerable<WarScoreFiefSnapshotRecord> snapshots)
        {
            if (_conflictType != WarScoreConflictType.ForeignWar) return;
            var seen = new HashSet<string>();
            foreach (var s in _fiefSnapshots) if (s != null) seen.Add(s.SettlementId);
            foreach (var s in snapshots)
                if (s != null && !string.IsNullOrEmpty(s.SettlementId) && seen.Add(s.SettlementId))
                {
                    var copy = new WarScoreFiefSnapshotRecord(s.SettlementId, s.OriginalOwnerKingdomId, s.OwnerClanId, s.IsTown, s.IsCastle);
                    copy.RetargetOwnerRealm(s.OriginalOwnerKingdomId, s.OwnerKingdomId);
                    _fiefSnapshots.Add(copy);
                }
        }

        internal bool CanRetargetCivilWarDefender(string previous, string successor) =>
            _isActive && _conflictType == WarScoreConflictType.CivilWar
            && !string.IsNullOrWhiteSpace(previous) && !string.IsNullOrWhiteSpace(successor)
            && previous != successor && successor != _attackerKingdomId
            && !_whitePeaceOfferPending && !_parleyPending && !_resolutionPending
            && (_defenderKingdomId == previous || _defenderKingdomId == successor);

        internal bool TryRetargetCivilWarDefender(string previous, string successor)
        {
            if (!CanRetargetCivilWarDefender(previous, successor)) return false;
            if (_defenderKingdomId == successor) return true;
            if (_defenderKingdomId != previous) return false;
            EnsureRaidHistory();
            _originalWarKey = _originalWarKey ?? _warKey;
            foreach (var snapshot in _fiefSnapshots) snapshot?.RetargetOwnerRealm(previous, successor);
            _defenderKingdomId = successor;
            _warKey = PairKey(_attackerKingdomId, successor);
            return true;
        }

        internal static string PairKey(string first, string second) => string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second : second + "|" + first;

        internal bool CanPromoteRivalry(string winnerShell, string survivor, string crown) =>
            _isActive && _conflictType == WarScoreConflictType.CivilWar
            && !string.IsNullOrWhiteSpace(winnerShell) && !string.IsNullOrWhiteSpace(survivor)
            && !string.IsNullOrWhiteSpace(crown) && winnerShell != survivor && crown != survivor && crown != winnerShell
            && !_resolutionPending && !_terminalResolutionQueued && !_parleyPending && !_whitePeaceOfferPending
            && ((_contextId == survivor && _attackerKingdomId == survivor && _defenderKingdomId == crown)
                || _contextId?.StartsWith(CivilWarPairRecord.RivalryPrefix, System.StringComparison.Ordinal) == true
                && ((_attackerKingdomId == survivor && _defenderKingdomId == winnerShell)
                    || (_attackerKingdomId == winnerShell && _defenderKingdomId == survivor)));

        internal bool TryPromoteRivalry(string winnerShell, string survivor, string crown)
        {
            if (!CanPromoteRivalry(winnerShell, survivor, crown)) return false;
            if (_contextId == survivor) return true;
            EnsureRaidHistory();
            if (_attackerKingdomId != survivor)
            {
                var raids = _attackerRaidedVillages;
                _attackerRaidedVillages = _defenderRaidedVillages;
                _defenderRaidedVillages = raids;
                _score = -_score; _occupationScore = -_occupationScore; _battleScore = -_battleScore;
                _raidScore = -_raidScore; _prisonerScore = -_prisonerScore; _tickingScore = -_tickingScore;
                _objectiveScore = -_objectiveScore; _landlessPressureScore = -_landlessPressureScore;
                if (_prisonerCustody != null)
                    foreach (var id in _prisonerCustody.Keys.ToList()) _prisonerCustody[id] = -_prisonerCustody[id];
                foreach (var entry in _events) entry?.ReverseScorePerspective();
            }
            _originalWarKey = _originalWarKey ?? _warKey;
            foreach (var snapshot in _fiefSnapshots) snapshot?.RetargetOwnerRealm(winnerShell, crown);
            _attackerKingdomId = survivor;
            _defenderKingdomId = crown;
            _warKey = PairKey(survivor, crown);
            _contextId = survivor;
            return true;
        }

        public void SetOccupationScore(float value)
        {
            if (IsScoreLocked)
                return;

            _occupationScore = Clamp(value);
            RecalculateScore();
        }

        public void AddBattleScore(float delta, float cap)
        {
            if (IsScoreLocked)
                return;

            _battleScore = ClampComponent(_battleScore + delta, cap);
            RecalculateScore();
        }

        public void AddRaidScore(float delta, float cap)
        {
            if (IsScoreLocked)
                return;

            _raidScore = ClampComponent(_raidScore + delta, cap);
            RecalculateScore();
        }

        internal void EnsureRaidHistory()
        {
            if (_attackerRaidedVillages != null && _defenderRaidedVillages != null) return;
            _attackerRaidedVillages = _attackerRaidedVillages ?? new List<string>();
            _defenderRaidedVillages = _defenderRaidedVillages ?? new List<string>();
            // Older saves can recover credited villages from their retained event history.
            foreach (var entry in _events ?? new List<WarScoreEventRecord>())
            {
                if (entry?.EventType != WarScoreEventType.VillageRaided || string.IsNullOrWhiteSpace(entry.SettlementId)) continue;
                var raids = entry.ActorKingdomId == _attackerKingdomId ? _attackerRaidedVillages
                    : entry.ActorKingdomId == _defenderKingdomId ? _defenderRaidedVillages
                    : entry.Delta > 0 ? _attackerRaidedVillages : entry.Delta < 0 ? _defenderRaidedVillages : null;
                if (raids != null && !raids.Contains(entry.SettlementId)) raids.Add(entry.SettlementId);
            }
        }

        internal bool TryCreditVillageRaid(string villageId, string raiderKingdomId, float value, float cap)
        {
            if (!_isActive || IsScoreLocked || string.IsNullOrWhiteSpace(villageId) || value <= 0) return false;
            EnsureRaidHistory();
            var raids = raiderKingdomId == _attackerKingdomId ? _attackerRaidedVillages
                : raiderKingdomId == _defenderKingdomId ? _defenderRaidedVillages : null;
            if (raids == null || raids.Contains(villageId)) return false;
            // Consume this village's credit even at the cap, so it cannot be farmed after enemy raids.
            raids.Add(villageId);
            AddRaidScore(raiderKingdomId == _attackerKingdomId ? value : -value, cap);
            return true;
        }

        public void AddPrisonerScore(float delta, float cap)
        {
            if (IsScoreLocked)
                return;

            _prisonerScore = ClampComponent(_prisonerScore + delta, cap);
            RecalculateScore();
        }

        internal void ReconcilePrisoners(Dictionary<string, float> current, float cap)
        {
            if (_resolutionPending || _parleyPending && _parleyForced) return;
            var next = new Dictionary<string, float>();
            foreach (var pair in current)
            {
                float value = pair.Value;
                if (_prisonerCustody != null && _prisonerCustody.TryGetValue(pair.Key, out float recorded)
                    && System.Math.Sign(recorded) == System.Math.Sign(value)) value = recorded;
                next[pair.Key] = value;
            }
            _prisonerCustody = next;
            SetCustodyTotal(cap);
        }

        internal void RecordPrisoner(string id, float value, float cap)
        {
            if (_resolutionPending || _parleyPending && _parleyForced || string.IsNullOrEmpty(id)) return;
            _prisonerCustody = _prisonerCustody ?? new Dictionary<string, float>();
            if (!_prisonerCustody.TryGetValue(id, out float previous) || System.Math.Sign(previous) != System.Math.Sign(value))
                _prisonerCustody[id] = value;
            SetCustodyTotal(cap);
        }

        internal void RemovePrisoner(string id, float cap)
        {
            if (_resolutionPending || _parleyPending && _parleyForced || id == null) return;
            if (_prisonerCustody?.Remove(id) == true) SetCustodyTotal(cap);
        }

        private void SetCustodyTotal(float cap)
        {
            float value = ClampComponent(_prisonerCustody.Values.Sum(), cap);
            if (System.Math.Abs(value - _prisonerScore) < .0001f) return;
            _prisonerScore = value;
            RecalculateReversibleScore();
        }

        internal void SetObjectiveControlScore(float value)
        {
            if (_resolutionPending || _parleyPending && _parleyForced) return;
            value = Clamp(value);
            if (System.Math.Abs(value - _objectiveScore) < .0001f) return;
            _objectiveScore = value;
            RecalculateReversibleScore();
        }

        private void RecalculateReversibleScore()
        {
            RecalculateScore();
            if (System.Math.Abs(_score) < 100f) ClearTerminalResolution();
        }

        public void AddTickingScore(float delta, float cap, float day)
        {
            if (IsScoreLocked)
                return;

            _tickingScore = ClampComponent(_tickingScore + delta, cap);
            _lastTickDay = day;
            RecalculateScore();
        }

        public void AddObjectiveScore(float delta, float cap)
        {
            if (IsScoreLocked)
                return;

            _objectiveScore = ClampComponent(_objectiveScore + delta, cap);
            RecalculateScore();
        }

        public void SetLandlessPressureScore(float value, float cap)
        {
            if (IsScoreLocked)
                return;

            _landlessPressureScore = ClampComponent(value, cap);
            RecalculateScore();
        }

        public void ForceScore(float value)
        {
            _score = Clamp(value);
        }

        private bool IsScoreLocked => _resolutionPending
            || _terminalResolutionQueued
            || (_parleyPending && _parleyForced);

        public float GetSelfRelativeScore(string kingdomId)
        {
            if (string.IsNullOrWhiteSpace(kingdomId))
                return 0f;

            if (kingdomId == _attackerKingdomId)
                return _score;
            if (kingdomId == _defenderKingdomId)
                return -_score;
            return 0f;
        }

        public bool BeginResolution()
        {
            if (_resolutionPending || _terminalResolutionQueued || !_isActive)
                return false;

            _resolutionPending = true;
            return true;
        }

        public void CancelResolution()
        {
            _resolutionPending = false;
        }

        public bool CanCheckWhitePeace(float currentDay)
        {
            return _isActive
                && !_resolutionPending
                && !_terminalResolutionQueued
                && !_whitePeaceOfferPending
                && currentDay >= _nextWhitePeaceCheckDay;
        }

        public void ScheduleNextWhitePeaceCheck(float day)
        {
            _nextWhitePeaceCheckDay = day;
        }

        public void SetWhitePeaceOfferPending(bool pending)
        {
            _whitePeaceOfferPending = pending;
        }

        public bool BeginParley(bool forced, float day)
        {
            if (!_isActive || _resolutionPending || _terminalResolutionQueued || _parleyPending)
                return false;

            _parleyPending = true;
            _parleyForced = forced;
            _parleyOpenedDay = day;
            return true;
        }

        public void EndParley()
        {
            _parleyPending = false;
            _parleyForced = false;
            _parleyOpenedDay = -1f;
        }

        public bool QueueTerminalResolution(string reason)
        {
            if (!_isActive || _resolutionPending || _parleyPending)
                return false;

            _terminalResolutionQueued = true;
            if (!string.IsNullOrWhiteSpace(reason))
                _terminalResolutionReason = reason;
            return true;
        }

        public void ClearTerminalResolution()
        {
            _terminalResolutionQueued = false;
            _terminalResolutionReason = string.Empty;
        }

        public void AddEvent(WarScoreEventRecord eventRecord)
        {
            if (eventRecord != null)
            {
                _events.Add(eventRecord);
                if (eventRecord.EventType == WarScoreEventType.TownCaptured
                    || eventRecord.EventType == WarScoreEventType.CastleCaptured
                    || eventRecord.EventType == WarScoreEventType.CoreFiefRetaken
                    || eventRecord.EventType == WarScoreEventType.MajorBattleWon
                    || eventRecord.EventType == WarScoreEventType.RulerCaptured)
                    ResetPeaceReconsideration();
            }
        }

        public void MarkEnded(float day)
        {
            _endedDay = day;
            _isActive = false;
            _resolutionPending = false;
            _whitePeaceOfferPending = false;
            ClearTerminalResolution();
            EndParley();
        }

        public WarScoreFiefSnapshotRecord GetSnapshot(string settlementId)
        {
            if (string.IsNullOrWhiteSpace(settlementId) || _fiefSnapshots == null)
                return null;

            return _fiefSnapshots.Find(snapshot => snapshot != null && snapshot.SettlementId == settlementId);
        }

        private static float Clamp(float value)
        {
            if (value > 100f)
                return 100f;
            if (value < -100f)
                return -100f;
            return value;
        }

        private static float ClampComponent(float value, float cap)
        {
            float absCap = System.Math.Abs(cap);
            if (absCap <= 0f)
                return 0f;
            if (value > absCap)
                return absCap;
            if (value < -absCap)
                return -absCap;
            return value;
        }

        private void RecalculateScore()
        {
            _score = Clamp(_occupationScore + _battleScore + _raidScore + _prisonerScore + _tickingScore + _objectiveScore + _landlessPressureScore);
        }
    }
}
