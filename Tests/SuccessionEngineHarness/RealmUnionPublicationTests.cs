using System;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class RealmUnionPublicationTests
{
    internal static void Run(Action<bool, string> check)
    {
        CheckEligibility(check);
        var publish = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionPublication"), "TryPublish");
        var record = new CrownAccessionRecord();
        var draft = new RealmUnionRecord();
        bool accept = false, interrupt = false, write = false, replace = false;
        int calls = 0;
        var other = new RealmUnionRecord();
        bool Run()
        {
            Func<bool> preflight = () => {
                calls++;
                check(record.Union == draft, "Publication preflight can verify registered draft identity");
                if (write) draft.ClientTransferStarted = true;
                if (replace) record.Union = other;
                if (interrupt) throw new InvalidOperationException("preflight failed");
                return accept;
            };
            var args = new object[] { record, draft, preflight, (Func<RealmUnionRecord, bool>)(j => j.ClientTransferStarted), null };
            bool ok = (bool)publish.Invoke(null, args);
            check(ok ? args[4] == null : !string.IsNullOrEmpty(args[4] as string), "Publication reports success or a deferral reason");
            return ok;
        }
        check(!Run() && record.Union == null, "Failed preflight discards only an unstarted draft");
        interrupt = true;
        check(!Run() && record.Union == null, "Thrown preflight leaves no stale unstarted journal");
        interrupt = false;
        accept = true;
        check(Run() && record.Union == draft, "Successful preflight retains the captured journal without native writes");
        int previousCalls = calls;
        check(!Run() && record.Union == draft && calls == previousCalls, "Retry cannot overwrite an existing journal");
        record.Union = null;
        record.Completed = true;
        check(!Run() && calls == previousCalls, "Completed accession cannot publish a new union");
        record.Completed = false;
        write = true;
        check(!Run() && record.Union == draft && draft.ClientTransferStarted,
            "Unexpected preflight write retains recovery evidence instead of discarding it");
        record.Union = null;
        previousCalls = calls;
        check(!Run() && calls == previousCalls, "Already-started draft cannot be published as new");
        draft = new RealmUnionRecord();
        write = false;
        replace = true;
        check(!Run() && record.Union == other, "Failed publication never erases a replacement journal");
        var behavior = new CrownAccessionBehavior();
        var native = new object[] { new CrownAccessionRecord(), null };
        check(!(bool)AccessTools.Method(typeof(CrownAccessionBehavior), "TryPublishRealmUnion").Invoke(behavior, native),
            "Native publication rejects unregistered accessions before campaign access");
    }

    private static void CheckEligibility(Action<bool, string> check)
    {
        var supports = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionSnapshotService"), "SupportsAccession");
        var record = new CrownAccessionRecord();
        bool Eligible() => (bool)supports.Invoke(null, new object[] { record });
        check(Eligible(), "Unstarted death accession can enter full union eligibility checks");
        foreach (string fieldName in new[] { "Completed", "RegentReplacement", "ElectiveElection", "Emergency", "MandateExpiry",
            "TitleTransferred", "ForeignMoveStarted", "VoluntaryAbdication", "ForcedAbdication" })
        {
            var field = AccessTools.Field(typeof(CrownAccessionRecord), fieldName);
            field.SetValue(record, true);
            check(!Eligible(), "Initial union trigger excludes incompatible accession state: " + fieldName);
            field.SetValue(record, false);
        }
        record.Union = new RealmUnionRecord();
        check(!Eligible(), "Existing union journal must resume instead of being recaptured");
        check(!(bool)supports.Invoke(null, new object[] { null }), "Missing accession cannot trigger a union");
    }
}
