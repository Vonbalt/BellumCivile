using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public class BellumClanPoliticsModel : ClanPoliticsModel
    {
        private readonly ClanPoliticsModel _baseModel;

        public BellumClanPoliticsModel(ClanPoliticsModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultClanPoliticsModel();
        }

        public override ExplainedNumber CalculateInfluenceChange(Clan clan, bool includeDescriptions = false)
        {
            ExplainedNumber result = _baseModel.CalculateInfluenceChange(clan, includeDescriptions);
            Behaviors.FeudalTitleBehavior titleBehavior = Behaviors.FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<Behaviors.FeudalTitleBehavior>();
            float feudalAuthority = titleBehavior?.CalculateDeFactoAuthorityInfluence(clan) ?? 0f;
            if (feudalAuthority > 0f)
                result.Add(feudalAuthority, new TextObject("{=BC_Influence_FeudalAuthority}Feudal Authority"));

            Behaviors.PrivyCouncilBehavior councilBehavior = Campaign.Current?.GetCampaignBehavior<Behaviors.PrivyCouncilBehavior>();
            float councilInfluence = councilBehavior?.GetCouncilInfluenceGain(clan) ?? 0f;
            if (councilInfluence > 0f)
            {
                TextObject councilOffice = new TextObject("{=BC_Influence_PrivyCouncil}{COUNCIL_NAME} Office");
                result.Add(
                    councilInfluence,
                    CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(councilOffice, clan?.Kingdom));
            }

            Behaviors.RealmPeaceEnforcementBehavior peaceBehavior = Campaign.Current?.GetCampaignBehavior<Behaviors.RealmPeaceEnforcementBehavior>();
            if (clan == null || peaceBehavior?.HasTyrantsDebt(clan) != true || result.ResultNumber <= 0f)
                return result;

            float penalty = result.ResultNumber * (C.RoyalPeaceInfluenceGainMultiplier - 1f);
            result.Add(penalty, new TextObject("{=BC_RoyalPeace_TyrantsDebt}Tyrant's Debt"));
            return result;
        }

        public override float CalculateSupportForPolicyInClan(Clan clan, PolicyObject policy)
        {
            return _baseModel.CalculateSupportForPolicyInClan(clan, policy);
        }

        public override float CalculateRelationshipChangeWithSponsor(Clan sponsorClan, Clan rewardedClan)
        {
            return _baseModel.CalculateRelationshipChangeWithSponsor(sponsorClan, rewardedClan);
        }

        public override int GetInfluenceRequiredToOverrideKingdomDecision(DecisionOutcome popularOption, DecisionOutcome overridingOption, KingdomDecision decision)
        {
            if (Behaviors.WarPeaceRevampBehavior.IsRevampEnabled()
                && decision is DeclareWarDecision
                && popularOption != null
                && overridingOption != null
                && popularOption != overridingOption)
            {
                PoliticalInfluenceVoteTally tally = WarDeclarationCouncilService.CalculateFromOutcomes(
                    decision,
                    new[] { popularOption, overridingOption });
                return WarDeclarationCouncilService.IsWarOutcome(overridingOption)
                    ? tally.RatificationOverrideCost
                    : tally.RejectionOverrideCost;
            }

            return _baseModel.GetInfluenceRequiredToOverrideKingdomDecision(popularOption, overridingOption, decision);
        }

        public override bool CanHeroBeGovernor(Hero hero)
        {
            return _baseModel.CanHeroBeGovernor(hero);
        }
    }
}
