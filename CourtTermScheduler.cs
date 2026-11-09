using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class CourtTermScheduler
    {
        internal const int DailyRealmBudget = 5;

        internal static CourtTermPreparationRecord Create(string realmId, double due, double now,
            float termDays, int deliberationDays)
        {
            if (string.IsNullOrEmpty(realmId) || double.IsNaN(due) || double.IsInfinity(due)
                || double.IsNaN(now) || double.IsInfinity(now) || due > now
                || float.IsNaN(termDays) || float.IsInfinity(termDays) || termDays <= 0) return null;
            // Missed historical terms are not replayed. Retain the saved calendar's anchor.
            double start = due + Math.Floor((now - due) / termDays) * termDays;
            return new CourtTermPreparationRecord { RealmId = realmId, StartDay = start,
                EndDay = start + termDays, TermDays = termDays, DeliberationDays = Math.Max(0, deliberationDays) };
        }

        internal static IReadOnlyList<CourtTermPreparationRecord> Batch(IEnumerable<CourtTermPreparationRecord> pending,
            double now, string playerRealmId)
        {
            var ordered = pending.Where(p => p?.IsCurrent(now) == true)
                .OrderByDescending(p => p.RealmId == playerRealmId)
                .ThenBy(p => p.LatestPreparationDay).ThenBy(p => p.StartDay)
                .ThenBy(p => p.RealmId, StringComparer.Ordinal).ToList();
            int budget = DailyRealmBudget;
            // Increase the normal budget only enough to drain each deadline group in time.
            for (int i = 0; i < ordered.Count; i++)
            {
                double days = Math.Max(1, Math.Floor(ordered[i].LatestPreparationDay - now + 1e-6) + 1);
                budget = Math.Max(budget, (int)Math.Ceiling((i + 1) / days));
            }
            return ordered.Take(budget).ToArray();
        }

        internal static void SessionOffsets(CourtTermPreparationRecord term, double now, out int earliest, out int latest)
        {
            earliest = term.NominationDays + 1;
            latest = Math.Max(earliest, (int)Math.Floor(term.EndDay - now - term.DeliberationDays + 1e-6));
        }
    }
}
