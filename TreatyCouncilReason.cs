namespace BellumCivile
{
    public sealed class TreatyCouncilReason
    {
        public string Label { get; }
        public float Amount { get; }

        public TreatyCouncilReason(string label, float amount)
        {
            Label = label ?? string.Empty;
            Amount = amount;
        }
    }
}
