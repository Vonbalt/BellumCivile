namespace BellumCivile
{
    public enum ForeignPolicyTributeClass
    {
        None,
        Favorable,
        Affordable,
        Costly,
        Humiliating,
        Ruinous
    }

    public sealed class ForeignPolicyTributeAssessment
    {
        public ForeignPolicyTributeClass Classification { get; set; }
        public int DailyTribute { get; set; }
        public int DurationDays { get; set; }
        public long TotalCost { get; set; }
        public long RealmLiquidWealth { get; set; }
        public float BurdenRatio { get; set; }
    }
}
