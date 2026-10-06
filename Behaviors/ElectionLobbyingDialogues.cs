using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectionLobbyingBehavior
    {
        private void AddDialogues(CampaignGameStarter s)
        {
            s.AddPlayerLine("el_entry", "lord_talk_speak_diplomacy_2", "el_response",
                "{=BC_EL_Entry}Whom will your house support to lead us?", Entry, Begin);
            s.AddDialogLine("el_not_head", "el_response", "lord_pretalk",
                "{=BC_EL_NotHead}You should speak to {EL_HEAD}. Our house's voice is theirs to pledge.", NotLeader, null, 120);
            s.AddDialogLine("el_stance", "el_response", "el_choices", "{EL_STANCE}", Stance, null);
            s.AddPlayerLine("el_why", "el_choices", "el_reason", "{=BC_EL_Why}What draws you to that choice?", null, null);
            s.AddDialogLine("el_reason", "el_reason", "el_choices", "{EL_REASON}", Explain, null);
            s.AddPlayerLine("el_propose", "el_choices", "el_candidates_intro",
                "{=BC_EL_Propose}There is someone I would ask you to support.", () => _candidates.Count > 0, null);
            s.AddDialogLine("el_candidates_intro", "el_candidates_intro", "el_candidates",
                "{=BC_EL_Who}Whose cause would you have me consider?", null, null);
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                s.AddPlayerLine("el_candidate_" + i, "el_candidates", "el_candidate_response", "{EL_NAME" + i + "}",
                    () => Slot(slot), () => Select(slot), 110 - i);
            }
            s.AddPlayerLine("el_more", "el_candidates", "el_candidates_intro", "{=BC_EL_More}Another name...",
                () => (_page + 1) * 4 < _candidates.Count, () => _page++);
            s.AddPlayerLine("el_previous", "el_candidates", "el_candidates_intro", "{=BC_EL_Previous}Let us return to those other names.",
                () => _page > 0, () => _page--);
            s.AddDialogLine("el_candidate_response", "el_candidate_response", "el_actions",
                "{=BC_EL_Consider}I will hear what you have to say.", null, null);
            s.AddPlayerLine("el_persuade", "el_actions", "el_persuasion_intro",
                "{=BC_EL_Persuade}Let me explain why this would be the right choice.", null, StartPersuasion, 110, Persuadable);
            s.AddPlayerLine("el_bribe", "el_actions", "el_barter_intro",
                "{=BC_EL_Bribe}Your support would not go unrewarded. Shall we discuss terms?", null, null, 100, Bribable);
            s.AddDialogLine("el_barter_intro", "el_barter_intro", "el_barter_offer",
                "{=BC_EL_BarterIntro}I am listening. What are you prepared to offer?", null, null);
            s.AddPlayerLine("el_barter_offer", "el_barter_offer", "el_barter_result",
                "{=BC_EL_Offer}Here is my offer.", null, LaunchBarter, 110, Bribable);
            s.AddDialogLine("el_barter_success", "el_barter_result", "lord_pretalk", "{EL_SUCCESS}",
                () => BribeCommitted && SuccessText(), null, 110);
            s.AddDialogLine("el_barter_cancel", "el_barter_result", "el_actions",
                "{=BC_EL_NoDeal}We have made no agreement. My voice remains my own.", null, null);
            s.AddDialogLine("el_persuasion_intro", "el_persuasion_intro", "el_arguments",
                "{=BC_EL_HearArguments}Speak, then. Why should I lend my voice to this cause?", null, null);
            for (int i = 0; i < 6; i++)
            {
                int argument = i;
                s.AddPlayerLine("el_argument_" + i, "el_arguments", "el_result", "{EL_ARG" + i + "}",
                    () => { MBTextManager.SetTextVariable("EL_ARG" + argument, ArgumentText(argument)); return true; },
                    () => UseArgument(argument), 120 - i,
                    (out TextObject hint) => ArgumentAllowed(argument, out hint), () => Option(argument));
            }
            s.AddDialogLine("el_changed", "el_result", "lord_pretalk",
                "{=BC_EL_Changed}Circumstances have changed. We must leave this matter for now.",
                () => !_persuading || !Ready(), () => EndPersuasion(false), 130);
            s.AddDialogLine("el_success", "el_result", "lord_pretalk", "{EL_SUCCESS}",
                () => ConversationManager.GetPersuasionProgressSatisfied() && SuccessText(), () => EndPersuasion(true), 120);
            s.AddDialogLine("el_failed", "el_result", "el_actions",
                "{=BC_EL_Failed}I have heard enough. Your arguments have not changed my judgment.", Failed, () => EndPersuasion(false), 110);
            s.AddDialogLine("el_continue", "el_result", "el_arguments",
                "{=BC_EL_Continue}I am not persuaded yet. What else would you say?", null, null);
            foreach (string token in new[] { "el_choices", "el_candidates", "el_actions", "el_barter_offer", "el_arguments" })
                s.AddPlayerLine(token + "_leave", token, "lord_pretalk",
                    "{=BC_EL_Leave}Let us leave this matter for now.", null, () => EndPersuasion(false), 50);
        }

        private bool SuccessText()
        {
            MBTextManager.SetTextVariable("EL_SUCCESS", new TextObject(_candidate == Hero.MainHero
                ? "{=BC_EL_SuccessSelf}Very well. You shall have my house's voice until the court next weighs its choice, should the election not settle it sooner. Beyond that, I make no promise."
                : "{=BC_EL_SuccessOther}Very well. My house will support {EL_CANDIDATE} until the court next weighs its choice, should the election not settle it sooner. Beyond that, I make no promise."));
            return true;
        }

        private TextObject ArgumentText(int argument)
        {
            bool self = _candidate == Hero.MainHero;
            switch (argument)
            {
                case 0: return new TextObject(self
                    ? "{=BC_EL_LawSelf}I will uphold the laws and customs of this realm. You know the worth of an oath."
                    : "{=BC_EL_LawOther}{EL_CANDIDATE} will uphold our laws and customs. You know the worth of an oath.");
                case 1: return new TextObject(self
                    ? "{=BC_EL_FairSelf}I will deal fairly with all, and protect the common folk from those who would trample their rights."
                    : "{=BC_EL_FairOther}{EL_CANDIDATE} will deal fairly with all, and protect the common folk from those who would trample their rights.");
                case 2: return new TextObject(self
                    ? "{=BC_EL_NobleSelf}Under my rule, your house's ancient rights will be respected. No crown should diminish the nobility that sustains it."
                    : "{=BC_EL_NobleOther}{EL_CANDIDATE} will respect your house's ancient rights. No crown should diminish the nobility that sustains it.");
                case 3: return new TextObject(self
                    ? "{=BC_EL_UnitySelf}Stand with me, and we can bring these divided houses together. Our enemies must see a realm united."
                    : "{=BC_EL_UnityOther}{EL_CANDIDATE} can bring these divided houses together. Our enemies must see a realm united.");
                case 4: return new TextObject(self
                    ? "{=BC_EL_RewardSelf}I will remember those who stood beside me. A generous ruler knows the worth of loyal friends."
                    : "{=BC_EL_RewardOther}{EL_CANDIDATE} will remember those who stood beside them. A generous ruler knows the worth of loyal friends.");
                default: return new TextObject("{=BC_EL_Trust}You know me. We have more between us than courtly words. Trust my judgment in this.");
            }
        }
    }
}
