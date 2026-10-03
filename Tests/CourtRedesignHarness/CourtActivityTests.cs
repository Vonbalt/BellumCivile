using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtActivityTests
{
    private sealed class Source : ICourtObjectiveSource<int, int>
    {
        public string Kind { get; }
        internal int Count = 1;
        internal double Weight = 1;
        internal Func<int, bool> Available = _ => true;
        internal Source(string kind) { Kind = kind; }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(int context, int owner) =>
            Enumerable.Range(0, Count).Select(i => new CourtObjectiveCandidate(i.ToString(), "test"));
        public CourtObjectiveEvaluation EvaluateCandidate(int context, int owner, CourtObjectiveCandidate candidate) =>
            new CourtObjectiveEvaluation(Available(owner), true, new CourtObjectiveWeight(Weight), "fixture");
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (float mood in new[] { -100f, -60, -21, -20, 0, 20, 21, 60, 100 })
        {
            check(CourtActivityRules.MatchesMood(mood, true) == (mood >= 21), "Positive activity mood boundary");
            check(CourtActivityRules.MatchesMood(mood, false) == (mood <= -21), "Negative activity mood boundary");
            check(CourtActivityRules.Admit(mood, .29) == (Math.Abs(mood) >= 21), "One admission gate respects neutral mood");
            check(!CourtActivityRules.Admit(mood, .30), "Admission roll uses an exclusive upper bound");
        }
        foreach (double roll in new[] { double.NaN, double.PositiveInfinity, -1, 1 })
            check(!CourtActivityRules.Admit(60, roll), "Invalid activity roll is rejected");

        foreach (int competitors in new[] { 0, 1, 2 })
        foreach (int events in new[] { 1, 30 })
        {
            var selector = new CourtObjectiveSelector<int, int>();
            for (int i = 0; i < competitors; i++) selector.Register(new Source("politics_" + i));
            bool admitted = false;
            selector.Register(new Source(CourtActivityRules.Kind) { Count = events, Weight = CourtActivityRules.Weight, Available = _ => admitted });
            var admissionRandom = new Random(919);
            var selectionRandom = new Random(1084);
            int selected = 0;
            const int trials = 100000;
            for (int i = 0; i < trials; i++)
            {
                admitted = CourtActivityRules.Admit(50, admissionRandom.NextDouble());
                if (selector.Select(0, 0, selectionRandom.NextDouble)?.Kind == CourtActivityRules.Kind) selected++;
            }
            double expected = CourtActivityRules.AdmissionChance * CourtActivityRules.Weight / (competitors + CourtActivityRules.Weight);
            check(Math.Abs(selected / (double)trials - expected) < .004, "Event count does not purchase extra category odds");
            Console.WriteLine($"Activity simulation: {competitors} competing categories, {events} event types: {selected * 100d / trials:0.00}% selected (expected {expected * 100:0.00}%).");
        }

        var batch = new CourtObjectiveSelector<int, int>();
        batch.Register(new Source("policy"));
        batch.Register(new Source(CourtActivityRules.Kind) { Weight = CourtActivityRules.Weight, Available = owner => owner < 2 });
        batch.Register(new Source("council_appointment"));
        int draws = 0;
        var result = batch.SelectBatch(0, new[] { 0, 1, 2, 3 }, "council_appointment", () => ++draws == 5 ? .6 : .99);
        check(result[0].Kind == CourtActivityRules.Kind && result[1].Kind == CourtActivityRules.Kind,
            "Admitted activities remain available to council arbitration losers");
        check(result[2].Kind == "council_appointment" && result[3].Kind == "policy",
            "Fallback does not introduce an activity for a sponsor whose admission failed");

        var admittedOwners = new bool[4];
        var mixed = new CourtObjectiveSelector<int, int>();
        mixed.Register(new Source("policy"));
        mixed.Register(new Source(CourtActivityRules.Kind) { Count = 30, Weight = CourtActivityRules.Weight, Available = owner => admittedOwners[owner] });
        mixed.Register(new Source("council_appointment"));
        var mixedRandom = new Random(1909);
        int activities = 0, invalidAdmissions = 0, duplicateCouncils = 0;
        const int terms = 50000;
        for (int term = 0; term < terms; term++)
        {
            for (int owner = 0; owner < 4; owner++) admittedOwners[owner] = CourtActivityRules.Admit(50, mixedRandom.NextDouble());
            var choices = mixed.SelectBatch(0, new[] { 0, 1, 2, 3 }, "council_appointment", mixedRandom.NextDouble);
            duplicateCouncils += choices.Count(c => c?.Kind == "council_appointment") > 1 ? 1 : 0;
            for (int owner = 0; owner < 4; owner++)
                if (choices[owner]?.Kind == CourtActivityRules.Kind)
                { activities++; if (!admittedOwners[owner]) invalidAdmissions++; }
        }
        check(invalidAdmissions == 0 && duplicateCouncils == 0, "Mixed batch never rerolls failed admissions or awards two council agendas");
        check(activities > terms * 4 * .033 && activities < terms * 4 * .06, "Council fallback retains a low activity frequency");
        Console.WriteLine($"Activity batch simulation: four sponsors, policy/council/activity competition: {activities * 100d / (terms * 4):0.00}% final activity agendas after arbitration.");

        var recipient = new CourtActivityTarget();
        check(recipient.TryBegin() && !recipient.TryBegin(), "Interrupted recipient is sealed before native effect and never replays");
        recipient.Completed = true;
        check(!recipient.TryBegin(), "Completed recipient cannot be paid twice");
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.Announced,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtActivityRules.Kind }, SessionDate = CampaignTime.Days(10) };
        agenda.FreezeSchedule(84, 7);
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 10, "Activity executes on session date, not vote date");
        check(CourtAgendaPresentation.Status(CourtAgendaState.Completed).Contains("completed"), "Activity completion is not ballot passage");
    }
}
