using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using JetBrains.Annotations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.HeirSelectionPopup;
using TaleWorlds.Library;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues")]
    [UsedImplicitly]
    internal sealed class HeirSelectionPopupLegalHeirMixin : BaseViewModelMixin<HeirSelectionPopupVM>
    {
        private bool _bellumCanConfirmLegalHeir = true;

        public HeirSelectionPopupLegalHeirMixin(HeirSelectionPopupVM vm) : base(vm)
        {
            HeirSelectionPopupLegalHeirMixinRegistry.Register(vm, this);
            RefreshLegalHeirState();
        }

        public override void OnRefresh()
        {
            base.OnRefresh();
            RefreshLegalHeirState();
        }

        public void RefreshLegalHeirState()
        {
            Hero selectedHeir = ViewModel?.CurrentSelectedHero?.Hero;
            RegencyBehavior regency = RegencyBehavior.Instance;
            if (regency?.HasPendingPlayerSuccessionSelection == true)
            {
                ViewModel.TitleText = regency.GetPlayerSuccessionSelectionTitle().ToString();
                ViewModel.ButtonOkLabel = regency.GetPlayerSuccessionSelectionButtonText().ToString();
                BellumCanConfirmLegalHeir = regency.CanSelectPendingPlayerSuccessor(selectedHeir);
                return;
            }

            bool canConfirm = SuccessionLawHelper.CanConfirmSelectedPlayerHeir(
                selectedHeir,
                out _,
                out _,
                out _,
                out _);

            BellumCanConfirmLegalHeir = canConfirm;
        }

        [DataSourceProperty]
        public bool BellumCanConfirmLegalHeir
        {
            get => _bellumCanConfirmLegalHeir;
            set
            {
                if (value != _bellumCanConfirmLegalHeir)
                {
                    _bellumCanConfirmLegalHeir = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumCanConfirmLegalHeir");
                }
            }
        }
    }
}
