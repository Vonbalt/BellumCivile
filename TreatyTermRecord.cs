using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class TreatyTermRecord
    {
        [SaveableField(1)] private TreatyTermType _type;
        [SaveableField(2)] private int _warScoreCost;
        [SaveableField(3)] private string _settlementId;
        [SaveableField(4)] private int _goldAmount;
        [SaveableField(5)] private int _dailyGold;
        [SaveableField(6)] private int _durationDays;
        [SaveableField(7)] private string _fromKingdomId;
        [SaveableField(8)] private string _toKingdomId;
        [SaveableField(9)] private bool _wasOccupiedAtDrafting;
        [SaveableField(10)] private string _heroId;
        [SaveableField(11)] private string _clanId;
        [SaveableField(12)] private string _titleId;
        [SaveableField(13)] private string _secondaryHeroId;
        [SaveableField(14)] private string _thirdKingdomId;
        [SaveableField(15)] private bool _wasVoluntaryOffering;
        [SaveableField(16)] private int _hostageTier;
        [SaveableField(17)] private string _hostageReceivingClanId;

        public TreatyTermType Type => _type;
        public int WarScoreCost => _warScoreCost;
        public string SettlementId => _settlementId;
        public int GoldAmount => _goldAmount;
        public int DailyGold => _dailyGold;
        public int DurationDays => _durationDays;
        public string FromKingdomId => _fromKingdomId;
        public string ToKingdomId => _toKingdomId;
        public bool WasOccupiedAtDrafting => _wasOccupiedAtDrafting;
        public string HeroId => _heroId;
        public string ClanId => _clanId;
        public string TitleId => _titleId;
        public string SecondaryHeroId => _secondaryHeroId;
        public string ThirdKingdomId => _thirdKingdomId;
        public bool WasVoluntaryOffering => _wasVoluntaryOffering;
        public int HostageTier => _hostageTier;
        public string HostageReceivingClanId => _hostageReceivingClanId;

        public TreatyTermRecord(
            TreatyTermType type,
            int warScoreCost,
            string settlementId = "",
            int goldAmount = 0,
            int dailyGold = 0,
            int durationDays = 0,
            string fromKingdomId = "",
            string toKingdomId = "",
            bool wasOccupiedAtDrafting = false,
            string heroId = "",
            string clanId = "",
            string titleId = "",
            string secondaryHeroId = "",
            string thirdKingdomId = "",
            bool wasVoluntaryOffering = false,
            int hostageTier = 0,
            string hostageReceivingClanId = "")
        {
            _type = type;
            _warScoreCost = warScoreCost;
            _settlementId = settlementId ?? string.Empty;
            _goldAmount = goldAmount;
            _dailyGold = dailyGold;
            _durationDays = durationDays;
            _fromKingdomId = fromKingdomId ?? string.Empty;
            _toKingdomId = toKingdomId ?? string.Empty;
            _wasOccupiedAtDrafting = wasOccupiedAtDrafting;
            _heroId = heroId ?? string.Empty;
            _clanId = clanId ?? string.Empty;
            _titleId = titleId ?? string.Empty;
            _secondaryHeroId = secondaryHeroId ?? string.Empty;
            _thirdKingdomId = thirdKingdomId ?? string.Empty;
            _wasVoluntaryOffering = wasVoluntaryOffering;
            _hostageTier = hostageTier;
            _hostageReceivingClanId = hostageReceivingClanId ?? string.Empty;
        }

        public int GetWarScoreImpact(string favoredKingdomId)
        {
            // This is the uncapped face impact of one term. Proposal-level accounting applies
            // the aggregate reciprocal-credit ceiling across the complete draft.
            if (_type == TreatyTermType.WhitePeace || string.IsNullOrWhiteSpace(favoredKingdomId))
                return 0;
            if (_toKingdomId == favoredKingdomId)
                return _warScoreCost;
            if (_fromKingdomId == favoredKingdomId && _wasVoluntaryOffering)
                return -(int)System.Math.Floor(_warScoreCost * BellumCivileConstants.TreatyOfferingCreditMultiplier);
            return 0;
        }
    }
}
