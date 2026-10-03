using TaleWorlds.Library;
using System;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    /// <summary>
    /// Why did I do this file?
    /// To provide a selectable view model for the different types of factions a player can found (e.g., Secessionists, Royalists), handling the UI state for the creation menu.
    /// </summary>
    public class FactionTypeVM : ViewModel
    {
        private string _name;
        private bool _isSelected;
        private bool _isEnabled;
        private HintViewModel _hint;
        private Action<FactionTypeVM> _onSelection;

        public FactionType FactionType { get; private set; }

        public FactionTypeVM(FactionType type, string prettyName, Action<FactionTypeVM> onSelection, bool isEnabled = true, string hintText = "")
        {
            FactionType = type;
            _name = prettyName;
            _onSelection = onSelection;
            _isEnabled = isEnabled;
            _hint = new HintViewModel(new TextObject(hintText));
        }

        public void ExecuteSelect()
        {
            if (IsEnabled)
            {
                _onSelection(this);
            }
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "Name"); } }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, "IsSelected"); } }
        }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (value != _isEnabled) { _isEnabled = value; OnPropertyChangedWithValue(value, "IsEnabled"); } }
        }

        [DataSourceProperty]
        public HintViewModel Hint
        {
            get => _hint;
            set { if (value != _hint) { _hint = value; OnPropertyChangedWithValue(value, "Hint"); } }
        }
    }
}