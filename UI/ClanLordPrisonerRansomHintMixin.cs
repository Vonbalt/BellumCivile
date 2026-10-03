using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class ClanLordPrisonerRansomHintMixin : BaseViewModelMixin<ClanLordItemVM>
    {
        private HintViewModel _prisonerRansomHint;

        public ClanLordPrisonerRansomHintMixin(ClanLordItemVM vm) : base(vm)
        {
            RefreshPrisonerHint();
        }

        public override void OnRefresh()
        {
            RefreshPrisonerHint();
        }

        [DataSourceProperty]
        public HintViewModel PrisonerRansomHint
        {
            get => _prisonerRansomHint;
            private set
            {
                if (value != _prisonerRansomHint)
                {
                    _prisonerRansomHint = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(PrisonerRansomHint));
                }
            }
        }

        private void RefreshPrisonerHint()
        {
            Hero hero = ViewModel?.GetHero();
            if (hero?.IsPrisoner != true)
            {
                PrisonerRansomHint = new HintViewModel(new TextObject(string.Empty));
                return;
            }

            var pact = HostagePactText.Find(hero);
            if (pact != null)
            {
                ViewModel.CurrentActionText = new TextObject(pact.Phase == HostagePactPhase.Active
                    ? "{=BC_Hostage_ClanRow}Hostage for peace"
                    : "{=BC_Hostage_ClanPending}Awaiting disposition").ToString();
                var location = new TextObject(pact.Phase == HostagePactPhase.Active
                    ? "{=BC_Hostage_ClanDetail}Held as a hostage to secure peace with {REALM}"
                    : "{=BC_Hostage_ClanPendingDetail}Held in treaty custody, awaiting disposition");
                HostagePactText.Fill(location, pact, hero);
                ViewModel.LocationText = location.ToString();
                PrisonerRansomHint = new HintViewModel(HostagePactText.Status(pact, hero));
                return;
            }

            TextObject hint = new TextObject(
                "{=BC_ClanMember_PrisonerRansomHint}{CHARACTER_NAME} is imprisoned.{newline}You can negotiate their release at the peace parley, arrange a ransom through a broker, or wait for an incoming offer. Otherwise, they remain captive until rescued or they escape.");
            hint.SetTextVariable("CHARACTER_NAME", hero.Name);
            hint.SetTextVariable("newline", "\n");
            PrisonerRansomHint = new HintViewModel(hint);
        }
    }
}
