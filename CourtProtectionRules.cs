using System;

namespace BellumCivile
{
    internal enum CourtProtectionReach { None, Maritime, Land }

    internal sealed class CourtProtectionScore
    {
        internal double Baseline => 50;
        internal double Military { get; set; }
        internal double OtherWars { get; set; }
        internal double StrategicValue { get; set; }
        internal double Relations { get; set; }
        internal double Personality { get; set; }
        internal double ExistingWar { get; set; }
        internal double Total => Baseline + Military + OtherWars + StrategicValue + Relations + Personality + ExistingWar;
        internal bool WouldAccept => Total >= CourtProtectionRules.AcceptanceThreshold;
    }

    internal static class CourtProtectionRules
    {
        internal const string Kind = "crown_seek_protection";
        internal const double AcceptanceThreshold = 60;
        internal static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static double Clamp(double n, double min, double max) => Math.Max(min, Math.Min(max, n));
        internal static bool Desperate(double own, double enemy, int fiefCount, double fiefValue) =>
            Finite(own) && Finite(enemy) && Finite(fiefValue) && own >= 0 && enemy > 0
            && enemy >= 2 * own && fiefCount > 0 && fiefValue >= 0 && fiefValue < 150;

        internal static CourtProtectionScore Score(double ratio, double otherLoad, double value,
            double relation, double personality, bool alreadyAtWar)
        {
            if (!Finite(ratio) || ratio < 0 || !Finite(otherLoad) || otherLoad < 0
                || !Finite(value) || !Finite(relation) || !Finite(personality)) return null;
            return new CourtProtectionScore {
                Military = Clamp((ratio < 1 ? 60 : 40) * (ratio - 1), -30, 25),
                OtherWars = -25 * Clamp(otherLoad, 0, 1), StrategicValue = Clamp(value, 0, 15),
                Relations = Clamp(relation * 0.1, -10, 10), Personality = Clamp(personality, -10, 10),
                ExistingWar = alreadyAtWar ? 15 : 0
            };
        }

        internal static double StrategicValue(CourtProtectionReach reach, double fiefValue, bool sharedEnemyFrontier) =>
            !Finite(fiefValue) || fiefValue < 0 ? 0 : (reach == CourtProtectionReach.Land ? 5 : reach == CourtProtectionReach.Maritime ? 3 : 0)
            + 5 * Clamp(fiefValue / 150, 0, 1) + (sharedEnemyFrontier ? 5 : 0);

        internal static double ProtectorPersonality(int valor, int mercy, int honor, int calculating, double ratio) =>
            !Finite(ratio) || ratio < 0 ? 0 : Clamp(2.5 * (valor + mercy + honor + calculating * Clamp(2 * (ratio - 1), -1, 1)), -10, 10);

        // Competes with other Crown business; never a hard personality gate for the player.
        internal static double MotionWeight(double threatRatio, double ownWarScore, double warWill, int calculating, int valor) =>
            !Finite(threatRatio) || !Finite(ownWarScore) || !Finite(warWill) || threatRatio < 2 ? 0
            : Clamp(0.10 + 0.10 * Clamp(threatRatio - 2, 0, 3) + 0.10 * Clamp(-ownWarScore / 100, 0, 1)
                + 0.10 * Clamp((100 - warWill) / 100, 0, 1) + 0.025 * calculating - 0.025 * valor, 0.025, 0.50);
    }
}
