using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionLegacyTributeProjection
    {
        internal static List<ActiveTreatyTributeRecord> Copy(IEnumerable<ActiveTreatyTributeRecord> records) => records
            .Select(t => new ActiveTreatyTributeRecord(t.PayerKingdomId, t.RecipientKingdomId, t.DailyGold, t.RemainingDays)).ToList();

        internal static Dictionary<string, string> Snapshot(IEnumerable<ActiveTreatyTributeRecord> records, string realmId)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var t in records.Where(t => t != null && t.RemainingDays > 0
                && (t.PayerKingdomId == realmId || t.RecipientKingdomId == realmId))
                .OrderBy(t => t.PayerKingdomId, StringComparer.Ordinal).ThenBy(t => t.RecipientKingdomId, StringComparer.Ordinal)
                .ThenBy(t => t.DailyGold).ThenBy(t => t.RemainingDays))
                result.Add("legacy-tribute:" + (index++).ToString(CultureInfo.InvariantCulture),
                    t.PayerKingdomId + "|" + t.RecipientKingdomId + "|" + t.DailyGold.ToString(CultureInfo.InvariantCulture)
                    + "|" + t.RemainingDays.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        private static Dictionary<string, string> Entries(IDictionary<string, string> state) => state
            .Where(p => p.Key.StartsWith("legacy-tribute:", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        internal static bool TryPrepare(RealmUnionRecord journal, out List<ActiveTreatyTributeRecord> replacement, out string reason)
        {
            replacement = null;
            reason = "legacy tribute requires frozen structured schedules";
            if (journal?.SourceObligations == null || journal.DestinationObligations == null
                || journal.Source == null || journal.Destination == null || journal.SourceLegacyTributes == null
                || journal.DestinationLegacyTributes == null) return false;
            string source = journal.Source.StringId, destination = journal.Destination.StringId;
            bool Valid(List<ActiveTreatyTributeRecord> records, string realm) => records.All(t => t != null && t.RemainingDays > 0
                && t.DailyGold > 0 && !string.IsNullOrWhiteSpace(t.PayerKingdomId) && !string.IsNullOrWhiteSpace(t.RecipientKingdomId)
                && t.PayerKingdomId != t.RecipientKingdomId && (t.PayerKingdomId == realm || t.RecipientKingdomId == realm));
            if (!Valid(journal.SourceLegacyTributes, source) || !Valid(journal.DestinationLegacyTributes, destination)
                || !RealmUnionObligationRules.Same(Entries(journal.SourceObligations), Snapshot(journal.SourceLegacyTributes, source))
                || !RealmUnionObligationRules.Same(Entries(journal.DestinationObligations), Snapshot(journal.DestinationLegacyTributes, destination))) return false;
            foreach (var entry in journal.SourceLegacyTributes.Concat(journal.DestinationLegacyTributes))
            {
                string payer = entry.PayerKingdomId == source ? destination : entry.PayerKingdomId;
                string recipient = entry.RecipientKingdomId == source ? destination : entry.RecipientKingdomId;
                string partner = payer == destination ? recipient : payer;
                int sign = payer == destination ? 1 : -1;
                foreach (var obligations in new[] { journal.SourceObligations, journal.DestinationObligations })
                    if (obligations.TryGetValue("tribute:" + partner, out string value)
                        && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) || Math.Sign(rate) != sign))
                    { reason = "native and legacy tribute directions conflict after absorption"; return false; }
            }
            return RealmUnionLegacyTributeAdapter.TryPrepare(journal.SourceLegacyTributes.Concat(journal.DestinationLegacyTributes).ToList(),
                source, destination, partner => journal.SourceObligations.ContainsKey("alliance:" + partner)
                    || journal.DestinationObligations.ContainsKey("alliance:" + partner)
                    || journal.SourceObligations.ContainsKey("war:" + partner) || journal.DestinationObligations.ContainsKey("war:" + partner),
                out replacement, out reason);
        }

        internal static bool TryProject(RealmUnionRecord journal, IDictionary<string, string> source, IDictionary<string, string> destination,
            out Dictionary<string, string> projectedSource, out Dictionary<string, string> projectedDestination, out string reason)
        {
            projectedSource = projectedDestination = null;
            reason = "legacy tribute receipts are incomplete";
            if (journal.LegacyTributeTransferStarted != journal.LegacyTributeTransferReturned) return false;
            var nextSource = new Dictionary<string, string>(source, StringComparer.Ordinal);
            var nextDestination = new Dictionary<string, string>(destination, StringComparer.Ordinal);
            if (journal.LegacyTributeTransferReturned)
            {
                if (source.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal) || k.StartsWith("alliance:", StringComparison.Ordinal)
                    || RealmUnionTributeProjection.IsTributeKey(k)))
                { reason = "legacy schedules must follow the other agreement and tribute transfers"; return false; }
                if (!TryPrepare(journal, out var records, out reason)) return false;
                foreach (string key in Entries(nextSource).Keys) nextSource.Remove(key);
                foreach (string key in Entries(nextDestination).Keys) nextDestination.Remove(key);
                foreach (var item in Snapshot(records, journal.Destination.StringId)) nextDestination.Add(item.Key, item.Value);
            }
            projectedSource = nextSource;
            projectedDestination = nextDestination;
            reason = null;
            return true;
        }
    }
}
