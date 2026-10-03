using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    public sealed class HierarchyTitleNodeVM : ViewModel
    {
        private static readonly Color PlayerHeldColor = Color.ConvertStringToColor("#82E06AFF");
        private static readonly Color RealmHeldColor = Color.ConvertStringToColor("#F1D8A4FF");
        private static readonly Color DividedColor = Color.ConvertStringToColor("#FF8C00FF");
        private readonly Action<HierarchyTitleNodeVM> _onSelect;
        private readonly Action<string> _onHierarchyChanged;
        private readonly Clan _deFactoHolder;
        private readonly BasicTooltipViewModel _tooltip;
        private bool _isSelected;
        private bool _isClaimEnabled;
        private bool _isUsurpEnabled;
        private bool _isFormationVisible;
        private bool _isFormationEnabled;
        private bool _isServiceEnabled;
        private bool _isGrantEnabled;
        private bool _isRevocationEnabled;
        private bool _isRenameEnabled;
        private bool _isDissolutionEnabled;
        private HintViewModel _claimHint;
        private HintViewModel _usurpHint;
        private HintViewModel _formationHint;
        private HintViewModel _serviceHint;
        private HintViewModel _grantHint;
        private HintViewModel _revocationHint;
        private HintViewModel _renameHint;
        private HintViewModel _dissolutionHint;
        private bool _hasChildTitles;
        private int _rightMargin = 0;

        public HierarchyTitleNodeVM(
            FeudalTitleRecord title,
            FeudalTitleBehavior titleBehavior,
            Kingdom hierarchyKingdom,
            FeudalClaimFabricationRecord playerFabrication,
            Action<HierarchyTitleNodeVM> onSelect,
            Action<string> onHierarchyChanged)
        {
            Title = title;
            _onSelect = onSelect;
            _onHierarchyChanged = onHierarchyChanged;
            Branch = new MBBindingList<HierarchyTitleNodeVM>();
            _deFactoHolder = ResolveClan(title?.DeFactoHolderClanId);
            Clan deJureHolder = ResolveClan(title?.DeJureHolderClanId);
            Name = FormatTitleName(title);
            TitleColor = CalculateTitleColor(hierarchyKingdom, deJureHolder, _deFactoHolder);
            Tier = GetTierName(title?.TitleType ?? FeudalTitleType.Barony);
            HolderName = _deFactoHolder?.Name?.ToString() ?? new TextObject("{=BC_Hierarchy_Vacant}Vacant").ToString();
            ParentName = FormatTitleName(titleBehavior?.GetTitle(title?.ParentTitleId));
            IsContested = title != null && title.DeJureHolderClanId != title.DeFactoHolderClanId;
            Status = FeudalTitleDisplayHelper.GetHierarchyTenure(title);

            if (_deFactoHolder?.Banner != null)
                BannerVisual = new BannerImageIdentifierVM(_deFactoHolder.Banner, true);
            if (_deFactoHolder?.Leader?.CharacterObject != null)
                PortraitVisual = new CharacterImageIdentifierVM(BellumCivile.UI.PortraitAppearance.Create(_deFactoHolder.Leader.CharacterObject));

            if (playerFabrication != null && playerFabrication.TargetTitleId == title?.TitleId)
            {
                HasFabrication = true;
                int percent = (int)Math.Round(playerFabrication.Progress * 100f);
                float remaining = playerFabrication.DailyProgressDelta > 0f
                    ? Math.Max(0f, (1f - playerFabrication.Progress) / playerFabrication.DailyProgressDelta)
                    : 0f;
                TextObject progress = new TextObject("{=BC_Hierarchy_FabricationProgress}Claim fabrication: {PROGRESS}% ({DAYS} days remaining)");
                progress.SetTextVariable("PROGRESS", percent);
                progress.SetTextVariable("DAYS", (int)Math.Ceiling(remaining));
                FabricationText = progress.ToString();
            }
            else
            {
                FabricationText = string.Empty;
            }

            _tooltip = new BasicTooltipViewModel(() => BuildTooltip(
                titleBehavior,
                title,
                ResolveClan(title?.DeJureHolderClanId),
                ResolveClan(title?.DeFactoHolderClanId),
                playerFabrication));
            RefreshPlayerActions();
        }

        public FeudalTitleRecord Title { get; }
        [DataSourceProperty] public MBBindingList<HierarchyTitleNodeVM> Branch { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public Color TitleColor { get; }
        [DataSourceProperty] public string Tier { get; }
        [DataSourceProperty] public string HolderName { get; }
        [DataSourceProperty] public string ParentName { get; }
        [DataSourceProperty] public string Status { get; }
        [DataSourceProperty] public bool IsContested { get; }
        [DataSourceProperty] public bool HasFabrication { get; }
        [DataSourceProperty] public string FabricationText { get; }
        [DataSourceProperty] public BannerImageIdentifierVM BannerVisual { get; }
        [DataSourceProperty] public CharacterImageIdentifierVM PortraitVisual { get; }
        [DataSourceProperty] public string ClaimText => new TextObject("{=BC_Hierarchy_ActionClaim}Claim").ToString();
        [DataSourceProperty] public string UsurpText => new TextObject("{=BC_Hierarchy_ActionUsurp}Usurp").ToString();
        [DataSourceProperty] public string ChallengeText => new TextObject("{=BC_Hierarchy_ActionChallenge}Challenge").ToString();
        [DataSourceProperty] public bool IsChallengeVisible { get; private set; }
        [DataSourceProperty] public bool IsChallengeEnabled { get; private set; }
        [DataSourceProperty] public HintViewModel ChallengeHint { get; private set; }
        [DataSourceProperty] public string RenounceText => new TextObject("{=BC_Hierarchy_ActionRenounce}Renounce").ToString();
        [DataSourceProperty] public bool IsRenounceVisible { get; private set; }
        [DataSourceProperty] public bool IsRenounceEnabled { get; private set; }
        [DataSourceProperty] public HintViewModel RenounceHint { get; private set; }
        [DataSourceProperty] public int ActionMenuHeight => 288 + (IsChallengeVisible ? 34 : 0) + (IsRenounceVisible ? 34 : 0);

        public void ExecuteRenounce()
        {
            RefreshPlayerActions();
            if (!IsRenounceEnabled) return;
            var description = new TextObject("{=BC_Renounce_Confirm}Your house will relinquish its existing claims to {TITLE_NAME}. Other claims remain unchanged. Recovering this claim would require acquiring it anew.\n\nRenounce these claims?");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            InformationManager.ShowInquiry(new InquiryData(RenounceText, description.ToString(), true, true,
                new TextObject("{=BC_Renounce_Accept}Renounce Claim").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(), () =>
                {
                    if (!FeudalTitlePlayerActionService.TryExecuteRenunciation(Title, out var reason))
                    {
                        ShowActionFailure(reason?.ToString());
                        RefreshPlayerActions();
                        return;
                    }
                    var message = new TextObject("{=BC_Renounce_Success}Your house has relinquished its claims to {TITLE_NAME}.");
                    message.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
                    InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Green));
                    RefreshPlayerActions();
                    _onHierarchyChanged?.Invoke(Title?.TitleId);
                }, null), true);
        }

        public void ExecuteChallenge()
        {
            RefreshPlayerActions();
            if (!IsChallengeEnabled) return;
            InformationManager.ShowInquiry(new InquiryData(ChallengeText,
                new TextObject("{=BC_PlayerChallenge_Confirm}Call upon the houses of the realm to support your claim to the crown? Once their answers arrive, you may send an ultimatum or withdraw, even if outmatched. Your coalition must hold a town or castle. Failure or withdrawal will delay another challenge.").ToString(),
                true, true, ChallengeText, new TextObject("{=BC_PlayerChallenge_Cancel}Cancel").ToString(),
                () =>
                {
                    SuccessionChallengeBehavior.Instance?.StartPlayerChallenge(Title);
                    RefreshPlayerActions();
                }, null), true);
        }
        [DataSourceProperty] public string RevokeText => new TextObject("{=BC_Hierarchy_ActionRevoke}Revoke").ToString();
        [DataSourceProperty] public string FormText => new TextObject("{=BC_Hierarchy_ActionForm}Form").ToString();
        [DataSourceProperty] public string ServiceText => new TextObject("{=BC_Hierarchy_ActionService}Service").ToString();
        [DataSourceProperty] public string GrantText => new TextObject("{=BC_Hierarchy_ActionGrant}Grant").ToString();
        [DataSourceProperty] public string RenameText => new TextObject("{=BC_Hierarchy_ActionRename}Rename").ToString();
        [DataSourceProperty] public string DissolveText => new TextObject("{=BC_Hierarchy_ActionDissolve}Dissolve").ToString();

        [DataSourceProperty]
        public int RightMargin
        {
            get => _rightMargin;
            private set { if (value != _rightMargin) { _rightMargin = value; OnPropertyChangedWithValue(value, nameof(RightMargin)); } }
        }

        public void FinalizeBranchLayout()
        {
            _hasChildTitles = Branch.Count > 0;
            RefreshRightMargin();
        }

        public void ExecuteSelect() => _onSelect?.Invoke(this);

        [DataSourceProperty]
        public string OptionsText => new TextObject("{=BC_Hierarchy_TitleOptions}Options").ToString();

        public void ExecuteOpenClan()
        {
            if (_deFactoHolder != null)
                Campaign.Current?.EncyclopediaManager?.GoToLink(_deFactoHolder.EncyclopediaLink);
        }

        public void ExecuteBeginHint() => _tooltip?.ExecuteBeginHint();
        public void ExecuteEndHint() => _tooltip?.ExecuteEndHint();

        public void ExecuteClaim()
        {
            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            FeudalFabricationPreview preview = behavior?.GetFabricationPreview(Clan.PlayerClan, Title);
            if (preview == null || !preview.CanStart)
            {
                RefreshPlayerActions();
                return;
            }

            TextObject description = new TextObject("{=BC_Hierarchy_ClaimConfirmDesc}Fabricating a claim to the {TITLE_NAME} will cost {GOLD_COST} denars and {INFLUENCE_COST} influence. The work is expected to take roughly {DAYS} days. Proceed?");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("GOLD_COST", preview.GoldCost);
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));
            description.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(preview.EstimatedDays)));

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_ClaimConfirmTitle}Fabricate Claim").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionClaim}Claim").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                () => StartPlayerFabrication(),
                null), true);
        }

        public void ExecuteUsurp()
        {
            FeudalUsurpationPreview preview = FeudalTitlePlayerActionService.GetUsurpationPreview(Clan.PlayerClan, Title);
            if (!preview.CanUsurp)
            {
                RefreshPlayerActions();
                return;
            }

            TextObject description = preview.IsLawfulAssumption
                ? new TextObject("{=BC_Hierarchy_AssumeConfirmDesc}Assuming the vacant {TITLE_NAME} will cost {GOLD_COST} denars and {INFLUENCE_COST} influence. Your house already owns the lawful title and will now exercise its authority. Proceed?")
                : new TextObject("{=BC_Hierarchy_UsurpConfirmDesc}Usurping the {TITLE_NAME} will cost {GOLD_COST} denars and {INFLUENCE_COST} influence, replacing its current legal holder with your clan. Proceed?");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("GOLD_COST", preview.GoldCost);
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));

            FeudalSovereignElevationPreview elevation = preview.SovereignElevation;
            if (elevation?.IsRequired == true && elevation.HasExternalHoldings)
            {
                TextObject separationDescription = new TextObject("{=BC_Hierarchy_UsurpSovereignChooseDesc}Usurping the {TITLE_NAME} will cost {GOLD_COST} denars and {INFLUENCE_COST} influence. A vassal cannot hold a title equal to or higher than their liege's sovereign title. Choose whether to relinquish holdings outside the new title's legal hierarchy and depart peacefully, or retain them and fight your former realm.");
                separationDescription.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
                separationDescription.SetTextVariable("GOLD_COST", preview.GoldCost);
                separationDescription.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));

                List<InquiryElement> choices = new List<InquiryElement>
                {
                    new InquiryElement(
                        FeudalSovereignElevationChoice.PeacefulSeparation,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignPeaceful}Relinquish and Depart").ToString(),
                        null,
                        true,
                        BuildPeacefulSovereignSeparationHint(elevation)),
                    new InquiryElement(
                        FeudalSovereignElevationChoice.RetainHoldingsAndRebel,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignDefy}Retain Holdings and Defy").ToString(),
                        null,
                        true,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignDefyHint}Keep every title and settlement your clan holds. Your new realm will immediately enter a war of separation against its former liege, and relations with the old realm will suffer accordingly.").ToString())
                };
                MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                    new TextObject("{=BC_Hierarchy_UsurpSovereignTitle}Claim a Sovereign Title").ToString(),
                    separationDescription.ToString(),
                    choices,
                    true,
                    1,
                    1,
                    new TextObject("{=BC_Hierarchy_ActionUsurp}Usurp").ToString(),
                    new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                    selected =>
                    {
                        if (selected?.FirstOrDefault()?.Identifier is FeudalSovereignElevationChoice choice)
                            CompletePlayerUsurpation(choice);
                    },
                    null);
                MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
                return;
            }

            if (elevation?.IsRequired == true)
            {
                description = new TextObject("{=BC_Hierarchy_UsurpSovereignDirectDesc}Usurping the {TITLE_NAME} will cost {GOLD_COST} denars and {INFLUENCE_COST} influence. Because this title equals or outranks your liege's sovereign title, your clan will peacefully depart to rule it as an independent realm. Proceed?");
                description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
                description.SetTextVariable("GOLD_COST", preview.GoldCost);
                description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));
            }

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_UsurpConfirmTitle}Usurp Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionUsurp}Usurp").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                () => CompletePlayerUsurpation(elevation?.IsRequired == true
                    ? FeudalSovereignElevationChoice.PeacefulSeparation
                    : FeudalSovereignElevationChoice.NotApplicable),
                null), true);
        }

        public void ExecuteRevoke()
        {
            FeudalRevocationPreview preview = FeudalTitlePlayerActionService.GetRevocationPreview(Clan.PlayerClan, Title);
            if (!preview.CanRevoke)
            {
                RefreshPlayerActions();
                return;
            }

            TextObject description = preview.HolderLikelyDefies
                ? new TextObject("{=BC_Hierarchy_RevokeConfirmDefyDesc}Demand the {TITLE_NAME} from the {HOLDER_CLAN} by right of claim? This will cost {INFLUENCE_COST} influence and damage relations. They are likely to refuse, immediately turning the dispute into a private feud war.")
                : new TextObject("{=BC_Hierarchy_RevokeConfirmDesc}Demand the {TITLE_NAME} from the {HOLDER_CLAN} by right of claim? This will cost {INFLUENCE_COST} influence and damage relations. They are expected to accept the revocation.");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("HOLDER_CLAN", preview.HolderClan?.Name ?? new TextObject("?"));
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_RevokeConfirmTitle}Revoke Claimed Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionRevoke}Revoke").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                CompletePlayerRevocation,
                null), true);
        }

        public void ExecuteForm()
        {
            FeudalFormationPreview preview = FeudalTitlePlayerActionService.GetFormationPreview(Clan.PlayerClan, Title);
            if (!preview.CanForm)
            {
                RefreshPlayerActions();
                return;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            List<InquiryElement> choices = preview.SelectableTitles
                .Where(title => title != null && title.TitleId != Title.TitleId)
                .Select(title => new InquiryElement(
                    title,
                    FormatTitleName(title, Clan.PlayerClan),
                    null,
                    true,
                    BuildFormationChildHint(titleBehavior, title)))
                .ToList();
            int minimumAdditionalTitles = Math.Max(1, FeudalTitleBehavior.GetMinimumFormationChildren() - 1);
            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_Hierarchy_FormChooseTitle}Choose Subordinate Titles").ToString(),
                BuildFormationSelectionDescription(preview.TargetType),
                choices,
                true,
                minimumAdditionalTitles,
                choices.Count,
                new TextObject("{=BC_UI_Continue}Continue").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    List<string> childTitleIds = new List<string> { Title.TitleId };
                    childTitleIds.AddRange(selected?
                        .Select(element => element.Identifier as FeudalTitleRecord)
                        .Where(title => title != null)
                        .Select(title => title.TitleId) ?? Enumerable.Empty<string>());
                    BeginFormationNaming(titleBehavior, preview.TargetType, childTitleIds);
                },
                null);
            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        public void ExecuteDissolve()
        {
            FeudalDissolutionPreview preview = FeudalTitlePlayerActionService.GetDissolutionPreview(Clan.PlayerClan, Title);
            if (!preview.CanDissolve)
            {
                RefreshPlayerActions();
                return;
            }

            TextObject description = new TextObject("{=BC_Hierarchy_DissolveConfirmDesc}Dissolve the {TITLE_NAME} for {INFLUENCE_COST} influence? Its immediate subordinate titles will return to the dissolved title's liege. This cannot be undone.");
            string resentmentWarning = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()
                ?.ReorganizationWarning(Clan.PlayerClan, new[] { Title.TitleId }) ?? string.Empty;
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_DissolveConfirmTitle}Dissolve Title").ToString(),
                description.ToString() + resentmentWarning,
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionDissolve}Dissolve").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                CompletePlayerDissolution,
                null), true);
        }

        public void ExecuteRename()
        {
            FeudalRenamePreview preview = FeudalTitlePlayerActionService.GetRenamePreview(Clan.PlayerClan, Title);
            if (!preview.CanRename)
            {
                RefreshPlayerActions();
                return;
            }

            string currentName = !string.IsNullOrWhiteSpace(Title?.Name)
                ? new TextObject(Title.Name).ToString()
                : string.Empty;
            TextObject description = new TextObject("{=BC_Hierarchy_RenameDesc}Choose a new name for the {TITLE_NAME}.");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            InformationManager.ShowTextInquiry(new TextInquiryData(
                new TextObject("{=BC_Hierarchy_RenameTitle}Rename Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionRename}Rename").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                CompletePlayerRename,
                null,
                false,
                enteredName =>
                {
                    bool valid = FeudalTitleBehavior.IsValidCustomTitleName(enteredName, out _);
                    string validationText = valid
                        ? string.Empty
                        : new TextObject("{=BC_Hierarchy_FormNameInvalid}Enter a name of no more than 64 characters without braces or line breaks.").ToString();
                    return new Tuple<bool, string>(valid, validationText);
                },
                string.Empty,
                currentName), true);
        }

        public void ExecuteService()
        {
            FeudalServicePreview preview = FeudalTitlePlayerActionService.GetServicePreview(Clan.PlayerClan, Title);
            if (!preview.CanChange)
            {
                RefreshPlayerActions();
                return;
            }

            List<InquiryElement> choices = Enum.GetValues(typeof(FeudalServiceLevel))
                .Cast<FeudalServiceLevel>()
                .OrderBy(level => (int)level)
                .Select(level => new InquiryElement(
                    level,
                    FeudalTitlePlayerActionService.GetServiceLevelName(level).ToString(),
                    null,
                    true,
                    BuildServiceChoiceHint(level, preview.CurrentLevel)))
                .ToList();

            TextObject description = new TextObject("{=BC_Hierarchy_ServiceChooseDesc}Set the service owed by the {TITLE_NAME} to its immediate liege, the {LIEGE_TITLE}.");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("LIEGE_TITLE", new TextObject("{=!}" + FormatTitleName(preview.ParentTitle)));
            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_Hierarchy_ServiceChooseTitle}Set Feudal Service").ToString(),
                description.ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_Hierarchy_ActionService}Service").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    if (selected?.FirstOrDefault()?.Identifier is FeudalServiceLevel level)
                        CompletePlayerServiceChange(level);
                },
                null);
            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        public void ExecuteGrant()
        {
            FeudalGrantPreview preview = FeudalTitlePlayerActionService.GetGrantPreview(Clan.PlayerClan, Title);
            if (!preview.CanOpen)
            {
                RefreshPlayerActions();
                return;
            }

            List<InquiryElement> choices = preview.Recipients
                .Select(recipient => new InquiryElement(
                    recipient,
                    recipient.Clan?.Name?.ToString() ?? new TextObject("{=BC_Hierarchy_UnknownClan}Unknown Clan").ToString(),
                    null,
                    recipient.CanReceive,
                    BuildGrantRecipientHint(recipient)))
                .ToList();

            TextObject description = new TextObject("{=BC_Hierarchy_GrantChooseDesc}Choose which vassal should receive the {TITLE_NAME}. The grant will cost {INFLUENCE_COST} influence.");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_Hierarchy_GrantChooseTitle}Grant Title").ToString(),
                description.ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_Hierarchy_ActionGrant}Grant").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    FeudalGrantRecipientPreview recipient = selected?.FirstOrDefault()?.Identifier as FeudalGrantRecipientPreview;
                    if (recipient?.Clan != null)
                        ShowGrantConfirmation(recipient);
                },
                null);
            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected)
                    return;
                _isSelected = value;
                OnPropertyChangedWithValue(value, nameof(IsSelected));
                RefreshRightMargin();
                if (value)
                    RefreshPlayerActions();
            }
        }

        private void RefreshRightMargin()
        {
            RightMargin = 0;
        }

        [DataSourceProperty]
        public bool IsClaimEnabled
        {
            get => _isClaimEnabled;
            private set { if (value != _isClaimEnabled) { _isClaimEnabled = value; OnPropertyChangedWithValue(value, nameof(IsClaimEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsUsurpEnabled
        {
            get => _isUsurpEnabled;
            private set { if (value != _isUsurpEnabled) { _isUsurpEnabled = value; OnPropertyChangedWithValue(value, nameof(IsUsurpEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsRevocationEnabled
        {
            get => _isRevocationEnabled;
            private set { if (value != _isRevocationEnabled) { _isRevocationEnabled = value; OnPropertyChangedWithValue(value, nameof(IsRevocationEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsFormationVisible
        {
            get => _isFormationVisible;
            private set { if (value != _isFormationVisible) { _isFormationVisible = value; OnPropertyChangedWithValue(value, nameof(IsFormationVisible)); } }
        }

        [DataSourceProperty]
        public bool IsFormationEnabled
        {
            get => _isFormationEnabled;
            private set { if (value != _isFormationEnabled) { _isFormationEnabled = value; OnPropertyChangedWithValue(value, nameof(IsFormationEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsServiceEnabled
        {
            get => _isServiceEnabled;
            private set { if (value != _isServiceEnabled) { _isServiceEnabled = value; OnPropertyChangedWithValue(value, nameof(IsServiceEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsGrantEnabled
        {
            get => _isGrantEnabled;
            private set { if (value != _isGrantEnabled) { _isGrantEnabled = value; OnPropertyChangedWithValue(value, nameof(IsGrantEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsRenameEnabled
        {
            get => _isRenameEnabled;
            private set { if (value != _isRenameEnabled) { _isRenameEnabled = value; OnPropertyChangedWithValue(value, nameof(IsRenameEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsDissolutionEnabled
        {
            get => _isDissolutionEnabled;
            private set { if (value != _isDissolutionEnabled) { _isDissolutionEnabled = value; OnPropertyChangedWithValue(value, nameof(IsDissolutionEnabled)); } }
        }

        [DataSourceProperty]
        public HintViewModel ClaimHint
        {
            get => _claimHint;
            private set { if (value != _claimHint) { _claimHint = value; OnPropertyChangedWithValue(value, nameof(ClaimHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel UsurpHint
        {
            get => _usurpHint;
            private set { if (value != _usurpHint) { _usurpHint = value; OnPropertyChangedWithValue(value, nameof(UsurpHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel RevocationHint
        {
            get => _revocationHint;
            private set { if (value != _revocationHint) { _revocationHint = value; OnPropertyChangedWithValue(value, nameof(RevocationHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel FormationHint
        {
            get => _formationHint;
            private set { if (value != _formationHint) { _formationHint = value; OnPropertyChangedWithValue(value, nameof(FormationHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel ServiceHint
        {
            get => _serviceHint;
            private set { if (value != _serviceHint) { _serviceHint = value; OnPropertyChangedWithValue(value, nameof(ServiceHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel GrantHint
        {
            get => _grantHint;
            private set { if (value != _grantHint) { _grantHint = value; OnPropertyChangedWithValue(value, nameof(GrantHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel RenameHint
        {
            get => _renameHint;
            private set { if (value != _renameHint) { _renameHint = value; OnPropertyChangedWithValue(value, nameof(RenameHint)); } }
        }

        [DataSourceProperty]
        public HintViewModel DissolutionHint
        {
            get => _dissolutionHint;
            private set { if (value != _dissolutionHint) { _dissolutionHint = value; OnPropertyChangedWithValue(value, nameof(DissolutionHint)); } }
        }

        private void StartPlayerFabrication()
        {
            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            string reason = behavior == null ? "title behavior unavailable" : null;
            if (behavior == null || !behavior.TryStartFabrication(Clan.PlayerClan, Title, true, out _, out reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            RefreshPlayerActions();
        }

        private void CompletePlayerUsurpation(FeudalSovereignElevationChoice elevationChoice)
        {
            if (!FeudalTitlePlayerActionService.TryExecuteUsurpation(Clan.PlayerClan, Title, elevationChoice, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            _onHierarchyChanged?.Invoke(Title?.TitleId);
        }

        private void CompletePlayerRevocation()
        {
            FeudalRevocationPreview preview = FeudalTitlePlayerActionService.GetRevocationPreview(Clan.PlayerClan, Title);
            if (!FeudalTitlePlayerActionService.TryExecuteRevocation(Clan.PlayerClan, Title, out bool holderDefied, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            TextObject message = holderDefied
                ? new TextObject("{=BC_Hierarchy_RevokeDefied}The {HOLDER_CLAN} has refused your demand over the {TITLE_NAME}. The dispute is turning into open feud.")
                : new TextObject("{=BC_Hierarchy_RevokeCompleted}The {TITLE_NAME} has been revoked into your hands.");
            message.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            message.SetTextVariable("HOLDER_CLAN", preview?.HolderClan?.Name ?? new TextObject("?"));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), holderDefied ? Colors.Red : Colors.Green));
            _onHierarchyChanged?.Invoke(Title?.TitleId);
        }

        private void BeginFormationNaming(FeudalTitleBehavior behavior, FeudalTitleType targetType, List<string> childTitleIds)
        {
            if (behavior == null)
            {
                ShowActionFailure("title behavior unavailable");
                return;
            }

            string defaultName = behavior.GetDefaultFormationTitleRoot(Title, targetType);
            if (!behavior.TryBuildFormationCandidate(
                Clan.PlayerClan,
                targetType,
                childTitleIds,
                Title.TitleId,
                defaultName,
                allowReorganization: true,
                out _,
                out string reason))
            {
                ShowActionFailure(reason);
                return;
            }

            string titleNoun = FeudalTitleDisplayHelper.GetLandedTitleNoun(
                targetType,
                Clan.PlayerClan,
                Clan.PlayerClan?.Kingdom,
                Title?.FallbackCultureRef);
            TextObject description = new TextObject("{=BC_Hierarchy_FormNameDesc}How will your {TITLE_NOUN} be called?");
            description.SetTextVariable("TITLE_NOUN", new TextObject("{=!}" + titleNoun));

            InformationManager.ShowTextInquiry(new TextInquiryData(
                new TextObject("{=BC_Hierarchy_FormNameTitle}Name the New Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_UI_Continue}Continue").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                enteredName =>
                {
                    if (!behavior.TryBuildFormationCandidate(
                        Clan.PlayerClan,
                        targetType,
                        childTitleIds,
                        Title.TitleId,
                        enteredName,
                        allowReorganization: true,
                        out FeudalTitleFormationCandidate candidate,
                        out string buildReason))
                    {
                        ShowActionFailure(buildReason);
                        return;
                    }

                    ShowFormationConfirmation(candidate);
                },
                null,
                false,
                enteredName =>
                {
                    bool valid = FeudalTitleBehavior.IsValidCustomTitleName(enteredName, out _);
                    string validationText = valid
                        ? string.Empty
                        : new TextObject("{=BC_Hierarchy_FormNameInvalid}Enter a name of no more than 64 characters without braces or line breaks.").ToString();
                    return new Tuple<bool, string>(valid, validationText);
                },
                string.Empty,
                defaultName), true);
        }

        private void ShowFormationConfirmation(FeudalTitleFormationCandidate candidate)
        {
            if (candidate == null)
                return;

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalSovereignElevationPreview elevation = behavior?.GetFormationSovereignElevationPreview(
                Clan.PlayerClan,
                candidate);
            if (elevation?.IsRequired == true && elevation.HasExternalHoldings)
            {
                TextObject choiceDescription = new TextObject("{=BC_Hierarchy_FormSovereignChooseDesc}{FORMATION_DESC}\n\nThis new title equals or outranks your liege's sovereign title. Choose whether to relinquish holdings outside its legal hierarchy and depart peacefully, or retain them and fight your former realm.");
                choiceDescription.SetTextVariable(
                    "FORMATION_DESC",
                    new TextObject("{=!}" + BuildFormationConfirmationDescription(candidate, behavior, null)));

                List<InquiryElement> choices = new List<InquiryElement>
                {
                    new InquiryElement(
                        FeudalSovereignElevationChoice.PeacefulSeparation,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignPeaceful}Relinquish and Depart").ToString(),
                        null,
                        true,
                        BuildPeacefulSovereignSeparationHint(elevation)),
                    new InquiryElement(
                        FeudalSovereignElevationChoice.RetainHoldingsAndRebel,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignDefy}Retain Holdings and Defy").ToString(),
                        null,
                        true,
                        new TextObject("{=BC_Hierarchy_UsurpSovereignDefyHint}Keep every title and settlement your clan holds. Your new realm will immediately enter a war of separation against its former liege, and relations with the old realm will suffer accordingly.").ToString())
                };

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    new TextObject("{=BC_Hierarchy_FormSovereignTitle}Form a Sovereign Title").ToString(),
                    choiceDescription.ToString(),
                    choices,
                    true,
                    1,
                    1,
                    new TextObject("{=BC_Hierarchy_ActionForm}Form").ToString(),
                    new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                    selected =>
                    {
                        if (selected?.FirstOrDefault()?.Identifier is FeudalSovereignElevationChoice choice)
                            CompletePlayerFormation(candidate, choice);
                    },
                    null), true);
                return;
            }

            TextObject sovereignWarning = elevation?.IsRequired == true
                ? new TextObject("{=BC_Hierarchy_FormSovereignDirectWarning}\n\nThis title equals or outranks your liege's sovereign title. Your clan will peacefully depart to rule it as an independent realm.")
                : null;
            TextObject description = BuildFormationConfirmationDescription(candidate, behavior, sovereignWarning);
            FeudalSovereignElevationChoice elevationChoice = elevation?.IsRequired == true
                ? FeudalSovereignElevationChoice.PeacefulSeparation
                : FeudalSovereignElevationChoice.NotApplicable;
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_FormConfirmTitle}Form Higher Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionForm}Form").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                () => CompletePlayerFormation(candidate, elevationChoice),
                null), true);
        }

        private static TextObject BuildFormationConfirmationDescription(
            FeudalTitleFormationCandidate candidate,
            FeudalTitleBehavior behavior,
            TextObject sovereignWarning)
        {
            string childTitles = string.Join(", ", candidate.ChildTitleIds
                .Select(id => FormatTitleName(behavior?.GetTitle(id), Clan.PlayerClan))
                .Where(name => !string.IsNullOrWhiteSpace(name)));
            TextObject description = new TextObject("{=BC_Hierarchy_FormConfirmDesc}Form the {TITLE_NAME} from {CHILD_TITLES}? This will cost {GOLD_COST} denars and reward your clan with {INFLUENCE_REWARD} influence.{REORGANIZATION}{SUCCESSION_WARNING}{SOVEREIGN_WARNING}");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatCandidateTitleName(candidate)));
            description.SetTextVariable("CHILD_TITLES", new TextObject("{=!}" + childTitles));
            description.SetTextVariable("GOLD_COST", candidate.GoldCost);
            description.SetTextVariable("INFLUENCE_REWARD", (int)Math.Round(candidate.InfluenceReward));
            description.SetTextVariable("REORGANIZATION", BuildFormationReorganizationWarning(behavior, candidate));
            description.SetTextVariable("SUCCESSION_WARNING", candidate.HasIndependentSuccessionRisk
                ? new TextObject("{=BC_Hierarchy_FormSuccessionWarning}\n\nWarning: this creates another sovereign title of equal rank. Without a higher crown, succession may divide these realms among different heirs.")
                : new TextObject(string.Empty));
            description.SetTextVariable("SOVEREIGN_WARNING", sovereignWarning ?? new TextObject(string.Empty));
            return new TextObject("{=!}" + description.ToString() + behavior.ReorganizationWarning(
                Clan.PlayerClan, candidate.ChildTitleIds.Concat(candidate.AffectedParentTitleIds)));
        }

        private static TextObject BuildFormationReorganizationWarning(
            FeudalTitleBehavior behavior,
            FeudalTitleFormationCandidate candidate)
        {
            if (behavior == null
                || candidate == null
                || candidate.Mode == FeudalTitleFormationMode.Consolidation)
            {
                return new TextObject(string.Empty);
            }

            string movedTitles = string.Join("; ", candidate.Moves
                .Where(move => !string.IsNullOrWhiteSpace(move.OldDeJureParentTitleId)
                    || !string.IsNullOrWhiteSpace(move.OldDeFactoParentTitleId))
                .Select(move =>
                {
                    FeudalTitleRecord child = behavior.GetTitle(move.ChildTitleId);
                    FeudalTitleRecord oldParent = behavior.GetTitle(move.OldDeJureParentTitleId)
                        ?? behavior.GetTitle(move.OldDeFactoParentTitleId);
                    return $"{FormatTitleName(child, ResolveClan(move.HolderClanId))} from {FormatTitleName(oldParent)}";
                })
                .Where(text => !string.IsNullOrWhiteSpace(text)));

            TextObject warning = new TextObject("{=BC_Hierarchy_FormReorganizationWarning}\n\nThis reorganizes the existing hierarchy: {MOVED_TITLES}. Current vassal holders retain their titles and service beneath the new parent.");
            warning.SetTextVariable("MOVED_TITLES", new TextObject("{=!}" + movedTitles));
            return warning;
        }

        private static string BuildPeacefulSovereignSeparationHint(FeudalSovereignElevationPreview elevation)
        {
            return new TextObject("{=BC_Hierarchy_UsurpSovereignPeacefulHint}Return {SETTLEMENT_COUNT} settlements and {UPPER_TITLE_COUNT} upper titles outside the new title's legal hierarchy to your former realm. The settlements will enter normal land-grant votes, the upper titles will pass to the old ruling house, your claims to all relinquished titles will be removed, and no war will begin.")
                .SetTextVariable("SETTLEMENT_COUNT", elevation?.ExternalSettlementCount ?? 0)
                .SetTextVariable("UPPER_TITLE_COUNT", elevation?.ExternalUpperTitleCount ?? 0)
                .ToString();
        }

        private void CompletePlayerFormation(
            FeudalTitleFormationCandidate candidate,
            FeudalSovereignElevationChoice elevationChoice)
        {
            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            string reason = behavior == null ? "title behavior unavailable" : null;
            FeudalTitleRecord formedTitle = null;
            if (behavior == null
                || !behavior.TryFormTitle(
                    Clan.PlayerClan,
                    candidate,
                    true,
                    elevationChoice,
                    out formedTitle,
                    out _,
                    out reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            _onHierarchyChanged?.Invoke(formedTitle?.TitleId);
        }

        private void CompletePlayerDissolution()
        {
            string parentTitleId = !string.IsNullOrWhiteSpace(Title?.DeFactoParentTitleId)
                ? Title.DeFactoParentTitleId
                : Title?.ParentTitleId;
            string dissolvedTitleName = FormatTitleName(Title);
            if (!FeudalTitlePlayerActionService.TryExecuteDissolution(Clan.PlayerClan, Title, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            TextObject message = new TextObject("{=BC_Hierarchy_DissolveCompleted}The {TITLE_NAME} has been dissolved and its subordinate titles have returned to the higher hierarchy.");
            message.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + dissolvedTitleName));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Green));
            _onHierarchyChanged?.Invoke(parentTitleId);
        }

        private void CompletePlayerRename(string requestedName)
        {
            string oldTitleName = FormatTitleName(Title);
            if (!FeudalTitlePlayerActionService.TryExecuteRename(Clan.PlayerClan, Title, requestedName, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            TextObject message = new TextObject("{=BC_Hierarchy_RenameCompleted}The {OLD_TITLE_NAME} is now known as the {NEW_TITLE_NAME}.");
            message.SetTextVariable("OLD_TITLE_NAME", new TextObject("{=!}" + oldTitleName));
            message.SetTextVariable("NEW_TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Green));
            _onHierarchyChanged?.Invoke(Title?.TitleId);
        }

        private void CompletePlayerServiceChange(FeudalServiceLevel level)
        {
            if (!FeudalTitlePlayerActionService.TrySetServiceLevel(Clan.PlayerClan, Title, level, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            TextObject message = new TextObject("{=BC_Hierarchy_ServiceChanged}The {TITLE_NAME} now owes {SERVICE_LEVEL} to its immediate liege.");
            message.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            message.SetTextVariable("SERVICE_LEVEL", FeudalTitlePlayerActionService.GetServiceLevelName(level));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            _onHierarchyChanged?.Invoke(Title?.TitleId);
        }

        private void ShowGrantConfirmation(FeudalGrantRecipientPreview recipient)
        {
            if (recipient?.Clan == null)
                return;

            TextObject description = recipient.CreatesIndependentRealm
                ? new TextObject("{=BC_Hierarchy_GrantConfirmIndependentDesc}Grant the {TITLE_NAME} to the {RECIPIENT_CLAN} for {INFLUENCE_COST} influence? Because this title equals your own sovereign rank, they will become an independent peer. Expected relation gain: +{RELATION_GAIN}.")
                : new TextObject("{=BC_Hierarchy_GrantConfirmDesc}Grant the {TITLE_NAME} to the {RECIPIENT_CLAN} for {INFLUENCE_COST} influence? Expected relation gain: +{RELATION_GAIN}.");
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title, recipient.Clan)));
            description.SetTextVariable("RECIPIENT_CLAN", recipient.Clan.Name ?? new TextObject("?"));
            description.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(FeudalTitlePlayerActionService.GrantInfluenceCost));
            description.SetTextVariable("RELATION_GAIN", recipient.RelationGain);

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Hierarchy_GrantConfirmTitle}Grant Title").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_Hierarchy_ActionGrant}Grant").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                () => CompletePlayerGrant(recipient.Clan),
                null), true);
        }

        private void CompletePlayerGrant(Clan recipientClan)
        {
            if (!FeudalTitlePlayerActionService.TryExecuteGrant(Clan.PlayerClan, Title, recipientClan, out FeudalGrantResult result, out string reason))
            {
                ShowActionFailure(reason);
                RefreshPlayerActions();
                return;
            }

            TextObject message = new TextObject("{=BC_Hierarchy_GrantCompleted}The {TITLE_NAME} has been granted to the {RECIPIENT_CLAN}.");
            message.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title, recipientClan)));
            message.SetTextVariable("RECIPIENT_CLAN", recipientClan?.Name ?? new TextObject("?"));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            _onHierarchyChanged?.Invoke(result?.Title?.TitleId ?? Title?.TitleId);
        }

        private void RefreshPlayerActions()
        {
            var renounceBlock = FeudalTitlePlayerActionService.GetRenunciationBlock(Title, out bool renounceVisible);
            IsRenounceVisible = renounceVisible;
            IsRenounceEnabled = renounceVisible && renounceBlock == null;
            var renounceHint = new TextObject("{=BC_Renounce_Hint}Renounce your house's claims to {TITLE_NAME}.\n\nRemoves all your clan's existing weak and strong claims to this title. This may ease relations with its holders by removing a contested-claim penalty. Other claims and past grievances remain.\n\nYou cannot renounce a claim while pressing it through an active feud or claimant rebellion.");
            renounceHint.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            RenounceHint = new HintViewModel(renounceBlock ?? renounceHint);
            OnPropertyChanged(nameof(IsRenounceVisible));
            OnPropertyChanged(nameof(IsRenounceEnabled));
            OnPropertyChanged(nameof(RenounceHint));
            var challenges = SuccessionChallengeBehavior.Instance;
            IsChallengeVisible = challenges?.IsPlayerChallengeTitle(Title) == true;
            var challengeBlock = challenges?.PlayerChallengeBlock(Title);
            IsChallengeEnabled = IsChallengeVisible && challenges != null && challengeBlock == null;
            ChallengeHint = new HintViewModel(challengeBlock ?? new TextObject("{=BC_PlayerChallenge_Hint}As a lawful heir and head of your own house, you may rally support to challenge the ruler for the crown."));
            OnPropertyChanged(nameof(IsChallengeVisible));
            OnPropertyChanged(nameof(IsChallengeEnabled));
            OnPropertyChanged(nameof(ChallengeHint));
            OnPropertyChanged(nameof(ActionMenuHeight));
            FeudalClaimFabricationBehavior fabricationBehavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            FeudalFabricationPreview fabrication = fabricationBehavior?.GetFabricationPreview(Clan.PlayerClan, Title)
                ?? new FeudalFabricationPreview { Reason = "title behavior unavailable" };
            FeudalUsurpationPreview usurpation = FeudalTitlePlayerActionService.GetUsurpationPreview(Clan.PlayerClan, Title);
            FeudalFormationPreview formation = FeudalTitlePlayerActionService.GetFormationPreview(Clan.PlayerClan, Title);
            FeudalRevocationPreview revocation = FeudalTitlePlayerActionService.GetRevocationPreview(Clan.PlayerClan, Title);
            FeudalServicePreview service = FeudalTitlePlayerActionService.GetServicePreview(Clan.PlayerClan, Title);
            FeudalGrantPreview grant = FeudalTitlePlayerActionService.GetGrantPreview(Clan.PlayerClan, Title);
            FeudalRenamePreview rename = FeudalTitlePlayerActionService.GetRenamePreview(Clan.PlayerClan, Title);
            FeudalDissolutionPreview dissolution = FeudalTitlePlayerActionService.GetDissolutionPreview(Clan.PlayerClan, Title);

            IsClaimEnabled = fabrication.CanStart;
            IsUsurpEnabled = usurpation.CanUsurp;
            IsRevocationEnabled = revocation.CanRevoke;
            IsFormationVisible = formation.IsVisible;
            IsFormationEnabled = formation.CanForm;
            IsServiceEnabled = service.CanChange;
            IsGrantEnabled = grant.CanOpen;
            IsRenameEnabled = rename.CanRename;
            IsDissolutionEnabled = dissolution.CanDissolve;
            ClaimHint = new HintViewModel(BuildClaimHint(fabrication));
            UsurpHint = new HintViewModel(BuildUsurpHint(usurpation));
            RevocationHint = new HintViewModel(BuildRevocationHint(revocation));
            FormationHint = new HintViewModel(BuildFormationHint(formation));
            ServiceHint = new HintViewModel(BuildServiceHint(service));
            GrantHint = new HintViewModel(BuildGrantHint(grant));
            RenameHint = new HintViewModel(BuildRenameHint(rename));
            DissolutionHint = new HintViewModel(BuildDissolutionHint(dissolution));
        }

        private TextObject BuildClaimHint(FeudalFabricationPreview preview)
        {
            if (preview.ActiveRecord != null
                && preview.ActiveRecord.IsActive
                && string.Equals(preview.ActiveRecord.TargetTitleId, Title?.TitleId, StringComparison.Ordinal))
            {
                float remainingDays = preview.ActiveRecord.DailyProgressDelta > 0f
                    ? Math.Max(0f, (1f - preview.ActiveRecord.Progress) / preview.ActiveRecord.DailyProgressDelta)
                    : 0f;
                TextObject activeText = new TextObject("{=BC_Hierarchy_ClaimHintInProgress}You are currently fabricating a claim to the {TITLE_NAME}.\n\nProgress: {PROGRESS}% ({DAYS} days remaining)");
                activeText.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
                activeText.SetTextVariable("PROGRESS", (preview.ActiveRecord.Progress * 100f).ToString("0.0"));
                activeText.SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(remainingDays)));
                return AppendFabricationSummary(activeText, preview.ActiveRecord);
            }

            TextObject text = preview.CanStart
                ? new TextObject("{=BC_Hierarchy_ClaimHintEnabled}Fabricate a weak claim to the {TITLE_NAME}.\n\nCost:\n{GOLD_COST} denars\n{INFLUENCE_COST} influence\n\nEstimated duration: {DAYS} days")
                : new TextObject("{=BC_Hierarchy_ClaimHintDisabled}You cannot fabricate a claim to the {TITLE_NAME}:\n{REASON}\n\nCost:\n{GOLD_COST} denars\n{INFLUENCE_COST} influence");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("GOLD_COST", preview.GoldCost);
            text.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));
            text.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(preview.EstimatedDays)));
            text.SetTextVariable("REASON", GetActionReason(preview.Reason));
            return AppendFabricationSummary(text, null);
        }

        private TextObject AppendFabricationSummary(TextObject text, FeudalClaimFabricationRecord record)
        {
            if (Title == null || Clan.PlayerClan?.Leader == null)
                return text;
            TextObject combined = new TextObject("{=BC_Fabrication_WithSummary}{DETAILS}\n\n{SUMMARY}");
            combined.SetTextVariable("DETAILS", text);
            combined.SetTextVariable("SUMMARY", new FeudalFabricationStagePreview(Title, record).BuildSummary());
            return combined;
        }

        private TextObject BuildUsurpHint(FeudalUsurpationPreview preview)
        {
            TextObject text = preview.CanUsurp
                ? new TextObject("{=BC_Hierarchy_UsurpHintEnabled}Usurp the {TITLE_NAME}. Cost: {GOLD_COST} denars and {INFLUENCE_COST} influence. Controlled subordinate titles: {CONTROLLED}/{TOTAL}.")
                : new TextObject("{=BC_Hierarchy_UsurpHintDisabled}You cannot usurp the {TITLE_NAME}: {REASON}\n\nYou need a weak or strong claim and de facto control. Baronies must be held directly; higher titles require control of more than half of their actual immediate subordinate titles, including those governed by your de facto vassals. Cost: {GOLD_COST} denars and {INFLUENCE_COST} influence.");
            if (preview.IsLawfulAssumption)
                text = preview.CanUsurp
                    ? new TextObject("{=BC_Hierarchy_AssumeHintEnabled}Assume the vacant {TITLE_NAME}. Your lawful ownership replaces the claim requirement. Cost: {GOLD_COST} denars and {INFLUENCE_COST} influence. Controlled subordinate titles: {CONTROLLED}/{TOTAL}.")
                    : new TextObject("{=BC_Hierarchy_AssumeHintDisabled}You cannot assume the vacant {TITLE_NAME}: {REASON}\n\nYour house already owns the lawful title. You must control more than half of its immediate subordinate titles and pay {GOLD_COST} denars and {INFLUENCE_COST} influence. Controlled: {CONTROLLED}/{TOTAL}.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("GOLD_COST", preview.GoldCost);
            text.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview.InfluenceCost));
            text.SetTextVariable("CONTROLLED", preview.ControlledTitles);
            text.SetTextVariable("TOTAL", preview.TotalTitles);
            text.SetTextVariable("REASON", GetActionReason(preview.Reason));
            return text;
        }

        private TextObject BuildRevocationHint(FeudalRevocationPreview preview)
        {
            TextObject text = preview != null && preview.CanRevoke
                ? new TextObject("{=BC_Hierarchy_RevokeHintEnabled}Demand revocation of the {TITLE_NAME} from the {HOLDER_CLAN}. Cost: {INFLUENCE_COST} influence.\n\nRequires direct de facto liege authority and a weak or strong claim. If the vassal refuses, the dispute immediately becomes a private feud war.\n\nLikely response: {RESPONSE}")
                : new TextObject("{=BC_Hierarchy_RevokeHintDisabled}You cannot revoke the {TITLE_NAME}: {REASON}\n\nOnly a direct de facto liege with a weak or strong claim can demand revocation from an immediate vassal.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("HOLDER_CLAN", preview?.HolderClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview?.InfluenceCost ?? FeudalTitlePlayerActionService.RevocationInfluenceCost));
            text.SetTextVariable("REASON", GetActionReason(preview?.Reason));
            text.SetTextVariable("RESPONSE", preview?.HolderLikelyDefies == true
                ? new TextObject("{=BC_Hierarchy_RevokeResponseDefy}refusal and private war")
                : new TextObject("{=BC_Hierarchy_RevokeResponseAccept}begrudging acceptance"));
            return text;
        }

        private TextObject BuildFormationHint(FeudalFormationPreview preview)
        {
            if (preview == null)
                return new TextObject("{=BC_Hierarchy_FormStatusGenericUnavailable}Title formation is currently unavailable.");

            FeudalTitleType childType = Title != null && Title.TitleType < FeudalTitleType.Empire
                ? Title.TitleType
                : preview.TargetType > FeudalTitleType.Barony
                    ? (FeudalTitleType)((int)preview.TargetType - 1)
                    : FeudalTitleType.Barony;
            TextObject hint = new TextObject("{=BC_Hierarchy_FormHintAssessment}{STATUS}\n\nEligible contiguous {CHILD_TIER} titles: {AVAILABLE}/{REQUIRED}\nCost: {GOLD_COST} denars\nReward: {INFLUENCE_REWARD} influence{OBSTACLES}");
            hint.SetTextVariable("STATUS", BuildFormationStatus(preview, childType));
            hint.SetTextVariable("CHILD_TIER", new TextObject("{=!}" + GetTierName(childType).ToLowerInvariant()));
            hint.SetTextVariable("AVAILABLE", preview.ContiguousEligibleTitles);
            hint.SetTextVariable("REQUIRED", preview.RequiredTitles);
            hint.SetTextVariable("GOLD_COST", preview.GoldCost);
            hint.SetTextVariable("INFLUENCE_REWARD", (int)Math.Round(preview.InfluenceReward));
            hint.SetTextVariable("OBSTACLES", BuildFormationObstacleText(preview));
            return hint;
        }

        private TextObject BuildFormationStatus(FeudalFormationPreview preview, FeudalTitleType childType)
        {
            FeudalTitleFormationCandidate candidate = preview.Candidates.FirstOrDefault();
            if (preview.CanForm && candidate != null)
            {
                TextObject enabled = new TextObject("{=BC_Hierarchy_FormStatusEnabled}Available: form the {TITLE_NAME} from {CHILD_COUNT} contiguous titles.{MULTIPLE}");
                enabled.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatCandidateTitleName(candidate)));
                enabled.SetTextVariable("CHILD_COUNT", candidate.ChildTitleIds.Count);
                TextObject multiple = new TextObject(string.Empty);
                if (preview.Candidates.Count > 1)
                {
                    multiple = new TextObject("{=BC_Hierarchy_FormStatusMultiple} {COUNT} valid formations are available.");
                    multiple.SetTextVariable("COUNT", preview.Candidates.Count);
                }
                enabled.SetTextVariable("MULTIPLE", multiple);
                return enabled;
            }

            string seatName = FormatTitleName(Title, Clan.PlayerClan);
            string childTier = GetTierName(childType).ToLowerInvariant();
            TextObject status;
            switch (preview.BlockReason)
            {
                case FeudalTitleFormationBlockReason.HighestTier:
                    status = new TextObject("{=BC_Hierarchy_FormStatusHighestTier}The {SEAT_TITLE} is already of the highest title tier.");
                    break;
                case FeudalTitleFormationBlockReason.MissingLeader:
                    status = new TextObject("{=BC_Hierarchy_FormStatusMissingLeader}Your clan needs a living leader to form a higher title.");
                    break;
                case FeudalTitleFormationBlockReason.SeatNotFullyHeld:
                    status = new TextObject("{=BC_Hierarchy_FormStatusSeatNotHeld}The {SEAT_TITLE} must be held by your clan both de jure and de facto before it can serve as the new title's seat.");
                    break;
                case FeudalTitleFormationBlockReason.SeatDisputed:
                    status = new TextObject("{=BC_Hierarchy_FormStatusSeatDisputed}The {SEAT_TITLE} cannot serve as a seat while an active claim feud concerns its estate.");
                    break;
                case FeudalTitleFormationBlockReason.NoNeighboringTitles:
                    status = new TextObject("{=BC_Hierarchy_FormStatusNoNeighbors}No other {CHILD_TIER} title neighbors the {SEAT_TITLE} under the formation rules.");
                    break;
                case FeudalTitleFormationBlockReason.NeighboringTitlesIneligible:
                    status = new TextObject("{=BC_Hierarchy_FormStatusNeighborsIneligible}Neighboring {CHILD_TIER} titles exist, but none can lawfully be included with the {SEAT_TITLE}.");
                    break;
                case FeudalTitleFormationBlockReason.InsufficientContiguousTitles:
                    status = new TextObject("{=BC_Hierarchy_FormStatusTooFew}Only {AVAILABLE} of the required {REQUIRED} contiguous {CHILD_TIER} titles are eligible around the {SEAT_TITLE}.");
                    break;
                case FeudalTitleFormationBlockReason.InsufficientGold:
                    status = new TextObject("{=BC_Hierarchy_FormStatusGold}The title structure is valid, but you have only {CURRENT_GOLD} of the required {GOLD_COST} denars.");
                    break;
                case FeudalTitleFormationBlockReason.ConflictingParents:
                    status = new TextObject("{=BC_Hierarchy_FormStatusConflictingParents}A selected title has conflicting de jure and de facto parents of the proposed rank.");
                    break;
                case FeudalTitleFormationBlockReason.UnauthorizedHolder:
                    status = new TextObject("{=BC_Hierarchy_FormStatusUnauthorizedHolder}A required title is not fully held by your clan or by an eligible direct vassal.");
                    break;
                case FeudalTitleFormationBlockReason.IneligibleVassal:
                    status = new TextObject("{=BC_Hierarchy_FormStatusIneligibleVassal}A required vassal title is not held by an eligible direct vassal of your realm.");
                    break;
                case FeudalTitleFormationBlockReason.NoReorganizationAuthority:
                    status = new TextObject("{=BC_Hierarchy_FormStatusNoAuthority}You do not hold the affected parent title and lack sovereign authority to reorganize it.");
                    break;
                case FeudalTitleFormationBlockReason.ParentDisputed:
                    status = new TextObject("{=BC_Hierarchy_FormStatusParentDisputed}An affected parent title is contested in an active claim feud.");
                    break;
                case FeudalTitleFormationBlockReason.WouldEmptyParent:
                    status = new TextObject("{=BC_Hierarchy_FormStatusEmptyParent}This formation would leave an existing parent title without subordinate titles.");
                    break;
                case FeudalTitleFormationBlockReason.SplitVassalEstate:
                    status = new TextObject("{=BC_Hierarchy_FormStatusSplitEstate}A selected vassal also holds lands or titles outside the proposed sovereign title.");
                    break;
                case FeudalTitleFormationBlockReason.ExistingHigherTitle:
                    status = new TextObject("{=BC_Hierarchy_FormStatusExistingParent}An active title of the proposed rank already governs part of this selection.");
                    break;
                default:
                    status = new TextObject("{=BC_Hierarchy_FormStatusUnavailable}No valid higher title can currently be formed from the {SEAT_TITLE}.");
                    break;
            }

            status.SetTextVariable("SEAT_TITLE", new TextObject("{=!}" + seatName));
            status.SetTextVariable("CHILD_TIER", new TextObject("{=!}" + childTier));
            status.SetTextVariable("AVAILABLE", preview.ContiguousEligibleTitles);
            status.SetTextVariable("REQUIRED", preview.RequiredTitles);
            status.SetTextVariable("CURRENT_GOLD", preview.CurrentGold);
            status.SetTextVariable("GOLD_COST", preview.GoldCost);
            return status;
        }

        private TextObject BuildFormationObstacleText(FeudalFormationPreview preview)
        {
            if (preview.BlockReason != FeudalTitleFormationBlockReason.NeighboringTitlesIneligible
                && preview.BlockReason != FeudalTitleFormationBlockReason.InsufficientContiguousTitles)
            {
                return new TextObject(string.Empty);
            }

            List<FeudalTitleFormationObstacle> visibleObstacles = preview.Obstacles
                .Where(obstacle => obstacle?.Title != null)
                .Take(2)
                .ToList();
            if (visibleObstacles.Count == 0)
                return new TextObject(string.Empty);

            List<string> lines = new List<string>();
            foreach (FeudalTitleFormationObstacle obstacle in visibleObstacles)
            {
                TextObject line = new TextObject("{=BC_Hierarchy_FormObstacleLine}\n{TITLE_NAME}: {REASON}");
                line.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(obstacle.Title, Clan.PlayerClan)));
                line.SetTextVariable("REASON", GetFormationObstacleReason(obstacle.Reason));
                lines.Add(line.ToString());
            }

            int remaining = preview.Obstacles.Count - visibleObstacles.Count;
            if (remaining > 0)
            {
                TextObject more = new TextObject("{=BC_Hierarchy_FormObstacleMore}\n{COUNT} more neighboring titles are unavailable.");
                more.SetTextVariable("COUNT", remaining);
                lines.Add(more.ToString());
            }

            return new TextObject("{=!}" + string.Join(string.Empty, lines));
        }

        private static TextObject GetFormationObstacleReason(FeudalTitleFormationBlockReason reason)
        {
            switch (reason)
            {
                case FeudalTitleFormationBlockReason.SeatDisputed:
                    return new TextObject("{=BC_Hierarchy_FormObstacleDisputed}involved in an active claim feud");
                case FeudalTitleFormationBlockReason.NoReorganizationAuthority:
                    return new TextObject("{=BC_Hierarchy_FormObstacleNoAuthority}not personally held, and you lack sovereign authority to reorganize it");
                case FeudalTitleFormationBlockReason.IneligibleVassal:
                    return new TextObject("{=BC_Hierarchy_FormObstacleVassal}not fully held by an eligible direct vassal of your realm");
                case FeudalTitleFormationBlockReason.ParentNotControlled:
                    return new TextObject("{=BC_Hierarchy_FormObstacleParent}its immediate higher title is not fully under your authority");
                case FeudalTitleFormationBlockReason.ExistingHigherTitle:
                    return new TextObject("{=BC_Hierarchy_FormObstacleExistingParent}already governed by an active title of the proposed rank");
                default:
                    return new TextObject("{=BC_Hierarchy_FormObstacleUnavailable}not eligible for this formation");
            }
        }

        private string BuildFormationSelectionDescription(FeudalTitleType targetType)
        {
            string seedName = FormatTitleName(Title, Clan.PlayerClan);
            string titleNoun = FeudalTitleDisplayHelper.GetLandedTitleNoun(
                targetType,
                Clan.PlayerClan,
                Clan.PlayerClan?.Kingdom,
                Title?.FallbackCultureRef);
            TextObject description = new TextObject("{=BC_Hierarchy_FormChooseDesc}The {SEED_TITLE} will be the seat of the new {TITLE_NOUN}. Choose at least {ADDITIONAL_COUNT} additional contiguous titles to bind beneath it.");
            description.SetTextVariable("SEED_TITLE", new TextObject("{=!}" + seedName));
            description.SetTextVariable("TITLE_NOUN", new TextObject("{=!}" + titleNoun));
            description.SetTextVariable("ADDITIONAL_COUNT", Math.Max(1, FeudalTitleBehavior.GetMinimumFormationChildren() - 1));
            return description.ToString();
        }

        private static string BuildFormationChildHint(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            if (title == null)
                return string.Empty;

            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            FeudalTitleRecord parent = titleBehavior?.GetTitle(title.ParentTitleId)
                ?? titleBehavior?.GetTitle(title.DeFactoParentTitleId);
            TextObject hint = new TextObject("{=BC_Hierarchy_FormChildHint}{TITLE_NAME}\nDe facto holder: {HOLDER_NAME}\nCurrent parent: {PARENT_NAME}\n\nSelect only titles connected to the chosen seat and to one another. A vassal retains possession when the title is reorganized.");
            hint.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(title, Clan.PlayerClan)));
            hint.SetTextVariable("HOLDER_NAME", holder?.Name ?? new TextObject("{=BC_Hierarchy_Vacant}Vacant"));
            hint.SetTextVariable("PARENT_NAME", parent != null
                ? new TextObject("{=!}" + FormatTitleName(parent))
                : new TextObject("{=BC_Hierarchy_None}None"));
            return hint.ToString();
        }

        private TextObject BuildServiceHint(FeudalServicePreview preview)
        {
            FeudalServiceLevel current = preview?.CurrentLevel ?? FeudalServiceBehavior.DefaultLevel;
            TextObject text = preview != null && preview.CanChange
                ? new TextObject("{=BC_Hierarchy_ServiceHintEnabled}Change the service owed by the {TITLE_NAME} to the {LIEGE_TITLE}.\n\nCurrent service: {SERVICE_LEVEL}\nTax share: {TAX_SHARE}%\nArmy cost: {ARMY_COST}%\nOngoing relation modifier: {RELATION_MODIFIER}")
                : new TextObject("{=BC_Hierarchy_ServiceHintDisabled}You cannot change service for the {TITLE_NAME}: {REASON}\n\nOnly titles held by your immediate de facto vassals can have their service changed.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("LIEGE_TITLE", new TextObject("{=!}" + FormatTitleName(preview?.ParentTitle)));
            text.SetTextVariable("SERVICE_LEVEL", FeudalTitlePlayerActionService.GetServiceLevelName(current));
            text.SetTextVariable("TAX_SHARE", FormatPercentWhole(FeudalServiceBehavior.GetTaxShare(current)));
            text.SetTextVariable("ARMY_COST", FormatPercentWhole(FeudalServiceBehavior.GetArmyCostMultiplier(current)));
            text.SetTextVariable("RELATION_MODIFIER", FormatSigned(FeudalServiceBehavior.GetOngoingRelationModifier(current)));
            text.SetTextVariable("REASON", GetActionReason(preview?.Reason));
            return text;
        }

        private string BuildServiceChoiceHint(FeudalServiceLevel level, FeudalServiceLevel currentLevel)
        {
            TextObject hint = new TextObject("{=BC_Hierarchy_ServiceChoiceHint}{CURRENT}Tax share: {TAX_SHARE}%\nArmy cost: {ARMY_COST}%\nOngoing relation modifier: {RELATION_MODIFIER}\n{MEMORY_LINE}");
            hint.SetTextVariable("CURRENT", level == currentLevel
                ? new TextObject("{=BC_Hierarchy_ServiceChoiceCurrent}Current contract\n")
                : new TextObject(""));
            hint.SetTextVariable("TAX_SHARE", FormatPercentWhole(FeudalServiceBehavior.GetTaxShare(level)));
            hint.SetTextVariable("ARMY_COST", FormatPercentWhole(FeudalServiceBehavior.GetArmyCostMultiplier(level)));
            hint.SetTextVariable("RELATION_MODIFIER", FormatSigned(FeudalServiceBehavior.GetOngoingRelationModifier(level)));
            int memoryChange = FeudalServiceBehavior.GetRelationMemoryChange(currentLevel, level);
            if (memoryChange == 0)
            {
                hint.SetTextVariable("MEMORY_LINE", new TextObject("{=BC_Hierarchy_ServiceChoiceMemoryNone}Relation memory on change: none"));
            }
            else
            {
                TextObject memoryLine = new TextObject("{=BC_Hierarchy_ServiceChoiceMemory}Relation memory on change: {RELATION_MEMORY} for {DURATION} years");
                memoryLine.SetTextVariable("RELATION_MEMORY", FormatSigned(memoryChange));
                memoryLine.SetTextVariable("DURATION", FeudalServiceBehavior.RelationMemoryDurationYears.ToString("0"));
                hint.SetTextVariable("MEMORY_LINE", memoryLine);
            }
            return hint.ToString();
        }

        private TextObject BuildGrantHint(FeudalGrantPreview preview)
        {
            TextObject text = preview != null && preview.CanOpen
                ? new TextObject("{=BC_Hierarchy_GrantHintEnabled}Grant the {TITLE_NAME} directly to one of your settled vassals. Cost: {INFLUENCE_COST} influence.\n\nBaronies transfer their settlement. Higher titles require the recipient to hold at least one immediate child title.")
                : new TextObject("{=BC_Hierarchy_GrantHintDisabled}You cannot grant the {TITLE_NAME}: {REASON}\n\nOnly titles held directly by your ruling clan can be granted to settled vassals.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview?.InfluenceCost ?? FeudalTitlePlayerActionService.GrantInfluenceCost));
            text.SetTextVariable("REASON", GetActionReason(preview?.Reason));
            return text;
        }

        private TextObject BuildDissolutionHint(FeudalDissolutionPreview preview)
        {
            TextObject text = preview != null && preview.CanDissolve
                ? new TextObject("{=BC_Hierarchy_DissolveHintEnabled}Dissolve the {TITLE_NAME}. Cost: {INFLUENCE_COST} influence. Its immediate subordinate titles will return to the next active liege in each hierarchy.")
                : new TextObject("{=BC_Hierarchy_DissolveHintDisabled}You cannot dissolve the {TITLE_NAME}: {REASON}\n\nOnly a non-sovereign upper title held both de jure and de facto by your clan may be dissolved, and no active claim feud may concern its estate.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("INFLUENCE_COST", (int)Math.Ceiling(preview?.InfluenceCost ?? 0f));
            text.SetTextVariable("REASON", GetActionReason(preview?.Reason));
            return text;
        }

        private TextObject BuildRenameHint(FeudalRenamePreview preview)
        {
            TextObject text = preview != null && preview.CanRename
                ? new TextObject("{=BC_Hierarchy_RenameHintEnabled}Rename the {TITLE_NAME}. The title must be held by your clan both de jure and de facto.")
                : new TextObject("{=BC_Hierarchy_RenameHintDisabled}You cannot rename the {TITLE_NAME}: {REASON}\n\nOnly a title held by your clan both de jure and de facto may be renamed.");
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FormatTitleName(Title)));
            text.SetTextVariable("REASON", GetActionReason(preview?.Reason));
            return text;
        }

        private string BuildGrantRecipientHint(FeudalGrantRecipientPreview recipient)
        {
            if (recipient == null)
                return string.Empty;

            TextObject hint = recipient.CanReceive
                ? new TextObject("{=BC_Hierarchy_GrantRecipientHintEnabled}Grant to the {RECIPIENT_CLAN}.\nRelation gain: +{RELATION_GAIN}\nTransfers: {TRANSFER_SCOPE}{CLAIM_BONUS}{INDEPENDENCE}")
                : new TextObject("{=BC_Hierarchy_GrantRecipientHintDisabled}The {RECIPIENT_CLAN} cannot receive this title: {REASON}");
            hint.SetTextVariable("RECIPIENT_CLAN", recipient.Clan?.Name ?? new TextObject("?"));
            hint.SetTextVariable("RELATION_GAIN", recipient.RelationGain);
            hint.SetTextVariable("REASON", GetActionReason(recipient.Reason));
            hint.SetTextVariable("TRANSFER_SCOPE", BuildGrantTransferScope(recipient));
            hint.SetTextVariable("CLAIM_BONUS", recipient.HadClaim
                ? new TextObject("{=BC_Hierarchy_GrantClaimBonus}\nClaim recognized: +5 relation")
                : new TextObject(""));
            hint.SetTextVariable("INDEPENDENCE", recipient.CreatesIndependentRealm
                ? new TextObject("{=BC_Hierarchy_GrantIndependentHint}\nCreates an independent peer realm; relation gains are doubled.")
                : new TextObject(""));
            return hint.ToString();
        }

        private static TextObject BuildGrantTransferScope(FeudalGrantRecipientPreview recipient)
        {
            if (recipient == null)
                return new TextObject("{=BC_Hierarchy_GrantTransferNone}none");
            if (recipient.TransfersDeJure && recipient.TransfersDeFacto)
                return new TextObject("{=BC_Hierarchy_GrantTransferFull}de jure and de facto");
            if (recipient.TransfersDeJure)
                return new TextObject("{=BC_Hierarchy_GrantTransferDeJure}de jure only");
            if (recipient.TransfersDeFacto)
                return new TextObject("{=BC_Hierarchy_GrantTransferDeFacto}de facto only");
            return new TextObject("{=BC_Hierarchy_GrantTransferNone}none");
        }

        private static TextObject GetActionReason(string reason)
        {
            switch (reason ?? string.Empty)
            {
                case "fabrication is already underway": return new TextObject("{=BC_Hierarchy_ActionReasonSameFabrication}fabrication is already underway");
                case "clan is already fabricating another claim": return new TextObject("{=BC_Hierarchy_ActionReasonOtherFabrication}your clan is already fabricating another claim");
                case "clan is already the de jure holder":
                case "already de jure holder": return new TextObject("{=BC_Hierarchy_ActionReasonDeJureHolder}your clan is already the de jure holder");
                case "clan already has an active claim": return new TextObject("{=BC_Hierarchy_ActionReasonHasClaim}your clan already has a valid claim");
                case "no weak or strong claim": return new TextObject("{=BC_Hierarchy_ActionReasonNoClaim}your clan has no weak or strong claim");
                case "barony claimant is not de facto holder":
                case "barony claimant is not the de facto holder":
                    return new TextObject("{=BC_Hierarchy_ActionReasonNotDeFacto}your clan is not the de facto holder");
                case "insufficient control of subordinate titles": return new TextObject("{=BC_Hierarchy_ActionReasonControl}your authority controls no strict majority of the title's immediate subordinate branches");
                case "title has no active subordinate titles": return new TextObject("{=BC_Hierarchy_ActionReasonNoSubordinates}the title has no active subordinate titles from which a lawful majority can be established");
                case "insufficient gold": return new TextObject("{=BC_Hierarchy_ActionReasonGold}your clan leader lacks the required gold");
                case "insufficient influence": return new TextObject("{=BC_Hierarchy_ActionReasonInfluence}your clan lacks the required influence");
                case "selected title already belongs to the immediate higher tier": return new TextObject("{=BC_Hierarchy_ActionReasonAlreadyBound}the title already belongs to an active title of that rank");
                case "no valid contiguous title cluster": return new TextObject("{=BC_Hierarchy_ActionReasonNoCluster}there are not enough eligible contiguous titles around this seat");
                case "too few subordinate titles selected": return new TextObject("{=BC_Hierarchy_ActionReasonTooFewSelected}too few subordinate titles were selected");
                case "one or more selected titles are no longer eligible": return new TextObject("{=BC_Hierarchy_ActionReasonSelectionStale}one or more selected titles are no longer eligible");
                case "selected titles are not contiguous": return new TextObject("{=BC_Hierarchy_ActionReasonNotContiguous}the selected titles do not form one contiguous territory");
                case "selected titles contain no lawful seed": return new TextObject("{=BC_Hierarchy_ActionReasonNoLawfulSeed}your clan must hold at least one selected title both de jure and de facto");
                case "the selected seat must be held both de jure and de facto by your clan": return new TextObject("{=BC_Hierarchy_ActionReasonSeatNotHeld}your clan must hold the selected seat both de jure and de facto");
                case "a selected title has conflicting immediate parents": return new TextObject("{=BC_Hierarchy_ActionReasonConflictingParents}a selected title has conflicting de jure and de facto parents of the proposed rank");
                case "a selected title is not fully held by an authorized clan": return new TextObject("{=BC_Hierarchy_ActionReasonUnauthorizedHolder}a selected title is not fully held by your clan or an eligible direct vassal");
                case "a selected vassal title is not held by your direct same-realm vassal": return new TextObject("{=BC_Hierarchy_ActionReasonNotDirectVassal}a selected vassal title is not held by an eligible direct vassal of your realm");
                case "you lack authority to detach a selected title from its current parent": return new TextObject("{=BC_Hierarchy_ActionReasonNoReorganizationAuthority}you do not fully hold the parent title or the sovereign authority required to reorganize it");
                case "a source parent title is involved in an active claim or dispute": return new TextObject("{=BC_Hierarchy_ActionReasonParentDisputed}an active claim feud concerns one of the affected parent titles");
                case "formation would leave an existing parent title without subordinate titles": return new TextObject("{=BC_Hierarchy_ActionReasonEmptyParent}the formation would leave an existing parent title without any subordinate titles");
                case "a selected vassal holds lands or titles outside the proposed sovereign title": return new TextObject("{=BC_Hierarchy_ActionReasonSplitVassalEstate}a selected vassal also holds lands or titles outside the proposed sovereign title");
                case "an active title of that rank already governs one or more selected titles": return new TextObject("{=BC_Hierarchy_ActionReasonDuplicateFormation}an active title of that rank already governs part of the selection; use claim, usurp, or revoke instead");
                case "title dissolution is unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonDissolveUnavailable}title dissolution is unavailable");
                case "title renaming is unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonRenameUnavailable}title renaming is unavailable");
                case "only active upper titles can be dissolved": return new TextObject("{=BC_Hierarchy_ActionReasonDissolveUpperOnly}only active county-tier or higher titles can be dissolved");
                case "title is not fully held by your clan": return new TextObject("{=BC_Hierarchy_ActionReasonDissolveNotHeld}your clan must hold the title both de jure and de facto");
                case "a sovereign title cannot be dissolved": return new TextObject("{=BC_Hierarchy_ActionReasonDissolveSovereign}a realm's sovereign title cannot be dissolved");
                case "title is involved in an active claim or dispute": return new TextObject("{=BC_Hierarchy_ActionReasonDissolveDisputed}an active claim feud still concerns this title's estate");
                case "target is not de facto held, same-rank neighboring, immediate liege, or immediate subordinate title": return new TextObject("{=BC_Hierarchy_ActionReasonInvalidTarget}The title is not held by your clan de facto, a same-rank neighbor, an immediate liege or subordinate, or an eligible barony in your realm for a landless clan.");
                case "service contracts are unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonServiceUnavailable}service contracts are unavailable");
                case "title has no immediate de facto liege": return new TextObject("{=BC_Hierarchy_ActionReasonNoServiceLiege}the title has no immediate de facto liege");
                case "holder outranks or is a peer to the de jure liege": return new TextObject("{=BC_Hierarchy_ActionReasonServiceDecoupled}holder outranks or is a peer to the de jure liege");
                case "selected title is held directly by your clan": return new TextObject("{=BC_Hierarchy_ActionReasonServiceOwnTitle}the title is held directly by your clan");
                case "selected title and liege title share the same holder": return new TextObject("{=BC_Hierarchy_ActionReasonServiceSameHolder}the title and its liege title are held by the same clan");
                case "selected title does not belong to your immediate vassal": return new TextObject("{=BC_Hierarchy_ActionReasonServiceNotVassal}the title is not held by one of your immediate de facto vassals");
                case "title grant is unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonGrantUnavailable}title grants are unavailable");
                case "grantor is not the ruling clan": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNotRuler}only the ruling clan can grant titles");
                case "title is not held directly by your clan": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNotHeld}the title is not held directly by your clan");
                case "recipient is not a settled vassal": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNotVassal}the recipient is not a settled vassal of your realm");
                case "title holder is not a settled vassal": return new TextObject("{=BC_Hierarchy_ActionReasonHolderNotVassal}the title holder is not a settled vassal of your realm");
                case "title holder is not a valid vassal": return new TextObject("{=BC_Hierarchy_ActionReasonInvalidHolder}the title holder is not a valid vassal");
                case "title revocation is unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonRevokeUnavailable}title revocation is unavailable");
                case "claim feud behavior unavailable": return new TextObject("{=BC_Hierarchy_ActionReasonFeudUnavailable}claim feud behavior is unavailable");
                case "one side is already occupied by a claim feud": return new TextObject("{=BC_Hierarchy_ActionReasonFeudOccupied}one of the involved clans is already occupied by a claim feud");
                case "recipient holds no immediate child title": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNoChild}higher titles can only be granted to vassals who hold at least one immediate child title under them");
                case "title is not a spare peer title": return new TextObject("{=BC_Hierarchy_ActionReasonGrantPrimaryTitle}you cannot grant away your own sovereign title");
                case "no settled vassals": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNoVassals}there are no settled vassals to receive this title");
                case "no eligible vassals": return new TextObject("{=BC_Hierarchy_ActionReasonGrantNoEligible}no vassal currently meets the requirements to receive this title");
                case "independent realm could not be created": return new TextObject("{=BC_Hierarchy_ActionReasonGrantIndependentFailed}the independent realm could not be created");
                case "missing grantor, recipient, or title": return new TextObject("{=BC_Hierarchy_ActionReasonGrantMissing}the grant target is incomplete");
                case "cannot grant title to self": return new TextObject("{=BC_Hierarchy_ActionReasonGrantSelf}you cannot grant this title to yourself");
                case "target title is missing or inactive":
                case "title is missing or inactive": return new TextObject("{=BC_Hierarchy_ActionReasonInactive}the title is inactive");
                default: return new TextObject("{=BC_Hierarchy_ActionReasonUnavailable}the action is currently unavailable");
            }
        }

        private static void ShowActionFailure(string reason)
        {
            TextObject message = new TextObject("{=BC_Hierarchy_ActionFailed}The title action could not be completed: {REASON}.");
            message.SetTextVariable("REASON", GetActionReason(reason));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Red));
        }

        private static List<TooltipProperty> BuildTooltip(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            Clan deJureHolder,
            Clan deFactoHolder,
            FeudalClaimFabricationRecord playerFabrication)
        {
            string vacant = new TextObject("{=BC_Hierarchy_Vacant}Vacant").ToString();
            string currentHolder = deFactoHolder?.Leader?.Name?.ToString()
                ?? deFactoHolder?.Name?.ToString()
                ?? (deJureHolder != null ? new TextObject("{=BC_Hierarchy_NoPossessor}No de facto holder").ToString() : vacant);
            List<TooltipProperty> properties = new List<TooltipProperty>
            {
                new TooltipProperty(currentHolder, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator),
                new TooltipProperty(new TextObject("{=BC_Hierarchy_Tier}Tier").ToString(), GetTierName(title?.TitleType ?? FeudalTitleType.Barony), 0)
            };

            bool rightful = deJureHolder != null
                && deFactoHolder != null
                && deJureHolder.StringId == deFactoHolder.StringId;
            bool whollyVacant = deJureHolder == null && deFactoHolder == null;
            properties.Add(new TooltipProperty(
                new TextObject("{=BC_Hierarchy_Tenure}Tenure").ToString(),
                FeudalTitleDisplayHelper.GetHierarchyTenure(title),
                0));

            if (!rightful && !whollyVacant)
            {
                properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_DeJureHolder}De jure holder").ToString(), deJureHolder?.Name?.ToString() ?? vacant, 0));
                properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_DeFactoHolder}De facto holder").ToString(), deFactoHolder?.Name?.ToString() ?? vacant, 0));
            }

            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));

            FeudalTitleRecord parent = titleBehavior?.GetTitle(title?.ParentTitleId);
            if (parent != null)
                properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_LiegeTitle}De jure liege").ToString(), FormatTitleName(parent), 0));
            AddServiceTooltip(properties, titleBehavior, title);

            string allegiance = deFactoHolder == null
                ? vacant
                : deFactoHolder.Kingdom?.Name?.ToString()
                    ?? new TextObject("{=BC_Hierarchy_Independent}Independent").ToString();
            properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_Allegiance}Allegiance").ToString(), allegiance, 0));

            AddDriftTooltip(properties, title);
            AddFabricationTooltip(properties, title, playerFabrication);

            List<FeudalClaimRecord> claims = titleBehavior?.GetActiveClaimsByTitle(title)
                .OrderByDescending(claim => claim.Strength)
                .GroupBy(claim => !string.IsNullOrWhiteSpace(claim.CarrierHeroId)
                    ? "hero:" + claim.CarrierHeroId
                    : "clan:" + claim.ClaimantClanId)
                .Select(group => group.First())
                .OrderByDescending(claim => claim.Strength)
                .ToList() ?? new List<FeudalClaimRecord>();
            if (claims.Count > 0)
            {
                properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
                properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_ActiveClaimants}Active Claimants").ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title));
                foreach (FeudalClaimRecord claim in claims)
                {
                    Clan claimant = ResolveClan(claim.ClaimantClanId);
                    Hero carrier = ResolveHero(claim.CarrierHeroId) ?? claimant?.Leader;
                    string label = claim.Strength == FeudalClaimStrength.Strong
                        ? new TextObject("{=BC_Hierarchy_StrongClaim}Strong").ToString()
                        : new TextObject("{=BC_Hierarchy_WeakClaim}Weak").ToString();
                    properties.Add(new TooltipProperty(label, carrier?.Name?.ToString() ?? claimant?.Name?.ToString() ?? claim.ClaimantClanId, 0));
                }
            }

            return properties;
        }

        private static void AddFabricationTooltip(
            List<TooltipProperty> properties,
            FeudalTitleRecord title,
            FeudalClaimFabricationRecord playerFabrication)
        {
            if (properties == null
                || title == null
                || playerFabrication == null
                || !playerFabrication.IsActive
                || !string.Equals(playerFabrication.TargetTitleId, title.TitleId, StringComparison.Ordinal))
            {
                return;
            }

            float remainingDays = playerFabrication.DailyProgressDelta > 0f
                ? Math.Max(0f, (1f - playerFabrication.Progress) / playerFabrication.DailyProgressDelta)
                : 0f;
            TextObject value = new TextObject("{=BC_Hierarchy_ClaimProgressValue}{PROGRESS}% ({DAYS} days remaining)");
            value.SetTextVariable("PROGRESS", (playerFabrication.Progress * 100f).ToString("0.0"));
            value.SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(remainingDays)));
            properties.Add(new TooltipProperty(
                new TextObject("{=BC_Hierarchy_ClaimProgress}Claim Progress").ToString(),
                value.ToString(),
                0));
            FeudalFabricationStagePreview stages = new FeudalFabricationStagePreview(title, playerFabrication);
            TextObject currentStage = stages.GetCurrentStageDescription();
            if (currentStage != null)
                properties.Add(new TooltipProperty(
                    new TextObject("{=BC_Fabrication_CurrentStage}Current stage").ToString(),
                    currentStage.ToString(), 0));
        }

        private static void AddServiceTooltip(List<TooltipProperty> properties, FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            if (properties == null || titleBehavior == null || title == null)
                return;

            FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            if (parent == null || !parent.IsActive)
                return;

            if (titleBehavior.IsServiceDecoupledByRank(title)
                || string.Equals(parent.DeFactoHolderClanId, title.DeFactoHolderClanId, StringComparison.Ordinal))
            {
                properties.Add(new TooltipProperty(
                    new TextObject("{=BC_Hierarchy_ServiceOwedLabel}Service Owed").ToString(),
                    new TextObject("{=BC_Hierarchy_ServiceOwedNone}None").ToString(),
                    0));
                return;
            }

            FeudalServiceBehavior serviceBehavior = Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            FeudalServiceLevel level = serviceBehavior?.GetServiceLevel(title, parent) ?? FeudalServiceBehavior.DefaultLevel;
            properties.Add(new TooltipProperty(
                new TextObject("{=BC_Hierarchy_ServiceOwedLabel}Service Owed").ToString(),
                FeudalTitlePlayerActionService.GetServiceLevelName(level).ToString(),
                0));
        }

        private static void AddDriftTooltip(
            List<TooltipProperty> properties,
            FeudalTitleRecord title)
        {
            FeudalDeJureDriftBehavior driftBehavior = Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>();
            if (driftBehavior == null || title == null)
                return;

            FeudalDeJureDriftAssessment assessment = driftBehavior.GetDisplayAssessment(title);
            if (assessment?.ShouldDisplay != true)
                return;

            TextObject reason = null;
            TextObject value;
            if (assessment.State == FeudalDeJureDriftDisplayState.Blocked)
            {
                reason = GetDriftBlockReasonText(assessment);
                value = assessment.TargetKingdom == null
                    ? new TextObject("{=BC_Hierarchy_DriftBlockedNoRealm}Blocked ({REASON})")
                    : new TextObject("{=BC_Hierarchy_DriftBlocked}{TARGET_REALM} - blocked ({REASON})");
            }
            else if (assessment.State == FeudalDeJureDriftDisplayState.Ready)
            {
                value = new TextObject("{=BC_Hierarchy_DriftReady}{TARGET_REALM} - ready to begin (~{YEARS} years)");
                value.SetTextVariable("YEARS", Math.Max(1, (int)Math.Ceiling(
                    FeudalDeJureDriftBehavior.CalculateEstimatedYearsRemaining(
                        assessment.Progress,
                        assessment.Integrator))));
            }
            else if (assessment.State == FeudalDeJureDriftDisplayState.Paused)
            {
                reason = GetDriftBlockReasonText(assessment);
                value = new TextObject("{=BC_Hierarchy_DriftPausedReason}{TARGET_REALM} - {PROGRESS}% (paused: {REASON})");
            }
            else if (assessment.State == FeudalDeJureDriftDisplayState.Reversing)
            {
                reason = GetDriftBlockReasonText(assessment);
                value = new TextObject("{=BC_Hierarchy_DriftReversingReason}{TARGET_REALM} - {PROGRESS}% (reversing: {REASON})");
            }
            else
            {
                value = new TextObject("{=BC_Hierarchy_DriftAdvancing}{TARGET_REALM} - {PROGRESS}% (~{YEARS} years)");
                value.SetTextVariable("YEARS", Math.Max(1, (int)Math.Ceiling(
                    FeudalDeJureDriftBehavior.CalculateEstimatedYearsRemaining(
                        assessment.Progress,
                        assessment.Integrator))));
            }

            value.SetTextVariable(
                "TARGET_REALM",
                assessment.TargetKingdom?.Name ?? new TextObject("{=BC_Hierarchy_DriftUnknownRealm}Unknown realm"));
            value.SetTextVariable("PROGRESS", (assessment.Progress * 100f).ToString("0.0"));
            if (reason != null)
                value.SetTextVariable("REASON", reason);
            if (assessment.IsIncludedInPackage)
            {
                TextObject includedValue = new TextObject("{=BC_Hierarchy_DriftIncluded}Included with {PACKAGE_TITLE}: {DETAILS}");
                includedValue.SetTextVariable(
                    "PACKAGE_TITLE",
                    FeudalTitleDisplayHelper.FormatTitleName(assessment.PackageRoot));
                includedValue.SetTextVariable("DETAILS", value);
                value = includedValue;
            }
            properties.Add(new TooltipProperty(new TextObject("{=BC_Hierarchy_DeJureDrift}De jure drift").ToString(), value.ToString(), 0));
        }

        private static TextObject GetDriftBlockReasonText(FeudalDeJureDriftAssessment assessment)
        {
            TextObject reason;
            switch (assessment?.BlockReason ?? FeudalDeJureDriftBlockReason.RequirementsNotMet)
            {
                case FeudalDeJureDriftBlockReason.HighestTier:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonHighestTier}top-level titles cannot drift");
                    break;
                case FeudalDeJureDriftBlockReason.MissingLegalParent:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonMissingParent}no active de jure liege title exists");
                    break;
                case FeudalDeJureDriftBlockReason.UnresolvedRights:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonUnresolvedRights}the title's de jure or de facto rights are unresolved");
                    break;
                case FeudalDeJureDriftBlockReason.SplitRights:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonSplitRights}de jure and de facto rights are split between realms");
                    break;
                case FeudalDeJureDriftBlockReason.NoControllingRealm:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonNoRealm}the de facto holder belongs to no realm");
                    break;
                case FeudalDeJureDriftBlockReason.TemporaryRealm:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonTemporaryRealm}the controlling realm is temporary, rebellious, or lacks a ruler");
                    break;
                case FeudalDeJureDriftBlockReason.AlreadyIntegrated:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonIntegrated}the title already belongs to this realm's de jure hierarchy");
                    break;
                case FeudalDeJureDriftBlockReason.PartialPackageControl:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonPartialControl}only {CONTROLLED}/{REQUIRED} titles are fully controlled");
                    reason.SetTextVariable("CONTROLLED", assessment.ControlledTitles);
                    reason.SetTextVariable("REQUIRED", assessment.RequiredTitles);
                    break;
                case FeudalDeJureDriftBlockReason.NoReceivingTitle:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonNoReceiver}no higher de jure title in the realm can receive it");
                    break;
                case FeudalDeJureDriftBlockReason.IncludedInHigherPackage:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonHigherPackage}the title must drift as part of a higher title package");
                    break;
                case FeudalDeJureDriftBlockReason.ControlShifted:
                    if (assessment.CurrentControlKingdom != null)
                    {
                        reason = new TextObject("{=BC_Hierarchy_DriftReasonControlShifted}control shifted to {CURRENT_REALM}");
                        reason.SetTextVariable("CURRENT_REALM", assessment.CurrentControlKingdom.Name);
                    }
                    else
                    {
                        reason = new TextObject("{=BC_Hierarchy_DriftReasonControlLost}control shifted away from the target realm");
                    }
                    break;
                case FeudalDeJureDriftBlockReason.AwaitingEvaluation:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonAwaitingEvaluation}requirements changed; awaiting the next drift evaluation");
                    break;
                case FeudalDeJureDriftBlockReason.CompletionPending:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonCompletionPending}integration is complete but final reparenting is pending");
                    break;
                case FeudalDeJureDriftBlockReason.InactiveTitle:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonInactive}the title is inactive");
                    break;
                default:
                    reason = new TextObject("{=BC_Hierarchy_DriftReasonRequirements}drift requirements are not currently met");
                    break;
            }

            return reason;
        }

        public static string GetTierName(FeudalTitleType type)
        {
            switch (type)
            {
                case FeudalTitleType.Empire: return new TextObject("{=BC_Hierarchy_TierEmpire}Empire").ToString();
                case FeudalTitleType.Kingdom: return new TextObject("{=BC_Hierarchy_TierKingdom}Kingdom").ToString();
                case FeudalTitleType.Duchy: return new TextObject("{=BC_Hierarchy_TierDuchy}Duchy").ToString();
                case FeudalTitleType.County: return new TextObject("{=BC_Hierarchy_TierCounty}County").ToString();
                default: return new TextObject("{=BC_Hierarchy_TierBarony}Barony").ToString();
            }
        }

        private static string FormatCandidateTitleName(FeudalTitleFormationCandidate candidate)
        {
            return candidate == null
                ? string.Empty
                : FeudalTitleDisplayHelper.FormatTitleName(
                    candidate.TargetType,
                    candidate.Name,
                    Clan.PlayerClan,
                    Clan.PlayerClan?.Kingdom);
        }

        private static string FormatTitleName(FeudalTitleRecord title)
        {
            return FormatTitleName(title, ResolveClan(title?.DeFactoHolderClanId) ?? ResolveClan(title?.DeJureHolderClanId));
        }

        private static string FormatTitleName(FeudalTitleRecord title, Clan styleClan)
        {
            return title == null
                ? string.Empty
                : FeudalTitleDisplayHelper.FormatTitleName(title, styleClan);
        }

        private static string FormatPercentWhole(float value)
        {
            return Math.Round(value * 100f).ToString("0");
        }

        private static string FormatSigned(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Hero ResolveHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId)
                ? null
                : Hero.FindFirst(hero => hero != null && hero.StringId == heroId);
        }

        private static Color CalculateTitleColor(Kingdom hierarchyKingdom, Clan deJureHolder, Clan deFactoHolder)
        {
            if (deJureHolder == null && deFactoHolder == null)
                return Colors.Gray;

            bool unifiedClanControl = deJureHolder != null
                && deFactoHolder != null
                && deJureHolder == deFactoHolder;
            if (!unifiedClanControl)
                return DividedColor;

            if (deFactoHolder == Clan.PlayerClan)
                return PlayerHeldColor;

            if (deFactoHolder.Kingdom == hierarchyKingdom)
                return RealmHeldColor;

            return Colors.Red;
        }
    }
}
