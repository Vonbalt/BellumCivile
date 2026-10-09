using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed partial class PeaceParleyVM
    {
        [DataSourceProperty] public bool HasHostagePact => _proposal?.Terms.Any(t => t.Type == TreatyTermType.HostagePeace) == true;
        [DataSourceProperty] public int HostageControlsHeight => HasHostagePact ? 48 : 0;
        [DataSourceProperty] public string HostageDurationText => new TextObject("{=BC_Parley_PactDuration}Pact duration: {DAYS} days")
            .SetTextVariable("DAYS", TreatyHostageTerms.DurationForDraft(_proposal?.Terms)).ToString();
        [DataSourceProperty] public bool IsHostageDecreaseDisabled => IsDraftEditingDisabled || !HasHostagePact
            || TreatyHostageTerms.DurationForDraft(_proposal.Terms) <= HostagePactRules.MinimumNegotiatedDurationDays;
        [DataSourceProperty] public bool IsHostageIncreaseDisabled => IsDraftEditingDisabled || !HasHostagePact
            || TreatyHostageTerms.DurationForDraft(_proposal.Terms) >= HostagePactRules.MaximumDurationDays;

        public void ExecuteDecreaseHostageDuration() => AdjustHostageDuration(false);
        public void ExecuteIncreaseHostageDuration() => AdjustHostageDuration(true);

        private void AdjustHostageDuration(bool increase)
        {
            if (increase ? IsHostageIncreaseDisabled : IsHostageDecreaseDisabled) return;
            int days = TreatyHostageTerms.DurationForDraft(_proposal.Terms);
            int step = HostagePactRules.DurationStepDays;
            int next = increase ? (days / step + 1) * step : ((days + step - 1) / step - 1) * step;
            next = Math.Max(HostagePactRules.MinimumNegotiatedDurationDays, Math.Min(HostagePactRules.MaximumDurationDays, next));
            ReplaceDraft(TreatyHostageTerms.WithDuration(GetEditableDemandTerms(), next));
        }

        private void RefreshHostageControls()
        {
            OnPropertyChanged(nameof(HasHostagePact));
            OnPropertyChanged(nameof(HostageControlsHeight));
            OnPropertyChanged(nameof(HostageDurationText));
            OnPropertyChanged(nameof(IsHostageDecreaseDisabled));
            OnPropertyChanged(nameof(IsHostageIncreaseDisabled));
        }

        private void AddHostageOption(Kingdom supplier, Kingdom receiver)
        {
            var existing = _proposal.Terms.FirstOrDefault(t => t.Type == TreatyTermType.HostagePeace && t.FromKingdomId == supplier.StringId);
            int durationDays = TreatyHostageTerms.DurationForDraft(_proposal.Terms);
            bool durationPriced = !_proposal.Terms.Any(t => t.Type == TreatyTermType.HostagePeace && !t.HostageDurationPriced);
            var candidates = TreatyHostageTerms.Available(supplier, receiver, durationDays);
            if (existing == null && candidates.Count == 0) return;
            if (supplier.StringId == _proposal.WinnerKingdomId && !IsOfferingsMode) return;
            var hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == existing?.HeroId);
            var label = existing == null ? new TextObject("{=BC_Parley_HostageOption}Secure peace with a royal hostage")
                : new TextObject("{=BC_Parley_HostageSelected}Hostage for peace: {HERO}").SetTextVariable("HERO", hero?.Name ?? TextObject.GetEmpty());
            AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM("hostage_peace", label.ToString(),
                existing?.WarScoreCost ?? candidates.Min(c => durationPriced ? HostagePactRules.GetNegotiatedCost(c.Tier, durationDays) : c.Cost), existing != null, IsDraftEditingDisabled,
                ToggleHostageTerm, new TextObject("{=BC_Parley_HostageHint}A blood relative of the ruler remains in the other house's custody for {DAYS} days, securing the peace. Ordinary ransom and escape cannot end the pledge. Breaking it may cost the hostage their life. Select a relative to review the cost; select an existing pledge to remove it.")
                    .SetTextVariable("DAYS", durationDays)));
        }

        private void ToggleHostageTerm(string key)
        {
            if (key != "hostage_peace" || IsDraftEditingDisabled || _proposal == null
                || !TryResolveCurrentDirection(out Kingdom supplier, out Kingdom receiver)) return;
            var terms = GetEditableDemandTerms();
            var existing = terms.FirstOrDefault(t => t.Type == TreatyTermType.HostagePeace && t.FromKingdomId == supplier.StringId);
            if (existing != null) { terms.Remove(existing); ReplaceDraft(terms); return; }
            bool offering = IsOfferingsMode;
            if (supplier.StringId == _proposal.WinnerKingdomId && !offering) return;
            var proposal = _proposal;
            int draftRevision = proposal.DraftRevision;
            int durationDays = TreatyHostageTerms.DurationForDraft(terms);
            bool durationPriced = !terms.Any(t => t.Type == TreatyTermType.HostagePeace && !t.HostageDurationPriced);
            var candidates = TreatyHostageTerms.Available(supplier, receiver, durationDays);
            if (candidates.Count == 0) return;
            var choices = candidates.Select(c => new InquiryElement(c.Hero.StringId,
                new TextObject("{=BC_Parley_HostageCandidate}{HERO} - {COST} WS (tier {TIER})")
                    .SetTextVariable("HERO", c.Hero.Name).SetTextVariable("COST", durationPriced
                        ? HostagePactRules.GetNegotiatedCost(c.Tier, durationDays) : c.Cost).SetTextVariable("TIER", c.Tier).ToString(), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Parley_HostageTitle}A Pledge of Peace").ToString(),
                new TextObject("{=BC_Parley_HostageChoose}Which relative shall stand hostage for {DAYS} days? They remain a member of their own house. A voluntary offering earns half its value in treaty credit, subject to the shared offering limit.")
                    .SetTextVariable("DAYS", durationDays).ToString(),
                choices, true, 1, 1, new TextObject("{=BC_UI_Continue}Continue").ToString(), new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    if (_proposal != proposal || proposal.DraftRevision != draftRevision || IsDraftEditingDisabled) return;
                    var candidate = TreatyHostageTerms.Available(supplier, receiver, durationDays)
                        .FirstOrDefault(c => c.Hero.StringId == selected?.FirstOrDefault()?.Identifier as string);
                    if (candidate == null)
                    {
                        DisplayActionFailure("the selected relative is no longer available as a hostage");
                        return;
                    }
                    var updated = GetEditableDemandTerms();
                    updated.RemoveAll(t => t.Type == TreatyTermType.HostagePeace && t.FromKingdomId == supplier.StringId);
                    updated.Add(TreatyHostageTerms.Create(supplier, receiver, candidate, offering, durationDays, durationPriced));
                    ReplaceDraft(updated);
                }, null), true);
        }
    }
}
