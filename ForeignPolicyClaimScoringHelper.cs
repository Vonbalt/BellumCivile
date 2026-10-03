using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class ForeignPolicyClaimScoreBreakdown
    {
        public float RealmPressure { get; set; }
        public float PersonalPressure { get; set; }
        public float ScoreBonus { get; set; }
        public int ClaimCount { get; set; }
    }

    internal static class ForeignPolicyClaimScoringHelper
    {
        public static ForeignPolicyClaimScoreBreakdown Calculate(
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan evaluatingClan,
            float rawDiplomacyScore,
            float decisionThreshold)
        {
            IReadOnlyList<ForeignPolicyClaimStake> stakes = ForeignPolicyEvaluationService.GetClaimStakes(
                sourceKingdom,
                targetKingdom);
            return Calculate(stakes, sourceKingdom, evaluatingClan, rawDiplomacyScore, decisionThreshold);
        }

        public static ForeignPolicyClaimScoreBreakdown Calculate(
            IEnumerable<ForeignPolicyClaimStake> claimStakes,
            Kingdom sourceKingdom,
            Clan evaluatingClan,
            float rawDiplomacyScore,
            float decisionThreshold)
        {
            ForeignPolicyClaimScoreBreakdown result = new ForeignPolicyClaimScoreBreakdown();
            if (sourceKingdom == null
                || rawDiplomacyScore <= C.ForeignPolicyHardWarVetoScore)
            {
                return result;
            }

            List<ForeignPolicyClaimStake> stakes = claimStakes?
                .Where(stake => stake != null && stake.Title != null && stake.ClaimantClan != null)
                .ToList() ?? new List<ForeignPolicyClaimStake>();
            result.ClaimCount = stakes.Count;
            result.RealmPressure = Math.Min(
                C.ForeignPolicyRealmClaimPressureCap,
                stakes.Sum(stake => stake.Pressure));

            if (evaluatingClan != null && evaluatingClan.Kingdom == sourceKingdom)
            {
                result.PersonalPressure = Math.Min(
                    C.ForeignPolicyPersonalClaimPressureCap,
                    stakes.Where(stake => stake.ClaimantClan == evaluatingClan).Sum(stake => stake.Pressure));
            }

            float scoreScale = Math.Max(C.ForeignPolicyMinimumScoreScale, Math.Abs(decisionThreshold));
            result.ScoreBonus = scoreScale * (result.RealmPressure + result.PersonalPressure) / 100f;
            return result;
        }
    }
}
