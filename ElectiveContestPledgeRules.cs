using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class ElectiveContestAllocation
    {
        internal Dictionary<Clan, Hero> Sides = new Dictionary<Clan, Hero>();
        internal HashSet<Hero> Active = new HashSet<Hero>();
        internal HashSet<Hero> Withdrawn = new HashSet<Hero>();
        internal Dictionary<Hero, double> Power = new Dictionary<Hero, double>();
    }

    internal static class ElectiveContestPledgeRules
    {
        internal static ElectiveContestAllocation Resolve(ElectiveContestRecord record, Hero player = null)
        {
            if (record == null || !record.PledgesCaptured || record.ElectedWinner == null || record.ElectedHouse == null
                || record.Candidates == null || record.Pledges == null || record.Candidates.Count > 3
                || record.Candidates.Any(c => c == null || c.Candidate == null || c.House == null
                    || c.Decision == ElectiveContestDecision.Pending || c.Decision == ElectiveContestDecision.AwaitingPlayer)
                || record.Candidates.Select(c => c.Candidate).Distinct().Count() != record.Candidates.Count
                || record.Candidates.Select(c => c.House).Distinct().Count() != record.Candidates.Count
                || !record.Candidates.Any(c => c.Candidate == record.ElectedWinner && c.House == record.ElectedHouse)
                || record.Pledges.Any(p => p == null || p.House == null || p.AwaitingPlayer
                    || !SuccessionChallengeRules.Finite(p.Power) || p.Power < 0 || p.Preferences == null
                    || p.Preferences.Any(x => x == null || x.Candidate == null || x.LiegeChances == null
                        || !record.Candidates.Any(c => c.Candidate == x.Candidate)))
                || record.Pledges.Select(p => p.House).Distinct().Count() != record.Pledges.Count
                || !SuccessionChallengeRules.Finite(record.Pledges.Sum(p => p.Power))
                || record.Candidates.Any(c => (c.Candidate == record.ElectedWinner
                    || c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn)
                    && !record.Pledges.Any(p => p.House == c.House))) return null;

            var result = new ElectiveContestAllocation();
            foreach (var c in record.Candidates.Where(c => c.Candidate != record.ElectedWinner
                && c.Decision == ElectiveContestDecision.Contest))
            {
                if (!SuccessionChallengeRules.Finite(c.Threshold) || c.Threshold <= 0) return null;
                if (c.Withdrawn) result.Withdrawn.Add(c.Candidate);
                else result.Active.Add(c.Candidate);
            }
            // Each pass can only remove initiators. With two losers this terminates in at most three passes.
            while (true)
            {
                result.Sides = Allocate(record, result.Active);
                result.Power = record.Candidates.ToDictionary(c => c.Candidate,
                    c => record.Pledges.Where(p => result.Sides[p.House] == c.Candidate).Sum(p => p.Power));
                var failed = result.Active.Where(hero =>
                {
                    var candidate = record.Candidates.First(c => c.Candidate == hero);
                    double opposition = ElectiveContestUltimatumRules.Opposition(result.Power[record.ElectedWinner],
                        result.Active.Where(other => other != hero).Sum(other => result.Power[other]));
                    return !SuccessionChallengeRules.CanProceed(result.Power[hero], opposition, candidate.Threshold,
                        hero == player, record.Pledges.Any(p => p.HasStronghold && result.Sides[p.House] == hero));
                }).ToList();
                if (failed.Count == 0) return result;
                foreach (var hero in failed) { result.Active.Remove(hero); result.Withdrawn.Add(hero); }
            }
        }

        private static Dictionary<Clan, Hero> Allocate(ElectiveContestRecord record, HashSet<Hero> active)
        {
            var sides = new Dictionary<Clan, Hero> { [record.ElectedHouse] = record.ElectedWinner };
            foreach (var c in record.Candidates.Where(c => active.Contains(c.Candidate))) sides[c.House] = c.Candidate;
            // Personal choices take priority over feudal calls. Explicit player choices never get overridden.
            foreach (var pledge in record.Pledges.Where(p => !sides.ContainsKey(p.House)))
            {
                var choice = pledge.Preferences.FirstOrDefault(p =>
                    (active.Contains(p.Candidate) || p.Candidate == record.ElectedWinner)
                    && (pledge.PlayerChoice || SuccessionPledgeRules.Passes(p.Roll, p.PersonalChance)));
                if (choice != null) sides[pledge.House] = choice.Candidate;
                else if (pledge.PlayerChoice) sides[pledge.House] = record.ElectedWinner;
            }
            bool changed;
            do
            {
                var additions = new Dictionary<Clan, Hero>();
                foreach (var pledge in record.Pledges.Where(p => !sides.ContainsKey(p.House)))
                {
                    var choice = pledge.Preferences.FirstOrDefault(p => active.Contains(p.Candidate)
                        && p.LiegeChances.Any(l => sides.TryGetValue(l.Key, out var side) && side == p.Candidate
                            && SuccessionPledgeRules.Passes(p.Roll, l.Value)));
                    if (choice != null) additions[pledge.House] = choice.Candidate;
                }
                changed = additions.Count > 0;
                foreach (var addition in additions) sides.Add(addition.Key, addition.Value);
            } while (changed);
            foreach (var pledge in record.Pledges.Where(p => !sides.ContainsKey(p.House)))
                sides[pledge.House] = record.ElectedWinner;
            return sides;
        }
    }
}
