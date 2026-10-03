using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyFiefClaimantVM : ViewModel
    {
        private BannerImageIdentifierVM _bannerVisual;
        private HintViewModel _hint;

        public TreatyFiefClaimantVM(Clan clan, FeudalClaimStrength strength)
        {
            BannerVisual = clan?.Banner == null ? null : new BannerImageIdentifierVM(clan.Banner, true);
            TextObject hint = strength == FeudalClaimStrength.Strong
                ? new TextObject("{=BC_Parley_FiefStrongClaimantHint}{CLAN_NAME} has a strong claim to this fief.")
                : new TextObject("{=BC_Parley_FiefWeakClaimantHint}{CLAN_NAME} has a weak claim to this fief.");
            Hint = new HintViewModel(hint.SetTextVariable("CLAN_NAME", clan?.Name ?? TextObject.GetEmpty()));
        }

        public void ExecuteBeginHint() => Hint?.ExecuteBeginHint();
        public void ExecuteEndHint() => Hint?.ExecuteEndHint();

        [DataSourceProperty]
        public BannerImageIdentifierVM BannerVisual
        {
            get => _bannerVisual;
            set
            {
                if (value != _bannerVisual)
                {
                    _bannerVisual = value;
                    OnPropertyChangedWithValue(value, nameof(BannerVisual));
                }
            }
        }

        [DataSourceProperty]
        public HintViewModel Hint
        {
            get => _hint;
            set
            {
                if (value != _hint)
                {
                    _hint = value;
                    OnPropertyChangedWithValue(value, nameof(Hint));
                }
            }
        }
    }
}
