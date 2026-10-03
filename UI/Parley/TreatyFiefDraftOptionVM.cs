using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyFiefDraftOptionVM : ViewModel
    {
        private readonly string _settlementId;
        private readonly Action<string> _toggleAction;
        private string _name;
        private string _costText;
        private bool _isSelected;
        private bool _isDisabled;
        private string _selectionText;

        public TreatyFiefDraftOptionVM(
            Settlement settlement,
            int cost,
            bool selected,
            bool disabled,
            Action<string> toggleAction,
            Kingdom receivingRealm,
            bool occupied = false,
            int prisonerCount = 0,
            Kingdom clientBeneficiary = null)
        {
            _settlementId = settlement?.StringId ?? string.Empty;
            _toggleAction = toggleAction;
            string settlementName = settlement?.Name?.ToString() ?? string.Empty;
            Name = clientBeneficiary == null ? BuildName(settlementName, occupied, prisonerCount)
                : ClientWarTerritory.RecognitionText(settlement, clientBeneficiary).ToString();
            RowHeight = clientBeneficiary == null ? 38 : 76;
            CostText = new TextObject("{=BC_Parley_WarScoreCost}{VALUE} WS")
                .SetTextVariable("VALUE", cost)
                .ToString();
            IsSelected = selected;
            SelectionText = selected ? "[X]" : "[ ]";
            IsDisabled = disabled;
            Claimants = new MBBindingList<TreatyFiefClaimantVM>();
            PopulateClaimants(settlement, receivingRealm);
        }

        private void PopulateClaimants(Settlement settlement, Kingdom receivingRealm)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || receivingRealm?.Clans == null || !titles.TryGetBarony(settlement, out FeudalTitleRecord title))
                return;

            var claimants = receivingRealm.Clans
                .Where(clan => clan != null && !clan.IsEliminated)
                .Select(clan =>
                {
                    if (titles.HasActiveClaim(clan, title, FeudalClaimStrength.Strong))
                        return new { Clan = clan, Strength = (FeudalClaimStrength?)FeudalClaimStrength.Strong };
                    if (titles.HasActiveClaim(clan, title, FeudalClaimStrength.Weak))
                        return new { Clan = clan, Strength = (FeudalClaimStrength?)FeudalClaimStrength.Weak };
                    return new { Clan = clan, Strength = (FeudalClaimStrength?)null };
                })
                .Where(entry => entry.Strength.HasValue)
                .OrderByDescending(entry => entry.Strength.Value == FeudalClaimStrength.Strong)
                .ThenByDescending(entry => entry.Clan.CurrentTotalStrength)
                .ThenBy(entry => entry.Clan.Name?.ToString() ?? string.Empty)
                .Take(3);

            foreach (var claimant in claimants)
                Claimants.Add(new TreatyFiefClaimantVM(claimant.Clan, claimant.Strength.Value));
        }

        private static string BuildName(string settlementName, bool occupied, int prisonerCount)
        {
            TextObject text;
            if (prisonerCount == 1)
            {
                text = occupied
                    ? new TextObject("{=BC_Parley_OccupiedFiefOnePrisoner}{FIEF_NAME} (Occupied; 1 prisoner)")
                    : new TextObject("{=BC_Parley_FiefOnePrisoner}{FIEF_NAME} (1 prisoner)");
            }
            else if (prisonerCount > 1)
            {
                text = occupied
                    ? new TextObject("{=BC_Parley_OccupiedFiefPrisoners}{FIEF_NAME} (Occupied; {COUNT} prisoners)")
                    : new TextObject("{=BC_Parley_FiefPrisoners}{FIEF_NAME} ({COUNT} prisoners)");
                text.SetTextVariable("COUNT", prisonerCount);
            }
            else
            {
                text = occupied
                    ? new TextObject("{=BC_Parley_OccupiedFief}{FIEF_NAME} (Occupied)")
                    : new TextObject("{=BC_Parley_FiefName}{FIEF_NAME}");
            }

            return text.SetTextVariable("FIEF_NAME", settlementName).ToString();
        }

        public void ExecuteToggle()
        {
            if (!IsDisabled && !string.IsNullOrWhiteSpace(_settlementId))
                _toggleAction?.Invoke(_settlementId);
        }

        [DataSourceProperty]
        public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } } }
        [DataSourceProperty] public int RowHeight { get; }
        [DataSourceProperty]
        public string CostText { get => _costText; set { if (value != _costText) { _costText = value; OnPropertyChangedWithValue(value, nameof(CostText)); } } }
        [DataSourceProperty]
        public bool IsSelected { get => _isSelected; set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } } }
        [DataSourceProperty]
        public string SelectionText { get => _selectionText; set { if (value != _selectionText) { _selectionText = value; OnPropertyChangedWithValue(value, nameof(SelectionText)); } } }
        [DataSourceProperty]
        public bool IsDisabled { get => _isDisabled; set { if (value != _isDisabled) { _isDisabled = value; OnPropertyChangedWithValue(value, nameof(IsDisabled)); } } }
        [DataSourceProperty]
        public MBBindingList<TreatyFiefClaimantVM> Claimants { get; }
    }
}
