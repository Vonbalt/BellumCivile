using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class EncyclopediaClanSupportMixin : BaseViewModelMixin<EncyclopediaClanPageVM>
    {
        private string _estimate = string.Empty;
        [DataSourceProperty] public MBBindingList<EncyclopediaClanSupportItemVM> BellumClanSupport { get; } = new MBBindingList<EncyclopediaClanSupportItemVM>();
        [DataSourceProperty] public string BellumClanSupportTitle => new TextObject("{=BC_ClanSupport_Header}Vassals & Alliances").ToString();
        [DataSourceProperty] public string BellumClanSupportEstimate => _estimate;
        [DataSourceProperty] public bool HasBellumClanSupport => EncyclopediaClanSupportPrefab.Enabled && BellumClanSupport.Count > 0;
        [DataSourceProperty] public bool HasBellumClanSupportEstimate => !string.IsNullOrEmpty(_estimate);

        public EncyclopediaClanSupportMixin(EncyclopediaClanPageVM vm) : base(vm)
        {
            vm.PropertyChangedWithBoolValue += OnPageChanged;
        }

        private void OnPageChanged(object sender, PropertyChangedWithBoolValueEventArgs args)
        {
            if (args.PropertyName == nameof(EncyclopediaClanPageVM.IsLoadingOver) && ViewModel.IsLoadingOver) OnRefresh();
        }

        public override void OnRefresh()
        {
            Clear();
            if (EncyclopediaClanSupportPrefab.Enabled)
            {
                List<ClanSupportConnection> ties = RebellionPowerHelper.GetClanSupportNetwork(ViewModel.Obj as Clan, out float? ownPower);
                foreach (var tie in ties.OrderByDescending(tie => tie.ServiceShare.HasValue)
                    .ThenByDescending(tie => tie.ExpectedSupport?.CreditedPower ?? 0f).ThenBy(tie => tie.Clan.Name.ToString()))
                    BellumClanSupport.Add(new EncyclopediaClanSupportItemVM(tie, ownPower.HasValue));

                if (ownPower.HasValue && ties.Count > 0)
                {
                    float support = ties.Sum(tie => tie.ExpectedSupport?.CreditedPower ?? 0f);
                    _estimate = new TextObject("{=BC_ClanSupport_Summary}Own power: {OWN}  |  Expected support: {SUPPORT}  |  Projected total: {TOTAL}")
                        .SetTextVariable("OWN", ownPower.Value.ToString("N0"))
                        .SetTextVariable("SUPPORT", support.ToString("N0"))
                        .SetTextVariable("TOTAL", (ownPower.Value + support).ToString("N0")).ToString();
                }
            }

            ViewModel.OnPropertyChanged(nameof(HasBellumClanSupport));
            ViewModel.OnPropertyChanged(nameof(HasBellumClanSupportEstimate));
            ViewModel.OnPropertyChanged(nameof(BellumClanSupportEstimate));
            ViewModel.OnPropertyChanged(nameof(BellumClanSupportTitle));
        }

        private void Clear()
        {
            foreach (var item in BellumClanSupport) item.OnFinalize();
            BellumClanSupport.Clear();
            _estimate = string.Empty;
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithBoolValue -= OnPageChanged;
            Clear();
            base.OnFinalize();
        }
    }
}
