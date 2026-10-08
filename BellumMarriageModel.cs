using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    /// <summary>
    /// Uses the agreed household during weddings, with an explicit treaty exception.
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

            var agreement = PlayerMarriageAgreementBehavior.Instance?.Find(firstHero, secondHero);
            using (agreement == null && PlayerMarriageValidationScope.Current == null
                ? null : new PlayerMarriageValidationScope(agreement))
                if (!base.IsCoupleSuitableForMarriage(firstHero, secondHero)) return false;
            var native = NativeNpcMarriageHouseholdScope.Current;
            return native == null || TreatyMarriageClanContext.TryResolve(firstHero, secondHero, out _)
                || native.Policy.TryChoose(firstHero, secondHero, base.GetClanAfterMarriage(firstHero, secondHero), out _);
        }

        public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero)
        {
            if (TreatyMarriageClanContext.TryResolve(firstHero, secondHero, out Clan treatyClan))
                return treatyClan;
            if (NpcMarriageClanContext.TryResolve(firstHero, secondHero, out Clan agreedClan)) return agreedClan;
            var agreement = PlayerMarriageAgreementBehavior.Instance?.Find(firstHero, secondHero);
            if (agreement != null) return agreement.Destination;
            Clan ordinary = base.GetClanAfterMarriage(firstHero, secondHero);
            var native = NativeNpcMarriageHouseholdScope.Current;
            return native != null && native.Policy.TryChoose(firstHero, secondHero, ordinary, out Clan chosen)
                ? chosen : ordinary;
        }
    }
}
