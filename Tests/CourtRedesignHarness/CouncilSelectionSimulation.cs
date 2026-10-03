using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;

internal static class CouncilSelectionSimulation
{
    private sealed class Source : ICourtObjectiveSource<int, int>
    {
        public string Kind { get; }
        internal readonly double[] Weights;
        internal bool Available = true;
        internal int Evaluations;
        internal Source(string kind, params double[] weights) { Kind = kind; Weights = weights; }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(int context, int owner)
        {
            for (int i = 0; i < Weights.Length; i++)
                yield return new CourtObjectiveCandidate(i.ToString(), Kind == "council_appointment" && i % 2 == 0 ? "fill" : "replace");
        }
        public CourtObjectiveEvaluation EvaluateCandidate(int context, int owner, CourtObjectiveCandidate candidate)
        {
            Evaluations++;
            return new CourtObjectiveEvaluation(Available, Available,
                new CourtObjectiveWeight(Weights[int.Parse(candidate.TargetId)]), "simulation fixture");
        }
    }

    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        const int draws = 6000;
        foreach (double councilWeight in new[] { .75, 1, 1.5, 2, 3 })
        {
            int? reference = null;
            foreach (int policyCount in new[] { 1, 10, 100 })
            foreach (int officeCount in new[] { 1, 4, 8 })
            {
                var selector = new CourtObjectiveSelector<int, int>();
                var policies = new Source("policy", Enumerable.Repeat(1d, policyCount).ToArray());
                var council = new Source("council_appointment", Enumerable.Repeat(councilWeight, officeCount).ToArray());
                selector.Register(policies);
                selector.Register(new Source("revoke_fief", 1));
                selector.Register(council);
                int selected = 0;
                var random = new Random(1909);
                for (int i = 0; i < draws; i++)
                {
                    int calls = 0;
                    var choice = selector.Select(0, 0, () => calls++ == 0 ? (i + .5) / draws : random.NextDouble());
                    if (choice.Kind == council.Kind) selected++;
                }
                double expected = councilWeight / (2 + councilWeight);
                Check(Math.Abs(selected / (double)draws - expected) <= 1d / draws, "Category share differs from expected weight");
                Check(reference == null || selected == reference, "Extra policies or offices distorted category chance");
                Check(policies.Evaluations == policyCount * draws && council.Evaluations == officeCount * draws,
                    "Candidate evaluation count was not bounded per selection");
                reference = selected;
            }
            Console.WriteLine($"Council weight {councilWeight:0.##}: {reference * 100d / draws:0.00}% against policy=1 and revocation=1; invariant across 1/10/100 policies and 1/4/8 offices.");
        }

        // Once the category wins, lower-weight offices must not displace its best opportunity unfairly.
        var offices = new CourtObjectiveSelector<int, int>();
        offices.Register(new Source("council_appointment", 3, 1, 1, 1));
        var selectedOffices = new int[4];
        for (int i = 0; i < draws; i++)
            selectedOffices[int.Parse(offices.Select(0, 0, () => (i + .5) / draws).Candidate.TargetId)]++;
        Check(selectedOffices.SequenceEqual(new[] { 3000, 1000, 1000, 1000 }), "Within-category target weighting changed");
        Console.WriteLine("Office weights 3:1:1:1: preferred office receives 50% of council selections, not a guaranteed selection.");

        var blocked = new CourtObjectiveSelector<int, int>();
        blocked.Register(new Source("policy", 1));
        var unavailable = new Source("council_appointment", 1000) { Available = false };
        blocked.Register(unavailable);
        Check(blocked.Select(0, 0, () => .99).Kind == "policy", "Weight bypassed unavailable council proceeding");
        unavailable.Available = true;
        Check(blocked.Select(0, 0, () => .99).Kind == "council_appointment", "New vacancy could not reopen council eligibility");

        RunArbitration(Check);
        Console.WriteLine($"{checks} deterministic selector checks passed. No campaign votes, support forecasts or runtime cache benchmarks were simulated.");
    }

    internal static void RunArbitration(Action<bool, string> check)
    {
        foreach (var owners in new[] { new[] { 0, 1, 2, 3 }, new[] { 3, 2, 1, 0 } })
        {
            var selector = new CourtObjectiveSelector<int, int>();
            var policy = new Source("policy", 1);
            var council = new Source("council_appointment", 2);
            selector.Register(policy);
            selector.Register(council);
            var wins = new int[4];
            for (int i = 0; i < 6000; i++)
            {
                int draws = 0;
                var result = selector.SelectBatch(0, owners, council.Kind,
                    () => ++draws <= 4 ? .99 : (i + .5) / 6000);
                check(result.Count(c => c?.Kind == council.Kind) == 1, "Exactly one shared council proceeding wins");
                check(result.Count(c => c?.Kind == policy.Kind) == 3, "Other sponsors fall back to ordinary business");
                check(draws == 5, "One equal lottery follows four tentative choices, with no extra retries");
                wins[owners[Enumerable.Range(0, 4).Single(n => result[n].Kind == council.Kind)]]++;
            }
            check(wins.All(n => n == 1500), "Every sponsor receives an equal share even with reversed enumeration order");
            check(policy.Evaluations == 24000 && council.Evaluations == 24000,
                "Fallback never re-evaluates candidates or repeats support forecasts");
        }

        var onlyCouncil = new CourtObjectiveSelector<int, int>();
        onlyCouncil.Register(new Source("council_appointment", 2));
        var emptyFallback = onlyCouncil.SelectBatch(0, new[] { 0, 1 }, "council_appointment", () => .75);
        check(emptyFallback[0] == null && emptyFallback[1]?.Kind == "council_appointment",
            "Unavailable fallback leaves the losing sponsor without a motion");
        check(onlyCouncil.SelectBatch(0, new[] { 0 }, "council_appointment", () => throw new Exception("Unexpected draw"))[0] != null,
            "An uncontested council choice consumes no arbitration draw");
        check(onlyCouncil.SelectBatch(0, Array.Empty<int>(), "council_appointment", () => throw new Exception("Unexpected draw")).Count == 0,
            "Empty term selection is harmless");
        var ordinary = new CourtObjectiveSelector<int, int>();
        ordinary.Register(new Source("policy", 1));
        ordinary.Register(new Source("council_appointment", 1000) { Available = false });
        check(ordinary.SelectBatch(0, new[] { 0, 1, 2 }, "council_appointment", () => throw new Exception("Unexpected draw"))
            .All(c => c.Kind == "policy"), "Ineligible council choices never enter arbitration");
        Console.WriteLine("Actual batch arbitration: equal sponsor shares in both owner orders; one cached fallback per loser; unavailable/empty/single-owner cases passed.");
    }
}
