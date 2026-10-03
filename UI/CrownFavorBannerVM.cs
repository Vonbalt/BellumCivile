using System;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    public sealed class CrownFavorBannerVM : ViewModel
    {
        private readonly Action _select;
        public CrownFavorBannerVM(Kingdom realm, FactionObject faction, Action refresh)
        {
            var behavior = CourtAgendaBehavior.Current;
            var ruler = realm.RulingClan;
            var player = Hero.MainHero;
            var expiry = behavior?.GetCrownFavorExpiry(realm);
            Name = faction.GetDisplayName().ToString();
            BannerVisual = new BannerImageIdentifierVM(faction.Leader.Banner, true);
            IsSelected = behavior?.GetFavoredBloc(realm) == faction.Type;
            IsEnabled = behavior?.CanSelectCrownFavor(realm) == true;
            Hint = new HintViewModel(new TextObject("{=!}" + Name + "\n" + behavior?.GetCrownFavorSelectionHint(realm)));
            _select = () =>
            {
                behavior?.TrySelectCrownFavor(realm, faction, ruler, player, expiry);
                refresh?.Invoke();
            };
        }
        public void ExecuteSelect() => _select();
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string InfluenceCostText => CrownFavorSelectionRules.Cost.ToString();
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get; }
        [DataSourceProperty] public HintViewModel Hint { get; }
        [DataSourceProperty] public bool IsSelected { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
    }
}
