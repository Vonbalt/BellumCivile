using System;
using System.Collections.Generic;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class RealmUnionSequenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var execute = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionSequence"), "Execute");
        var record = new CrownAccessionRecord { Union = new RealmUnionRecord() };
        var calls = new List<string>();
        string fail = null, throws = null;
        string reason = null;
        Func<bool> Stage(string name) => () => {
            calls.Add(name);
            if (name == throws) throw new InvalidOperationException("stage failed");
            return name != fail;
        };
        bool Run()
        {
            calls.Clear();
            var args = new object[] { record, Stage("capture"), Stage("agreements"), Stage("crown"), Stage("clans"),
                Stage("retire"), Stage("finish"), null };
            bool ok = (bool)execute.Invoke(null, args);
            reason = args[7] as string;
            check(ok ? reason == null : !string.IsNullOrEmpty(reason), "Sequence reports pending stage or clean success");
            return ok;
        }
        check(Run() && string.Join(",", calls) == "capture,agreements,crown,clans,retire,finish",
            "Union sequence executes capture, obligations, Crown, clans, retirement and completion in order");
        var stages = new[] { "capture", "agreements", "crown", "clans", "retire", "finish" };
        for (int i = 0; i < stages.Length; i++)
        {
            fail = stages[i];
            check(!Run() && calls.Count == i + 1, "Failure stops before later union stages: " + fail);
        }
        fail = null;
        throws = "clans";
        check(!Run() && calls.Count == 4 && reason.Contains("stage failed"), "Interrupted clan delivery cannot fall through to retirement");
        throws = null;
        record.Union.SourceObligations = new Dictionary<string, string>();
        record.Union.DestinationObligations = new Dictionary<string, string>();
        check(Run() && calls[0] == "agreements" && calls.Count == 5, "Captured obligations are not recaptured on resume");
        record.Union.RetirementStarted = true;
        fail = "retire";
        check(!Run() && calls.Count == 1 && calls[0] == "retire", "Started retirement skips earlier writes and requires retirement adapter verification");
        fail = null;
        check(Run() && string.Join(",", calls) == "retire,finish", "Retirement resume cannot replay diplomatic or clan transfers");
        record.Union.SourceRetired = true;
        check(!Run() && calls.Count == 0, "Source-retired flag without native return receipt blocks sequence");
        record.Union.RetirementReturned = true;
        check(Run() && calls.Count == 2, "Verified retirement still goes through current-state validation before finishing");
        record.Union.Completed = true;
        check(Run() && calls.Count == 1 && calls[0] == "finish", "Completed journal routes only outer-record reconciliation");
        record.Union.Completed = false;
        record.Completed = true;
        check(!Run() && calls.Count == 0, "Outer completed flag cannot authorize unfinished union writes");
        var behavior = new CrownAccessionBehavior();
        var nativeArgs = new object[] { record, null };
        check(!(bool)AccessTools.Method(typeof(CrownAccessionBehavior), "TryAdvanceRealmUnion").Invoke(behavior, nativeArgs),
            "Sequence adapter rejects unregistered journal before campaign access");
    }
}
