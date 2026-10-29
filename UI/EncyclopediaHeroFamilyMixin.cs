using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Library;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class EncyclopediaHeroFamilyMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
    {
        [DataSourceProperty] public EncyclopediaHeroFamilyVM BellumFamily { get; } = new EncyclopediaHeroFamilyVM();

        public EncyclopediaHeroFamilyMixin(EncyclopediaHeroPageVM vm) : base(vm)
        {
            vm.PropertyChangedWithBoolValue += OnPageChanged;
        }

        private void OnPageChanged(object sender, PropertyChangedWithBoolValueEventArgs args)
        {
            // Patching native Refresh at module load initializes CampaignUIHelper before game texts exist.
            if (args.PropertyName == nameof(EncyclopediaHeroPageVM.IsLoadingOver) && ViewModel.IsLoadingOver)
                OnRefresh();
        }

        public override void OnRefresh()
        {
            var page = Campaign.Current?.EncyclopediaManager?.GetPageOf(typeof(Hero));
            BellumFamily.Refresh(ViewModel.Obj as Hero, ViewModel.IsInformationHidden,
                page == null ? (System.Func<Hero, bool>)null : hero => page.IsValidEncyclopediaItem(hero));
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithBoolValue -= OnPageChanged;
            BellumFamily.OnFinalize();
            base.OnFinalize();
        }
    }
}
