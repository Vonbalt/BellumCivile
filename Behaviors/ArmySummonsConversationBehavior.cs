using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Gives the vanilla face-to-face army request an immersive refusal path
    /// and mirrors the personal-friend exception available through the army interface.
    /// </summary>
    public class ArmySummonsConversationBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddDialogLine(
                "bc_army_summons_friend_acceptance",
                "lord_considers_joining_player_army",
                "lord_pretalk",
                "{=BC_Army_DialogueFriendAcceptance}I owe you no allegiance, but for friendship's sake I will ride beneath your banner.",
                IsPersonalFriendAcceptance,
                JoinConversationLordToPlayerArmy,
                200);

            starter.AddDialogLine(
                "bc_army_summons_not_bound_refusal",
                "lord_considers_joining_player_army",
                "lord_pretalk",
                "{=BC_Army_DialogueNotBoundRefusal}You are not my liege, and I am not sworn to your service. I will not answer your summons.",
                IsNotBoundRefusal,
                null,
                200);

            starter.AddDialogLine(
                "bc_army_summons_soured_relations_refusal",
                "lord_considers_joining_player_army",
                "lord_pretalk",
                "{=BC_Army_DialogueSouredRelationsRefusal}Our relations have soured too deeply. I will not place my soldiers under your command.",
                IsSouredRelationsRefusal,
                null,
                200);

            starter.AddDialogLine(
                "bc_army_summons_rebellious_faction_refusal",
                "lord_considers_joining_player_army",
                "lord_pretalk",
                "{=BC_Army_DialogueRebelliousFactionRefusal}My peers and I have no intention of placing our soldiers under your command.",
                IsRebelliousFactionRefusal,
                null,
                200);
        }

        internal static bool ShouldVanillaDeductInfluence()
        {
            if (!TryGetConversationRefusal(out ArmySummonsRefusalKind refusalKind))
                return true;

            return ArmySummonsEligibilityHelper.CanAnswerAsPersonalFriend(
                Hero.MainHero,
                Hero.OneToOneConversationHero,
                refusalKind);
        }

        private static bool IsPersonalFriendAcceptance()
        {
            return TryGetConversationRefusal(out ArmySummonsRefusalKind refusalKind)
                && ArmySummonsEligibilityHelper.CanAnswerAsPersonalFriend(
                    Hero.MainHero,
                    Hero.OneToOneConversationHero,
                    refusalKind);
        }

        private static bool IsNotBoundRefusal()
        {
            return IsRefusalOfKind(ArmySummonsRefusalKind.NotLegalVassal);
        }

        private static bool IsSouredRelationsRefusal()
        {
            return IsRefusalOfKind(ArmySummonsRefusalKind.SouredRelations);
        }

        private static bool IsRebelliousFactionRefusal()
        {
            return IsRefusalOfKind(ArmySummonsRefusalKind.RebelliousFaction);
        }

        private static bool IsRefusalOfKind(ArmySummonsRefusalKind expectedKind)
        {
            if (!TryGetConversationRefusal(out ArmySummonsRefusalKind refusalKind)
                || refusalKind != expectedKind)
            {
                return false;
            }

            return !ArmySummonsEligibilityHelper.CanAnswerAsPersonalFriend(
                Hero.MainHero,
                Hero.OneToOneConversationHero,
                refusalKind);
        }

        private static bool TryGetConversationRefusal(out ArmySummonsRefusalKind refusalKind)
        {
            refusalKind = ArmySummonsRefusalKind.None;
            Clan callerClan = Hero.MainHero?.Clan;
            Clan calledClan = Hero.OneToOneConversationHero?.Clan;
            return ArmySummonsEligibilityHelper.ShouldRefuseCallToArms(
                callerClan,
                calledClan,
                out refusalKind,
                out _);
        }

        private static void JoinConversationLordToPlayerArmy()
        {
            MobileParty targetParty = Hero.OneToOneConversationHero?.PartyBelongedTo;
            Army playerArmy = MobileParty.MainParty?.Army;
            if (targetParty == null || playerArmy == null || targetParty.Army != null)
                return;

            targetParty.Army = playerArmy;
            playerArmy.AddPartyToMergedParties(targetParty);
        }
    }
}
