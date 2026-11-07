using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class MarriageParticipantPolicy
    {
        internal static bool IsCompanion(Hero hero) => hero?.IsWanderer == true && hero.Clan != null
            && hero.CompanionOf == hero.Clan && HouseholdMembership.Supports(hero.Clan);

        internal static bool IsParticipant(Hero hero) => hero != null && (hero.IsLord || IsCompanion(hero));

        internal static float RetainerCost(Hero member, Hero spouse) =>
            Behaviors.MarriageHealthRules.MixedStatusCost(HouseholdMembership.IsFamily(member), HouseholdMembership.IsFamily(spouse), 0);

        internal static bool IsDynasticPair(Hero first, Hero second) => first?.Clan != null && second?.Clan != null
            && first.Clan != second.Clan && HouseholdMembership.IsFamily(first) && HouseholdMembership.IsFamily(second);
    }
}
