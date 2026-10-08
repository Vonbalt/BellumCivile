using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    internal static partial class BellumMarriageStrategyHelper
    {
        internal static float PlayerMarriageHouseRisk(Clan clan) => AssessHouseHealth(clan).Risk;

        private sealed class HouseHealth
        {
            public int Adults, Children;
            public float Risk;
            public bool HasFertileCouple;
            public readonly HashSet<Hero> Prospects = new HashSet<Hero>();
            public HashSet<Hero> BloodProspects;
        }

        private static HouseHealth AssessHouseHealth(Clan clan)
        {
            var health = new HouseHealth();
            float coverage = 0;
            foreach (Hero hero in clan.Heroes)
            {
                // Captivity and campaign activity do not remove a living family member.
                if (hero == null || !hero.IsAlive || !hero.IsLord) continue;
                if (hero.IsChild) { health.Children++; continue; }
                health.Adults++;
                if (HouseholdMarriageProspect(hero)) health.Prospects.Add(hero);
                if (hero.IsFemale && hero.Age <= 45 && hero.Spouse?.IsAlive == true && hero.Spouse.Clan == clan)
                {
                    health.HasFertileCouple = true;
                    coverage += MarriageHealthRules.ReproductiveCoverage(hero.Age);
                }
            }
            health.Risk = MarriageHealthRules.Risk(health.Adults, health.Children, health.Prospects.Count, coverage);
            return health;
        }
    }
}
