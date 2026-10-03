using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;

internal static class RealmUnionTributeProjectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var journal = new RealmUnionRecord { SourceObligations = new Dictionary<string, string>(),
            DestinationObligations = new Dictionary<string, string>(), TributeDestinationPeaceDates = new Dictionary<string, string> { ["a"] = "12", ["b"] = "15" } };
        foreach (string id in new[] { "a", "b" })
        {
            journal.SourceObligations["tribute:" + id] = id == "a" ? "100" : "-100";
            journal.SourceObligations["tribute-paid:" + id] = id == "a" ? "2500" : "-2500";
            journal.SourceObligations["tribute-installments:" + id] = "100";
            journal.SourceObligations["tribute-date:" + id] = "10";
        }
        var source = new Dictionary<string, string>(journal.SourceObligations);
        var destination = new Dictionary<string, string>();
        var rules = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionObligationRules");
        bool Verify(string method) => (bool)AccessTools.Method(rules, method).Invoke(null,
            new object[] { journal, source, destination, null });
        void Transfer(string id)
        {
            foreach (string prefix in new[] { "tribute:", "tribute-paid:", "tribute-installments:", "tribute-date:" })
            {
                destination[prefix + id] = prefix == "tribute-date:" ? journal.TributeDestinationPeaceDates[id] : source[prefix + id];
                source.Remove(prefix + id);
            }
        }
        check(Verify("VerifyProgress") && !Verify("VerifyUnchanged"), "Native debts are valid pending obligations, not settled ones");
        var projection = AccessTools.Method(rules, "TryProjectTrade");
        var args = new object[] { source, destination, null, null, null };
        check((bool)projection.Invoke(null, args), "Complete valid native ledgers permit earlier agreement preparation");
        Transfer("a");
        journal.TributeTransfersStarted.Add("a");
        check(!Verify("VerifyProgress"), "Native ledger movement without returned receipt cannot complete");
        journal.TributeTransfersReturned.Add("a");
        check(Verify("VerifyProgress") && !Verify("VerifyUnchanged"), "One returned tribute verifies while the second remains pending");
        Transfer("b");
        journal.TributeTransfersStarted.Add("b"); journal.TributeTransfersReturned.Add("b");
        check(Verify("VerifyUnchanged"), "Both transferred native debts satisfy final obligation verification");
        var restored = new RealmUnionRecord();
        foreach (var field in typeof(RealmUnionRecord).GetFields()) field.SetValue(restored, field.GetValue(journal));
        journal = restored;
        check(Verify("VerifyUnchanged"), "Saved per-partner receipts reconstruct native debt projection");
        destination["tribute-paid:a"] = "2600";
        check(!Verify("VerifyProgress"), "Intervening tribute payment blocks stale projection");
        destination["tribute-paid:a"] = "2500";
        destination["tribute-date:a"] = "10";
        check(!Verify("VerifyProgress"), "Source peace date cannot replace destination peace history");
        destination["tribute-date:a"] = "12";
        journal.TributeTransfersReturned.Add("a");
        check(!Verify("VerifyProgress"), "Duplicate native tribute receipts fail closed");
        journal.TributeTransfersReturned.RemoveAt(2);
        journal.TributeTransfersStarted.Add("unknown"); journal.TributeTransfersReturned.Add("unknown");
        check(!Verify("VerifyProgress"), "Unrecorded tribute partner cannot be considered delivered");
        journal.TributeTransfersStarted.Remove("unknown"); journal.TributeTransfersReturned.Remove("unknown");
        journal.TributeDestinationPeaceDates.Remove("b");
        check(!Verify("VerifyProgress"), "Missing frozen destination history blocks receipt recovery");
    }
}
