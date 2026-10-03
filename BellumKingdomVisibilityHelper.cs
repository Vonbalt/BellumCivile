using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class BellumKingdomVisibilityHelper
    {
        public static bool IsBellumGeneratedKingdom(Kingdom kingdom)
        {
            return kingdom != null && IsBellumGeneratedKingdomId(kingdom.StringId);
        }

        public static bool IsBellumGeneratedKingdomId(string kingdomId)
        {
            return !string.IsNullOrEmpty(kingdomId)
                && (kingdomId.Contains("_rebels_")
                    || kingdomId.StartsWith("bc_feud_")
                    || kingdomId.StartsWith("bc_treaty_release_")
                    || kingdomId.Contains("_indep_")
                    || kingdomId.Contains("_restored"));
        }

        public static bool ShouldUseCultureAdjective(Kingdom kingdom)
        {
            if (kingdom == null)
                return false;

            if (IsBellumGeneratedKingdom(kingdom))
                return true;

            return Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.IsIndependentRealmShell(kingdom) == true;
        }

        public static bool HasBellumSuccessor(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return false;

            string restoredPrefix = kingdom.StringId + "_restored";
            string independentPrefix = kingdom.StringId + "_indep_";

            return Kingdom.All.Any(k => k != null
                                      && k != kingdom
                                      && !k.IsEliminated
                                      && !string.IsNullOrEmpty(k.StringId)
                                      && (k.StringId == restoredPrefix
                                          || k.StringId.StartsWith(restoredPrefix + "_")
                                          || k.StringId.StartsWith(independentPrefix)));
        }

        public static bool IsSupersededEmptyKingdomShell(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !IsBellumGeneratedKingdom(kingdom)
                && HasBellumSuccessor(kingdom)
                && CountStrongholds(kingdom) == 0
                && !kingdom.Clans.Any(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && !c.IsMinorFaction);
        }

        public static bool ShouldHideFromKingdomLists(Kingdom kingdom)
        {
            if (kingdom == null)
                return false;

            if (IsSupersededEmptyKingdomShell(kingdom))
                return true;

            if (IsTemporaryFeudKingdom(kingdom))
                return true;

            if (!kingdom.IsEliminated)
                return false;

            return IsBellumGeneratedKingdom(kingdom) || HasBellumSuccessor(kingdom);
        }

        public static bool ShouldSuppressVanillaDestroyedNotification(Kingdom kingdom)
        {
            return IsTemporaryBellumKingdom(kingdom);
        }

        public static bool IsTemporaryBellumKingdom(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return false;

            return kingdom.StringId.Contains("_rebels_")
                || IsTemporaryFeudKingdom(kingdom);
        }

        public static bool IsTemporaryFeudKingdom(Kingdom kingdom)
        {
            return !string.IsNullOrEmpty(kingdom?.StringId)
                && kingdom.StringId.StartsWith("bc_feud_");
        }

        public static int CountStrongholds(Kingdom kingdom)
        {
            return kingdom?.Fiefs.Count(f => f != null && (f.IsTown || f.IsCastle)) ?? 0;
        }
    }
}
