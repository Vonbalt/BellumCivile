using System;

namespace BellumCivile
{
    internal enum RealmUnionResumeStep
    {
        Blocked,
        BeginAction,
        VerifyAction,
        BeginRestoration,
        VerifyRestoration,
        Complete
    }

    // Read-only assessment. Never infer that an interrupted native action is safe
    // to repeat merely because its visible allegiance change has not happened.
    internal static class RealmUnionReconciliation
    {
        internal static RealmUnionResumeStep AssessClan(string source, string destination,
            RealmUnionClanSnapshot original, RealmUnionClanSnapshot current,
            bool actionStarted, bool actionCompleted, bool restorationStarted,
            bool restorationCompleted, bool preservedStateVerified, bool actionReturned,
            bool restorationReturned, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination)
                || source == destination || !Valid(original) || !Valid(current)
                || original.ClanId != current.ClanId || original.RealmId != source)
                return Block("missing or inconsistent clan identity and snapshot", out reason);
            bool mercenary = original.UnderMercenaryContract;
            if (actionReturned && !actionStarted || actionCompleted && !actionReturned
                || restorationStarted && !actionCompleted
                || restorationReturned && !restorationStarted
                || restorationCompleted && !restorationReturned
                || mercenary && (restorationStarted || restorationCompleted || restorationReturned))
                return Block("inconsistent transfer receipts", out reason);

            bool atSource = current.RealmId == source
                && current.UnderMercenaryContract == mercenary;
            bool atTarget = mercenary
                ? string.IsNullOrEmpty(current.RealmId) && !current.UnderMercenaryContract
                : current.RealmId == destination && !current.UnderMercenaryContract;
            if (!actionStarted)
            {
                if (!atSource || current.Influence != original.Influence || current.Debt != original.Debt
                    || !preservedStateVerified)
                    return Block("clan changed since capture; initial action requires revalidation", out reason);
                return RealmUnionResumeStep.BeginAction;
            }
            if (!actionReturned)
                return Block("native transfer did not return; allegiance alone cannot complete it", out reason);
            if (!atTarget)
                return Block("started transfer has no verified destination; reconcile without repeating the native action", out reason);
            if (!actionCompleted) return RealmUnionResumeStep.VerifyAction;
            if (mercenary)
                return preservedStateVerified ? RealmUnionResumeStep.Complete
                    : Block("released company requires preservation verification", out reason);
            if (!restorationStarted) return RealmUnionResumeStep.BeginRestoration;
            if (!restorationReturned)
                return Block("restoration did not return; visible balances cannot complete it", out reason);
            if (!preservedStateVerified || current.Influence != original.Influence || current.Debt != original.Debt)
                return Block("restoration is incomplete or stale; do not overwrite current state on retry", out reason);
            return restorationCompleted ? RealmUnionResumeStep.Complete : RealmUnionResumeStep.VerifyRestoration;
        }

        private static bool Valid(RealmUnionClanSnapshot clan) => clan != null
            && !string.IsNullOrWhiteSpace(clan.ClanId)
            && !float.IsNaN(clan.Influence) && !float.IsInfinity(clan.Influence);

        private static RealmUnionResumeStep Block(string message, out string reason)
        {
            reason = message;
            return RealmUnionResumeStep.Blocked;
        }
    }
}
