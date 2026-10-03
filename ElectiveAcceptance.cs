using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile
{
    internal sealed class ElectiveAcceptanceAssessment
    {
        internal Hero Winner;
        internal bool Leading;
        internal double Baseline, Honor, Mercy, PersonalityCap, Relations, Claim, Margin, Military;
        internal double Personality => Honor + Mercy + PersonalityCap;
        internal double Backing, Loyalists, Threshold;
        internal double Total => Leading ? 100 : Math.Max(0, Math.Min(100, Baseline + Personality + Relations + Claim + Margin + Military));
    }

    internal static class ElectiveAcceptanceRules
    {
        internal static HashSet<Clan> ExpectedBackingClans(Hero candidate, IEnumerable<ElectiveCommitment> votes)
        {
            var houses = new HashSet<Clan>(votes.Where(v => v.Supported == candidate && v.Clan != null).Select(v => v.Clan));
            if (candidate?.Clan != null) houses.Add(candidate.Clan);
            return houses;
        }

        internal static ElectiveAcceptanceAssessment Assess(bool leading, int honor, int mercy, int relation,
            int claimStrength, double candidateSupport, double voteDeficit, double backing, double loyalists, double threshold,
            double rivalPower = 0)
        {
            double opposition = Math.Max(0, loyalists) + 0.5 * Math.Max(0, rivalPower);
            double required = Math.Max(0.01, threshold) * opposition;
            double honorPoints = Math.Max(-2, Math.Min(2, honor)) * 10;
            double mercyPoints = Math.Max(-2, Math.Min(2, mercy)) * 5;
            return new ElectiveAcceptanceAssessment
            {
                Leading = leading,
                Baseline = 100 - Math.Max(0, Math.Min(100, candidateSupport)),
                Honor = honorPoints, Mercy = mercyPoints,
                PersonalityCap = Math.Max(-25, Math.Min(25, honorPoints + mercyPoints)) - honorPoints - mercyPoints,
                Relations = Math.Max(-100, Math.Min(100, relation)) * 0.25,
                Claim = claimStrength == 2 ? -15 : claimStrength == 1 ? -5 : 0,
                Margin = Math.Max(0, Math.Min(15, voteDeficit * 0.5)),
                Military = backing <= 0 ? 20 : required <= 0 ? -20
                    : Math.Max(-20, Math.Min(20, 20 * (1 - backing / required))),
                Backing = Math.Max(0, backing), Loyalists = Math.Max(0, loyalists), Threshold = threshold
            };
        }
    }

    // Disposable forecasts only. The election behavior owns lifetime and invalidation.
    internal sealed class ElectiveAcceptanceCache
    {
        private sealed class Entry
        {
            internal object[] Inputs;
            internal readonly Dictionary<Hero, ElectiveAcceptanceAssessment> Scores = new Dictionary<Hero, ElectiveAcceptanceAssessment>();
        }
        private readonly Dictionary<Kingdom, Entry> _realms = new Dictionary<Kingdom, Entry>();
        internal void Clear() => _realms.Clear();

        internal ElectiveAcceptanceAssessment Get(ElectiveSuccessionRecord record, Hero candidate)
        {
            if (record?.Realm == null || candidate?.Clan == null || !candidate.IsAlive
                || !ElectiveSuccessionBehavior.UsesElection(record.Realm) || !record.Finalists.Contains(candidate)
                || record.Winner?.IsAlive != true || record.Winner.Clan == null
                || candidate.Clan.Kingdom != record.Realm || record.Winner.Clan.Kingdom != record.Realm
                || ElectiveSuccessionBehavior.LegalHead(candidate.Clan) != candidate
                || ElectiveSuccessionBehavior.LegalHead(record.Winner.Clan) != record.Winner) return null;
            Hero winner = record.Winner;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var title = titles?.GetRealmSovereignTitle(record.Realm);
            var claims = new Dictionary<Hero, int>();
            var relations = new Dictionary<Hero, int>();
            var inputs = new List<object> { record, winner, record.Frozen, record.Completed,
                (int)CampaignTime.Now.ToDays, record.Law, record.Gender, title?.TitleId };
            foreach (var finalist in record.Finalists)
            {
                int claim = title != null && titles.HasActiveClaim(finalist.Clan, title, FeudalClaimStrength.Strong) ? 2
                    : title != null && titles.HasActiveClaim(finalist.Clan, title, FeudalClaimStrength.Weak) ? 1 : 0;
                int relation = finalist == winner ? 0 : CharacterRelationManager.GetHeroRelation(finalist, winner);
                claims[finalist] = claim; relations[finalist] = relation;
                inputs.Add(finalist); inputs.Add(finalist.Clan); inputs.Add(finalist.IsAlive);
                inputs.Add(claim); inputs.Add(relation);
                inputs.Add(finalist.GetTraitLevel(DefaultTraits.Honor)); inputs.Add(finalist.GetTraitLevel(DefaultTraits.Mercy));
                inputs.Add(RebellionPowerHelper.CalculateRebellionPowerThreshold(finalist));
            }
            foreach (var vote in record.Votes) { inputs.Add(vote.Clan); inputs.Add(vote.Supported); inputs.Add(vote.Weight); }
            foreach (var clan in record.Realm.Clans)
            {
                inputs.Add(clan); inputs.Add(clan.Leader); inputs.Add(clan.IsEliminated);
                inputs.Add(clan.IsUnderMercenaryService); inputs.Add(clan.IsMinorFaction);
                inputs.Add(RebellionPowerHelper.CalculateClanPower(clan));
            }
            var signature = inputs.ToArray();
            if (!_realms.TryGetValue(record.Realm, out var entry) || !entry.Inputs.SequenceEqual(signature))
            {
                entry = new Entry { Inputs = signature };
                _realms[record.Realm] = entry;
            }
            if (entry.Scores.TryGetValue(candidate, out var cached)) return cached;
            double backing = 0, loyalists = 0;
            if (candidate != winner)
            {
                var expectedBackers = ElectiveAcceptanceRules.ExpectedBackingClans(candidate, record.Votes)
                    .Where(c => c.Kingdom == record.Realm && !c.IsEliminated).ToList();
                var expectedLoyalists = ElectiveAcceptanceRules.ExpectedBackingClans(winner, record.Votes);
                // Public endorsements seed this forecast only; no armed pledge is recorded.
                var projection = RebellionPowerHelper.CalculateProjectedConflictPower(record.Realm,
                    expectedBackers, candidate.Clan, null, true, winner, expectedLoyalists);
                backing = projection.FactionPower;
                loyalists = projection.LoyalistPower;
            }
            double totalWeight = record.TotalWeight;
            double support = totalWeight > 0 ? 100 * record.Support(candidate) / totalWeight : 0;
            double deficit = totalWeight > 0 ? 100 * record.Support(winner) / totalWeight - support : 0;
            var result = ElectiveAcceptanceRules.Assess(candidate == winner,
                candidate.GetTraitLevel(DefaultTraits.Honor), candidate.GetTraitLevel(DefaultTraits.Mercy),
                relations[candidate], claims[candidate], support, deficit, backing, loyalists,
                RebellionPowerHelper.CalculateRebellionPowerThreshold(candidate));
            result.Winner = winner;
            entry.Scores[candidate] = result;
            return result;
        }
    }
}
