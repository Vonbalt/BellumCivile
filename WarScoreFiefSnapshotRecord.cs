using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class WarScoreFiefSnapshotRecord
    {
        [SaveableField(1)] private string _settlementId;
        [SaveableField(2)] private string _ownerKingdomId;
        [SaveableField(3)] private string _ownerClanId;
        [SaveableField(4)] private bool _isTown;
        [SaveableField(5)] private bool _isCastle;
        [SaveableField(6)] private string _originalOwnerKingdomId;

        public string SettlementId => _settlementId;
        public string OwnerKingdomId => _ownerKingdomId;
        public string OriginalOwnerKingdomId => _originalOwnerKingdomId ?? _ownerKingdomId;
        public string OwnerClanId => _ownerClanId;
        public bool IsTown => _isTown;
        public bool IsCastle => _isCastle;

        public WarScoreFiefSnapshotRecord(
            string settlementId,
            string ownerKingdomId,
            string ownerClanId,
            bool isTown,
            bool isCastle)
        {
            _settlementId = settlementId ?? string.Empty;
            _ownerKingdomId = ownerKingdomId ?? string.Empty;
            _originalOwnerKingdomId = _ownerKingdomId;
            _ownerClanId = ownerClanId ?? string.Empty;
            _isTown = isTown;
            _isCastle = isCastle;
        }

        internal void RetargetOwnerRealm(string previous, string successor)
        {
            if (_ownerKingdomId != previous) return;
            _originalOwnerKingdomId = _originalOwnerKingdomId ?? _ownerKingdomId;
            _ownerKingdomId = successor;
        }

        internal void RecordVoluntaryGrant(string donorRealm, string donorClan, string recipientRealm, string recipientClan)
        {
            if (_ownerKingdomId != donorRealm || _ownerClanId != donorClan
                || string.IsNullOrEmpty(recipientRealm) || string.IsNullOrEmpty(recipientClan)) return;
            _ownerKingdomId = recipientRealm;
            _ownerClanId = recipientClan;
        }
    }
}
