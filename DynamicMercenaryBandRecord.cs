using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class DynamicMercenaryBandRecord
    {
        [SaveableField(1)] private string _clanId;
        [SaveableField(2)] private string _founderHeroId;
        [SaveableField(3)] private string _sourceClanId;
        [SaveableField(4)] private string _cultureId;
        [SaveableField(5)] private string _homeSettlementId;
        [SaveableField(6)] private CampaignTime _createdAt;

        public string ClanId => _clanId ?? string.Empty;
        public string FounderHeroId => _founderHeroId ?? string.Empty;
        public string SourceClanId => _sourceClanId ?? string.Empty;
        public string CultureId => _cultureId ?? string.Empty;
        public string HomeSettlementId => _homeSettlementId ?? string.Empty;
        public CampaignTime CreatedAt => _createdAt;

        public DynamicMercenaryBandRecord()
        {
        }

        public DynamicMercenaryBandRecord(
            string clanId,
            string founderHeroId,
            string sourceClanId,
            string cultureId,
            string homeSettlementId,
            CampaignTime createdAt)
        {
            _clanId = clanId ?? string.Empty;
            _founderHeroId = founderHeroId ?? string.Empty;
            _sourceClanId = sourceClanId ?? string.Empty;
            _cultureId = cultureId ?? string.Empty;
            _homeSettlementId = homeSettlementId ?? string.Empty;
            _createdAt = createdAt;
        }
    }
}
