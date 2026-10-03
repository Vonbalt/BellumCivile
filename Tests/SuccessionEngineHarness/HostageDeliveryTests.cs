using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using HarmonyLib;

internal static class HostageDeliveryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var method = AccessTools.Method(typeof(HostagePactRecord).Assembly.GetType("BellumCivile.HostageTreatyDelivery"), "Run");
        var calls = new List<string>();
        bool valid = true, place = true, settle = true, peace = true, activate = true, fail = false;
        HostagePactRecord pact = null;
        void Reset()
        {
            calls.Clear(); valid = place = settle = peace = activate = true; fail = false;
            pact = new HostagePactRecord { TreatyProposalId = "delivery-test" };
        }
        string Run() => method.Invoke(null, new object[] { pact,
            new Func<bool>(() => { calls.Add("validate"); return valid; }),
            new Func<bool>(() => { calls.Add("place"); return place; }),
            new Func<bool>(() => { calls.Add("settle"); if (fail) throw new InvalidOperationException("interrupted"); return settle; }),
            new Func<bool>(() => { calls.Add("peace"); return peace; }),
            new Func<bool>(() => { calls.Add("activate"); if (activate) pact.Phase = HostagePactPhase.Active; return activate; }),
            new Action(() => calls.Add("rollback")) }).ToString();
        Reset();
        check(Run() == "Applied" && calls.SequenceEqual(new[] { "validate", "place", "settle", "peace", "activate" }),
            "Hostages placed before concessions; pact activates only after peace");
        calls.Clear(); check(Run() == "Applied" && calls.Count == 0, "Completed delivery cannot repeat treaty actions");
        Reset(); valid = false;
        check(Run() == "Rejected" && calls.SequenceEqual(new[] { "validate", "rollback" }) && !pact.TreatySettlementStarted,
            "Invalid handover never settles treaty");
        Reset(); place = false;
        check(Run() == "Rejected" && calls.Last() == "rollback" && !calls.Contains("settle"),
            "Partial custody failure returns hostages before concessions");
        Reset(); settle = false;
        check(Run() == "RecoveryRequired" && pact.TreatySettlementStarted && !pact.TreatySettlementCompleted && !calls.Contains("rollback"),
            "Failed settlement retains receipt instead of pretending concessions were undone");
        calls.Clear(); check(Run() == "RecoveryRequired" && calls.Count == 0, "Interrupted concessions never replay on retry");
        Reset(); peace = false;
        check(Run() == "RecoveryRequired" && pact.TreatySettlementCompleted && !calls.Contains("activate"),
            "Pact cannot activate while war remains");
        peace = true; calls.Clear();
        check(Run() == "Applied" && calls.SequenceEqual(new[] { "peace", "activate" }), "Completed settlement recovers without repeating gold or land transfers");
        Reset(); fail = true;
        try { Run(); check(false, "Settlement failure propagated"); }
        catch (TargetInvocationException ex)
        { check(ex.InnerException is InvalidOperationException && pact.TreatySettlementStarted && !calls.Contains("rollback"), "Thrown settlement failure preserves irreversible-boundary receipt"); }
        Reset(); activate = false;
        check(Run() == "RecoveryRequired" && pact.TreatySettlementCompleted, "Failed pact activation preserves completed settlement");
    }
}
