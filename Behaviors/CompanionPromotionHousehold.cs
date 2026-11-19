using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile.Behaviors
{
    internal static class CompanionPromotionHousehold
    {
        internal static List<Hero> GetMovers(Clan source, Clan destination)
        {
            Hero founder = destination?.Leader;
            if (source == null || source == destination || source.IsEliminated || source.Kingdom == null
                || destination == null || destination.IsEliminated || destination.Kingdom != source.Kingdom
                || founder?.IsAlive != true || founder.Clan != destination)
                return new List<Hero>();

            var candidates = HouseholdMembership.Members(source).Where(hero => hero.Clan == source
                && hero != Hero.MainHero && hero != source.Leader && hero.IsAlive && !hero.IsDisabled
                && !hero.IsNotable && !HouseholdMembership.IsFamily(hero)).ToList();
            var bloodFamily = new HashSet<Hero>(candidates.Where(hero => HouseholdMembership.IsBloodRelative(hero, founder)));
            // Relatives already in the new house can still have a spouse waiting to move.
            bloodFamily.UnionWith(HouseholdMembership.Members(destination).Where(hero => hero.Clan == destination
                && hero.IsAlive && HouseholdMembership.IsBloodRelative(hero, founder)));
            bloodFamily.Add(founder);
            return candidates.Where(hero => bloodFamily.Contains(hero) || bloodFamily.Contains(hero.Spouse)
                || bloodFamily.Any(relative => relative.Spouse == hero)).ToList();
        }

        internal static bool TryMove(Hero hero, Clan source, Clan destination)
        {
            if (source == null || hero?.Clan != source || destination == null) return true;
            MobileParty party = hero.PartyBelongedTo;
            bool staysWithParty = party != null && !party.IsMainParty
                && (party.ActualClan == destination || party.LeaderHero == destination.Leader);
            bool ledParty = party?.LeaderHero == hero;
            if (hero.GovernorOf?.Settlement?.IsUnderSiege == true
                || (party != null && !staysWithParty && (party.MapEvent != null || party.SiegeEvent != null
                    || party.IsCurrentlyAtSea || party.IsTransitionInProgress || party.CurrentSettlement?.IsUnderSiege == true
                    || (ledParty && (party.IsMainParty || (!party.IsLordParty && !party.IsCaravan)))))) return false;

            if (hero.GovernorOf != null)
                ChangeGovernorAction.RemoveGovernorOf(hero);

            if (party != null && !staysWithParty)
            {
                if (ledParty)
                {
                    // Release command before making the hero a fugitive, so native code keeps the troops.
                    party.RemovePartyLeader();
                }
                if (hero.PartyBelongedTo == party) MakeHeroFugitiveAction.Apply(hero);
                if (ledParty && party.IsActive) DisbandPartyAction.StartDisband(party);
            }

            if (hero.Clan != source) return true;
            if (hero.CompanionOf == source)
            {
                hero.CompanionOf = destination;
                CampaignEventDispatcher.Instance.OnHeroChangedClan(hero, source);
            }
            else
                hero.Clan = destination;

            BellumCivileLogger.Log($"Companion promotion family moved; hero={hero.StringId}; old_clan={source.StringId}; new_clan={destination.StringId}.");
            return true;
        }
    }
}
