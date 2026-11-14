using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    // Callers supply either draft projections or confirmed delivery facts; this class never changes campaign state.
    internal static class DefensivePactTreatyCalculator
    {
        internal static IReadOnlyDictionary<string, DefensivePactTreatyThreat> Calculate(
            IEnumerable<TreatyTermRecord> terms,
            Func<TreatyTermRecord, bool> isEffective,
            Func<TreatyTermRecord, bool> isTerritorialConcession,
            Func<TreatyTermRecord, bool> hasClaimOrDeJure)
        {
            if (isEffective == null) throw new ArgumentNullException(nameof(isEffective));
            if (isTerritorialConcession == null) throw new ArgumentNullException(nameof(isTerritorialConcession));
            if (hasClaimOrDeJure == null) throw new ArgumentNullException(nameof(hasClaimOrDeJure));
            var result = new Dictionary<string, DefensivePactTreatyThreat>(StringComparer.Ordinal);
            var list = (terms ?? Enumerable.Empty<TreatyTermRecord>()).Where(t => t != null).ToList();
            if (list.Any(t => t.Type == TreatyTermType.WhitePeace)) return result;
            var countedFiefs = new HashSet<string>(StringComparer.Ordinal);
            DefensivePactTreatyThreat For(string realmId)
            {
                if (!result.TryGetValue(realmId, out var impact)) result[realmId] = impact = new DefensivePactTreatyThreat();
                return impact;
            }
            foreach (var term in list)
            {
                if (term.WarScoreCost <= 0 || string.IsNullOrWhiteSpace(term.FromKingdomId)
                    || string.IsNullOrWhiteSpace(term.ToKingdomId) || term.FromKingdomId == term.ToKingdomId
                    || !isEffective(term)) continue;

                bool fief = term.Type == TreatyTermType.TransferFief || term.Type == TreatyTermType.RecognizeClientOccupation;
                if (fief)
                {
                    if (string.IsNullOrWhiteSpace(term.SettlementId) || countedFiefs.Contains(term.SettlementId)
                        || !isTerritorialConcession(term)) continue;
                    bool legitimate = hasClaimOrDeJure(term);
                    countedFiefs.Add(term.SettlementId);
                    For(term.ToKingdomId).GainFief(term.WarScoreCost, legitimate);
                    For(term.FromKingdomId).LoseFief(term.WarScoreCost);
                }
                else if (!term.WasVoluntaryOffering && (term.Type == TreatyTermType.ForceVassalization
                    || term.Type == TreatyTermType.MakeClientKingdom || term.Type == TreatyTermType.ArrangeRoyalMarriage))
                    For(term.ToKingdomId).GainForcedTerm(term.WarScoreCost);
            }
            return result;
        }
    }
}
