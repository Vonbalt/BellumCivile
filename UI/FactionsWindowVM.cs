using BellumCivile.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    /// <summary>
    /// Why did I do this file?
    /// This is the master ViewModel for the Gauntlet UI. It binds all complex backend math, like soft power apathy, trait modifiers, and dual-column policy agendas, directly into formats displayable on the campaign map menu.
    /// </summary>
    public class FactionsWindowVM : ViewModel
    {
        private MBBindingList<FactionItemVM> _factions;
        private FactionItemVM _selectedFaction;

        private bool _hasSelectedFaction;
        private string _headerText;

        private string _joinLeaveText;
        private bool _isJoinLeaveEnabled;
        private string _createDestroyText;
        private bool _isCreateDestroyEnabled;
        private bool _isDisbandMode;
        private bool _isSurrenderMode;
        private bool _isLoyalistSurrenderMode;
        private bool _isClaimFeudAbandonMode;
        private bool _isClaimFeudSurrenderMode;

        private HintViewModel _joinLeaveHint;
        private HintViewModel _createDestroyHint;
        private HintViewModel _ultimatumHint;
        private HintViewModel _discontentHint;
        private BasicTooltipViewModel _balanceOfPowerHint;

        private BasicTooltipViewModel _factionPowerHint;
        private BasicTooltipViewModel _loyalistPowerHint;
        private BasicTooltipViewModel _thresholdHint;

        private bool _isCreatingFaction;
        private MBBindingList<PoliticalActionVM> _availableFactionTypes;
        private PoliticalActionVM _selectedFactionType;
        private bool _isFoundCostVisible;
        private bool _isSelectedActionCostVisible;
        private string _selectedActionCostText;

        private bool _isUltimatumEnabled;

        private bool _isRebellionSelected;
        private bool _isIdeologySelected;
        private int _factionMood;
        private BasicTooltipViewModel _moodHint;

        private string _opposingSideText;
        private string _factionMoodText;
        private Color _factionMoodColor;
        private string _balanceOfPowerText;
        private Color _balanceOfPowerColor;
        private MBBindingList<AgendaItemVM> _factionLikesList;
        private MBBindingList<AgendaItemVM> _factionDislikesList;

        private MBBindingList<AgendaItemVM> _agendaListLeft;
        private MBBindingList<AgendaItemVM> _agendaListRight;

        public FactionsWindowVM()
        {
            Factions = new MBBindingList<FactionItemVM>();
            AvailableFactionTypes = new MBBindingList<PoliticalActionVM>();
            AgendaListLeft = new MBBindingList<AgendaItemVM>();
            AgendaListRight = new MBBindingList<AgendaItemVM>();
            FactionLikesList = new MBBindingList<AgendaItemVM>();
            FactionDislikesList = new MBBindingList<AgendaItemVM>();

            JoinLeaveHint = new HintViewModel();
            CreateDestroyHint = new HintViewModel();
            UltimatumHint = new HintViewModel();
            MoodHint = CreateEmptyTooltip();
            TextObject discontentHint = new TextObject("{=BC_UI_Hint_Discontent}Discontent rises daily whenever Faction Power exceeds the Current Threshold. The larger the power advantage, the faster it rises. At {THRESHOLD} rebellious intent, a formal ultimatum is automatically issued to the ruler.");
            discontentHint.SetTextVariable("THRESHOLD", C.DiscontentTrigger.ToString("0"));
            DiscontentHint = new HintViewModel(discontentHint);
            BalanceOfPowerHint = CreateTextTooltip(
                new TextObject("{=BC_UI_BalanceOfPowerTooltipTitle}Balance of Power"),
                new TextObject("{=BC_UI_Hint_BoP}Combined clan strength + influence of every clan supporting their faction measured against each other."));

            FactionPowerHint = CreateEmptyTooltip();
            LoyalistPowerHint = CreateEmptyTooltip();
            ThresholdHint = CreateEmptyTooltip();

            RefreshAvailableFactionTypes();
            RefreshFactionList(preserveSelection: false);
            
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void OnFinalize()
        {
            base.OnFinalize();
            CampaignEvents.DailyTickEvent.ClearListeners(this);
        }

        private void OnDailyTick()
        {
            RefreshAvailableFactionTypes();
            RefreshFactionList();
            RefreshButtonStates();
            SelectedFaction?.Refresh();
        }

        private Kingdom GetPlayerPoliticalKingdom(FactionManagerBehavior factionManager = null)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null) return null;

            factionManager = factionManager ?? Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject activeCivilWar = factionManager?.GetFactionByRebelKingdom(playerKingdom);
            return activeCivilWar?.ParentKingdom ?? playerKingdom;
        }

        private List<FactionObject> GetVisibleFactions(FactionManagerBehavior factionManager, Kingdom politicalKingdom)
        {
            if (factionManager == null || politicalKingdom == null)
                return new List<FactionObject>();

            return factionManager.GetFactionsInKingdom(politicalKingdom)
                .Where(f => f != null)
                .Distinct()
                .ToList();
        }

        private void RefreshFactionList(bool preserveSelection = true)
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject previousSelection = preserveSelection ? SelectedFaction?.BackendFactionModel as FactionObject : null;
            Kingdom politicalKingdom = GetPlayerPoliticalKingdom(factionManager);

            Factions.Clear();

            if (politicalKingdom != null)
            {
                TextObject headerObj = new TextObject("{=BC_UI_HeaderInKingdom}Factions in {KINGDOM_NAME}");
                headerObj.SetTextVariable("KINGDOM_NAME", politicalKingdom.Name);
                HeaderText = headerObj.ToString();

                foreach (FactionObject faction in GetVisibleFactions(factionManager, politicalKingdom))
                {
                    Factions.Add(new FactionItemVM(GetDynamicFactionName(faction), faction, OnFactionSelected));
                }
            }
            else
            {
                HeaderText = new TextObject("{=BC_UI_HeaderFactions}Factions").ToString();
            }

            FactionItemVM restoredSelection = previousSelection != null
                ? Factions.FirstOrDefault(f => f.BackendFactionModel == previousSelection)
                : null;

            if (restoredSelection != null) OnFactionSelected(restoredSelection);
            else if (!ShouldKeepFactionSelectionEmpty && Factions.Count > 0) OnFactionSelected(Factions[0]);
            else if (!ShouldKeepFactionSelectionEmpty) OnFactionSelected(null);
        }

        protected virtual bool ShouldKeepFactionSelectionEmpty => false;

        private static bool IsIdeologyType(FactionType type)
        {
            return type == FactionType.Glory
                || type == FactionType.Nobility
                || type == FactionType.Liberty;
        }

        private void RefreshAvailableFactionTypes()
        {
            AvailableFactionTypes.Clear();

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            bool isMercenary = Clan.PlayerClan?.IsUnderMercenaryService ?? false;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            Kingdom politicalKingdom = GetPlayerPoliticalKingdom(factionManager);
            bool isRuler = playerKingdom != null && playerKingdom.RulingClan == Clan.PlayerClan;
            List<FactionObject> activeFactions = GetVisibleFactions(factionManager, politicalKingdom);
            bool playerHasThroneClaim = RoyalistClaimHelper.TryGetThroneClaimStrength(Clan.PlayerClan, politicalKingdom, factionManager, out _);

            FactionObject currentIdeology = factionManager?.GetIdeologicalFaction(Clan.PlayerClan);

            foreach (FactionType type in Enum.GetValues(typeof(FactionType)))
            {
                if (type == FactionType.Royalists)
                    continue;

                bool isIdeology = IsIdeologyType(type);

                if (isIdeology && (politicalKingdom == null || activeFactions.Any(f => f.Type == type)))
                    continue;

                bool isEnabled = true;
                string disabledReason = "";

                if (isMercenary)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_Mercenary}Mercenaries cannot found political factions.").ToString();
                }
                else if (type == FactionType.InstallRuler && !playerHasThroneClaim)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_ThroneClaim}You need a weak or strong claim to the kingdom title before you can press your right to the throne.").ToString();
                }

                if (isRuler)
                {
                    isEnabled = false;
                    disabledReason = isIdeology ? CourtMembershipEligibility.RulerReason.ToString()
                        : new TextObject("{=BC_UI_Err_RulerSelf}You cannot plot a rebellion against yourself.").ToString();
                }

                if (isIdeology && currentIdeology != null)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_AlreadyCourtFaction}You already belong to a court faction. Leave it before founding another party.").ToString();
                }

                AvailableFactionTypes.Add(PoliticalActionVM.ForFaction(
                    type,
                    GetPrettyCreationName(type),
                    OnFactionTypeSelected,
                    isEnabled,
                    BuildFactionTypeHint(type, disabledReason),
                    100));
            }

            AppendClaimFeudActions(isMercenary, isRuler, playerRebelFaction: factionManager?.GetRebelFaction(Clan.PlayerClan));

            if (SelectedFactionType != null)
            {
                PoliticalActionVM refreshedSelection = AvailableFactionTypes.FirstOrDefault(f =>
                    f.ActionKind == SelectedFactionType.ActionKind
                    && f.FactionType == SelectedFactionType.FactionType
                    && f.TargetTitleId == SelectedFactionType.TargetTitleId);
                if (refreshedSelection != null)
                {
                    SelectedFactionType = refreshedSelection;
                    SelectedFactionType.IsSelected = true;
                }
                else
                {
                    SelectedFactionType = null;
                }
            }

            RefreshSelectedActionCost();
        }

        private void AppendClaimFeudActions(bool isMercenary, bool isRuler, FactionObject playerRebelFaction)
        {
            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            IReadOnlyList<ClaimFeudActionPreview> previews = feudBehavior?.GetPressableFeudPreviews(Clan.PlayerClan) ?? Array.Empty<ClaimFeudActionPreview>();
            foreach (ClaimFeudActionPreview preview in previews)
            {
                bool isEnabled = preview.IsEnabled;
                string disabledReason = preview.DisabledReason ?? string.Empty;
                if (isMercenary)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_Mercenary}Mercenaries cannot found political factions.").ToString();
                }
                else if (isRuler)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_RulerFeud}Rulers settle title disputes through judgment and decree, not private house feuds.").ToString();
                }
                else if (playerRebelFaction != null)
                {
                    isEnabled = false;
                    disabledReason = new TextObject("{=BC_UI_Err_AlreadyRebel}You are already participating in a rebellion.").ToString();
                }

                TextObject name = new TextObject("{=BC_Menu_StartClaimFeud}Start feud over {TITLE_NAME}");
                name.SetTextVariable("TITLE_NAME", preview.TitleName);

                AvailableFactionTypes.Add(PoliticalActionVM.ForClaimFeud(
                    new ClaimFeudActionPreview
                    {
                        TitleId = preview.TitleId,
                        TitleName = preview.TitleName,
                        HolderClanId = preview.HolderClanId,
                        HolderName = preview.HolderName,
                        Strength = preview.Strength,
                        Score = preview.Score,
                        IsEnabled = isEnabled,
                        DisabledReason = disabledReason
                    },
                    OnFactionTypeSelected,
                    name.ToString(),
                    BuildClaimFeudActionHint(preview, disabledReason)));
            }
        }

        private string BuildClaimFeudActionHint(ClaimFeudActionPreview preview, string disabledReason = "")
        {
            if (preview == null)
                return string.Empty;

            TextObject description = new TextObject("{=BC_UI_Hint_StartClaimFeud}Press your {CLAIM_STRENGTH} claim to the {TITLE_NAME}.\n\nCurrent holder: {HOLDER_CLAN}\nFeud pressure score: {SCORE}\n\nStarting this feud will begin a private dispute between your house and the title holder. If tensions escalate, the matter may be brought before the ruler.");
            description.SetTextVariable("CLAIM_STRENGTH", GetClaimStrengthText(preview.Strength));
            description.SetTextVariable("TITLE_NAME", preview.TitleName);
            description.SetTextVariable("HOLDER_CLAN", preview.HolderName);
            description.SetTextVariable("SCORE", preview.Score.ToString("0"));

            if (string.IsNullOrWhiteSpace(disabledReason))
                return description.ToString();

            TextObject hint = new TextObject("{=BC_UI_Hint_FactionType_WithReason}{DESCRIPTION}\n\nUnavailable: {REASON}");
            hint.SetTextVariable("DESCRIPTION", description.ToString());
            hint.SetTextVariable("REASON", disabledReason);
            return hint.ToString();
        }

        private static TextObject GetClaimStrengthText(FeudalClaimStrength strength)
        {
            return strength == FeudalClaimStrength.Strong
                ? new TextObject("{=BC_ClaimStrength_Strong}strong")
                : new TextObject("{=BC_ClaimStrength_Weak}weak");
        }

        private string GetDynamicFactionName(FactionObject faction)
        {
            return faction?.GetDisplayName().ToString() ?? string.Empty;
        }

        private string GetPrettyCreationName(FactionType type)
        {
            switch (type)
            {
                case FactionType.Independence: return new TextObject("{=BC_Menu_Indep}Independence Movement").ToString();
                case FactionType.Abdication: return new TextObject("{=BC_Menu_Abdic}Overthrow Ruler").ToString();
                case FactionType.InstallRuler: return new TextObject("{=BC_Menu_Install}Claim the Throne").ToString();
                case FactionType.Royalists:
                case FactionType.Glory:
                case FactionType.Nobility:
                case FactionType.Liberty:
                    TextObject found = new TextObject("{=BC_Menu_FoundCourtFaction}Found {FACTION_NAME}");
                    found.SetTextVariable(
                        "FACTION_NAME",
                        CourtInstitutionDisplayHelper.GetCourtFactionName(type, Clan.PlayerClan?.Kingdom));
                    return found.ToString();
                default: return type.ToString();
            }
        }

        private string GetFactionTypeHint(FactionType type)
        {
            switch (type)
            {
                case FactionType.Independence:
                    return new TextObject("{=BC_UI_Hint_FactionType_Indep}Break away from the realm and found a new independent kingdom.").ToString();
                case FactionType.Abdication:
                    return new TextObject("{=BC_UI_Hint_FactionType_Abdic}Force the current ruler to abdicate and let the realm crown a new leader.").ToString();
                case FactionType.InstallRuler:
                    return new TextObject("{=BC_UI_Hint_FactionType_Install}Depose the current ruler and press a weak or strong claim to the kingdom title by force. Dynastic inheritance or claim fabrication can provide the legal pretext.").ToString();
                case FactionType.Royalists:
                    return new TextObject("{=BC_UI_Hint_FactionType_Royalists}Defends lawful succession, established tradition, and crown authority. Like other court factions, it can demand the ruler's abdication when deeply discontented.").ToString();
                case FactionType.Glory:
                    return new TextObject("{=BC_UI_Hint_FactionType_Militarists}Advocates constant war, military prestige, and rule by strength rather than compromise.").ToString();
                case FactionType.Nobility:
                    return new TextObject("{=BC_UI_Hint_FactionType_Aristocrats}Defends noble privilege, inheritance, and the political rights of the great clans.").ToString();
                case FactionType.Liberty:
                    return new TextObject("{=BC_UI_Hint_FactionType_Populists}Advocates broader opportunity, relief for lesser lords, and policies favoring the common people.").ToString();
                default:
                    return "";
            }
        }

        private string BuildFactionTypeHint(FactionType type, string disabledReason = "")
        {
            string description = GetFactionTypeHint(type);
            if (string.IsNullOrWhiteSpace(disabledReason))
            {
                return description;
            }

            TextObject hint = new TextObject("{=BC_UI_Hint_FactionType_WithReason}{DESCRIPTION}\n\nUnavailable: {REASON}");
            hint.SetTextVariable("DESCRIPTION", description);
            hint.SetTextVariable("REASON", disabledReason);
            return hint.ToString();
        }

        protected virtual void OnFactionSelected(FactionItemVM faction)
        {
            if (SelectedFaction != null) SelectedFaction.IsSelected = false;
            SelectedFaction = faction;
            if (SelectedFaction != null) SelectedFaction.IsSelected = true;

            HasSelectedFaction = SelectedFaction != null;
            RefreshButtonStates();
            OnRegularFactionSelected();
        }

        protected virtual void OnRegularFactionSelected()
        {
        }

        private void OnFactionTypeSelected(PoliticalActionVM typeVM)
        {
            if (SelectedFactionType != null) SelectedFactionType.IsSelected = false;
            SelectedFactionType = typeVM;
            SelectedFactionType.IsSelected = true;
            RefreshSelectedActionCost();
        }

        private void RefreshSelectedActionCost()
        {
            if (SelectedFactionType != null && SelectedFactionType.InfluenceCost > 0)
            {
                IsSelectedActionCostVisible = true;
                SelectedActionCostText = SelectedFactionType.InfluenceCost.ToString();
            }
            else
            {
                IsSelectedActionCostVisible = false;
                SelectedActionCostText = string.Empty;
            }
        }

        private static bool CanAffordAction(PoliticalActionVM action)
        {
            if (action == null || !action.IsEnabled)
                return false;

            return action.InfluenceCost <= 0 || Clan.PlayerClan.Influence >= action.InfluenceCost;
        }

        protected virtual ClaimFeudRecord GetSelectedClaimFeudRecord()
        {
            return null;
        }

        protected virtual void OnClaimFeudActionCompleted()
        {
        }

        private bool TryApplySelectedClaimFeudButtonState(ClaimFeudRecord record)
        {
            if (record != null && Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>() is ClaimFeudBehavior feuds
                && feuds.IsPlayerFeudSupporter(record))
            {
                JoinLeaveText = new TextObject("{=BC_UI_Leave}Leave").ToString();
                var block = feuds.PlayerWithdrawalBlock(record);
                IsJoinLeaveEnabled = block == null;
                var hint = new TextObject("{=BC_FeudLeave_Hint}Withdraw your house from this feud. Your former leader will remember you as an oathbreaker (-20 relations), as will the other houses on your side (-10), for {YEARS} years. During a private war, your house returns to the parent realm: its prewar estates are restored and its feud conquests returned. You cannot rejoin this dispute.");
                hint.SetTextVariable("YEARS", (10f * BellumCivileOptions.RelationMemoryDurationMultiplier).ToString("0.#"));
                JoinLeaveHint = new HintViewModel(block ?? hint);
                IsCreateDestroyEnabled = false;
                IsFoundCostVisible = false;
                CreateDestroyText = new TextObject("{=BC_UI_AbandonFeud}Abandon").ToString();
                CreateDestroyHint = new HintViewModel(new TextObject("{=BC_FeudLeave_NotSupporter}Only a pledged supporting house may leave. The principal houses must settle the dispute."));
                return true;
            }
            if (record == null || record.ClaimantClanId != Clan.PlayerClan?.StringId)
                return false;

            IsFoundCostVisible = false;
            _isDisbandMode = false;
            _isSurrenderMode = false;

            if (record.State == ClaimFeudState.Agitating
                || record.State == ClaimFeudState.PetitionReady
                || record.State == ClaimFeudState.Paused)
            {
                _isClaimFeudAbandonMode = true;
                CreateDestroyText = new TextObject("{=BC_UI_AbandonFeud}Abandon").ToString();
                IsCreateDestroyEnabled = true;
                CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Hint_AbandonFeud}Cool this feud before it escalates. Your claim remains, but this dispute enters a cooldown."));
                return true;
            }

            if (record.State == ClaimFeudState.DefiedPendingWar || record.State == ClaimFeudState.WarActive)
            {
                _isClaimFeudSurrenderMode = true;
                CreateDestroyText = new TextObject("{=BC_UI_Surrender}Surrender").ToString();
                IsCreateDestroyEnabled = true;
                CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Hint_SurrenderFeud}Surrender this feud. The holder's right will be upheld and your claim will be weakened or removed."));
                return true;
            }

            if (record.State == ClaimFeudState.AwaitingPlayerResponse)
            {
                CreateDestroyText = new TextObject("{=BC_ClaimFeud_PlayerRuling_Accept}Accept Judgment").ToString();
                IsCreateDestroyEnabled = false;
                CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Hint_AwaitingFeudResponse}Answer the ruler's judgment in the pending message."));
                return true;
            }

            CreateDestroyText = new TextObject("{=BC_UI_AbandonFeud}Abandon").ToString();
            IsCreateDestroyEnabled = false;
            CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_FeudAlreadyResolved}This feud is already resolved or cooling down."));
            return true;
        }

        private bool ShouldShowMainFoundCost()
        {
            List<PoliticalActionVM> enabledActions = AvailableFactionTypes
                .Where(action => action != null && action.IsEnabled)
                .ToList();

            return enabledActions.Count > 0
                && enabledActions.All(action => action.InfluenceCost > 0)
                && enabledActions.Select(action => action.InfluenceCost).Distinct().Count() == 1;
        }

        protected void RefreshButtonStates()
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject selectedBackendFaction = SelectedFaction?.BackendFactionModel as FactionObject;

            bool isIdeology = selectedBackendFaction?.IsIdeology ?? false;
            bool isActiveCivilWarRebellion = selectedBackendFaction != null && !isIdeology && selectedBackendFaction.IsCivilWarActive();
            bool isCourtFactionInActiveCivilWar = IsCourtFactionInActiveCivilWar(selectedBackendFaction, factionManager);
            Kingdom selectedRebelKingdom = isActiveCivilWarRebellion ? selectedBackendFaction.GetRebelKingdom() : null;
            bool playerInSelectedRebelKingdom = selectedRebelKingdom != null && Clan.PlayerClan?.Kingdom == selectedRebelKingdom;
            bool playerInSelectedParentKingdom = selectedBackendFaction?.ParentKingdom != null && Clan.PlayerClan?.Kingdom == selectedBackendFaction.ParentKingdom;
            IsRebellionSelected = selectedBackendFaction != null && !isIdeology;
            IsIdeologySelected = selectedBackendFaction != null && isIdeology;

            FactionObject playerRelevantFaction = null;
            if (selectedBackendFaction != null)
            {
                playerRelevantFaction = isIdeology
                    ? factionManager?.GetIdeologicalFaction(Clan.PlayerClan)
                    : factionManager?.GetRebelFaction(Clan.PlayerClan);
            }

            bool isRuler = Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom.RulingClan == Clan.PlayerClan;
            bool isMercenary = Clan.PlayerClan?.IsUnderMercenaryService ?? false;
            JoinLeaveHint = new HintViewModel(new TextObject(""));
            CreateDestroyHint = new HintViewModel(new TextObject(""));
            UltimatumHint = new HintViewModel(new TextObject(""));
            MoodHint = CreateEmptyTooltip();
            _isClaimFeudAbandonMode = false;
            _isClaimFeudSurrenderMode = false;
            _isLoyalistSurrenderMode = false;

            if (playerRelevantFaction != null)
            {
                if (playerRelevantFaction == selectedBackendFaction)
                {
                    JoinLeaveText = isActiveCivilWarRebellion && playerInSelectedRebelKingdom
                        ? new TextObject("{=BC_UI_RejoinCrown}Rejoin Crown").ToString()
                        : new TextObject("{=BC_UI_Leave}Leave").ToString();

                    if (!isIdeology && playerRelevantFaction.Leader == Clan.PlayerClan)
                    {
                        IsJoinLeaveEnabled = false;
                        JoinLeaveHint = isActiveCivilWarRebellion
                            ? new HintViewModel(new TextObject("{=BC_UI_Err_LeaveLeadWar}You cannot abandon a rebellion you lead during a civil war. Use Surrender if you wish to submit to the crown."))
                            : new HintViewModel(new TextObject("{=BC_UI_Err_LeaveLead}You cannot leave a rebellion you lead. You must disband it."));
                    }
                    else
                    {
                        IsJoinLeaveEnabled = true;
                        JoinLeaveHint = isActiveCivilWarRebellion && playerInSelectedRebelKingdom
                            ? new HintViewModel(new TextObject("{=BC_UI_Hint_RejoinCrown}Abandon the rebel side and return to the crown in the ongoing civil war."))
                            : new HintViewModel(new TextObject("{=BC_UI_Hint_Leave}Leave this faction."));
                    }
                }
                else
                {
                    JoinLeaveText = new TextObject("{=BC_UI_Join}Join").ToString();
                    IsJoinLeaveEnabled = false;
                    JoinLeaveHint = new HintViewModel(new TextObject(isIdeology ? "{=BC_UI_Err_JoinIdeology}You are already part of another political party." : "{=BC_UI_Err_JoinRebellion}You are already part of another rebellion."));
                }
            }
            else
            {
                JoinLeaveText = isActiveCivilWarRebellion
                    ? new TextObject("{=BC_UI_Defect}Defect").ToString()
                    : new TextObject("{=BC_UI_Join}Join").ToString();
                if (isMercenary)
                {
                    IsJoinLeaveEnabled = false;
                    JoinLeaveHint = new HintViewModel(new TextObject("{=BC_UI_Err_MercJoin}Mercenaries cannot participate in kingdom politics."));
                }
                else if (isRuler)
                {
                    IsJoinLeaveEnabled = false;
                    JoinLeaveHint = new HintViewModel(isIdeology ? CourtMembershipEligibility.RulerReason
                        : new TextObject("{=BC_UI_Err_JoinSelf}You can't join a rebellion against yourself."));
                }
                else if (isIdeology && !CourtMembershipEligibility.CanBelong(Clan.PlayerClan, selectedBackendFaction.ParentKingdom))
                {
                    IsJoinLeaveEnabled = false;
                    JoinLeaveHint = new HintViewModel(CourtMembershipEligibility.IneligibleReason);
                }
                else if (isCourtFactionInActiveCivilWar)
                {
                    IsJoinLeaveEnabled = false;
                    JoinLeaveHint = new HintViewModel(new TextObject("{=BC_UI_Err_JoinCourtFactionCivilWar}This court faction is already fighting in a civil war. Join the active rebel coalition instead if you want to switch sides."));
                }
                else
                {
                    IsJoinLeaveEnabled = selectedBackendFaction != null && (!isActiveCivilWarRebellion || playerInSelectedParentKingdom);
                    if (!IsJoinLeaveEnabled) JoinLeaveHint = new HintViewModel(new TextObject("{=BC_UI_Hint_SelectJoin}Select a faction to join."));
                    else JoinLeaveHint = isActiveCivilWarRebellion
                        ? new HintViewModel(new TextObject("{=BC_UI_Hint_DefectCivilWar}Defect to the rebel side in the ongoing civil war."))
                        : new HintViewModel(new TextObject("{=BC_UI_Hint_Join}Join this faction."));
                }
            }

            FactionObject playerRebelFaction = factionManager?.GetRebelFaction(Clan.PlayerClan);

            ClaimFeudRecord selectedClaimFeud = GetSelectedClaimFeudRecord();
            if (TryApplySelectedClaimFeudButtonState(selectedClaimFeud))
            {
            }
            else if (isActiveCivilWarRebellion
                && playerInSelectedParentKingdom
                && selectedBackendFaction.ParentKingdom.RulingClan == Clan.PlayerClan)
            {
                CreateDestroyText = new TextObject("{=BC_UI_Surrender}Surrender").ToString();
                _isDisbandMode = false;
                _isSurrenderMode = false;
                _isLoyalistSurrenderMode = true;
                IsCreateDestroyEnabled = selectedRebelKingdom != null;
                IsFoundCostVisible = false;
                CreateDestroyHint = IsCreateDestroyEnabled
                    ? new HintViewModel(new TextObject("{=BC_UI_Hint_LoyalistSurrender}Yield to the selected rebellion and enforce its demands as a rebel victory."))
                    : new HintViewModel(new TextObject("{=BC_UI_Err_LoyalistSurrenderUnavailable}The crown cannot surrender because the active rebel realm could not be found."));
            }
            else if (PlayerLeadsRebellion(playerRebelFaction))
            {
                bool playerRebellionIsActiveCivilWar = playerRebelFaction.IsCivilWarActive();
                CreateDestroyText = playerRebellionIsActiveCivilWar
                    ? new TextObject("{=BC_UI_Surrender}Surrender").ToString()
                    : new TextObject("{=BC_UI_Disband}Disband").ToString();
                _isDisbandMode = true;
                _isSurrenderMode = playerRebellionIsActiveCivilWar;
                IsFoundCostVisible = false;

                if (selectedBackendFaction == playerRebelFaction)
                {
                    IsCreateDestroyEnabled = true;
                    CreateDestroyHint = playerRebellionIsActiveCivilWar
                        ? new HintViewModel(new TextObject("{=BC_UI_Hint_Surrender}Surrender your rebellion to the crown and resolve the civil war as a loyalist victory."))
                        : new HintViewModel(new TextObject("{=BC_UI_Hint_Disband}Disband your active rebellion."));
                }
                else if (isIdeology)
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_DisbandIdeology}Courtly factions are permanent and cannot be disbanded. Select your rebellion to disband it."));
                }
                else
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_DisbandOther}You can only disband a rebellion that you lead. Select your rebellion to disband it."));
                }
            }
            else
            {
                CreateDestroyText = new TextObject("{=BC_UI_Found}Found").ToString();
                _isDisbandMode = false;
                _isSurrenderMode = false;

                if (isMercenary)
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_MercFound}Mercenaries cannot found political factions."));
                    IsFoundCostVisible = false;
                }
                else if (playerRebelFaction != null)
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_AlreadyRebel}You are already participating in a rebellion."));
                    IsFoundCostVisible = true;
                }
                else if (AvailableFactionTypes.Count == 0 || AvailableFactionTypes.All(f => !f.IsEnabled))
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_NoValidFactions}There are no valid factions for you to found right now."));
                    IsFoundCostVisible = false;
                }
                else if (!AvailableFactionTypes.Any(CanAffordAction))
                {
                    IsCreateDestroyEnabled = false;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Err_NeedInfluence}You need more influence to perform any available political action."));
                    IsFoundCostVisible = ShouldShowMainFoundCost();
                }
                else
                {
                    IsCreateDestroyEnabled = true;
                    CreateDestroyHint = new HintViewModel(new TextObject("{=BC_UI_Hint_Found}Found a new faction or press a political claim."));
                    IsFoundCostVisible = ShouldShowMainFoundCost();
                }
            }

            if (selectedBackendFaction != null && selectedBackendFaction.ParentKingdom != null)
            {
                RebellionPowerProjection powerProjection = selectedBackendFaction.CalculatePowerProjection(forceRefresh: true);
                FactionPowerHint = BuildProjectedFactionPowerTooltip(powerProjection, factionManager);

                OpposingSideText = new TextObject("{=BC_UI_Opposing_Loyalists}Loyalists").ToString();

                bool isIdeologyFaction = selectedBackendFaction.IsIdeology;
                Kingdom factionKingdom = selectedBackendFaction.ParentKingdom;

                LoyalistPowerHint = BuildProjectedLoyalistPowerTooltip(
                    powerProjection,
                    factionKingdom,
                    factionManager,
                    isIdeologyFaction);

                // What does this complex formula do?
                // Calculates the balance of power against the rebel faction and dynamically adjusts the danger threshold based on the faction leader's traits.
                float currentFactionPower = powerProjection.FactionPower;
                float currentLoyalistPower = powerProjection.LoyalistPower;
                int balancePct = currentLoyalistPower > 0f ? (int)((currentFactionPower / currentLoyalistPower) * 100f) : 999;

                int currentThreshold = 80;
                List<TooltipProperty> thresholdProperties = CreateTooltipWithTitle(new TextObject("{=BC_BoP_TooltipTitle}Balance Threshold"));
                AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_BaseLabel}Base value"), "80%");

                Hero factionLeader = selectedBackendFaction.Leader?.Leader;

                if (factionLeader != null)
                {
                    int calcLevel = factionLeader.GetTraitLevel(DefaultTraits.Calculating);
                    if (calcLevel <= -2) { currentThreshold -= 20; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_HotheadedLabel}Leader is Hotheaded"), "-20%"); }
                    else if (calcLevel == -1) { currentThreshold -= 10; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_ImpulsiveLabel}Leader is Impulsive"), "-10%"); }
                    else if (calcLevel == 1) { currentThreshold += 10; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_CautiousLabel}Leader is Cautious"), "+10%"); }
                    else if (calcLevel >= 2) { currentThreshold += 20; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_CalculatingLabel}Leader is Calculating"), "+20%"); }

                    int valorLevel = factionLeader.GetTraitLevel(DefaultTraits.Valor);
                    if (valorLevel >= 2) { currentThreshold -= 20; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_FearlessLabel}Leader is Fearless"), "-20%"); }
                    else if (valorLevel == 1) { currentThreshold -= 10; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_BraveLabel}Leader is Brave"), "-10%"); }
                    else if (valorLevel == -1) { currentThreshold += 10; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_CarefulLabel}Leader is Careful"), "+10%"); }
                    else if (valorLevel <= -2) { currentThreshold += 20; AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_CravenLabel}Leader is Craven"), "+20%"); }
                }

                if (currentThreshold < 40) currentThreshold = 40;

                BalanceOfPowerText = $"{balancePct}%";
                if (balancePct >= currentThreshold) BalanceOfPowerColor = Color.ConvertStringToColor("#82E06AFF");
                else if (balancePct >= currentThreshold - 20) BalanceOfPowerColor = Color.ConvertStringToColor("#FF8C00FF");
                else BalanceOfPowerColor = Colors.Red;

                AddTooltipSeparator(thresholdProperties);
                AddTooltipRow(thresholdProperties, new TextObject("{=BC_BoP_CurrentThresholdLabel}Current Threshold"), currentThreshold + "%");
                ThresholdHint = new BasicTooltipViewModel(() => thresholdProperties);

                if (isIdeology)
                {
                    IsUltimatumEnabled = false;
                    FactionMood = (int)selectedBackendFaction.Mood;

                    int rawMood = (int)selectedBackendFaction.Mood;
                    TextObject moodState;
                    Color moodColor = Colors.White;

                    if (rawMood >= 61) { moodState = new TextObject("{=BC_MoodState_Loyal}Loyal"); moodColor = Color.ConvertStringToColor("#82E06AFF"); }
                    else if (rawMood >= 21) { moodState = new TextObject("{=BC_MoodState_Happy}Happy"); moodColor = Color.ConvertStringToColor("#82E06AFF"); }
                    else if (rawMood >= -20) { moodState = new TextObject("{=BC_MoodState_Neutral}Neutral"); moodColor = Color.ConvertStringToColor("#F1D8A4FF"); }
                    else if (rawMood >= -59) { moodState = new TextObject("{=BC_MoodState_Unhappy}Unhappy"); moodColor = Color.ConvertStringToColor("#FF8C00FF"); }
                    else { moodState = new TextObject("{=BC_MoodState_Rebellious}Rebellious"); moodColor = Colors.Red; }

                    FactionMoodText = $"{moodState.ToString()} ({rawMood})";
                    FactionMoodColor = moodColor;

                    var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
                    var shockBehavior = Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>();
                    string breakdownText = "";
                    List<TooltipProperty> moodProperties = CreateTooltipWithTitle(new TextObject("{=BC_UI_FactionMoodTooltipTitle}Faction Mood"));
                    AddTooltipRow(moodProperties, new TextObject("{=BC_UI_MoodCurrentLabel}Current Mood"), rawMood.ToString());
                    if (selectedBackendFaction.AccommodationBonus > 0)
                    {
                        AddTooltipRow(moodProperties, new TextObject("{=BC_CrownUnderlyingMood}Underlying mood"), selectedBackendFaction.UnderlyingMood.ToString("0.#"));
                        AddTooltipRow(moodProperties, new TextObject("{=BC_CrownAccommodationMood}Crown accommodation"), "+20");
                        AddTooltipMessage(moodProperties, new TextObject("{=BC_CrownAccommodationUntil}The Crown's accommodation lasts until {DATE}, or until the ruler is replaced. Underlying mood continues to drift toward its baseline; this temporary allowance is added separately.")
                            .SetTextVariable("DATE", selectedBackendFaction.CrownAccommodation.Until.ToString()));
                    }
                    
                    if (ideologyBehavior != null && factionManager != null)
                    {
                        var (targetBaseline, breakdown) = ideologyBehavior.GetTargetBaselineBreakdown(selectedBackendFaction, selectedBackendFaction.ParentKingdom, factionManager);
                        breakdownText = breakdown;
                        AddTooltipRow(moodProperties, new TextObject("{=BC_UI_MoodBaselineLabel}Target Baseline"), targetBaseline.ToString("0.#"));
                        AddTooltipSeparator(moodProperties);
                        foreach (string line in SplitTooltipLines(breakdown))
                            AddTooltipBreakdownLine(moodProperties, line);
                    }
                    AddTooltipSeparator(moodProperties);
                    AddTooltipMessage(moodProperties, new TextObject("{=BC_UI_MoodHintNote}Mood drifts by 1 point daily towards the Target Baseline. Recent world events apply immediate temporary spikes to the current mood."));
                    MoodHint = new BasicTooltipViewModel(() => moodProperties);

                    EnactedPolicyLists.Populate(selectedBackendFaction.ParentKingdom, selectedBackendFaction.Type, false,
                        AgendaListLeft, AgendaListRight);

                    FactionLikesList.Clear();
                    FactionDislikesList.Clear();

                    Color neutralColor = Color.ConvertStringToColor("#F1D8A4FF");
                    Color greenColor = Color.ConvertStringToColor("#82E06AFF");
                    Color redColor = Colors.Red;

                    bool hasRecentAward = shockBehavior?.HasRecentFiefAward(selectedBackendFaction) ?? false;
                    bool hasRecentSnub = shockBehavior?.HasRecentFiefSnub(selectedBackendFaction) ?? false;
                    bool hasRecentExecution = shockBehavior?.HasRecentNobleExecution(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentCapture = shockBehavior?.HasRecentRulerCapture(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentRaid = shockBehavior?.HasRecentVillageRaid(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentVictory = shockBehavior?.HasRecentMajorVictory(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentDefeat = shockBehavior?.HasRecentMajorDefeat(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentPromotion = shockBehavior?.HasRecentCompanionPromotion(selectedBackendFaction.ParentKingdom) ?? false;
                    bool hasRecentWin = shockBehavior?.HasRecentCandidateWon(selectedBackendFaction) ?? false;
                    bool hasRecentLoss = shockBehavior?.HasRecentCandidateLost(selectedBackendFaction) ?? false;
                    bool hasRecentHumiliation = shockBehavior?.HasRecentFiefHumiliation(selectedBackendFaction) ?? false;
                    bool hasRecentExpulsionAttempt = shockBehavior?.HasRecentExpulsionAttempt(selectedBackendFaction) ?? false;
                    bool hasRecentCouncilAppointment = shockBehavior?.HasRecentCouncilAppointment(selectedBackendFaction) ?? false;
                    bool hasRecentCouncilRetention = shockBehavior?.HasRecentCouncilRetention(selectedBackendFaction) ?? false;
                    bool hasRecentCouncilSnub = shockBehavior?.HasRecentCouncilSnub(selectedBackendFaction) ?? false;
                    bool hasRecentCouncilOverride = shockBehavior?.HasRecentCouncilOverride(selectedBackendFaction) ?? false;
                    bool hasRecentCouncilDismissal = shockBehavior?.HasRecentCouncilDismissal(selectedBackendFaction) ?? false;

                    foreach (var condition in ideologyBehavior.GetMoodConditions(selectedBackendFaction, selectedBackendFaction.ParentKingdom, factionManager))
                    {
                        var list = condition.Positive ? FactionLikesList : FactionDislikesList;
                        list.Add(new AgendaItemVM(condition.Label, condition.Value == 0 ? neutralColor : condition.Positive ? greenColor : redColor,
                            CourtMoodPresentation.Hint(condition.Id, selectedBackendFaction.Type, selectedBackendFaction.ParentKingdom)
                            + "\n\n" + condition.Rule + "\n"
                            + new TextObject("{=BC_CourtMoodContribution}Current contribution to target mood: {VALUE}")
                                .SetTextVariable("VALUE", condition.Value.ToString("+0;-0;0")).ToString()));
                    }
                    void Recent(bool active, bool positive, string id, string label)
                    {
                        if (!active) return;
                        (positive ? FactionLikesList : FactionDislikesList).Add(new AgendaItemVM(
                            CourtMoodPresentation.Name(id, selectedBackendFaction.Type, label), positive ? greenColor : redColor,
                            CourtMoodPresentation.Hint(id, selectedBackendFaction.Type, selectedBackendFaction.ParentKingdom)));
                    }
                    Recent(hasRecentAward, true, "BC_Ag_AwardedFief", "Faction Member Awarded Fief");
                    Recent(hasRecentSnub, false, "BC_Ag_SnubFief", "Competing Faction Awarded Fief");
                    Recent(hasRecentWin, true, "BC_Ag_WinElec", "Supported Candidate Won Election");
                    Recent(hasRecentLoss, false, "BC_Ag_LostElec", "Supported Candidate Lost Election");
                    Recent(hasRecentHumiliation, false, "BC_Ag_FiefHumiliation", "Member Dispossessed");
                    Recent(hasRecentExpulsionAttempt, false, "BC_Ag_ExpulsionAttempt", "Member Accused of Treason");
                    Recent(shockBehavior?.HasRecentTribunalApproval(selectedBackendFaction) == true,
                        true, "BC_Ag_TribunalApproval", "Judgment of the Defeated Welcomed");
                    Recent(shockBehavior?.HasRecentTribunalReprisal(selectedBackendFaction) == true,
                        false, "BC_Ag_TribunalReprisal", "Judgment of the Defeated Resented");
                    if (selectedBackendFaction.Type == FactionType.Glory)
                    {
                        Recent(hasRecentVictory, true, "BC_Ag_MilVic", "Recent Major Victory");
                        Recent(hasRecentDefeat, false, "BC_Ag_MilDef", "Recent Major Defeat");
                        Recent(hasRecentCapture, false, "BC_Ag_RulerCaptured", "Ruler Captured");
                    }
                    if (selectedBackendFaction.Type == FactionType.Liberty)
                    {
                        Recent(hasRecentPromotion, true, "BC_Ag_NewHouse", "New House Ennobled");
                        Recent(hasRecentRaid, false, "BC_Ag_Raids", "Villages Raided");
                    }
                    if (selectedBackendFaction.Type == FactionType.Nobility)
                        Recent(hasRecentExecution, false, "BC_Ag_NobleExecuted", "Noble Executed");

                    Recent(selectedBackendFaction.Type == FactionType.Nobility
                        && shockBehavior?.HasRecentLordImprisoned(selectedBackendFaction.ParentKingdom) == true,
                        false, "BC_Ag_LordCaptured", "A Noble in Enemy Hands");
                    Recent(selectedBackendFaction.Type == FactionType.Glory
                        && shockBehavior?.HasRecentMaterialEvent(selectedBackendFaction.ParentKingdom, "Conquest") == true,
                        true, "BC_Ag_Conquest", "Our Banners Advance");
                    Recent(selectedBackendFaction.Type == FactionType.Glory
                        && shockBehavior?.HasRecentMaterialEvent(selectedBackendFaction.ParentKingdom, "LostSettlement") == true,
                        false, "BC_Ag_LostSettlement", "Our Hold Broken");
                    Recent(selectedBackendFaction.Type == FactionType.Liberty
                        && shockBehavior?.HasRecentMaterialEvent(selectedBackendFaction.ParentKingdom, "SettlementRebellion") == true,
                        false, "BC_Ag_SettlementRebellion", "A Town in Revolt");
                    Recent(shockBehavior?.HasRecentMaterialEvent(selectedBackendFaction.ParentKingdom, "Agitation_" + selectedBackendFaction.Type) == true,
                        false, "BC_Ag_Agitation", "Whispers Against the Crown");

                    var agendaBehavior = CourtAgendaBehavior.Current;
                    if (agendaBehavior != null)
                        foreach (var result in agendaBehavior.RecentResults(selectedBackendFaction))
                        {
                            bool positive = CourtAgendaBehavior.ReactionApproval(result) == true;
                            string id = result.IsPolicy || result.State == CourtAgendaState.Passed || result.State == CourtAgendaState.Defeated
                                ? positive ? "BC_CourtAgendaSuccess" : "BC_CourtAgendaFailure"
                                : positive ? "BC_CourtObjectiveSuccess" : "BC_CourtObjectiveFailure";
                            string subject = agendaBehavior.ReactionSubject(result);
                            string hint = CourtMoodPresentation.Hint(id, selectedBackendFaction.Type, selectedBackendFaction.ParentKingdom);
                            if (!positive && result.ObjectiveData?.State == CourtObjectiveState.Expired)
                                hint += "\n\n" + new TextObject("{=BC_CourtHistoryExpired}The term ended before the objective was fulfilled.").ToString();
                            (positive ? FactionLikesList : FactionDislikesList).Add(new AgendaItemVM(
                                CourtMoodPresentation.Name(id, selectedBackendFaction.Type, ""),
                                positive ? greenColor : redColor, subject + "\n\n" + hint));
                        }

                    Recent(hasRecentCouncilAppointment, true, "BC_Ag_CouncilMemberAppointed", "A Seat at the Ruler's Side");
                    Recent(hasRecentCouncilRetention, true, "BC_Ag_CouncilIncumbentRetained", "Our Counsel Reaffirmed");
                    Recent(hasRecentCouncilSnub, false, "BC_Ag_CouncilCandidatePassedOver", "Our Nominee Passed Over");
                    Recent(hasRecentCouncilOverride, false, "BC_Ag_CouncilChoiceOverruled", "The Court's Choice Overruled");
                    Recent(hasRecentCouncilDismissal, false, "BC_Ag_CouncilMemberDismissed", "Cast Out of Council");
                }
                else
                {
                    FactionMood = 0;
                    FactionMoodText = "";
                    FactionMoodColor = Colors.White;
                    AgendaListLeft.Clear();
                    AgendaListRight.Clear();
                    FactionLikesList.Clear();
                    FactionDislikesList.Clear();

                    if (selectedBackendFaction.Leader != Clan.PlayerClan)
                    {
                        IsUltimatumEnabled = false;
                        UltimatumHint = new HintViewModel(new TextObject("{=BC_UI_Err_UltimatumLeader}Only the faction leader can issue an ultimatum."));
                    }
                    else if (selectedBackendFaction.Discontent < C.DiscontentTrigger * 0.8f)
                    {
                        IsUltimatumEnabled = false;
                        UltimatumHint = new HintViewModel(new TextObject("{=BC_UI_Err_UltimatumWait}Discontent must be at least 80% to issue an ultimatum."));
                    }
                    else
                    {
                        IsUltimatumEnabled = true;
                        UltimatumHint = new HintViewModel(new TextObject("{=BC_UI_Hint_Ultimatum}Present your demands to the crown. Acceptance averts war; refusal means open rebellion."));
                    }
                }
            }
            else
            {
                IsUltimatumEnabled = false;
                UltimatumHint = new HintViewModel(new TextObject(""));
                FactionPowerHint = CreateEmptyTooltip();
                LoyalistPowerHint = CreateEmptyTooltip();
                ThresholdHint = CreateEmptyTooltip();
                FactionMood = 0;
                FactionMoodText = "";
                FactionMoodColor = Colors.White;
                BalanceOfPowerText = "";
                AgendaListLeft.Clear();
                AgendaListRight.Clear();
                FactionLikesList.Clear();
                FactionDislikesList.Clear();
                OpposingSideText = new TextObject("{=BC_UI_Opposing_Loyalists}Loyalists").ToString();
            }

        }

        private bool PlayerLeadsRebellion(FactionObject playerRebelFaction)
        {
            return playerRebelFaction != null && playerRebelFaction.Leader == Clan.PlayerClan;
        }

        private bool IsCourtFactionInActiveCivilWar(FactionObject faction, FactionManagerBehavior factionManager)
        {
            if (faction == null || !faction.IsIdeology || factionManager == null)
                return false;

            return faction.Members.Any(member =>
                member != null
                && member != Clan.PlayerClan
                && factionManager.IsClanOnActiveCivilWarRebelSide(member, out FactionObject activeFaction, out Kingdom _)
                && activeFaction?.ParentKingdom == faction.ParentKingdom);
        }

        private void ApplyJoinRelations(FactionObject faction)
        {
            if (faction == null || faction.IsIdeology) return;
            bool activeRebellion = faction?.IsCivilWarActive() == true;
            string sourceId = activeRebellion
                ? RelationMemorySources.HonoredCallToArms
                : RelationMemorySources.CourtPolitics;
            float durationYears = activeRebellion ? 10f : 5f;
            RelationMemoryScope scope = activeRebellion ? RelationMemoryScope.House : RelationMemoryScope.Personal;
            string context = GetDynamicFactionName(faction);
            if (faction?.Leader != Clan.PlayerClan && faction?.Leader?.Leader != null)
                RelationMemoryService.ApplyChange(Hero.MainHero, faction.Leader.Leader, 10, true,
                    sourceId, durationYears, scope, context);

            foreach (var memberClan in faction.Members)
            {
                if (memberClan != Clan.PlayerClan && memberClan != faction.Leader && memberClan.Leader != null)
                    RelationMemoryService.ApplyChange(Hero.MainHero, memberClan.Leader, 5, true,
                        sourceId, durationYears, scope, context);
            }
        }

        private void ApplyLeaveRelations(FactionObject faction)
        {
            if (faction == null || faction.IsIdeology) return;
            bool activeRebellion = faction?.IsCivilWarActive() == true;
            string sourceId = activeRebellion
                ? RelationMemorySources.AbandonedMyCause
                : RelationMemorySources.CourtPolitics;
            float durationYears = activeRebellion ? 15f : 5f;
            RelationMemoryScope scope = activeRebellion ? RelationMemoryScope.House : RelationMemoryScope.Personal;
            string context = GetDynamicFactionName(faction);
            if (faction?.Leader != Clan.PlayerClan && faction?.Leader?.Leader != null)
                RelationMemoryService.ApplyChange(Hero.MainHero, faction.Leader.Leader, -20, true,
                    sourceId, durationYears, scope, context);

            foreach (var memberClan in faction.Members)
            {
                if (memberClan != Clan.PlayerClan && memberClan != faction.Leader && memberClan.Leader != null)
                    RelationMemoryService.ApplyChange(Hero.MainHero, memberClan.Leader, -10, true,
                        sourceId, durationYears, scope, context);
            }
        }

        private bool TryDefectToActiveRebellion(FactionObject faction)
        {
            Kingdom rebelKingdom = faction?.GetRebelKingdom();
            if (faction == null || faction.IsIdeology || rebelKingdom == null || !faction.IsCivilWarActive())
                return false;

            if (Clan.PlayerClan?.Kingdom != faction.ParentKingdom)
            {
                BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_UI_Err_DefectWrongSide}You must still be sworn to the crown to defect to this rebellion."), BellumNotificationColors.Danger);
                return true;
            }

            ApplyJoinRelations(faction);
            faction.AddMember(Clan.PlayerClan);
            faction.MoveClanToKingdomPreservingCivilWarInfluence(Clan.PlayerClan, rebelKingdom, preserveCustomBanner: true, showNotification: false);

            TextObject defectMsg = new TextObject("{=BC_UI_Msg_DefectedToRebels}You have defected to the {FACTION_NAME}.");
            defectMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(faction));
            BellumCivileNotifications.ShowPersonal(defectMsg, BellumNotificationColors.Success);

            RefreshAvailableFactionTypes();
            RefreshFactionList();
            return true;
        }

        private bool TryRejoinCrownFromActiveRebellion(FactionObject faction)
        {
            Kingdom rebelKingdom = faction?.GetRebelKingdom();
            if (faction == null || faction.IsIdeology || rebelKingdom == null || !faction.IsCivilWarActive())
                return false;

            if (Clan.PlayerClan?.Kingdom != rebelKingdom)
                return false;

            if (faction.Leader == Clan.PlayerClan)
            {
                BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_UI_Err_LeaveLead}You cannot leave a rebellion you lead. You must disband it."), BellumNotificationColors.Danger);
                return true;
            }

            ApplyLeaveRelations(faction);
            faction.MoveClanToKingdomPreservingCivilWarInfluence(Clan.PlayerClan, faction.ParentKingdom, preserveCustomBanner: true, showNotification: false);
            faction.RemoveMember(Clan.PlayerClan);

            TextObject rejoinMsg = new TextObject("{=BC_UI_Msg_RejoinedCrown}You have abandoned the {FACTION_NAME} and returned to the crown.");
            faction.RecordLoyaltyDeclaration(Clan.PlayerClan);
            rejoinMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(faction));
            BellumCivileNotifications.ShowPersonal(rejoinMsg, BellumNotificationColors.Warning);

            RefreshAvailableFactionTypes();
            RefreshFactionList();
            return true;
        }

        public void ExecuteJoin()
        {
            var feud = GetSelectedClaimFeudRecord();
            if (feud != null)
            {
                var behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
                var block = behavior?.PlayerWithdrawalBlock(feud);
                if (behavior == null || block != null)
                {
                    if (block != null) BellumCivileNotifications.ShowPersonal(block, BellumNotificationColors.Warning);
                    RefreshButtonStates();
                    return;
                }
                var text = new TextObject("{=BC_FeudLeave_Confirm}Withdraw your house from the feud over {TITLE}? The house you pledged to will remember this broken oath (-20 relations), as will the other houses on your side (-10), for {YEARS} years. You cannot rejoin this dispute.\n\n{WAR_TEXT}The feud will continue without you.");
                text.SetTextVariable("TITLE", FeudalTitleBehavior.Instance?.GetTitle(feud.TargetTitleId)?.Name ?? feud.TargetTitleId);
                text.SetTextVariable("YEARS", (10f * BellumCivileOptions.RelationMemoryDurationMultiplier).ToString("0.#"));
                text.SetTextVariable("WAR_TEXT", feud.State == ClaimFeudState.WarActive
                    ? new TextObject("{=BC_FeudLeave_WarText}Your house will return to the surviving parent realm. Its prewar estates will be restored and any feud conquests it holds returned to their former houses. Unrelated possessions remain yours.\n\n")
                    : new TextObject("{=!}"));
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_FeudLeave_Title}Withdraw Your Pledge").ToString(),
                    text.ToString(), true, true, new TextObject("{=BC_UI_Leave}Leave").ToString(),
                    new TextObject("{=BC_FeudLeave_Remain}Remain").ToString(), () =>
                    {
                        bool success = behavior.TryWithdrawPlayerSupport(feud.RecordId, out var reason);
                        BellumCivileNotifications.ShowPersonal(success
                            ? new TextObject("{=BC_FeudLeave_Done}Your house has withdrawn its swords from the feud. Your former allies will remember your broken pledge.")
                            : reason, BellumNotificationColors.Warning);
                        OnClaimFeudActionCompleted();
                        RefreshButtonStates();
                    }, null), true);
                return;
            }
            FactionObject selectedBackendFaction = SelectedFaction?.BackendFactionModel as FactionObject;
            if (selectedBackendFaction == null) return;
            bool isPlayerInSelectedFaction = selectedBackendFaction.Members.Contains(Clan.PlayerClan);
            if (!isPlayerInSelectedFaction && selectedBackendFaction.IsIdeology
                && !CourtMembershipEligibility.CanBelong(Clan.PlayerClan, selectedBackendFaction.ParentKingdom))
            {
                BellumCivileNotifications.ShowPersonal(CourtMembershipEligibility.IsRuler(Clan.PlayerClan)
                    ? CourtMembershipEligibility.RulerReason : CourtMembershipEligibility.IneligibleReason, BellumNotificationColors.Warning);
                RefreshButtonStates();
                return;
            }

            if (isPlayerInSelectedFaction)
            {
                if (TryRejoinCrownFromActiveRebellion(selectedBackendFaction))
                    return;

                ApplyLeaveRelations(selectedBackendFaction);
                if (selectedBackendFaction.IsIdeology)
                    Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.ForgetPlayerCourtAffiliation();
                selectedBackendFaction.RemoveMember(Clan.PlayerClan);
                TextObject leftMsg = new TextObject("{=BC_UI_Msg_Left}You have left {FACTION_NAME}.");
                leftMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(selectedBackendFaction));
                BellumCivileNotifications.ShowPersonal(leftMsg, BellumNotificationColors.Warning);

                if (selectedBackendFaction.Members.Count == 0)
                {
                    var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                    factionManager?.RemoveFaction(selectedBackendFaction);
                    Factions.Remove(SelectedFaction);

                    if (selectedBackendFaction.IsIdeology)
                    {
                        AvailableFactionTypes.Add(PoliticalActionVM.ForFaction(
                            selectedBackendFaction.Type,
                            GetPrettyCreationName(selectedBackendFaction.Type),
                            OnFactionTypeSelected,
                            true,
                            BuildFactionTypeHint(selectedBackendFaction.Type),
                            100));
                    }

                    if (Factions.Count > 0) OnFactionSelected(Factions[0]);
                    else OnFactionSelected(null);
                    return;
                }
            }
            else
            {
                if (TryDefectToActiveRebellion(selectedBackendFaction))
                    return;

                ApplyJoinRelations(selectedBackendFaction);
                selectedBackendFaction.AddMember(Clan.PlayerClan);
                TextObject joinMsg = new TextObject("{=BC_UI_Msg_Joined}You have joined the {FACTION_NAME}.");
                joinMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(selectedBackendFaction));
                BellumCivileNotifications.ShowPersonal(joinMsg, BellumNotificationColors.Success);
            }

            SelectedFaction?.Refresh();
            RefreshButtonStates();
        }

        public void ExecuteCreate()
        {
            if (_isClaimFeudAbandonMode || _isClaimFeudSurrenderMode)
            {
                ClaimFeudRecord record = GetSelectedClaimFeudRecord();
                ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
                string report = null;
                bool success = _isClaimFeudSurrenderMode
                    ? feudBehavior != null && feudBehavior.TrySurrenderPlayerFeud(record?.RecordId, Clan.PlayerClan, out report)
                    : feudBehavior != null && feudBehavior.TryAbandonPlayerFeud(record?.RecordId, Clan.PlayerClan, out report);

                if (!success)
                {
                    TextObject failed = new TextObject("{=BC_UI_Err_FeudActionFailed}The feud action failed: {REASON}");
                    failed.SetTextVariable("REASON", report ?? string.Empty);
                    BellumCivileNotifications.ShowPersonal(failed, BellumNotificationColors.Danger);
                    RefreshButtonStates();
                    return;
                }

                TextObject message = _isClaimFeudSurrenderMode
                    ? new TextObject("{=BC_UI_Msg_FeudSurrendered}You have surrendered the feud. The dispute is settled in favor of the current holder.")
                    : new TextObject("{=BC_UI_Msg_FeudAbandoned}You have cooled the feud for now.");
                BellumCivileNotifications.ShowPersonal(message, _isClaimFeudSurrenderMode ? BellumNotificationColors.Danger : BellumNotificationColors.Warning);
                OnClaimFeudActionCompleted();
                RefreshAvailableFactionTypes();
                RefreshFactionList();
                RefreshButtonStates();
                return;
            }

            if (_isLoyalistSurrenderMode)
            {
                FactionObject selectedRebellion = SelectedFaction?.BackendFactionModel as FactionObject;
                Kingdom parentKingdom = selectedRebellion?.ParentKingdom;
                Kingdom rebelKingdom = selectedRebellion?.GetRebelKingdom();
                CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
                if (selectedRebellion == null
                    || selectedRebellion.IsIdeology
                    || !selectedRebellion.IsCivilWarActive()
                    || parentKingdom?.RulingClan != Clan.PlayerClan
                    || Clan.PlayerClan?.Kingdom != parentKingdom
                    || rebelKingdom == null
                    || resolutionBehavior == null)
                {
                    BellumCivileNotifications.ShowPersonal(
                        new TextObject("{=BC_UI_Err_LoyalistSurrenderUnavailable}The crown cannot surrender because the active rebel realm could not be found."),
                        BellumNotificationColors.Danger);
                    RefreshButtonStates();
                    return;
                }

                TextObject surrenderMsg = new TextObject("{=BC_UI_Msg_LoyalistSurrendered}The crown has surrendered to the {FACTION_NAME}. Its demands will now be enforced.");
                surrenderMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(selectedRebellion));
                BellumCivileNotifications.ShowPersonal(surrenderMsg, BellumNotificationColors.Danger);
                resolutionBehavior.ResolveRebelVictory(selectedRebellion, rebelKingdom);
                RefreshFactionList(false);
                return;
            }

            if (_isDisbandMode)
            {
                var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                FactionObject playerFaction = factionManager?.GetRebelFaction(Clan.PlayerClan);

                if (playerFaction != null && playerFaction.Leader == Clan.PlayerClan)
                {
                    if (_isSurrenderMode || playerFaction.IsCivilWarActive())
                    {
                        Kingdom rebelKingdom = playerFaction.GetRebelKingdom();
                        CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
                        if (rebelKingdom != null && resolutionBehavior != null)
                        {
                            TextObject surrenderMsg = new TextObject("{=BC_UI_Msg_Surrendered}{FACTION_NAME} has surrendered to the crown.");
                            surrenderMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(playerFaction));
                            BellumCivileNotifications.ShowPersonal(surrenderMsg, BellumNotificationColors.Danger);

                            resolutionBehavior.ResolveLiegeVictory(playerFaction, rebelKingdom);
                            RefreshFactionList(false);
                            return;
                        }

                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_UI_Err_SurrenderUnavailable}The rebellion cannot surrender because its active rebel kingdom could not be found."), BellumNotificationColors.Danger);
                        RefreshButtonStates();
                        return;
                    }

                    foreach (var memberClan in playerFaction.Members)
                    {
                        if (memberClan != Clan.PlayerClan && memberClan.Leader != null)
                        {
                            RelationMemoryService.ApplyChange(Hero.MainHero, memberClan.Leader, -10, true,
                                RelationMemorySources.AbandonedMyCause, 10f, RelationMemoryScope.House,
                                GetDynamicFactionName(playerFaction));
                        }
                    }

                    factionManager.RemoveFaction(playerFaction);
                    Factions.Remove(SelectedFaction);

                    TextObject disbandMsg = new TextObject("{=BC_UI_Msg_Disbanded}You have disbanded the {FACTION_NAME}.");
                    disbandMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(playerFaction));
                    BellumCivileNotifications.ShowPersonal(disbandMsg, BellumNotificationColors.Danger);

                    if (Factions.Count > 0) OnFactionSelected(Factions[0]);
                    else OnFactionSelected(null);
                }
            }
            else
            {
                IsCreatingFaction = true;
                PoliticalActionVM firstValid = AvailableFactionTypes.FirstOrDefault(CanAffordAction)
                    ?? AvailableFactionTypes.FirstOrDefault(f => f.IsEnabled);
                if (firstValid != null) OnFactionTypeSelected(firstValid);
            }
        }

        public void ExecuteConfirmCreate()
        {
            if (SelectedFactionType != null)
            {
                if (!SelectedFactionType.IsEnabled)
                {
                    BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_UI_Err_ActionUnavailable}This political action is not available right now."), BellumNotificationColors.Danger);
                    return;
                }

                if (!CanAffordAction(SelectedFactionType))
                {
                    TextObject influenceError = new TextObject("{=BC_UI_Err_NeedActionInfluence}You need {COST} influence to perform this action.");
                    influenceError.SetTextVariable("COST", SelectedFactionType.InfluenceCost.ToString());
                    BellumCivileNotifications.ShowPersonal(influenceError, BellumNotificationColors.Danger);
                    return;
                }

                if (SelectedFactionType.ActionKind == PoliticalActionKind.StartClaimFeud)
                {
                    ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
                    ClaimFeudRecord record = null;
                    string report = null;
                    bool startedFeud = feudBehavior != null
                        && feudBehavior.TryStartPlayerFeud(Clan.PlayerClan, SelectedFactionType.TargetTitleId, out record, out report);
                    if (!startedFeud)
                    {
                        TextObject failed = new TextObject("{=BC_UI_Err_StartFeudFailed}The feud could not be started: {REASON}");
                        failed.SetTextVariable("REASON", report ?? new TextObject("{=BC_RoyalPeace_ErrUnavailable}The realm's peace cannot be enforced right now.").ToString());
                        BellumCivileNotifications.ShowPersonal(failed, BellumNotificationColors.Danger);
                        RefreshAvailableFactionTypes();
                        RefreshButtonStates();
                        return;
                    }

                    if (SelectedFactionType.InfluenceCost > 0)
                        ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -SelectedFactionType.InfluenceCost);

                    TextObject started = new TextObject("{=BC_UI_Msg_StartedClaimFeud}You have begun pressing your claim. The feud is now active.");
                    BellumCivileNotifications.ShowPersonal(started, BellumNotificationColors.Warning);
                    RefreshAvailableFactionTypes();
                    RefreshFactionList();
                    IsCreatingFaction = false;
                    return;
                }

                var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                bool isIdeology = IsIdeologyType(SelectedFactionType.FactionType);

                if (isIdeology)
                {
                    if (!CourtMembershipEligibility.CanBelong(Clan.PlayerClan, Clan.PlayerClan.Kingdom)) return;
                    FactionObject currentIdeology = factionManager.GetIdeologicalFaction(Clan.PlayerClan);
                    if (currentIdeology != null)
                    {
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_UI_Err_AlreadyCourtFaction}You already belong to a court faction. Leave it before founding another party."), BellumNotificationColors.Danger);
                        RefreshAvailableFactionTypes();
                        RefreshButtonStates();
                        return;
                    }
                }

                if (SelectedFactionType.InfluenceCost > 0)
                    ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -SelectedFactionType.InfluenceCost);

                string backendName = $"{Clan.PlayerClan.Name} Revolt";
                switch (SelectedFactionType.FactionType)
                {
                    case FactionType.Independence: backendName = $"{Clan.PlayerClan.Name} Secessionists"; break;
                    case FactionType.Abdication: backendName = $"{Clan.PlayerClan.Name} Coalition"; break;
                    case FactionType.InstallRuler: backendName = $"{Clan.PlayerClan.Name} Claimants"; break;
                    case FactionType.Royalists: backendName = "Traditionalists"; break;
                    case FactionType.Glory: backendName = "Militarists"; break;
                    case FactionType.Nobility: backendName = "Aristocrats"; break;
                    case FactionType.Liberty: backendName = "Populists"; break;
                }
                
                FactionObject newFaction = new FactionObject(backendName, Clan.PlayerClan.Kingdom, Clan.PlayerClan, SelectedFactionType.FactionType);

                factionManager.RegisterNewFaction(newFaction);

                RefreshAvailableFactionTypes();

                FactionItemVM newItem = new FactionItemVM(GetDynamicFactionName(newFaction), newFaction, OnFactionSelected);
                Factions.Add(newItem);
                OnFactionSelected(newItem);

                TextObject formedMsg = new TextObject("{=BC_UI_Msg_Formed}You formed the {FACTION_NAME}!");
                formedMsg.SetTextVariable("FACTION_NAME", GetDynamicFactionName(newFaction));
                BellumCivileNotifications.ShowPersonal(formedMsg, BellumNotificationColors.Success);
            }
            IsCreatingFaction = false;
        }

        private static bool BreakdownContainsSignedLabel(string breakdownText, string labelId, string fallbackLabel, bool positive)
        {
            if (string.IsNullOrEmpty(breakdownText)) return false;
            string label = new TextObject("{=" + labelId + "}" + fallbackLabel).ToString();
            return breakdownText.Contains(label + (positive ? ": +" : ": -"));
        }

        private static BasicTooltipViewModel CreateEmptyTooltip()
        {
            return new BasicTooltipViewModel(() => string.Empty);
        }

        private static BasicTooltipViewModel CreateTextTooltip(TextObject title, TextObject message)
        {
            return new BasicTooltipViewModel(() =>
            {
                List<TooltipProperty> properties = CreateTooltipWithTitle(title);
                AddTooltipMessage(properties, message);
                return properties;
            });
        }

        private static BasicTooltipViewModel BuildProjectedFactionPowerTooltip(
            RebellionPowerProjection projection,
            FactionManagerBehavior factionManager)
        {
            List<Clan> committedClans = projection?.CommittedRebels
                ?.Where(clan => clan != null && !clan.IsEliminated)
                .OrderByDescending(clan => RebellionPowerHelper.CalculateClanPower(clan) + projection.GetProjectedSupportPower(clan))
                .ToList() ?? new List<Clan>();
            List<ProjectedRebellionSupport> supporters = projection?.ProjectedSupporters?.ToList()
                ?? new List<ProjectedRebellionSupport>();

            return new BasicTooltipViewModel(() =>
            {
                List<TooltipProperty> properties = CreateTooltipWithTitle(
                    new TextObject("{=BC_UI_FactionPowerTooltipTitle}Faction Power"));
                if (committedClans.Count == 0)
                {
                    AddTooltipMessage(properties, new TextObject("{=BC_UI_NoPowerContributors}No contributors."));
                    return properties;
                }

                for (int index = 0; index < committedClans.Count; index++)
                {
                    Clan clan = committedClans[index];
                    List<ProjectedRebellionSupport> clanSupporters = supporters
                        .Where(support => support.RootSponsor == clan)
                        .OrderByDescending(support => support.CreditedPower)
                        .ToList();
                    float ownPower = RebellionPowerHelper.CalculateClanPower(clan);
                    float projectedSupportPower = clanSupporters.Sum(support => support.CreditedPower);

                    AddTooltipRow(properties, clan.Name.ToString(), (ownPower + projectedSupportPower).ToString("N0"));
                    AddTooltipSeparator(properties);
                    AddTooltipRow(properties, new TextObject("{=BC_UI_OwnPower}  Own power"), ownPower.ToString("N0"));
                    AddTooltipRow(properties, new TextObject("{=BC_UI_MilitaryStrength}    Military strength"), RebellionPowerHelper.GetClanMilitaryStrength(clan).ToString("N0"));
                    AddTooltipRow(properties, new TextObject("{=BC_UI_InfluencePower}    Influence"), clan.Influence.ToString("N0"));

                    if (clanSupporters.Count > 0)
                    {
                        AddTooltipRow(
                            properties,
                            new TextObject("{=BC_UI_ExpectedSupporters}  Expected supporters"),
                            projectedSupportPower.ToString("N0"));

                        foreach (ProjectedRebellionSupport support in clanSupporters)
                        {
                            {
                                string value = string.Format(
                                    "+{0:N0} / {1:N0} ({2:0}%, {3})",
                                    support.CreditedPower,
                                    support.FullPower,
                                    support.JoinChance * 100f,
                                    GetSolidarityReasonText(support.Reason));
                                AddTooltipRow(properties, "    " + support.Clan.Name.ToString(), value);
                            }
                        }
                    }

                    if (index < committedClans.Count - 1)
                        AddTooltipSeparator(properties);
                }

                AddTooltipSeparator(properties);
                AddTooltipRow(
                    properties,
                    new TextObject("{=BC_UI_ProjectedTotalPower}Projected total"),
                    (projection?.FactionPower ?? 0f).ToString("N0"));

                return properties;
            });
        }

        private static BasicTooltipViewModel BuildProjectedLoyalistPowerTooltip(
            RebellionPowerProjection projection,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            bool isIdeologyFaction)
        {
            List<ProjectedLoyalistContribution> contributors = projection?.LoyalistContributions
                ?.Where(entry => entry.Clan != null && entry.CreditedPower > 0f)
                .OrderByDescending(entry => entry.CreditedPower)
                .ToList() ?? new List<ProjectedLoyalistContribution>();

            return new BasicTooltipViewModel(() =>
            {
                List<TooltipProperty> properties = CreateTooltipWithTitle(
                    new TextObject("{=BC_UI_LoyalistPowerTooltipTitle}Loyalist Power"));
                if (contributors.Count == 0)
                {
                    AddTooltipMessage(properties, new TextObject("{=BC_UI_NoPowerContributors}No contributors."));
                    return properties;
                }

                foreach (ProjectedLoyalistContribution entry in contributors)
                {
                    Clan clan = entry.Clan;
                    FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
                    string modifierText;
                    if (clan == kingdom?.RulingClan)
                    {
                        modifierText = isIdeologyFaction
                            ? new TextObject("{=BC_Mod_Ruler}(Ruler)").ToString()
                            : new TextObject("{=BC_Mod_WarRuler}(100% Ruler)").ToString();
                    }
                    else if (clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan))
                    {
                        modifierText = new TextObject("{=BC_Mod_WarMerc}(100% Mercenary)").ToString();
                    }
                    else if (ideology != null)
                    {
                        modifierText = GetLoyalistContributionText(kingdom, clan, ideology);
                    }
                    else
                    {
                        modifierText = new TextObject("{=BC_Mod_WarUnaligned}(100% Unaligned)").ToString();
                    }

                    string value = string.Format("{0:N0} / {1:N0} {2}", entry.CreditedPower, entry.FullPower, modifierText);
                    AddTooltipRow(properties, clan.Name.ToString(), value);
                    if (entry.RebelJoinChance > 0f)
                    {
                        AddTooltipRow(
                            properties,
                            new TextObject("{=BC_UI_ProjectedRebelPull}  Projected rebel pull"),
                            "-" + (entry.RebelJoinChance * 100f).ToString("0") + "%");
                    }
                }

                AddTooltipSeparator(properties);
                AddTooltipRow(
                    properties,
                    new TextObject("{=BC_UI_ProjectedTotalPower}Projected total"),
                    (projection?.LoyalistPower ?? 0f).ToString("N0"));
                return properties;
            });
        }

        private static string GetSolidarityReasonText(CivilWarSolidarityReason reason)
        {
            switch (reason)
            {
                case CivilWarSolidarityReason.CrownLoyalty:
                    return new TextObject("{=BC_UI_SolidarityReasonCrown}crown loyalty").ToString();
                case CivilWarSolidarityReason.DirectVassal:
                    return new TextObject("{=BC_UI_SolidarityReasonVassal}vassal").ToString();
                case CivilWarSolidarityReason.MarriageAlliance:
                    return new TextObject("{=BC_UI_SolidarityReasonMarriage}marriage alliance").ToString();
                case CivilWarSolidarityReason.DynasticKin:
                    return new TextObject("{=BC_UI_SolidarityReasonKin}blood kinship").ToString();
                case CivilWarSolidarityReason.Friendship:
                    return new TextObject("{=BC_UI_SolidarityReasonFriendship}friendship").ToString();
                default:
                    return new TextObject("{=BC_UI_SolidarityReasonIntent}rebellious intent").ToString();
            }
        }

        private static string GetRealmOppositionContributionText(float creditedPower, float fullPower)
        {
            int percent = fullPower > 0f
                ? (int)TaleWorlds.Library.MathF.Round(
                    TaleWorlds.Library.MathF.Clamp(creditedPower / fullPower, 0f, 1f) * 100f)
                : 0;
            TextObject text = new TextObject("{=BC_Mod_RealmOpposition}({PERCENT}% opposition)");
            text.SetTextVariable("PERCENT", percent);
            return text.ToString();
        }

        private static TextObject GetCourtMoodState(FactionObject ideology)
        {
            if (ideology == null)
            {
                return new TextObject("{=BC_MoodState_Unaligned}Unaligned");
            }

            if (ideology.Mood >= C.ArmyMoodContent + 1f)
            {
                return new TextObject("{=BC_MoodState_Loyal}Loyal");
            }

            if (ideology.Mood >= C.MoodThresholdHappy + 1f)
            {
                return new TextObject("{=BC_MoodState_Happy}Happy");
            }

            if (ideology.Mood >= C.MoodThresholdUnhappy)
            {
                return new TextObject("{=BC_MoodState_Neutral}Neutral");
            }

            if (ideology.Mood >= C.GrandCoalitionJoinMoodThreshold)
            {
                return new TextObject("{=BC_MoodState_Unhappy}Unhappy");
            }

            return new TextObject("{=BC_MoodState_Rebellious}Rebellious");
        }

        private static List<TooltipProperty> CreateTooltipWithTitle(TextObject title)
        {
            List<TooltipProperty> properties = new List<TooltipProperty>
            {
                new TooltipProperty(title.ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title)
            };
            AddTooltipSeparator(properties);
            return properties;
        }

        private static IEnumerable<string> SplitTooltipLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            foreach (string line in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }

        private static void AddTooltipBreakdownLine(List<TooltipProperty> properties, string line)
        {
            // Translations may use the full-width colon; MultiLine rows display only the value column.
            int split = line.LastIndexOfAny(new[] { ':', '\uFF1A' });
            if (split > 0 && split < line.Length - 1)
            {
                AddTooltipRow(properties, line.Substring(0, split).Trim(), line.Substring(split + 1).Trim());
                return;
            }

            properties.Add(new TooltipProperty(string.Empty, line, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
        }

        private static void AddTooltipRow(List<TooltipProperty> properties, TextObject label, string value)
        {
            AddTooltipRow(properties, label.ToString(), value);
        }

        private static void AddTooltipRow(List<TooltipProperty> properties, string label, string value)
        {
            properties.Add(new TooltipProperty(label, value, 0));
        }

        private static void AddTooltipMessage(List<TooltipProperty> properties, TextObject message)
        {
            properties.Add(new TooltipProperty(new TextObject("{=BC_UI_TooltipInfoLabel}Info").ToString(), message.ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
        }

        private static void AddTooltipSeparator(List<TooltipProperty> properties)
        {
            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
        }

        public void ExecuteCancelCreate()
        {
            IsCreatingFaction = false;
        }

        public void ExecuteUltimatum()
        {
            FactionObject selectedBackendFaction = SelectedFaction?.BackendFactionModel as FactionObject;

            if (selectedBackendFaction != null
                && !selectedBackendFaction.IsIdeology
                && selectedBackendFaction.Leader == Clan.PlayerClan
                && selectedBackendFaction.ParentKingdom != null
                && Clan.PlayerClan?.Kingdom == selectedBackendFaction.ParentKingdom
                && !selectedBackendFaction.IsCivilWarActive()
                && !selectedBackendFaction.HasTrackedRebelKingdom
                && selectedBackendFaction.Discontent >= C.DiscontentTrigger * 0.8f)
            {
                selectedBackendFaction.TriggerUltimatum();
                RefreshFactionList(preserveSelection: false);
            }
        }

        [DataSourceProperty] public MBBindingList<FactionItemVM> Factions { get => _factions; set { if (value != _factions) { _factions = value; OnPropertyChangedWithValue(value, "Factions"); } } }
        [DataSourceProperty] public FactionItemVM SelectedFaction { get => _selectedFaction; set { if (value != _selectedFaction) { _selectedFaction = value; OnPropertyChangedWithValue(value, "SelectedFaction"); } } }
        [DataSourceProperty] public bool HasSelectedFaction { get => _hasSelectedFaction; set { if (value != _hasSelectedFaction) { _hasSelectedFaction = value; OnPropertyChangedWithValue(value, "HasSelectedFaction"); } } }
        [DataSourceProperty] public string HeaderText { get => _headerText; set { if (value != _headerText) { _headerText = value; OnPropertyChangedWithValue(value, "HeaderText"); } } }
        [DataSourceProperty] public MBBindingList<PoliticalActionVM> AvailableFactionTypes { get => _availableFactionTypes; set { if (value != _availableFactionTypes) { _availableFactionTypes = value; OnPropertyChangedWithValue(value, "AvailableFactionTypes"); } } }
        [DataSourceProperty] public PoliticalActionVM SelectedFactionType { get => _selectedFactionType; set { if (value != _selectedFactionType) { _selectedFactionType = value; OnPropertyChangedWithValue(value, "SelectedFactionType"); } } }
        [DataSourceProperty] public bool IsSelectedActionCostVisible { get => _isSelectedActionCostVisible; set { if (value != _isSelectedActionCostVisible) { _isSelectedActionCostVisible = value; OnPropertyChangedWithValue(value, "IsSelectedActionCostVisible"); } } }
        [DataSourceProperty] public string SelectedActionCostText { get => _selectedActionCostText; set { if (value != _selectedActionCostText) { _selectedActionCostText = value; OnPropertyChangedWithValue(value, "SelectedActionCostText"); } } }
        [DataSourceProperty] public string JoinLeaveText { get => _joinLeaveText; set { if (value != _joinLeaveText) { _joinLeaveText = value; OnPropertyChangedWithValue(value, "JoinLeaveText"); } } }
        [DataSourceProperty] public bool IsJoinLeaveEnabled { get => _isJoinLeaveEnabled; set { if (value != _isJoinLeaveEnabled) { _isJoinLeaveEnabled = value; OnPropertyChangedWithValue(value, "IsJoinLeaveEnabled"); } } }
        [DataSourceProperty] public string CreateDestroyText { get => _createDestroyText; set { if (value != _createDestroyText) { _createDestroyText = value; OnPropertyChangedWithValue(value, "CreateDestroyText"); } } }
        [DataSourceProperty] public bool IsCreateDestroyEnabled { get => _isCreateDestroyEnabled; set { if (value != _isCreateDestroyEnabled) { _isCreateDestroyEnabled = value; OnPropertyChangedWithValue(value, "IsCreateDestroyEnabled"); } } }
        [DataSourceProperty] public bool IsCreatingFaction { get => _isCreatingFaction; set { if (value != _isCreatingFaction) { _isCreatingFaction = value; OnPropertyChangedWithValue(value, "IsCreatingFaction"); } } }
        [DataSourceProperty] public bool IsFoundCostVisible { get => _isFoundCostVisible; set { if (value != _isFoundCostVisible) { _isFoundCostVisible = value; OnPropertyChangedWithValue(value, "IsFoundCostVisible"); } } }
        [DataSourceProperty] public bool IsUltimatumEnabled { get => _isUltimatumEnabled; set { if (value != _isUltimatumEnabled) { _isUltimatumEnabled = value; OnPropertyChangedWithValue(value, "IsUltimatumEnabled"); } } }

        [DataSourceProperty] public bool IsRebellionSelected { get => _isRebellionSelected; set { if (value != _isRebellionSelected) { _isRebellionSelected = value; OnPropertyChangedWithValue(value, "IsRebellionSelected"); } } }
        [DataSourceProperty] public bool IsIdeologySelected { get => _isIdeologySelected; set { if (value != _isIdeologySelected) { _isIdeologySelected = value; OnPropertyChangedWithValue(value, "IsIdeologySelected"); } } }
        [DataSourceProperty] public int FactionMood { get => _factionMood; set { if (value != _factionMood) { _factionMood = value; OnPropertyChangedWithValue(value, "FactionMood"); } } }

        [DataSourceProperty] public BasicTooltipViewModel MoodHint { get => _moodHint; set { if (value != _moodHint) { _moodHint = value; OnPropertyChangedWithValue(value, "MoodHint"); } } }
        [DataSourceProperty] public HintViewModel JoinLeaveHint { get => _joinLeaveHint; set { if (value != _joinLeaveHint) { _joinLeaveHint = value; OnPropertyChangedWithValue(value, "JoinLeaveHint"); } } }
        [DataSourceProperty] public HintViewModel CreateDestroyHint { get => _createDestroyHint; set { if (value != _createDestroyHint) { _createDestroyHint = value; OnPropertyChangedWithValue(value, "CreateDestroyHint"); } } }
        [DataSourceProperty] public HintViewModel UltimatumHint { get => _ultimatumHint; set { if (value != _ultimatumHint) { _ultimatumHint = value; OnPropertyChangedWithValue(value, "UltimatumHint"); } } }
        [DataSourceProperty] public HintViewModel DiscontentHint { get => _discontentHint; set { if (value != _discontentHint) { _discontentHint = value; OnPropertyChangedWithValue(value, "DiscontentHint"); } } }
        [DataSourceProperty] public BasicTooltipViewModel BalanceOfPowerHint { get => _balanceOfPowerHint; set { if (value != _balanceOfPowerHint) { _balanceOfPowerHint = value; OnPropertyChangedWithValue(value, "BalanceOfPowerHint"); } } }
        [DataSourceProperty] public BasicTooltipViewModel FactionPowerHint { get => _factionPowerHint; set { if (value != _factionPowerHint) { _factionPowerHint = value; OnPropertyChangedWithValue(value, "FactionPowerHint"); } } }
        [DataSourceProperty] public BasicTooltipViewModel LoyalistPowerHint { get => _loyalistPowerHint; set { if (value != _loyalistPowerHint) { _loyalistPowerHint = value; OnPropertyChangedWithValue(value, "LoyalistPowerHint"); } } }
        [DataSourceProperty] public BasicTooltipViewModel ThresholdHint { get => _thresholdHint; set { if (value != _thresholdHint) { _thresholdHint = value; OnPropertyChangedWithValue(value, "ThresholdHint"); } } }

        [DataSourceProperty] public string OpposingSideText { get => _opposingSideText; set { if (value != _opposingSideText) { _opposingSideText = value; OnPropertyChangedWithValue(value, "OpposingSideText"); } } }
        [DataSourceProperty] public string FactionMoodText { get => _factionMoodText; set { if (value != _factionMoodText) { _factionMoodText = value; OnPropertyChangedWithValue(value, "FactionMoodText"); } } }
        [DataSourceProperty] public Color FactionMoodColor { get => _factionMoodColor; set { if (value != _factionMoodColor) { _factionMoodColor = value; OnPropertyChangedWithValue(value, "FactionMoodColor"); } } }
        [DataSourceProperty] public string BalanceOfPowerText { get => _balanceOfPowerText; set { if (value != _balanceOfPowerText) { _balanceOfPowerText = value; OnPropertyChangedWithValue(value, "BalanceOfPowerText"); } } }
        [DataSourceProperty] public Color BalanceOfPowerColor { get => _balanceOfPowerColor; set { if (value != _balanceOfPowerColor) { _balanceOfPowerColor = value; OnPropertyChangedWithValue(value, "BalanceOfPowerColor"); } } }
        [DataSourceProperty] public MBBindingList<AgendaItemVM> FactionLikesList { get => _factionLikesList; set { if (value != _factionLikesList) { _factionLikesList = value; OnPropertyChangedWithValue(value, "FactionLikesList"); } } }
        [DataSourceProperty] public MBBindingList<AgendaItemVM> FactionDislikesList { get => _factionDislikesList; set { if (value != _factionDislikesList) { _factionDislikesList = value; OnPropertyChangedWithValue(value, "FactionDislikesList"); } } }

        [DataSourceProperty] public MBBindingList<AgendaItemVM> AgendaListLeft { get => _agendaListLeft; set { if (value != _agendaListLeft) { _agendaListLeft = value; OnPropertyChangedWithValue(value, "AgendaListLeft"); } } }
        [DataSourceProperty] public MBBindingList<AgendaItemVM> AgendaListRight { get => _agendaListRight; set { if (value != _agendaListRight) { _agendaListRight = value; OnPropertyChangedWithValue(value, "AgendaListRight"); } } }

        [DataSourceProperty] public string FactionMoodLabel => new TextObject("{=BC_UI_FactionMood}Faction Mood: ").ToString();
        [DataSourceProperty] public string LikesText => new TextObject("{=BC_UI_Likes}Likes").ToString();
        [DataSourceProperty] public string DislikesText => new TextObject("{=BC_UI_Dislikes}Dislikes").ToString();
        [DataSourceProperty] public string PolicySupportText => new TextObject("{=BC_EnactedPolicySupport}Support").ToString();
        [DataSourceProperty] public string PolicyOpposeText => new TextObject("{=BC_EnactedPolicyOppose}Oppose").ToString();
        [DataSourceProperty] public string UltimatumText => new TextObject("{=BC_UI_Ultimatum}Ultimatum").ToString();
        [DataSourceProperty] public string ChooseFactionTypeText => new TextObject("{=BC_UI_ChooseFactionType}Choose Faction Type").ToString();
        [DataSourceProperty] public string ConfirmText => new TextObject("{=BC_UI_Confirm}Confirm").ToString();
        [DataSourceProperty] public string CancelText => new TextObject("{=BC_UI_Cancel}Cancel").ToString();

        private static string GetLoyalistContributionText(Kingdom kingdom, Clan clan, FactionObject ideology)
        {
            if (kingdom == null || clan == null)
                return "";

            if (clan == kingdom.RulingClan)
                return new TextObject("{=BC_Mod_WarRuler}(100% Ruler)").ToString();

            if (clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan))
                return new TextObject("{=BC_Mod_WarMerc}(100% Mercenary)").ToString();

            if (ideology == null)
                return new TextObject("{=BC_Mod_WarUnaligned}(100% Unaligned)").ToString();

            float mood = ideology.Mood;
            if (mood >= C.ArmyMoodContent + 1f)
                return new TextObject("{=BC_Mod_WarLoyal}(100% Loyal)").ToString();

            if (mood >= C.MoodThresholdHappy + 1f)
                return new TextObject("{=BC_Mod_WarHappy}(75% Happy)").ToString();

            if (mood >= C.MoodThresholdUnhappy)
                return new TextObject("{=BC_Mod_WarNeutral}(50% Neutral)").ToString();

            if (mood >= C.GrandCoalitionJoinMoodThreshold)
                return new TextObject("{=BC_Mod_WarUnhappy}(25% Unhappy)").ToString();

            return new TextObject("{=BC_Mod_WarRebellious}(0% Rebellious)").ToString();
        }
    }

    public class AgendaItemVM : ViewModel
    {
        private string _policyName;
        private Color _textColor;
        private HintViewModel _hint;

        public AgendaItemVM(string name, bool isActive, string hintText = "")
        {
            PolicyName = "\u2022 " + name;
            TextColor = isActive ? Color.ConvertStringToColor("#82E06AFF") : Color.ConvertStringToColor("#F1D8A4FF");
            _hint = new HintViewModel(new TextObject(hintText));
        }

        public AgendaItemVM(string name, Color color, string hintText = "")
        {
            PolicyName = "\u2022 " + name;
            TextColor = color;
            _hint = new HintViewModel(new TextObject(hintText));
        }

        [DataSourceProperty]
        public string PolicyName
        {
            get => _policyName;
            set { if (value != _policyName) { _policyName = value; OnPropertyChangedWithValue(value, "PolicyName"); } }
        }

        [DataSourceProperty]
        public Color TextColor
        {
            get => _textColor;
            set { if (value != _textColor) { _textColor = value; OnPropertyChangedWithValue(value, "TextColor"); } }
        }

        [DataSourceProperty]
        public HintViewModel Hint
        {
            get => _hint;
            set { if (value != _hint) { _hint = value; OnPropertyChangedWithValue(value, "Hint"); } }
        }
    }
}
