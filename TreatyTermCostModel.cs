using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class TreatyTermCostModel
    {
        public static int GetFiefTransferCost(Settlement settlement, Kingdom recipient, bool occupied)
        {
            if (settlement == null)
                return 0;

            int baseCost = settlement.IsTown ? C.TreatyTownCost : C.TreatyCastleCost;
            FeudalClaimStrength? strongestClaim = GetStrongestRealmClaim(settlement, recipient);
            float claimMultiplier = strongestClaim == FeudalClaimStrength.Strong
                ? 1f - C.TreatyStrongClaimDiscount
                : strongestClaim == FeudalClaimStrength.Weak
                    ? 1f - C.TreatyWeakClaimDiscount
                    : 1f;
            float occupationMultiplier = occupied ? 1f : C.TreatyUnoccupiedFiefCostMultiplier;
            float distanceMultiplier = occupied ? 1f : GetDistanceMultiplier(settlement, recipient);
            int strategicCost = (int)Math.Ceiling(baseCost * occupationMultiplier * distanceMultiplier * claimMultiplier);
            int prosperityCost = GetProsperityWarScoreCost(settlement);
            return Math.Max(1, strategicCost + prosperityCost);
        }

        public static int GetProsperityWarScoreCost(Settlement settlement)
        {
            float prosperity = Math.Max(0f, Math.Min(C.TreatyFiefProsperityCap, settlement?.Town?.Prosperity ?? 0f));
            return (int)Math.Floor(prosperity / C.TreatyFiefProsperityPerWarScore);
        }

        public static int GetOccupiedFiefCost(Settlement settlement, Kingdom winner)
        {
            return GetFiefTransferCost(settlement, winner, occupied: true);
        }

        public static int GetIntrinsicFiefWarScoreCost(Settlement settlement)
        {
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                return 0;

            int baseCost = settlement.IsTown ? C.TreatyTownCost : C.TreatyCastleCost;
            return Math.Max(1, baseCost + GetProsperityWarScoreCost(settlement));
        }

        public static int GetStructuralFiefWarScoreCost(IEnumerable<Settlement> settlements)
        {
            int total = (settlements ?? Enumerable.Empty<Settlement>())
                .Where(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle))
                .Distinct()
                .Sum(GetIntrinsicFiefWarScoreCost);
            return Math.Max(C.TreatyStructuralMinimumCost, total);
        }

        public static int GetProjectedRealmStructuralCost(
            Kingdom realm,
            IEnumerable<TreatyTermRecord> projectedTerms = null)
        {
            if (realm == null)
                return C.TreatyStructuralMinimumCost;

            Dictionary<string, Settlement> projectedFiefs = Settlement.All
                .Where(settlement => settlement != null
                    && (settlement.IsTown || settlement.IsCastle)
                    && settlement.OwnerClan?.Kingdom == realm)
                .GroupBy(settlement => settlement.StringId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (TreatyTermRecord term in projectedTerms ?? Enumerable.Empty<TreatyTermRecord>())
            {
                if (term == null)
                    continue;

                if (term.Type == TreatyTermType.ReleaseVassal && term.FromKingdomId == realm.StringId)
                {
                    FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                    FeudalTitleRecord root = titles?.GetTitle(term.TitleId);
                    if (root == null)
                        continue;

                    foreach (FeudalTitleRecord title in titles.GetTitleAndDescendants(root, FeudalHierarchyMode.DeFacto))
                    {
                        if (title?.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                            projectedFiefs.Remove(title.CapitalSettlementId);
                    }
                    continue;
                }

                if (term.Type != TreatyTermType.TransferFief || string.IsNullOrWhiteSpace(term.SettlementId))
                    continue;

                Settlement settlement = Settlement.All.FirstOrDefault(candidate => candidate?.StringId == term.SettlementId);
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                    continue;

                if (term.FromKingdomId == realm.StringId)
                    projectedFiefs.Remove(settlement.StringId);
                if (term.ToKingdomId == realm.StringId)
                    projectedFiefs[settlement.StringId] = settlement;
            }

            return GetStructuralFiefWarScoreCost(projectedFiefs.Values);
        }

        public static int GetClientReleaseCost(Kingdom clientRealm)
        {
            int structuralCost = GetProjectedRealmStructuralCost(clientRealm);
            return Math.Max(C.TreatyClientReleaseMinimumCost,
                (int)Math.Ceiling(structuralCost * C.TreatyClientReleaseCostMultiplier));
        }

        public static int GetEnforceRebelDemandsCost(FactionObject faction, Kingdom rebelRealm, WarScoreRecord civilWar)
        {
            float rebelScore = civilWar?.GetSelfRelativeScore(rebelRealm?.StringId) ?? 0f;
            int remainingCost = (int)Math.Ceiling(
                Math.Max(0f, C.WarScoreForcePeaceThreshold - rebelScore)
                * C.TreatyEnforceRebelRemainingScoreMultiplier);
            int minimum = C.TreatyEnforceRebelMinimumCost;
            if (faction?.Type == FactionType.Independence && rebelRealm != null)
                minimum = Math.Max(minimum, GetClientReleaseCost(rebelRealm));
            return Math.Max(minimum, Math.Min((int)C.WarScoreForcePeaceThreshold, remainingCost));
        }

        public static int GetReparationsForWarScore(int warScore)
        {
            return Math.Max(0, warScore) * BellumCivileOptions.TreatyReparationsGoldPerWarScore;
        }

        public static int GetDailyTributeForWarScore(int warScore)
        {
            return Math.Max(0, warScore) * BellumCivileOptions.TreatyDailyTributePerWarScore;
        }

        public static int GetPrisonerReleaseCost(Hero hero)
        {
            return GetPrisonerReleaseCost(hero, hero?.Clan == null || hero.Clan.Kingdom?.Leader == hero
                ? null : TreatyDraftReadScope.GetHeir(hero.Clan.Kingdom));
        }

        internal static int GetPrisonerReleaseCost(Hero hero, Hero recognizedHeir)
        {
            if (hero?.Clan == null)
                return 0;

            int cost = C.TreatyPrisonerBaseCost;
            if (hero.Clan.Leader == hero)
                cost += C.TreatyPrisonerClanLeaderSurcharge;

            Kingdom kingdom = hero.Clan.Kingdom;
            if (kingdom?.Leader == hero)
                cost += C.TreatyPrisonerRulerSurcharge;
            else
            {
                if (recognizedHeir == hero)
                    cost += C.TreatyPrisonerHeirSurcharge;
            }

            return Math.Max(1, cost);
        }

        private static FeudalClaimStrength? GetStrongestRealmClaim(Settlement settlement, Kingdom kingdom)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || kingdom?.Clans == null || !titles.TryGetBarony(settlement, out FeudalTitleRecord title))
                return null;

            if (kingdom.Clans.Any(clan => titles.HasActiveClaim(clan, title, FeudalClaimStrength.Strong)))
                return FeudalClaimStrength.Strong;
            if (kingdom.Clans.Any(clan => titles.HasActiveClaim(clan, title, FeudalClaimStrength.Weak)))
                return FeudalClaimStrength.Weak;
            return null;
        }

        private static float GetDistanceMultiplier(Settlement target, Kingdom recipient)
        {
            if (target == null || recipient == null || Campaign.Current == null)
                return 1f;

            float nearest = Settlement.All
                .Where(settlement => settlement != null
                    && settlement != target
                    && settlement.OwnerClan?.Kingdom == recipient
                    && (settlement.IsTown || settlement.IsCastle))
                .Select(settlement => Campaign.Current.Models.MapDistanceModel.GetDistance(
                    target,
                    settlement,
                    isFromPort: false,
                    isTargetingPort: false,
                    MobileParty.NavigationType.All))
                .DefaultIfEmpty(0f)
                .Min();
            float stepDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            if (nearest <= 0f || stepDistance <= 0f)
                return 1f;

            int extraSteps = Math.Max(0, (int)Math.Ceiling(nearest / stepDistance) - 1);
            float surcharge = Math.Min(C.TreatyDistantFiefCostMaximum, extraSteps * C.TreatyDistantFiefCostPerStep);
            return 1f + surcharge;
        }
    }
}
