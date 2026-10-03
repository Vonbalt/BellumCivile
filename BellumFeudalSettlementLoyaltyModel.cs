using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    /// <summary>
    /// Applies legal-title unrest to settlement loyalty while preserving the active loyalty model underneath.
    /// </summary>
    public class BellumFeudalSettlementLoyaltyModel : SettlementLoyaltyModel
    {
        private readonly SettlementLoyaltyModel _baseModel;

        public BellumFeudalSettlementLoyaltyModel(SettlementLoyaltyModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultSettlementLoyaltyModel();
        }

        public override float HighLoyaltyProsperityEffect => _baseModel.HighLoyaltyProsperityEffect;
        public override int LowLoyaltyProsperityEffect => _baseModel.LowLoyaltyProsperityEffect;
        public override int MilitiaBoostPercentage => _baseModel.MilitiaBoostPercentage;
        public override float HighSecurityLoyaltyEffect => _baseModel.HighSecurityLoyaltyEffect;
        public override float LowSecurityLoyaltyEffect => _baseModel.LowSecurityLoyaltyEffect;
        public override float GovernorSameCultureLoyaltyEffect => _baseModel.GovernorSameCultureLoyaltyEffect;
        public override float GovernorDifferentCultureLoyaltyEffect => _baseModel.GovernorDifferentCultureLoyaltyEffect;
        public override float SettlementOwnerDifferentCultureLoyaltyEffect => _baseModel.SettlementOwnerDifferentCultureLoyaltyEffect;
        public override int ThresholdForTaxBoost => _baseModel.ThresholdForTaxBoost;
        public override int RebellionStartLoyaltyThreshold => _baseModel.RebellionStartLoyaltyThreshold;
        public override int ThresholdForTaxCorruption => _baseModel.ThresholdForTaxCorruption;
        public override int ThresholdForHigherTaxCorruption => _baseModel.ThresholdForHigherTaxCorruption;
        public override int ThresholdForProsperityBoost => _baseModel.ThresholdForProsperityBoost;
        public override int ThresholdForProsperityPenalty => _baseModel.ThresholdForProsperityPenalty;
        public override int AdditionalStarvationPenaltyStartDay => _baseModel.AdditionalStarvationPenaltyStartDay;
        public override int AdditionalStarvationLoyaltyEffect => _baseModel.AdditionalStarvationLoyaltyEffect;
        public override int RebelliousStateStartLoyaltyThreshold => _baseModel.RebelliousStateStartLoyaltyThreshold;
        public override int LoyaltyBoostAfterRebellionStartValue => _baseModel.LoyaltyBoostAfterRebellionStartValue;
        public override int SettlementLoyaltyChangeDueToSecurityThreshold => _baseModel.SettlementLoyaltyChangeDueToSecurityThreshold;
        public override int MaximumLoyaltyInSettlement => _baseModel.MaximumLoyaltyInSettlement;
        public override int LoyaltyDriftMedium => _baseModel.LoyaltyDriftMedium;
        public override float ThresholdForNotableRelationBonus => _baseModel.ThresholdForNotableRelationBonus;
        public override int DailyNotableRelationBonus => _baseModel.DailyNotableRelationBonus;

        public override ExplainedNumber CalculateLoyaltyChange(Town town, bool includeDescriptions = false)
        {
            ExplainedNumber result = _baseModel.CalculateLoyaltyChange(town, includeDescriptions);
            if (FeudalTitlePenaltyHelper.IsContestedLegalTitle(town, out FeudalTitleRecord _))
                result.Add(C.FeudalTitleContestedLoyaltyPenalty, FeudalTitlePenaltyHelper.ContestedLegalTitleText);

            return result;
        }

        public override void CalculateGoldGainDueToHighLoyalty(Town town, ref ExplainedNumber explainedNumber)
        {
            _baseModel.CalculateGoldGainDueToHighLoyalty(town, ref explainedNumber);
        }

        public override void CalculateGoldCutDueToLowLoyalty(Town town, ref ExplainedNumber explainedNumber)
        {
            _baseModel.CalculateGoldCutDueToLowLoyalty(town, ref explainedNumber);
        }
    }
}
