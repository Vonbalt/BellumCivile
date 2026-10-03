using TaleWorlds.Library;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile.UI
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as the ViewModel for individual political factions or armed rebellions, binding their core data (leader, strength, demands, rivals, and members) directly to the Gauntlet UI list.
    /// </summary>
    public class FactionItemVM : ViewModel
    {
        private string _name;
        private string _clanName;
        private string _foundingDate;
        private string _demands;
        private string _rivalsText;
        private bool _isSelected;
        private bool _hasMembers;
        private readonly Action<FactionItemVM> _onSelection;
        private Clan _leaderClan;
        private bool _showRebelliousIntent;

        private BannerImageIdentifierVM _bannerVisual;
        private CharacterImageIdentifierVM _leaderPortraitVisual;
        private HintViewModel _leaderScoreHint;
        private HintViewModel _activityHint;
        [DataSourceProperty] public bool HasActivityHint => _activityHint != null;
        public void ExecuteBeginActivityHint() => _activityHint?.ExecuteBeginHint();
        public void ExecuteEndActivityHint() => _activityHint?.ExecuteEndHint();
        [DataSourceProperty] public bool CanReviewMarriage => BackendFactionModel is FactionObject f && CourtAgendaBehavior.Current?.CanReviewDynastic(f) == true;
        [DataSourceProperty] public bool CanReviewTitlePetition => BackendFactionModel is FactionObject f && CourtAgendaBehavior.Current?.CanReviewTitlePetition(f) == true;
        [DataSourceProperty] public string ReviewTitlePetitionText => new TextObject("{=BC_TitleGrantReview}Review title petition").ToString();
        public void ExecuteReviewTitlePetition()
        {
            if (BackendFactionModel is FactionObject f) CourtAgendaBehavior.Current?.RequestTitlePetitionReview(f);
            OnPropertyChanged(nameof(CanReviewTitlePetition));
        }
        [DataSourceProperty] public string ReviewMarriageText => new TextObject("{=BC_CourtDynasticReview}Review marriage proposal").ToString();
        public void ExecuteReviewMarriage()
        {
            if (BackendFactionModel is FactionObject f) CourtAgendaBehavior.Current?.RequestDynasticReview(f);
            OnPropertyChanged(nameof(CanReviewMarriage));
        }
        private BasicTooltipViewModel _leaderScoreTooltip;
        private MBBindingList<FactionMemberVM> _members;

        private int _factionStrength;
        private int _loyalistStrength;
        private int _totalStrength;
        private string _powerText;
        private string _discontentText;

        public object BackendFactionModel { get; private set; }

        public FactionItemVM(string name, object backendModel, Action<FactionItemVM> onSelection)
        {
            _name = name;
            BackendFactionModel = backendModel;
            _onSelection = onSelection;
            Members = new MBBindingList<FactionMemberVM>();

            if (backendModel is FactionObject faction && faction.Leader != null)
            {
                TextObject foundedText = new TextObject("{=BC_Item_Founded}Founded: {DATE}");
                foundedText.SetTextVariable("DATE", faction.CreationDate.ToString());
                FoundingDate = foundedText.ToString();

                Refresh();
            }
        }

        public void Refresh()
        {
            if (BackendFactionModel is FactionObject faction && faction.Leader != null)
            {
                _showRebelliousIntent = !faction.IsIdeology;
                OnPropertyChanged(nameof(ShowRebelliousIntent));
                OnPropertyChanged(nameof(ShowPlainPortrait));
                _leaderClan = faction.Leader;
                ClanName = faction.Leader.Name.ToString();
                BannerVisual = new BannerImageIdentifierVM(faction.Leader.Banner, true);
                if (faction.Leader.Leader?.CharacterObject != null)
                    LeaderPortraitVisual = new CharacterImageIdentifierVM(BellumCivile.UI.PortraitAppearance.Create(faction.Leader.Leader.CharacterObject));
                
                TextObject demandsText = new TextObject("{=BC_Item_Demands}Demands: {DEMANDS}");
                demandsText.SetTextVariable("DEMANDS", faction.GetDemandDescription());
                Demands = demandsText.ToString();


                if (faction.IsIdeology)
                {
                    var activityText = CourtAgendaBehavior.Current?.RallyHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.TitleGrantHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.TradeHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.DynasticHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.SubjugationHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.CampaignHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.PeaceHint(faction.ParentKingdom, faction)
                        ?? CourtAgendaBehavior.Current?.ActivityHint(faction.ParentKingdom, faction);
                    activityText = CourtAgendaBehavior.Current?.ClaimHint(faction.ParentKingdom, faction, activityText) ?? activityText;
                    _activityHint = activityText == null ? null : new HintViewModel(activityText);
                    RivalsText = CourtAgendaBehavior.Current?.DescribeAgenda(faction.ParentKingdom, faction, compact: true) ?? "";
                    AgendaColor = Color.ConvertStringToColor(CourtAgendaPresentation.Color(CourtAgendaBehavior.Current?.GetDisplayedAgenda(faction.ParentKingdom, faction)?.State));
                }
                else
                {
                    _activityHint = null;
                    RivalsText = "";
                }
                OnPropertyChanged(nameof(HasActivityHint));
                OnPropertyChanged(nameof(CanReviewMarriage));
                OnPropertyChanged(nameof(CanReviewTitlePetition));

                if (_showRebelliousIntent)
                {
                    LeaderScoreHint = new HintViewModel(new TextObject(FactionMemberVM.GenerateScoreBreakdown(faction.Leader)));
                    _leaderScoreTooltip = new BasicTooltipViewModel(() => FactionMemberVM.BuildScoreTooltipProperties(_leaderClan));
                }
                else
                {
                    LeaderScoreHint = null;
                    _leaderScoreTooltip = null;
                }

                Members.Clear();
                if (faction.Members != null)
                {
                    foreach (var clan in faction.Members)
                    {
                        if (clan != faction.Leader)
                        {
                            Members.Add(new FactionMemberVM(clan, _showRebelliousIntent));
                        }
                    }
                }
                HasMembers = Members.Count > 0;

                RebellionPowerProjection projection = faction.CalculatePowerProjection();
                FactionStrength = (int)projection.FactionPower;
                LoyalistStrength = (int)projection.LoyalistPower;
                TotalStrength = FactionStrength + LoyalistStrength;
                if (TotalStrength <= 0) TotalStrength = 1;

                PowerText = $"{FactionStrength:N0} vs {LoyalistStrength:N0}";

                float threshold = O.DiscontentTrigger;
                float discontentPercent = threshold > 0f ? (faction.Discontent / threshold) * 100f : faction.Discontent;
                TextObject discontentText = new TextObject("{=BC_Item_Discontent}Discontent: {DISCONTENT}%");
                discontentText.SetTextVariable("DISCONTENT", discontentPercent.ToString("0"));
                DiscontentText = discontentText.ToString();
            }
        }

        public void ExecuteSelect()
        {
            _onSelection(this);
        }

        public void OnSelect()
        {
            ExecuteSelect();
        }

        public void ExecuteOpenClan()
        {
            if (_leaderClan != null && Campaign.Current?.EncyclopediaManager != null)
                Campaign.Current.EncyclopediaManager.GoToLink(_leaderClan.EncyclopediaLink);
        }

        [DataSourceProperty] public bool ShowRebelliousIntent => _showRebelliousIntent;
        [DataSourceProperty] public bool ShowPlainPortrait => !_showRebelliousIntent;

        public void ExecuteBeginScoreHint()
        {
            if (!_showRebelliousIntent)
                return;

            if (_leaderScoreTooltip != null)
                _leaderScoreTooltip.ExecuteBeginHint();
            else
                LeaderScoreHint?.ExecuteBeginHint();
        }

        public void ExecuteEndScoreHint()
        {
            if (!_showRebelliousIntent)
                return;

            if (_leaderScoreTooltip != null)
                _leaderScoreTooltip.ExecuteEndHint();
            else
                LeaderScoreHint?.ExecuteEndHint();
        }

        private static string BuildRivalsText(FactionType rivalType, Kingdom kingdom)
        {
            TextObject text = new TextObject("{=BC_Item_Rivals}Rivals: {FACTION_NAME}");
            text.SetTextVariable(
                "FACTION_NAME",
                CourtInstitutionDisplayHelper.GetCourtFactionName(rivalType, kingdom));
            return text.ToString();
        }

        [DataSourceProperty] public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "Name"); } } }
        [DataSourceProperty] public string ClanName { get => _clanName; set { if (value != _clanName) { _clanName = value; OnPropertyChangedWithValue(value, "ClanName"); } } }
        [DataSourceProperty] public string FoundingDate { get => _foundingDate; set { if (value != _foundingDate) { _foundingDate = value; OnPropertyChangedWithValue(value, "FoundingDate"); } } }
        [DataSourceProperty] public string Demands { get => _demands; set { if (value != _demands) { _demands = value; OnPropertyChangedWithValue(value, "Demands"); } } }
        [DataSourceProperty] public string RivalsText { get => _rivalsText; set { if (value != _rivalsText) { _rivalsText = value; OnPropertyChangedWithValue(value, "RivalsText"); } } }
        private Color _agendaColor = Color.ConvertStringToColor("#F1D8A4FF");
        [DataSourceProperty] public Color AgendaColor { get => _agendaColor; set { _agendaColor = value; OnPropertyChangedWithValue(value, nameof(AgendaColor)); } }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, "IsSelected"); } } }
        [DataSourceProperty] public bool HasMembers { get => _hasMembers; set { if (value != _hasMembers) { _hasMembers = value; OnPropertyChangedWithValue(value, "HasMembers"); } } }
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get => _bannerVisual; set { if (value != _bannerVisual) { _bannerVisual = value; OnPropertyChangedWithValue(value, "BannerVisual"); } } }
        [DataSourceProperty] public CharacterImageIdentifierVM LeaderPortraitVisual { get => _leaderPortraitVisual; set { if (value != _leaderPortraitVisual) { _leaderPortraitVisual = value; OnPropertyChangedWithValue(value, "LeaderPortraitVisual"); } } }
        [DataSourceProperty] public HintViewModel LeaderScoreHint { get => _leaderScoreHint; set { if (value != _leaderScoreHint) { _leaderScoreHint = value; OnPropertyChangedWithValue(value, "LeaderScoreHint"); } } }
        [DataSourceProperty] public MBBindingList<FactionMemberVM> Members { get => _members; set { if (value != _members) { _members = value; OnPropertyChangedWithValue(value, "Members"); } } }

        [DataSourceProperty] public int FactionStrength { get => _factionStrength; set { if (value != _factionStrength) { _factionStrength = value; OnPropertyChangedWithValue(value, "FactionStrength"); } } }
        [DataSourceProperty] public int LoyalistStrength { get => _loyalistStrength; set { if (value != _loyalistStrength) { _loyalistStrength = value; OnPropertyChangedWithValue(value, "LoyalistStrength"); } } }
        [DataSourceProperty] public int TotalStrength { get => _totalStrength; set { if (value != _totalStrength) { _totalStrength = value; OnPropertyChangedWithValue(value, "TotalStrength"); } } }
        [DataSourceProperty] public string PowerText { get => _powerText; set { if (value != _powerText) { _powerText = value; OnPropertyChangedWithValue(value, "PowerText"); } } }
        [DataSourceProperty] public string DiscontentText { get => _discontentText; set { if (value != _discontentText) { _discontentText = value; OnPropertyChangedWithValue(value, "DiscontentText"); } } }

        [DataSourceProperty] public string LeaderText => new TextObject("{=BC_UI_Leader}Leader").ToString();
        [DataSourceProperty] public string MembersText => new TextObject("{=BC_UI_Members}Members").ToString();
        [DataSourceProperty] public string BalanceOfPowerLabel => new TextObject("{=BC_UI_BalanceOfPower}Balance of Power: ").ToString();
        [DataSourceProperty]
        public string FactionText => new TextObject("{=BC_UI_Faction}Faction").ToString();
    }
}
