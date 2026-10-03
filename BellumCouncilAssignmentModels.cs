using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class BellumCouncilAssignmentModelHelper
    {
        public static Kingdom GetKingdom(Settlement settlement)
        {
            return settlement?.OwnerClan?.Kingdom;
        }

        public static float GetMultiplier(
            Settlement settlement,
            PrivyCouncilOffice office,
            string assignmentId,
            bool requireFunding = false)
        {
            Kingdom kingdom = GetKingdom(settlement);
            if (kingdom == null)
                return 0f;

            return CouncilAssignmentRuntimePatches.GetCouncilBehavior()?
                .GetAssignmentEffectMultiplier(kingdom, office, assignmentId, requireFunding) ?? 0f;
        }

        public static float GetIncidentMultiplier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId)
        {
            return kingdom == null
                ? 1f
                : CouncilAssignmentRuntimePatches.GetIncidentBehavior()?
                    .GetAssignmentAspectMultiplier(kingdom, office, assignmentId, aspectId) ?? 1f;
        }
    }

    public sealed class BellumCouncilSettlementPatrolModel : SettlementPatrolModel
    {
        private readonly SettlementPatrolModel _baseModel;

        public BellumCouncilSettlementPatrolModel(SettlementPatrolModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultSettlementPatrolModel();
        }

        public override CampaignTime GetPatrolPartySpawnDuration(Settlement settlement, bool isNaval)
        {
            CampaignTime duration = _baseModel.GetPatrolPartySpawnDuration(settlement, isNaval);
            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                settlement,
                PrivyCouncilOffice.Marshal,
                "marshal_organize_patrols");
            if (efficiency <= 0f)
                return duration;

            Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(settlement);
            float incidentMultiplier = BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_organize_patrols",
                    CouncilIncidentAspects.PatrolFormationSpeed);
            efficiency *= incidentMultiplier;

            return CampaignTime.Hours((float)duration.ToHours * (1f - 0.15f * efficiency));
        }

        public override bool CanSettlementHavePatrolParties(Settlement settlement, bool isNaval)
        {
            return _baseModel.CanSettlementHavePatrolParties(settlement, isNaval);
        }

        public override PartyTemplateObject GetPartyTemplateForPatrolParty(Settlement settlement, bool isNaval)
        {
            return _baseModel.GetPartyTemplateForPatrolParty(settlement, isNaval);
        }
    }

    public sealed class BellumCouncilSettlementMilitiaModel : SettlementMilitiaModel
    {
        private readonly SettlementMilitiaModel _baseModel;

        public BellumCouncilSettlementMilitiaModel(SettlementMilitiaModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultSettlementMilitiaModel();
        }

        public override int MilitiaToSpawnAfterSiege(Town town)
        {
            return _baseModel.MilitiaToSpawnAfterSiege(town);
        }

        public override ExplainedNumber CalculateMilitiaChange(Settlement settlement, bool includeDescriptions = false)
        {
            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                settlement,
                PrivyCouncilOffice.Marshal,
                "marshal_train_militia");

            if (efficiency <= 0f)
                return _baseModel.CalculateMilitiaChange(settlement, includeDescriptions);

            Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(settlement);
            float incidentMultiplier = BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_train_militia",
                    CouncilIncidentAspects.MilitiaGrowth);
            efficiency *= incidentMultiplier;

            // Training reinforces sources of militia growth without amplifying losses such as retirement.
            ExplainedNumber result = _baseModel.CalculateMilitiaChange(settlement, true);
            float positiveGrowth = 0f;
            foreach (var line in result.GetLines())
            {
                if (line.Item2 > 0f)
                    positiveGrowth += line.Item2;
            }

            // Some replacement models may not expose explanation lines even when requested.
            if (positiveGrowth <= 0f && result.ResultNumber > 0f)
                positiveGrowth = result.ResultNumber;

            float trainingBonus = positiveGrowth * 0.10f * efficiency;
            if (trainingBonus > 0f)
            {
                result.Add(
                    trainingBonus,
                    new TextObject("{=BC_Council_AssignmentMilitiaGrowth}Council militia training"));
            }

            return result;
        }

        public override ExplainedNumber CalculateVeteranMilitiaSpawnChance(Settlement settlement)
        {
            ExplainedNumber result = _baseModel.CalculateVeteranMilitiaSpawnChance(settlement);
            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                settlement,
                PrivyCouncilOffice.Marshal,
                "marshal_train_militia");
            if (efficiency > 0f)
            {
                Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(settlement);
                float incidentMultiplier = BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_train_militia",
                        CouncilIncidentAspects.VeteranMilitiaChance);
                efficiency *= incidentMultiplier;
                result.AddFactor(
                    0.10f * efficiency,
                    new TextObject("{=BC_Council_AssignmentMilitiaQuality}Council militia training"));
            }
            return result;
        }

        public override void CalculateMilitiaSpawnRate(Settlement settlement, out float meleeTroopRate, out float rangedTroopRate)
        {
            _baseModel.CalculateMilitiaSpawnRate(settlement, out meleeTroopRate, out rangedTroopRate);
        }
    }

    public sealed class BellumCouncilBuildingConstructionModel : BuildingConstructionModel
    {
        private readonly BuildingConstructionModel _baseModel;

        public BellumCouncilBuildingConstructionModel(BuildingConstructionModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultBuildingConstructionModel();
        }

        public override int TownBoostCost => _baseModel.TownBoostCost;
        public override int TownBoostBonus => _baseModel.TownBoostBonus;
        public override int CastleBoostCost => _baseModel.CastleBoostCost;
        public override int CastleBoostBonus => _baseModel.CastleBoostBonus;

        public override ExplainedNumber CalculateDailyConstructionPower(Town town, bool includeDescriptions = false)
        {
            ExplainedNumber result = _baseModel.CalculateDailyConstructionPower(town, includeDescriptions);
            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                town?.Settlement,
                PrivyCouncilOffice.Seneschal,
                "seneschal_subsidize_infrastructure",
                requireFunding: true);
            if (efficiency > 0f)
            {
                Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(town?.Settlement);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_subsidize_infrastructure",
                    CouncilIncidentAspects.InfrastructureConstructionStrength);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_subsidize_infrastructure",
                    CouncilIncidentAspects.InfrastructureEfficiency);
                result.AddFactor(
                    0.10f * efficiency,
                    new TextObject("{=BC_Council_AssignmentConstruction}Council infrastructure subsidies"));
            }
            return result;
        }

        public override int CalculateDailyConstructionPowerWithoutBoost(Town town)
        {
            int result = _baseModel.CalculateDailyConstructionPowerWithoutBoost(town);
            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                town?.Settlement,
                PrivyCouncilOffice.Seneschal,
                "seneschal_subsidize_infrastructure",
                requireFunding: true);
            if (efficiency > 0f)
            {
                Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(town?.Settlement);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_subsidize_infrastructure",
                    CouncilIncidentAspects.InfrastructureConstructionStrength);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_subsidize_infrastructure",
                    CouncilIncidentAspects.InfrastructureEfficiency);
            }
            return result + (int)System.Math.Round(result * 0.10f * efficiency);
        }

        public override int GetBoostCost(Town town)
        {
            return _baseModel.GetBoostCost(town);
        }

        public override int GetBoostAmount(Town town)
        {
            return _baseModel.GetBoostAmount(town);
        }
    }

    public sealed class BellumCouncilSettlementFoodModel : SettlementFoodModel
    {
        private readonly SettlementFoodModel _baseModel;

        public BellumCouncilSettlementFoodModel(SettlementFoodModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultSettlementFoodModel();
        }

        public override int FoodStocksUpperLimit => _baseModel.FoodStocksUpperLimit;
        public override int NumberOfProsperityToEatOneFood => _baseModel.NumberOfProsperityToEatOneFood;
        public override int NumberOfMenOnGarrisonToEatOneFood => _baseModel.NumberOfMenOnGarrisonToEatOneFood;
        public override int CastleFoodStockUpperLimitBonus => _baseModel.CastleFoodStockUpperLimitBonus;

        public override ExplainedNumber CalculateTownFoodStocksChange(
            Town town,
            bool includeMarketStocks = true,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = _baseModel.CalculateTownFoodStocksChange(
                town,
                includeMarketStocks,
                includeDescriptions);
            if (CouncilAssignmentHapIntegration.IsExternalRequisitionApiActive)
                return result;

            float efficiency = BellumCouncilAssignmentModelHelper.GetMultiplier(
                town?.Settlement,
                PrivyCouncilOffice.Seneschal,
                "seneschal_stockpile_provisions",
                requireFunding: true);
            if (efficiency > 0f)
            {
                Kingdom kingdom = BellumCouncilAssignmentModelHelper.GetKingdom(town?.Settlement);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_stockpile_provisions",
                    CouncilIncidentAspects.ProvisionsFoodYield);
                efficiency *= BellumCouncilAssignmentModelHelper.GetIncidentMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_stockpile_provisions",
                    CouncilIncidentAspects.ProvisionsDistributionEfficiency);
                result.Add(
                    2f * efficiency,
                    new TextObject("{=BC_Council_AssignmentFoodStocks}Council provision stockpiles"));
            }
            return result;
        }
    }

}
