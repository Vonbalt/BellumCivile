using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed class TreatyCouncilSummaryVM : ViewModel
    {
        private readonly BasicTooltipViewModel _tooltip;
        private bool _isHintVisible;

        [DataSourceProperty] public int YayInfluence { get; }
        [DataSourceProperty] public int TotalInfluence { get; }
        [DataSourceProperty] public Color OppositionColor { get; }
        [DataSourceProperty] public string BalanceText { get; }
        public string CouncilText { get; }
        public string DecisionText { get; }
        public string SummaryText => CouncilText + "\n" + DecisionText;
        public string RulerReason { get; }
        public int RulerOverrideCommitment { get; }

        public TreatyCouncilSummaryVM(Kingdom kingdom, TreatyCouncilEvaluation council,
            bool forced, bool accepts, bool usesOverride)
        {
            YayInfluence = council?.YayInfluence ?? 0;
            TotalInfluence = council?.Participation ?? 0;
            OppositionColor = Color.ConvertStringToColor(TotalInfluence > 0 ? "#B94040FF" : "#555555FF");
            int yayPercent = TotalInfluence == 0 ? 0
                : (int)Math.Round(100d * YayInfluence / TotalInfluence, MidpointRounding.AwayFromZero);
            BalanceText = TotalInfluence > 0
                ? new TextObject("{=BC_Parley_VoteBalance}{YAY}% YAY / {NAY}% NAY")
                    .SetTextVariable("YAY", yayPercent).SetTextVariable("NAY", 100 - yayPercent).ToString()
                : new TextObject(council?.IsSoleRulerDecision == true
                    ? "{=BC_Parley_RulerDecisionBalance}Ruler decision"
                    : "{=BC_Parley_UncommittedBalance}Uncommitted").ToString();

            bool actualOverride = council != null && !council.IsBudgetBlocked && !forced
                && !council.IsSoleRulerDecision && TotalInfluence > 0 && usesOverride;
            RulerOverrideCommitment = actualOverride
                ? Math.Max(0, accepts ? council.OverrideCost : council.RejectionOverrideCost) : 0;
            CouncilText = BuildCouncilText(kingdom, council);
            DecisionText = BuildDecisionText(kingdom, council, forced, accepts, actualOverride,
                RulerOverrideCommitment);
            RulerReason = BuildRulerReason(kingdom, council, forced, accepts, actualOverride);
            _tooltip = new BasicTooltipViewModel(() => new List<TooltipProperty>
            {
                new TooltipProperty(CouncilText, string.Empty, 0),
                new TooltipProperty(DecisionText, string.Empty, 0)
            });
        }

        public void ExecuteBeginHint()
        {
            _isHintVisible = true;
            _tooltip.ExecuteBeginHint();
        }

        public void ExecuteEndHint()
        {
            if (!_isHintVisible) return;
            _isHintVisible = false;
            _tooltip.ExecuteEndHint();
        }

        private static string BuildCouncilText(Kingdom kingdom, TreatyCouncilEvaluation council)
        {
            if (council == null)
                return new TextObject("{=BC_Parley_CouncilUnavailable}The council's decision is unavailable.").ToString();
            string template;
            if (council.IsSoleRulerDecision)
                template = "{=BC_Parley_NoVassalCouncil}There is no vassal council in {REALM}.";
            else if (council.Participation == 0)
                template = "{=BC_Parley_CouncilNoCommitments}The council of {REALM} has committed no influence.";
            else if (council.YayInfluence > council.NayInfluence)
                template = "{=BC_Parley_CouncilSupportsTally}The council of {REALM} supports this treaty, with {YAY} influence committed YAY against {NAY} committed NAY.";
            else if (council.NayInfluence > council.YayInfluence)
                template = "{=BC_Parley_CouncilOpposesTally}The council of {REALM} opposes this treaty, with {NAY} influence committed NAY against {YAY} committed YAY.";
            else
                template = "{=BC_Parley_CouncilTiedTally}The council of {REALM} is divided, with {NAY} influence committed NAY against {YAY} committed YAY.";
            return new TextObject(template)
                .SetTextVariable("REALM", kingdom?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("YAY", council.YayInfluence)
                .SetTextVariable("NAY", council.NayInfluence).ToString();
        }

        private static string BuildDecisionText(Kingdom kingdom, TreatyCouncilEvaluation council,
            bool forced, bool accepts, bool usesOverride, int overrideCost)
        {
            if (council == null) return string.Empty;
            if (council.IsBudgetBlocked)
                return new TextObject("{=BC_Parley_BudgetBlocksDecision}The draft exceeds the available war score and cannot be ratified.").ToString();
            if (forced)
                return new TextObject("{=BC_Parley_CompulsoryDecision}Peace is compulsory; {REALM} must accept these terms.")
                    .SetTextVariable("REALM", kingdom?.Name ?? TextObject.GetEmpty()).ToString();
            if (kingdom?.RulingClan?.Leader == null)
                return new TextObject("{=BC_Parley_NoRulerDecision}No ruler is available to approve this treaty.").ToString();
            string template = usesOverride
                ? accepts
                    ? "{=BC_Parley_RulerWouldOverrideAccept}{RULER} would approve the treaty with {COST} additional influence to overrule the council."
                    : "{=BC_Parley_RulerWouldOverrideReject}{RULER} would reject the treaty with {COST} additional influence to overrule the council."
                : accepts
                    ? "{=BC_Parley_RulerWouldAccept}{RULER} would approve the treaty."
                    : "{=BC_Parley_RulerWouldReject}{RULER} would reject the treaty.";
            return new TextObject(template).SetTextVariable("RULER", kingdom.RulingClan.Leader.Name)
                .SetTextVariable("COST", overrideCost).ToString();
        }

        private static string BuildRulerReason(Kingdom kingdom, TreatyCouncilEvaluation council,
            bool forced, bool accepts, bool usesOverride)
        {
            Clan ruler = kingdom?.RulingClan;
            TreatyCouncilMemberEvaluation position = council?.Members.FirstOrDefault(member => member?.Clan == ruler);
            if (ruler == null || position == null || council.IsBudgetBlocked || forced
                || council.IsSoleRulerDecision || council.Participation == 0)
                return string.Empty;
            if (usesOverride)
                return new TextObject(accepts
                    ? "{=BC_Parley_RulerOverridesForPeace}I will spend my influence to secure this peace despite my vassals."
                    : "{=BC_Parley_RulerOverridesAgainstPeace}I will spend my influence to reject these terms despite my vassals.").ToString();

            // Match the affordability policy used by the decision, including protected reserves.
            if (council.IsRatified && accepts && position.Stance == TreatyCouncilVoteStance.Nay)
            {
                bool canOppose = NpcInfluenceBudgetService.CanAfford(ruler,
                    Math.Max(0, council.RejectionOverrideCost) + Math.Max(0, position.InfluenceCommitment),
                    NpcInfluenceExpenseKind.CouncilCommitment);
                return new TextObject(!canOppose && ruler != Clan.PlayerClan
                    ? "{=BC_Parley_RulerLacksInfluenceToOppose}I lack the influence to oppose my vassals."
                    : "{=BC_Parley_RulerYieldsToCouncil}I will not defy my vassals over these terms.").ToString();
            }
            if (!council.IsRatified && !accepts && position.Stance == TreatyCouncilVoteStance.Yay
                && !NpcInfluenceBudgetService.CanAfford(ruler,
                    Math.Max(0, council.OverrideCost) + Math.Max(0, position.InfluenceCommitment),
                    NpcInfluenceExpenseKind.CouncilCommitment))
                return new TextObject("{=BC_Parley_RulerLacksInfluenceToApprove}I lack the influence to secure this peace against my vassals.").ToString();
            return string.Empty;
        }
    }
}
