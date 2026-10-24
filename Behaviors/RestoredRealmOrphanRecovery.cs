using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private void RetireOrphanedRestoredRealms()
        {
            foreach (var realm in Kingdom.All.Where(IsOrphanedRestoredRealm).ToList())
            {
                var successor = FindExistingRestoredSuccessor(realm);
                if (successor == null || !NobleClanEligibilityHelper.IsValidRulingClan(successor.RulingClan, successor)) continue;
                if (DrainAndDestroyRebelKingdom(realm, successor))
                    BellumCivileLogger.Log($"Retired orphaned empty restored realm; realm={realm.StringId}; successor={successor.StringId}.");
            }
        }

        private bool IsOrphanedRestoredRealm(Kingdom realm)
        {
            if (realm == null || realm.IsEliminated || string.IsNullOrEmpty(realm.StringId)
                || !realm.StringId.Contains("_restored") || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || realm.Leader != null
                || CountStrongholds(realm) > 0
                || realm.Clans.Any(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService)
                || Clan.All.Any(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c.Kingdom == realm)) return false;
            if (CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null
                || Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.IsCrownPromotionRealmProtected(realm) == true
                || IsKingdomIdReferencedByBellumCivileState(realm.StringId)) return false;
            // Cross-check actual ownership before acting on an old realm's cached fief list.
            return !Settlement.All.Any(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == realm);
        }
    }
}
