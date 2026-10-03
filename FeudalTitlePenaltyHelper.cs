using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class FeudalTitlePenaltyHelper
    {
        public static TextObject ContestedLegalTitleText => new TextObject("{=BC_TitlePenalty_ContestedLegalTitle}Contested legal title");

        public static bool IsContestedLegalTitle(Town town, out FeudalTitleRecord title)
        {
            return IsContestedLegalTitle(town?.Settlement, out title);
        }

        public static bool IsBoundToContestedLegalTitle(Village village, out FeudalTitleRecord title)
        {
            return IsContestedLegalTitle(village?.Bound, out title);
        }

        public static bool IsContestedLegalTitle(Settlement settlement, out FeudalTitleRecord title)
        {
            title = null;

            if (settlement == null)
                return false;

            Clan ownerClan = settlement.OwnerClan;
            if (ownerClan == null || string.IsNullOrWhiteSpace(ownerClan.StringId))
                return false;

            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null || !behavior.TryGetBarony(settlement, out title))
                return false;

            if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.DeJureHolderClanId))
                return false;

            return title.DeJureHolderClanId != ownerClan.StringId;
        }
    }
}
