using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;

internal static class CourtObjectiveFrameworkTests
{
    private sealed class Source : ICourtObjectiveSource<int, int>
    {
        public string Kind { get; }
        internal int Count, Evaluated;
        internal double Weight = 1;
        internal Func<string, double> TargetWeight;
        internal bool Eligible = true, Viable = true, Duplicate;
        internal Source(string kind, int count) { Kind = kind; Count = count; }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(int context, int owner)
        {
            for (int i = 0; i < Count; i++)
                yield return new CourtObjectiveCandidate(Duplicate ? "same" : i.ToString(), "test");
        }
        public CourtObjectiveEvaluation EvaluateCandidate(int context, int owner, CourtObjectiveCandidate candidate)
        {
            Evaluated++;
            return new CourtObjectiveEvaluation(Eligible, Viable, new CourtObjectiveWeight(TargetWeight?.Invoke(candidate.TargetId) ?? Weight), "test");
        }
    }

    private sealed class TrackedObjective : ICourtObjectiveProgress<string>
    {
        public CourtObjectiveOutcome EvaluateProgress(CourtObjectiveRecord objective, string target) =>
            target == objective.TargetId ? new CourtObjectiveOutcome(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "achieved") : null;
        public CourtObjectiveOutcome EvaluateDeadline(CourtObjectiveRecord objective) => null;
    }

    internal static void Run(Action<bool, string> check)
    {
        CouncilSelectionSimulation.RunArbitration(check);
        CourtActivityTests.Run(check);
        foreach (bool vacant in new[] { false, true })
        foreach (double days in new[] { 0d, 7, 8, 30, 90, 365 })
        foreach (double controversy in new[] { 0d, 40, 60, 100 })
        foreach (double merit in new[] { -20d, 0, 8, 40, 100 })
        {
            var normal = CourtCouncilWeights.Calculate(vacant, days, controversy, merit, false);
            var favored = CourtCouncilWeights.Calculate(vacant, days, controversy, merit, true);
            check(normal.Total >= (vacant ? 1 : .75) && favored.Total <= (vacant ? 2.75 : 2.25),
                "Council category weights remain comparable to ordinary agenda categories");
            check(Math.Abs(favored.Total - normal.Total - .25) < .000001,
                "Office preference contributes exactly once");
        }
        check(CourtCouncilWeights.Calculate(true, 7, 100, 100, false).Total == 1,
            "Vacancy weight does not inherit incumbent controversy or merit-gap penalties");
        check(CourtCouncilWeights.Calculate(false, 365, 0, -10, false).Total == .75,
            "Replacement urgency does not grow with irrelevant vacancy duration");
        var beneficiary = new CourtObjectiveCandidate("Marshal", "fill", "preferred_house");
        check(beneficiary.BeneficiaryId == "preferred_house", "Council target carries its nominated house independently of the office");
        for (int fiefs = 0; fiefs < 20; fiefs++)
        {
            check(CourtExecutiveRules.GrantEligible(fiefs) == (fiefs > 5), "Crown grants require more than five fiefs");
            check(CourtExecutiveRules.LibertyRevocationEligible(fiefs) == (fiefs >= 5), "Liberty revocation begins at five fiefs");
        }
        check(CourtExecutiveRules.CanOverwrite(false, true, 100, 50), "Unfiled future Crown session permits decree overwrite");
        check(!CourtExecutiveRules.CanOverwrite(true, true, 100, 50), "Filed Crown motion always defers decree");
        check(!CourtExecutiveRules.CanOverwrite(false, false, 100, 50), "Fulfilled Crown term defers decree");
        check(!CourtExecutiveRules.CanOverwrite(false, true, 100, 100), "Past or reached session cannot be retroactively replaced");
        for (int generosity = -2; generosity <= 2; generosity++)
            for (int honor = -2; honor <= 2; honor++)
                for (int calculating = -2; calculating <= 2; calculating++)
                {
                    double normal = CourtExecutiveRules.GrantWeight(false, false, generosity, honor, calculating);
                    double weak = CourtExecutiveRules.GrantWeight(false, true, generosity, honor, calculating);
                    double strong = CourtExecutiveRules.GrantWeight(true, false, generosity, honor, calculating);
                    check(normal > 0 && weak > normal && strong > weak, "Claims improve grant weight without being required");
                }
        check(CourtAgendaPresentation.Status(CourtAgendaState.Decreed).EndsWith("decree issued"), "Decree is never described as a passed vote");
        var decree = new CourtDecreeCase { Id = "realm|ruler|accused", Assigned = true, Announced = true };
        check(decree.TrySeal() && !decree.TrySeal(), "Decree consequences are sealed once before dispatch");
        var reloadedDecree = new CourtDecreeCase();
        foreach (var field in typeof(CourtDecreeCase).GetFields()) field.SetValue(reloadedDecree, field.GetValue(decree));
        check(reloadedDecree.Id == decree.Id && reloadedDecree.Assigned && reloadedDecree.Announced && !reloadedDecree.TrySeal(),
            "Case identity, assignment, announcement and issued guard survive field round trip");
        var decreeIds = typeof(CourtDecreeCase).GetFields().Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(decreeIds.Count == 7 && decreeIds.All(id => id.HasValue) && decreeIds.Distinct().Count() == 7, "Decree save fields have unique IDs");
        var savedIds = typeof(CourtObjectiveRecord).GetFields()
            .Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(savedIds.Count == 10 && savedIds.All(id => id.HasValue) && savedIds.Distinct().Count() == 10,
            "Objective saved fields have unique IDs");
        var selector = new CourtObjectiveSelector<int, int>();
        check(selector.Select(0, 0, () => throw new Exception("Empty selection consumed randomness")) == null, "Empty registry yields no objective");
        var policies = new Source("policy", 20);
        var peace = new Source("peace", 1);
        selector.Register(policies);
        selector.Register(peace);
        check(selector.FindSource("peace") == peace && selector.FindSource("missing") == null, "Handler lookup uses stable kind ID");
        bool duplicateRejected = false;
        try { selector.Register(new Source("policy", 1)); } catch (ArgumentException) { duplicateRejected = true; }
        check(duplicateRejected, "Duplicate handler IDs rejected");
        int policyDraws = 0, peaceDraws = 0;
        for (int i = 0; i < 100; i++)
        {
            int calls = 0;
            var choice = selector.Select(0, 0, () => calls++ == 0 ? i / 100.0 : .5);
            if (choice.Kind == "policy") policyDraws++; else peaceDraws++;
        }
        check(policyDraws == 50 && peaceDraws == 50, "Twenty policies do not outnumber one peace category");
        TestSelectionFamilies(check);
        peace.Weight = 3;
        check(selector.Select(0, 0, () => .249).Kind == "policy", "Weighted category lower boundary");
        check(selector.Select(0, 0, () => .25).Kind == "peace", "Weighted category upper boundary");
        peace.Eligible = false;
        peace.Weight = 1000;
        check(selector.Select(0, 0, () => .999).Kind == "policy", "Weight never overrides eligibility");
        peace.Eligible = true;
        peace.Viable = false;
        check(selector.Select(0, 0, () => .999).Kind == "policy", "Weight never overrides viability");
        policies.Weight = 0;
        check(selector.Select(0, 0, () => throw new Exception("No candidates should draw")) == null, "Zero weight and unviable candidates yield no proposal");

        var one = new CourtObjectiveSelector<int, int>();
        var duplicate = new Source("one", 10) { Duplicate = true };
        one.Register(duplicate);
        int diagnostics = 0;
        check(one.Select(0, 0, () => throw new Exception("Singleton should not draw"), c => diagnostics++) != null, "Singleton selection");
        check(duplicate.Evaluated == 1 && diagnostics == 1, "Duplicate concrete targets cannot multiply weight or evaluation work");
        var uniform = new CourtObjectiveSelector<int, int>();
        uniform.Register(new Source("policy", 20));
        for (int i = 0; i < 20; i++)
            check(uniform.Select(0, 0, () => (i + .5) / 20).Candidate.TargetId == i.ToString(), "Equal policy weights preserve uniform target selection");
        var weighted = new CourtObjectiveSelector<int, int>();
        weighted.Register(new Source("targets", 2) { TargetWeight = id => id == "0" ? 1 : 3 });
        check(weighted.Select(0, 0, () => .249).Candidate.TargetId == "0", "Target weights lower boundary");
        check(weighted.Select(0, 0, () => .25).Candidate.TargetId == "1", "Target weights upper boundary");
        foreach (double invalid in new[] { -1.0, 1.0, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { uniform.Select(0, 0, () => invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Invalid RNG input rejected");
        }
        check(new CourtObjectiveWeight(10, 5, 3, -2, 4).Total == 12, "Weight breakdown arithmetic");
        check(new CourtObjectiveWeight(1, repetitionPenalty: 5).Total == 0, "Negative weight clamped");
        check(new CourtObjectiveWeight(2000).Total == 1000, "Weight ceiling");
        check(new CourtObjectiveWeight(double.NaN, double.PositiveInfinity).Total == 0, "Nonfinite weight components sanitized");

        var handler = new TrackedObjective();
        var objective = NewObjective();
        objective.FreezeTerm(20, 500);
        check(objective.SelectedDay == 10 && objective.DeadlineDay == 100, "MCM-style reconfiguration cannot move frozen term dates");
        check(!CourtObjectiveLifecycle.OnEvent(objective, handler, "target", 9), "Pre-term event cannot satisfy objective");
        check(!CourtObjectiveLifecycle.OnEvent(objective, handler, "other", 30), "Unrelated target cannot satisfy objective");
        check(objective.State == CourtObjectiveState.Active, "Tracked objective can activate without filing a motion");
        check(!CourtObjectiveLifecycle.OnDeadline(objective, handler, 99, false), "No premature deadline");
        check(!CourtObjectiveLifecycle.OnDeadline(objective, handler, 100, false), "Deadline instant remains open for same-date events");
        check(!CourtObjectiveLifecycle.OnEvent(objective, handler, "target", double.NaN), "Invalid event date cannot satisfy objective");
        check(!CourtObjectiveLifecycle.OnEvent(objective, handler, "target", 101), "Late event cannot satisfy a term-bound objective");
        check(CourtObjectiveLifecycle.OnEvent(objective, handler, "target", 100), "Event at deadline can succeed before expiry evaluation");
        check(objective.Credit == CourtObjectiveCredit.Sponsor && objective.State == CourtObjectiveState.Succeeded, "Success and sponsor credit recorded separately");
        check(!CourtObjectiveLifecycle.OnDeadline(objective, handler, 100, false), "Expiry cannot overwrite success");
        check(objective.TryClaimResult() && !objective.TryClaimResult(), "Result claimed once");
        var loaded = Copy(objective);
        check(!loaded.TryClaimResult(), "Claimed result stays claimed after field round trip");
        check(!CourtObjectiveLifecycle.OnEvent(loaded, handler, "target", 90), "Repeated event cannot reopen terminal record");
        var pending = NewObjective();
        check(!CourtObjectiveLifecycle.OnDeadline(pending, handler, 120, true), "Filed motion survives term boundary");
        var carried = NewObjective();
        carried.Activate();
        check(!CourtObjectiveLifecycle.OnMotionResult(carried, handler, "other") && carried.State == CourtObjectiveState.Active,
            "Motion result need not complete its objective");
        check(CourtObjectiveLifecycle.OnMotionResult(carried, handler, "target"), "Linked motion completion supports carry-over without moving dates");
        check(CourtObjectiveLifecycle.OnDeadline(pending, handler, 120, false) && pending.State == CourtObjectiveState.Expired, "Unfulfilled tracked objective expires");
        check(!CourtObjectiveLifecycle.OnDeadline(new CourtObjectiveRecord(), handler, 120, false), "Legacy unknown deadline never guessed");
        var elsewhere = NewObjective();
        check(elsewhere.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.FulfilledElsewhere, "elsewhere"), "External fulfillment recorded");
        check(!elsewhere.Finish(CourtObjectiveState.Failed, CourtObjectiveCredit.None, "late_failure"), "Terminal outcome is immutable");
        check(elsewhere.TryClaimResult() && elsewhere.Credit == CourtObjectiveCredit.FulfilledElsewhere, "External fulfillment is distinguishable when rewards are evaluated");

        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        {
            var agenda = new CourtAgendaRecord { PolicyId = "serfdom", State = state, Abolish = true };
            var bridge = agenda.GetObjective();
            check(bridge.Kind == "policy" && bridge.TargetId == "serfdom" && bridge.ActionId == "repeal", "Legacy policy identity reconstructed without selection");
            check(!bridge.HasTermSnapshot, "Legacy dates are not fabricated");
            check(!bridge.TryClaimResult(), "Bridge cannot duplicate existing policy rewards");
            if (state == CourtAgendaState.Passed) check(bridge.State == CourtObjectiveState.Succeeded && bridge.Credit == CourtObjectiveCredit.Sponsor, "Passed policy bridge");
            if (state == CourtAgendaState.FulfilledElsewhere) check(bridge.State == CourtObjectiveState.Succeeded && bridge.Credit == CourtObjectiveCredit.FulfilledElsewhere, "External policy fulfillment bridge");
            if (state == CourtAgendaState.Defeated) check(bridge.State == CourtObjectiveState.Failed, "Defeated policy bridge");
            if (agenda.IsFiled) check(bridge.State == CourtObjectiveState.Active, "Filed policy is active objective");
        }
        var nominated = new CourtAgendaRecord { PolicyId = "old", State = CourtAgendaState.AwaitingNomination };
        nominated.GetObjective().FreezeTerm(10, 100);
        nominated.PolicyId = "replacement";
        nominated.State = CourtAgendaState.Announced;
        check(nominated.GetObjective().TargetId == "replacement" && nominated.GetObjective().DeadlineDay == 100, "Player substitution updates target without rescheduling");
        var nonPolicy = new CourtAgendaRecord { ObjectiveData = new CourtObjectiveRecord { Kind = "tracked", TargetId = "war" } };
        check(nonPolicy.GetObjective().Kind == "tracked" && nonPolicy.GetObjective().TargetId == "war", "Policy bridge leaves other kinds untouched");
    }

    private static void TestSelectionFamilies(Action<bool, string> check)
    {
        var selector = new CourtObjectiveSelector<int, int>();
        selector.Register(new Source("policy", 1));
        var war = new Source("war", 20);
        var peace = new Source("peace", 1);
        selector.Register(war, "foreign_affairs");
        selector.Register(peace, "foreign_affairs");
        selector.Register(new Source("trade", 1), "foreign_affairs");
        int foreign = 0;
        for (int i = 0; i < 100; i++)
        {
            int draws = 0;
            var choice = selector.Select(0, 0, () => draws++ == 0 ? i / 100d : .5);
            if (choice.SelectionFamily == "foreign_affairs") foreign++;
            check(selector.FindSource(choice.Kind) != null, "Family choice retains its concrete execution handler");
        }
        check(foreign == 50, "Three foreign kinds together have the weight of one ordinary category");
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 300; i++)
        {
            int draws = 0;
            var choice = selector.Select(0, 0, () => ++draws == 1 ? .9 : draws == 2 ? (i + .5) / 300 : .5);
            counts[choice.Kind] = counts.TryGetValue(choice.Kind, out int count) ? count + 1 : 1;
        }
        check(counts["war"] == 100 && counts["peace"] == 100 && counts["trade"] == 100,
            "Twenty war targets do not outweigh one peace or trade action inside a family");
        peace.Weight = 3;
        check(selector.Select(0, 0, () => .249).Kind == "policy", "Family weight uses its best opportunity, not its sum");
        check(selector.Select(0, 0, () => .25).Kind == "peace", "Family and action selection honor weighted boundaries");
        peace.Eligible = false;
        peace.Weight = 1000;
        check(selector.Select(0, 0, () => .49).Kind == "policy", "Ineligible high weight cannot inflate family odds");
        bool rejected = false;
        try { selector.Register(new Source("blank", 1), " "); } catch (ArgumentException) { rejected = true; }
        check(rejected && selector.FindSource("blank") == null, "Invalid family registration leaves registry unchanged");
        selector.Register(new Source("blank", 0));
        check(new CourtObjectiveChoice("legacy", null, null).SelectionFamily == "legacy", "Unspecified family retains kind identity");

        var batch = new CourtObjectiveSelector<int, int>();
        var exclusive = new Source("exclusive", 1) { Weight = 3 };
        var alternative = new Source("alternative", 1);
        batch.Register(exclusive, "shared");
        batch.Register(alternative, "shared");
        var results = batch.SelectBatch(0, new[] { 1, 2, 3 }, "exclusive", () => 0);
        check(results.Count(c => c.Kind == "exclusive") == 1 && results.Count(c => c.Kind == "alternative") == 2,
            "Exclusive arbitration filters only its handler, not sibling kinds in the same family");
        check(exclusive.Evaluated == 3 && alternative.Evaluated == 3, "Family fallback reuses evaluated candidates");
        var multi = new CourtObjectiveSelector<int, int>();
        var council = new Source("council", 1);
        var mandate = new Source("mandate", 1);
        var ordinary = new Source("ordinary", 1);
        multi.Register(council); multi.Register(mandate); multi.Register(ordinary);
        var independent = multi.SelectBatch(0, new[] { 0, 1, 2, 3 }, new[] { "council", "mandate" }, () => 0);
        check(independent.Count(c => c?.Kind == "council") == 1 && independent.Count(c => c?.Kind == "mandate") == 1
            && independent.Count(c => c?.Kind == "ordinary") == 2, "Separate locks permit one council and one law vote, including fallback collisions");
        check(council.Evaluated == 4 && mandate.Evaluated == 4 && ordinary.Evaluated == 4, "Multiple locks still reuse cached forecasts");
    }

    private static CourtObjectiveRecord NewObjective()
    {
        var result = new CourtObjectiveRecord { Kind = "test", TargetId = "target", ActionId = "achieve" };
        result.FreezeTerm(10, 100);
        return result;
    }

    private static CourtObjectiveRecord Copy(CourtObjectiveRecord record)
    {
        var copy = new CourtObjectiveRecord();
        foreach (var field in typeof(CourtObjectiveRecord).GetFields()) field.SetValue(copy, field.GetValue(record));
        return copy;
    }
}
