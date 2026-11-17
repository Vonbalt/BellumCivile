using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using BellumCivile.UI.Diplomacy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class KingdomDiplomacyOverviewMixin : BaseViewModelMixin<KingdomDiplomacyVM>
    {
        private bool _overviewSelected = true;
        private bool _firstThreatVisible, _secondThreatVisible;
        [DataSourceProperty] public BellumDiplomacyOverviewVM BellumOverview { get; }
            = new BellumDiplomacyOverviewVM();
        [DataSourceProperty] public bool BellumOverviewSelected => _overviewSelected;
        [DataSourceProperty] public bool BellumStatsSelected => !_overviewSelected;
        [DataSourceProperty] public bool BellumStatsControlsVisible => !_overviewSelected && ViewModel.IsWar;
        [DataSourceProperty] public string BellumOverviewText => new TextObject("{=BC_Overview_Title}Overview").ToString();
        [DataSourceProperty] public string BellumStatsText => new TextObject("{=BC_Overview_Stats}Stats").ToString();
        [DataSourceProperty] public bool BellumCentralDiplomacyActionVisible => BellumCivileOptions.EnableWarPeaceLogicRevamp;
        [DataSourceProperty] public bool BellumNativeDiplomacyActionsVisible => !BellumCentralDiplomacyActionVisible;
        [DataSourceProperty] public bool BellumDiplomacyExternalButtonsVisible => !BellumCentralDiplomacyActionVisible
            || !DiplomacyExternalActionAdapter.CanReplace(ViewModel.CurrentSelectedDiplomacyItem);
        [DataSourceProperty] public string BellumDiplomaticActionText => new TextObject("{=BC_Diplomacy_ActionButton}Diplomatic Action").ToString();
        [DataSourceProperty] public HintViewModel BellumFirstThreatHint { get; } = new HintViewModel();
        [DataSourceProperty] public HintViewModel BellumSecondThreatHint { get; } = new HintViewModel();
        [DataSourceProperty] public bool BellumFirstThreatVisible => _firstThreatVisible;
        [DataSourceProperty] public bool BellumSecondThreatVisible => _secondThreatVisible;

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
            RefreshThreats(item?.Faction1 as Kingdom, item?.Faction2 as Kingdom);
            NotifySelection();
            ViewModel.OnPropertyChangedWithValue(BellumCentralDiplomacyActionVisible, nameof(BellumCentralDiplomacyActionVisible));
            ViewModel.OnPropertyChangedWithValue(BellumNativeDiplomacyActionsVisible, nameof(BellumNativeDiplomacyActionsVisible));
            ViewModel.OnPropertyChangedWithValue(BellumDiplomacyExternalButtonsVisible, nameof(BellumDiplomacyExternalButtonsVisible));
            ViewModel.OnPropertyChangedWithValue(BellumDiplomaticActionText, nameof(BellumDiplomaticActionText));
        }

        private void RefreshThreats(Kingdom first, Kingdom second)
        {
            _firstThreatVisible = false;
            _secondThreatVisible = false;
            var tracker = Campaign.Current?.GetCampaignBehavior<DefensivePactThreatBehavior>();
            if (BellumCivileOptions.DefensivePactsActive && tracker?.IsReady == true)
            {
                try
                {
                    double now = CampaignTime.Now.ToDays;
                    _firstThreatVisible = UpdateThreatHint(first, tracker, now, BellumFirstThreatHint);
                    _secondThreatVisible = UpdateThreatHint(second, tracker, now, BellumSecondThreatHint);
                }
                catch (System.Exception ex) { BellumCivileLogger.Log("Diplomacy threat display unavailable: " + ex); }
            }
            ViewModel.OnPropertyChangedWithValue(_firstThreatVisible, nameof(BellumFirstThreatVisible));
            ViewModel.OnPropertyChangedWithValue(_secondThreatVisible, nameof(BellumSecondThreatVisible));
        }

        private static bool UpdateThreatHint(Kingdom realm, DefensivePactThreatBehavior tracker, double now, HintViewModel hint)
        {
            if (realm == null || realm.IsEliminated || string.IsNullOrWhiteSpace(realm.StringId) || tracker == null || hint == null)
                return false;
            hint.HintText = DefensivePactThreatHint.Current(realm.Name, tracker.GetThreat(realm.StringId, now));
            return true;
        }

        [DataSourceMethod]
        public void ExecuteBellumDiplomaticAction() => BellumDiplomacyActionMenu.Open(ViewModel);

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
