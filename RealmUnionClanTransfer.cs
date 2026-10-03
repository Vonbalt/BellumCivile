using System;

namespace BellumCivile
{
    // Absorption-only executor. Partition transfers retain their own saved return receipts.
    internal static class RealmUnionClanTransfer
    {
        internal static bool Execute(RealmUnionClanRecord receipt, Func<bool> sourceVerified,
            Action moveOrRelease, Func<bool> destinationVerified, Func<bool> restorationSafe,
            Action restore, Func<bool> restoredVerified, out string reason)
        {
            reason = null;
            if (receipt == null || sourceVerified == null || moveOrRelease == null || destinationVerified == null
                || !receipt.EndMercenaryContract && (restorationSafe == null || restore == null || restoredVerified == null)
                || receipt.ActionReturned && !receipt.ActionStarted
                || receipt.ActionCompleted && !receipt.ActionReturned
                || receipt.RestorationStarted && !receipt.ActionCompleted
                || receipt.RestorationReturned && !receipt.RestorationStarted
                || receipt.RestorationCompleted && !receipt.RestorationReturned
                || receipt.EndMercenaryContract && (receipt.RestorationStarted || receipt.RestorationReturned || receipt.RestorationCompleted))
            { reason = "invalid absorption transfer receipts"; return false; }
            try
            {
                if (!receipt.ActionStarted)
                {
                    if (!sourceVerified()) { reason = "source clan changed before absorption"; return false; }
                    if (!receipt.TryBeginAction()) return false;
                    moveOrRelease();
                    receipt.ActionReturned = true;
                }
                if (!receipt.ActionReturned)
                { reason = "native transfer was interrupted; it will not be replayed"; return false; }
                if (!destinationVerified() || !receipt.CompleteAction(true))
                { reason = "destination or retained clan state does not verify"; return false; }
                if (receipt.EndMercenaryContract) return true;
                if (!receipt.RestorationStarted)
                {
                    if (!restorationSafe()) { reason = "post-transfer state changed before restoration"; return false; }
                    if (!receipt.TryBeginRestoration()) return false;
                    restore();
                    receipt.RestorationReturned = true;
                }
                if (!receipt.RestorationReturned)
                { reason = "restoration was interrupted; saved balances will not be reapplied"; return false; }
                if (!restoredVerified() || !receipt.CompleteRestoration(true))
                { reason = "restored clan state does not verify"; return false; }
                return true;
            }
            catch (Exception ex)
            { reason = "absorption transfer interrupted: " + ex.Message; return false; }
        }
    }
}
