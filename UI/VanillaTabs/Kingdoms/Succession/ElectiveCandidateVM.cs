using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Succession
{
    public sealed class ElectiveCandidateVM : ViewModel
    {
        private readonly ElectiveSuccessionRecord _record;
        private readonly Hero _candidate;
        private readonly Action _refresh;
        public ElectiveCandidateVM(ElectiveSuccessionRecord record, Hero candidate, Action refresh)
        {
            _record = record; _candidate = candidate; _refresh = refresh;
            Person = new SuccessionHeirVM(candidate, 1, record.Sovereign);
            SupportHint = new BasicTooltipViewModel(BuildCandidateTooltip);
        }
        [DataSourceProperty] public SuccessionHeirVM Person { get; }
        [DataSourceProperty] public string ClanName => _candidate.Clan?.Name?.ToString() ?? string.Empty;
        [DataSourceProperty] public bool IsSelected => _record.Votes.Any(v => v.Clan == Clan.PlayerClan && v.Nominee == _candidate);
        [DataSourceProperty] public string CheckboxBrush => IsSelected ? "SPOptions.Checkbox.Full.Button" : "SPOptions.Checkbox.Empty.Button";
        [DataSourceProperty] public bool CanSelect => !_record.Completed && (!_record.Frozen || !_record.PlayerConfirmed)
            && _record.Votes.Any(v => v.Clan == Clan.PlayerClan);
        [DataSourceProperty] public BasicTooltipViewModel SupportHint { get; }
        private ElectiveAcceptanceAssessment Acceptance => ElectiveSuccessionBehavior.Instance?.Acceptance.Get(_record, _candidate);
        [DataSourceProperty] public string AcceptanceText
        {
            get
            {
                var score = Acceptance;
                return score == null ? new TextObject("{=BC_Succession_AcceptancePlaceholder}Acceptance: --").ToString()
                    : new TextObject("{=BC_Acceptance_Value}Acceptance: {VALUE}%")
                        .SetTextVariable("VALUE", score.Total.ToString("0.#")).ToString();
            }
        }
        [DataSourceProperty] public string SupportText
        {
            get
            {
                var text = new TextObject("{=BC_Election_SupportShare}Support: {SHARE}%");
                text.SetTextVariable("SHARE", (_record.TotalWeight > 0 ? 100 * _record.Support(_candidate) / _record.TotalWeight : 0).ToString("0.0"));
                return text.ToString();
            }
        }
        [DataSourceMethod] public void ExecuteOpenHero() => Person.ExecuteOpenHero();
        [DataSourceMethod] public void ExecuteBeginSupportHint() => SupportHint.ExecuteBeginHint();
        [DataSourceMethod] public void ExecuteEndSupportHint() => SupportHint.ExecuteEndHint();
        [DataSourceMethod] public void ExecuteSelect()
        {
            ElectiveSuccessionBehavior.Instance?.SetPlayerSupport(_record.Realm, IsSelected ? null : _candidate);
            _refresh();
        }
        private List<TooltipProperty> BuildCandidateTooltip()
        {
            var rows = new List<TooltipProperty>
            {
                new TooltipProperty(_candidate.Name.ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator),
                // Value-only rows select the native beige description brush.
                new TooltipProperty(string.Empty, new TextObject("{=BC_Election_SupportersSection}Supporters").ToString(), 0)
            };
            foreach (var vote in _record.Votes.Where(v => v.Supported == _candidate))
                rows.Add(new TooltipProperty(vote.Clan.Name.ToString(), vote.Weight.ToString("0.0") + " ("
                    + (_record.TotalWeight > 0 ? 100 * vote.Weight / _record.TotalWeight : 0).ToString("0.0") + "%)", 0));
            if (!_record.Votes.Any(v => v.Supported == _candidate))
                rows.Add(new TooltipProperty(new TextObject("{=BC_Election_NoSupporters}None").ToString(), string.Empty, 0));
            rows.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            rows.Add(new TooltipProperty(string.Empty, new TextObject("{=BC_Election_AcceptanceSection}Acceptance").ToString(), 0));
            var score = Acceptance;
            if (score == null)
            {
                rows.Add(new TooltipProperty(string.Empty, "--", 0));
                return rows;
            }
            if (score.Leading)
            {
                rows.Add(new TooltipProperty(new TextObject("{=BC_Acceptance_Leading}Currently projected to win.").ToString(), "100%", 0));
                return rows;
            }
            AddAcceptanceRow(rows, "{=BC_Acceptance_Baseline}Baseline", score.Baseline);
            AddAcceptanceRow(rows, "{=BC_Acceptance_Honor}Honor", score.Honor);
            AddAcceptanceRow(rows, "{=BC_Acceptance_Mercy}Mercy", score.Mercy);
            if (score.PersonalityCap != 0)
                AddAcceptanceRow(rows, "{=BC_Acceptance_PersonalityCap}Personality limit", score.PersonalityCap);
            AddAcceptanceRow(rows, "{=BC_Acceptance_Relations}Respect for leader", score.Relations);
            AddAcceptanceRow(rows, score.Claim == -15 ? "{=BC_Acceptance_StrongClaim}Strong throne claim"
                : score.Claim == -5 ? "{=BC_Acceptance_WeakClaim}Weak throne claim" : "{=BC_Acceptance_NoClaim}Throne claim", score.Claim);
            AddAcceptanceRow(rows, "{=BC_Acceptance_Margin}Margin of defeat", score.Margin);
            AddAcceptanceRow(rows, "{=BC_Acceptance_Military}Military prospects", score.Military);
            rows.Add(new TooltipProperty(new TextObject("{=BC_Acceptance_Total}Acceptance of defeat").ToString(), score.Total.ToString("0.#") + "%", 0));
            rows.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            rows.Add(new TooltipProperty(new TextObject("{=BC_Acceptance_Backing}Projected backing").ToString(), score.Backing.ToString("0.#"), 0));
            rows.Add(new TooltipProperty(new TextObject("{=BC_Acceptance_Loyalists}Projected loyalists").ToString(), score.Loyalists.ToString("0.#"), 0));
            rows.Add(new TooltipProperty(new TextObject("{=BC_Acceptance_Threshold}Required power ratio").ToString(), (score.Threshold * 100).ToString("0.#") + "%", 0));
            return rows;
        }
        private static void AddAcceptanceRow(List<TooltipProperty> rows, string label, double value) =>
            rows.Add(new TooltipProperty(new TextObject(label).ToString(), value.ToString("+0.##;-0.##;0"), 0));
        public override void OnFinalize() { Person.OnFinalize(); base.OnFinalize(); }
    }
}
