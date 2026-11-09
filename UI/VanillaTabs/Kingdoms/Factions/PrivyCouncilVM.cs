using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Factions
{
    public sealed class PrivyCouncilVM : ViewModel
    {
        private readonly Kingdom _kingdom;
        private string _headerText;
        private string _rulerControversyText;
        private string _realmCapacityText;
        private MBBindingList<PrivyCouncilOfficeVM> _offices;
        private PrivyCouncilOfficeVM _selectedOffice;
        private bool _isProposeEnabled;
        private string _proposeText;
        private string _proposalInfluenceCostText;
        private HintViewModel _proposeHint;
        private bool _isDismissEnabled;
        private string _dismissText;
        private string _dismissInfluenceCostText;
        private HintViewModel _dismissHint;

        private static readonly PrivyCouncilOffice[] DisplayOrder =
        {
            PrivyCouncilOffice.Marshal,
            PrivyCouncilOffice.Chancellor,
            PrivyCouncilOffice.FirstAdvisor,
            PrivyCouncilOffice.Seneschal,
            PrivyCouncilOffice.Spymaster,
            PrivyCouncilOffice.SecondAdvisor
        };

        public PrivyCouncilVM(Kingdom kingdom)
        {
            _kingdom = kingdom;
            Offices = new MBBindingList<PrivyCouncilOfficeVM>();
            Refresh();
        }

        public void Refresh()
        {
            PrivyCouncilOffice? selectedOffice = SelectedOffice?.Office;
            PrivyCouncilBehavior behavior = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            behavior?.EnsureCouncilForKingdom(_kingdom);

            string conciseKingdomName = FeudalTitleDisplayHelper.ResolveConciseSovereignTitleName(_kingdom);
            if (string.IsNullOrWhiteSpace(conciseKingdomName))
                conciseKingdomName = "?";

            TextObject header = new TextObject("{=BC_Council_Header}{COUNCIL_NAME} of {KINGDOM_NAME}");
            header.SetTextVariable("KINGDOM_NAME", new TextObject("{=!}" + conciseKingdomName));
            HeaderText = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(header, _kingdom).ToString();

            TextObject controversy = new TextObject("{=BC_Council_RulerControversy}Ruler controversy: {VALUE}");
            controversy.SetTextVariable("VALUE", (behavior?.GetRulerControversy(_kingdom) ?? 0f).ToString("0.0"));
            RulerControversyText = controversy.ToString();

            int availableNobles = 0;
            if (_kingdom != null)
            {
                foreach (Clan clan in _kingdom.Clans)
                {
                    if (clan != _kingdom.RulingClan && NobleClanEligibilityHelper.IsLiveNobleClan(clan))
                        availableNobles++;
                }
            }

            TextObject capacity = new TextObject("{=BC_Council_RealmCapacity}The realm can sustain {SEATS} of 4 core offices.");
            capacity.SetTextVariable("SEATS", Math.Min(4, availableNobles));
            RealmCapacityText = capacity.ToString();

            Offices.Clear();
            SelectedOffice = null;
            foreach (PrivyCouncilOffice office in DisplayOrder)
            {
                PrivyCouncilOfficeVM officeVm = new PrivyCouncilOfficeVM(_kingdom, office, behavior, OnOfficeSelected);
                officeVm.IsSelected = selectedOffice == office;
                Offices.Add(officeVm);
                if (officeVm.IsSelected)
                    SelectedOffice = officeVm;
            }

            RefreshProposalState(behavior);
        }

        private void OnOfficeSelected(PrivyCouncilOfficeVM selected)
        {
            if (selected == null)
                return;

            foreach (PrivyCouncilOfficeVM office in Offices)
                office.IsSelected = office == selected;

            SelectedOffice = selected;
            RefreshProposalState(Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>());
        }

        public void ExecutePropose()
        {
            PrivyCouncilBehavior behavior = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            RefreshProposalState(behavior);
            if (!IsProposeEnabled || SelectedOffice == null)
                return;

            PrivyCouncilOffice office = SelectedOffice.Office;
            CouncilAppointmentDeliberationBehavior deliberation =
                Campaign.Current?.GetCampaignBehavior<CouncilAppointmentDeliberationBehavior>();
            TextObject failureReason = null;
            if (deliberation == null
                || !deliberation.TryProposePlayerAppointment(_kingdom, office, out failureReason, Refresh))
            {
                if (failureReason != null && !string.IsNullOrWhiteSpace(failureReason.ToString()))
                    BellumCivileNotifications.ShowPersonal(failureReason, BellumNotificationColors.Warning);
                BellumCivileLogger.Log($"Player privy council appointment was not filed; kingdom={_kingdom?.StringId ?? "null"}; office={office}.");
                RefreshProposalState(behavior);
                return;
            }


            int influenceCost = behavior.GetFactionMotionInfluenceCost(Clan.PlayerClan, PrivyCouncilBehavior.AppointmentProposalInfluenceCost);
            BellumCivileLogger.Log($"Player proposed privy council appointment; kingdom={_kingdom.StringId}; office={office}; influence_cost={influenceCost}; court_agenda={CourtAgendaBehavior.Current != null}.");
            Refresh();
        }

        public void ExecuteDismiss()
        {
            PrivyCouncilBehavior behavior = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            RefreshProposalState(behavior);
            if (!IsDismissEnabled || SelectedOffice == null || behavior == null)
                return;

            PrivyCouncilOffice office = SelectedOffice.Office;
            TextObject description = new TextObject("{=BC_Council_DismissConfirmDesc}Dismiss {COUNCILOR_NAME} as {OFFICE}? This will cost {COST} influence, damage relations, and anger their court faction. The office will remain vacant until a new appointment is proposed.");
            description.SetTextVariable("COUNCILOR_NAME", SelectedOffice.HolderName);
            description.SetTextVariable("OFFICE", SelectedOffice.OfficeName);
            description.SetTextVariable("COST", PrivyCouncilBehavior.DirectDismissalInfluenceCost);

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Council_DismissConfirmTitle}Dismiss Councillor").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Council_Dismiss}Dismiss").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                () => CompleteDismissal(behavior, office),
                null), true);
        }

        private void CompleteDismissal(PrivyCouncilBehavior behavior, PrivyCouncilOffice office)
        {
            if (!behavior.TryDismissOfficeHolderByRuler(
                    _kingdom,
                    office,
                    Clan.PlayerClan,
                    out TextObject failureReason))
            {
                if (failureReason != null && !string.IsNullOrWhiteSpace(failureReason.ToString()))
                    BellumCivileNotifications.ShowPersonal(failureReason, BellumNotificationColors.Warning);
            }

            Refresh();
        }

        private void RefreshProposalState(PrivyCouncilBehavior behavior)
        {
            ProposeText = new TextObject("{=BC_Council_Propose}Appoint").ToString();
            int influenceCost = behavior?.GetFactionMotionInfluenceCost(Clan.PlayerClan, PrivyCouncilBehavior.AppointmentProposalInfluenceCost)
                ?? PrivyCouncilBehavior.AppointmentProposalInfluenceCost;
            ProposalInfluenceCostText = influenceCost.ToString();

            TextObject reason = GetProposalReason(behavior, out bool enabled);
            IsProposeEnabled = enabled;
            ProposeHint = new HintViewModel(reason);

            DismissText = new TextObject("{=BC_Council_Dismiss}Dismiss").ToString();
            DismissInfluenceCostText = PrivyCouncilBehavior.DirectDismissalInfluenceCost.ToString();
            TextObject dismissReason = GetDismissReason(behavior, out bool dismissEnabled);
            IsDismissEnabled = dismissEnabled;
            DismissHint = new HintViewModel(dismissReason);
        }

        private TextObject GetProposalReason(PrivyCouncilBehavior behavior, out bool enabled)
        {
            enabled = false;
            if (SelectedOffice == null)
                return new TextObject("{=BC_Council_ProposalSelectOffice}Select a council office before proposing an appointment vote.");

            if (_kingdom == null || Clan.PlayerClan?.Kingdom != _kingdom)
                return new TextObject("{=BC_Council_ProposalWrongRealm}You may only propose appointments within your own realm.");

            if (behavior == null || !behavior.IsOfficeUnlocked(_kingdom, SelectedOffice.Office))
                return new TextObject("{=BC_Council_ProposalLockedOffice}This council office is not available to the realm.");

            if (CourtAgendaBehavior.Current != null)
            {
                enabled = CourtAgendaBehavior.Current.TryNominateCouncilAppointment(_kingdom, SelectedOffice.Office, out var agendaReason, readOnly: true);
                return agendaReason;
            }

            return new TextObject("{=BC_CourtAgenda_Unavailable}The court cannot receive a motion at present.");
        }

        private TextObject GetDismissReason(PrivyCouncilBehavior behavior, out bool enabled)
        {
            enabled = false;
            if (SelectedOffice == null)
                return new TextObject("{=BC_Council_DismissVacant}Select an occupied council office before dismissing a councillor.");

            if (_kingdom == null || Clan.PlayerClan?.Kingdom != _kingdom || _kingdom.RulingClan != Clan.PlayerClan)
                return new TextObject("{=BC_Council_DismissOnlyRuler}Only the ruler may outright dismiss a councillor.");

            if (behavior == null || !behavior.IsOfficeUnlocked(_kingdom, SelectedOffice.Office))
                return new TextObject("{=BC_Council_ProposalLockedOffice}This council office is not available to the realm.");

            if (!SelectedOffice.HasHolder)
                return new TextObject("{=BC_Council_DismissVacant}Select an occupied council office before dismissing a councillor.");

            CouncilAppointmentDeliberationBehavior appointmentDeliberation =
                Campaign.Current?.GetCampaignBehavior<CouncilAppointmentDeliberationBehavior>();
            if (appointmentDeliberation?.HasPendingAppointment(_kingdom) == true
                || _kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any())
            {
                return new TextObject("{=BC_Council_DismissPending}A council appointment is already before the realm and must be settled first.");
            }

            if ((Clan.PlayerClan?.Influence ?? 0f) < PrivyCouncilBehavior.DirectDismissalInfluenceCost)
            {
                TextObject influence = new TextObject("{=BC_Council_DismissInfluence}You need {COST} influence to dismiss this councillor outright.");
                influence.SetTextVariable("COST", PrivyCouncilBehavior.DirectDismissalInfluenceCost);
                return influence;
            }

            enabled = true;
            TextObject available = new TextObject("{=BC_Council_DismissHint}Dismiss {COUNCILOR_NAME} as {OFFICE}. This will damage relations and anger their court faction, leaving the office vacant.");
            available.SetTextVariable("COUNCILOR_NAME", SelectedOffice.HolderName);
            available.SetTextVariable("OFFICE", SelectedOffice.OfficeName);
            return available;
        }

        [DataSourceProperty]
        public string HeaderText
        {
            get => _headerText;
            set
            {
                if (value == _headerText) return;
                _headerText = value;
                OnPropertyChangedWithValue(value, nameof(HeaderText));
            }
        }

        [DataSourceProperty]
        public string RulerControversyText
        {
            get => _rulerControversyText;
            set
            {
                if (value == _rulerControversyText) return;
                _rulerControversyText = value;
                OnPropertyChangedWithValue(value, nameof(RulerControversyText));
            }
        }

        [DataSourceProperty]
        public string RealmCapacityText
        {
            get => _realmCapacityText;
            set
            {
                if (value == _realmCapacityText) return;
                _realmCapacityText = value;
                OnPropertyChangedWithValue(value, nameof(RealmCapacityText));
            }
        }

        [DataSourceProperty]
        public MBBindingList<PrivyCouncilOfficeVM> Offices
        {
            get => _offices;
            set
            {
                if (value == _offices) return;
                _offices = value;
                OnPropertyChangedWithValue(value, nameof(Offices));
            }
        }

        [DataSourceProperty]
        public PrivyCouncilOfficeVM SelectedOffice
        {
            get => _selectedOffice;
            private set
            {
                if (value == _selectedOffice) return;
                _selectedOffice = value;
                OnPropertyChangedWithValue(value, nameof(SelectedOffice));
            }
        }

        [DataSourceProperty]
        public bool IsProposeEnabled
        {
            get => _isProposeEnabled;
            private set
            {
                if (value == _isProposeEnabled) return;
                _isProposeEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsProposeEnabled));
            }
        }

        [DataSourceProperty]
        public string ProposeText
        {
            get => _proposeText;
            private set
            {
                if (value == _proposeText) return;
                _proposeText = value;
                OnPropertyChangedWithValue(value, nameof(ProposeText));
            }
        }

        [DataSourceProperty]
        public string ProposalInfluenceCostText
        {
            get => _proposalInfluenceCostText;
            private set
            {
                if (value == _proposalInfluenceCostText) return;
                _proposalInfluenceCostText = value;
                OnPropertyChangedWithValue(value, nameof(ProposalInfluenceCostText));
            }
        }

        [DataSourceProperty]
        public HintViewModel ProposeHint
        {
            get => _proposeHint;
            private set
            {
                if (value == _proposeHint) return;
                _proposeHint = value;
                OnPropertyChangedWithValue(value, nameof(ProposeHint));
            }
        }

        [DataSourceProperty]
        public bool IsDismissEnabled
        {
            get => _isDismissEnabled;
            private set
            {
                if (value == _isDismissEnabled) return;
                _isDismissEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsDismissEnabled));
            }
        }

        [DataSourceProperty]
        public string DismissText
        {
            get => _dismissText;
            private set
            {
                if (value == _dismissText) return;
                _dismissText = value;
                OnPropertyChangedWithValue(value, nameof(DismissText));
            }
        }

        [DataSourceProperty]
        public string DismissInfluenceCostText
        {
            get => _dismissInfluenceCostText;
            private set
            {
                if (value == _dismissInfluenceCostText) return;
                _dismissInfluenceCostText = value;
                OnPropertyChangedWithValue(value, nameof(DismissInfluenceCostText));
            }
        }

        [DataSourceProperty]
        public HintViewModel DismissHint
        {
            get => _dismissHint;
            private set
            {
                if (value == _dismissHint) return;
                _dismissHint = value;
                OnPropertyChangedWithValue(value, nameof(DismissHint));
            }
        }
    }

    public sealed class PrivyCouncilOfficeVM : ViewModel
    {
        private static readonly Color CriticalColor = Colors.Red;
        private static readonly Color PoorColor = Color.ConvertStringToColor("#FF8C00FF");
        private static readonly Color NeutralColor = Color.ConvertStringToColor("#F1D8A4FF");
        private static readonly Color GoodColor = Color.ConvertStringToColor("#B6D96BFF");
        private static readonly Color ExcellentColor = Color.ConvertStringToColor("#82E06AFF");
        private static readonly Color MutedColor = Color.ConvertStringToColor("#9A9286FF");

        private readonly Action<PrivyCouncilOfficeVM> _onSelected;
        private readonly Clan _portraitClan;
        private readonly BasicTooltipViewModel _tooltip;
        private readonly Kingdom _kingdom;
        private readonly PrivyCouncilBehavior _behavior;
        private readonly IReadOnlyList<PrivyCouncilAssignmentDefinition> _assignments;
        private bool _isSelected;
        private bool _isAssignmentEnabled;
        private bool _isAssignmentLocked;
        private bool _suppressAssignmentChange;
        private string _currentAssignmentName;
        private HintViewModel _assignmentHint;

        public PrivyCouncilOfficeVM(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilBehavior behavior,
            Action<PrivyCouncilOfficeVM> onSelected)
        {
            _onSelected = onSelected;
            _kingdom = kingdom;
            _behavior = behavior;
            Office = office;
            OfficeName = GetOfficeName(office, kingdom);

            PrivyCouncilOfficeRecord record = behavior?.GetOfficeRecord(kingdom, office);
            Clan holder = behavior?.GetOfficeHolder(kingdom, office);
            _portraitClan = holder;
            IsUnlocked = behavior?.IsOfficeUnlocked(kingdom, office) == true;
            HasHolder = holder != null;
            IsVacant = IsUnlocked && holder == null;
            ShowStatusText = !HasHolder;
            HolderName = FormatHolderName(holder);
            ClanName = holder?.Name?.ToString() ?? new TextObject("{=BC_Council_Vacant}Vacant").ToString();

            if (holder?.Banner != null)
                BannerVisual = new BannerImageIdentifierVM(holder.Banner, true);
            if (holder?.Leader?.CharacterObject != null)
                PortraitVisual = new CharacterImageIdentifierVM(BellumCivile.UI.PortraitAppearance.Create(holder.Leader.CharacterObject));

            float competence = behavior?.GetOfficeCompetence(kingdom, office) ?? 0f;
            float support = behavior?.GetOfficeSupportPercent(kingdom, office) ?? 0f;
            float controversy = record?.Controversy ?? 0f;
            int salary = behavior?.GetDailySalary(kingdom, office) ?? 0;
            CompetenceLabelText = new TextObject("{=BC_Council_CompetenceLabel}Competence:").ToString();
            SupportLabelText = new TextObject("{=BC_Council_SupportLabel}Support:").ToString();
            ControversyLabelText = new TextObject("{=BC_Council_ControversyLabel}Controversy:").ToString();
            CompetenceValueText = GetCompetenceTier(competence) + " (" + competence.ToString("0") + ")";
            SupportValueText = GetSupportTier(support) + " (" + support.ToString("0") + "%)";
            ControversyValueText = GetControversyTier(controversy) + " (" + controversy.ToString("0.0") + ")";
            CompetenceValueColor = GetPositiveMetricColor(competence);
            SupportValueColor = GetPositiveMetricColor(support);
            ControversyValueColor = GetNegativeMetricColor(controversy);
            StatusText = BuildStatusText(kingdom, office, behavior, record, holder);
            StatusColor = !IsUnlocked || behavior?.IsVacancyExcused(kingdom, office) == true
                ? MutedColor
                : IsVacant ? PoorColor : ExcellentColor;

            _assignments = behavior?.GetAssignments(office)
                ?? PrivyCouncilAssignmentRegistry.GetAssignments(office);
            PrivyCouncilAssignmentDefinition currentAssignment = behavior?.GetOfficeAssignment(kingdom, office)
                ?? PrivyCouncilAssignmentRegistry.GetDefaultAssignment(office);
            int selectedAssignmentIndex = Math.Max(0, _assignments
                .Select((assignment, index) => new { assignment, index })
                .Where(item => item.assignment.Id == currentAssignment?.Id)
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .First());
            // SelectorVM invokes its callback during construction, before this property is assigned.
            AssignmentSelector = new SelectorVM<SelectorItemVM>(
                _assignments.Select(assignment => assignment.Name),
                selectedAssignmentIndex,
                null);
            for (int i = 0; i < AssignmentSelector.ItemList.Count && i < _assignments.Count; i++)
                AssignmentSelector.ItemList[i].Hint = new HintViewModel(
                    _behavior?.GetAssignmentDescription(_assignments[i], kingdom)
                    ?? _assignments[i].Description);
            CurrentAssignmentName = currentAssignment?.Name?.ToString() ?? string.Empty;
            RefreshAssignmentState();
            AssignmentSelector.SetOnChangeAction(OnAssignmentChanged);

            _tooltip = new BasicTooltipViewModel(() => BuildTooltipProperties(
                kingdom,
                office,
                record,
                HolderName,
                HasHolder,
                StatusText,
                competence,
                support,
                controversy,
                salary,
                CurrentAssignmentName));
        }

        public PrivyCouncilOffice Office { get; }
        [DataSourceProperty] public string OfficeName { get; }
        [DataSourceProperty] public string HolderName { get; }
        [DataSourceProperty] public string ClanName { get; }
        [DataSourceProperty] public string CompetenceLabelText { get; }
        [DataSourceProperty] public string SupportLabelText { get; }
        [DataSourceProperty] public string ControversyLabelText { get; }
        [DataSourceProperty] public string CompetenceValueText { get; }
        [DataSourceProperty] public string SupportValueText { get; }
        [DataSourceProperty] public string ControversyValueText { get; }
        [DataSourceProperty] public string StatusText { get; }
        [DataSourceProperty] public bool IsUnlocked { get; }
        [DataSourceProperty] public bool HasHolder { get; }
        [DataSourceProperty] public bool IsVacant { get; }
        [DataSourceProperty] public bool ShowStatusText { get; }
        [DataSourceProperty] public Color CompetenceValueColor { get; }
        [DataSourceProperty] public Color SupportValueColor { get; }
        [DataSourceProperty] public Color ControversyValueColor { get; }
        [DataSourceProperty] public Color StatusColor { get; }
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get; }
        [DataSourceProperty] public CharacterImageIdentifierVM PortraitVisual { get; }
        [DataSourceProperty] public SelectorVM<SelectorItemVM> AssignmentSelector { get; }

        [DataSourceProperty]
        public bool IsAssignmentEnabled
        {
            get => _isAssignmentEnabled;
            private set
            {
                if (value == _isAssignmentEnabled) return;
                _isAssignmentEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsAssignmentEnabled));
            }
        }

        [DataSourceProperty]
        public bool IsAssignmentLocked
        {
            get => _isAssignmentLocked;
            private set
            {
                if (value == _isAssignmentLocked) return;
                _isAssignmentLocked = value;
                OnPropertyChangedWithValue(value, nameof(IsAssignmentLocked));
            }
        }

        [DataSourceProperty]
        public string CurrentAssignmentName
        {
            get => _currentAssignmentName;
            private set
            {
                if (value == _currentAssignmentName) return;
                _currentAssignmentName = value;
                OnPropertyChangedWithValue(value, nameof(CurrentAssignmentName));
            }
        }

        [DataSourceProperty]
        public HintViewModel AssignmentHint
        {
            get => _assignmentHint;
            private set
            {
                if (value == _assignmentHint) return;
                _assignmentHint = value;
                OnPropertyChangedWithValue(value, nameof(AssignmentHint));
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected) return;
                _isSelected = value;
                OnPropertyChangedWithValue(value, nameof(IsSelected));
            }
        }

        public void ExecuteSelect()
        {
            _onSelected?.Invoke(this);
        }

        public void ExecuteBeginHint()
        {
            _tooltip?.ExecuteBeginHint();
        }

        public void ExecuteOpenClan()
        {
            if (_portraitClan != null)
                Campaign.Current?.EncyclopediaManager?.GoToLink(_portraitClan.EncyclopediaLink);
        }

        public void ExecuteEndHint()
        {
            _tooltip?.ExecuteEndHint();
        }

        private void OnAssignmentChanged(SelectorVM<SelectorItemVM> selector)
        {
            if (_suppressAssignmentChange || selector == null || selector.SelectedIndex < 0
                || selector.SelectedIndex >= _assignments.Count)
            {
                return;
            }

            PrivyCouncilAssignmentDefinition requested = _assignments[selector.SelectedIndex];
            TextObject failureReason = TextObject.GetEmpty();
            if (_behavior?.TrySetOfficeAssignment(
                    _kingdom,
                    Office,
                    requested.Id,
                    Clan.PlayerClan,
                    ignoreCooldown: false,
                    out failureReason) == true)
            {
                CurrentAssignmentName = requested.Name.ToString();
                RefreshAssignmentState();
                return;
            }

            PrivyCouncilAssignmentDefinition current = _behavior?.GetOfficeAssignment(_kingdom, Office)
                ?? PrivyCouncilAssignmentRegistry.GetDefaultAssignment(Office);
            int currentIndex = _assignments
                .Select((assignment, index) => new { assignment, index })
                .Where(item => item.assignment.Id == current?.Id)
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .First();
            _suppressAssignmentChange = true;
            AssignmentSelector.SelectedIndex = currentIndex;
            _suppressAssignmentChange = false;
            AssignmentHint = new HintViewModel(failureReason ?? TextObject.GetEmpty());
        }

        private void RefreshAssignmentState()
        {
            PrivyCouncilAssignmentDefinition current = _behavior?.GetOfficeAssignment(_kingdom, Office)
                ?? PrivyCouncilAssignmentRegistry.GetDefaultAssignment(Office);
            CurrentAssignmentName = current?.Name?.ToString() ?? string.Empty;
            TextObject assignmentStatus = _behavior?.GetAssignmentStatusHint(_kingdom, Office)
                ?? current?.Description
                ?? TextObject.GetEmpty();

            TextObject hint;
            bool enabled = true;
            if (!IsUnlocked)
            {
                enabled = false;
                hint = new TextObject("{=BC_Council_AssignmentUnavailable}This council office is not available to the realm.");
            }
            else if (!HasHolder)
            {
                enabled = false;
                hint = new TextObject("{=BC_Council_AssignmentVacant}A vacant council office cannot be given an assignment.");
            }
            else if (_behavior?.IsOfficeHolderCaptive(_kingdom, Office) == true)
            {
                enabled = false;
                hint = _behavior.GetAssignmentCaptivityHint(_kingdom, Office);
            }
            else if (_kingdom?.RulingClan != Clan.PlayerClan)
            {
                enabled = false;
                hint = CombineAssignmentHint(
                    assignmentStatus,
                    CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(
                        new TextObject("{=BC_Council_AssignmentRulerOnly}Only the ruler may assign work to the {COUNCIL_NAME}."),
                        _kingdom));
            }
            else
            {
                float remaining = _behavior?.GetAssignmentCooldownRemaining(_kingdom, Office) ?? 0f;
                if (remaining > 0f)
                {
                    enabled = false;
                    TextObject cooldown = new TextObject("{=BC_Council_AssignmentCooldown}This councillor may be reassigned in {DAYS} days.");
                    cooldown.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(remaining)));
                    hint = CombineAssignmentHint(assignmentStatus, cooldown);
                }
                else
                {
                    hint = CombineAssignmentHint(
                        assignmentStatus,
                        new TextObject("{=BC_Council_AssignmentReassignmentAvailable}This councillor is available to be reassigned."));
                }
            }

            IsAssignmentEnabled = enabled;
            // An unavailable or vacant office has no assignment control to interact with.
            // The disabled selector is reserved for an occupied office whose assignment
            // is temporarily locked by authority or cooldown.
            IsAssignmentLocked = HasHolder && !enabled;
            AssignmentHint = new HintViewModel(hint);
            for (int i = 0; i < AssignmentSelector.ItemList.Count; i++)
            {
                SelectorItemVM item = AssignmentSelector.ItemList[i];
                item.CanBeSelected = enabled;
                item.Hint = new HintViewModel(
                    enabled && i < _assignments.Count
                        ? _behavior?.GetAssignmentDescription(_assignments[i], _kingdom)
                            ?? _assignments[i].Description
                        : hint);
            }
        }

        private static TextObject CombineAssignmentHint(TextObject status, TextObject footer)
        {
            string statusText = status?.ToString() ?? string.Empty;
            string footerText = footer?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(statusText))
                return new TextObject(footerText);
            if (string.IsNullOrEmpty(footerText))
                return new TextObject(statusText);

            return new TextObject(statusText + "\n\n" + footerText);
        }

        private static string FormatHolderName(Clan holder)
        {
            Hero leader = holder?.Leader;
            if (leader == null)
                return new TextObject("{=BC_Council_Vacant}Vacant").ToString();

            return leader.Name?.ToString() ?? holder.Name?.ToString() ?? string.Empty;
        }

        private static string BuildStatusText(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilBehavior behavior,
            PrivyCouncilOfficeRecord record,
            Clan holder)
        {
            if (behavior?.IsOfficeUnlocked(kingdom, office) != true)
            {
                return office == PrivyCouncilOffice.FirstAdvisor
                    ? new TextObject("{=BC_Council_LockedLargeRealm}Locked: requires more than 30 fiefs").ToString()
                    : new TextObject("{=BC_Council_LockedEmpire}Locked: requires an empire-tier sovereign").ToString();
            }

            if (holder != null)
                return holder.Leader?.IsPrisoner == true
                    ? new TextObject("{=BC_Council_CaptiveStatus}Held captive: duties suspended").ToString() : string.Empty;

            if (office > PrivyCouncilOffice.Spymaster)
                return new TextObject("{=BC_Council_AdvisorVacancy}Vacant: this office generates no vacancy controversy.").ToString();

            if (behavior.IsVacancyExcused(kingdom, office))
                return new TextObject("{=BC_Council_VacancyExcused}Vacant: realm too small to sustain this office").ToString();

            float vacancyDays = behavior.GetVacancyDays(record);
            if (vacancyDays <= 7f)
            {
                TextObject grace = new TextObject("{=BC_Council_VacancyGrace}Vacant: grace period ({DAYS}/7 days)");
                grace.SetTextVariable("DAYS", Math.Max(0, (int)Math.Floor(vacancyDays)));
                return grace.ToString();
            }

            return new TextObject("{=BC_Council_VacancyPressure}Vacant: generating ruler controversy").ToString();
        }

        private static List<TooltipProperty> BuildTooltipProperties(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilOfficeRecord record,
            string holderName,
            bool hasHolder,
            string statusText,
            float competence,
            float support,
            float controversy,
            int salary,
            string assignmentName)
        {
            string officeName = GetOfficeName(office, kingdom);
            string lastReason = string.IsNullOrWhiteSpace(record?.LastReason)
                ? new TextObject("{=BC_Council_NoRecentDevelopment}None recorded").ToString()
                : LocalizeReason(record.LastReason);
            string lastChange = record == null ? "0.0" : record.LastChange.ToString("+0.0;-0.0;0.0");

            List<TooltipProperty> properties = new List<TooltipProperty>
            {
                new TooltipProperty(
                    hasHolder ? holderName : officeName,
                    string.Empty,
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.Title)
            };

            AddTooltipSeparator(properties);
            if (hasHolder)
                AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipOffice}Office"), officeName);

            properties.Add(new TooltipProperty(
                string.Empty,
                GetOfficeRole(office, kingdom),
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.MultiLine));

            properties.Add(new TooltipProperty(string.Empty, CouncilCompetencePresentation.Requirements(office).ToString(),
                0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));

            AddTooltipSeparator(properties);
            if (!string.IsNullOrWhiteSpace(statusText))
                AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipStatus}Status"), statusText);

            if (hasHolder && !string.IsNullOrWhiteSpace(assignmentName))
                AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipAssignment}Assignment"), assignmentName);

            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipCompetence}Competence"), GetCompetenceTier(competence) + " (" + competence.ToString("0") + ")");
            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipControversy}Controversy"), GetControversyTier(controversy) + " (" + controversy.ToString("0.0") + ")");
            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipSupport}Support"), GetSupportTier(support) + " (" + support.ToString("0") + "%)");
            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipSalary}Daily salary"), salary + " " + new TextObject("{=BC_Council_Denars}denars"));
            int fundedSalary = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?.ExpectedCouncilSalary(kingdom, office) ?? 0;
            if (fundedSalary < salary)
                AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipAffordableSalary}Affordable from Crown funds"),
                    fundedSalary + " " + new TextObject("{=BC_Council_Denars}denars"));
            if (salary > 0)
                properties.Add(new TooltipProperty(string.Empty,
                    new TextObject("{=BC_Council_SalaryPaymentNote}The Crown funds wages through its daily budget. Funded wages are collected through your clan's daily income; payment depends on the Crown's available funds.").ToString(),
                    0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipRecovery}Daily recovery"),
                (council?.GetDailyOfficeRecovery(kingdom, office) ?? 0f).ToString("0.00"));
            if (council?.IsOfficeHolderCaptive(kingdom, office) == true)
                properties.Add(new TooltipProperty(string.Empty, council.GetCaptivityEffectsHint(kingdom, office).ToString(),
                    0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));

            AddTooltipSeparator(properties);
            AddTooltipRow(properties, new TextObject("{=BC_Council_TooltipLastDevelopment}Last development"), lastReason + " (" + lastChange + ")");
            return properties;
        }

        private static void AddTooltipRow(List<TooltipProperty> properties, TextObject label, string value)
        {
            properties.Add(new TooltipProperty(label.ToString(), value ?? string.Empty, 0));
        }

        private static void AddTooltipSeparator(List<TooltipProperty> properties)
        {
            properties.Add(new TooltipProperty(
                string.Empty,
                string.Empty,
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
        }

        private static string GetOfficeRole(PrivyCouncilOffice office, Kingdom kingdom)
        {
            TextObject role;
            switch (office)
            {
                case PrivyCouncilOffice.Marshal:
                    role = new TextObject("{=BC_Council_MarshalRole}The {OFFICE} bears responsibility for the realm's armies and may summon the full royal host.");
                    break;
                case PrivyCouncilOffice.Chancellor:
                    role = new TextObject("{=BC_Council_ChancellorRole}The {OFFICE} moderates claim feuds and reduces the influence cost of motions from their court faction.");
                    break;
                case PrivyCouncilOffice.Seneschal:
                    role = new TextObject("{=BC_Council_SeneschalRole}The {OFFICE} improves tax collection from the crown's most prosperous fiefs.");
                    break;
                case PrivyCouncilOffice.Spymaster:
                    role = new TextObject("{=BC_Council_SpymasterRole}The {OFFICE} conducts the realm's intrigue checks and supports agents sent on covert missions.");
                    break;
                default:
                    role = new TextObject("{=BC_Council_AdvisorRole}The {OFFICE} holds no administrative office, but gives a powerful house representation at court.");
                    break;
            }

            return CourtInstitutionDisplayHelper.ApplyOfficeName(role, office, kingdom).ToString();
        }

        private static string LocalizeReason(string reason)
        {
            switch (reason)
            {
                case "competent administration": return new TextObject("{=BC_Council_ReasonCompetentAdministration}Competent administration").ToString();
                case "vacant council office": return new TextObject("{=BC_Council_ReasonVacantOffice}Vacant council office").ToString();
                case "war on multiple fronts": return new TextObject("{=BC_Council_ReasonMultipleFronts}War on multiple fronts").ToString();
                case "ruler held captive": return new TextObject("{=BC_Council_ReasonRulerCaptive}Ruler held captive").ToString();
                case "councillor held captive": return new TextObject("{=BC_Council_ReasonCouncillorCaptive}Councillor held captive").ToString();
                case "disastrous war progress": return new TextObject("{=BC_Council_ReasonDisastrousWar}Disastrous war progress").ToString();
                case "poor war progress": return new TextObject("{=BC_Council_ReasonPoorWar}Poor war progress").ToString();
                case "court factions in open discontent": return new TextObject("{=BC_Council_ReasonOpenDiscontent}Court factions in open discontent").ToString();
                case "court factions dissatisfied": return new TextObject("{=BC_Council_ReasonFactionDissatisfaction}Court factions dissatisfied").ToString();
                case "crown isolated from its vassals": return new TextObject("{=BC_Council_ReasonCrownIsolated}Crown isolated from its vassals").ToString();
                case "strained relations with the nobility": return new TextObject("{=BC_Council_ReasonStrainedNobility}Strained relations with the nobility").ToString();
                case "unresolved noble feuds": return new TextObject("{=BC_Council_ReasonUnresolvedFeuds}Unresolved noble feuds").ToString();
                case "starving settlements": return new TextObject("{=BC_Council_ReasonStarvingSettlements}Starving settlements").ToString();
                case "low settlement loyalty": return new TextObject("{=BC_Council_ReasonLowLoyalty}Low settlement loyalty").ToString();
                case "declining realm prosperity": return new TextObject("{=BC_Council_ReasonDecliningProsperity}Declining realm prosperity").ToString();
                case "active conspiracies": return new TextObject("{=BC_Council_ReasonActiveConspiracies}Active conspiracies").ToString();
                case "rebellion escaped detection": return new TextObject("{=BC_Council_ReasonUndetectedRebellion}Rebellion escaped detection").ToString();
                case "dangerous court agitation": return new TextObject("{=BC_Council_ReasonCourtAgitation}Dangerous court agitation").ToString();
                case "legacy ruler controversy": return new TextObject("{=BC_Council_ReasonLegacyControversy}Inherited ruler controversy").ToString();
                case "unfavorable peace settlement": return new TextObject("{=BC_Council_ReasonUnfavorablePeace}Unfavorable peace settlement").ToString();
                case "military setback": return new TextObject("{=BC_Council_ReasonMilitarySetback}Military setback").ToString();
                // Incident reasons are saved as plain text; translate them only when displayed.
                case "successful patrol recruitment": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPatrolRecruitment}Successful patrol recruitment").ToString();
                case "failed patrol recruitment": return new TextObject("{=BC_CouncilIncident_ReasonFailedPatrolRecruitment}Failed patrol recruitment").ToString();
                case "successful limited patrol recruitment": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedPatrolRecruitment}Successful limited patrol recruitment").ToString();
                case "failed limited patrol recruitment": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedPatrolRecruitment}Failed limited patrol recruitment").ToString();
                case "successful remount requisition": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulRemountRequisition}Successful remount requisition").ToString();
                case "failed remount requisition": return new TextObject("{=BC_CouncilIncident_ReasonFailedRemountRequisition}Failed remount requisition").ToString();
                case "successful limited remount requisition": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedRemountRequisition}Successful limited remount requisition").ToString();
                case "failed limited remount requisition": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedRemountRequisition}Failed limited remount requisition").ToString();
                case "successful suppression of road brigands": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulSuppressionOfRoadBrigands}Successful suppression of road brigands").ToString();
                case "failed suppression of road brigands": return new TextObject("{=BC_CouncilIncident_ReasonFailedSuppressionOfRoadBrigands}Failed suppression of road brigands").ToString();
                case "successful limited sweep against road brigands": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedSweepAgainstRoadBrigands}Successful limited sweep against road brigands").ToString();
                case "failed limited sweep against road brigands": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedSweepAgainstRoadBrigands}Failed limited sweep against road brigands").ToString();
                case "successful revision of the muster rolls": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulRevisionOfTheMusterRolls}Successful revision of the muster rolls").ToString();
                case "failed revision of the muster rolls": return new TextObject("{=BC_CouncilIncident_ReasonFailedRevisionOfTheMusterRolls}Failed revision of the muster rolls").ToString();
                case "successful trial of revised muster rolls": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulTrialOfRevisedMusterRolls}Successful trial of revised muster rolls").ToString();
                case "failed trial of revised muster rolls": return new TextObject("{=BC_CouncilIncident_ReasonFailedTrialOfRevisedMusterRolls}Failed trial of revised muster rolls").ToString();
                case "successful live-steel militia exercises": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLiveSteelMilitiaExercises}Successful live-steel militia exercises").ToString();
                case "failed live-steel militia exercises": return new TextObject("{=BC_CouncilIncident_ReasonFailedLiveSteelMilitiaExercises}Failed live-steel militia exercises").ToString();
                case "successful supervised live-steel exercises": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulSupervisedLiveSteelExercises}Successful supervised live-steel exercises").ToString();
                case "failed supervised live-steel exercises": return new TextObject("{=BC_CouncilIncident_ReasonFailedSupervisedLiveSteelExercises}Failed supervised live-steel exercises").ToString();
                case "successful harvest exemptions": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulHarvestExemptions}Successful harvest exemptions").ToString();
                case "failed harvest exemptions": return new TextObject("{=BC_CouncilIncident_ReasonFailedHarvestExemptions}Failed harvest exemptions").ToString();
                case "successful limited harvest exemptions": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedHarvestExemptions}Successful limited harvest exemptions").ToString();
                case "failed limited harvest exemptions": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedHarvestExemptions}Failed limited harvest exemptions").ToString();
                case "successful regulation of camp followers": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulRegulationOfCampFollowers}Successful regulation of camp followers").ToString();
                case "failed regulation of camp followers": return new TextObject("{=BC_CouncilIncident_ReasonFailedRegulationOfCampFollowers}Failed regulation of camp followers").ToString();
                case "successful limited regulation of camp followers": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedRegulationOfCampFollowers}Successful limited regulation of camp followers").ToString();
                case "failed limited regulation of camp followers": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedRegulationOfCampFollowers}Failed limited regulation of camp followers").ToString();
                case "successful regulation of army foraging": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulRegulationOfArmyForaging}Successful regulation of army foraging").ToString();
                case "failed regulation of army foraging": return new TextObject("{=BC_CouncilIncident_ReasonFailedRegulationOfArmyForaging}Failed regulation of army foraging").ToString();
                case "successful limited army foraging": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedArmyForaging}Successful limited army foraging").ToString();
                case "failed limited army foraging": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedArmyForaging}Failed limited army foraging").ToString();
                case "successful audit of army quartermasters": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulAuditOfArmyQuartermasters}Successful audit of army quartermasters").ToString();
                case "failed audit of army quartermasters": return new TextObject("{=BC_CouncilIncident_ReasonFailedAuditOfArmyQuartermasters}Failed audit of army quartermasters").ToString();
                case "successful limited audit of army quartermasters": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedAuditOfArmyQuartermasters}Successful limited audit of army quartermasters").ToString();
                case "failed limited audit of army quartermasters": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedAuditOfArmyQuartermasters}Failed limited audit of army quartermasters").ToString();
                case "a successful double-agent operation": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulDoubleAgentOperation}A successful double-agent operation").ToString();
                case "a compromised double-agent operation": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedDoubleAgentOperation}A compromised double-agent operation").ToString();
                case "a useful controlled exchange with a foreign agent": return new TextObject("{=BC_CouncilIncident_ReasonUsefulControlledExchangeWithAForeignAgent}A useful controlled exchange with a foreign agent").ToString();
                case "a failed controlled exchange with a foreign agent": return new TextObject("{=BC_CouncilIncident_ReasonFailedControlledExchangeWithAForeignAgent}A failed controlled exchange with a foreign agent").ToString();
                case "a successful reform of covert warrants": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulReformOfCovertWarrants}A successful reform of covert warrants").ToString();
                case "deepening paranoia within the court": return new TextObject("{=BC_CouncilIncident_ReasonDeepeningParanoiaWithinTheCourt}Deepening paranoia within the court").ToString();
                case "protected channels for trusted operatives": return new TextObject("{=BC_CouncilIncident_ReasonProtectedChannelsForTrustedOperatives}Protected channels for trusted operatives").ToString();
                case "compromised channels for trusted operatives": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedChannelsForTrustedOperatives}Compromised channels for trusted operatives").ToString();
                case "a successful purge of compromised guards": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPurgeOfCompromisedGuards}A successful purge of compromised guards").ToString();
                case "a disastrous purge of the palace guard": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousPurgeOfThePalaceGuard}A disastrous purge of the palace guard").ToString();
                case "a discreet removal of compromised guards": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetRemovalOfCompromisedGuards}A discreet removal of compromised guards").ToString();
                case "a failed partial rotation of the palace guard": return new TextObject("{=BC_CouncilIncident_ReasonFailedPartialRotationOfThePalaceGuard}A failed partial rotation of the palace guard").ToString();
                case "a successful deception against suspected conspirators": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulDeceptionAgainstSuspectedConspirators}A successful deception against suspected conspirators").ToString();
                case "an exposed fabrication of treason": return new TextObject("{=BC_CouncilIncident_ReasonExposedFabricationOfTreason}An exposed fabrication of treason").ToString();
                case "a useful false message among conspirators": return new TextObject("{=BC_CouncilIncident_ReasonUsefulFalseMessageAmongConspirators}A useful false message among conspirators").ToString();
                case "an exposed false message among conspirators": return new TextObject("{=BC_CouncilIncident_ReasonExposedFalseMessageAmongConspirators}An exposed false message among conspirators").ToString();
                case "a successful operation against hostile claimants": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulOperationAgainstHostileClaimants}A successful operation against hostile claimants").ToString();
                case "a failed operation against hostile claimants": return new TextObject("{=BC_CouncilIncident_ReasonFailedOperationAgainstHostileClaimants}A failed operation against hostile claimants").ToString();
                case "a useful approach to hostile claim agents": return new TextObject("{=BC_CouncilIncident_ReasonUsefulApproachToHostileClaimAgents}A useful approach to hostile claim agents").ToString();
                case "an exposed approach to hostile claim agents": return new TextObject("{=BC_CouncilIncident_ReasonExposedApproachToHostileClaimAgents}An exposed approach to hostile claim agents").ToString();
                case "a successful infiltration of an underground network": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulInfiltrationOfAnUndergroundNetwork}A successful infiltration of an underground network").ToString();
                case "a failed infiltration of an underground network": return new TextObject("{=BC_CouncilIncident_ReasonFailedInfiltrationOfAnUndergroundNetwork}A failed infiltration of an underground network").ToString();
                case "a successful disruption of conspiratorial couriers": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulDisruptionOfConspiratorialCouriers}A successful disruption of conspiratorial couriers").ToString();
                case "a failed disruption of conspiratorial couriers": return new TextObject("{=BC_CouncilIncident_ReasonFailedDisruptionOfConspiratorialCouriers}A failed disruption of conspiratorial couriers").ToString();
                case "a successful campaign of hostile pamphlets": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulCampaignOfHostilePamphlets}A successful campaign of hostile pamphlets").ToString();
                case "an exposed campaign of hostile pamphlets": return new TextObject("{=BC_CouncilIncident_ReasonExposedCampaignOfHostilePamphlets}An exposed campaign of hostile pamphlets").ToString();
                case "a credible campaign of selected rumors": return new TextObject("{=BC_CouncilIncident_ReasonCredibleCampaignOfSelectedRumors}A credible campaign of selected rumors").ToString();
                case "an exposed campaign of selected rumors": return new TextObject("{=BC_CouncilIncident_ReasonExposedCampaignOfSelectedRumors}An exposed campaign of selected rumors").ToString();
                case "a discreet arrangement with a rival servant": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetArrangementWithARivalServant}A discreet arrangement with a rival servant").ToString();
                case "an exposed attempt to bribe a rival servant": return new TextObject("{=BC_CouncilIncident_ReasonExposedAttemptToBribeARivalServant}An exposed attempt to bribe a rival servant").ToString();
                case "a discreet exchange with a rival servant": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetExchangeWithARivalServant}A discreet exchange with a rival servant").ToString();
                case "a compromised exchange with a rival servant": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedExchangeWithARivalServant}A compromised exchange with a rival servant").ToString();
                case "a successful rebuttal of hostile rumors": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulRebuttalOfHostileRumors}A successful rebuttal of hostile rumors").ToString();
                case "a disastrous rebuttal of hostile rumors": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousRebuttalOfHostileRumors}A disastrous rebuttal of hostile rumors").ToString();
                case "a measured rebuttal of hostile rumors": return new TextObject("{=BC_CouncilIncident_ReasonMeasuredRebuttalOfHostileRumors}A measured rebuttal of hostile rumors").ToString();
                case "a failed selective rebuttal of hostile rumors": return new TextObject("{=BC_CouncilIncident_ReasonFailedSelectiveRebuttalOfHostileRumors}A failed selective rebuttal of hostile rumors").ToString();
                case "successful council reporting reforms": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulCouncilReportingReforms}Successful council reporting reforms").ToString();
                case "failed council reporting reforms": return new TextObject("{=BC_CouncilIncident_ReasonFailedCouncilReportingReforms}Failed council reporting reforms").ToString();
                case "a successful trial of shared council reports": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulTrialOfSharedCouncilReports}A successful trial of shared council reports").ToString();
                case "a failed trial of shared council reports": return new TextObject("{=BC_CouncilIncident_ReasonFailedTrialOfSharedCouncilReports}A failed trial of shared council reports").ToString();
                case "useful lessons drawn from an ancient treatise": return new TextObject("{=BC_CouncilIncident_ReasonUsefulLessonsDrawnFromAnAncientTreatise}Useful lessons drawn from an ancient treatise").ToString();
                case "misguided reforms drawn from an ancient treatise": return new TextObject("{=BC_CouncilIncident_ReasonMisguidedReformsDrawnFromAnAncientTreatise}Misguided reforms drawn from an ancient treatise").ToString();
                case "a successful trial of ancient council practices": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulTrialOfAncientCouncilPractices}A successful trial of ancient council practices").ToString();
                case "a failed trial of ancient council practices": return new TextObject("{=BC_CouncilIncident_ReasonFailedTrialOfAncientCouncilPractices}A failed trial of ancient council practices").ToString();
                case "the exposure of a court sycophant": return new TextObject("{=BC_CouncilIncident_ReasonExposureOfACourtSycophant}The exposure of a court sycophant").ToString();
                case "a destructive inquiry into court sycophancy": return new TextObject("{=BC_CouncilIncident_ReasonDestructiveInquiryIntoCourtSycophancy}A destructive inquiry into court sycophancy").ToString();
                case "a careful review of altered council reports": return new TextObject("{=BC_CouncilIncident_ReasonCarefulReviewOfAlteredCouncilReports}A careful review of altered council reports").ToString();
                case "an exposed review of council reports": return new TextObject("{=BC_CouncilIncident_ReasonExposedReviewOfCouncilReports}An exposed review of council reports").ToString();
                case "the successful arbitration of a council rivalry": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulArbitrationOfACouncilRivalry}The successful arbitration of a council rivalry").ToString();
                case "a failed arbitration between rival councillors": return new TextObject("{=BC_CouncilIncident_ReasonFailedArbitrationBetweenRivalCouncillors}A failed arbitration between rival councillors").ToString();
                case "a successful working accord between rival councillors": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulWorkingAccordBetweenRivalCouncillors}A successful working accord between rival councillors").ToString();
                case "a collapsed accord between rival councillors": return new TextObject("{=BC_CouncilIncident_ReasonCollapsedAccordBetweenRivalCouncillors}A collapsed accord between rival councillors").ToString();
                case "the exposure of a bribed council mediator": return new TextObject("{=BC_CouncilIncident_ReasonExposureOfABribedCouncilMediator}The exposure of a bribed council mediator").ToString();
                case "a widening scandal among council mediators": return new TextObject("{=BC_CouncilIncident_ReasonWideningScandalAmongCouncilMediators}A widening scandal among council mediators").ToString();
                case "a discreet inquiry into a bribed mediator": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetInquiryIntoABribedMediator}A discreet inquiry into a bribed mediator").ToString();
                case "a compromised inquiry into a bribed mediator": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedInquiryIntoABribedMediator}A compromised inquiry into a bribed mediator").ToString();
                case "a successful settlement of council deadlock": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulSettlementOfCouncilDeadlock}A successful settlement of council deadlock").ToString();
                case "a failed settlement imposed upon the council": return new TextObject("{=BC_CouncilIncident_ReasonFailedSettlementImposedUponTheCouncil}A failed settlement imposed upon the council").ToString();
                case "a successful limited council settlement": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedCouncilSettlement}A successful limited council settlement").ToString();
                case "a failed limited council settlement": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedCouncilSettlement}A failed limited council settlement").ToString();
                case "a successful hearing of faction demands": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulHearingOfFactionDemands}A successful hearing of faction demands").ToString();
                case "a disastrous hearing of faction demands": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousHearingOfFactionDemands}A disastrous hearing of faction demands").ToString();
                case "a successful limited faction hearing": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLimitedFactionHearing}A successful limited faction hearing").ToString();
                case "a failed limited faction hearing": return new TextObject("{=BC_CouncilIncident_ReasonFailedLimitedFactionHearing}A failed limited faction hearing").ToString();
                case "the restoration of compromised faction channels": return new TextObject("{=BC_CouncilIncident_ReasonRestorationOfCompromisedFactionChannels}The restoration of compromised faction channels").ToString();
                case "a failed purge of faction intermediaries": return new TextObject("{=BC_CouncilIncident_ReasonFailedPurgeOfFactionIntermediaries}A failed purge of faction intermediaries").ToString();
                case "a discreet restoration of faction channels": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetRestorationOfFactionChannels}A discreet restoration of faction channels").ToString();
                case "a suspicious replacement of faction intermediaries": return new TextObject("{=BC_CouncilIncident_ReasonSuspiciousReplacementOfFactionIntermediaries}A suspicious replacement of faction intermediaries").ToString();
                case "a successful public faction petition": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPublicFactionPetition}A successful public faction petition").ToString();
                case "a disorderly public faction petition": return new TextObject("{=BC_CouncilIncident_ReasonDisorderlyPublicFactionPetition}A disorderly public faction petition").ToString();
                case "a successful private faction deputation": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPrivateFactionDeputation}A successful private faction deputation").ToString();
                case "a failed private faction deputation": return new TextObject("{=BC_CouncilIncident_ReasonFailedPrivateFactionDeputation}A failed private faction deputation").ToString();
                case "a successful examination of concealed service ledgers": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulExaminationOfConcealedServiceLedgers}A successful examination of concealed service ledgers").ToString();
                case "a failed seizure of noble service ledgers": return new TextObject("{=BC_CouncilIncident_ReasonFailedSeizureOfNobleServiceLedgers}A failed seizure of noble service ledgers").ToString();
                case "a discreet examination of concealed service ledgers": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetExaminationOfConcealedServiceLedgers}A discreet examination of concealed service ledgers").ToString();
                case "an exposed examination of noble service ledgers": return new TextObject("{=BC_CouncilIncident_ReasonExposedExaminationOfNobleServiceLedgers}An exposed examination of noble service ledgers").ToString();
                case "a well-judged noble exemption": return new TextObject("{=BC_CouncilIncident_ReasonWellJudgedNobleExemption}A well-judged noble exemption").ToString();
                case "a divisive noble exemption": return new TextObject("{=BC_CouncilIncident_ReasonDivisiveNobleExemption}A divisive noble exemption").ToString();
                case "a narrow exemption from disputed service": return new TextObject("{=BC_CouncilIncident_ReasonNarrowExemptionFromDisputedService}A narrow exemption from disputed service").ToString();
                case "a failed compromise over disputed service": return new TextObject("{=BC_CouncilIncident_ReasonFailedCompromiseOverDisputedService}A failed compromise over disputed service").ToString();
                case "a disciplined royal assessment commission": return new TextObject("{=BC_CouncilIncident_ReasonDisciplinedRoyalAssessmentCommission}A disciplined royal assessment commission").ToString();
                case "a discredited royal assessment commission": return new TextObject("{=BC_CouncilIncident_ReasonDiscreditedRoyalAssessmentCommission}A discredited royal assessment commission").ToString();
                case "a useful provincial assessment": return new TextObject("{=BC_CouncilIncident_ReasonUsefulProvincialAssessment}A useful provincial assessment").ToString();
                case "a failed provincial assessment": return new TextObject("{=BC_CouncilIncident_ReasonFailedProvincialAssessment}A failed provincial assessment").ToString();
                case "the exposure of corrupt infrastructure contracts": return new TextObject("{=BC_CouncilIncident_ReasonExposureOfCorruptInfrastructureContracts}The exposure of corrupt infrastructure contracts").ToString();
                case "a failed purge of royal contractors": return new TextObject("{=BC_CouncilIncident_ReasonFailedPurgeOfRoyalContractors}A failed purge of royal contractors").ToString();
                case "a discreet reform of royal contracts": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetReformOfRoyalContracts}A discreet reform of royal contracts").ToString();
                case "an exposed inquiry into royal contracts": return new TextObject("{=BC_CouncilIncident_ReasonExposedInquiryIntoRoyalContracts}An exposed inquiry into royal contracts").ToString();
                case "a well-organized labor levy": return new TextObject("{=BC_CouncilIncident_ReasonWellOrganizedLaborLevy}A well-organized labor levy").ToString();
                case "a ruinous labor levy": return new TextObject("{=BC_CouncilIncident_ReasonRuinousLaborLevy}A ruinous labor levy").ToString();
                case "a successful local labor levy": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLocalLaborLevy}A successful local labor levy").ToString();
                case "a failed local labor levy": return new TextObject("{=BC_CouncilIncident_ReasonFailedLocalLaborLevy}A failed local labor levy").ToString();
                case "an efficient response to material shortages": return new TextObject("{=BC_CouncilIncident_ReasonEfficientResponseToMaterialShortages}An efficient response to material shortages").ToString();
                case "a wasteful response to material shortages": return new TextObject("{=BC_CouncilIncident_ReasonWastefulResponseToMaterialShortages}A wasteful response to material shortages").ToString();
                case "a successful priority supply program": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPrioritySupplyProgram}A successful priority supply program").ToString();
                case "a failed priority supply program": return new TextObject("{=BC_CouncilIncident_ReasonFailedPrioritySupplyProgram}A failed priority supply program").ToString();
                case "a successful cleansing of royal granaries": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulCleansingOfRoyalGranaries}A successful cleansing of royal granaries").ToString();
                case "a disastrous cleansing of royal granaries": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousCleansingOfRoyalGranaries}A disastrous cleansing of royal granaries").ToString();
                case "a successful treatment of major granaries": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulTreatmentOfMajorGranaries}A successful treatment of major granaries").ToString();
                case "a failed treatment of major granaries": return new TextObject("{=BC_CouncilIncident_ReasonFailedTreatmentOfMajorGranaries}A failed treatment of major granaries").ToString();
                case "a successful response to grain hoarding": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulResponseToGrainHoarding}A successful response to grain hoarding").ToString();
                case "a disastrous intervention in the grain market": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousInterventionInTheGrainMarket}A disastrous intervention in the grain market").ToString();
                case "successful contracts with grain merchants": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulContractsWithGrainMerchants}Successful contracts with grain merchants").ToString();
                case "failed contracts with grain merchants": return new TextObject("{=BC_CouncilIncident_ReasonFailedContractsWithGrainMerchants}Failed contracts with grain merchants").ToString();
                case "a successful reform of provision distribution": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulReformOfProvisionDistribution}A successful reform of provision distribution").ToString();
                case "a failed reform of provision distribution": return new TextObject("{=BC_CouncilIncident_ReasonFailedReformOfProvisionDistribution}A failed reform of provision distribution").ToString();
                case "successful inspections of vulnerable supply routes": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulInspectionsOfVulnerableSupplyRoutes}Successful inspections of vulnerable supply routes").ToString();
                case "failed inspections of vulnerable supply routes": return new TextObject("{=BC_CouncilIncident_ReasonFailedInspectionsOfVulnerableSupplyRoutes}Failed inspections of vulnerable supply routes").ToString();
                case "a persuasive reconstruction of the royal lineage": return new TextObject("{=BC_CouncilIncident_ReasonPersuasiveReconstructionOfTheRoyalLineage}A persuasive reconstruction of the royal lineage").ToString();
                case "an exposed fabrication of the royal lineage": return new TextObject("{=BC_CouncilIncident_ReasonExposedFabricationOfTheRoyalLineage}An exposed fabrication of the royal lineage").ToString();
                case "a useful private genealogy": return new TextObject("{=BC_CouncilIncident_ReasonUsefulPrivateGenealogy}A useful private genealogy").ToString();
                case "a compromised private genealogy": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedPrivateGenealogy}A compromised private genealogy").ToString();
                case "the containment of a compromised informant": return new TextObject("{=BC_CouncilIncident_ReasonContainmentOfACompromisedInformant}The containment of a compromised informant").ToString();
                case "the escape of a compromised informant": return new TextObject("{=BC_CouncilIncident_ReasonEscapeOfACompromisedInformant}The escape of a compromised informant").ToString();
                case "a successful deception of a loose-lipped informant": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulDeceptionOfALooseLippedInformant}A successful deception of a loose-lipped informant").ToString();
                case "a failed deception of a loose-lipped informant": return new TextObject("{=BC_CouncilIncident_ReasonFailedDeceptionOfALooseLippedInformant}A failed deception of a loose-lipped informant").ToString();
                case "a discreet arrangement with cooperative record-keepers": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetArrangementWithCooperativeRecordKeepers}A discreet arrangement with cooperative record-keepers").ToString();
                case "an exposed attempt to bribe archival clerks": return new TextObject("{=BC_CouncilIncident_ReasonExposedAttemptToBribeArchivalClerks}An exposed attempt to bribe archival clerks").ToString();
                case "a discreet approach to archival clerks": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetApproachToArchivalClerks}A discreet approach to archival clerks").ToString();
                case "a compromised approach to archival clerks": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedApproachToArchivalClerks}A compromised approach to archival clerks").ToString();
                case "a celebrated diplomatic embassy": return new TextObject("{=BC_CouncilIncident_ReasonCelebratedDiplomaticEmbassy}A celebrated diplomatic embassy").ToString();
                case "a humiliated diplomatic embassy": return new TextObject("{=BC_CouncilIncident_ReasonHumiliatedDiplomaticEmbassy}A humiliated diplomatic embassy").ToString();
                case "a well-judged diplomatic gift": return new TextObject("{=BC_CouncilIncident_ReasonWellJudgedDiplomaticGift}A well-judged diplomatic gift").ToString();
                case "an insulting diplomatic gift": return new TextObject("{=BC_CouncilIncident_ReasonInsultingDiplomaticGift}An insulting diplomatic gift").ToString();
                case "a successful season of diplomatic courtship": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulSeasonOfDiplomaticCourtship}A successful season of diplomatic courtship").ToString();
                case "a scandalous season of diplomatic courtship": return new TextObject("{=BC_CouncilIncident_ReasonScandalousSeasonOfDiplomaticCourtship}A scandalous season of diplomatic courtship").ToString();
                case "a discreet exchange of diplomatic introductions": return new TextObject("{=BC_CouncilIncident_ReasonDiscreetExchangeOfDiplomaticIntroductions}A discreet exchange of diplomatic introductions").ToString();
                case "a compromised exchange of diplomatic introductions": return new TextObject("{=BC_CouncilIncident_ReasonCompromisedExchangeOfDiplomaticIntroductions}A compromised exchange of diplomatic introductions").ToString();
                case "a dignified settlement of a border incident": return new TextObject("{=BC_CouncilIncident_ReasonDignifiedSettlementOfABorderIncident}A dignified settlement of a border incident").ToString();
                case "a humiliating apology for a border incident": return new TextObject("{=BC_CouncilIncident_ReasonHumiliatingApologyForABorderIncident}A humiliating apology for a border incident").ToString();
                case "a successful local border settlement": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulLocalBorderSettlement}A successful local border settlement").ToString();
                case "a failed local border settlement": return new TextObject("{=BC_CouncilIncident_ReasonFailedLocalBorderSettlement}A failed local border settlement").ToString();
                case "a successful banquet of reconciliation": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulBanquetOfReconciliation}A successful banquet of reconciliation").ToString();
                case "a disastrous banquet of reconciliation": return new TextObject("{=BC_CouncilIncident_ReasonDisastrousBanquetOfReconciliation}A disastrous banquet of reconciliation").ToString();
                case "a successful private supper with estranged vassals": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulPrivateSupperWithEstrangedVassals}A successful private supper with estranged vassals").ToString();
                case "a failed private supper with estranged vassals": return new TextObject("{=BC_CouncilIncident_ReasonFailedPrivateSupperWithEstrangedVassals}A failed private supper with estranged vassals").ToString();
                case "a masterful arbitration of noble hunting rights": return new TextObject("{=BC_CouncilIncident_ReasonMasterfulArbitrationOfNobleHuntingRights}A masterful arbitration of noble hunting rights").ToString();
                case "a failed arbitration of noble hunting rights": return new TextObject("{=BC_CouncilIncident_ReasonFailedArbitrationOfNobleHuntingRights}A failed arbitration of noble hunting rights").ToString();
                case "a successful compromise over noble hunting rights": return new TextObject("{=BC_CouncilIncident_ReasonSuccessfulCompromiseOverNobleHuntingRights}A successful compromise over noble hunting rights").ToString();
                case "a failed compromise over noble hunting rights": return new TextObject("{=BC_CouncilIncident_ReasonFailedCompromiseOverNobleHuntingRights}A failed compromise over noble hunting rights").ToString();
                case "a well-received ceremonial dignity": return new TextObject("{=BC_CouncilIncident_ReasonWellReceivedCeremonialDignity}A well-received ceremonial dignity").ToString();
                case "a ridiculed ceremonial dignity": return new TextObject("{=BC_CouncilIncident_ReasonRidiculedCeremonialDignity}A ridiculed ceremonial dignity").ToString();
                case "a tactful private commendation": return new TextObject("{=BC_CouncilIncident_ReasonTactfulPrivateCommendation}A tactful private commendation").ToString();
                case "a divisive private commendation": return new TextObject("{=BC_CouncilIncident_ReasonDivisivePrivateCommendation}A divisive private commendation").ToString();
                default: return reason ?? string.Empty;
            }
        }

        private static string GetOfficeName(PrivyCouncilOffice office, Kingdom kingdom)
        {
            return PrivyCouncilBehavior.GetLocalizedOfficeName(office, kingdom).ToString();
        }

        private static string GetCompetenceTier(float competence)
        {
            return CouncilCompetencePresentation.Tier(competence);
        }

        private static string GetSupportTier(float support)
        {
            switch (GetMetricTier(support))
            {
                case 0: return new TextObject("{=BC_Council_SupportIsolated}Isolated").ToString();
                case 1: return new TextObject("{=BC_Council_SupportContested}Contested").ToString();
                case 2: return new TextObject("{=BC_Council_SupportAccepted}Accepted").ToString();
                case 3: return new TextObject("{=BC_Council_SupportRespected}Respected").ToString();
                default: return new TextObject("{=BC_Council_SupportEntrenched}Entrenched").ToString();
            }
        }

        private static string GetControversyTier(float controversy)
        {
            switch (GetMetricTier(controversy))
            {
                case 0: return new TextObject("{=BC_Council_ControversyUnblemished}Unblemished").ToString();
                case 1: return new TextObject("{=BC_Council_ControversyQuestioned}Questioned").ToString();
                case 2: return new TextObject("{=BC_Council_ControversyContentious}Contentious").ToString();
                case 3: return new TextObject("{=BC_Council_ControversyScandalous}Scandalous").ToString();
                default: return new TextObject("{=BC_Council_ControversyRuinous}Ruinous").ToString();
            }
        }

        private static int GetMetricTier(float value)
        {
            if (value < 20f) return 0;
            if (value < 40f) return 1;
            if (value < 60f) return 2;
            if (value < 80f) return 3;
            return 4;
        }

        private static Color GetPositiveMetricColor(float value)
        {
            switch (GetMetricTier(value))
            {
                case 0: return CriticalColor;
                case 1: return PoorColor;
                case 2: return NeutralColor;
                case 3: return GoodColor;
                default: return ExcellentColor;
            }
        }

        private static Color GetNegativeMetricColor(float value)
        {
            switch (GetMetricTier(value))
            {
                case 0: return ExcellentColor;
                case 1: return GoodColor;
                case 2: return NeutralColor;
                case 3: return PoorColor;
                default: return CriticalColor;
            }
        }
    }
}
