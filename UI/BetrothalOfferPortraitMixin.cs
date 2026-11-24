using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Patches;
using JetBrains.Annotations;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.Library;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues")]
    [UsedImplicitly]
    internal sealed class BetrothalOfferPortraitMixin : BaseViewModelMixin<MarriageOfferPopupHeroVM>
    {
        public BetrothalOfferPortraitMixin(MarriageOfferPopupHeroVM vm) : base(vm) { }

        [DataSourceProperty]
        public bool BellumBetrothalChildPortrait => BetrothalOfferPopupPatch.ShowsChildPortrait(ViewModel);
    }
}
