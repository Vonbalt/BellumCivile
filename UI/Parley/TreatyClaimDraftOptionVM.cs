using System;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyClaimDraftOptionVM : ViewModel
    {
        private readonly string _key;
        private readonly Action<string> _toggleAction;
        private string _name;
        private string _costText;
        private string _selectionText;
        private bool _isSelected;
        private bool _isDisabled;
        private HintViewModel _hint;

        internal TreatyClaimDraftOptionVM(TreatyClaimRenunciationCandidate candidate, bool selected, bool disabled, Action<string> toggleAction)
        {
            _key = candidate?.ClaimantClan?.StringId + "|" + candidate?.Title?.TitleId;
            _toggleAction = toggleAction;
            TextObject strength = candidate?.Strength == FeudalClaimStrength.Strong
                ? new TextObject("{=BC_Claim_Strong}Strong")
                : new TextObject("{=BC_Claim_Weak}Weak");
            Name = new TextObject("{=BC_Parley_ClaimRenunciationOption}{CLAN_NAME}: {STRENGTH} claim to {TITLE_NAME}")
                .SetTextVariable("CLAN_NAME", candidate?.ClaimantClan?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("STRENGTH", strength)
                .SetTextVariable("TITLE_NAME", candidate?.Title == null
                    ? TextObject.GetEmpty()
                    : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(candidate.Title, candidate.ClaimantClan)))
                .ToString();
            CostText = new TextObject("{=BC_Parley_WarScoreCost}{VALUE} WS").SetTextVariable("VALUE", candidate?.WarScoreCost ?? 0).ToString();
            IsSelected = selected;
            SelectionText = selected ? "[X]" : "[ ]";
            IsDisabled = disabled;
            Hint = new HintViewModel(new TextObject("{=BC_Parley_Politics_RenounceClaimHint}Forces {CLAN_NAME} to renounce its {STRENGTH} claim to {TITLE_NAME}. The claim is removed when the treaty is ratified.")
                .SetTextVariable("CLAN_NAME", candidate?.ClaimantClan?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("STRENGTH", strength)
                .SetTextVariable("TITLE_NAME", candidate?.Title == null
                    ? TextObject.GetEmpty()
                    : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(candidate.Title, candidate.ClaimantClan))));
        }

        internal TreatyClaimDraftOptionVM(string key, string name, int cost, bool selected, bool disabled,
            Action<string> toggleAction, TextObject hint = null)
        {
            _key = key ?? string.Empty;
            _toggleAction = toggleAction;
            Name = name ?? string.Empty;
            CostText = new TextObject("{=BC_Parley_WarScoreCost}{VALUE} WS").SetTextVariable("VALUE", cost).ToString();
            IsSelected = selected;
            SelectionText = selected ? "[X]" : "[ ]";
            IsDisabled = disabled;
            Hint = new HintViewModel(hint ?? TextObject.GetEmpty());
        }

        public void ExecuteToggle()
        {
            if (!IsDisabled && !string.IsNullOrWhiteSpace(_key))
                _toggleAction?.Invoke(_key);
        }

        public void ExecuteBeginHint() => Hint?.ExecuteBeginHint();
        public void ExecuteEndHint() => Hint?.ExecuteEndHint();

        [DataSourceProperty] public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } } }
        [DataSourceProperty] public string CostText { get => _costText; set { if (value != _costText) { _costText = value; OnPropertyChangedWithValue(value, nameof(CostText)); } } }
        [DataSourceProperty] public string SelectionText { get => _selectionText; set { if (value != _selectionText) { _selectionText = value; OnPropertyChangedWithValue(value, nameof(SelectionText)); } } }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } } }
        [DataSourceProperty] public bool IsDisabled { get => _isDisabled; set { if (value != _isDisabled) { _isDisabled = value; OnPropertyChangedWithValue(value, nameof(IsDisabled)); } } }
        [DataSourceProperty] public HintViewModel Hint { get => _hint; set { if (value != _hint) { _hint = value; OnPropertyChangedWithValue(value, nameof(Hint)); } } }
    }
}
