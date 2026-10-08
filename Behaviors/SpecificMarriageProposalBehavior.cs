using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Adds a player-controlled marriage proposal branch that lets the player pick
    /// both their own clan member and the specific bride/groom from the other clan.
    /// </summary>
    public class SpecificMarriageProposalBehavior : CampaignBehaviorBase
    {
        private Hero _selectedPlayerHero;
        private Hero _selectedOtherHero;
        private bool _playerCandidateListInitialized;
        private bool _otherCandidateListInitialized;
        private PlayerMarriageAgreement _agreement;

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
                "bc_specific_marriage_start",
                "lord_talk_speak_diplomacy_2",
                "bc_specific_marriage_choose_player_prompt",
                "{=BC_Marriage_SpecificProposal}I would like to propose an alliance between our families through marriage.",
                CanOpenSpecificMarriageProposal,
                ClearSelection,
                121);

            starter.AddDialogLine(
                "bc_specific_marriage_choose_player_prompt",
                "bc_specific_marriage_choose_player_prompt",
                "bc_specific_marriage_choose_player",
                "{=BC_Marriage_ChoosePlayerPrompt}And whose hand are you offering?",
                null,
                SetupPlayerCandidateList);

            starter.AddPlayerLine(
                "bc_specific_marriage_choose_self",
                "bc_specific_marriage_choose_player",
                "bc_specific_marriage_choose_other_prompt",
                "{=BC_Marriage_PlayerSelf}My own hand.",
                CanSelectMainHero,
                SelectMainHero,
                120);

            starter.AddRepeatablePlayerLine(
                "bc_specific_marriage_choose_player_relative",
                "bc_specific_marriage_choose_player",
                "bc_specific_marriage_choose_other_prompt",
                "{=BC_Marriage_PlayerRelative}The hand of {BC_PLAYER_CANDIDATE.NAME}.",
                "{=BC_Marriage_PlayerRelativeOther}I am thinking of a different person.",
                "bc_specific_marriage_choose_player_prompt",
                PlayerCandidateRepeatCondition,
                SelectPlayerCandidate);

            starter.AddPlayerLine(
                "bc_specific_marriage_choose_player_cancel",
                "bc_specific_marriage_choose_player",
                "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.",
                null,
                ClearSelection,
                120);

            starter.AddDialogLine(
                "bc_specific_marriage_choose_other_prompt",
                "bc_specific_marriage_choose_other_prompt",
                "bc_specific_marriage_choose_other",
                "{=BC_Marriage_ChooseOtherPrompt}And whose hand from my family do you seek?",
                HasSelectedPlayerHero,
                SetupOtherCandidateList);

            starter.AddRepeatablePlayerLine(
                "bc_specific_marriage_choose_other_relative",
                "bc_specific_marriage_choose_other",
                "bc_specific_marriage_household_prompt",
                "{=BC_Marriage_OtherRelative}I seek the hand of {BC_OTHER_CANDIDATE.NAME}.",
                "{=BC_Marriage_OtherRelativeOther}I was thinking of someone else.",
                "bc_specific_marriage_choose_other_prompt",
                OtherCandidateRepeatCondition,
                SelectOtherCandidate,
                120);

            starter.AddPlayerLine(
                "bc_specific_marriage_choose_other_back",
                "bc_specific_marriage_choose_other",
                "bc_specific_marriage_choose_player",
                "{=BC_Marriage_BackToPlayerChoice}I should offer a different member of my family.",
                null,
                ResetOtherSelection,
                110);

            starter.AddPlayerLine(
                "bc_specific_marriage_choose_other_cancel",
                "bc_specific_marriage_choose_other",
                "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.",
                null,
                ClearSelection,
                90);

            starter.AddDialogLine("bc_specific_marriage_household_prompt", "bc_specific_marriage_household_prompt",
                "bc_specific_marriage_household", "{=BC_Marriage_HouseholdQuestion}Which house would the couple belong to?", null, SetHouseholdNames);
            foreach (bool matrilineal in new[] { false, true })
            {
                bool choice = matrilineal;
                starter.AddPlayerLine("bc_specific_marriage_household_" + choice, "bc_specific_marriage_household",
                    "bc_specific_marriage_confirm_prompt", choice
                        ? "{=BC_Marriage_ChooseMatrilineal}A matrilineal marriage. {BC_GROOM} would join {BC_BRIDE_HOUSE}."
                        : "{=BC_Marriage_ChoosePatrilineal}A patrilineal marriage. {BC_BRIDE} would join {BC_GROOM_HOUSE}.",
                    HasSelectedMarriagePair, () => SelectHousehold(choice), 120,
                    (out TextObject reason) => CanChooseHousehold(choice, out reason), null);
            }
            starter.AddPlayerLine("bc_specific_marriage_household_cancel", "bc_specific_marriage_household", "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.", null, ClearSelection);
            starter.AddDialogLine("bc_specific_marriage_stale", "bc_specific_marriage_confirm_prompt", "lord_pretalk",
                "{=BC_Marriage_AgreementChanged}This marriage can no longer proceed on the agreed terms.",
                () => !HasSelectedMarriagePair() || _agreement == null, ClearSelection, 200);

            starter.AddDialogLine(
                "bc_specific_marriage_confirm_prompt",
                "bc_specific_marriage_confirm_prompt",
                "bc_specific_marriage_confirm",
                "{=BC_Marriage_ConfirmHousehold}{BC_MARRIAGE_TERMS} Let us speak of the terms.",
                HasSelectedMarriagePair,
                null);

            starter.AddPlayerLine(
                "bc_specific_marriage_confirm_terms",
                "bc_specific_marriage_confirm",
                "lord_pretalk",
                "{=BC_Marriage_DiscussTerms}Very good. Let us discuss the terms.",
                HasSelectedMarriagePair,
                StartSpecificMarriageBarter,
                120);

            starter.AddPlayerLine(
                "bc_specific_marriage_confirm_cancel",
                "bc_specific_marriage_confirm",
                "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.",
                null,
                ClearSelection,
                120);
        }

        private bool CanOpenSpecificMarriageProposal()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc?.Clan == null || npc.Clan == Clan.PlayerClan)
                return false;

            IFaction mapFaction = npc.MapFaction;
            if ((mapFaction != null && mapFaction.IsMinorFaction) || npc.IsPrisoner)
                return false;

            if (npc.Clan.Leader != npc)
                return false;

            if (Hero.MainHero?.Clan == null || FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, npc.MapFaction))
                return false;

            return GetPlayerCandidates(npc.Clan).Count > 0;
        }

        private bool CanSelectMainHero()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (Hero.MainHero == null || npc?.Clan == null)
                return false;

            return GetOtherCandidates(Hero.MainHero, npc.Clan).Count > 0;
        }

        private void SelectMainHero()
        {
            _selectedPlayerHero = Hero.MainHero;
            _selectedOtherHero = null;
            _otherCandidateListInitialized = false;
        }

        private bool HasSelectedPlayerHero()
        {
            return _selectedPlayerHero != null && Hero.OneToOneConversationHero?.Clan != null;
        }

        private bool HasSelectedMarriagePair()
        {
            return _selectedPlayerHero != null
                && _selectedOtherHero != null
                && Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(_selectedPlayerHero, _selectedOtherHero);
        }

        private void SetupPlayerCandidateList()
        {
            if (_playerCandidateListInitialized)
                return;

            Clan otherClan = Hero.OneToOneConversationHero?.Clan;
            ConversationSentence.SetObjectsToRepeatOver(GetPlayerCandidates(otherClan).Select(h => h.CharacterObject).ToList());
            _playerCandidateListInitialized = true;
        }

        private bool PlayerCandidateRepeatCondition()
        {
            if (ConversationSentence.CurrentProcessedRepeatObject is CharacterObject character)
            {
                StringHelpers.SetRepeatableCharacterProperties("BC_PLAYER_CANDIDATE", character);
                return character.HeroObject != Hero.MainHero;
            }

            return false;
        }

        private void SelectPlayerCandidate()
        {
            if (ConversationSentence.SelectedRepeatObject is CharacterObject character)
            {
                _selectedPlayerHero = character.HeroObject;
                _selectedOtherHero = null;
                _otherCandidateListInitialized = false;
            }
        }

        private void SetupOtherCandidateList()
        {
            if (_otherCandidateListInitialized)
                return;

            Clan otherClan = Hero.OneToOneConversationHero?.Clan;
            ConversationSentence.SetObjectsToRepeatOver(GetOtherCandidates(_selectedPlayerHero, otherClan).Select(h => h.CharacterObject).ToList());
            _otherCandidateListInitialized = true;
        }

        private bool OtherCandidateRepeatCondition()
        {
            if (ConversationSentence.CurrentProcessedRepeatObject is CharacterObject character)
            {
                StringHelpers.SetRepeatableCharacterProperties("BC_OTHER_CANDIDATE", character);
                return true;
            }

            return false;
        }

        private void SelectOtherCandidate()
        {
            if (ConversationSentence.SelectedRepeatObject is CharacterObject character)
                _selectedOtherHero = character.HeroObject;
        }

        private void StartSpecificMarriageBarter()
        {
            Hero target = Hero.OneToOneConversationHero;
            var agreement = _agreement;
            if (target == null || agreement == null || !HasSelectedMarriagePair())
            {
                ClearSelection();
                return;
            }

            PlayerMarriageAgreementBehavior.ConfirmDeparture(agreement, () => OpenBarter(target, agreement), ClearSelection);
        }

        private void OpenBarter(Hero target, PlayerMarriageAgreement agreement)
        {
            if (!PlayerMarriageAgreementBehavior.Ready(agreement, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); ClearSelection(); return; }

            Hero playerHero = agreement.Player;
            Hero otherHero = agreement.Other;
            MarriageBarterable marriageBarterable = new MarriageBarterable(Hero.MainHero, PartyBase.MainParty, playerHero, otherHero);
            PlayerMarriagePricing.Bind(marriageBarterable, agreement, out _);

            using (new NpcMarriageClanContext(playerHero, otherHero, agreement.Destination))
            BarterManager.Instance.StartBarterOffer(
                Hero.MainHero,
                target,
                PartyBase.MainParty,
                target.PartyBelongedTo?.Party,
                null,
                (Barterable barterableObj, BarterData args, object obj) =>
                    BarterManager.Instance.InitializeMarriageBarterContext(barterableObj, args, new Tuple<Hero, Hero>(playerHero, otherHero)),
                0,
                isAIBarter: false,
                new Barterable[] { marriageBarterable });

            ClearSelection();
        }

        private Clan Household(bool matrilineal)
            => (_selectedPlayerHero.IsFemale == matrilineal ? _selectedPlayerHero : _selectedOtherHero).Clan;

        private bool CanChooseHousehold(bool matrilineal, out TextObject reason)
            => PlayerMarriageAgreement.CanChoose(_selectedPlayerHero, _selectedOtherHero,
                _selectedPlayerHero == null || _selectedOtherHero == null ? null : Household(matrilineal),
                true, new MarriageHouseholdPolicy(), out reason);

        private void SetHouseholdNames()
        {
            if (_selectedPlayerHero == null || _selectedOtherHero == null) return;
            Hero bride = _selectedPlayerHero.IsFemale ? _selectedPlayerHero : _selectedOtherHero;
            Hero groom = bride == _selectedPlayerHero ? _selectedOtherHero : _selectedPlayerHero;
            MBTextManager.SetTextVariable("BC_BRIDE", bride.Name);
            MBTextManager.SetTextVariable("BC_GROOM", groom.Name);
            MBTextManager.SetTextVariable("BC_BRIDE_HOUSE", bride.Clan.Name);
            MBTextManager.SetTextVariable("BC_GROOM_HOUSE", groom.Clan.Name);
        }

        private void SelectHousehold(bool matrilineal)
        {
            if (!CanChooseHousehold(matrilineal, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); _agreement = null; return; }
            _agreement = PlayerMarriageAgreement.Create(_selectedPlayerHero, _selectedOtherHero, Household(matrilineal), true);
            TextObject terms = _agreement.HouseholdText();
            if (new MarriageHouseholdPolicy().CrownHeirs.Contains(_agreement.Departing))
                terms = new TextObject("{=BC_Marriage_TermsWithCrown}{TERMS} {CROWN}")
                    .SetTextVariable("TERMS", terms).SetTextVariable("CROWN", _agreement.CrownWarning());
            MBTextManager.SetTextVariable("BC_MARRIAGE_TERMS", terms);
        }

        private List<Hero> GetPlayerCandidates(Clan otherClan)
        {
            if (Clan.PlayerClan == null || otherClan == null)
                return new List<Hero>();

            return Clan.PlayerClan.AliveLords
                .Where(hero => hero != null && GetOtherCandidates(hero, otherClan).Count > 0)
                .OrderByDescending(hero => hero == Hero.MainHero)
                .ThenByDescending(hero => hero.Age)
                .ToList();
        }

        private List<Hero> GetOtherCandidates(Hero playerClanHero, Clan otherClan)
        {
            if (playerClanHero == null || otherClan == null)
                return new List<Hero>();

            return otherClan.AliveLords
                .Where(hero => hero != null && Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(playerClanHero, hero))
                .OrderByDescending(hero => hero == otherClan.Leader)
                .ThenByDescending(hero => hero.Age)
                .ToList();
        }

        private void ResetOtherSelection()
        {
            _selectedOtherHero = null;
            _playerCandidateListInitialized = false;
            _otherCandidateListInitialized = false;
            SetupPlayerCandidateList();
        }

        private void ClearSelection()
        {
            _agreement = null;
            _selectedPlayerHero = null;
            _selectedOtherHero = null;
            _playerCandidateListInitialized = false;
            _otherCandidateListInitialized = false;
        }
    }
}
