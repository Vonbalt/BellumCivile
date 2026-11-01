using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private Settlement _conversationVillage;
        private Hero _conversationRecipient;
        private bool _conversationCompleted;

        private void AddDialogues(CampaignGameStarter starter)
        {
            AddVassalLordshipDialogues(starter);
            starter.AddPlayerLine("bc_lordship_start", "hero_main_options", "bc_lordship_response",
                "{=BC_Lordship_DialogueOffer}You have served my household faithfully. I would entrust a lordship to you.",
                () => Ready && Hero.OneToOneConversationHero?.Clan == Clan.PlayerClan
                    && Villages(Clan.PlayerClan).Any(v => CanGrant(Clan.PlayerClan, v, Hero.OneToOneConversationHero, out _)),
                () => { _conversationRecipient = Hero.OneToOneConversationHero; _conversationVillage = null; _conversationCompleted = false; }, 120);
            starter.AddDialogLine("bc_lordship_response", "bc_lordship_response", "bc_lordship_villages",
                "{=BC_Lordship_DialogueResponse}You honor me. Which lands would you have me hold?", null,
                () => ConversationSentence.SetObjectsToRepeatOver(Villages(Clan.PlayerClan)
                    .Where(v => CanGrant(Clan.PlayerClan, v, _conversationRecipient, out _)).Cast<object>().ToList()), 120);
            starter.AddRepeatablePlayerLine("bc_lordship_village", "bc_lordship_villages", "bc_lordship_selected",
                "{=BC_Lordship_DialogueVillage}The lordship of {VILLAGE}.",
                "{=BC_Lordship_DialogueAnother}I am considering another lordship.", "bc_lordship_response",
                () =>
                {
                    if (!(ConversationSentence.CurrentProcessedRepeatObject is Settlement village)) return false;
                    ConversationSentence.SelectedRepeatLine.SetTextVariable("VILLAGE", village.Name);
                    return true;
                }, () =>
                {
                    _conversationVillage = ConversationSentence.SelectedRepeatObject as Settlement;
                    MBTextManager.SetTextVariable("VILLAGE", _conversationVillage?.Name ?? TextObject.GetEmpty());
                }, 120);
            starter.AddPlayerLine("bc_lordship_cancel", "bc_lordship_villages", "hero_main_options",
                "{=BC_Lordship_DialogueCancel}Let us leave this for another time.", null, null, 100);
            starter.AddDialogLine("bc_lordship_selected", "bc_lordship_selected", "bc_lordship_confirm",
                "{=BC_Lordship_DialogueSelected}{VILLAGE}? I would be honored to hold it in your service.", null, null, 120);
            starter.AddPlayerLine("bc_lordship_confirm", "bc_lordship_confirm", "bc_lordship_result",
                "{=BC_Lordship_DialogueGrant}I grant you the lordship of {VILLAGE}. Its revenues shall be yours, in return for faithful service to my house.",
                null, () =>
                {
                    _conversationCompleted = _conversationRecipient == Hero.OneToOneConversationHero
                        && TryGrant(Clan.PlayerClan, _conversationVillage, _conversationRecipient, out _);
                }, 120);
            starter.AddPlayerLine("bc_lordship_cancel_confirm", "bc_lordship_confirm", "hero_main_options",
                "{=BC_Lordship_DialogueCancel}Let us leave this for another time.", null, null, 100);
            starter.AddDialogLine("bc_lordship_success", "bc_lordship_result", "hero_main_options",
                "{=BC_Lordship_DialogueAccept}You honor me, my {?PLAYER.GENDER}lady{?}lord{\\?}. I will hold {VILLAGE} faithfully in your service.",
                () => _conversationCompleted, null, 120);
            starter.AddDialogLine("bc_lordship_failure", "bc_lordship_result", "hero_main_options",
                "{=BC_Lordship_DialogueUnavailable}It seems that grant can no longer be made. We may speak of it another time.",
                () => !_conversationCompleted, null, 100);
            starter.AddPlayerLine("bc_lordship_revoke_start", "hero_main_options", "bc_lordship_revoke_confirm",
                "{=BC_Lordship_DialogueRevokeOffer}We must speak of the lordship you hold from my household.",
                () => GetGrant(Hero.OneToOneConversationHero)?.GrantingClan == Clan.PlayerClan,
                () =>
                {
                    _conversationRecipient = Hero.OneToOneConversationHero;
                    _conversationVillage = GetGrant(_conversationRecipient)?.Village;
                    MBTextManager.SetTextVariable("VILLAGE", _conversationVillage?.Name ?? TextObject.GetEmpty());
                }, 120);
            starter.AddDialogLine("bc_lordship_revoke_response", "bc_lordship_revoke_confirm", "bc_lordship_revoke_choice",
                "{=BC_Lordship_DialogueRevokeResponse}What is your wish?", null, null, 120);
            starter.AddPlayerLine("bc_lordship_revoke", "bc_lordship_revoke_choice", "bc_lordship_revoke_done",
                "{=BC_Lordship_DialogueRevoke}Your tenure of {VILLAGE} is at an end. Its revenues will return to the household.",
                null, () => _conversationCompleted = _conversationRecipient == Hero.OneToOneConversationHero
                    && TryRevoke(Clan.PlayerClan, _conversationVillage, out _), 120);
            starter.AddPlayerLine("bc_lordship_revoke_cancel", "bc_lordship_revoke_choice", "hero_main_options",
                "{=BC_Lordship_DialogueCancel}Let us leave this for another time.", null, null, 100);
            starter.AddDialogLine("bc_lordship_revoke_done", "bc_lordship_revoke_done", "hero_main_options",
                "{=BC_Lordship_DialogueSurrender}As you command. I surrender the lordship.", () => _conversationCompleted, null, 120);
            starter.AddDialogLine("bc_lordship_revoke_failed", "bc_lordship_revoke_done", "hero_main_options",
                "{=BC_Lordship_DialogueUnavailable}It seems that grant can no longer be made. We may speak of it another time.",
                () => !_conversationCompleted, null, 100);
        }
    }
}
