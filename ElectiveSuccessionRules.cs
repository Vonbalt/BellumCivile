using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class ElectiveSuccessionRules
    {
        internal static bool RetainPromise(ElectiveCommitment old, ICollection<Hero> candidates,
            bool frozen, double day) => old != null && old.Source != "natural" && old.Source != "player"
                && (frozen || old.Until.ToDays > day) && candidates.Contains(old.Nominee);

        internal static double Preference(double law, double politics, bool minor) =>
            .60 * law + .40 * Math.Max(0, Math.Min(100, politics)) - (minor ? 25 : 0);

        internal static bool RefreshWeights(ElectiveSuccessionRecord record, Func<Clan, double> calculatePower)
        {
            if (record.Frozen || record.Completed) return false;
            var weights = record.Votes.Select(v => Math.Max(0, calculatePower(v.Clan))).ToArray();
            if (weights.Length > 0 && weights.All(w => w == 0))
                for (int i = 0; i < weights.Length; i++) weights[i] = 1;
            bool changed = false;
            for (int i = 0; i < weights.Length; i++)
            {
                changed |= record.Votes[i].Weight != weights[i];
                record.Votes[i].Weight = weights[i];
            }
            changed |= record.Votes.Any(v => v.Source == "natural" && record.Finalists.Contains(v.Speaker)
                && v.Supported != v.Speaker);
            if (changed) Tally(record);
            return changed;
        }

        internal static Hero ResolveSupport(ElectiveCommitment vote, ICollection<Hero> finalists)
        {
            if (vote.Source != "natural") return finalists.Contains(vote.Nominee) ? vote.Nominee : null;
            // Finalist selection uses nominations; self-support cannot promote its own nomination.
            if (vote.Speaker != null && finalists.Contains(vote.Speaker)) return vote.Speaker;
            return vote.Preferences.Where(p => finalists.Contains(p.Candidate)).OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.Law).ThenBy(p => p.Candidate.StringId, StringComparer.Ordinal).FirstOrDefault()?.Candidate;
        }

        internal static void Tally(ElectiveSuccessionRecord record)
        {
            foreach (var vote in record.Votes) vote.Weight = Math.Max(0, vote.Weight);
            if (record.Votes.Count > 0 && record.TotalWeight == 0)
                foreach (var vote in record.Votes) vote.Weight = 1;
            var candidates = record.Votes.SelectMany(v => v.Preferences).Select(p => p.Candidate).Distinct().ToList();
            double Aggregate(Hero hero) => record.Votes.Sum(v => v.Weight * (v.Preferences.FirstOrDefault(p => p.Candidate == hero)?.Score ?? 0));
            double Merit(Hero hero) => record.Votes.SelectMany(v => v.Preferences).First(p => p.Candidate == hero).Law;
            record.Finalists = candidates.OrderByDescending(c => record.Votes.Where(v => v.Nominee == c).Sum(v => v.Weight))
                .ThenByDescending(Aggregate).ThenByDescending(Merit).ThenBy(c => c.StringId, StringComparer.Ordinal).Take(3).ToList();
            foreach (var vote in record.Votes)
                vote.Supported = ResolveSupport(vote, record.Finalists);
            record.Winner = record.Finalists.OrderByDescending(record.Support).ThenByDescending(Aggregate)
                .ThenByDescending(Merit).ThenBy(c => c.StringId, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
