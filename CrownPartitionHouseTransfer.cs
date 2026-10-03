using System;

namespace BellumCivile
{
    internal static class CrownPartitionHouseTransfer
    {
        internal static bool Execute(CrownPartitionHouseRecord house, Func<bool> sourceVerified, Action move,
            Func<bool> destinationVerified, Func<bool> restorationSafe, Action restore,
            Func<bool> restoredVerified, out string reason)
        {
            reason = null;
            var receipt = house?.Transfer;
            if (receipt == null || receipt.EndMercenaryContract || sourceVerified == null || move == null
                || destinationVerified == null || restorationSafe == null || restore == null || restoredVerified == null
                || house.MovementReturned && !receipt.ActionStarted
                || receipt.ActionCompleted && !house.MovementReturned
                || receipt.RestorationStarted && !receipt.ActionCompleted
                || house.RestorationReturned && !receipt.RestorationStarted
                || receipt.RestorationCompleted && !house.RestorationReturned)
            { reason = "invalid house transfer receipts"; return false; }
            try
            {
                if (!receipt.ActionStarted)
                {
                    if (!sourceVerified()) { reason = "source house changed or is not safe to move"; return false; }
                    if (!receipt.TryBeginAction()) return false;
                    move();
                    house.MovementReturned = true;
                }
                if (!house.MovementReturned)
                { reason = "house movement was interrupted; native join will not be replayed"; return false; }
                if (!destinationVerified() || !receipt.CompleteAction(true))
                { reason = "house destination or retained holdings do not verify"; return false; }
                if (!receipt.RestorationStarted)
                {
                    if (!restorationSafe()) { reason = "post-move state changed before restoration"; return false; }
                    if (!receipt.TryBeginRestoration()) return false;
                    restore();
                    house.RestorationReturned = true;
                }
                if (!house.RestorationReturned)
                { reason = "house restoration was interrupted; saved balances will not be reapplied"; return false; }
                if (!restoredVerified() || !receipt.CompleteRestoration(true))
                { reason = "restored house state does not verify"; return false; }
                return true;
            }
            catch (Exception ex)
            { reason = "house transfer interrupted: " + ex.Message; return false; }
        }
    }
}
