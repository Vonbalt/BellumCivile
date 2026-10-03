using System;
using System.Reflection;
using BellumCivile;

internal static class RealmUnionReconciliationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(RealmUnionRecord).Assembly;
        var facts = assembly.GetType("BellumCivile.RealmUnionClanSnapshot");
        var assess = assembly.GetType("BellumCivile.RealmUnionReconciliation")
            .GetMethod("AssessClan", BindingFlags.Static | BindingFlags.NonPublic);
        object Clan(string realm, bool mercenary, float influence = 100, int debt = 25, string id = "house")
            => Activator.CreateInstance(facts, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { id, realm, mercenary, influence, debt }, null);
        string Assess(object original, object current, bool started = false, bool completed = false,
            bool restoring = false, bool restored = false, bool preserved = true,
            bool? returned = null, bool? restoreReturned = null)
        {
            var args = new object[] { "source", "destination", original, current,
                started, completed, restoring, restored, preserved, returned ?? started, restoreReturned ?? restoring, null };
            string result = assess.Invoke(null, args).ToString();
            check(result != "Blocked" || !string.IsNullOrEmpty(args[11] as string), "Union reconciliation explains blocked state");
            return result;
        }
        var noble = Clan("source", false);
        var target = Clan("destination", false);
        check(Assess(noble, target, true, returned: false) == "Blocked", "Visible allegiance cannot recover an interrupted callback");
        check(Assess(noble, target, true, true, true, restoreReturned: false) == "Blocked", "Visible balances cannot recover an interrupted restoration");
        check(Assess(noble, noble) == "BeginAction", "Unchanged captured noble may begin transfer");
        check(Assess(noble, noble, true) == "Blocked", "Interrupted native transfer must not be repeated");
        check(Assess(noble, target) == "Blocked", "Unjournaled allegiance change is not union completion");
        check(Assess(noble, target, true) == "VerifyAction", "Interrupted successful movement awaits receipt verification");
        check(Assess(noble, target, true, true) == "BeginRestoration", "Verified noble movement proceeds to restoration");
        check(Assess(noble, target, true, true, true) == "VerifyRestoration", "Restored state can recover missing completion receipt");
        check(Assess(noble, target, true, true, true, true) == "Complete", "Verified restored noble is complete");
        check(Assess(noble, target, true, true, true, true, false) == "Blocked", "Completion flags cannot replace holdings and banner verification");
        check(Assess(noble, Clan("destination", false, 0), true, true, true) == "Blocked", "Partial restoration cannot replay balance writes");
        check(Assess(noble, Clan("source", false, 90)) == "Blocked", "Changed balance invalidates unused snapshot");
        check(Assess(noble, Clan("destination", false, debt: 0), true, true, true, true) == "Blocked", "Debt mismatch blocks completed receipt");
        check(Assess(noble, Clan("third", false), true, true) == "Blocked", "Foreign allegiance blocks resume");
        check(Assess(noble, null) == "Blocked", "Missing clan is not a successful transfer");
        check(Assess(noble, Clan("destination", false, id: "other"), true) == "Blocked", "Wrong clan identity blocks resume");
        check(Assess(noble, Clan("source", false, float.NaN)) == "Blocked", "Invalid current balance blocks resume");
        var company = Clan("source", true);
        var independent = Clan(null, false, 0, 0);
        check(Assess(company, company) == "BeginAction", "Captured contract may be ended");
        check(Assess(company, independent, true) == "VerifyAction", "Released company can recover termination receipt");
        check(Assess(company, independent, true, true) == "Complete", "Released company does not restore noble influence");
        check(Assess(company, independent, true, true, preserved: false) == "Blocked", "Company preservation remains mandatory");
        check(Assess(company, Clan("destination", true), true, true) == "Blocked", "Rehired company cannot be mistaken for contract termination");
        for (int flags = 0; flags < 16; flags++)
        {
            bool a = (flags & 1) != 0, c = (flags & 2) != 0;
            bool r = (flags & 4) != 0, f = (flags & 8) != 0;
            if (c && !a || r && !c || f && !r)
                check(Assess(noble, target, a, c, r, f) == "Blocked", "Contradictory noble receipts fail closed: " + flags);
            if (r || f)
                check(Assess(company, independent, a, c, r, f) == "Blocked", "Mercenary restoration receipts fail closed: " + flags);
        }
    }
}
