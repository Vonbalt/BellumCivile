namespace BellumCivile
{
    internal static class TreatyPoliticalCostModel
    {
        public static int GetClaimRenunciationCost(FeudalTitleRecord title, FeudalClaimStrength strength)
        {
            if (title == null)
                return 0;

            int weakCost;
            switch (title.TitleType)
            {
                case FeudalTitleType.County: weakCost = 8; break;
                case FeudalTitleType.Duchy: weakCost = 10; break;
                case FeudalTitleType.Kingdom: weakCost = 15; break;
                case FeudalTitleType.Empire: weakCost = 20; break;
                default: weakCost = 5; break;
            }

            return strength == FeudalClaimStrength.Strong ? weakCost * 2 : weakCost;
        }
    }
}
