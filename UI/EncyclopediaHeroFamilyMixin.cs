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
        [DataSourceProperty] public bool BellumUseFamilyLayout => EncyclopediaFamilyLayoutCompatibility.Enabled;

        public EncyclopediaHeroFamilyMixin(EncyclopediaHeroPageVM vm) : base(vm)
        {
            vm.PropertyChangedWithBoolValue += OnPageChanged;
            EncyclopediaFamilyLayoutCompatibility.Changed += OnLayoutChanged;
            EncyclopediaFamilyLayoutCompatibility.Refresh();
        }

        private void OnPageChanged(object sender, PropertyChangedWithBoolValueEventArgs args)
        {
            // Patching native Refresh at module load initializes CampaignUIHelper before game texts exist.
            if (args.PropertyName == nameof(EncyclopediaHeroPageVM.IsLoadingOver) && ViewModel.IsLoadingOver)
                OnRefresh();
        }

        public override void OnRefresh()
        {
            RefreshFamily();
        }

        private void OnLayoutChanged()
        {
            ViewModel.OnPropertyChanged(nameof(BellumUseFamilyLayout));
            RefreshFamily();
        }

        private void RefreshFamily()
        {
            var page = Campaign.Current?.EncyclopediaManager?.GetPageOf(typeof(Hero));
            BellumFamily.Refresh(ViewModel.Obj as Hero, ViewModel.IsInformationHidden || !BellumUseFamilyLayout,
                page == null ? (System.Func<Hero, bool>)null : hero => page.IsValidEncyclopediaItem(hero));
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithBoolValue -= OnPageChanged;
            EncyclopediaFamilyLayoutCompatibility.Changed -= OnLayoutChanged;
            BellumFamily.OnFinalize();
            base.OnFinalize();
        }
    }
}
