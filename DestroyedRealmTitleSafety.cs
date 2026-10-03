using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class DestroyedRealmTitleSafety
    {
        public static bool CanVacate(Clan possessor, Kingdom destroyedRealm)
        {
            // Historical realm association alone is not evidence that current possession is stale.
            return possessor != null && destroyedRealm != null && possessor == destroyedRealm.RulingClan
                && (possessor.IsEliminated || possessor.Kingdom == destroyedRealm);
        }
    }
}
