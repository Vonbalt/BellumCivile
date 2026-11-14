using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class DefensivePactWarJustification
    {
        internal static DefensivePactLegalBasis Evaluate(IEnumerable<string> claimantClans, ISet<string> targetClans,
            Func<string, IEnumerable<string>> activeClaims, Func<string, IEnumerable<FeudalTitleRecord>> deJureTitles,
            Func<string, FeudalTitleRecord> findTitle)
        {
            if (claimantClans == null || targetClans == null || targetClans.Count == 0
                || activeClaims == null || deJureTitles == null || findTitle == null) return DefensivePactLegalBasis.Unknown;
            bool hasClaimant = false, missing = false;
            foreach (string clanId in claimantClans)
            {
                if (string.IsNullOrWhiteSpace(clanId)) { missing = true; continue; }
                hasClaimant = true;
                var claims = activeClaims(clanId);
                var titles = deJureTitles(clanId);
                if (claims == null || titles == null) { missing = true; continue; }
                foreach (string titleId in claims)
                {
                    var title = string.IsNullOrWhiteSpace(titleId) ? null : findTitle(titleId);
                    if (title == null) { missing = true; continue; }
                    if (title.IsActive && targetClans.Contains(title.DeFactoHolderClanId)) return DefensivePactLegalBasis.Established;
                }
                foreach (var title in titles)
                {
                    if (title == null) { missing = true; continue; }
                    if (title.IsActive && title.DeJureHolderClanId == clanId && title.DeFactoHolderClanId != clanId
                        && targetClans.Contains(title.DeFactoHolderClanId)) return DefensivePactLegalBasis.Established;
                }
            }
            return hasClaimant && !missing ? DefensivePactLegalBasis.NotEstablished : DefensivePactLegalBasis.Unknown;
        }

        internal static DefensivePactLegalBasis Capture(Kingdom attacker, Kingdom defender, FeudalTitleBehavior titles)
        {
            if (attacker == null || defender == null || attacker == defender || titles == null
                || attacker.Clans == null || defender.Clans == null) return DefensivePactLegalBasis.Unknown;
            var source = attacker.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService
                && !string.IsNullOrWhiteSpace(c.StringId)).ToDictionary(c => c.StringId, c => c, StringComparer.Ordinal);
            var target = new HashSet<string>(defender.Clans.Where(c => c != null && !c.IsEliminated
                && !string.IsNullOrWhiteSpace(c.StringId)).Select(c => c.StringId), StringComparer.Ordinal);
            return Evaluate(source.Keys, target,
                id => titles.GetActiveClaimsByClan(source[id]).Select(claim => claim?.TargetTitleId),
                id => titles.GetTitlesHeldByClan(source[id], deJure: true), titles.GetTitle);
        }
    }
}
