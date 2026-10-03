using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;

internal static class RealmUnionAgreementProgressTests
{
    internal static void Run(Action<bool, string> check)
    {
        var rules = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionObligationRules");
        var journal = new RealmUnionRecord {
            SourceObligations = new Dictionary<string, string> { ["trade:partner"] = "100", ["alliance:ally"] = "150" },
            DestinationObligations = new Dictionary<string, string> { ["alliance:ally"] = "120", ["trade:other"] = "200" } };
        var source = new Dictionary<string, string>(journal.SourceObligations);
        var destination = new Dictionary<string, string>(journal.DestinationObligations);
        bool Verify(string method) => (bool)AccessTools.Method(rules, method).Invoke(null,
            new object[] { journal, source, destination, null });
        bool Project(string method)
        {
            var args = new object[] { source, destination, null, null, null };
            bool ok = (bool)AccessTools.Method(rules, method).Invoke(null, args);
            if (ok) { source = (Dictionary<string, string>)args[2]; destination = (Dictionary<string, string>)args[3]; }
            return ok;
        }
        check(Verify("VerifyProgress") && !Verify("VerifyUnchanged"), "Unmigrated agreements have valid baseline but cannot release Crown transfer");
        check(Project("TryProjectTrade") && source.ContainsKey("alliance:ally") && !source.ContainsKey("trade:partner"),
            "Trade stage leaves source alliances for their adapter");
        journal.TradeTransferStarted = journal.TradeTransferReturned = true;
        check(Verify("VerifyProgress") && !Verify("VerifyUnchanged"), "Trade completion permits resume but does not settle alliance obligations");
        check(Project("TryProjectAlliance") && source.Count == 0 && destination["alliance:ally"] == "150",
            "Alliance stage keeps the longer inherited expiry");
        journal.AllianceTransferStarted = true;
        check(!Verify("VerifyProgress"), "Native alliance write without callback return cannot be inferred complete");
        journal.AllianceTransferReturned = true;
        check(Verify("VerifyProgress") && Verify("VerifyUnchanged"), "Both returned transfers verify combined final obligations");
        var restored = new RealmUnionRecord();
        foreach (var field in typeof(RealmUnionRecord).GetFields()) field.SetValue(restored, field.GetValue(journal));
        journal = restored;
        check(Verify("VerifyUnchanged"), "Reconstructed receipts retain combined obligation projection");
        destination["alliance:ally"] = "151";
        check(!Verify("VerifyProgress"), "Later alliance renewal cannot silently overwrite captured expiry");
        destination["alliance:ally"] = "150";
        for (int flags = 0; flags < 16; flags++)
        {
            journal.TradeTransferStarted = (flags & 1) != 0;
            journal.TradeTransferReturned = (flags & 2) != 0;
            journal.AllianceTransferStarted = (flags & 4) != 0;
            journal.AllianceTransferReturned = (flags & 8) != 0;
            check(Verify("VerifyUnchanged") == (flags == 15), "Final native agreement state requires complete ordered receipts: " + flags);
        }
        source = new Dictionary<string, string> { ["alliance:ally"] = "150" };
        destination = new Dictionary<string, string> { ["tribute:ally"] = "10" };
        check(!Project("TryProjectTrade"), "Known alliance/tribute conflict prevents trade stage before either mutation");
        destination.Clear();
        source["alliance:ally"] = "NaN";
        check(!Project("TryProjectTrade"), "Malformed future alliance prevents partial trade migration");
    }
}
