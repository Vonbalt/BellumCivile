using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    /// <summary>
    /// Canonical per-kingdom dynastic state. Other systems should consult this record instead of
    /// separately inferring the rightful dynasty from court affiliation, cadet origins, or cached heirs.
    /// </summary>
    public class DynasticSuccessionStateRecord
    {
        [SaveableField(1)] private string _kingdomId;
        [SaveableField(2)] private string _rightfulDynastyClanId;
        [SaveableField(3)] private string _heirHeroId;
        [SaveableField(4)] private string _claimCarrierClanId;
        [SaveableField(5)] private string _claimCarrierHeroId;
        [SaveableField(6)] private string _source;
        [SaveableField(7)] private CampaignTime _lockedUntil;
        [SaveableField(8)] private float _lastUpdatedDay;

        public string KingdomId => _kingdomId;
        public string RightfulDynastyClanId => _rightfulDynastyClanId;
        public string HeirHeroId => _heirHeroId;
        public string ClaimCarrierClanId => _claimCarrierClanId;
        public string ClaimCarrierHeroId => _claimCarrierHeroId;
        public string Source => _source;
        public CampaignTime LockedUntil { get => _lockedUntil; set => _lockedUntil = value; }
        public float LastUpdatedDay => _lastUpdatedDay;
        public bool IsLocked => !_lockedUntil.IsPast;

        public DynasticSuccessionStateRecord(
            string kingdomId,
            string rightfulDynastyClanId,
            string heirHeroId,
            string claimCarrierClanId,
            string claimCarrierHeroId,
            string source,
            CampaignTime lockedUntil,
            float lastUpdatedDay)
        {
            _kingdomId = kingdomId ?? string.Empty;
            _rightfulDynastyClanId = rightfulDynastyClanId ?? string.Empty;
            _heirHeroId = heirHeroId ?? string.Empty;
            _claimCarrierClanId = claimCarrierClanId ?? string.Empty;
            _claimCarrierHeroId = claimCarrierHeroId ?? string.Empty;
            _source = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            _lockedUntil = lockedUntil;
            _lastUpdatedDay = lastUpdatedDay;
        }
    }
}
