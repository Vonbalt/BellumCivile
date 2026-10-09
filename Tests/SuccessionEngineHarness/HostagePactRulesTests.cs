using System;
using System.Linq;
using BellumCivile;
using HarmonyLib;

internal static class HostagePactRulesTests
{
    internal static void Run(Action<bool, string> check)
    {
        Type rules = typeof(TreatyTermRecord).Assembly.GetType("BellumCivile.HostagePactRules", true);
        object Call(string name, params object[] args) => AccessTools.Method(rules, name).Invoke(null, args);
        check((int)AccessTools.Field(rules, "DefaultDurationDays").GetRawConstantValue() == 50,
            "Hostage pact duration defaults to 50 days");
        check((int)AccessTools.Field(rules, "MaximumDurationDays").GetRawConstantValue() == 1000,
            "Hostage pact duration permits up to 1000 days");
        check((bool)Call("IsValidDuration", 0) && (bool)Call("IsValidDuration", 1000)
            && !(bool)Call("IsValidDuration", -1) && !(bool)Call("IsValidDuration", 1001),
            "Hostage duration validates both slider boundaries");
        check((int)Call("GetTier", 0) == 4 && (int)Call("GetTier", -1) == 4
            && (int)Call("GetTier", 20) == 4, "Unranked and later heirs use lowest hostage tier");
        for (int tier = 1; tier <= 4; tier++)
        {
            check((int)Call("GetTier", tier) == tier, "Hostage ranking preserves rightful position");
            check((int)Call("GetTreatyCost", tier) == new[] { 30, 24, 18, 12 }[tier - 1], "Hostage tier cost");
            check((double)Call("GetHouseReluctance", tier, 0, 0) == new[] { 60, 45, 30, 20 }[tier - 1],
                "Neutral supplying house reluctance");
            check((double)Call("GetWarDeterrence", tier, 0, 0, true, true) == 105 - 15 * tier,
                "Neutral ruler hostage deterrence");
        }
        int cases = 0;
        for (int h = -2; h <= 2; h++)
        for (int m = -2; m <= 2; m++)
        for (int c = -2; c <= 2; c++)
        for (int r = -100; r <= 100; r += 25)
        for (int tier = 1; tier <= 4; tier++)
        foreach (bool supplier in new[] { false, true })
        foreach (bool allowed in new[] { false, true })
        {
            var p = (double[])Call("GetDispositionProbabilities", tier, h, m, c, r, supplier, allowed);
            if (p.Any(v => double.IsNaN(v) || v < 0 || v > 1) || Math.Abs(p.Sum() - 1) > 1e-12
                || (!allowed && p[2] != 0)) throw new Exception("Invalid hostage distribution");
            if (Call("ChooseDisposition", tier, h, m, c, r, supplier, allowed, 0d).ToString() != "Release"
                || Call("ChooseDisposition", tier, h, m, c, r, supplier, allowed, .999999999d).ToString()
                    != (allowed ? "Execute" : "Retain")) throw new Exception("Invalid hostage roll boundary");
            cases++;
        }
        check(cases == 18000, "All 18000 hostage trait/breach/death-protection combinations pass");
        var extreme = (double[])Call("GetDispositionProbabilities", 1, 200, -200, 200, -1000, true, true);
        var bounded = (double[])Call("GetDispositionProbabilities", 1, 2, -2, 2, -100, true, true);
        check(extreme.SequenceEqual(bounded), "External trait and relation values are bounded");
        check(Math.Abs((double)Call("GetCouncilDeterrence", 1, 0, 0, true, true) - 54) < 1e-9,
            "Council restraint remains separate from clamped target score");
        bool ordered = true, scaled = true;
        for (int tier = 1; tier <= 4; tier++)
        for (int honor = -2; honor <= 2; honor++)
        for (int mercy = -2; mercy <= 2; mercy++)
        {
            double ruler = (double)Call("GetWarDeterrence", tier, honor, mercy, true, true);
            double vassal = (double)Call("GetWarDeterrence", tier, honor, mercy, true, false);
            double captor = (double)Call("GetWarDeterrence", tier, honor, mercy, false, false);
            ordered &= ruler > vassal && vassal > captor && captor > 0;
            scaled &= Math.Abs((double)Call("GetCouncilDeterrence", tier, honor, mercy, true, true) - .6 * ruler) < 1e-9;
        }
        check(ordered, "All 100 tier/personality combinations restrain supplying house most strongly");
        check(scaled, "All 100 council deterrence combinations retain agreed 60 percent scale");
    }
}
