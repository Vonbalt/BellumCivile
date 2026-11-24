using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    internal sealed partial class BetrothalBehavior
    {
        private BetrothalRecord _breakSelection;
        private bool _brokeSelectedPledge;
        private Clan _proposalHouse;
        private Hero _proposedPlayer, _proposedOther;
        private bool _hasProposalChoices;
        private BetrothalRecord _proposal;
        private ProposalResult _proposalResult;

        private enum ProposalResult { Ready, Stale, Refused, Pledged }

        private void RegisterDialogue(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_betrothal_propose", "lord_talk_speak_diplomacy_2", "bc_betrothal_propose_reply",
                "{=BC_Betrothal_Propose}I wish to speak of a betrothal between our houses.",
                () => CanDiscussProposal(OtherLeaderInConversation()), BeginProposal, 123);
            starter.AddDialogLine("bc_betrothal_propose_reply", "bc_betrothal_propose_reply", "bc_betrothal_choose_player",
                "{=BC_Betrothal_ProposeReply}Such a promise is not lightly made. Tell me whom you have in mind.",
                () => _hasProposalChoices, null);
            starter.AddDialogLine("bc_betrothal_no_candidates", "bc_betrothal_propose_reply", "lord_pretalk",
                "{=BC_Betrothal_NoCandidates}There is no suitable betrothal between our houses at present.",
                () => !_hasProposalChoices, ClearProposal);
            starter.AddRepeatablePlayerLine("bc_betrothal_player_choice", "bc_betrothal_choose_player", "bc_betrothal_other_prompt",
                "{=BC_Betrothal_PlayerChoice}I would pledge {BC_BETROTHAL_CANDIDATE}.",
                "{=BC_Betrothal_AnotherCandidate}I had someone else in mind.", "bc_betrothal_propose_reply",
                ShowProposalCandidate, SelectPlayerCandidate, 120);
            starter.AddPlayerLine("bc_betrothal_player_cancel", "bc_betrothal_choose_player", "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.", null, ClearProposal, 100);
            starter.AddDialogLine("bc_betrothal_other_prompt", "bc_betrothal_other_prompt", "bc_betrothal_choose_other",
                "{=BC_Betrothal_TheirCandidates}And whom would you ask me to pledge in return?", () => _hasProposalChoices, null);
            starter.AddDialogLine("bc_betrothal_no_counterpart", "bc_betrothal_other_prompt", "lord_pretalk",
                "{=BC_Betrothal_NoCandidates}There is no suitable betrothal between our houses at present.",
                () => !_hasProposalChoices, ClearProposal);
            starter.AddRepeatablePlayerLine("bc_betrothal_other_choice", "bc_betrothal_choose_other", "bc_betrothal_household_prompt",
                "{=BC_Betrothal_OtherChoice}I would ask for {BC_BETROTHAL_CANDIDATE}.",
                "{=BC_Betrothal_AnotherCandidate}I had someone else in mind.", "bc_betrothal_other_prompt",
                ShowProposalCandidate, () => _proposedOther = ConversationSentence.SelectedRepeatObject as Hero, 120);
            starter.AddPlayerLine("bc_betrothal_other_back", "bc_betrothal_choose_other", "bc_betrothal_propose_reply",
                "{=BC_Marriage_BackToPlayerChoice}I should offer a different member of my household.", null, BeginProposal, 110);
            starter.AddPlayerLine("bc_betrothal_other_cancel", "bc_betrothal_choose_other", "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.", null, ClearProposal, 100);
            starter.AddDialogLine("bc_betrothal_household_prompt", "bc_betrothal_household_prompt", "bc_betrothal_household",
                "{=BC_Betrothal_HouseholdText}When {BC_BETROTHAL_FIRST} and {BC_BETROTHAL_SECOND} marry, which house will they join? The wedding is expected on {BC_BETROTHAL_DATE}.",
                ShowProposalHouseholds, null);
            starter.AddDialogLine("bc_betrothal_pair_changed", "bc_betrothal_household_prompt", "lord_pretalk",
                "{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.", null, ClearProposal, 90);
            foreach (bool receivingOurHouse in new[] { true, false })
            {
                bool ours = receivingOurHouse;
                starter.AddPlayerLine("bc_betrothal_household_" + (ours ? "ours" : "theirs"), "bc_betrothal_household",
                    "bc_betrothal_terms_reply", ours
                        ? "{=BC_Betrothal_OurHouseChoice}They will join my house, {BC_BETROTHAL_OUR_HOUSE}."
                        : "{=BC_Betrothal_TheirHouseChoice}They will join your house, {BC_BETROTHAL_THEIR_HOUSE}.",
                    null, () => ChooseProposalHousehold(ProposalDestination(ours)), 120,
                    (out TextObject hint) => CanChooseProposalHousehold(ProposalDestination(ours), out hint), null);
            }
            starter.AddPlayerLine("bc_betrothal_household_back", "bc_betrothal_household", "bc_betrothal_other_prompt",
                "{=BC_Betrothal_ChooseAnotherCounterpart}Let us speak of another match.", null, PrepareOtherCandidates, 110);
            starter.AddPlayerLine("bc_betrothal_household_cancel", "bc_betrothal_household", "lord_pretalk",
                "{=BC_Marriage_NeverMind}Actually, never mind.", null, ClearProposal, 100);
            starter.AddDialogLine("bc_betrothal_terms_refused", "bc_betrothal_terms_reply", "bc_betrothal_terms_reconsider",
                "{=BC_Betrothal_Refusal}I cannot give my word on those terms.", () => _proposalResult == ProposalResult.Refused, null, 120);
            starter.AddDialogLine("bc_betrothal_terms_ready", "bc_betrothal_terms_reply", "bc_betrothal_terms_confirm",
                "{=BC_Betrothal_TermsReply}So be it. Let us set down what we have agreed.",
                () => _proposalResult == ProposalResult.Ready && _proposal != null, null, 110);
            starter.AddDialogLine("bc_betrothal_terms_stale", "bc_betrothal_terms_reply", "lord_pretalk",
                "{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.", null, ClearProposal, 90);
            starter.AddPlayerLine("bc_betrothal_offer_terms", "bc_betrothal_terms_confirm", "bc_betrothal_barter_result",
                "{=BC_Betrothal_OfferTerms}Let us settle the pledge.", null, OpenProposalBarter, 120);
            foreach (string token in new[] { "bc_betrothal_terms_confirm", "bc_betrothal_terms_reconsider" })
            {
                starter.AddPlayerLine(token + "_back", token, "bc_betrothal_household_prompt",
                    "{=BC_Betrothal_ReconsiderHousehold}Perhaps we should speak again of which house they will join.", null, null, 110);
                starter.AddPlayerLine(token + "_cancel", token, "lord_pretalk",
                    "{=BC_Marriage_NeverMind}Actually, never mind.", null, ClearProposal, 100);
            }
            starter.AddDialogLine("bc_betrothal_barter_agreed", "bc_betrothal_barter_result", "lord_pretalk",
                "{=BC_Betrothal_Agreed}Then our houses have given their word. May this pledge bring us closer.",
                () => _proposalResult == ProposalResult.Pledged, ClearProposal, 120);
            starter.AddDialogLine("bc_betrothal_barter_stale", "bc_betrothal_barter_result", "lord_pretalk",
                "{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.",
                () => _proposalResult == ProposalResult.Stale || _proposalResult == ProposalResult.Refused, ClearProposal, 110);
            starter.AddDialogLine("bc_betrothal_barter_cancelled", "bc_betrothal_barter_result", "lord_pretalk",
                "{=BC_Betrothal_BarterCancelled}Then we shall leave the matter for another day.", null, ClearProposal, 100);

            starter.AddPlayerLine("bc_betrothal_break", "lord_talk_speak_diplomacy_2", "bc_betrothal_break_reply",
                "{=BC_Betrothal_DiscussBreaking}I would speak with you about ending a betrothal between our houses.",
                () => PledgesWith(OtherLeaderInConversation()).Any(), () => { _breakSelection = null; _brokeSelectedPledge = false; }, 122);
            starter.AddDialogLine("bc_betrothal_break_reply", "bc_betrothal_break_reply", "bc_betrothal_break_choose",
                "{=BC_Betrothal_BreakReply}We gave one another our word. Which pledge are you asking me to set aside?", null,
                () => ConversationSentence.SetObjectsToRepeatOver(PledgesWith(OtherLeaderInConversation()).ToList()));
            starter.AddRepeatablePlayerLine("bc_betrothal_break_choice", "bc_betrothal_break_choose", "bc_betrothal_break_question",
                "{=BC_Betrothal_BreakChoice}The pledge between {BC_PLEDGE_FIRST} and {BC_PLEDGE_SECOND}.",
                "{=BC_Betrothal_AnotherPledge}I meant another pledge.", "bc_betrothal_break_reply", ShowBreakChoice,
                () => _breakSelection = ConversationSentence.SelectedRepeatObject as BetrothalRecord, 120);
            starter.AddPlayerLine("bc_betrothal_break_choose_back", "bc_betrothal_break_choose", "lord_pretalk",
                "{=BC_Betrothal_LeaveIt}Let us leave it as it stands.", null, () => _breakSelection = null);
            starter.AddDialogLine("bc_betrothal_break_question", "bc_betrothal_break_question", "bc_betrothal_break_confirm",
                "{=BC_Betrothal_BreakQuestion}My house has made plans on the strength of your word. Are you truly withdrawing it?{BC_BETROTHAL_BREAK_COST}",
                ShowBreakConsequences, null);
            starter.AddPlayerLine("bc_betrothal_break_confirm", "bc_betrothal_break_confirm", "bc_betrothal_break_answer",
                "{=BC_Betrothal_WithdrawWord}I am. The betrothal is at an end.", null,
                () => { _brokeSelectedPledge = BreakWithPlayer(_breakSelection, OtherLeaderInConversation()); _breakSelection = null; },
                100, CanBreakSelectedPledge, null);
            starter.AddPlayerLine("bc_betrothal_break_cancel", "bc_betrothal_break_confirm", "lord_pretalk",
                "{=BC_Betrothal_LeaveIt}Let us leave it as it stands.", null, () => _breakSelection = null);
            starter.AddDialogLine("bc_betrothal_break_anger", "bc_betrothal_break_answer", "lord_pretalk",
                "{=BC_Betrothal_BreakAnger}So your promise was worth no more than that. I shall remember what your word is worth.",
                () => _brokeSelectedPledge, () => _brokeSelectedPledge = false, 110);
            starter.AddDialogLine("bc_betrothal_break_stale", "bc_betrothal_break_answer", "lord_pretalk",
                "{=BC_Betrothal_BreakStale}There is no such pledge between our houses now.", null, null, 100);
        }

        private static Clan OtherLeaderInConversation()
        {
            var leader = Hero.OneToOneConversationHero;
            return leader?.Clan != null && leader == leader.Clan.Leader && !leader.IsPrisoner
                && Clan.PlayerClan != null && leader.Clan != Clan.PlayerClan && Hero.MainHero == Clan.PlayerClan.Leader
                ? leader.Clan : null;
        }

        private IEnumerable<BetrothalRecord> PledgesWith(Clan other) => other == null ? Enumerable.Empty<BetrothalRecord>()
            : _pledges.Where(p => p.Accepted && p.IsPlayerPledge && p.OtherPlayerHouse == other && IdentityValid(p));

        private bool ShowBreakChoice()
        {
            var p = ConversationSentence.CurrentProcessedRepeatObject as BetrothalRecord;
            if (p == null || !PledgesWith(OtherLeaderInConversation()).Contains(p)) return false;
            MBTextManager.SetTextVariable("BC_PLEDGE_FIRST", p.First.Name);
            MBTextManager.SetTextVariable("BC_PLEDGE_SECOND", p.Second.Name);
            return true;
        }

        private bool BreakWithPlayer(BetrothalRecord p, Clan other) => p != null && other != null
            && p.Accepted && p.IsPlayerPledge && p.OtherPlayerHouse == other && IdentityValid(p)
            && End(p, "player_renounced", Clan.PlayerClan);

        private bool CanBreakSelectedPledge(out TextObject hint)
        {
            hint = null;
            if (_breakSelection == null || CanSettleEscrow(_breakSelection, Clan.PlayerClan)) return true;
            hint = MarriagePayments.Unaffordable(Clan.PlayerClan, MarriagePayments.RenunciationPremium(_breakSelection.HeldDeposit));
            return false;
        }

        private bool ShowBreakConsequences()
        {
            int held = _breakSelection?.HeldDeposit ?? 0;
            TextObject consequence = held <= 0 ? new TextObject(string.Empty)
                : (_breakSelection.Destination == Clan.PlayerClan
                    ? new TextObject("{=BC_Betrothal_BreakForfeits} Your deposit of {DEPOSIT} denars will pass to my house.")
                    : new TextObject("{=BC_Betrothal_BreakRepays} My house will have its {DEPOSIT}-denar deposit returned, and you will owe us {PREMIUM} more."))
                    .SetTextVariable("DEPOSIT", held).SetTextVariable("PREMIUM", MarriagePayments.RenunciationPremium(held));
            MBTextManager.SetTextVariable("BC_BETROTHAL_BREAK_COST", consequence);
            return true;
        }

        private static bool CanDiscussProposal(Clan other) => CanArrange && Clan.PlayerClan != null
            && HouseExists(other) && !AtWar(Clan.PlayerClan, other);

        private void ClearProposal()
        {
            _proposalHouse = null;
            _proposedPlayer = _proposedOther = null;
            _proposal = null;
            _hasProposalChoices = false;
            _proposalResult = ProposalResult.Ready;
        }

        private void BeginProposal()
        {
            ClearProposal();
            _proposalHouse = OtherLeaderInConversation();
            if (!CanDiscussProposal(_proposalHouse)) return;
            var others = HouseholdMembership.Members(_proposalHouse).Where(Potential).ToList();
            var policy = new MarriageHouseholdPolicy();
            SetProposalCandidates(HouseholdMembership.Members(Clan.PlayerClan).Where(Potential)
                .Where(first => others.Any(second => HasProposalHousehold(first, second, policy))));
        }

        private static bool HasProposalHousehold(Hero first, Hero second, MarriageHouseholdPolicy policy) =>
            CompatiblePair(first, second) && (CanSetDestination(first, second, first.Clan, policy)
                || CanSetDestination(first, second, second.Clan, policy));

        private void SelectPlayerCandidate()
        {
            _proposedPlayer = ConversationSentence.SelectedRepeatObject as Hero;
            PrepareOtherCandidates();
        }

        private void PrepareOtherCandidates()
        {
            _proposedOther = null;
            _proposal = null;
            _hasProposalChoices = false;
            if (_proposalHouse != OtherLeaderInConversation() || !CanDiscussProposal(_proposalHouse)
                || _proposedPlayer?.Clan != Clan.PlayerClan || !Potential(_proposedPlayer)) return;
            var policy = new MarriageHouseholdPolicy();
            SetProposalCandidates(HouseholdMembership.Members(_proposalHouse).Where(Potential)
                .Where(second => HasProposalHousehold(_proposedPlayer, second, policy)));
        }

        private void SetProposalCandidates(IEnumerable<Hero> candidates)
        {
            var choices = candidates.OrderBy(hero => hero.IsChild ? 0 : 1).ThenBy(hero => hero.Age).ToList();
            _hasProposalChoices = choices.Count > 0;
            ConversationSentence.SetObjectsToRepeatOver(choices);
        }

        private static bool ShowProposalCandidate()
        {
            if (!(ConversationSentence.CurrentProcessedRepeatObject is Hero hero)) return false;
            MBTextManager.SetTextVariable("BC_BETROTHAL_CANDIDATE", hero.Name);
            return true;
        }

        private bool ProposalPairIsValid() => _proposalHouse == OtherLeaderInConversation() && CanDiscussProposal(_proposalHouse)
            && _proposedPlayer?.Clan == Clan.PlayerClan && _proposedOther?.Clan == _proposalHouse
            && Potential(_proposedPlayer) && Potential(_proposedOther) && CompatiblePair(_proposedPlayer, _proposedOther);

        private bool ShowProposalHouseholds()
        {
            if (!ProposalPairIsValid()) return false;
            var preview = new BetrothalRecord { First = _proposedPlayer, Second = _proposedOther };
            MBTextManager.SetTextVariable("BC_BETROTHAL_FIRST", _proposedPlayer.Name);
            MBTextManager.SetTextVariable("BC_BETROTHAL_SECOND", _proposedOther.Name);
            MBTextManager.SetTextVariable("BC_BETROTHAL_OUR_HOUSE", Clan.PlayerClan.Name);
            MBTextManager.SetTextVariable("BC_BETROTHAL_THEIR_HOUSE", _proposalHouse.Name);
            MBTextManager.SetTextVariable("BC_BETROTHAL_DATE", preview.ExpectedDate(Adulthood).ToString());
            return true;
        }

        private Clan ProposalDestination(bool ours) => ours ? Clan.PlayerClan : _proposalHouse;

        private bool CanChooseProposalHousehold(Clan destination, out TextObject hint)
        {
            hint = null;
            if (!ProposalPairIsValid())
                hint = new TextObject("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.");
            else if (!CanSetDestination(_proposedPlayer, _proposedOther, destination, new MarriageHouseholdPolicy()))
                hint = new TextObject("{=BC_Betrothal_HouseholdBlocked}A house cannot let its head, heir or last family successor leave.");
            else
            {
                int deposit = MarriagePayments.Deposit(BetrothalDowry(_proposedPlayer, _proposedOther, destination));
                if (!MarriagePayments.CanPay(destination, deposit)) hint = MarriagePayments.Unaffordable(destination, deposit);
            }
            return hint == null;
        }

        private bool AcceptsProposalTerms(Clan destination) =>
            BetrothalScore(_proposedOther, _proposedPlayer, destination, BetrothalDelay(_proposedOther, _proposedPlayer))
                >= BellumCivileOptions.MarriageAcceptanceThreshold;

        private void ChooseProposalHousehold(Clan destination)
        {
            _proposal = null;
            if (!CanChooseProposalHousehold(destination, out _)) { _proposalResult = ProposalResult.Stale; return; }
            if (!AcceptsProposalTerms(destination)) { _proposalResult = ProposalResult.Refused; return; }
            _proposal = new BetrothalRecord { First = _proposedPlayer, Second = _proposedOther, FirstHouse = Clan.PlayerClan,
                SecondHouse = _proposalHouse, Destination = destination,
                Dowry = BetrothalDowry(_proposedPlayer, _proposedOther, destination) };
            _proposalResult = ProposalResult.Ready;
        }

        private bool CanConfirmProposal(BetrothalRecord terms) => terms != null && terms == _proposal
            && IdentityValid(terms) && ProposalPairIsValid()
            && CanSetDestination(terms.First, terms.Second, terms.Destination, new MarriageHouseholdPolicy()) && CanFundDeposit(terms)
            && AcceptsProposalTerms(terms.Destination);

        private void OpenProposalBarter()
        {
            var manager = BarterManager.Instance;
            var terms = _proposal;
            if (manager == null || !CanConfirmProposal(terms)) { _proposalResult = ProposalResult.Stale; return; }
            Hero speaker = Hero.OneToOneConversationHero;
            var gold = new MarriageDowryBarterable(MarriagePayments.Wallet(terms.Destination),
                MarriagePayments.Wallet(terms.SendingHouse), terms.Destination, terms.SendingHouse, terms.Deposit, isDeposit: true);
            var item = new PledgeBarterable(speaker, terms, gold, () => CanConfirmProposal(terms),
                () => CommitProposal(terms, speaker));
            // Native barter previews the deposit; only the pledge can move it into escrow.
            var barter = new BarterData(Hero.MainHero, speaker, PartyBase.MainParty, speaker.PartyBelongedTo?.Party,
                (candidate, _, __) => candidate == item || candidate == gold);
            barter.AddBarterable<OtherBarterGroup>(item, isContextDependent: true);
            barter.AddBarterable<GoldBarterGroup>(gold, isContextDependent: true);
            if (barter.GetBarterables().Count != 2) { _proposalResult = ProposalResult.Stale; return; }
            item.SetIsOffered(true);
            gold.SetIsOffered(true);
            Campaign.Current.ConversationManager.CurrentConversationIsFirst = false;
            manager.BeginPlayerBarter(barter);
        }

        private void CommitProposal(BetrothalRecord terms, Hero speaker)
        {
            if (terms != _proposal || _proposalResult != ProposalResult.Ready || speaker != Hero.OneToOneConversationHero) return;
            _proposalResult = CanConfirmProposal(terms) && CreateWithTerms(terms.First, terms.Second, terms.Destination, false, terms.Dowry)
                ? ProposalResult.Pledged : ProposalResult.Stale;
        }

        private sealed class PledgeBarterable : Barterable
        {
            private readonly BetrothalRecord _terms;
            internal readonly MarriageDowryBarterable Gold;
            private readonly Func<bool> _ready;
            private readonly Action _commit;

            internal PledgeBarterable(Hero speaker, BetrothalRecord terms, MarriageDowryBarterable gold, Func<bool> ready, Action commit)
                : base(speaker, speaker.PartyBelongedTo?.Party) { _terms = terms; Gold = gold; _ready = ready; _commit = commit; }

            public override string StringID => "bc_betrothal_pledge";
            public override TextObject Name => Terms(_terms, "{=BC_Betrothal_BarterDeposit}Betrothal of {FIRST} and {SECOND}: {BALANCE} denars due at the wedding")
                .SetTextVariable("BALANCE", _terms.Dowry - _terms.Deposit);
            public override ImageIdentifier GetVisualIdentifier() => null;
            public override int GetUnitValueForFaction(IFaction faction) => !Gold.IsOffered || Gold.CurrentAmount != _terms.Deposit
                ? -1000000000 : faction == _terms.Destination ? _terms.Deposit : -_terms.Deposit;
            internal bool CanCommit => IsOffered && CurrentAmount == 1 && Gold.IsOffered && Gold.CurrentAmount == _terms.Deposit && _ready();
            public override void Apply() { if (IsOffered && CurrentAmount == 1 && Gold.IsOffered && Gold.CurrentAmount == _terms.Deposit) _commit(); }
        }

        internal static bool ValidatePledgeBarter(IList<Barterable> items)
        {
            var pledge = items.OfType<PledgeBarterable>().FirstOrDefault();
            return pledge == null ? !items.OfType<MarriageDowryBarterable>().Any(gold => gold.IsDeposit)
                : items.Count == 2 && items.Contains(pledge.Gold) && pledge.CanCommit;
        }

        private static void Say(string text) => InformationManager.DisplayMessage(new InformationMessage(new TextObject(text).ToString()));
        private static TextObject Terms(BetrothalRecord p, string line) => new TextObject(line).SetTextVariable("FIRST", p.First.Name)
            .SetTextVariable("SECOND", p.Second.Name).SetTextVariable("HOUSE", p.Destination?.Name ?? new TextObject(string.Empty))
            .SetTextVariable("OTHER_HOUSE", p.OtherPlayerHouse.Name)
            .SetTextVariable("DATE", p.ExpectedDate(Adulthood).ToString());

        private void PresentPendingPlayerInquiry()
        {
            if (Clan.PlayerClan == null || _pledges.Count == 0) return;
            if (HasIncomingOffer && CanArrange && IdentityValid(_incomingOffer))
            {
                if (Patches.BetrothalOfferPopupPatch.IsAvailable)
                {
                    TryPublishIncomingOfferNotice();
                }
                else if (CanPresentInquiry()) { ShowTextOffer(_incomingOffer); return; }
            }
            if (!CanPresentInquiry()) return;
            foreach (var p in _pledges)
            {
                if (!p.Accepted || !p.IsPlayerPledge || !IdentityValid(p)) continue;
                if (p.PlayerConsentedToWedding || !BothOfAge(p) || TemporaryBlocker(p)
                    || !CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy())
                    || !CanFundWedding(p) || !CanExecuteWedding(p)) continue;
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_Betrothal_WeddingTitle}A Promise Comes Due").ToString(),
                    AppendFinancialTerms(Terms(p, "{=BC_Betrothal_WeddingLetter}A messenger from {OTHER_HOUSE} brings word that {FIRST} and {SECOND} have both come of age. Their house asks you to honor the pledge made between your families. The couple would join {HOUSE}."), p, true), true, true,
                    new TextObject("{=BC_Betrothal_Honor}Honor the pledge").ToString(),
                    new TextObject("{=BC_Betrothal_Renounce}Break the betrothal").ToString(),
                    () => HonorPlayerPledge(p), () => BreakWithPlayer(p, p.OtherPlayerHouse)), true);
                return;
            }
        }

        internal bool TryPublishIncomingOfferNotice()
        {
            if (!Patches.BetrothalOfferPopupPatch.IsAvailable || !HasIncomingOffer) return false;
            if (!_incomingOffer.OfferNoticePublished) PublishOfferNotice(_incomingOffer);
            return _incomingOffer.OfferNoticePublished;
        }

        private static void PublishOfferNotice(BetrothalRecord pledge)
        {
            var manager = Campaign.Current?.CampaignInformationManager;
            if (manager == null) return;
            if (!manager.InformationDataExists<BetrothalOfferNotice>(notice => notice.Pledge == pledge))
                manager.NewMapNoticeAdded(new BetrothalOfferNotice(pledge));
            pledge.OfferNoticePublished = true;
        }

        internal void InspectIncomingOffer(BetrothalRecord pledge)
        {
            if (!IsIncomingOfferCurrent(pledge) || !CanPresentInquiry()) return;
            if (!CanArrange || !IdentityValid(pledge) || !CompatiblePair(pledge.First, pledge.Second)
                || !CanSetDestination(pledge.First, pledge.Second, pledge.Destination, new MarriageHouseholdPolicy()))
            {
                if (End(pledge, "offer_lapsed")) Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.");
                return;
            }

            Patches.BetrothalOfferPopupPatch.ShowResult result = Patches.BetrothalOfferPopupPatch.TryShow(pledge);
            if (result == Patches.BetrothalOfferPopupPatch.ShowResult.Unavailable) ShowTextOffer(pledge);
        }

        private void ShowTextOffer(BetrothalRecord p) => InformationManager.ShowInquiry(new InquiryData(
            BetrothalOfferNotice.OfferTitle(p).ToString(),
            AppendFinancialTerms(Terms(p, "{=BC_Betrothal_OfferLetter}A messenger from {OTHER_HOUSE} brings a letter proposing that {FIRST} and {SECOND} be betrothed. They would join {HOUSE} when they marry. The wedding is expected on {DATE}."), p), CanFundDeposit(p), true,
            new TextObject("{=BC_Betrothal_AcceptOffer}Give my word").ToString(),
            new TextObject("{=BC_Betrothal_DeclineOffer}Decline").ToString(),
            () => ResolveIncomingOffer(p, accept: true), () => ResolveIncomingOffer(p, accept: false)), true);

        internal void ResolveIncomingOffer(BetrothalRecord pledge, bool accept)
        {
            if (!IsIncomingOfferCurrent(pledge)) return;
            if (!accept) { End(pledge, "offer_declined"); return; }
            if (!CanFundDeposit(pledge))
            {
                PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(pledge.Destination, pledge.Deposit));
                return;
            }
            if (!AcceptOffer(pledge) && End(pledge, "offer_lapsed"))
                Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms.");
        }

        private void HonorPlayerPledge(BetrothalRecord p)
        {
            if (!_pledges.Contains(p) || !p.IsPlayerPledge || !p.Accepted || !IdentityValid(p) || !BothOfAge(p)) return;
            p.PlayerConsentedToWedding = true;
            p.NextReviewDay = Day;
            ReviewWedding(p);
        }

        private static bool CanPresentInquiry()
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            return map != null && !map.AtMenu && !map.MapConversationActive && map.NextIncident == null && !map.IsSimulationActive
                && !InformationManager.IsAnyInquiryActive() && Campaign.Current?.CurrentMenuContext == null
                && Campaign.Current?.ConversationManager?.IsConversationInProgress != true && Hero.MainHero?.IsPrisoner != true
                && MobileParty.MainParty != null && MobileParty.MainParty.MapEvent == null && MobileParty.MainParty.SiegeEvent == null;
        }

        private static void ShowPledgeLapsed(BetrothalRecord p)
        {
            ShowBetrothalOutcome(p, "lapsed", null);
        }

        internal static TextObject BetrothalDepositText(BetrothalRecord p) =>
            new TextObject("{=BC_Betrothal_DepositTerms}{PAYER} will put {DEPOSIT} denars in escrow now and pay the remaining {BALANCE} to {PAYEE} at the wedding.")
                .SetTextVariable("PAYER", p.Destination.Name).SetTextVariable("PAYEE", p.SendingHouse.Name)
                .SetTextVariable("DEPOSIT", p.Deposit).SetTextVariable("BALANCE", p.Dowry - p.Deposit);

        private static string AppendFinancialTerms(TextObject intro, BetrothalRecord p, bool wedding = false)
        {
            if (p.Dowry <= 0) return intro.ToString();
            TextObject terms = wedding ? MarriagePayments.DowryText(p.Destination, p.SendingHouse, p.Balance) : BetrothalDepositText(p);
            return intro + "\n\n" + terms + (!wedding && !CanFundDeposit(p) ? "\n\n" + MarriagePayments.Unaffordable(p.Destination, p.Deposit) : string.Empty);
        }

        private static void ShowBetrothalOutcome(BetrothalRecord p, string outcome, Clan breaker)
        {
            if (!BellumCivileNotifications.ShouldPrepare(primaryClan: p.FirstHouse, secondaryClan: p.SecondHouse, isPersonal: p.IsPlayerPledge)) return;
            string line = outcome == "pledged" ? "{=BC_Betrothal_ChatPledged}{FIRST} and {SECOND} have been betrothed."
                : outcome == "renounced" ? "{=BC_Betrothal_ChatBroken}House {HOUSE} has broken its word, casting aside the betrothal of {FIRST} and {SECOND}."
                : "{=BC_Betrothal_Lapsed}The betrothal of {FIRST} and {SECOND} has come to an end.";
            var text = new TextObject(line).SetTextVariable("FIRST", p.First?.Name ?? new TextObject(string.Empty))
                .SetTextVariable("SECOND", p.Second?.Name ?? new TextObject(string.Empty));
            if (breaker != null) text.SetTextVariable("HOUSE", breaker.Name);
            BellumCivileNotifications.Show(text, BellumNotificationColors.Warning,
                primaryClan: p.FirstHouse, secondaryClan: p.SecondHouse, isPersonal: p.IsPlayerPledge);
        }

        private static void ShowBetrothalSettlement(BetrothalRecord p, Clan breaker, int held)
        {
            if (!p.IsPlayerPledge) return;
            bool forfeited = breaker != null && breaker == p.Destination;
            string line = forfeited
                ? "{=BC_Betrothal_DepositForfeited}{PAYER} forfeited its {GOLD}-denar deposit to {PAYEE}."
                : breaker != null ? "{=BC_Betrothal_DepositCompensated}{PAYER} recovered {GOLD} denars, including compensation for the broken pledge."
                : "{=BC_Betrothal_DepositRefunded}{PAYER}'s deposit of {GOLD} denars was returned.";
            BellumCivileNotifications.ShowPersonal(new TextObject(line).SetTextVariable("PAYER", p.Destination?.Name ?? p.OriginalDepositor?.Name)
                .SetTextVariable("PAYEE", p.SendingHouse?.Name ?? new TextObject(string.Empty))
                .SetTextVariable("GOLD", held + (breaker != null && !forfeited ? MarriagePayments.RenunciationPremium(held) : 0)),
                BellumNotificationColors.Warning);
        }

        private static void ShowBetrothalWeddingPayment(BetrothalRecord p, int released)
        {
            if (!p.IsPlayerPledge || released <= 0) return;
            BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_Betrothal_DepositReleased}{PAYEE} received {DOWRY} denars at the wedding, including the {DEPOSIT}-denar deposit.")
                .SetTextVariable("PAYEE", p.SendingHouse.Name).SetTextVariable("DOWRY", p.Dowry)
                .SetTextVariable("DEPOSIT", released), BellumNotificationColors.Warning);
        }
    }
}
