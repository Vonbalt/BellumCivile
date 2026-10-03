using System;
using System.Linq;
using System.Reflection;
using BellumCivile;

internal static class CourtProtectionExecutionTests
{
    private sealed class Execution : ICourtProtectionExecution
    {
        internal bool Valid = true, Allowed = true, War, Client, Aligned, BlockDeclaration, ThrowDeclaration, ThrowEstablish, FailAlignment, ChangeRuler;
        internal int Declarations, Establishments;
        public bool IdentityValid(CourtProtectionRecord p) => Valid;
        public bool Preflight(CourtProtectionRecord p) => Allowed;
        public bool AtWar(CourtProtectionRecord p) => War;
        public void Declare(CourtProtectionRecord p)
        {
            Declarations++;
            War = !BlockDeclaration;
            if (ChangeRuler) Valid = false;
            if (ThrowDeclaration) throw new InvalidOperationException();
        }
        public void Establish(CourtProtectionRecord p)
        {
            Establishments++;
            if (!War) throw new Exception("Clientage attempted without defense");
            Client = true;
            if (ThrowEstablish) throw new InvalidOperationException();
            Aligned = !FailAlignment;
        }
        public bool Verify(CourtProtectionRecord p) => War && Client && Aligned;
    }
    private static CourtProtectionRecord Offer() => new CourtProtectionRecord {
        Phase = CourtProtectionPhase.Accepted, NextAttempt = 10, RecoveryUntil = 11.1, TermStart = 0, TermEnd = 11 };
    private static CourtProtectionRecord Roundtrip(CourtProtectionRecord p)
    {
        var copy = new CourtProtectionRecord();
        foreach (var field in typeof(CourtProtectionRecord).GetFields()) field.SetValue(copy, field.GetValue(p));
        return copy;
    }
    internal static void Run(Action<bool, string> check)
    {
        void Step(CourtProtectionRecord p, Execution e, double day = 10) => CourtProtectionExecution.Step(p, e, day);
        var p = Offer(); var e = new Execution();
        Step(p, e); Step(p, e, 11);
        check(p.Phase == CourtProtectionPhase.Completed && e.Declarations == 1 && e.Establishments == 1,
            "Successful protection declares before clientage and never repeats completed execution");
        p = Offer(); e = new Execution { War = true }; Step(p, e);
        check(p.Phase == CourtProtectionPhase.Completed && e.Declarations == 0 && e.Establishments == 1,
            "Existing belligerent creates no duplicate declaration");
        p = Offer(); e = new Execution { BlockDeclaration = true }; Step(p, e); Step(p, e, 11);
        check(p.Phase == CourtProtectionPhase.Failed && !e.Client && e.Declarations == 1,
            "Blocked intervention never strips the applicant's independence");
        p = Offer(); e = new Execution { Allowed = false }; Step(p, e);
        check(p.Phase == CourtProtectionPhase.Failed && e.Declarations == 0 && !e.Client,
            "Changed pact or permission blocks all execution before war");
        p = Offer(); e = new Execution { ChangeRuler = true }; Step(p, e);
        check(p.Phase == CourtProtectionPhase.Failed && e.War && !e.Client,
            "Ruler replacement during declaration cannot bind an unconsenting successor or force peace rollback");
        p = Offer(); e = new Execution { ThrowDeclaration = true }; Step(p, e);
        p = Roundtrip(p); Step(p, e, 10.5);
        check(p.Phase == CourtProtectionPhase.Completed && e.Declarations == 1 && e.Establishments == 1,
            "Reload after a post-stance exception resumes without redeclaring war");
        p = Offer(); e = new Execution { ThrowDeclaration = true, BlockDeclaration = true }; Step(p, e);
        e.ThrowDeclaration = false; p = Roundtrip(p); Step(p, e, 10.5);
        check(p.Phase == CourtProtectionPhase.Failed && e.Declarations == 1 && !e.Client,
            "An interrupted blocked declaration cannot be retried against the same realm");
        p = Offer(); e = new Execution { ThrowEstablish = true }; Step(p, e);
        p = Roundtrip(p); e.ThrowEstablish = false; Step(p, e, 10.5);
        check(p.Phase == CourtProtectionPhase.Completed && e.Declarations == 1 && e.Establishments == 2,
            "Partial clientage resumes alignment rather than declaring another war");
        p = Offer(); e = new Execution { ThrowEstablish = true }; Step(p, e);
        e.Aligned = true; p = Roundtrip(p); Step(p, e, 10.5);
        check(p.Phase == CourtProtectionPhase.Completed && e.Establishments == 1,
            "Verified recovered completion does not invoke establishment again");
        p = Offer(); e = new Execution { FailAlignment = true };
        Step(p, e); Step(p, e, 10.2);
        check(p.Attempts == 1, "Frequent ticks cannot consume retries before their scheduled time");
        Step(p, e, 10.5); Step(p, e, 11); Step(p, e, 12);
        check(p.Phase == CourtProtectionPhase.Failed && p.Attempts == 3 && e.Declarations == 1 && e.Establishments == 3,
            "Incomplete alignment terminates after three bounded attempts without erasing clientage or war");
        p = Offer(); e = new Execution(); Step(p, e, 12);
        check(p.Phase == CourtProtectionPhase.Failed && e.Declarations == 0, "Late reload cannot reopen an expired execution window");
        foreach (CourtProtectionPhase phase in Enum.GetValues(typeof(CourtProtectionPhase)))
        {
            p = Offer(); p.Phase = phase; e = new Execution(); Step(p, e);
            check((e.Declarations > 0) == (phase == CourtProtectionPhase.Accepted), "Only an accepted agreement may execute diplomacy");
        }
        foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 9d })
        {
            p = Offer(); e = new Execution(); Step(p, e, time);
            check(p.Attempts == 0 && e.Declarations == 0, "Invalid or early time cannot execute an agreement");
        }
        var fields = typeof(CourtProtectionRecord).GetFields();
        var ids = fields.Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(fields.Length == 23 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 23, "Offer identities, deadlines and stage receipts have unique save fields");
        p = Offer(); p.Phase = CourtProtectionPhase.Offered; p.ReplyDay = 3; p.ReplyDeadline = 5; p.PopupPending = true; p.Reported = true;
        var loaded = Roundtrip(p);
        check(loaded.Phase == CourtProtectionPhase.Offered && loaded.ReplyDay == 3 && loaded.ReplyDeadline == 5
            && loaded.PopupPending && loaded.Reported && loaded.TermEnd == p.TermEnd,
            "Field roundtrip preserves pending response, outcome acknowledgement and original deadlines");
    }
}
