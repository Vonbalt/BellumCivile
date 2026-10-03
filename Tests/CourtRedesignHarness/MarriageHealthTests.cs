using System;
using BellumCivile.Behaviors;

internal static class MarriageHealthTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(MarriageHealthRules.OutgoingKinshipValue(0, true, true, true) == 30
            && MarriageHealthRules.OutgoingKinshipValue(.5f, true, true, true) == 15,
            "Healthy and vulnerable sending houses value outgoing kinship in proportion to security");
        check(MarriageHealthRules.OutgoingKinshipValue(1, true, true, true) == 0
            && MarriageHealthRules.OutgoingKinshipValue(0, false, true, true) == 0
            && MarriageHealthRules.OutgoingKinshipValue(0, true, false, true) == 0
            && MarriageHealthRules.OutgoingKinshipValue(0, true, true, false) == 0,
            "Outgoing kinship does not subsidize extinction, infertility, non-blood members or receiving houses");
        check(MarriageHealthRules.Risk(1, 0, 1, 0) == 1, "Isolated unmarried house has maximum continuity pressure");
        check(MarriageHealthRules.Risk(4, 2, 2, 1) == 0, "Established reproductive household is healthy");
        check(MarriageHealthRules.Risk(3, 2, 1, 0) == .25f, "Children protect continuity without pretending to be spouses");
        for (int adults = 0; adults <= 15; adults++)
        for (int children = 0; children <= 8; children++)
        for (int prospects = 0; prospects <= adults; prospects++)
        for (int capacity = 0; capacity <= 10; capacity++)
        {
            float risk = MarriageHealthRules.Risk(adults, children, prospects, capacity / 10f);
            check(risk >= 0 && risk <= 1, "House risk bounded");
            check(MarriageHealthRules.Risk(adults, children + 1, prospects, capacity / 10f) <= risk + .0001f,
                "A child never increases extinction pressure");
            check(MarriageHealthRules.Risk(adults, children, prospects + 1, capacity / 10f) <= risk + .0001f,
                "Another prospect never increases extinction pressure");
            check(MarriageHealthRules.Risk(adults, children, prospects, (capacity + 1) / 10f) <= risk + .0001f,
                "Additional reproductive coverage never increases risk");
            check(MarriageHealthRules.Continuity(risk, false, true, true) == 0,
                "No household fertility reward for a non-reproductive marriage");
            check(MarriageHealthRules.StrategyScale(risk) >= .75f, "Strategy retains meaning under survival pressure");
        }
        check(MarriageHealthRules.ReproductiveCoverage(30) == 1 && MarriageHealthRules.ReproductiveCoverage(40) == .5f
            && MarriageHealthRules.ReproductiveCoverage(45) == 0, "Reproductive cover fades with age instead of a single cliff");
        check(MarriageHealthRules.Continuity(1, true, false, true) == 0,
            "Children destined for another household do not solve own household extinction");
        check(MarriageHealthRules.ForeignDistanceCost(0, true) == 5
            && MarriageHealthRules.ForeignDistanceCost(1, true) == 35
            && MarriageHealthRules.ForeignDistanceCost(1, false) == 5,
            "Foreign distance weighs on an endangered departing house, not on a receiving house");
        check(MarriageHealthRules.Prestige(1, 6) == 30 && MarriageHealthRules.Prestige(6, 1) == 0,
            "Upward prestige capped and directional");
    }
}
