using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
namespace BellumCivile
{
    internal static class CourtMembershipEligibility
    {
        internal static bool IsRuler(Clan clan) => clan != null && clan.Kingdom != null && clan.Kingdom.RulingClan == clan
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom);
        internal static bool CanBelong(Clan clan, Kingdom realm) => clan != null && realm != null
            && clan.Kingdom == realm && !clan.IsEliminated && (!clan.IsMinorFaction || clan == Clan.PlayerClan)
            && !clan.IsUnderMercenaryService && clan != realm.RulingClan;
        internal static TextObject RulerReason => new TextObject("{=BC_CrownMembershipBlocked}As ruler, your house represents the Crown and cannot belong to a court faction.");
        internal static TextObject IneligibleReason => new TextObject("{=BC_CourtMembershipIneligible}Only eligible vassal houses sworn to this realm can join its court factions.");
    }
}
