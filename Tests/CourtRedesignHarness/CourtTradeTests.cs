using System;
using BellumCivile;

internal static class CourtTradeTests
{
    internal static void Run(Action<bool, string> check)
    {
        for (int i = 0; i <= 1000; i++)
        {
            float score = i / 10f;
            float after = CourtTradeRules.Support(score, true);
            check(after >= score && after <= 100, "Trade bonus bounded and nonnegative");
            check(CourtTradeRules.Support(score, false) == score, "Ineligible trade voter unchanged");
            check((after > 50) == (score > 35), "Trade preference threshold");
            check(Math.Abs((after - (100 - after)) - (score - (100 - score)) - 2 * (after-score)) < .0001,
                "Trade yes/no complement moves consistently");
        }
        check(CourtTradeRules.InWindow(10, 10, 20) && CourtTradeRules.InWindow(20, 10, 20), "Trade includes exact term boundaries");
        check(!CourtTradeRules.InWindow(20.01, 10, 20) && !CourtTradeRules.InWindow(double.NaN, 10, 20), "Trade rejects late or invalid signature");
        check(CourtTradeRules.WarResult(true, true, 15, 20) == CourtObjectiveState.Failed, "Own deliberate war fails trade");
        check(CourtTradeRules.WarResult(false, true, 15, 20) == CourtObjectiveState.Cancelled, "Enemy attack cancels trade neutrally");
        check(CourtTradeRules.WarResult(true, false, 15, 20) == CourtObjectiveState.Cancelled, "Propagated war cancels neutrally");
        check(CourtTradeRules.WarResult(true, true, 21, 20) == CourtObjectiveState.Expired, "Late war cannot change expired result");
        var objective = new CourtObjectiveRecord();
        objective.FreezeTerm(10,20);
        check(objective.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "signed") && objective.TryClaimResult(), "Trade receipt claims once");
        check(!objective.TryClaimResult() && !objective.Finish(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "late"), "Repeated event cannot reverse success");
        check(new CourtTradeRecord().SignedDay == -1, "Absent signature has explicit sentinel");
    }
}
