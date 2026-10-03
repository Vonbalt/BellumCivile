using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile.Behaviors
{
    internal static class DeliberationDialogueHelper
    {
        internal const int PageSize = 4;

        internal static bool IsConversationInArmy(Hero target)
        {
            return target?.PartyBelongedTo?.Army != null
                || MobileParty.MainParty?.Army != null;
        }
    }
}
