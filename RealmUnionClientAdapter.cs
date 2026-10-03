using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionClientAdapter
    {
        internal static bool TryPrepare(IReadOnlyList<ClientKingdomRecord> original, string source, string destination,
            Func<string, bool> conflicts, out List<ClientKingdomRecord> replacement, out string reason)
        {
            replacement = null;
            reason = "client inheritance requires complete records and distinct independent overlords";
            if (original == null || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination)
                || source == destination || conflicts == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in original)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ClientKingdomId) || string.IsNullOrWhiteSpace(record.SuzerainKingdomId)
                    || record.ClientKingdomId == record.SuzerainKingdomId || !ids.Add(record.ClientKingdomId)
                    || record.ClientKingdomId == source || record.ClientKingdomId == destination
                    || float.IsNaN(record.StartedDay) || float.IsInfinity(record.StartedDay)
                    || float.IsNaN(record.LiberationCooldownUntilDay) || float.IsInfinity(record.LiberationCooldownUntilDay)) return false;
            }
            if (original.Any(r => ids.Contains(r.SuzerainKingdomId)))
            { reason = "nested clientage or a clientage cycle requires separate reconciliation"; return false; }
            var result = new List<ClientKingdomRecord>(original.Count);
            foreach (var record in original)
            {
                if (record.SuzerainKingdomId != source) { result.Add(record); continue; }
                if (conflicts(record.ClientKingdomId))
                { reason = "inherited client is unavailable or has incompatible diplomacy"; return false; }
                result.Add(new ClientKingdomRecord(record.ClientKingdomId, destination, record.StartedDay,
                    record.WasVoluntary, record.LiberationCooldownUntilDay));
            }
            replacement = result;
            reason = null;
            return true;
        }
    }
}
