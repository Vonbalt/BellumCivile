using System;
using TaleWorlds.Library;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Factions
{
    public class KingdomFactionTabItemVM : ViewModel
    {
        private readonly Action<KingdomFactionTabItemVM> _onSelect;
        private string _name;
        private string _subtitle;
        private bool _isSelected;

        public KingdomFactionTabItemVM(string name, string subtitle, object backendModel, Action<KingdomFactionTabItemVM> onSelect)
        {
            _name = name;
            _subtitle = subtitle;
            BackendModel = backendModel;
            _onSelect = onSelect;
        }

        public object BackendModel { get; }

        public void ExecuteSelect()
        {
            _onSelect?.Invoke(this);
        }

        public void OnSelect()
        {
            ExecuteSelect();
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set
            {
                if (value != _name)
                {
                    _name = value;
                    OnPropertyChangedWithValue(value, "Name");
                }
            }
        }

        [DataSourceProperty]
        public string Subtitle
        {
            get => _subtitle;
            set
            {
                if (value != _subtitle)
                {
                    _subtitle = value;
                    OnPropertyChangedWithValue(value, "Subtitle");
                }
            }
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
                    OnPropertyChangedWithValue(value, "IsSelected");
                }
            }
        }
    }
}
