using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalClaimRecord
    {
        [SaveableField(1)] private string _claimId;
        [SaveableField(2)] private string _claimantClanId;
        [SaveableField(3)] private string _targetTitleId;
        [SaveableField(4)] private FeudalClaimStrength _strength;
        [SaveableField(5)] private string _source;
        [SaveableField(6)] private string _sourceHeroId;
        [SaveableField(7)] private string _originClanId;
        [SaveableField(8)] private float _createdDay;
        [SaveableField(9)] private float _expiresDay;
        [SaveableField(10)] private bool _isActive;
        [SaveableField(11)] private int _generationDepth;
        [SaveableField(12)] private string _carrierHeroId;

        public string ClaimId => _claimId;
        public string ClaimantClanId => _claimantClanId;
        public string TargetTitleId => _targetTitleId;
        public FeudalClaimStrength Strength => _strength;
        public string Source => _source;
        public string SourceHeroId => _sourceHeroId;
        public string OriginClanId => _originClanId;
        public float CreatedDay => _createdDay;
        public float ExpiresDay => _expiresDay;
        public bool IsActive => _isActive;
        public int GenerationDepth => _generationDepth;
        public string CarrierHeroId => _carrierHeroId;

        public FeudalClaimRecord(
            string claimId,
            string claimantClanId,
            string targetTitleId,
            FeudalClaimStrength strength,
            string source,
            string sourceHeroId,
            string originClanId,
            float createdDay,
            float expiresDay,
            int generationDepth = 0,
            string carrierHeroId = null,
            bool isActive = true)
        {
            _claimId = claimId ?? string.Empty;
            _claimantClanId = claimantClanId ?? string.Empty;
            _targetTitleId = targetTitleId ?? string.Empty;
            _strength = strength;
            _source = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            _sourceHeroId = sourceHeroId ?? string.Empty;
            _originClanId = originClanId ?? string.Empty;
            _createdDay = createdDay;
            _expiresDay = expiresDay;
            _generationDepth = generationDepth;
            _carrierHeroId = carrierHeroId ?? string.Empty;
            _isActive = isActive;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
        }

        internal FeudalClaimRecord CopyForEstate() => new FeudalClaimRecord(
            _claimId, _claimantClanId, _targetTitleId, _strength, _source, _sourceHeroId,
            _originClanId, _createdDay, _expiresDay, _generationDepth, _carrierHeroId, _isActive);

        public void ReplaceCarrier(Hero carrier)
        {
            _carrierHeroId = carrier?.StringId ?? string.Empty;
        }

        internal bool MoveWithCarrier(string carrierId, string previousClanId, string receivingClanId)
        {
            if (!_isActive || string.IsNullOrWhiteSpace(carrierId)
                || string.IsNullOrWhiteSpace(previousClanId) || string.IsNullOrWhiteSpace(receivingClanId)
                || previousClanId == receivingClanId || _carrierHeroId != carrierId
                || _claimantClanId != previousClanId)
                return false;
            // This is the same person's claim, not inheritance by a new generation.
            _claimantClanId = receivingClanId;
            return true;
        }
    }
}
