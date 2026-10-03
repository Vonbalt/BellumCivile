using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionCrownTransfer
    {
        internal static bool VerifyTitles(RealmUnionRecord journal, string previousHouseId, string survivingHouseId,
            Func<string, FeudalTitleRecord> resolve, bool transferred, out string reason)
        {
            reason = "Crown title snapshot is missing or inconsistent";
            if (journal?.Titles == null || resolve == null || string.IsNullOrWhiteSpace(previousHouseId)
                || string.IsNullOrWhiteSpace(survivingHouseId) || previousHouseId == survivingHouseId
                || string.IsNullOrWhiteSpace(journal.InheritedCrownId) || string.IsNullOrWhiteSpace(journal.PrimaryCrownId)
                || journal.InheritedCrownId == journal.PrimaryCrownId) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var before in journal.Titles)
                if (before == null || string.IsNullOrWhiteSpace(before.TitleId) || !ids.Add(before.TitleId)) return false;
            var crown = journal.Titles.FirstOrDefault(t => t.TitleId == journal.InheritedCrownId);
            var primary = journal.Titles.FirstOrDefault(t => t.TitleId == journal.PrimaryCrownId);
            if (crown == null || primary == null || crown.Rank != primary.Rank
                || crown.LegalHolderId != previousHouseId || crown.ActualHolderId != previousHouseId
                || primary.LegalHolderId != survivingHouseId || primary.ActualHolderId != survivingHouseId) return false;
            foreach (var before in journal.Titles)
            {
                var current = resolve(before.TitleId);
                bool inherited = transferred && before.TitleId == journal.InheritedCrownId;
                if (current?.IsActive != true || current.IsDeliberatelyDissolved || current.TitleId != before.TitleId
                    || current.TitleType != before.Rank
                    || current.DeJureHolderClanId != (inherited ? survivingHouseId : before.LegalHolderId)
                    || current.DeFactoHolderClanId != (inherited ? survivingHouseId : before.ActualHolderId)
                    || current.ParentTitleId != before.LegalParentId || current.DeFactoParentTitleId != before.ActualParentId
                    || current.AssociatedKingdomId != before.OriginRealmId || current.CapitalSettlementId != before.CapitalId)
                { reason = "Crown transfer title state changed: " + before.TitleId; return false; }
            }
            reason = null;
            return true;
        }

        internal static bool Execute(RealmUnionRecord journal, Func<bool> beforeVerified,
            Action transfer, Func<bool> afterVerified, out string reason)
        {
            reason = "invalid Crown transfer receipts";
            if (journal == null || journal.Completed || journal.RetirementStarted
                || beforeVerified == null || transfer == null || afterVerified == null
                || journal.CrownTransferReturned && !journal.CrownTransferStarted
                || journal.CrownVerified && !journal.CrownTransferReturned) return false;
            try
            {
                if (!journal.CrownTransferStarted)
                {
                    if (!beforeVerified()) { reason = "Crown estate changed before transfer"; return false; }
                    journal.CrownTransferStarted = true;
                    transfer();
                    journal.CrownTransferReturned = true;
                }
                if (!journal.CrownTransferReturned)
                { reason = "Crown transfer was interrupted; title effects will not be replayed"; return false; }
                if (!afterVerified()) { reason = "inherited Crown or retained hierarchy does not verify"; return false; }
                journal.CrownVerified = true;
                reason = null;
                return true;
            }
            catch (Exception ex)
            { reason = "Crown transfer interrupted: " + ex.Message; return false; }
        }
    }
}
