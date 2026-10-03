using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class ActiveTreatyTributeRecord
    {
        [SaveableField(1)] private string _payerKingdomId;
        [SaveableField(2)] private string _recipientKingdomId;
        [SaveableField(3)] private int _dailyGold;
        [SaveableField(4)] private int _remainingDays;

        public string PayerKingdomId => _payerKingdomId;
        public string RecipientKingdomId => _recipientKingdomId;
        public int DailyGold => _dailyGold;
        public int RemainingDays => _remainingDays;

        public ActiveTreatyTributeRecord(string payerKingdomId, string recipientKingdomId, int dailyGold, int durationDays)
        {
            _payerKingdomId = payerKingdomId ?? string.Empty;
            _recipientKingdomId = recipientKingdomId ?? string.Empty;
            _dailyGold = dailyGold;
            _remainingDays = durationDays;
        }

        public void TickDay()
        {
            _remainingDays--;
        }
    }
}
