using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    /// <summary>
    /// Keeps noble-clan eligibility consistent across succession, civil-war settlement,
    /// and emergency ruler repair. Bannerlord may flag the player clan as a minor faction,
    /// but that must not disqualify a landed player vassal or sovereign.
    /// </summary>
    public static class NobleClanEligibilityHelper
    {
        public static bool IsNonPlayerMinorClan(Clan clan)
        {
            return clan != null && clan.IsMinorFaction && clan != Clan.PlayerClan;
        }

        public static bool IsLiveNobleClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && !IsNonPlayerMinorClan(clan)
                && clan.Leader != null
                && !clan.Leader.IsDead;
        }

        public static bool IsValidRulingClan(Clan clan, Kingdom kingdom)
        {
            return kingdom != null
                && IsLiveNobleClan(clan)
                && clan.Kingdom == kingdom;
        }
    }
}
