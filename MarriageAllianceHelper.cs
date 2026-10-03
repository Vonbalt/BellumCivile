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

            MarriageAllianceBehavior behavior = MarriageAllianceBehavior.Instance
                ?? Campaign.Current?.GetCampaignBehavior<MarriageAllianceBehavior>();
            if (behavior != null)
                return behavior.AreClansIntermarried(clan, alliedClan);

            return clan.Heroes.Any(h => h?.IsAlive == true
                                     && h.Spouse?.Clan != null
                                     && h.Spouse.Clan == alliedClan);
        }
    }
}
