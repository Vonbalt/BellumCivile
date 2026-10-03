using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.WarPeace
{
    internal sealed class WarFrontReadinessAssessment
    {
        public int ExistingForeignWarCount { get; set; }
        public float RealmStrength { get; set; }
        public float ExistingEnemyStrength { get; set; }
        public float ProposedTargetStrength { get; set; }
        public float CombinedOppositionStrength { get; set; }
        public float PowerRatio { get; set; }
        public float CouncilSupportPenalty { get; set; }
        public float TargetScorePenalty { get; set; }

        public bool IsAdditionalFront => ExistingForeignWarCount > 0;
        public bool IsThirdOrLaterFront => ExistingForeignWarCount >= 2;
        public bool MeetsMinimumPowerRatio => !IsAdditionalFront
            || PowerRatio >= C.WarPeaceRevampSecondFrontMinimumPowerRatio;
        public bool CanDeclare => !IsThirdOrLaterFront && MeetsMinimumPowerRatio;
    }

    internal static class WarFrontReadinessService
    {
        public static WarFrontReadinessAssessment Assess(Kingdom source, Kingdom proposedTarget)
        {
            WarFrontReadinessAssessment result = new WarFrontReadinessAssessment
            {
                RealmStrength = Math.Max(1f, source?.CurrentTotalStrength ?? 1f),
                ProposedTargetStrength = Math.Max(1f, proposedTarget?.CurrentTotalStrength ?? 1f)
            };

            if (source == null || proposedTarget == null || source == proposedTarget
                || source.IsAtWarWith(proposedTarget))
            {
                result.CombinedOppositionStrength = result.ProposedTargetStrength;
                result.PowerRatio = result.RealmStrength / result.CombinedOppositionStrength;
                return result;
            }

            List<Kingdom> existingEnemies = source.FactionsAtWarWith
                .OfType<Kingdom>()
                .Where(enemy => IsForeignEnemy(source, enemy) && enemy != proposedTarget)
                .GroupBy(enemy => enemy.StringId)
                .Select(group => group.First())
                .ToList();

            result.ExistingForeignWarCount = existingEnemies.Count;
            result.ExistingEnemyStrength = existingEnemies.Sum(enemy => Math.Max(1f, enemy.CurrentTotalStrength));
            result.CombinedOppositionStrength = Math.Max(
                1f,
                result.ExistingEnemyStrength + result.ProposedTargetStrength);
            result.PowerRatio = result.RealmStrength / result.CombinedOppositionStrength;

            if (result.ExistingForeignWarCount == 1)
            {
                float risk = 1f - Clamp01(
                    (result.PowerRatio - C.WarPeaceRevampSecondFrontMinimumPowerRatio)
                    / (C.WarPeaceRevampSecondFrontSafePowerRatio - C.WarPeaceRevampSecondFrontMinimumPowerRatio));
                result.CouncilSupportPenalty = risk * C.WarPeaceRevampSecondFrontMaximumCouncilPenalty;
                result.TargetScorePenalty = risk * C.WarPeaceRevampSecondFrontMaximumTargetPenalty;
            }

            return result;
        }

        private static bool IsForeignEnemy(Kingdom source, Kingdom enemy)
        {
            if (enemy == null || enemy.IsEliminated || enemy == source
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(enemy))
            {
                return false;
            }

            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            WarScoreRecord record = warScore?.GetActiveWar(source, enemy);
            return record == null || record.ConflictType == WarScoreConflictType.ForeignWar;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
