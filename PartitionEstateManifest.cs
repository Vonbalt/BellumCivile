using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class PartitionEstateManifest
    {
        internal static bool TryCapture(FeudalInheritancePlan plan, Hero primary,
            IReadOnlyList<Hero> secondaryHeirs, out List<CrossClanEstateShare> shares, out string reason)
        {
            shares = null;
            reason = "complete estate plan and distinct beneficiaries required";
            if (plan?.IsValid != true || primary == null || secondaryHeirs == null
                || string.IsNullOrWhiteSpace(primary.StringId)
                || secondaryHeirs.Any(h => h == null || h == primary || string.IsNullOrWhiteSpace(h.StringId))
                || secondaryHeirs.Select(h => h.StringId).Distinct().Count() != secondaryHeirs.Count) return false;
            if (plan.EstateTitles.Any(t => t == null || !t.IsActive || string.IsNullOrWhiteSpace(t.TitleId))
                || plan.EstateTitles.Select(t => t.TitleId).Distinct().Count() != plan.EstateTitles.Count
                || plan.EstateFiefs.Any(f => f?.Settlement == null || string.IsNullOrWhiteSpace(f.Settlement.StringId))
                || plan.EstateFiefs.Select(f => f.Settlement.StringId).Distinct().Count() != plan.EstateFiefs.Count)
            { reason = "estate asset manifest is incomplete or duplicated"; return false; }
            var estateTitles = new HashSet<string>(plan.EstateTitles.Select(t => t.TitleId), StringComparer.Ordinal);
            var estateFiefs = new HashSet<string>(plan.EstateFiefs.Select(f => f.Settlement.StringId), StringComparer.Ordinal);
            var assignedTitles = new HashSet<string>(StringComparer.Ordinal);
            var assignedFiefs = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<CrossClanEstateShare>();
            bool lesserSeen = false;
            int count = Math.Min(secondaryHeirs.Count, plan.SecondaryPackages.Count);
            for (int i = 0; i < count; i++)
            {
                var package = plan.SecondaryPackages[i];
                if (package == null || package.Titles.Any(t => t == null) || package.Fiefs.Any(f => f?.Settlement == null))
                { reason = "invalid secondary package"; return false; }
                if (package.Titles.Select(t => t.TitleId).Distinct().Count() != package.Titles.Count)
                { reason = "duplicated title inside secondary package"; return false; }
                bool crown = package.RootTitle?.TitleType >= FeudalTitleType.Kingdom;
                if (crown && lesserSeen)
                { reason = "Crown-first estate places a sovereign package after lesser shares"; return false; }
                lesserSeen |= !crown;
                var share = new CrossClanEstateShare
                {
                    Heir = secondaryHeirs[i], RootTitleId = package.RootTitle?.TitleId ?? string.Empty,
                    PrimaryFiefId = package.PrimaryFief?.Settlement?.StringId ?? string.Empty,
                    Titles = package.Titles.Select(t => t.TitleId).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                    Fiefs = package.Fiefs.Select(f => f.Settlement.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList()
                };
                // Ordinary landed packages list higher titles separately from physical fiefs.
                // Include owned barony rights, but never invent rights over occupied land.
                share.Titles = share.Titles.Concat(plan.EstateTitles.Where(t => t.TitleType == FeudalTitleType.Barony
                    && share.Fiefs.Contains(t.CapitalSettlementId)).Select(t => t.TitleId))
                    .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (share.Titles.Count == 0 && share.Fiefs.Count == 0
                    || share.Titles.Any(id => !estateTitles.Contains(id) || !assignedTitles.Add(id))
                    || share.Fiefs.Any(id => !estateFiefs.Contains(id) || !assignedFiefs.Add(id))
                    || share.RootTitleId.Length != 0 && !share.Titles.Contains(share.RootTitleId)
                    || share.PrimaryFiefId.Length != 0 && !share.Fiefs.Contains(share.PrimaryFiefId)
                    || plan.PrimarySovereignTitle != null && share.Titles.Contains(plan.PrimarySovereignTitle.TitleId)
                    || plan.PrimaryTitleChain.Any(t => share.Titles.Contains(t.TitleId))
                    || package.Fiefs.Any(f => plan.ReservedPersonalFiefs.Contains(f)))
                { reason = "secondary share overlaps, leaves the estate, or consumes a primary reservation"; return false; }
                result.Add(share);
            }
            result.Insert(0, new CrossClanEstateShare
            {
                Heir = primary, Primary = true, Recipient = plan.ParentClan,
                RootTitleId = plan.PrimarySovereignTitle?.TitleId ?? string.Empty,
                Titles = estateTitles.Except(assignedTitles).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                Fiefs = estateFiefs.Except(assignedFiefs).OrderBy(id => id, StringComparer.Ordinal).ToList()
            });
            shares = result;
            reason = null;
            return true;
        }
    }
}
