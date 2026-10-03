using System;
using BellumCivile.Behaviors;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    public class PoliticalActionVM : ViewModel
    {
        private readonly Action<PoliticalActionVM> _onSelection;
        private string _name;
        private bool _isSelected;
        private bool _isEnabled;
        private HintViewModel _hint;

        public PoliticalActionKind ActionKind { get; private set; }
        public FactionType FactionType { get; private set; }
        public string TargetTitleId { get; private set; }
        public int InfluenceCost { get; private set; }

        private PoliticalActionVM(
            PoliticalActionKind actionKind,
            FactionType factionType,
            string targetTitleId,
            string name,
            Action<PoliticalActionVM> onSelection,
            bool isEnabled,
            string hintText,
            int influenceCost)
        {
            ActionKind = actionKind;
            FactionType = factionType;
            TargetTitleId = targetTitleId;
            _name = name;
            _onSelection = onSelection;
            _isEnabled = isEnabled;
            _hint = new HintViewModel(new TextObject(hintText ?? string.Empty));
            InfluenceCost = influenceCost;
        }

        public static PoliticalActionVM ForFaction(
            FactionType type,
            string prettyName,
            Action<PoliticalActionVM> onSelection,
            bool isEnabled,
            string hintText,
            int influenceCost)
        {
            return new PoliticalActionVM(
                PoliticalActionKind.FoundFaction,
                type,
                null,
                prettyName,
                onSelection,
                isEnabled,
                hintText,
                influenceCost);
        }

        public static PoliticalActionVM ForClaimFeud(
            ClaimFeudActionPreview preview,
            Action<PoliticalActionVM> onSelection,
            string prettyName,
            string hintText)
        {
            return new PoliticalActionVM(
                PoliticalActionKind.StartClaimFeud,
                default(FactionType),
                preview?.TitleId,
                prettyName,
                onSelection,
                preview?.IsEnabled == true,
                hintText,
                100);
        }

        public void ExecuteSelect()
        {
            if (IsEnabled)
                _onSelection?.Invoke(this);
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } }
        }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (value != _isEnabled) { _isEnabled = value; OnPropertyChangedWithValue(value, nameof(IsEnabled)); } }
        }

        [DataSourceProperty]
        public HintViewModel Hint
        {
            get => _hint;
            set { if (value != _hint) { _hint = value; OnPropertyChangedWithValue(value, nameof(Hint)); } }
        }
    }
}
