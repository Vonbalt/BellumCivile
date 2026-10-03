using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class ForeignPolicyTributeHelper
    {
        public static ForeignPolicyTributeAssessment AssessCurrentTerms(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            if (sourceKingdom?.RulingClan == null || targetKingdom?.RulingClan == null)
                return new ForeignPolicyTributeAssessment();

            try
            {
                int durationDays;
                int dailyTribute = Campaign.Current.Models.DiplomacyModel.GetDailyTributeToPay(
                    sourceKingdom.RulingClan,
                    targetKingdom.RulingClan,
                    out durationDays);
                return Assess(sourceKingdom, dailyTribute, durationDays);
            }
            catch
            {
                return new ForeignPolicyTributeAssessment();
            }
        }

        public static ForeignPolicyTributeAssessment Assess(Kingdom payingKingdom, int dailyTribute, int durationDays)
        {
            ForeignPolicyTributeAssessment result = new ForeignPolicyTributeAssessment
            {
                DailyTribute = dailyTribute,
                DurationDays = Math.Abs(durationDays),
                RealmLiquidWealth = CalculateRealmLiquidWealth(payingKingdom)
            };

            if (dailyTribute < 0)
            {
                result.Classification = ForeignPolicyTributeClass.Favorable;
                return result;
            }
            if (dailyTribute == 0 || result.DurationDays <= 0)
            {
                result.Classification = ForeignPolicyTributeClass.None;
                return result;
            }

            result.TotalCost = (long)dailyTribute * result.DurationDays;
            result.BurdenRatio = result.TotalCost / (float)Math.Max(1L, result.RealmLiquidWealth);
            if (result.BurdenRatio <= C.ForeignPolicyTributeAffordableBurden)
                result.Classification = ForeignPolicyTributeClass.Affordable;
            else if (result.BurdenRatio <= C.ForeignPolicyTributeCostlyBurden)
                result.Classification = ForeignPolicyTributeClass.Costly;
            else if (result.BurdenRatio <= C.ForeignPolicyTributeHumiliatingBurden)
                result.Classification = ForeignPolicyTributeClass.Humiliating;
            else
                result.Classification = ForeignPolicyTributeClass.Ruinous;

            return result;
        }

        private static long CalculateRealmLiquidWealth(Kingdom kingdom)
        {
            if (kingdom == null)
                return 0L;

            return kingdom.Clans
                .Where(clan => clan != null && !clan.IsEliminated && clan.Leader != null)
                .Sum(clan => (long)Math.Max(0, clan.Leader.Gold));
        }
    }
}
