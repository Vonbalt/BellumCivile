using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal readonly struct PeaceReadinessAssessment
    {
        internal float Exhaustion { get; }
        internal float EarlyWarReluctance { get; }
        internal float InconclusiveWar { get; }
        internal float ProlongedExhaustion { get; }
        internal float Total => Exhaustion + EarlyWarReluctance + InconclusiveWar + ProlongedExhaustion;

        internal PeaceReadinessAssessment(float exhaustion, float early, float inconclusive, float prolonged)
        {
            Exhaustion = exhaustion;
            EarlyWarReluctance = early;
            InconclusiveWar = inconclusive;
            ProlongedExhaustion = prolonged;
        }
    }

    // Shared willingness, not permission to impose terms or a replacement for treaty utility.
    internal static class PeaceReadiness
    {
        internal static PeaceReadinessAssessment Evaluate(float days, float score, float enthusiasm, int reluctanceDays)
        {
            days = Math.Max(0f, days);
            enthusiasm = Math.Max(0f, Math.Min(100f, enthusiasm));
            float period = Math.Max(1, reluctanceDays);
            float resolve = Unit(enthusiasm / C.WarPeaceRevampPeaceProposalThreshold);
            float exhaustion = (20f - enthusiasm) * .5f + 35f * (1f - resolve);
            float early = -Math.Max(0f, period - days) * (100f / period) * resolve;
            float inconclusive = -30f * Unit((50f - Math.Abs(score)) / 50f)
                * resolve * Unit((90f - days) / 60f);
            float prolonged = days >= period && enthusiasm < C.WarPeaceRevampPeaceProposalThreshold ? 30f : 0f;
            return new PeaceReadinessAssessment(exhaustion, early, inconclusive, prolonged);
        }

        internal static bool CanConcede(float relativeWinnerScore, float losingEnthusiasm, float days, int reluctanceDays)
        {
            if (relativeWinnerScore >= C.WarScoreForcePeaceThreshold) return true;
            bool ordinary = relativeWinnerScore >= C.WarScoreExhaustedVictoryThreshold
                && losingEnthusiasm < C.WarPeaceRevampPeaceProposalThreshold;
            bool prolonged = days >= Math.Max(1, reluctanceDays)
                && relativeWinnerScore > C.WarScoreWhitePeaceMaximumScore && losingEnthusiasm <= 0f;
            return (ordinary || prolonged)
                && Evaluate(days, relativeWinnerScore, losingEnthusiasm, reluctanceDays).Total >= C.WarPeaceReadinessSupportThreshold;
        }

        internal static bool CanConsiderWhitePeace(float score, float firstWill, float secondWill, float days, int reluctanceDays)
        {
            return Math.Abs(score) <= C.WarScoreWhitePeaceMaximumScore
                && firstWill <= C.WarPeaceRevampMutualWhitePeaceThreshold
                && secondWill <= C.WarPeaceRevampMutualWhitePeaceThreshold
                && Evaluate(days, score, firstWill, reluctanceDays).Total >= C.WarPeaceReadinessSupportThreshold
                && Evaluate(days, score, secondWill, reluctanceDays).Total >= C.WarPeaceReadinessSupportThreshold;
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
