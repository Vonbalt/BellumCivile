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
    private static readonly List<string> Debug = new List<string>();

    private static bool Log(string message)
    {
        if (message.StartsWith("Court timing;", StringComparison.Ordinal)) { Daily.Add(message); return false; }
        if (message.StartsWith("[BellumCivile court-objectives debug]", StringComparison.Ordinal)) { Debug.Add(message); return false; }
        return true;
    }

    private static bool Report(string category, string report)
    {
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
            Sample("BeginDay", 1086);
            check(Yearly.Count == 1 && Yearly[0].Contains("daily:count=2,total_ms=58,peak_ms=50; maintenance:count=2,total_ms=38,peak_ms=30")
                && Yearly[0].Contains("realm:count=2,total_ms=18,peak_ms=13,peak_realm=east"),
                "The yearly rollup includes quiet days and identifies the slowest prepared realm");
            Sample("EndDay", 86.0, Ms(9));
            Sample("BeginDay", 1087);
            check(Yearly.Count == 2 && Yearly[1].Contains("daily:count=1,total_ms=9,peak_ms=9")
                && Yearly[1].Contains("realm:count=0,total_ms=0,peak_ms=0,peak_realm=none") && Daily.Count == 1,
                "Year rollover clears peak realm and batch data without replaying a prior report");

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
            Daily.Clear(); Yearly.Clear(); Debug.Clear();
        }
    }
}
