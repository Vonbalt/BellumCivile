using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    /// <summary>
    /// Uses consistent native household placement, with an explicit treaty exception.
    /// </summary>
    public class BellumMarriageModel : DefaultMarriageModel
    {
        public override int MinimumMarriageAgeMale => BellumCivileOptions.AdulthoodAge;

        public override int MinimumMarriageAgeFemale => BellumCivileOptions.AdulthoodAge;

        public override bool IsSuitableForMarriage(Hero hero)
        {
            if (CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(hero) == true) return false;
            if (!MarriageProspectEvaluation.Active || BellumMarriageStrategyHelper.MarriageParticipantReady(hero))
                return base.IsSuitableForMarriage(hero);
            if (hero?.IsAlive != true || !(hero.IsActive || hero.IsFugitive || hero.IsPrisoner)
                || hero.Spouse != null || !hero.IsLord || hero.IsMinorFactionHero || hero.IsNotable || hero.IsTemplate)
                return false;
            var offers = Campaign.Current.GetCampaignBehavior<IMarriageOfferCampaignBehavior>();
            return offers?.IsHeroEngaged(hero) != true
                && hero.Age >= (hero.IsFemale ? MinimumMarriageAgeFemale : MinimumMarriageAgeMale);
        }

        public override bool IsCoupleSuitableForMarriage(Hero firstHero, Hero secondHero)
        {
            if (CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(firstHero) == true
                || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(secondHero) == true
                || RegencyBehavior.Instance?.IsGeneratedRegent(firstHero) == true
                || RegencyBehavior.Instance?.IsGeneratedRegent(secondHero) == true)
            {
                return false;
            }

            return base.IsCoupleSuitableForMarriage(firstHero, secondHero);
        }

        public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero)
        {
            if (TreatyMarriageClanContext.TryResolve(firstHero, secondHero, out Clan treatyClan))
                return treatyClan;

            return base.GetClanAfterMarriage(firstHero, secondHero);
        }
    }
}
