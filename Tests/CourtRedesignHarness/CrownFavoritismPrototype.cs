using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;

// Experimental decision math only. Not compiled into Bellum's gameplay assembly.
internal static class CrownFavoritismPrototype
{
    private static readonly FactionType[] Blocs = { FactionType.Nobility, FactionType.Glory, FactionType.Liberty };
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }
    private static double Clamp(double n, double min, double max) => Math.Max(min, Math.Min(max, n));
    private sealed class Court
    {
        public string Name;
        public int[] Houses;
        public double[] Mood;
        public int Eligible;
        public Court(string name, int[] houses, double[] mood, int eligible = 0)
        { Name = name; Houses = houses; Mood = mood; Eligible = eligible > 0 ? eligible : houses.Sum(); }
    }
    private sealed class Result
    {
        public double[] Affinity = new double[3], Size = new double[3], Appeasement = new double[3], Risk = new double[3], Score = new double[3];
        public int Choice = -1;
    }
    private static Result Evaluate(int[] traits, Court court, bool useRisk = true, double tieTolerance = 0)
    {
        var raw = CourtAffiliationMath.Personality(traits[0], traits[1], traits[2], traits[3], traits[4]);
        var r = new Result();
        for (int i = 0; i < 3; i++)
        {
            double share = court.Eligible == 0 ? 0 : (double)court.Houses[i] / court.Eligible;
            // One common scale preserves the existing relative trait weights.
            r.Affinity[i] = Clamp(raw[Blocs[i]] / 4.0, -10, 10);
            r.Size[i] = 5 * share;
            r.Appeasement[i] = 10 * share * Clamp(-court.Mood[i] / 60, 0, 1);
            if (useRisk)
                for (int j = 0; j < 3; j++)
                    if (j != i && court.Eligible > 0)
                        r.Risk[i] += 5.0 * court.Houses[j] / court.Eligible * Clamp((-court.Mood[j] - 40) / 20, 0, 1);
            r.Score[i] = court.Houses[i] == 0 ? double.NegativeInfinity : r.Affinity[i] + r.Size[i] + r.Appeasement[i] - r.Risk[i];
        }
        int[] order = Enumerable.Range(0, 3).Where(i => court.Houses[i] > 0).OrderByDescending(i => r.Score[i]).ToArray();
        if (order.Length > 0 && r.Score[order[0]] >= 5
            && (order.Length == 1 || r.Score[order[0]] - r.Score[order[1]] > tieTolerance + 1e-9)) r.Choice = order[0];
        return r;
    }
    private static string Label(int choice) => choice < 0 ? "Impartial" : Blocs[choice].ToString();
    internal static void Run()
    {
        var courts = new[]
        {
            new Court("Balanced calm", new[]{3,3,3}, new double[]{0,0,0}),
            new Court("Glory dominates", new[]{2,6,2}, new double[]{0,0,0}),
            new Court("Glory crisis", new[]{2,6,2}, new double[]{0,-60,0}),
            new Court("Small Glory crisis", new[]{5,1,4}, new double[]{0,-60,0}),
            new Court("Two crises", new[]{4,4,2}, new double[]{-60,-60,0}),
            new Court("All rebellious", new[]{3,3,3}, new double[]{-60,-60,-60}),
            new Court("Two single-house blocs", new[]{1,1,0}, new double[]{0,0,0}),
            new Court("One bloc, others unaligned", new[]{1,0,0}, new double[]{0,0,0}, 4),
            new Court("Only one noble house", new[]{1,0,0}, new double[]{0,0,0}),
            new Court("No blocs", new[]{0,0,0}, new double[]{0,0,0}, 3)
        };
        var rulers = new Dictionary<string,int[]>
        {
            ["Neutral"] = new[]{0,0,0,0,0},
            ["Nobility inclined"] = new[]{2,-1,0,0,1},
            ["Glory inclined"] = new[]{0,0,-1,2,-1},
            ["Liberty inclined"] = new[]{1,2,1,0,0}
        };
        Console.WriteLine("Prototype: affinity=raw/4; size=5*share; appease=10*share*clamp(-mood/60); risk=sum(5*otherShare*clamp((-otherMood-40)/20)); threshold=5; exact ties=impartial.");
        Console.WriteLine("Scenario | ruler | N / G / L scores | choice");
        foreach (Court court in courts)
            foreach (var ruler in rulers)
            {
                Result r = Evaluate(ruler.Value, court);
                Console.WriteLine($"{court.Name} | {ruler.Key} | {string.Join(" / ", r.Score.Select(s => double.IsNegativeInfinity(s) ? "absent" : s.ToString("0.00")))} | {Label(r.Choice)}");
            }

        // Exhaustive synthetic grid, not an estimate of Bannerlord's NPC population.
        Console.WriteLine("Exhaustive 3,125 trait combinations per court: N / G / L / impartial; risk-changed choices; one-point deadband changes");
        foreach (Court court in courts)
        {
            int[] counts = new int[4]; int riskChanges = 0, deadbandChanges = 0;
            for (int code = 0; code < 3125; code++)
            {
                int n = code; int[] traits = new int[5];
                for (int axis = 0; axis < 5; axis++) { traits[axis] = n % 5 - 2; n /= 5; }
                Result r = Evaluate(traits, court);
                var raw = CourtAffiliationMath.Personality(traits[0], traits[1], traits[2], traits[3], traits[4]);
                for (int i = 0; i < 3; i++)
                {
                    double live = CrownFavoritismMath.Score(raw[Blocs[i]], i, court.Houses, court.Mood, court.Eligible);
                    Check(double.IsNegativeInfinity(live) && double.IsNegativeInfinity(r.Score[i]) || Math.Abs(live - r.Score[i]) < 1e-9,
                        "Production score matches tested prototype");
                }
                counts[r.Choice < 0 ? 3 : r.Choice]++;
                if (r.Choice != Evaluate(traits, court, false).Choice) riskChanges++;
                if (r.Choice != Evaluate(traits, court, true, 1).Choice) deadbandChanges++;
                Check(r.Choice < 0 || court.Houses[r.Choice] > 0, "Absent bloc never selected");
                Check(r.Affinity.All(x => x >= -10 && x <= 10), "Affinity bounded");
                Check(r.Size.All(x => x >= 0 && x <= 5), "Size bounded");
                Check(r.Appeasement.All(x => x >= 0 && x <= 10), "Appeasement bounded");
                Check(r.Risk.All(x => x >= 0 && x <= 5), "Risk bounded");
                Check(r.Choice < 0 || r.Score[r.Choice] >= 5, "Threshold respected");
            }
            Console.WriteLine($"{court.Name}: {string.Join(" / ", counts)}; {riskChanges}; {deadbandChanges}");
        }
        var neutral = rulers["Neutral"];
        Check(Evaluate(neutral, courts[0]).Choice == -1, "Calm neutral ruler remains impartial");
        Check(Evaluate(neutral, courts[2]).Choice == 1, "Large threatened bloc can attract neutral ruler");
        Check(Evaluate(neutral, courts[3]).Choice == -1, "One angry house does not dominate neutral ruler");
        Check(Evaluate(neutral, courts[5]).Choice == -1, "Symmetric crises do not choose by enum order");
        Check(Evaluate(rulers["Nobility inclined"], courts[0]).Choice == 0, "Personal preference in calm court");
        Check(Evaluate(rulers["Nobility inclined"], courts[2]).Choice == 1, "Crisis can overcome personal preference");
        Check(Math.Abs(Evaluate(neutral, courts[3]).Risk[0] - .5) < 1e-9, "Small-bloc risk retains fractional share");

        // A simple target-convergence illustration, not the game's daily mood simulation.
        Console.WriteLine("Repeated-term probe: fixed non-favor baseline -55 for three equal blocs, neutral ruler; full convergence to previous term's target.");
        foreach (bool adjusted in new[]{false,true})
        {
            int previous = -1; var choices = new List<string>();
            for (int term = 0; term < 8; term++)
            {
                double[] mood = new[]{-55.0,-55.0,-55.0};
                if (!adjusted && previous >= 0)
                    for (int i = 0; i < 3; i++) mood[i] += i == previous ? 10 : -5;
                // Start with a prior Glory favoritism to test persistence/rotation.
                if (term == 0 && !adjusted) mood = new[]{-60.0,-45.0,-60.0};
                previous = Evaluate(neutral, new Court("Repeat", new[]{3,3,3}, mood)).Choice;
                choices.Add(Label(previous));
            }
            Console.WriteLine($"{(adjusted ? "Underlying baseline" : "Actual converged mood")}: {string.Join(" -> ",choices)}");
        }
        foreach (double baseline in new[]{-20.0,-40.0,-55.0})
        {
            int[] rotating = new int[2]; string example = null;
            for (int code = 0; code < 3125; code++)
            {
                int n = code; int[] traits = new int[5];
                for (int axis = 0; axis < 5; axis++) { traits[axis] = n % 5 - 2; n /= 5; }
                for (int mode = 0; mode < 2; mode++)
                {
                    var choices = new List<int>(); int previous = -1;
                    for (int term = 0; term < 8; term++)
                    {
                        double[] mood = new[]{baseline,baseline,baseline};
                        if (mode == 0 && previous >= 0)
                            for (int i = 0; i < 3; i++) mood[i] += i == previous ? 10 : -5;
                        previous = Evaluate(traits, new Court("Feedback",new[]{3,3,3},mood)).Choice;
                        choices.Add(previous);
                    }
                    if (choices.Skip(2).Distinct().Count() > 1)
                    {
                        rotating[mode]++;
                        if (mode == 0 && example == null) example = $"traits={string.Join(",",traits)}: {string.Join(" -> ",choices.Select(Label))}";
                    }
                    if (mode == 1) Check(choices.Distinct().Count() == 1, "Unchanged underlying conditions give stable choice");
                }
            }
            Console.WriteLine($"Feedback baseline {baseline}: rotating actual/underlying = {rotating[0]}/{rotating[1]} out of 3125; example {example ?? "none"}");
        }
        Console.WriteLine($"PASS: {_checks} prototype checks. No campaign simulation, UI, saved state or production balance changes.");
    }
}
