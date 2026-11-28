using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class MarriageAllianceHelper
    {
        public static bool HasMarriageAlliance(Clan clan, Clan alliedClan)
        {
            if (clan == null || alliedClan == null || clan == alliedClan)
                return false;

            MarriageAllianceBehavior behavior = GetBehavior();
            if (behavior != null)
                return behavior.AreClansIntermarried(clan, alliedClan);

            return FindNativeSpouse(clan, alliedClan) != null;
        }

        internal static (Hero First, Hero Second) GetMarriageAllianceSpouses(Clan clan, Clan alliedClan)
        {
            if (clan == null || alliedClan == null || clan == alliedClan) return (null, null);
            var behavior = GetBehavior();
            if (behavior != null) return behavior.GetMarriageAllianceSpouses(clan, alliedClan);
            var hero = FindNativeSpouse(clan, alliedClan);
            return (hero, hero?.Spouse);
        }

        private static MarriageAllianceBehavior GetBehavior() => MarriageAllianceBehavior.Instance
            ?? Campaign.Current?.GetCampaignBehavior<MarriageAllianceBehavior>();

        private static Hero FindNativeSpouse(Clan clan, Clan alliedClan) =>
            clan.Heroes.FirstOrDefault(h => h?.IsAlive == true
                                     && h.Spouse?.Clan != null
                                     && h.Spouse.Clan == alliedClan && MarriageParticipantPolicy.IsDynasticPair(h, h.Spouse));
    }
}
