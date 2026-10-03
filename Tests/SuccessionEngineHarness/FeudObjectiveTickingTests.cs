using System;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class FeudObjectiveTickingTests
{
    internal static void Run(Action<bool, string> check)
    {
        WarScoreRecord New(WarScoreConflictType type = WarScoreConflictType.ClaimFeud)
            => new WarScoreRecord("a|b", "a", "b", 0f, null, type);
        var war = New();
        bool Update(bool? claimantControls, float day, string claimant = "a")
            => (bool)AccessTools.Method(typeof(WarScoreRecord), "UpdateFeudObjectiveControl")
                .Invoke(war, new object[] { claimant, claimantControls, day });

        Update(true, 10.25f);
        check(war.ObjectiveScore == 0f, "Claimant control starts without an immediate objective bonus");
        Update(true, 11f);
        check(war.ObjectiveScore == 0f, "Crossing midnight is not a completed uninterrupted control day");
        Update(true, 11.25f);
        check(war.ObjectiveScore == 1f, "Claimant earns one point per completed control day");
        Update(true, 11.25f);
        check(war.ObjectiveScore == 1f, "Repeated reconciliation cannot double-credit a day");
        Update(true, 15.75f);
        check(war.ObjectiveScore == 5f, "Skipped ticks count completed days without losing the fractional remainder");
        Update(true, 100f);
        check(war.ObjectiveScore == 50f, "Claimant objective pressure caps at fifty");
        Update(false, 100.25f);
        check(war.ObjectiveScore == 0f, "Control change removes all prior objective pressure immediately");
        Update(false, 101.25f);
        check(war.ObjectiveScore == -1f, "Holder earns negative objective pressure after a completed day");
        Update(false, 200f);
        check(war.ObjectiveScore == -50f, "Holder objective pressure caps at negative fifty");
        Update(true, 200.25f);
        Update(false, 200.5f);
        Update(false, 201.25f);
        check(war.ObjectiveScore == 0f, "Same-day loss and recapture starts a new uninterrupted period");
        Update(false, 201.5f);
        check(war.ObjectiveScore == -1f, "Recapture accrues only from the latest change of control");

        war = New();
        Update(true, 0f, "b"); Update(true, 3f, "b");
        check(war.ObjectiveScore == -3f, "Defender claimant earns correctly signed pressure");
        Update(false, 3f, "b"); Update(false, 5f, "b");
        check(war.ObjectiveScore == 2f, "Attacker holder earns correctly signed pressure");

        war = New();
        war.AddBattleScore(20f, 40f);
        war.AddObjectiveScore(50f, 100f);
        war.AddTickingScore(4f, 10f, 99f);
        Update(false, 100f);
        check(war.ObjectiveScore == 0f && war.TickingScore == 0f && war.Score == 20f
            && war.LastTickDay == 100f, "Legacy migration removes objective and generic ticking but preserves battle score");
        Update(false, 100.75f);
        check(war.ObjectiveScore == 0f, "Legacy holder receives no invented retroactive days");
        Update(false, 101f);
        check(war.ObjectiveScore == -1f, "Legacy holder starts earning one full day after migration");

        // Emulate save restoration of the persisted clock fields into a new record.
        var saved = war;
        war = New();
        foreach (string name in new[] { "_feudObjectiveVerifiedController", "_feudObjectiveControlSinceDay" })
        {
            var field = AccessTools.Field(typeof(WarScoreRecord), name);
            field.SetValue(war, field.GetValue(saved));
        }
        Update(false, 102.5f);
        check(war.ObjectiveScore == -2f, "Restored objective clock retains its original fractional-day anchor");

        war = New();
        war.SetOccupationScore(60f);
        Update(true, 0f); Update(true, 50f);
        war.QueueTerminalResolution("objective pressure");
        Update(false, 50.25f);
        check(war.Score == 60f && !war.TerminalResolutionQueued,
            "Losing control cancels a reversible terminal queue when total score drops below terminal");
        war = New();
        war.SetOccupationScore(100f);
        Update(true, 0f); Update(true, 50f);
        war.QueueTerminalResolution("other score remains terminal");
        Update(false, 50f);
        check(war.Score == 100f && war.TerminalResolutionQueued,
            "Control change preserves a terminal queue still supported by other components");
        war = New();
        war.SetOccupationScore(60f); war.AddObjectiveScore(50f, 100f);
        war.AddTickingScore(2f, 10f, 99f); war.QueueTerminalResolution("legacy objective");
        Update(true, 100f);
        check(war.Score == 60f && !war.TerminalResolutionQueued,
            "Migration removes an uncommitted legacy terminal victory before resolution");

        foreach (string locked in new[] { "resolution", "forced parley", "ended" })
        {
            war = New();
            Update(true, 0f); Update(true, 5f);
            if (locked == "resolution") war.BeginResolution();
            else if (locked == "forced parley") war.BeginParley(true, 5f);
            else war.MarkEnded(5f);
            check(!Update(false, 6f) && war.ObjectiveScore == 5f,
                "Objective updates preserve " + locked + " records");
        }
        war = New();
        Update(true, 0f); war.BeginParley(false, 0f); Update(true, 2f);
        check(war.ObjectiveScore == 2f, "Voluntary parley does not freeze reversible objective pressure");
        war = New(WarScoreConflictType.ForeignWar);
        war.AddTickingScore(3f, 10f, 1f);
        check(!Update(true, 10f) && war.TickingScore == 3f, "Foreign-war generic ticking is untouched");
        war = New();
        check(!Update(true, float.NaN) && !Update(true, float.PositiveInfinity)
            && !Update(true, 0f, "outsider"), "Invalid time or claimant cannot initialize the objective clock");
        Update(true, 10f); Update(true, 15f); Update(true, 9f);
        check(war.ObjectiveScore == 0f, "Backward time safely rebases control without inventing days");

        bool? Controller(string[] owners, string[] claimantIds = null, string[] holderIds = null)
            => (bool?)AccessTools.Method(typeof(ClaimFeudWarBehavior), "ResolveObjectiveController")
                .Invoke(null, new object[] { owners, claimantIds ?? new[] { "claimant" }, holderIds ?? new[] { "holder" } });
        check(Controller(new[] { "claimant" }) == true, "Claimant must explicitly own a barony objective");
        check(Controller(new[] { "holder" }) == false, "Holder must explicitly own a barony objective");
        check(Controller(new[] { "outsider" }) == null, "Third-party barony control verifies neither feud side");
        check(Controller(new string[] { null }) == null && Controller(new string[0]) == null,
            "Missing ownership or objectives verifies neither side");
        check(Controller(new[] { "claimant", "holder" }) == null,
            "A disputed equal split does not default to holder control");
        check(Controller(new[] { "claimant", "holder", "outsider" }) == null,
            "Foreign objectives remain in the denominator when verifying a majority");
        check(Controller(new[] { "holder", "holder", "outsider" }) == false
            && Controller(new[] { "claimant", "claimant", "outsider" }) == true,
            "Either side can verify the existing strict-majority objective rule");
        check(Controller(new[] { "shared" }, new[] { "shared" }, new[] { "shared" }) == null,
            "Overlapping side membership cannot verify objective control");

        foreach (bool side in new[] { true, false })
        {
            war = New();
            Update(side, 0f); Update(side, 50f); Update(null, 50.25f);
            check(war.ObjectiveScore == 0f, "Neutral control removes either side's accumulated pressure");
            Update(null, 100f);
            check(war.ObjectiveScore == 0f, "Neither side earns while objective control remains neutral");
            Update(side, 100.25f); Update(side, 101f);
            check(war.ObjectiveScore == 0f, "Recovery after neutral control cannot reuse earlier control days");
            Update(side, 101.25f);
            check(war.ObjectiveScore == (side ? 1f : -1f), "Verified recovery earns only a new completed day");
        }
        war = New();
        war.SetOccupationScore(60f); Update(true, 0f); Update(true, 50f);
        war.QueueTerminalResolution("objective pressure"); Update(null, 50.5f);
        check(war.Score == 60f && !war.TerminalResolutionQueued,
            "Neutral control cancels a terminal queue dependent on objective pressure");
        war = New();
        war.AddObjectiveScore(50f, 100f); war.AddTickingScore(3f, 10f, 90f);
        Update(null, 100f);
        check(war.Score == 0f && war.TickingScore == 0f,
            "Neutral legacy migration removes all obsolete objective and generic time pressure");
    }
}
