using System;

namespace BellumCivile
{
    // Provisional balance for voluntary proposals; war treaties retain their own rules.
    internal static class PeacetimeHostagePactRules
    {
        internal const int ProposalInfluenceCost = 100;
        internal const int EvaluationIntervalDays = 30;
        internal const int MaxRealmEvaluationsPerDay = 5;
        internal const int RefusalCooldownDays = 90;
        internal const float EvaluationChance = 0.05f;
        internal const float AcceptanceThreshold = 50f;

        internal static double PaymentBonus(int gold)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            return 30d * (1d - Math.Exp(-gold / 50000d));
        }

        internal static double WarTargetPenalty(float score)
        {
            if (float.IsNaN(score) || float.IsInfinity(score)) throw new ArgumentOutOfRangeException(nameof(score));
            return 15d * Math.Max(0d, Math.Min(100d, score)) / 100d;
        }

        internal static double Score(int relation, bool commonThreat, bool trade, bool rulingMarriage,
            float warTargetScore, int ownHostageTier, int otherHostageTier,
            int durationDays, int durationBaselineDays, int paymentReceived)
        {
            if (ownHostageTier < 1 || ownHostageTier > 4 || otherHostageTier < 1 || otherHostageTier > 4)
                throw new ArgumentOutOfRangeException(nameof(ownHostageTier));
            if (durationBaselineDays <= 0 || !HostagePactRules.IsNegotiableDuration(durationDays, durationBaselineDays))
                throw new ArgumentOutOfRangeException(nameof(durationDays));
            if (float.IsNaN(warTargetScore) || float.IsInfinity(warTargetScore))
                throw new ArgumentOutOfRangeException(nameof(warTargetScore));

            double relationEffect = Math.Max(-10d, Math.Min(10d, relation / 5d));
            double warEffect = WarTargetPenalty(warTargetScore);
            double heirReluctance = (4 - ownHostageTier) * 5d;
            double hostageBalance = (ownHostageTier - otherHostageTier) * 4d;
            double result = 40d + relationEffect + (commonThreat ? 15d : 0d)
                + (trade ? 5d : 0d) + (rulingMarriage ? 8d : 0d)
                - warEffect - heirReluctance + hostageBalance
                - HostagePactRules.GetDurationPenalty(durationDays, durationBaselineDays)
                + PaymentBonus(paymentReceived);
            return Math.Max(0d, Math.Min(100d, result));
        }
    }
}
