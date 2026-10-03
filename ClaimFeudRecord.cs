using System;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class ClaimFeudRecord
    {
        [SaveableField(1)] private string _recordId;
        [SaveableField(2)] private string _parentKingdomId;
        [SaveableField(3)] private string _claimantClanId;
        [SaveableField(4)] private string _holderClanId;
        [SaveableField(5)] private string _targetTitleId;
        [SaveableField(6)] private FeudalClaimStrength _claimStrength;
        [SaveableField(7)] private ClaimFeudState _state;
        [SaveableField(8)] private float _pressure;
        [SaveableField(9)] private float _startedDay;
        [SaveableField(10)] private float _lastTickDay;
        [SaveableField(11)] private float _cooldownUntilDay;
        [SaveableField(12)] private string _sourceClaimId;
        [SaveableField(13)] private string _debugReason;
        [SaveableField(14)] private ClaimFeudJudgment _judgment;
        [SaveableField(15)] private ClaimFeudResponse _claimantResponse;
        [SaveableField(16)] private ClaimFeudResponse _holderResponse;
        [SaveableField(17)] private float _rulingDay;
        [SaveableField(18)] private float _claimantSidePower;
        [SaveableField(19)] private float _holderSidePower;
        [SaveableField(20)] private string _claimantSupporterIds;
        [SaveableField(21)] private string _holderSupporterIds;
        [SaveableField(22)] private bool _hasPassed33;
        [SaveableField(23)] private bool _hasPassed66;
        [SaveableField(24)] private ClaimFeudState _resumeState;
        [SaveableField(25)] private string _pauseReason;
        [SaveableField(26)] private float _pausedDay;
        [SaveableField(27)] private string _withdrawnClanIds;

        public bool HasWithdrawn(string clanId) => !string.IsNullOrEmpty(clanId)
            && (_withdrawnClanIds ?? "").Split(',').Contains(clanId);

        internal void RecordWithdrawal(string clanId)
        {
            if (string.IsNullOrEmpty(clanId) || HasWithdrawn(clanId)) return;
            _withdrawnClanIds = string.Join(",", (_withdrawnClanIds ?? "").Split(',')
                .Where(id => !string.IsNullOrEmpty(id)).Concat(new[] { clanId }));
            _claimantSupporterIds = Without(_claimantSupporterIds, clanId);
            _holderSupporterIds = Without(_holderSupporterIds, clanId);
        }

        internal static string Without(string ids, string clanId) => string.Join(",",
            (ids ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(id => id != clanId));

        public string RecordId => _recordId;
        public string ParentKingdomId => _parentKingdomId;
        public string ClaimantClanId => _claimantClanId;
        public string HolderClanId => _holderClanId;
        public string TargetTitleId => _targetTitleId;
        public FeudalClaimStrength ClaimStrength => _claimStrength;
        public ClaimFeudState State => _state;
        public float Pressure => _pressure;
        public float StartedDay => _startedDay;
        public float LastTickDay => _lastTickDay;
        public float CooldownUntilDay => _cooldownUntilDay;
        public string SourceClaimId => _sourceClaimId;
        public string DebugReason => _debugReason;
        public ClaimFeudJudgment Judgment => _judgment;
        public ClaimFeudResponse ClaimantResponse => _claimantResponse;
        public ClaimFeudResponse HolderResponse => _holderResponse;
        public float RulingDay => _rulingDay;
        public float ClaimantSidePower => _claimantSidePower;
        public float HolderSidePower => _holderSidePower;
        public string ClaimantSupporterIds => _claimantSupporterIds;
        public string HolderSupporterIds => _holderSupporterIds;
        public bool HasPassed33 => _hasPassed33;
        public bool HasPassed66 => _hasPassed66;
        public ClaimFeudState ResumeState => _resumeState;
        public string PauseReason => _pauseReason;
        public float PausedDay => _pausedDay;

        public ClaimFeudRecord(
            string recordId,
            string parentKingdomId,
            string claimantClanId,
            string holderClanId,
            string targetTitleId,
            FeudalClaimStrength claimStrength,
            float pressure,
            float startedDay,
            string sourceClaimId,
            string debugReason)
        {
            _recordId = recordId ?? string.Empty;
            _parentKingdomId = parentKingdomId ?? string.Empty;
            _claimantClanId = claimantClanId ?? string.Empty;
            _holderClanId = holderClanId ?? string.Empty;
            _targetTitleId = targetTitleId ?? string.Empty;
            _claimStrength = claimStrength;
            _state = ClaimFeudState.Agitating;
            _pressure = pressure;
            _startedDay = startedDay;
            _lastTickDay = startedDay;
            _cooldownUntilDay = -1f;
            _sourceClaimId = sourceClaimId ?? string.Empty;
            _debugReason = debugReason ?? string.Empty;
            _judgment = ClaimFeudJudgment.None;
            _claimantResponse = ClaimFeudResponse.None;
            _holderResponse = ClaimFeudResponse.None;
            _rulingDay = -1f;
            _claimantSidePower = 0f;
            _holderSidePower = 0f;
            _claimantSupporterIds = string.Empty;
            _holderSupporterIds = string.Empty;
            _hasPassed33 = pressure >= 33f;
            _hasPassed66 = pressure >= 66f;
            _resumeState = ClaimFeudState.Agitating;
            _pauseReason = string.Empty;
            _pausedDay = -1f;
        }

        public void SetPressure(float pressure)
        {
            _pressure = pressure;
            if (_pressure < 0f)
                _pressure = 0f;
            if (_pressure > 100f)
                _pressure = 100f;

            if (_pressure < 33f)
                _hasPassed33 = false;
            if (_pressure < 66f)
                _hasPassed66 = false;
        }

        public void MarkMilestonePassed(float threshold)
        {
            if (threshold <= 33f)
                _hasPassed33 = true;
            else if (threshold <= 66f)
                _hasPassed66 = true;
        }

        public void SetState(ClaimFeudState state)
        {
            _state = state;
        }

        public void Pause(ClaimFeudState resumeState, string reason, float day)
        {
            if (resumeState == ClaimFeudState.Paused
                || resumeState == ClaimFeudState.Resolved
                || resumeState == ClaimFeudState.Cooldown
                || resumeState == ClaimFeudState.Settled
                || resumeState == ClaimFeudState.SuppressedCooldown)
            {
                resumeState = ClaimFeudState.Agitating;
            }

            _resumeState = resumeState;
            _pauseReason = reason ?? string.Empty;
            _pausedDay = day;
            _lastTickDay = day;
            _state = ClaimFeudState.Paused;
        }

        public ClaimFeudState Resume(float day)
        {
            ClaimFeudState restored = _resumeState;
            if (restored == ClaimFeudState.Paused
                || restored == ClaimFeudState.Resolved
                || restored == ClaimFeudState.Cooldown
                || restored == ClaimFeudState.Settled
                || restored == ClaimFeudState.SuppressedCooldown)
            {
                restored = ClaimFeudState.Agitating;
            }

            _state = restored;
            _resumeState = ClaimFeudState.Agitating;
            _pauseReason = string.Empty;
            _pausedDay = -1f;
            _lastTickDay = day;
            return restored;
        }

        public void SetLastTickDay(float day)
        {
            _lastTickDay = day;
        }

        public void SetCooldownUntilDay(float day)
        {
            _cooldownUntilDay = day;
        }

        public void SetDebugReason(string reason)
        {
            _debugReason = reason ?? string.Empty;
        }

        public void Reactivate(
            string parentKingdomId,
            FeudalClaimStrength claimStrength,
            float pressure,
            float startedDay,
            string sourceClaimId,
            string debugReason)
        {
            _parentKingdomId = parentKingdomId ?? string.Empty;
            _claimStrength = claimStrength;
            _state = ClaimFeudState.Agitating;
            _pressure = pressure < 0f ? 0f : pressure > 100f ? 100f : pressure;
            _startedDay = startedDay;
            _lastTickDay = startedDay;
            _cooldownUntilDay = -1f;
            _sourceClaimId = sourceClaimId ?? string.Empty;
            _debugReason = debugReason ?? string.Empty;
            _judgment = ClaimFeudJudgment.None;
            _claimantResponse = ClaimFeudResponse.None;
            _holderResponse = ClaimFeudResponse.None;
            _rulingDay = -1f;
            _claimantSidePower = 0f;
            _holderSidePower = 0f;
            _claimantSupporterIds = string.Empty;
            _holderSupporterIds = string.Empty;
            _hasPassed33 = _pressure >= 33f;
            _hasPassed66 = _pressure >= 66f;
            _resumeState = ClaimFeudState.Agitating;
            _pauseReason = string.Empty;
            _pausedDay = -1f;
        }

        public void RecordRuling(
            ClaimFeudJudgment judgment,
            ClaimFeudResponse claimantResponse,
            ClaimFeudResponse holderResponse,
            float rulingDay,
            float claimantSidePower,
            float holderSidePower,
            string claimantSupporterIds,
            string holderSupporterIds)
        {
            _judgment = judgment;
            _claimantResponse = claimantResponse;
            _holderResponse = holderResponse;
            _rulingDay = rulingDay;
            _claimantSidePower = claimantSidePower;
            _holderSidePower = holderSidePower;
            _claimantSupporterIds = claimantSupporterIds ?? string.Empty;
            _holderSupporterIds = holderSupporterIds ?? string.Empty;
        }

        public void RecordSupporters(
            float claimantSidePower,
            float holderSidePower,
            string claimantSupporterIds,
            string holderSupporterIds)
        {
            _claimantSidePower = claimantSidePower;
            _holderSidePower = holderSidePower;
            _claimantSupporterIds = claimantSupporterIds ?? string.Empty;
            _holderSupporterIds = holderSupporterIds ?? string.Empty;
        }
    }
}
