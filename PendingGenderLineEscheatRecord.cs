using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PendingGenderLineEscheatRecord
    {
        [SaveableField(1)] private string _triggerHeroId;
        [SaveableField(2)] private string _clanId;
        [SaveableField(3)] private string _kingdomId;
        [SaveableField(4)] private GenderSuccessionLaw _genderLaw;
        [SaveableField(5)] private CampaignTime _readyDate;
        [SaveableField(6)] private bool _wasRulingClan;
        [SaveableField(7)] private bool _dynasticLineExtinction;

        public string TriggerHeroId => _triggerHeroId ?? string.Empty;
        public string ClanId => _clanId ?? string.Empty;
        public string KingdomId => _kingdomId ?? string.Empty;
        public GenderSuccessionLaw GenderLaw => _genderLaw;
        public CampaignTime ReadyDate { get => _readyDate; set => _readyDate = value; }
        public bool WasRulingClan => _wasRulingClan;
        public bool DynasticLineExtinction => _dynasticLineExtinction;

        public PendingGenderLineEscheatRecord(
            string triggerHeroId,
            string clanId,
            string kingdomId,
            GenderSuccessionLaw genderLaw,
            CampaignTime readyDate,
            bool wasRulingClan,
            bool dynasticLineExtinction = false)
        {
            _triggerHeroId = triggerHeroId ?? string.Empty;
            _clanId = clanId ?? string.Empty;
            _kingdomId = kingdomId ?? string.Empty;
            _genderLaw = genderLaw;
            _readyDate = readyDate;
            _wasRulingClan = wasRulingClan;
            _dynasticLineExtinction = dynasticLineExtinction;
        }
    }
}
