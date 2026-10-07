using System.Linq;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class ExpulsionRelationHelper
    {
        internal static void ApplyFriendMemories(Kingdom kingdom, Clan rulingClan, Hero ruler, Clan expelledClan)
        {
            Hero expelledLeader = expelledClan?.Leader;
            if (kingdom == null || rulingClan == null || ruler == null || ruler.IsDead
                || expelledLeader == null || expelledLeader.IsDead)
                return;

            foreach (Clan otherClan in kingdom.Clans.ToList())
            {
                Hero friend = otherClan?.Leader;
                if (otherClan == rulingClan || otherClan == expelledClan || friend == null || friend.IsDead)
                    continue;

                if (friend.GetRelation(expelledLeader) > C.ExpelResolveFriendRelThreshold)
                    RelationMemoryService.ApplyChange(friend, ruler, C.ExpelResolveFriendDragged, true,
                        RelationMemorySources.ExpelledMyFriend, 10f, RelationMemoryScope.Personal, expelledLeader.Name?.ToString());
            }
        }
    }
}
