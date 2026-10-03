using System;
using BellumCivile;
using HarmonyLib;

internal static class CrownPartitionHouseTransferTests
{
    internal static void Run(Action<bool, string> check)
    {
        var execute = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.CrownPartitionHouseTransfer"), "Execute");
        var house = new CrownPartitionHouseRecord { Transfer = new RealmUnionClanRecord() };
        bool source = true, destination = false, safeRestore = true, restored = false;
        int movements = 0, restorations = 0;
        Action move = () => { movements++; source = false; destination = true; };
        Action restore = () => { restorations++; restored = true; };
        bool Run()
        {
            var args = new object[] { house, (Func<bool>)(() => source), move, (Func<bool>)(() => destination),
                (Func<bool>)(() => safeRestore), restore, (Func<bool>)(() => restored), null };
            bool ok = (bool)execute.Invoke(null, args);
            check(ok || !string.IsNullOrEmpty(args[7] as string), "House-transfer refusal explains pending condition");
            return ok;
        }
        check(Run() && movements == 1 && restorations == 1 && house.Transfer.RestorationCompleted,
            "Normal house transfer restores and verifies once");
        check(Run() && movements == 1 && restorations == 1, "Completed house movement cannot replay or restore balances twice");
        restored = false;
        check(!Run() && restorations == 1, "Changed restored state blocks without overwriting newer values");
        house = new CrownPartitionHouseRecord { Transfer = new RealmUnionClanRecord() };
        check(!Run() && !house.Transfer.ActionStarted, "Changed source membership blocks native join");
        source = true; destination = false;
        move = () => { movements++; destination = true; throw new InvalidOperationException("native callback"); };
        check(!Run() && house.Transfer.ActionStarted && !house.MovementReturned, "Interrupted native join is recorded even if allegiance changed");
        check(!Run() && movements == 2, "Partial native join cannot be repeated or mistaken for completed callbacks");
        house.MovementReturned = true;
        safeRestore = false;
        check(!Run() && house.Transfer.ActionCompleted && !house.Transfer.RestorationStarted,
            "Changed post-move state blocks balance restoration");
        safeRestore = true;
        restore = () => { restorations++; restored = true; throw new InvalidOperationException("banner restore"); };
        check(!Run() && house.Transfer.RestorationStarted && !house.RestorationReturned,
            "Partial restoration keeps incomplete receipt");
        check(!Run() && restorations == 2, "Interrupted restoration cannot apply balances again");
        house.RestorationReturned = true;
        check(Run() && house.Transfer.RestorationCompleted && movements == 2 && restorations == 2,
            "Returned actions recover completion verification without native replay");
        destination = false;
        check(!Run(), "Completed flags cannot substitute for current membership and holdings");
        for (int flags = 0; flags < 64; flags++)
        {
            bool a = (flags & 1) != 0, returned = (flags & 2) != 0, completed = (flags & 4) != 0;
            bool r = (flags & 8) != 0, rr = (flags & 16) != 0, rc = (flags & 32) != 0;
            if (!(returned && !a || completed && !returned || r && !completed || rr && !r || rc && !rr)) continue;
            house = new CrownPartitionHouseRecord { MovementReturned = returned, RestorationReturned = rr,
                Transfer = new RealmUnionClanRecord { ActionStarted = a, ActionCompleted = completed,
                    RestorationStarted = r, RestorationCompleted = rc } };
            check(!Run(), "Contradictory transfer receipt combination fails closed: " + flags);
        }
        house = new CrownPartitionHouseRecord { Transfer = new RealmUnionClanRecord { EndMercenaryContract = true } };
        check(!Run(), "Crown partition does not move mercenary contracts as vassal houses");
    }
}
