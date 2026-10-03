using System;

// Standalone balance experiment. No campaign state, claim creation or marriage execution.
internal static class MarriageStrategySimulation
{
    internal record Side
    {
        public double Risk { get; init; }
        public double OldNeed { get; init; } = 24;
        public bool Domestic { get; init; } = true;
        public bool SameCulture { get; init; } = true;
        public bool Allied { get; init; }
        public bool Fertile { get; init; } = true;
        public bool Receives { get; init; }
        public bool Blood { get; init; } = true;
        public double ClanRelation { get; init; }
        public double PersonalRelation { get; init; }
        public double AgeGap { get; init; }
        public double TierDown { get; init; }
        public double TierUp { get; init; }
        public double Departure { get; init; }
        public double Politics { get; init; }
        public double Claim { get; init; }
        public double ClaimReach { get; init; } = 1;
        public bool ExistingTie { get; init; }
        public double OldPolitics { get; init; }
        public double OldClaim { get; init; }
        public double HeirContinuity { get; init; }
    }

    private static double Baseline(Side s)
    {
        double score = (s.Domestic ? 45 : -35) + (s.SameCulture ? 30 : 0)
            + (s.Allied ? 45 : 0) + .35 * s.ClanRelation + .20 * s.PersonalRelation
            - .75 * s.AgeGap - 18 * s.TierDown * (1 - Math.Clamp(s.OldNeed / 90, 0, 1) * .75);
        if (s.Fertile) score += s.OldNeed * (s.Receives ? .65 : s.Blood ? .25 : 0) + s.HeirContinuity;
        return score - s.Departure + s.OldPolitics + s.OldClaim;
    }

    private static double Proposed(Side s, double survival = 100, double strategyRetention = .75)
    {
        double risk = Math.Clamp(s.Risk, 0, 1);
        double preference = 1 - .75 * risk;
        double strategy = 1 - (1 - strategyRetention) * risk;
        double score = (s.Domestic ? 45 : -35 * preference) + (s.SameCulture ? 30 : 0)
            + (s.Allied ? 45 : 0) + .35 * s.ClanRelation + .20 * s.PersonalRelation
            - .75 * s.AgeGap * preference - 18 * s.TierDown * preference;
        if (s.Fertile)
            score += (s.Receives ? 20 + survival * risk : s.Blood ? 10 * (1 - risk) : 0)
                + s.HeirContinuity;
        double politics = s.ExistingTie ? 0 : Math.Clamp(s.Politics, 0, 100);
        double claims = s.Receives ? Math.Clamp(s.Claim * s.ClaimReach, 0, 85) : 0;
        double prestige = Math.Min(30, 10 * s.TierUp);
        return score - s.Departure + strategy * (politics + claims + prestige);
    }

    private static int checks;
    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }

    private static bool Accepted(double a, double b) => a >= 95 && b >= 95;

    public static void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var receiver = new Side { Receives = true };
        var donor = new Side();
        var desperate = receiver with { Risk = 1, OldNeed = 150 };
        var cases = new (string Name, Side A, Side B)[]
        {
            ("Healthy ordinary peers", receiver, donor),
            ("Healthy peers, relations +30", receiver with { ClanRelation = 30 }, donor with { ClanRelation = 30 }),
            ("Established friends, relations +70", receiver with { ClanRelation = 70 }, donor with { ClanRelation = 70 }),
            ("Existing royal political incentive", receiver with { Politics = 45, OldPolitics = 45 }, donor with { ClanRelation = 70 }),
            ("Healthy useful political partners", receiver with { Politics = 35 }, donor with { Politics = 35 }),
            ("Healthy last-prospect donor, strong partner", receiver with { Politics = 35 }, donor with { Risk = .5, OldNeed = 80, Departure = 25, Politics = 60 }),
            ("Endangered donor, ordinary partner", receiver, donor with { Risk = 1, OldNeed = 150, Departure = 25 }),
            ("Endangered donor, exceptional partner", receiver with { Politics = 50 }, donor with { Risk = 1, OldNeed = 150, Departure = 25, Politics = 100 }),
            ("Endangered receiver marries down two tiers", desperate with { TierDown = 2 }, donor with { TierUp = 2 }),
            ("Same match, no reproductive opportunity", desperate with { TierDown = 2, Fertile = false }, donor with { TierUp = 2, Fertile = false }),
            ("Children present, moderate continuity risk", receiver with { Risk = .25, OldNeed = 55, TierDown = 2 }, donor with { TierUp = 2 }),
            ("Older non-reproductive spouses, useful politics", receiver with { Fertile = false, Politics = 35 }, donor with { Fertile = false, Politics = 35 }),
            ("Departing Crown heir retains lawful rights", receiver, donor with { Risk = .5, OldNeed = 80, Departure = 45, HeirContinuity = 80 }),
            ("Healthy prestige-only, three-tier gap", receiver with { TierUp = 3 }, donor with { TierDown = 3 }),
            ("Useful incoming strong claim", receiver with { Claim = 65, OldClaim = 65 }, donor with { Politics = 20 }),
            ("Useful incoming weak claim", receiver with { Claim = 40, OldClaim = 40 }, donor with { Politics = 20 }),
            ("New marriage birthright forecast", receiver with { Claim = 65 }, donor with { Politics = 20 }),
            ("Remote claim, quarter practical value", receiver with { Claim = 65, ClaimReach = .25, OldClaim = 65 }, donor with { Politics = 20 }),
            ("Uncertain inheritance, quarter confidence", receiver with { Claim = 65, ClaimReach = .25 }, donor with { Politics = 20 }),
            ("Claim attributed to wrong destination", receiver, donor with { Claim = 65, OldClaim = 65 }),
            ("Foreign royal: threatened / secure", receiver with { Domestic = false, Politics = 100, OldPolitics = 65, HeirContinuity = 80 }, donor with { Domestic = false, Politics = 45, OldPolitics = 65 }),
            ("Foreign royal: both share urgent enemy", receiver with { Domestic = false, Politics = 100, OldPolitics = 87.5, HeirContinuity = 80 }, donor with { Domestic = false, Politics = 100, OldPolitics = 87.5 }),
            ("Foreign royal: allied, slots full, useful tie", receiver with { Domestic = false, Allied = true, Politics = 45, OldPolitics = -60 }, donor with { Domestic = false, Allied = true, Politics = 45, OldPolitics = -60 }),
            ("Existing domestic marriage tie", receiver with { Politics = 60, ExistingTie = true }, donor with { Politics = 60, ExistingTie = true }),
            ("Foreign cross-cultural survival match", desperate with { Domestic = false, SameCulture = false, TierDown = 2 }, donor with { Domestic = false, SameCulture = false, TierUp = 2 }),
            ("Borderline mutual political benefit", receiver with { Politics = 10 }, donor with { Politics = 10 }),
        };
        Console.WriteLine("Scenario | Current A/B | Prototype A/B | Mutual acceptance current -> prototype");
        foreach (var c in cases)
        {
            double a = Proposed(c.A), b = Proposed(c.B);
            double oldA = Baseline(c.A), oldB = Baseline(c.B);
            Console.WriteLine($"{c.Name} | {oldA:F2}/{oldB:F2} | {a:F2}/{b:F2} | {Accepted(oldA, oldB)} -> {Accepted(a, b)}");
            Check(Accepted(a, b) == Accepted(b, a), "Proposal orientation does not change independent acceptance");
        }
        // Controlled continuous sweeps, not synthetic campaign acceptance-rate estimates.
        for (int r = 0; r <= 100; r++)
        for (int tier = 0; tier <= 6; tier++)
        {
            var s = receiver with { Risk = r / 100.0, TierDown = tier, AgeGap = 12 };
            Check(Proposed(s with { Risk = Math.Min(1, s.Risk + .01) }) >= Proposed(s) - 1e-9,
                "Survival pressure never worsens plain fertile receiving match");
            Check(Proposed(s with { Claim = 85 }) >= Proposed(s), "Obtainable claim helps receiver");
            var d = s with { Receives = false };
            Check(Proposed(d with { Claim = 85 }) == Proposed(d), "Outgoing claim is not incoming benefit");
            Check(Proposed(s with { ExistingTie = true, Politics = 100 }) == Proposed(s), "No repeated new-tie utility");
        }
        Check(!Accepted(200, 94.99) && Accepted(95, 95), "Both sides retain 95 floor");
        Check(Proposed(desperate with { Fertile = false }) < Proposed(desperate), "No survival reward without reproductive opportunity");
        Check(Proposed(donor with { Risk = 1, Departure = 25, Politics = 100 }) >= 95,
            "Exceptional political benefit can outweigh even last-prospect loss");
        Console.WriteLine("Sensitivity: endangered foreign cross-cultural receiver, two-tier decline");
        foreach (double survival in new[] { 80d, 100d, 120d })
            Console.WriteLine($"Survival +{survival}: {Proposed(desperate with { Domestic = false, SameCulture = false, TierDown = 2 }, survival):F2}");
        Console.WriteLine("Sensitivity: endangered donor, last prospect, exceptional partner");
        foreach (double retention in new[] { .5, .75, 1.0 })
            Console.WriteLine($"Strategy retained {retention:P0}: {Proposed(donor with { Risk = 1, Departure = 25, Politics = 100 }, strategyRetention: retention):F2}");
        Console.WriteLine($"Marriage strategy prototype: {checks} checks passed. No campaign mutation.");
    }
}
