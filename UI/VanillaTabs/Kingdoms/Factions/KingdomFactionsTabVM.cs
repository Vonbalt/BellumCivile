using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Factions
{
    public class KingdomFactionsTabVM : FactionsWindowVM
    {
        private readonly Kingdom _kingdom;
        private bool _isCrownSelected;
        private CrownPanelVM _crown;
        [DataSourceProperty] public string CrownText => new TextObject("{=BC_CourtCrown}Crown").ToString();
        [DataSourceProperty] public bool HasCrown => _kingdom?.RulingClan?.Leader != null && !_kingdom.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(_kingdom);
        [DataSourceProperty] public CrownPanelVM Crown { get => _crown; private set { _crown = value; OnPropertyChangedWithValue(value, nameof(Crown)); } }
        [DataSourceProperty] public bool IsCrownSelected
        {
            get => _isCrownSelected;
            private set { _isCrownSelected = value; OnPropertyChangedWithValue(value, nameof(IsCrownSelected)); OnPropertyChanged(nameof(ShowFactionActionButtons)); }
        }
        public void ExecuteSelectCrown()
        {
            if (!HasCrown) return;
            if (SelectedFaction != null) SelectedFaction.IsSelected = false;
            SelectedFaction = null; HasSelectedFaction = false;
            if (SelectedFeud != null) SelectedFeud.IsSelected = false;
            SelectedFeud = null; HasSelectedFeud = false;
            IsPrivyCouncilSelected = false;
            IsCrownSelected = true;
            Crown = new CrownPanelVM(_kingdom, RefreshValues);
            RefreshButtonStates();
        }
        private MBBindingList<FactionItemVM> _courtFactions;
        private MBBindingList<FactionItemVM> _rebelFactions;
        private MBBindingList<ClaimFeudItemVM> _feudFactions;
        private PrivyCouncilVM _privyCouncil;
        private ClaimFeudItemVM _selectedFeud;
        private bool _isPrivyCouncilSelected;
        private bool _isSelected;
        private bool _hasCourtFactions;
        private bool _hasRebelFactions;
        private bool _hasFeuds;
        private bool _showCourtFactions = true;
        private bool _showRebelFactions = true;
        private bool _showFeuds = true;
        private bool _hasSelectedFeud;
        private string _courtFactionCountText;
        private string _rebelFactionCountText;
        private string _feudFactionCountText;

        public KingdomFactionsTabVM(Kingdom kingdom)
        {
            _kingdom = kingdom;
            CourtFactions = new MBBindingList<FactionItemVM>();
            RebelFactions = new MBBindingList<FactionItemVM>();
            FeudFactions = new MBBindingList<ClaimFeudItemVM>();
            PrivyCouncil = new PrivyCouncilVM(_kingdom);
            RefreshFactionBuckets();
            if (HasPrivyCouncil)
                ExecuteSelectPrivyCouncil();
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnCouncilDailyTick);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, (realm, clan) => { if (realm == _kingdom) RefreshValues(); });
        }

        protected override bool ShouldKeepFactionSelectionEmpty => IsCrownSelected || IsPrivyCouncilSelected || HasSelectedFeud;

        public override void OnFinalize()
        {
            CampaignEvents.RulingClanChanged.ClearListeners(this);
            Crown?.OnFinalize();
            base.OnFinalize();
        }

        private void OnCouncilDailyTick()
        {
            if (IsCrownSelected) Crown = new CrownPanelVM(_kingdom, RefreshValues);
            if (IsPrivyCouncilSelected)
                PrivyCouncil?.Refresh();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            SelectedFaction?.Refresh();
            SelectedFeud?.Refresh();
            PrivyCouncil?.Refresh();
            if (IsCrownSelected) Crown = new CrownPanelVM(_kingdom, RefreshValues);
            OnPropertyChanged(nameof(HasCrown));
            RefreshFactionBuckets();
        }

        private void RefreshFactionBuckets()
        {
            string selectedFeudId = SelectedFeud?.Record?.RecordId;
            CourtFactions.Clear();
            RebelFactions.Clear();
            FeudFactions.Clear();

            foreach (FactionItemVM item in Factions.Where(f => f?.BackendFactionModel is FactionObject))
            {
                if (item.BackendFactionModel is FactionObject faction && faction.IsIdeology)
                    CourtFactions.Add(item);
                else
                    RebelFactions.Add(item);
            }

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            foreach (ClaimFeudRecord feud in feudBehavior?.GetVisibleFeuds(_kingdom) ?? Enumerable.Empty<ClaimFeudRecord>())
            {
                FeudFactions.Add(new ClaimFeudItemVM(feud, OnFeudSelected, RefreshValues));
            }

            HasCourtFactions = HasCrown || HasPrivyCouncil || CourtFactions.Count > 0;
            HasRebelFactions = RebelFactions.Count > 0;
            HasFeuds = FeudFactions.Count > 0;
            if (!string.IsNullOrWhiteSpace(selectedFeudId))
            {
                ClaimFeudItemVM restoredFeud = FeudFactions.FirstOrDefault(feud => feud?.Record?.RecordId == selectedFeudId);
                if (SelectedFeud != null)
                    SelectedFeud.IsSelected = false;
                SelectedFeud = restoredFeud;
                if (SelectedFeud != null)
                    SelectedFeud.IsSelected = true;
                HasSelectedFeud = SelectedFeud != null;
            }
            CourtFactionCountText = FormatCount(CourtFactions.Count + (HasPrivyCouncil ? 1 : 0) + (HasCrown ? 1 : 0));
            RebelFactionCountText = FormatCount(RebelFactions.Count);
            FeudFactionCountText = FormatCount(FeudFactions.Count);
            OnPropertyChanged(nameof(CourtFactionsText));
            OnPropertyChanged(nameof(CourtFactionsHeaderText));
            OnPropertyChanged(nameof(PrivyCouncilText));
            OnPropertyChanged(nameof(RebelFactionsHeaderText));
            OnPropertyChanged(nameof(FeudsHeaderText));
            BellumCivileLogger.Log($"Bellum Factions tab data refreshed: kingdom={_kingdom?.StringId ?? "null"}, court={CourtFactions.Count}, rebel={RebelFactions.Count}, feuds={FeudFactions.Count}.");
        }

        private static string FormatCount(int count)
        {
            return "(" + count + ")";
        }

        public void ExecuteToggleCourtFactions()
        {
            ShowCourtFactions = !ShowCourtFactions;
        }

        public void ExecuteToggleRebelFactions()
        {
            ShowRebelFactions = !ShowRebelFactions;
        }

        public void ExecuteToggleFeuds()
        {
            ShowFeuds = !ShowFeuds;
        }

        protected override void OnRegularFactionSelected()
        {
            IsCrownSelected = false;
            IsPrivyCouncilSelected = false;
            if (SelectedFeud != null)
                SelectedFeud.IsSelected = false;
            SelectedFeud = null;
            HasSelectedFeud = false;
        }

        private void OnFeudSelected(ClaimFeudItemVM feud)
        {
            IsCrownSelected = false;
            IsPrivyCouncilSelected = false;
            if (SelectedFaction != null)
                SelectedFaction.IsSelected = false;
            SelectedFaction = null;
            HasSelectedFaction = false;

            if (SelectedFeud != null)
                SelectedFeud.IsSelected = false;
            SelectedFeud = feud;
            if (SelectedFeud != null)
                SelectedFeud.IsSelected = true;
            HasSelectedFeud = SelectedFeud != null;
            RefreshButtonStates();
        }

        public void ExecuteSelectPrivyCouncil()
        {
            IsCrownSelected = false;
            if (SelectedFaction != null)
                SelectedFaction.IsSelected = false;
            SelectedFaction = null;
            HasSelectedFaction = false;

            if (SelectedFeud != null)
                SelectedFeud.IsSelected = false;
            SelectedFeud = null;
            HasSelectedFeud = false;

            IsPrivyCouncilSelected = true;
            PrivyCouncil?.Refresh();
            RefreshButtonStates();
        }

        protected override ClaimFeudRecord GetSelectedClaimFeudRecord()
        {
            return SelectedFeud?.Record;
        }

        protected override void OnClaimFeudActionCompleted()
        {
            if (SelectedFeud != null)
                SelectedFeud.IsSelected = false;
            SelectedFeud = null;
            HasSelectedFeud = false;
            RefreshFactionBuckets();
        }

        public new void ExecuteJoin()
        {
            base.ExecuteJoin();
            RefreshFactionBuckets();
        }

        public new void ExecuteCreate()
        {
            base.ExecuteCreate();
            RefreshFactionBuckets();
        }

        public new void ExecuteConfirmCreate()
        {
            base.ExecuteConfirmCreate();
            RefreshFactionBuckets();
        }

        public new void ExecuteCancelCreate()
        {
            base.ExecuteCancelCreate();
            RefreshFactionBuckets();
        }

        public new void ExecuteUltimatum()
        {
            base.ExecuteUltimatum();
            RefreshValues();
            RefreshFactionBuckets();
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value, nameof(IsSelected));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<FactionItemVM> CourtFactions
        {
            get => _courtFactions;
            set
            {
                if (value != _courtFactions)
                {
                    _courtFactions = value;
                    OnPropertyChangedWithValue(value, nameof(CourtFactions));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<FactionItemVM> RebelFactions
        {
            get => _rebelFactions;
            set
            {
                if (value != _rebelFactions)
                {
                    _rebelFactions = value;
                    OnPropertyChangedWithValue(value, nameof(RebelFactions));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<ClaimFeudItemVM> FeudFactions
        {
            get => _feudFactions;
            set
            {
                if (value != _feudFactions)
                {
                    _feudFactions = value;
                    OnPropertyChangedWithValue(value, nameof(FeudFactions));
                }
            }
        }

        [DataSourceProperty]
        public ClaimFeudItemVM SelectedFeud
        {
            get => _selectedFeud;
            set
            {
                if (value != _selectedFeud)
                {
                    _selectedFeud = value;
                    OnPropertyChangedWithValue(value, nameof(SelectedFeud));
                }
            }
        }

        [DataSourceProperty]
        public PrivyCouncilVM PrivyCouncil
        {
            get => _privyCouncil;
            set
            {
                if (value != _privyCouncil)
                {
                    _privyCouncil = value;
                    OnPropertyChangedWithValue(value, nameof(PrivyCouncil));
                }
            }
        }

        [DataSourceProperty]
        public bool IsPrivyCouncilSelected
        {
            get => _isPrivyCouncilSelected;
            set
            {
                if (value != _isPrivyCouncilSelected)
                {
                    _isPrivyCouncilSelected = value;
                    OnPropertyChangedWithValue(value, nameof(IsPrivyCouncilSelected));
                    OnPropertyChanged(nameof(ShowFactionActionButtons));
                }
            }
        }

        [DataSourceProperty]
        public bool HasCourtFactions
        {
            get => _hasCourtFactions;
            set
            {
                if (value != _hasCourtFactions)
                {
                    _hasCourtFactions = value;
                    OnPropertyChangedWithValue(value, nameof(HasCourtFactions));
                }
            }
        }

        [DataSourceProperty]
        public bool HasRebelFactions
        {
            get => _hasRebelFactions;
            set
            {
                if (value != _hasRebelFactions)
                {
                    _hasRebelFactions = value;
                    OnPropertyChangedWithValue(value, nameof(HasRebelFactions));
                }
            }
        }

        [DataSourceProperty]
        public bool HasFeuds
        {
            get => _hasFeuds;
            set
            {
                if (value != _hasFeuds)
                {
                    _hasFeuds = value;
                    OnPropertyChangedWithValue(value, nameof(HasFeuds));
                }
            }
        }

        [DataSourceProperty]
        public bool HasSelectedFeud
        {
            get => _hasSelectedFeud;
            set
            {
                if (value != _hasSelectedFeud)
                {
                    _hasSelectedFeud = value;
                    OnPropertyChangedWithValue(value, nameof(HasSelectedFeud));
                }
            }
        }

        [DataSourceProperty]
        public bool ShowCourtFactions
        {
            get => _showCourtFactions;
            set
            {
                if (value != _showCourtFactions)
                {
                    _showCourtFactions = value;
                    OnPropertyChangedWithValue(value, nameof(ShowCourtFactions));
                    OnPropertyChanged(nameof(CourtFactionsHeaderText));
                }
            }
        }

        [DataSourceProperty]
        public bool ShowRebelFactions
        {
            get => _showRebelFactions;
            set
            {
                if (value != _showRebelFactions)
                {
                    _showRebelFactions = value;
                    OnPropertyChangedWithValue(value, nameof(ShowRebelFactions));
                    OnPropertyChanged(nameof(RebelFactionsHeaderText));
                }
            }
        }

        [DataSourceProperty]
        public bool ShowFeuds
        {
            get => _showFeuds;
            set
            {
                if (value != _showFeuds)
                {
                    _showFeuds = value;
                    OnPropertyChangedWithValue(value, nameof(ShowFeuds));
                    OnPropertyChanged(nameof(FeudsHeaderText));
                }
            }
        }

        [DataSourceProperty] public string ListHeaderText => HeaderText;
        [DataSourceProperty] public string PrivyCouncilText => CourtInstitutionDisplayHelper.GetPrivyCouncilName(_kingdom).ToString();
        [DataSourceProperty] public bool HasPrivyCouncil => _kingdom != null && !_kingdom.IsEliminated && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(_kingdom);
        [DataSourceProperty] public bool ShowFactionActionButtons => !IsPrivyCouncilSelected && !IsCrownSelected;
        [DataSourceProperty]
        public string CourtFactionsText
        {
            get
            {
                string courtName = FeudalTitleDisplayHelper.ResolveConciseSovereignTitleName(_kingdom);
                if (string.IsNullOrWhiteSpace(courtName))
                    courtName = "?";

                TextObject text = new TextObject("{=BC_KFactions_CourtOf}Court of {TITLE_NAME}");
                text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + courtName));
                return text.ToString();
            }
        }
        [DataSourceProperty] public string RebelFactionsText => new TextObject("{=BC_KFactions_RebelHeader}Rebel Factions").ToString();
        [DataSourceProperty] public string FeudsText => new TextObject("{=BC_KFactions_FeudsHeader}Feuds").ToString();
        [DataSourceProperty] public string CourtFactionsHeaderText => FormatSectionHeader(ShowCourtFactions, CourtFactionsText, CourtFactionCountText);
        [DataSourceProperty] public string RebelFactionsHeaderText => FormatSectionHeader(ShowRebelFactions, RebelFactionsText, RebelFactionCountText);
        [DataSourceProperty] public string FeudsHeaderText => FormatSectionHeader(ShowFeuds, FeudsText, FeudFactionCountText);

        private static string FormatSectionHeader(bool isOpen, string label, string count)
        {
            return (isOpen ? "[-] " : "[+] ") + label + " " + count;
        }

        [DataSourceProperty]
        public string CourtFactionCountText
        {
            get => _courtFactionCountText;
            set
            {
                if (value != _courtFactionCountText)
                {
                    _courtFactionCountText = value;
                    OnPropertyChangedWithValue(value, nameof(CourtFactionCountText));
                }
            }
        }

        [DataSourceProperty]
        public string RebelFactionCountText
        {
            get => _rebelFactionCountText;
            set
            {
                if (value != _rebelFactionCountText)
                {
                    _rebelFactionCountText = value;
                    OnPropertyChangedWithValue(value, nameof(RebelFactionCountText));
                }
            }
        }

        [DataSourceProperty]
        public string FeudFactionCountText
        {
            get => _feudFactionCountText;
            set
            {
                if (value != _feudFactionCountText)
                {
                    _feudFactionCountText = value;
                    OnPropertyChangedWithValue(value, nameof(FeudFactionCountText));
                }
            }
        }
    }
}
