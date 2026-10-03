using TaleWorlds.CampaignSystem.GameComponents;

namespace BellumCivile
{
    public sealed class BellumAgeModel : DefaultAgeModel
    {
        public override int HeroComesOfAge => BellumCivileOptions.AdulthoodAge;
    }
}
