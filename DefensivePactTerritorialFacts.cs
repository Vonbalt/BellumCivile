using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal enum DefensivePactTerritorialResult { Unknown, Concession, ReturnedToOwnSide }

    internal static class DefensivePactTerritorialFacts
    {
        // Side membership is captured before settlement because clientage can change while applying terms.
        internal static DefensivePactTerritorialResult Classify(TreatyTermRecord term,
            IEnumerable<WarScoreFiefSnapshotRecord> snapshots, Func<string, string, bool?> sameSide)
        {
            if (term == null || (term.Type != TreatyTermType.TransferFief && term.Type != TreatyTermType.RecognizeClientOccupation)
                || string.IsNullOrWhiteSpace(term.SettlementId) || string.IsNullOrWhiteSpace(term.FromKingdomId)
                || string.IsNullOrWhiteSpace(term.ToKingdomId) || term.FromKingdomId == term.ToKingdomId
                || snapshots == null || sameSide == null) return DefensivePactTerritorialResult.Unknown;
            var matches = snapshots.Where(s => s != null && s.SettlementId == term.SettlementId).Take(2).ToList();
            if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].OwnerKingdomId))
                return DefensivePactTerritorialResult.Unknown;
            string original = matches[0].OwnerKingdomId;
            bool? onSource = original == term.FromKingdomId ? true : sameSide(original, term.FromKingdomId);
            bool? onDestination = original == term.ToKingdomId ? true : sameSide(original, term.ToKingdomId);
            if (!onSource.HasValue || !onDestination.HasValue || onSource == onDestination)
                return DefensivePactTerritorialResult.Unknown;
            return onSource.Value ? DefensivePactTerritorialResult.Concession : DefensivePactTerritorialResult.ReturnedToOwnSide;
        }
    }
}
