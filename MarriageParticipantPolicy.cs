using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class MarriageParticipantPolicy
    {
        internal static bool IsCompanion(Hero hero) => hero?.IsWanderer == true && hero.Clan != null
            && hero.CompanionOf == hero.Clan && HouseholdMembership.Supports(hero.Clan);

        internal static bool IsParticipant(Hero hero) => hero != null && (hero.IsLord || IsCompanion(hero));

        internal static float RetainerCost(Hero member, Hero spouse) =>
            (HouseholdMembership.IsFamily(member) ? 0 : BellumCivileConstants.MarriageScoreRetainerPenalty)
            + (HouseholdMembership.IsFamily(spouse) ? 0 : BellumCivileConstants.MarriageScoreRetainerPenalty);
    }
}
