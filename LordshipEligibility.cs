using System;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class LordshipEligibility
    {
        [ThreadStatic] internal static Clan SelectingClan;
        [ThreadStatic] internal static bool ListingCandidates;
        [ThreadStatic] internal static bool CheckingClanCommand;

        internal static bool Required(Hero hero) => LordshipBehavior.Instance?.Ready == true && HouseholdMembership.IsRetainer(hero);

        internal static bool Permitted(Hero hero, bool governor = false, Town destination = null)
        {
            var registry = LordshipBehavior.Instance;
            if (!Required(hero) || registry.GetGrant(hero) != null) return true;
            if (governor && registry.HasLegacyAppointment(hero, town: destination ?? hero.GovernorOf)) return true;
            if (!governor && hero.PartyBelongedTo?.LeaderHero == hero && registry.HasLegacyAppointment(hero, party: hero.PartyBelongedTo)) return true;
            if (SelectingClan != hero.Clan || SelectingClan == Clan.PlayerClan) return false;
            var village = registry.FindVacancy(hero.Clan);
            return village != null && registry.CanGrant(hero.Clan, village, hero, out _);
        }

        internal static TextObject Reason(bool governor) => governor
            ? LordshipBehavior.Text("BC_Lordship_GovernorRequired", "This retainer must receive a village endowment before serving as governor.")
            : LordshipBehavior.Text("BC_Lordship_CommandRequired", "This retainer must receive a village endowment before leading a clan party.");
    }
}
