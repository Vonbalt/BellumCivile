namespace BellumCivile
{
    internal static class CrownFavorSelectionRules
    {
        internal const int Cost = 100;
        internal static bool CanSelect(bool playerRuler, bool ownerMatches, int? choice, double expires, double now) =>
            playerRuler && ownerMatches && (choice == -1 || choice == -2) && expires > now;
    }
}
