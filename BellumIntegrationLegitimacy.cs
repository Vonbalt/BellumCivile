using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public static partial class BellumIntegration
    {
        public static bool IsIllegitimate(Hero hero)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return BellumIntegrationBehavior.IsBarred(hero);
        }

        /// <summary>Excludes future hereditary inheritance; does not change parentage or settled property.</summary>
        public static bool MarkIllegitimate(Hero hero)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return BellumIntegrationBehavior.Current.SetIllegitimate(hero, true);
        }

        public static bool Legitimize(Hero hero)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return BellumIntegrationBehavior.Current.SetIllegitimate(hero, false);
        }
    }
}
