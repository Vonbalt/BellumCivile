using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private string _mandateConversation;
        private Hero _mandateSpeaker;
        private bool _mandateRequested;
        private readonly Dictionary<int, PersuasionOptionArgs> _mandateArguments = new Dictionary<int, PersuasionOptionArgs>();
        private static readonly string[] MandateArgumentTexts = {
            "{=BC_MandateArgHonor}Our successors must inherit a lawful settlement, not uncertainty over the Crown. Support this course for the honor of the realm.",
            "{=BC_MandateArgRealm}Think of those who must live beneath the laws we make. Let their welfare guide your judgment on the mandate.",
            "{=BC_MandateArgStrategy}Consider how this settlement will shape the next reign. It offers the sounder course for our realm.",
            "{=BC_MandateArgCourage}Do not let fear of displeasing other houses decide your vote. Stand for the course you believe is right."
        };
        private bool MandateConversationLive() => _mandateSpeaker == Hero.OneToOneConversationHero
            && CanLobbyMandate(_mandateConversation, _mandateSpeaker);
        private CourtMandatePledge ConversationPledge => MandatePledge(_mandateConversation, _mandateSpeaker?.Clan);
        private void SelectMandateConversation()
        {
            _mandateSpeaker = Hero.OneToOneConversationHero;
            _mandateConversation = PendingMandate(Clan.PlayerClan?.Kingdom)?.Mandate.Id;
            _mandateArguments.Clear();
        }
        private void SetMandateOutcomeText()
        {
            var m = MandateAgenda(_mandateConversation)?.Mandate;
            MBTextManager.SetTextVariable("BC_MANDATE_NEW", RealmLawRegistry.Instance.Find(m?.NewLaw)?.Name ?? TextObject.GetEmpty());
            MBTextManager.SetTextVariable("BC_MANDATE_OLD", RealmLawRegistry.Instance.Find(m?.OldLaw)?.Name ?? TextObject.GetEmpty());
            MBTextManager.SetTextVariable("BC_MANDATE_OUTCOME", RealmLawRegistry.Instance.Find(_mandateRequested ? m?.NewLaw : m?.OldLaw)?.Name ?? TextObject.GetEmpty());
        }
        private void RegisterMandateDialogues(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_mandate_entry", "hero_main_options", "bc_mandate_answer",
                "{=BC_MandateQuestion}What is your judgment on the proposed change to elective mandates?",
                () => { var a = PendingMandate(Clan.PlayerClan?.Kingdom); return a != null && CanLobbyMandate(a.Mandate.Id, Hero.OneToOneConversationHero); },
                SelectMandateConversation, 100, null, null);
            starter.AddDialogLine("bc_mandate_stale", "bc_mandate_answer", "lord_pretalk",
                "{=BC_MandateTalkStale}That proposal is no longer before us. There is nothing to settle here.", () => !MandateConversationLive(), null, 200, null);
            starter.AddDialogLine("bc_mandate_promised", "bc_mandate_answer", "bc_mandate_choices",
                "{=BC_MandatePromised}My word is already given on this proposal. I shall vote for {BC_MANDATE_OUTCOME}.",
                () => { if (ConversationPledge?.Committed != true) return false; _mandateRequested = ConversationPledge.Reform; SetMandateOutcomeText(); return true; }, null, 150, null);
            starter.AddDialogLine("bc_mandate_support", "bc_mandate_answer", "bc_mandate_choices",
                "{=BC_MandateInclined}I am inclined to favor {BC_MANDATE_OUTCOME}, though I have not yet given my word. The change concerns future reigns; our present ruler's mandate would stand.",
                () => { var a = MandateAgenda(_mandateConversation); if (a == null) return false;
                    _mandateRequested = a.Mandate.Decision.NaturalSupport(_mandateSpeaker.Clan) > 50; SetMandateOutcomeText(); return true; }, null, 100, null);
            starter.AddPlayerLine("bc_mandate_why", "bc_mandate_choices", "bc_mandate_motives",
                "{=BC_MandateWhy}Why do you favor this course?", MandateConversationLive, null, 120, null, null);
            starter.AddDialogLine("bc_mandate_motive", "bc_mandate_motives", "bc_mandate_choices", "{=BC_MandateMotiveLine}{BC_MANDATE_MOTIVE}",
                () => { MBTextManager.SetTextVariable("BC_MANDATE_MOTIVE", MandateMotive()); return true; }, null, 100, null);
            foreach (bool reform in new[] { true, false })
            {
                bool request = reform;
                starter.AddPlayerLine("bc_mandate_request_" + reform, "bc_mandate_choices", "bc_mandate_request_answer",
                    reform ? "{=BC_MandateAskReform}I ask you to support {BC_MANDATE_NEW}." : "{=BC_MandateAskRetain}I ask you to retain {BC_MANDATE_OLD}.",
                    () => MandateConversationLive() && ConversationPledge?.Committed != true,
                    () => { _mandateRequested = request; SetMandateOutcomeText(); }, 110, null, null);
            }
            starter.AddDialogLine("bc_mandate_request_answer", "bc_mandate_request_answer", "bc_mandate_lobby",
                "{=BC_MandateHearCase}And why should I pledge my vote to that course?", null, null, 100, null);
            starter.AddPlayerLine("bc_mandate_persuade", "bc_mandate_lobby", "bc_mandate_arguments_intro",
                "{=BC_MandatePersuade}Allow me to make the case.", null, StartMandatePersuasion, 110, MandatePersuasionClickable, null);
            starter.AddPlayerLine("bc_mandate_bargain", "bc_mandate_lobby", "bc_mandate_barter_intro",
                "{=BC_MandateBargain}Perhaps a private arrangement would settle your doubts.", null, null, 100, MandateBribeClickable, null);
            starter.AddDialogLine("bc_mandate_barter_intro", "bc_mandate_barter_intro", "bc_mandate_barter_start",
                "{=BC_MandateBargainAnswer}I am prepared to hear your terms. My word would bind me on this proposal alone.", null, null, 100, null);
            starter.AddPlayerLine("bc_mandate_barter_start", "bc_mandate_barter_start", "lord_pretalk",
                "{=BC_PolicyDelib_DiscussTerms}Let us discuss terms.", null, LaunchMandateBarter, 100, MandateBribeClickable, null);
            foreach (string token in new[] { "bc_mandate_choices", "bc_mandate_lobby", "bc_mandate_barter_start" })
                starter.AddPlayerLine(token + "_leave", token, "lord_pretalk", "{=BC_Deliberation_Back}Never mind. Let us speak of something else.", null, null, 50, null, null);
            starter.AddDialogLine("bc_mandate_arguments_intro", "bc_mandate_arguments_intro", "bc_mandate_arguments",
                "{=BC_PolicyDelib_PersuasionResponse}You may try. What argument do you offer?", null, null, 100, null);
            for (int i = 0; i < MandateArgumentTexts.Length; i++)
            {
                int slot = i;
                starter.AddPlayerLine("bc_mandate_argument_" + i, "bc_mandate_arguments", "bc_mandate_result", MandateArgumentTexts[i], null,
                    () => MandateArgument(slot).BlockTheOption(true), 120 - i,
                    (out TextObject hint) => { hint = new TextObject("{=9ACJsI6S}Blocked"); return MandateConversationLive() && !MandateArgument(slot).IsBlocked; },
                    () => MandateArgument(slot));
            }
            starter.AddDialogLine("bc_mandate_result_stale", "bc_mandate_result", "lord_pretalk",
                "{=BC_MandateTalkStale}That proposal is no longer before us. There is nothing to settle here.",
                () => !MandateConversationLive(), EndMandatePersuasion, 200, null);
            starter.AddDialogLine("bc_mandate_result_success", "bc_mandate_result", "lord_pretalk",
                "{=BC_MandatePersuaded}Very well. When this proposal comes before the court, my vote will be for {BC_MANDATE_OUTCOME}.",
                () => ConversationManager.GetPersuasionProgressSatisfied(),
                () => { CommitMandate(_mandateConversation, _mandateSpeaker, _mandateRequested, false); EndMandatePersuasion(); }, 150, null);
            starter.AddDialogLine("bc_mandate_result_failed", "bc_mandate_result", "lord_pretalk",
                "{=BC_MandateRejected}I have heard your case, but my judgment remains unchanged.", MandatePersuasionFailed,
                () => { CommitMandate(_mandateConversation, _mandateSpeaker, _mandateRequested, false, true); EndMandatePersuasion(); }, 140, null);
            starter.AddDialogLine("bc_mandate_result_continue", "bc_mandate_result", "bc_mandate_arguments",
                "{=BC_PolicyDelib_PersuasionContinue}You have not convinced me yet. What else can you say?", null, null, 100, null);
        }
        private TextObject MandateMotive()
        {
            if (ConversationPledge?.Committed == true) return new TextObject("{=BC_MandateMotivePromise}I gave you my word on this vote, and I mean to honor it.");
            var a = MandateAgenda(_mandateConversation);
            var faction = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(_mandateSpeaker?.Clan);
            if (_mandateSpeaker?.Clan == a?.Realm.RulingClan && a.Realm.RulingClan != Clan.PlayerClan && TitleFactionFavored(a.Realm, a.Faction.Type))
                return new TextObject("{=BC_MandateMotiveCrown}I have chosen to work with the houses bringing this proposal. Their counsel carries weight with me.");
            if (_mandateSpeaker?.Clan != a?.Realm.RulingClan && faction?.Type == FactionType.Nobility)
                return new TextObject("{=BC_MandateMotiveNobility}The Crown needs time to govern. Frequent contests unsettle the great houses and the obligations that bind us.");
            if (_mandateSpeaker?.Clan != a?.Realm.RulingClan && faction?.Type == FactionType.Liberty)
                return new TextObject("{=BC_MandateMotiveLiberty}A ruler should return before the electors regularly. No house should forget that the Crown is held in trust.");
            if ((_mandateSpeaker?.GetTraitLevel(DefaultTraits.Honor) ?? 0) > 0)
                return new TextObject("{=BC_MandateMotiveCustom}Our existing settlement deserves respect. I would need good reason to disturb the customs under which the Crown is held.");
            int relation = a?.Sponsor?.Leader == null ? 0 : _mandateSpeaker.GetRelation(a.Sponsor.Leader);
            if (relation > 50) return new TextObject("{=BC_MandateMotiveTrust}I trust the lord bringing this proposal. That gives me reason to listen, even where I might otherwise favor leaving matters as they are.");
            if (relation < -50) return new TextObject("{=BC_MandateMotiveDistrust}I distrust the lord behind this proposal. I am not eager to rewrite our settlement at their urging.");
            return new TextObject("{=BC_MandateMotiveStability}Unless reform offers a clear advantage, I would leave the present settlement undisturbed.");
        }
        private bool MandatePersuasionClickable(out TextObject hint)
        {
            hint = new TextObject("{=BC_MandateUnavailable}This vote is no longer open to negotiation.");
            if (!MandateConversationLive() || ConversationPledge?.Committed == true) return false;
            if (ConversationPledge?.FailedPersuasion == true)
            { hint = new TextObject("{=BC_PolicyDelib_PersuasionAlreadyFailed}They have already rejected your argument in this matter."); return false; }
            hint = new TextObject("{=BC_PolicyDelib_PersuasionLowRelation}They do not trust you enough to be swayed by argument. Relation required: 30.");
            return Hero.MainHero.GetRelation(_mandateSpeaker) >= 30;
        }
        internal double MandateResistance(string id, Hero speaker, bool reform)
        {
            var a = MandateAgenda(id);
            float yes = a.Mandate.Decision.NaturalSupport(speaker.Clan);
            return CourtMandateRules.Resistance(reform ? yes : 100 - yes);
        }
        internal double MandateBribeOpenness(string id, Hero speaker, bool reform) => CourtMandateRules.Openness(
            MandateResistance(id, speaker, reform), Hero.MainHero.GetRelation(speaker), speaker.GetTraitLevel(DefaultTraits.Honor),
            speaker.GetTraitLevel(DefaultTraits.Mercy), speaker.GetTraitLevel(DefaultTraits.Generosity), speaker.GetTraitLevel(DefaultTraits.Calculating));
        private bool MandateBribeClickable(out TextObject hint)
        {
            hint = new TextObject("{=BC_MandateUnavailable}This vote is no longer open to negotiation.");
            if (!MandateConversationLive() || ConversationPledge?.Committed == true) return false;
            hint = new TextObject("{=BC_PolicyDelib_BribeRefusal}They are not willing to bargain over this vote.");
            return MandateBribeOpenness(_mandateConversation, _mandateSpeaker, _mandateRequested) >= 35;
        }
        private void StartMandatePersuasion()
        {
            if (!MandatePersuasionClickable(out _)) return;
            _mandateArguments.Clear();
            ConversationManager.StartPersuasion(2, 1, 0, 2, 1, 0, PersuasionDifficulty.Medium);
        }
        private PersuasionOptionArgs MandateArgument(int index)
        {
            if (_mandateArguments.TryGetValue(index, out var option)) return option;
            var traits = new[] { DefaultTraits.Honor, DefaultTraits.Mercy, DefaultTraits.Calculating, DefaultTraits.Valor };
            int fit = _mandateSpeaker?.GetTraitLevel(traits[index]) ?? 0;
            if (index == 1) fit = Math.Max(fit, _mandateSpeaker?.GetTraitLevel(DefaultTraits.Generosity) ?? 0);
            double gap = MandateConversationLive() ? MandateResistance(_mandateConversation, _mandateSpeaker, _mandateRequested) : 100;
            int difficulty = (gap > 50 ? 2 : gap > 10 ? 1 : 0) - Math.Sign(fit);
            var strengths = new[] { PersuasionArgumentStrength.ExtremelyEasy, PersuasionArgumentStrength.VeryEasy, PersuasionArgumentStrength.Easy,
                PersuasionArgumentStrength.Normal, PersuasionArgumentStrength.Hard, PersuasionArgumentStrength.VeryHard, PersuasionArgumentStrength.ExtremelyHard };
            option = new PersuasionOptionArgs(DefaultSkills.Charm, traits[index], TraitEffect.Positive,
                strengths[(int)CourtMandateRules.Bound(3 + difficulty, 0, 6)], false, new TextObject(MandateArgumentTexts[index]),
                new[] { Tuple.Create(traits[index], 1) }, false, false, false);
            return _mandateArguments[index] = option;
        }
        private bool MandatePersuasionFailed() => ConversationManager.GetPersuasionIsFailure()
            || ConversationManager.GetPersuasionChosenOptions()?.LastOrDefault()?.Item2 == PersuasionOptionResult.CriticalFailure
            || _mandateArguments.Values.Count(a => a.IsBlocked) >= 4;
        private void EndMandatePersuasion() { ConversationManager.EndPersuasion(); _mandateArguments.Clear(); }
        private void LaunchMandateBarter()
        {
            if (!MandateBribeClickable(out _)) return;
            var item = new MandateVoteBribeBarterable(_mandateConversation, _mandateSpeaker, _mandateRequested);
            BarterManager.Instance.StartBarterOffer(Hero.MainHero, _mandateSpeaker, PartyBase.MainParty, _mandateSpeaker.PartyBelongedTo?.Party,
                null, (Barterable barterable, BarterData data, object obj) => { data.AddBarterable<MandateVoteBribeBarterable>(item); return true; },
                0, false, new Barterable[] { item });
        }
    }
}
