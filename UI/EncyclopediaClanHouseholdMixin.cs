using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Library;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class EncyclopediaClanHouseholdMixin : BaseViewModelMixin<EncyclopediaClanPageVM>
    {
        [DataSourceProperty] public MBBindingList<HeroVM> BellumClanFamily { get; } = new MBBindingList<HeroVM>();
        [DataSourceProperty] public MBBindingList<HeroVM> BellumClanRetainers { get; } = new MBBindingList<HeroVM>();
        [DataSourceProperty] public string BellumClanFamilyText => LordshipBehavior.Text("BC_HeroFamily_Header", "Family").ToString();
        [DataSourceProperty] public string BellumClanRetainersText => LordshipBehavior.Text("BC_Lordship_Retainers", "Retainers").ToString();
        [DataSourceProperty] public bool HasBellumClanFamily => EncyclopediaClanHouseholdPrefabPatch.Enabled && BellumClanFamily.Count > 0;
        [DataSourceProperty] public bool HasBellumClanRetainers => EncyclopediaClanHouseholdPrefabPatch.Enabled && BellumClanRetainers.Count > 0;

        public EncyclopediaClanHouseholdMixin(EncyclopediaClanPageVM vm) : base(vm)
        { vm.PropertyChangedWithBoolValue += Changed; }

        private void Changed(object sender, PropertyChangedWithBoolValueEventArgs args)
        { if (args.PropertyName == "IsLoadingOver" && ViewModel.IsLoadingOver) OnRefresh(); }

        public override void OnRefresh()
        {
            BellumClanFamily.Clear();
            BellumClanRetainers.Clear();
            if (ViewModel.Members != null)
                foreach (var portrait in ViewModel.Members)
                {
                    if (portrait?.Hero == null) continue;
                    (HouseholdMembership.IsRetainer(portrait.Hero) ? BellumClanRetainers : BellumClanFamily).Add(portrait);
                }
            foreach (string property in new[] { nameof(HasBellumClanFamily), nameof(HasBellumClanRetainers), nameof(BellumClanFamilyText), nameof(BellumClanRetainersText) })
                ViewModel.OnPropertyChanged(property);
        }

        public override void OnFinalize()
        {
            ViewModel.PropertyChangedWithBoolValue -= Changed;
            // The portraits belong to the native page; do not finalize them a second time.
            BellumClanFamily.Clear();
            BellumClanRetainers.Clear();
            base.OnFinalize();
        }
    }
}
