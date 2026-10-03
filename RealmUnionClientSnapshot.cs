using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionClientSnapshot
    {
        internal static bool TryCapture(IEnumerable<ClientKingdomRecord> records, string overlord,
            out List<ClientKingdomRecord> copies, out Dictionary<string, string> entries, out string reason)
        {
            copies = null;
            entries = null;
            reason = "client history is unavailable or invalid";
            if (records == null || string.IsNullOrWhiteSpace(overlord)) return false;
            var result = new List<ClientKingdomRecord>();
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null) return false;
                if (record.SuzerainKingdomId != overlord) continue;
                string id = record.ClientKingdomId;
                if (string.IsNullOrWhiteSpace(id) || id == overlord || snapshot.ContainsKey("client:" + id)
                    || !Finite(record.StartedDay) || !Finite(record.LiberationCooldownUntilDay)) return false;
                result.Add(new ClientKingdomRecord(id, overlord, record.StartedDay,
                    record.WasVoluntary, record.LiberationCooldownUntilDay));
                snapshot.Add("client:" + id, overlord);
                snapshot.Add("client-start:" + id, record.StartedDay.ToString("R", CultureInfo.InvariantCulture));
                snapshot.Add("client-voluntary:" + id, record.WasVoluntary ? "true" : "false");
                snapshot.Add("client-cooldown:" + id, record.LiberationCooldownUntilDay.ToString("R", CultureInfo.InvariantCulture));
            }
            copies = result;
            entries = snapshot;
            reason = null;
            return true;
        }

        internal static bool Matches(IEnumerable<ClientKingdomRecord> records, string overlord,
            IDictionary<string, string> obligations)
        {
            if (obligations == null) return false;
            var entries = obligations.Where(p => IsClientKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            // Older empty journals need no client history migration. Never reconstruct nonempty history.
            if (records == null) return entries.Count == 0;
            var captured = records.ToList();
            if (captured.Any(r => r == null || r.SuzerainKingdomId != overlord)) return false;
            return TryCapture(captured, overlord, out _, out var expected, out _)
                && RealmUnionObligationRules.Same(expected, entries);
        }

        private static bool IsClientKey(string key) => key != null && (key.StartsWith("client:", StringComparison.Ordinal)
            || key.StartsWith("client-start:", StringComparison.Ordinal)
            || key.StartsWith("client-voluntary:", StringComparison.Ordinal)
            || key.StartsWith("client-cooldown:", StringComparison.Ordinal));

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
