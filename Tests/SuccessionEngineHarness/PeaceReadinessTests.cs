using System;
using BellumCivile;
using HarmonyLib;

internal static class PeaceReadinessTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(WarScoreRecord).Assembly.GetType("BellumCivile.PeaceReadiness", true);
        var evaluate = AccessTools.Method(type, "Evaluate");
        var concede = AccessTools.Method(type, "CanConcede");
        var white = AccessTools.Method(type, "CanConsiderWhitePeace");
        object Assessment(float day, float score, float enthusiasm, int period = 100)
            => evaluate.Invoke(null, new object[] { day, score, enthusiasm, period });
        float Part(object a, string name) => (float)AccessTools.Property(a.GetType(), name).GetValue(a);
        float Total(float day, float score, float enthusiasm, int period = 100)
            => Part(Assessment(day, score, enthusiasm, period), "Total");
        bool Concede(float score, float enthusiasm, float day, int period = 100)
            => (bool)concede.Invoke(null, new object[] { score, enthusiasm, day, period });
        bool White(float score, float first, float second, float day)
            => (bool)white.Invoke(null, new object[] { score, first, second, day, 100 });
        bool Near(float a, float b) => Math.Abs(a - b) < .001f;

        check(Near(Part(Assessment(0, 50, 50), "EarlyWarReluctance"), -100), "Early reluctance starts at minus one hundred");
        check(Near(Part(Assessment(30, 50, 5), "EarlyWarReluctance"), -35), "Low enthusiasm proportionally softens duration pressure");
        check(Near(Total(99, 50, 9), 8.1f) && Near(Total(100, 50, 9), 39), "Day-hundred flat bonus reproduces the agreed turning point");
        check(Near(Total(100, 50, 10), 5) && Near(Total(100, 50, 9.99f), 35.04f), "Flat exhaustion bonus has the explicit enthusiasm boundary");
        check(Near(Total(100, 50, 0), 75) && Near(Total(1000, 50, 0), 75), "Prolonged bonus never stacks over time");
        check(Near(Total(55, 50, 4), 11), "Day-fifty-five holder scenario reaches mild support");
        check(Concede(50, 4, 55), "Readiness at exactly eleven admits concession");
        check(!Concede(50, 9, 50), "Fifty leverage alone does not remove early resistance");
        check(Concede(50, 0, 1), "Complete exhaustion can end a decisive short conflict");
        check(!Concede(50, 10, 100) && Concede(50, 9.99f, 100), "Ordinary concession uses a strict enthusiasm ceiling");
        check(!Concede(49.99f, 0, 99) && Concede(49.99f, 0, 100), "Internal middle-score fallback waits for the reluctance period");
        check(!Concede(20, .01f, 100) && Concede(20, 0, 100), "Fallback requires actual zero enthusiasm");
        check(!Concede(10, 0, 100) && Concede(10.01f, 0, 100), "Fallback never replaces low-score white peace");
        check(!Concede(-50, 0, 100) && Concede(100, 100, 0), "Score orientation and total victory remain authoritative");
        check(White(10, 0, 0, 1) && White(-10, 0, 0, 1), "White peace accepts either orientation at the score boundary");
        check(!White(10.01f, 0, 0, 100) && !White(-10.01f, 0, 0, 100), "White peace excludes leverage above ten");
        check(!White(0, 0, 10.01f, 100) && !White(0, 10.01f, 0, 100), "Both sides must meet white-peace exhaustion limits");
        check(!White(0, 10, 10, 100), "White-peace eligibility limits do not guarantee willingness");
        check(Near(Part(Assessment(25, 50, 50, 50), "EarlyWarReluctance"), -50), "Configured period scales decay rather than the initial penalty");
        check(Part(Assessment(50, 50, 5, 50), "ProlongedExhaustion") == 30 && Concede(20, 0, 50, 50),
            "Configured period aligns the bonus and prolonged concession fallback");

        for (int day = 0; day <= 150; day += 5)
        for (int score = -100; score <= 100; score += 5)
        for (int enthusiasm = 0; enthusiasm <= 100; enthusiasm += 5)
        {
            float value = Total(day, score, enthusiasm);
            check(!float.IsNaN(value) && !float.IsInfinity(value)
                && Total(day + 1, score, enthusiasm) >= value - .001f
                && (enthusiasm == 0 || Total(day, score, enthusiasm - 1) >= value - .001f),
                "Peace readiness remains finite and increases with time and exhaustion");
        }
    }
}
