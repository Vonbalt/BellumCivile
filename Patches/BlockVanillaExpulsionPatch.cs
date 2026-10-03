using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// Player proposals nominate into the Crown term; NPC proposals are selected by the hub.
    /// Only the deliberation executor may file the actual scheduled expulsion vote.
    /// </summary>
    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    public class BlockVanillaExpulsionPatch
    {
        public static bool Prefix(KingdomDecision kingdomDecision, bool ignoreInfluenceCost)
        {
            var expelDecision = kingdomDecision as ExpelClanFromKingdomDecision;
            if (expelDecision == null) return true;

            if (IdeologyBehavior.IsModAddingDecision) return true;

            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null) return false;

            if (expelDecision.Kingdom != playerKingdom) return false;

            Clan targetClan = expelDecision.ClanToExpel;
            if (targetClan == null) return false;

            var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null) return false;

            Clan proposerClan = expelDecision.ProposerClan ?? playerKingdom.RulingClan;
            if (proposerClan == Clan.PlayerClan)
                ideologyBehavior.TryStartTreasonVote(playerKingdom, targetClan, proposerClan, out _);
            return false;
        }
    }
}
