using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Factions;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using JetBrains.Annotations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues")]
    [UsedImplicitly]
    internal sealed class KingdomManagementVMMixin : BaseViewModelMixin<KingdomManagementVM>
    {
        private string _factionsLabel;
        private string _hierarchyLabel;
        private bool _factionsLabelBindingObserved;
        private bool _hierarchyLabelBindingObserved;
        private string _factionsInitializationError;
        private string _hierarchyInitializationError;

        [DataSourceProperty]
        public string FactionsLabel
        {
            get
            {
                _factionsLabelBindingObserved = true;
                return _factionsLabel;
            }
            set => _factionsLabel = value;
        }

        [DataSourceProperty]
        public bool ShowBellumCivileFactionsTab { get; set; }

        [DataSourceProperty]
        public string HierarchyLabel
        {
            get
            {
                _hierarchyLabelBindingObserved = true;
                return _hierarchyLabel;
            }
            set => _hierarchyLabel = value;
        }

        private bool _bellumFactionsSelected;
        private KingdomFactionsTabVM _bellumFactions;
        private bool _bellumHierarchySelected;
        private KingdomHierarchyTabVM _bellumHierarchy;

        public KingdomManagementVMMixin(KingdomManagementVM vm) : base(vm)
        {
            FactionsLabel = new TextObject("{=BC_KingdomTab_Factions}Factions").ToString();
            HierarchyLabel = new TextObject("{=BC_KingdomTab_Hierarchy}Hierarchy").ToString();
            RefreshStandaloneTabVisibility();
            BellumFactionsKingdomTabState.Register(
                vm,
                ClearBellumSelections,
                SelectBellumFactions,
                BuildDiagnosticFailures);
            BellumCivileLogger.Log("Constructed Bellum Civile KingdomManagementVMMixin.");
        }

        public override void OnRefresh()
        {
            base.OnRefresh();
            RefreshStandaloneTabVisibility();
            if (BellumFactionsSelected)
                EnsureBellumFactions()?.RefreshValues();
            if (BellumHierarchySelected)
                EnsureBellumHierarchy()?.RefreshValues();
            if (ViewModel.Clan.Show || ViewModel.Settlement.Show || ViewModel.Policy.Show || ViewModel.Army.Show || ViewModel.Diplomacy.Show)
            {
                ClearBellumSelections();
            }
        }

        [DataSourceMethod]
        [UsedImplicitly]
        public void ExecuteShowFactions()
        {
            SelectBellumFactions();
        }

        [DataSourceMethod]
        [UsedImplicitly]
        public void SelectBellumFactions()
        {
            BellumCivileLogger.Log("Bellum Civile kingdom Factions tab clicked.");
            KingdomFactionsTabVM factionsVm = EnsureBellumFactions();
            if (factionsVm == null)
                return;

            HideVanillaTabs();
            ClearBellumHierarchySelection();
            factionsVm.RefreshValues();
            factionsVm.ExecuteSelectPrivyCouncil();
            BellumFactionsSelected = true;
            factionsVm.IsSelected = true;
        }

        [DataSourceMethod]
        [UsedImplicitly]
        public void ExecuteShowHierarchy()
        {
            SelectBellumHierarchy();
        }

        [DataSourceMethod]
        [UsedImplicitly]
        public void SelectBellumHierarchy()
        {
            KingdomHierarchyTabVM hierarchyVm = EnsureBellumHierarchy();
            if (hierarchyVm == null)
                return;

            HideVanillaTabs();
            ClearBellumFactionsSelection();
            hierarchyVm.RefreshValues();
            BellumHierarchySelected = true;
            hierarchyVm.IsSelected = true;
        }

        private void HideVanillaTabs()
        {
            ViewModel.Clan.Show = false;
            ViewModel.Settlement.Show = false;
            ViewModel.Policy.Show = false;
            ViewModel.Army.Show = false;
            ViewModel.Diplomacy.Show = false;
        }

        private void ClearBellumFactionsSelection()
        {
            BellumFactionsSelected = false;
            if (BellumFactions != null)
                BellumFactions.IsSelected = false;
        }

        private void ClearBellumHierarchySelection()
        {
            BellumHierarchy?.ClearInteractionState();
            BellumHierarchySelected = false;
            if (BellumHierarchy != null)
                BellumHierarchy.IsSelected = false;
        }

        private void ClearBellumSelections()
        {
            ClearBellumFactionsSelection();
            ClearBellumHierarchySelection();
        }

        private void RefreshStandaloneTabVisibility()
        {
            ShowBellumCivileFactionsTab = SubModule.ShouldShowStandaloneFactionsTab;
            ViewModel.OnPropertyChangedWithValue(ShowBellumCivileFactionsTab, "ShowBellumCivileFactionsTab");
        }

        private KingdomFactionsTabVM EnsureBellumFactions()
        {
            if (BellumFactions != null)
                return BellumFactions;

            try
            {
                BellumFactions = new KingdomFactionsTabVM(ViewModel.Kingdom);
                return BellumFactions;
            }
            catch (System.Exception ex)
            {
                _factionsInitializationError = FormatException(ex);
                BellumCivileLogger.Log($"Failed to initialize Bellum kingdom factions tab; kingdom={ViewModel?.Kingdom?.StringId ?? "null"} error={_factionsInitializationError}");
                ReportInitializationFailure("Factions", "KTAB-F-DATA", _factionsInitializationError);
                return null;
            }
        }

        private KingdomHierarchyTabVM EnsureBellumHierarchy()
        {
            if (BellumHierarchy != null)
                return BellumHierarchy;

            try
            {
                BellumHierarchy = new KingdomHierarchyTabVM(ViewModel.Kingdom);
                return BellumHierarchy;
            }
            catch (System.Exception ex)
            {
                _hierarchyInitializationError = FormatException(ex);
                BellumCivileLogger.Log($"Failed to initialize Bellum kingdom hierarchy tab; kingdom={ViewModel?.Kingdom?.StringId ?? "null"} error={_hierarchyInitializationError}");
                ReportInitializationFailure("Hierarchy", "KTAB-H-DATA", _hierarchyInitializationError);
                return null;
            }
        }

        private IReadOnlyList<string> BuildDiagnosticFailures()
        {
            List<string> failures = new List<string>();
            string kingdomId = ViewModel?.Kingdom?.StringId ?? "null";

            if (string.IsNullOrWhiteSpace(_hierarchyLabel))
            {
                failures.Add("[KTAB-H-LABEL] Hierarchy failed: its localized tab label resolved to an empty value.");
            }
            else if (!_hierarchyLabelBindingObserved)
            {
                failures.Add("[KTAB-H-BIND] Hierarchy failed: the injected button did not bind to Bellum's view model. Another KingdomManagement prefab extension or an incompatible UIExtenderEx version likely displaced the binding.");
            }

            if (SubModule.ShouldShowStandaloneFactionsTab)
            {
                if (string.IsNullOrWhiteSpace(_factionsLabel))
                {
                    failures.Add("[KTAB-F-LABEL] Factions failed: its localized tab label resolved to an empty value.");
                }
                else if (!_factionsLabelBindingObserved)
                {
                    failures.Add("[KTAB-F-BIND] Factions failed: the injected button did not bind to Bellum's view model. Another KingdomManagement prefab extension or an incompatible UIExtenderEx version likely displaced the binding.");
                }
            }
            else if (!SubModule.IsDiplomacyFactionsButtonIntegrationReady)
            {
                failures.Add("[KTAB-F-DIPLOMACY] Factions failed: Diplomacy is loaded, but Bellum could not attach its compatibility handler to Diplomacy's Factions button.");
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                failures.Add("[KTAB-H-BEHAVIOR] Hierarchy failed: FeudalTitleBehavior is missing from the active campaign.");
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
            {
                failures.Add("[KTAB-F-BEHAVIOR] Factions failed: FactionManagerBehavior is missing from the active campaign.");
            }

            if (!string.IsNullOrWhiteSpace(_hierarchyInitializationError))
            {
                failures.Add($"[KTAB-H-DATA] Hierarchy failed to initialize for kingdom={kingdomId}: {_hierarchyInitializationError ?? "unknown error"}.");
            }
            else if (titleBehavior != null && ViewModel?.Kingdom != null)
            {
                FeudalTitleRecord sovereign = titleBehavior.GetRealmSovereignTitle(ViewModel.Kingdom, FeudalHierarchyMode.DeJure)
                    ?? titleBehavior.GetRealmSovereignTitle(ViewModel.Kingdom, FeudalHierarchyMode.DeFacto);
                if (sovereign == null)
                {
                    failures.Add($"[KTAB-H-TITLE] Hierarchy initialized, but no de jure or de facto sovereign title resolves for kingdom={kingdomId}.");
                }
            }

            if (!string.IsNullOrWhiteSpace(_factionsInitializationError))
            {
                failures.Add($"[KTAB-F-DATA] Factions failed to initialize for kingdom={kingdomId}: {_factionsInitializationError ?? "unknown error"}.");
            }

            return failures;
        }

        private void ReportInitializationFailure(string tabName, string code, string error)
        {
            if (!BellumCivileDebug.ShowInGameMessages)
                return;

            BellumCivileDebug.Trace(
                "kingdom tabs",
                $"[{code}] {tabName} failed to initialize for kingdom={ViewModel?.Kingdom?.StringId ?? "null"}: {error}.",
                requestInGameDisplay: true);
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null)
                return "unknown error";

            string message = string.IsNullOrWhiteSpace(ex.Message) ? "no message" : ex.Message;
            return $"{ex.GetType().Name}: {message}";
        }

        [DataSourceProperty]
        public bool BellumFactionsSelected
        {
            get => _bellumFactionsSelected;
            set
            {
                if (value != _bellumFactionsSelected)
                {
                    _bellumFactionsSelected = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumFactionsSelected");
                }
            }
        }

        [DataSourceProperty]
        public KingdomFactionsTabVM BellumFactions
        {
            get => _bellumFactions;
            set
            {
                if (value != _bellumFactions)
                {
                    _bellumFactions = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumFactions");
                }
            }
        }

        [DataSourceProperty]
        public bool BellumHierarchySelected
        {
            get => _bellumHierarchySelected;
            set
            {
                if (value != _bellumHierarchySelected)
                {
                    _bellumHierarchySelected = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumHierarchySelected");
                }
            }
        }

        [DataSourceProperty]
        public KingdomHierarchyTabVM BellumHierarchy
        {
            get => _bellumHierarchy;
            set
            {
                if (value != _bellumHierarchy)
                {
                    _bellumHierarchy = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumHierarchy");
                }
            }
        }

    }
}
