using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyPrisonerDraftOptionVM : ViewModel
    {
        private readonly string _heroId;
        private readonly Action<string> _toggleAction;
        private string _name;
        private string _costText;
        private string _selectionText;
        private bool _isSelected;
        private bool _isDisabled;

        public TreatyPrisonerDraftOptionVM(
            Hero hero,
            Settlement holdingSettlement,
            int cost,
            bool selected,
            bool disabled,
            Action<string> toggleAction,
            bool includedWithFief = false)
        {
            _heroId = hero?.StringId ?? string.Empty;
            _toggleAction = toggleAction;
            TextObject name = holdingSettlement == null
                ? new TextObject("{=BC_Parley_PrisonerRelease}Release {HERO_NAME}")
                    .SetTextVariable("HERO_NAME", hero?.Name ?? TextObject.GetEmpty())
                : new TextObject(includedWithFief
                        ? "{=BC_Parley_PrisonerReleaseHeldIncluded}Release {HERO_NAME} (Held in {SETTLEMENT_NAME}; included with fief)"
                        : "{=BC_Parley_PrisonerReleaseHeld}Release {HERO_NAME} (Held in {SETTLEMENT_NAME})")
                    .SetTextVariable("HERO_NAME", hero?.Name ?? TextObject.GetEmpty())
                    .SetTextVariable("SETTLEMENT_NAME", holdingSettlement.Name ?? TextObject.GetEmpty());
            Name = name.ToString();
            CostText = new TextObject("{=BC_Parley_WarScoreCost}{VALUE} WS").SetTextVariable("VALUE", cost).ToString();
            IsSelected = selected || includedWithFief;
            SelectionText = selected || includedWithFief ? "[X]" : "[ ]";
            IsDisabled = disabled;
        }

        public void ExecuteToggle()
        {
            if (!IsDisabled && !string.IsNullOrWhiteSpace(_heroId))
                _toggleAction?.Invoke(_heroId);
        }

        [DataSourceProperty] public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } } }
        [DataSourceProperty] public string CostText { get => _costText; set { if (value != _costText) { _costText = value; OnPropertyChangedWithValue(value, nameof(CostText)); } } }
        [DataSourceProperty] public string SelectionText { get => _selectionText; set { if (value != _selectionText) { _selectionText = value; OnPropertyChangedWithValue(value, nameof(SelectionText)); } } }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } } }
        [DataSourceProperty] public bool IsDisabled { get => _isDisabled; set { if (value != _isDisabled) { _isDisabled = value; OnPropertyChangedWithValue(value, nameof(IsDisabled)); } } }
    }
}
