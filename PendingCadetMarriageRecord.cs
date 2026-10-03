using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To persist a dynastic marriage that should found a cadet branch on the next daily tick,
    /// along with the captured marriage payment that will be passed through as the new house's
    /// starting treasury.
    /// </summary>
    public class PendingCadetMarriageRecord
    {
        [SaveableField(1)] private string _heiressId;
        [SaveableField(2)] private string _spouseId;
        [SaveableField(3)] private string _originKingdomId;
        [SaveableField(4)] private string _dynastyClanId;
        [SaveableField(5)] private string _brideClanId;
        [SaveableField(6)] private string _marriedClanId;
        [SaveableField(7)] private CampaignTime _readyDate;
        [SaveableField(8)] private int _marriageGold;
        [SaveableField(9)] private int _attempts;
        [SaveableField(10)] private CampaignTime _expiresOn;

        public string HeiressId => _heiressId;
        public string SpouseId => _spouseId;
        public string OriginKingdomId => _originKingdomId;
        public string DynastyClanId => _dynastyClanId;
        public string BrideClanId => _brideClanId;
        public string MarriedClanId => _marriedClanId;
        public CampaignTime ReadyDate { get => _readyDate; set => _readyDate = value; }
        public int MarriageGold { get => _marriageGold; set => _marriageGold = value; }
        public int Attempts { get => _attempts; set => _attempts = value; }
        public CampaignTime ExpiresOn { get => _expiresOn; set => _expiresOn = value; }

        public PendingCadetMarriageRecord(string heiressId, string spouseId, string originKingdomId, string dynastyClanId, string brideClanId, string marriedClanId, CampaignTime readyDate, int marriageGold)
        {
            _heiressId = heiressId ?? string.Empty;
            _spouseId = spouseId ?? string.Empty;
            _originKingdomId = originKingdomId ?? string.Empty;
            _dynastyClanId = dynastyClanId ?? string.Empty;
            _brideClanId = brideClanId ?? string.Empty;
            _marriedClanId = marriedClanId ?? string.Empty;
            _readyDate = readyDate;
            _marriageGold = marriageGold;
            _attempts = 0;
            _expiresOn = readyDate + CampaignTime.Days(1f);
        }
    }
}
