namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        internal void RefreshLegitimacyClaims() => RequestClaimIndexRebuild();

        internal static bool IsHereditaryClaim(FeudalClaimRecord claim) => claim != null
            && (claim.GenerationDepth > 0 || claim.Source == "partition_bloodright"
                || claim.Source == "partition_inherited_claim" || claim.Source == "inherited_blood_claim"
                || claim.Source == "marriage_birthright" || claim.Source == "royal_heiress_player_marriage");
    }
}
