using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtAgendaTimingTests
{
    private static readonly List<string> Daily = new List<string>();
    private static readonly List<string> Yearly = new List<string>();
    private static readonly List<string> SlowMaintenance = new List<string>();
    private static readonly List<string> YearlyMaintenance = new List<string>();
    private static readonly List<string> Debug = new List<string>();

    private static bool Log(string message)
    {
        if (message.StartsWith("Court timing;", StringComparison.Ordinal)) { Daily.Add(message); return false; }
        if (message.StartsWith("Court maintenance timing;", StringComparison.Ordinal)) { SlowMaintenance.Add(message); return false; }
        if (message.StartsWith("[BellumCivile court-objectives debug]", StringComparison.Ordinal)) { Debug.Add(message); return false; }
        return true;
    }

    private static bool Report(string category, string report)
    {
        if (category == "court maintenance steps") { YearlyMaintenance.Add(report); return false; }
        if (category != "court timing") return true;
        Yearly.Add(report);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.court_agenda_timing");
        var assembly = typeof(CourtAgendaBehavior).Assembly;
        Type Type(string name) => assembly.GetType("BellumCivile." + name, throwOnError: true);
        object Make(string name, params object[] args) => Activator.CreateInstance(Type(name),
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, args, null);
        var debugField = AccessTools.Field(Type("BellumCivileDebug"), "_showInGameMessagesOverride");
        object previousDebug = debugField.GetValue(null);
        var timing = Make("CourtAgendaTiming");
        void Sample(string action, params object[] args) => AccessTools.Method(timing.GetType(), action).Invoke(timing, args);
        long Ms(int amount) => Stopwatch.Frequency * amount / 1000L;
        void Step(string name, int milliseconds, string realm = null) =>
            Sample("RecordMaintenanceStep", Enum.Parse(Type("CourtMaintenanceStep"), name), Ms(milliseconds), realm);

        try
        {
            harmony.Patch(AccessTools.Method(Type("BellumCivileLogger"), "Log"),
                prefix: new HarmonyMethod(typeof(CourtAgendaTimingTests), nameof(Log)));
            harmony.Patch(AccessTools.Method(Type("BellumCivileLogger"), "LogYearlyReport"),
                prefix: new HarmonyMethod(typeof(CourtAgendaTimingTests), nameof(Report)));
            debugField.SetValue(null, false);

            Sample("BeginDay", 1085);
            Sample("EndDay", 84.0, Ms(8));
            check(Daily.Count == 0 && Yearly.Count == 0, "An ordinary maintenance day produces no extra log line");
            Sample("BeginDay", 1085);
            Sample("RecordSelection", Ms(6));
            Sample("RecordRealm", "east", Ms(13));
            Sample("RecordRealm", "west", Ms(5));
            Sample("RecordPreparation", Ms(20));
            Sample("EndDay", 85.5, Ms(50));
            check(Daily.Count == 1 && Daily[0].Contains("day=85.5; daily_ms=50; maintenance_ms=30; preparation_ms=20; queue_ms=2; prepared=2"),
                "With debug off a prepared batch records separate maintenance, queue and preparation times");
            check(Daily[0].Contains("east:term_ms=13,selection_ms=6 | west:term_ms=5,selection_ms=0"),
                "Each realm reports its own elapsed and selection times without carrying another realm's selection cost");
            Sample("BeginPass", 1085);
            Sample("RecordSelection", Ms(4));
            Sample("RecordRealm", "south", Ms(10));
            Sample("EndHour", 85.6, Ms(12));
            check(Daily.Count == 2 && Daily[1].Contains("day=85.6; source=hourly; preparation_ms=12; queue_ms=2; prepared=1")
                && Daily[1].Contains("south:term_ms=10,selection_ms=4") && !Daily[1].Contains("maintenance_ms"),
                "Hourly preparation does not masquerade as a daily maintenance sample");
            Sample("BeginDay", 1086);
            check(Yearly.Count == 1 && Yearly[0].Contains("daily:count=2,total_ms=58,peak_ms=50; maintenance:count=2,total_ms=38,peak_ms=30")
                && Yearly[0].Contains("preparation:count=2,total_ms=32,peak_ms=20")
                && Yearly[0].Contains("realm:count=3,total_ms=28,peak_ms=13,peak_realm=east"),
                "The yearly rollup includes separate hourly preparation and daily maintenance");
            Sample("EndDay", 86.0, Ms(9));
            Sample("BeginPass", 1087);
            check(Yearly.Count == 2 && Yearly[1].Contains("daily:count=1,total_ms=9,peak_ms=9")
                && Yearly[1].Contains("realm:count=0,total_ms=0,peak_ms=0,peak_realm=none") && Daily.Count == 2,
                "Year rollover clears peak realm and batch data without replaying a prior report");

            Sample("BeginDay", 1087);
            Step("TitleGrants", 20);
            Sample("RecordPreparation", Ms(80));
            Sample("EndDay", 87.0, Ms(100));
            check(SlowMaintenance.Count == 0, "Expensive preparation alone does not trigger a maintenance spike report");

            Sample("BeginDay", 1087);
            Step("EmergencyChallenges", 75, "north");
            Step("EmergencyChallenges", 9, "south");
            Step("AgendaAdvance", 20, "east");
            Sample("RecordPreparation", Ms(10));
            Sample("EndDay", 88.0, Ms(127));
            check(SlowMaintenance.Count == 1 && SlowMaintenance[0].Contains("day=88; maintenance_ms=117; unattributed_ms=13; "
                + "steps=EmergencyChallenges:calls=2,total_ms=84,peak_call_ms=75,peak_realm=north"
                + " | AgendaAdvance:calls=1,total_ms=20,peak_call_ms=20,peak_realm=east"),
                "A slow day reports sorted step totals and the peak realm, excluding preparation and yesterday's samples with debug off");

            Sample("BeginPass", 1087);
            Sample("RecordRealm", "south", Ms(78));
            Sample("EndHour", 88.25, Ms(80));
            check(SlowMaintenance.Count == 1, "Hourly agenda preparation never contributes to maintenance spike reports");
            Sample("BeginDay", 1087);
            Step("EmergencyChallenges", 3, "east");
            Sample("EndDay", 89.0, Ms(3));
            Sample("BeginDay", 1088);
            check(YearlyMaintenance.Count == 1
                && YearlyMaintenance[0].Contains("year=1087; steps=EmergencyChallenges:calls=3,total_ms=87,peak_call_ms=75,peak_realm=north")
                && YearlyMaintenance[0].Contains("TitleGrants:calls=1,total_ms=20,peak_call_ms=20"),
                "The yearly maintenance summary includes quiet days and keeps the peak invocation across realms");
            Step("RoyalPeaceCases", 50);
            Sample("EndDay", 90.0, Ms(50));
            Sample("BeginDay", 1089);
            check(SlowMaintenance.Count == 2 && YearlyMaintenance.Count == 2
                && YearlyMaintenance[1] == "year=1088; steps=RoyalPeaceCases:calls=1,total_ms=50,peak_call_ms=50",
                "The maintenance spike threshold includes 50 ms and year rollover clears old step samples");

            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); realm.StringId = "test_realm";
            var diagnostic = AccessTools.Method(typeof(CourtAgendaBehavior), "CreateObjectiveDiagnostic");
            check(diagnostic.Invoke(null, new object[] { realm }) == null && Debug.Count == 0,
                "Debug-off selection receives no callback, so candidate debug strings cannot be built");
            debugField.SetValue(null, true);
            var trace = (Delegate)diagnostic.Invoke(null, new object[] { realm });
            var weight = Make("CourtObjectiveWeight", 5d, 0d, 0d, 0d, 0d);
            var evaluation = Make("CourtObjectiveEvaluation", true, true, weight, "test motive");
            var candidate = Make("CourtObjectiveCandidate", "test_target", "test_action", null);
            trace.DynamicInvoke(Make("CourtObjectiveOwner", null, null), Make("CourtObjectiveChoice", "test_kind", candidate, evaluation, null));
            check(Debug.Count == 1 && Debug[0].Contains("realm=test_realm; owner=Crown; kind=test_kind; target=test_target")
                && Debug[0].Contains("test motive"), "Debug-on selection still logs candidate details");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            debugField.SetValue(null, previousDebug);
            Daily.Clear(); Yearly.Clear(); SlowMaintenance.Clear(); YearlyMaintenance.Clear(); Debug.Clear();
        }
    }
}
