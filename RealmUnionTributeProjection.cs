using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionTributeProjection
    {
        private static readonly string[] Prefixes = { "tribute:", "tribute-paid:", "tribute-installments:", "tribute-date:" };
        internal static bool IsTributeKey(string key) => key != null && Prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
        internal static List<string> Partners(IDictionary<string, string> obligations) => obligations.Keys
            .Where(k => k.StartsWith("tribute:", StringComparison.Ordinal)).Select(k => k.Substring(8))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        internal static bool ValidateLedgers(IDictionary<string, string> source, IDictionary<string, string> destination, out string reason)
        {
            reason = "native tribute snapshot is incomplete or conflicts with destination commitments";
            if (source == null || destination == null) return false;
            var partners = Partners(source);
            if (source.Keys.Count(IsTributeKey) != partners.Count * Prefixes.Length) return false;
            foreach (string partner in partners)
            {
                if (string.IsNullOrWhiteSpace(partner) || Prefixes.Any(p => !source.ContainsKey(p + partner) || destination.ContainsKey(p + partner))
                    || source.ContainsKey("alliance:" + partner) || destination.ContainsKey("alliance:" + partner)
                    || source.ContainsKey("war:" + partner) || destination.ContainsKey("war:" + partner)
                    || !Integer(source["tribute:" + partner], out int rate) || rate == 0 || rate == int.MinValue
                    || !Integer(source["tribute-paid:" + partner], out int paid) || paid == int.MinValue
                    || !Integer(source["tribute-installments:" + partner], out int count) || count <= 0
                    || !Date(source["tribute-date:" + partner])) return false;
                long total = (long)rate * count, outstanding = total - paid;
                if (total < int.MinValue || total > int.MaxValue || outstanding < int.MinValue || outstanding > int.MaxValue
                    || paid != 0 && Math.Sign(paid) != Math.Sign(rate) || Math.Abs((long)paid) > Math.Abs(total)
                    || outstanding / rate <= 0) return false;
            }
            reason = null;
            return true;
        }

        internal static bool TryProject(RealmUnionRecord journal, IDictionary<string, string> source, IDictionary<string, string> destination,
            out Dictionary<string, string> projectedSource, out Dictionary<string, string> projectedDestination, out string reason)
        {
            projectedSource = projectedDestination = null;
            reason = "native tribute receipts are incomplete or inconsistent";
            if (journal == null || source == null || destination == null) return false;
            var started = journal.TributeTransfersStarted ?? new List<string>();
            var returned = journal.TributeTransfersReturned ?? new List<string>();
            if (started.Any(string.IsNullOrWhiteSpace) || returned.Any(string.IsNullOrWhiteSpace)
                || started.Distinct(StringComparer.Ordinal).Count() != started.Count
                || returned.Distinct(StringComparer.Ordinal).Count() != returned.Count
                || !new HashSet<string>(started, StringComparer.Ordinal).SetEquals(returned)) return false;
            var nextSource = new Dictionary<string, string>(source, StringComparer.Ordinal);
            var nextDestination = new Dictionary<string, string>(destination, StringComparer.Ordinal);
            if (started.Count > 0)
            {
                if (!ValidateLedgers(source, destination, out reason)) return false;
                var partners = Partners(source);
                if (journal.TributeDestinationPeaceDates == null
                    || !new HashSet<string>(partners).SetEquals(journal.TributeDestinationPeaceDates.Keys)
                    || journal.TributeDestinationPeaceDates.Values.Any(v => !Date(v)) || started.Except(partners).Any()
                    || source.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal) || k.StartsWith("alliance:", StringComparison.Ordinal)))
                { reason = "tribute transfer requires its frozen destination dates and completed agreement transfers"; return false; }
                foreach (string partner in returned)
                    foreach (string prefix in Prefixes)
                    {
                        nextSource.Remove(prefix + partner);
                        nextDestination[prefix + partner] = prefix == "tribute-date:"
                            ? journal.TributeDestinationPeaceDates[partner] : source[prefix + partner];
                    }
            }
            projectedSource = nextSource;
            projectedDestination = nextDestination;
            reason = null;
            return true;
        }

        private static bool Integer(string value, out int number) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        private static bool Date(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double day)
            && !double.IsNaN(day) && !double.IsInfinity(day);
    }
}
