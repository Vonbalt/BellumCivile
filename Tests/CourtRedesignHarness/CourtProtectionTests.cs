using System;
using BellumCivile;

internal static class CourtProtectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool already in new[] { false, true })
        {
            int accepted = 0;
            foreach (double ratio in new[] { .25, .5, .75, 1, 1.25, 1.5, 2 })
            foreach (double load in new[] { 0d, .5, 1 })
            foreach (double value in new[] { 0d, 7.5, 15 })
            foreach (double relation in new[] { -100d, 0, 100 })
            foreach (double trait in new[] { -10d, 0, 10 })
            {
                var s = CourtProtectionRules.Score(ratio, load, value, relation, trait, already);
                if (s.WouldAccept) accepted++;
                double expected = 50 + Math.Max(-30, Math.Min(25, (ratio < 1 ? 60 : 40) * (ratio - 1)))
                    - 25 * load + value + relation * .1 + trait + (already ? 15 : 0);
                check(Math.Abs(s.Total - expected) < 0.00001, "Production acceptance reproduces approved simulation");
                check(s.WouldAccept == (expected >= 60), "Acceptance uses unrounded 60 threshold");
                check(CourtProtectionRules.Score(ratio + .1, load, value, relation, trait, already).Total >= s.Total - .00001,
                    "Additional military strength cannot hurt acceptance");
                check(CourtProtectionRules.Score(ratio, load + .1, value, relation, trait, already).Total <= s.Total + .00001,
                    "Other-front burden cannot improve acceptance");
            }
            check(accepted == (already ? 277 : 167), "Production factorial sweep matches standalone simulation totals");
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            check(CourtProtectionRules.Score(bad, 0, 0, 0, 0, false) == null, "Invalid strength fails closed");
            check(CourtProtectionRules.Score(1, bad, 0, 0, 0, false) == null, "Invalid war burden fails closed");
            check(CourtProtectionRules.Score(1, 0, bad, 0, 0, false) == null, "Invalid value fails closed");
            check(CourtProtectionRules.Score(1, 0, 0, bad, 0, false) == null, "Invalid relation fails closed");
            check(CourtProtectionRules.Score(1, 0, 0, 0, bad, false) == null, "Invalid personality fails closed");
            check(!CourtProtectionRules.Desperate(100, bad, 1, 100), "Invalid enemy cannot qualify an applicant");
        }
        check(CourtProtectionRules.Score(-1, 0, 0, 0, 0, false) == null && CourtProtectionRules.Score(1, -1, 0, 0, 0, false) == null,
            "Negative strength ratio or war load is rejected");
        check(CourtProtectionRules.Desperate(100, 200, 1, 149) && !CourtProtectionRules.Desperate(100, 199, 1, 149)
            && !CourtProtectionRules.Desperate(100, 200, 1, 150) && !CourtProtectionRules.Desperate(0, 0, 1, 1)
            && !CourtProtectionRules.Desperate(0, 200, 0, 1), "Applicant boundaries require a real landed realm and threat");
        check(CourtProtectionRules.StrategicValue(CourtProtectionReach.Land, 90, true) == 13
            && CourtProtectionRules.StrategicValue(CourtProtectionReach.Maritime, 90, true) == 11,
            "Geographic value uses explicit land/sea and territorial components");
        foreach (int valor in new[] { -2, -1, 0, 1, 2 })
        foreach (int mercy in new[] { -2, -1, 0, 1, 2 })
        foreach (int honor in new[] { -2, -1, 0, 1, 2 })
        foreach (int calculating in new[] { -2, -1, 0, 1, 2 })
        foreach (double ratio in new[] { .5, 1, 2 })
        {
            double p = CourtProtectionRules.ProtectorPersonality(valor, mercy, honor, calculating, ratio);
            check(p >= -10 && p <= 10, "All normal protector trait combinations respect the agreed cap");
            double stronger = CourtProtectionRules.ProtectorPersonality(valor, mercy, honor, calculating, ratio + .01);
            check(CourtProtectionRules.Score(ratio + .01, 0, 10, 0, stronger, false).Total
                >= CourtProtectionRules.Score(ratio, 0, 10, 0, p, false).Total - .00001,
                "Personality response cannot make a stronger military outlook less acceptable");
            double w = CourtProtectionRules.MotionWeight(2, -50, 20, calculating, valor);
            check(w >= .025 && w <= .5, "Applicant personality modifies priority without forbidding submission");
        }
        check(CourtProtectionRules.ProtectorPersonality(0, 0, 0, 1, .5) == -2.5
            && CourtProtectionRules.ProtectorPersonality(0, 0, 0, 1, 1.5) == 2.5,
            "Calculating ruler prefers a sound military outlook");
        check(CourtProtectionRules.MotionWeight(2, 0, 100, 0, 0) < CourtProtectionRules.MotionWeight(4, -80, 10, 0, 0),
            "Losing war, military imbalance and low enthusiasm increase Crown priority");
    }
}
