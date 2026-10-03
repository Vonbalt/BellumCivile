using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.UI.VanillaTabs.Kingdoms.Succession;
using JetBrains.Annotations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues")]
    [UsedImplicitly]
    internal sealed class KingdomPoliciesSuccessionVMMixin : BaseViewModelMixin<KingdomPoliciesVM>
    {
        private static readonly ConditionalWeakTable<KingdomPoliciesVM, KingdomPoliciesSuccessionVMMixin> Instances =
            new ConditionalWeakTable<KingdomPoliciesVM, KingdomPoliciesSuccessionVMMixin>();

        private KingdomSuccessionPolicyVM _bellumSuccession;
        private bool _bellumSuccessionSelected;
        private string _successionLawsEntryText;

        public KingdomPoliciesSuccessionVMMixin(KingdomPoliciesVM vm) : base(vm)
        {
            SuccessionLawsEntryText = new TextObject("{=BC_SuccessionLaws_Header}Succession Laws").ToString();
            EnsureSuccessionViewModel();
            RefreshDisplayedActivePolicyCount();

            Instances.Remove(vm);
            Instances.Add(vm, this);
        }

        [DataSourceProperty]
        public KingdomSuccessionPolicyVM BellumSuccession
        {
            get => _bellumSuccession;
            private set
            {
                if (_bellumSuccession == value)
                    return;

                _bellumSuccession = value;
                OnPropertyChangedWithValue(value, nameof(BellumSuccession));
            }
        }

        [DataSourceProperty]
        public bool BellumSuccessionSelected
        {
            get => _bellumSuccessionSelected;
            private set
            {
                if (_bellumSuccessionSelected == value)
                    return;

                _bellumSuccessionSelected = value;
                OnPropertyChangedWithValue(value, nameof(BellumSuccessionSelected));
                OnPropertyChanged(nameof(HideNoPolicySelectionText));
            }
        }

        [DataSourceProperty]
        public string SuccessionLawsEntryText
        {
            get => _successionLawsEntryText;
            private set
            {
                if (_successionLawsEntryText == value)
                    return;

                _successionLawsEntryText = value;
                OnPropertyChangedWithValue(value, nameof(SuccessionLawsEntryText));
            }
        }

        [DataSourceProperty]
        public bool HideNoPolicySelectionText => BellumSuccessionSelected || ViewModel?.IsAcceptableItemSelected == true;

        public override void OnRefresh()
        {
            base.OnRefresh();
            SuccessionLawsEntryText = new TextObject("{=BC_SuccessionLaws_Header}Succession Laws").ToString();
            EnsureSuccessionViewModel();
            if (BellumSuccessionSelected)
                BellumSuccession?.RefreshValues();
            RefreshDisplayedActivePolicyCount();
            OnPropertyChanged(nameof(HideNoPolicySelectionText));
        }

        public override void OnFinalize()
        {
            KingdomPoliciesVM vm = ViewModel;
            BellumSuccession?.OnFinalize();
            BellumSuccession = null;
            if (vm != null)
                Instances.Remove(vm);
            base.OnFinalize();
        }

        [DataSourceMethod]
        [UsedImplicitly]
        public void ExecuteShowSuccessionLaws()
        {
            if (ViewModel == null)
                return;

            EnsureSuccessionViewModel();
            if (BellumSuccession == null)
                return;

            if (ViewModel.CurrentSelectedPolicy != null)
                ViewModel.CurrentSelectedPolicy.IsSelected = false;
            ViewModel.CurrentSelectedPolicy = null;
            SetPrivate<PolicyObject>("_currentSelectedPolicyObject", null);
            SetPrivate<KingdomDecision>("_currentItemsUnresolvedDecision", null);
            ViewModel.CanProposeOrDisavowPolicy = false;
            ViewModel.IsAcceptableItemSelected = false;

            BellumSuccession.RefreshValues();
            BellumSuccessionSelected = true;
        }

        internal static void NotifyVanillaPolicySelected(KingdomPoliciesVM vm, KingdomPolicyItemVM policy)
        {
            if (vm == null || policy == null || !Instances.TryGetValue(vm, out KingdomPoliciesSuccessionVMMixin mixin))
                return;

            mixin.BellumSuccessionSelected = false;
        }

        internal static void SelectSuccessionLaws(KingdomPoliciesVM vm)
        {
            if (vm != null && Instances.TryGetValue(vm, out KingdomPoliciesSuccessionVMMixin mixin))
                mixin.ExecuteShowSuccessionLaws();
        }

        private void EnsureSuccessionViewModel()
        {
            if (BellumSuccession != null)
                return;

            Kingdom kingdom = GetPrivate<Kingdom>("_playerKingdom") ?? Clan.PlayerClan?.Kingdom;
            if (kingdom != null)
                BellumSuccession = new KingdomSuccessionPolicyVM(kingdom);
        }

        private void RefreshDisplayedActivePolicyCount()
        {
            if (ViewModel == null)
                return;

            GameTexts.SetVariable("STR", (ViewModel.ActivePolicies?.Count ?? 0) + 1);
            ViewModel.NumOfActivePoliciesText = GameTexts.FindText("str_STR_in_parentheses").ToString();
        }
    }
}
