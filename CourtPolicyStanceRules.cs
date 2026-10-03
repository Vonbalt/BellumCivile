namespace BellumCivile
{
    internal static class CourtPolicyStanceRules
    {
        internal const float OpposeThrough = 20f;
        internal const float SupportFrom = 60f;

        internal static CourtPolicyStance Resolve(CourtPolicyStance interest, bool crown, float mood)
        {
            if (!crown) return interest;
            if (mood >= SupportFrom) return CourtPolicyStance.Support;
            if (mood > OpposeThrough) return CourtPolicyStance.Neutral;
            return CourtPolicyStance.Oppose;
        }

        internal static bool LostMandate(bool captured, CourtPolicyStance initial, CourtPolicyStance current, bool repeal)
        {
            var required = repeal ? CourtPolicyStance.Oppose : CourtPolicyStance.Support;
            return captured && initial == required && current != required;
        }
    }
}
