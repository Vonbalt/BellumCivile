using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionLegacyTributeAdapter
    {
        internal static bool TryPrepare(IReadOnlyList<ActiveTreatyTributeRecord> original, string source, string destination,
            Func<string, bool> conflicts, out List<ActiveTreatyTributeRecord> replacement, out string reason)
        {
            replacement = null;
            reason = "legacy tribute inheritance requires a complete ledger and distinct realms";
            if (original == null || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination)
                || source == destination || conflicts == null) return false;
            var result = new List<ActiveTreatyTributeRecord>(original.Count);
            foreach (var entry in original)
            {
                if (entry == null || entry.RemainingDays <= 0
                    || entry.PayerKingdomId != source && entry.RecipientKingdomId != source)
                { result.Add(entry); continue; }
                string payer = entry.PayerKingdomId == source ? destination : entry.PayerKingdomId;
                string recipient = entry.RecipientKingdomId == source ? destination : entry.RecipientKingdomId;
                string partner = payer == destination ? recipient : payer;
                if (string.IsNullOrWhiteSpace(partner) || payer == recipient || entry.DailyGold <= 0 || conflicts(partner))
                { reason = "legacy tribute has an internal, invalid or incompatible counterparty"; return false; }
                result.Add(new ActiveTreatyTributeRecord(payer, recipient, entry.DailyGold, entry.RemainingDays));
            }
            foreach (var entry in result.Where(t => t != null && t.RemainingDays > 0
                && (t.PayerKingdomId == destination || t.RecipientKingdomId == destination)))
                if (result.Any(other => other != null && other.RemainingDays > 0
                    && other.PayerKingdomId == entry.RecipientKingdomId && other.RecipientKingdomId == entry.PayerKingdomId))
                { reason = "opposing legacy tribute obligations need separate reconciliation"; return false; }
            replacement = result;
            reason = null;
            return true;
        }
    }
}
