using System;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class TreatyWinnerLeverageTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(TreatyTermRecord).Assembly;
        var financial = assembly.GetType("BellumCivile.TreatyDraftService")
            .GetMethod("GetAffordableFinancialDemandValue", BindingFlags.Static | BindingFlags.NonPublic);
        var tribute = assembly.GetType("BellumCivile.TreatyAiDraftService")
            .GetMethod("GetAffordableTributeScore", BindingFlags.Static | BindingFlags.NonPublic);
        var desiredSpend = assembly.GetType("BellumCivile.TreatyAiDraftService")
            .GetMethod("CalculateDesiredSpend", BindingFlags.Static | BindingFlags.Public);
        var winner = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var loser = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        foreach (int budget in new[] { 0, 11, 20, 50, 80, 100 })
        {
            var proposal = new TreatyProposalRecord("war", "winner", "loser", "winner", 0, budget, false);
            check((int)desiredSpend.Invoke(null, new object[] { proposal, winner, loser, winner, null }) == budget,
                "NPC winner initially targets its whole earned budget, including granular financial remainder");
        }
        int Value(long gold, int remaining, long reparationsRate = 5000, long tributeRate = 25000)
            => (int)financial.Invoke(null, new object[] { gold, remaining, reparationsRate, tributeRate });
        int Tribute(long gold, int remaining, long reparationsRate = 5000, long tributeRate = 25000)
            => (int)tribute.Invoke(null, new object[] { gold, remaining, reparationsRate, tributeRate });

        check(Value(4999, 40) == 0, "Sub-point financial capacity forgives unused leverage");
        check(Value(5000, 40) == 1, "One affordable point does not expose all unused leverage");
        check(Value(14999, 40) == 2, "Financial capacity rounds down to complete points");
        check(Value(1000000, 7) == 7, "Financial leverage is bounded by remaining score");
        check(Value(-1, 40) == 0 && Value(5000, -1) == 0, "Negative capacity and score clamp to zero");
        check(Value(24999, 40, 0) == 0 && Value(25000, 40, 0) == 1,
            "Tribute-only capacity includes the full hundred-day burden");
        check(Value(50000, 40, 0, 0) == 0, "Disabled financial rates provide no leverage");
        check(Tribute(25000, 5) == 0, "Tribute preference must not sacrifice five attainable reparations points");
        check(Tribute(45000, 5) == 1, "Surplus wealth allows tribute without reducing total leverage");
        check(Tribute(125000, 5) == 5, "Fully affordable tribute retains preference");
        check(Tribute(25000, 5, 0) == 1, "Tribute works when reparations are disabled");
        check(Tribute(25000, 5, 5000, 0) == 0, "Disabled tribute cannot consume wealth");
        check(Tribute(25000, 5, 25000, 5000) == 5, "Cheaper configured tribute maximizes leverage");

        foreach (long gold in new long[] { 0, 4999, 5000, 24999, 25000, 45000, 125000, 1000000 })
        {
            for (int remaining = 0; remaining <= 100; remaining++)
            {
                int tributeScore = Tribute(gold, remaining);
                long balance = gold - tributeScore * 25000L;
                int reparationsScore = Value(balance, remaining - tributeScore, 5000, 0);
                check(balance >= 0 && tributeScore + reparationsScore == Value(gold, remaining),
                    "Preferred tribute plus reparations exhausts attainable leverage within affordability");
            }
        }
    }
}
