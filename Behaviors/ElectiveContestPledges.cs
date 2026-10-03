using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveContestBehavior
    {
        // Controlled appeals use this entry; panel reads never capture or reroll pledges.
        internal bool CapturePledges(ElectiveContestRecord record, Func<float> nextRoll)
        {
            if (!_contests.Contains(record) || !record.AccessionCompleted || record.Closed) return false;
            if (record.PledgesCaptured) return true;
            if (record.Candidates.Any(c => c.Decision == ElectiveContestDecision.Pending
                || c.Decision == ElectiveContestDecision.AwaitingPlayer)) return false;
            var realm = record.Realm;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (realm?.IsEliminated != false || manager == null || ideology == null || titles == null
                || realm.RulingClan != record.ElectedHouse) return false;
            var houses = realm.Clans.Where(c => !c.IsEliminated && !c.IsBanditFaction && !c.IsUnderMercenaryService
                && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c) && c.Leader?.IsAlive == true)
                .OrderBy(c => c.StringId, StringComparer.Ordinal).ToList();
            if (record.Candidates.Any(c => (c.Candidate == record.ElectedWinner
                || c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn) && !houses.Contains(c.House))) return false;
            var challengers = record.Candidates.Where(c => c.Candidate != record.ElectedWinner
                && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn).ToList();
            if (challengers.Any(c => c.House.Leader != c.Candidate || !SuccessionChallengeBehavior.Available(c.Candidate))) return false;
            var pledges = new List<ElectiveContestPledge>();
            foreach (var house in houses)
            {
                var pledge = new ElectiveContestPledge
                {
                    House = house, Speaker = house.Leader,
                    Power = Math.Max(0, RebellionPowerHelper.CalculateClanPower(house)),
                    HasStronghold = house.Fiefs.Any(f => f.IsTown || f.IsCastle)
                };
                // Challenger houses are forced onto their own side while active, but still
                // need contingencies if their candidate later withdraws.
                bool fixedSide = house == record.ElectedHouse;
                if (!fixedSide && (manager.IsClanPacified(house) || manager.GetRebelFaction(house) != null))
                    pledge.Preferences.Add(new ElectiveContestPreference { Candidate = record.ElectedWinner, PersonalChance = 1, Roll = 0 });
                else if (!fixedSide && house == Clan.PlayerClan)
                {
                    pledge.PlayerChoice = true;
                    pledge.AwaitingPlayer = true;
                }
                else if (!fixedSide)
                {
                    float roll = nextRoll();
                    if (float.IsNaN(roll) || roll < 0 || roll >= 1) throw new ArgumentOutOfRangeException(nameof(nextRoll));
                    float intent = ideology.CalculateRebellionScore(house);
                    var endorsement = record.Votes.FirstOrDefault(v => v.House == house)?.Supported;
                    var choices = challengers.Where(c => c.House != house).Select(c => new
                    {
                        Candidate = c,
                        Chance = CivilWarSolidarityHelper.AssessSupport(house, c.House, realm, manager, ideology,
                            false, intent, record.ElectedWinner, c.Candidate)?.JoinChance ?? 0
                    }).OrderByDescending(c => c.Candidate.Candidate == endorsement)
                        .ThenByDescending(c => c.Chance).ThenBy(c => c.Candidate.Candidate.StringId, StringComparer.Ordinal);
                    var lieges = houses.Where(s => s != house && s != record.ElectedHouse
                        && CivilWarSolidarityHelper.IsImmediateVassalOf(titles, house, s)).ToList();
                    var obligations = lieges.ToDictionary(s => s, s => CivilWarSolidarityHelper.AssessSupport(
                        house, s, realm, manager, ideology, true, intent, record.ElectedWinner)?.JoinChance ?? 0);
                    foreach (var choice in choices)
                        pledge.Preferences.Add(new ElectiveContestPreference { Candidate = choice.Candidate.Candidate,
                            PersonalChance = choice.Chance, Roll = roll, LiegeChances = new Dictionary<Clan, float>(obligations) });
                }
                pledges.Add(pledge);
            }
            record.Pledges = pledges;
            record.PledgesCaptured = true;
            return true;
        }

        internal bool AnswerPledge(ElectiveContestRecord record, Clan house, IReadOnlyList<Hero> orderedSides)
        {
            if (!_contests.Contains(record) || record.Closed || record.PledgesResolved || orderedSides == null) return false;
            var pledge = record.Pledges.FirstOrDefault(p => p.House == house);
            if (house != Clan.PlayerClan || pledge?.AwaitingPlayer != true || house.Leader != pledge.Speaker
                || orderedSides.Distinct().Count() != orderedSides.Count
                || orderedSides.Any(h => h != record.ElectedWinner && !record.Candidates.Any(c =>
                    c.Candidate == h && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn))) return false;
            pledge.Preferences = orderedSides.Select(h => new ElectiveContestPreference { Candidate = h }).ToList();
            pledge.AwaitingPlayer = false;
            return true;
        }
    }
}
