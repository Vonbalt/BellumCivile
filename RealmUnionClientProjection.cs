using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionClientProjection
    {
        internal static bool TryPlan(RealmUnionRecord journal, out Dictionary<string, string> source,
            out Dictionary<string, string> destination, out string reason)
        {
            source = destination = null;
            reason = "client inheritance needs captured obligations";
            if (journal?.SourceObligations == null || journal.DestinationObligations == null) return false;
            if (!RealmUnionClientSnapshot.Matches(journal.SourceClientRecords, journal.Source?.StringId, journal.SourceObligations)
                || !RealmUnionClientSnapshot.Matches(journal.DestinationClientRecords, journal.Destination?.StringId, journal.DestinationObligations))
            { reason = "client history does not match the captured absorption obligations"; return false; }
            var nextSource = new Dictionary<string, string>(journal.SourceObligations);
            var nextDestination = new Dictionary<string, string>(journal.DestinationObligations);
            if (journal.SourceClientRecords?.Count > 0)
            {
                var all = journal.SourceClientRecords.Concat(journal.DestinationClientRecords ?? new List<ClientKingdomRecord>()).ToList();
                if (!RealmUnionClientAdapter.TryPrepare(all, journal.Source?.StringId, journal.Destination?.StringId,
                    id => nextDestination.ContainsKey("war:" + id), out var replacement, out reason)) return false;
                foreach (var client in journal.SourceClientRecords)
                {
                    string id = client.ClientKingdomId;
                    if (!nextSource.ContainsKey("alliance:" + id) || !nextSource.ContainsKey("trade:" + id))
                    { reason = "inherited client is missing its protected agreements"; return false; }
                    nextSource.Remove("client:" + id);
                    nextSource.Remove("client-start:" + id);
                    nextSource.Remove("client-voluntary:" + id);
                    nextSource.Remove("client-cooldown:" + id);
                }
                if (!RealmUnionClientSnapshot.TryCapture(replacement, journal.Destination.StringId,
                    out _, out var inherited, out reason)) return false;
                foreach (var entry in inherited) nextDestination[entry.Key] = entry.Value;
            }
            source = nextSource;
            destination = nextDestination;
            reason = null;
            return true;
        }

        internal static bool TryProject(RealmUnionRecord journal, out Dictionary<string, string> source,
            out Dictionary<string, string> destination, out string reason)
        {
            source = destination = null;
            reason = "client inheritance receipts are incomplete; registry transfer will not be replayed";
            if (journal == null || journal.ClientTransferStarted != journal.ClientTransferReturned) return false;
            if (!TryPlan(journal, out var plannedSource, out var plannedDestination, out reason)) return false;
            bool hasClients = journal.SourceClientRecords?.Count > 0;
            if (journal.ClientTransferReturned && !hasClients)
            { reason = "client transfer receipt has no captured source clients"; return false; }
            if (hasClients && !journal.ClientTransferReturned && (journal.TradeTransferStarted || journal.AllianceTransferStarted
                || journal.LegacyTributeTransferStarted || journal.TributeTransfersStarted?.Count > 0 || journal.CrownTransferStarted
                || journal.Clans?.Any(c => c?.ActionStarted == true) == true))
            { reason = "client registry inheritance must precede other absorption transfers"; return false; }
            source = journal.ClientTransferReturned ? plannedSource : new Dictionary<string, string>(journal.SourceObligations);
            destination = journal.ClientTransferReturned ? plannedDestination : new Dictionary<string, string>(journal.DestinationObligations);
            return true;
        }
    }
}
