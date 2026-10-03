using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionRetirementRules
    {
        internal static bool VerifyReceipts(RealmUnionRecord journal, out string reason)
        {
            reason = "retirement requires a completed Crown transfer and verified obligations";
            if (journal == null || journal.Completed || journal.SourceRetired
                || !journal.CrownTransferStarted || !journal.CrownTransferReturned || !journal.CrownVerified
                || !journal.ObligationsSettled) return false;
            return VerifyDeliveredClans(journal, out reason);
        }

        internal static bool VerifyDeliveredClans(RealmUnionRecord journal, out string reason)
        {
            reason = "retirement requires complete, unique clan delivery receipts";
            if (journal?.Clans == null || journal.Clans.Count == 0) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in journal.Clans)
            {
                if (receipt?.Clan == null || string.IsNullOrWhiteSpace(receipt.Clan.StringId)
                    || !seen.Add(receipt.Clan.StringId) || !receipt.ActionStarted || !receipt.ActionReturned
                    || !receipt.ActionCompleted || !receipt.PostMoveCaptured
                    || receipt.Holdings == null || receipt.Holdings.Any(string.IsNullOrWhiteSpace)
                    || receipt.Holdings.Distinct(StringComparer.Ordinal).Count() != receipt.Holdings.Count
                    || float.IsNaN(receipt.Influence) || float.IsInfinity(receipt.Influence)
                    || float.IsNaN(receipt.PostMoveInfluence) || float.IsInfinity(receipt.PostMoveInfluence)) return false;
                if (receipt.EndMercenaryContract)
                {
                    if (receipt.RestorationStarted || receipt.RestorationReturned || receipt.RestorationCompleted) return false;
                }
                else if (!receipt.RestorationStarted || !receipt.RestorationReturned || !receipt.RestorationCompleted) return false;
            }
            if (journal.Clans.All(c => c.EndMercenaryContract)) return false;
            reason = null;
            return true;
        }
    }
}
