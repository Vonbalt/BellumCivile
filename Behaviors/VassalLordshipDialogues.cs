using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private const string LocalWelcome = "{=BC_Lordship_LocalWelcome}Greetings, my {?PLAYER.GENDER}lady{?}lord{\\?}. We have heard that {GRANTOR} has placed our village under your protection. We hope you will be a good {?PLAYER.GENDER}lady{?}lord{\\?} to us.";

        private void AddVassalLordshipDialogues(CampaignGameStarter starter)
        {
            starter.AddDialogLine("bc_vassal_lordship_awarded", "lord_give_oath_go_on_3", "lord_give_conclude",
                "{=BC_Lordship_VassalAward}As a token of my esteem and to help you maintain your men, I grant you the lordship of {VILLAGE} as your fief. Go to them, protect them, and collect your dues.",
                () =>
                {
                    if (_oathGrant == null || _oathGrant.Grantor != Hero.OneToOneConversationHero || _oathGrant.Holder != Hero.MainHero
                        || GetGrant(Hero.MainHero) != _oathGrant || !_oathGrant.IsValid) return false;
                    MBTextManager.SetTextVariable("VILLAGE", _oathGrant.Village.Name);
                    return true;
                }, () => _oathGrant = null, 120);
            starter.AddPlayerLine("bc_vassal_lordship_return", "lord_talk_speak_diplomacy_2", "bc_vassal_lordship_return_warning",
                "{=BC_Lordship_VassalReturn}I wish to return the lordship of {VILLAGE} to your house.",
                () =>
                {
                    var grant = GetGrant(Hero.MainHero);
                    if (grant?.IsVassalGrant != true || !grant.IsValid || grant.GrantingClan.Leader != Hero.OneToOneConversationHero) return false;
                    MBTextManager.SetTextVariable("VILLAGE", grant.Village.Name);
                    return true;
                }, null, 120);
            starter.AddDialogLine("bc_vassal_lordship_return_warning", "bc_vassal_lordship_return_warning", "bc_vassal_lordship_return_choice",
                "{=BC_Lordship_VassalReturnWarning}I bestowed it as a mark of my favor. I shall accept its return, though I cannot help but take it as a slight.",
                () => GetGrant(Hero.MainHero)?.Grantor == Hero.OneToOneConversationHero, null, 120);
            starter.AddDialogLine("bc_vassal_lordship_return_successor_warning", "bc_vassal_lordship_return_warning", "bc_vassal_lordship_return_choice",
                "{=BC_Lordship_VassalReturnHouseWarning}My house bestowed it as a mark of favor. I shall accept its return, though I cannot help but take it as a slight.", null, null, 100);
            starter.AddPlayerLine("bc_vassal_lordship_return_confirm", "bc_vassal_lordship_return_choice", "bc_vassal_lordship_return_result",
                "{=BC_Lordship_VassalReturnConfirm}I am resolved. I return the lordship.", null,
                () => _conversationCompleted = ReturnVassalLordship(Hero.MainHero, Hero.OneToOneConversationHero), 120);
            starter.AddPlayerLine("bc_vassal_lordship_return_cancel", "bc_vassal_lordship_return_choice", "lord_talk_speak_diplomacy_2",
                "{=BC_Lordship_VassalReturnCancel}Then I shall continue to hold it.", null, null, 100);
            starter.AddDialogLine("bc_vassal_lordship_return_done", "bc_vassal_lordship_return_result", "lord_talk_speak_diplomacy_2",
                "{=BC_Lordship_VassalReturnDone}So be it. The lordship returns to my house.", () => _conversationCompleted, null, 120);
            starter.AddDialogLine("bc_vassal_lordship_return_unavailable", "bc_vassal_lordship_return_result", "lord_talk_speak_diplomacy_2",
                "{=BC_Lordship_VassalReturnUnavailable}It seems that grant is no longer yours to return.", () => !_conversationCompleted, null, 100);
            starter.AddDialogLine("bc_vassal_lordship_welcome_known", "start", "lord_start", LocalWelcome,
                () => CanWelcomeLordshipHolder(false), () =>
                {
                    Campaign.Current.ConversationManager.CurrentConversationIsFirst = false;
                    MarkLordshipWelcome();
                }, 105);
            starter.AddDialogLine("bc_vassal_lordship_welcome_unmet", "start", "lord_meet_player_response", LocalWelcome,
                () => CanWelcomeLordshipHolder(true), () =>
                {
                    Campaign.Current.ConversationManager.CurrentConversationIsFirst = true;
                    Hero.OneToOneConversationHero.SetHasMet();
                    MarkLordshipWelcome();
                }, 105);
        }

        private bool CanWelcomeLordshipHolder(bool firstMeeting)
        {
            var npc = Hero.OneToOneConversationHero;
            var grant = GetGrant(Hero.MainHero);
            var context = Campaign.Current?.CurrentConversationContext;
            if (!Ready || grant == null || !grant.IsValid || npc?.IsNotable != true || npc.IsPrisoner || Hero.MainHero.IsPrisoner || npc.Spouse == Hero.MainHero
                || npc.HasMet == firstMeeting || grant.Village != npc.HomeSettlement || Settlement.CurrentSettlement != grant.Village
                || (context != ConversationContext.Default && context != ConversationContext.PartyEncounter)
                || npc.MapFaction?.IsAtWarWith(Hero.MainHero.MapFaction) == true
                || grant.WelcomedNotables?.Contains(npc) == true || PlayerEncounter.Current?.IsJoinedBattle == true) return false;
            StringHelpers.SetCharacterProperties("PLAYER", Hero.MainHero.CharacterObject);
            StringHelpers.SetCharacterProperties("CONVERSATION_NPC", npc.CharacterObject);
            MBTextManager.SetTextVariable("GRANTOR", (grant.Grantor ?? grant.GrantingClan.Leader)?.Name ?? TextObject.GetEmpty());
            return true;
        }

        private void MarkLordshipWelcome()
        {
            var grant = GetGrant(Hero.MainHero);
            var npc = Hero.OneToOneConversationHero;
            if (grant == null || npc == null || !grant.IsValid || npc.HomeSettlement != grant.Village) return;
            grant.WelcomedNotables = grant.WelcomedNotables ?? new List<Hero>();
            if (!grant.WelcomedNotables.Contains(npc)) grant.WelcomedNotables.Add(npc);
        }
    }
}
