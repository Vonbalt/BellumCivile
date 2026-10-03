using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    internal static class MarriageMatchmakingRules
    {
        // Keep the annual roll singular, but allow a short, year-bounded availability window.
        internal static int WindowDays(int yearDays) => Math.Min(7, Math.Max(3, yearDays / 4));
        internal static bool InWindow(int day, int yearDays, int offset) => day % yearDays >= offset
            && day % yearDays <= Math.Min(yearDays - 1, offset + WindowDays(yearDays));

        internal static float DepartureCost(bool leaves, bool hasFertileCouple, int remainingProspects,
            bool foreignRoyal, float risk)
        {
            if (!leaves) return 0;
            float household = hasFertileCouple ? 0 : remainingProspects == 0
                ? BellumCivileConstants.MarriageEndangeredHouseDepartureCost
                : remainingProspects == 1 ? BellumCivileConstants.MarriageEndangeredHouseDepartureCost * .5f : 0;
            // Reserve weights used to be halved inside the shared diplomatic score.
            float reserve = !foreignRoyal ? 0 : remainingProspects == 0
                ? BellumCivileConstants.MarriageScoreRoyalDynasticReservePenalty * .5f * risk
                : remainingProspects == 1 ? BellumCivileConstants.MarriageScoreRoyalLastSparePenalty * .5f * risk : 0;
            return Math.Max(household, reserve); // One loss, not two overlapping penalties.
        }
    }

    internal static partial class BellumMarriageStrategyHelper
    {
        internal static Kingdom MarriagePoliticalRealm(Clan clan)
        {
            if (clan?.Kingdom == null) return null;
            var campaign = Campaign.Current;
            var parent = campaign?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.GetParentKingdomForTemporaryRealm(clan.Kingdom);
            if (parent != null) return parent;
            var factions = campaign?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factions?.IsClanOnActiveCivilWarRebelSide(clan, out var faction, out _) == true)
                return faction.ParentKingdom ?? clan.Kingdom;
            return clan.Kingdom;
        }

        internal static bool HouseholdMarriageProspect(Hero hero) => hero?.IsAlive == true && hero.IsLord
            && !hero.IsChild && hero.Spouse == null && IsWithinStrategicMarriageAge(hero);

        internal static bool HasHouseholdFertileCouple(Clan clan) => clan?.Heroes.Any(h => h.IsAlive && h.IsLord
            && h.IsFemale && !h.IsChild && h.Age <= 45 && h.Spouse?.IsAlive == true && h.Spouse.Clan == clan) == true;
    }
}
