using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    public sealed class HierarchyKingdomItemVM : ViewModel
    {
        private readonly Action<HierarchyKingdomItemVM> _onSelect;
        private bool _isSelected;

        public HierarchyKingdomItemVM(
            Kingdom kingdom,
            FeudalTitleRecord sovereignTitle,
            bool isHistorical,
            Action<HierarchyKingdomItemVM> onSelect)
        {
            Kingdom = kingdom;
            SovereignTitle = sovereignTitle;
            IsHistorical = isHistorical;
            _onSelect = onSelect;
            Name = kingdom?.Name?.ToString() ?? string.Empty;
            if (kingdom?.Banner != null)
                BannerVisual = new BannerImageIdentifierVM(kingdom.Banner, true);
        }

        public Kingdom Kingdom { get; }
        public FeudalTitleRecord SovereignTitle { get; }
        public bool IsHistorical { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get; }

        public void ExecuteSelect() => _onSelect?.Invoke(this);

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected)
                    return;
                _isSelected = value;
                OnPropertyChangedWithValue(value, nameof(IsSelected));
            }
        }
    }
}
