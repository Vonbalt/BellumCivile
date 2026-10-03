using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.UI.Diplomacy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class KingdomDiplomacyOverviewMixin : BaseViewModelMixin<KingdomDiplomacyVM>
    {
        private bool _overviewSelected = true;
        [DataSourceProperty] public BellumDiplomacyOverviewVM BellumOverview { get; }
            = new BellumDiplomacyOverviewVM();
        [DataSourceProperty] public bool BellumOverviewSelected => _overviewSelected;
        [DataSourceProperty] public bool BellumStatsSelected => !_overviewSelected;
        [DataSourceProperty] public bool BellumStatsControlsVisible => !_overviewSelected && ViewModel.IsWar;
        [DataSourceProperty] public string BellumOverviewText => new TextObject("{=BC_Overview_Title}Overview").ToString();
        [DataSourceProperty] public string BellumStatsText => new TextObject("{=BC_Overview_Stats}Stats").ToString();

        public KingdomDiplomacyOverviewMixin(KingdomDiplomacyVM vm) : base(vm)
        {
            vm.PropertyChangedWithValue += OnSelectionChanged;
            OnRefresh();
        }

        private void OnSelectionChanged(object sender, PropertyChangedWithValueEventArgs args)
        {
            if (args.PropertyName == nameof(KingdomDiplomacyVM.CurrentSelectedDiplomacyItem)) OnRefresh();
        }

        public override void OnRefresh()
        {
            var item = ViewModel.CurrentSelectedDiplomacyItem;
            BellumOverview.Refresh(item?.Faction1 as Kingdom, item?.Faction2 as Kingdom);
            NotifySelection();
        }

        [DataSourceMethod]
        public void ExecuteBellumOverview()
        {
            _overviewSelected = true;
            OnRefresh();
        }

        [DataSourceMethod]
        public void ExecuteBellumStats()
        {
            _overviewSelected = false;
            NotifySelection();
        }

        private void NotifySelection()
        {
            ViewModel.OnPropertyChangedWithValue(BellumOverviewSelected, nameof(BellumOverviewSelected));
            ViewModel.OnPropertyChangedWithValue(BellumStatsSelected, nameof(BellumStatsSelected));
            ViewModel.OnPropertyChangedWithValue(BellumStatsControlsVisible, nameof(BellumStatsControlsVisible));
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithValue -= OnSelectionChanged;
            BellumOverview.OnFinalize();
            base.OnFinalize();
        }
    }
}
