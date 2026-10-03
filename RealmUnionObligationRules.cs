using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionObligationRules
    {
        internal static bool VerifyRetired(RealmUnionRecord journal, IDictionary<string, string> source,
            IDictionary<string, string> destination, out string reason)
        {
            reason = "retired source retains diplomatic obligations or lacks retirement receipts";
            if (journal?.RetirementStarted != true || !journal.RetirementReturned || source == null || source.Count != 0
                || journal.SourceObligations == null) return false;
            // Native retirement removes the source's war stances. The surviving realm
            // must still match the full pre-retirement projection, including those wars.
            var priorWars = journal.SourceObligations.Where(p => p.Key.StartsWith("war:", StringComparison.Ordinal))
                .ToDictionary(p => p.Key, p => p.Value);
            return VerifyUnchanged(journal, priorWars, destination, out reason);
        }

        internal static bool VerifyUnchanged(RealmUnionRecord journal, IDictionary<string, string> source,
            IDictionary<string, string> destination, out string reason)
        {
            if (!VerifyProgress(journal, source, destination, out reason)) return false;
            string unsupported = source.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .FirstOrDefault(k => !k.StartsWith("war:", StringComparison.Ordinal));
            if (unsupported != null)
            { reason = "source obligation still needs transfer: " + unsupported; return false; }
            var sourceWars = new HashSet<string>(source.Keys.Where(k => k.StartsWith("war:", StringComparison.Ordinal)), StringComparer.Ordinal);
            if (!sourceWars.SetEquals(destination.Keys.Where(k => k.StartsWith("war:", StringComparison.Ordinal))))
            { reason = "foreign wars need alignment before absorption"; return false; }
            reason = null;
            return true;
        }

        internal static bool VerifyProgress(RealmUnionRecord journal, IDictionary<string, string> source,
            IDictionary<string, string> destination, out string reason)
        {
            reason = "absorption obligations have not been captured";
            if (journal?.SourceObligations == null || journal.DestinationObligations == null
                || source == null || destination == null) return false;
            if (!RealmUnionClientProjection.TryProject(journal, out var clientSource, out var clientDestination, out reason)) return false;
            IDictionary<string, string> expectedSource = clientSource;
            IDictionary<string, string> expectedDestination = clientDestination;
            if (journal.TradeTransferReturned && !journal.TradeTransferStarted
                || journal.TradeTransferStarted && !journal.TradeTransferReturned)
            { reason = "trade inheritance receipts are incomplete; native transfer will not be replayed"; return false; }
            if (journal.AllianceTransferReturned && !journal.AllianceTransferStarted
                || journal.AllianceTransferStarted && !journal.AllianceTransferReturned)
            { reason = "alliance inheritance receipts are incomplete; native transfer will not be replayed"; return false; }
            if (journal.AllianceTransferStarted && !journal.TradeTransferReturned
                && journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)))
            { reason = "alliance inheritance cannot precede pending trade inheritance"; return false; }
            if (journal.TradeTransferReturned)
            {
                if (!TryProjectTrade(expectedSource, expectedDestination,
                    out var projectedSource, out var projectedDestination, out reason)) return false;
                expectedSource = projectedSource;
                expectedDestination = projectedDestination;
            }
            if (journal.AllianceTransferReturned)
            {
                if (!TryProjectAlliance(expectedSource, expectedDestination,
                    out var projectedSource, out var projectedDestination, out reason)) return false;
                expectedSource = projectedSource;
                expectedDestination = projectedDestination;
            }
            if (!RealmUnionTributeProjection.TryProject(journal, expectedSource, expectedDestination,
                out var tributeSource, out var tributeDestination, out reason)) return false;
            expectedSource = tributeSource;
            expectedDestination = tributeDestination;
            if (!RealmUnionLegacyTributeProjection.TryProject(journal, expectedSource, expectedDestination,
                out var legacySource, out var legacyDestination, out reason)) return false;
            expectedSource = legacySource;
            expectedDestination = legacyDestination;
            if (!Same(expectedSource, source) || !Same(expectedDestination, destination))
            { reason = "diplomatic obligations changed since absorption capture"; return false; }
            reason = null;
            return true;
        }

        internal static bool TryProjectTrade(IDictionary<string, string> source, IDictionary<string, string> destination,
            out Dictionary<string, string> projectedSource, out Dictionary<string, string> projectedDestination, out string reason)
            => TryProjectTimed(source, destination, "trade:", out projectedSource, out projectedDestination, out reason);

        internal static bool TryProjectAlliance(IDictionary<string, string> source, IDictionary<string, string> destination,
            out Dictionary<string, string> projectedSource, out Dictionary<string, string> projectedDestination, out string reason)
            => TryProjectTimed(source, destination, "alliance:", out projectedSource, out projectedDestination, out reason);

        private static bool TryProjectTimed(IDictionary<string, string> source, IDictionary<string, string> destination, string prefix,
            out Dictionary<string, string> projectedSource, out Dictionary<string, string> projectedDestination, out string reason)
        {
            projectedSource = projectedDestination = null;
            reason = "agreement inheritance needs complete obligation snapshots";
            if (source == null || destination == null || source.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null)
                || destination.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null)) return false;
            string unsupported = source.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .FirstOrDefault(k => !k.StartsWith("war:", StringComparison.Ordinal) && !k.StartsWith("trade:", StringComparison.Ordinal)
                    && !k.StartsWith("alliance:", StringComparison.Ordinal) && !k.StartsWith("legacy-tribute:", StringComparison.Ordinal)
                    && !RealmUnionTributeProjection.IsTributeKey(k));
            if (unsupported != null)
            { reason = "source obligation needs an absorption adapter: " + unsupported; return false; }
            if (!RealmUnionTributeProjection.ValidateLedgers(source, destination, out reason)) return false;
            if (!new HashSet<string>(source.Keys.Where(k => k.StartsWith("war:", StringComparison.Ordinal)))
                .SetEquals(destination.Keys.Where(k => k.StartsWith("war:", StringComparison.Ordinal))))
            { reason = "foreign wars need alignment before agreement inheritance"; return false; }
            var nextSource = new Dictionary<string, string>(source, StringComparer.Ordinal);
            var nextDestination = new Dictionary<string, string>(destination, StringComparer.Ordinal);
            // Validate both kinds before either adapter writes, preventing a known bad
            // alliance from stranding a union after its trade agreements have moved.
            foreach (var agreement in source.Where(p => p.Key.StartsWith("trade:", StringComparison.Ordinal)
                || p.Key.StartsWith("alliance:", StringComparison.Ordinal)))
            {
                string partner = agreement.Key.Substring(agreement.Key.StartsWith("trade:", StringComparison.Ordinal) ? 6 : 9);
                if (string.IsNullOrWhiteSpace(partner) || destination.ContainsKey("war:" + partner)
                    || agreement.Key.StartsWith("alliance:", StringComparison.Ordinal) && destination.ContainsKey("tribute:" + partner)
                    || !TryDate(agreement.Value, out double inherited))
                { reason = "agreement inheritance has an invalid expiry, hostile partner or conflicting tribute"; return false; }
                string expiry = agreement.Value;
                if (destination.TryGetValue(agreement.Key, out string existing))
                {
                    if (!TryDate(existing, out double retained))
                    { reason = "existing destination agreement expiry is invalid"; return false; }
                    if (retained > inherited) expiry = existing;
                }
                if (agreement.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    nextSource.Remove(agreement.Key);
                    nextDestination[agreement.Key] = expiry;
                }
            }
            projectedSource = nextSource;
            projectedDestination = nextDestination;
            reason = null;
            return true;
        }

        private static bool TryDate(string value, out double day) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out day)
                && !double.IsNaN(day) && !double.IsInfinity(day) && day >= 0;

        internal static bool Same(IDictionary<string, string> expected, IDictionary<string, string> actual) =>
            expected.Count == actual.Count && expected.All(p => p.Key != null && p.Value != null
                && actual.TryGetValue(p.Key, out string value) && value == p.Value);
    }
}
