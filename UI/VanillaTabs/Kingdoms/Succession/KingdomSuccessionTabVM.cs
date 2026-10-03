using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Succession
{
    public sealed class KingdomSuccessionPolicyVM : ViewModel
    {
        private readonly Kingdom _kingdom;
        private readonly SuccessionLawBehavior _behavior;
        private readonly List<Hero> _additionalHeirs = new List<Hero>();
        private readonly List<SuccessionPretender> _additionalPretenders = new List<SuccessionPretender>();
        private bool _hasAdditionalHeirs;
        private SuccessionHeirVM _crownHeir;
        private string _additionalHeirsText;

        public KingdomSuccessionPolicyVM(Kingdom kingdom)
        {
            _kingdom = kingdom;
            _behavior = Campaign.Current?.GetCampaignBehavior<SuccessionLawBehavior>();
            NextHeirs = new MBBindingList<SuccessionHeirVM>();
            ElectionCandidates = new MBBindingList<ElectiveCandidateVM>();
            GenderLaws = new MBBindingList<SuccessionLawOptionVM>();
            HereditaryLaws = new MBBindingList<SuccessionLawOptionVM>();
            ElectiveLaws = new MBBindingList<SuccessionLawOptionVM>();
            ElectiveTerms = new MBBindingList<SuccessionLawOptionVM>();
            AdditionalHeirsHint = new BasicTooltipViewModel(BuildAdditionalHeirsTooltip);
            RefreshValues();
        }

        [DataSourceProperty] public string CrownHeirLabel { get; private set; }
        [DataSourceProperty] public string LineOfSuccessionLabel => new TextObject("{=BC_SuccessionPanel_PretenderLine}Line of Succession and Pretenders").ToString();
        [DataSourceProperty] public int NextHeirsWidth => 3 * 228;
        [DataSourceProperty] public bool HasNextHeirs => NextHeirs.Count > 0;
        [DataSourceProperty] public string GenderLawsLabel { get; private set; }
        [DataSourceProperty] public string HereditaryLawsLabel => new TextObject("{=BC_SuccessionPanel_Hereditary}Hereditary Succession").ToString();
        [DataSourceProperty] public string ElectiveLawsLabel => new TextObject("{=BC_SuccessionPanel_Elective}Elective Succession").ToString();
        [DataSourceProperty] public string ElectiveTermsLabel => new TextObject("{=BC_SuccessionPanel_Terms}Elective Terms").ToString();
        [DataSourceProperty] public string ElectionHeading => new TextObject("{=BC_SuccessionPanel_ElectionHeading}Election Standings").ToString();
        [DataSourceProperty] public string ElectionDate => ElectiveSuccessionBehavior.Instance?.MandateLabel(_kingdom) ?? string.Empty;
        [DataSourceProperty] public string RegencyText { get; private set; }
        [DataSourceProperty] public bool HasRegency => !string.IsNullOrEmpty(RegencyText);
        [DataSourceProperty] public bool IsHereditary { get; private set; }
        [DataSourceProperty] public bool IsElective { get; private set; }
        [DataSourceProperty] public bool HasNoHeir => CrownHeir == null;
        [DataSourceProperty] public string NoHeirText => new TextObject("{=BC_SuccessionLine_NoValidHeir}No valid heir").ToString();
        [DataSourceProperty] public string ReformCostText { get; private set; }
        [DataSourceProperty] public MBBindingList<SuccessionHeirVM> NextHeirs { get; }
        [DataSourceProperty] public MBBindingList<ElectiveCandidateVM> ElectionCandidates { get; }
        [DataSourceProperty] public MBBindingList<SuccessionLawOptionVM> GenderLaws { get; }
        [DataSourceProperty] public MBBindingList<SuccessionLawOptionVM> HereditaryLaws { get; }
        [DataSourceProperty] public MBBindingList<SuccessionLawOptionVM> ElectiveLaws { get; }
        [DataSourceProperty] public MBBindingList<SuccessionLawOptionVM> ElectiveTerms { get; }
        [DataSourceProperty] public BasicTooltipViewModel AdditionalHeirsHint { get; }

        [DataSourceProperty]
        public bool HasAdditionalHeirs
        {
            get => _hasAdditionalHeirs;
            private set
            {
                if (_hasAdditionalHeirs == value) return;
                _hasAdditionalHeirs = value;
                OnPropertyChangedWithValue(value, nameof(HasAdditionalHeirs));
            }
        }

        [DataSourceProperty]
        public SuccessionHeirVM CrownHeir
        {
            get => _crownHeir;
            private set
            {
                if (_crownHeir == value) return;
                _crownHeir = value;
                OnPropertyChangedWithValue(value, nameof(CrownHeir));
            }
        }

        [DataSourceProperty]
        public string AdditionalHeirsText
        {
            get => _additionalHeirsText;
            private set
            {
                if (_additionalHeirsText == value) return;
                _additionalHeirsText = value;
                OnPropertyChangedWithValue(value, nameof(AdditionalHeirsText));
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            CrownHeirLabel = new TextObject("{=BC_SuccessionPanel_LawfulHeir}Crown Heir").ToString();
            GenderLawsLabel = new TextObject("{=BC_GenderLaws_Header}Gender Laws").ToString();
            TextObject cost = new TextObject("{=BC_SuccessionPanel_ReformCost}Changing either law costs {COST} influence and displeases every vassal house.");
            cost.SetTextVariable("COST", SuccessionLawBehavior.LawChangeInfluenceCost);
            ReformCostText = cost.ToString();

            RefreshLaws();
            RefreshHeirs();
            foreach (var candidate in ElectionCandidates) candidate.OnFinalize();
            ElectionCandidates.Clear();
            if (IsElective)
            {
                ElectiveSuccessionBehavior.Instance?.Maintain(_kingdom);
                var election = ElectiveSuccessionBehavior.Instance?.Get(_kingdom);
                if (election != null)
                {
                    foreach (Hero candidate in election.Finalists)
                        ElectionCandidates.Add(new ElectiveCandidateVM(election, candidate, RefreshValues));
                }
            }
            var regency = Campaign.Current?.GetCampaignBehavior<RegencyBehavior>();
            Hero ward = regency?.GetWard(_kingdom?.RulingClan);
            Hero regent = regency?.GetRegent(_kingdom?.RulingClan);
            RegencyText = string.Empty;
            if (IsHereditary && ward?.IsAlive == true && regent?.IsAlive == true)
            {
                var text = new TextObject("{=BC_SuccessionPanel_Regency}Regent: {REGENT} | Sovereign: {WARD}");
                text.SetTextVariable("REGENT", regent.Name);
                text.SetTextVariable("WARD", ward.Name);
                RegencyText = text.ToString();
            }
            OnPropertyChanged(nameof(CrownHeirLabel));
            OnPropertyChanged(nameof(GenderLawsLabel));
            OnPropertyChanged(nameof(HereditaryLawsLabel));
            OnPropertyChanged(nameof(ElectiveLawsLabel));
            OnPropertyChanged(nameof(ElectiveTermsLabel));
            OnPropertyChanged(nameof(LineOfSuccessionLabel));
            OnPropertyChanged(nameof(ElectionHeading));
            OnPropertyChanged(nameof(ElectionDate));
            OnPropertyChanged(nameof(RegencyText));
            OnPropertyChanged(nameof(HasRegency));
            OnPropertyChanged(nameof(IsHereditary));
            OnPropertyChanged(nameof(IsElective));
            OnPropertyChanged(nameof(HasNoHeir));
            OnPropertyChanged(nameof(NoHeirText));
            OnPropertyChanged(nameof(ReformCostText));
        }

        public override void OnFinalize()
        {
            foreach (var candidate in ElectionCandidates) candidate.OnFinalize();
            CrownHeir?.OnFinalize();
            foreach (SuccessionHeirVM heir in NextHeirs)
                heir?.OnFinalize();
            base.OnFinalize();
        }

        private void RefreshHeirs()
        {
            CrownHeir?.OnFinalize();
            foreach (SuccessionHeirVM heir in NextHeirs)
                heir?.OnFinalize();
            NextHeirs.Clear();

            List<Hero> line = IsHereditary ? BuildCrownSuccessionLine() : new List<Hero>();
            CrownHeir = line.Count > 0 ? new SuccessionHeirVM(line[0], 1, _kingdom?.Leader, _kingdom) : null;
            foreach (Hero hero in line.Skip(1).Take(3))
                NextHeirs.Add(new SuccessionHeirVM(hero, NextHeirs.Count + 2, _kingdom?.Leader, _kingdom));

            var pretenders = IsHereditary ? SuccessionPretender.Get(_kingdom, line) : new List<SuccessionPretender>();
            int visiblePretenders = Math.Max(0, 3 - NextHeirs.Count);
            foreach (var pretender in pretenders.Take(visiblePretenders))
                NextHeirs.Add(new SuccessionHeirVM(pretender.Hero, 0, _kingdom?.Leader, null, pretender.Strength));

            OnPropertyChanged(nameof(NextHeirsWidth));
            OnPropertyChanged(nameof(HasNextHeirs));

            _additionalHeirs.Clear();
            _additionalHeirs.AddRange(line.Skip(4));
            _additionalPretenders.Clear();
            _additionalPretenders.AddRange(pretenders.Skip(visiblePretenders));
            int remaining = _additionalHeirs.Count + _additionalPretenders.Count;
            HasAdditionalHeirs = remaining > 0;
            AdditionalHeirsText = HasAdditionalHeirs ? "+" + remaining : string.Empty;
        }

        private List<TooltipProperty> BuildAdditionalHeirsTooltip()
        {
            var rows = _additionalHeirs.Count > 0
                ? SuccessionLawHelper.BuildSuccessionRemainderTooltipProperties(_additionalHeirs, 5)
                : new List<TooltipProperty>
                {
                    new TooltipProperty(LineOfSuccessionLabel, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title)
                };
            foreach (var pretender in _additionalPretenders)
                rows.Add(new TooltipProperty(SuccessionPretender.Label(pretender.Strength), pretender.Hero.Name.ToString(), 0));
            return rows;
        }

        private List<Hero> BuildCrownSuccessionLine()
        {
            if (CrownAccessionBehavior.IsHereditaryRealm(_kingdom))
                return HereditaryLoyaltyBehavior.Instance?.GetLine(_kingdom).ToList() ?? HereditaryRealmSuccession.GetLine(_kingdom);
            DynasticHeirBehavior dynastic = Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>();
            RegencyBehavior regency = Campaign.Current?.GetCampaignBehavior<RegencyBehavior>();
            Hero crownHeir = dynastic?.GetDynasticSuccessionCandidate(_kingdom)
                ?? dynastic?.GetDynasticHeir(_kingdom);
            Clan dynasty = dynastic?.GetRightfulDynastyClan(_kingdom)
                ?? _kingdom?.RulingClan
                ?? crownHeir?.Clan;
            Hero ward = regency?.GetWard(dynasty);
            Hero successionRoot = ward ?? dynasty?.Leader;
            List<Hero> line = successionRoot == null
                ? new List<Hero>()
                : SuccessionLawHelper.GetLegalSuccessionLine(
                    dynasty,
                    successionRoot,
                    SuccessionLawHelper.GetLawsForClan(dynasty));
            // A ward already reigning through a regent is the sovereign, not their own heir.
            if (ward != null && ward.IsAlive && dynasty != _kingdom?.RulingClan)
                line.Insert(0, ward);
            // The ordered line is authoritative within the rightful house. Only inject an
            // external claim carrier, such as a recognized cadet branch or marriage claimant.
            if (ward == null && crownHeir != null && crownHeir.Clan != dynasty)
            {
                line.Remove(crownHeir);
                line.Insert(0, crownHeir);
            }
            return line.Where(hero => hero != null && hero.IsAlive).Distinct().ToList();
        }

        private void RefreshLaws()
        {
            GenderLaws.Clear();
            HereditaryLaws.Clear();
            ElectiveLaws.Clear();
            ElectiveTerms.Clear();
            SuccessionLawSet current = _behavior?.GetLawsForKingdom(_kingdom, out _)
                ?? SuccessionLawHelper.GetLawsForKingdom(_kingdom);
            IsHereditary = SuccessionRealmRules.Classify(current.SuccessionLaw) == RealmSuccessionSystem.Hereditary;
            IsElective = SuccessionRealmRules.Classify(current.SuccessionLaw) == RealmSuccessionSystem.Elective;
            TextObject failure = null;
            bool canChange = _behavior?.CanPlayerChangeLaws(_kingdom, out failure) == true;
            failure = failure ?? new TextObject("{=BC_SuccessionLaw_Unavailable}Succession law cannot be altered at this time.");

            var registry = RealmLawRegistry.Instance;
            var groups = Campaign.Current.GetCampaignBehavior<RealmLawBehavior>();
            foreach (var definition in registry.InGroup(RealmLawRegistry.GenderGroup))
                GenderLaws.Add(new SuccessionLawOptionVM(definition.Name, definition.Description,
                    groups.GetActiveLawId(_kingdom, definition.GroupId) == definition.Id,
                    canChange, failure, () => ConfirmLawDefinitionChange(definition)));

            foreach (var definition in registry.InGroup(RealmLawRegistry.SuccessionGroup))
            {
                var list = definition.Category == "hereditary" ? HereditaryLaws : ElectiveLaws;
                list.Add(new SuccessionLawOptionVM(definition.Name, definition.Description,
                    groups.GetActiveLawId(_kingdom, definition.GroupId) == definition.Id,
                    canChange, failure, () => ConfirmLawDefinitionChange(definition)));
            }

            var termHint = new TextObject("{=BC_SuccessionPanel_TermsHereditary}Elective mandates do not apply to hereditary succession.");
            foreach (var definition in registry.InGroup(RealmLawRegistry.TermGroup))
            {
                ElectiveTerms.Add(new SuccessionLawOptionVM(definition.Name, definition.Description,
                    groups.GetActiveLawId(_kingdom, definition.GroupId) == definition.Id,
                    canChange && IsElective, IsElective ? failure : termHint, () => ConfirmLawDefinitionChange(definition)));
            }
        }

        private void ConfirmLawDefinitionChange(RealmLawDefinition definition)
        {
            var groups = Campaign.Current.GetCampaignBehavior<RealmLawBehavior>();
            string expected = groups.GetActiveLawId(_kingdom, definition.GroupId);
            ConfirmLawChange(definition.Name, () =>
            {
                bool changed = groups.TryApplyPlayerLaw(_kingdom, definition.Id, expected, out TextObject failure);
                if (!changed) InformationManager.DisplayMessage(new InformationMessage(failure.ToString()));
                return changed;
            });
        }

        private void ConfirmLawChange(TextObject lawName, Func<bool> apply)
        {
            TextObject failure = null;
            if (_behavior == null || !_behavior.CanPlayerChangeLaws(_kingdom, out failure))
            {
                InformationManager.DisplayMessage(new InformationMessage(failure?.ToString() ?? string.Empty));
                return;
            }

            TextObject description = new TextObject("{=BC_SuccessionLaw_Confirm}Proclaim {LAW_NAME} throughout the realm? This costs {COST} influence and every vassal house will lose {RELATION} relation with the ruling house.");
            description.SetTextVariable("LAW_NAME", lawName);
            description.SetTextVariable("COST", SuccessionLawBehavior.LawChangeInfluenceCost);
            description.SetTextVariable("RELATION", Math.Abs(SuccessionLawBehavior.LawChangeVassalRelationPenalty));
            InquiryData inquiry = new InquiryData(
                new TextObject("{=BC_SuccessionLaw_ConfirmTitle}Reform Succession").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_SuccessionLaw_Enact}Enact").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                () =>
                {
                    if (apply()) RefreshValues();
                },
                null);
            InformationManager.ShowInquiry(inquiry, true);
        }
    }

    public sealed class SuccessionHeirVM : ViewModel
    {
        private readonly Hero _hero;
        private readonly Kingdom _loyaltyRealm;

        public SuccessionHeirVM(Hero hero, int position, Hero ruler, Kingdom loyaltyRealm = null, FeudalClaimStrength? pretender = null)
        {
            _hero = hero;
            _loyaltyRealm = pretender.HasValue ? null : loyaltyRealm;
            Name = hero?.Name?.ToString() ?? string.Empty;
            CrownHeirLabel = new TextObject("{=BC_SuccessionPanel_LawfulHeir}Crown Heir").ToString();
            TextObject positionText = new TextObject("{=BC_SuccessionLine_KinPosition}{POSITION} {KINSHIP}");
            positionText.SetTextVariable("POSITION", GetOrdinal(position));
            positionText.SetTextVariable("KINSHIP", GetKinship(hero, ruler));
            PositionText = pretender.HasValue ? SuccessionPretender.Label(pretender.Value) : positionText.ToString();
            if (hero?.CharacterObject != null)
            {
                Portrait = new CharacterImageIdentifierVM(
                    BellumCivile.UI.PortraitAppearance.Create(hero.CharacterObject));
                if (hero.Clan?.Banner != null)
                    BannerVisual = new BannerImageIdentifierVM(hero.Clan.Banner, true);
            }
            Hint = new BasicTooltipViewModel(BuildTooltip);
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string CrownHeirLabel { get; }
        [DataSourceProperty] public bool HasHero => _hero != null;
        [DataSourceProperty] public string PositionText { get; }
        [DataSourceProperty] public string LoyaltyLabel => new TextObject("{=BC_Loyalty_Prefix}Loyalty:").ToString();
        private bool IsPlayerHeir => _hero != null && _hero == Hero.MainHero;
        [DataSourceProperty] public string LoyaltyText
        {
            get
            {
                if (IsPlayerHeir) return "--";
                var assessment = HereditaryLoyaltyBehavior.Instance?.Get(_loyaltyRealm, _hero);
                return assessment == null ? "--" : SuccessionDisposition.Value(assessment.Total);
            }
        }
        [DataSourceProperty] public Color LoyaltyColor => SuccessionDisposition.Color(
            IsPlayerHeir ? (double?)null : HereditaryLoyaltyBehavior.Instance?.Get(_loyaltyRealm, _hero)?.Total);
        [DataSourceProperty] public CharacterImageIdentifierVM Portrait { get; }
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get; }
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }

        [DataSourceMethod]
        public void ExecuteOpenHero()
        {
            if (_hero != null)
                Campaign.Current?.EncyclopediaManager?.GoToLink(_hero.EncyclopediaLink);
        }

        private List<TooltipProperty> BuildTooltip()
        {
            var rows = new List<TooltipProperty>
            {
                new TooltipProperty(Name, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator),
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_Position}Position").ToString(), PositionText, 0),
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_Age}Age").ToString(), Math.Max(0, (int)(_hero?.Age ?? 0f)).ToString(), 0),
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_House}House").ToString(), _hero?.Clan?.Name?.ToString() ?? string.Empty, 0)
            };
            if (IsPlayerHeir && _loyaltyRealm != null)
            {
                var ruler = RegencyBehavior.Instance?.GetLegalClanHead(_loyaltyRealm.RulingClan) ?? _loyaltyRealm.Leader;
                rows.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
                rows.Add(new TooltipProperty(new TextObject("{=BC_Loyalty_Sovereign}Loyalty to").ToString(), ruler?.Name?.ToString() ?? string.Empty, 0));
                rows.Add(new TooltipProperty(new TextObject("{=BC_Loyalty_PlayerReasons}Only you know your own reasons.").ToString(), string.Empty, 0));
                return rows;
            }
            var loyalty = HereditaryLoyaltyBehavior.Instance?.Get(_loyaltyRealm, _hero);
            if (loyalty == null) return rows;
            rows.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            var sovereign = RegencyBehavior.Instance?.GetLegalClanHead(_loyaltyRealm.RulingClan) ?? _loyaltyRealm.Leader;
            rows.Add(new TooltipProperty(new TextObject("{=BC_Loyalty_Sovereign}Loyalty to").ToString(), sovereign?.Name?.ToString() ?? string.Empty, 0));
            AddLoyaltyRow(rows, "{=BC_Loyalty_Baseline}Baseline", 80);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Honor}Honor", loyalty.Honor);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Mercy}Mercy", loyalty.Mercy);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Relations}Affinity", loyalty.Relations);
            if (loyalty.Concession != 0) AddLoyaltyRow(rows, "{=BC_Loyalty_Concession}Demand satisfied", loyalty.Concession);
            if (loyalty.Submission != 0) AddLoyaltyRow(rows, "{=BC_Loyalty_Submission}Defeated rebellion", loyalty.Submission);
            if (loyalty.PersonalityCap != 0) AddLoyaltyRow(rows, "{=BC_Loyalty_PersonalityCap}Personality limit", loyalty.PersonalityCap);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Position}Succession position", loyalty.Position);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Reign}Length of reign", loyalty.Reign);
            AddLoyaltyRow(rows, "{=BC_Loyalty_Shock}Succession shock", loyalty.Shock);
            AddLoyaltyRow(rows, loyalty.MinorityRegency ? "{=BC_Loyalty_MinorControversy}Ruler Controversy (minority regency x2)"
                : "{=BC_Loyalty_Controversy}Ruler Controversy", loyalty.Controversy);
            if (loyalty.InheritanceStatus != "none") AddLoyaltyRow(rows, loyalty.InheritanceStatus == "waiting"
                ? "{=BC_Loyalty_Waiting}Awaiting landed inheritance" : "{=BC_Loyalty_Excluded}Excluded from landed inheritance", loyalty.Inheritance);
            if (loyalty.TestAdjustment != 0) AddLoyaltyRow(rows, "{=BC_Loyalty_Test}Testing override", loyalty.TestAdjustment);
            rows.Add(new TooltipProperty(new TextObject("{=BC_Loyalty_Disposition}Loyalty").ToString(),
                SuccessionDisposition.Value(loyalty.Total), 0));
            return rows;
        }

        private static void AddLoyaltyRow(List<TooltipProperty> rows, string label, double value) =>
            rows.Add(new TooltipProperty(new TextObject(label).ToString(), value.ToString("+0.##;-0.##;0"), 0));

        private static TextObject GetKinship(Hero hero, Hero ruler)
        {
            if (hero != null && ruler != null)
            {
                var ancestors = new HashSet<Hero> { hero };
                var visited = new HashSet<Hero> { hero };
                for (int generation = 1; generation <= 3; generation++)
                {
                    var parents = new HashSet<Hero>();
                    foreach (Hero descendant in ancestors)
                    {
                        if (descendant.Father != null) parents.Add(descendant.Father);
                        if (descendant.Mother != null) parents.Add(descendant.Mother);
                    }
                    if (parents.Contains(ruler))
                    {
                        if (generation == 1) return hero.IsFemale ? new TextObject("{=BC_SuccessionKin_Daughter}daughter") : new TextObject("{=BC_SuccessionKin_Son}son");
                        if (generation == 2) return hero.IsFemale ? new TextObject("{=BC_SuccessionKin_Granddaughter}granddaughter") : new TextObject("{=BC_SuccessionKin_Grandson}grandson");
                        return hero.IsFemale ? new TextObject("{=BC_SuccessionKin_GreatGranddaughter}great-granddaughter") : new TextObject("{=BC_SuccessionKin_GreatGrandson}great-grandson");
                    }
                    parents.RemoveWhere(parent => !visited.Add(parent));
                    ancestors = parents;
                }
                if (hero == ruler.Father) return new TextObject("{=BC_SuccessionKin_Father}father");
                if (hero == ruler.Mother) return new TextObject("{=BC_SuccessionKin_Mother}mother");
                if (hero == ruler.Spouse) return new TextObject("{=BC_SuccessionKin_Spouse}spouse");
                if ((hero.Father != null && hero.Father == ruler.Father) || (hero.Mother != null && hero.Mother == ruler.Mother))
                    return hero.IsFemale ? new TextObject("{=BC_SuccessionKin_Sister}sister") : new TextObject("{=BC_SuccessionKin_Brother}brother");
                if (SuccessionLawHelper.IsBloodRelative(hero, ruler)) return new TextObject("{=BC_SuccessionKin_Relative}relative");
            }
            return new TextObject("{=BC_SuccessionKin_Candidate}candidate");
        }

        private static string GetOrdinal(int number)
        {
            int lastTwo = number % 100;
            TextObject suffix;
            if (lastTwo >= 11 && lastTwo <= 13)
                suffix = new TextObject("{=BC_OrdinalSuffix_Th}th");
            else if (number % 10 == 1)
                suffix = new TextObject("{=BC_OrdinalSuffix_St}st");
            else if (number % 10 == 2)
                suffix = new TextObject("{=BC_OrdinalSuffix_Nd}nd");
            else if (number % 10 == 3)
                suffix = new TextObject("{=BC_OrdinalSuffix_Rd}rd");
            else
                suffix = new TextObject("{=BC_OrdinalSuffix_Th}th");

            TextObject ordinal = new TextObject("{=BC_OrdinalFormat}{NUMBER}{SUFFIX}");
            ordinal.SetTextVariable("NUMBER", number);
            ordinal.SetTextVariable("SUFFIX", suffix);
            return ordinal.ToString();
        }
    }

    public sealed class SuccessionLawOptionVM : ViewModel
    {
        private readonly Action _onSelected;

        public SuccessionLawOptionVM(
            TextObject name,
            TextObject description,
            bool isActive,
            bool canChange,
            TextObject failureReason,
            Action onSelected)
        {
            Name = name?.ToString() ?? string.Empty;
            Description = description?.ToString() ?? string.Empty;
            IsActive = isActive;
            IsEnabled = canChange && !isActive;
            _onSelected = onSelected;
            TextObject hint = onSelected == null
                ? new TextObject(description?.ToString() ?? string.Empty)
                : isActive
                ? new TextObject("{=BC_SuccessionLaw_ActiveHint}{DESCRIPTION}\n\nThis law is currently in force.")
                : canChange
                    ? new TextObject("{=BC_SuccessionLaw_AvailableHint}{DESCRIPTION}\n\nClick to enact this law.")
                    : new TextObject("{=BC_SuccessionLaw_BlockedHint}{DESCRIPTION}\n\n{BLOCK_REASON}");
            hint.SetTextVariable("DESCRIPTION", description);
            hint.SetTextVariable("BLOCK_REASON", failureReason ?? new TextObject(string.Empty));
            Hint = new HintViewModel(hint);
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Description { get; }
        [DataSourceProperty] public bool IsActive { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        [DataSourceProperty] public bool IsButtonEnabled => IsActive || IsEnabled;
        [DataSourceProperty] public HintViewModel Hint { get; }

        [DataSourceMethod]
        public void ExecuteSelect()
        {
            if (IsEnabled)
                _onSelected?.Invoke();
        }
    }
}
