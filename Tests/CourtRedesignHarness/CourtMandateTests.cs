using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.SaveSystem;

internal static class CourtMandateTests
{
    internal static void Run(Action<bool, string> check)
    {
        var terms = new[] { 1, 5, 10, 0 };
        for (int i = 0; i < terms.Length; i++)
        foreach (int direction in new[] { -1, 1 })
        {
            int next = i + direction;
            check(CourtMandateRules.Next(terms[i], direction) == (next < 0 || next >= terms.Length ? (int?)null : terms[next]), "Only adjacent mandates allowed");
        }
        check(CourtMandateRules.Next(2, 1) == null && CourtMandateRules.Next(5, 2) == null, "Invalid term and skip rejected");
        foreach (int direction in new[] { -1, 1 })
        foreach (int lean in new[] { -20, 0, 20 })
        for (int relation = -100; relation <= 100; relation++)
        for (int honor = -2; honor <= 2; honor++)
        {
            double expected = Math.Max(0, Math.Min(100, 45 + direction * lean + relation * .1 - 5 * Math.Max(0, honor)));
            float score = CourtMandateRules.Support(direction, lean, relation, honor, false);
            check(Math.Abs(score - expected) < .00001, "Production natural utility matches simulated contract");
            check(CourtMandateRules.Support(direction, lean, relation, honor, true) >= score, "Aligned Crown cannot reduce reform support");
        }
        check(CourtMandateRules.Price(10, 0, 0, 0) == 50000 && CourtMandateRules.Price(11, 0, 0, 0) == 100000, "Neutral price boundary");
        check(CourtMandateRules.Price(50, 0, 0, 0) == 100000 && CourtMandateRules.Price(51, 0, 0, 0) == 150000, "Strong resistance price boundary");
        check(CourtMandateRules.Openness(60, 0, 1, 0, 0, 0) == 10 && CourtMandateRules.Openness(60, 80, 1, 0, 0, 0) == 42, "Honor refusal and trusted exception");
        for (int requested = 0; requested <= 100; requested++)
            check(CourtMandateRules.Resistance(requested) == Math.Max(0, 100 - requested * 2), "Direction-specific resistance");
        foreach (var type in new[] { typeof(CourtMandateRecord), typeof(CourtMandatePledge) })
        {
            var ids = type.GetFields().Select(f => f.GetCustomAttribute<SaveableFieldAttribute>()?.Id).ToArray();
            check(ids.All(x => x.HasValue) && ids.Distinct().Count() == ids.Length, "Mandate receipt fields saved uniquely");
        }
    }
}
