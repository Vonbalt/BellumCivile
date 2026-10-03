using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To restrict the in-game fief revocation button so that only the ruler or the leaders of the realm's
    /// ideological factions may propose a revocation vote, mirroring the BellumCivile Fief Ambition system
    /// where faction mood drives revocation proposals through council meetings.
    /// </summary>
    [HarmonyPatch]
    public class KingdomAnnexButtonPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Settlements.KingdomSettlementVM:GetCanAnnexSettlementWithReason");

        public static void Postfix(ref bool __result, ref TextObject disabledReason)
        {
            if (!__result) return;

            if (Clan.PlayerClan?.Kingdom == null) return;

            bool playerIsRuler = Clan.PlayerClan == Clan.PlayerClan.Kingdom.RulingClan;
            if (playerIsRuler) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            bool playerLeadsFaction = false;
            foreach (FactionObject faction in factionManager.GetFactionsInKingdom(Clan.PlayerClan.Kingdom))
            {
                if (faction.IsIdeology && faction.Leader == Clan.PlayerClan)
                {
                    playerLeadsFaction = true;
                    break;
                }
            }

            if (!playerLeadsFaction)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_UI_AnnexDisabled}Only the ruler or the leaders in court may move to revoke a vassal's lands.");
            }
        }
    }
}
