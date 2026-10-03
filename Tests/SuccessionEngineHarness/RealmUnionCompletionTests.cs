using System;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class RealmUnionCompletionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var execute = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionCompletion"), "Execute");
        var record = new CrownAccessionRecord { Union = new RealmUnionRecord() };
        int cleanups = 0, announcements = 0, verifies = 0;
        bool valid = true, clean = true, throwAnnouncement = false, mutateCleanup = false, mutateAnnouncement = false;
        bool Run()
        {
            Func<bool> verify = () => { verifies++; return valid; };
            Func<bool> cleanup = () => { cleanups++; if (mutateCleanup) valid = false; return clean; };
            Action announce = () => { announcements++; if (mutateAnnouncement) valid = false;
                if (throwAnnouncement) throw new InvalidOperationException("display unavailable"); };
            var args = new object[] { record, verify, cleanup, announce, null };
            bool ok = (bool)execute.Invoke(null, args);
            check(ok || !string.IsNullOrEmpty(args[4] as string), "Incomplete union completion explains its refusal");
            return ok;
        }
        void Ready()
        {
            record = new CrownAccessionRecord { Union = new RealmUnionRecord {
                RetirementStarted = true, RetirementReturned = true, SourceRetired = true } };
            valid = clean = true;
            mutateCleanup = mutateAnnouncement = throwAnnouncement = false;
        }
        check(!Run() && cleanups == 0 && announcements == 0 && verifies == 0,
            "Unretired union cannot run completion side effects");
        Ready();
        valid = false;
        check(!Run() && cleanups == 0, "Stale post-retirement state blocks court cleanup");
        valid = true;
        clean = false;
        check(!Run() && announcements == 0 && !record.Union.Completed, "Incomplete cleanup retains guards without announcing");
        clean = true;
        mutateCleanup = true;
        check(!Run() && announcements == 0, "Cleanup effects are verified before notification");
        valid = true;
        mutateCleanup = false;
        throwAnnouncement = true;
        check(!Run() && record.Union.Announced && !record.Union.Completed && !record.Completed,
            "Notification failure does not release guards or complete accession");
        throwAnnouncement = false;
        check(Run() && announcements == 1 && record.Union.Completed && record.Completed,
            "Retry completes without duplicating a reserved inheritance notification");
        int priorCleanup = cleanups, priorVerify = verifies;
        record.Completed = false;
        check(Run() && record.Completed && cleanups == priorCleanup && verifies == priorVerify && announcements == 1,
            "Completed journal repairs outer flag without replaying side effects");
        Ready();
        record.Completed = true;
        check(!Run(), "Outer completion flag alone cannot authorize unfinished union completion");
        Ready();
        mutateAnnouncement = true;
        check(!Run() && !record.Union.Completed, "State changes during announcement prevent final flag publication");
        Ready();
        record.Union.Completed = true;
        check(!Run(), "Completed union without notification receipt is not silently accepted");
        var native = new CrownAccessionBehavior();
        var call = new object[] { record, null };
        check(!(bool)AccessTools.Method(typeof(CrownAccessionBehavior), "TryFinishRealmUnion").Invoke(native, call),
            "Completion adapter rejects unregistered journals before touching campaign state");
    }
}
