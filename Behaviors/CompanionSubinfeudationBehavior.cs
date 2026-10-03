using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed class CompanionSubinfeudationBehavior : CampaignBehaviorBase
    {
        private CompanionSubinfeudationPreview _selectedGrant;
        private string _selectedCompanionId;
        private bool _grantCompleted;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine(
                "bc_subinfeudation_start",
                "hero_main_options",
                "bc_subinfeudation_response",
                "{=BC_Subinfeudation_Start}I wish to establish you as a landed vassal within my domains.",
                CanStartConversation,
                ResetFlow,
                120);

            starter.AddDialogLine(
                "bc_subinfeudation_caravan",
                "bc_subinfeudation_response",
                "bc_subinfeudation_caravan_end",
                "{=BC_Subinfeudation_CaravanResponse}I would be honored, my {?PLAYER.GENDER}lady{?}lord{\\?}, but I cannot assume a landed estate while leading this caravan.",
                IsConversationCompanionLeadingCaravan,
                null,
                130);

            starter.AddPlayerLine(
                "bc_subinfeudation_caravan_end",
                "bc_subinfeudation_caravan_end",
                "lord_pretalk",
                "{=BC_Subinfeudation_CaravanEnd}Then we will speak again once you are relieved of that duty.",
                null,
                ClearFlow,
                120);

            starter.AddDialogLine(
                "bc_subinfeudation_response_ready",
                "bc_subinfeudation_response",
                "bc_subinfeudation_offer",
                "{=BC_Subinfeudation_Response}You would grant me lands to hold beneath your protection? I would be honored to found a house within your domains and serve its rightful liege.",
                null,
                null,
                100);

            starter.AddPlayerLine(
                "bc_subinfeudation_offer",
                "bc_subinfeudation_offer",
                "bc_subinfeudation_check_fiefs",
                "{=BC_Subinfeudation_Offer}You have served my house faithfully. I would grant you an estate of your own.",
                null,
                SetupGrantList,
                120);

            starter.AddDialogLine(
                "bc_subinfeudation_no_fiefs",
                "bc_subinfeudation_check_fiefs",
                "bc_subinfeudation_no_fiefs_end",
                "{=BC_Subinfeudation_NoFiefs}I am grateful, but you personally hold no suitable barony beneath one of your superior titles.",
                HasNoEligibleGrants,
                null,
                120);

            starter.AddPlayerLine(
                "bc_subinfeudation_no_fiefs_end",
                "bc_subinfeudation_no_fiefs_end",
                "lord_pretalk",
                "{=BC_Subinfeudation_NoFiefsEnd}Then we will revisit the matter another time.",
                null,
                ClearFlow,
                120);

            starter.AddDialogLine(
                "bc_subinfeudation_choose_fief",
                "bc_subinfeudation_check_fiefs",
                "bc_subinfeudation_fief_list",
                "{=BC_Subinfeudation_ChooseFief}Which estate would you entrust to my new house?",
                HasEligibleGrants,
                SetupGrantList,
                110);

            starter.AddRepeatablePlayerLine(
                "bc_subinfeudation_fief",
                "bc_subinfeudation_fief_list",
                "bc_subinfeudation_fief_selected",
                "{=BC_Subinfeudation_FiefChoice}{SETTLEMENT_NAME}.",
                "{=BC_Subinfeudation_FiefChoiceOther}I am considering another estate.",
                "bc_subinfeudation_check_fiefs",
                GrantRepeatCondition,
                SelectGrant,
                120,
                GrantClickableCondition);

            starter.AddPlayerLine(
                "bc_subinfeudation_fief_cancel",
                "bc_subinfeudation_fief_list",
                "lord_pretalk",
                "{=BC_Subinfeudation_Cancel}Actually, let us leave this matter for another time.",
                null,
                ClearFlow,
                100);

            starter.AddDialogLine(
                "bc_subinfeudation_fief_selected",
                "bc_subinfeudation_fief_selected",
                "bc_subinfeudation_confirm",
                "{=BC_Subinfeudation_FiefSelected}{SETTLEMENT_NAME}? I will hold it faithfully and render the service owed through {LIEGE_TITLE}.",
                SetSelectedGrantText,
                null,
                120);

            starter.AddPlayerLine(
                "bc_subinfeudation_confirm_full",
                "bc_subinfeudation_confirm",
                "bc_subinfeudation_name_prompt",
                "{=BC_Subinfeudation_ConfirmFull}I grant you both possession of {SETTLEMENT_NAME} and its lawful title.",
                IsFullRightsGrant,
                null,
                120,
                CanConfirmGrant);

            starter.AddPlayerLine(
                "bc_subinfeudation_confirm_possession",
                "bc_subinfeudation_confirm",
                "bc_subinfeudation_name_prompt",
                "{=BC_Subinfeudation_ConfirmPossession}I grant you possession of {SETTLEMENT_NAME}, subject to its existing lawful rights.",
                IsPossessionOnlyGrant,
                null,
                120,
                CanConfirmGrant);

            starter.AddPlayerLine(
                "bc_subinfeudation_confirm_back",
                "bc_subinfeudation_confirm",
                "bc_subinfeudation_check_fiefs",
                "{=BC_Subinfeudation_ChooseAnother}I should choose another estate.",
                null,
                SetupGrantList,
                110);

            starter.AddPlayerLine(
                "bc_subinfeudation_confirm_cancel",
                "bc_subinfeudation_confirm",
                "lord_pretalk",
                "{=BC_Subinfeudation_Cancel}Actually, let us leave this matter for another time.",
                null,
                ClearFlow,
                100);

            starter.AddDialogLine(
                "bc_subinfeudation_name_prompt",
                "bc_subinfeudation_name_prompt",
                "bc_subinfeudation_conclude",
                "{=BC_Subinfeudation_NamePrompt}Then let my oath be witnessed. It would be an honor if you chose the name of my new house.",
                null,
                OpenGrantConfirmation,
                120);

            starter.AddDialogLine(
                "bc_subinfeudation_success",
                "bc_subinfeudation_conclude",
                "close_window",
                "{=BC_Subinfeudation_Success}I swear to hold these lands from you and to render the service owed by my house.[ib:hip][if:convo_happy]",
                WasGrantCompleted,
                EndSuccessfulConversation,
                130);

            starter.AddDialogLine(
                "bc_subinfeudation_not_completed",
                "bc_subinfeudation_conclude",
                "hero_main_options",
                "{=BC_Subinfeudation_NotCompleted}As you wish. I remain ready should you decide to renew the offer.",
                null,
                ClearFlow,
                100);
        }

        private bool CanStartConversation()
        {
            return CompanionSubinfeudationService.CanDiscussSubinfeudation(Hero.OneToOneConversationHero);
        }

        private void ResetFlow()
        {
            _selectedGrant = null;
            _selectedCompanionId = Hero.OneToOneConversationHero?.StringId;
            _grantCompleted = false;
        }

        private void ClearFlow()
        {
            _selectedGrant = null;
            _selectedCompanionId = null;
            _grantCompleted = false;
        }

        private bool IsConversationCompanionLeadingCaravan()
        {
            return CompanionSubinfeudationService.IsLeadingCaravan(GetConversationCompanion());
        }

        private bool HasNoEligibleGrants()
        {
            return GetEligibleGrants().Count == 0;
        }

        private bool HasEligibleGrants()
        {
            return GetEligibleGrants().Count > 0;
        }

        private void SetupGrantList()
        {
            _selectedGrant = null;
            ConversationSentence.SetObjectsToRepeatOver(
                GetEligibleGrants()
                    .Select(preview => preview.Settlement)
                    .Cast<object>()
                    .ToList());
        }

        private List<CompanionSubinfeudationPreview> GetEligibleGrants()
        {
            return CompanionSubinfeudationService.GetEligibleGrants(GetConversationCompanion());
        }

        private bool GrantRepeatCondition()
        {
            if (!(ConversationSentence.CurrentProcessedRepeatObject is Settlement settlement))
                return false;

            ConversationSentence.SelectedRepeatLine.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
            return true;
        }

        private bool GrantClickableCondition(out TextObject explanation)
        {
            explanation = null;
            if (!(ConversationSentence.CurrentProcessedRepeatObject is Settlement settlement)
                || !CompanionSubinfeudationService.TryBuildPreview(
                    GetConversationCompanion(),
                    settlement,
                    out CompanionSubinfeudationPreview preview,
                    out explanation))
            {
                explanation = explanation ?? new TextObject("{=BC_Subinfeudation_TitleUnavailable}The barony's feudal title is unavailable.");
                return false;
            }

            explanation = BuildGrantExplanation(preview, includeCosts: false);
            return true;
        }

        private void SelectGrant()
        {
            Settlement settlement = ConversationSentence.SelectedRepeatObject as Settlement;
            CompanionSubinfeudationService.TryBuildPreview(
                GetConversationCompanion(),
                settlement,
                out _selectedGrant,
                out _);
        }

        private bool SetSelectedGrantText()
        {
            if (!TryRefreshSelectedGrant(out _))
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", _selectedGrant.Settlement.Name);
            MBTextManager.SetTextVariable("LIEGE_TITLE", new TextObject("{=!}" + _selectedGrant.LiegeTitle.Name));
            return true;
        }

        private bool IsFullRightsGrant()
        {
            if (!TryRefreshSelectedGrant(out _))
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", _selectedGrant.Settlement.Name);
            return _selectedGrant.TransfersDeJure;
        }

        private bool IsPossessionOnlyGrant()
        {
            if (!TryRefreshSelectedGrant(out _))
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", _selectedGrant.Settlement.Name);
            return !_selectedGrant.TransfersDeJure;
        }

        private bool CanConfirmGrant(out TextObject explanation)
        {
            if (!TryRefreshSelectedGrant(out explanation))
                return false;

            if ((Hero.MainHero?.Gold ?? 0) < C.SubinfeudationGoldCost
                || (Clan.PlayerClan?.Influence ?? 0f) < C.SubinfeudationInfluenceCost)
            {
                explanation = BuildResourceRequirement();
                return false;
            }

            explanation = BuildGrantExplanation(_selectedGrant, includeCosts: true);
            return true;
        }

        private bool TryRefreshSelectedGrant(out TextObject failure)
        {
            failure = null;
            if (_selectedGrant?.Settlement == null)
            {
                failure = new TextObject("{=BC_Subinfeudation_NoSelection}No estate has been selected.");
                return false;
            }

            if (!CompanionSubinfeudationService.TryBuildPreview(
                GetConversationCompanion(),
                _selectedGrant.Settlement,
                out CompanionSubinfeudationPreview refreshed,
                out failure))
            {
                return false;
            }

            if (refreshed.BaronyTitle.TitleId != _selectedGrant.BaronyTitle.TitleId
                || refreshed.LiegeTitle.TitleId != _selectedGrant.LiegeTitle.TitleId
                || refreshed.TransfersDeJure != _selectedGrant.TransfersDeJure)
            {
                failure = new TextObject("{=BC_Subinfeudation_GrantChanged}The proposed grant has changed and must be reconsidered.");
                return false;
            }

            _selectedGrant = refreshed;
            return true;
        }

        private void OpenGrantConfirmation()
        {
            if (!TryRefreshSelectedGrant(out TextObject failure))
            {
                ShowFailureAndContinue(failure);
                return;
            }

            InquiryData inquiry = new InquiryData(
                new TextObject("{=BC_Subinfeudation_ConfirmTitle}Establish a Vassal House").ToString(),
                BuildGrantExplanation(_selectedGrant, includeCosts: true).ToString(),
                true,
                true,
                GameTexts.FindText("str_yes").ToString(),
                GameTexts.FindText("str_no").ToString(),
                OpenClanNameInquiry,
                CancelGrantConfirmation);
            InformationManager.ShowInquiry(inquiry);
        }

        private void OpenClanNameInquiry()
        {
            if (!TryRefreshSelectedGrant(out TextObject failure)
                || (Hero.MainHero?.Gold ?? 0) < C.SubinfeudationGoldCost
                || (Clan.PlayerClan?.Influence ?? 0f) < C.SubinfeudationInfluenceCost)
            {
                ShowFailureAndContinue(failure ?? BuildResourceRequirement());
                return;
            }

            Hero companion = GetConversationCompanion();
            TextObject title = new TextObject("{=BC_Subinfeudation_SelectClanName}Choose the name of {COMPANION.NAME}{.o} new house:");
            if (companion?.CharacterObject != null)
                StringHelpers.SetCharacterProperties("COMPANION", companion.CharacterObject, title);

            InformationManager.ShowTextInquiry(new TextInquiryData(
                title.ToString(),
                string.Empty,
                true,
                true,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                CompleteGrant,
                CancelClanNameInquiry,
                false,
                FactionHelper.IsClanNameApplicable));
        }

        private void CompleteGrant(string clanName)
        {
            try
            {
                _grantCompleted = CompanionSubinfeudationService.TryCreateVassalClan(
                    GetConversationCompanion(),
                    _selectedGrant,
                    clanName,
                    out _,
                    out TextObject failure);
                if (!_grantCompleted)
                    BellumCivileNotifications.ShowPersonal(failure, BellumNotificationColors.Warning);
            }
            catch (Exception ex)
            {
                _grantCompleted = false;
                BellumCivileLogger.Log($"Companion subinfeudation failed during clan creation: {ex}");
                BellumCivileNotifications.ShowPersonal(
                    new TextObject("{=BC_Subinfeudation_UnexpectedFailure}The new house could not be established because the grant failed unexpectedly."),
                    BellumNotificationColors.Warning);
            }

            Campaign.Current?.ConversationManager?.ContinueConversation();
        }

        private void CancelGrantConfirmation()
        {
            _grantCompleted = false;
            Campaign.Current?.ConversationManager?.ContinueConversation();
        }

        private void CancelClanNameInquiry()
        {
            CancelGrantConfirmation();
        }

        private bool WasGrantCompleted()
        {
            return _grantCompleted;
        }

        private void EndSuccessfulConversation()
        {
            if (PlayerEncounter.Current != null)
                PlayerEncounter.LeaveEncounter = true;
            ClearFlow();
        }

        private Hero GetConversationCompanion()
        {
            Hero companion = Hero.OneToOneConversationHero;
            return companion != null
                && (string.IsNullOrWhiteSpace(_selectedCompanionId) || companion.StringId == _selectedCompanionId)
                ? companion
                : null;
        }

        private static TextObject BuildGrantExplanation(CompanionSubinfeudationPreview preview, bool includeCosts)
        {
            if (preview == null)
                return new TextObject("{=BC_Subinfeudation_TitleUnavailable}The barony's feudal title is unavailable.");

            TextObject text;
            if (preview.TransfersDeJure)
            {
                text = new TextObject("{=BC_Subinfeudation_FullHint}{SETTLEMENT} will be granted with both possession and lawful title. The new house will owe feudal service through {LIEGE_TITLE}.{RELATION}{COSTS}");
            }
            else
            {
                Clan lawfulHolder = Clan.All.FirstOrDefault(clan => clan?.StringId == preview.BaronyTitle.DeJureHolderClanId);
                text = new TextObject("{=BC_Subinfeudation_PossessionHint}{SETTLEMENT} will be granted in possession only. Its lawful title remains with {LAWFUL_HOLDER}, and existing claims or disputes remain in force. The new house will owe feudal service through {LIEGE_TITLE}.{RELATION}{COSTS}");
                text.SetTextVariable(
                    "LAWFUL_HOLDER",
                    lawfulHolder?.Name ?? new TextObject("{=BC_Subinfeudation_UnknownHolder}its present lawful holder"));
            }

            TextObject relation = new TextObject("{=BC_Subinfeudation_RelationHint}{newline}Relation with the new house: +{RELATION_GAIN}.");
            relation.SetTextVariable("RELATION_GAIN", preview.RelationGain);
            text.SetTextVariable("SETTLEMENT", preview.Settlement.Name);
            text.SetTextVariable("LIEGE_TITLE", new TextObject("{=!}" + preview.LiegeTitle.Name));
            text.SetTextVariable("RELATION", relation);
            text.SetTextVariable("COSTS", includeCosts ? BuildCostText() : new TextObject("{=!}"));
            return text;
        }

        private static TextObject BuildCostText()
        {
            TextObject costs = new TextObject("{=BC_Subinfeudation_CostHint}{newline}Cost: {GOLD} denars and {INFLUENCE} influence.");
            costs.SetTextVariable("GOLD", C.SubinfeudationGoldCost.ToString("N0"));
            costs.SetTextVariable("INFLUENCE", C.SubinfeudationInfluenceCost.ToString("N0"));
            return costs;
        }

        private static TextObject BuildResourceRequirement()
        {
            bool lacksGold = (Hero.MainHero?.Gold ?? 0) < C.SubinfeudationGoldCost;
            bool lacksInfluence = (Clan.PlayerClan?.Influence ?? 0f) < C.SubinfeudationInfluenceCost;
            if (lacksGold && lacksInfluence)
            {
                TextObject both = new TextObject("{=BC_Subinfeudation_NeedBoth}You need {GOLD} denars and {INFLUENCE} influence.");
                both.SetTextVariable("GOLD", C.SubinfeudationGoldCost.ToString("N0"));
                both.SetTextVariable("INFLUENCE", C.SubinfeudationInfluenceCost.ToString("N0"));
                return both;
            }

            TextObject requirement = lacksGold
                ? new TextObject("{=BC_Subinfeudation_NeedGoldAmount}You need {GOLD} denars.")
                : new TextObject("{=BC_Subinfeudation_NeedInfluenceAmount}You need {INFLUENCE} influence.");
            requirement.SetTextVariable("GOLD", C.SubinfeudationGoldCost.ToString("N0"));
            requirement.SetTextVariable("INFLUENCE", C.SubinfeudationInfluenceCost.ToString("N0"));
            return requirement;
        }

        private static void ShowFailureAndContinue(TextObject failure)
        {
            BellumCivileNotifications.ShowPersonal(
                failure ?? new TextObject("{=BC_Subinfeudation_GrantChanged}The proposed grant has changed and must be reconsidered."),
                BellumNotificationColors.Warning);
            Campaign.Current?.ConversationManager?.ContinueConversation();
        }
    }
}
