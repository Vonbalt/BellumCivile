using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;

internal static class CourtTermSchedulerTests
{
    internal static void Run(Action<bool, string> check)
    {
        var anchored = CourtTermScheduler.Create("home", 84, 90, 84, 5);
        check(anchored.StartDay == 84 && anchored.EndDay == 168, "Late preparation preserves the scheduled calendar anchor");
        var recovered = CourtTermScheduler.Create("home", 84, 400, 84, 5);
        check(recovered.StartDay == 336 && recovered.EndDay == 420, "Missed historical terms are skipped without replaying political actions");
        check(CourtTermScheduler.Create("home", 84, 83, 84, 5) == null, "A future term is not queued");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            check(CourtTermScheduler.Create("home", invalid, 100, 84, 5) == null
                && CourtTermScheduler.Create("home", 84, invalid, 84, 5) == null, "Invalid calendar values cannot enter the queue");
        check(CourtTermScheduler.Create("home", 0, 0, 0, 5) == null
            && CourtTermScheduler.Create("home", 0, 0, float.NaN, 5) == null, "Invalid term lengths cannot enter the queue");

        List<CourtTermPreparationRecord> Realms(int count, float length, int deliberation) => Enumerable.Range(0, count)
            .Select(i => CourtTermScheduler.Create("realm_" + i.ToString("D3"), 0, 0, length, deliberation)).ToList();
        var ordinary = Realms(60, 84, 5);
        var first = CourtTermScheduler.Batch(ordinary.AsEnumerable().Reverse(), 0, "realm_059");
        check(first.Count == 5 && first[0].RealmId == "realm_059", "The player's realm uses the first slot within the normal five-realm budget");
        check(first.Select(p => p.RealmId).SequenceEqual(CourtTermScheduler.Batch(ordinary, 0, "realm_059").Select(p => p.RealmId)),
            "Queue order is stable regardless of dictionary or kingdom enumeration order");
        var shortTerm = Realms(100, 21, 5);
        check(CourtTermScheduler.Batch(shortTerm, 0, null).Count == 8,
            "One hundred quarterly courts use a smooth eight-realm budget rather than overrunning their decision windows");
        var urgent = Realms(6, 7, 5); urgent.Add(CourtTermScheduler.Create("player", 0, 0, 84, 5));
        check(CourtTermScheduler.Batch(urgent, 0, "player").Count == 7,
            "Player priority cannot consume an urgent realm's last preparation slot");

        foreach (int count in new[] { 8, 60, 100, 300 })
        foreach (float length in new[] { 6f, 21f, 84f, 336f })
        foreach (int deliberation in new[] { 0, 5, 30 })
        {
            var pending = Realms(count, length, deliberation);
            var completed = new HashSet<string>();
            int lastDay = -1;
            for (int day = 0; day < length && pending.Count > 0; day++)
            {
                var batch = CourtTermScheduler.Batch(pending, day, "realm_007");
                check(batch.Count > 0, "A waiting court queue always makes progress");
                foreach (var term in batch)
                {
                    check(completed.Add(term.RealmId) && term.HasPreparationWindow(day),
                        "Every realm opens at most once and retains a usable preparation window");
                    CourtTermScheduler.SessionOffsets(term, day, out int earliest, out int latest);
                    check(earliest == term.NominationDays + 1 && latest >= earliest && term.EndDay == length,
                        "Staggering preserves nomination time and the original term end");
                    if (term.NominationDays + 1 + deliberation <= length)
                        check(day + latest + deliberation <= term.EndDay,
                            "New session and deliberation dates fit within the fixed term");
                    pending.Remove(term);
                }
                lastDay = day;
            }
            check(pending.Count == 0 && completed.Count == count, "Large and short-term realm queues drain before their preparation deadlines");
            if (count == 60 && length == 84 && deliberation == 5)
                check(lastDay == 11, "Sixty annual courts open over twelve daily batches");
        }

        CourtTermScheduler.SessionOffsets(anchored, 100, out int min, out int max);
        check(min == 11 && max == 63 && 100 + max + anchored.DeliberationDays == 168,
            "Delayed sessions use remaining time instead of extending the term by the delay");
        check(!anchored.HasPreparationWindow(160) && !anchored.IsCurrent(168),
            "A ruler unavailable too long does not receive a newly doomed or expired agenda");
        check(CourtTermScheduler.Batch(new[] { null, anchored }, 168, null).Count == 0,
            "Malformed and expired pending records do not consume work slots");
    }
}
