using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static partial class MarriageHouseholdTests
{
    private static readonly List<string> MarriageFundingTraceLines = new List<string>();
    private static bool CaptureMarriageFundingTrace(string category, string message, bool requestInGameDisplay)
    {
        if (category != "marriage" || !requestInGameDisplay) return true;
        MarriageFundingTraceLines.Add(message);
        return false;
    }

    private static void CheckMarriageFundingDiagnostics(Action<bool, string> check, object context, Func<object> evaluate,
        Hero first, Hero second)
    {
        var assembly = typeof(BellumCivile.PlayerMarriageAgreement).Assembly;
        var debug = assembly.GetType("BellumCivile.BellumCivileDebug");
        var payments = assembly.GetType("BellumCivile.MarriagePayments");
        var statsType = assembly.GetType("BellumCivile.Behaviors.BellumMarriageEvaluationStats");
        var toggle = AccessTools.Field(debug, "_showInGameMessagesOverride");
        var oldToggle = toggle.GetValue(null);
        var capture = new Harmony("bellum.tests.marriage_funding_diagnostics");
        var diagnostics = AccessTools.Field(context.GetType(), "FundingDiagnostics");
        var best = AccessTools.Property(statsType, "BestFundingBlock");
        var receiving = first.Clan;
        var sending = second.Clan;
        int payerGold = receiving.Leader.Gold;
        int payeeGold = sending.Leader.Gold;
        float oldScore = _secondScore;
        object stats = Activator.CreateInstance(statsType, nonPublic: true);
        void Quote() => AccessTools.Method(payments, "TraceQuote").Invoke(null,
            new object[] { "test quote", first, second, receiving, sending, 40000, 10000 });
        void Consider(float score, int amount, bool deposit) =>
            AccessTools.Method(statsType, "ConsiderFundingBlock").Invoke(stats,
                new object[] { first, second, receiving, sending, score, 40000, amount, deposit });
        try
        {
            capture.Patch(AccessTools.Method(debug, "Trace"),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(CaptureMarriageFundingTrace)));
            toggle.SetValue(null, false);
            Quote();
            check(MarriageFundingTraceLines.Count == 0, "Funding quotes do not produce detailed traces with debug disabled");

            toggle.SetValue(null, true);
            diagnostics.SetValue(context, stats);
            receiving.Leader.Gold = 39999;
            _secondScore = 94;
            check(evaluate() == null && best.GetValue(stats) == null, "A match below the acceptance floor is never blamed on funding alone");
            _secondScore = oldScore;
            check(evaluate() == null && best.GetValue(stats) != null, "A willing marriage blocked solely by funds is captured");
            receiving.Leader.Gold = 9000;
            Consider(120, 10000, deposit: true);
            Consider(110, 40000, deposit: false);
            receiving.Leader.Gold = 30000;
            object sample = best.GetValue(stats);
            AccessTools.Method(sample.GetType(), "Trace").Invoke(sample, new object[] { sending });
            check(MarriageFundingTraceLines.Count == 1 && MarriageFundingTraceLines[0].Contains("kind=betrothal")
                && MarriageFundingTraceLines[0].Contains("dowry=40000; due_now=10000; payer_gold=9000; shortfall=1000")
                && MarriageFundingTraceLines[0].Contains("payer=" + receiving.StringId),
                "Only the strongest blocked match is reported, using the payer's balance at evaluation time");

            Quote();
            check(MarriageFundingTraceLines[1].Contains("payer_gold=30000"), "A quoted agreement reports current funds and the required payment");

            var reserve = AccessTools.Method(payments, "Reserve");
            var failed = (IDisposable)reserve.Invoke(null, new object[] { first, second, receiving, sending, 30000, 10000 });
            failed.Dispose();
            check(receiving.Leader.Gold == 30000 && MarriageFundingTraceLines[2].Contains("transferred=0; refunded=30000; escrow=10000"),
                "A failed wedding reports the balance refund, not a settled dowry");
            var completed = (IDisposable)reserve.Invoke(null, new object[] { first, second, receiving, sending, 30000, 10000 });
            Spouses[first] = second; Spouses[second] = first; Houses[second] = receiving;
            completed.Dispose(); completed.Dispose();
            check(MarriageFundingTraceLines.Count == 4
                && MarriageFundingTraceLines[3].Contains("agreed=40000; transferred=40000; refunded=0; escrow=10000; wedding_balance=30000"),
                "A completed wedding reports its actual transfer and escrow once");
        }
        finally
        {
            diagnostics.SetValue(context, null);
            toggle.SetValue(null, oldToggle);
            capture.UnpatchAll(capture.Id);
            MarriageFundingTraceLines.Clear();
            receiving.Leader.Gold = payerGold; sending.Leader.Gold = payeeGold;
            _secondScore = oldScore;
            Spouses.Clear(); Houses[second] = sending;
        }
    }
}
