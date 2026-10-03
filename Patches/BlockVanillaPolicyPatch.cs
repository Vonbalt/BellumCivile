using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To intercept direct player-kingdom policy proposals and route them through the delayed
    /// deliberation flow. Other kingdoms keep their immediate BellumCivile / vanilla behavior,
    /// while player-kingdom policy votes proposed through the UI are delayed for a week.
    /// </summary>
    [HarmonyPatch(typeof(Kingdom), "AddDecision")]
    public class BlockVanillaPolicyPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(KingdomDecision kingdomDecision, bool ignoreInfluenceCost)
        {
            var policyDecision = kingdomDecision as KingdomPolicyDecision;
            if (policyDecision == null)
                return true;

            if (IdeologyBehavior.IsModAddingDecision)
                return true;

            if (CourtAgendaBehavior.Current?.CanPropose(policyDecision.Kingdom, policyDecision.Policy) == false)
                return false;

            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null) return true;

            if (policyDecision.Kingdom != playerKingdom) return true;
            if (policyDecision.ProposerClan != Clan.PlayerClan) return true;
            if (policyDecision.Policy == null) return true;

            var deliberation = Campaign.Current.GetCampaignBehavior<PolicyDeliberationBehavior>();
            if (deliberation == null) return true;

            bool abolish = playerKingdom.ActivePolicies.Contains(policyDecision.Policy);
            bool playerIsRuler = playerKingdom.RulingClan == Clan.PlayerClan
                && CourtAgendaBehavior.Current?.IsNominationOpen(playerKingdom) != true;
            if (!playerIsRuler && !deliberation.CanPlayerUsePolicyMandate(playerKingdom, policyDecision.Policy, abolish))
                return false;

            if (!playerIsRuler
                && deliberation.TryShowPlayerPolicyMandateDeviationConfirmation(
                    playerKingdom,
                    policyDecision.Policy,
                    abolish,
                    () => deliberation.QueuePlayerProposedVote(
                        playerKingdom,
                        policyDecision.Policy,
                        abolish,
                        agendaDeviationConfirmed: true)))
            {
                return false;
            }

            if (deliberation.QueuePlayerProposedVote(
                playerKingdom,
                policyDecision.Policy,
                abolish,
                agendaDeviationConfirmed: true))
                return false;

            if (deliberation.HasPendingVoteForPolicy(playerKingdom, policyDecision.Policy)
                || deliberation.HasPlayerProposedVote(playerKingdom))
                return false;

            return false;
        }
    }
}
