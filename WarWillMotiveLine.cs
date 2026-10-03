namespace BellumCivile
{
    public sealed class WarWillMotiveLine
    {
        public WarWillReasonType ReasonType { get; }
        public float Amount { get; }
        public string ShortLabel { get; }
        public string TooltipLine { get; }

        public WarWillMotiveLine(WarWillReasonType reasonType, float amount, string shortLabel, string tooltipLine)
        {
            ReasonType = reasonType;
            Amount = amount;
            ShortLabel = shortLabel ?? string.Empty;
            TooltipLine = tooltipLine ?? string.Empty;
        }
    }
}
