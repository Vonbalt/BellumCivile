using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class FeudalTitleUsurpationAssessment
    {
        public bool CanUsurp { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool HasClaim { get; set; }
        public bool IsLawfulAssumption { get; set; }
        public float ControlShare { get; set; }
        public int ControlledTitles { get; set; }
        public int RequiredTitles { get; set; }
        public int TotalTitles { get; set; }
        public int GoldCost { get; set; }
        public float InfluenceCost { get; set; }
        public IReadOnlyList<FeudalTitleRecord> ControlUnits { get; set; }
            = Array.Empty<FeudalTitleRecord>();
    }

    /// <summary>
    /// The single authority for title-usurpation eligibility. Upper titles are assessed against
    /// their actual direct de jure branches, even when drift has left an irregular hierarchy.
    /// </summary>
    public static class FeudalTitleUsurpationAssessmentService
    {
        public static FeudalTitleUsurpationAssessment Evaluate(
            FeudalTitleBehavior titleBehavior,
            Clan claimant,
            FeudalTitleRecord title,
            bool checkResources,
            int minimumGoldReserve = 0)
        {
            FeudalTitleUsurpationAssessment result = new FeudalTitleUsurpationAssessment();
            if (titleBehavior == null)
            {
                result.Reason = "title behavior unavailable";
                return result;
            }

            if (claimant?.Leader == null || claimant.Leader.IsDead)
            {
                result.Reason = "claimant clan has no living leader";
                return result;
            }

            if (title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                result.Reason = "title is missing or inactive";
                return result;
            }

            if (titleBehavior.IsCurrentRealmSovereignTitle(claimant, title))
            {
                result.Reason = "the realm's sovereign title must be contested through succession or rebellion";
                return result;
            }

            result.GoldCost = titleBehavior.GetTitleFormationGoldCost(title.TitleType);
            result.InfluenceCost = titleBehavior.GetTitleFormationInfluenceCost(title.TitleType);
            result.ControlUnits = GetControlUnits(titleBehavior, title);
            result.TotalTitles = result.ControlUnits.Count;
            result.ControlledTitles = result.ControlUnits.Count(unit => IsControlUnitHeldByClaimant(titleBehavior, unit, claimant));
            result.RequiredTitles = title.TitleType == FeudalTitleType.Barony
                ? 1
                : GetStrictMajorityCount(result.TotalTitles);
            result.ControlShare = result.TotalTitles > 0
                ? result.ControlledTitles / (float)result.TotalTitles
                : 0f;

            bool isLawfulHolder = string.Equals(title.DeJureHolderClanId, claimant.StringId, StringComparison.Ordinal);
            result.IsLawfulAssumption = isLawfulHolder && title.TitleType > FeudalTitleType.Barony
                && string.IsNullOrWhiteSpace(title.DeFactoHolderClanId);
            if (isLawfulHolder && !result.IsLawfulAssumption)
            {
                result.Reason = "clan is already the de jure holder";
                return result;
            }

            result.HasClaim = titleBehavior.HasActiveClaim(claimant, title);
            if (!result.HasClaim && !result.IsLawfulAssumption)
            {
                result.Reason = "no weak or strong claim";
                return result;
            }

            if (title.TitleType == FeudalTitleType.Barony)
            {
                // A barony is physical land and must be held by the claimant personally.
                if (!string.Equals(title.DeFactoHolderClanId, claimant.StringId, StringComparison.Ordinal))
                {
                    result.Reason = "barony claimant is not the de facto holder";
                    return result;
                }
            }
            else
            {
                if (result.TotalTitles == 0)
                {
                    result.Reason = "title has no active subordinate titles";
                    return result;
                }

                if (result.ControlledTitles < result.RequiredTitles)
                {
                    result.Reason = "insufficient control of subordinate titles";
                    return result;
                }
            }

            if (checkResources)
            {
                if (claimant.Leader.Gold < result.GoldCost + Math.Max(0, minimumGoldReserve))
                {
                    result.Reason = minimumGoldReserve > 0
                        ? "insufficient gold reserve"
                        : "insufficient gold";
                    return result;
                }

                if (claimant.Influence < result.InfluenceCost)
                {
                    result.Reason = "insufficient influence";
                    return result;
                }
            }

            result.CanUsurp = true;
            return result;
        }

        public static List<FeudalTitleRecord> GetControlUnits(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title)
        {
            if (titleBehavior == null || title == null || !title.IsActive || title.IsDeliberatelyDissolved)
                return new List<FeudalTitleRecord>();

            if (title.TitleType == FeudalTitleType.Barony)
                return new List<FeudalTitleRecord> { title };

            return titleBehavior.GetChildTitles(title, FeudalHierarchyMode.DeJure)
                .Where(child => child != null && child.IsActive)
                .GroupBy(child => child.TitleId)
                .Select(group => group.First())
                .ToList();
        }

        public static bool IsControlUnitHeldByClaimant(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord controlUnit,
            Clan claimant)
        {
            if (titleBehavior == null || controlUnit == null || claimant == null)
                return false;

            if (titleBehavior.IsTitleWithinDeFactoAuthority(controlUnit, claimant))
                return true;

            Clan holder = Clan.All.FirstOrDefault(clan => clan != null
                && string.Equals(clan.StringId, controlUnit.DeFactoHolderClanId, StringComparison.Ordinal));
            return holder != null
                && holder != claimant
                && titleBehavior.IsClanWithinDeFactoAuthority(holder, claimant);
        }

        public static int GetStrictMajorityCount(int totalTitles)
        {
            return totalTitles > 0 ? (totalTitles / 2) + 1 : 0;
        }
    }
}
