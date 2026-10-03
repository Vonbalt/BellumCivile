using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class FeudalDeJureDriftEligibility
    {
        public FeudalDeJureDriftEligibility(
            FeudalTitleRecord title,
            FeudalTitleRecord originalParent,
            FeudalTitleRecord targetParent,
            Kingdom targetKingdom,
            Hero integrator)
        {
            Title = title;
            OriginalParent = originalParent;
            TargetParent = targetParent;
            TargetKingdom = targetKingdom;
            Integrator = integrator;
        }

        public FeudalTitleRecord Title { get; }
        public FeudalTitleRecord OriginalParent { get; }
        public FeudalTitleRecord TargetParent { get; }
        public Kingdom TargetKingdom { get; }
        public Hero Integrator { get; }
    }
}
