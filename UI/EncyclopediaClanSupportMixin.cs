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
        [DataSourceProperty] public MBBindingList<EncyclopediaClanSupportItemVM> BellumClanSupport { get; } = new MBBindingList<EncyclopediaClanSupportItemVM>();
        [DataSourceProperty] public string BellumClanSupportTitle => new TextObject("{=BC_ClanSupport_Header}Vassals & Alliances").ToString();
        [DataSourceProperty] public bool HasBellumClanSupport => EncyclopediaClanSupportPrefab.Enabled && BellumClanSupport.Count > 0;

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
                var ties = RebellionPowerHelper.GetClanSupportNetwork(ViewModel.Obj as Clan, out bool canForecast);
                foreach (var tie in ties.OrderByDescending(tie => tie.ServiceShare.HasValue)
                    .ThenByDescending(tie => tie.ExpectedSupport?.CreditedPower ?? 0f).ThenBy(tie => tie.Clan.Name.ToString()))
                    BellumClanSupport.Add(new EncyclopediaClanSupportItemVM(tie, canForecast));
            }

            ViewModel.OnPropertyChanged(nameof(HasBellumClanSupport));
            ViewModel.OnPropertyChanged(nameof(BellumClanSupportTitle));
        }

        private void Clear()
        {
            foreach (var item in BellumClanSupport) item.OnFinalize();
            BellumClanSupport.Clear();
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithBoolValue -= OnPageChanged;
            Clear();
            base.OnFinalize();
        }
    }
}
