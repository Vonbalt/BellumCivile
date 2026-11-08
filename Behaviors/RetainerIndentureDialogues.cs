using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private Hero _indentureRecipient;
        private int _indentureYears;
        private double _indentureExpectedEnd;
        private bool _indentureSigned;

        private void BeginIndentureDialogue()
        {
            _indentureRecipient = Hero.OneToOneConversationHero;
            _indentureYears = 0;
            _indentureSigned = false;
        }

        private bool IndentureDialogueCurrent() => _indentureRecipient == Hero.OneToOneConversationHero
            && CanDiscussIndenture(_indentureRecipient);

        private void SelectIndentureTerm(int years)
        {
            _indentureYears = years;
            _indentureExpectedEnd = GetIndenture(_indentureRecipient)?.EndDay ?? 0;
            MBTextManager.SetTextVariable("BC_INDENTURE_TERM", IndentureTerm(years));
            MBTextManager.SetTextVariable("BC_INDENTURE_FEE", IndentureFee(years));
        }

        private void AddIndentureDialogues(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_indent_offer", "hero_main_options", "bc_indent_response",
                "{=BC_Indenture_DialogueOffer}I would have you serve as a captain of my household.",
                () => CanDiscussIndenture(Hero.OneToOneConversationHero) && GetIndenture(Hero.OneToOneConversationHero) == null,
                BeginIndentureDialogue, 120);
            starter.AddPlayerLine("bc_indent_renew", "hero_main_options", "bc_indent_terms_prompt",
                "{=BC_Indenture_DialogueRenew}Our indenture is nearing its end. I would retain your service.",
                () => CanDiscussIndenture(Hero.OneToOneConversationHero) && GetIndenture(Hero.OneToOneConversationHero) is RetainerIndentureRecord contract
                    && contract.EndDay - CampaignTime.Now.ToDays <= IndentureRenewalDays,
                BeginIndentureDialogue, 120);
            starter.AddDialogLine("bc_indent_response", "bc_indent_response", "bc_indent_explain",
                "{=BC_Indenture_DialogueResponse}What terms do you offer?", null, null, 120);
            starter.AddPlayerLine("bc_indent_landless", "bc_indent_explain", "bc_indent_terms_prompt",
                "{=BC_Indenture_DialogueLandless}I have no lands to grant you for your service. Instead, I would retain you for a fee, with the terms set down in an indenture.",
                () => IndentureDialogueCurrent() && FindVacancy(Clan.PlayerClan) == null, null, 120);
            starter.AddPlayerLine("bc_indent_landed", "bc_indent_explain", "bc_indent_terms_prompt",
                "{=BC_Indenture_DialogueLanded}Rather than grant you an estate, I would retain your services for a fee, with the terms set down in an indenture.",
                () => IndentureDialogueCurrent() && FindVacancy(Clan.PlayerClan) != null, null, 120);
            starter.AddDialogLine("bc_indent_terms_prompt", "bc_indent_terms_prompt", "bc_indent_terms",
                "{=BC_Indenture_DialogueTerms}Name the term and the fee.", IndentureDialogueCurrent, null, 120);
            for (int years = 1; years <= 3; years++)
            {
                int term = years;
                string line = years == 1 ? "{=BC_Indenture_DialogueOne}For one year, at a fee of 5,000 denars."
                    : years == 2 ? "{=BC_Indenture_DialogueTwo}For two years, at a fee of 8,000 denars."
                    : "{=BC_Indenture_DialogueThree}For three years, at a fee of 13,000 denars.";
                starter.AddPlayerLine("bc_indent_term_" + years, "bc_indent_terms", "bc_indent_selected", line,
                    IndentureDialogueCurrent, () => SelectIndentureTerm(term), 120 - years,
                    (out TextObject reason) => CanSignIndenture(_indentureRecipient, term, out reason));
            }
            starter.AddDialogLine("bc_indent_selected", "bc_indent_selected", "bc_indent_confirm",
                "{=BC_Indenture_DialogueSelected}{BC_INDENTURE_TERM}, then, at a fee of {BC_INDENTURE_FEE} denars. You shall have my service until that term is done.",
                IndentureDialogueCurrent, null, 120);
            starter.AddPlayerLine("bc_indent_confirm", "bc_indent_confirm", "bc_indent_result",
                "{=BC_Indenture_DialogueConfirm}Agreed. Here is your fee.", IndentureDialogueCurrent,
                () => _indentureSigned = IndentureDialogueCurrent()
                    && TrySignIndenture(_indentureRecipient, _indentureYears, out _, _indentureExpectedEnd), 120,
                (out TextObject reason) => CanSignIndenture(_indentureRecipient, _indentureYears, out reason));
            starter.AddDialogLine("bc_indent_success", "bc_indent_result", "hero_main_options",
                "{=BC_Indenture_DialogueAccept}Then I pledge you my service. I shall lead your men and serve your household faithfully.",
                () => _indentureSigned, null, 120);
            starter.AddDialogLine("bc_indent_failure", "bc_indent_result", "hero_main_options",
                "{=BC_Indenture_Unavailable}This indenture can no longer be agreed. We must speak of it another time.", null, null, 100);
            foreach (string token in new[] { "bc_indent_explain", "bc_indent_terms", "bc_indent_confirm" })
                starter.AddPlayerLine(token + "_cancel", token, "lord_pretalk",
                    "{=BC_Lordship_DialogueCancel}Let us leave this for another time.", null, null, 90);
            foreach (string token in new[] { "bc_indent_terms_prompt", "bc_indent_selected" })
                starter.AddDialogLine(token + "_stale", token, "hero_main_options",
                    "{=BC_Indenture_Unavailable}This indenture can no longer be agreed. We must speak of it another time.",
                    () => !IndentureDialogueCurrent(), null, 200);
        }
    }
}
