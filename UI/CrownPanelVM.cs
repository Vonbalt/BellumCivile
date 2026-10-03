using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    public sealed class CrownPanelVM : ViewModel
    {
        private readonly Kingdom _realm;
        private readonly Action _refresh;
        public CrownPanelVM(Kingdom realm, Action refresh = null)
        {
            _realm = realm;
            _refresh = refresh;
            if (realm?.RulingClan?.Leader == null) return;
            Ruler = new FactionMemberVM(realm.RulingClan, false);
            Title = new TextObject("{=BC_CourtCrown}Crown").ToString();
            ReignText = CourtAgendaBehavior.Current?.DescribeCrownReign(realm) ?? "";
            var demands = new TextObject("{=BC_CrownDemands}Demands: Enact the will of {RULER}");
            demands.SetTextVariable("RULER", realm.RulingClan.Leader.Name);
            Demands = demands.ToString();
            Favor = CourtAgendaBehavior.Current?.DescribeCrownFavor(realm) ?? "";
            Agenda = CourtAgendaBehavior.Current?.DescribeAgenda(realm, null, compact: true) ?? "";
            AgendaColor = Color.ConvertStringToColor(CourtAgendaPresentation.Color(CourtAgendaBehavior.Current?.GetDisplayedAgenda(realm, null)?.State));
            EnactedPolicyLists.Populate(realm, CourtAgendaBehavior.Current?.GetFavoredBloc(realm), true, AgendaListLeft, AgendaListRight);
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager != null)
                foreach (var type in new[] { FactionType.Nobility, FactionType.Glory, FactionType.Liberty })
                {
                    var faction = manager.GetFactionsInKingdom(realm).FirstOrDefault(f => f.Type == type
                        && f.Leader != null && CourtAgendaBehavior.MemberCount(f) > 0);
                    if (faction != null) FavorBanners.Add(new CrownFavorBannerVM(realm, faction, refresh));
                }
        }
        [DataSourceProperty] public string CrownActionsText => new TextObject("{=BC_CrownActions}Crown Actions").ToString();
        [DataSourceProperty] public bool CanUseCrownActions => CourtAgendaBehavior.Current?.CanUseCrownActions(_realm) == true;
        [DataSourceProperty] public HintViewModel CrownActionsHint => new HintViewModel(CourtAgendaBehavior.Current?.CrownActionsHint(_realm)
            ?? new TextObject("{=BC_CourtAgenda_Unavailable}The court cannot receive a motion at present."));
        public void ExecuteCrownActions() => CourtAgendaBehavior.Current?.ShowCrownActions(_realm, _refresh);
        [DataSourceProperty] public FactionMemberVM Ruler { get; }
        [DataSourceProperty] public string Title { get; } = "";
        [DataSourceProperty] public string Favor { get; } = "";
        [DataSourceProperty] public string Agenda { get; } = "";
        [DataSourceProperty] public MBBindingList<CrownFavorBannerVM> FavorBanners { get; } = new MBBindingList<CrownFavorBannerVM>();
        [DataSourceProperty] public string ReignText { get; } = "";
        [DataSourceProperty] public string Demands { get; } = "";
        [DataSourceProperty] public Color AgendaColor { get; }
        [DataSourceProperty] public MBBindingList<AgendaItemVM> AgendaListLeft { get; } = new MBBindingList<AgendaItemVM>();
        [DataSourceProperty] public MBBindingList<AgendaItemVM> AgendaListRight { get; } = new MBBindingList<AgendaItemVM>();
        [DataSourceProperty] public string PolicySupportText => new TextObject("{=BC_EnactedPolicySupport}Support").ToString();
        [DataSourceProperty] public string PolicyOpposeText => new TextObject("{=BC_EnactedPolicyOppose}Oppose").ToString();
    }
}
