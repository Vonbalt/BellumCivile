using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal enum DefensivePactLegalBasis { Unknown, Established, NotEstablished }

    internal static class DefensivePactLegalBasisRules
    {
        internal static DefensivePactLegalBasis Evaluate(string baronyId, string sovereignId,
            ISet<string> claimIds, Func<string, FeudalTitleRecord> titleById)
        {
            if (string.IsNullOrWhiteSpace(baronyId) || claimIds == null || titleById == null)
                return DefensivePactLegalBasis.Unknown;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string id = baronyId;
            while (!string.IsNullOrWhiteSpace(id))
            {
                if (!visited.Add(id)) return DefensivePactLegalBasis.Unknown;
                var title = titleById(id);
                if (title == null || !title.IsActive) return DefensivePactLegalBasis.Unknown;
                if (claimIds.Contains(id) || (!string.IsNullOrWhiteSpace(sovereignId) && id == sovereignId))
                    return DefensivePactLegalBasis.Established;
                id = title.ParentTitleId;
            }
            return string.IsNullOrWhiteSpace(sovereignId)
                ? DefensivePactLegalBasis.Unknown : DefensivePactLegalBasis.NotEstablished;
        }
    }

    // A preview/application captures this before ownership and claims can change.
    internal sealed class DefensivePactLegalBasisSnapshot
    {
        private readonly FeudalTitleBehavior _titles;
        private readonly string _sovereignId;
        private readonly HashSet<string> _claims;
        private readonly Dictionary<string, DefensivePactLegalBasis> _cache =
            new Dictionary<string, DefensivePactLegalBasis>(StringComparer.Ordinal);

        internal DefensivePactLegalBasisSnapshot(FeudalTitleBehavior titles, Kingdom realm)
        {
            _titles = titles;
            _sovereignId = titles?.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeJure)?.TitleId;
            _claims = new HashSet<string>(StringComparer.Ordinal);
            if (titles == null || realm?.Clans == null) return;
            foreach (var clan in realm.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService))
                foreach (var claim in titles.GetActiveClaimsByClan(clan))
                    if (!string.IsNullOrWhiteSpace(claim?.TargetTitleId)) _claims.Add(claim.TargetTitleId);
        }

        internal DefensivePactLegalBasis For(Settlement fief)
        {
            if (_titles == null || fief == null || string.IsNullOrWhiteSpace(fief.StringId))
                return DefensivePactLegalBasis.Unknown;
            if (_cache.TryGetValue(fief.StringId, out var basis)) return basis;
            basis = _titles.TryGetBarony(fief, out var barony) && barony?.IsActive == true
                ? DefensivePactLegalBasisRules.Evaluate(barony.TitleId, _sovereignId, _claims, _titles.GetTitle)
                : DefensivePactLegalBasis.Unknown;
            _cache[fief.StringId] = basis;
            return basis;
        }
    }
}
