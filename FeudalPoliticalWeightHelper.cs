using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class FeudalPoliticalWeightHelper
    {
        public static FeudalTitleType? GetHighestHeldTitleRank(Clan clan, bool deFacto = true)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || clan == null)
                return clan?.Fiefs?.Count > 0 ? FeudalTitleType.Barony : (FeudalTitleType?)null;

            FeudalTitleRecord highest = titleBehavior.GetTitlesHeldByClan(clan, deJure: !deFacto)
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .FirstOrDefault();

            if (highest != null)
                return highest.TitleType;

            return clan.Fiefs.Count > 0 ? FeudalTitleType.Barony : (FeudalTitleType?)null;
        }

        public static float GetAristocraticFiefVoteScore(FeudalTitleType? titleType)
        {
            if (!titleType.HasValue)
                return -C.FiefAristLandlessPenalty;

            switch (titleType.Value)
            {
                case FeudalTitleType.Barony:
                    return C.FiefAristBaronyBonus;
                case FeudalTitleType.County:
                    return C.FiefAristCountyBonus;
                case FeudalTitleType.Duchy:
                    return C.FiefAristDuchyBonus;
                case FeudalTitleType.Kingdom:
                    return C.FiefAristKingdomBonus;
                case FeudalTitleType.Empire:
                    return C.FiefAristEmpireBonus;
                default:
                    return 0f;
            }
        }

        public static float GetPopulistFiefVoteScore(FeudalTitleType? titleType)
        {
            if (!titleType.HasValue)
                return 0f;

            switch (titleType.Value)
            {
                case FeudalTitleType.Barony:
                    return C.FiefPopBaronyOnlyBonus;
                case FeudalTitleType.Duchy:
                case FeudalTitleType.Kingdom:
                case FeudalTitleType.Empire:
                    return -C.FiefPopHighTitlePenalty;
                default:
                    return 0f;
            }
        }

        public static bool IsHighLandedRank(FeudalTitleType? titleType)
        {
            return titleType.HasValue && titleType.Value >= FeudalTitleType.Duchy;
        }

    }
}
