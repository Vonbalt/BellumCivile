using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To stop vanilla from deleting landless noble clans that Bellum Civile intends to keep alive
    /// in exile long enough to find a valid refuge kingdom.
    /// </summary>
    [HarmonyPatch(typeof(FactionDiscontinuationCampaignBehavior), "DiscontinueClan")]
    public static class ExiledClanDiscontinuationPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Clan clan)
        {
            if (!ExiledClanRecoveryBehavior.IsRecoverableExileCandidate(clan))
                return true;

            ExiledClanRecoveryBehavior recoveryBehavior = Campaign.Current?.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
            if (recoveryBehavior == null)
                return true;

            return !recoveryBehavior.TryPreserveClanFromDiscontinuation(clan);
        }
    }
}
