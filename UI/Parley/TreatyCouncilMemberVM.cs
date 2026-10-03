using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyCouncilMemberVM : ViewModel
    {
        private string _displayName;
        private string _voteText;
        private Color _voteColor;
        private string _enthusiasmText;
        private string _influenceText;
        private string _primaryReasonText;
        private int _enthusiasmValue;
        private Color _enthusiasmColor;
        private CharacterImageIdentifierVM _portraitVisual;
        private BannerImageIdentifierVM _bannerVisual;
        private readonly BasicTooltipViewModel _reasonTooltip;

        public TreatyCouncilMemberVM(
            TreatyCouncilMemberEvaluation evaluation,
            bool isRuler = false,
            bool realmAccepts = false,
            int rulerOverrideCommitment = 0,
            string rulerCouncilReason = null)
        {
            Clan clan = evaluation?.Clan;
            DisplayName = BuildDisplayName(clan);
            VoteText = isRuler
                ? BuildRulerDecisionText(realmAccepts)
                : BuildVoteText(evaluation?.Stance ?? TreatyCouncilVoteStance.Abstain);
            VoteColor = isRuler
                ? BuildRulerDecisionColor(realmAccepts)
                : BuildVoteColor(evaluation?.Stance ?? TreatyCouncilVoteStance.Abstain);
            EnthusiasmValue = Math.Max(0, Math.Min(100, (int)Math.Round(evaluation?.Enthusiasm ?? 0f)));
            EnthusiasmColor = EnthusiasmValue <= 30
                ? Color.ConvertStringToColor("#82E06AFF")
                : EnthusiasmValue <= 70
                    ? Color.ConvertStringToColor("#E4B85EFF")
                    : Colors.Red;
            EnthusiasmText = new TextObject("{=BC_Parley_Enthusiasm}Enthusiasm: {VALUE}")
                .SetTextVariable("VALUE", EnthusiasmValue + "%")
                .ToString();
            int committedInfluence = isRuler
                ? Math.Max(0, rulerOverrideCommitment)
                : Math.Max(0, evaluation?.InfluenceCommitment ?? 0);
            InfluenceText = new TextObject("{=BC_Parley_Commitment}Influence commitment: {VALUE}")
                .SetTextVariable("VALUE", committedInfluence)
                .ToString();

            TreatyCouncilReason primary = evaluation?.PrimaryReason;
            PrimaryReasonText = isRuler && !string.IsNullOrWhiteSpace(rulerCouncilReason)
                ? rulerCouncilReason
                : primary == null
                ? new TextObject("{=BC_Parley_NoStrongMotive}No decisive motive").ToString()
                : primary.Label;
            if (clan?.Leader?.CharacterObject != null)
                PortraitVisual = new CharacterImageIdentifierVM(BellumCivile.UI.PortraitAppearance.Create(clan.Leader.CharacterObject));
            if (clan?.Banner != null)
                BannerVisual = new BannerImageIdentifierVM(clan.Banner, true);

            _reasonTooltip = new BasicTooltipViewModel(() => BuildTooltip(
                evaluation,
                isRuler,
                realmAccepts,
                committedInfluence,
                rulerCouncilReason));
        }

        public void ExecuteBeginReasonHint()
        {
            _reasonTooltip?.ExecuteBeginHint();
        }

        public void ExecuteEndReasonHint()
        {
            _reasonTooltip?.ExecuteEndHint();
        }

        [DataSourceProperty]
        public string DisplayName { get => _displayName; set { if (value != _displayName) { _displayName = value; OnPropertyChangedWithValue(value, "DisplayName"); } } }
        [DataSourceProperty]
        public string VoteText { get => _voteText; set { if (value != _voteText) { _voteText = value; OnPropertyChangedWithValue(value, "VoteText"); } } }
        [DataSourceProperty]
        public Color VoteColor { get => _voteColor; set { if (value != _voteColor) { _voteColor = value; OnPropertyChangedWithValue(value, "VoteColor"); } } }
        [DataSourceProperty]
        public string EnthusiasmText { get => _enthusiasmText; set { if (value != _enthusiasmText) { _enthusiasmText = value; OnPropertyChangedWithValue(value, "EnthusiasmText"); } } }
        [DataSourceProperty]
        public string InfluenceText { get => _influenceText; set { if (value != _influenceText) { _influenceText = value; OnPropertyChangedWithValue(value, "InfluenceText"); } } }
        [DataSourceProperty]
        public string PrimaryReasonText { get => _primaryReasonText; set { if (value != _primaryReasonText) { _primaryReasonText = value; OnPropertyChangedWithValue(value, "PrimaryReasonText"); } } }
        [DataSourceProperty]
        public int EnthusiasmValue { get => _enthusiasmValue; set { if (value != _enthusiasmValue) { _enthusiasmValue = value; OnPropertyChangedWithValue(value, "EnthusiasmValue"); } } }
        [DataSourceProperty]
        public Color EnthusiasmColor { get => _enthusiasmColor; set { if (value != _enthusiasmColor) { _enthusiasmColor = value; OnPropertyChangedWithValue(value, "EnthusiasmColor"); } } }
        [DataSourceProperty]
        public CharacterImageIdentifierVM PortraitVisual { get => _portraitVisual; set { if (value != _portraitVisual) { _portraitVisual = value; OnPropertyChangedWithValue(value, "PortraitVisual"); } } }
        [DataSourceProperty]
        public BannerImageIdentifierVM BannerVisual { get => _bannerVisual; set { if (value != _bannerVisual) { _bannerVisual = value; OnPropertyChangedWithValue(value, "BannerVisual"); } } }

        private static string BuildDisplayName(Clan clan)
        {
            TextObject text = new TextObject("{=BC_Parley_CouncilMemberName}{HERO_NAME} {CLAN_NAME}");
            text.SetTextVariable("HERO_NAME", clan?.Leader?.Name ?? new TextObject("?"));
            text.SetTextVariable("CLAN_NAME", clan?.Name ?? new TextObject("?"));
            return text.ToString();
        }

        private static string BuildVoteText(TreatyCouncilVoteStance stance)
        {
            switch (stance)
            {
                case TreatyCouncilVoteStance.Yay: return new TextObject("{=BC_Parley_VoteYay}YAY").ToString();
                case TreatyCouncilVoteStance.Nay: return new TextObject("{=BC_Parley_VoteNay}NAY").ToString();
                default: return new TextObject("{=BC_Parley_VoteAbstain}ABSTAIN").ToString();
            }
        }

        private static Color BuildVoteColor(TreatyCouncilVoteStance stance)
        {
            switch (stance)
            {
                case TreatyCouncilVoteStance.Yay: return Color.ConvertStringToColor("#82E06AFF");
                case TreatyCouncilVoteStance.Nay: return Colors.Red;
                default: return Color.ConvertStringToColor("#E7D7B1FF");
            }
        }

        private static string BuildRulerDecisionText(bool accepts)
        {
            return accepts
                ? new TextObject("{=BC_Parley_RulerAccept}ACCEPT").ToString()
                : new TextObject("{=BC_Parley_RulerReject}REJECT").ToString();
        }

        private static Color BuildRulerDecisionColor(bool accepts)
        {
            return accepts
                ? Color.ConvertStringToColor("#82E06AFF")
                : Colors.Red;
        }

        private static List<TooltipProperty> BuildTooltip(
            TreatyCouncilMemberEvaluation evaluation,
            bool isRuler,
            bool realmAccepts,
            int committedInfluence,
            string rulerCouncilReason)
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            string name = BuildDisplayName(evaluation?.Clan);
            properties.Add(new TooltipProperty(name, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title));
            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            properties.Add(new TooltipProperty(new TextObject("{=BC_Parley_TreatyUtility}Treaty Assessment").ToString(), (evaluation?.Utility ?? 0f).ToString("+0.0;-0.0;0.0"), 0));
            properties.Add(new TooltipProperty(new TextObject("{=BC_Parley_Enthusiasm}Enthusiasm").ToString(), (evaluation?.Enthusiasm ?? 0f).ToString("0") + "%", 0));
            if (isRuler)
                properties.Add(new TooltipProperty(new TextObject("{=BC_Parley_RulerDecision}Ruler Decision").ToString(), BuildRulerDecisionText(realmAccepts), 0));
            properties.Add(new TooltipProperty(new TextObject("{=BC_Parley_CommittedInfluence}Committed Influence").ToString(), committedInfluence.ToString(), 0));
            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            if (isRuler && !string.IsNullOrWhiteSpace(rulerCouncilReason))
                properties.Add(new TooltipProperty(rulerCouncilReason, string.Empty, 0));
            foreach (TreatyCouncilReason reason in evaluation?.Reasons ?? new List<TreatyCouncilReason>())
                properties.Add(new TooltipProperty(reason.Label, reason.Amount.ToString("+0.0;-0.0;0.0"), 0));
            return properties;
        }

    }
}
