using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using BellumCivile.Behaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(DestroyKingdomAction), "ApplyInternal")]
    internal static class CivilWarParentDestructionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom destroyedKingdom, bool isKingdomLeaderDeath)
        {
            // A saved inheritance transfer must be resolved before either shell is retired,
            // including when a death occurs between native creation/movement callbacks.
            if (Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?
                .IsCrownPromotionRealmProtected(destroyedKingdom) == true) return false;
            var accession = CrownAccessionBehavior.Instance;
            if (accession?.IsRealmUnionProtected(destroyedKingdom) == true)
                return !isKingdomLeaderDeath && accession.TryConsumeRealmUnionRetirementAuthorization(destroyedKingdom);
            if (isKingdomLeaderDeath) return true;
            try
            {
                return Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?
                    .ReserveCollapseBeforeDestruction(destroyedKingdom) != true;
            }
            catch (System.Exception ex)
            {
                BellumCivileLogger.Log($"Pre-destruction collapse reservation failed; realm={destroyedKingdom?.StringId}; error={ex}");
                return !CivilWarConflictBehavior.IsRealmTransferPending(destroyedKingdom);
            }
        }
    }
}
