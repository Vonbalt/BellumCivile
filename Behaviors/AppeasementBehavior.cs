using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To inject custom dialogue options that allow the player to interact directly with the rebellion systems: either recruiting AI lords into a plot against the crown, or bribing rebels to lay down their arms.
    /// </summary>
    public class AppeasementBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents() => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("recruit_vassal_start", "lord_talk_speak_diplomacy_2", "recruit_vassal_response",
                "{=BC_Appeasement_RecruitStart}I am gathering supporters to challenge the crown. What would it take to secure the loyalty of your family?",
                JoinConversationCondition, null, 100, null, null);

            starter.AddDialogLine("recruit_vassal_in_army", "recruit_vassal_response", "hero_main_options",
                "{=BC_Appeasement_RecruitArmy}I will not discuss such matters with so many prying eyes around us. Seek me out when we are both away from the host.",
                conversation_in_army_condition, null, 100, null);

            starter.AddDialogLine("recruit_vassal_leader", "recruit_vassal_response", "recruit_vassal_barter",
                "{=BC_Appeasement_RecruitLeader}Treason is a dangerous game, my friend. But... every risk has its price. Let us hear what you are offering.",
                conversation_is_leader_condition, null, 100, null);
            starter.AddPlayerLine("recruit_vassal_barter_start", "recruit_vassal_barter", "lord_pretalk", "{=BC_Appeasement_DiscussTerms}Let us discuss terms.", null, LaunchJoinFactionBarter);

            starter.AddDialogLine("recruit_vassal_not_leader", "recruit_vassal_response", "hero_main_options",
                "{=BC_Appeasement_RecruitNotLeader}Such matters of state and allegiance are not mine to decide. I speak only for myself, not for the {CLAN_NAME}. If it is politics you wish to discuss, you must seek out {CLAN_LEADER}, the head of our family.",
                conversation_is_not_leader_condition, null, 100, null);

            starter.AddPlayerLine("appease_vassal_start", "lord_talk_speak_diplomacy_2", "appease_vassal_response",
                "{=BC_Appeasement_AppeaseStart}Your involvement in this rebellion troubles me. What terms can we reach to bring your family back into the fold?",
                AppeaseConversationCondition, null, 100, null, null);

            starter.AddDialogLine("appease_vassal_in_army", "appease_vassal_response", "hero_main_options",
                "{=BC_Appeasement_RecruitArmy}I will not discuss such matters with so many prying eyes around us. Seek me out when we are both away from the host.",
                conversation_in_army_condition, null, 100, null);

            starter.AddDialogLine("appease_vassal_leader", "appease_vassal_response", "appease_vassal_barter",
                "{=BC_Appeasement_AppeaseLeader}Our grievances are many, my liege. But if you are willing to make amends... I am willing to listen.",
                conversation_is_leader_condition, null, 100, null);
            starter.AddPlayerLine("appease_vassal_barter_start", "appease_vassal_barter", "lord_pretalk", "{=BC_Appeasement_DiscussTerms}Let us discuss terms.", null, LaunchAppeasementBarter);

            starter.AddDialogLine("appease_vassal_not_leader", "appease_vassal_response", "hero_main_options",
                "{=BC_Appeasement_AppeaseNotLeader}I cannot abandon our cause nor break our oaths on my own authority. The {CLAN_NAME} stands together. You must direct your offers of appeasement to {CLAN_LEADER}.",
                conversation_is_not_leader_condition, null, 100, null);
        }

        private bool AppeaseConversationCondition()
        {
            Hero target = Hero.OneToOneConversationHero;

            if (target?.Clan?.Kingdom == null || target.Clan.Kingdom != Clan.PlayerClan.Kingdom || target.Clan == Clan.PlayerClan) return false;

            if (Clan.PlayerClan.Kingdom.RulingClan != Clan.PlayerClan) return false;

            return Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetRebelFaction(target.Clan) != null;
        }

        private bool JoinConversationCondition()
        {
            Hero target = Hero.OneToOneConversationHero;

            if (Clan.PlayerClan.Kingdom == null) return false;
            if (target?.Clan?.Kingdom != Clan.PlayerClan.Kingdom || target.Clan == Clan.PlayerClan || target.Clan == Clan.PlayerClan.Kingdom.RulingClan) return false;

            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return false;

            FactionObject playerFaction = manager.GetRebelFaction(Clan.PlayerClan);

            if (playerFaction == null || playerFaction.Leader != Clan.PlayerClan) return false;
            if (playerFaction.Members.Contains(target.Clan)) return false;

            return true;
        }

        private bool conversation_is_leader_condition()
        {
            Hero npc = Hero.OneToOneConversationHero;
            if (npc == null || npc.Clan == null || npc != npc.Clan.Leader) return false;
            return !conversation_in_army_condition();
        }

        private bool conversation_is_not_leader_condition()
        {
            Hero npc = Hero.OneToOneConversationHero;

            if (npc?.Clan == null || npc == npc.Clan.Leader) return false;
            if (conversation_in_army_condition()) return false;

            MBTextManager.SetTextVariable("CLAN_NAME", npc.Clan.Name);
            string leaderName = npc.Clan.Leader != null ? npc.Clan.Leader.Name.ToString() : new TextObject("{=BC_Appeasement_HeadOfClan}the head of our clan").ToString();
            MBTextManager.SetTextVariable("CLAN_LEADER", leaderName);

            return true;
        }

        private bool conversation_in_army_condition()
        {
            Hero target = Hero.OneToOneConversationHero;
            return DeliberationDialogueHelper.IsConversationInArmy(target);
        }

        private void LaunchAppeasementBarter()
        {
            Hero target = Hero.OneToOneConversationHero;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject activeFaction = manager?.GetRebelFaction(target.Clan);

            if (activeFaction != null)
            {
                LeaveFactionBarterable bribeItem = new LeaveFactionBarterable(activeFaction, target, Hero.MainHero);

                BarterManager.Instance.StartBarterOffer(Hero.MainHero, target, PartyBase.MainParty, target.PartyBelongedTo?.Party, null, (Barterable barterable, BarterData args, object obj) =>
                {
                    args.AddBarterable<LeaveFactionBarterable>(bribeItem);
                    
                    foreach (Settlement settlement in Hero.MainHero.Clan.Settlements)
                    {
                        if (settlement.IsTown || settlement.IsCastle)
                        {
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(settlement, Hero.MainHero, target));
                        }
                    }
                    
                    return true;
                }, 0, false, new Barterable[] { bribeItem });
            }
        }

        private void LaunchJoinFactionBarter()
        {
            Hero target = Hero.OneToOneConversationHero;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject playerFaction = manager?.GetRebelFaction(Clan.PlayerClan);

            if (playerFaction != null)
            {
                JoinFactionBarterable bribeItem = new JoinFactionBarterable(playerFaction, target, Hero.MainHero);

                BarterManager.Instance.StartBarterOffer(Hero.MainHero, target, PartyBase.MainParty, target.PartyBelongedTo?.Party, null, (Barterable barterable, BarterData args, object obj) =>
                {
                    args.AddBarterable<JoinFactionBarterable>(bribeItem);
                    
                    foreach (Settlement settlement in Hero.MainHero.Clan.Settlements)
                    {
                        if (settlement.IsTown || settlement.IsCastle)
                        {
                            args.AddBarterable<FiefBarterable>(new FiefBarterable(settlement, Hero.MainHero, target));
                        }
                    }
                    
                    return true;
                }, 0, false, new Barterable[] { bribeItem });
            }
        }
    }
}
