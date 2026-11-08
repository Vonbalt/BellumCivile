using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    internal static partial class BellumMarriageStrategyHelper
    {
        internal static float PlayerMarriageHouseRisk(Clan clan) => AssessHouseHealth(clan).Risk;

        internal static float PlayerMarriageStaffRisk(Hero member, Hero spouse, Clan destination)
        {
            var health = AssessHouseHealth(member.Clan);
            return destination != member.Clan ? health.StaffDepartureCost(member) / BellumCivileConstants.MarriageHouseholdStaffLoss
                : health.StaffIncomingValue(member, spouse) / BellumCivileConstants.MarriageHouseholdStaffGain;
        }

        private sealed class HouseHealth
        {
            public int Adults, Children, BloodAdults, ServiceAdults, ServiceChildren, ServiceDemand;
            public int FreeAdults, Endowed, Unendowed, Vacancies;
            public float Risk;
            public float ManpowerRisk, HouseholdNeed;
            public bool EndowmentsRequired;
            public bool HasFertileCouple;
            public readonly HashSet<Hero> Prospects = new HashSet<Hero>();
            public HashSet<Hero> BloodProspects;
            public readonly HashSet<Hero> ServiceMembers = new HashSet<Hero>();
            public readonly HashSet<Hero> EndowedMembers = new HashSet<Hero>();
            public readonly HashSet<Hero> UnendowedMembers = new HashSet<Hero>();
            public readonly HashSet<Hero> ReleasingEndowments = new HashSet<Hero>();

            public float StaffDepartureCost(Hero member)
            {
                if (!ServiceMembers.Contains(member)) return 0;
                int after = MarriageHealthRules.ServiceCapacity(FreeAdults - (EndowedMembers.Contains(member) || UnendowedMembers.Contains(member) ? 0 : 1),
                    Endowed - (EndowedMembers.Contains(member) ? 1 : 0), Unendowed - (UnendowedMembers.Contains(member) ? 1 : 0),
                    Vacancies + (ReleasingEndowments.Contains(member) ? 1 : 0));
                return MarriageHealthRules.StaffDepartureCost(ServiceAdults, after, ServiceDemand);
            }

            public float StaffIncomingValue(Hero member, Hero spouse)
            {
                if (ManpowerRisk <= 0) return 0;
                // A widow's next spouse is not automatically kin to the household's ruling bloodline.
                Hero head = MarriageHouseholdPolicy.LegalHead(member.Clan);
                bool room = !EndowmentsRequired || HouseholdMembership.IsBloodRelative(member, head)
                    || HouseholdMembership.IsBloodRelative(spouse, head) || Vacancies > Unendowed;
                return room && spouse?.IsAlive == true && !spouse.IsDisabled && !spouse.IsChild
                    ? BellumCivileConstants.MarriageHouseholdStaffGain * ManpowerRisk : 0;
            }
        }

        private static HouseHealth AssessHouseHealth(Clan clan)
        {
            var health = new HouseHealth();
            if (clan == null) return health;
            Hero head = MarriageHouseholdPolicy.LegalHead(clan);
            var registry = LordshipBehavior.Instance;
            health.EndowmentsRequired = registry?.Ready == true;
            health.Vacancies = health.EndowmentsRequired ? registry.CountVacancies(clan) : 0;
            float coverage = 0, serviceCoverage = 0;
            foreach (Hero hero in HouseholdMembership.Members(clan))
            {
                // Captivity and campaign activity do not remove a living family member.
                if (!hero.IsAlive || !MarriageParticipantPolicy.IsParticipant(hero)) continue;
                bool family = HouseholdMembership.IsFamily(hero);
                bool blood = HouseholdMembership.IsBloodRelative(hero, head);
                if (hero.IsChild)
                {
                    if (blood) health.Children++;
                    health.ServiceChildren++;
                    continue;
                }
                if (family) { health.Adults++; if (HouseholdMarriageProspect(hero)) health.Prospects.Add(hero); }
                if (blood) health.BloodAdults++;
                if (!hero.IsDisabled)
                {
                    health.ServiceMembers.Add(hero);
                    if (!health.EndowmentsRequired || !HouseholdMembership.IsRetainer(hero)) health.FreeAdults++;
                    else if (registry.GetGrant(hero) is PersonalLordshipRecord grant)
                    {
                        health.Endowed++; health.EndowedMembers.Add(hero);
                        if (grant.GrantingClan == clan && !grant.IsVassalGrant) health.ReleasingEndowments.Add(hero);
                    }
                    else if (registry.GetIndenture(hero) != null)
                    {
                        health.Endowed++; health.EndowedMembers.Add(hero);
                    }
                    else { health.Unendowed++; health.UnendowedMembers.Add(hero); }
                }
                if (!hero.IsFemale || hero.Age < SuccessionLawHelper.GetAgeOfMajority() || hero.Age > 45
                    || hero.Spouse?.IsAlive != true || hero.Spouse.Clan != clan) continue;
                float reproductive = MarriageHealthRules.ReproductiveCoverage(hero.Age);
                serviceCoverage += reproductive;
                if (!blood && !HouseholdMembership.IsBloodRelative(hero.Spouse, head)) continue;
                health.HasFertileCouple = true;
                coverage += reproductive;
            }
            health.BloodProspects = new HashSet<Hero>();
            foreach (Hero hero in health.Prospects)
                if (HouseholdMembership.IsBloodRelative(hero, head)) health.BloodProspects.Add(hero);
            health.Risk = MarriageHealthRules.Risk(health.BloodAdults, health.Children, health.BloodProspects.Count, coverage);
            int fiefs = clan.Fiefs?.Count ?? 0;
            int limit = Math.Max(1, Campaign.Current?.Models?.ClanTierModel?.GetPartyLimitForTier(clan, clan.Tier) ?? 1);
            // Land supports planned parties; the native cap is a ceiling, not a payroll target.
            int parties = Math.Min(limit, Math.Max(clan.WarPartyComponents?.Count ?? 0, Math.Max(1, fiefs)));
            health.ServiceDemand = parties + fiefs + 1;
            health.ServiceAdults = MarriageHealthRules.ServiceCapacity(health.FreeAdults, health.Endowed, health.Unendowed, health.Vacancies);
            health.ManpowerRisk = MarriageHealthRules.StaffShortage(health.ServiceAdults, health.ServiceDemand);
            health.HouseholdNeed = MarriageHealthRules.HouseholdNeed(health.ManpowerRisk, health.ServiceChildren, serviceCoverage,
                !health.EndowmentsRequired || health.Vacancies > health.Unendowed);
            return health;
        }
    }
}
