using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    internal sealed partial class BetrothalBehavior
    {
        private BetrothalRecord _breakSelection;
        private bool _brokeSelectedPledge;

        private void RegisterDialogue(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_betrothal_propose", "lord_talk_speak_diplomacy_2", "bc_betrothal_propose_reply",
                "{=BC_Betrothal_Propose}I wish to speak of a betrothal between our houses.",
                () => CanArrange && OtherLeaderInConversation() != null && !AtWar(Clan.PlayerClan, OtherLeaderInConversation()), null, 123);
            starter.AddDialogLine("bc_betrothal_propose_reply", "bc_betrothal_propose_reply", "lord_pretalk",
                "{=BC_Betrothal_ProposeReply}Such a promise is not lightly made. Tell me whom you have in mind.",
                null, BeginProposal);

            starter.AddPlayerLine("bc_betrothal_break", "lord_talk_speak_diplomacy_2", "bc_betrothal_break_reply",
                "{=BC_Betrothal_DiscussBreaking}I would speak with you about ending a betrothal between our houses.",
                () => PledgesWith(OtherLeaderInConversation()).Any(), () => { _breakSelection = null; _brokeSelectedPledge = false; }, 122);
            starter.AddDialogLine("bc_betrothal_break_reply", "bc_betrothal_break_reply", "bc_betrothal_break_choose",
                "{=BC_Betrothal_BreakReply}We gave one another our word. Which pledge are you asking me to set aside?", null,
                () => ConversationSentence.SetObjectsToRepeatOver(PledgesWith(OtherLeaderInConversation()).ToList()));
            starter.AddRepeatablePlayerLine("bc_betrothal_break_choice", "bc_betrothal_break_choose", "bc_betrothal_break_question",
                "{=BC_Betrothal_BreakChoice}The pledge between {BC_PLEDGE_FIRST} and {BC_PLEDGE_SECOND}.",
                "{=BC_Betrothal_AnotherPledge}I meant another pledge.", "bc_betrothal_break_reply", ShowBreakChoice,
                () => _breakSelection = ConversationSentence.CurrentProcessedRepeatObject as BetrothalRecord, 120);
            starter.AddPlayerLine("bc_betrothal_break_choose_back", "bc_betrothal_break_choose", "lord_pretalk",
                "{=BC_Betrothal_LeaveIt}Let us leave it as it stands.", null, () => _breakSelection = null);
            starter.AddDialogLine("bc_betrothal_break_question", "bc_betrothal_break_question", "bc_betrothal_break_confirm",
                "{=BC_Betrothal_BreakQuestion}My house has made plans on the strength of your word. Are you truly withdrawing it?", null, null);
            starter.AddPlayerLine("bc_betrothal_break_confirm", "bc_betrothal_break_confirm", "bc_betrothal_break_answer",
                "{=BC_Betrothal_WithdrawWord}I am. The betrothal is at an end.", null,
                () => { _brokeSelectedPledge = BreakWithPlayer(_breakSelection, OtherLeaderInConversation()); _breakSelection = null; });
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

        private void BeginProposal()
        {
            var other = OtherLeaderInConversation();
            if (other == null || !CanArrange) return;
            var counterparts = HouseholdMembership.Members(other).Where(Potential).ToList();
            ChooseHero("{=BC_Betrothal_OurCandidates}Whom will you pledge?", HouseholdMembership.Members(Clan.PlayerClan)
                .Where(Potential).Where(first => counterparts.Any(second => CompatiblePair(first, second))), first =>
                    ChooseHero("{=BC_Betrothal_TheirCandidates}Whom will you ask for?", counterparts.Where(second =>
                        Potential(second) && CompatiblePair(first, second)), second => ProposeHousehold(first, second)));
        }

        private void ProposeHousehold(Hero first, Hero second)
        {
            if (!CanArrange || !Potential(first) || !Potential(second) || !CompatiblePair(first, second))
            { Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms."); return; }
            var p = new BetrothalRecord { First = first, Second = second, FirstHouse = first.Clan, SecondHouse = second.Clan };
            var policy = new MarriageHouseholdPolicy();
            var choices = new[] { first.Clan, second.Clan }.Select(destination =>
            {
                bool canChoose = CanSetDestination(first, second, destination, policy);
                return new InquiryElement(destination, destination.Name.ToString(), null, canChoose, canChoose ? null :
                    new TextObject("{=BC_Betrothal_HouseholdBlocked}Their house cannot let its head, heir or last family successor leave.").ToString());
            }).ToList();
            if (!choices.Any(c => c.IsEnabled)) { Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms."); return; }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Betrothal_HouseholdTitle}The Future Household").ToString(),
                Terms(p, "{=BC_Betrothal_HouseholdText}When {FIRST} and {SECOND} marry, which house should they join? The wedding is expected on {DATE}.").ToString(),
                choices, true, 1, 1, new TextObject("{=BC_Betrothal_OfferTerms}Offer these terms").ToString(), CancelText(), selected =>
                {
                    var destination = selected.FirstOrDefault()?.Identifier as Clan;
                    if (destination == null) return;
                    if (p.First.Clan != Clan.PlayerClan || p.Second.Clan != p.SecondHouse || !Potential(first) || !Potential(second))
                    { Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms."); return; }
                    if (BetrothalScore(p.Second, p.First, destination, BetrothalDelay(p.First, p.Second)) < BellumCivileOptions.MarriageAcceptanceThreshold)
                    { Say("{=BC_Betrothal_Refusal}The other house is unwilling to give its word on those terms."); return; }
                    if (!Create(first, second, destination, offerToPlayer: false))
                    { Say("{=BC_Betrothal_NoLongerPossible}The betrothal can no longer be agreed on those terms."); return; }
                    InformationManager.DisplayMessage(new InformationMessage(Terms(p,
                        "{=BC_Betrothal_Agreed}The houses have pledged {FIRST} and {SECOND} in betrothal.").ToString()));
                }, null), true);
        }

        private static void ChooseHero(string title, IEnumerable<Hero> heroes, Action<Hero> choose)
        {
            var choices = heroes.OrderBy(hero => hero.IsChild ? 0 : 1).ThenBy(hero => hero.Age)
                .Select(hero => new InquiryElement(hero, hero.Name.ToString(), null)).ToList();
            if (choices.Count == 0) { Say("{=BC_Betrothal_NoCandidates}There is no suitable betrothal between our houses at present."); return; }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(new TextObject(title).ToString(), string.Empty,
                choices, true, 1, 1, new TextObject("{=BC_Betrothal_Continue}Continue").ToString(), CancelText(), selected =>
                {
                    if (selected.FirstOrDefault()?.Identifier is Hero hero) choose(hero);
                }, null), true);
        }

        private static string CancelText() => new TextObject("{=BC_Betrothal_Cancel}Cancel").ToString();
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
                    || !CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy()) || !CanExecuteWedding(p)) continue;
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_Betrothal_WeddingTitle}A Promise Comes Due").ToString(),
                    Terms(p, "{=BC_Betrothal_WeddingLetter}A messenger from {OTHER_HOUSE} brings word that {FIRST} and {SECOND} have both come of age. Their house asks you to honor the pledge made between your families. The couple would join {HOUSE}.").ToString(), true, true,
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
            Terms(p, "{=BC_Betrothal_OfferLetter}A messenger from {OTHER_HOUSE} brings a letter proposing that {FIRST} and {SECOND} be betrothed. They would join {HOUSE} when they marry. The wedding is expected on {DATE}.").ToString(), true, true,
            new TextObject("{=BC_Betrothal_AcceptOffer}Give my word").ToString(),
            new TextObject("{=BC_Betrothal_DeclineOffer}Decline").ToString(),
            () => ResolveIncomingOffer(p, accept: true), () => ResolveIncomingOffer(p, accept: false)), true);

        internal void ResolveIncomingOffer(BetrothalRecord pledge, bool accept)
        {
            if (!IsIncomingOfferCurrent(pledge)) return;
            if (!accept) { End(pledge, "offer_declined"); return; }
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
            if (p.IsPlayerPledge) InformationManager.DisplayMessage(new InformationMessage(Terms(p,
                "{=BC_Betrothal_Lapsed}The betrothal of {FIRST} and {SECOND} has come to an end.").ToString()));
        }
    }
}
