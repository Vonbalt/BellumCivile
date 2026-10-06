using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To intercept immediate SettlementClaimantDecision additions for the player's kingdom and
    /// block the capture / relinquish flows that BellumCivile deliberately delays by a week.
    /// Preliminary revocation votes are allowed through, and enforced claimant decisions created
    /// by a successful preliminary vote are also allowed through, so the vanilla two-stage fief
    /// revocation chain can finish normally. Mod-added decisions (IsModAddingDecision = true) and
    /// AI kingdoms pass through untouched.
    /// </summary>
    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    public class BlockVanillaFiefVotePatch
    {
        private static int _allowedImmediatePlayerClaimantVotes;

        internal static void AllowNextImmediatePlayerClaimantVote()
        {
            _allowedImmediatePlayerClaimantVotes++;
        }

        public static bool Prefix(KingdomDecision kingdomDecision, bool ignoreInfluenceCost)
        {
            // Temporary war realms leave captured holdings unassigned until settlement.
            // Reject before every bypass, including the vanilla reliability fallback.
            if ((kingdomDecision is SettlementClaimantDecision || kingdomDecision is SettlementClaimantPreliminaryDecision)
                && BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdomDecision.Kingdom))
                return false;

            if (kingdomDecision is SettlementClaimantPreliminaryDecision)
                return true;

            if (!(kingdomDecision is SettlementClaimantDecision claimantDecision))
                return true;

            if (BellumTreatyTransferContext.IsTreatyTransfer)
                return false;

            if (IdeologyBehavior.IsModAddingDecision) return true;

            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null) return true;

            Kingdom decisionKingdom = kingdomDecision.Kingdom;
            if (decisionKingdom == null || decisionKingdom != playerKingdom) return true;

            if (_allowedImmediatePlayerClaimantVotes > 0)
            {
                _allowedImmediatePlayerClaimantVotes--;
                return true;
            }

            FiefDeliberationBehavior deliberation = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            if (deliberation == null)
                return true;

            if (claimantDecision.IsEnforced)
            {
                Settlement settlement = claimantDecision.Settlement;
                Clan proposerClan = claimantDecision.ProposerClan ?? playerKingdom.RulingClan;

                if (settlement == null || proposerClan == null)
                    return true;

                if (deliberation.HasPendingFiefVoteForSettlement(playerKingdom, settlement))
                    return false;

                if (deliberation.QueueRevocationSettlementVote(settlement, proposerClan))
                    return false;

                return true;
            }

            if (deliberation.QueueBlockedClaimantDecision(claimantDecision))
                return false;

            if (!deliberation.HasOpenedAllocation(decisionKingdom, claimantDecision.Settlement))
                BellumCivileLogger.Log(
                    $"Could not replace suppressed claimant decision for {claimantDecision.Settlement?.StringId ?? "null"}; allowing vanilla decision to avoid an unassigned fief.");
            return true;
        }
    }
}
