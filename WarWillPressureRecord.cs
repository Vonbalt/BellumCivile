using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class WarWillPressureRecord
    {
        [SaveableField(1)] private string _recordId;
        [SaveableField(2)] private string _clanId;
        [SaveableField(3)] private string _targetKingdomId;
        [SaveableField(4)] private string _conflictKey;
        [SaveableField(5)] private float _amount;
        [SaveableField(6)] private string _reason;
        [SaveableField(7)] private string _contextSettlementId;
        [SaveableField(8)] private string _contextTitleId;
        [SaveableField(9)] private string _contextHeroId;
        [SaveableField(10)] private float _createdDay;
        [SaveableField(11)] private float _expiresDay;
        [SaveableField(12)] private bool _isActive;
        [SaveableField(13)] private WarWillReasonType _reasonType;

        public string RecordId => _recordId;
        public string ClanId => _clanId;
        public string TargetKingdomId => _targetKingdomId;
        public string ConflictKey => _conflictKey;
        public float Amount => _amount;
        public string Reason => _reason;
        public string ContextSettlementId => _contextSettlementId;
        public string ContextTitleId => _contextTitleId;
        public string ContextHeroId => _contextHeroId;
        public float CreatedDay => _createdDay;
        public float ExpiresDay => _expiresDay;
        public bool IsActive => _isActive;
        public WarWillReasonType ReasonType => _reasonType;

        public WarWillPressureRecord(
            string recordId,
            string clanId,
            string targetKingdomId,
            string conflictKey,
            float amount,
            string reason,
            float createdDay,
            float expiresDay,
            WarWillReasonType reasonType = WarWillReasonType.Unknown,
            string contextSettlementId = "",
            string contextTitleId = "",
            string contextHeroId = "")
        {
            _recordId = recordId ?? Guid.NewGuid().ToString("N");
            _clanId = clanId ?? string.Empty;
            _targetKingdomId = targetKingdomId ?? string.Empty;
            _conflictKey = conflictKey ?? string.Empty;
            _amount = amount;
            _reason = reason ?? string.Empty;
            _createdDay = createdDay;
            _expiresDay = expiresDay;
            _contextSettlementId = contextSettlementId ?? string.Empty;
            _contextTitleId = contextTitleId ?? string.Empty;
            _contextHeroId = contextHeroId ?? string.Empty;
            _isActive = true;
            _reasonType = reasonType;
        }

        public bool IsExpired(float day)
        {
            return _expiresDay > 0f && day >= _expiresDay;
        }

        public void End()
        {
            _isActive = false;
        }

        internal void RetargetConflict(string previousKey, string successorKey, string previousRealm, string successorRealm)
        {
            if (!_isActive || _conflictKey != previousKey) return;
            _conflictKey = successorKey;
            if (_targetKingdomId == previousRealm) _targetKingdomId = successorRealm;
        }
    }
}
